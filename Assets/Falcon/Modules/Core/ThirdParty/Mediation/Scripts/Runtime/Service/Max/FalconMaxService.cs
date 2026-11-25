/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-06
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Falcon.Helpers.Devkit;
using Falcon.Helpers.EventBus;
using Falcon.Modules.ActionLog.Runtime;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfigCms;
using Falcon.Modules.Core.ThirdParty.Firebase.Scripts.Runtime;
using Falcon.Modules.Core.ThirdParty.Mmp.Runtime;
using Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic;
using Falcon.Modules.Core.UI.Runtime;
using Falcon.Modules.Core.Utils.Time.Runtime;
using Falcon.Modules.Level.Core;
#if TIKTOK_ENABLE
using SDK;
#endif
#if MAX_ENABLE
using Falcon.Modules.Core.SaveLoad.Runtime;
#endif
#if AMAZON_ENABLE && !UNITY_EDITOR
using AmazonAds;
#endif
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public class FalconMaxService : IAdsService
    {
        private const string _INTERSTITIAL_DISPLAYED = "MEDIATION_INTERSTITIAL_DISPLAYED";
        private const string _REWARDED_DISPLAYED = "MEDIATION_REWARDED_DISPLAYED";
        private const string _INTERSTITIAL_CLOSED = "MEDIATION_INTERSTITIAL_CLOSED";
        private const string _REWARDED_CLOSED = "MEDIATION_REWARDED_CLOSED";
        private const string _MAX = "Max";
        private const string _LUA_ADMOB_IMPRESSION_EVENT = "meta_mediation_admob_impression";
        private const string _LUA_UPLIFT_REVENUE_PARAM = "uplift_revenue";

        /// <summary>
        /// Số lần load fail LIÊN TIẾP của một đợt thì coi là "bỏ cuộc cả đợt" → bắn nhãn
        /// <c>LOAD_ERROR</c> (<c>Ad.OnLoadFailed</c>), MỘT lần cho mỗi ad view.
        /// <br/>
        /// ⚠ MAX retry VÔ HẠN theo backoff nên không có mốc bỏ cuộc thật; 6 = lúc backoff
        /// <c>2^min(6, count)</c> chạm trần 64s, tức đợt này đã không fill nổi trong ~2 phút.
        /// Đây là ngưỡng do mediation TỰ ĐẶT — cần data team chốt trước khi tin số
        /// <c>fill rate = 1 − load_error/request</c>. Đổi ở đúng một chỗ này.
        /// </summary>
        private const int _LOAD_GIVE_UP_ATTEMPTS = 6;

        /// <summary>
        /// Chỗ chiếu của AppOpen — hằng, không phải placement gameplay: ad này bật khi người chơi
        /// quay lại app chứ không do màn chơi nào gọi. Giữ đúng chuỗi mà
        /// <c>MediationManager.AdsClosed</c> vẫn dùng để hai bên không lệch nhãn.
        /// </summary>
        private const string _APP_OPEN_WHERE = "AppOpen";

        //giá trị dự phòng cho ad_where, để không log null nếu callback về mà chưa qua hàm show nào
        private const string _PLACEMENT_UNKNOWN = "unknown";
        private string _placementIdBanner;

        //Chỗ chiếu của lượt xem ad hiện tại. LUẬT DUY NHẤT: gán ở hàm show, giữ nguyên tới lượt
        //show kế tiếp. KHÔNG reset ở mốc request — mọi log của một lượt (CSAdStart, impression,
        //CSAdFinish, FActionLog, close) đều đọc field này, và vòng đời show → closed có thể vắt qua
        //nhiều lần load của cùng format (multicall N ad unit, tầng AdMob xin lại nhóm floor, retry
        //backoff): reset ở request là xoá mất chỗ chiếu của lượt ĐANG chiếu.
        //
        //Bản trước từng reset ở request vì sợ MAX bắn paid event ngay lúc load — số liệu thống kê
        //09/09 đã bác bỏ: paid event chỉ về ở quãng show → closed. Đừng thêm lại reset đó.
        private string _placementIdRewarded;
        private string _placementIdInterstitial;

        private bool _isShowForRewarded;
        private bool _isShowingAds;
        private const string _KEY_FOR_SEGMENT_MAX = "key_for_segment_max";
        private const string _KEY_DECISION_POLICY = "decision_policy_load_ad";
        private const string _DECISION_POLICY_NO_MULTICALL = "client-nomulticall";
        private IFDeviceInfoRepository _deviceInfoRepository = MySingletonService.Instance<IFDeviceInfoRepository>();

        private bool _closedRewarded, _receivedRewarded;

        private SOMediationSetting _settings;

        private static bool? _isInitialized;

        private Action _onRewardedComplete;
        private Action _onRewardedFailed;
        private Action _onRewardedHidden;
        private Action _onInterstitialClosed;
        private Action _onInterstitialFailed;
        private DateTime _dateTimeTimeShow;

        private bool _useMultiCall;
        private float _timeBreak;

        private bool _callShowBanner;

        private bool _bannerIsLoaded;
        private bool _bannerIsShow;

        private DateTime _showAppOpenTime;

        private double _eCpmInterstitial;
        private double _eCpmRewarded;
        private AdReadyInfo _readyInterstitial;
        private AdReadyInfo _readyAppOpen;

        //Tại 1 thời điểm chỉ có đúng 1 quảng cáo fullscreen được show.
        //Cờ này bật khi bắt đầu 1 lượt show (và cả khi SDK báo displayed, phòng luồng show nào chưa gán),
        //rồi tắt ở callback hidden đầu tiên. Nhờ vậy hidden bắn lặp - do SDK gửi 2 lần hoặc do handler
        //bị đăng ký chồng - sẽ không gửi thêm CSAdFinish/action log nào nữa.
        private bool _hasShowSession;

        //Luồng load interstitial riêng (vd Floor Personalization / Databuckets Rule), cắm qua interface —
        //module chưa có / chưa cấu hình thì là bản rỗng, interstitial chạy logic mặc định.
        private static IInterstitialPersonalization _floorPersonalization => InterstitialPersonalization.Current;

        /// <summary>
        /// Callback interstitial của ad unit này đi theo nhánh "unit đơn" (FalconMaxService tự lo show →
        /// hidden): không multicall, HOẶC unit thuộc workflow personalization — multicall bỏ qua unit đó.
        /// </summary>
        //phiên này load/show inter + rewarded bằng LuaDynamic (chốt ở Init, xem LuaAdMonetize.LockMode)
        private bool _useLua;

        //phiên này interstitial chạy workflow Databuckets (chốt ở Init, chỉ khi không Lua)
        private bool _usePersonalization;

        private bool HandlesInterstitialUnit(string adUnitId) =>
            !_useMultiCall || _floorPersonalization.IsOwnedUnit(adUnitId);

        private string DescribeLoadMode()
        {
#if MAX_ENABLE
            if (_useLua)
                return $"LUA | algorithm={AdViewRequestGate.LuaAlgorithm(LuaAdMonetize.ScriptId)}" +
                       $" | ad unit từ sc_ad_bid_floor_v2: {LuaDynamic.AdBidFloorsV2.List.Count} id" +
                       $" (server đã gửi v2 phiên này: {LuaDynamic.AdBidFloorsV2.HasResponded})";
#endif
            var bidFloors = MultiCall.Instance.ListAdBidFloors.Count;
            var providerIndex = MultiCall.ProviderIndex();
            var prefix = _usePersonalization
                ? $"DATABUCKETS (inter: {_floorPersonalization.AdUnitIds.Count} unit workflow) | rewarded/banner/AOA + inter dự phòng: "
                : "";
            if (_useMultiCall)
                return prefix + $"MULTICALL | algorithm={AdViewRequestGate.MultiCallAlgorithm(providerIndex)}" +
                       $" | ad unit từ sc_ad_bid_floor: {bidFloors} id" +
                       $" (server đã gửi v1 phiên này: {MultiCall.Instance.getListAdBidFloorFromServer})";
            return prefix + $"UNIT ĐƠN | v1 có {bidFloors} id, mobile data={MultiCall.Instance.IsUseMobileData()}" +
                   $", máy yếu={MultiCall.Instance.IsLowEndDevice()}";
        }

        internal static FalconMaxService Current { get; private set; }

        /// <summary>
        /// MAX đã init xong (callback OnSdkInitialized: đã qua CallBeforeInit -> InitializeSdk -> CallAfterInit).
        /// MỌI API theo ad unit của MAX (load / show / is-ready / extra parameter...) chỉ được gọi khi true.
        /// </summary>
        private bool IsMaxInitialized => _isInitialized == true;

        public virtual void Init(Action actionFromUser = null)
        {
            MediationManager.Instance.DebugLog("Init max");
            if (_isInitialized.HasValue)
            {
                return;
            }

            Current = this;

            // MaxSdk.SetVerboseLogging(true);

            var settings = Resources.Load<SOMediationSetting>("SOMediationSetting");
            _settings = settings;

            //Chốt chế độ load cho CẢ PHIÊN, theo thứ tự ưu tiên:
            //1. LUA (sc_ad_monetize_lua_script): load/show inter + rewarded bằng Lua — KHÔNG Databuckets, KHÔNG
            //   MultiCall, KHÔNG id đơn của SOMediationSetting.
            //2. DATABUCKETS (remote config moMulFloorPersonalizationConfig, CMS rồi data4game): interstitial do
            //   workflow floor lo; MultiCall / id đơn vẫn chạy cho rewarded / banner / AOA và là đường dự phòng
            //   của interstitial khi Rule trả Control / API lỗi (bị chặn khi workflow đang chạy).
            //3. MULTICALL (sc_ad_bid_floor) — 4. UNIT ĐƠN: như cũ.
#if MAX_ENABLE
            _useLua = LuaAdMonetize.LockMode();
#endif
            //phiên Lua KHÔNG gọi TryActivate -> module personalization tắt cả phiên
            _usePersonalization = !_useLua && _floorPersonalization.TryActivate();
            _useMultiCall = !_useLua && MultiCall.Instance.ListAdBidFloors.Count > 0 &&
                            !MultiCall.Instance.IsUseMobileData() && !MultiCall.Instance.IsLowEndDevice();
            MediationManager.Instance.DebugLog($"MAX chế độ load: {DescribeLoadMode()}");
            // if (_useMultiCall)
            // {
            //     MultiCall.Instance.ProcessConfigFromServer(MediationType.Max);
            // }
            // else
            // {
            //     MediationManager.Instance.DebugLog("isLowEnd Device : " + MultiCall.Instance.IsLowEndDevice());
            // }

            var idBanner = _settings.bannerIdMaxAndroid;
#if UNITY_IOS
            idBanner = _settings.bannerIdMaxIOS;
#endif
            if (!string.IsNullOrWhiteSpace(idBanner))
            {
                if (_useMultiCall)
                {
                    MultiCall.Instance.SetIdBanner(idBanner);
                }
            }

            var idAppOpen = _settings.AppOpenIdMaxAndroid;
#if UNITY_IOS
            idAppOpen = _settings.AppOpenIdMaxIOS;
#endif
            if (!string.IsNullOrWhiteSpace(idAppOpen))
            {
                if (_useMultiCall)
                {
                    MultiCall.Instance.SetIdAppOpen(idAppOpen);
                }
            }

            if (_useMultiCall)
            {
                MultiCall.Instance.SetIdInterstitialRewarded();
                MultiCall.Instance.ProcessAdUnitId(MediationType.Max);
                //unit của workflow cũng cần có trong tham số init + disable_b2b/disable_auto_retries
                MultiCall.Instance.AddExtraInterstitialIds(_floorPersonalization.AdUnitIds);
            }

            GameEvent<object>.Register(MediationManager.EVENT_START_PURCHASE, Action, null);

            void Action(object obj)
            {
                _isShowingAds = true;
            }

            _timeBreak = FConfigControllerCms.Instance.Config<MediationConfig>().timeBreak;

            _isInitialized = false;

            var appIdAmazon = settings.appIdAmazonMaxAndroid;
#if UNITY_IOS
            appIdAmazon = settings.appIdAmazonMaxIOS;
#endif
#if AMAZON_ENABLE && !UNITY_EDITOR
            AmazonAds.Amazon.Initialize(appIdAmazon);
#if UNITY_IOS
            AmazonAds.Amazon.SetAPSPublisherExtendedIdFeatureEnabled(true);
#endif
#endif

#if MAX_ENABLE
            MaxSdkCallbacks.OnSdkInitializedEvent += sdkConfiguration =>
            {
                //CallAfterInit TRƯỚC khi mở cờ init: mọi API theo ad unit (IsMaxInitialized) chỉ chạy sau 3 bước
                if (_useMultiCall)
                {
                    MultiCall.Instance.CallAfterInit();
                }
                else
                {
                    DisableAutoRetriesForPreInitUnits();
                }

                _isInitialized = true;
                MaxPreInitAdUnits.MarkAfterInitDone();

                //AdMob luôn init SAU khi MAX init thành công: phiên Lua do LuaAdMonetize, phiên MultiCall do bridge
                if (_useLua) LuaAdMonetize.OnMaxSdkInitialized();
                else Falcon.Admob.AdmobMediationFalconBridge.NotifyMaxInitialized();

                if (_settings.UseAppOpenMax && !IsRemoveAds())
                {
                    RegisterAppOpenEvents();
                    ShowAppOpenAdIfReady();
                }

                if (_settings.UseMrecMax)
                {
                    RegisterMrectEvents();
                }

                if (_settings.UseBannerMax)
                {
                    CreateBannerView();
                    // ShowBanner();
                    HideBanner(true);
                    RegisterBannerEvents();
                }

                if (_settings.UseInterstitialMax)
                {
                    //TRƯỚC lượt load mặc định đầu tiên (cả nhánh multicall ở OnMediationInitCompleted):
                    //lượt chờ First Ad Rule phải kịp chặn nó
                    _floorPersonalization.OnMaxInitialized(ResumeDefaultInterstitialLoading);
                    if (!_useMultiCall && !_useLua)
                        LoadInterstitial();
                    RegisterInterstitialEvents();
                }

                if (_settings.UseRewardedMax)
                {
                    if (!_useMultiCall && !_useLua)
                        LoadRewardedAd();
                    RegisterRewardedEvent();
                }

                actionFromUser?.Invoke();
                if (_useMultiCall)
                    MultiCall.Instance.OnMediationInitCompleted();
            };
            var userId = _deviceInfoRepository.DeviceId;
            MaxSdk.SetUserId(userId);
            var campaignId = GameData4Appsflyer.Instance.campaignID;
            if (!string.IsNullOrEmpty(campaignId))
            {
                var old = SaveLoadHandler.Load(_KEY_FOR_SEGMENT_MAX, "");
                if (!old.Equals(campaignId))
                {
                    MaxSegmentCollection collection = MaxSegmentCollection.Builder()
                        .AddSegment(new MaxSegment(1,
                            new List<int> { EncodeStringToInt(campaignId) }))
                        .Build();
                    MaxSdk.SetSegmentCollection(collection);
                    SaveLoadHandler.Save(_KEY_FOR_SEGMENT_MAX, campaignId);
                }
            }

            EventStartInit();
#endif
        }

        //id đơn đã đưa vào disable_b2b + mảng init (phiên đơn có luồng chạy kèm) -> set disable_auto_retries sau init.
        //null = không truyền (phiên đơn đứng một mình, hoặc phiên MultiCall / Lua)
        private string _singleInterIdForRetry;
        private string _singleRewardedIdForRetry;

        private string SingleAdUnitId(bool enabled, bool rewarded)
        {
            if (!enabled || _settings == null) return null;
#if UNITY_IOS
            var id = rewarded ? _settings.rewardedIdMaxIOS : _settings.interstitialIdMaxIOS;
#else
            var id = rewarded ? _settings.rewardedIdMaxAndroid : _settings.interstitialIdMaxAndroid;
#endif
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        private IEnumerable<string> NonLuaPlatformAdUnitIds()
        {
#if UNITY_IOS
            var ids = new[] { _settings.bannerIdMaxIOS, _settings.AppOpenIdMaxIOS };
#else
            var ids = new[] { _settings.bannerIdMaxAndroid, _settings.AppOpenIdMaxAndroid };
#endif
            return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim());
        }

        private void EventStartInit()
        {
            MediationManager.Instance.DebugLog("Max Initializing.");
#if MAX_ENABLE
            //unit MAX của module ngoài (vd slot DFPool): chốt danh sách NGAY trước init -> cả 3 chế độ dưới đây
            //đều đưa chúng vào disable_b2b_ad_unit_ids + mảng init, và set disable_auto_retries sau init
            //(xem DisableAutoRetriesForPreInitUnits). Module ngoài chỉ được load id đã chốt ở đây.
            MaxPreInitAdUnits.Lock();
            var preInitIds = MaxPreInitAdUnits.All.ToArray();
            if (preInitIds.Length > 0)
                MediationManager.Instance.DebugLog("CallBeforeInit : unit module ngoài (DFPool...) - " +
                                                   string.Join(",", preInitIds));

            if (_useMultiCall)
            {
                MultiCall.Instance.AddExtraInterstitialIds(MaxPreInitAdUnits.Interstitial);
                MultiCall.Instance.AddExtraRewardedIds(MaxPreInitAdUnits.Rewarded);
                MultiCall.Instance.CallBeforeInit();
                MaxSdk.InitializeSdk(MultiCall.Instance.GetInitParams());
            }
            else if (_useLua)
            {
                //script đã chạy OnInit trước lúc init -> biết sẵn các unit MAX nó sẽ load. Lua tự quyết lượt
                //load/retry -> tắt back-to-back load của MAX cho các unit đó (+ slot module ngoài, vd DFPool).
                //Phiên Lua không có workflow Databuckets.
                var noB2B = LuaAdMonetize.MaxAdUnitIds().Union(preInitIds).ToArray();
                if (noB2B.Length > 0)
                    MaxSdk.SetExtraParameter("disable_b2b_ad_unit_ids", string.Join(",", noB2B));
                MediationManager.Instance.DebugLog("CallBeforeInit (LUA) : disable_b2b_ad_unit_ids - " +
                                                   string.Join(",", noB2B));
                var initIds = noB2B.Union(NonLuaPlatformAdUnitIds()).ToArray();
                MediationManager.Instance.DebugLog("CallBeforeInit (LUA) : init ad unit ids - " +
                                                   string.Join(",", initIds));
                MaxSdk.InitializeSdk(initIds.Length > 0 ? initIds : null);
            }
            else
            {
                //workflow / module ngoài tự quyết lượt load/retry -> tắt back-to-back load của MAX cho unit của nó
                var floorIds = _floorPersonalization.IsEnabled
                    ? _floorPersonalization.AdUnitIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray()
                    : Array.Empty<string>();
                var companions = floorIds.Union(preInitIds).ToArray();

                if (companions.Length > 0)
                {
                    //CÓ luồng chạy kèm (FloorPersonalization / DFPool) -> id inter + rewarded đơn của SOMediationSetting
                    //cũng đi đủ 3 bước như unit của luồng kèm: disable_b2b (ở đây), mảng init (ở đây),
                    //disable_auto_retries (sau init, DisableAutoRetriesForPreInitUnits). Banner / AppOpen chỉ vào mảng
                    //init, như MultiCall / Lua.
                    _singleInterIdForRetry = SingleAdUnitId(_settings.UseInterstitialMax, false);
                    _singleRewardedIdForRetry = SingleAdUnitId(_settings.UseRewardedMax, true);
                    var singleIds = new[] { _singleInterIdForRetry, _singleRewardedIdForRetry }
                        .Where(id => id != null);
                    var noB2B = companions.Union(singleIds).ToArray();
                    MaxSdk.SetExtraParameter("disable_b2b_ad_unit_ids", string.Join(",", noB2B));
                    var initIds = noB2B.Union(NonLuaPlatformAdUnitIds()).ToArray();
                    MediationManager.Instance.DebugLog("CallBeforeInit (UNIT ĐƠN + luồng kèm) : disable_b2b_ad_unit_ids - " +
                                                       string.Join(",", noB2B) + " | init ad unit ids - " +
                                                       string.Join(",", initIds));
                    MaxSdk.InitializeSdk(initIds);
                }
                else
                {
                    //CHỈ có id đơn, không luồng nào chạy kèm -> không truyền gì vào cả 3 bước
                    MaxSdk.InitializeSdk();
                }
            }
#endif
        }

        /// <summary>
        /// Bước 3 ở phiên LUA / UNIT ĐƠN cho unit module ngoài (DFPool) + unit FloorPersonalization (khi bật) —
        /// phiên MultiCall đã gộp cả hai vào hash của <c>MultiCall.CallAfterInit</c>. Chạy trong callback MAX init,
        /// trước khi module ngoài thấy <c>MaxSdk.IsInitialized()</c> và trước lượt load đầu của FloorPersonalization
        /// (<c>OnMaxInitialized</c> chạy sau trong cùng callback).
        /// </summary>
        private void DisableAutoRetriesForPreInitUnits()
        {
#if MAX_ENABLE
            //id đơn: chỉ khi phiên đơn có luồng chạy kèm (đã đưa vào disable_b2b + mảng init ở EventStartInit)
            if (_singleInterIdForRetry != null)
            {
                MaxSdk.SetInterstitialExtraParameter(_singleInterIdForRetry, "disable_auto_retries", "true");
                MediationManager.Instance.DebugLog("CallAfterInit (unit đơn) interstitial: " + _singleInterIdForRetry +
                                                   " - disable_auto_retries");
            }

            if (_singleRewardedIdForRetry != null)
            {
                MaxSdk.SetRewardedAdExtraParameter(_singleRewardedIdForRetry, "disable_auto_retries", "true");
                MediationManager.Instance.DebugLog("CallAfterInit (unit đơn) rewarded: " + _singleRewardedIdForRetry +
                                                   " - disable_auto_retries");
            }

            if (_floorPersonalization.IsEnabled)
                foreach (var id in _floorPersonalization.AdUnitIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                {
                    MaxSdk.SetInterstitialExtraParameter(id, "disable_auto_retries", "true");
                    MediationManager.Instance.DebugLog("CallAfterInit (FloorPersonalization) interstitial: " + id +
                                                       " - disable_auto_retries");
                }

            foreach (var id in MaxPreInitAdUnits.Interstitial)
            {
                MaxSdk.SetInterstitialExtraParameter(id, "disable_auto_retries", "true");
                MediationManager.Instance.DebugLog("CallAfterInit (module ngoài) interstitial: " + id +
                                                   " - disable_auto_retries");
            }

            foreach (var id in MaxPreInitAdUnits.Rewarded)
            {
                MaxSdk.SetRewardedAdExtraParameter(id, "disable_auto_retries", "true");
                MediationManager.Instance.DebugLog("CallAfterInit (module ngoài) rewarded: " + id +
                                                   " - disable_auto_retries");
            }
#endif
        }

#if MAX_ENABLE
        private void RegisterMrectEvents()
        {
            MaxSdkCallbacks.MRec.OnAdRevenuePaidEvent += OnMrecAdRevenuePaidEvent;
        }

        private void OnMrecAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            OnAdRevenuePaidEvent(adUnitId, AdType.MREC, adInfo);
        }

        private void RegisterAppOpenEvents()
        {
            MaxSdkCallbacks.AppOpen.OnAdHiddenEvent += OnAppOpenDismissedEvent;
            MaxSdkCallbacks.AppOpen.OnAdRevenuePaidEvent += OnAppOpenAdRevenuePaidEvent;
            MaxSdkCallbacks.AppOpen.OnAdLoadedEvent += OnAppOpenLoadedEvent;
            MaxSdkCallbacks.AppOpen.OnAdLoadFailedEvent += OnAppOpenLoadFailedEvent;
            MaxEvent.Instance.applicationPauseEvent += OnApplicationPause;
        }

        private void OnAppOpenLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            MediationManager.Instance.DebugLog("Max > AppOpen load failed : " + errorInfo.Message);
            //AppOpen đi CÙNG bộ mốc với inter/rewarded (doc: "Rewarded/AppOpen y hệt, đổi AdType").
            //AppOpen là một unit đơn, không multicall -> KHÔNG đặt bậc nào -> tier để null.
            //floor (GIÁ SÀN THẬT, USD) cũng bỏ trống: MAX giữ số đó trên dashboard, client không biết.
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
            {
                adType = AdType.AppOpen,
                adUnitId = adUnitId,
                success = false,
                adMediation = _MAX,
                networkName = null,
                tier = null,
                errorMess = errorInfo.Code.ToString(),
                loadingMs = errorInfo.LatencyMillis
            });
        }

        private void OnAppOpenLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            _readyAppOpen = ReadyInfoOf(adUnitId, adInfo);
            MediationManager.Instance.DebugLog("Max > AppOpen loaded an ad");
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
            {
                adType = AdType.AppOpen,
                adUnitId = adUnitId,
                success = true,
                adMediation = _MAX,
                networkName = adInfo.NetworkName,
                tier = null,
                estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue),
                loadingMs = adInfo.LatencyMillis
            });
        }

        private void OnAppOpenAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            OnAdRevenuePaidEvent(adUnitId, AdType.AppOpen, adInfo);
        }

        public int EncodeStringToInt(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                // Lấy 4 byte đầu để tạo int
                return BitConverter.ToInt32(hashBytes, 0);
            }
        }

        private void OnAdRevenuePaidEvent(string adUnit, AdType type, MaxSdkBase.AdInfo adInfo)
        {
            //log to client data
            AdLogClient(adInfo, type);
            //log to bigdata
            AdLog(adInfo, type);
            if (HandlesInterstitialUnit(adUnit))
            {
                //log to adInfo
                AdLogInfo(adInfo, type);
            }

            //log to taichi/bamboo
            LogBambooTaichi.Instance.Log(adInfo.Revenue, type);

            LogRevenueToPartners(adUnit, type, adInfo);
        }

        internal void LogExternalMaxRevenue(MaxSdkBase.AdInfo adInfo, AdType type)
        {
            AdLogClient(adInfo, type);
            LogBambooTaichi.Instance.Log(adInfo.Revenue, type);
            LogRevenueToPartners(adInfo.AdUnitIdentifier, type, adInfo);
        }

        private void LogRevenueToPartners(string adUnit, AdType type, MaxSdkBase.AdInfo adInfo)
        {
            var adRevenue = new AdRevenueInfo
            {
                adUnit = adUnit,
                network = adInfo.NetworkName,
                format = adInfo.AdFormat,
                revenue = adInfo.Revenue,
                placement = adInfo.Placement
            };

            //user chơi lần đầu (session đầu tiên) + quảng cáo đầu tiên --> hoãn log firebase/mmp,
            //để log cùng lúc với khi CSAdFinish được gửi
            if (!_usedDeferFirstAdRevenue && IsFirstAdOfFirstSession(type))
            {
                _usedDeferFirstAdRevenue = true;
                _pendingAdRevenue = adRevenue;
                MediationManager.Instance.DebugLog("MAX > delay log firebase/mmp of first ad until CSAdFinish");
                return;
            }

            //đề phòng quảng cáo đầu tiên không nhận được sự kiện đóng --> gửi nốt log đang chờ
            FlushPendingAdRevenue();
            LogAdRevenue(adRevenue);
#if TIKTOK_ENABLE
            LogTikTok(adInfo);
#endif
        }

