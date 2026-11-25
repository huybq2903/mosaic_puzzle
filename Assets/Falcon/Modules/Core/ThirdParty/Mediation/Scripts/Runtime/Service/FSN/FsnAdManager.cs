using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AppsFlyerSDK;
using Falcon.Admob;
using Falcon.Helpers.Devkit;
using Falcon.Helpers.EventBus;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfig;
using Falcon.Modules.Core.ThirdParty.Firebase.Scripts.Runtime;
using Falcon.Modules.Core.ThirdParty.Mmp.Runtime;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public class FsnAdManager : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FsnAdIOS_ForceDismissVisibleNativeContainers();
#endif
        private static FsnAdManager _instance;
        private FsnAdsConfigsData _fsnAdsConfigsData;
        private bool _getConfigFromServer;
        private string _campaignID;
        private bool _enableFsn;

        private const string _CampaignID = "falcon.module.core.thirdparty.mediation.appsflyer_campaign_id";
        private const string _CampaignIdAppsflyer = "campaign_id";

        public static string AppsflyerInstallTime;
        public static string AppsflyerMediaSource;

        /// <summary>
        /// af_status của conversion data ("Organic" / "Non-organic"). Dùng để suy ra ua_channel khi
        /// media_source rỗng — xem <c>AdmobMediationFalconBridge.ResolveUaChannel</c>.
        /// </summary>
        public static string AppsflyerAfStatus;
        public event Action<FsnAdType> OnNativeAdHidden;
        public event Action<FsnAdType> OnNativeAdReady;
        public event Action<FsnAdType> OnNativeAdLoadFailed;
        public event Action<FsnAdType, string, string, long, string> OnNativeAdPaid;
        public static FsnAdManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject { name = "FsnAdManager" };
                    _instance = go.AddComponent<FsnAdManager>();
                    DontDestroyOnLoad(go);

                    // JSON hỏng không được ném ra khỏi getter: Instance bị chạm từ MultiCall / MAX init, ném
                    // ở đây là chết luôn cả luồng show ad lẫn đăng ký OnUpdateFromNet bên dưới.
                    _instance._fsnAdsConfigsData = ParseConfig(ReadConfigJson()) ?? new FsnAdsConfigsData();

                    //Đăng ký muộn (lần đầu chạm Instance) KHÔNG mất giá trị đầu tiên: ngay phía trên đã
                    //đọc thẳng config đang có. Event chỉ để bắt các lần refresh SAU đó. Nghe CẢ HAI nguồn:
                    //key FSN đọc CMS trước, CMS không khai mới tới data4game (ResolveConfig).
                    FConfigController.Instance.OnUpdateFromNet += OnRemoteConfigUpdated;
                    try
                    {
                        Falcon.Modules.Core.RemoteConfigCms.FConfigControllerCms.Instance.OnUpdateFromNet +=
                            OnRemoteConfigUpdated;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

                return _instance;
            }
        }

        private static void OnRemoteConfigUpdated()
        {
            try
            {
                if (_instance == null) return;

                // Config mới hỏng thì giữ config cũ, không để _fsnAdsConfigsData thành null.
                var parsed = ParseConfig(ReadConfigJson());
                if (parsed != null) _instance._fsnAdsConfigsData = parsed;
                //Chỉ coi là "đã có config" khi mọi key đã CHỐT nguồn: data4game về trước mà CMS chưa
                //trả lời thì chưa được bật FSN (đường SetCampaignIdFromAppsflyer cũng gác ở cờ này).
                if (IsConfigReady) _instance._getConfigFromServer = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void Awake()
        {
            // FalconAppsflyerService emit campaign_id qua EventBus khi có conversion data.
            GameEvent<string>.Register("falcon.modules.thirdparty.appsflyer.campaign_id",
                OnUpdateCampaignIdFromAppsflyer, null);
        }

        private void OnUpdateCampaignIdFromAppsflyer(string campaignId)
        {
            SetCampaignIdFromAppsflyer(campaignId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            _ = Instance;
        }

        private static string ReadConfigJson()
        {
            // Chỉ đọc Ver2 (có adMultipliers). moMulFsnAdsConfigs cũ để nguyên cho các bản app cũ.
            return ResolveConfig().moMulFsnAdsConfigsVer2;
        }

        private const string _KEY_ADS_CONFIG = nameof(FsnAdsConfig.moMulFsnAdsConfigsVer2);
        private const string _KEY_CAMPAIGN_FILTER = nameof(FsnAdsConfig.moMulEnableCampaignFilter);
        private const string _KEY_CAMPAIGN_LIST = nameof(FsnAdsConfig.moMulListCampaignIdsConfigs);

        /// <summary>
        /// Config FSN đã gộp, TỪNG KEY một: CMS đã khai key (kể cả giá trị rỗng / false) thì lấy CMS, chưa
        /// khai thì lấy data4game. Một nguồn đọc lỗi không kéo chết nguồn kia.
        /// </summary>
        private static FsnAdsConfig ResolveConfig()
        {
            FsnAdsConfig game = null;
            FsnAdsConfigCms cms = null;
            try
            {
                game = FConfigController.Instance.Config<FsnAdsConfig>();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                cms = Falcon.Modules.Core.RemoteConfigCms.FConfig.Config<FsnAdsConfigCms>();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            game ??= new FsnAdsConfig();
            cms ??= new FsnAdsConfigCms();
            return new FsnAdsConfig
            {
                moMulFsnAdsConfigsVer2 = AdsConfigGate.CmsHasKey(_KEY_ADS_CONFIG)
                    ? cms.moMulFsnAdsConfigsVer2
                    : game.moMulFsnAdsConfigsVer2,
                moMulEnableCampaignFilter = AdsConfigGate.CmsHasKey(_KEY_CAMPAIGN_FILTER)
                    ? cms.moMulEnableCampaignFilter
                    : game.moMulEnableCampaignFilter,
                moMulListCampaignIdsConfigs = AdsConfigGate.CmsHasKey(_KEY_CAMPAIGN_LIST)
                    ? cms.moMulListCampaignIdsConfigs
                    : game.moMulListCampaignIdsConfigs,
                moMulFsnAdsConfigs = game.moMulFsnAdsConfigs
            };
        }

        /// <summary>
        /// CMS đã khai đủ mọi key FSN cần chưa — đủ thì khỏi chờ data4game. Danh sách campaign chỉ cần khi
        /// bộ lọc campaign đang bật.
        /// </summary>
        private static bool CmsHasAllKeys()
        {
            if (!AdsConfigGate.CmsHasKey(_KEY_ADS_CONFIG) || !AdsConfigGate.CmsHasKey(_KEY_CAMPAIGN_FILTER))
                return false;
            return !ResolveConfig().moMulEnableCampaignFilter || AdsConfigGate.CmsHasKey(_KEY_CAMPAIGN_LIST);
        }

        /// <summary>Config FSN đã chốt nguồn chưa — cùng luật với AdMob, xem <see cref="AdsConfigGate.IsCmsFirstReady"/>.</summary>
        private static bool IsConfigReady => AdsConfigGate.IsCmsFirstReady(CmsHasAllKeys());

        /// <summary>null nếu chuỗi rỗng hoặc JSON hỏng — người gọi tự quyết giữ config cũ hay dùng rỗng.</summary>
        private static FsnAdsConfigsData ParseConfig(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<FsnAdsConfigsData>(json);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_Tag}[FSN] moMulFsnAdsConfigsVer2 không parse được: {e.Message}");
#endif
                return null;
            }
        }

        /// <summary>
        /// Gọi callback / event ra ngoài module. Exception của project không được phép làm dở dang phần dọn
        /// state phía sau (overlay đen, pause gameplay, nạp ad kế tiếp...).
        /// </summary>
        private static void SafeInvoke(Action action)
        {
            if (action == null) return;

            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnNativeAdHiddenSafe(FsnAdType adType) => SafeInvoke(() => OnNativeAdHidden?.Invoke(adType));

        private void OnNativeAdReadySafe(FsnAdType adType) => SafeInvoke(() => OnNativeAdReady?.Invoke(adType));

        private void OnNativeAdLoadFailedSafe(FsnAdType adType) =>
            SafeInvoke(() => OnNativeAdLoadFailed?.Invoke(adType));

        private bool TryGetPreparedAd(FsnAdType adType, out PreparedAdInfo info)
        {
            if (_preparedAds.TryGetValue(adType, out info) && info != null) return true;

#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"{_Tag}[FSN] Không có PreparedAdInfo cho AdType={adType}.");
#endif
            return false;
        }

        private const string _Tag = "FNS";

        // iOS/Unity native overlay does not pause the Unity loop by itself.
        // Keep a small guard so nested ads cannot restore Time.timeScale too early.
        private int _fsnNativePauseDepth;

        // Failsafe cho case user bấm Home khi Native full-screen đang show:
        // callback close từ native có thể không về, làm Time.timeScale kẹt ở 0 khi mở lại app.
        private bool _fsnNativePausedDuringApplicationBackground;
        private Coroutine _fsnNativeResumeRecoveryCoroutine;

        // Native view của FSN được add ở tầng OS (Android Activity / iOS UIWindow),
        // KHÔNG đồng bộ với frame của Unity. Sau khi gọi ShowNativeAd vài frame thì
        // FSN thường vẫn chưa thực sự phủ kín màn hình (còn phải post lên UI thread,
        // inflate layout, render media...). Nếu bỏ overlay đen ngay lúc đó thì user
        // sẽ nhìn thấy màn hình game ở khoảng giữa MAX và FSN.
        //
        // Vì vậy overlay được giữ thêm: tối thiểu N frame, rồi cộng thêm một khoảng
        // realtime nữa mới tắt. Giữ dư vài trăm ms là an toàn vì lúc này FSN đã nằm
        // đè lên Unity view, overlay đen ở dưới không gây ảnh hưởng gì về mặt hình ảnh.
        private const int _NATIVE_SHOWN_OVERLAY_HOLD_MIN_FRAMES = 5;
        private const float _NATIVE_SHOWN_OVERLAY_HOLD_SECONDS = 1f;
        private int _nativeShownOverlayHoldMinFrames = _NATIVE_SHOWN_OVERLAY_HOLD_MIN_FRAMES;
        private float _nativeShownOverlayHoldSeconds = _NATIVE_SHOWN_OVERLAY_HOLD_SECONDS;
        private Coroutine _hideOverlayAfterNativeShownCoroutine;

        // iOS có thể chưa phát Unity frame ngay sau khi MAX vừa dismiss.
        // Vì vậy không được chỉ dựa vào WaitForEndOfFrame hoặc callback native
        // để bỏ màn overlay đen.
        // Timeout phải LỚN HƠN thời gian giữ overlay ở trên, nếu không failsafe sẽ
        // tắt màn đen trước khi FSN kịp hiện.
        private const float _IOS_NATIVE_TRANSITION_OVERLAY_BASE_TIMEOUT_SECONDS = 3f;

        private float IosNativeTransitionOverlayTimeoutSeconds =>
            _IOS_NATIVE_TRANSITION_OVERLAY_BASE_TIMEOUT_SECONDS +
            Mathf.Max(0f, _nativeShownOverlayHoldSeconds);

        private Coroutine _iosNativeTransitionOverlayFailsafeCoroutine;
        private int _iosNativeTransitionOverlayToken;
        private bool _iosNativeTransitionOverlayPending;

        // Class lưu trữ thông tin của lượt Ad đang được chuẩn bị (Lazy Load)
        private class PreparedAdInfo
        {
            // Ad đã được chọn (đắt nhất trong slots) và rút ra để show. Ad đang load/chờ show nằm trong slots.
            public FsnAd adInstance;

            // Thang giá: slots[0] = DEFAULT (multiplier 0), phía sau là các slot floor theo thứ tự config.
            public readonly List<LadderSlot> slots = new List<LadderSlot>();

            // Tăng mỗi khi bắt đầu lượt mới (load default). Callback/coroutine của lượt cũ tự dừng.
            public int cycleSequence;
            public Coroutine floorCoroutine;

            // Multiplier / revenue của ad đang được show (chỉ để log).
            public float shownMultiplier;
            public double shownRevenue;

            // Instance đang thực sự hiển thị trên màn hình.
            // HideTrackedNative chỉ được đụng vào instance này.
            public FsnAd showingAdInstance;

            // Token chống callback show cũ đụng nhầm lượt show mới.
            public int showSequence;

            // Callback/guard dùng khi app bị background trong lúc Native đang pause gameplay.
            // Nếu lifecycle recovery đã force-complete lượt show này thì callback native muộn phải bị bỏ qua.
            public Action lifecycleNativeFinishedCallback;
            public int lifecycleShowingSequence;
            public bool lifecycleForceCompleted;

            // Token chống callback load cũ đụng nhầm lượt load mới.
            // currentAdUnitId không đủ an toàn nếu 2 lượt load dùng cùng 1 ad unit id.
            public int loadSequence;

            // Ad DEFAULT đang load (floor load riêng trong từng slot).
            public bool isLoading;

            // Chỉ là cache trạng thái để debug/guard. Trạng thái thật vẫn hỏi IsNativeAdReady().
            public bool isReady;

            // Layout của ad được chọn để show (copy từ slot lúc chọn).
            public string currentAdUnitId;
            public int styleIndex;
            public string layoutDebugName;
            public FsnNativeAdType nativeType;
            public FsnNativeAdRatio nativeRatio;
            public int openStorePosition;
            public int closePosition;

            public string GetDebugSummary()
            {
                return
                    $"AdUnitId={currentAdUnitId}, x{shownMultiplier:0.##}, Revenue=${shownRevenue:F6}, NativeType={nativeType}, Ratio={nativeRatio}, Style={styleIndex}({layoutDebugName}), OpenStoreUI={openStorePosition}, CloseUI={closePosition}, DefaultLoading={isLoading}, IsReady={isReady}";
            }
        }

        // Một bậc trong thang giá: một adUnitId + multiplier, giữ tối đa MỘT ad (FsnAd = 1 native ad).
        private class LadderSlot
        {
            public string adUnitId;
            public float multiplier; // 0 = default (không floor)
            public bool IsDefault => multiplier <= 0f;

            public FsnAd ad;
            public bool isLoading;
            public int loadSequence; // chống callback load cũ
            public double revenue;   // USD / impression đọc sau khi load, 0 = không đọc được
            public double floorUsd;  // floor đã xin (USD / impression)

            // Layout random riêng cho ad của slot này.
            public int styleIndex;
            public string layoutDebugName;
            public FsnNativeAdType nativeType;
            public FsnNativeAdRatio nativeRatio;
            public int openStorePosition;
            public int closePosition;

            public string GetDebugSummary()
            {
                var state = isLoading ? "loading" : (ad != null ? $"ad revenue=${revenue:F6}" : "rỗng");
                return $"x{multiplier:0.##} floor=${floorUsd:F6} [{state}] ID={adUnitId} Style={styleIndex}";
            }
        }

        // Quản lý riêng biệt instance cho từng loại Ad
        private readonly Dictionary<FsnAdType, PreparedAdInfo> _preparedAds =
            new Dictionary<FsnAdType, PreparedAdInfo>()
            {
                { FsnAdType.Interstitial, new PreparedAdInfo() },
                { FsnAdType.Rewarded, new PreparedAdInfo() },
                { FsnAdType.Aoa, new PreparedAdInfo() },
                { FsnAdType.AdBreak, new PreparedAdInfo() },
                { FsnAdType.Collapsible, new PreparedAdInfo() }
            };

        public void InitFsnAd()
        {
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_Tag}[FSN] ========== InitFsnAd START ==========");
#endif
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            StartCoroutine(WaitFromServer());

            IEnumerator WaitFromServer()
            {
                //Chờ config CHỐT nguồn (CMS trước, data4game sau) chứ không chỉ chờ data4game: load ad
                //bằng ID data4game rồi CMS mới về là ID cũ không gỡ được nữa. Trần dài, chỉ để không treo
                //khi CMS kẹt NotStarted.
                var deadline = Time.realtimeSinceStartup + AdsConfigGate.CMS_FIRST_TIMEOUT_SECONDS;
                yield return new WaitUntil(() => IsConfigReady || Time.realtimeSinceStartup >= deadline);
#if ENABLE_DEBUG_LOG
                Debug.Log($"{_Tag}[FSN] " + (IsConfigReady ? "config đã chốt nguồn" : "TIMEOUT chờ config") +
                          " — " + AdsConfigGate.Describe());
#endif
                //config có thể về qua CMS mà không qua event data4game -> đọc lại một lần trước khi dùng
                var parsed = ParseConfig(ReadConfigJson());
                if (parsed != null) _fsnAdsConfigsData = parsed;
                _getConfigFromServer = true;
                CheckAfterGetFromServer();
            }
#else
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_Tag}[FSN] Platform: NOT ANDROID or IOS - Skipping FSN init");
#endif
#endif
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_Tag}[FSN] ========== InitFsnAd END ==========");
#endif
        }

        private void OnApplicationPause(bool pauseStatus)
        {
#if UNITY_ANDROID || UNITY_IOS
            if (pauseStatus)
            {
                NotifyFullScreenAdDisplayed();
                if (_fsnNativePauseDepth > 0)
                {
                    _fsnNativePausedDuringApplicationBackground = true;
#if ENABLE_DEBUG_LOG
                    Debug.Log($"{_Tag}[FSN][Lifecycle] App paused while FSN Native is pausing gameplay. Will recover on resume. PauseDepth={_fsnNativePauseDepth}");
#endif
                }

                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            ForceHidePendingIosTransitionOverlayOnResume("OnApplicationPause(false)");
#endif
            RecoverFsnNativePauseAfterApplicationResume("OnApplicationPause(false)");
#endif
        }

        private void OnApplicationFocus(bool hasFocus)
        {
#if UNITY_ANDROID || UNITY_IOS
            if (!hasFocus) NotifyFullScreenAdDisplayed();
            if (hasFocus)
            {
#if UNITY_IOS && !UNITY_EDITOR
                ForceHidePendingIosTransitionOverlayOnResume("OnApplicationFocus(true)");
#endif
                RecoverFsnNativePauseAfterApplicationResume("OnApplicationFocus(true)");
            }
#endif
        }

        private void CheckAfterGetFromServer()
        {
            if (!_getConfigFromServer) return;

            try
            {
                _campaignID = PlayerPrefs.GetString(_CampaignID, "");
                var fsnConfig = ResolveConfig();
                var isEnableFilter = fsnConfig != null && fsnConfig.moMulEnableCampaignFilter;
                if (!isEnableFilter)
                {
                    _enableFsn = true;
                    CallPrepare();
                }
                else if (!string.IsNullOrEmpty(_campaignID))
                {
                    // Remote config chưa khai báo danh sách -> chuỗi null, Split sẽ NRE.
                    var moMulListCampaignIdsConfigs = fsnConfig.moMulListCampaignIdsConfigs ?? string.Empty;
                    var list = moMulListCampaignIdsConfigs.Split(';').ToList();

                    if (list.Contains(_campaignID))
                    {
                        // Kích hoạt chuẩn bị ad lần đầu tiên cho từng vị trí
                        _enableFsn = true;
                        CallPrepare();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private bool _isPrepareAll;

        private void CallPrepare()
        {
            if (_isPrepareAll)
            {
                return;
            }

            _isPrepareAll = true;
            PrepareNextAd(FsnAdType.Interstitial);
            PrepareNextAd(FsnAdType.Rewarded);
            PrepareNextAd(FsnAdType.Collapsible);
            PrepareNextAd(FsnAdType.Aoa);
            PrepareNextAd(FsnAdType.AdBreak);
        }

        public void ProcessConversionDataFromAppsflyer(string conversionData)
        {
            // Chạy trong callback conversion data của AppsFlyer: chuỗi rỗng/hỏng, hoặc value JSON null
            // ("campaign_id": null -> .ToString() NRE) đều không được ném ngược lên SDK.
            try
            {
                if (string.IsNullOrEmpty(conversionData)) return;

                Dictionary<string, object> conversionDataDictionary =
                    AppsFlyer.CallbackStringToDictionary(conversionData);
                if (conversionDataDictionary == null) return;

                // Gán dữ liệu attribution TRƯỚC: SetCampaignIdFromAppsflyer chạy tiếp cả luồng load FSN.
                if (TryGetConversionValue(conversionDataDictionary, "install_time", out var installTime))
                {
                    AppsflyerInstallTime = installTime;
                }

                if (TryGetConversionValue(conversionDataDictionary, "media_source", out var mediaSource))
                {
                    AppsflyerMediaSource = mediaSource;
                }

                if (TryGetConversionValue(conversionDataDictionary, "af_status", out var afStatus))
                {
                    AppsflyerAfStatus = afStatus;
                }

                if (TryGetConversionValue(conversionDataDictionary, _CampaignIdAppsflyer, out var campaignId))
                {
                    SetCampaignIdFromAppsflyer(campaignId);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static bool TryGetConversionValue(Dictionary<string, object> data, string key, out string value)
        {
            value = null;
            if (!data.TryGetValue(key, out var raw) || raw == null) return false;

            value = raw.ToString();
            return true;
        }

        public void SetCampaignIdFromAppsflyer(string campaignId)
        {
            _campaignID = campaignId;
            PlayerPrefs.SetString(_CampaignID, _campaignID);
            CheckAfterGetFromServer();
        }

        // Khoảng cách giữa các request floor liên tiếp của cùng một lượt.
        private const float _FLOOR_REQUEST_INTERVAL_SECONDS = 0.1f;

        /// <summary>
        /// BƯỚC 1 (moMulFsnAdsConfigsVer2): bắt đầu một LƯỢT mới = load ad DEFAULT (multiplier = 0, không
        /// set floor). Load xong mới biết revenue của default -> <see cref="LoadFloorSlotsCoroutine"/> xin
        /// các ad floor (revenue × multiplier), cách nhau <see cref="_FLOOR_REQUEST_INTERVAL_SECONDS"/>.
        ///
        /// Chỉ bắt đầu lượt mới khi slot default đang RỖNG: lúc khởi động, sau khi ad default được show
        /// (show ad floor thì default vẫn còn -> không làm gì), default load fail hoặc hết hạn.
        /// </summary>
        private void PrepareNextAd(FsnAdType adType)
        {
            if (_fsnAdsConfigsData == null || !_enableFsn) return;
            var config = _fsnAdsConfigsData.GetConfig(adType);
            if (config == null || !config.isEnable)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_Tag}[FSN] AdType {adType} bị tắt hoặc chưa có config.");
#endif
                return;
            }

            var entries = config.BuildLadder();
            if (entries.Count == 0 || !entries[0].IsDefault)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"{_Tag}[FSN] AdType {adType}: adIds/adMultipliers không có ad unit nào multiplier = 0 (default). Bỏ qua.");
#endif
                return;
            }

            if (!TryGetPreparedAd(adType, out var info)) return;

            // Không tạo request mới khi ad đang hiển thị.
            // Đặc biệt với Collapsible: close/hide callback sẽ là nơi bắt đầu load lượt kế tiếp.
            if (info.showingAdInstance != null)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{adType}] Bỏ qua PrepareNextAd vì vẫn còn ad đang hiển thị. {info.GetDebugSummary()}");
#endif
                return;
            }

            SyncLadderSlots(adType, info, entries);

            var defaultSlot = info.slots[0];

            if (defaultSlot.isLoading)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log($"{_Tag}[FSN][{adType}] Bỏ qua PrepareNextAd vì ad DEFAULT đang load. {DescribeLadder(info)}");
#endif
                return;
            }

            if (defaultSlot.ad != null && defaultSlot.ad.IsNativeAdReady())
            {
#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{adType}] Bỏ qua PrepareNextAd vì ad DEFAULT vẫn còn (chưa show). {DescribeLadder(info)}");
#endif
                return;
            }

            // Lượt mới: coroutine floor của lượt cũ (nếu còn đang rải request) phải dừng.
            int cycle = ++info.cycleSequence;
            StopFloorCoroutine(info);

#if ENABLE_DEBUG_LOG
            Debug.Log($"{_Tag}[FSN][{adType}] ===== Lượt #{cycle}: load ad DEFAULT (multiplier 0, không floor) =====");
#endif
            LoadLadderSlot(adType, info, config, defaultSlot, 0d, cycle);
        }

        /// <summary>
        /// Đồng bộ danh sách slot với config hiện tại (config có thể đổi giữa phiên). Slot cũ trùng
        /// adUnitId + multiplier được giữ nguyên (kèm ad đang có / đang load); slot không còn trong
        /// config bị bỏ — callback load muộn của nó tự bị từ chối.
        /// </summary>
        private void SyncLadderSlots(FsnAdType adType, PreparedAdInfo info, List<FsnLadderEntry> entries)
        {
            var oldSlots = new List<LadderSlot>(info.slots);
            var newSlots = new List<LadderSlot>(entries.Count);

            foreach (var entry in entries)
            {
                var reused = oldSlots.FirstOrDefault(s =>
                    s.adUnitId == entry.adUnitId && Mathf.Approximately(s.multiplier, entry.multiplier));

                if (reused != null)
                {
                    oldSlots.Remove(reused);
                    newSlots.Add(reused);
                }
                else
                {
                    newSlots.Add(new LadderSlot { adUnitId = entry.adUnitId, multiplier = entry.multiplier });
                }
            }

            if (oldSlots.Count > 0)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log($"{_Tag}[FSN][{adType}] Config đổi -> bỏ {oldSlots.Count} slot không còn trong config.");
#endif

                foreach (var dropped in oldSlots)
                {
                    DestroyFsnAd(dropped.ad);
                    dropped.ad = null;
                }
            }

            info.slots.Clear();
            info.slots.AddRange(newSlots);
        }

        /// <summary>
        /// Random style + vị trí OpenStore/Close cho MỘT slot (mỗi ad có layout riêng, được dùng lại đúng
        /// như vậy lúc show).
        /// </summary>
        private void RollSlotLayout(FsnAdType adType, FsnAdUnitConfig config, LadderSlot slot)
        {
            // 1. Random Style Index (0 -> 3) từ ratioStyle
            List<int> styleWeights = new List<int>
            {
                config.ratioTemplate.style0, config.ratioTemplate.style1, config.ratioTemplate.style2,
                config.ratioTemplate.style3
            };
            slot.styleIndex = GetRandomWeightedIndex(styleWeights);
            slot.layoutDebugName = GetStyleDebugName(slot.styleIndex);

            // 2. Random vị trí nút Open Store (UI) dựa trên openStoreRatio
            List<int> openStoreWeights = new List<int>
            {
                config.openStorePositionRatio.topRight, config.openStorePositionRatio.topLeft,
                config.openStorePositionRatio.bottomLeft,
                config.openStorePositionRatio.bottomRight
            };
            slot.openStorePosition = GetRandomWeightedIndex(openStoreWeights);

            // 3. Random style Template Ads (OpenStore / Skip / Countdown) dựa trên nativeStyleRatio.
            // Thay cho styleOpenStore cố định của bản cũ; phải chốt trước bước 4 vì check trùng góc phụ thuộc style.
            List<int> nativeStyleWeights = new List<int>
            {
                config.nativeStyleRatio.openStore, config.nativeStyleRatio.skip,
                config.nativeStyleRatio.countdown
            };
            FsnNativeAdType rolledNativeType = (FsnNativeAdType)GetRandomWeightedIndex(nativeStyleWeights);

            // 4. Random vị trí nút Close (UI) dựa trên closeRatioPosition.
            // Với Native type = 0 (OpenStore) và type = 1 (FakeClose, iv_skip nằm ở góc OpenStore),
            // nút Close không được trùng góc với OpenStore.
            // Vị trí OpenStore sẽ bị loại khỏi phép random, ba trọng số còn lại được giữ nguyên tỷ lệ.
            List<int> closeWeights = GetClosePositionWeights(config);

            bool shouldPreventOpenStoreCloseOverlap =
                adType != FsnAdType.Collapsible
                && (rolledNativeType == FsnNativeAdType.Native
                    || rolledNativeType == FsnNativeAdType.NativeFakeClose);

            slot.closePosition = shouldPreventOpenStoreCloseOverlap
                ? GetRandomWeightedIndexExcluding(closeWeights, slot.openStorePosition)
                : GetRandomWeightedIndex(closeWeights);

            slot.nativeType = adType == FsnAdType.Collapsible
                ? FsnNativeAdType.Collapsible
                : rolledNativeType;
            slot.nativeRatio = (FsnNativeAdRatio)config.ratioAds;
        }

        /// <summary>
        /// Hàm lõi: tạo FsnAd mới cho <paramref name="slot"/> và load với giá sàn
        /// <paramref name="floorUsd"/> (USD / impression, 0 = không set sàn).
        /// </summary>
        private void LoadLadderSlot(FsnAdType adType, PreparedAdInfo info, FsnAdUnitConfig config,
            LadderSlot slot, double floorUsd, int cycle)
        {
            if (string.IsNullOrWhiteSpace(slot.adUnitId))
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_Tag}[FSN] AdUnitID của {adType} bị rỗng.");
#endif
                return;
            }

            RollSlotLayout(adType, config, slot);

            string placementName = adType switch
            {
                FsnAdType.Interstitial => "fsn_interstitial",
                FsnAdType.Rewarded => "fsn_rewarded",
                FsnAdType.Collapsible => "fsn_collapsible",
                FsnAdType.Aoa => "fsn_aoa",
                FsnAdType.AdBreak => "fsn_adBreak",
                _ => UnknownPlacement(adType)
            };

            int loadSequence = ++slot.loadSequence;
            slot.isLoading = true;
            slot.revenue = 0d;
            slot.floorUsd = floorUsd;
            if (slot.IsDefault) info.isLoading = true;

            string adIdOfThisRequest = slot.adUnitId;

            try
            {
                // Ad cũ của slot (hết hạn / lượt trước còn đang load) không ai dùng nữa -> giải phóng.
                DestroyFsnAd(slot.ad);
                slot.ad = new FsnAd();
                slot.ad.InitNative(slot.nativeType, slot.nativeRatio, slot.adUnitId,
                    config.timeShowCloseButton, slot.styleIndex, config.delayTimeForCountdown);

                slot.ad.SetNativeListener(
                    onLoadingCompleted: (errorCode, errorMsg) =>
                        OnLadderSlotLoadCompleted(adType, slot, loadSequence, cycle, errorCode, errorMsg),
                    onLoadingStarted: () =>
                    {
#if ENABLE_DEBUG_LOG
                        Debug.Log(
                            $"{_Tag}[FSN][{adType}] Khởi động Load Native. ID={adIdOfThisRequest}, x{slot.multiplier:0.##}, floor=${floorUsd:F6}, LoadSeq={loadSequence}, Style={slot.styleIndex}({slot.layoutDebugName})");
#endif
                    },
                    onAdPaid: (src, id, val, cur) =>
                    {
                        // Lỗi log doanh thu không được chặn event paid của project (và ngược lại).
                        try
                        {
                            LogAll(fsnType: adType, adFormat: "native", adSource: src, adUnitId: id, valueMicros: val,
                                currencyCode: cur, placement: placementName);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }

                        SafeInvoke(() => OnNativeAdPaid?.Invoke(adType, src, id, val, cur));
                    }
                );

#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{adType}] Đang Load Native. {slot.GetDebugSummary()}, LoadSeq={loadSequence}, Cycle={cycle}");
#endif
                slot.ad.LoadNativeAd(floorUsd);
            }
            catch (Exception e)
            {
                // Bridge native ném -> sẽ không có callback load nào về. Không nhả isLoading thì mọi lượt
                // PrepareNextAd sau đều bị chặn ở "đang load" và format này chết tới hết phiên.
                Debug.LogException(e);

                if (slot.loadSequence == loadSequence)
                {
                    slot.isLoading = false;
                    DestroyFsnAd(slot.ad);
                    slot.ad = null;
                    if (slot.IsDefault) info.isLoading = false;
                }

                if (slot.IsDefault) OnNativeAdLoadFailedSafe(adType);
            }
        }

        /// <summary>
        /// XỬ LÝ CALLBACK KHI MỘT SLOT LOAD XONG (HOẶC LỖI).
        /// Default thành công -> xin các floor theo revenue vừa đọc. Floor fail -> bỏ, KHÔNG retry.
        /// </summary>
        private void OnLadderSlotLoadCompleted(FsnAdType adType, LadderSlot slot, int respondedLoadSequence,
            int cycle, int errorCode, string errorMessage)
        {
            if (!TryGetPreparedAd(adType, out var info)) return;

            // Callback của lượt load cũ, hoặc slot đã bị bỏ khỏi config -> bỏ qua.
            if (slot.loadSequence != respondedLoadSequence || !info.slots.Contains(slot))
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"{_Tag}[FSN][{adType}] Từ chối callback muộn. ID={slot.adUnitId}, CallbackLoadSeq={respondedLoadSequence}, CurrentLoadSeq={slot.loadSequence}");
#endif
                return;
            }

            slot.isLoading = false;
            if (slot.IsDefault) info.isLoading = false;

            // Chỉ errorCode == 0 mới là thành công.
            // Trên iOS có trường hợp NSError trả message rỗng; không được dùng
            // errorMessage rỗng để suy ra load thành công.
            if (errorCode == 0)
            {
                slot.revenue = slot.ad != null ? slot.ad.GetLoadedRevenue() : 0d;

#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{adType}] Tải quảng cáo THÀNH CÔNG. {slot.GetDebugSummary()}, LoadSeq={respondedLoadSequence}");
#endif
                OnNativeAdReadySafe(adType);

                if (slot.IsDefault) StartFloorLoads(adType, info, slot, cycle);
                return;
            }

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = "Native load failed without an error message";
            }

            DestroyFsnAd(slot.ad);
            slot.ad = null;
            slot.revenue = 0d;

