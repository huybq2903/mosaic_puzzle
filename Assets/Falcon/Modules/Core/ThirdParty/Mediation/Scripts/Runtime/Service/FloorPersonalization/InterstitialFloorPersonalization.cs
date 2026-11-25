/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-21
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Phần client của Interstitial eCPM Floor Personalization (Databuckets Rule):
    /// <br/>1. Gọi Rule — First Ad Rule lúc MAX init xong, Subsequent Ad Rule mỗi khi một interstitial lên
    /// hình (chuẩn bị cho ad kế tiếp, <c>ad_index</c> &gt;= 2).
    /// <br/>2. Thực thi workflow — load tại <c>ecpm_floor</c> của step (MAX <c>jC7Fp</c>), retry thêm
    /// <c>retry_times</c> lần, rồi đi theo <c>transitions.success/fail</c> tới khi hết step.
    /// <br/>3. Quản lý Ready Candidate Pool — mỗi lần load thành công là một candidate nằm trên một ad unit
    /// riêng (<see cref="FloorPersonalizationConfig.InterstitialIds"/>); candidate chưa chiếu được giữ lại
    /// cho các opportunity sau.
    /// <br/>4. Chọn ad để show — candidate có estimated eCPM cao nhất.
    /// <br/>5. Analytics — module Databucket hỏi param của ad_request / ad_impression qua
    /// <see cref="LoadContextParams"/> / <see cref="ShowContextParams"/> (không sửa BigData);
    /// segment/variant/device_tier đi qua <see cref="RuleEvaluated"/>.
    /// <br/>Response rỗng flow (Control / base_line), API lỗi hoặc response hỏng -> opportunity đó chạy
    /// logic load MẶC ĐỊNH (MultiCall / unit đơn) như chưa có tính năng này.
    /// <br/>Bật theo remote config <see cref="FloorPersonalizationConfigSource.KEY"/> (CMS rồi data4game), chốt một
    /// lần ở <see cref="TryActivate"/>. Phiên Lua không gọi TryActivate -> tắt cả phiên.
    /// <br/>Mọi hàm public đều chạy trên main thread (callback của MAX / coroutine).
    /// </summary>
    public sealed class InterstitialFloorPersonalization : IInterstitialPersonalization
    {
        /// <summary>
        /// Cắm vào lõi Mediation qua <see cref="InterstitialPersonalization"/> — lõi không tham chiếu thẳng
        /// lớp này, nên xoá cả thư mục FloorPersonalization vẫn compile được (interstitial chạy mặc định).
        /// Factory gọi lười; config đọc ở <see cref="TryActivate"/>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterToMediation()
        {
            InterstitialPersonalization.Register(() => Instance);
        }

        private const string _LOG = "[FloorPersonalization] ";
        private const string _MAX = "Max";
        private const string _SUCCESS = "success";
        private const string _FAIL = "fail";
        private const string _DEFAULT_SUBSEQUENT_START_STEP = "decay";

        //chặn vòng lặp transitions do config server trỏ vòng
        private const int _MAX_STEPS_PER_OPPORTUNITY = 32;

        // Key DWH (snake_case) của Databuckets — tracking plan "Interstitial eCPM Floor Personalization".
        public const string KEY_AD_OPPORTUNITY_ID = "ad_opportunity_id";
        public const string KEY_AD_CANDIDATE_ID = "ad_candidate_id";
        public const string KEY_AD_INDEX_SESSION = "ad_index_session";
        public const string KEY_ECPM_FLOOR_TYPE = "ecpm_floor_type";
        public const string KEY_ECPM_FLOOR_PERCENTILE = "ecpm_floor_percentile";
        public const string KEY_AD_LOAD_ATTEMPT = "ad_load_attempt";
        public const string KEY_FIRST_AD_ECPM = "first_ad_ecpm";
        public const string KEY_PREVIOUS_AD_ECPM = "previous_ad_ecpm";

        private static InterstitialFloorPersonalization _instance;
        public static InterstitialFloorPersonalization Instance => _instance ??= new InterstitialFloorPersonalization();

        /// <summary>
        /// Rule Evaluation thành công + response hợp lệ (kể cả Control). Module ngoài (Databucket) nghe để
        /// ghi realtime_segments / realtime_variants / device_tier — Mediation không phụ thuộc ngược.
        /// </summary>
        public static event Action<FloorRuleResponse> RuleEvaluated;

        /// <summary>
        /// Cho module ngoài ghi đè thông tin user gửi lên Rule (vd Databuckets: country theo format của
        /// họ). Trả null/rỗng thì dùng giá trị mặc định của mediation.
        /// </summary>
        public static Func<string> UidProvider;

        public static Func<string> CountryProvider;
        public static Func<string> DeviceModelProvider;

        private enum Mode
        {
            Idle, //chưa bắt đầu / tính năng tắt
            Pending, //đang chờ Rule -> chặn load mặc định để không tranh ad unit với workflow
            Personalized, //workflow đang/đã chạy cho opportunity hiện tại
            Default //opportunity hiện tại chạy logic load mặc định
        }

        private sealed class Opportunity
        {
            public string id;
            public int adIndex;
            public bool isFirst;
            public double? firstAdEcpm;
            public double? previousAdEcpm;
            public FloorFlow flow;
            public string stepId;
            public int stepAttempt;
            public int stepsRun;
            public bool finished;

            //lượt load đang chờ ad unit rảnh (mọi unit đang giữ candidate / đang chiếu)
            public FloorStep pendingStep;
        }

        private sealed class LoadAttempt
        {
            public Opportunity opportunity;
            public FloorStep step;
            public FloorAdCandidate candidate;
        }

        private FloorPersonalizationConfig _setting;
        private bool _activated;
        private bool _enabled;
        private readonly List<string> _adUnitIds = new();
        private readonly HashSet<string> _adUnitIdSet = new();
        private Action _resumeDefaultLoading;
        private bool _maxInitialized;

        private Mode _mode = Mode.Idle;
        private Opportunity _opportunity;

        //vai gắn lên dòng load MẶC ĐỊNH của opportunity hiện tại: rule_error / rule_timeout khi rơi về mặc
        //định vì gọi Rule hỏng; null khi Control (flow rỗng) hoặc khi opportunity mới bắt đầu
        private string _defaultLoadFloorType;
        private string _displayedOpportunityId; //opportunity mà interstitial vừa lên hình phục vụ
        private string _uid;

        //eCPM thật của interstitial đầu tiên trong session (giữ nguyên cả session) và của ad vừa chiếu
        private double? _firstAdEcpm;
        private double? _previousAdEcpm;

        //first/previous eCPM mà ad VỪA lên hình đã thấy (chốt trước khi cộng eCPM của chính nó) —
        //cho impression của ad load mặc định
        private double? _displayedFirstAdEcpm;
        private double? _displayedPreviousAdEcpm;

        private readonly Dictionary<string, LoadAttempt> _loading = new(); //key = ad unit
        private readonly Dictionary<string, FloorAdCandidate> _ready = new(); //Ready Candidate Pool
        private readonly Dictionary<string, FloorAdCandidate> _shown = new(); //candidate đã đem đi chiếu
        private string _showingAdUnitId; //unit đang chiếu, chưa hidden -> chưa được load lại
        private float _opportunityEndedAt = float.NegativeInfinity;

        private InterstitialFloorPersonalization()
        {
        }

        /// <summary>
        /// Phiên vừa bật chế độ personalization (sau <see cref="TryActivate"/> thành công) — module Databuckets
        /// Analytics nghe để Init SDK bằng chính config này. Bắn trên main thread, một lần mỗi phiên.
        /// </summary>
        public static event Action<FloorPersonalizationConfig> Activated;

        /// <summary>Config của phiên (null khi chưa / không bật).</summary>
        public FloorPersonalizationConfig Config => _setting;

        public bool IsConfigReady => FloorPersonalizationConfigSource.IsReady;

        public bool TryActivate()
        {
            if (_activated) return _enabled;
            _activated = true;

            var config = FloorPersonalizationConfigSource.Resolve(out var source, out var error);
            if (config == null)
            {
                FloorPersonalizationLog.Info($"{_LOG}tắt: {error} -> MultiCall / unit đơn");
                return false;
            }

            if (!config.enable)
            {
                FloorPersonalizationLog.Info($"{_LOG}tắt: enable = false ({source}) -> MultiCall / unit đơn");
                return false;
            }

            foreach (var id in config.InterstitialIds ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(id) || !_adUnitIdSet.Add(id.Trim())) continue;
                _adUnitIds.Add(id.Trim());
            }

            //api key được để trống khi evalUrl là proxy của game backend
            if (_adUnitIds.Count == 0 || string.IsNullOrWhiteSpace(config.evalUrl))
            {
                _adUnitIds.Clear();
                _adUnitIdSet.Clear();
                FloorPersonalizationLog.Error(
                    $"{_LOG}tắt: config {source} chưa có ad unit / evalUrl -> MultiCall / unit đơn");
                return false;
            }

            _setting = config;
            _enabled = true;
            FloorPersonalizationLog.Info($"{_LOG}chế độ DATABUCKETS (config từ {source}): " +
                                         $"{_adUnitIds.Count} ad unit inter [{string.Join(", ", _adUnitIds)}]");
            try
            {
                Activated?.Invoke(config);
            }
            catch (Exception e)
            {
                FloorPersonalizationLog.Error(_LOG + "Activated handler lỗi: " + e);
            }

            return true;
        }

        public bool IsEnabled => _enabled;

        /// <summary>Ad unit dành riêng cho workflow — multicall cần biết để gộp vào tham số init của MAX.</summary>
        public IReadOnlyList<string> AdUnitIds => _adUnitIds;

        public bool IsOwnedUnit(string adUnitId) => adUnitId != null && _adUnitIdSet.Contains(adUnitId);

        /// <summary>
        /// Logic load interstitial mặc định (MultiCall / unit đơn) phải đứng yên: đang chờ Rule, hoặc
        /// opportunity hiện tại đang do workflow lo.
        /// </summary>
        public bool BlocksDefaultLoading => _enabled && _mode is Mode.Pending or Mode.Personalized;

        /// <summary>
        /// <c>rule_error</c> / <c>rule_timeout</c> khi opportunity hiện tại chạy load mặc định vì gọi Rule hỏng
        /// (phiếu BigData <c>Request-Mediation-FirstInterFloorRule.md</c> W1, Q2 chốt 23/11). KHÔNG gắn cho
        /// Control / base_line — ca đó phân biệt bằng <c>realtimeVariants</c> mức user.
        /// </summary>
        public string DefaultLoadFloorType => _enabled ? _defaultLoadFloorType : null;

        #region lifecycle

        /// <summary>
        /// Gọi ngay khi MAX init xong, TRƯỚC khi logic mặc định load interstitial đầu tiên — để lượt chờ
        /// First Ad Rule kịp chặn nó lại.
        /// </summary>
        /// <param name="resumeDefaultLoading">Kích lại logic load mặc định khi opportunity rơi về Default.</param>
        public void OnMaxInitialized(Action resumeDefaultLoading)
        {
            if (!_enabled || _maxInitialized) return;
            _maxInitialized = true;
            _resumeDefaultLoading = resumeDefaultLoading;
            FloorPersonalizationLog.Info(
                $"{_LOG}BẬT — {_adUnitIds.Count} ad unit [{string.Join(", ", _adUnitIds)}], " +
                $"rule first={_setting.firstAdRule} sub={_setting.subsequentAdRule}, url={_setting.evalUrl}");
#if MAX_ENABLE
            foreach (var id in _adUnitIds)
            {
                //retry do workflow quyết định (retry_times), không để MAX tự retry
                MaxSdk.SetInterstitialExtraParameter(id, "disable_auto_retries", "true");
            }

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnAdLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnAdLoadFailed;
#endif
            StartOpportunity();
        }

        /// <summary>
        /// Một interstitial (bất kỳ nguồn nào) vừa lên hình: chốt eCPM thật, tăng index rồi chuẩn bị
        /// opportunity cho ad kế tiếp bằng Subsequent Ad Rule.
        /// </summary>
        /// <param name="actualEcpm">eCPM thật (USD/1000) của ad vừa chiếu; null = không biết.</param>
        public void OnInterstitialDisplayed(string adUnitId, double? actualEcpm)
        {
            //AdSessionCounter đã được lõi cộng ad này (InterstitialPersonalization.OnInterstitialDisplayed)
            _displayedOpportunityId = _opportunity?.id;
            _displayedFirstAdEcpm = _firstAdEcpm;
            _displayedPreviousAdEcpm = _previousAdEcpm;
            if (IsOwnedUnit(adUnitId)) _showingAdUnitId = adUnitId;
            if (actualEcpm.HasValue)
            {
                _firstAdEcpm ??= actualEcpm;
                _previousAdEcpm = actualEcpm;
            }

            if (_enabled)
                FloorPersonalizationLog.Info(
                    $"{_LOG}DISPLAYED inter #{AdSessionCounter.InterstitialShown} unit={adUnitId ?? "admob"} " +
                    $"(workflow={IsOwnedUnit(adUnitId)}) eCPM thật={actualEcpm} -> first_ad_ecpm={_firstAdEcpm} " +
                    $"current_ad_ecpm={_previousAdEcpm}" +
                    (_maxInitialized ? "" : " — CHƯA qua OnMaxInitialized, KHÔNG gọi Subsequent Rule"));

            if (_maxInitialized) StartOpportunity();
        }

        /// <summary>Ad của unit workflow đã rời màn hình (hidden) hoặc chiếu hỏng -> unit rảnh để load lại.</summary>
        public void OnOwnedAdFinished(string adUnitId)
        {
            if (_showingAdUnitId == adUnitId) _showingAdUnitId = null;
            ResumePendingLoad();
        }

        #endregion

        #region rule

        private void StartOpportunity()
        {
            if (!_enabled) return;

            //opportunity cũ còn chạy dở thì dừng; load đang bay vẫn về pool như thường, chỉ không đi tiếp
            if (_opportunity != null) _opportunity.finished = true;

            var shown = AdSessionCounter.InterstitialShown;
            var opp = new Opportunity
            {
                id = Guid.NewGuid().ToString(),
                adIndex = shown + 1,
                isFirst = shown == 0,
                firstAdEcpm = _firstAdEcpm,
                previousAdEcpm = _previousAdEcpm
            };
            _opportunity = opp;
            _mode = Mode.Pending;
            _defaultLoadFloorType = null; //vai lỗi chỉ sống tới khi opportunity kế bắt đầu

            //Chạy trong callback init MAX / displayed: ném ra ngoài là làm dở dang luồng của FalconMaxService
            //-> lỗi gì cũng nuốt ở đây và rơi về load mặc định, không để opportunity kẹt Pending (chặn MultiCall).
            try
            {
                var parameters = new List<KeyValuePair<string, string>>
                {
                    new("uid", Uid()),
                    new("retention_day", Number(FPlayerInfoService.Instance.Session.Retention)),
                    new("country", Country()),
                    new("local_hour", Number(DateTime.Now.Hour)),
                    new("device_model", DeviceModel())
                };
                string rule;
                if (opp.isFirst)
                {
                    rule = _setting.firstAdRule;
                }
                else
                {
                    rule = _setting.subsequentAdRule;
                    //Subsequent Ad Rule: first_ad_ecpm giữ nguyên trong session, current_ad_ecpm = ad vừa chiếu
                    parameters.Add(new("first_ad_ecpm", Number(opp.firstAdEcpm)));
                    parameters.Add(new("current_ad_ecpm", Number(opp.previousAdEcpm)));
                    parameters.Add(new("ad_index", Number(opp.adIndex)));
                }

                FloorPersonalizationLog.Info($"{_LOG}opportunity {opp.id} ad_index={opp.adIndex} -> rule {rule}");
                FloorRuleClient.Evaluate(_setting, rule, parameters,
                    (response, failure) => OnRuleResponse(opp, response, failure));
            }
            catch (Exception e)
            {
                FloorPersonalizationLog.Error($"{_LOG}không gọi được Rule: {e} -> load mặc định");
                SwitchToDefault(opp, FloorRuleClient.FAIL_ERROR);
            }
        }

        private void OnRuleResponse(Opportunity opp, FloorRuleResponse response, string failure)
        {
            if (opp != _opportunity || opp.finished) return; //đã có opportunity mới hơn

            var defaultStart = opp.isFirst ? null : _DEFAULT_SUBSEQUENT_START_STEP;
            if (response == null || !response.IsValid(defaultStart))
            {
                //response về được mà không hợp lệ (thiếu config, start_step trỏ sai…) cũng là rule_error
                var role = failure ?? FloorRuleClient.FAIL_ERROR;
                FloorPersonalizationLog.Error($"{_LOG}API lỗi / response không hợp lệ ({role}) -> load mặc định");
                SwitchToDefault(opp, role);
                return;
            }

            FloorPersonalizationUserInfo.Update(response);

            try
            {
                RuleEvaluated?.Invoke(response);
            }
            catch (Exception e)
            {
                FloorPersonalizationLog.Error(_LOG + "RuleEvaluated handler lỗi: " + e);
            }

            if (!response.HasFlow)
            {
                FloorPersonalizationLog.Info(
                    $"{_LOG}variant {response.realtimeVariant}: flow rỗng -> load mặc định");
                SwitchToDefault(opp, null); //Control không phải lỗi: không vai
                return;
            }

            _mode = Mode.Personalized;
            opp.flow = response.config.ecpmFloorFlow;
            opp.stepId = opp.flow.StartStepOr(defaultStart);
            FloorPersonalizationLog.Info(
                $"{_LOG}variant {response.realtimeVariant}: chạy workflow v{opp.flow.flowVersion} từ '{opp.stepId}'");
            RunStep(opp);
        }

        /// <param name="floorType">Vai gắn lên dòng load mặc định của opportunity này (rule_error /
        /// rule_timeout); null = không gắn (Control).</param>
        private void SwitchToDefault(Opportunity opp, string floorType)
        {
            opp.finished = true;
            _mode = Mode.Default;
            _defaultLoadFloorType = floorType;
            FloorPersonalizationLog.Info(
                $"{_LOG}nhả chặn -> load inter mặc định (MultiCall / unit đơn), pool workflow còn {_ready.Count}" +
                (floorType == null ? "" : $", dòng load mang ecpmFloorType={floorType}"));
            _resumeDefaultLoading?.Invoke();
        }

        #endregion

        #region workflow

        private void RunStep(Opportunity opp)
        {
            if (opp != _opportunity || opp.finished) return;
            if (++opp.stepsRun > _MAX_STEPS_PER_OPPORTUNITY || !opp.flow.steps.TryGetValue(opp.stepId, out var step))
            {
                Finish(opp, "step không tồn tại / quá số step");
                return;
            }

            opp.stepAttempt = 0;
            Load(opp, step);
        }

        private void Load(Opportunity opp, FloorStep step)
        {
            if (opp != _opportunity || opp.finished) return;

            var adUnitId = FreeAdUnit();
            if (adUnitId == null)
            {
                //Mọi unit đều đang giữ candidate / đang chiếu -> CHỜ, không bỏ step. Ca điển hình với 3 unit:
                //base + P50 còn dư trong pool, unit P75 đang chiếu -> decay load ngay trên unit P75 khi ad
                //đó đóng (OnOwnedAdFinished) hoặc khi pool nhả một candidate (PruneReady).
                opp.pendingStep = step;
                FloorPersonalizationLog.Info($"{_LOG}'{opp.stepId}' chờ ad unit rảnh");
                return;
            }

            opp.pendingStep = null;
            opp.stepAttempt++;
            var candidate = new FloorAdCandidate
            {
                opportunityId = opp.id,
                candidateId = Guid.NewGuid().ToString(),
                adUnitId = adUnitId,
                adIndex = opp.adIndex,
                stepId = opp.stepId,
                ecpmFloor = Math.Max(0d, step.ecpmFloor),
                //Tracking plan: ecpm_floor_type = VAI TRÒ của floor trong flow (base, base_half,
                //upgrade_initial…) = TÊN STEP. Field ecpm_floor_type trong step của API lại là nhãn
                //percentile ("P10", "P10_half"…) -> không dùng.
                ecpmFloorType = opp.stepId,
                ecpmFloorPercentile = step.ecpmFloorPercentile,
                loadAttempt = opp.stepAttempt,
                firstAdEcpm = opp.firstAdEcpm,
                previousAdEcpm = opp.previousAdEcpm
            };
            _loading[adUnitId] = new LoadAttempt { opportunity = opp, step = step, candidate = candidate };

            FloorPersonalizationLog.Info(
                $"{_LOG}LOAD '{opp.stepId}' floor={candidate.ecpmFloor.ToString(CultureInfo.InvariantCulture)} " +
                $"lần {opp.stepAttempt}/{step.MaxAttempts} unit={adUnitId}");
#if MAX_ENABLE
            //no_floor vẫn là một lần load, chỉ là floor = 0
            MaxSdk.SetInterstitialExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR,
                candidate.ecpmFloor.ToString(CultureInfo.InvariantCulture));
            AdViewRequestGate.Open(AdType.Interstitial, _MAX, AdViewRequestGate.ALGORITHM_FLOOR_PERSONALIZATION);
            MaxSdk.LoadInterstitial(adUnitId);