#if TIKTOK_ENABLE
        private void LogTikTok(MaxSdkBase.AdInfo adInfo)
        {
            //chưa điền TikTokSettings -> SDK không init -> gửi event chỉ tốn công. Hỏi IsInitialized
            //chứ không phải HasConfig: có id mà init lỗi thì vẫn không gửi được.
            if (!TikTokSettings.IsInitialized) return;

            double revenue = adInfo.Revenue;

            // Miscellaneous data
            string countryCode = MaxSdk.GetSdkConfiguration().CountryCode;
            string networkName = adInfo.NetworkName;
            string adUnitIdentifier = adInfo.AdUnitIdentifier;
            string placement = adInfo.Placement;
            string networkPlacement = adInfo.NetworkPlacement;
            string format = adInfo.AdFormat;

            Dictionary<string, object> adRevenue = new Dictionary<string, object>();
            adRevenue.Add("device_ad_mediation_platform", "applovin_max_sdk");
            adRevenue.Add("revenue", revenue);
            adRevenue.Add("country_code", countryCode);
            adRevenue.Add("network_name", networkName);
            adRevenue.Add("ad_format", format);
            adRevenue.Add("ad_unit_id", adUnitIdentifier);
            adRevenue.Add("placement", placement);
            adRevenue.Add("network_placement", networkPlacement);

            // Make sure the App Events SDK has been initialized before calling this
            //eventId là khoá CHỐNG TRÙNG của TikTok: cùng một id gửi lại chỉ được tính một lần, nên
            //phải RIÊNG cho từng impression. Dùng _idAd (guid của lượt show hiện tại) khi có, chưa có
            //thì sinh guid mới.
            string eventId = string.IsNullOrEmpty(_idAd) ? Guid.NewGuid().ToString() : _idAd;
            TikTokAdRevenueEvent adRevenueEvent = new TikTokAdRevenueEvent(adRevenue, eventId);
            TikTokBusinessSDK.TrackTTEvent(adRevenueEvent);
        }