#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][{adType}] Tải thất bại ID [{slot.adUnitId}] x{slot.multiplier:0.##} floor=${slot.floorUsd:F6}! LoadSeq={respondedLoadSequence}, Code: {errorCode} | Msg: {errorMessage}");
#endif

            // Default fail = lượt này không có giá cơ sở -> không xin floor. Lượt mới bắt đầu ở lần
            // PrepareNextAd kế tiếp (vd lần show tiếp theo không có ad).
            if (slot.IsDefault) OnNativeAdLoadFailedSafe(adType);
        }

        private void StartFloorLoads(FsnAdType adType, PreparedAdInfo info, LadderSlot defaultSlot, int cycle)
        {
            // Lượt đã bị thay bởi lượt mới hơn trong lúc default còn đang load.
            if (cycle != info.cycleSequence) return;

            if (info.slots.Count <= 1) return;

            if (defaultSlot.revenue <= 0d)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"{_Tag}[FSN][{adType}] Ad DEFAULT load xong nhưng KHÔNG đọc được revenue -> không có giá sàn, bỏ qua các ad floor lượt #{cycle}.");
#endif
                return;
            }

            StopFloorCoroutine(info);
            info.floorCoroutine = StartCoroutine(LoadFloorSlotsCoroutine(adType, info, defaultSlot.revenue, cycle));
        }

        /// <summary>
        /// Xin lần lượt mỗi slot floor một ad, floor = revenue default × multiplier của slot, các request
        /// cách nhau <see cref="_FLOOR_REQUEST_INTERVAL_SECONDS"/>. Slot đang load / đang còn ad chưa show
        /// thì bỏ qua (giữ ad cũ).
        /// </summary>
        private IEnumerator LoadFloorSlotsCoroutine(FsnAdType adType, PreparedAdInfo info, double baseRevenue,
            int cycle)
        {
            var floorSlots = info.slots.Where(s => !s.IsDefault).ToList();
            bool firstRequest = true;

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][{adType}] Lượt #{cycle}: revenue default=${baseRevenue:F6} -> xin {floorSlots.Count} ad floor.");
#endif

            foreach (var slot in floorSlots)
            {
                if (cycle != info.cycleSequence || !_enableFsn) break;

                if (slot.isLoading || (slot.ad != null && slot.ad.IsNativeAdReady()))
                {
#if ENABLE_DEBUG_LOG
                    Debug.Log(
                        $"{_Tag}[FSN][{adType}] Bỏ qua floor x{slot.multiplier:0.##} ({slot.adUnitId}): đang load hoặc còn ad chưa show.");
#endif
                    continue;
                }

                if (!firstRequest)
                {
                    yield return new WaitForSecondsRealtime(_FLOOR_REQUEST_INTERVAL_SECONDS);

                    if (cycle != info.cycleSequence || !_enableFsn) break;
                    if (!info.slots.Contains(slot)) continue;
                }

                firstRequest = false;

                // Đọc lại config ngay lúc bắn: có thể vừa đổi trong lúc chờ.
                var config = _fsnAdsConfigsData?.GetConfig(adType);
                if (config == null) break;

                try
                {
                    LoadLadderSlot(adType, info, config, slot, baseRevenue * slot.multiplier, cycle);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (cycle == info.cycleSequence) info.floorCoroutine = null;
        }

        private void StopFloorCoroutine(PreparedAdInfo info)
        {
            if (info.floorCoroutine == null) return;

            StopCoroutine(info.floorCoroutine);
            info.floorCoroutine = null;
        }

        /// <summary>Slot đang có ad sẵn sàng với revenue cao nhất; null nếu không có.</summary>
        private static LadderSlot FindBestReadySlot(PreparedAdInfo info)
        {
            LadderSlot best = null;

            foreach (var slot in info.slots)
            {
                if (slot.isLoading || slot.ad == null || !slot.ad.IsNativeAdReady()) continue;

                // Hoà revenue (vd cả hai đều không đọc được = 0) -> ưu tiên multiplier cao hơn, để ad
                // default được giữ lại lâu nhất (show default là bắt đầu lượt mới).
                if (best == null || slot.revenue > best.revenue ||
                    (slot.revenue == best.revenue && slot.multiplier > best.multiplier))
                {
                    best = slot;
                }
            }

            return best;
        }

        /// <summary>
        /// Chuyển ad đắt nhất sang <c>info.adInstance</c> (kèm layout của nó) để luồng show dùng. Ad bị rút
        /// khỏi slot -> slot đó rỗng. Rút ad default ra là điều kiện để lượt mới bắt đầu sau khi show.
        /// </summary>
        private void PromoteBestReadySlot(FsnAdType adType, PreparedAdInfo info)
        {
            // Đã có ad được rút ra mà chưa show (không nên xảy ra) -> dùng luôn, đừng làm rơi nó.
            if (info.adInstance != null && info.adInstance.IsNativeAdReady()) return;

            var best = FindBestReadySlot(info);
            if (best == null) return;

            // Ad đã rút ra trước đó nhưng hết hạn, sắp bị ghi đè -> giải phóng (trừ khi nó đang hiển thị).
            if (info.adInstance != null && !ReferenceEquals(info.adInstance, info.showingAdInstance))
            {
                DestroyFsnAd(info.adInstance);
            }

            info.adInstance = best.ad;
            info.currentAdUnitId = best.adUnitId;
            info.styleIndex = best.styleIndex;
            info.layoutDebugName = best.layoutDebugName;
            info.nativeType = best.nativeType;
            info.nativeRatio = best.nativeRatio;
            info.openStorePosition = best.openStorePosition;
            info.closePosition = best.closePosition;
            info.shownMultiplier = best.multiplier;
            info.shownRevenue = best.revenue;

            best.ad = null;

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][{adType}] Chọn ad đắt nhất để show: x{best.multiplier:0.##} revenue=${best.revenue:F6} ID={best.adUnitId}. Ladder: {DescribeLadder(info)}");
#endif
        }

        /// <summary>
        /// Giải phóng một FsnAd không còn dùng (native ad + object bridge). Không destroy thì NativeAd
        /// cùng ảnh/video của nó nằm lại trong bộ nhớ tới hết phiên, nhân lên theo thang floor -> OOM.
        /// </summary>
        private static void DestroyFsnAd(FsnAd ad)
        {
            if (ad == null) return;

            try
            {
                ad.Destroy();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static string DescribeLadder(PreparedAdInfo info)
        {
            if (info.slots.Count == 0) return "(rỗng)";
            return string.Join(" | ", info.slots.Select(s => s.GetDebugSummary()));
        }

        private string GetStyleDebugName(int styleIndex)
        {
            return styleIndex switch
            {
                0 => "Style1-FullMediaDark",
                1 => "Style2-TopMediaWhite",
                2 => "Style3-FullscreenVideoBottomInfo",
                3 => "Style4-CenteredPopup",
                _ => "Unknown-FallbackStyle3"
            };
        }

        private string GetPositionDebugName(int position)
        {
            return position switch
            {
                0 => "TopRight",
                1 => "TopLeft",
                2 => "BottomLeft",
                3 => "BottomRight",
                _ => "Unknown"
            };
        }

        // Thuật toán chọn ngẫu nhiên theo trọng số
        private int GetRandomWeightedIndex(List<int> weights)
        {
            int totalWeight = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                totalWeight += Mathf.Max(0, weights[i]);
            }

            if (totalWeight <= 0) return 0;

            int randomValue = UnityEngine.Random.Range(0, totalWeight);
            int currentSum = 0;

            for (int i = 0; i < weights.Count; i++)
            {
                currentSum += Mathf.Max(0, weights[i]);
                if (randomValue < currentSum)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// Chọn vị trí theo trọng số nhưng loại hoàn toàn một index khỏi tập random.
        /// Dùng cho type = 0/OpenStore để Close không thể xuất hiện cùng góc với OpenStore.
        /// Các index vị trí vẫn giữ nguyên: 0=TopRight, 1=TopLeft, 2=BottomLeft, 3=BottomRight.
        /// </summary>
        /// <summary>
        /// closePositionRatio theo đúng thứ tự index vị trí: 0=TopRight, 1=TopLeft, 2=BottomLeft, 3=BottomRight.
        /// </summary>
        private static List<int> GetClosePositionWeights(FsnAdUnitConfig config)
        {
            return new List<int>
            {
                config.closePositionRatio.topRight, config.closePositionRatio.topLeft,
                config.closePositionRatio.bottomLeft, config.closePositionRatio.bottomRight
            };
        }

        private int GetRandomWeightedIndexExcluding(List<int> weights, int excludedIndex)
        {
            if (weights == null || weights.Count == 0)
            {
                return 0;
            }

            if (excludedIndex < 0 || excludedIndex >= weights.Count)
            {
                return GetRandomWeightedIndex(weights);
            }

            int totalWeight = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (i == excludedIndex)
                {
                    continue;
                }

                totalWeight += Mathf.Max(0, weights[i]);
            }

            if (totalWeight > 0)
            {
                int randomValue = UnityEngine.Random.Range(0, totalWeight);
                int currentSum = 0;

                for (int i = 0; i < weights.Count; i++)
                {
                    if (i == excludedIndex)
                    {
                        continue;
                    }

                    currentSum += Mathf.Max(0, weights[i]);
                    if (randomValue < currentSum)
                    {
                        return i;
                    }
                }
            }

            // Server cấu hình cả ba trọng số còn lại bằng 0:
            // chọn vị trí hợp lệ đầu tiên để vẫn đảm bảo không trùng OpenStore.
            for (int i = 0; i < weights.Count; i++)
            {
                if (i != excludedIndex)
                {
                    return i;
                }
            }

            return 0;
        }

        public bool IsNativeReady(FsnAdType type)
        {
            if (!TryGetPreparedAd(type, out var info)) return false;

            bool isReady = (info.adInstance != null && info.adInstance.IsNativeAdReady())
                           || FindBestReadySlot(info) != null;
            info.isReady = isReady;
            return isReady;
        }

        // ==========================================
        // SHOW NATIVE & LÀM MỚI KHI HOÀN THÀNH
        // ==========================================

        public void ShowTrackedNativeWithFallback(FsnAdType type, Action onDone)
        {
            ShowTrackedNativeByType(type, onDone);
        }

        private void ShowTrackedNativeByType(FsnAdType adType, Action onDone)
        {
            _nativeShownAt[adType] = DateTime.Now;
#if UNITY_ANDROID || UNITY_IOS
            if (!TryGetPreparedAd(adType, out var info))
            {
                SafeInvoke(onDone);
                return;
            }

            if (info.showingAdInstance != null)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{adType}] Bỏ qua ShowTrackedNative vì ad đang hiển thị. {info.GetDebugSummary()}");
#endif
                SafeInvoke(onDone);
                return;
            }

            // Chọn ad có revenue cao nhất trong thang giá (default + floor) để show.
            PromoteBestReadySlot(adType, info);

            bool hasReadyAd = info.adInstance != null && info.adInstance.IsNativeAdReady();
            info.isReady = hasReadyAd;

            if (hasReadyAd)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{adType}] Sẵn sàng hiển thị Native Layout. {info.GetDebugSummary()}, OpenStorePosName={GetPositionDebugName(info.openStorePosition)}, ClosePosName={GetPositionDebugName(info.closePosition)}");
#endif

                Action nativeFinishedCallback = onDone;
                onDone = null;

                bool shouldPauseUnityGameplay = adType != FsnAdType.Collapsible;

                // Chốt instance tại thời điểm show.
                // Không dùng trực tiếp info.adInstance trong callback/hide,
                // vì info.adInstance có thể được clear để đánh dấu ad đã bị consume.
                FsnAd showingAdInstance = info.adInstance;
                info.showingAdInstance = showingAdInstance;
                info.isReady = false;
                info.isLoading = false;

                int showSequence = ++info.showSequence;
                bool completionHandled = false;

                info.lifecycleNativeFinishedCallback = adType != FsnAdType.Collapsible ? nativeFinishedCallback : null;
                info.lifecycleShowingSequence = showSequence;
                info.lifecycleForceCompleted = false;

                if (shouldPauseUnityGameplay)
                {
                    PauseUnityGameplayForFsnNative();
                }

                try
                {
#if ENABLE_DEBUG_LOG
                    Debug.Log(
                        $"{_Tag}[FSN][{adType}] Gọi ShowNativeAd với layout: {info.GetDebugSummary()}. Instance={showingAdInstance.GetDebugSummary()}");
#endif
                    ValidatePositionsBeforeShow(adType, info);

                    // Gửi kèm closePositionRatio để native (Android) chọn lại vị trí Close theo cùng quy tắc
                    // nếu phát hiện Close đè lên OpenStore/Skip trên màn hình thật.
                    FsnAdUnitConfig showConfig = _fsnAdsConfigsData?.GetConfig(adType);
                    int[] closePositionWeights = showConfig != null
                        ? GetClosePositionWeights(showConfig).ToArray()
                        : null;

                    showingAdInstance.ShowNativeAd(errorMessage =>
                    {
                        if (completionHandled)
                        {
#if ENABLE_DEBUG_LOG
                            Debug.Log($"{_Tag}[FSN][{adType}] Bỏ qua callback đóng bị lặp. Seq={showSequence}");
#endif
                            return;
                        }

                        if (info.showSequence != showSequence)
                        {
#if ENABLE_DEBUG_LOG
                            Debug.Log($"{_Tag}[FSN][{adType}] Bỏ qua callback đóng của lượt show cũ. CallbackSeq={showSequence}, CurrentSeq={info.showSequence}");
#endif
                            return;
                        }

                        if (info.lifecycleForceCompleted && info.lifecycleShowingSequence == showSequence)
                        {
                            // FALCON-PATCH(fsn-black-screen): chốt luôn cờ local để callback lặp lần sau
                            // thoát ngay ở check đầu tiên, không phụ thuộc state trong info (state đó có
                            // thể đã bị một lượt show mới ghi đè).
                            completionHandled = true;
#if ENABLE_DEBUG_LOG
                            Debug.Log($"{_Tag}[FSN][{adType}] Bỏ qua callback native muộn vì lifecycle recovery đã xử lý. Seq={showSequence}");
#endif
                            return;
                        }

                        completionHandled = true;
                        bool shouldNotifyHidden = false;

                        try
                        {
                            if (adType == FsnAdType.Collapsible)
                            {
                                // Callback của lượt show cũ thì bỏ qua hoàn toàn.
                                // Quan trọng: không Invoke OnNativeAdHidden trước đoạn check này.
                                if (info.showSequence != showSequence)
                                {
#if ENABLE_DEBUG_LOG
                                    Debug.Log(
                                        $"{_Tag}[FSN][Collapsible] Bỏ qua callback đóng của lượt show cũ. CallbackSeq={showSequence}, CurrentSeq={info.showSequence}");
#endif
                                    return;
                                }
                            }

                            if (string.IsNullOrEmpty(errorMessage))
                            {
#if ENABLE_DEBUG_LOG
                                Debug.Log($"{_Tag}[FSN][{adType}] Quảng cáo đã kết thúc và đóng thành công.");
#endif
                            }
                            else
                            {
#if ENABLE_DEBUG_LOG
                                Debug.LogWarning(
                                    $"{_Tag}[FSN][{adType}] Quảng cáo kết thúc với thông báo: {errorMessage}");
#endif

#if UNITY_IOS && !UNITY_EDITOR
                                ForceHideFsnOverlayNow(
                                    $"{adType}-native-show-error-{errorMessage}",
                                    cleanupIosNativeContainers: true
                                );
#endif
                            }

                            // Clear ad đang hiển thị trước khi load lượt mới.
                            if (ReferenceEquals(info.showingAdInstance, showingAdInstance))
                            {
                                info.showingAdInstance = null;
                            }

                            if (ReferenceEquals(info.adInstance, showingAdInstance))
                            {
                                info.adInstance = null;
                            }

                            info.isReady = false;
                            info.isLoading = false;

                            // Ad đã đóng -> instance này hết vòng đời (mỗi FsnAd chỉ một lượt show).
                            DestroyFsnAd(showingAdInstance);

#if ENABLE_DEBUG_LOG
                            Debug.Log($"{_Tag}[FSN][{adType}] Bắt đầu nạp ad kế tiếp sau khi ad đã đóng. Seq={showSequence}");
#endif
                            PrepareNextAd(adType);

                            shouldNotifyHidden = true;
                        }
                        finally
                        {
                            if (info.lifecycleShowingSequence == showSequence)
                            {
                                info.lifecycleNativeFinishedCallback = null;
                                info.lifecycleForceCompleted = false;
                                info.lifecycleShowingSequence = 0;
                            }

                            if (shouldPauseUnityGameplay)
                            {
                                ResumeUnityGameplayAfterFsnNative();
                            }

                            // Bọc riêng: subscriber của OnNativeAdHidden ném thì callback kết thúc flow
                            // (gỡ overlay đen, trả thưởng) VẪN phải chạy.
                            if (shouldNotifyHidden)
                            {
                                OnNativeAdHiddenSafe(adType);
                            }

                            if (adType != FsnAdType.Collapsible)
                            {
                                SafeInvoke(nativeFinishedCallback);
                            }
                        }
                    }, info.openStorePosition, info.closePosition, closePositionWeights);

                    if (adType != FsnAdType.Collapsible)
                    {
                        // Ẩn màn đen sau khi FSN đã hiện được vài frame + vài trăm ms,
                        // KHÔNG gọi onDone ở đây. onDone phải đợi user đóng Native Ad.
                        ScheduleHideOverlayAfterNativeShown(adType);

                        // FALCON-PATCH(fsn-black-screen): lưới an toàn cuối cùng nếu callback đóng
                        // không bao giờ về từ tầng native.
                        StartNativeShowCompletionWatchdog(adType, showSequence, showingAdInstance);
                    }
                    else
                    {
                        // Collapsible không khóa flow chính; báo xong ngay sau khi show.
                        // SafeInvoke: callback ném ở đây sẽ rơi xuống catch bên dưới và bị gọi LẦN HAI.
                        SafeInvoke(nativeFinishedCallback);

                        // Ad này đã được consume để hiển thị.
                        // Không preload gối đầu ở đây nữa; ad kế tiếp sẽ chỉ load trong callback đóng.
                        if (ReferenceEquals(info.adInstance, showingAdInstance))
                        {
                            info.adInstance = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);

#if UNITY_IOS && !UNITY_EDITOR
                    ForceHideFsnOverlayNow(
                        $"{adType}-show-exception",
                        cleanupIosNativeContainers: true
                    );
#endif

                    if (ReferenceEquals(info.showingAdInstance, showingAdInstance))
                    {
                        info.showingAdInstance = null;
                    }

                    if (ReferenceEquals(info.adInstance, showingAdInstance))
                    {
                        info.adInstance = null;
                    }

                    info.isReady = false;
                    info.isLoading = false;

                    // Native nếu vẫn đang hiện thì tự bỏ qua destroy, để luồng đóng của nó dọn.
                    DestroyFsnAd(showingAdInstance);

                    if (info.lifecycleShowingSequence == showSequence)
                    {
                        info.lifecycleNativeFinishedCallback = null;
                        info.lifecycleForceCompleted = false;
                        info.lifecycleShowingSequence = 0;
                    }

                    if (shouldPauseUnityGameplay)
                    {
                        ResumeUnityGameplayAfterFsnNative();
                    }

                    // Callback đóng của native có thể đã chạy xong trước khi exception xảy ra -> chỉ báo
                    // xong cho game nếu chưa báo, tránh game nhận hai lần onDone.
                    if (!completionHandled)
                    {
                        completionHandled = true;
                        SafeInvoke(nativeFinishedCallback);
                    }

                    SafeInvoke(() => PrepareNextAd(adType));
                }
            }
            else
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"{_Tag}[FSN][{adType}] Không có ad sẵn sàng để hiển thị. IsLoading={info.isLoading}, HasInstance={info.adInstance != null}. Không gọi Hide/Show xuống native.");
#endif