#endif
        }

        private string FreeAdUnit()
        {
            foreach (var id in _adUnitIds)
            {
                if (_loading.ContainsKey(id) || _ready.ContainsKey(id) || id == _showingAdUnitId) continue;
#if MAX_ENABLE
                //ad còn trong kho SDK mà pool không giữ (hiếm) -> không load đè
                if (MaxSdk.IsInterstitialReady(id)) continue;
#endif
                return id;
            }

            return null;
        }

#if MAX_ENABLE
        private void OnAdLoaded(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (!_loading.TryGetValue(adUnitId, out var attempt)) return;
            _loading.Remove(adUnitId);

            var candidate = attempt.candidate;
            candidate.estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue);
            candidate.networkName = adInfo.NetworkName;
            _ready[adUnitId] = candidate;

            ReportLoad(candidate, true, adInfo.NetworkName, null, adInfo.LatencyMillis);
            FloorPersonalizationLog.Info(
                $"{_LOG}LOAD OK '{candidate.stepId}' estimated={candidate.estimatedEcpm} pool={_ready.Count}");
            OnStepResult(attempt, true);
        }

        private void OnAdLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            if (!_loading.TryGetValue(adUnitId, out var attempt)) return;
            _loading.Remove(adUnitId);

            ReportLoad(attempt.candidate, false, null, errorInfo.Code.ToString(), errorInfo.LatencyMillis);
            FloorPersonalizationLog.Info($"{_LOG}LOAD FAIL '{attempt.candidate.stepId}' {errorInfo.Code}");

            var opp = attempt.opportunity;
            if (opp == _opportunity && !opp.finished && opp.stepAttempt < attempt.step.MaxAttempts)
            {
                FloorPersonalizationLog.Info(
                    $"{_LOG}retry '{opp.stepId}' sau {_setting.retryDelaySeconds}s " +
                    $"(đã thử {opp.stepAttempt}/{attempt.step.MaxAttempts})");
                MainGameObj.Instance.StartCoroutine(RetryAfter(_setting.retryDelaySeconds, opp, attempt.step));
                return;
            }

            OnStepResult(attempt, false);
        }