#endif

        //quảng cáo đầu tiên của user chơi lần đầu (chỉ hoãn với loại quảng cáo có gửi CSAdFinish)
        private bool _usedDeferFirstAdRevenue;
        private AdRevenueInfo _pendingAdRevenue;

        private class AdRevenueInfo
        {
            public string adUnit;
            public string network;
            public string format;
            public double revenue;
            public string placement;
        }

        private static bool IsFirstAdOfFirstSession(AdType type)
        {
            if (type != AdType.Interstitial && type != AdType.Reward) return false;
            return FPlayerInfoService.Instance.Session.SessionId == 1;
        }

        //log firebase/mmp của quảng cáo đầu tiên, gọi cùng lúc với khi gửi CSAdFinish
        private void FlushPendingAdRevenue()
        {
            if (_pendingAdRevenue == null) return;
            var adRevenue = _pendingAdRevenue;
            _pendingAdRevenue = null;
            LogAdRevenue(adRevenue);
        }

        private void LogAdRevenue(AdRevenueInfo adRevenue)
        {
            //log firebase
            FalconFirebaseLog.LogImpression(_MAX, adRevenue.network, adRevenue.adUnit, adRevenue.format,
                adRevenue.revenue);
            //log mmp
            FalconMmpLog.LogRevenue(adRevenue.network, adRevenue.format, adRevenue.revenue, adRevenue.adUnit,
                adRevenue.placement);
            if (!GameData4AntiFraude.Instance.sendDataToAppsflyer)
            {
                GameData4AntiFraude.Instance.sendDataToAppsflyer = true;
                GameData4AntiFraude.Instance.UpdateToServer();
            }
        }

        private void AdLogInfo(MaxSdkBase.AdInfo adInfo, AdType type)
        {
            double timeShow = 0;
            if (type == AdType.Interstitial || type == AdType.Reward)
            {
                timeShow = (DateTime.Now - _dateTimeTimeShow).TotalSeconds;
            }

            new CSAdInfo(_idAd, Placement(ImpressionPlacement(type)), type.ToString(), adInfo.NetworkName, _MAX,
                    (int)timeShow, adInfo.Revenue)
                .Send();
        }

        private void AdLogClient(MaxSdkBase.AdInfo adInfo, AdType type)
        {
            if (GameData4AdInfo.Instance.adTypeToInfo == null)
            {
                GameData4AdInfo.Instance.adTypeToInfo = new Dictionary<AdType, AdInfo>();
            }

            if (GameData4AdInfo.Instance.adTypeToInfo.ContainsKey(type))
            {
                GameData4AdInfo.Instance.adTypeToInfo[type].ltv += adInfo.Revenue;
                GameData4AdInfo.Instance.adTypeToInfo[type].count++;
            }
            else
            {
                GameData4AdInfo.Instance.adTypeToInfo.Add(type, new AdInfo { count = 1, ltv = adInfo.Revenue });
            }

            GameData4AdInfo.Instance.UpdateToServer();
        }

        private void AdLog(MaxSdkBase.AdInfo impressionData, AdType type)
        {
            var dict = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(impressionData.CreativeIdentifier))
            {
                dict.Add("creative_id", impressionData.CreativeIdentifier);
            }
            if (!string.IsNullOrEmpty(impressionData.DspName))
            {
                dict.Add("dsp", impressionData.DspName);
            }

            int? waterfallPosition = null;
            var responses = impressionData.WaterfallInfo?.NetworkResponses;
            if (responses != null)
                for (int i = 0; i < responses.Count; i++)
                    if (responses[i].AdLoadState == MaxSdkBase.MaxAdLoadState.AdLoaded)
                    {
                        waterfallPosition = i + 1;
                        break;
                    }
            var param = new AdParam()
            {
                adCountry = MaxSdk.GetSdkConfiguration().CountryCode,
                adFormatRaw = impressionData.AdFormat,
                adInstanceName = impressionData.NetworkPlacement,
                adMediation = "Max",
                adNetwork = impressionData.NetworkName,
                adPrecision = impressionData.RevenuePrecision,
                adRev = impressionData.Revenue,
                adUnitId = impressionData.AdUnitIdentifier,
                currentLevel = LevelData.Instance.level,
                extraMeta = dict,
                type = type,
                waterfallPosition = waterfallPosition,
                //Chỉ cho Databuckets: Revenue của impression MAX chính là giá ước tính đã trả lúc load
                ecpmFloor = MultiCallLoadedFloor(impressionData.AdUnitIdentifier),
                estimatedEcpm = MultiCall.ToEcpm(impressionData.Revenue)
            };
            //Chưa quy chiếu được chỗ chiếu thì KHÔNG gán gì cả: AdParam khởi tạo sẵn adWhere = "Unknown",
            //và AdLogService.Decor còn đọc ad-view cache thêm một lần nữa lúc gửi (muộn hơn ở đây một
            //nhịp) — mốc nào tự nhập thì thắng, bỏ trống mới lấy từ cache. Gán null vào đây là ăn
            //Debug.LogError của CorrectValues() mà kết quả vẫn về đúng "Unknown" đó.
            var placementId = ImpressionPlacement(type);
            if (!string.IsNullOrWhiteSpace(placementId)) param.adWhere = placementId;
            FalconBigDataController.Ad.OnImpression(param);
        }

        /// <summary>
        /// Giá sàn eCPM của request ra ad đang show — chỉ multicall mới tự đặt sàn (jC7Fp) nên mới biết.
        /// Không multicall thì sàn nằm trên dashboard MAX, client không biết -> null.
        /// </summary>
        private double? MultiCallLoadedFloor(string adUnitId)
        {
            //unit của workflow personalization: floor của step đã load ra candidate đang chiếu
            var candidate = _floorPersonalization.ShownCandidate(adUnitId);
            if (candidate != null) return candidate.ecpmFloor;
            if (!_useMultiCall || string.IsNullOrEmpty(adUnitId)) return null;
            if (MultiCall.Instance.dictionaryInterstitial.TryGetValue(adUnitId, out var inter)) return inter.loadedFloor;
            if (MultiCall.Instance.dictionaryRewarded.TryGetValue(adUnitId, out var reward)) return reward.loadedFloor;
            return null;
        }

        /// <summary>
        /// Mốc show hỏng của BigData — kèm unit/network/giá của ad định chiếu, chỉ cho Databuckets
        /// (ad_impression is_show = 0), không lên BigData.
        /// </summary>
        private void ReportShowFailed(AdType type, string adUnitId, MaxSdkBase.ErrorInfo errorInfo,
            MaxSdkBase.AdInfo adInfo)
        {
            FalconBigDataController.Ad.OnShowFailed(type, errorInfo.Code.ToString(), adUnitId,
                adInfo?.NetworkName, MultiCallLoadedFloor(adUnitId),
                adInfo == null ? null : MultiCall.ToEcpm(adInfo.Revenue));
        }

        private void OnAppOpenDismissedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            var id = _settings.AppOpenIdMaxAndroid;
#if UNITY_IOS
            id = _settings.AppOpenIdMaxIOS;
#endif
            //adCompleted để TRỐNG: AppOpen không có callback đo "xem hết", đừng đoán.
            FalconBigDataController.Ad.OnClosed(AdType.AppOpen, adWhere: _APP_OPEN_WHERE);
            AdViewRequestGate.Close(AdType.AppOpen);
            //refill ngay sau khi đóng = đợt MỚI -> mở view mới qua van.
            AdViewRequestGate.Open(AdType.AppOpen, _MAX, AdViewRequestGate.ALGORITHM_SINGLE);
            MaxSdk.LoadAppOpenAd(id);
            MediationManager.Instance.AdsClosed(AdType.AppOpen, _APP_OPEN_WHERE);
        }

        public void OnApplicationPause(bool pauseStatus)
        {
            if (_settings.UseAppOpenMax && !pauseStatus)
            {
                MaxEvent.Instance.StartCoroutine(Wait02S());
            }
        }

        private static AdReadyInfo ReadyInfoOf(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            return new AdReadyInfo
            {
                adNetwork = adInfo?.NetworkName,
                adUnitId = adUnitId,
                estimatedEcpm = adInfo == null ? null : MultiCall.ToEcpm(adInfo.Revenue)
            };
        }

#endif

        public AdReadyInfo ReadyInterstitialInfo()
        {
            //MAX chưa init: không hỏi SDK theo ad unit (PeekInterstitial / BestMaxRevenue gọi Is...Ready)
            if (!IsMaxInitialized) return null;
#if MAX_ENABLE
            //cùng thứ tự chọn với ShowInterstitial (PickInterstitial)
            switch (PickInterstitial(null, out _, log: false))
            {
                //phiên Lua không TryActivate personalization -> pool rỗng, không bao giờ ra FloorCandidate
                case ShowPick.FloorCandidate:
                    return ReadyInfoOf(_floorPersonalization.PeekBestCandidate());
                case ShowPick.Primary when _useMultiCall:
                case ShowPick.Secondary when _useMultiCall:
                    return MultiCall.Instance.PeekInterstitial();
                case ShowPick.Secondary:
                    return SecondaryReadyInfo(AdType.Interstitial);
                case ShowPick.Primary when _useLua:
                    var lua = LuaAdMonetize.PeekBest(AdType.Interstitial, false);
                    return lua != null ? ReadyInfoOf(lua) : null;
                case ShowPick.Primary:
                    return _readyInterstitial;
                default:
                    return null;
            }
#else
            var candidate = _floorPersonalization.PeekBestCandidate();
            if (candidate != null) return ReadyInfoOf(candidate);
            if (_useMultiCall) return MultiCall.Instance.PeekInterstitial();
            return _readyInterstitial;
#endif
        }

        public AdReadyInfo ReadyAppOpenInfo() => _readyAppOpen;

        private static AdReadyInfo ReadyInfoOf(InterstitialAdCandidate candidate)
        {
            if (candidate == null) return null;
            return new AdReadyInfo
            {
                adNetwork = candidate.networkName,
                adUnitId = candidate.adUnitId,
                estimatedEcpm = candidate.estimatedEcpm
            };
        }

#if MAX_ENABLE
        private static AdReadyInfo ReadyInfoOf(LuaAdRecord record)
        {
            return new AdReadyInfo
            {
                adMediation = record.mediation,
                adNetwork = record.networkName,
                adUnitId = record.adUnitId,
                estimatedEcpm = record.eCpm
            };
        }
#endif

        private IEnumerator Wait02S()
        {
            yield return new WaitForSeconds(0.2f);
            if (_isShowingAds)
            {
                _isShowingAds = false;
                yield break;
            }

            var adAvailable = false;
#if MAX_ENABLE
            var id = _settings.AppOpenIdMaxAndroid;
#if UNITY_IOS
            id = _settings.AppOpenIdMaxIOS;
#endif
            adAvailable = MaxSdk.IsAppOpenAdReady(id);
#endif
            var readyAd = adAvailable ? MediationManager.Instance.GetReadyAppOpenInfo() : null;

            if (IsRemoveAds())
            {
                FalconBigDataController.Ad.OnShowSkipped(AdType.AppOpen, _APP_OPEN_WHERE, AdSkipReason.NoAds,
                    adAvailable: adAvailable, readyAd: readyAd);
                yield break;
            }

            var skipReason = MediationManager.Instance.GetAppOpenSkipReason();
            if (skipReason == null)
            {
                ShowAppOpenAdIfReady();
            }
            else
            {
                FalconBigDataController.Ad.OnShowSkipped(AdType.AppOpen, _APP_OPEN_WHERE, skipReason.Value,
                    adAvailable: adAvailable, readyAd: readyAd);
            }
        }

        public AdSkipReason? AppOpenSkipReason()
        {
            if (!IsOverTimeCoolDown())
            {
                MediationManager.Instance.DebugLog("Is over time cool down");
                return AdSkipReason.Cooldown;
            }

            if (_settings.minLevelShowAoaMax > LevelData.Instance.level)
            {
                MediationManager.Instance.DebugLog("level < min level for show");
                return AdSkipReason.LevelGate;
            }

            return null;
        }

        public bool InternalCanShowAppOpen(string placementId)
        {
            return AppOpenSkipReason() == null;
        }

        private bool IsOverTimeCoolDown()
        {
            return (DateTime.Now - _showAppOpenTime).TotalSeconds >= _settings.cooldownTimeMax;
        }

        //không show app open ở lần tiếp resume theo
        public void IgnoreNextAoa()
        {
            _isShowingAds = true;
        }

        public void ShowAppOpenAdIfReady()
        {
            if (IsRemoveAds())
            {
                MediationManager.Instance.DebugLog("MAX > remove Ads don't show app open");
                return;
            }

            //MAX chưa init: không gọi IsAppOpenAdReady / LoadAppOpenAd
            if (!IsMaxInitialized) return;

            var id = _settings.AppOpenIdMaxAndroid;
#if UNITY_IOS
            id = _settings.AppOpenIdMaxIOS;
#endif
#if MAX_ENABLE
            //MỐC CẦU: "muốn chiếu AppOpen" — bắn TRƯỚC khi hỏi kho, cả hai nhánh, để ca kho rỗng
            //cũng có dòng. Nhánh IsRemoveAds() ở đầu hàm đã thoát trước và KHÔNG đi qua đây: user
            //gỡ quảng cáo thì không phải "muốn chiếu mà hụt ad".
            FalconBigDataController.Ad.OnShowAttempt(
                AdType.AppOpen, adWhere: _APP_OPEN_WHERE,
                adAvailable: MaxSdk.IsAppOpenAdReady(id), adMediation: _MAX);

            if (MaxSdk.IsAppOpenAdReady(id))
            {
                MediationManager.Instance.DebugLog("MAX > show open app");
                //adWhere của AppOpen là hạ tầng chứ không phải placement gameplay: ad này bật khi
                //người chơi quay lại app, không có "chỗ chiếu" nào để game khai.
                FalconBigDataController.Ad.OnShown(new AdShowParam
                    { type = AdType.AppOpen, adWhere = _APP_OPEN_WHERE, adMediation = _MAX, adUnitId = id });
                MaxSdk.ShowAppOpenAd(id);
                _showAppOpenTime = DateTime.Now;
            }
            else
            {
                MediationManager.Instance.DebugLog("MAX > load open app");
                AdViewRequestGate.Open(AdType.AppOpen, _MAX, AdViewRequestGate.ALGORITHM_SINGLE);
                MaxSdk.LoadAppOpenAd(id);
            }
#endif
        }

        #region Intertitial

        private int _countAttemptInterstitial = 0;

        //ecpmFloorType của lần load inter unit đơn đang bay (vd rule_error) — chụp lúc GỬI, dòng kết quả đọc lại
        private string _requestFloorTypeInterstitial;

        private void RegisterInterstitialEvents()
        {
#if MAX_ENABLE
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent += OnInterstitialDisplayedEvent;
            MaxSdkCallbacks.Interstitial.OnAdClickedEvent += OnInterstitialClickedEvent;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHiddenEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdFailedToDisplayEvent;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += OnInterstitialAdRevenuePaidEvent;
#endif
        }

