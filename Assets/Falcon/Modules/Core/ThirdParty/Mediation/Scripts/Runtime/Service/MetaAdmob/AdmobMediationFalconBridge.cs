using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfig;
using Falcon.Modules.Core.RemoteConfigCms;
using Falcon.Modules.Core.ThirdParty.Firebase.Scripts.Runtime;
using Falcon.Modules.Core.ThirdParty.Mediation.Runtime;
#if MAX_ENABLE
using Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic;
#endif
using Falcon.Modules.Core.ThirdParty.Mmp.Runtime;
using Falcon.Modules.Level.Core;
using UnityEngine;
using AdmobPrecision = GoogleMobileAds.Api.AdValue.PrecisionType;
// Hai hệ remote config đều có class tĩnh tên FConfig -> gọi qua alias, gọi trần là CS0104 (ambiguous)
using GameConfig = Falcon.Modules.Core.RemoteConfig.FConfig;
using CmsConfig = Falcon.Modules.Core.RemoteConfigCms.FConfig;

namespace Falcon.Admob
{
    /// <summary>
    /// CẦU NỐI giữa tầng AdMob thuần (các file <c>Admob*.cs</c> cùng thư mục) và hạ tầng Falcon. Để
    /// CÙNG thư mục module cho tiện export một lần, nhưng KHÔNG thuộc phần lõi: dự án chỉ chạy AdMob
    /// thuần cứ XOÁ file này đi, phần còn lại vẫn chạy bình thường (chỉ mất bước C).
    ///
    /// PHỤ THUỘC NGOÀI — RIÊNG file này cần, các file <c>Admob*.cs</c> khác KHÔNG cần gì ngoài
    /// GoogleMobileAds:
    ///  - Remote config bước C (<c>moMulAdmobMetaConfig</c>): CMS (<see cref="CmsConfig"/>) nếu CMS đã
    ///    khai key, chưa khai thì data4game (<see cref="GameConfig"/>). Luồng init chờ cả hai nguồn.
    ///  - <see cref="FalconFirebaseLog"/> / <see cref="FalconMmpLog"/> / <see cref="FalconBigDataController"/>
    ///    — đúng bộ ba mà <c>FalconMaxService.LogAdRevenue</c> + <c>FalconMaxService.AdLog</c> dùng cho MAX.
    ///  - <see cref="MultiCall"/> + MaxSdkCallbacks — nguồn revenue của mediation chạy song song.
    ///  - <see cref="LevelData"/> — level mặc định cho ad-log (project gán đè được).
    /// Thiếu thứ nào thì sửa đúng chỗ dùng nó, hoặc xoá hẳn file này.
    ///
    /// Nối 4 nhóm:
    ///  1. Remote config bước C: CMS, rơi về data4game -> AdmobMediationHooks.remoteConfigProvider.
    ///  2. Mediation chạy song song: MAX -> bước C + highest_other_bid + impression lúc MAX thắng giá.
    ///  3. Analytics: Firebase ad_impression + MMP logAdRevenue + FalconBigDataController.Ad.OnImpression.
    ///  4. AdFlowGuard: chặn App Open hiện chồng, dùng CHUNG bộ đếm với tầng MAX.
    ///
    /// Gọi <see cref="Install"/> TRƯỚC <see cref="AdmobMediation.Initialize"/>. Càng sớm càng tốt:
    /// Install chỉ gán delegate, không đụng SDK nào, còn ad MAX nào load xong TRƯỚC lúc Install thì
    /// tín hiệu revenue của nó mất luôn (event thường, không phát lại).
    /// </summary>
    public static class AdmobMediationFalconBridge
    {
        private const string _AD_PLATFORM = "AdMob";   // ad_platform của log Firebase (MAX gửi "MAX")
        private const string _MEDIATION = "Admob";     // giá trị adMediation gửi lên big data
        private const string _UNKNOWN = "unknown";

        //tiền tố của log vòng đời remote config — luôn bắn, KHÔNG nằm sau cờ enableLog
        private const string _HOOK_TAG = "[AdmobBridge][remote-config]";

        private static bool _installed;
        private static volatile bool _maxInitialized;

        public static void NotifyMaxInitialized() => _maxInitialized = true;
        private static bool _remoteConfigHooked;

        /// <summary>Nối toàn bộ hook. Gọi lại nhiều lần cũng an toàn.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            AdmobMediationHooks.remoteConfigProvider = ReadRemoteSettings;
            AdmobMediationHooks.externalBestRevenueProvider = BestExternalRevenue;
            AdmobMediationHooks.externalTierProvider = ExternalTier;
            AdmobMediationHooks.onImpression = ReportImpression;
            AdmobMediationHooks.onAdRequested = ReportAdRequested;
            AdmobMediationHooks.onAdShown = ReportAdShown;
            AdmobMediationHooks.onAdDisplayed = _ => FsnAdManager.NotifyFullScreenAdDisplayed();
            AdmobMediationHooks.onAdShowFailed = ReportAdShowFailed;
            AdmobMediationHooks.onAdClicked = ReportAdClicked;
            AdmobMediationHooks.onAdClosed = ReportAdClosed;
            AdmobMediationHooks.onLoadResult = ReportLoadResult;
            //kèm af_level như mọi event MMP của FalconMaxService (af_inters_displayed / af_rewarded_displayed...)
            AdmobMediationHooks.onTrackEvent = eventName => FalconMmpLog.LogEvent(eventName,
                new Dictionary<string, string> { { "af_level", CurrentLevel() + "" } });
            AdmobMediationHooks.uaChannelProvider = () => GameData4Appsflyer.Instance.mediaSource;
            AdmobMediationHooks.daysSinceInstallProvider = GetDaysSinceInstall;
            AdmobMediationHooks.onAdFlowBegin = AdFlowGuard.BeginAdFlow;
            AdmobMediationHooks.onAdFlowEnd = AdFlowGuard.EndAdFlow;