#if UNITY_IOS && !UNITY_EDITOR
                ForceHideFsnOverlayNow(
                    $"{adType}-show-no-ready",
                    cleanupIosNativeContainers: true
                );
#endif

                SafeInvoke(onDone);
                info.isReady = false;

                // Nếu đang load thì không được tạo request mới, vì sẽ đạp mất instance đang chờ callback.
                // Nếu chưa có request nào chạy thì mới bắt đầu load.
                if (!info.isLoading)
                {
                    PrepareNextAd(adType);
                }
                else
                {
#if ENABLE_DEBUG_LOG
                    Debug.Log($"{_Tag}[FSN][{adType}] Giữ nguyên request đang load, chờ OnNativeAdReady.");
#endif
                }
            }
#else
            onDone?.Invoke();
#endif
        }

        /// <summary>
        /// Cho phép chỉnh thời gian giữ overlay đen sau khi đã gọi ShowNativeAd,
        /// dùng khi cần tune riêng theo máy/độ trễ render của FSN.
        /// </summary>
        public void SetNativeShownOverlayHold(int minFrames, float seconds)
        {
            _nativeShownOverlayHoldMinFrames = Mathf.Max(0, minFrames);
            _nativeShownOverlayHoldSeconds = Mathf.Max(0f, seconds);

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][Overlay] Cập nhật thời gian giữ overlay đen. " +
                $"MinFrames={_nativeShownOverlayHoldMinFrames}, Seconds={_nativeShownOverlayHoldSeconds}");