#if MAX_ENABLE
        private void OnInterstitialAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            OnAdRevenuePaidEvent(adUnitId, AdType.Interstitial, adInfo);
        }

        private void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            // MediationManager.Instance.DebugLog("MAX > Loaded event intertitial : " + adUnitId);
            _eCpmInterstitial = adInfo.Revenue;
            FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_LOAD_SUCCESS);
            if (_useMultiCall) return; //multi call đã xử lý
            //unit của workflow personalization tự log ad_request (kèm param của step) và tự đi tiếp
            if (_floorPersonalization.IsOwnedUnit(adUnitId)) return;
            //không multicall thì chỉ có 1 unit và KHÔNG đặt bậc nào --> tier để NULL.
            //Doc: "Không chạy MultiCall (hoặc prefix không phải số) thì để null (field vắng mặt)".
            //Gửi 0 là SAI: 0 là bậc THẬT của unit default bên multicall, trộn hai nhóm vào
            //cùng một bucket thì win rate theo bậc của unit default hết đọc được.
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
            {
                adType = AdType.Interstitial,
                adUnitId = adUnitId,
                success = true,
                adMediation = _MAX,
                networkName = adInfo.NetworkName,
                tier = null,
                estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue),
                ecpmFloorType = _requestFloorTypeInterstitial,
                loadingMs = adInfo.LatencyMillis
            });
            LogAdLoadSuccess(adUnitId, adInfo);
            _countAttemptInterstitial = 0;
            _networkNameInterstitial = adInfo.NetworkName;
            _readyInterstitial = ReadyInfoOf(adUnitId, adInfo);
        }

        private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            // MediationManager.Instance.DebugLog("MAX > Load failed event intertitial : " + adUnitId + " ---- : " +
            //                                    errorInfo.Message);
            FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_LOAD_FAILED, errorInfo.ToString());
            if (_useMultiCall) return; //multi call đã xử lý
            //workflow personalization tự retry theo retry_times — đừng kích retry backoff của unit đơn
            if (_floorPersonalization.IsOwnedUnit(adUnitId)) return;
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
            {
                adType = AdType.Interstitial,
                adUnitId = adUnitId,
                success = false,
                adMediation = _MAX,
                networkName = null,
                tier = null,
                ecpmFloorType = _requestFloorTypeInterstitial,
                errorMess = errorInfo.Code.ToString(),
                loadingMs = errorInfo.LatencyMillis
            });
            LogAdLoadFailed(adUnitId, errorInfo);
            _countAttemptInterstitial++;
            if (_countAttemptInterstitial >= _LOAD_GIVE_UP_ATTEMPTS)
                AdViewRequestGate.ReportLoadGaveUp(AdType.Interstitial, errorInfo.Code.ToString());
            var retryDelay = (float)Math.Pow(2, Math.Min(6, _countAttemptInterstitial));
            DelayCall(retryDelay, LoadInterstitial);
        }

        private string _idAd;

        private void OnInterstitialDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            // MediationManager.Instance.DebugLog("MAX > OnInterstitialDisplayedEvent intertitial");
            _hasShowSession = true;

            //Floor Personalization ĐẦU TIÊN, bọc riêng: chốt eCPM thật của ad vừa chiếu (mọi unit) rồi gọi
            //Subsequent Ad Rule cho ad kế tiếp. Đặt cuối hàm thì Firebase/MMP/LevelData ném exception là
            //hook bị bỏ qua im lặng -> không bao giờ xin floor cho ad tiếp theo.
            try
            {
                InterstitialPersonalization.OnInterstitialDisplayed(adUnitId, MultiCall.ToEcpm(adInfo.Revenue));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            //log FActionLog trước, để start_ không bị mất nếu Firebase/MMP/LevelData ném exception
            var dict = new Dictionary<string, string>
            {
                { "ad_where", Placement(_placementIdInterstitial) },
                { "ad_network", adInfo.NetworkName }
            };
            FActionLogManager.Log($"start_{AdType.Interstitial.ToString()}", dict);

            FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_SHOW);
            FalconMmpLog.LogEvent(FalconMmpLog.MMP_INTERS_DISPLAYED, new Dictionary<string, string>
            {
                { "af_level", LevelData.Instance.level + "" }
            });

            if (HandlesInterstitialUnit(adUnitId))
                MediationManager.Instance.NumberShowInterstitial++;
        }

        private void OnInterstitialAdFailedToDisplayEvent(
            string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            // MediationManager.Instance.DebugLog("MAX > OnInterstitialAdFailedToDisplayEvent intertitial");
            ReportShowFailed(AdType.Interstitial, adUnitId, errorInfo, adInfo);
            //show hỏng là view chết -> đóng van TRƯỚC nhánh multicall (handler này chạy ở CẢ hai
            //đường), đợt load bù sau đó mới được mở view mới.
            AdViewRequestGate.Close(AdType.Interstitial);
            if (_floorPersonalization.IsOwnedUnit(adUnitId)) _floorPersonalization.OnOwnedAdFinished(adUnitId);
            if (!HandlesInterstitialUnit(adUnitId)) return; //multi call đã xử lý
            _onInterstitialFailed?.Invoke();
            DelayCall(0.5f, LoadInterstitial);
        }

        private void OnInterstitialClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            // MediationManager.Instance.DebugLog("MAX > OnInterstitialClickedEvent intertitial");
            FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_CLICKED);
            FalconBigDataController.Ad.OnClicked(AdType.Interstitial);
        }

        private void OnInterstitialHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            // MediationManager.Instance.DebugLog("MAX > OnInterstitialHiddenEvent intertitial");

            var isFirstHidden = ConsumeShowSession();
            if (_floorPersonalization.IsOwnedUnit(adUnitId)) _floorPersonalization.OnOwnedAdFinished(adUnitId);
            if (isFirstHidden)
            {
                if (HandlesInterstitialUnit(adUnitId))
                {
                    var time = (int)(DateTime.Now - _dateTimeTimeShow).TotalSeconds;
                    new CSAdFinish(_idAd, AdType.Interstitial.ToString(), Placement(_placementIdInterstitial),
                        adInfo.NetworkName, _MAX, adUnitId, adInfo.Revenue, time).Send();
                }

                MediationManager.Instance.AdsClosed(AdType.Interstitial, _placementIdInterstitial);
                //log FActionLog
                var dict = new Dictionary<string, string>
                {
                    { "ad_where", Placement(_placementIdInterstitial) },
                    { "ad_network", adInfo.NetworkName }
                };
                FActionLogManager.Log($"finish_{AdType.Interstitial.ToString()}", dict);
            }
            else
            {
                MediationManager.Instance.DebugError(
                    "MAX > bỏ qua log hidden interstitial lặp của lượt show đã kết thúc : " + adUnitId);
            }

            //log firebase/mmp của quảng cáo đầu tiên (nếu đang hoãn) cùng lúc với CSAdFinish
            FlushPendingAdRevenue();

            _eCpmInterstitial = -100;
            if (_isShowForRewarded)
            {
                GameEvent.Emit(_REWARDED_CLOSED);
            }
            else
            {
                GameEvent.Emit(_INTERSTITIAL_CLOSED);
            }

            if (!HandlesInterstitialUnit(adUnitId)) return; //multi call đã xử lý
            //where truyền TƯỜNG MINH: bỏ trống thì OnClosed đọc ad-view cache của BigData, mà cache
            //đó chỉ có MỘT slot cho mỗi AdType và bị xoá trắng ở mọi OnRequested — log close ra
            //rỗng → DWH default "Unknown".
            //Gác bằng isFirstHidden: MAX bắn lặp hidden của lượt đã kết thúc (chính lý do có
            //ConsumeShowSession) thì close cũng phải bỏ qua, không thì một view đẻ hai mốc close.
            if (isFirstHidden)
                FalconBigDataController.Ad.OnClosed(
                    AdType.Interstitial, adWhere: Placement(_placementIdInterstitial));
            //van mở lại VÔ ĐIỀU KIỆN — kẹt van là mọi đợt load sau mất luôn mốc request.
            AdViewRequestGate.Close(AdType.Interstitial);
            _onInterstitialClosed?.Invoke();
            DelayCall(0.5f, LoadInterstitial);
        }

#endif

        public bool IsInterstitialReady()
        {
            if (_settings == null) return false;
            if (!_settings.UseInterstitialMax) return false;
            if (_isInitialized == null || !_isInitialized.Value) return false;
#if MAX_ENABLE
            //chế độ Lua: ad của Lua + nguồn phụ (DFPool / meta AdMob), chiếu được như ở MultiCall.
            //Phiên Lua không có workflow Databuckets / MultiCall / id đơn.
            if (_useLua)
                return LuaAdMonetize.HasReady(AdType.Interstitial) || SecondaryAdSource.IsReady(AdType.Interstitial);
#endif
            if (_floorPersonalization.HasReadyCandidate()) return true;
            var id = _settings.interstitialIdMaxAndroid;
#if UNITY_IOS
            id = _settings.interstitialIdMaxIOS;
#endif
            if (_useMultiCall) return MultiCall.Instance.IsInterstitialReady();
#if MAX_ENABLE
            return MaxSdk.IsInterstitialReady(id) || SecondaryAdSource.IsReady(AdType.Interstitial);
#else
            return false;
#endif
        }

        public void ShowInterstitial(
            string where, Action onInterstitialClosed = null, Action onFail = null,
            bool needAdBreak = false)
        {
            ShowInterstitial(where, onInterstitialClosed, onFail, needAdBreak, false);
        }

        private void ShowInterstitial(
            string where, Action onInterstitialClosed = null, Action onFail = null,
            bool needAdBreak = false, bool showForRewarded = false)
        {
            if (_settings == null)
            {
                MediationManager.Instance.DebugError("ShowInterstitial setting is null");
                onFail?.Invoke();
                return;
            }

            //chuẩn hoá 1 lần ở đây, mọi log phía sau (CSAdStart, FAdCalledLog, multicall) dùng chung giá trị này
            where = Placement(where);

            //inter chiếu thay rewarded: mang adWhen / triggerPlayTurnId của lượt rewarded sang
            if (showForRewarded) MediationManager.ApplyRewardedContextToInterstitial();

            if (IsRemoveAds() && !showForRewarded)
            {
                MediationManager.Instance.DebugLog($"MAX > ShowInterstitial bỏ qua: RemoveAds @ {where}");
                onInterstitialClosed?.Invoke();
                return;
            }

            if (!_settings.UseInterstitialMax)
            {
                MediationManager.Instance.DebugError("_settings.UseInterstitialMax = false");
                onFail?.Invoke();
                return;
            }

            if (!_isInitialized.HasValue || !_isInitialized.Value)
            {
                MediationManager.Instance.DebugError("_isInitialized.HasValue = false || _isInitialized.Value = false");
                onFail?.Invoke();
                return;
            }

            if (!IsInterstitialReady())
            {
                MediationManager.Instance.DebugLog($"MAX > ShowInterstitial fail: chưa có inter Ready @ {where}");
                onFail?.Invoke();
                _floorPersonalization.OnShowMiss();
                LoadInterstitial();
                return;
            }

            MediationManager.Instance.DebugLog("MAX > ShowInterstitial");
            _placementIdInterstitial = where;
            _isShowingAds = true;
            _isShowForRewarded = showForRewarded;
            var id = _settings.interstitialIdMaxAndroid;
#if UNITY_IOS
            id = _settings.interstitialIdMaxIOS;
#endif
            _dateTimeTimeShow = DateTime.Now;
            if ((needAdBreak && _timeBreak > 0 && !showForRewarded) ||
                MediationManager.Instance.GetRewardedCallbackFromGroupId(where).listReward.Count > 0)
            {
                UIWrapper.OpenPopup("UIPopupAdBreak",
                    p => { p.SendMessage("UpdateUI", (_timeBreak, (Action)OnClose)); });
            }
            else
            {
                OnClose();
            }

            return;

            void OnClose()
            {
                _hasShowSession = true;
#if MAX_ENABLE
                //inter đắt nhất trên MỌI nguồn của phiên (bên chính | candidate FloorPersonalization | nguồn phụ),
                //cùng một luật cho Lua / MultiCall / unit đơn — xem PickInterstitial
                var pick = PickInterstitial(where, out var primaryLeft);
                switch (pick)
                {
                    //Candidate workflow Databuckets chỉ có ở chế độ DATABUCKETS (phiên Lua pool luôn rỗng)
                    case ShowPick.FloorCandidate:
                        var candidate = _floorPersonalization.TakeBestCandidate();
                        if (candidate != null)
                            ShowSingleUnitInterstitial(candidate.adUnitId, candidate.networkName, where,
                                onInterstitialClosed, onFail, showForRewarded);
                        else
                            FailInterstitialShow(where, onFail);
                        break;
                    case ShowPick.Secondary when _useMultiCall:
                    case ShowPick.Primary when _useMultiCall:
                        //MultiCall tự so ad MAX với nguồn phụ (cùng luật hoà -> MAX), giữ nguyên bộ log của nó
                        MultiCall.Instance.ShowInterstitial(where, onInterstitialClosed, onFail, showForRewarded);
                        break;
                    case ShowPick.Secondary:
                        ShowSecondaryAd(AdType.Interstitial, where, onInterstitialClosed, onFail, showForRewarded,
                            primaryLeft);
                        break;
                    case ShowPick.Primary when _useLua:
                        ShowLuaAd(AdType.Interstitial, where, onInterstitialClosed, onFail, showForRewarded, false);
                        break;
                    case ShowPick.Primary:
                        ShowSingleUnitInterstitial(id, _networkNameInterstitial, where,
                            onInterstitialClosed, onFail, showForRewarded);
                        break;
                    default:
                        FailInterstitialShow(where, onFail);
                        return;
                }
#endif

                FalconMmpLog.LogEvent(FalconMmpLog.MMP_INTERS_SHOW, new Dictionary<string, string>
                {
                    { "af_level", LevelData.Instance.level + "" }
                });
            }
        }

