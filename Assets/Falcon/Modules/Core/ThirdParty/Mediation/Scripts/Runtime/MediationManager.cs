/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-06
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Falcon.Admob;
using Falcon.Helpers.Devkit;
using Falcon.Helpers.EventBus;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfigCms;
using Falcon.Modules.Core.ThirdParty.Ump.Runtime;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    [Serializable]
    public class RewardedCallback
    {
        public List<Reward> listReward;
    }

    [Serializable]
    public class Reward
    {
        public string name;
        public int amount;
        public string data;
    }

    public class AppsflyerCampaignInfo
    {
        public string campId;
        public string country;
        public string campName;
        public float cost;
        public float cpi;
        public int installs;
        public string firstDataTime;
        public string lastUpdatedTime;
        public DateTime createdAt;
        public DateTime lastUpdatedAt;
    }

    public class StatusAds
    {
        public StatusShowAds statusShowAds;
        public double remainingTime;
        public int remainingAttempts;
        public double remainingTimeForGroup;
    }

    public enum StatusShowAds
    {
        Active,
        LevelLimit,
        IntervalTime,
        IntervalLevel,
        IntervalBetweenIv,
        DayLimit,
        SessionLimit,
        LevelUnlock,
        DeActive,
        DayLimitGroup
    }

    public class MediationManager : AutoSingleton<MediationManager>
    {
        private bool _isInitialized;
        private bool _isInitializedManual;
        public Action onBannerShow;
        public Action onBannerHide;
        public Action onBannerLoaded;
        public Action onInitialized;

        private SOMediationSetting _setting;

        private readonly DateTime _sessionStartUtc = DateTime.UtcNow;
        private DateTime _lastInterstitialShownUtc;
        private int _numberShowInterstitial;

        public int NumberShowInterstitial
        {
            get => _numberShowInterstitial;
            set
            {
                if (value > _numberShowInterstitial) _lastInterstitialShownUtc = DateTime.UtcNow;
                _numberShowInterstitial = value;
            }
        }

        public double SecondsSinceLastInterstitial =>
            (DateTime.UtcNow - (_numberShowInterstitial > 0 ? _lastInterstitialShownUtc : _sessionStartUtc))
            .TotalSeconds;

        private MediationType _mediationType;

        public MediationType CurrentMediationType()
        {
            return _mediationType;
        }

        public Action onButtonRemoveAds;

        private readonly List<IMediationProvider> _providers = new();

        public void Register(IMediationProvider provider)
        {
            if (provider == null) return;
            if (_providers.Contains(provider)) return;
            _providers.Add(provider);
            _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }


        private IAdsService _mediationGma;

        private IAdsService MediationGma
        {
            get
            {
                if (_mediationGma == null)
                    _mediationGma = new FalconGmaService();

                return _mediationGma;
            }
        }

        private IAdsService _mediationMax;

        private IAdsService MediationMax
        {
            get
            {
                if (_mediationMax == null)
                    _mediationMax = new FalconMaxService();

                return _mediationMax;
            }
        }

        private IAdsService _mediationIronSource;

        private IAdsService MediationIronSource
        {
            get
            {
                if (_mediationIronSource == null)
                    _mediationIronSource = new FalconIronSourceService();

                return _mediationIronSource;
            }
        }

        public IAdsService GetAdsService()
        {
            var mediationDefault = FConfigControllerCms.Instance.Config<MediationConfig>().mediationDefault;
            switch (mediationDefault)
            {
                case (int)MediationType.Admob:
                    return MediationGma;
                case (int)MediationType.LevelPlay:
                    return MediationIronSource;
            }

            return MediationMax;
        }

        public const string EVENT_START_PURCHASE = "falcon.modules.iap.start_purchase";
        public const string EVENT_START_INIT = "falcon.modules.mediation.start_init";
        public const string EVENT_SHOWING_ADS = "falcon.modules.mediation.showing_ads";

        private bool _register;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            _ = Instance;
        }

        private IMediationProvider GetProvider()
        {
            if (_providers.Count > 0)
            {
                return _providers[0];
            }

            return null;
        }

        private bool _doneConfigUpdateFromNet;
        private bool _doneUmp;
        private bool _doneCampaignIdAppsflyer;
        private bool _doneCampaignNameAppsflyer;

        private string _campaignId;
        private string _campaignName;

        private void OnUpdateCampaignNameFromAppsflyer(string campaignName)
        {
            if (_doneCampaignNameAppsflyer) return;
            _doneCampaignNameAppsflyer = true;
            _campaignName = campaignName;
        }

        private void OnUpdateCampaignIdFromAppsflyer(string campaignId)
        {
            if (_doneCampaignIdAppsflyer) return;
            _doneCampaignIdAppsflyer = true;
            _campaignId = campaignId;
        }

        public void SetUpBidFloor(List<AdBidFloor> adBidFloor)
        {
            foreach (var bidFloor in adBidFloor)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log("adunitid : " + bidFloor.adUnitId);
                Debug.Log("isrewarded : " + bidFloor.rewarded);
                Debug.Log("multiplier : " + bidFloor.multiplier);
#endif
            }
        }

        private IEnumerator SendRequest()
        {
            string url =
                    "https://dwh-access.data4game.com/api/v1/client/appsflyer-camp-cpi/" +
                    _campaignId +
                    "?packageName=" +
                    Application.identifier +
                    "&platform="
#if UNITY_ANDROID
                    + "android"
#elif UNITY_IOS
                    + "ios"
#endif
                ;
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Client-Key", "68f88e5d9179f33fa5c416a6");
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                var data = JsonUtility.FromJson<AppsflyerCampaignInfo>(request.downloadHandler.text);
                if (DateTime.TryParseExact(data.firstDataTime, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime parsedDate))
                {
                    data.createdAt = parsedDate;
                }

                if (DateTime.TryParseExact(data.lastUpdatedTime, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime lastUpdated))
                {
                    data.lastUpdatedAt = lastUpdated;
                }

                _doneCampaignNameAppsflyer = true;
                _campaignName = data.campName;
            }
            else
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"Request failed: {request.result} | {request.error}");
#endif
            }
        }

        private IEnumerator InitMediationFromCampaignName(string campaignName)
        {
            if (campaignName.ToLower().Contains("_ios_ap_") || campaignName.ToLower().Contains("_and_ap_"))
            {
                InternalInitMediation(MediationType.Max);
            }
            else if (campaignName.ToLower().Contains("_ios_is_") || campaignName.ToLower().Contains("_and_is_"))
            {
                InternalInitMediation(MediationType.LevelPlay);
            }
            // else if (campaignName.ToLower().Contains("_ios_ga_") || campaignName.ToLower().Contains("_and_ga_"))
            // {
            //     InitMediation(MediationType.Admob);
            // }
            else
            {
                //đợi remote config
                yield return new WaitUntil(() => _doneConfigUpdateFromNet || Time.time - _startTime >= _TIME_OUT);
                InitMediationFromConfig();
            }
        }

        private void InitMediationFromConfig()
        {
            DebugLog("Init Mediation From Config");
            var mediationDefault = FConfigControllerCms.Instance.Config<MediationConfig>().mediationDefault;
            DebugLog("mediationDefault : " + mediationDefault);

            if (mediationDefault == (int)MediationType.LevelPlay)
            {
                InternalInitMediation(MediationType.LevelPlay);
            }
            else
            {
                InternalInitMediation(MediationType.Max);
            }
            // else
            // {
            //     InitMediation(MediationType.Admob);
            // }
        }

        private float _startTime;
        private const float _TIME_OUT = 7f;

        /// <summary>
        /// Đủ dữ liệu để chọn Lua hay MultiCall: server đã trả lời bản tin Lua VÀ bản tin bid floor đã về.
        /// Lua cũng cần bid floor: danh sách ad unit (+ có id AdMob hay không) lấy từ đó.
        /// </summary>
        private static bool LuaDecided()
        {
#if MAX_ENABLE
            if (!LuaDynamic.LuaAdMonetize.HasResponded) return false;
            if (LuaDynamic.LuaAdMonetize.IsRunning && !LuaDynamic.AdBidFloorsV2.HasResponded) return false;
            return MultiCall.Instance.getListAdBidFloorFromServer;
#else
            return MultiCall.Instance.getListAdBidFloorFromServer;
#endif
        }

        /// <summary>
        /// Phiên Lua không cần config Databuckets (Lua thắng). Không Lua thì config Databuckets (luồng load
        /// interstitial riêng, nếu module có mặt) phải chốt nguồn xong mới chọn được Databuckets hay MultiCall.
        /// </summary>
        private static bool PersonalizationDecided()
        {
#if MAX_ENABLE
            if (LuaDynamic.LuaAdMonetize.IsRunning) return true;
#endif
            return InterstitialPersonalization.Current.IsConfigReady;
        }

        /// <summary>
        /// Đủ remote config để biết HẾT adUnitId MAX của mọi luồng (Lua, MultiCall, DFPool, workflow Databuckets)
        /// và chốt được chế độ load.
        /// </summary>
        private static bool MaxConfigsReady()
            => LuaDecided() && AdsConfigGate.IsReady && MaxPreInitAdUnits.IsReady && PersonalizationDecided();

        /// <summary>Các nguồn remote config MAX còn chưa về (để log khi hết trần chờ).</summary>
        private static string PendingMaxConfigs()
        {
            var pending = new List<string>();
#if MAX_ENABLE
            if (!LuaDynamic.LuaAdMonetize.HasResponded) pending.Add("sc_ad_monetize_lua_script");
            if (LuaDynamic.LuaAdMonetize.IsRunning && !LuaDynamic.AdBidFloorsV2.HasResponded)
                pending.Add("sc_ad_bid_floor_v2");
#endif
            if (!MultiCall.Instance.getListAdBidFloorFromServer) pending.Add("sc_ad_bid_floor");
            if (!AdsConfigGate.IsReady) pending.Add("CMS");
            if (!MaxPreInitAdUnits.IsReady) pending.Add("DFPool (moMulDfPoolConfig)");
            if (!PersonalizationDecided()) pending.Add("Databuckets (moMulFloorPersonalizationConfig)");
            return string.Join(", ", pending);
        }

        private static string DescribeAdsMode()
        {
#if MAX_ENABLE
            return $"lua: trả lời={LuaDynamic.LuaAdMonetize.HasResponded} hợp lệ={LuaDynamic.LuaAdMonetize.IsRunning}" +
                   $", bid floor: trả lời={MultiCall.Instance.getListAdBidFloorFromServer}" +
                   $", bid floor v2: trả lời={LuaDynamic.AdBidFloorsV2.HasResponded}" +
                   $", personalization config chốt={InterstitialPersonalization.Current.IsConfigReady}";
#else
            return $"bid floor: trả lời={MultiCall.Instance.getListAdBidFloorFromServer}";
#endif
        }

        private IEnumerator StartProcess()
        {
            DebugLog("start process");
            _startTime = Time.time;
            // đợi ump
            yield return new WaitUntil(() => _doneUmp);
            DebugLog("ump done");
            if (_setting.useMax && _setting.useIronSource)
            {
                //đợi appsflyer service
                yield return new WaitUntil(() => _doneCampaignIdAppsflyer || Time.time - _startTime >= _TIME_OUT);
                DebugLog(_doneCampaignIdAppsflyer
                    ? "done campaign id from appsflyer"
                    : "timeout wait campaign id from appsflyer");
                _doneCampaignIdAppsflyer = true;
                //có campaign name
                if (!string.IsNullOrEmpty(_campaignName))
                {
                    DebugLog("init mediation from campaign name : " + _campaignName);
                    StartCoroutine(InitMediationFromCampaignName(_campaignName));
                }
                else
                {
                    //không có campaign name
                    //đợi remote config
                    DebugLog("campaign name is null, waiting remote config");
                    yield return new WaitUntil(() => _doneConfigUpdateFromNet || Time.time - _startTime >= _TIME_OUT);
                    DebugLog(_doneConfigUpdateFromNet ? "done wait remote config" : "timeout wait remote config");
                    //nếu campaign id rỗng thì lấy remote config init default
                    if (string.IsNullOrEmpty(_campaignId))
                    {
                        DebugLog("campaign id is null");
                        InitMediationFromConfig();
                    }
                    else
                    {
                        //campaign id khác rỗng
                        //so sánh với chuỗi campaign id của ironsource và admob
                        DebugLog("campaign id is NOT null");
                        var ironSource = FConfigControllerCms.Instance.Config<MediationConfig>()
                            .appsflyerCampaignIdsForIronSource;
                        var google = FConfigControllerCms.Instance.Config<MediationConfig>()
                            .appsflyerCampaignIdsForGoogle;

                        var listCamGma = new List<string>();
                        var listCamIrs = new List<string>();

                        if (!string.IsNullOrEmpty(ironSource))
                        {
                            listCamIrs = ironSource.Split(';').ToList();
                        }

                        if (!string.IsNullOrEmpty(google))
                        {
                            listCamGma = google.Split(';').ToList();
                        }

                        if (listCamIrs.Contains(_campaignId))
                        {
                            //init ironsource
                            InternalInitMediation(MediationType.LevelPlay);
                        }
                        // else if (listCamGma.Contains(_campaignId))
                        // {
                        //     //init gma
                        //     InitMediation(MediationType.Admob);
                        // }
                        else
                        {
                            //lấy campaign name từ api
                            DebugLog("get campaign name from api");
                            yield return StartCoroutine(SendRequest());
                            if (!string.IsNullOrEmpty(_campaignName))
                            {
                                DebugLog("init mediation from campaign name : " + _campaignName);
                                StartCoroutine(InitMediationFromCampaignName(_campaignName));
                            }
                            else
                            {
                                DebugLog("init mediation from remote config");
                                InitMediationFromConfig();
                            }
                        }
                    }
                }
            }
            else if (_setting.useMax)
            {
                //chờ CẢ hai nguồn remote config (CMS cho MAX, data4game cho AdMob) rồi mới init: tầng
                //AdMob chỉ init sau khi MAX init thành công (callback init của MAX), xem AdsConfigGate.
                //Lua (sc_ad_monetize_lua_script) ưu tiên trước MultiCall (sc_ad_bid_floor): phải chờ server
                //trả lời bản tin Lua rồi mới biết dùng bên nào. Lua cũng chờ bid floor (danh sách ad unit của Lua).
                //Chế độ được chốt cho cả phiên ở FalconMaxService.Init (LuaAdMonetize.LockMode).
                //Không Lua thì chờ thêm remote config Databuckets chốt nguồn (CMS, CMS không khai thì data4game):
                //Databuckets ưu tiên trên MultiCall, chế độ chốt luôn ở FalconMaxService.Init (đã gộp vào MaxConfigsReady).
                //Module ngoài (vd DFPool) cần config của nó chốt để khai unit MAX TRƯỚC init (MaxPreInitAdUnits):
                //unit không khai trước init thì module đó không được load cả phiên.
                //MAX init cần ĐỦ adUnitId của mọi luồng (truyền vào CallBeforeInit / Init / CallAfterInit) -> chờ hết
                //remote config: Lua + bid floor v2, bid floor v1 (MultiCall), CMS (provider index MultiCall),
                //config DFPool. Đồng hồ tính từ SAU UMP, bằng realtime (trước đây tính cả lúc chờ UMP + Time.time
                //theo timeScale -> UMP lâu hơn trần là init ngay, không chờ config nào).
                //Hết trần mà còn thiếu: init bằng cái đang có (cache); id MAX về sau init bị bỏ cả phiên.
                var configWaitStart = Time.realtimeSinceStartup;
                yield return new WaitUntil(() =>
                    MaxConfigsReady() || Time.realtimeSinceStartup - configWaitStart >= _TIME_OUT);
                DebugLog((MaxConfigsReady()
                             ? "init MAX: đã đủ remote config"
                             : $"init MAX: HẾT {_TIME_OUT}s chờ, còn thiếu [{PendingMaxConfigs()}] -> init bằng cache, " +
                               "id MAX về sau init bị bỏ cả phiên") +
                         " > " + AdsConfigGate.Describe() + " | " + DescribeAdsMode() +
                         $" | unit module ngoài sẵn sàng={MaxPreInitAdUnits.IsReady}");
                InternalInitMediation(MediationType.Max);
            }
            else if (_setting.useIronSource)
            {
                var configWaitStart = Time.realtimeSinceStartup;
                yield return new WaitUntil(() =>
                    (MultiCall.Instance.getListAdBidFloorFromServer && AdsConfigGate.IsReady)
                    || Time.realtimeSinceStartup - configWaitStart >= _TIME_OUT);
                InternalInitMediation(MediationType.LevelPlay);
            }
        }

        private void Start()
        {
            DebugLog($"Mediation module version {MediationVersion.VALUE}");
            GameData4Mediation.Instance.SyncVersion();
            GetProvider()?.InitFromManager();
            GameEvent<string>.Register("falcon.modules.thirdparty.appsflyer.campaign_name",
                OnUpdateCampaignNameFromAppsflyer, null);
            GameEvent<string>.Register("falcon.modules.thirdparty.appsflyer.campaign_id",
                OnUpdateCampaignIdFromAppsflyer, null);
            GameEvent<bool>.Register(FalconUMP.FALCON_UMP_COMPLETE, DoneUmp, null);
            GameEvent<bool>.Register("falcon.modules.thirdparty.mediation.remove_ads", SetRemoveAds, null);

            //OnUpdateFromNet là event Action trần: bắn một lần, KHÔNG phát lại cho người đăng ký sau.
            //Start() của MonoBehaviour hoàn toàn có thể chạy SAU khi CMS đã về -> đăng ký vào chỗ trống
            //và _doneConfigUpdateFromNet không bao giờ true, StartProcess phải chờ hết 7s timeout.
            //Nên hỏi TRẠNG THÁI trước (InitState là ảnh chụp, không có race), event chỉ để bắt lần về sau.
            _doneConfigUpdateFromNet = AdsConfigGate.IsCmsConfigSettled;
            DebugLog($"[remote-config] ĐÃ ĐĂNG KÝ CMS OnUpdateFromNet — frame={Time.frameCount} " +
                     $"t={Time.realtimeSinceStartup:F2}s, đã settled sẵn lúc đăng ký={_doneConfigUpdateFromNet}");
            FConfigControllerCms.Instance.OnUpdateFromNet += () =>
            {
                _doneConfigUpdateFromNet = true;
                DebugLog($"[remote-config] CMS OnUpdateFromNet ĐÃ BẮN — frame={Time.frameCount} " +
                         $"t={Time.realtimeSinceStartup:F2}s");
            };
            _setting = Resources.Load<SOMediationSetting>("SOMediationSetting");
            StartCoroutine(StartProcess());
        }

        private void DoneUmp(bool obj)
        {
            _doneUmp = true;
        }

        private void InternalInitMediation(MediationType type)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            _mediationType = type;
            DebugLog("mediation type : " + type);
            if (_setting.manualInit)
            {
                CheckInit();
                return;
            }
            InitMediationByType(type);
        }

        private void CheckInit()
        {
            if (_isInitialized && _isInitializedManual)
            {
                InitMediationByType(_mediationType);
            }
        }

        private void InitMediationByType(MediationType type)
        {
            DebugLog("Init mediation : " + type);
            switch (type)
            {
                case MediationType.Max:
                    AdmobMediationFalconBridge.InstallAndInitialize(new AdmobMediationConfig());
                    MediationMax.Init(() =>
                    {
                        onInitialized?.Invoke();
                        FsnAdManager.Instance.InitFsnAd();
                    });
                    break;
                case MediationType.LevelPlay:
                    MediationIronSource.Init(() =>
                    {
                        onInitialized?.Invoke();
                        FsnAdManager.Instance.InitFsnAd();
                    });
                    break;
                case MediationType.Admob:
                    MediationGma.Init(() =>
                    {
                        onInitialized?.Invoke();
                        FsnAdManager.Instance.InitFsnAd();
                    });
                    break;
            }
        }

        public void InitMediation()
        {
            if (_isInitializedManual) return;
            _isInitializedManual = true;
            CheckInit();
        }

        public bool CanShowInterstitial(string placementId)
        {
            if (GameData4RemoveAds.Instance.removeAds) return false;
            var status = GetStatusAdsFromPlacement(placementId, isRewarded: false);
            if (status == null || status.statusShowAds != StatusShowAds.Active) return false;

            return IsInterstitialReady() && IsInterstitialAllowedByLua(placementId);
        }

        private string DescribeInterstitialShow(string placementId)
        {
            var status = GetStatusAdsFromPlacement(placementId, isRewarded: false);
            var lua = "";
#if MAX_ENABLE
            lua = $" luaMode={LuaDynamic.LuaAdMonetize.IsLuaMode}" +
                  $" luaReady={LuaDynamic.LuaAdMonetize.HasReady(AdType.Interstitial)}";
#endif
            return $"inter @ {placementId}: provider={GetProvider()?.Name ?? "null"}" +
                   $" status={status?.statusShowAds.ToString() ?? "null"} ready={IsInterstitialReady()}" +
                   $" removeAds={IsRemoveAds()}{lua}";
        }

        private bool IsInterstitialAllowedByLua(string placementId)
        {
#if MAX_ENABLE
            if (!LuaDynamic.LuaAdMonetize.IsLuaMode || !LuaDynamic.LuaAdMonetize.HasReady(AdType.Interstitial))
                return true;
#endif
            return IsShowInterstitial(placementId);
        }

        public bool CanShowAppOpen(string placementId = "AppOpen")
        {
            if (_setting.UseAppOpenMax && _mediationType == MediationType.Max)
            {
                var provider = GetProvider();
                if (provider != null)
                {
                    return provider.CanShowAppOpen(placementId);
                }
            }

            return true;
        }

        public AdSkipReason? GetAppOpenSkipReason(string placementId = "AppOpen")
        {
            if (!(_setting.UseAppOpenMax && _mediationType == MediationType.Max)) return null;

            var provider = GetProvider();
            if (provider == null || provider.CanShowAppOpen(placementId)) return null;

            var status = provider.GetStatusAdsFromPlacement(placementId, true)?.statusShowAds ?? StatusShowAds.Active;
            if (status != StatusShowAds.Active) return AdSkipReasonMap.From(status);

            return (GetAdsService() as FalconMaxService)?.AppOpenSkipReason() ?? AdSkipReason.Other;
        }

        public bool IsBannerReady()
        {
            return GetAdsService().IsBannerReady();
        }

        private bool IsRemoveAds()
        {
            return GameData4RemoveAds.Instance.removeAds;
        }

        public void ShowBanner(string placement = "Banner")
        {
            if (IsRemoveAds()) return;
            DebugLog("mediation manager -> show banner");
            GetProvider()?.ShowBanner(placement);
        }

        public void HideBanner()
        {
            GetAdsService().HideBanner();
        }

        public void IgnoreNextAoa()
        {
#if MAX_ENABLE
            MediationMax.IgnoreNextAoa();
#else
            MediationGma.IgnoreNextAoa();
#endif
        }

        /// <summary>
        /// Chiếu interstitial.
        /// <code>
        /// MediationManager.Instance.ShowInterstitial("level_end", onClosed, onFail,
        ///     adWhen: FAdWhen.LevelWin, triggerPlayTurnId: FalconBigDataController.Level.LastEndedTurnId);
        /// </code>
        /// </summary>
        /// <param name="adWhen">QC chiếu VÌ SAO — hằng <see cref="FAdWhen"/> (BigData <c>ad_when</c>). Bỏ trống = như cũ.</param>
        /// <param name="triggerPlayTurnId">
        /// Lượt chơi đã kích hoạt lần chiếu (hợp đồng BigData đợt 3 B12): Inter sau thắng / thua =
        /// <c>FalconBigDataController.Level.LastEndedTurnId</c>, QC trong lượt = <c>Level.CurrentTurnId</c>, QC không do
        /// lượt nào (mở app, home, shop) thì bỏ trống.
        /// </param>
        public void ShowInterstitial(
            string placementId, Action onInterstitialClosed = null, Action onFail = null, bool needAdBreak = false,
            string adWhen = null, string triggerPlayTurnId = null)
        {
            DebugLog("mediation manager -> show interstitial");
            //khai TRƯỚC mọi nhánh (luật / kho / Lua): mọi mốc sau của lần chiếu (show_attempt, show_skipped, show,
            //impression, close) SDK tự mang hai giá trị này
            UpdateShowContext(AdType.Interstitial, adWhen, triggerPlayTurnId);
#if ENABLE_DEBUG_LOG
            DebugLog(DescribeInterstitialShow(placementId));
#endif

            var provider = GetProvider();
            if (provider == null)
            {
                DebugError("show inter : provider is null");
                onInterstitialClosed?.Invoke();
                return;
            }

            var reportOpportunity = !IsRemoveAds();
            var adAvailable = IsInterstitialReady();
            var readyAd = GetReadyInterstitialInfo();

            if (!reportOpportunity)
                FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, placementId, AdSkipReason.NoAds,
                    adAvailable: adAvailable, readyAd: readyAd);

            if (reportOpportunity)
            {
                var status = provider.GetStatusAdsFromPlacement(placementId, false)?.statusShowAds
                             ?? StatusShowAds.Active;
                if (status != StatusShowAds.Active)
                {
                    DebugLog($"show inter bị luật chặn ({status}) @ {placementId}");
                    FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, placementId,
                        AdSkipReasonMap.From(status), adAvailable: adAvailable, readyAd: readyAd);
                    onInterstitialClosed?.Invoke();
                    return;
                }
            }

            if (!IsInterstitialAllowedByLua(placementId))
            {
                DebugLog($"show inter bị chặn bởi Lua IsShowInterstitial @ {placementId}");
                if (reportOpportunity)
                    FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, placementId, AdSkipReason.Other,
                        adAvailable: adAvailable, readyAd: readyAd);
                (onFail ?? onInterstitialClosed)?.Invoke();
                return;
            }

            if (reportOpportunity)
                FalconBigDataController.Ad.OnShowAttempt(
                    AdType.Interstitial,
                    adWhere: string.IsNullOrWhiteSpace(placementId) ? null : placementId,
                    adAvailable: adAvailable);

            provider.ShowInterstitial(placementId, onInterstitialClosed, onFail, needAdBreak);
        }

        public bool IsInterstitialReady()
        {
            return GetAdsService().IsInterstitialReady();
        }

        public AdReadyInfo GetReadyInterstitialInfo()
        {
            if (!IsInterstitialReady()) return null;
            switch (GetAdsService())
            {
                case FalconMaxService max: return max.ReadyInterstitialInfo();
                case FalconGmaService gma: return gma.ReadyInterstitialInfo();
                default: return null;
            }
        }

        public AdReadyInfo GetReadyAppOpenInfo()
        {
            return (GetAdsService() as FalconMaxService)?.ReadyAppOpenInfo();
        }

        /// <summary>
        /// Hỏi Lua script (LuaDynamic, server gửi qua sc_ad_monetize_lua_script) có nên show interstitial không.
        /// Gọi được từ bất cứ đâu, bất cứ lúc nào (main thread). Lua nhận <c>context.params</c> =
        /// { where = <paramref name="placementId"/>, level } + mọi cặp trong <paramref name="param"/>
        /// (string / bool / số; kiểu khác đổi thành chuỗi) và trả true/false.
        /// Chưa có script, script không có hàm IsShowInterstitial, hoặc script lỗi -> <paramref name="defaultValue"/>.
        /// Chỉ là câu hỏi: KHÔNG show gì, game tự gọi <see cref="ShowInterstitial"/> nếu muốn.
        /// </summary>
        public bool IsShowInterstitial(string placementId, Dictionary<string, object> param = null,
            bool defaultValue = true)
        {
#if MAX_ENABLE
            try
            {
                return LuaDynamic.LuaAdMonetize.IsShowInterstitial(placementId, param, defaultValue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return defaultValue;
            }
#else
            return defaultValue;
#endif
        }

        /// <summary>
        /// Như <see cref="IsShowInterstitial"/>, cho rewarded: hỏi hàm <c>IsShowRewarded()</c> của Lua script.
        /// Chỉ là câu hỏi: KHÔNG show gì, game tự gọi <see cref="ShowRewardedVideo"/> nếu muốn.
        /// </summary>
        public bool IsShowRewarded(string placementId, Dictionary<string, object> param = null,
            bool defaultValue = true)
        {
#if MAX_ENABLE
            try
            {
                return LuaDynamic.LuaAdMonetize.IsShowRewarded(placementId, param, defaultValue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return defaultValue;
            }
#else
            return defaultValue;
#endif
        }

        public void AdsClosed(AdType adType, string placementId)
        {
            GetProvider()?.AdsClosed(adType, placementId);
        }

        public void SetRemoveAds(bool value)
        {
            GameData4RemoveAds.Instance.removeAds = value;
            GameData4RemoveAds.Instance.Save();
            GameData4RemoveAds.Instance.UpdateToServer();
        }

        /// <summary>
        /// Chiếu rewarded.
        /// <code>
        /// MediationManager.Instance.ShowRewardedVideo("revive", onReward, onFail,
        ///     adWhen: FAdWhen.Revive, triggerPlayTurnId: FalconBigDataController.Level.CurrentTurnId);
        /// </code>
        /// </summary>
        /// <param name="adWhen">Xem <see cref="ShowInterstitial"/>.</param>
        /// <param name="triggerPlayTurnId">Xem <see cref="ShowInterstitial"/>.</param>
        public void ShowRewardedVideo(
            string placementId, Action onRewardedComplete = null, Action onFail = null,
            bool showInterstitialInstead = true, string adWhen = null, string triggerPlayTurnId = null)
        {
            DebugLog("mediation manager -> show rewarded");
            UpdateShowContext(AdType.Reward, adWhen, triggerPlayTurnId);

            //Mốc cầu — xem ghi chú ở ShowInterstitial.
            FalconBigDataController.Ad.OnShowAttempt(
                AdType.Reward,
                adWhere: string.IsNullOrWhiteSpace(placementId) ? null : placementId,
                adAvailable: IsRewardedVideoReady());

            var provider = GetProvider();
            if (provider != null)
            {
                //lượt này có thể đổi sang inter (showInterstitialInstead): nhánh đổi gọi
                //ApplyRewardedContextToInterstitial ngay trước khi chiếu inter — chỉ trong phạm vi lời gọi này
                _rewardedShowContext = (adWhen, triggerPlayTurnId);
                try
                {
                    provider.ShowRewardedVideo(placementId, onRewardedComplete, onFail, showInterstitialInstead);
                }
                finally
                {
                    _rewardedShowContext = null;
                }
            }
            else
            {
                DebugError(" ShowRewardedVideo provider is null");
                onFail?.Invoke();
            }
        }

        //adWhen / triggerPlayTurnId của lượt ShowRewardedVideo ĐANG chạy (null ngoài lời gọi đó)
        private static (string adWhen, string triggerPlayTurnId)? _rewardedShowContext;

        /// <summary>
        /// Nhánh "rewarded đổi sang inter" (<c>showInterstitialInstead</c>) gọi ngay trước khi chiếu inter: chép ngữ cảnh
        /// của lượt rewarded sang lần xem interstitial, để show / impression / close của inter mang đúng lý do + lượt chơi.
        /// Ngoài lời gọi <see cref="ShowRewardedVideo"/> thì không làm gì.
        /// </summary>
        internal static void ApplyRewardedContextToInterstitial()
        {
            if (_rewardedShowContext is not { } context) return;
            UpdateShowContext(AdType.Interstitial, context.adWhen, context.triggerPlayTurnId);
        }

        /// <summary>
        /// Khai <c>adWhen</c> / <c>triggerPlayTurnId</c> cho lần xem hiện tại của format. Cả hai trống thì không gọi:
        /// giá trị trống không xoá gì bên SDK, gọi chỉ thêm cảnh báo "chưa có lần xem" vô ích.
        /// </summary>
        private static void UpdateShowContext(AdType type, string adWhen, string triggerPlayTurnId)
        {
            if (string.IsNullOrEmpty(adWhen) && string.IsNullOrEmpty(triggerPlayTurnId)) return;
            try
            {
                FalconBigDataController.Ad.UpdateContext(type, adWhen: adWhen, triggerPlayTurnId: triggerPlayTurnId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public bool IsRewardedVideoReady()
        {
            return GetAdsService().IsRewardedVideoReady();
        }

        public void DebugLog(object obj)
        {
#if ENABLE_DEBUG_LOG
            Debug.Log("<color=#FFFF80>-----  " + obj + "  -----</color>");
#endif
        }

        public void DebugError(object obj)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError(obj);
#endif
        }

        public StatusAds GetStatusAdsFromPlacement(string placementId, bool isRewarded = true)
        {
            var provider = GetProvider();
            if (provider != null)
            {
                return provider.GetStatusAdsFromPlacement(placementId, isRewarded);
            }

            var sa = new StatusAds
            {
                statusShowAds = StatusShowAds.DayLimit
            };
            DebugError("GetStatusAdsFromPlacement provider is null");
            return sa;
        }

        public RewardedCallback GetRewardedCallbackFromGroupId(string placementId)
        {
            var provider = GetProvider();
            if (provider != null)
            {
                return provider.GetRewardedCallbackFromGroupId(placementId);
            }

            DebugError("GetRewardedCallbackFromGroupId provider is null");
            return new RewardedCallback()
            {
                listReward = new List<Reward>()
            };
        }

        public int GetMaxViewByGroupId(string groupId)
        {
            var provider = GetProvider();
            if (provider != null)
            {
                return provider.GetMaxViewByGroupId(groupId);
            }

            DebugError("GetMaxViewByGroupId provider is null");
            return -1;
        }

        public int GetViewCountByGroupId(string groupId)
        {
            var provider = GetProvider();
            if (provider != null)
            {
                return provider.GetViewCountByGroupId(groupId);
            }

            DebugError("GetViewCountByGroupId provider is null");
            return 0;
        }

        public float GetBannerHeightInPixels()
        {
            return GetAdsService().GetBannerHeightInPixels();
        }
    }

    public enum MediationType
    {
        Max,
        LevelPlay,
        Admob
    }
}
