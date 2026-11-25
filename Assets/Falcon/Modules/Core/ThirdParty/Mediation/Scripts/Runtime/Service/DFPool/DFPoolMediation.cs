/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-27
 */

#if MAX_ENABLE
using System;
using System.Collections;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine;
using GameConfig = Falcon.Modules.Core.RemoteConfig.FConfig;
using CmsConfig = Falcon.Modules.Core.RemoteConfigCms.FConfig;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.DFPool
{
    /// <summary>
    /// ĐIỂM VÀO của DFPool. Tự khởi động (RuntimeInitializeOnLoadMethod) — không file nào ngoài thư
    /// mục này gọi tới nó, nên XOÁ CẢ THƯ MỤC là gỡ sạch.
    ///
    /// Luồng:
    ///  0. defaultAdId (nếu có): luôn giữ sẵn 1 ad AdMob xin KHÔNG giá sàn trong KHO, không chờ MAX.
    ///  1. Ad MAX bước 1 (MultiCall / unit đơn) load xong -> revenue R.
    ///  2. Mỗi target trong config -> một request AdMob ở giá sàn R × multiplier. Ad về -> vào KHO.
    ///  3. Slot MAX (2 id / format) mượn ad đắt nhất trong kho -> MaxSdk.LoadX(slot); waterfall
    ///     DFPoolNetwork nhận tier đầu tiên <= giá ad đó.
    ///  Lúc show, MultiCall so giá slot đắt nhất (qua <see cref="SecondaryAdSource"/>) với ad MAX bước 1.
    ///
    /// Bật/tắt bằng remote config <c>moMulDfPoolConfig</c> (CMS trước, rồi data4game): rỗng = tắt.
    /// Chỉ chạy trên Android.
    /// </summary>
    public static class DFPoolMediation
    {
        private const string _TAG = "[DFPool]";
        private const float _TICK_SECONDS = 1f;

        private static bool _bootstrapped;
        private static bool _started;
        private static bool _starting;
        private static DFPoolFormat _inter;
        private static DFPoolFormat _rewarded;
        private static readonly Dictionary<string, DFPoolFormat> _bySlot = new Dictionary<string, DFPoolFormat>();
        private static DFPoolSource _source;
        private static Runner _runner;
        private static int _nextHandle;

        //revenue MAX bước 1 về TRƯỚC lúc module start -> giữ mốc mới nhất, phát lại khi start
        private static double _bufferedInterRevenue;
        private static double _bufferedRewardedRevenue;

        //số lượt show fullscreen đang mở (slot của ta + MAX bên ngoài) -> không show giả lúc này
        private static int _showDepth;
        private static int _externalShowing;

        /// <summary>Module đang chạy (đã đọc được config hợp lệ).</summary>
        public static bool IsRunning => _started;

        internal static bool IsAnyFullscreenShowing => _showDepth > 0 || _externalShowing > 0;

        internal static int NextHandle() => ++_nextHandle;

        internal static void BeginShow() => _showDepth++;

        internal static void EndShow()
        {
            if (_showDepth > 0) _showDepth--;
        }

        /// <summary>Mọi log của DFPool: <c>[DFPool][mã giai đoạn] ...</c> — xem <see cref="DFPoolStage"/>.</summary>
        internal static void Log(string stage, string message)
        {
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_TAG}[{stage}] {message}");
#endif
        }

        #region khởi động

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_bootstrapped || !DFPoolNative.IsSupported) return;
            _bootstrapped = true;

            //meta AdMob chờ DFPool quyết định có nhận format nào không rồi mới init (tránh request AdMob gấp đôi)
            AdmobTakeover.Expect();

            //slot MAX phải được khai TRƯỚC MAX init (disable_b2b + mảng init + disable_auto_retries), thiếu
            //có thể crash -> FalconMaxService hỏi danh sách slot ngay trước MaxSdk.InitializeSdk
            MaxPreInitAdUnits.Register("DFPool", () => DFPoolConfigReader.IsSettled, CollectPreInitSlots);

            //đăng ký sớm: revenue bước 1 về trước khi start vẫn được giữ lại
            SubscribeMax();

            var go = new GameObject("DFPoolMediation");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();

            //config về muộn (sau lần đọc đầu bị rỗng) -> thử start lại
            GameConfig.OnUpdateFromNet += OnConfigUpdated;
            CmsConfig.OnUpdateFromNet += OnConfigUpdated;

            Log(DFPoolStage.INIT, "bootstrap xong, chờ MAX init + remote config");
            _runner.StartCoroutine(WaitThenStart());
        }

        /// <summary>FalconMaxService gọi ngay trước MaxSdk.InitializeSdk: đọc config đang có, khai mọi slot hợp lệ.</summary>
        private static void CollectPreInitSlots(ICollection<string> interstitial, ICollection<string> rewarded)
        {
            var json = DFPoolConfigReader.ReadJson(out var source);
            var config = DFPoolConfigReader.Parse(json);
            if (config == null)
            {
                Log(DFPoolStage.INIT, $"trước MAX init: '{DFPoolConfigReader.KEY}' ({source}, " +
                                      $"đã chốt nguồn={DFPoolConfigReader.IsSettled}) rỗng / không hợp lệ -> " +
                                      "KHÔNG khai slot nào, DFPool tắt cả phiên");
                return;
            }

            if (config.interstitial != null && config.interstitial.IsValid)
                foreach (var id in config.interstitial.ValidSlotIds()) interstitial.Add(id);
            if (config.rewarded != null && config.rewarded.IsValid)
                foreach (var id in config.rewarded.ValidSlotIds()) rewarded.Add(id);
            Log(DFPoolStage.INIT, $"trước MAX init ({source}, đã chốt nguồn={DFPoolConfigReader.IsSettled}): " +
                                  $"khai slot inter=[{string.Join(", ", interstitial)}] " +
                                  $"rewarded=[{string.Join(", ", rewarded)}]");
        }

        /// <summary>
        /// Chỉ giữ slot đã khai trước MAX init (đúng format). Slot lạ (config về muộn / đổi giữa phiên) KHÔNG
        /// được load: nó không nằm trong disable_b2b / mảng init / disable_auto_retries -> có thể crash.
        /// </summary>
        private static DFPoolFormatConfig KeepPreInitSlots(DFPoolFormatConfig format, bool rewarded)
        {
            if (format == null) return null;
            var kept = new List<string>();
            foreach (var id in format.ValidSlotIds())
            {
                if (MaxPreInitAdUnits.IsRegistered(id, rewarded)) kept.Add(id);
                else
                    Debug.LogWarning($"{_TAG} slot {(rewarded ? "rewarded" : "inter")} {id} KHÔNG được khai " +
                                     "trước MAX init (config về muộn / đổi giữa phiên) -> bỏ, không load");
            }

            format.maxSlotIds = kept.ToArray();
            return format;
        }

        private static IEnumerator WaitThenStart()
        {
            //MAX init xong = đã qua consent, slot MAX load được
            //chờ ĐỦ 3 bước init của MAX (CallBeforeInit -> Init -> CallAfterInit), không dùng MaxSdk.IsInitialized()
            while (!MaxPreInitAdUnits.IsAfterInitDone) yield return null;
            Log(DFPoolStage.INIT, "MAX đã init -> chờ config '" + DFPoolConfigReader.KEY + "' chốt nguồn");

            var deadline = Time.realtimeSinceStartup + AdsConfigGate.CMS_FIRST_TIMEOUT_SECONDS;
            while (!DFPoolConfigReader.IsSettled && Time.realtimeSinceStartup < deadline) yield return null;
            Log(DFPoolStage.INIT, DFPoolConfigReader.IsSettled
                ? "config đã chốt nguồn -> đọc config"
                : "hết thời gian chờ config -> đọc config đang có");

            TryStart();
        }

        private static void OnConfigUpdated()
        {
            if (_started || _runner == null) return;
            //hoãn 1 frame: cache Config<T> có thể chưa bị xoá lúc event này bắn (xem bridge AdMob)
            _runner.StartCoroutine(StartNextFrame());
        }

        private static IEnumerator StartNextFrame()
        {
            yield return null;
            Log(DFPoolStage.INIT, "remote config vừa cập nhật (OnUpdateFromNet) -> thử bật lại");
            if (MaxPreInitAdUnits.IsAfterInitDone) TryStart();
        }

        private static void TryStart()
        {
            if (_started || _starting) return;
            _starting = true;
            try
            {
                var json = DFPoolConfigReader.ReadJson(out var source);
                var config = DFPoolConfigReader.Parse(json);
                if (config == null)
                {
                    Log(DFPoolStage.INIT, $"'{DFPoolConfigReader.KEY}' ({source}) rỗng / không hợp lệ -> TẮT");
                    return;
                }

                if (!MaxPreInitAdUnits.IsLocked)
                {
                    Log(DFPoolStage.INIT, "MAX không được init qua FalconMaxService -> slot chưa khai trước init -> TẮT");
                    return;
                }

                config.interstitial = KeepPreInitSlots(config.interstitial, false);
                config.rewarded = KeepPreInitSlots(config.rewarded, true);
                if (!config.HasAnyFormat)
                {
                    Log(DFPoolStage.INIT, "không còn slot nào đã khai trước MAX init -> TẮT");
                    return;
                }

                if (!DFPoolNative.Init(Debug.isDebugBuild, config.adValueInMicros))
                {
                    Log(DFPoolStage.INIT, "không gọi được native DFPoolBridge (thiếu file Java trong build?) -> TẮT");
                    return;
                }

                if (config.interstitial != null && config.interstitial.IsValid)
                    _inter = CreateFormat(AdType.Interstitial, config.interstitial, config);
                if (config.rewarded != null && config.rewarded.IsValid)
                    _rewarded = CreateFormat(AdType.Reward, config.rewarded, config);

                if (_inter == null && _rewarded == null) return;

                DFPoolNative.AdLoaded += OnNativeLoaded;
                DFPoolNative.AdFailed += OnNativeFailed;
                DFPoolNative.AdPaid += OnNativePaid;

                _source = new DFPoolSource();
                SecondaryAdSource.Register(_source);
                _started = true;

                Log(DFPoolStage.INIT, $"BẬT ({source}) — inter {Describe(_inter)} | rewarded {Describe(_rewarded)} | " +
                    $"ttl={config.ttlMinutes}m cap={config.poolCap}");

                if (_bufferedInterRevenue > 0d || _bufferedRewardedRevenue > 0d)
                    Log(DFPoolStage.MAX_REVENUE, "phát lại mốc MAX nhận được trước lúc bật");
                if (_bufferedInterRevenue > 0d) _inter?.OnExternalRevenue(_bufferedInterRevenue);
                if (_bufferedRewardedRevenue > 0d) _rewarded?.OnExternalRevenue(_bufferedRewardedRevenue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _starting = false;
                //format DFPool chạy -> meta AdMob bỏ id mediation=admob của format đó; tắt / lỗi -> trả lại cả hai
                AdmobTakeover.Resolve(_started && _inter != null, _started && _rewarded != null);
            }
        }

        private static DFPoolFormat CreateFormat(AdType type, DFPoolFormatConfig formatConfig, DFPoolConfigData root)
        {
            var format = new DFPoolFormat(type, formatConfig, root);
            foreach (var slotId in format.SlotIds)
            {
                if (_bySlot.ContainsKey(slotId))
                {
                    Debug.LogWarning($"{_TAG} slot {slotId} khai trùng giữa hai format -> bỏ qua");
                    continue;
                }

                _bySlot[slotId] = format;
                //từ giờ FalconMaxService / MultiCall / bridge AdMob bỏ qua event của slot này
                ExternalMaxAdUnits.Claim(slotId);
                if (type == AdType.Reward)
                    MaxSdk.SetRewardedAdLocalExtraParameter(slotId, "dfpool_slot_id", slotId);
                else
                    MaxSdk.SetInterstitialLocalExtraParameter(slotId, "dfpool_slot_id", slotId);
            }

            return format;
        }

        private static string Describe(DFPoolFormat f) => f == null
            ? "(tắt)"
            : $"slots=[{string.Join(", ", f.SlotIds)}] mặc định={f.DefaultAdUnitId ?? "(không)"} " +
              $"thang giá sàn={f.TargetCount} mức";

        private sealed class Runner : MonoBehaviour
        {
            private float _nextTick;

            private void Update()
            {
                DFPoolNative.Drain();
                if (!_started || Time.unscaledTime < _nextTick) return;
                _nextTick = Time.unscaledTime + _TICK_SECONDS;
                _inter?.Pump();
                _rewarded?.Pump();
            }

            //Cuối frame: mọi callback AdMob (Drain ở Update) + MAX (MaxSdkCallbacks) của frame này đã về
            //đủ -> quyết định MỘT lần trên toàn bộ kho, ad đắt nhất cả đợt được gắn trước.
            private void LateUpdate()
            {
                if (!_started) return;
                _inter?.PumpIfDirty();
                _rewarded?.PumpIfDirty();
            }

            private void OnApplicationPause(bool paused)
            {
                //quay lại app sau lâu -> soát hạn dùng ngay, không chờ tick
                if (!paused && _started) _nextTick = 0f;
            }
        }

        #endregion

        #region native callbacks

        private static DFPoolFormat FormatOfHandle(int handle)
        {
            if (_inter != null && _inter.OwnsHandle(handle)) return _inter;
            if (_rewarded != null && _rewarded.OwnsHandle(handle)) return _rewarded;
            return null;
        }

        private static void OnNativeLoaded(int handle, double valueUsd, string adSource, string responseId)
        {
            var format = FormatOfHandle(handle);
            if (format == null)
            {
                DFPoolNative.DestroyAd(handle);
                return;
            }

            format.OnNativeLoaded(handle, valueUsd, adSource, responseId);
        }

        private static void OnNativeFailed(int handle, int code, string message, string responseId)
            => FormatOfHandle(handle)?.OnNativeFailed(handle, code, message, responseId);

        private static void OnNativePaid(string slotId, int handle, long valueMicros, string currency, int precision,
            string adSource)
        {
            if (slotId != null && _bySlot.TryGetValue(slotId, out var format))
                format.OnNativePaid(slotId, valueMicros, currency, precision, adSource);
        }

        #endregion

        #region MAX callbacks

        private static void SubscribeMax()
        {
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (id, info) => OnLoaded(AdType.Interstitial, id, info);
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (id, err) => Route(id)?.OnMaxLoadFailed(id, err);
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (id, _) => OnDisplayed(id);
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (id, err, _) => OnDisplayFailed(id, err);
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += (id, _) => Route(id)?.OnMaxClicked(id);
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (id, _) => OnHidden(id);
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += (id, info) => OnRevenuePaid(AdType.Interstitial, id, info);

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += (id, info) => OnLoaded(AdType.Reward, id, info);
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += (id, err) => Route(id)?.OnMaxLoadFailed(id, err);
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += (id, _) => OnDisplayed(id);
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (id, err, _) => OnDisplayFailed(id, err);
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += (id, _) => Route(id)?.OnMaxClicked(id);
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (id, _) => OnHidden(id);
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += (id, info) => OnRevenuePaid(AdType.Reward, id, info);
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (id, _, _) => Route(id)?.OnMaxReward(id);
        }

        private static DFPoolFormat Route(string adUnitId)
            => adUnitId != null && _bySlot.TryGetValue(adUnitId, out var f) ? f : null;

        /// <summary>
        /// Revenue-paid của MAX. Slot của ta -> slot tự xử lý. Ad MAX của mediation chính (MultiCall / unit đơn
        /// / Lua) = MAX THẮNG giá lượt show đó -> khai báo impression với AdMob cho các ad AdMob trong kho đã
        /// thua (Google multi-platform mediation), y như tầng meta AdMob (bridge OnExternalImpression).
        /// </summary>
        private static void OnRevenuePaid(AdType type, string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            try
            {
                var own = Route(adUnitId);
                if (own != null)
                {
                    own.OnMaxRevenuePaid(adUnitId, adInfo);
                    return;
                }

                if (!_started || adInfo == null) return;
                if (ExternalMaxAdUnits.IsExternal(adUnitId) && !ExternalMaxAdUnits.IsPriceAnchor(adUnitId)) return;
                var format = type == AdType.Reward ? _rewarded : _inter;
                format?.OnExternalImpression(adInfo.Revenue, adInfo.RevenuePrecision);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void OnLoaded(AdType type, string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            try
            {
                var own = Route(adUnitId);
                if (own != null)
                {
                    own.OnMaxLoaded(adUnitId, adInfo);
                    return;
                }

                //BƯỚC 1: ad MAX của mediation chính -> mốc giá cho bước 2
                //unit ngoài bị bỏ, TRỪ ad MAX thật của phiên Lua (đánh dấu mốc giá)
                if ((ExternalMaxAdUnits.IsExternal(adUnitId) && !ExternalMaxAdUnits.IsPriceAnchor(adUnitId)) ||
                    adInfo == null || adInfo.Revenue <= 0d) return;
                var label = type == AdType.Reward ? "[rewarded]" : "[inter]";
                var ecpm = (adInfo.Revenue * 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                if (!_started)
                {
                    Log(DFPoolStage.MAX_REVENUE, $"{label} MAX {adUnitId} load xong ${ecpm} eCPM " +
                                                 $"({adInfo.NetworkName}) — module chưa bật, giữ mốc để phát lại");
                    if (type == AdType.Reward) _bufferedRewardedRevenue = adInfo.Revenue;
                    else _bufferedInterRevenue = adInfo.Revenue;
                    return;
                }

                var format = type == AdType.Reward ? _rewarded : _inter;
                if (format == null) return; //format này không bật
                Log(DFPoolStage.MAX_REVENUE, $"{label} MAX {adUnitId} load xong ${ecpm} eCPM " +
                                             $"({adInfo.NetworkName}) -> bước 2");
                format.OnExternalRevenue(adInfo.Revenue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // Ad MAX bên ngoài lên / rời màn hình -> chặn show giả trong lúc đó
        private static void OnDisplayed(string adUnitId)
        {
            var own = Route(adUnitId);
            if (own != null) own.OnMaxDisplayed(adUnitId);
            else _externalShowing = 1;
        }

        private static void OnDisplayFailed(string adUnitId, MaxSdkBase.ErrorInfo error)
        {
            var own = Route(adUnitId);
            if (own != null) own.OnMaxDisplayFailed(adUnitId, error);
            else _externalShowing = 0;
        }

        private static void OnHidden(string adUnitId)
        {
            var own = Route(adUnitId);
            if (own != null) own.OnMaxHidden(adUnitId);
            else _externalShowing = 0;
        }

        #endregion

        /// <summary>Kho DFPool nhìn từ phía MultiCall lúc so giá để show.</summary>
        private sealed class DFPoolSource : ISecondaryAdSource
        {
            private static DFPoolFormat Of(AdType type) => type switch
            {
                AdType.Interstitial => _inter,
                AdType.Reward => _rewarded,
                _ => null
            };

            public bool IsReady(AdType type) => Of(type)?.IsReady() ?? false;

            public double BestRevenue(AdType type) => Of(type)?.BestRevenue() ?? SecondaryAdSource.NO_AD;

            public string BestAdUnitId(AdType type) => Of(type)?.BestAdUnitId();

            public string BestNetwork(AdType type) => Of(type)?.BestNetwork();

            public string DescribeCache(AdType type)
                => Of(type)?.DescribeCache() ?? "    (DFPool: format này không bật)";

            public bool Show(AdType type, string where, Action onReward, Action onHidden, Action onFail,
                SecondaryAdPaid onPaid)
                => Of(type)?.Show(where, onReward, onHidden, onFail, onPaid) ?? false;
        }
    }
}
#endif