#if MAX_ENABLE
        /// <summary>
        /// Chiếu ad của bộ ad unit riêng LuaDynamic (độc lập với MultiCall). FalconMaxService lo phần log của
        /// lượt show giống các nhánh khác (CSAdStart/CSAdFinish, action log, GameEvent, đếm cap placement);
        /// BigData shown/closed/impression do <see cref="LuaAdMonetize"/> tự bắn.
        /// <paramref name="showType"/> = Reward: chọn eCpm cao nhất trong RW (+ inter nếu
        /// <paramref name="allowInterForReward"/>). <paramref name="showForRewarded"/>: lượt interstitial này
        /// thực chất thay cho rewarded (luồng cũ chuyển sang).
        /// </summary>
        private void ShowLuaAd(AdType showType, string where, Action onDone, Action onFail,
            bool showForRewarded, bool allowInterForReward)
        {
            var ad = LuaAdMonetize.PeekBest(showType, allowInterForReward);
            if (ad == null)
            {
                MediationManager.Instance.DebugLog($"Lua > show {showType} fail: Lua không có ad @ {where}");
                onFail?.Invoke();
                return;
            }

            var isRewardFlow = showType == AdType.Reward;
            var adUnitId = ad.adUnitId;
            var network = ad.networkName;
            var mediation = ad.mediation;
            var revenue = ad.Revenue;
            MediationManager.Instance.DebugLog(
                $"Lua > show {showType} bằng {ad.adUnitType} {mediation} {adUnitId} eCpm={ad.eCpm}");

            _isShowingAds = true;
            _hasShowSession = true;
            _dateTimeTimeShow = DateTime.Now;
            if (isRewardFlow)
            {
                _closedRewarded = false;
                _receivedRewarded = false;
            }

            //adId = adViewId — xem lý do ở ShowSingleUnitInterstitial.
            var idAd = FalconBigDataController.Ad.CurrentViewId(ad.format) ?? Guid.NewGuid().ToString();
            _idAd = idAd;
            new CSAdStart(idAd, ad.format.ToString(), where, network, mediation, adUnitId).Send();

            var csMediation = mediation == LuaAdRecord.MEDIATION_MAX ? MultiCall.MAX : mediation;
            var maxRevenueLeft = mediation == LuaAdRecord.MEDIATION_ADMOB
                ? LuaAdMonetize.BestReadyRevenue(ad.format, LuaAdRecord.MEDIATION_MAX, ad)
                : 0d;
            var actionLog = new Dictionary<string, string>
            {
                { "ad_where", Placement(where) },
                { "ad_network", network }
            };

            var shownType = ad.format;

            void Displayed()
            {
                FActionLogManager.Log($"start_{shownType}", actionLog);
                var levelParams = new Dictionary<string, string> { { "af_level", LevelData.Instance.level + "" } };
                if (shownType == AdType.Reward)
                {
                    AdSessionCounter.OnRewardedShown();
                    FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_SHOW_SUCCESS);
                    FalconMmpLog.LogEvent(FalconMmpLog.MMP_REWARDED_DISPLAYED, levelParams);
                    return;
                }

                MediationManager.Instance.NumberShowInterstitial++;
                FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_SHOW);
                FalconMmpLog.LogEvent(FalconMmpLog.MMP_INTERS_DISPLAYED, levelParams);
                try
                {
                    InterstitialPersonalization.OnInterstitialDisplayed(null, MultiCall.ToEcpm(revenue));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            void Finish()
            {
                if (!ConsumeShowSession()) return;
                var time = (int)(DateTime.Now - _dateTimeTimeShow).TotalSeconds;
                new CSAdFinish(idAd, shownType.ToString(), Placement(where), network, csMediation, adUnitId, revenue,
                    time).Send();
                FActionLogManager.Log($"finish_{shownType}", actionLog);
                if (shownType == AdType.Interstitial) MediationManager.Instance.AdsClosed(AdType.Interstitial, where);
                FlushPendingAdRevenue();
                if (mediation == LuaAdRecord.MEDIATION_ADMOB)
                    FalconFirebaseLog.LogNumber(_LUA_ADMOB_IMPRESSION_EVENT, _LUA_UPLIFT_REVENUE_PARAM,
                        Math.Max(0d, revenue) - maxRevenueLeft);
            }

            void Paid(AdType paidType, string paidNetwork, double paidRevenue, LuaPaidDetail detail)
            {
                var time = (int)(DateTime.Now - _dateTimeTimeShow).TotalSeconds;
                new CSAdInfo(idAd, Placement(where), paidType.ToString(), paidNetwork, csMediation, time, paidRevenue)
                    .Send();
                if (detail.maxInfo != null) LogExternalMaxRevenue(detail.maxInfo, paidType);
            }

            if (isRewardFlow)
            {
                FsnAdManager.Instance.ShowRewardedThenNative(
                    showRewarded: callbacks =>
                    {
                        var shown = LuaAdMonetize.Show(AdType.Reward, where, allowInterForReward, Displayed,
                            onReward: () =>
                            {
                                _receivedRewarded = true;
                                callbacks.OnRewardReceived();
                                CheckReceivedRewarded();
                            },
                            onHidden: () =>
                            {
                                Finish();
                                _closedRewarded = true;
                                GameEvent.Emit(_REWARDED_CLOSED);
                                callbacks.OnHidden();
                                CheckReceivedRewarded();
                            },
                            onFail: () =>
                            {
                                ConsumeShowSession();
                                callbacks.OnDisplayFailed();
                            },
                            onPaid: Paid);

                        //hết ad đúng lúc gọi show -> không callback nào được bắn, tự trả fail
                        if (!shown)
                        {
                            ConsumeShowSession();
                            callbacks.OnDisplayFailed();
                        }
                    },
                    onSuccess: () => { onDone?.Invoke(); },
                    where: where,
                    onFailed: () => { onFail?.Invoke(); });
                GameEvent.Emit(_REWARDED_DISPLAYED);
                return;
            }

            FsnAdManager.Instance.ShowInterstitialThenNative(
                showInterstitial: callbacks =>
                {
                    var shown = LuaAdMonetize.Show(AdType.Interstitial, where, false, Displayed,
                        onReward: null,
                        onHidden: () =>
                        {
                            Finish();
                            GameEvent.Emit(showForRewarded ? _REWARDED_CLOSED : _INTERSTITIAL_CLOSED);
                            callbacks.OnHidden();
                        },
                        onFail: () =>
                        {
                            ConsumeShowSession();
                            callbacks.OnDisplayFailed();
                        },
                        onPaid: Paid);

                    if (!shown)
                    {
                        ConsumeShowSession();
                        callbacks.OnDisplayFailed();
                    }
                },
                onSuccess: () => { onDone?.Invoke(); },
                where: where,
                onFailed: () => { onFail?.Invoke(); });
            GameEvent.Emit(showForRewarded ? _REWARDED_DISPLAYED : _INTERSTITIAL_DISPLAYED);
        }

        #region nguồn phụ (DFPool / meta AdMob) ở phiên unit đơn + Lua — cùng luật so giá với MultiCall

        //"không có ad" của phía MAX / Lua, cùng quy ước với MultiCall (mọi giá trị < 0 = không có ad)
        private const double _NO_AD_REVENUE = -100d;

        //adMediation của nguồn phụ trên log — khớp MultiCallProvider._SECONDARY_MEDIATION
        private const string _SECONDARY_MEDIATION = "Admob";

        /// <summary>Revenue (USD / impression) ad đắt nhất của Lua đang Ready; không có = -100.</summary>
        private static double LuaRevenue(AdType showType, bool allowInterForReward)
        {
            if (!LuaAdMonetize.HasReady(showType, allowInterForReward)) return _NO_AD_REVENUE;
            var ad = LuaAdMonetize.PeekBest(showType, allowInterForReward);
            return ad == null ? _NO_AD_REVENUE : Math.Max(0d, ad.Revenue);
        }

        /// <summary>
        /// Revenue ad unit đơn đang Ready (USD/impression), &lt;= 0 = không có. Chỉ khi phiên unit đơn (không
        /// MultiCall, không Lua) — dùng cho hob multi-platform mediation của tầng AdMob.
        /// </summary>
        internal double SingleUnitReadyRevenue(AdType type)
            => _useLua || _useMultiCall ? 0d : Math.Max(0d, SingleMaxRevenue(type));

        /// <summary>Revenue ad của unit đơn SOMediationSetting đang Ready trong SDK; không có = -100.</summary>
        private double SingleMaxRevenue(AdType type)
        {
            if (_settings == null || _isInitialized != true) return _NO_AD_REVENUE;
            if (type == AdType.Reward)
            {
                var rewardedId = _settings.rewardedIdMaxAndroid;
#if UNITY_IOS
                rewardedId = _settings.rewardedIdMaxIOS;
#endif
                return MaxSdk.IsRewardedAdReady(rewardedId) ? Math.Max(0d, _eCpmRewarded) : _NO_AD_REVENUE;
            }

            var interId = _settings.interstitialIdMaxAndroid;
#if UNITY_IOS
            interId = _settings.interstitialIdMaxIOS;
#endif
            return MaxSdk.IsInterstitialReady(interId) ? Math.Max(0d, _eCpmInterstitial) : _NO_AD_REVENUE;
        }

        /// <summary>Bên được chọn ở một lượt show.</summary>
        private enum ShowPick
        {
            None,

            /// <summary>Bên chính của phiên: ad Lua / ad MAX MultiCall / ad unit đơn.</summary>
            Primary,

            /// <summary>Candidate của workflow FloorPersonalization (chỉ inter).</summary>
            FloorCandidate,

            /// <summary>Nguồn phụ (<see cref="SecondaryAdSource"/>: DFPool / meta AdMob).</summary>
            Secondary
        }

        /// <summary>
        /// Revenue (USD / impression) ad đắt nhất ĐANG SẴN SÀNG của bên chính, KHÔNG tính nguồn phụ / candidate
        /// FloorPersonalization; &lt; 0 = không có. Lua: chỉ đúng format (không cho inter thay rewarded ở đây).
        /// </summary>
        private double PrimaryRevenue(AdType type)
        {
            if (_useLua) return LuaRevenue(type, false);
            if (_useMultiCall) return MultiCall.Instance.BestMaxRevenue(type);
            return SingleMaxRevenue(type);
        }

        /// <summary>Revenue candidate FloorPersonalization đắt nhất đang sẵn sàng; &lt; 0 = không có (kể cả khi tắt).</summary>
        private double FloorCandidateRevenue()
        {
            var candidate = _floorPersonalization.PeekBestCandidate();
            if (candidate == null) return _NO_AD_REVENUE;
            return candidate.estimatedEcpm is { } ecpm && ecpm > 0d ? ecpm / MultiCall.MULTIPLIER : 0d;
        }

        /// <summary>
        /// Chọn inter đắt nhất trên MỌI nguồn của phiên: bên chính | candidate FloorPersonalization | nguồn phụ.
        /// Hoà: candidate FloorPersonalization thắng bên chính (giữ thứ tự ưu tiên cũ của workflow), cả hai
        /// thắng nguồn phụ (như MultiCall). <paramref name="primaryLeft"/> = giá ad MAX/Lua đắt nhất bị bỏ lại
        /// khi nguồn phụ thắng (để tính uplift).
        /// </summary>
        private ShowPick PickInterstitial(string where, out double primaryLeft, bool log = true)
        {
            var primary = PrimaryRevenue(AdType.Interstitial);
            var floor = FloorCandidateRevenue();
            var secondary = SecondaryAdSource.BestRevenue(AdType.Interstitial);

            var pick = ShowPick.None;
            var best = double.NegativeInfinity;
            if (floor >= 0d)
            {
                pick = ShowPick.FloorCandidate;
                best = floor;
            }

            if (primary >= 0d && primary > best)
            {
                pick = ShowPick.Primary;
                best = primary;
            }

            if (secondary >= 0d && secondary > best) pick = ShowPick.Secondary;

            primaryLeft = Math.Max(primary, floor);
            if (!log) return pick;
            MediationManager.Instance.DebugLog(
                $"SHOW INTER @ '{where}' so giá: chính={Rev(primary)} FloorPersonalization={Rev(floor)} " +
                $"nguồn phụ={Rev(secondary)} -> {pick}" +
                (secondary >= 0d ? "\n" + SecondaryAdSource.DescribeCache(AdType.Interstitial) : ""));
            return pick;
        }

        /// <summary>
        /// Lượt rewarded: inter đắt nhất (mọi nguồn, chỉ khi <paramref name="allowInter"/>) có ĐẮT HƠN hẳn rewarded
        /// đắt nhất (bên chính + nguồn phụ) không. Hoà -> rewarded. allowInter = false: không xét inter.
        /// </summary>
        private bool InterBeatsRewarded(string where, bool allowInter, out double bestRewarded, out double bestInter)
        {
            var primaryRewarded = PrimaryRevenue(AdType.Reward);
            var secondaryRewarded = SecondaryAdSource.BestRevenue(AdType.Reward);
            bestRewarded = Math.Max(primaryRewarded, secondaryRewarded);
            bestInter = allowInter
                ? Math.Max(Math.Max(PrimaryRevenue(AdType.Interstitial), FloorCandidateRevenue()),
                    SecondaryAdSource.BestRevenue(AdType.Interstitial))
                : _NO_AD_REVENUE;

            var interWins = bestInter >= 0d && bestInter > bestRewarded;
            MediationManager.Instance.DebugLog(
                $"SHOW REWARDED @ '{where}' so giá: RW chính={Rev(primaryRewarded)} RW nguồn phụ={Rev(secondaryRewarded)}" +
                (allowInter ? $" | inter đắt nhất (mọi nguồn)={Rev(bestInter)}" : " | KHÔNG cho chiếu inter thay rewarded") +
                $" -> {(interWins ? "CHIẾU INTER" : bestRewarded >= 0d ? "CHIẾU REWARDED" : "không có ad")}");
            return interWins;
        }

        /// <summary>Lúc chiếu không còn ad nào (ad vừa hết hạn giữa lúc kiểm tra ready và lúc chiếu).</summary>
        private void FailInterstitialShow(string where, Action onFail)
        {
            MediationManager.Instance.DebugLog($"MAX > ShowInterstitial fail: không nguồn nào còn ad @ {where}");
            ConsumeShowSession();
            _isShowingAds = false;
            onFail?.Invoke();
            _floorPersonalization.OnShowMiss();
            LoadInterstitial();
        }

        private static void LogRewardedShowMmp()
        {
            FalconMmpLog.LogEvent(FalconMmpLog.MMP_REWARDED_SHOW, new Dictionary<string, string>
            {
                { "af_level", LevelData.Instance.level + "" }
            });
        }

        private static string Rev(double revenue)
            => revenue < 0d ? "không có" : revenue.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Chiếu ad đắt nhất của nguồn phụ (<see cref="SecondaryAdSource"/>: DFPool / meta AdMob) ở phiên unit đơn
        /// hoặc Lua. Cùng bộ log với nhánh nguồn phụ của MultiCall (CSAdStart / CSAdFinish / CSAdInfo, Firebase
        /// <c>meta_mediation_admob_impression</c>) + phần log lượt show của FalconMaxService (start_/finish_,
        /// AdsClosed, GameEvent). BigData shown / impression / closed do nguồn phụ tự bắn.
        /// <paramref name="format"/> = format THẬT được chiếu; <paramref name="showForRewarded"/>: inter chiếu thay
        /// rewarded. <paramref name="primaryLeft"/>: giá ad bên chính bị bỏ lại (uplift).
        /// </summary>
        private void ShowSecondaryAd(AdType format, string where, Action onDone, Action onFail,
            bool showForRewarded, double primaryLeft)
        {
            //chụp TRƯỚC khi show: ad lên hình là rời kho, hỏi lại ra ad khác
            var revenue = SecondaryAdSource.BestRevenue(format);
            var adUnitId = SecondaryAdSource.BestAdUnitId(format);
            var network = SecondaryAdSource.BestNetwork(format);
            var isReward = format == AdType.Reward;
            MediationManager.Instance.DebugLog(
                $"{(_useLua ? "Lua" : "MAX đơn")} > show {format} NGUỒN PHỤ {adUnitId} ({network}) revenue={revenue}" +
                (showForRewarded ? " thay rewarded" : ""));

            _isShowingAds = true;
            _hasShowSession = true;
            _dateTimeTimeShow = DateTime.Now;
            if (isReward)
            {
                _placementIdRewarded = where;
                _closedRewarded = false;
                _receivedRewarded = false;
            }
            else
            {
                _placementIdInterstitial = where;
                _isShowForRewarded = showForRewarded;
            }

            //adId = adViewId — xem lý do ở ShowSingleUnitInterstitial.
            var idAd = FalconBigDataController.Ad.CurrentViewId(format) ?? Guid.NewGuid().ToString();
            _idAd = idAd;
            new CSAdStart(idAd, format.ToString(), where, network, _SECONDARY_MEDIATION, adUnitId).Send();

            var actionLog = new Dictionary<string, string>
            {
                { "ad_where", Placement(where) },
                { "ad_network", network }
            };

            //nguồn phụ không có mốc "displayed" riêng: ad đã đóng = đã lên hình (giống MultiCall).
            //MMP af_*_displayed KHÔNG bắn ở đây: nguồn phụ tự bắn (meta AdMob / DFPool), bắn thêm là đếm đôi.
            void Displayed()
            {
                FActionLogManager.Log($"start_{format}", actionLog);
                if (isReward)
                {
                    AdSessionCounter.OnRewardedShown();
                    FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_SHOW_SUCCESS);
                    return;
                }

                MediationManager.Instance.NumberShowInterstitial++;
                FalconFirebaseLog.Log(FalconFirebaseLog.AD_INTER_SHOW);
                try
                {
                    _floorPersonalization.OnInterstitialDisplayed(null, MultiCall.ToEcpm(revenue));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            void Finish()
            {
                if (!ConsumeShowSession()) return;
                var time = (int)(DateTime.Now - _dateTimeTimeShow).TotalSeconds;
                new CSAdFinish(idAd, format.ToString(), Placement(where), network, _SECONDARY_MEDIATION, adUnitId,
                    revenue < 0d ? 0d : revenue, time).Send();
                FActionLogManager.Log($"finish_{format}", actionLog);
                if (!isReward) MediationManager.Instance.AdsClosed(AdType.Interstitial, where);
                FalconFirebaseLog.LogNumber(_LUA_ADMOB_IMPRESSION_EVENT, _LUA_UPLIFT_REVENUE_PARAM,
                    Math.Max(0d, revenue) - Math.Max(0d, primaryLeft));
            }

            void Paid(string paidMediation, string paidNetwork, double paidRevenue)
            {
                var time = (int)(DateTime.Now - _dateTimeTimeShow).TotalSeconds;
                new CSAdInfo(idAd, Placement(where), format.ToString(), paidNetwork, paidMediation, time, paidRevenue)
                    .Send();
            }

            if (isReward)
            {
                FsnAdManager.Instance.ShowRewardedThenNative(
                    showRewarded: callbacks =>
                    {
                        var shown = SecondaryAdSource.Show(AdType.Reward, where,
                            onReward: () =>
                            {
                                _receivedRewarded = true;
                                callbacks.OnRewardReceived();
                                CheckReceivedRewarded();
                            },
                            onHidden: () =>
                            {
                                Displayed();
                                Finish();
                                _closedRewarded = true;
                                GameEvent.Emit(_REWARDED_CLOSED);
                                callbacks.OnHidden();
                                CheckReceivedRewarded();
                            },
                            onFail: () =>
                            {
                                ConsumeShowSession();
                                callbacks.OnDisplayFailed();
                            },
                            onPaid: Paid);

                        //hết ad đúng lúc gọi show -> không callback nào được bắn, tự trả fail
                        if (!shown)
                        {
                            ConsumeShowSession();
                            callbacks.OnDisplayFailed();
                        }
                    },
                    onSuccess: () => { onDone?.Invoke(); },
                    where: where,
                    onFailed: () => { onFail?.Invoke(); });
                GameEvent.Emit(_REWARDED_DISPLAYED);
                return;
            }

            FsnAdManager.Instance.ShowInterstitialThenNative(
                showInterstitial: callbacks =>
                {
                    var shown = SecondaryAdSource.Show(AdType.Interstitial, where,
                        onReward: null,
                        onHidden: () =>
                        {
                            Displayed();
                            Finish();
                            GameEvent.Emit(showForRewarded ? _REWARDED_CLOSED : _INTERSTITIAL_CLOSED);
                            callbacks.OnHidden();
                        },
                        onFail: () =>
                        {
                            ConsumeShowSession();
                            callbacks.OnDisplayFailed();
                        },
                        onPaid: Paid);

                    if (!shown)
                    {
                        ConsumeShowSession();
                        callbacks.OnDisplayFailed();
                    }
                },
                onSuccess: () => { onDone?.Invoke(); },
                where: where,
                onFailed: () => { onFail?.Invoke(); });
            GameEvent.Emit(showForRewarded ? _REWARDED_DISPLAYED : _INTERSTITIAL_DISPLAYED);
        }

        /// <summary>Thông tin "ad sẽ chiếu" của nguồn phụ (cho <see cref="ReadyInterstitialInfo"/>).</summary>
        private static AdReadyInfo SecondaryReadyInfo(AdType type)
        {
            var revenue = SecondaryAdSource.BestRevenue(type);
            if (revenue < 0d) return null;
            return new AdReadyInfo
            {
                adMediation = _SECONDARY_MEDIATION,
                adNetwork = SecondaryAdSource.BestNetwork(type),
                adUnitId = SecondaryAdSource.BestAdUnitId(type),
                estimatedEcpm = MultiCall.ToEcpm(revenue)
            };
        }

        #endregion

        /// <summary>
        /// Chiếu interstitial của MỘT ad unit, FalconMaxService tự lo trọn show → hidden: unit đơn của
        /// SOMediationSetting (không multicall) hoặc candidate của workflow personalization.
        /// </summary>
        private void ShowSingleUnitInterstitial(string adUnitId, string networkName, string where,
            Action onInterstitialClosed, Action onFail, bool showForRewarded)
        {
            //adId của CSAdStart/CSAdFinish = adViewId của BigData -> game server và DWH
            //join được thành trọn vòng đời request -> show -> impression -> close.
            //(Trước đây gọi CurrentViewId rồi VỨT giá trị trả về, adId vẫn là Guid riêng
            //nên hai kho không nối được với nhau.)
            //Fallback Guid khi format chưa từng qua mốc request — CSAdStart vẫn cần một id.
            _idAd = FalconBigDataController.Ad.CurrentViewId(AdType.Interstitial)
                    ?? Guid.NewGuid().ToString();
            new CSAdStart(_idAd, AdType.Interstitial.ToString(), where, networkName, _MAX, adUnitId).Send();
            FsnAdManager.Instance.ShowInterstitialThenNative(
                showInterstitial: callbacks =>
                {
                    _onInterstitialClosed = callbacks.OnHidden;
                    _onInterstitialFailed = callbacks.OnDisplayFailed;
                    FalconBigDataController.Ad.OnShown(new AdShowParam
                        { type = AdType.Interstitial, adWhere = where, adMediation = _MAX, adUnitId = adUnitId });
                    MaxSdk.ShowInterstitial(adUnitId);
                },
                onSuccess: () =>
                {
                    onInterstitialClosed?.Invoke();
                }, //success là hàm callback sẽ đc gọi sau khi xem xong quảng cáo interstitial, do user tự code
                where: where,
                onFailed: () =>
                {
                    onFail?.Invoke();
                } //failure là hàm callback sẽ được gọi nếu quảng cáo không show được
            );

            if (showForRewarded)
            {
                GameEvent.Emit(_REWARDED_DISPLAYED);
            }
            else
            {
                GameEvent.Emit(_INTERSTITIAL_DISPLAYED);
            }
        }
#endif

        /// <summary>
        /// Opportunity hiện tại của personalization rơi về Default (Control / API lỗi) -> kích lại logic
        /// load mặc định mà lượt chờ Rule đã chặn.
        /// </summary>
        private void ResumeDefaultInterstitialLoading()
        {
            if (_useMultiCall) MultiCall.Instance.ResumeInterstitialLoading();
            else LoadInterstitial();
        }

        public void LoadInterstitial()
        {
            if (!_settings.UseInterstitialMax) return;
            if (!_isInitialized.HasValue || !_isInitialized.Value) return;
            //đang chờ Rule / workflow personalization đang lo opportunity này -> đứng yên
            if (_floorPersonalization.BlocksDefaultLoading) return;
            //chế độ Lua: script tự lo load, không đụng id đơn của SOMediationSetting
            if (_useLua) return;
            //MULTICALL ĐANG CHẠY -> KHÔNG được đụng tới id đơn của SOMediationSetting.
            //Id đó KHÔNG nằm trong disable_b2b_ad_unit_ids (CallBeforeInit), KHÔNG nằm trong mảng
            //truyền cho MaxSdk.InitializeSdk, và KHÔNG được set disable_auto_retries (CallAfterInit)
            //— cả ba thứ chỉ áp cho các id lấy từ ListAdBidFloors. Load nó là bắn một request
            //back-to-back, không có waterfall dựng sẵn, tự retry theo backoff riêng của MAX; nó kéo
            //chậm luôn cả các unit multicall khác. Ad unit của multicall do MultiCallProvider tự lo.
            if (_useMultiCall) return;

            // MediationManager.Instance.DebugLog("MAX > LoadInterstitial");
            var id = _settings.interstitialIdMaxAndroid;
#if UNITY_IOS
            id = _settings.interstitialIdMaxIOS;
#endif
#if MAX_ENABLE
            //KHÔNG reset _placementIdInterstitial ở đây — xem luật ở chỗ khai báo field.
            //Qua VAN: retry (DelayCall ở OnInterstitialLoadFailedEvent) gọi lại đúng hàm này, mà
            //hợp đồng cấm bắn mốc request cho retry — van tự nuốt, chỉ lần xin ĐẦU của đợt đi qua.
            AdViewRequestGate.Open(AdType.Interstitial, _MAX, AdViewRequestGate.ALGORITHM_SINGLE);
            _requestFloorTypeInterstitial = _floorPersonalization.DefaultLoadFloorType;
            MaxSdk.LoadInterstitial(id);
            LogAdRequest(id, AdType.Interstitial);
#endif
        }

        #endregion

        #region Banner

        private int _countAttemptBanner = 0;

        public bool IsBannerReady()
        {
            return _bannerIsLoaded;
        }

        public void LoadCollapsibleBanner()
        {
            MediationManager.Instance.DebugError("max not support collapsible Banner --> call from Google");
            // throw new NotImplementedException();
        }

        public void ShowCollapsibleBanner()
        {
            MediationManager.Instance.DebugError("max not support collapsible Banner --> call from Google");
            // throw new NotImplementedException();
        }

        public void HideCollapsibleBanner()
        {
            MediationManager.Instance.DebugError("max not support collapsible Banner --> call from Google");
            // throw new NotImplementedException();
        }

        public void ShowBanner(string placementId)
        {
            MediationManager.Instance.DebugLog("Call > ShowBanner");
            _callShowBanner = true;
            _placementIdBanner = placementId;
            if (_settings == null) return;
            if (!_settings.UseBannerMax) return;
            if (!_isInitialized.HasValue || !_isInitialized.Value) return;
            if (IsRemoveAds()) return;
            MediationManager.Instance.DebugLog("MAX > ShowBanner");
            var id = _settings.bannerIdMaxAndroid;
#if UNITY_IOS
            id = _settings.bannerIdMaxIOS;
#endif
#if MAX_ENABLE
            MaxSdk.ShowBanner(id);
#endif
            if (_bannerIsLoaded)
            {
                _bannerIsShow = true;
                MediationManager.Instance.onBannerShow?.Invoke();
                if (_settings.useButtonCloseBanner)
                {
                    float bannerHeightPx = GetBannerHeightInPixels();
                    BannerCloseButtonUI.Show(bannerHeightPx);
                }
            }
        }

        public float GetBannerHeightInPixels()
        {
            var heightPx = 0f;
#if MAX_ENABLE
            float heightDp = MaxSdkUtils.GetAdaptiveBannerHeight();
            heightPx = heightDp * (Screen.dpi / 160f);
#endif
            return heightPx;
        }

        public void HideBanner(bool fromInit = false)
        {
            MediationManager.Instance.DebugLog("Call > HideBanner");
            if (!fromInit)
            {
                _callShowBanner = false;
            }

            if (_settings == null) return;
            if (!_settings.UseBannerMax) return;
            if (!_isInitialized.HasValue || !_isInitialized.Value) return;
            MediationManager.Instance.DebugLog("MAX > HideBanner");
            var id = _settings.bannerIdMaxAndroid;
#if UNITY_IOS
            id = _settings.bannerIdMaxIOS;
#endif
#if MAX_ENABLE
            MaxSdk.HideBanner(id);
#endif
            if (_bannerIsShow)
            {
                _bannerIsShow = false;
                MediationManager.Instance.onBannerHide?.Invoke();
                BannerCloseButtonUI.Hide();
            }
        }

#if AMAZON_ENABLE && !UNITY_EDITOR
        private APSBannerAdRequest bannerAdRequest;
#endif
        public void CreateBannerView()
        {
            if (!_settings.UseBannerMax) return;
            if (!_isInitialized.HasValue || !_isInitialized.Value) return;
            MediationManager.Instance.DebugLog("MAX > Creating banner view");

#if AMAZON_ENABLE && !UNITY_EDITOR
            if (string.IsNullOrWhiteSpace(_settings.bannerIdAmazonMaxAndroid)) return;
            if (bannerAdRequest != null) bannerAdRequest.DestroyFetchManager();
            var size = GetSizeAdaptiveBanner();
            var idAmazon = _settings.bannerIdAmazonMaxAndroid;
            var id = _settings.bannerIdMaxAndroid;
#if UNITY_IOS
            idAmazon = _settings.bannerIdAmazonMaxIOS;
            id = _settings.bannerIdMaxIOS;
#endif
            bannerAdRequest =
                new APSBannerAdRequest((int)size.x, (int)size.y, idAmazon, new AdNetworkInfo(ApsAdNetwork.MAX));
            bannerAdRequest.onFailedWithError += (adError) =>
            {
#if MAX_ENABLE
                MaxSdk.SetBannerLocalExtraParameter(id, "amazon_ad_error", adError.GetAdError());
#endif
                LoadBanner();
            };
            bannerAdRequest.onSuccess += (adResponse) =>
            {
#if MAX_ENABLE
                MaxSdk.SetBannerLocalExtraParameter(id, "amazon_ad_response", adResponse.GetResponse());
#endif
                LoadBanner();
            };

            bannerAdRequest.LoadAd();
#else
            LoadBanner();
#endif
        }

        private Vector2 GetSizeAdaptiveBanner()
        {
            // Lấy chiều rộng màn hình (theo pixels, vì Max SDK dùng pixel)
            int screenWidth = Screen.width;

            // Tính chiều cao banner dựa theo chiều rộng (Google gợi ý: 50dp cho phones, 90dp cho tablets)
            // Với Unity, ta lấy DPI thực để convert dp -> px
            float dpi = Screen.dpi;
            if (dpi == 0) dpi = 160; // fallback nếu không xác định được DPI

            // 50dp tương đương:
            int heightPx = Mathf.RoundToInt(50 * (dpi / 160f));

            // Cập nhật kích thước banner
            return new Vector2(screenWidth, heightPx);
        }

        public void DestroyBanner()
        {
            if (!_settings.UseBannerMax) return;
            if (!_isInitialized.HasValue || !_isInitialized.Value) return;
            UnregisterBannerEvents();
            var id = _settings.bannerIdMaxAndroid;
#if UNITY_IOS
            id = _settings.bannerIdMaxIOS;
#endif
#if MAX_ENABLE
            MaxSdk.DestroyBanner(id);
#endif
#if AMAZON_ENABLE && !UNITY_EDITOR
            bannerAdRequest.Dispose();
#endif
        }

        private void RegisterBannerEvents()
        {
#if MAX_ENABLE
            MaxSdkCallbacks.Banner.OnAdLoadedEvent += OnBannerAdLoadedEvent;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += OnBannerAdLoadFailedEvent;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += OnBannerAdRevenuePaidEvent;
            //remove for performance
            // MaxSdkCallbacks.Banner.OnAdClickedEvent += OnBannerAdClickedEvent;
            // MaxSdkCallbacks.Banner.OnAdExpandedEvent += OnBannerAdExpandedEvent;
            // MaxSdkCallbacks.Banner.OnAdCollapsedEvent += OnBannerAdCollapsedEvent;
#endif
        }

        private void UnregisterBannerEvents()
        {
#if MAX_ENABLE
            MaxSdkCallbacks.Banner.OnAdLoadedEvent -= OnBannerAdLoadedEvent;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent -= OnBannerAdLoadFailedEvent;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent -= OnBannerAdRevenuePaidEvent;
            //remove for performance
            // MaxSdkCallbacks.Banner.OnAdClickedEvent -= OnBannerAdClickedEvent;
            // MaxSdkCallbacks.Banner.OnAdExpandedEvent -= OnBannerAdExpandedEvent;
            // MaxSdkCallbacks.Banner.OnAdCollapsedEvent -= OnBannerAdCollapsedEvent;
#endif
        }
#if MAX_ENABLE
        private void OnBannerAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            OnAdRevenuePaidEvent(adUnitId, AdType.Banner, adInfo);
        }

        void OnBannerAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            MediationManager.Instance.DebugLog("Max > Banner view loaded an ad");
            _bannerIsLoaded = true;
            _countAttemptBanner = 0;
            MediationManager.Instance.onBannerLoaded?.Invoke();
            if (_callShowBanner)
            {
                ShowBanner(_placementIdBanner);
                _callShowBanner = false;
            }
        }

        void OnBannerAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            MediationManager.Instance.DebugLog("Max > Banner view failed to load an ad with error : " +
                                               errorInfo.Message);
            _countAttemptBanner++;
            var retryDelay = (float)Math.Pow(2, Math.Min(6, _countAttemptBanner));
            DelayCall(retryDelay, LoadBanner);
            _bannerIsLoaded = false;
        }
        /*
                void OnBannerAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
                {
                    MediationManager.Instance.DebugLog("Max > Banner view was clicked.");
                }

                void OnBannerAdExpandedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
                {
                    MediationManager.Instance.DebugLog("Max > Banner view Expanded.");
                }

                void OnBannerAdCollapsedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
                {
                    MediationManager.Instance.DebugLog("Max > Banner view collapsed.");
                }
                */