#endif

        private IEnumerator RetryAfter(float seconds, Opportunity opp, FloorStep step)
        {
            if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
            Load(opp, step);
        }

        private void OnStepResult(LoadAttempt attempt, bool success)
        {
            var opp = attempt.opportunity;
            if (opp != _opportunity || opp.finished) return;

            string next = null;
            attempt.step.transitions?.TryGetValue(success ? _SUCCESS : _FAIL, out next);
            if (string.IsNullOrEmpty(next) || !opp.flow.steps.TryGetValue(next, out var nextStep))
            {
                Finish(opp, "hết transition");
                return;
            }

            if (ShouldStopEarly(opp, success, nextStep))
            {
                Finish(opp, "đã có ready candidate phù hợp");
                return;
            }

            opp.stepId = next;
            RunStep(opp);
        }

        /// <summary>
        /// Subsequent Ad: step vừa fail và sắp rơi xuống floor thấp hơn, mà pool đã có candidate đạt
        /// floor đó -> kết thúc sớm (spec mục 5). First Ad luôn chạy hết flow (upgrade sau base).
        /// </summary>
        private bool ShouldStopEarly(Opportunity opp, bool success, FloorStep nextStep)
        {
            if (opp.isFirst || success) return false;
            var best = BestReady();
            return best?.estimatedEcpm is { } ecpm && ecpm >= nextStep.ecpmFloor;
        }

        private void Finish(Opportunity opp, string reason)
        {
            if (opp.finished) return;
            opp.finished = true;
            _opportunityEndedAt = Time.realtimeSinceStartup;
            FloorPersonalizationLog.Info($"{_LOG}workflow kết thúc ({reason}), pool={_ready.Count}");
        }

        #endregion

        #region ready candidate pool & show

        /// <summary>Có candidate chiếu được không — loại luôn candidate mà SDK đã huỷ (hết hạn…).</summary>
        public bool HasReadyCandidate()
        {
            PruneReady();
            return _ready.Count > 0;
        }

        /// <summary>
        /// Lấy candidate có estimated eCPM cao nhất ra khỏi pool để chiếu. Gắn opportunity/index HIỆN TẠI
        /// vào candidate: đây là final show của opportunity đang mở, kể cả khi candidate được load từ
        /// opportunity trước (dư lại trong pool).
        /// </summary>
        public FloorAdCandidate PeekBestCandidate()
        {
            PruneReady();
            return BestReady();
        }

        public FloorAdCandidate TakeBestCandidate()
        {
            PruneReady();
            var best = BestReady();
            if (best == null)
            {
                if (_enabled)
                    FloorPersonalizationLog.Info($"{_LOG}SHOW: pool rỗng -> chiếu theo đường mặc định");
                return null;
            }

            _ready.Remove(best.adUnitId);
            //giữ unit ngay từ lúc đem đi chiếu, không đợi displayed: lượt load đang chờ không được nhảy vào
            _showingAdUnitId = best.adUnitId;
            if (_opportunity != null)
            {
                best.showOpportunityId = _opportunity.id;
                best.showAdIndex = _opportunity.adIndex;
                best.showFirstAdEcpm = _opportunity.firstAdEcpm;
                best.showPreviousAdEcpm = _opportunity.previousAdEcpm;
            }
            else
            {
                best.showFirstAdEcpm = best.firstAdEcpm;
                best.showPreviousAdEcpm = best.previousAdEcpm;
            }

            _shown[best.adUnitId] = best;
            FloorPersonalizationLog.Info(
                $"{_LOG}SHOW candidate '{best.stepId}' unit={best.adUnitId} floor={best.ecpmFloor} " +
                $"estimated={best.estimatedEcpm} opportunity={best.showOpportunityId} " +
                $"ad_index={best.showAdIndex}, còn lại pool={_ready.Count}");
            return best;
        }

        /// <summary>Candidate đã đem chiếu trên unit này — impression / show fail đọc lại để gắn param.</summary>
        public FloorAdCandidate ShownCandidate(string adUnitId) =>
            adUnitId != null && _shown.TryGetValue(adUnitId, out var c) ? c : null;

        /// <summary>
        /// Game muốn chiếu mà không có ad nào. Workflow đã chạy hết mà pool rỗng thì mở lại opportunity
        /// (giới hạn bởi <see cref="FloorPersonalizationConfig.restartCooldownSeconds"/>).
        /// </summary>
        public void OnShowMiss()
        {
            if (!_enabled || !_maxInitialized || _mode != Mode.Personalized) return;
            if (_opportunity is { finished: false } || HasReadyCandidate() || _loading.Count > 0) return;
            var sinceEnd = Time.realtimeSinceStartup - _opportunityEndedAt;
            if (sinceEnd < _setting.restartCooldownSeconds)
            {
                FloorPersonalizationLog.Info(
                    $"{_LOG}show hụt, pool rỗng — chờ cooldown ({sinceEnd:F0}/{_setting.restartCooldownSeconds}s) mới chạy lại");
                return;
            }

            FloorPersonalizationLog.Info($"{_LOG}show hụt, pool rỗng -> chạy lại opportunity");
            StartOpportunity();
        }

        InterstitialAdCandidate IInterstitialPersonalization.PeekBestCandidate() => PeekBestCandidate();
        InterstitialAdCandidate IInterstitialPersonalization.TakeBestCandidate() => TakeBestCandidate();
        InterstitialAdCandidate IInterstitialPersonalization.ShownCandidate(string adUnitId) => ShownCandidate(adUnitId);

        private FloorAdCandidate BestReady()
        {
            FloorAdCandidate best = null;
            foreach (var c in _ready.Values)
            {
                if (best == null || (c.estimatedEcpm ?? -1d) > (best.estimatedEcpm ?? -1d)) best = c;
            }

            return best;
        }

        private void PruneReady()
        {
#if MAX_ENABLE
            List<string> gone = null;
            foreach (var id in _ready.Keys)
            {
                if (MaxSdk.IsInterstitialReady(id)) continue;
                (gone ??= new List<string>()).Add(id);
            }

            if (gone == null) return;
            foreach (var id in gone)
            {
                FloorPersonalizationLog.Error($"{_LOG}candidate trên {id} không còn trong SDK -> bỏ khỏi pool");
                _ready.Remove(id);
            }

            ResumePendingLoad();
#endif
        }

        /// <summary>Có unit vừa rảnh -> chạy tiếp lượt load đang chờ của opportunity hiện tại.</summary>
        private void ResumePendingLoad()
        {
            if (_opportunity is not { finished: false, pendingStep: { } step } opp) return;
            if (FreeAdUnit() == null) return;
            Load(opp, step);
        }

        #endregion

        #region analytics

        //candidate đang được báo kết quả load — chỉ khác null TRONG lúc gọi Ad.OnLoadResult
        private FloorAdCandidate _reportingLoad;

        private void ReportLoad(FloorAdCandidate candidate, bool success, string network, string error,
            long latencyMs)
        {
            //FBigDataHooks.AdLoadResult bắn ĐỒNG BỘ bên trong OnLoadResult -> module ngoài đọc param của
            //lần load này qua LoadContextParams mà không cần BigData mang hộ
            _reportingLoad = candidate;
            try
            {
                FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
                {
                    adType = AdType.Interstitial,
                    adUnitId = candidate.adUnitId,
                    success = success,
                    adMediation = _MAX,
                    adAlgorithm = AdViewRequestGate.ALGORITHM_FLOOR_PERSONALIZATION,
                    networkName = network,
                    tier = null,
                    ecpmFloor = candidate.ecpmFloor,
                    estimatedEcpm = success ? candidate.estimatedEcpm : null,
                    errorMess = error,
                    loadingMs = latencyMs,
                    adCandidateId = candidate.candidateId,
                    ecpmFloorType = Blank(candidate.ecpmFloorType),
                    ecpmFloorPercentile = Blank(candidate.ecpmFloorPercentile),
                    adLoadAttempt = candidate.loadAttempt
                });
            }
            finally
            {
                _reportingLoad = null;
            }
        }

        /// <summary>
        /// Param personalization cho <c>ad_request</c> của một lần load interstitial (key = tên cột DWH,
        /// value null = để rỗng). Gọi từ handler đồng bộ của <c>FBigDataHooks.AdLoadResult</c>: lần load
        /// của workflow thì trả param của step; load mặc định thì trả ngữ cảnh opportunity.
        /// </summary>
        public Dictionary<string, object> LoadContextParams(string adUnitId)
        {
            return _reportingLoad != null && _reportingLoad.adUnitId == adUnitId
                ? LoadParams(_reportingLoad)
                : DefaultContextParams(displayed: false);
        }

        /// <summary>
        /// Param personalization cho <c>ad_impression</c> của interstitial: candidate của workflow đã chiếu
        /// trên unit này thì lấy đúng candidate; còn lại chỉ có ngữ cảnh opportunity.
        /// </summary>
        /// <param name="displayed">true = impression thật; false = chiếu hỏng / kho rỗng.</param>
        public Dictionary<string, object> ShowContextParams(string adUnitId, bool displayed)
        {
            var candidate = ShownCandidate(adUnitId);
            return candidate != null ? ShowParams(candidate) : DefaultContextParams(displayed);
        }

        /// <summary>Param bổ sung cho ad_request (mỗi lần load của workflow). Giá trị null = để rỗng.</summary>
        private static Dictionary<string, object> LoadParams(FloorAdCandidate c) => new()
        {
            [KEY_AD_OPPORTUNITY_ID] = c.opportunityId,
            [KEY_AD_CANDIDATE_ID] = c.candidateId,
            [KEY_AD_INDEX_SESSION] = c.adIndex,
            [KEY_ECPM_FLOOR_TYPE] = c.ecpmFloorType,
            [KEY_ECPM_FLOOR_PERCENTILE] = Blank(c.ecpmFloorPercentile),
            [KEY_AD_LOAD_ATTEMPT] = c.loadAttempt,
            [KEY_FIRST_AD_ECPM] = c.firstAdEcpm,
            [KEY_PREVIOUS_AD_ECPM] = c.previousAdEcpm
        };

        /// <summary>
        /// Param bổ sung cho ad_impression của candidate đã chiếu (cả is_show = 0 khi chiếu hỏng).
        /// first/previous eCPM lấy theo opportunity lúc ĐEM CHIẾU (khớp ad_opportunity_id / ad_index_session),
        /// load_attempt giữ của lần load ra candidate.
        /// </summary>
        private static Dictionary<string, object> ShowParams(FloorAdCandidate c) => new()
        {
            [KEY_AD_OPPORTUNITY_ID] = c.showOpportunityId ?? c.opportunityId,
            [KEY_AD_CANDIDATE_ID] = c.candidateId,
            [KEY_AD_INDEX_SESSION] = c.showAdIndex ?? c.adIndex,
            [KEY_ECPM_FLOOR_TYPE] = c.ecpmFloorType,
            [KEY_ECPM_FLOOR_PERCENTILE] = Blank(c.ecpmFloorPercentile),
            [KEY_AD_LOAD_ATTEMPT] = c.loadAttempt,
            [KEY_FIRST_AD_ECPM] = c.showFirstAdEcpm,
            [KEY_PREVIOUS_AD_ECPM] = c.showPreviousAdEcpm
        };

        /// <summary>
        /// Ngữ cảnh của ad interstitial KHÔNG do workflow load (logic mặc định / Control / API lỗi): vẫn
        /// có opportunity (khi tính năng bật), index trong session và first/previous eCPM, để so sánh được
        /// giữa các variant. candidate_id / floor_type / percentile / load_attempt để rỗng (Databuckets chốt).
        /// </summary>
        /// <param name="displayed">
        /// true = impression của ad VỪA lên hình: opportunity mới đã mở ngay lúc displayed nên phải lấy
        /// opportunity mà ad này phục vụ, index = số đã chiếu, first/previous eCPM = giá trị trước khi
        /// cộng ad này. false = load / chờ chiếu / chiếu hỏng: opportunity đang mở, index = số đã chiếu + 1.
        /// </param>
        private Dictionary<string, object> DefaultContextParams(bool displayed)
        {
            return new Dictionary<string, object>
            {
                [KEY_AD_INDEX_SESSION] = AdSessionCounter.InterstitialShown + (displayed ? 0 : 1),
                [KEY_AD_OPPORTUNITY_ID] = displayed ? _displayedOpportunityId : _opportunity?.id,
                [KEY_FIRST_AD_ECPM] = displayed ? _displayedFirstAdEcpm : _firstAdEcpm,
                [KEY_PREVIOUS_AD_ECPM] = displayed ? _displayedPreviousAdEcpm : _previousAdEcpm
            };
        }

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        #endregion

        #region user context

        private string Uid()
        {
            //spec: dùng CÙNG uid cho cả hai rule -> chốt một lần cho cả session
            if (!string.IsNullOrEmpty(_uid)) return _uid;
            _uid = SafeInvoke(UidProvider);
            if (!string.IsNullOrEmpty(_uid))
            {
                FloorPersonalizationLog.Info($"{_LOG}uid (chốt cả session) = {_uid} — nguồn: UidProvider");
                return _uid;
            }

            _uid = MySingletonService.Instance<IFDeviceInfoRepository>().DeviceId;
            //UidProvider có mà trả rỗng = SDK analytics chưa sẵn sàng -> Rule và analytics lệch uid cả session
            if (UidProvider != null)
                FloorPersonalizationLog.Error($"{_LOG}UidProvider trả rỗng -> dùng DeviceId {_uid}: uid gửi Rule " +
                                              "sẽ KHÁC user_pseudo_id của Databuckets suốt session này");
            else
                FloorPersonalizationLog.Info($"{_LOG}uid (chốt cả session) = {_uid} — nguồn: DeviceId");
            return _uid;
        }

        private static string Country()
        {
            var country = SafeInvoke(CountryProvider);
            if (!string.IsNullOrEmpty(country)) return country;
#if MAX_ENABLE
            country = MaxSdk.GetSdkConfiguration()?.CountryCode;
#endif
            return string.IsNullOrEmpty(country) ? null : country.ToUpperInvariant();
        }

        private static string DeviceModel()
        {
            var model = SafeInvoke(DeviceModelProvider);
            return string.IsNullOrEmpty(model) ? MySingletonService.Instance<IFDeviceInfoRepository>().DeviceModel : model;
        }

        private static string SafeInvoke(Func<string> provider)
        {
            if (provider == null) return null;
            try
            {
                return provider();
            }
            catch (Exception e)
            {
                FloorPersonalizationLog.Error(_LOG + "provider lỗi: " + e.Message);
                return null;
            }
        }

        private static string Number(double? value) =>
            value?.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        #endregion
    }

    /// <summary>Một lần load thành công của workflow — phần tử của Ready Candidate Pool.</summary>
    /// <remarks>adUnitId / networkName / ecpmFloor / estimatedEcpm nằm ở lớp cha của lõi Mediation.</remarks>
    public sealed class FloorAdCandidate : InterstitialAdCandidate
    {
        public string opportunityId; //opportunity đã load ra candidate
        public string candidateId;
        public int adIndex;
        public string stepId;
        public string ecpmFloorType;
        public string ecpmFloorPercentile;
        public int loadAttempt;
        public double? firstAdEcpm;
        public double? previousAdEcpm;

        //opportunity / index lúc đem chiếu — có thể khác lúc load nếu candidate dư từ opportunity trước
        public string showOpportunityId;
        public int? showAdIndex;
        public double? showFirstAdEcpm;
        public double? showPreviousAdEcpm;
    }
}
