/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-10-28
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Falcon.Helpers.Devkit;
using Falcon.Helpers.EventBus;
using Falcon.Modules.ActionLog.Runtime;
using Falcon.Modules.Core.BigData;
using Falcon.Modules.Core.RemoteConfigCms;
using Falcon.Modules.Core.SaveLoad.Runtime;
using Falcon.Modules.Core.ThirdParty.Firebase.Scripts.Runtime;
using Falcon.Modules.Core.Utils.Time.Runtime;
using Falcon.Modules.Level.Core;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    public abstract class MultiCallProvider<T> : IMultiCallProvider where T : MultiCallProvider<T>, new()
    {
        private const string _INTERSTITIAL_DISPLAYED = "MEDIATION_INTERSTITIAL_DISPLAYED";
        private const string _REWARDED_DISPLAYED = "MEDIATION_REWARDED_DISPLAYED";

        //cùng tên với FalconMaxService: lượt show nguồn phụ cũng phải báo đóng như ad MAX
        private const string _INTERSTITIAL_CLOSED = "MEDIATION_INTERSTITIAL_CLOSED";
        private const string _REWARDED_CLOSED = "MEDIATION_REWARDED_CLOSED";
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new T();
                }

                return _instance;
            }
        }

        protected abstract int IndexProvider { get; }
        private int _indexProvider;
        protected abstract MediationType MediationType { get; }
        private MediationType _mediationType;


        private bool _initialized;

        protected Action onRewardedComplete;
        protected Action onRewardedFailed;
        protected Action onRewardedHidden;
        protected Action onInterstitialClosed;
        protected Action onInterstitialFailed;

        protected bool closedRewarded, receivedRewarded;

        /// <summary>
        /// Một lượt rewarded chỉ tính là XONG khi user vừa nhận thưởng vừa đóng ad. Hai mốc đó về theo
        /// thứ tự nào cũng được, nên cả hai callback đều gọi hàm này và mốc đến sau là mốc chốt.
        ///
        /// Nằm ở lớp base vì cả nhánh MAX (<c>MultiCallMaxProviderParent</c>) lẫn nhánh nguồn phụ
        /// (<see cref="ShowSecondaryRewarded"/>) đều dùng chung đúng cặp cờ này.
        /// </summary>
        protected void CheckReceivedRewarded()
        {
            if (!closedRewarded || !receivedRewarded) return;

            //tiêu thụ 2 cờ (được reset lại ở đầu mỗi lượt show) để callback lặp
            //không tính thêm 1 lượt xem nữa vào cap của placement
            closedRewarded = false;
            receivedRewarded = false;
            MediationManager.Instance.AdsClosed(AdType.Reward, placementId);
        }

        private SOMediationSetting _setting;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterProvider();
        }

        protected virtual void RegisterProvider()
        {
        }

        protected bool enableLog;

        public int GetIndex()
        {
            return IndexProvider;
        }

        public void RegisterEvent()
        {
            MediationManager.Instance.DebugLog("provider : " + IndexProvider);
            enableLog = FConfigControllerCms.Instance.Config<MultiCallConfig>().enableLogMultiCall;
            LoadInterstitial();
            RegisterInterstitialEvents();
            LoadRewarded();
            RegisterRewardedEvents();
            Application.lowMemory += OnLowMemory;
        }

        protected virtual void RegisterInterstitialEvents()
        {
        }

        protected virtual void RegisterRewardedEvents()
        {
        }

        protected bool throttled;

        private void OnLowMemory()
        {
            Resources.UnloadUnusedAssets();
            GC.Collect();
            if (throttled) return;
            throttled = true;
            var ram = SystemInfo.systemMemorySize / 1024f;
            var maxAds = MultiCall.Instance.dictionaryInterstitial.Values.Count(x => x.eCpm != -100) +
                         MultiCall.Instance.dictionaryRewarded.Values.Count(x => x.eCpm != -100);
            new MThrottledLog(ram, maxAds, IndexProvider).Send();
        }

        /// <summary>
        /// Load rewarded video ads
        /// </summary>
        public virtual void LoadRewarded()
        {
        }

        protected virtual void InternalLoadRewarded(string adUnitId)
        {
        }

        protected virtual void ResetMultiplier(string adUnitId)
        {
        }

        /// <summary>
        /// Cổng cuối trước khi thật sự gửi request rewarded. Mọi đường load (cascade, retry, hidden,
        /// display-failed, SyncMaxSlotsWithSdk) đều đi qua đây, nên provider nào cần giới hạn bậc floor
        /// được load đồng thời thì override hàm này. Mặc định: luôn cho load.
        /// </summary>
        protected virtual bool CanLoadRewarded(MultiCall.AdTypeMultiCall a)
        {
            return true;
        }

        /// <summary>
        /// true (mặc định): đã có qc ở mức multiplier cao hơn thì KHÔNG load id mức thấp hơn.
        /// Provider tự quản thứ tự load các mức (vd. luôn lấp mức trống thấp nhất trước) thì trả false.
        /// </summary>
        protected virtual bool SkipLoadWhenHigherTierLoaded => true;

        protected void LoadRewardedAd(string adUnitId)
        {
            //kiểm tra có đang dùng mobile data ko
            if (MultiCall.Instance.IsUseMobileData())
            {
                throttled = true;
            }

            if (MultiCall.Instance.dictionaryRewarded.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryRewarded[adUnitId];
                MediationManager.Instance.DebugLog("Load Rewarded Ad : " + adUnitId);

                foreach (var multiCall in MultiCall.Instance.dictionaryRewarded)
                {
                    //id default thì bỏ qua
                    if (a.multiplier == 0 || !SkipLoadWhenHigherTierLoaded) break;
                    if (multiCall.Value.eCpm >= 0 && multiCall.Value.multiplier >= a.multiplier && a.multiplier != -1)
                    {
                        //đã load được qc có multiplier > id này
                        MediationManager.Instance.DebugLog("đã load được qc : " + multiCall.Key + " x " +
                                                           multiCall.Value.multiplier + " > " + adUnitId + " x " +
                                                           a.multiplier);
                        ResetMultiplier(adUnitId);
                        return;
                    }
                }

                //chưa có quảng cáo
                //đang ko trong trạng thái chờ callback quảng cáo

                if (!IsRewardedVideoReady(adUnitId) && !a.isWaiting && CanLoadRewarded(a))
                {
                    if (!throttled || (throttled && a.index == 0))
                    {
                        InternalLoadRewarded(adUnitId);

                        a.timeLoad = TimeUtils.UTCNow;
                        a.countLoadAttempt++;
                        a.isWaiting = true;
                        if (LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId) == null)
                        {
                            var adRequestLogItem = new AdRequestLogItem()
                            {
                                adUnitId = adUnitId,
                                adType = AdType.Reward.ToString().ToLower(),
                                decisionPolicy = SaveLoadHandler.Load("decision_policy_load_ad",
                                    "client-" + MultiCall.ProviderIndex()),
                                numRequest = 0,
                                errorDetail = new Dictionary<string, int>(),
                                adSuccessInfos = new List<AdSuccessInfo>()
                            };
                            LogInstance.Instance.AddAdLog(adRequestLogItem);
                        }

                        if (LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId) != null)
                        {
                            LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId).numRequest++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check Rewarded ready to show or not
        /// </summary>
        public bool IsRewardedVideoReady()
        {
#if MAX_ENABLE
            foreach (var call in MultiCall.Instance.dictionaryRewarded)
            {
                if (IsRewardedVideoReady(call.Key))
                {
                    return true;
                }
            }
#endif
            //ad của nguồn phụ (AdMob bước B + C) cũng chiếu được -> phải tính là ready, nếu không game
            //sẽ không bao giờ gọi Show và ad AdMob nằm chết trong kho
            return SecondaryAdSource.IsReady(AdType.Reward);
        }

        protected virtual bool IsRewardedVideoReady(string adUnitId)
        {
            return false;
        }

        protected string idAd;
        protected string placementId;

        //giá trị dự phòng cho ad_where, để không log null nếu callback về mà chưa qua hàm show nào
        protected const string PLACEMENT_UNKNOWN = "unknown";

        protected static string Placement(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? PLACEMENT_UNKNOWN : value;
        }

        //Bật khi bắt đầu 1 lượt show, tắt ở callback hidden đầu tiên.
        //Hidden bắn lặp sẽ không gửi thêm CSAdFinish, nhưng phần load lại vẫn chạy bình thường.
        private bool _hasShowSession;

        protected bool ConsumeShowSession()
        {
            if (!_hasShowSession) return false;
            _hasShowSession = false;
            return true;
        }

        protected void OpenShowSession()
        {
            _hasShowSession = true;
        }

        protected virtual void InternalShowRewarded(string adUnitId, string where)
        {
        }

        /// <summary>
        /// Show rewarded ads.
        ///
        /// So giá 4 ứng viên rồi chiếu cái đắt nhất — rewarded của MAX, rewarded của nguồn phụ (AdMob
        /// bước B + C), và cả HAI ứng viên interstitial, vì inter được phép chiếu thay rewarded:
        ///
        ///   rewarded MAX | rewarded AdMob | inter MAX | inter AdMob
        ///
        /// Thứ tự ưu tiên khi HOÀ giá: rewarded thắng inter (giữ nếp cũ — đúng loại ad user yêu cầu),
        /// MAX thắng nguồn phụ. Nhánh inter uỷ hẳn cho <see cref="ShowInterstitial"/> nên phần so giá
        /// MAX-vs-AdMob của inter chỉ nằm ở một chỗ duy nhất.
        /// </summary>
        /// <param name="where">Position show ads. eg: home, end game...</param>
        /// <param name="allowInterstitial">
        /// false = người gọi cố ý KHÔNG cho chiếu inter thay rewarded -> inter không tham gia so giá, chỉ chọn
        /// rewarded đắt nhất (MAX hoặc nguồn phụ).
        /// </param>
        public void ShowRewardedVideo(string where, Action onRewardedComplete, Action onFailed,
            bool allowInterstitial = true)
        {
            //gán ngay từ đầu: lượt này có thể được show bằng interstitial ở nhánh dưới,
            //mọi log (CSAdStart/CSAdFinish/CSAdInfo) đều đọc từ placementId này
            placementId = Placement(where);

            //soát cache với SDK TRƯỚC khi so giá. Inter chỉ khi được phép chiếu thay rewarded.
            SyncMaxSlotsWithSdk(AdType.Reward);
            if (allowInterstitial) SyncMaxSlotsWithSdk(AdType.Interstitial);

            var maxRewardedEntry = BestEntry(MultiCall.Instance.dictionaryRewarded);
            var maxRewardedRevenue = RevenueOf(maxRewardedEntry);
            var secondaryRewardedRevenue = SecondaryAdSource.BestRevenue(AdType.Reward);

            var maxInterRevenue = allowInterstitial
                ? RevenueOf(BestEntry(MultiCall.Instance.dictionaryInterstitial))
                : _NO_AD;
            var secondaryInterRevenue = allowInterstitial ? SecondaryAdSource.BestRevenue(AdType.Interstitial) : _NO_AD;

            var bestRewarded = Math.Max(maxRewardedRevenue, secondaryRewardedRevenue);
            var bestInter = Math.Max(maxInterRevenue, secondaryInterRevenue);

            MediationManager.Instance.DebugLog(DescribeShowCandidates(AdType.Reward, alsoInterstitial: allowInterstitial) +
                                               (allowInterstitial ? "" : "\n  (người gọi KHÔNG cho chiếu inter thay rewarded)"));

            if (bestRewarded < 0 && bestInter < 0)
            {
                //ads don't exists
                MediationManager.Instance.DebugLog(allowInterstitial
                    ? "=> KHÔNG có ad nào (cả 4 ứng viên) -> load lại + báo fail"
                    : "=> KHÔNG có ad rewarded nào -> load lại + báo fail");
                LoadRewarded();
                onFailed?.Invoke();
                return;
            }

            //inter chỉ được chiếu thay rewarded khi thật sự ĐẮT HƠN, hoà thì rewarded thắng
            if (bestInter > bestRewarded)
            {
                MediationManager.Instance.DebugLog(
                    $"=> CHỌN INTERSTITIAL thay rewarded: inter {Money(bestInter)} > rewarded {Money(bestRewarded)}");
                ShowInterstitial(placementId, onRewardedComplete, onFailed, true);
                return;
            }

            //hoà giữa MAX và nguồn phụ -> ưu tiên MAX
            if (secondaryRewardedRevenue > maxRewardedRevenue)
            {
                MediationManager.Instance.DebugLog(
                    $"=> CHỌN REWARDED ADMOB {Money(secondaryRewardedRevenue)} " +
                    $"(> MAX {Money(maxRewardedRevenue)})");
                ShowSecondaryRewarded(maxRewardedRevenue, onRewardedComplete, onFailed);
                return;
            }

            MediationManager.Instance.DebugLog(
                $"=> CHỌN REWARDED MAX {Money(maxRewardedRevenue)} " +
                $"(AdMob {Money(secondaryRewardedRevenue)}, hoà thì MAX thắng)");

#if MAX_ENABLE
            var a = maxRewardedEntry.Value;
            MultiCall.Instance.dateTimeTimeShow = TimeUtils.UTCNow;
            //KHÔNG xoá giá ở đây — xem MarkAdConsumed: ad chỉ tính là đã tiêu khi SDK báo nó rời kho.
            OpenShowSession();
            //adId của CSAdStart/CSAdFinish = adViewId của BigData -> game server và DWH join được
            //thành trọn vòng đời request -> show -> impression -> close.
            //(Trước đây gọi CurrentViewId rồi VỨT giá trị trả về, idAd vẫn là Guid riêng nên hai
            //kho không nối được với nhau.) Fallback Guid khi format chưa qua mốc request nào.
            idAd = FalconBigDataController.Ad.CurrentViewId(AdType.Reward) ?? Guid.NewGuid().ToString();
            new CSAdStart(idAd, AdType.Reward.ToString(), placementId, a.networkName, MediationType.ToString(),
                maxRewardedEntry.Key).Send();
            FsnAdManager.Instance.ShowRewardedThenNative(
                showRewarded: callbacks =>
                {
                    this.onRewardedComplete = callbacks.OnRewardReceived;
                    onRewardedFailed = callbacks.OnDisplayFailed;
                    onRewardedHidden = callbacks.OnHidden;
                    InternalShowRewarded(maxRewardedEntry.Key, placementId);
                },
                onSuccess: () =>
                {
                    onRewardedComplete?.Invoke();
                }, //success là action được gọi khi user xem xong Rewarded Video, trả thưởng cho user, hồi sinh, …
                where: placementId,
                onFailed: () =>
                {
                    onFailed?.Invoke();
                } //failure là action được gọi khi quảng cáo bị lỗi không hiển thị được
            );
            GameEvent.Emit(_REWARDED_DISPLAYED);
#else
            //không có MAX thì ứng viên duy nhất còn lại là nguồn phụ, mà nhánh trên đã loại nó rồi
            onFailed?.Invoke();
#endif
        }

        #region interstitial

        /// <summary>
        /// Check interstitial ads ready or not
        /// </summary>
        public bool IsInterstitialReady()
        {
            foreach (var call in MultiCall.Instance.dictionaryInterstitial)
            {
                if (IsInterstitialReady(call.Key))
                {
                    return true;
                }
            }

            return SecondaryAdSource.IsReady(AdType.Interstitial);
        }

        protected virtual bool IsInterstitialReady(string adUnitId)
        {
            return false;
        }

        protected virtual void InternalShowInterstitial(string adUnitId)
        {
        }

        /// <summary>
        /// Show Interstitial ads. So giá ad inter đắt nhất của MAX với ad inter đắt nhất của nguồn phụ
        /// (AdMob bước B + C) rồi chiếu bên đắt hơn; hoà thì MAX thắng.
        /// </summary>
        public AdReadyInfo PeekInterstitial()
        {
            PriceInterstitial(false, out var maxEntry, out var maxRevenue, out var secondaryRevenue);
            if (NoInterstitial(maxRevenue, secondaryRevenue)) return null;

            if (SecondaryInterstitialWins(maxRevenue, secondaryRevenue))
                return new AdReadyInfo
                {
                    adMediation = _SECONDARY_MEDIATION,
                    adNetwork = SecondaryAdSource.BestNetwork(AdType.Interstitial),
                    adUnitId = SecondaryAdSource.BestAdUnitId(AdType.Interstitial),
                    estimatedEcpm = MultiCall.ToEcpm(secondaryRevenue)
                };

            return new AdReadyInfo
            {
                adNetwork = maxEntry.Value.networkName,
                adUnitId = maxEntry.Key,
                estimatedEcpm = MultiCall.ToEcpm(maxRevenue)
            };
        }

        private void PriceInterstitial(bool syncWithSdk, out KeyValuePair<string, MultiCall.AdTypeMultiCall> maxEntry,
            out double maxRevenue, out double secondaryRevenue)
        {
            if (syncWithSdk)
            {
                SyncMaxSlotsWithSdk(AdType.Interstitial);
                maxEntry = BestEntry(MultiCall.Instance.dictionaryInterstitial);
            }
            else
            {
                maxEntry = BestEntry(MultiCall.Instance.dictionaryInterstitial, IsInterstitialReady);
            }

            maxRevenue = RevenueOf(maxEntry);
            secondaryRevenue = SecondaryAdSource.BestRevenue(AdType.Interstitial);
        }

        private static bool NoInterstitial(double maxRevenue, double secondaryRevenue)
            => maxRevenue < 0 && secondaryRevenue < 0;

        private static bool SecondaryInterstitialWins(double maxRevenue, double secondaryRevenue)
            => secondaryRevenue > maxRevenue;

        public void ShowInterstitial(string where, Action onInterstitialClosed, Action onFailed, bool showForRewarded)
        {
            placementId = Placement(where);
            //inter chiếu thay rewarded: mang adWhen / triggerPlayTurnId của lượt rewarded sang (gọi lặp vô hại)
            if (showForRewarded) MediationManager.ApplyRewardedContextToInterstitial();

            //lượt cross-show từ rewarded đã soát rồi; gọi lại vô hại vì LoadXAd tự chống request trùng
            PriceInterstitial(true, out var maxEntry, out var maxRevenue, out var secondaryRevenue);

            //lượt show rewarded uỷ sang đây thì bảng so giá đã in ở trên rồi, đừng in lại
            if (!showForRewarded)
                MediationManager.Instance.DebugLog(
                    DescribeShowCandidates(AdType.Interstitial, alsoInterstitial: false));

            //phải kiểm tra "không bên nào có ad" TRƯỚC khi so giá: hai bên dùng hai mốc "không có ad"
            //khác nhau (-100 vs -1), so thẳng thì -1 luôn thắng -100 và ta sẽ đi chiếu một cái kho rỗng
            if (NoInterstitial(maxRevenue, secondaryRevenue))
            {
                //ads don't exists
                MediationManager.Instance.DebugLog("=> KHÔNG có ad inter nào -> load lại + báo fail");
                LoadInterstitial();
                onFailed?.Invoke();
                return;
            }

            //hoà -> ưu tiên MAX
            if (SecondaryInterstitialWins(maxRevenue, secondaryRevenue))
            {
                MediationManager.Instance.DebugLog(
                    $"=> CHỌN INTER ADMOB {Money(secondaryRevenue)} (> MAX {Money(maxRevenue)})");
                ShowSecondaryInterstitial(maxRevenue, showForRewarded, onInterstitialClosed, onFailed);
                return;
            }

            MediationManager.Instance.DebugLog(
                $"=> CHỌN INTER MAX {Money(maxRevenue)} " +
                $"(AdMob {Money(secondaryRevenue)}, hoà thì MAX thắng)");

            var a = maxEntry.Value;
            MultiCall.Instance.dateTimeTimeShow = TimeUtils.UTCNow;
            //KHÔNG xoá giá ở đây — xem MarkAdConsumed: ad chỉ tính là đã tiêu khi SDK báo nó rời kho.
            OpenShowSession();
            //adId = adViewId — xem lý do ở nhánh rewarded.
            idAd = FalconBigDataController.Ad.CurrentViewId(AdType.Interstitial) ?? Guid.NewGuid().ToString();
            new CSAdStart(idAd, AdType.Interstitial.ToString(), placementId, a.networkName,
                MediationType.ToString(), maxEntry.Key).Send();
            FsnAdManager.Instance.ShowInterstitialThenNative(
                showInterstitial: callbacks =>
                {
                    this.onInterstitialClosed = callbacks.OnHidden;
                    onInterstitialFailed = callbacks.OnDisplayFailed;
                    InternalShowInterstitial(maxEntry.Key);
                },
                onSuccess: () =>
                {
                    onInterstitialClosed?.Invoke();
                }, //success là hàm callback sẽ đc gọi sau khi xem xong quảng cáo interstitial, do user tự code
                where: placementId,
                onFailed: () =>
                {
                    onFailed?.Invoke();
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

        #region so giá & chiếu ad của nguồn phụ (AdMob bước B + C)

        //MAX dùng -100 cho "chưa có ad / vừa tiêu ad", tầng AdMob dùng -1: mọi giá trị < 0 đều là
        //"không có ad", nên chỗ nào so giá cũng chỉ cần kiểm tra dấu.
        private const double _NO_AD = -100d;

        //adMediation của nguồn phụ trên log — khớp với giá trị AdmobMediationFalconBridge gửi big data.
        private const string _SECONDARY_MEDIATION = "Admob";

        //Firebase: một event cho MỖI impression mà tầng AdMob thắng giá, mang phần doanh thu tăng thêm
        //so với ad MAX đắt nhất bị bỏ lại. Lượt nào MAX thắng thì KHÔNG bắn — event này chỉ để đo
        //"tầng meta AdMob đem về thêm bao nhiêu", không phải để đếm impression.
        private const string _ADMOB_IMPRESSION_EVENT = "meta_mediation_admob_impression";
        private const string _UPLIFT_REVENUE_PARAM = "uplift_revenue";

        /// <summary>
        /// Bảng so giá đầy đủ của một lượt show: MỌI ad đang giữ của cả hai nguồn kèm revenue, rồi
        /// dòng kết luận ai thắng. Đây là thứ đọc đầu tiên khi hỏi "sao lại show cái này".
        ///
        /// <paramref name="alsoInterstitial"/> = true cho lượt show rewarded: inter được phép chiếu
        /// thay rewarded nên nó cũng là ứng viên, phải liệt kê ra mới hiểu vì sao rewarded thua.
        /// </summary>
        private string DescribeShowCandidates(AdType type, bool alsoInterstitial)
        {
            var sb = new StringBuilder();
            sb.Append("SHOW ").Append(type.ToString().ToUpperInvariant())
                .Append(" @ '").Append(placementId).Append('\'')
                .Append(" | algorithm=").Append(IndexProvider);

            AppendCandidates(sb, type, crossShow: false);
            if (alsoInterstitial) AppendCandidates(sb, AdType.Interstitial, crossShow: true);

            return sb.ToString();
        }

        private void AppendCandidates(StringBuilder sb, AdType type, bool crossShow)
        {
            var slots = type == AdType.Reward
                ? MultiCall.Instance.dictionaryRewarded
                : MultiCall.Instance.dictionaryInterstitial;

            var secondaryBest = SecondaryAdSource.BestRevenue(type);

            sb.Append(DescribeMaxSlots(type, slots, crossShow));

            sb.Append("\n  [AdMob] ").Append(type).Append("  best=").Append(Money(secondaryBest));
            sb.Append('\n').Append(SecondaryAdSource.DescribeCache(type));
        }

        /// <summary>
        /// Nhãn bậc của slot trong log show ("default", "x1.5"). Provider override để in thêm hệ số gốc
        /// -> hệ số hiện tại khi đã bị giảm (vd. "x1.5->1.3").
        /// </summary>
        protected virtual string DescribeSlotLevel(AdType type, MultiCall.AdTypeMultiCall slot)
            => slot.multiplier == 0
                ? "default"
                : "x" + slot.multiplier.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Đuôi của dòng tổng quan trong log show (vd. " slot floor=3/3"); mặc định rỗng.</summary>
        protected virtual string DescribeFloorSlots(AdType type) => string.Empty;

        /// <summary>
        /// Liệt kê từng ad unit MAX của format: bậc, hệ số, giá đang giữ (eCpm &lt; 0 = không có ad), VÀ
        /// trạng thái thật trong SDK.
        ///
        /// Phải in cả <c>ready</c> lẫn <c>isWaiting</c> cạnh giá, vì eCpm là con số DUY NHẤT dùng để so
        /// giá còn ready mới là kho thật của MAX — hai thứ đó lệch nhau là mọi lượt show sau đều sai,
        /// mà nhìn riêng giá thì không thấy gì:
        ///  - ready=False mà CÓ giá  -> giá ma: nó đè lên ad thật của nguồn phụ rồi show hỏng.
        ///  - ready=True mà KHÔNG giá -> ad nằm chết trong SDK, không ai đem ra so giá.
        /// isWaiting cho biết slot rỗng đó có đang được nạp lại hay đã bị bỏ quên hẳn.
        /// </summary>
        private string DescribeMaxSlots(AdType type, Dictionary<string, MultiCall.AdTypeMultiCall> slots,
            bool crossShow)
        {
            var sb = new StringBuilder();
            sb.Append("\n-- MAX ").Append(type == AdType.Reward ? "rewarded" : "interstitial");
            if (crossShow) sb.Append(" (có thể được show chéo thay rewarded)");
            sb.Append(" --");

            var baseValue = type == AdType.Reward
                ? MultiCall.Instance.valueDefaultRewarded
                : MultiCall.Instance.valueDefaultInterstitial;
            sb.Append("\n    algo=").Append(IndexProvider)
                .Append("    base=").Append(Usd(baseValue))
                .Append(DescribeFloorSlots(type));

            if (slots == null || slots.Count == 0) return sb.Append("\n    (không có ad unit MAX nào)").ToString();

            var best = BestEntry(slots).Value;
            foreach (var pair in slots.OrderBy(x => x.Value?.index ?? int.MaxValue))
            {
                var slot = pair.Value;
                if (slot == null) continue;

                var ready = type == AdType.Reward
                    ? IsRewardedVideoReady(pair.Key)
                    : IsInterstitialReady(pair.Key);
                var hasPrice = slot.eCpm >= 0d;

                //Loaded = đang giữ ad; Loading = đang chờ callback; Waiting = trống, chờ retry / chờ tới lượt
                var state = hasPrice ? "Loaded" : slot.isWaiting ? "Loading" : "Waiting";

                //có ad: số lần fail trước lần load ra ad đó; trống: fail liên tiếp (request đang bay chưa tính)
                var fails = hasPrice
                    ? slot.failCountBeforeLoad
                    : slot.isWaiting ? Math.Max(0, slot.countLoadAttempt - 1) : slot.countLoadAttempt;

                sb.Append("\n    [").Append(DescribeSlotLevel(type, slot)).Append("] ").Append(pair.Key)
                    .Append(" state=").Append(state)
                    .Append(" ready=").Append(ready)
                    .Append(" eCpm=").Append(Usd(slot.eCpm));

                //floor cùng thang với eCpm (USD / impression); currentFloor lưu theo eCPM nên chia lại
                if (slot.multiplier != 0)
                    sb.Append(" floor=").Append(Usd(slot.currentFloor / MultiCall.MULTIPLIER));

                sb.Append(" fails=").Append(fails)
                    .Append(" network=").Append(string.IsNullOrEmpty(slot.networkName) ? "?" : slot.networkName);

                if (hasPrice && ReferenceEquals(slot, best)) sb.Append("   <== MAX BEST");

                if (ready != hasPrice)
                {
                    sb.Append(ready
                        ? "   <== LỆCH: SDK còn ad nhưng cache không có giá -> không ai đem ra so"
                        : "   <== LỆCH: cache còn giá nhưng SDK hết ad -> giá ma");
                }
            }

            return sb.ToString();
        }

        private static string Money(double revenue)
            => revenue < 0d ? "(không có ad)" : "$" + revenue.ToString("F6", CultureInfo.InvariantCulture);

        //in nguyên số (kể cả -100 = không có ad) để khớp đúng giá trị đang dùng so giá
        private static string Usd(double value) => "$" + value.ToString("F6", CultureInfo.InvariantCulture);

        /// <summary>
        /// Soát lại cache giá của MAX với kho THẬT trong SDK, chạy ngay trước mỗi lượt so giá.
        ///
        /// <c>eCpm</c> là con số duy nhất dùng để so giá nhưng nó chỉ được cập nhật qua callback, mà MAX
        /// đang bị tắt auto-retry (<c>disable_auto_retries</c>, xem <c>MultiCall.CallAfterInit</c>) nên
        /// ad hết hạn / bị SDK thu hồi là im lặng tuyệt đối: không callback, không tự nạp lại. Hai kiểu
        /// lệch cùng được vá ở đây:
        ///
        ///  - cache CÓ giá mà SDK hết ad -> "giá ma": nó đè lên ad thật của nguồn phụ rồi show hỏng.
        ///  - slot rỗng, không ai đang xin lại -> bị bỏ quên hẳn, vì đường hồi sinh duy nhất của một ad
        ///    unit là callback hidden/display-failed của CHÍNH nó, còn cascade sau OnAdLoaded thì không
        ///    bao giờ chạm tới các unit bậc default.
        ///
        /// Slot rỗng mà KHÔNG ai đang xin lại thì gọi <c>LoadXAd</c>; hàm đó còn tự gác thêm bằng
        /// <c>!IsXReady &amp;&amp; !isWaiting</c> nên gọi thừa KHÔNG tạo request trùng.
        ///
        /// Slot đang <c>isWaiting</c> thì để yên, KHÔNG đặt hạn chờ: có network trả callback rất muộn
        /// (quá 1 phút), tự ý coi là hỏng chỉ tạo thêm request cho một lượt load vẫn còn sống.
        ///
        /// Chiều ngược lại (SDK CÒN ad mà cache không có giá) cố ý KHÔNG tự vá: giá của ad đó chỉ có
        /// trong callback loaded, bịa ra một con số sẽ làm sai cả so giá lẫn uplift. Nó chỉ được đánh
        /// dấu LỆCH trong log của <see cref="DescribeMaxSlots"/>.
        /// </summary>
        private void SyncMaxSlotsWithSdk(AdType type)
        {
            var slots = type == AdType.Reward
                ? MultiCall.Instance.dictionaryRewarded
                : MultiCall.Instance.dictionaryInterstitial;
            if (slots == null || slots.Count == 0) return;

            //gom key rồi mới nạp lại: LoadXAd duyệt chính dictionary này, đừng gọi trong lúc đang foreach
            List<string> needLoad = null;

            foreach (var pair in slots)
            {
                var slot = pair.Value;
                if (slot == null) continue;

                var ready = type == AdType.Reward
                    ? IsRewardedVideoReady(pair.Key)
                    : IsInterstitialReady(pair.Key);
                if (ready) continue;

                if (slot.eCpm >= 0d)
                {
                    MediationManager.Instance.DebugError(
                        $"MAX {type} '{pair.Key}' đang giữ giá {Money(slot.eCpm)} nhưng SDK KHÔNG còn ad " +
                        $"-> xoá giá ma (isWaiting={slot.isWaiting}, true = đã có lượt load đang chạy).");
                    slot.eCpm = _NO_AD;
                }

                //đang chờ callback thì để yên, KHÔNG đặt hạn: có network trả về rất muộn (trên 1 phút),
                //cắt ngang chỉ tạo request thừa cho một lượt load vẫn còn sống.
                if (slot.isWaiting) continue;

                if (needLoad == null) needLoad = new List<string>();
                needLoad.Add(pair.Key);
            }

            if (needLoad == null) return;

            for (var i = 0; i < needLoad.Count; i++)
            {
                if (type == AdType.Reward) LoadRewardedAd(needLoad[i]);
                else LoadInterstitialAd(needLoad[i]);
            }
        }

        /// <summary>
        /// Slot có eCpm cao nhất. Thay cho OrderByDescending().FirstOrDefault() cũ: dictionary rỗng
        /// (game không bật multi-call, hoặc chỉ chạy AdMob) thì FirstOrDefault trả về entry mặc định có
        /// Value = null -> đọc .eCpm là NRE.
        /// </summary>
        private static KeyValuePair<string, MultiCall.AdTypeMultiCall> BestEntry(
            Dictionary<string, MultiCall.AdTypeMultiCall> slots, Func<string, bool> isReady = null)
        {
            var best = default(KeyValuePair<string, MultiCall.AdTypeMultiCall>);
            if (slots == null) return best;

            var bestRevenue = double.NegativeInfinity;
            foreach (var pair in slots)
            {
                if (pair.Value == null || pair.Value.eCpm <= bestRevenue) continue;
                if (isReady != null && !isReady(pair.Key)) continue;

                bestRevenue = pair.Value.eCpm;
                best = pair;
            }

            return best;
        }

        private static double RevenueOf(KeyValuePair<string, MultiCall.AdTypeMultiCall> entry)
            => entry.Value?.eCpm ?? _NO_AD;

        public double BestMaxRevenue(AdType type)
        {
            var entry = type == AdType.Reward
                ? BestEntry(MultiCall.Instance.dictionaryRewarded, IsRewardedVideoReady)
                : BestEntry(MultiCall.Instance.dictionaryInterstitial, IsInterstitialReady);
            return RevenueOf(entry);
        }

        /// <summary>
        /// Ad của mediation ngoài đã RỜI KHO SDK -> xoá giá đang cache, để lượt so giá sau không chọn
        /// phải một slot rỗng. Gọi ở đúng ba mốc mà SDK khẳng định ad không còn dùng lại được:
        /// displayed, display-failed, hidden.
        ///
        /// Trước đây mốc này nằm ngay trong hàm show, TRƯỚC khi SDK thật sự chiếu. Lượt show nào không
        /// tới được SDK (hoặc callback show bị mất) là slot mất giá vĩnh viễn trong khi kho SDK vẫn còn
        /// ad -> <c>LoadXAd</c> bỏ qua vì <c>IsXReady</c> vẫn true, không ai nạp lại, slot nằm chết cả
        /// session. Đó cũng là lý do phải xoá ở CẢ display-failed: show hỏng thì MAX huỷ ad, giá cũ còn
        /// lại là giá ma và sẽ đè lên ad thật của nguồn phụ ở lượt sau.
        /// </summary>
        protected static void MarkAdConsumed(Dictionary<string, MultiCall.AdTypeMultiCall> slots, string adUnitId)
        {
            if (slots == null || string.IsNullOrEmpty(adUnitId)) return;
            if (!slots.TryGetValue(adUnitId, out var slot) || slot == null) return;

            slot.eCpm = _NO_AD;
        }

        //maxRevenue = giá ad inter MAX đắt nhất vừa bị bỏ lại ở nhánh so giá, mang theo chỉ để tính
        //uplift lúc ad AdMob lên hình — đọc lại ở đó là ra số khác vì MAX có thể đã load thêm ad mới.
        private void ShowSecondaryInterstitial(
            double maxRevenue, bool showForRewarded, Action onInterstitialClosed, Action onFailed)
        {
            //chụp lại TRƯỚC khi show: ngay khi ad lên hình nó bị rút khỏi kho nên hỏi lại là ra ad khác
            var revenue = SecondaryAdSource.BestRevenue(AdType.Interstitial);
            var adUnitId = SecondaryAdSource.BestAdUnitId(AdType.Interstitial);
            var network = SecondaryAdSource.BestNetwork(AdType.Interstitial);

            MediationManager.Instance.DebugLog("Show inters ads AdMob : " + adUnitId + " revenue=" + revenue);

            MultiCall.Instance.dateTimeTimeShow = TimeUtils.UTCNow;
            OpenShowSession();
            //adId = adViewId — xem lý do ở nhánh rewarded.
            idAd = FalconBigDataController.Ad.CurrentViewId(AdType.Interstitial) ?? Guid.NewGuid().ToString();
            new CSAdStart(idAd, AdType.Interstitial.ToString(), placementId, network, _SECONDARY_MEDIATION,
                adUnitId).Send();

            FsnAdManager.Instance.ShowInterstitialThenNative(
                showInterstitial: callbacks =>
                {
                    var shown = SecondaryAdSource.Show(AdType.Interstitial, placementId,
                        onReward: null,
                        onHidden: () =>
                        {
                            //tầng AdMob không có mốc "displayed" riêng; ad đã đóng nghĩa là đã lên hình
                            MediationManager.Instance.NumberShowInterstitial++;
                            InterstitialPersonalization.OnInterstitialDisplayed(null,
                                MultiCall.ToEcpm(revenue));
                            FinishSecondaryShow(AdType.Interstitial, adUnitId, network, revenue,
                                maxRevenue);
                            GameEvent.Emit(showForRewarded ? _REWARDED_CLOSED : _INTERSTITIAL_CLOSED);
                            callbacks.OnHidden();
                        },
                        onFail: () =>
                        {
                            ConsumeShowSession();
                            callbacks.OnDisplayFailed();
                        },
                        onPaid: SecondaryAdInfoSender(AdType.Interstitial));

                    //hết ad đúng lúc gọi show -> không callback nào được bắn, tự trả fail
                    if (!shown)
                    {
                        ConsumeShowSession();
                        callbacks.OnDisplayFailed();
                    }
                },
                onSuccess: () => { onInterstitialClosed?.Invoke(); },
                where: placementId,
                onFailed: () => { onFailed?.Invoke(); }
            );

            GameEvent.Emit(showForRewarded ? _REWARDED_DISPLAYED : _INTERSTITIAL_DISPLAYED);
        }

        //maxRevenue: xem ghi chú ở ShowSecondaryInterstitial — giá ad rewarded MAX đắt nhất bị bỏ lại.
        private void ShowSecondaryRewarded(double maxRevenue, Action onRewardedComplete, Action onFailed)
        {
            var revenue = SecondaryAdSource.BestRevenue(AdType.Reward);
            var adUnitId = SecondaryAdSource.BestAdUnitId(AdType.Reward);
            var network = SecondaryAdSource.BestNetwork(AdType.Reward);

            MediationManager.Instance.DebugLog("Show rewarded ads AdMob : " + adUnitId + " revenue=" + revenue);

            MultiCall.Instance.dateTimeTimeShow = TimeUtils.UTCNow;
            OpenShowSession();
            //adId = adViewId — xem lý do ở nhánh rewarded.
            idAd = FalconBigDataController.Ad.CurrentViewId(AdType.Reward) ?? Guid.NewGuid().ToString();
            new CSAdStart(idAd, AdType.Reward.ToString(), placementId, network, _SECONDARY_MEDIATION,
                adUnitId).Send();

            //cùng cặp cờ với nhánh MAX -> CheckReceivedRewarded() dùng lại được nguyên vẹn
            closedRewarded = false;
            receivedRewarded = false;

            FsnAdManager.Instance.ShowRewardedThenNative(
                showRewarded: callbacks =>
                {
                    var shown = SecondaryAdSource.Show(AdType.Reward, placementId,
                        onReward: () =>
                        {
                            receivedRewarded = true;
                            callbacks.OnRewardReceived();
                            CheckReceivedRewarded();
                        },
                        onHidden: () =>
                        {
                            AdSessionCounter.OnRewardedShown();
                            FinishSecondaryShow(AdType.Reward, adUnitId, network, revenue, maxRevenue);
                            GameEvent.Emit(_REWARDED_CLOSED);
                            closedRewarded = true;
                            callbacks.OnHidden();
                            CheckReceivedRewarded();
                        },
                        onFail: () =>
                        {
                            ConsumeShowSession();
                            callbacks.OnDisplayFailed();
                        },
                        onPaid: SecondaryAdInfoSender(AdType.Reward));

                    if (!shown)
                    {
                        ConsumeShowSession();
                        callbacks.OnDisplayFailed();
                    }
                },
                onSuccess: () => { onRewardedComplete?.Invoke(); },
                where: placementId,
                onFailed: () => { onFailed?.Invoke(); }
            );

            GameEvent.Emit(_REWARDED_DISPLAYED);
        }

        //CSAdFinish của lượt show nguồn phụ. Revenue lấy từ giá đọc được lúc load (ad_value_string) —
        //đúng con số đã dùng để so giá, và cũng là mức AdMob sẽ trả.
        private void FinishSecondaryShow(AdType type, string adUnitId, string network, double revenue,
            double maxRevenue)
        {
            if (!ConsumeShowSession())
            {
                MediationManager.Instance.DebugError(
                    "AdMob > bỏ qua CSAdFinish lặp của lượt show đã kết thúc : " + adUnitId);
                return;
            }

            var time = (int)(TimeUtils.UTCNow - MultiCall.Instance.dateTimeTimeShow).TotalSeconds;
            new CSAdFinish(idAd, type.ToString(), Placement(placementId), network, _SECONDARY_MEDIATION,
                adUnitId, revenue < 0d ? 0d : revenue, time).Send();

            //Cùng bộ log phiên mà ad MAX nhận ở FalconMaxService (displayed/hidden) — trước đây lượt nguồn phụ
            //(meta AdMob / DFPool) thiếu: start_/finish_, Firebase show, AdsClosed của interstitial (cap/interval).
            //Nguồn phụ không có mốc displayed riêng -> bắn cùng lúc đóng. MMP af_*_displayed do nguồn phụ tự bắn.
            try
            {
                var actionLog = new Dictionary<string, string>
                {
                    { "ad_where", Placement(placementId) },
                    { "ad_network", network }
                };
                FActionLogManager.Log($"start_{type}", actionLog);
                FalconFirebaseLog.Log(type == AdType.Reward
                    ? FalconFirebaseLog.ADS_REWARD_SHOW_SUCCESS
                    : FalconFirebaseLog.AD_INTER_SHOW);
                FActionLogManager.Log($"finish_{type}", actionLog);
                if (type == AdType.Interstitial) MediationManager.Instance.AdsClosed(AdType.Interstitial, placementId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            //Chỉ tới đây mới bắn: hàm này nằm sau ConsumeShowSession nên đúng MỘT lần cho MỘT ad AdMob
            //đã lên hình, và cả hai lối vào nó đều là nhánh "AdMob thắng giá".
            var uplift = UpliftRevenue(revenue, maxRevenue);
            MediationManager.Instance.DebugLog(
                $"AdMob thắng giá -> Firebase {_ADMOB_IMPRESSION_EVENT} " +
                $"{_UPLIFT_REVENUE_PARAM}={Money(uplift)} " +
                $"(AdMob {Money(revenue)} - MAX {Money(maxRevenue)})");
            FalconFirebaseLog.LogNumber(_ADMOB_IMPRESSION_EVENT, _UPLIFT_REVENUE_PARAM, uplift);
        }

        /// <summary>
        /// Phần doanh thu TĂNG THÊM của lượt show này: giá ad AdMob vừa chiếu trừ giá ad MAX đắt nhất
        /// bị bỏ lại.
        ///
        /// Hai mốc "không có ad" (-100 của MAX, -1 của AdMob) KHÔNG phải giá thật nên phải quy về 0
        /// trước khi trừ — để nguyên thì lượt nào MAX hết ad cũng ra uplift +100, đủ để một mình nó
        /// kéo lệch cả biểu đồ trên Firebase.
        /// </summary>
        private SecondaryAdPaid SecondaryAdInfoSender(AdType type)
        {
            var showIdAd = idAd;
            var showPlacement = Placement(placementId);
            var showStartedAt = MultiCall.Instance.dateTimeTimeShow;
            return (mediation, network, paidRevenue) =>
            {
                var time = (int)(TimeUtils.UTCNow - showStartedAt).TotalSeconds;
                new CSAdInfo(showIdAd, showPlacement, type.ToString(), network, mediation, time, paidRevenue)
                    .Send();
            };
        }

        private static double UpliftRevenue(double secondaryRevenue, double maxRevenue)
        {
            var admob = secondaryRevenue < 0d ? 0d : secondaryRevenue;
            var max = maxRevenue < 0d ? 0d : maxRevenue;
            return admob - max;
        }

        #endregion

        /// <summary>
        /// Load interstitial ads to show
        /// </summary>
        public virtual void LoadInterstitial()
        {
        }

        protected virtual void InternalLoadInterstitial(string adUnitId)
        {
        }

        /// <summary>
        /// Cổng cuối trước khi thật sự gửi request interstitial — xem <see cref="CanLoadRewarded"/>.
        /// </summary>
        protected virtual bool CanLoadInterstitial(MultiCall.AdTypeMultiCall a)
        {
            return true;
        }

        //đã có ít nhất một request interstitial lọt qua lượt chặn của Floor Personalization chưa
        private bool _interstitialLoadIssued;

        /// <summary>
        /// Floor Personalization vừa nhả chặn (opportunity rơi về Default). Lượt khởi động đầu phiên bị
        /// chặn trọn -> chạy lại đúng lượt khởi động (<see cref="LoadInterstitial"/>, có reset hệ số).
        /// Đã chạy rồi thì chỉ nạp lại slot rỗng: chuỗi refill/retry (DelayCall) bị cắt trong lúc chặn,
        /// còn hệ số floor đang thích nghi thì phải giữ nguyên.
        /// </summary>
        public void ResumeInterstitialLoading()
        {
            if (!_interstitialLoadIssued) LoadInterstitial();
            else SyncMaxSlotsWithSdk(AdType.Interstitial);
        }

        protected void LoadInterstitialAd(string adUnitId)
        {
            //Floor Personalization đang chờ Rule / đang chạy workflow cho opportunity này -> multicall
            //đứng yên. Nhả chặn thì personalization gọi ResumeInterstitialLoading().
            if (InterstitialPersonalization.Current.BlocksDefaultLoading) return;
            _interstitialLoadIssued = true;

            //kiểm tra xem có đang dùng 3G ko
            if (MultiCall.Instance.IsUseMobileData())
            {
                throttled = true;
            }

            if (MultiCall.Instance.dictionaryInterstitial.ContainsKey(adUnitId))
            {
                var a = MultiCall.Instance.dictionaryInterstitial[adUnitId];
                // MediationManager.Instance.DebugLog("Load Interstitial Ad : " + adUnitId);

                foreach (var multiCall in MultiCall.Instance.dictionaryInterstitial)
                {
                    //id default thì bỏ qua
                    if (a.multiplier == 0 || !SkipLoadWhenHigherTierLoaded) break;
                    if (multiCall.Value.eCpm >= 0 && multiCall.Value.multiplier > a.multiplier && a.multiplier != -1)
                    {
                        //đã load được qc có multiplier > id này
                        // MediationManager.Instance.DebugLog("đã load được qc : " + multiCall.Key + " x " +
                        //                                    multiCall.Value.multiplier + " > " + adUnitId + " x " +
                        //                                    a.multiplier);
                        ResetMultiplier(adUnitId);
                        return;
                    }
                }

                //chưa có quảng cáo
                //đang ko trong trạng thái chờ callback quảng cáo

                if (!IsInterstitialReady(adUnitId) && !a.isWaiting && CanLoadInterstitial(a))
                {
                    if (!throttled || (throttled && a.index == 0))
                    {
                        InternalLoadInterstitial(adUnitId);

                        a.timeLoad = TimeUtils.UTCNow;
                        a.countLoadAttempt++;
                        a.isWaiting = true;
                        if (LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId) == null)
                        {
                            var adRequestLogItem = new AdRequestLogItem()
                            {
                                adUnitId = adUnitId,
                                adType = AdType.Interstitial.ToString().ToLower(),
                                decisionPolicy = SaveLoadHandler.Load("decision_policy_load_ad",
                                    "client-" + MultiCall.ProviderIndex()),
                                numRequest = 0,
                                errorDetail = new Dictionary<string, int>(),
                                adSuccessInfos = new List<AdSuccessInfo>()
                            };
                            LogInstance.Instance.AddAdLog(adRequestLogItem);
                        }

                        if (LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId) != null)
                        {
                            LogInstance.Instance.GetAdRequestLogItemByAdUnitId(adUnitId).numRequest++;
                        }
                    }
                }
            }
        }

        #endregion
    }
}