#endif
        public void LoadBanner()
        {
            MediationManager.Instance.DebugLog("Max > LoadBanner");
            var id = _settings.bannerIdMaxAndroid;
#if UNITY_IOS
            id = _settings.bannerIdMaxIOS;
#endif
#if MAX_ENABLE
            _placementIdBanner = "create_banner";
            //Banner có ĐÚNG 2 mốc theo hợp đồng: OnRequested lúc tạo instance + OnImpression mỗi
            //paid event. KHÔNG gọi OnShown/OnClicked/OnClosed (3 mốc phễu chỉ dành cho
            //inter/rewarded). Van giữ một view cho cả vòng đời banner: các lần refresh của MAX
            //dùng lại đúng adViewId đó, đúng ngữ nghĩa "một banner instance đẻ N impression",
            //và retry load (DelayCall ở OnBannerAdLoadFailedEvent) không mở view mới.
            AdViewRequestGate.Open(AdType.Banner, _MAX, AdViewRequestGate.ALGORITHM_SINGLE);
            MaxSdk.CreateBanner(id, new MaxSdkBase.AdViewConfiguration(MaxSdkBase.AdViewPosition.BottomCenter));
#endif
        }

        #endregion

        #region Rewarded

        private int _countAttemptRewarded;

        public void LoadRewardedAd()
        {
            if (!_settings.UseRewardedMax) return;
            if (!_isInitialized.HasValue || !_isInitialized.Value) return;
            //chế độ Lua: script tự lo load, không đụng id đơn của SOMediationSetting
            if (_useLua) return;
            //MULTICALL ĐANG CHẠY -> KHÔNG được đụng tới id đơn của SOMediationSetting.
            //Id đó KHÔNG nằm trong disable_b2b_ad_unit_ids (CallBeforeInit), KHÔNG nằm trong mảng
            //truyền cho MaxSdk.InitializeSdk, và KHÔNG được set disable_auto_retries (CallAfterInit)
            //— cả ba thứ chỉ áp cho các id lấy từ ListAdBidFloors. Load nó là bắn một request
            //back-to-back, không có waterfall dựng sẵn, tự retry theo backoff riêng của MAX; nó kéo
            //chậm luôn cả các unit multicall khác. Ad unit của multicall do MultiCallProvider tự lo.
            if (_useMultiCall) return;

            MediationManager.Instance.DebugLog("Max > LoadRewardedAd");
            var id = _settings.rewardedIdMaxAndroid;
#if UNITY_IOS
            id = _settings.rewardedIdMaxIOS;
#endif
#if MAX_ENABLE
            //KHÔNG reset _placementIdRewarded ở đây — xem luật ở chỗ khai báo field.
            //Qua VAN — xem lý do ở LoadInterstitial.
            AdViewRequestGate.Open(AdType.Reward, _MAX, AdViewRequestGate.ALGORITHM_SINGLE);
            MaxSdk.LoadRewardedAd(id);
            LogAdRequest(id, AdType.Reward);
#endif
        }

        public bool IsRewardedVideoReady()
        {
            if (_settings == null) return false;
            if (!_settings.UseRewardedMax) return false;
            //MAX chưa init (chưa qua CallBeforeInit -> Init -> CallAfterInit): không đụng ad unit nào
            if (!IsMaxInitialized) return false;
#if MAX_ENABLE
            //chế độ Lua: chỉ ad của Lua (RW hoặc inter chiếu thay)
            //+ nguồn phụ (DFPool / meta AdMob): chiếu được như ở MultiCall
            if (_useLua) return LuaAdMonetize.HasReady(AdType.Reward) || SecondaryAdSource.IsReady(AdType.Reward);
#endif
            var id = _settings.rewardedIdMaxAndroid;
#if UNITY_IOS
            id = _settings.rewardedIdMaxIOS;
#endif
            if (_useMultiCall) return MultiCall.Instance.IsRewardedVideoReady();
#if MAX_ENABLE
            return MaxSdk.IsRewardedAdReady(id) || SecondaryAdSource.IsReady(AdType.Reward);
#endif
            return false;
        }

        private void RegisterRewardedEvent()
        {
#if MAX_ENABLE
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += OnRewardedAdDisplayedEvent;
            MaxSdkCallbacks.Rewarded.OnAdClickedEvent += OnRewardedAdClickedEvent;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += OnRewardedAdRevenuePaidEvent;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdFailedToDisplayEvent;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;
#endif
        }