            // Readme bảo project tự gán levelProvider TRƯỚC Install -> chỉ điền khi còn trống.
            AdmobMediationHooks.levelProvider ??= CurrentLevel;

            // KHÔNG đăng ký FConfig.OnUpdateFromNet ở đây — Install() chạy muộn (sau UMP, sau hàng rào
            // config của MediationManager) nên event có thể đã bắn xong từ trước và ta đăng ký vào chỗ
            // trống. Việc đó do HookRemoteConfigEarly() lo, chạy ở BeforeSceneLoad.
            // Lần đọc ĐẦU TIÊN không phụ thuộc event: AdmobMediation.Initialize luôn gọi
            // AdmobRemoteConfigStore.Reload() để đọc thẳng config đang có tại thời điểm init.

            // BƯỚC C: mỗi ad unit MAX load xong đẩy revenue sang tầng AdMob để xin cả thang giá quanh đó.
            SubscribeExternalMediation();

            // id AdMob lấy từ sc_ad_bid_floor: bản tin về muộn / đổi giữa phiên -> đọc lại (xem OnBidFloorsChanged)
            if (!_watchingBidFloors)
            {
                _watchingBidFloors = true;
                AdmobMediation.Runner.StartCoroutine(WatchBidFloors());
            }

            // DFPool bật / tắt muộn (config về sau lúc init) -> đọc lại id AdMob để gỡ / trả format bị nhận
            AdmobTakeover.OnChanged += OnTakeoverChanged;

            // Lúc SHOW: cắm tầng AdMob vào chỗ so giá của MultiCall để ad bước B/C được đem ra đọ với
            // ad MAX. Không cắm thì MultiCall chỉ thấy ad MAX và ad AdMob nằm chết trong kho.
            SecondaryAdSource.Current = new AdmobSecondaryAdSource();
        }

        /// <summary>
        /// ĐIỂM VÀO KHUYẾN NGHỊ của project: nối hook rồi CHỜ đủ remote config mới init AdMob.
        ///
        /// Config bước C nằm ở remote config data4game, về độc lập với CMS của MAX; init trước lúc nó
        /// về thì <see cref="AdmobRemoteConfigStore"/> rỗng và bước C không có ad unit nào để xin — ad
        /// vẫn chạy nhưng chỉ còn mỗi bước B, và lỗi đó im lặng. Tầng MAX gác ở đúng hàng rào này
        /// (<see cref="AdsConfigGate"/>, xem <c>MediationManager.StartProcess</c>) nên hai tầng khởi
        /// động cùng một mốc.
        ///
        /// Vẫn phải gọi SAU khi user trả lời consent form (UMP/CMP) như trước.
        /// </summary>
        public static void InstallAndInitialize(
            AdmobMediationConfig config = null, Action onInitialized = null)
        {
            Install();
            AdmobMediation.Runner.StartCoroutine(WaitConfigThenInitialize(config, onInitialized));
        }

        private static IEnumerator WaitConfigThenInitialize(
            AdmobMediationConfig config, Action onInitialized)
        {
            //Id AdMob (bước B + C) lấy từ sc_ad_bid_floor — MediationManager đã chờ bản tin này (hoặc hết
            //timeout thì dùng cache) trước khi init MAX, nên tới đây danh sách đã có.
#if MAX_ENABLE
            //Chờ chốt chế độ phiên (FalconMaxService.Init, chạy ngay sau lệnh này). Phiên LUA: script Lua tự
            //load id AdMob của sc_ad_bid_floor -> meta AdMob KHÔNG chạy, nếu không cùng một id bị load 2 lần.
            while (!LuaAdMonetize.IsModeLocked) yield return null;
            if (LuaAdMonetize.IsLuaMode)
            {
#if ENABLE_DEBUG_LOG
                Debug.Log($"{_HOOK_TAG} phiên LUA -> meta AdMob KHÔNG chạy (id AdMob do script Lua load)");
#endif
                yield break;
            }

#if ENABLE_DEBUG_LOG
            if (!_maxInitialized) Debug.Log($"{_HOOK_TAG} chờ MAX init thành công rồi mới init AdMob");
#endif
            while (!_maxInitialized) yield return null;

            //DFPool (custom adapter) có config thì nó load AdMob của format đó, meta AdMob bỏ id mediation=admob
            //-> phải chờ nó quyết định xong, không thì cả hai cùng xin AdMob. Trần chờ = trần chờ config của
            //DFPool (+ dư), quá mốc thì init luôn, DFPool bật sau thì OnTakeoverChanged gỡ lại.
            var takeoverDeadline = Time.realtimeSinceStartup + AdsConfigGate.CMS_FIRST_TIMEOUT_SECONDS + 5f;
            while (!AdmobTakeover.IsDecided && Time.realtimeSinceStartup < takeoverDeadline) yield return null;
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_HOOK_TAG} MAX init thành công -> init AdMob " +
                      $"(DFPool nhận: inter={AdmobTakeover.IsClaimed(false)} rewarded={AdmobTakeover.IsClaimed(true)}" +
                      (AdmobTakeover.IsDecided ? ")" : ", hết thời gian chờ DFPool quyết định)"));