#endif
        }

        /// <summary>
        /// Hẹn giờ tắt overlay đen SAU KHI FSN đã thực sự hiện lên được một lúc.
        /// Không được tắt ngay vài frame sau ShowNativeAd, vì native view nằm ở tầng OS
        /// và thường chưa kịp phủ kín màn hình -> user sẽ thấy màn hình game.
        /// </summary>
        private void ScheduleHideOverlayAfterNativeShown(FsnAdType adType)
        {
            StopScheduledOverlayHide($"reschedule-{adType}");

            _hideOverlayAfterNativeShownCoroutine =
                StartCoroutine(HideOverlayAfterNativeShownCoroutine(adType));

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][{adType}][Overlay] Hẹn tắt overlay đen sau khi FSN hiện. " +
                $"MinFrames={_nativeShownOverlayHoldMinFrames}, HoldSeconds={_nativeShownOverlayHoldSeconds}");
#endif
        }

        private void StopScheduledOverlayHide(string reason)
        {
            if (_hideOverlayAfterNativeShownCoroutine == null)
            {
                return;
            }

            StopCoroutine(_hideOverlayAfterNativeShownCoroutine);
            _hideOverlayAfterNativeShownCoroutine = null;

#if ENABLE_DEBUG_LOG
            Debug.Log($"{_Tag}[FSN][Overlay] Huỷ lịch tắt overlay đen. Reason={reason}");
#endif
        }

        private IEnumerator HideOverlayAfterNativeShownCoroutine(FsnAdType adType)
        {
            // 1. Chờ tối thiểu vài frame để Unity submit xong frame hiện tại và
            //    native view có cơ hội được attach vào view hierarchy của OS.
            for (int i = 0; i < _nativeShownOverlayHoldMinFrames; i++)
            {
#if UNITY_IOS && !UNITY_EDITOR
                // iOS có thể chưa phát frame ngay sau khi MAX dismiss,
                // WaitForEndOfFrame ở đây dễ bị treo lâu hơn mong đợi.
                yield return null;
#else
                yield return new WaitForEndOfFrame();
#endif
            }

            // 2. Giữ thêm màn đen bằng realtime cho tới khi FSN chắc chắn đã render xong.
            //    Dùng realtime vì Time.timeScale có thể đang bằng 0 trong lúc show ad.
            float holdSeconds = Mathf.Max(0f, _nativeShownOverlayHoldSeconds);
            if (holdSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(holdSeconds);
            }

            // Clear handle TRƯỚC khi Hide, vì ForceHideFsnOverlayNow cũng gọi
            // StopScheduledOverlayHide - không được để nó tự stop chính coroutine này.
            _hideOverlayAfterNativeShownCoroutine = null;

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][{adType}][Overlay] Tắt overlay đen sau khi FSN đã hiện. " +
                $"MinFrames={_nativeShownOverlayHoldMinFrames}, HoldSeconds={holdSeconds}");