#if MAX_ENABLE
        private void OnRewardedAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            OnAdRevenuePaidEvent(adUnitId, AdType.Reward, adInfo);
        }

        private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            MediationManager.Instance.DebugLog("Max > Rewarded ad OnRewardedAdLoadedEvent");
            _eCpmRewarded = adInfo.Revenue;
            FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_LOAD);
            if (_useMultiCall) return; //multi call đã xử lý
            //không multicall thì chỉ có 1 unit và KHÔNG đặt bậc nào --> tier để NULL.
            //Doc: "Không chạy MultiCall (hoặc prefix không phải số) thì để null (field vắng mặt)".
            //Gửi 0 là SAI: 0 là bậc THẬT của unit default bên multicall, trộn hai nhóm vào
            //cùng một bucket thì win rate theo bậc của unit default hết đọc được.
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
            {
                adType = AdType.Reward,
                adUnitId = adUnitId,
                success = true,
                adMediation = _MAX,
                networkName = adInfo.NetworkName,
                tier = null,
                estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue),
                loadingMs = adInfo.LatencyMillis
            });
            LogAdLoadSuccess(adUnitId, adInfo);
            _countAttemptRewarded = 0;
            _networkNameRewarded = adInfo.NetworkName;
        }

        private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            MediationManager.Instance.DebugLog(
                "Max > Rewarded ad OnRewardedAdLoadFailedEvent ---- " + errorInfo.Message);
            if (_useMultiCall) return;
            FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam()
            {
                adType = AdType.Reward,
                adUnitId = adUnitId,
                success = false,
                adMediation = _MAX,
                networkName = null,
                tier = null,
                errorMess = errorInfo.Code.ToString(),
                loadingMs = errorInfo.LatencyMillis
            });
            LogAdLoadFailed(adUnitId, errorInfo);
            _countAttemptRewarded++;
            if (_countAttemptRewarded >= _LOAD_GIVE_UP_ATTEMPTS)
                AdViewRequestGate.ReportLoadGaveUp(AdType.Reward, errorInfo.Code.ToString());
            var retryDelay = (float)Math.Pow(2, Math.Min(6, _countAttemptRewarded));
            DelayCall(retryDelay, LoadRewardedAd);
        }
