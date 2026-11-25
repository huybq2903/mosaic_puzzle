using System;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Falcon.Admob
{
    /// <summary>
    /// ĐIỂM VÀO DUY NHẤT của module AdMob mediation.
    ///
    /// Cách dùng tối thiểu ở một dự án mới:
    ///
    /// <code>
    /// // 1. (tuỳ chọn) nối remote config + analytics của project
    /// AdmobMediationHooks.remoteConfigProvider = () => new AdmobRemoteSettings {
    ///     metaConfigJson = MyRemoteConfig.GetString("moMulAdmobMetaConfig"),
    /// };
    ///
    /// // 2. init — CHỦ ĐỘNG gọi, thường sau khi user trả lời consent form (UMP/CMP)
    /// AdmobMediation.Initialize();
    ///
    /// // 3. show
    /// AdmobMediation.Rewarded.Show("end_game", onCompleted: GiveReward, onFailed: ShowToast);
    /// </code>
    ///
    /// KHÔNG cần add component nào lên scene: GameObject <c>[AdmobMediation]</c> được tạo tự động
    /// (<see cref="AutoBootstrap"/>) và giữ qua mọi scene. KHÔNG có asset settings nào phải điền: ad
    /// unit của CẢ bước B lẫn bước C đều lấy từ remote config (<see cref="AdmobRemoteConfigStore"/>),
    /// <see cref="AdmobMediationConfig"/> chỉ còn các cờ hành vi.
    /// </summary>
    public static class AdmobMediation
    {
        /// <summary>Không có ad nào sẵn sàng. Cùng quy ước với mediation ngoài để so trực tiếp.</summary>
        public const double NoAd = AdmobBidAd.NoAd;

        /// <summary>Có ad nhưng chưa biết giá -> thắng "không có gì" nhưng thua mọi mức giá thật.</summary>
        public const double UnknownRevenue = AdmobBidAd.UnknownRevenue;

        private const string _RunnerName = "[AdmobMediation]";

        /// <summary>Tiền tố của các dòng log "quyết định init" — luôn bắn, không gate sau enableLog.</summary>
        private const string _INIT_TAG = "[AdmobMediation][init]";

        private static AdmobMediationRunner _runner;
        private static AdmobMediationConfig _config;
        private static bool _initializeRequested;

        // Đã gọi Initialize nhưng bỏ qua vì remote config chưa/không có ad unit nào. Config về sau thì
        // RefreshRemoteConfig() thử lại.
        private static bool _skippedForMissingAdUnits;

        private static event Action _onSdkInitialized;

        /// <summary>MobileAds.Initialize đã chạy xong chưa.</summary>
        public static bool SdkInitialized { get; private set; }

        /// <summary><see cref="Initialize"/> đã được gọi chưa (SDK có thể còn đang init).</summary>
        public static bool IsInitializing => _initializeRequested && !SdkInitialized;

        /// <summary>Config đang chạy; null nếu chưa <see cref="Initialize"/>.</summary>
        public static AdmobMediationConfig Config => _config;

        /// <summary>Tầng interstitial (bước B + C). null nếu chưa <see cref="Initialize"/>.</summary>
        public static AdmobFullscreenAds Interstitial { get; private set; }

        /// <summary>Tầng rewarded (bước B + C). null nếu chưa <see cref="Initialize"/>.</summary>
        public static AdmobFullscreenAds Rewarded { get; private set; }

        /// <summary>MonoBehaviour chạy coroutine của module (tự tạo, DontDestroyOnLoad).</summary>
        public static MonoBehaviour Runner => EnsureRunner();

        /// <summary>
        /// Tự dựng GameObject của module ngay khi game chạy — người dùng KHÔNG phải kéo component nào lên
        /// scene. Chỉ dựng object và đọc remote config; MobileAds KHÔNG được init ở đây, phải chờ project
        /// chủ động gọi <see cref="Initialize"/> (thường là sau consent form).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap() => EnsureRunner();

        /// <summary>
        /// Init AdMob. GỌI CHỦ ĐỘNG (thường sau khi user đã trả lời consent form). An toàn khi gọi lại
        /// nhiều lần — lần thứ hai trở đi chỉ đăng ký thêm callback.
        /// </summary>
        /// <param name="config">
        /// Null = dùng mặc định. Đây chỉ là tham số HÀNH VI (log, bidding on/off...) — mọi ad unit id
        /// đều đến từ remote config, xem <see cref="AdmobRemoteConfigStore"/>.
        /// </param>
        /// <param name="onInitialized">Chạy khi SDK init xong (gọi ngay nếu đã init trước đó).</param>
        public static void Initialize(AdmobMediationConfig config = null, Action onInitialized = null)
        {
            AddOnSdkInitialized(onInitialized);

            if (_initializeRequested)
            {
                Log("Initialize() bị gọi lại — bỏ qua, SDK đã đang/đã init.");
                return;
            }

            _config = (config ?? new AdmobMediationConfig()).Clone();

            AdmobMultiPlatformUtils.LogEnabled = _config.enableLog;
            AdmobRemoteConfigStore.Reload();

            // Remote config không cấp ad unit nào — cả bước B lẫn bước C, cả hai format — thì AdMob
            // KHÔNG có việc gì để làm: đừng init MobileAds (tốn thời gian khởi động + consent + một
            // GameObject sống mãi), để game chạy riêng bước A của MultiCall.
            //
            // KHÔNG set _initializeRequested: đây là "chưa đủ điều kiện", không phải "đã init". Config
            // về muộn thì RefreshRemoteConfig() gọi lại đúng hàm này.
            if (!AdmobRemoteConfigStore.HasAnyFullscreenAdUnit)
            {
                _skippedForMissingAdUnits = true;

                // Vô điều kiện: "AdMob có init không" là câu hỏi đầu tiên khi soi log, không được phép
                // nằm sau cờ enableLog.
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_INIT_TAG} KHÔNG INIT ADMOB — remote config không có ad unit nào " +
                                 "(cả bước B lẫn bước C, cả interstitial lẫn rewarded). " +
                                 $"Game chạy mỗi bước A của MultiCall.\n  {AdmobRemoteConfigStore.Describe()}");
#endif
                return;
            }

            _skippedForMissingAdUnits = false;
            _initializeRequested = true;

            var runner = EnsureRunner();
            Interstitial = new AdmobFullscreenAds(runner, AdmobAdFormat.Interstitial, _config);
            Rewarded = new AdmobFullscreenAds(runner, AdmobAdFormat.Rewarded, _config);

#if ENABLE_DEBUG_LOG
            Debug.Log($"{_INIT_TAG} INIT ADMOB — publisherId={_config.ResolvePublisherId()}\n" +
                      $"  interstitial : {DescribeSteps(Interstitial)}\n" +
                      $"  rewarded     : {DescribeSteps(Rewarded)}\n" +
                      $"  {AdmobRemoteConfigStore.Describe()}");
#endif

            // PHẢI set TRƯỚC Initialize. Mặc định GMA bắn callback (OnAdPaid, OnAdLoaded, ...) trên
            // background thread của Android; mọi Unity API / SDK analytics trong các callback đó sẽ hỏng
            // nếu project không tự marshal.
            if (_config.raiseAdEventsOnUnityMainThread) MobileAds.RaiseAdEventsOnUnityMainThread = true;

            Log("MobileAds.Initialize() ...");
            MobileAds.Initialize(OnInitializationComplete);
        }

        /// <summary>
        /// Đọc lại remote config bước C. Project gọi mỗi khi config đổi (vd event OnUpdateFromNet).
        /// Mọi lần xin ad sau đó dùng ngay giá trị mới — không cần restart.
        /// </summary>
        public static void RefreshRemoteConfig()
        {
            AdmobRemoteConfigStore.Reload();
            if (_config != null && _config.enableLog)
                Log($"remote config cập nhật: {AdmobRemoteConfigStore.Describe()}");

            // Lần Initialize trước bỏ qua vì chưa có ad unit nào — config vừa về có thể đã có -> thử lại.
            if (_skippedForMissingAdUnits && AdmobRemoteConfigStore.HasAnyFullscreenAdUnit)
            {
                Log("remote config đã có ad unit -> init lại AdMob (lần trước bỏ qua vì config rỗng).");
                Initialize(_config);
                return;
            }

            // Ad unit BƯỚC B cũng nằm trong config này, nên đây có thể là lần đầu hai tầng có đủ thứ để
            // chạy (config về sau MobileAds.Initialize).
            Interstitial?.OnRemoteConfigUpdated();
            Rewarded?.OnRemoteConfigUpdated();
        }

        /// <summary>
        /// BƯỚC C — project báo rằng mediation chạy song song (vd MAX) vừa load xong một ad với
        /// <paramref name="revenueUsd"/> USD/impression. Module xin AdMob cả thang giá quanh mức đó.
        ///
        /// Dự án chỉ chạy AdMob thì KHÔNG bao giờ gọi hàm này — bước C tự động không chạy.
        /// </summary>
        public static void NotifyExternalAdLoaded(AdmobAdFormat format, string externalAdUnitId,
            double revenueUsd)
        {
            switch (format)
            {
                case AdmobAdFormat.Interstitial:
                    Interstitial?.OnExternalAdLoaded(externalAdUnitId, revenueUsd);
                    break;
                case AdmobAdFormat.Rewarded:
                    Rewarded?.OnExternalAdLoaded(externalAdUnitId, revenueUsd);
                    break;
            }
        }

        /// <summary>
        /// Mediation chạy song song (vd MAX) vừa có impression với <paramref name="revenueUsd"/> USD —
        /// tức nó THẮNG giá ad AdMob ở lượt show này. Module vẫn khai báo impression với AdMob (cùng
        /// request <c>type=impression</c> bắn khi AdMob tự thắng), gắn vào response của MỌI ad AdMob
        /// đang giữ cho format đó (nối bằng dấu phẩy) — tất cả đều đã thua.
        ///
        /// Dự án chỉ chạy AdMob thì KHÔNG bao giờ gọi hàm này.
        /// </summary>
        public static void NotifyExternalImpression(AdmobAdFormat format, double revenueUsd,
            AdValue.PrecisionType precision)
        {
            switch (format)
            {
                case AdmobAdFormat.Interstitial:
                    Interstitial?.OnExternalImpression(revenueUsd, precision);
                    break;
                case AdmobAdFormat.Rewarded:
                    Rewarded?.OnExternalImpression(revenueUsd, precision);
                    break;
            }
        }

        /// <summary>
        /// Đăng ký callback init. SDK đã init rồi thì gọi luôn — tránh bug kinh điển "subscribe sau khi
        /// event đã bắn thì không bao giờ chạy".
        /// </summary>
        public static void AddOnSdkInitialized(Action callback)
        {
            if (callback == null) return;

            if (SdkInitialized)
            {
                callback();
                return;
            }

            _onSdkInitialized += callback;
        }

        public static void RemoveOnSdkInitialized(Action callback)
        {
            if (callback == null) return;
            _onSdkInitialized -= callback;
        }

        private static AdmobMediationRunner EnsureRunner()
        {
            if (_runner != null) return _runner;

            var go = new GameObject(_RunnerName);
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<AdmobMediationRunner>();
            return _runner;
        }

        private static void OnInitializationComplete(InitializationStatus status)
        {
            if (SdkInitialized) return;
            SdkInitialized = true;

            if (_config != null && _config.enableLog)
            {
                var map = status?.getAdapterStatusMap();
                if (map != null)
                {
                    foreach (var pair in map)
                        Log($"adapter '{pair.Key}' -> {pair.Value.InitializationState} ({pair.Value.Description})");
                }
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"{_INIT_TAG} MobileAds.Initialize DONE -> bắt đầu load ad.");
#endif

            Interstitial?.OnSdkInitialized();
            Rewarded?.OnSdkInitialized();

            var handlers = _onSdkInitialized;
            _onSdkInitialized = null;
            try
            {
                handlers?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        // Runner bị destroy (thoát play mode / load lại app) -> vứt hết state để lần init sau dựng lại
        // sạch, không giữ ad object trỏ vào SDK đã chết.
        internal static void OnRunnerDestroyed(AdmobMediationRunner runner)
        {
            if (!ReferenceEquals(_runner, runner)) return;

            Interstitial?.Dispose();
            Rewarded?.Dispose();

            Interstitial = null;
            Rewarded = null;

            _runner = null;
            _initializeRequested = false;
            _skippedForMissingAdUnits = false;
            SdkInitialized = false;
            _onSdkInitialized = null;
        }

        // Một dòng "format này chạy được bước nào", kèm luôn ad unit bước B để khỏi phải mò sang log khác.
        private static string DescribeSteps(AdmobFullscreenAds layer)
        {
            var stepB = layer.RunsStepB
                ? $"CHẠY (adUnit={layer.DefaultAdUnitId})"
                : "BỎ QUA (remote config không có defaultAdId)";

            var stepC = layer.RunsStepC
                ? "CHẠY"
                : "BỎ QUA (remote config không khai mức floor nào)";

            return $"bướcB={stepB} | bướcC={stepC}";
        }

        private static void Log(string message)
        {
#if ENABLE_DEBUG_LOG
            if (_config == null || _config.enableLog) Debug.Log($"[AdmobMediation] {message}");
#endif
        }
    }

    /// <summary>
    /// GameObject của module, tạo tự động và DontDestroyOnLoad. Chỉ làm hai việc: cho các tầng ad mượn
    /// StartCoroutine (retry backoff), và dọn dẹp khi bị destroy.
    /// </summary>
    public class AdmobMediationRunner : MonoBehaviour
    {
        private void OnDestroy() => AdmobMediation.OnRunnerDestroyed(this);
    }
}