#endif
#endif
            AdmobMediation.Initialize(config, onInitialized);
        }

        #region remote config (id AdMob lấy từ sc_ad_bid_floor)

        //SCAdBidFloor.OnData có thể chạy ngoài main thread -> chỉ bật cờ, vòng WatchBidFloors đọc lại trên main
        private static volatile bool _bidFloorDirty;
        private static bool _watchingBidFloors;

        /// <summary>
        /// SCAdBidFloor gọi khi nhận danh sách mới (gọi được từ mọi thread). Phiên MULTICALL: meta AdMob đọc lại
        /// id AdMob ngay frame sau (chưa init vì danh sách trước không có id AdMob thì init luôn).
        /// </summary>
        public static void OnBidFloorsChanged() => _bidFloorDirty = true;

        //cùng đường với bản tin sc_ad_bid_floor mới: vòng WatchBidFloors đọc lại trên main thread
        private static void OnTakeoverChanged() => _bidFloorDirty = true;

        private static IEnumerator WatchBidFloors()
        {
            while (true)
            {
                if (_bidFloorDirty)
                {
                    _bidFloorDirty = false;
#if MAX_ENABLE
                    if (!LuaAdMonetize.IsLuaMode)
#endif
                    {
#if ENABLE_DEBUG_LOG
                        Debug.Log($"{_HOOK_TAG} nhận sc_ad_bid_floor mới -> đọc lại id AdMob — {Stamp()}");
#endif
                        AdmobMediation.RefreshRemoteConfig();
                    }
                }

                yield return null;
            }
        }

        /// <summary>
        /// Dựng config bước B + C từ các id <c>mediation = admob</c> của <c>sc_ad_bid_floor</c> (bản thô, KHÔNG
        /// lọc algorithmIds — id AdMob để algorithmIds=[10] chỉ để bản cũ bỏ qua). Mỗi format:
        ///  - BƯỚC B: id có adUnitIdType chứa "default" (không có adUnitIdType thì multiplier 0 = default).
        ///    Module chỉ chạy 1 id default -> lấy id đầu theo tên adUnitIdType (default_0 trước default_1).
        ///  - BƯỚC C: các id còn lại, giá sàn = revenue ad MAX × multiplier (multiplier &lt;= 0 tính là 1).
        /// Phiên LUA trả rỗng: Lua tự load các id này.
        /// </summary>
        private static AdmobRemoteSettings ReadRemoteSettings()
        {
            try
            {
#if MAX_ENABLE
                if (LuaAdMonetize.IsLuaMode) return new AdmobRemoteSettings();
#endif
                var list = MultiCall.Instance.RawAdBidFloors ?? new List<AdBidFloor>();
                var data = new AdmobMetaAdsConfigsData();
                //format đã có DFPool (custom adapter) load AdMob -> bỏ id mediation=admob, tránh request gấp đôi
                if (!AdmobTakeover.IsClaimed(false)) FillFromBidFloors(data.interstitial, list, false);
                if (!AdmobTakeover.IsClaimed(true)) FillFromBidFloors(data.rewarded, list, true);
#if ENABLE_DEBUG_LOG
                if (AdmobTakeover.IsClaimed(false) || AdmobTakeover.IsClaimed(true))
                    Debug.Log($"{_HOOK_TAG} DFPool đang load AdMob -> bỏ id mediation=admob của sc_ad_bid_floor: " +
                              $"inter={AdmobTakeover.IsClaimed(false)} rewarded={AdmobTakeover.IsClaimed(true)}");
#endif

                var hasAny = data.interstitial.HasDefaultAdUnit || data.interstitial.HasFloorAdUnits ||
                             data.rewarded.HasDefaultAdUnit || data.rewarded.HasFloorAdUnits;
                var json = hasAny ? JsonUtility.ToJson(data) : null;

#if ENABLE_DEBUG_LOG
                Debug.Log($"{_HOOK_TAG} id AdMob lấy từ sc_ad_bid_floor " +
                          $"({(MultiCall.Instance.getListAdBidFloorFromServer ? "server" : "cache")}, {list.Count} id) — " +
                          (hasAny ? "" : "KHÔNG có id mediation=admob nào — ") + Stamp());
#endif
                return new AdmobRemoteSettings { moMulAdmobMetaConfig = json };
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return null;
            }
        }

        private static void FillFromBidFloors(AdmobMetaAdsUnitConfig config, List<AdBidFloor> list, bool rewarded)
        {
            var seen = new HashSet<string>();
            var defaults = new List<AdBidFloor>();
            var floors = new List<AdBidFloor>();
            foreach (var b in list)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.adUnitId) || b.rewarded != rewarded) continue;
                if (b.MediationKind() != AdBidFloor.MEDIATION_ADMOB) continue;
                if (!seen.Add(b.adUnitId.Trim())) continue;
                (IsDefaultBidFloor(b) ? defaults : floors).Add(b);
            }

            defaults.Sort(CompareByType);
            //thang giá: multiplier thấp -> cao (giống ví dụ remote config cũ), cùng mức thì theo tên
            floors.Sort((a, b) => a.multiplier != b.multiplier ? a.multiplier.CompareTo(b.multiplier) : CompareByType(a, b));

            config.defaultAdId = defaults.Count > 0 ? defaults[0].adUnitId.Trim() : null;
            config.adIds = new List<string>();
            config.multipliers = new List<float>();
            foreach (var f in floors)
            {
                config.adIds.Add(f.adUnitId.Trim());
                config.multipliers.Add(f.multiplier);
            }

