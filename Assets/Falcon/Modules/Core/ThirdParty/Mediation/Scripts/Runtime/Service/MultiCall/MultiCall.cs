/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-04
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.RemoteConfigCms;
using Falcon.Modules.Core.SaveLoad.Runtime;
using Falcon.Modules.Core.Utils.Time.Runtime;
#if IRONSOURCE_ENABLE
using Unity.Services.LevelPlay;
#endif
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Module multi call adUnitId for max sdk
    /// </summary>
    public class MultiCall
    {
        private const string _DISABLE_B2_B = "disable_b2b_ad_unit_ids";
        private const string _DISABLE_RETRIES = "disable_auto_retries";
        private const string _TRUE = "true";
        public bool enableLogMultiCall;

        public const string MAX = "MAX";
        public const string IRON_SOURCE = "IRONSOURCE";
        public const string ADS_LOAD_SUCCESS = "ads_load_success";
        public const string ADS_LOAD_FAILED = "ads_load_failed";
        public const string UNKNOWN = "Unknown";
        public const string ADS_REVENUE_PAID = "ads_revenue_paid";
        public const string ADS_DISPLAY_FAILED = "ads_display_failed";
        public const string ADS_DISPLAYED = "ads_displayed";
        public const string KEY_SET_BID_FLOOR = "jC7Fp";
        private const string _KEY_CACHE_AD_BID_FLOORS = "FALCON_LIST_AD_BID_FLOORS";

        //Danh sách THÔ đúng như server gửi ở sc_ad_bid_floor. Cache cũng lưu bản thô này.
        private List<AdBidFloor> _rawAdBidFloors;

        //Kết quả lọc theo provider index, dựng LƯỜI ở lần đọc đầu tiên. null = chưa dựng.
        private List<AdBidFloor> _listAdBidFloors;

        //Index đã dùng để dựng _listAdBidFloors. int.MinValue = chưa lọc lần nào (index thật luôn >= 0).
        private int _filteredForIndex = int.MinValue;

        public bool getListAdBidFloorFromServer;
        public double valueDefaultRewarded = 0;
        public double valueDefaultInterstitial = 0;

        public const float MULTIPLIER = 1000;

        /// <summary>
        /// Revenue/impression (USD) mà MAX trả trong <c>AdInfo.Revenue</c> -> eCPM (USD/1000 impression).
        /// Số không dương = KHÔNG BIẾT (MAX trả âm khi không có giá) -> null, không truyền 0 (AdEcpmAndParams V1).
        /// </summary>
        public static double? ToEcpm(double revenue) => revenue <= 0d ? null : revenue * MULTIPLIER;

        /// <summary>
        /// Provider index của mediation đang chạy, lấy từ CMS. Bộ lọc bid floor và
        /// <see cref="GetMultiCallProvider"/> BẮT BUỘC đọc chung một chỗ này: lọc danh sách bằng
        /// index này rồi chạy provider của index khác thì provider thiếu đúng những id dành cho nó.
        /// </summary>
        internal static int ProviderIndex()
        {
            switch (MediationManager.Instance.CurrentMediationType())
            {
                case MediationType.Max:
                    // return 3;
                    return FConfigControllerCms.Instance.Config<MultiCallConfig>().moMulProviderIndexMax;
                case MediationType.LevelPlay:
                    return FConfigControllerCms.Instance.Config<MultiCallConfig>().moMulProviderIndexIronSource;
                case MediationType.Admob:
                    return FConfigControllerCms.Instance.Config<MultiCallConfig>().moMulProviderIndexAdmob;
                default:
                    return 0;
            }
        }

        private static List<AdBidFloor> Filter(List<AdBidFloor> source, int index)
        {
            var result = new List<AdBidFloor>();
            //id AdMob (dành cho LuaDynamic) không được lọt vào MultiCall của MAX / LevelPlay: load bằng SDK đó là
            //fail mãi và làm lệch số id default / thứ tự bậc
            var skipAdmob = MediationManager.Instance.CurrentMediationType() != MediationType.Admob;
            foreach (var adBidFloor in source)
            {
                if (skipAdmob && adBidFloor.MediationKind() == AdBidFloor.MEDIATION_ADMOB) continue;
                if (adBidFloor.multiplier == 0 || adBidFloor.algorithmIds == null ||
                    adBidFloor.algorithmIds.Contains(index) || (index == 3 &&
                                                                (adBidFloor.algorithmIds.Contains(0) ||
                                                                 adBidFloor.algorithmIds.Contains(1) ||
                                                                 adBidFloor.algorithmIds.Contains(2))))
                {
                    result.Add(adBidFloor);
                }
            }

            result.Sort((a, b) => a.multiplier.CompareTo(b.multiplier));
            return result;
        }

        /// <summary>
        /// Danh sách bid floor ĐÃ LỌC theo provider index đang cấu hình.
        ///
        /// CỐ Ý lọc ở đây - lần đọc đầu tiên, tức lúc init mediation - chứ KHÔNG lọc trong setter:
        /// message sc_ad_bid_floor hoàn toàn có thể về TRƯỚC khi CMS settle, mà bộ lọc lại cần
        /// moMulProviderIndex* của CMS. Lọc ngay lúc nhận là lọc bằng index mặc định 0: vừa cắt mất
        /// id của provider thật, vừa GHI tập sai đó xuống cache nên lần mở app sau vẫn sai. Tới lúc
        /// getter này được gọi thì hàng rào remote config đã mở, xem AdsConfigGate + StartProcess.
        ///
        /// Cache đời cũ là bản ĐÃ lọc (có thể lọc nhầm bằng index 0); đọc nó như bản thô thì cùng
        /// lắm ra một tập con, và tự khỏi ngay khi server gửi list mới.
        /// </summary>
        public List<AdBidFloor> ListAdBidFloors
        {
            get
            {
                _rawAdBidFloors ??= SaveLoadHandler.Load(_KEY_CACHE_AD_BID_FLOORS, new List<AdBidFloor>());

                var index = ProviderIndex();
                if (_listAdBidFloors == null || _filteredForIndex != index)
                {
                    _listAdBidFloors = Filter(_rawAdBidFloors, index);
                    _filteredForIndex = index;
                }

                return _listAdBidFloors;
            }
            set
            {
                _rawAdBidFloors = value ?? new List<AdBidFloor>();
                //buộc getter lọc lại theo index tại thời điểm đọc
                _listAdBidFloors = null;
                _filteredForIndex = int.MinValue;
                getListAdBidFloorFromServer = true;
                SaveLoadHandler.Save(_KEY_CACHE_AD_BID_FLOORS, _rawAdBidFloors);
            }
        }

        /// <summary>
        /// Server ĐÃ trả lời sc_ad_bid_floor nhưng không có floor nào. Mở hàng rào init để
        /// StartProcess khỏi phải chờ hết 7s timeout, và CỐ Ý không đụng vào list/cache đang có:
        /// payload rỗng được hiểu là "không có gì để cập nhật", không phải lệnh xoá cấu hình.
        /// </summary>
        /// <summary>
        /// Danh sách THÔ đúng như server gửi ở sc_ad_bid_floor (chưa lọc theo provider index), đọc cache nếu phiên
        /// này server chưa gửi. LuaDynamic dùng bản này: script Lua tự quyết thuật toán.
        /// </summary>
        public List<AdBidFloor> RawAdBidFloors =>
            _rawAdBidFloors ??= SaveLoadHandler.Load(_KEY_CACHE_AD_BID_FLOORS, new List<AdBidFloor>());

        public void MarkAdBidFloorResponded()
        {
            getListAdBidFloorFromServer = true;
        }

        public void ProcessAdUnitId(MediationType type)
        {
            //qua property: _listAdBidFloors dựng lười nên đọc thẳng field có thể còn null.
            var listAdBidFloors = ListAdBidFloors;
            if (dictionaryRewarded.Count + dictionaryInterstitial.Count != listAdBidFloors.Count)
            {
                dictionaryRewarded.Clear();
                dictionaryInterstitial.Clear();
                foreach (var adBidFloor in listAdBidFloors)
                {
                    var timeConfig = adBidFloor.timeConfig;
                    var timeConfigs = timeConfig.Split(',');
                    var list = new List<int>();
                    foreach (var config in timeConfigs)
                    {
                        bool a = int.TryParse(config, out int b);
                        if (a)
                        {
                            list.Add(b);
                        }
                    }

                    if (adBidFloor.rewarded)
                    {
                        if (!dictionaryRewarded.ContainsKey(adBidFloor.adUnitId))
                        {
                            if (type == MediationType.LevelPlay)
                            {
                                dictionaryRewarded.Add(adBidFloor.adUnitId, new AdTypeMultiCall
                                {
                                    index = dictionaryRewarded.Count, adUnitId = adBidFloor.adUnitId,
                                    listTimeRetry = list, countLoadAttempt = 0, timeLoad = TimeUtils.UTCNow,
                                    eCpm = -100, multiplier = adBidFloor.multiplier
#if IRONSOURCE_ENABLE
                                    , levelPlayRewardedAd = new LevelPlayRewardedAd(adBidFloor.adUnitId)
#endif
                                });
                            }
                            else
                            {
                                dictionaryRewarded.Add(adBidFloor.adUnitId, new AdTypeMultiCall
                                {
                                    index = dictionaryRewarded.Count, adUnitId = adBidFloor.adUnitId,
                                    listTimeRetry = list, countLoadAttempt = 0, timeLoad = TimeUtils.UTCNow,
                                    eCpm = -100, multiplier = adBidFloor.multiplier
                                });
                            }
                        }
                    }
                    else
                    {
                        if (!dictionaryInterstitial.ContainsKey(adBidFloor.adUnitId))
                        {
                            if (type == MediationType.LevelPlay)
                            {
                                dictionaryInterstitial.Add(adBidFloor.adUnitId, new AdTypeMultiCall
                                {
                                    index = dictionaryInterstitial.Count, adUnitId = adBidFloor.adUnitId,
                                    listTimeRetry = list, countLoadAttempt = 0, timeLoad = TimeUtils.UTCNow,
                                    eCpm = -100, multiplier = adBidFloor.multiplier
#if IRONSOURCE_ENABLE
                                    , levelPlayInterstitialAd = new LevelPlayInterstitialAd(adBidFloor.adUnitId)
#endif
                                });
                            }
                            else
                            {
                                dictionaryInterstitial.Add(adBidFloor.adUnitId, new AdTypeMultiCall
                                {
                                    index = dictionaryInterstitial.Count, adUnitId = adBidFloor.adUnitId,
                                    listTimeRetry = list, countLoadAttempt = 0, timeLoad = TimeUtils.UTCNow,
                                    eCpm = -100, multiplier = adBidFloor.multiplier
                                });
                            }
                        }
                    }
                }
            }
        }

        public List<KeyValuePair<string, AdTypeMultiCall>> GetDictionarySortedByMultiplier(
            Dictionary<string, AdTypeMultiCall> dict)
        {
            return dict.OrderBy(x => x.Value.multiplier).ThenBy(x => x.Value.index).ToList();
        }

        public List<KeyValuePair<string, AdTypeMultiCall>> GetDictionarySortedByIndex(
            Dictionary<string, AdTypeMultiCall> dict)
        {
            return dict.OrderBy(x => x.Value.index).ToList();
        }

        public AdTypeMultiCall GetAdTypeMultiCallByIndex(int index, Dictionary<string, AdTypeMultiCall> dict)
        {
            foreach (var multiCall in dict)
            {
                if (multiCall.Value.index == index)
                {
                    return multiCall.Value;
                }
            }

            return null;
        }

        public DateTime dateTimeTimeShow;

        private static MultiCall _instance;

        public static MultiCall Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MultiCall();
                }

                return _instance;
            }
        }

        private IMultiCallProvider _provider;

        private readonly List<IMultiCallProvider> _multiCallProvidersMax = new();
        private readonly List<IMultiCallProvider> _multiCallProvidersIronSource = new();
        private readonly List<IMultiCallProvider> _multiCallProvidersAdmob = new();
        public readonly Dictionary<string, AdTypeMultiCall> dictionaryInterstitial = new();
        public readonly Dictionary<string, AdTypeMultiCall> dictionaryRewarded = new();

        public void Register(IMultiCallProvider provider, MediationType mediationType)
        {
            if (provider == null) return;
            switch (mediationType)
            {
                case MediationType.Max:
                    if (_multiCallProvidersMax.Contains(provider)) return;
                    _multiCallProvidersMax.Add(provider);
                    break;
                case MediationType.LevelPlay:
                    if (_multiCallProvidersIronSource.Contains(provider)) return;
                    _multiCallProvidersIronSource.Add(provider);
                    break;
                case MediationType.Admob:
                    if (_multiCallProvidersAdmob.Contains(provider)) return;
                    _multiCallProvidersAdmob.Add(provider);
                    break;
            }
        }

        private IMultiCallProvider GetMultiCallProvider()
        {
            if (_provider == null)
            {
                MediationManager.Instance.DebugLog("multiplier from remote config : " + MULTIPLIER);
                var type = MediationManager.Instance.CurrentMediationType();
                var index = ProviderIndex();
                var list = new List<IMultiCallProvider>();
                switch (type)
                {
                    case MediationType.Max:
                        list = _multiCallProvidersMax;
                        break;
                    case MediationType.LevelPlay:
                        list = _multiCallProvidersIronSource;
                        break;
                    case MediationType.Admob:
                        list = _multiCallProvidersAdmob;
                        break;
                }

                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].GetIndex() == index)
                    {
                        _provider = list[i];
                        break;
                    }
                }

                if (_provider == null && list.Count > 0)
                {
                    _provider = list[0];
                }
            }

            return _provider;
        }

        private MultiCall()
        {
        }

        private bool? _isLowEndDeviceCached;

        public bool IsLowEndDevice()
        {
            if (_isLowEndDeviceCached.HasValue) return _isLowEndDeviceCached.Value;
            var ram = SystemInfo.systemMemorySize / 1024f;
            var lowEnd = ram < FConfigControllerCms.Instance.Config<MultiCallConfig>().minRamMultiCall;
            _isLowEndDeviceCached = lowEnd;
            if (lowEnd)
            {
                MediationManager.Instance.DebugLog("RAM : " + ram);
            }

            return lowEnd;
        }

        public bool IsUseMobileData()
        {
            if (FConfigControllerCms.Instance.Config<MultiCallConfig>().blockMulticallFromMobileData)
            {
                if (Application.internetReachability == NetworkReachability.NotReachable ||
                    Application.internetReachability ==
                    NetworkReachability.ReachableViaCarrierDataNetwork)
                {
                    return true;
                }
            }

            return false;
        }

        public void OnMediationInitCompleted()
        {
            MediationManager.Instance.DebugLog("Init mediation completed");
            GetMultiCallProvider()?.RegisterEvent();
        }

        private readonly HashSet<string> _hashInterstitial = new();
        private readonly HashSet<string> _hashRewarded = new();
        private readonly HashSet<string> _hashBanner = new();
        private readonly HashSet<string> _hashAppOpen = new();

        public void SetIdInterstitialRewarded()
        {
            foreach (var adBidFloor in ListAdBidFloors)
            {
                if (adBidFloor.rewarded)
                {
                    _hashRewarded.Add(adBidFloor.adUnitId);
                }
                else
                {
                    _hashInterstitial.Add(adBidFloor.adUnitId);
                }
            }
        }

        /// <summary>
        /// Interstitial unit KHÔNG do multicall load (workflow Floor Personalization) nhưng vẫn cần đi cùng
        /// tham số init của MAX + disable_b2b_ad_unit_ids + disable_auto_retries. Không vào
        /// dictionaryInterstitial nên multicall không load / không chiếu chúng.
        /// </summary>
        public void AddExtraInterstitialIds(IEnumerable<string> adUnitIds)
        {
            if (adUnitIds == null) return;
            foreach (var id in adUnitIds)
            {
                if (!string.IsNullOrWhiteSpace(id)) _hashInterstitial.Add(id);
            }
        }

        /// <summary>
        /// Rewarded unit KHÔNG do multicall load (vd slot DFPool) nhưng vẫn cần đi cùng tham số init của MAX +
        /// disable_b2b_ad_unit_ids + disable_auto_retries. Xem <see cref="AddExtraInterstitialIds"/>.
        /// </summary>
        public void AddExtraRewardedIds(IEnumerable<string> adUnitIds)
        {
            if (adUnitIds == null) return;
            foreach (var id in adUnitIds)
            {
                if (!string.IsNullOrWhiteSpace(id)) _hashRewarded.Add(id);
            }
        }

        public void CallBeforeInit()
        {
            var result = string.Join(",", _hashInterstitial.Union(_hashRewarded));
#if MAX_ENABLE
            MaxSdk.SetExtraParameter(_DISABLE_B2_B, result);
            MediationManager.Instance.DebugLog("CallBeforeInit : " + _DISABLE_B2_B + " - " + result);
#endif
        }

        public void CallAfterInit()
        {
#if MAX_ENABLE
            foreach (var v in _hashInterstitial)
            {
                MaxSdk.SetInterstitialExtraParameter(v, _DISABLE_RETRIES, _TRUE);
                MediationManager.Instance.DebugLog("CallAfterInit interstitial: " + v + " - " + _DISABLE_RETRIES);
            }

            foreach (var v in _hashRewarded)
            {
                MaxSdk.SetRewardedAdExtraParameter(v, _DISABLE_RETRIES, _TRUE);
                MediationManager.Instance.DebugLog("CallAfterInit rewarded: " + v + " - " + _DISABLE_RETRIES);
            }
#endif
        }

        public void SetIdBanner(string idBanner)
        {
            _hashBanner.Add(idBanner);
        }

        public void SetIdAppOpen(string idAppOpen)
        {
            _hashAppOpen.Add(idAppOpen);
        }

        public string[] GetInitParams()
        {
            var merged = _hashInterstitial.Union(_hashRewarded).Union(_hashBanner).Union(_hashAppOpen);
            return merged.ToArray();
        }

        /// <summary>Kích provider load lại các interstitial slot (sau khi personalization nhả lượt chặn).</summary>
        public void ResumeInterstitialLoading()
        {
            GetMultiCallProvider()?.ResumeInterstitialLoading();
        }

        public bool IsInterstitialReady()
        {
            var provider = GetMultiCallProvider();
            if (provider != null)
            {
                return provider.IsInterstitialReady();
            }

            return false;
        }

        public Falcon.Modules.Core.BigData.AdReadyInfo PeekInterstitial()
            => GetMultiCallProvider()?.PeekInterstitial();

        public class AdTypeMultiCall
        {
            public int index;
            public string adUnitId;
            public List<int> listTimeRetry; //list time retry, split by ","
            public int countLoadAttempt; //number load ads
            public int failCountBeforeLoad; //số lần fail liên tiếp trước lần load ra ad đang giữ (chỉ để log)
            public DateTime timeLoad; //time call request load ads
            public bool isWaiting;
            public double eCpm;
            public string networkName;
            public float multiplier; //multiplier 
            public float requestTier; //multiplier ĐÃ DÙNG cho lần load đang bay — chụp lúc gọi LoadAd, dòng load đọc lại (thang hạ bậc sau đó không ảnh hưởng)
            public double currentFloor; //floor
            public string requestFloorType; //ecpmFloorType của lần load inter đang bay (vd rule_error) — chụp lúc gọi LoadAd như requestTier
            public double loadedFloor; //floor (eCPM) của request ra ad đang giữ — chụp lúc load xong, impression đọc lại
#if IRONSOURCE_ENABLE
            public LevelPlayInterstitialAd levelPlayInterstitialAd;
            public LevelPlayRewardedAd levelPlayRewardedAd;
#endif
        }

        public void ShowInterstitial(
            string placementId, Action onInterstitialClosed, Action onFailed, bool showForRewarded = false)
        {
            var provider = GetMultiCallProvider();
            if (provider != null)
            {
                provider.ShowInterstitial(placementId, onInterstitialClosed, onFailed, showForRewarded);
            }
            else
            {
                MediationManager.Instance.DebugError("show inter multicall provider is null");
                onInterstitialClosed?.Invoke();
            }
        }

        public bool IsRewardedVideoReady()
        {
            var provider = GetMultiCallProvider();
            if (provider != null)
            {
                return GetMultiCallProvider().IsRewardedVideoReady();
            }

            return false;
        }

        /// <summary>Giá ad MAX đắt nhất đang sẵn sàng (không tính nguồn phụ); &lt; 0 = không có.</summary>
        public double BestMaxRevenue(AdType type) => GetMultiCallProvider()?.BestMaxRevenue(type) ?? -100d;

        /// <param name="allowInterstitial">false = không cho chiếu inter thay rewarded (chỉ so giá rewarded).</param>
        public void ShowRewardedVideo(string placementId, Action onRewardedComplete, Action onFailed,
            bool allowInterstitial = true)
        {
            var provider = GetMultiCallProvider();
            if (provider != null)
            {
                provider.ShowRewardedVideo(placementId, onRewardedComplete, onFailed, allowInterstitial);
            }
            else
            {
                MediationManager.Instance.DebugError("show reward video multicall provider is null");
                onFailed?.Invoke();
            }
        }

        /// <summary>
        /// ⚠ ĐÃ NGỪNG GỬI (Request-Mediation-AdLifecycle.md, bảng "Những dòng phải XOÁ").
        /// <br/>
        /// <c>MAdLog</c> bắn event <c>f_sdk_mo_multiple_floor_adunit</c>, mà event đó **đã bị CHẶN
        /// trên server** vì spam per-attempt — còn gửi chỉ tốn băng thông của người chơi.
        /// Thay thế theo từng loại:
        /// <list type="bullet">
        /// <item><c>ADS_LOAD_SUCCESS</c> / <c>ADS_LOAD_FAILED</c> → <c>Ad.OnLoadResult</c>
        /// (SDK tự gộp cụm theo unit × floor × network × kết cục, flush lúc app pause);</item>
        /// <item><c>ADS_DISPLAY_FAILED</c> → <c>Ad.OnShowFailed</c> (đã nối ở FalconMaxService,
        /// chạy cho CẢ hai nhánh multicall và không-multicall);</item>
        /// <item><c>ADS_DISPLAYED</c> / <c>ADS_REVENUE_PAID</c> → bỏ hẳn, trùng 100% với
        /// <c>Ad.OnImpression</c> (mỗi lượt xem đang đẻ 3 event).</item>
        /// </list>
        /// Giữ lại chữ ký rỗng thay vì xoá hàm: nhánh IronSource
        /// (<c>MultiCallIronSourceProviderParent</c>, hiện compile-out vì không có
        /// <c>IRONSOURCE_ENABLE</c>) vẫn còn 8 chỗ gọi. Khi nhánh đó được migrate sang
        /// <c>FalconBigDataController.Ad</c> thì xoá luôn hàm này và cả class <c>MAdLog</c>.
        /// </summary>
        public void LogToServer(
            string eventName, int loadTimes, AdType adType, string adWhere, string networkName,
            string mediation, double revenue, string errorMess, string adUnitId, string floor, double loadingTime)
        {
        }

        public void DelayCall(float time, string adUnitId, Action<string> action)
        {
            MainGameObj.Instance.StartCoroutine(RetryDelay());

            IEnumerator RetryDelay()
            {
                yield return new WaitForSecondsRealtime(time);
                action.Invoke(adUnitId);
            }
        }
    }
}