#endif

            ForceHideFsnOverlayNow(
                "native-view-added",
                cleanupIosNativeContainers: false
            );

#if UNITY_IOS && !UNITY_EDITOR
            CancelIosNativeTransitionOverlayFailsafe("native-view-added");
#endif
        }

        // >>>>> FALCON-PATCH(fsn-black-screen) -- DO NOT REMOVE on module update <<<<<
        // Tầng native có thể dựng được container/activity nhưng KHÔNG bao giờ bắn callback đóng về C#
        // (dialog không attach được, activity bị chồng...). Khi đó:
        //  - native view nằm đè lên Unity đang bị pause -> user thấy màn hình đen không đóng được;
        //  - showingAdInstance != null vĩnh viễn -> PrepareNextAd bị chặn, format đó chết hẳn;
        //  - onDone không bao giờ chạy -> luồng rewarded/interstitial của game treo luôn.
        // Không có tín hiệu nào từ native để phân biệt "đang kẹt" với "user đang xem thật", nên watchdog
        // dùng timeout dài + ưu tiên đường dismiss chính thống (HideNativeAd) trước khi tự dọn state.
        // False positive (user xem/click-out quá lâu) chỉ dẫn tới việc ad bị đóng hộ — impression và
        // doanh thu đã được tính từ lúc show nên không mất tiền.
        private const float _NATIVE_SHOW_COMPLETION_TIMEOUT_SECONDS = 60f;
        private const float _NATIVE_SHOW_HIDE_GRACE_SECONDS = 2f;

        private float _nativeShowCompletionTimeoutSeconds = _NATIVE_SHOW_COMPLETION_TIMEOUT_SECONDS;

        /// <summary>
        /// Chỉnh thời gian chờ callback đóng của Native trước khi watchdog can thiệp.
        /// Đặt &lt;= 0 để tắt hẳn watchdog.
        /// </summary>
        public void SetNativeShowCompletionTimeout(float seconds)
        {
            _nativeShowCompletionTimeoutSeconds = seconds;

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][Watchdog] Cập nhật timeout chờ callback đóng Native. Seconds={seconds}");
#endif
        }

        private float _fullScreenAnswerTimeoutSeconds = 8f;
        private static int _fullScreenSignalCount;
        private static bool _maxDisplayedHooked;

        public void SetFullScreenAnswerTimeout(float seconds)
        {
            _fullScreenAnswerTimeoutSeconds = seconds;
        }

        public static void NotifyFullScreenAdDisplayed()
        {
            _fullScreenSignalCount++;
        }

        private static void HookMaxDisplayedOnce()
        {
            if (_maxDisplayedHooked) return;
            _maxDisplayedHooked = true;
#if MAX_ENABLE
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += (_, _) => NotifyFullScreenAdDisplayed();
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += (_, _) => NotifyFullScreenAdDisplayed();
#endif
        }

        private void StartFullScreenAnswerWatchdog(string flowName, Func<bool> hasAnswer, Action onSilent)
        {
            if (_fullScreenAnswerTimeoutSeconds <= 0f) return;
            HookMaxDisplayedOnce();
            StartCoroutine(FullScreenAnswerWatchdog(flowName, _fullScreenSignalCount, hasAnswer, onSilent));
        }

        private IEnumerator FullScreenAnswerWatchdog(string flowName, int signalAtStart, Func<bool> hasAnswer,
            Action onSilent)
        {
            var deadline = Time.realtimeSinceStartup + _fullScreenAnswerTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (hasAnswer() || _fullScreenSignalCount != signalAtStart) yield break;
                yield return null;
            }

            if (hasAnswer() || _fullScreenSignalCount != signalAtStart) yield break;
            Debug.LogError(
                $"{_Tag}[FSN][{flowName}] Ad toàn màn không trả callback nào sau {_fullScreenAnswerTimeoutSeconds}s: tắt overlay, trả fail về game");
            try
            {
                onSilent();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ForceHideFsnOverlayNow($"{flowName}-silent-watchdog", true);
            }
        }

        private void StartNativeShowCompletionWatchdog(FsnAdType adType, int showSequence, FsnAd instance)
        {
            if (_nativeShowCompletionTimeoutSeconds <= 0f)
            {
                return;
            }

            StartCoroutine(NativeShowCompletionWatchdogCoroutine(adType, showSequence, instance));
        }

        /// <summary>
        /// Lượt show này vẫn đang chờ callback đóng? Sai nếu đã đóng bình thường, đã bị lifecycle
        /// recovery dọn, hoặc đã có lượt show mới đè lên.
        /// </summary>
        private bool IsNativeShowStillPending(FsnAdType adType, int showSequence, FsnAd instance)
        {
            if (!_preparedAds.TryGetValue(adType, out var info))
            {
                return false;
            }

            return info.showSequence == showSequence &&
                   ReferenceEquals(info.showingAdInstance, instance);
        }

        private IEnumerator NativeShowCompletionWatchdogCoroutine(
            FsnAdType adType, int showSequence, FsnAd instance)
        {
            yield return new WaitForSecondsRealtime(_nativeShowCompletionTimeoutSeconds);

            if (!IsNativeShowStillPending(adType, showSequence, instance))
            {
                yield break;
            }

#if ENABLE_DEBUG_LOG
            Debug.LogError(
                $"{_Tag}[FSN][{adType}][Watchdog] Quá {_nativeShowCompletionTimeoutSeconds}s chưa nhận " +
                $"callback đóng của lượt show Seq={showSequence}. Thử HideNativeAd để gỡ native đang kẹt. " +
                $"Target={instance?.GetDebugSummary()}");
#endif

            try
            {
                HideTrackedNative(adType);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            yield return new WaitForSecondsRealtime(_NATIVE_SHOW_HIDE_GRACE_SECONDS);

            if (!IsNativeShowStillPending(adType, showSequence, instance))
            {
                yield break;
            }

#if ENABLE_DEBUG_LOG
            Debug.LogError(
                $"{_Tag}[FSN][{adType}][Watchdog] HideNativeAd vẫn không gỡ được (không có callback đóng). " +
                $"Tự giải phóng state C# và hoàn tất flow để game không kẹt. Seq={showSequence}");
#endif

            try
            {
                ForceCompleteStuckNativeShow(adType, showSequence, instance);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Dọn đúng MỘT lượt show bị treo: trả state về rỗng, gỡ overlay, nạp lượt kế tiếp và hoàn tất
        /// callback cho game. Giống <see cref="ForceCompleteLostNativeStateAfterResume"/> nhưng chỉ tác
        /// động vào đúng adType/sequence đang kẹt.
        /// </summary>
        private void ForceCompleteStuckNativeShow(FsnAdType adType, int showSequence, FsnAd instance)
        {
            if (!_preparedAds.TryGetValue(adType, out var info))
            {
                return;
            }

            Action callback = info.lifecycleShowingSequence == showSequence
                ? info.lifecycleNativeFinishedCallback
                : null;

            if (ReferenceEquals(info.showingAdInstance, instance))
            {
                info.showingAdInstance = null;
            }

            if (ReferenceEquals(info.adInstance, instance))
            {
                info.adInstance = null;
            }

            info.isReady = false;
            info.isLoading = false;

            // Giữ nguyên sequence + đánh dấu force-complete để callback native muộn (nếu sau này có về)
            // bị guard trong ShowTrackedNativeByType từ chối, không chạy nhánh đóng lần thứ hai.
            info.lifecycleNativeFinishedCallback = null;
            info.lifecycleForceCompleted = true;
            info.lifecycleShowingSequence = showSequence;

            ResumeUnityGameplayAfterFsnNative();

            ForceHideFsnOverlayNow(
                $"watchdog-stuck-{adType}",
                cleanupIosNativeContainers: true
            );

            SafeInvoke(() => PrepareNextAd(adType));
            OnNativeAdHiddenSafe(adType);
            SafeInvoke(callback);
        }
        // >>>>> END FALCON-PATCH(fsn-black-screen) <<<<<

        private void ShowFsnOverlaySafely(string reason)
        {
            try
            {
                FsnAdOverlay.Instance.Show();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void ForceHideFsnOverlayNow(
            string reason,
            bool cleanupIosNativeContainers)
        {
            // Mọi lệnh ẩn tức thời (native đóng, lỗi show, lifecycle recovery...) đều
            // phải huỷ lịch ẩn đang chờ, tránh coroutine cũ ẩn nhầm overlay của lượt sau.
            StopScheduledOverlayHide(reason);

#if UNITY_IOS && !UNITY_EDITOR
            if (cleanupIosNativeContainers)
            {
                try
                {
                    FsnAdIOS_ForceDismissVisibleNativeContainers();
                }
                catch (Exception e)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogWarning(
                        $"{_Tag}[FSN][iOS NativeCleanup] Failed. " +
                        $"Reason={reason}, Error={e.Message}");
#endif
                }
            }
#endif

            try
            {
                FsnAdOverlay.Instance.Hide();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void HideFsnOverlaySafely(string reason)
        {
            ForceHideFsnOverlayNow(
                reason,
                cleanupIosNativeContainers: false
            );
        }

        private void BeginIosNativeTransitionOverlayFailsafe(string reason)
        {
#if UNITY_IOS && !UNITY_EDITOR
            _iosNativeTransitionOverlayToken++;
            int token = _iosNativeTransitionOverlayToken;

            if (_iosNativeTransitionOverlayFailsafeCoroutine != null)
            {
                StopCoroutine(_iosNativeTransitionOverlayFailsafeCoroutine);
                _iosNativeTransitionOverlayFailsafeCoroutine = null;
            }

            _iosNativeTransitionOverlayPending = true;
            _iosNativeTransitionOverlayFailsafeCoroutine =
                StartCoroutine(IosNativeTransitionOverlayFailsafeCoroutine(token, reason));

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][iOS OverlayFailsafe] Armed. Token={token}, " +
                $"Timeout={IosNativeTransitionOverlayTimeoutSeconds}s, Reason={reason}");
#endif
#endif
        }

        private IEnumerator IosNativeTransitionOverlayFailsafeCoroutine(int token, string reason)
        {
            yield return new WaitForSecondsRealtime(
                IosNativeTransitionOverlayTimeoutSeconds
            );

#if UNITY_IOS && !UNITY_EDITOR
            if (!_iosNativeTransitionOverlayPending ||
                token != _iosNativeTransitionOverlayToken)
            {
                yield break;
            }

            _iosNativeTransitionOverlayPending = false;
            _iosNativeTransitionOverlayFailsafeCoroutine = null;

#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][iOS OverlayFailsafe] Timeout reached. " +
                $"Force hide overlay. Token={token}, Reason={reason}");
#endif

            ForceHideFsnOverlayNow(
                $"ios-timeout-{reason}",
                cleanupIosNativeContainers: false
            );
#endif
        }

        private void CancelIosNativeTransitionOverlayFailsafe(string reason)
        {
#if UNITY_IOS && !UNITY_EDITOR
            _iosNativeTransitionOverlayPending = false;
            _iosNativeTransitionOverlayToken++;

            if (_iosNativeTransitionOverlayFailsafeCoroutine != null)
            {
                StopCoroutine(_iosNativeTransitionOverlayFailsafeCoroutine);
                _iosNativeTransitionOverlayFailsafeCoroutine = null;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][iOS OverlayFailsafe] Cancelled. Reason={reason}");
#endif
#endif
        }

        private void ForceHidePendingIosTransitionOverlayOnResume(string reason)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_iosNativeTransitionOverlayPending)
            {
                return;
            }

            _iosNativeTransitionOverlayPending = false;
            _iosNativeTransitionOverlayToken++;

            if (_iosNativeTransitionOverlayFailsafeCoroutine != null)
            {
                StopCoroutine(_iosNativeTransitionOverlayFailsafeCoroutine);
                _iosNativeTransitionOverlayFailsafeCoroutine = null;
            }

#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][iOS OverlayFailsafe] App resumed while native " +
                $"transition overlay was pending. Force hide. Reason={reason}");
#endif

            ForceHideFsnOverlayNow(
                $"ios-resume-{reason}",
                cleanupIosNativeContainers: false
            );
#endif
        }

        private void PauseUnityGameplayForFsnNative()
        {
            if (_fsnNativePauseDepth == 0)
            {
                //Time.timeScale = 0f;
                //AudioListener.pause = true;

#if ENABLE_DEBUG_LOG
                Debug.Log($"{_Tag}[FSN] Unity gameplay paused for FSN Native Ad.");
#endif
            }

            _fsnNativePauseDepth++;
        }

        private void ResumeUnityGameplayAfterFsnNative()
        {
            if (_fsnNativePauseDepth <= 0)
            {
                return;
            }

            _fsnNativePauseDepth--;

            if (_fsnNativePauseDepth == 0)
            {
                _fsnNativePausedDuringApplicationBackground = false;

#if ENABLE_DEBUG_LOG
                Debug.Log($"{_Tag}[FSN] Unity gameplay resumed after FSN Native Ad.");
#endif
            }
        }

        private void RecoverFsnNativePauseAfterApplicationResume(string reason)
        {
            if (!_fsnNativePausedDuringApplicationBackground && _fsnNativePauseDepth <= 0)
            {
                return;
            }

            if (_fsnNativeResumeRecoveryCoroutine != null)
            {
                StopCoroutine(_fsnNativeResumeRecoveryCoroutine);
            }

            _fsnNativeResumeRecoveryCoroutine = StartCoroutine(RecoverFsnNativePauseAfterApplicationResumeCoroutine(reason));
        }

        private IEnumerator RecoverFsnNativePauseAfterApplicationResumeCoroutine(string reason)
        {
            // Chờ vài frame để native iOS/Android có cơ hội gửi callback close/hide nếu có.
            // Dùng yield null/EndOfFrame thay vì WaitForSeconds vì Time.timeScale có thể đang bằng 0.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            if (!_fsnNativePausedDuringApplicationBackground && _fsnNativePauseDepth <= 0)
            {
                _fsnNativeResumeRecoveryCoroutine = null;
                yield break;
            }

#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][Lifecycle] Resume while FSN Native may still be showing. Reason={reason}, PauseDepth={_fsnNativePauseDepth}, TimeScale={Time.timeScale}");
#endif

            if (HasShowingNonCollapsibleNativeAd())
            {
                KeepUnityGameplayPausedForShowingFsnNative(reason);

                // >>>>> FALCON-PATCH(fsn-resume-double-complete) -- DO NOT REMOVE on module update <<<<<
                // Resume/lấy lại focus KHÔNG có nghĩa native đã mất: kéo thanh thông báo, cuộc gọi,
                // click-out sang store rồi quay lại... native vẫn đang hiện và user chưa bấm đóng.
                // Trước đây sau 0.5s code coi native là mất -> gọi onDone (trả thưởng / EndAdFlow) khi
                // native còn trên màn hình, rồi callback đóng thật về sau lại chạy nhánh đóng lần 2.
                // Giờ giao cho watchdog (StartNativeShowCompletionWatchdog) xử lý native kẹt thật.
                if (_nativeShowCompletionTimeoutSeconds > 0f)
                {
#if ENABLE_DEBUG_LOG
                    Debug.Log(
                        $"{_Tag}[FSN][Lifecycle] Native still showing after resume. Wait for close callback, watchdog will handle if stuck. Reason={reason}");
#endif
                }
                else
                {
                    // Watchdog bị tắt -> không có lưới an toàn nào khác, giữ fallback cũ.
                    // Chờ thêm một chút bằng realtime, vì Time.timeScale có thể đang = 0.
                    yield return new WaitForSecondsRealtime(0.5f);

                    if (HasShowingNonCollapsibleNativeAd())
                    {
#if ENABLE_DEBUG_LOG
                        Debug.LogWarning(
                            $"{_Tag}[FSN][Lifecycle] Native still marked as showing after resume, no native callback and watchdog disabled. Force clear C# state and resume. Reason={reason}");
#endif

                        ForceCompleteLostNativeStateAfterResume(reason);
                    }
                }

                _fsnNativePausedDuringApplicationBackground = false;
                _fsnNativeResumeRecoveryCoroutine = null;
                yield break;
            }

            // Không còn native nào đang show mà pause flag vẫn kẹt.
            // Lúc này mới force resume để tránh game bị đứng vĩnh viễn.
#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][Lifecycle] No showing FSN Native found after resume. Force resume gameplay only. Reason={reason}");
#endif

            ForceResumeUnityGameplayAfterFsnNative(reason);

            try
            {
                ForceHideFsnOverlayNow(
                    $"lifecycle-{reason}",
                    cleanupIosNativeContainers: true
                );
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_Tag}[FSN][Lifecycle] Hide overlay failed during force resume: {e.Message}");
#endif
            }

            _fsnNativePausedDuringApplicationBackground = false;
            _fsnNativeResumeRecoveryCoroutine = null;
        }

        private void ValidatePositionsBeforeShow(FsnAdType adType, PreparedAdInfo info)
        {
            if (adType == FsnAdType.Collapsible ||
                (info.nativeType != FsnNativeAdType.Native && info.nativeType != FsnNativeAdType.NativeFakeClose) ||
                info.openStorePosition != info.closePosition)
            {
                return;
            }

            FsnAdUnitConfig config = _fsnAdsConfigsData?.GetConfig(adType);

            if (config == null)
            {
                // Không có config: đổi trái <-> phải trên cùng cạnh (0<->1, 2<->3), không nhảy chéo.
                info.closePosition = info.openStorePosition ^ 1;
                return;
            }

            var closeWeights = GetClosePositionWeights(config);

            int oldClosePosition = info.closePosition;

            info.closePosition = GetRandomWeightedIndexExcluding(
                closeWeights,
                info.openStorePosition
            );

#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][{adType}] Close position overlapped OpenStore before Show. " +
                $"OpenStore={info.openStorePosition}" +
                $"({GetPositionDebugName(info.openStorePosition)}), " +
                $"OldClose={oldClosePosition}" +
                $"({GetPositionDebugName(oldClosePosition)}), " +
                $"NewClose={info.closePosition}" +
                $"({GetPositionDebugName(info.closePosition)})"
            );