#if ENABLE_DEBUG_LOG
            if (defaults.Count > 1)
                Debug.LogWarning($"{_HOOK_TAG} {(rewarded ? "rewarded" : "interstitial")}: sc_ad_bid_floor có " +
                                 $"{defaults.Count} id AdMob default, meta AdMob chỉ chạy 1 -> dùng " +
                                 $"{defaults[0].adUnitIdType}={config.defaultAdId}, bỏ các id còn lại");
#endif
        }

        private static bool IsDefaultBidFloor(AdBidFloor b)
            => string.IsNullOrWhiteSpace(b.adUnitIdType)
                ? b.multiplier <= 0f
                : b.adUnitIdType.IndexOf("default", StringComparison.OrdinalIgnoreCase) >= 0;

        //theo tên adUnitIdType: phần chữ trước, số ở cuối so như SỐ (default_2 < default_10)
        private static int CompareByType(AdBidFloor a, AdBidFloor b)
        {
            SplitType(a.adUnitIdType, out var pa, out var na);
            SplitType(b.adUnitIdType, out var pb, out var nb);
            var c = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            if (na != nb) return na.CompareTo(nb);
            return string.CompareOrdinal(a.adUnitId, b.adUnitId);
        }

        private static void SplitType(string type, out string prefix, out long number)
        {
            type = type?.Trim() ?? string.Empty;
            var i = type.Length;
            while (i > 0 && char.IsDigit(type[i - 1])) i--;
            prefix = type.Substring(0, i);
            number = i < type.Length && long.TryParse(type.Substring(i), out var n) ? n : 0;
        }

        /// <summary>
        /// Đăng ký lắng nghe remote config (CẢ data4game lẫn CMS — key bước C có thể nằm ở một trong
        /// hai) Ở THỜI ĐIỂM SỚM NHẤT CÓ THỂ.
        ///
        /// <c>OnUpdateFromNet</c> của cả hai là <c>event Action</c> trần: bắn một lần rồi thôi,
        /// KHÔNG phát lại cho người đăng ký sau. Đăng ký nó trong <see cref="Install"/> là hỏng —
        /// Install chạy sau UMP và sau hàng rào config của MediationManager, config hoàn toàn có thể
        /// đã về xong từ trước, và ta đăng ký vào chỗ trống rồi ngồi chờ một event không bao giờ bắn nữa.
        ///
        /// BeforeSceneLoad chạy trước mọi MonoBehaviour nên chắc chắn sớm hơn mọi lời gọi fetch config.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookRemoteConfigEarly()
        {
            if (_remoteConfigHooked) return;
            _remoteConfigHooked = true;

            GameConfig.OnUpdateFromNet += OnGameConfigUpdatedFromNet;
            CmsConfig.OnUpdateFromNet += OnCmsConfigUpdatedFromNet;

            //Mốc để CHỨNG MINH thứ tự: dòng này phải xuất hiện TRƯỚC dòng "OnUpdateFromNet ĐÃ BẮN".
            //Nếu thấy dòng BẮN mà không có dòng này ở trên -> đăng ký muộn, đã hụt event.
            //Nếu thấy dòng này mà cả phiên không có dòng BẮN -> event chưa từng bắn (fetch hỏng / không
            //có mạng / FConfigUpdateReactCaller chưa được dựng), KHÔNG phải lỗi đăng ký.
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_HOOK_TAG} ĐÃ ĐĂNG KÝ OnUpdateFromNet (data4game + cms) — {Stamp()}, " +
                      $"InitState lúc đăng ký: data4game={GameConfig.InitState} cms={CmsConfig.InitState}");
#endif
        }

        //frame + đồng hồ thực: đủ để xếp thứ tự các dòng log kể cả khi chúng nằm xen giữa log khác.
        private static string Stamp()
            => $"frame={Time.frameCount} t={Time.realtimeSinceStartup:F2}s";

        // OnUpdateFromNet là Action trần (không có sender/args) -> mỗi nguồn một handler không tham số.
        private static void OnGameConfigUpdatedFromNet()
            => OnConfigUpdatedFromNet("data4game", GameConfig.InitState);

        private static void OnCmsConfigUpdatedFromNet()
            => OnConfigUpdatedFromNet("cms", CmsConfig.InitState);

        private static void OnConfigUpdatedFromNet(string source, ExecState initState)
        {
#if ENABLE_DEBUG_LOG
            Debug.Log($"{_HOOK_TAG} {source} OnUpdateFromNet ĐÃ BẮN — {Stamp()}, " +
                      $"installed={_installed}, InitState={initState}");
#endif

            // Config về TRƯỚC khi project gọi Install(): chưa có remoteConfigProvider nên Reload() chỉ
            // đọc được null. Bỏ qua, không mất gì — Initialize() đọc thẳng config đang có ngay sau đó.
            if (!_installed) return;

            // HOÃN 1 FRAME, KHÔNG đọc ngay. FConfig.ConfigUpdateCallback (thứ bắn event này) và
            // FConfigInstanceService (thứ XOÁ CACHE của Config<T>) là hai IConfigUpdatedReact nằm chung
            // một danh sách, gọi lần lượt theo thứ tự KHỞI TẠO SINGLETON — không đảm bảo cái xoá cache
            // chạy trước. Đọc ngay trong callback là có cửa trúng đúng instance cũ đã cache từ trước
            // lúc fetch, tức "config về rồi mà vẫn đọc ra rỗng".
            AdmobMediation.Runner.StartCoroutine(RefreshRemoteConfigNextFrame());
        }

        private static IEnumerator RefreshRemoteConfigNextFrame()
        {
            yield return null;

#if ENABLE_DEBUG_LOG
            Debug.Log($"{_HOOK_TAG} đọc lại remote config sau khi hoãn 1 frame — {Stamp()}");
#endif
            AdmobMediation.RefreshRemoteConfig();
        }

        #endregion

        #region mediation chạy song song (MAX)

        // MaxSdkCallbacks bắn trên Unity main thread nên gọi thẳng vào module là an toàn. Bắt Ở ĐÂY chứ
        // không sửa MultiCallMaxProviderParent: mọi ad unit MAX load xong đều là một mốc giá hợp lệ cho
        // bước C, kể cả unit đơn của FalconMaxService khi game không bật multi-call.
        //
        // Revenue-paid của MAX = MAX THẮNG giá lượt show đó -> vẫn khai báo impression với AdMob (request
        // type=impression), không chỉ lúc AdMob thắng. Bắt ở đây cùng lý do: đi qua MỌI đường show MAX.
        private static void SubscribeExternalMediation()
        {
#if MAX_ENABLE
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnMaxInterstitialLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnMaxRewardedLoaded;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnMaxInterstitialRevenuePaid;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnMaxRewardedRevenuePaid;
#endif
        }

