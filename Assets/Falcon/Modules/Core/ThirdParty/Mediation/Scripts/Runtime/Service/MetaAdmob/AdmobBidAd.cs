using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Falcon.Admob
{
    /// <summary>Tham số dùng chung cho mọi slot của cùng một format ad.</summary>
    public class AdmobBidSettings
    {
        /// <summary>Format của ad — dùng để bắn doanh thu đúng loại và hỏi giá mediation ngoài.</summary>
        public AdmobAdFormat format;

        public bool enableLog;

        /// <summary>
        /// Bật nếu tài khoản trả extras "ad_value_string" theo MICROS (giống AdValue.Value của OnAdPaid)
        /// thay vì theo đơn vị tiền tệ. Xem log "extras[...]" trên máy thật để xác định.
        /// </summary>
        public bool adValueInMicros;

        /// <summary>
        /// Nguồn publisher id để khai báo auction/impression. Là DELEGATE chứ không phải chuỗi vì id đó
        /// suy ra từ ad unit id bước B — thứ đến từ remote config và có thể đổi giữa phiên; chụp một
        /// lần lúc dựng slot là dính giá trị rỗng của lúc config chưa về.
        /// </summary>
        public Func<string> publisherIdProvider;

        /// <summary>
        /// Gom response id của các ad AdMob đã THUA lượt show vừa chiếu — mọi ad đang nằm trong kho của
        /// CÙNG format, ở mọi slot, load xong trước mốc truyền vào — để khai báo chung vào impression của
        /// ad thắng. Là delegate vì một slot không tự biết các slot anh em; tầng format
        /// (<see cref="AdmobFullscreenAds"/>) gán nó trỏ về pool.
        /// </summary>
        public Action<List<string>, DateTime> lostResponseIdsCollector;

        /// <summary>Gom id các ad thua cùng lượt vào <paramref name="into"/>; lỗi thì bỏ qua, không gom gì.</summary>
        public void CollectLostResponseIds(List<string> into, DateTime loadedBefore)
        {
            try
            {
                lostResponseIdsCollector?.Invoke(into, loadedBefore);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        /// <summary>Publisher id tại thời điểm gọi. Rỗng = bỏ qua phần khai báo multi-platform.</summary>
        public string PublisherId
        {
            get
            {
                try
                {
                    return publisherIdProvider?.Invoke() ?? string.Empty;
                }
                catch (Exception e)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError(e);
#endif
                    return string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// Một "kho ad" gắn với đúng MỘT ad unit AdMob. Mỗi lần <c>Load</c> thành công, SDK trả về một ad
    /// object HOÀN TOÀN ĐỘC LẬP nên kho GIỮ LẠI TẤT CẢ, không huỷ ad cũ đi: lúc show mới chọn ad đắt
    /// nhất trong kho. Mỗi ad nhớ kèm giá sàn đã xin và revenue AdMob trả về NGAY LÚC LOAD.
    ///
    /// Có 2 loại slot (xem <see cref="AdmobBidPool"/>), mỗi loại đi một AD UNIT ID RIÊNG:
    ///  - slot "default" (BƯỚC B, ad unit trong <see cref="AdmobMediationConfig"/>): load bằng AdRequest
    ///    trơn, không set sàn -> giá AdMob tự nhiên.
    ///  - slot "floor"   (BƯỚC C, ad unit lấy TỪ REMOTE CONFIG): load với extras
    ///    {"rusd": eCPM-tính-bằng-cent của (revenue mediation ngoài × hệ số của biến thể)} -> ép AdMob
    ///    phải trả giá cao hơn mức đó, không đủ giá thì no-fill.
    ///
    /// Revenue KHÔNG lấy từ OnAdPaid (event đó chỉ bắn lúc impression, quá muộn để so giá) mà lấy từ
    /// ResponseInfo.GetResponseExtras()["ad_value_string"] ngay khi load xong.
    ///
    /// Kho KHÔNG giới hạn số ad. Ad chỉ bị huỷ trong đúng 2 trường hợp: đã show xong, hoặc hết hạn
    /// (<c>CanShowAd()</c> = false — AdMob cho ad sống ~1 giờ nên kho tự co lại theo thời gian).
    /// </summary>
    public abstract class AdmobBidAd
    {
        /// <summary>Slot không có ad sẵn sàng -> thua mọi ứng viên khác khi so giá.</summary>
        public const double NoAd = -1d;

        /// <summary>
        /// Có ad nhưng AdMob không trả ad_value (tài khoản chưa bật, hoặc khác đơn vị tiền tệ).
        /// Thắng "không có gì" nhưng thua mọi mức giá thật -> mediation ngoài được ưu tiên khi hoà.
        /// </summary>
        public const double UnknownRevenue = 0d;

        private const string _AdValueKey = "ad_value_string";
        private const string _AdValueCurrencyKey = "ad_value_currency";
        private const string _BidFloorKey = "rusd";
        private const string _Usd = "USD";
        private const double _MicrosPerUnit = 1_000_000d;

        // Mediation ngoài trả revenue = doanh thu của ĐÚNG MỘT impression (USD), vd $0.01234. Giá sàn
        // AdMob lại theo quy ước eCPM (USD / 1000 impression) -> phải ×1000 mới ra giá USD để so; rusd
        // tính bằng CENT nên ×100 lần nữa. Tổng: revenue × 100_000 (0.01234 -> eCPM $12.34 -> "1234").
        private const double _ImpressionsPerEcpm = 1000d;
        private const double _CentsPerUsd = 100d;
        private const string _BidFloorFormat = "0.##";
        private const double _MinBidFloorCents = 0.005d;

        // Giá gửi cho AdMob theo USD thập phân chuẩn (0.0075), KHÔNG phải micros/cent: hob của
        // RecordAuctionEvent, và piv của impression mà mediation ngoài thắng giá.
        private const string _UsdDecimalFormat = "0.########";

        // piv của RecordImpressionEvent: AdValue.Value là micros -> quy về USD thập phân chuẩn.
        private const decimal _MicrosPerUnitDecimal = 1_000_000m;

        protected readonly string _adUnitId;
        protected readonly AdmobBidSettings _settings;

        private readonly MonoBehaviour _runner;

        private Coroutine _retryRoutine;
        private int _retryAttempt;
        private bool _loading;
        private bool _showing;
        private bool _disposed;

        // Floor được yêu cầu trong lúc slot đang có request bay -> chạy ngay khi request đó xong.
        private double? _queuedFloorUsd;
        private double? _queuedTier;

        // Mã lượt request (do AdmobBidPool cấp cho thác giá bước C) của lần load đang bay / đang xếp hàng.
        // Trả lại nguyên vẹn qua loadFinished để pool không nhận nhầm kết quả của lượt cũ.
        private int _requestId;
        private int _queuedRequestId;

        /// <summary>
        /// (slot, requestId, success) — bắn MỖI lần một request load xong (thành công hoặc thất bại).
        /// AdmobBidPool dùng để chạy thác giá bước C.
        /// </summary>
        internal Action<AdmobBidAd, int, bool> loadFinished;

        // Mốc bắt đầu request đang bay, để đo thời gian load của CHÍNH attempt đó. Dùng DateTime chứ
        // không phải Time.realtimeSinceStartup: callback của GMA có thể về trên thread nền, mà Unity API
        // chỉ gọi được từ main thread.
        private DateTime _loadStartedUtc;

        // Trạng thái của lượt show hiện tại.
        private string _where;
        private Action _onReward, _onHidden, _onFail;
        private bool _rewardEarned, _resultDelivered;

        // Mốc bắt đầu lượt show: ad load xong SAU mốc này không tham gia so giá của lượt đó, nên không
        // được tính là "thua cùng lượt" lúc khai báo impression.
        private DateTime _showStartedUtc;

        protected AdmobBidAd(MonoBehaviour runner, string adUnitId, string key, bool isDefaultSlot,
            AdmobBidSettings settings)
        {
            _runner = runner;
            _adUnitId = adUnitId;
            _settings = settings;
            Key = key;
            IsDefaultSlot = isDefaultSlot;
        }

        /// <summary>
        /// <see cref="AdmobBidPool.DefaultKey"/> cho slot bước B, hoặc "floor#k#i" — bậc mediation ngoài
        /// thứ k, ad unit thứ i trong nhóm của bậc đó theo remote config — cho slot bước C.
        /// </summary>
        public string Key { get; }

        /// <summary>Ad unit AdMob của slot này. Pool so với config để biết khi nào phải tạo slot mới.</summary>
        public string AdUnitId => _adUnitId;

        public bool IsDefaultSlot { get; }

        /// <summary>Giá sàn (USD) của request đang bay. 0 = không set sàn.</summary>
        public double BidFloorUsd { get; private set; }

        /// <summary>
        /// BẬC của request đang bay (multiplier của biến thể). null = slot bước B, không thuộc bậc
        /// nào. Đi kèm <see cref="BidFloorUsd"/> qua cả đường xếp hàng lẫn retry để telemetry gắn
        /// đúng bậc cho lần load này.
        /// </summary>
        public double? BidTier { get; private set; }

        /// <summary>Đang có request bay tới AdMob.</summary>
        public bool IsLoading => _loading;

        /// <summary>Đang trong một lượt show (chưa nhận callback đóng/lỗi).</summary>
        public bool IsShowing => _showing;

        /// <summary>Kho có ít nhất một ad còn show được không.</summary>
        public abstract bool IsReady { get; }

        /// <summary>Số ad còn show được đang giữ trong kho.</summary>
        public abstract int StoredAdCount { get; }

        /// <summary>
        /// Giá của ad ĐẮT NHẤT trong kho — đây cũng là ad sẽ được show nếu slot này thắng.
        /// <see cref="UnknownRevenue"/> nếu kho rỗng hoặc không đọc được giá.
        /// </summary>
        public abstract double Revenue { get; }

        /// <summary>Giá sàn đã xin của ad đắt nhất trong kho (0 = không set sàn). Chỉ để log.</summary>
        public abstract double LoadedFloorUsd { get; }

        /// <summary>
        /// Network thắng thầu của ad ĐẮT NHẤT trong kho — ad sẽ được chiếu nếu slot này thắng giá.
        /// null khi kho rỗng. Chỉ để log (CSAdStart của lượt show).
        /// </summary>
        public abstract string BestNetwork { get; }

        /// <summary>Giá dùng để so sánh: <see cref="NoAd"/> khi chưa có ad nào sẵn sàng.</summary>
        public double AvailableRevenue => IsReady ? Revenue : NoAd;

        /// <summary>ResponseInfo của ad ĐANG SHOW (null nếu không show ad nào).</summary>
        protected abstract ResponseInfo CurrentResponseInfo { get; }

        /// <summary>Giá sàn đã xin + giá lúc load (USD/impression) của ad ĐANG SHOW; null nếu không show ad nào.</summary>
        protected abstract (double floorUsd, double revenue)? CurrentAdPrice { get; }

        /// <summary>
        /// ResponseInfo của ad ĐẮT NHẤT trong kho (null nếu kho rỗng) — ad sẽ được chiếu nếu slot này
        /// thắng giá.
        /// </summary>
        protected abstract ResponseInfo BestResponseInfo { get; }

        /// <summary>
        /// Gom response id của MỌI ad đang nằm trong kho mà CHƯA từng được khai báo thua, rồi đánh dấu
        /// chúng (cùng một lần khoá) — mỗi ad chỉ bị khai báo thua MỘT lần dù nằm lại kho qua nhiều lượt.
        /// Ad đang chiếu đã rút khỏi kho nên không bao giờ bị gom.
        /// </summary>
        /// <param name="into">Danh sách để gom id vào.</param>
        /// <param name="loadedBefore">Chỉ lấy ad load xong TRƯỚC mốc này (null = lấy hết).</param>
        public abstract void ClaimLostResponseIds(List<string> into, DateTime? loadedBefore);

        /// <summary>Một dòng mô tả slot để dump cache lúc show.</summary>
        public string Describe()
        {
            var kind = IsDefaultSlot ? "default" : "floor";
            var count = StoredAdCount;
            var state = count > 0
                ? $"{count} ad, best revenue=${Revenue:F6}{DescribeStoredAds()}"
                : (_loading ? "đang load" : "KHÔNG có ad");

            return $"slot '{Key}' ({kind}, adUnit={_adUnitId}) {state} floorAdBest=${LoadedFloorUsd:F6} " +
                   $"floorRequest=${BidFloorUsd:F6} loading={_loading} showing={_showing}";
        }

        /// <summary>Liệt kê từng ad trong kho (revenue + floor) để dump lúc show.</summary>
        protected abstract string DescribeStoredAds();

        protected abstract string LogTag { get; }

        #region load

        /// <summary>
        /// Xin AdMob thêm 1 ad cho slot này với giá sàn <paramref name="floorUsd"/> (&lt;= 0 = không set
        /// sàn). Ad đang có trong kho KHÔNG bị đụng tới — ad mới về chỉ nằm thêm vào kho.
        /// </summary>
        /// <param name="tier">BẬC của biến thể (multiplier); null cho slot bước B.</param>
        /// <param name="requestId">Mã lượt request, trả lại qua <see cref="loadFinished"/>; 0 = không theo dõi.</param>
        public void Request(double floorUsd, double? tier = null, int requestId = 0)
        {
            if (_disposed || string.IsNullOrEmpty(_adUnitId)) return;

            // Mỗi slot chỉ để một request bay cùng lúc (retry/backoff dựa vào trạng thái này). Đang show
            // KHÔNG phải lý do hoãn: ad mới về được cất thêm vào kho, không đụng ad đang chiếu.
            if (_loading)
            {
                _queuedFloorUsd = floorUsd;
                _queuedTier = tier;
                _queuedRequestId = requestId;
                Log($"XẾP HÀNG slot '{Key}' floor=${floorUsd:F6} — đang có request bay, " +
                    "sẽ load ngay khi request đó xong.");
                return;
            }

            // Kho đang giữ ad đắt hơn mức sàn sắp xin KHÔNG phải lý do bỏ qua: giá sàn chỉ là mức TỐI
            // THIỂU chứ không phải giá AdMob sẽ trả, nên một bậc hệ số thấp vẫn có thể mang về ad đắt
            // hơn mọi ad đang có. Ad cũ nằm nguyên trong kho nên không mất gì ngoài một request.
            CancelRetry();
            _requestId = requestId;
            StartLoad(floorUsd, tier);
        }

        private void StartLoad(double floorUsd, double? tier)
        {
            _loading = true;
            BidFloorUsd = floorUsd < 0d ? 0d : floorUsd;
            BidTier = tier;

            var request = new AdRequest();

            request.Extras.Add(
                AdmobMultiPlatformUtils.AUCTION_CONTEXT_KEY,
                AdmobMultiPlatformUtils.AUCTION_CONTEXT_PARALLEL);

            var uaChannel = AdmobMediationHooks.UaChannel();
            if (!string.IsNullOrEmpty(uaChannel))
            {
                request.Extras.Add(
                    AdmobMultiPlatformUtils.USER_ACQUISITION_CHANNEL_KEY,
                    AdmobMultiPlatformUtils.UserAcquisitionChannel.FromNetwork(uaChannel));
            }

            var daysSinceInstall = AdmobMediationHooks.DaysSinceInstall();
            if (daysSinceInstall.HasValue)
            {
                request.Extras.Add(
                    AdmobMultiPlatformUtils.DAYS_SINCE_INSTALL_KEY,
                    daysSinceInstall.Value.ToString(CultureInfo.InvariantCulture));
            }

            var floorParam = FormatBidFloor(BidFloorUsd);
            if (floorParam != null)
            {
                // Ghi thêm vào Extras chứ không gán đè cả dictionary — GMA có thể tự nhét key riêng vào đó.
                if (request.Extras == null) request.Extras = new Dictionary<string, string>();
                request.Extras[_BidFloorKey] = floorParam;
            }

            if (IsDefaultSlot)
            {
                Log($">>> BẮT ĐẦU LOAD AdMob slot '{Key}' (DEFAULT — KHÔNG set giá sàn) adUnit={_adUnitId}");
            }
            else
            {
                Log($">>> BẮT ĐẦU LOAD AdMob slot '{Key}' (FLOOR — biến thể thang giá) " +
                    $"floor=${BidFloorUsd:F6}/impression rusd={(floorParam ?? "none")} cent (eCPM) " +
                    $"adUnit={_adUnitId}");
            }

            // Mốc REQUEST của vòng đời ad view — bắn ngay trước khi gọi SDK, đúng chỗ tầng MAX bắn.
            // Retry cũng đi qua đây nên mỗi lần xin là một lần request, không gộp.
            _loadStartedUtc = DateTime.UtcNow;
            AdmobMediationHooks.AdRequested(_settings.format);

            LoadAd(request);
        }

        // Giá sàn đã xin quy sang eCPM USD; null cho slot bước B (không set sàn) để trường floor vắng
        // mặt thay vì báo 0 — cùng quy ước với tầng MAX.
        private double? FloorEcpmUsd => BidFloorUsd > 0d ? BidFloorUsd * _ImpressionsPerEcpm : (double?)null;

        // Giá/impression đọc được lúc load -> eCPM; không đọc được giá thì null (Databuckets để trống).
        private static double? EcpmOf(double revenue)
            => revenue > UnknownRevenue ? revenue * _ImpressionsPerEcpm : (double?)null;

        private long ElapsedLoadMs()
        {
            if (_loadStartedUtc == default) return 0L;

            var elapsed = (DateTime.UtcNow - _loadStartedUtc).TotalMilliseconds;
            return elapsed <= 0d ? 0L : (long)elapsed;
        }

        /// <summary>Subclass gọi SDK load ở đây, rồi báo lại qua StoreLoadedAd / OnLoadFailed.</summary>
        protected abstract void LoadAd(AdRequest request);

        /// <summary>Subclass gọi SAU khi đã cất ad mới vào kho.</summary>
        protected void OnLoadSucceeded(double revenue, ResponseInfo responseInfo)
        {
            _loading = false;
            _retryAttempt = 0;

            // ResolveAdSourceName phải sang tận native để hỏi -> gọi ĐÚNG MỘT LẦN, dùng chung cho cả
            // telemetry lẫn log.
            var network = AdmobRevenueReporter.ResolveAdSourceName(responseInfo);

            AdmobMediationHooks.LoadResult(new AdmobLoadResult
            {
                format = _settings.format,
                adUnitId = _adUnitId,
                success = true,
                floorEcpmUsd = FloorEcpmUsd,
                multiplier = BidTier,
                estimatedEcpmUsd = EcpmOf(revenue),
                network = network,
                loadingMs = ElapsedLoadMs()
            });

            if (LogEnabled)
            {
                var kind = IsDefaultSlot ? "DEFAULT" : "FLOOR";
                var price = revenue > UnknownRevenue ? $"${revenue:F6}" : "KHÔNG ĐỌC ĐƯỢC (coi như $0)";
                Log($"<<< LOAD THÀNH CÔNG slot '{Key}' ({kind}) floor=${BidFloorUsd:F6} " +
                    $"revenue={price} network={network} " +
                    $"-> kho còn {StoredAdCount} ad, best=${Revenue:F6}");
            }

            // Chụp mã lượt TRƯỚC: RunQueuedRequest ghi đè _requestId bằng mã của request xếp hàng.
            var finishedRequestId = _requestId;
            RunQueuedRequest();
            NotifyLoadFinished(finishedRequestId, true);
        }

        protected void OnLoadFailed(string message, string errorCode = null)
        {
            _loading = false;

            AdmobMediationHooks.LoadResult(new AdmobLoadResult
            {
                format = _settings.format,
                adUnitId = _adUnitId,
                success = false,
                floorEcpmUsd = FloorEcpmUsd,
                multiplier = BidTier,
                errorCode = errorCode,
                errorMessage = message,
                loadingMs = ElapsedLoadMs()
            });

            var kind = IsDefaultSlot ? "DEFAULT" : "FLOOR";
            Log($"<<< LOAD THẤT BẠI slot '{Key}' ({kind}) floor=${BidFloorUsd:F6}: {message}" +
                (IsDefaultSlot ? " -> sẽ retry backoff." : " -> no-fill mức sàn này, xuống mức multiplier thấp hơn."));

            var finishedRequestId = _requestId;
            var ranQueued = RunQueuedRequest();

            // Slot default (B) phải luôn có ad -> retry backoff. Slot floor (C) no-fill KHÔNG retry cùng
            // mức sàn: AdmobBidPool nhận kết quả qua loadFinished và xin tiếp mức multiplier thấp hơn.
            if (!ranQueued && IsDefaultSlot) ScheduleRetry();

            NotifyLoadFinished(finishedRequestId, false);
        }

        private void NotifyLoadFinished(int requestId, bool success)
        {
            if (loadFinished == null) return;

            // Callback chạy trong callback của SDK -> lỗi ở pool không được ném ngược lên GMA.
            try
            {
                loadFinished(this, requestId, success);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private bool RunQueuedRequest()
        {
            if (_queuedFloorUsd == null) return false;

            var floor = _queuedFloorUsd.Value;
            var tier = _queuedTier;
            _requestId = _queuedRequestId;
            _queuedFloorUsd = null;
            _queuedTier = null;
            _queuedRequestId = 0;

            StartLoad(floor, tier);
            return true;
        }

        // floorUsd = doanh thu 1 impression. rusd nhận eCPM tính bằng CENT:
        // $0.01234/impression -> eCPM $12.34 -> "1234". Trả null nếu không cần set sàn.
        private static string FormatBidFloor(double floorUsd)
        {
            if (floorUsd <= 0d) return null;

            var cents = floorUsd * _ImpressionsPerEcpm * _CentsPerUsd;
            if (cents < _MinBidFloorCents) return null;

            return cents.ToString(_BidFloorFormat, CultureInfo.InvariantCulture);
        }

        protected double ParseRevenue(ResponseInfo responseInfo)
        {
            try
            {
                var extras = responseInfo?.GetResponseExtras();
                if (extras == null || extras.Count == 0)
                {
                    Log($"slot '{Key}' không có response extras -> revenue không xác định.");
                    return UnknownRevenue;
                }

                if (LogEnabled)
                {
                    foreach (var pair in extras) Log($"slot '{Key}' extras[{pair.Key}] = {pair.Value}");
                }

                if (!extras.TryGetValue(_AdValueKey, out var raw) || string.IsNullOrEmpty(raw))
                {
                    Log($"slot '{Key}' extras không có '{_AdValueKey}' -> revenue không xác định " +
                        "(sẽ thua mọi mức giá thật của mediation ngoài).");
                    return UnknownRevenue;
                }

                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    Log($"slot '{Key}' không parse được {_AdValueKey} = '{raw}'.");
                    return UnknownRevenue;
                }

                if (_settings.adValueInMicros) value /= _MicrosPerUnit;

                // Chỉ so giá được khi cùng đơn vị tiền với mediation ngoài (thường luôn quy về USD).
                if (extras.TryGetValue(_AdValueCurrencyKey, out var currency) &&
                    !string.IsNullOrEmpty(currency) &&
                    !currency.Equals(_Usd, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"slot '{Key}' {_AdValueCurrencyKey} = {currency} != USD -> không so giá được.");
                    return UnknownRevenue;
                }

                return value < 0d ? UnknownRevenue : value;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return UnknownRevenue;
            }
        }

        #endregion

        #region show

        /// <summary>
        /// Show ad ĐẮT NHẤT trong kho. Người gọi đã tự bọc guard/flow của mình nên ở đây KHÔNG bọc lại.
        /// </summary>
        public void Show(string where, Action onReward, Action onHidden, Action onFail)
        {
            if (!IsReady)
            {
                onFail?.Invoke();
                return;
            }

            _where = where;
            _showStartedUtc = DateTime.UtcNow;
            _onReward = onReward;
            _onHidden = onHidden;
            _onFail = onFail;
            _rewardEarned = false;
            _resultDelivered = false;
            _showing = true;

            if (ShowBestStoredAd())
            {
                // Mốc SHOW của vòng đời ad view — chỉ bắn khi thật sự có ad được đưa lên, đúng chỗ tầng
                // MAX bắn (ngay trước lời gọi show của SDK).
                AdmobMediationHooks.AdShown(_settings.format, where, _adUnitId);
                return;
            }

            // Ad cuối cùng vừa hết hạn đúng lúc này -> trả fail ngay, đừng để người gọi treo chờ callback.
            _showing = false;
            DeliverResult(false);
        }

        /// <summary>
        /// Lấy ad đắt nhất ra khỏi kho, gắn event và show. false = kho rỗng (không có gì để show).
        /// </summary>
        protected abstract bool ShowBestStoredAd();

        /// <summary>Huỷ ad vừa show xong và trả về giá sàn của nó để nạp lại đúng mức đó.</summary>
        protected abstract double ConsumeShownAd();

        protected void OnPaid(AdValue adValue)
        {
            var responseInfo = CurrentResponseInfo;
            var price = CurrentAdPrice;

            AdmobRevenueReporter.Report(_settings.format, _adUnitId, adValue, responseInfo, _where,
                price?.floorUsd * _ImpressionsPerEcpm, price is { } p ? EcpmOf(p.revenue) : null);
            RecordImpression(adValue, responseInfo);
        }

        // piv = giá trị impression theo USD thập phân chuẩn (7500 micros -> "0.0075"), cùng thang với hob
        // của RecordAuctionEvent.
        //
        // response_id = response của chính ad vừa chiếu (đứng đầu) + response của MỌI ad AdMob khác đã
        // thua lượt này (mọi slot cùng format, load xong trước lúc show), nối bằng dấu phẩy.
        private void RecordImpression(AdValue adValue, ResponseInfo responseInfo)
        {
            var responseId = responseInfo?.GetResponseId();
            if (string.IsNullOrEmpty(responseId)) return;

            var responseIds = new List<string> { responseId };
            _settings.CollectLostResponseIds(responseIds, _showStartedUtc);

            var value = (adValue.Value / _MicrosPerUnitDecimal).ToString(CultureInfo.InvariantCulture);
            var precision = ((int)adValue.Precision).ToString(CultureInfo.InvariantCulture);

            Log($"AdMob thắng giá -> khai báo impression: response '{responseId}' (ad vừa chiếu) + " +
                $"{responseIds.Count - 1} response AdMob thua cùng lượt, piv={value} pip={precision}.");
            AdmobMultiPlatformUtils.RecordImpressionEvent(_settings.PublisherId, responseIds, value, precision);
        }

        protected void NotifyRewardEarned()
        {
            if (_rewardEarned) return;
            _rewardEarned = true;
            _onReward?.Invoke();
        }

        /// <summary>Subclass gọi từ callback click của SDK — mốc click của vòng đời ad view.</summary>
        protected void NotifyClicked() => AdmobMediationHooks.AdClicked(_settings.format);

        /// <summary>
        /// Subclass gọi khi ad ĐÓNG HẲN — mốc close của vòng đời ad view. KHÔNG gọi khi show lỗi:
        /// nhánh đó đi <see cref="NotifyShowFailed"/> một mình, đúng như tầng MAX (display-failed
        /// cũng không bắn close).
        /// </summary>
        /// <param name="adCompleted">Rewarded: có được trả thưởng không. Interstitial: luôn true.</param>
        protected void NotifyClosed(bool adCompleted)
            => AdmobMediationHooks.AdClosed(_settings.format, adCompleted);

        /// <summary>
        /// Subclass gọi khi GỌI HIỂN THỊ HỎNG (<c>OnAdFullScreenContentFailed</c>) — mốc show-failed
        /// của vòng đời ad view, đúng chỗ tầng MAX bắn ở <c>OnAdDisplayFailedEvent</c>. Đi MỘT MÌNH,
        /// không kèm <see cref="NotifyClosed"/>.
        /// <br/>Gọi TRƯỚC <see cref="OnShowFinished"/> — lúc đó ad định chiếu chưa bị huỷ nên còn đọc
        /// được network/giá (chỉ cho Databuckets).
        /// </summary>
        protected void NotifyShowFailed(AdError error)
        {
            var responseInfo = CurrentResponseInfo;
            var price = CurrentAdPrice;

            AdmobMediationHooks.AdShowFailed(new AdmobShowFailedInfo
            {
                format = _settings.format,
                errorCode = error?.GetCode().ToString(CultureInfo.InvariantCulture),
                adUnitId = _adUnitId,
                network = responseInfo == null ? null : AdmobRevenueReporter.ResolveAdSourceName(responseInfo),
                floorEcpmUsd = price?.floorUsd * _ImpressionsPerEcpm,
                estimatedEcpmUsd = price is { } p ? EcpmOf(p.revenue) : null
            });
        }

        protected bool RewardEarned => _rewardEarned;

        /// <summary>Subclass gọi khi ad đóng hẳn hoặc show lỗi.</summary>
        protected void OnShowFinished(bool success)
        {
            DeliverResult(success);

            // Ad đã tiêu -> huỷ đúng ad đó, các ad còn lại trong kho không đụng tới.
            _showing = false;
            var floor = ConsumeShownAd();

            if (RunQueuedRequest()) return;

            // Slot floor (BƯỚC C) KHÔNG tự nạp lại: mức sàn của nó là revenue của ad MAX bậc tương ứng,
            // mà ad MAX đó vừa bị tiêu hoặc vẫn đang nằm chờ — xin lại lúc này là xin theo một mức giá
            // không còn ai bảo chứng. Nó chỉ được nạp lại khi ĐÚNG ad unit MAX của bậc đó load xong lần
            // kế tiếp và kéo bước C chạy lại (xem AdmobFullscreenAds.OnExternalAdLoaded).
            //
            // Slot default (BƯỚC B) thì ngược lại: phải luôn có ad nên nạp lại ngay.
            if (!IsDefaultSlot)
            {
                Log($"slot '{Key}' (FLOOR) show xong -> KHÔNG nạp lại, chờ ad unit MAX cùng bậc load lại.");
                return;
            }

            Request(floor);
        }

        // Chỉ trả kết quả đúng 1 lần cho mỗi lượt show: AdMob có thể bắn cả Failed lẫn Closed, mà người
        // gọi chỉ chờ đúng một callback để chạy tiếp.
        private void DeliverResult(bool success)
        {
            if (_resultDelivered) return;
            _resultDelivered = true;

            var hidden = _onHidden;
            var fail = _onFail;
            _onReward = null;
            _onHidden = null;
            _onFail = null;

            if (success) hidden?.Invoke();
            else fail?.Invoke();
        }

        #endregion

        #region retry / dispose

        private void ScheduleRetry()
        {
            if (_disposed || _retryRoutine != null) return;

            // StartCoroutine ném exception nếu component đang tắt. Slot default mất đường retry ở đây sẽ
            // nằm im tới lần Request() kế tiếp -> log lại để còn lần ra nếu AdMob im tiếng.
            if (_runner == null || !_runner.isActiveAndEnabled)
            {
                Log($"slot '{Key}' không retry được: runner đang tắt.");
                return;
            }

            _retryRoutine = _runner.StartCoroutine(RetryAfterBackoff());
        }

        private IEnumerator RetryAfterBackoff()
        {
            // 1, 2, 4, ... trần 64 giây — AdMob throttle nếu request dồn dập lúc no-fill.
            var delay = Mathf.Pow(2f, Mathf.Min(_retryAttempt, 6));
            _retryAttempt++;

            Log($"slot '{Key}' retry sau {delay}s (lần {_retryAttempt})");
            yield return new WaitForSecondsRealtime(delay);

            _retryRoutine = null;
            if (_disposed || _loading) yield break;
            StartLoad(BidFloorUsd, BidTier);
        }

        private void CancelRetry()
        {
            if (_retryRoutine == null) return;
            if (_runner != null) _runner.StopCoroutine(_retryRoutine);
            _retryRoutine = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelRetry();
            ReleaseAllAds();
        }

        /// <summary>Huỷ MỌI ad trong kho (kể cả ad đang show) và gỡ toàn bộ event.</summary>
        protected abstract void ReleaseAllAds();

        #endregion

        #region multi-platform mediation

        /// <summary>
        /// Khai báo với AdMob giá cao nhất đến từ nguồn KHÔNG phải AdMob cho đúng response vừa nhận.
        /// Gọi cho cả lượt fill lẫn no-fill, miễn là có response id.
        /// </summary>
        protected void RecordAuction(ResponseInfo responseInfo)
        {
            var responseId = responseInfo?.GetResponseId();
            if (string.IsNullOrEmpty(responseId)) return;

            AdmobMultiPlatformUtils.RecordAuctionEvent(
                _settings.PublisherId, responseId, HighestOtherBidUsd ?? "0");
        }

        /// <summary>Giá USD/impression -> chuỗi USD thập phân chuẩn gửi cho AdMob (hob, piv).</summary>
        internal static string FormatUsd(double revenueUsd)
            => revenueUsd.ToString(_UsdDecimalFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// Bid cao nhất KHÔNG đến từ AdMob = revenue cao nhất mà mediation chạy song song đang cầm sẵn
        /// cho cùng format (qua <see cref="AdmobMediationHooks.externalBestRevenueProvider"/>).
        /// Đơn vị: USD thập phân chuẩn; null = không có bid nào để khai báo.
        ///
        /// Quy ước thang giá dùng chung: &lt;= 0 (không có ad, hoặc có ad nhưng chưa biết giá) đều KHÔNG
        /// phải một mức giá thật để khai báo -> trả null, người gọi tự thay bằng "0".
        /// </summary>
        private string HighestOtherBidUsd
        {
            get
            {
                var revenue = AdmobMediationHooks.ExternalBestRevenue(_settings.format);
                if (double.IsNaN(revenue) || double.IsInfinity(revenue) || revenue <= 0d) return null;

                return FormatUsd(revenue);
            }
        }

        #endregion

        protected bool LogEnabled => _settings != null && _settings.enableLog;

        protected void Log(string message)
        {
#if ENABLE_DEBUG_LOG
            if (LogEnabled) Debug.Log($"[{LogTag}] {message}");
#endif
        }
    }

    /// <summary>
    /// Phần "kho ad" dùng chung cho cả interstitial lẫn rewarded. <typeparamref name="TAd"/> là kiểu ad
    /// của GMA (InterstitialAd / RewardedAd) — hai kiểu đó không có interface chung nên mọi thao tác lên
    /// chúng đi qua các hàm abstract ở cuối class này.
    ///
    /// Callback load của GMA có thể về trên thread nền (RaiseAdEventsOnUnityMainThread có thể đang tắt)
    /// trong khi luồng show đọc kho ở main thread -> mọi truy cập danh sách đều nằm trong <c>_lock</c>.
    /// Các lời gọi ra ngoài (show ad, chạy request kế tiếp) luôn thực hiện SAU khi đã nhả lock.
    /// </summary>
    public abstract class AdmobBidAd<TAd> : AdmobBidAd where TAd : class
    {
        /// <summary>Một ad trong kho: object của SDK + giá sàn đã xin + giá AdMob trả về lúc load.</summary>
        protected sealed class StoredAd
        {
            public TAd ad;
            public ResponseInfo responseInfo;
            public double revenue;
            public double floorUsd;

            // Mốc load xong — để biết ad có tham gia so giá của một lượt show hay về SAU lúc đó.
            public DateTime loadedUtc;

            // Đã được khai báo là THUA trong impression của một lượt nào đó (MAX thắng, hoặc ad AdMob
            // khác thắng) — chặn khai báo thua lặp khi ad nằm lại trong kho qua nhiều lượt.
            public bool lossRecorded;
        }

        private readonly List<StoredAd> _ads = new List<StoredAd>();
        private readonly object _lock = new object();

        private StoredAd _showingAd;

        protected AdmobBidAd(MonoBehaviour runner, string adUnitId, string key, bool isDefaultSlot,
            AdmobBidSettings settings)
            : base(runner, adUnitId, key, isDefaultSlot, settings)
        {
        }

        public override bool IsReady
        {
            get
            {
                lock (_lock)
                {
                    PruneExpiredLocked();
                    return _ads.Count > 0;
                }
            }
        }

        public override int StoredAdCount
        {
            get
            {
                lock (_lock)
                {
                    PruneExpiredLocked();
                    return _ads.Count;
                }
            }
        }

        public override double Revenue
        {
            get
            {
                lock (_lock)
                {
                    var best = BestLocked();
                    return best == null ? UnknownRevenue : best.revenue;
                }
            }
        }

        public override double LoadedFloorUsd
        {
            get
            {
                lock (_lock)
                {
                    var best = BestLocked();
                    return best == null ? 0d : best.floorUsd;
                }
            }
        }

        public override string BestNetwork
        {
            get
            {
                // ResolveAdSourceName phải sang tận native để hỏi -> gọi NGOÀI lock (BestResponseInfo
                // đã nhả lock khi trả về).
                var responseInfo = BestResponseInfo;
                return responseInfo == null ? null : AdmobRevenueReporter.ResolveAdSourceName(responseInfo);
            }
        }

        protected override ResponseInfo CurrentResponseInfo => _showingAd?.responseInfo;

        protected override (double floorUsd, double revenue)? CurrentAdPrice
            => _showingAd is { } shown ? (shown.floorUsd, shown.revenue) : null;

        protected override ResponseInfo BestResponseInfo
        {
            get
            {
                lock (_lock)
                {
                    return BestLocked()?.responseInfo;
                }
            }
        }

        public override void ClaimLostResponseIds(List<string> into, DateTime? loadedBefore)
        {
            List<ResponseInfo> claimed = null;

            lock (_lock)
            {
                PruneExpiredLocked();
                foreach (var stored in _ads)
                {
                    if (stored.lossRecorded) continue;
                    if (loadedBefore.HasValue && stored.loadedUtc > loadedBefore.Value) continue;

                    stored.lossRecorded = true;
                    (claimed ??= new List<ResponseInfo>()).Add(stored.responseInfo);
                }
            }

            if (claimed == null) return;

            // GetResponseId phải sang tận native để hỏi -> gọi NGOÀI lock.
            foreach (var responseInfo in claimed)
            {
                var responseId = responseInfo?.GetResponseId();
                if (!string.IsNullOrEmpty(responseId)) into.Add(responseId);
            }
        }

        protected override string DescribeStoredAds()
        {
            var sb = new StringBuilder();

            lock (_lock)
            {
                foreach (var stored in _ads)
                    sb.Append($" [rev=${stored.revenue:F6} floor=${stored.floorUsd:F6}]");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Subclass gọi khi SDK trả về ad mới: CẤT THÊM vào kho (không đụng ad cũ), rồi báo lên base.
        /// </summary>
        protected void StoreLoadedAd(TAd ad, ResponseInfo responseInfo)
        {
            var stored = new StoredAd
            {
                ad = ad,
                responseInfo = responseInfo,
                revenue = ParseRevenue(responseInfo),
                floorUsd = BidFloorUsd,
                loadedUtc = DateTime.UtcNow
            };

            lock (_lock)
            {
                _ads.Add(stored);
                PruneExpiredLocked();
            }

            OnLoadSucceeded(stored.revenue, responseInfo);
        }

        protected override bool ShowBestStoredAd()
        {
            StoredAd best;

            lock (_lock)
            {
                best = BestLocked();
                if (best == null) return false;

                // Rút hẳn khỏi kho: ad đang chiếu không còn là ứng viên so giá, và không thể bị prune
                // nhầm (CanShowAd trả false ngay khi bắt đầu show).
                _ads.Remove(best);
                _showingAd = best;
            }

            AttachShowHandlers(best.ad);
            ShowAd(best.ad);
            return true;
        }

        protected override double ConsumeShownAd()
        {
            var shown = _showingAd;
            _showingAd = null;

            if (shown == null) return 0d;

            DetachShowHandlers(shown.ad);
            DestroyAd(shown.ad);
            return shown.floorUsd;
        }

        protected override void ReleaseAllAds()
        {
            lock (_lock)
            {
                foreach (var stored in _ads) DestroyAd(stored.ad);
                _ads.Clear();
            }

            var shown = _showingAd;
            _showingAd = null;
            if (shown == null) return;

            DetachShowHandlers(shown.ad);
            DestroyAd(shown.ad);
        }

        private StoredAd BestLocked()
        {
            PruneExpiredLocked();

            StoredAd best = null;
            foreach (var stored in _ads)
                if (best == null || stored.revenue > best.revenue)
                    best = stored;

            return best;
        }

        // Ad AdMob hết hạn sau ~1 giờ (mediated network còn ngắn hơn): CanShowAd() trả false và ad đó
        // vĩnh viễn vô dụng -> huỷ ngay để trả bộ nhớ thay vì để nó chiếm chỗ trong kho.
        private void PruneExpiredLocked()
        {
            for (var i = _ads.Count - 1; i >= 0; i--)
            {
                var stored = _ads[i];
                if (CanShow(stored.ad)) continue;

                Log($"HẾT HẠN slot '{Key}': bỏ ad revenue=${stored.revenue:F6} floor=${stored.floorUsd:F6}.");
                DestroyAd(stored.ad);
                _ads.RemoveAt(i);
            }
        }

        /// <summary>Ad còn show được không (false = đã show, đã huỷ, hoặc hết hạn).</summary>
        protected abstract bool CanShow(TAd ad);

        protected abstract void DestroyAd(TAd ad);

        /// <summary>Gắn event cho đúng ad sắp show. Ad nằm trong kho KHÔNG gắn event nào.</summary>
        protected abstract void AttachShowHandlers(TAd ad);

        protected abstract void DetachShowHandlers(TAd ad);

        protected abstract void ShowAd(TAd ad);
    }

    /// <summary>Kho interstitial — xem <see cref="AdmobBidAd"/>.</summary>
    public class AdmobBidInterstitialAd : AdmobBidAd<InterstitialAd>
    {
        public AdmobBidInterstitialAd(MonoBehaviour runner, string adUnitId, string key, bool isDefaultSlot,
            AdmobBidSettings settings)
            : base(runner, adUnitId, key, isDefaultSlot, settings)
        {
        }

        protected override string LogTag => "AdmobBidInterstitial";

        protected override void LoadAd(AdRequest request)
        {
            InterstitialAd.Load(_adUnitId, request, (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    // Lúc no-fill vẫn phải khai báo auction (nếu có response id) TRƯỚC khi huỷ ad.
                    // No-fill thì ad = null -> response nằm ở error (trước đây đọc ad?. nên không bao giờ gửi).
                    RecordAuction(ad?.GetResponseInfo() ?? error?.GetResponseInfo());
                    ad?.Destroy();
                    OnLoadFailed(error?.GetMessage(), error?.GetCode().ToString());
                    return;
                }

                var responseInfo = ad.GetResponseInfo();
                StoreLoadedAd(ad, responseInfo);
                RecordAuction(responseInfo);
            });
        }

        protected override bool CanShow(InterstitialAd ad) => ad != null && ad.CanShowAd();

        protected override void DestroyAd(InterstitialAd ad) => ad?.Destroy();

        protected override void AttachShowHandlers(InterstitialAd ad)
        {
            ad.OnAdPaid += OnPaid;
            ad.OnAdFullScreenContentOpened += OnOpened;
            ad.OnAdFullScreenContentClosed += OnClosed;
            ad.OnAdFullScreenContentFailed += OnShowFailed;
            ad.OnAdClicked += OnClicked;
        }

        protected override void DetachShowHandlers(InterstitialAd ad)
        {
            ad.OnAdPaid -= OnPaid;
            ad.OnAdFullScreenContentOpened -= OnOpened;
            ad.OnAdFullScreenContentClosed -= OnClosed;
            ad.OnAdFullScreenContentFailed -= OnShowFailed;
            ad.OnAdClicked -= OnClicked;
        }

        //af_inters_show KHÔNG bắn ở đây: xem AdmobFullscreenAds.Show (chỉ đường show độc lập mới bắn)
        protected override void ShowAd(InterstitialAd ad) => ad.Show();

        private void OnOpened()
        {
            AdmobMediationHooks.AdDisplayed(_settings.format);
            AdmobMediationHooks.Track("af_inters_displayed");
        }

        private void OnClicked() => NotifyClicked();

        private void OnClosed()
        {
            Log("interstitial closed.");
            NotifyClosed(adCompleted: true);
            OnShowFinished(success: true);
        }

        private void OnShowFailed(AdError error)
        {
            Log($"interstitial show failed: {error?.GetMessage()}");
            NotifyShowFailed(error);
            OnShowFinished(success: false);
        }
    }

    /// <summary>Kho rewarded — xem <see cref="AdmobBidAd"/>.</summary>
    public class AdmobBidRewardedAd : AdmobBidAd<RewardedAd>
    {
        public AdmobBidRewardedAd(MonoBehaviour runner, string adUnitId, string key, bool isDefaultSlot,
            AdmobBidSettings settings)
            : base(runner, adUnitId, key, isDefaultSlot, settings)
        {
        }

        protected override string LogTag => "AdmobBidRewarded";

        protected override void LoadAd(AdRequest request)
        {
            RewardedAd.Load(_adUnitId, request, (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    // Lúc no-fill vẫn phải khai báo auction (nếu có response id) TRƯỚC khi huỷ ad.
                    // No-fill thì ad = null -> response nằm ở error (trước đây đọc ad?. nên không bao giờ gửi).
                    RecordAuction(ad?.GetResponseInfo() ?? error?.GetResponseInfo());
                    ad?.Destroy();
                    OnLoadFailed(error?.GetMessage(), error?.GetCode().ToString());
                    return;
                }

                var responseInfo = ad.GetResponseInfo();
                StoreLoadedAd(ad, responseInfo);
                RecordAuction(responseInfo);
            });
        }

        protected override bool CanShow(RewardedAd ad) => ad != null && ad.CanShowAd();

        protected override void DestroyAd(RewardedAd ad) => ad?.Destroy();

        protected override void AttachShowHandlers(RewardedAd ad)
        {
            ad.OnAdPaid += OnPaid;
            ad.OnAdFullScreenContentOpened += OnOpened;
            ad.OnAdFullScreenContentClosed += OnClosed;
            ad.OnAdFullScreenContentFailed += OnShowFailed;
            ad.OnAdClicked += OnClicked;
        }

        protected override void DetachShowHandlers(RewardedAd ad)
        {
            ad.OnAdPaid -= OnPaid;
            ad.OnAdFullScreenContentOpened -= OnOpened;
            ad.OnAdFullScreenContentClosed -= OnClosed;
            ad.OnAdFullScreenContentFailed -= OnShowFailed;
            ad.OnAdClicked -= OnClicked;
        }

        //af_rewarded_show KHÔNG bắn ở đây: xem AdmobFullscreenAds.Show (chỉ đường show độc lập mới bắn)
        protected override void ShowAd(RewardedAd ad) => ad.Show(OnUserEarnedReward);

        //af_rewarded_displayed lúc ad LÊN HÌNH như MAX (trước đây bắn lúc nhận thưởng -> rewarded bị tắt
        //giữa chừng thì mất event)
        private void OnOpened()
        {
            AdmobMediationHooks.AdDisplayed(_settings.format);
            AdmobMediationHooks.Track("af_rewarded_displayed");
        }

        private void OnUserEarnedReward(Reward reward)
        {
            Log($"earned reward: {reward?.Amount} {reward?.Type}");

            if (RewardEarned) return;
            NotifyRewardEarned();
        }

        private void OnClicked() => NotifyClicked();

        private void OnClosed()
        {
            Log("rewarded closed.");
            NotifyClosed(adCompleted: RewardEarned);
            // Đóng mà chưa earn reward = user bỏ giữa chừng -> báo fail để không trả thưởng.
            OnShowFinished(RewardEarned);
        }

        private void OnShowFailed(AdError error)
        {
            Log($"rewarded show failed: {error?.GetMessage()}");
            NotifyShowFailed(error);
            OnShowFinished(success: false);
        }
    }
}