#endif
        }

        private bool HasShowingNonCollapsibleNativeAd()
        {
            foreach (var pair in _preparedAds)
            {
                FsnAdType adType = pair.Key;

                if (adType == FsnAdType.Collapsible)
                {
                    continue;
                }

                PreparedAdInfo info = pair.Value;
                if (info.showingAdInstance != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void KeepUnityGameplayPausedForShowingFsnNative(string reason)
        {
            if (_fsnNativePauseDepth <= 0)
            {
                _fsnNativePauseDepth = 1;
            }

            //Time.timeScale = 0f;
            //AudioListener.pause = true;

#if ENABLE_DEBUG_LOG
            Debug.LogWarning(
                $"{_Tag}[FSN][Lifecycle] Keep Unity gameplay paused because FSN Native is still showing. Reason={reason}, PauseDepth={_fsnNativePauseDepth}, TimeScale={Time.timeScale}");
#endif
        }

        private void ForceResumeUnityGameplayAfterFsnNative(string reason)
        {
            bool wasPausedByFsnNative = _fsnNativePauseDepth > 0 || Mathf.Approximately(Time.timeScale, 0f);

            if (!wasPausedByFsnNative)
            {
                return;
            }

            _fsnNativePauseDepth = 0;

#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"{_Tag}[FSN][Lifecycle] Force resume Unity gameplay after FSN Native. Reason={reason}, RestoredTimeScale={Time.timeScale}, RestoredAudioPause={AudioListener.pause}");
#endif
        }

        public void HideTrackedNative(FsnAdType type)
        {
            if (!_preparedAds.TryGetValue(type, out var info))
            {
                return;
            }

            // Chỉ hide ad đang thật sự hiển thị.
            // Không được hide info.adInstance khi nó chỉ đang load/ready.
            // Đây là fix chính cho case:
            // show lúc chưa ready -> không có showingAdInstance -> hide phải bỏ qua,
            // nếu không native state của request đang load có thể bị clear và làm kẹt collapsible.
            FsnAd targetAd = info.showingAdInstance;

            if (targetAd == null)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][{type}] HideTrackedNative ignored vì không có ad đang hiển thị. {info.GetDebugSummary()}");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log(
                $"{_Tag}[FSN][{type}] HideTrackedNative target đang hiển thị. Prepared={info.GetDebugSummary()}, Target={targetAd.GetDebugSummary()}");
#endif

            try
            {
                targetAd.HideNativeAd();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ==========================================
        // LOG REVENUE & FLOWS
        // ==========================================
        private void LogAll(FsnAdType fsnType, string adFormat, string adSource, string adUnitId, long valueMicros,
            string currencyCode, string placement = "")
        {
            // Tách riêng: Firebase lỗi không được làm mất log doanh thu AppsFlyer / bigdata.
            try
            {
                FalconFirebaseLog.LogFsn(adFormat, adSource, adUnitId, valueMicros, currencyCode, placement);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                SendCsAdInfo(fsnType, adSource, valueMicros, currencyCode, placement);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                FalconMmpLog.LogFsn(adFormat, adSource, adUnitId, valueMicros, currencyCode, placement);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                LogToBigData(fsnType, adSource, adUnitId, valueMicros, currencyCode);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ==========================================
        // LOG BIGDATA — cùng cửa FalconBigDataController.Ad.OnImpression với AdmobMediationFalconBridge
        // ==========================================
        private const string _BIGDATA_MEDIATION = "Admob"; // khớp AdmobMediationFalconBridge._MEDIATION

        // Impression do FSN bắn — FSN gửi adMediation = "Admob" giống meta-mediation nên module ngoài nghe
        // pipeline BigData không phân biệt được qua payload. Bảng yếu: AdParam được GC thu thì entry tự mất.
        private static readonly ConditionalWeakTable<AdParam, object> _fsnImpressions = new();
        private static readonly object _fsnImpressionMarker = new();

        /// <summary>
        /// <paramref name="param"/> có phải impression do FSN bắn không — để module ngoài (nghe hook của
        /// BigData) tự lọc. Mediation không phụ thuộc vào bên hỏi.
        /// </summary>
        public static bool IsFsnImpression(AdParam param)
        {
            return param != null && _fsnImpressions.TryGetValue(param, out _);
        }

        private const string _USD = "USD";

        private readonly Dictionary<FsnAdType, DateTime> _nativeShownAt = new Dictionary<FsnAdType, DateTime>();

        private static bool IsUsdOrUnknown(string currencyCode)
        {
            return string.IsNullOrEmpty(currencyCode) ||
                   string.Equals(currencyCode, _USD, StringComparison.OrdinalIgnoreCase);
        }

        private void SendCsAdInfo(FsnAdType fsnType, string adSource, long valueMicros, string currencyCode,
            string placement)
        {
            if (!IsUsdOrUnknown(currencyCode))
            {
                Debug.LogError($"{_Tag}[FSN][{fsnType}] Native paid bằng {currencyCode}, không phải USD: bỏ CSAdInfo");
                return;
            }

            var time = _nativeShownAt.TryGetValue(fsnType, out var shownAt)
                ? (int)(DateTime.Now - shownAt).TotalSeconds
                : 0;
            new CSAdInfo(Guid.NewGuid().ToString(), placement, AdType.Native.ToString(),
                string.IsNullOrEmpty(adSource) ? "AdMob" : adSource, _BIGDATA_MEDIATION, time,
                valueMicros / 1_000_000.0).Send();
        }

        private readonly Dictionary<FsnAdType, string> _nativeWhere = new Dictionary<FsnAdType, string>();

        private void RememberNativeWhere(FsnAdType type, string where)
        {
            _nativeWhere[type] = string.IsNullOrWhiteSpace(where) ? null : where;
        }

        private string NativeWhere(FsnAdType type)
        {
            return _nativeWhere.TryGetValue(type, out var where) ? where : null;
        }

        private static string UnknownPlacement(FsnAdType type)
        {
            Debug.LogError($"{_Tag}[FSN] FsnAdType {type} chưa có tên placement: dùng fsn_unknown");
            return "fsn_unknown";
        }

        private static string NativeWhen(FsnAdType type)
        {
            return type switch
            {
                FsnAdType.Interstitial => "after_interstitial",
                FsnAdType.Rewarded => "after_rewarded",
                FsnAdType.Aoa => "app_open",
                FsnAdType.AdBreak => "ad_break",
                FsnAdType.Collapsible => "collapsible",
                _ => null
            };
        }

        private void LogToBigData(FsnAdType fsnType, string adSource, string adUnitId, long valueMicros,
            string currencyCode)
        {
            if (!IsUsdOrUnknown(currencyCode))
            {
                Debug.LogError($"{_Tag}[FSN][{fsnType}] Native paid bằng {currencyCode}, không phải USD: bỏ dòng BigData");
                return;
            }

            var revenue = valueMicros / 1_000_000.0;
            AddClientAdInfo(AdType.Native, revenue);

            var param = new AdParam
            {
                type = AdType.Native,
                adMediation = _BIGDATA_MEDIATION,
                adNetwork = string.IsNullOrEmpty(adSource) ? "AdMob" : adSource,
                adUnitId = adUnitId,
                adFormatRaw = fsnType.ToString(),
                adRev = revenue,
                currentLevel = AdmobMediationHooks.Level()
            };
            var where = NativeWhere(fsnType);
            if (where != null) param.adWhere = where;
            var when = NativeWhen(fsnType);
            if (when != null) param.adWhen = when;

            _fsnImpressions.Add(param, _fsnImpressionMarker);
            FalconBigDataController.Ad.OnImpression(param);
        }

        private static void AddClientAdInfo(AdType type, double revenue)
        {
            var data = GameData4AdInfo.Instance;
            data.adTypeToInfo ??= new Dictionary<AdType, AdInfo>();
            if (data.adTypeToInfo.TryGetValue(type, out var adInfo))
            {
                adInfo.ltv += revenue;
                adInfo.count++;
            }
            else
            {
                data.adTypeToInfo.Add(type, new AdInfo { count = 1, ltv = revenue });
            }

            data.UpdateToServer();
        }

        public void ShowRewardedThenNative(FsnShowRewardedDelegate showRewarded, Action onSuccess,
            Action onFailed = null, string where = null)
        {
            RememberNativeWhere(FsnAdType.Rewarded, where);
            // Overlay đen phải được bật trước MAX để che kín toàn bộ thời điểm chuyển giao
            // từ MAX Rewarded sang Native Ad. Nếu không có Native ready sau khi MAX đóng,
            // overlay sẽ được tắt ngay lập tức trong callback showNative.
            bool isRewardedReceived = false;
            bool blackOverlayShown = false;

            void ShowBlackOverlayBeforeMax()
            {
                if (blackOverlayShown)
                {
                    return;
                }

#if UNITY_IOS && !UNITY_EDITOR
                CancelIosNativeTransitionOverlayFailsafe("rewarded-max-start");
#endif
                blackOverlayShown = true;
                ShowFsnOverlaySafely("rewarded-before-max");

#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][Rewarded] Hiện overlay đen trước khi show MAX để che toàn bộ chuyển cảnh MAX -> Native.");
#endif
            }

            void HideBlackOverlay(
                string reason,
                bool cleanupIosNativeContainers = false)
            {
                // Luôn gọi Hide như lớp an toàn. Nếu overlay đã được ẩn bởi DelayHideOverlay
                // thì lời gọi lặp này vẫn giúp tránh trạng thái màn đen bị kẹt.
                blackOverlayShown = false;
#if UNITY_IOS && !UNITY_EDITOR
                CancelIosNativeTransitionOverlayFailsafe($"rewarded-hide-{reason}");
#endif
                ForceHideFsnOverlayNow(
                    $"rewarded-{reason}",
                    cleanupIosNativeContainers
                );

#if ENABLE_DEBUG_LOG
                Debug.Log($"{_Tag}[FSN][Rewarded] Ẩn overlay đen. Reason={reason}");
#endif
            }

            if (showRewarded == null)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"{_Tag}[FSN][Rewarded] showRewarded delegate is null. Không thể hiển thị MAX.");
#endif
                HideBlackOverlay("showRewarded-null", cleanupIosNativeContainers: true);
                onFailed?.Invoke();
                return;
            }

            bool maxAnswered = false;
            FsnRewardedCallbacks flowCallbacks = null;

            var flow = new FsnRewardedNativeFlow(
                showRewarded: callbacks =>
                {
                    flowCallbacks = callbacks;
                    // Bọc callbacks gốc để bắt chính xác việc user đã nhận reward từ MAX.
                    var wrappedCallbacks = new FsnRewardedCallbacks(
                        onRewardReceived: () =>
                        {
                            maxAnswered = true;
                            isRewardedReceived = true;
                            callbacks.OnRewardReceived();
                        },
                        onHidden: () =>
                        {
                            maxAnswered = true;
                            callbacks.OnHidden();
                        },
                        onDisplayFailed: () =>
                        {
                            maxAnswered = true;
                            callbacks.OnDisplayFailed();
                        }
                    );

                    showRewarded.Invoke(wrappedCallbacks);
                },
                showNative: onNativeDone =>
                {
                    // Callback này được gọi sau khi MAX Rewarded đã đóng.
                    // Overlay hiện vẫn đang bật từ trước lúc show MAX.
#if UNITY_IOS && !UNITY_EDITOR
                    BeginIosNativeTransitionOverlayFailsafe(
                        "rewarded-max-hidden-to-native"
                    );
#endif
                    bool nativeStepCompleted = false;

                    void FinishNativeStep()
                    {
                        if (nativeStepCompleted)
                        {
                            return;
                        }

                        nativeStepCompleted = true;
                        HideBlackOverlay("native-step-finished");
                        onNativeDone?.Invoke();
                    }

                    try
                    {
                        bool nativeReady = IsNativeReady(FsnAdType.Rewarded);

                        if (!nativeReady)
                        {
                            // Không có Native để phủ lên overlay: phải tắt overlay ngay,
                            // nếu không màn đen sẽ nằm lại sau khi MAX đóng.
#if ENABLE_DEBUG_LOG
                            Debug.LogWarning(
                                $"{_Tag}[FSN][Rewarded] MAX đã đóng nhưng Native chưa ready. " +
                                "Ẩn overlay ngay và bỏ qua lượt Native này.");
#endif

                            HideBlackOverlay("native-not-ready-after-max", cleanupIosNativeContainers: true);

                            // Đi qua hàm chung để giữ logic preload:
                            // - nếu request đang load thì tiếp tục chờ;
                            // - nếu chưa có request thì PrepareNextAd;
                            // - callback onNativeDone vẫn được hoàn tất.
                            ShowTrackedNativeWithFallback(
                                FsnAdType.Rewarded,
                                onDone: FinishNativeStep);
                            return;
                        }

                        // Native đã ready: giữ nguyên overlay đen trong lúc native view được add.
                        // ShowTrackedNativeByType sẽ gọi DelayHideOverlay sau vài frame,
                        // khi Native Ad đã nằm trên Unity.
                        ShowTrackedNativeWithFallback(
                            FsnAdType.Rewarded,
                            onDone: FinishNativeStep);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        FinishNativeStep();
                    }
                },
                onSuccess: () =>
                {
                    HideBlackOverlay("rewarded-flow-success");

                    if (isRewardedReceived)
                    {
                        onSuccess?.Invoke();
                    }
                    else
                    {
                        onFailed?.Invoke();
                    }
                },
                onFailed: () =>
                {
                    HideBlackOverlay("rewarded-flow-failed", cleanupIosNativeContainers: true);
                    onFailed?.Invoke();
                }
            );

            try
            {
                // Bật trước MAX, không bật sau callback đóng MAX.
                ShowBlackOverlayBeforeMax();
                flow.Start();
                StartFullScreenAnswerWatchdog("Rewarded", () => maxAnswered, () =>
                {
                    maxAnswered = true;
                    if (flowCallbacks != null)
                    {
                        flowCallbacks.OnDisplayFailed();
                        return;
                    }

                    HideBlackOverlay("silent-watchdog", cleanupIosNativeContainers: true);
                    onFailed?.Invoke();
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                HideBlackOverlay("rewarded-flow-exception", cleanupIosNativeContainers: true);
                onFailed?.Invoke();
            }
        }

        public void ShowInterstitialThenNative(FsnShowInterstitialDelegate showInterstitial, Action onSuccess,
            Action onFailed = null, string where = null)
        {
            RememberNativeWhere(FsnAdType.Interstitial, where);
            bool nativeStarted = false;
            bool finished = false;
            bool blackOverlayShown = false;

            void ShowBlackOverlayBeforeMax()
            {
                if (blackOverlayShown)
                {
                    return;
                }

#if UNITY_IOS && !UNITY_EDITOR
                CancelIosNativeTransitionOverlayFailsafe("interstitial-max-start");
#endif
                blackOverlayShown = true;
                ShowFsnOverlaySafely("interstitial-before-max");

#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][Interstitial] Hiện overlay đen trước khi show MAX để che toàn bộ chuyển cảnh MAX -> Native.");
#endif
            }

            void HideBlackOverlay(
                string reason,
                bool cleanupIosNativeContainers = false)
            {
                blackOverlayShown = false;
#if UNITY_IOS && !UNITY_EDITOR
                CancelIosNativeTransitionOverlayFailsafe($"interstitial-hide-{reason}");
#endif
                ForceHideFsnOverlayNow(
                    $"interstitial-{reason}",
                    cleanupIosNativeContainers
                );

#if ENABLE_DEBUG_LOG
                Debug.Log(
                    $"{_Tag}[FSN][Interstitial] Ẩn overlay đen. " +
                    $"Reason={reason}, CleanupNative={cleanupIosNativeContainers}");
#endif
            }

            void FinishSuccess()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                HideBlackOverlay("flow-success");
                onSuccess?.Invoke();
            }

            void FinishFailed()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                HideBlackOverlay("flow-failed", cleanupIosNativeContainers: true);
                onFailed?.Invoke();
            }

            void OnHidden()
            {
                if (finished || nativeStarted)
                {
                    return;
                }

                nativeStarted = true;

#if UNITY_IOS && !UNITY_EDITOR
                BeginIosNativeTransitionOverlayFailsafe(
                    "interstitial-max-hidden-to-native"
                );
#endif

                try
                {
                    bool nativeReady = IsNativeReady(FsnAdType.Interstitial);

                    if (!nativeReady)
                    {
                        // MAX đã đóng nhưng không có Native để hiển thị phía trên overlay.
                        // Tắt overlay trước khi kết thúc bước Native để không kẹt màn đen.
#if ENABLE_DEBUG_LOG
                        Debug.LogWarning(
                            $"{_Tag}[FSN][Interstitial] MAX đã đóng nhưng Native chưa ready. " +
                            "Ẩn overlay ngay và bỏ qua lượt Native này.");
#endif

                        HideBlackOverlay("native-not-ready-after-max", cleanupIosNativeContainers: true);

                        ShowTrackedNativeWithFallback(
                            FsnAdType.Interstitial,
                            onDone: FinishSuccess);
                        return;
                    }

                    // Native đã ready: giữ overlay đang bật và show Native ngay.
                    // DelayHideOverlay sẽ bỏ màn đen sau khi native view được add/draw.
                    ShowTrackedNativeWithFallback(
                        FsnAdType.Interstitial,
                        onDone: FinishSuccess);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    FinishFailed();
                }
            }

            void OnDisplayFailed()
            {
                if (finished)
                {
                    return;
                }

                FinishFailed();
            }

            var callbacks = new FsnInterstitialCallbacks(
                onHidden: OnHidden,
                onDisplayFailed: OnDisplayFailed);

            try
            {
                if (showInterstitial == null)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogWarning(
                        $"{_Tag}[FSN][Interstitial] showInterstitial delegate is null. Không thể hiển thị MAX.");
#endif
                    FinishFailed();
                    return;
                }

                // Khôi phục đúng luồng ban đầu: overlay phải bật trước MAX.
                ShowBlackOverlayBeforeMax();
                showInterstitial.Invoke(callbacks);
                StartFullScreenAnswerWatchdog("Interstitial", () => finished || nativeStarted, OnDisplayFailed);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                FinishFailed();
            }
        }

        private void ForceCompleteLostNativeStateAfterResume(string reason)
        {
            foreach (var pair in _preparedAds.ToList())
            {
                FsnAdType adType = pair.Key;

                if (adType == FsnAdType.Collapsible)
                {
                    continue;
                }

                PreparedAdInfo info = pair.Value;
                FsnAd showingAdInstance = info.showingAdInstance;

                if (showingAdInstance == null)
                {
                    continue;
                }

#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"{_Tag}[FSN][{adType}][Lifecycle] Native view seems lost after resume. Clear C# state only, do NOT call HideNativeAd. Reason={reason}, Prepared={info.GetDebugSummary()}, Target={showingAdInstance.GetDebugSummary()}");
#endif

                Action callback = info.lifecycleNativeFinishedCallback;

                if (ReferenceEquals(info.showingAdInstance, showingAdInstance))
                {
                    info.showingAdInstance = null;
                }

                if (ReferenceEquals(info.adInstance, showingAdInstance))
                {
                    info.adInstance = null;
                }

                // FALCON-PATCH(fsn-black-screen): giữ nguyên lifecycleShowingSequence + đánh dấu
                // force-complete để callback đóng native muộn bị guard trong ShowTrackedNativeByType
                // từ chối, không chạy nhánh đóng (onDone) lần thứ hai.
                info.lifecycleNativeFinishedCallback = null;
                info.lifecycleForceCompleted = true;

                info.isReady = false;
                info.isLoading = false;

                SafeInvoke(() => PrepareNextAd(adType));
                OnNativeAdHiddenSafe(adType);
                SafeInvoke(callback);
            }

            ForceResumeUnityGameplayAfterFsnNative(reason);
        }
    }

    // ==========================================
    // CONFIG DATA CLASSES (ĐỊNH NGHĨA BIẾN)
    // ==========================================
    public class FsnAdsConfig : IFalconConfig
    {
        // Bản mới CHỈ đọc biến này (cấu trúc FsnAdsConfigsData: adIds + adMultipliers, không maxAdLoad).
        public string moMulFsnAdsConfigsVer2;

        // Biến cũ (adIds theo style + maxAdLoad) — giữ nguyên cho các bản app cũ, bản mới không đọc nữa.
        public string moMulFsnAdsConfigs;
        public string moMulListCampaignIdsConfigs;
        public bool moMulEnableCampaignFilter;
    }

    /// <summary>
    /// Cùng bộ key FSN nhưng đọc từ CMS. Key nào CMS đã khai thì thắng data4game — xem
    /// <c>FsnAdManager.ResolveConfig</c>.
    /// </summary>
    public class FsnAdsConfigCms : Falcon.Modules.Core.RemoteConfigCms.IFalconConfigCms
    {
        public string moMulFsnAdsConfigsVer2;
        public string moMulListCampaignIdsConfigs;
        public bool moMulEnableCampaignFilter;
    }

    [Serializable]
    public class FsnAdsConfigsData
    {
        public FsnAdUnitConfig interstitial = new FsnAdUnitConfig();
        public FsnAdUnitConfig rewarded = new FsnAdUnitConfig();
        public FsnAdUnitConfig collapsible = new FsnAdUnitConfig();
        public FsnAdUnitConfig aoa = new FsnAdUnitConfig();
        public FsnAdUnitConfig adBreak = new FsnAdUnitConfig();

        public FsnAdUnitConfig GetConfig(FsnAdType type)
        {
            return type switch
            {
                FsnAdType.Interstitial => interstitial,
                FsnAdType.Rewarded => rewarded,
                FsnAdType.Collapsible => collapsible,
                FsnAdType.Aoa => aoa,
                FsnAdType.AdBreak => adBreak,
                _ => rewarded
            };
        }
    }

    /// <summary>Một bậc của thang giá FSN: ad unit + multiplier (0 = default, không floor).</summary>
    public readonly struct FsnLadderEntry
    {
        public readonly string adUnitId;
        public readonly float multiplier;

        public FsnLadderEntry(string adUnitId, float multiplier)
        {
            this.adUnitId = adUnitId;
            this.multiplier = multiplier;
        }

        public bool IsDefault => multiplier <= 0f;
    }

    [Serializable]
    public class FsnAdUnitConfig
    {
        /// <summary>
        /// adIds[i] đi cùng adMultipliers[i]. Multiplier "0" = ad DEFAULT (load không floor, lấy revenue
        /// làm giá cơ sở); multiplier khác 0 = ad FLOOR, floor = revenue default × multiplier.
        /// </summary>
        public List<string> adIds = new List<string>();
        public List<string> adMultipliers = new List<string>();
        public bool isEnable;
        public int timeShowCloseButton;
        // Bản cũ: style cố định. Bản mới KHÔNG đọc nữa (thay bằng nativeStyleRatio), chỉ giữ cho user bản cũ.
        public int styleOpenStore; //0 : openstore, 1 : fake close, 2 : countdown
        public int ratioAds;
        public long delayTimeForCountdown;
        public FsnTemplateRatioConfig ratioTemplate = new FsnTemplateRatioConfig();
        public FsnPositionOpenStoreRatio openStorePositionRatio = new FsnPositionOpenStoreRatio();
        public FsnPositionCloseRatio closePositionRatio = new FsnPositionCloseRatio();
        public FsnNativeStyleRatio nativeStyleRatio = new FsnNativeStyleRatio();

        /// <summary>
        /// Thang giá: phần tử đầu là ad DEFAULT (adId đầu tiên có multiplier 0), sau đó là các ad floor
        /// theo đúng thứ tự config. adId rỗng, multiplier thiếu/không parse được/âm bị bỏ qua; có nhiều
        /// multiplier 0 thì chỉ lấy cái đầu. Không có default -> trả list không bắt đầu bằng default,
        /// người gọi tự bỏ qua.
        /// </summary>
        public List<FsnLadderEntry> BuildLadder()
        {
            var result = new List<FsnLadderEntry>();
            if (adIds == null) return result;

            FsnLadderEntry? defaultEntry = null;
            var floors = new List<FsnLadderEntry>();

            for (int i = 0; i < adIds.Count; i++)
            {
                var adUnitId = adIds[i]?.Trim();
                if (string.IsNullOrEmpty(adUnitId)) continue;

                if (adMultipliers == null || i >= adMultipliers.Count ||
                    !float.TryParse(adMultipliers[i]?.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var multiplier) ||
                    float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogWarning($"[FSN] Bỏ adId '{adUnitId}' (index {i}): thiếu/sai adMultipliers.");
#endif
                    continue;
                }

                var entry = new FsnLadderEntry(adUnitId, multiplier);
                if (entry.IsDefault)
                {
                    if (defaultEntry == null) defaultEntry = entry;
                }
                else
                {
                    floors.Add(entry);
                }
            }

            if (defaultEntry != null) result.Add(defaultEntry.Value);
            result.AddRange(floors);
            return result;
        }
    }

    [Serializable]
    public class FsnPositionOpenStoreRatio
    {
        public int topRight;
        public int topLeft;
        public int bottomLeft;
        public int bottomRight;
    }

    [Serializable]
    public class FsnPositionCloseRatio
    {
        public int topRight;
        public int topLeft;
        public int bottomLeft;
        public int bottomRight;
    }

    /// <summary>
    /// Tỉ trọng style Template Ads, thay cho styleOpenStore cố định. Thứ tự khớp
    /// <see cref="FsnNativeAdType"/>: 0 = OpenStore, 1 = Skip (FakeClose), 2 = Countdown.
    /// </summary>
    [Serializable]
    public class FsnNativeStyleRatio
    {
        public int openStore;
        public int skip;
        public int countdown;
    }

    [Serializable]
    public class FsnTemplateRatioConfig
    {
        public int style0;
        public int style1;
        public int style2;
        public int style3;
    }

    public enum FsnAdType
    {
        Interstitial,
        Rewarded,
        Collapsible,
        Aoa,
        AdBreak
    }
}