#if MAX_ENABLE
        private static void OnMaxInterstitialLoaded(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool): không phải mốc giá MAX
            OnExternalAdLoaded(AdmobAdFormat.Interstitial, adUnitId, adInfo.Revenue);
        }

        private static void OnMaxRewardedLoaded(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool): không phải mốc giá MAX
            OnExternalAdLoaded(AdmobAdFormat.Rewarded, adUnitId, adInfo.Revenue);
        }

        private static void OnMaxInterstitialRevenuePaid(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool): không phải mốc giá MAX
            OnExternalImpression(AdmobAdFormat.Interstitial, adInfo);
        }

        private static void OnMaxRewardedRevenuePaid(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool): không phải mốc giá MAX
            OnExternalImpression(AdmobAdFormat.Rewarded, adInfo);
        }

        private static void OnExternalImpression(AdmobAdFormat format, MaxSdkBase.AdInfo adInfo)
        {
            try
            {
                AdmobMediation.NotifyExternalImpression(format, adInfo.Revenue,
                    ToAdmobPrecision(adInfo.RevenuePrecision));
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        // revenuePrecision của MAX -> AdValue.PrecisionType của AdMob, để pip cùng thang với lượt AdMob
        // tự thắng. "undefined" (iOS có thể trả chuỗi rỗng) rơi về Unknown.
        private static AdmobPrecision ToAdmobPrecision(string maxPrecision) => maxPrecision switch
        {
            "exact" => AdmobPrecision.Precise,
            "publisher_defined" => AdmobPrecision.PublisherProvided,
            "estimated" => AdmobPrecision.Estimated,
            _ => AdmobPrecision.Unknown
        };
#endif

        private static void OnExternalAdLoaded(AdmobAdFormat format, string adUnitId, double revenue)
        {
            try
            {
                AdmobMediation.NotifyExternalAdLoaded(format, adUnitId, revenue);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        // Bid cao nhất KHÔNG đến từ AdMob = revenue cao nhất trong các ad MAX cùng format mà MultiCall
        // đang cầm sẵn. MultiCall dùng eCpm = -100 cho "chưa có ad / vừa tiêu ad" nên mọi giá trị <= 0
        // đều không phải mức giá thật -> hook quy ước trả về 0 là không có gì để khai báo.
        private static double BestExternalRevenue(AdmobAdFormat format)
        {
            try
            {
#if MAX_ENABLE
                //phiên Lua: ad MAX đắt nhất mà script đang giữ (MultiCall không chạy)
                if (LuaAdMonetize.IsLuaMode)
                    return Math.Max(0d, LuaAdMonetize.BestReadyRevenue(ToFalconAdType(format),
                        LuaAdRecord.MEDIATION_MAX, null));

                //phiên unit đơn: ad của id đơn SOMediationSetting đang Ready
                var single = FalconMaxService.Current?.SingleUnitReadyRevenue(ToFalconAdType(format)) ?? 0d;
                if (single > 0d) return single;
#endif
                var slots = format switch
                {
                    AdmobAdFormat.Interstitial => MultiCall.Instance.dictionaryInterstitial,
                    AdmobAdFormat.Rewarded => MultiCall.Instance.dictionaryRewarded,
                    _ => null
                };

                if (slots == null) return 0d;

                var best = 0d;
                foreach (var slot in slots)
                {
                    var revenue = slot.Value?.eCpm ?? 0d;
                    if (revenue > best) best = revenue;
                }

                return best;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return 0d;
            }
        }

        // BẬC của một ad unit MAX = MultiCall.AdTypeMultiCall.index — thứ tự các ad unit của format đó
        // sau khi MultiCall sắp theo multiplier tăng dần (0 = mức default, 1..n-1 = các mức floor).
        // Đây chính là "max1, max2, max3, max4" trong config bước C, nên nhóm ad unit AdMob của bậc k
        // luôn đi cùng đúng ad unit MAX thứ k.
        //
        // Ad unit MAX không nằm trong MultiCall (game chạy unit đơn của FalconMaxService) -> bậc 0.
        private static int ExternalTier(AdmobAdFormat format, string adUnitId)
        {
            try
            {
                if (string.IsNullOrEmpty(adUnitId)) return 0;

                var slots = format switch
                {
                    AdmobAdFormat.Interstitial => MultiCall.Instance.dictionaryInterstitial,
                    AdmobAdFormat.Rewarded => MultiCall.Instance.dictionaryRewarded,
                    _ => null
                };

                if (slots != null && slots.TryGetValue(adUnitId, out var slot) && slot != null)
                    return slot.index;

                return 0;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return 0;
            }
        }

        /// <summary>
        /// Tầng AdMob nhìn từ phía MultiCall lúc SO GIÁ ĐỂ SHOW: bước B + C của format tương ứng gộp
        /// lại thành một "nguồn ad phụ", cùng thang giá USD/impression với eCpm của MAX.
        ///
        /// Dùng <see cref="AdmobFullscreenAds.ShowBest"/> (KHÔNG phải <c>Show</c>) vì MultiCall đã tự
        /// bọc luồng FSN + log của nó; phần duy nhất phải tự bọc ở đây là
        /// <see cref="AdFlowGuard"/> — bộ đếm chặn App Open dùng CHUNG với tầng MAX.
        /// </summary>
        private sealed class AdmobSecondaryAdSource : ISecondaryAdSource
        {
            public bool IsReady(AdType type) => Layer(type)?.IsReady() ?? false;

            public double BestRevenue(AdType type)
                => Layer(type)?.BestAvailableRevenue ?? SecondaryAdSource.NO_AD;

            public string BestAdUnitId(AdType type) => Layer(type)?.BestAdUnitId;

            public string BestNetwork(AdType type) => Layer(type)?.BestNetwork;

            public string DescribeCache(AdType type)
            {
                var layer = Layer(type);
                return layer == null
                    ? "    (tầng AdMob chưa init — remote config không có ad unit nào)"
                    : layer.DescribeCache();
            }

            public bool Show(AdType type, string where, Action onReward, Action onHidden, Action onFail,
                SecondaryAdPaid onPaid)
            {
                var layer = Layer(type);
                if (layer == null) return false;

                AdFlowGuard.BeginAdFlow();
                _showingPaidType = type;
                _showingOnPaid = onPaid;

                // AdMob có thể bắn cả display-failed lẫn closed cho một lượt show -> chỉ đóng phiên
                // đúng một lần, nếu không bộ đếm tụt xuống âm và guard hết tác dụng.
                var ended = false;

                void EndAdFlow()
                {
                    if (ended) return;
                    ended = true;
                    AdFlowGuard.EndAdFlow();
                    if (_showingOnPaid == onPaid) _showingOnPaid = null;
                }

                var shown = layer.ShowBest(where,
                    onReward: onReward,
                    onHidden: () =>
                    {
                        EndAdFlow();
                        onHidden?.Invoke();
                    },
                    onFail: () =>
                    {
                        EndAdFlow();
                        onFail?.Invoke();
                    });

                // Hết ad đúng lúc gọi show: ShowBest KHÔNG bắn callback nào -> tự đóng phiên.
                if (!shown) EndAdFlow();

                return shown;
            }

            private static AdmobFullscreenAds Layer(AdType type) => type switch
            {
                AdType.Interstitial => AdmobMediation.Interstitial,
                AdType.Reward => AdmobMediation.Rewarded,
                _ => null
            };
        }

        private static AdType ToFalconAdType(AdmobAdFormat format) => format switch
        {
            AdmobAdFormat.Banner => AdType.Banner,
            AdmobAdFormat.Interstitial => AdType.Interstitial,
            AdmobAdFormat.Rewarded => AdType.Reward,
            _ => AdType.AppOpen
        };

        #endregion

        #region analytics

        // Đúng đường mà FalconMaxService đang đi cho MAX: Firebase + MMP + big data, cộng sổ client
        // TRƯỚC khi bắn impression (FAdApi.OnImpression đọc sổ để đóng typeCount/adLtv).
        //
        // KHÔNG bắn thêm FAdLog/ExtendAdLog thủ công: FalconBigDataController.Ad.OnImpression là cửa
        // impression DUY NHẤT, chạy song song hai đường là đếm đôi doanh thu ở DWH.
        private static AdType _showingPaidType;
        private static SecondaryAdPaid _showingOnPaid;

        private static void NotifyShowingPaid(AdType adType, string network, double revenue)
        {
            var onPaid = _showingOnPaid;
            if (onPaid == null || adType != _showingPaidType) return;
            _showingOnPaid = null;
            try
            {
                onPaid(_MEDIATION, network, revenue);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void ReportImpression(AdmobImpressionInfo info)
        {
            try
            {
                var adType = ToFalconAdType(info.format);
                var adFormat = adType.ToString();
                var adUnitId = string.IsNullOrEmpty(info.adUnitId) ? _UNKNOWN : info.adUnitId;
                //Firebase/MMP vẫn cần một chuỗi không rỗng -> giữ fallback tên format cho RIÊNG hai
                //đường đó. BigData thì KHÔNG (xem chỗ dựng AdParam bên dưới).
                var adWhereForMmp = string.IsNullOrEmpty(info.where) ? adFormat : info.where;
                var network = string.IsNullOrEmpty(info.network)
                    ? AdmobRevenueReporter.AdMobSourceName
                    : info.network;

                //log firebase
                FalconFirebaseLog.LogImpression(_AD_PLATFORM, network, adUnitId, adFormat, info.revenue);

                //log mmp (AppsFlyer / Adjust)
                FalconMmpLog.LogRevenue(network, adFormat, info.revenue, adUnitId, adWhereForMmp);
                MarkRevenueSentToMmp();

                //log to taichi/bamboo
                LogBambooTaichi.Instance.Log(info.revenue, adType);

                //log to client data (sổ LTV) — phải cộng TRƯỚC OnImpression
                AddClientAdInfo(adType, info.revenue);

                NotifyShowingPaid(adType, network, info.revenue);

                //log to bigdata
                var param = new AdParam
                {
                    type = adType,
                    adMediation = _MEDIATION,
                    adNetwork = network,
                    adUnitId = adUnitId,
                    //format THÔ của AdMob, KHÁC vai `type` (enum chuẩn của SDK): giữ cả hai để
                    //server đối chiếu khi ToFalconAdType map lệch. Trước đây gán adType.ToString()
                    //tức là chép lại chính `type` -> mất hẳn tác dụng đối chiếu.
                    adFormatRaw = info.format.ToString(),
                    adPrecision = info.precision,
                    adRev = info.revenue,
                    adCountry = CountryCode(),
                    currentLevel = AdmobMediationHooks.Level(),
                    //chỉ cho Databuckets (ecpm_floor / estimated_ecpm), không lên f_sdk_ads_data
                    ecpmFloor = info.floorEcpmUsd,
                    estimatedEcpm = info.estimatedEcpmUsd
                };
                //Chưa biết chỗ chiếu thì KHÔNG gán gì cả — giống FalconMaxService.AdLog:
                //AdParam khởi tạo sẵn adWhere = "Unknown" và decorator còn đọc ad-view cache thêm
                //một nhịp nữa lúc gửi (mốc nào TỰ NHẬP thì thắng, bỏ trống mới lấy từ cache).
                //Bịa tên format vào đây là ĐÈ MẤT chỗ chiếu thật đã khai ở OnShown; còn gán null
                //là ăn Debug.LogError của CorrectValues() mà kết quả vẫn về đúng "Unknown" đó.
                if (!string.IsNullOrWhiteSpace(info.where)) param.adWhere = info.where;
                FalconBigDataController.Ad.OnImpression(param);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        // === Vòng đời MỘT LẦN XEM AD ===
        // Đúng bộ mốc mà tầng MAX bắn (MultiCallMaxProviderParent + FalconMaxService):
        //   OnRequested -> OnShown -> OnImpression -> OnClosed, cộng OnLoadResult cho telemetry fill.
        // Nhánh hỏng: OnShowFailed thay chỗ OnImpression/OnClosed — cùng chỗ FalconMaxService gọi ở
        // OnInterstitialAdFailedToDisplayEvent / OnRewardedAdFailedToDisplayEvent.
        //
        // MỌI mốc đều gửi adMediation = "Admob" (_MEDIATION), KHÔNG phải "Max" như tầng MAX.

        //Qua VAN: tầng này bắn LoadAd cho TỪNG floor + retry backoff, mà hợp đồng chỉ cho MỘT mốc
        //request cho mỗi ĐỢT. Gọi thẳng là phồng mẫu số request và tráo adViewId giữa các floor —
        //xem AdViewRequestGate.
        private static void ReportAdRequested(AdmobAdFormat format)
            => AdViewRequestGate.Open(ToFalconAdType(format), _MEDIATION, AdViewRequestGate.ALGORITHM_META_ADMOB);

        //Chỗ chiếu của LƯỢT SHOW đang mở, theo từng format — chỉ ghi ở mốc shown, giữ tới lượt show
        //kế tiếp. Cần vì hook onAdClosed KHÔNG mang where, mà bỏ trống thì Ad.OnClosed rơi về
        //ad-view cache của BigData: cache đó chỉ có MỘT slot cho mỗi AdType và bị xoá trắng ở mọi
        //OnRequested. Van (AdViewRequestGate) đã chặn phần lớn ca đó, nhưng đợt load MỚI mở ngay
        //sau khi ad đóng vẫn xoá slot trước khi hook close chạy -> vẫn phải tự giữ where ở đây.
        private static readonly Dictionary<AdmobAdFormat, string> _showWhereByFormat = new();

        //kèm adUnitId như mốc shown của MAX / Lua / DFPool (AdShowParam)
        private static void ReportAdShown(AdmobAdFormat format, string where, string adUnitId)
        {
            _showWhereByFormat[format] = where;
            FalconBigDataController.Ad.OnShown(new AdShowParam
            {
                type = ToFalconAdType(format),
                adWhere = string.IsNullOrWhiteSpace(where) ? null : where,
                adMediation = _MEDIATION,
                adUnitId = string.IsNullOrEmpty(adUnitId) ? null : adUnitId
            });
        }

        // errorCode của AdMob, cùng vai với errorInfo.Code của MAX — vào nhãn SHOW_ERROR của view
        // đang mở, nên KHÔNG kèm mốc close nào. Unit/network/giá chỉ cho Databuckets.
        private static void ReportAdShowFailed(AdmobShowFailedInfo info)
        {
            var adType = ToFalconAdType(info.format);
            FalconBigDataController.Ad.OnShowFailed(adType,
                string.IsNullOrEmpty(info.errorCode) ? _UNKNOWN : info.errorCode,
                info.adUnitId, info.network, info.floorEcpmUsd, info.estimatedEcpmUsd);
            //show hỏng là view chết -> mở van cho đợt load bù.
            AdViewRequestGate.Close(adType);
        }

        private static void ReportAdClicked(AdmobAdFormat format)
            => FalconBigDataController.Ad.OnClicked(ToFalconAdType(format));

        private static void ReportAdClosed(AdmobAdFormat format, bool adCompleted)
        {
            //Chưa qua mốc shown nào thì để null, KHÔNG bịa _UNKNOWN: adWhere là RemoveIfNull nên
            //field tự vắng mặt, còn hơn dán nhãn "unknown" lên một lượt mà ta không biết gì.
            _showWhereByFormat.TryGetValue(format, out var where);
            var adType = ToFalconAdType(format);
            //Interstitial: null như MAX / multicall / Lua / DFPool (không có khái niệm "xem đủ"); Rewarded giữ "đã nhận thưởng"
            FalconBigDataController.Ad.OnClosed(adType,
                adCompleted: adType == AdType.Reward ? adCompleted : (bool?)null, adWhere: where);
            //view kết thúc -> mở van, đợt refill sau đó mới được mở view mới.
            AdViewRequestGate.Close(adType);
        }

        private static void ReportLoadResult(AdmobLoadResult result)
        {
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
            {
                adType = ToFalconAdType(result.format),
                adUnitId = result.adUnitId,
                success = result.success,
                adMediation = _MEDIATION,
                //AdMob biết CẢ HAI: tier = bậc trong config (cùng trục với MAX), floor = giá sàn
                //THẬT đã xin, USD/1000 impression. Đừng suy cái này ra cái kia.
                //bậc <= 0 (slot không đặt sàn) -> trống: vắng = KHÔNG đặt sàn (AdEcpmAndParams V2)
                tier = result.multiplier is > 0d ? result.multiplier : null,
                floor = result.floorEcpmUsd,
                //Databuckets: sàn THẬT đã xin — slot bước B không set sàn tức là sàn 0
                ecpmFloor = result.floorEcpmUsd ?? 0d,
                estimatedEcpm = result.estimatedEcpmUsd,
                networkName = result.network,
                //CODE chứ không phải Message: cụm load-result gộp theo
                //(unit × floor × network × kết cục) và giữ bản CUỐI làm đại diện, nên cần giá trị
                //NGẮN phân loại được. Message của AdMob dài cả đoạn + khác nhau từng máy -> không
                //group nổi. Fallback về message khi SDK không trả code (không mất dấu vết hẳn).
                errorMess = string.IsNullOrEmpty(result.errorCode) ? result.errorMessage : result.errorCode,
                loadingMs = result.loadingMs
            });
        }

        // Cùng sổ với FalconMaxService.AdLogClient — LTV/số lần xem theo format, chính là số mà log
        // impression đọc ra để đóng typeCount/adLtv.
        private static void AddClientAdInfo(AdType type, double revenue)
        {
            try
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
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        // Cùng cờ anti-fraud mà FalconMaxService.LogAdRevenue bật: đánh dấu user này đã có ít nhất một
        // doanh thu ad đẩy sang MMP.
        private static void MarkRevenueSentToMmp()
        {
            try
            {
                var data = GameData4AntiFraude.Instance;
                if (data.sendDataToAppsflyer) return;

                data.sendDataToAppsflyer = true;
                data.UpdateToServer();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        // adCountry là field RemoveIfNull -> MAX chưa init thì bỏ trống, không bịa giá trị.
        private static string CountryCode()
        {
#if MAX_ENABLE
            try
            {
                return MaxSdk.GetSdkConfiguration()?.CountryCode;
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }

        private static int CurrentLevel()
        {
            try
            {
                return LevelData.Instance.level;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        #endregion

        #region AdRequest extras

        /// <summary>Số ngày kể từ lúc cài app, đọc từ install_time của AppsFlyer.</summary>
        private static int? GetDaysSinceInstall()
        {
            try
            {
                var installTimeString = GameData4Appsflyer.Instance.installTime;

                string[] formats = { "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss" };

                var parsed = DateTime.TryParseExact(
                    installTimeString,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var installTimeUtc);

                if (!parsed)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogWarning($"Không parse được install_time: {installTimeString}");
#endif
                    return 1;
                }

                var elapsed = DateTime.UtcNow - installTimeUtc;
                return Math.Max(1, (int)Math.Floor(elapsed.TotalDays) + 1);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }

    /// <summary>
    /// Remote config bước C, lấy từ REMOTE CONFIG DATA4GAME (<see cref="IFalconConfig"/>) — khác đường
    /// với <c>MultiCallConfig</c> của MAX (đường CMS). Hai nguồn về độc lập nên luồng init phải chờ cả
    /// hai, xem <see cref="AdsConfigGate"/>.
    /// </summary>
    public class AdmobMetaAdsConfig : IFalconConfig
    {
        /// <summary>JSON thang giá bước C, xem <see cref="AdmobMetaAdsConfigsData"/> trong module.</summary>
        public string moMulAdmobMetaConfig;
    }

    public class AdmobMetaAdsConfigCms : IFalconConfigCms
    {
        /// <summary>JSON thang giá bước C, xem <see cref="AdmobMetaAdsConfigsData"/> trong module.</summary>
        public string moMulAdmobMetaConfig;
    }
}