#endif
        private string _networkNameRewarded;
        private string _networkNameInterstitial;

        public void ShowRewardedVideo(
            string where, Action onRewardedComplete = null, Action onFail = null,
            bool showInterstitialInstead = true)
        {
            if (_settings == null)
            {
                onFail?.Invoke();
                return;
            }

            //MAX chưa init: so giá / soát slot đều gọi API theo ad unit của MAX -> chưa được làm gì
            if (!IsMaxInitialized)
            {
                MediationManager.Instance.DebugLog("MAX > ShowRewardedVideo fail: MAX chưa init xong");
                onFail?.Invoke();
                return;
            }

            //1 lượt rewarded có thể bị đổi thành interstitial: so sánh eCpm ở ngay dưới,
            //rewarded chưa sẵn sàng, hoặc multicall tự chọn interstitial bên trong MultiCallProvider.
            //Gán placement cho cả 2 loại để log của loại thực sự được show ra không bị null.
            where = Placement(where);
            _placementIdRewarded = where;
            _placementIdInterstitial = where;

#if MAX_ENABLE
            //CÙNG MỘT LUẬT cho Lua / MultiCall / unit đơn (giá đọc từ ad ĐANG SẴN SÀNG trong SDK, không dùng giá
            //cache của ad đã mất):
            // - showInterstitialInstead = false: chỉ chọn rewarded đắt nhất (bên chính | nguồn phụ), inter không
            //   tham gia so giá.
            // - true: inter đắt nhất trên MỌI nguồn (bên chính | candidate FloorPersonalization | nguồn phụ) ĐẮT
            //   HƠN hẳn rewarded đắt nhất -> chiếu inter (lượt inter tự chọn inter đắt nhất); hoà -> rewarded.
            var allowInter = showInterstitialInstead && _settings.UseInterstitialMax;
            if (InterBeatsRewarded(where, allowInter, out var bestRewarded, out _))
            {
                ShowInterstitial(where, onRewardedComplete, onFail, false, true);
                LogRewardedShowMmp();
                return;
            }

            if (bestRewarded < 0d)
            {
                //MultiCall: provider tự load lại các slot rồi báo fail, giữ nguyên bộ log của nó
                if (_useMultiCall)
                {
                    MultiCall.Instance.ShowRewardedVideo(where, onRewardedComplete, onFail, allowInter);
                    return;
                }

                MediationManager.Instance.DebugLog($"MAX > rewarded: không có ad rewarded nào @ {where}" +
                                                   (allowInter ? " (inter cũng không có)" : ""));
                LoadRewardedAd();
                onFail?.Invoke();
                return;
            }

            if (_useMultiCall)
            {
                //rewarded thắng: MultiCall tự so ad MAX với nguồn phụ (hoà -> MAX)
                MultiCall.Instance.ShowRewardedVideo(where, onRewardedComplete, onFail, allowInter);
                LogRewardedShowMmp();
                return;
            }

            var primaryRewarded = PrimaryRevenue(AdType.Reward);
            if (SecondaryAdSource.BestRevenue(AdType.Reward) > primaryRewarded)
            {
                //nguồn phụ (DFPool / meta AdMob) đắt hơn hẳn rewarded bên chính (hoà -> bên chính)
                ShowSecondaryAd(AdType.Reward, where, onRewardedComplete, onFail, false, primaryRewarded);
                LogRewardedShowMmp();
                return;
            }

            if (_useLua)
            {
                ShowLuaAd(AdType.Reward, where, onRewardedComplete, onFail, true, false);
                LogRewardedShowMmp();
                return;
            }
            //unit đơn: rewarded của id đơn đang sẵn sàng là đắt nhất -> chiếu ở dưới
#else
            onFail?.Invoke();
            return;
#endif

            MediationManager.Instance.DebugLog("Max > Rewarded ad ShowRewardedVideo");
            _closedRewarded = false;
            _receivedRewarded = false;

            _isShowingAds = true;
            _hasShowSession = true;
            _dateTimeTimeShow = DateTime.Now;
            var id = _settings.rewardedIdMaxAndroid;
#if UNITY_IOS
            id = _settings.rewardedIdMaxIOS;
#endif
#if MAX_ENABLE
            if (_useMultiCall)
                MultiCall.Instance.ShowRewardedVideo(where, onRewardedComplete, onFail);
            else
            {
                //adId = adViewId — xem lý do ở ShowInterstitial.
                _idAd = FalconBigDataController.Ad.CurrentViewId(AdType.Reward)
                        ?? Guid.NewGuid().ToString();
                new CSAdStart(_idAd, AdType.Reward.ToString(), where, _networkNameRewarded, _MAX, id).Send();
                FsnAdManager.Instance.ShowRewardedThenNative(
                    showRewarded: callbacks =>
                    {
                        _onRewardedComplete = callbacks.OnRewardReceived;
                        _onRewardedFailed = callbacks.OnDisplayFailed;
                        _onRewardedHidden = callbacks.OnHidden;
                        FalconBigDataController.Ad.OnShown(new AdShowParam
                            { type = AdType.Reward, adWhere = where, adMediation = _MAX, adUnitId = id });
                        MaxSdk.ShowRewardedAd(id);
                    },
                    onSuccess: () =>
                    {
                        onRewardedComplete?.Invoke();
                    }, //success là action được gọi khi user xem xong Rewarded Video, trả thưởng cho user, hồi sinh, …
                    where: where,
                    onFailed: () =>
                    {
                        onFail?.Invoke();
                    } //failure là action được gọi khi quảng cáo bị lỗi không hiển thị được
                );

                GameEvent.Emit(_REWARDED_DISPLAYED);
            }
#endif
            FalconMmpLog.LogEvent(FalconMmpLog.MMP_REWARDED_SHOW, new Dictionary<string, string>
            {
                { "af_level", LevelData.Instance.level + "" }
            });
        }

#if MAX_ENABLE
        private void OnRewardedAdDisplayedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            MediationManager.Instance.DebugLog("Max > Rewarded ad OnRewardedAdDisplayedEvent");
            _hasShowSession = true;
            AdSessionCounter.OnRewardedShown();

            //log FActionLog trước, để start_ không bị mất nếu Firebase/MMP/LevelData ném exception
            var dict = new Dictionary<string, string>
            {
                { "ad_where", Placement(_placementIdRewarded) },
                { "ad_network", adInfo.NetworkName }
            };
            FActionLogManager.Log($"start_{AdType.Reward.ToString()}", dict);

            FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_SHOW_SUCCESS);
            FalconMmpLog.LogEvent(FalconMmpLog.MMP_REWARDED_DISPLAYED, new Dictionary<string, string>
            {
                { "af_level", LevelData.Instance.level + "" }
            });
        }

        private void OnRewardedAdFailedToDisplayEvent(
            string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            MediationManager.Instance.DebugLog("Max > Rewarded ad OnRewardedAdFailedToDisplayEvent");
            FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_SHOW_FAIL, errorInfo.ToString());
            ReportShowFailed(AdType.Reward, adUnitId, errorInfo, adInfo);
            //đóng van — xem lý do ở OnInterstitialAdFailedToDisplayEvent.
            AdViewRequestGate.Close(AdType.Reward);
            if (_useMultiCall) return;
            _onRewardedFailed?.Invoke();
            DelayCall(0.5f, LoadRewardedAd);
        }

        private void OnRewardedAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            MediationManager.Instance.DebugLog("Max > Rewarded ad OnRewardedAdClickedEvent");
            FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_CLICK, adInfo.Placement);
            FalconBigDataController.Ad.OnClicked(AdType.Reward);
        }

        private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            _closedRewarded = true;
            MediationManager.Instance.DebugLog("Max > Rewarded ad OnRewardedAdHiddenEvent");
            var isFirstHidden = ConsumeShowSession();
            if (isFirstHidden)
            {
                if (!_useMultiCall)
                {
                    var time = (int)(DateTime.Now - _dateTimeTimeShow).TotalSeconds;
                    new CSAdFinish(_idAd, AdType.Reward.ToString(), Placement(_placementIdRewarded), adInfo.NetworkName,
                        _MAX, adUnitId, adInfo.Revenue, time).Send();
                }

                //log FActionLog
                var dict = new Dictionary<string, string>
                {
                    { "ad_where", Placement(_placementIdRewarded) },
                    { "ad_network", adInfo.NetworkName }
                };
                FActionLogManager.Log($"finish_{AdType.Reward.ToString()}", dict);
            }
            else
            {
                MediationManager.Instance.DebugError(
                    "MAX > bỏ qua log hidden rewarded lặp của lượt show đã kết thúc : " + adUnitId);
            }

            //log firebase/mmp của quảng cáo đầu tiên (nếu đang hoãn) cùng lúc với CSAdFinish
            FlushPendingAdRevenue();

            _eCpmRewarded = -100;
            GameEvent.Emit(_REWARDED_CLOSED);
            if (_useMultiCall) return;
            _onRewardedHidden?.Invoke();

            //MỐC CLOSE bắn NGAY Ở ĐÂY, không gác sau CheckReceivedRewarded nữa:
            //CheckReceivedRewarded chỉ chạy khi CẢ hidden LẪN reward đã về, nên rewarded bị người
            //chơi tắt giữa chừng (không có OnAdReceivedRewardEvent) là mất hẳn mốc close — và
            //adCompleted khi đó hardcode true thì không bao giờ ra được giá trị false.
            //Hợp đồng: "Rewarded: giữ bool từ OnAdReceivedRewardEvent rồi truyền vào đây
            //(Hidden đến sau Reward)" — tại thời điểm này _receivedRewarded đã đúng.
            if (isFirstHidden)
                FalconBigDataController.Ad.OnClosed(AdType.Reward, adCompleted: _receivedRewarded,
                    adWhere: Placement(_placementIdRewarded));
            //van mở lại VÔ ĐIỀU KIỆN — kẹt van là mọi đợt load sau mất luôn mốc request.
            AdViewRequestGate.Close(AdType.Reward);

            CheckReceivedRewarded();
            DelayCall(0.5f, LoadRewardedAd);
        }

        private void OnRewardedAdReceivedRewardEvent(
            string adUnitId, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo adInfo)
        {
            if (ExternalMaxAdUnits.IsExternal(adUnitId)) return; //slot của module ngoài (vd DFPool) tự lo
            _receivedRewarded = true;
            MediationManager.Instance.DebugLog("Max > Rewarded ad OnRewardedAdReceivedRewardEvent");
            FalconFirebaseLog.Log(FalconFirebaseLog.ADS_REWARD_COMPLETE, adInfo.Placement);
            if (_useMultiCall) return;
            _onRewardedComplete?.Invoke();
            CheckReceivedRewarded();
        }

        private void CheckReceivedRewarded()
        {
            if (_closedRewarded && _receivedRewarded)
            {
                //tiêu thụ 2 cờ (được reset lại ở ShowRewardedVideo) để callback lặp
                //không tính thêm 1 lượt xem nữa vào cap của placement
                _closedRewarded = false;
                _receivedRewarded = false;
                MediationManager.Instance.AdsClosed(AdType.Reward, _placementIdRewarded);
                //Ad.OnClosed KHÔNG còn ở đây — đã chuyển lên OnRewardedAdHiddenEvent để lượt
                //rewarded bị tắt giữa chừng cũng có mốc close (xem chú thích bên đó).
                //Hàm này giờ thuần gameplay: tiêu 2 cờ + đếm cap placement.
            }
        }
#endif

        #endregion

        #region CSAdRequestLog

#if MAX_ENABLE
        //Khi chạy multicall thì MultiCallProvider đã tự đếm request/lỗi/thành công.
        //Khi _useMultiCall = false (đang dùng 3G, máy low-end, hoặc không có bid floor) thì không có ai đếm,
        //nên LogInstance không có dữ liệu và CSAdRequestLog không bao giờ được gửi. Phần dưới bù cho trường hợp đó.
        private void LogAdRequest(string adUnitId, AdType adType)
        {
            if (_useMultiCall) return;
            if (string.IsNullOrWhiteSpace(adUnitId)) return;

            var item = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
            if (item == null)
            {
                item = new AdRequestLogItem
                {
                    adUnitId = adUnitId,
                    adType = adType.ToString().ToLower(),
                    decisionPolicy = SaveLoadHandler.Load(_KEY_DECISION_POLICY, _DECISION_POLICY_NO_MULTICALL),
                    numRequest = 0,
                    errorDetail = new Dictionary<string, int>(),
                    adSuccessInfos = new List<AdSuccessInfo>()
                };
                LogInstance.Instance.AddAdLog(item);
            }

            item.numRequest++;
        }

        private void LogAdLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
        {
            if (_useMultiCall) return;

            var item = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
            if (item == null)
            {
                MediationManager.Instance.DebugError("don't exists adRequestLogItem load failed id : " + adUnitId);
                return;
            }

            var code = errorInfo.Code.ToString();
            if (!item.errorDetail.ContainsKey(code))
            {
                item.errorDetail.Add(code, 0);
            }

            item.errorDetail[code]++;
        }

        private void LogAdLoadSuccess(string adUnitId, MaxSdkBase.AdInfo adInfo)
        {
            if (_useMultiCall) return;

            var item = LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId);
            if (item == null)
            {
                MediationManager.Instance.DebugError("don't exists adRequestLogItem loaded id : " + adUnitId);
                return;
            }

            item.adSuccessInfos.Add(new AdSuccessInfo
            {
                index = item.numRequest,
                revenueUsd = adInfo.Revenue,
                time = new DateTimeOffset(TimeUtils.UTCNow).ToUnixTimeMilliseconds(),
                adMediation = _MAX,
                adNetwork = adInfo.NetworkName
            });
        }
#endif

        #endregion

        public void UnregisterEvent()
        {
        }

        private void DelayCall(float time, Action action)
        {
            MaxEvent.Instance.StartCoroutine(RetryDelay());

            IEnumerator RetryDelay()
            {
                yield return new WaitForSecondsRealtime(time);
                action.Invoke();
            }
        }

        private bool IsRemoveAds()
        {
            return GameData4RemoveAds.Instance.removeAds;
        }

        //ad_where luôn phải là placement được truyền vào hàm show.
        //Chỉ rơi về _PLACEMENT_UNKNOWN khi callback của SDK về mà chưa có lượt show nào,
        //để log không bị null/rỗng và nhìn vào là biết ngay có luồng chưa gán placement.
        private static string Placement(string placementId)
        {
            return string.IsNullOrWhiteSpace(placementId) ? _PLACEMENT_UNKNOWN : placementId;
        }

        //ad_where cho log doanh thu (impression). Đọc thẳng _placementId* — chỗ chiếu của lượt
        //show hiện tại, gán ở hàm show và giữ nguyên tới lượt show kế tiếp (luật ở chỗ khai báo).
        //Số liệu 09/09 đã chốt: paid event của MAX chỉ về trong quãng show → closed chứ không bắn
        //lúc load, nên field này luôn đang mang đúng lượt mà doanh thu thuộc về. Vì vậy không còn
        //lưới đỡ đọc ad-view cache / gác theo _hasShowSession như bản trước.
        //Chưa có lượt show nào cho format thì trả null — AdLog giữ nguyên "Unknown" mặc định của
        //AdParam thay vì bịa một chỗ chiếu.
        private string ImpressionPlacement(AdType type)
        {
            switch (type)
            {
                case AdType.Interstitial:
                    return _placementIdInterstitial;
                case AdType.Reward:
                    return _placementIdRewarded;
                //Banner không đi qua vòng đời ad view (không có OnRequested/OnShown) nên phải tự
                //điền, và BẮT BUỘC khác rỗng: nhánh gộp cụm banner của AdRequestService.LogImpression
                //bỏ qua CorrectValues() nên giá trị rỗng chui thẳng vào DWH.
                case AdType.Banner:
                    return Placement(_placementIdBanner);
                //AppOpen/MREC/Native/CollapsibleBanner: cũng không có hàm show mang placement, lấy
                //chính tên format — trước đây cả ba dùng chung hằng "Banner" nên doanh thu của chúng
                //bị gắn nhãn banner trên DWH.
                default:
                    return type.ToString();
            }
        }

        //true nếu đây là callback hidden đầu tiên của lượt show hiện tại.
        //Chỉ chặn phần log, không chặn phần load lại/callback game để luồng quảng cáo không bị kẹt.
        private bool ConsumeShowSession()
        {
            if (!_hasShowSession) return false;
            _hasShowSession = false;
            return true;
        }
    }
}

public class TimeParam : FParam
{
    public int adTime;
}
