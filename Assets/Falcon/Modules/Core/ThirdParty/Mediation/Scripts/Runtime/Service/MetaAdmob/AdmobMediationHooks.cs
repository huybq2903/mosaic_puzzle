using System;
using UnityEngine;

namespace Falcon.Admob
{
    /// <summary>
    /// Ảnh chụp remote config mà project cấp cho module. Module KHÔNG còn asset settings nào trong
    /// Resources — TOÀN BỘ ad unit id (bước B lẫn bước C) đều nằm trong
    /// <see cref="moMulAdmobMetaConfig"/>, nên không có config = không có ad unit = AdMob không chạy.
    /// </summary>
    public class AdmobRemoteSettings
    {
        /// <summary>
        /// JSON chứa MỌI ad unit AdMob, tách theo format. Cấu trúc do
        /// <see cref="AdmobMetaAdsConfigsData"/> / <see cref="AdmobMetaAdsUnitConfig"/> định nghĩa —
        /// parse bằng <c>JsonUtility</c> nên tên field phải khớp CHÍNH XÁC.
        ///
        /// Mỗi format có hai phần:
        ///  - BƯỚC B — <c>defaultAdId</c>: ad unit mức "default", xin ad KHÔNG set giá sàn. Một trường
        ///    duy nhất, KHÔNG tách Android/iOS — remote config đã trả về theo đúng app đang chạy. Để
        ///    trống là bỏ qua bước B của format đó (bước C vẫn chạy bình thường).
        ///  - BƯỚC C — <c>adIds</c> + <c>multipliers</c>: thang giá sàn. Ad unit <c>adIds[i]</c> được
        ///    xin với giá sàn = revenue của ad MAX vừa load × <c>multipliers[i]</c>. Hệ số thiếu hoặc
        ///    &lt;= 0 tính là 1.0 (giá sàn ĐÚNG BẰNG revenue). Bỏ trống <c>adIds</c> là bỏ qua hẳn
        ///    bước C — module KHÔNG mượn ad unit bước B làm mức mặc định.
        ///
        /// Thang giá bước C chạy lại cho TỪNG BẬC ad unit MAX: max thứ k load xong revenue R thì xin
        /// <c>adIds[i]</c> ở giá sàn R × <c>multipliers[i]</c>. Cùng một ad unit AdMob nhưng mỗi bậc
        /// giữ kho riêng nên bậc này show xong không đụng gì tới bậc khác (bậc do
        /// <see cref="externalTierProvider"/> quyết định).
        ///
        /// Không có CẢ hai phần, ở CẢ hai format -> <see cref="AdmobMediation.Initialize"/> bỏ qua
        /// luôn, không đụng tới MobileAds.
        ///
        /// <code>
        /// {
        ///   "interstitial": {
        ///     "defaultAdId": "ca-app-pub-3046520253844875/1111111111",
        ///     "adIds": ["ca-app-pub-3046520253844875/1364369313",
        ///               "ca-app-pub-3046520253844875/8385203119",
        ///               "ca-app-pub-3046520253844875/2342393155"],
        ///     "multipliers": [1, 1.2, 1.5]
        ///   },
        ///   "rewarded": {
        ///     "defaultAdId": "ca-app-pub-3046520253844875/3333333333",
        ///     "adIds": ["ca-app-pub-3046520253844875/1755517485",
        ///               "ca-app-pub-3046520253844875/1029311482",
        ///               "ca-app-pub-3046520253844875/7371504347"],
        ///     "multipliers": [1, 1.2, 1.5]
        ///   }
        /// }
        /// </code>
        ///
        /// Ví dụ trên: cả hai format chạy đủ bước B lẫn bước C. Bỏ <c>defaultAdId</c> đi thì format đó
        /// chỉ còn bước C; bỏ luôn <c>adIds</c> thì chỉ còn bước B.
        ///
        /// <c>levels</c> là biến thể TUỲ CHỌN cho trường hợp MO muốn mỗi bậc MAX đi một bộ ad unit
        /// KHÁC nhau (xem <see cref="AdmobMetaAdsUnitConfig"/>); không khai thì dùng thang phẳng ở trên.
        ///
        /// publisherId cho multi-platform reporting tự suy từ id bước B (phần trước dấu '/'), không
        /// phải khai riêng.
        /// </summary>
        public string moMulAdmobMetaConfig;
    }

    /// <summary>
    /// Kết quả MỘT lần gọi load của MỘT ad unit — telemetry fill/floor, KHÔNG thuộc vòng đời một lần
    /// xem ad. Module bắn cho cả lần thành công lẫn thất bại, kể cả retry.
    /// </summary>
    public struct AdmobLoadResult
    {
        public AdmobAdFormat format;
        public string adUnitId;
        public bool success;

        /// <summary>   
        /// Giá sàn đã xin, quy sang eCPM USD (= floor/impression × 1000) để cùng thang với con số mà
        /// tầng MAX gửi. null = slot bước B, không set sàn.
        /// </summary>
        public double? floorEcpmUsd;

        /// <summary>
        /// BẬC của biến thể đã xin (<c>variants[i].multiplier</c> trong BuildFloorVariants) — cùng
        /// trục với <c>tier</c> mà tầng MAX gửi, để so bậc nào fill tốt hơn giữa hai mediation.
        /// null = slot bước B (không đặt sàn, không thuộc bậc nào).
        /// </summary>
        public double? multiplier;

        /// <summary>
        /// eCPM ước tính (USD/1000 impression) mà AdMob trả về lúc load xong (ad_value của response).
        /// null khi load fail hoặc AdMob không trả giá.
        /// </summary>
        public double? estimatedEcpmUsd;

        /// <summary>Network thắng thầu — chỉ có khi <see cref="success"/>.</summary>
        public string network;

        /// <summary>
        /// Code lỗi của lần fail này — NGẮN, phân loại được. Đây mới là thứ đi vào
        /// <c>AdLoadResultParam.errorMess</c>: cụm load-result giữ bản CUỐI làm đại diện nên cần
        /// giá trị ngắn; <see cref="errorMessage"/> của AdMob dài cả đoạn, gửi lên là mỗi máy một
        /// chuỗi khác nhau, không group nổi.
        /// </summary>
        public string errorCode;

        /// <summary>Message lỗi của lần fail này — CHỈ để log local, KHÔNG gửi lên DWH.</summary>
        public string errorMessage;

        /// <summary>Thời gian của CHÍNH attempt này (ms), module tự đo từ lúc gọi SDK.</summary>
        public long loadingMs;
    }

    /// <summary>Một impression AdMob đã trả tiền — module đẩy ra ngoài để project tự bắn analytics.</summary>
    public struct AdmobImpressionInfo
    {
        public AdmobAdFormat format;
        public string adUnitId;

        /// <summary>Tên network thắng thầu ("AdMob" nếu AdMob tự fill).</summary>
        public string network;

        public string currency;

        /// <summary>Doanh thu của ĐÚNG MỘT impression theo <see cref="currency"/> (đã quy từ micros).</summary>
        public double revenue;

        /// <summary>AdValue.Precision dạng chuỗi.</summary>
        public string precision;

        /// <summary>Placement game truyền vào lúc show.</summary>
        public string where;

        /// <summary>Giá sàn eCPM (USD/1000 impression) đã xin cho ad này; 0 = slot không set sàn.</summary>
        public double? floorEcpmUsd;

        /// <summary>eCPM ước tính lúc load của ad này; null khi AdMob không trả giá.</summary>
        public double? estimatedEcpmUsd;
    }

    /// <summary>Một lần gọi hiển thị hỏng — ad đã load sẵn nhưng SDK báo không chiếu được.</summary>
    public struct AdmobShowFailedInfo
    {
        public AdmobAdFormat format;

        /// <summary>Error code của AdMob.</summary>
        public string errorCode;

        public string adUnitId;

        /// <summary>Network thắng thầu của ad định chiếu; null nếu không đọc được.</summary>
        public string network;

        /// <summary>Giá sàn eCPM (USD/1000 impression) đã xin cho ad này; 0 = slot không set sàn.</summary>
        public double? floorEcpmUsd;

        /// <summary>eCPM ước tính lúc load của ad này; null khi AdMob không trả giá.</summary>
        public double? estimatedEcpmUsd;
    }

    /// <summary>
    /// TOÀN BỘ điểm nối giữa module và project. Module KHÔNG tham chiếu thẳng bất kỳ SDK nào ngoài
    /// GoogleMobileAds; mọi thứ còn lại (remote config, analytics, mediation chạy song song, guard chặn
    /// App Open) đều đi qua các delegate ở đây.
    ///
    /// Bỏ trống hook nào thì mất tính năng tương ứng, module vẫn chạy — TRỪ
    /// <see cref="remoteConfigProvider"/>: nó là nguồn của mọi ad unit id, không có nó thì AdMob không
    /// có gì để load và <see cref="AdmobMediation.Initialize"/> bỏ qua luôn.
    ///
    /// Gán một lần trước khi gọi <see cref="AdmobMediation.Initialize"/>.
    /// </summary>
    public static class AdmobMediationHooks
    {
        /// <summary>
        /// Nguồn remote config — thứ cấp MỌI ad unit id cho module (xem <see cref="AdmobRemoteSettings"/>).
        /// Trả null = không có ad unit nào -> AdMob không init, game chỉ chạy mediation ngoài.
        /// </summary>
        public static Func<AdmobRemoteSettings> remoteConfigProvider;

        /// <summary>
        /// Doanh thu (USD/impression) cao nhất mà mediation NGOÀI AdMob (vd MAX) đang cầm cho format
        /// tương ứng. Dùng để khai báo <c>highest_other_bid</c> với AdMob. Trả &lt;= 0 = không có.
        /// </summary>
        public static Func<AdmobAdFormat, double> externalBestRevenueProvider;

        /// <summary>
        /// BẬC của một ad unit mediation ngoài: (format, adUnitId) -> chỉ số 0..n-1 trong danh sách ad
        /// unit của format đó (MAX: 0 = mức default, 1..n-1 = các mức floor tăng dần).
        ///
        /// Đây là thứ nối BƯỚC C với ĐÚNG một mức MAX: thang giá bước C được xin LẠI cho từng bậc, mỗi
        /// bậc giữ kho ad riêng (key slot <c>floor#k#i</c>). Nhờ vậy max2 load xong chỉ đụng kho của
        /// bậc 2 — bậc khác giữ nguyên ad lẫn giá sàn của chúng.
        ///
        /// Không gán = mọi ad unit ngoài đều tính là bậc 0 -> mọi mức MAX dùng CHUNG một kho, giẫm lên
        /// nhau: mức nào load sau ghi đè giá sàn của mức trước.
        /// </summary>
        public static Func<AdmobAdFormat, string, int> externalTierProvider;

        /// <summary>Bắn doanh thu 1 impression (Firebase ad_impression, AppsFlyer logAdRevenue, ...).</summary>
        public static Action<AdmobImpressionInfo> onImpression;

        // === Vòng đời MỘT LẦN XEM AD: request -> show -> impression -> close ===
        // Năm mốc dưới đây + onImpression khớp đúng bộ mà tầng MAX đang bắn. Không gán = không log
        // vòng đời, mọi thứ khác chạy bình thường.

        /// <summary>Module sắp gọi SDK load một ad unit. Retry = một lần request mới.</summary>
        public static Action<AdmobAdFormat> onAdRequested;

        /// <summary>Module sắp chiếu ad; tham số thứ hai là placement game truyền vào lúc show.</summary>
        /// <summary>format, where, adUnitId của ad sắp chiếu.</summary>
        public static Action<AdmobAdFormat, string, string> onAdShown;

        public static Action<AdmobAdFormat> onAdDisplayed;

        /// <summary>
        /// GỌI HIỂN THỊ HỎNG — kèm error code của AdMob và ad định chiếu. Cùng mốc mà tầng MAX bắn ở
        /// <c>OnAdDisplayFailedEvent</c>. Show hỏng KHÔNG có mốc close đi kèm: vòng đời lần xem đó
        /// kết thúc ngay tại đây, không có impression nào mang cùng adViewId.
        /// </summary>
        public static Action<AdmobShowFailedInfo> onAdShowFailed;

        /// <summary>Người chơi bấm vào ad đang chiếu.</summary>
        public static Action<AdmobAdFormat> onAdClicked;

        /// <summary>
        /// Ad đã ĐÓNG (không phải show lỗi). Tham số thứ hai = xem trọn vẹn hay bỏ giữa chừng —
        /// rewarded thì là "có được trả thưởng không", interstitial luôn true.
        /// </summary>
        public static Action<AdmobAdFormat, bool> onAdClosed;

        /// <summary>Kết quả một lần gọi load của một ad unit (fill/floor telemetry).</summary>
        public static Action<AdmobLoadResult> onLoadResult;

        /// <summary>Event analytics đơn giản theo tên (af_inters_show, af_rewarded_displayed, ...).</summary>
        public static Action<string> onTrackEvent;

        /// <summary>
        /// Level hiện tại của user. Bản thân tầng AdMob KHÔNG dùng giá trị này — nó chỉ là trường bắt
        /// buộc của ad-log mà project bắn trong <see cref="onImpression"/>. Khai báo ở đây để phần
        /// analytics không phải tham chiếu thẳng lớp tiến độ của một game cụ thể. Không gán = 0.
        /// </summary>
        public static Func<int> levelProvider;

        /// <summary>Kênh UA của user, nhét vào AdRequest.Extras (<c>ua_channel</c>).</summary>
        public static Func<string> uaChannelProvider;

        /// <summary>Số ngày kể từ lúc cài app, nhét vào AdRequest.Extras (<c>days_since_install</c>).</summary>
        public static Func<int?> daysSinceInstallProvider;

        /// <summary>
        /// Mở/đóng một "phiên quảng cáo" của project (thường để chặn App Open hiện chồng). Module gọi
        /// quanh các lượt show mà chính nó khởi tạo — lượt show do project tự bọc thì KHÔNG gọi lại.
        /// </summary>
        public static Action onAdFlowBegin, onAdFlowEnd;

        internal static AdmobRemoteSettings ReadRemoteSettings()
        {
            try
            {
                return remoteConfigProvider?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return null;
            }
        }

        internal static double ExternalBestRevenue(AdmobAdFormat format)
        {
            try
            {
                return externalBestRevenueProvider?.Invoke(format) ?? 0d;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return 0d;
            }
        }

        internal static int ExternalTier(AdmobAdFormat format, string adUnitId)
        {
            try
            {
                var tier = externalTierProvider?.Invoke(format, adUnitId) ?? 0;
                return tier < 0 ? 0 : tier;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return 0;
            }
        }

        internal static void Impression(AdmobImpressionInfo info)
        {
            try
            {
                onImpression?.Invoke(info);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void AdRequested(AdmobAdFormat format)
        {
            try
            {
                onAdRequested?.Invoke(format);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void AdDisplayed(AdmobAdFormat format)
        {
            try
            {
                onAdDisplayed?.Invoke(format);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void AdShown(AdmobAdFormat format, string where, string adUnitId)
        {
            try
            {
                onAdShown?.Invoke(format, where, adUnitId);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void AdShowFailed(AdmobShowFailedInfo info)
        {
            try
            {
                onAdShowFailed?.Invoke(info);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void AdClicked(AdmobAdFormat format)
        {
            try
            {
                onAdClicked?.Invoke(format);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void AdClosed(AdmobAdFormat format, bool adCompleted)
        {
            try
            {
                onAdClosed?.Invoke(format, adCompleted);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void LoadResult(AdmobLoadResult result)
        {
            try
            {
                onLoadResult?.Invoke(result);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void Track(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            try
            {
                onTrackEvent?.Invoke(eventName);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static int Level()
        {
            try
            {
                return levelProvider?.Invoke() ?? 0;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return 0;
            }
        }

        internal static string UaChannel()
        {
            try
            {
                return uaChannelProvider?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return null;
            }
        }

        internal static int? DaysSinceInstall()
        {
            try
            {
                return daysSinceInstallProvider?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return null;
            }
        }

        internal static void BeginAdFlow()
        {
            try
            {
                onAdFlowBegin?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        internal static void EndAdFlow()
        {
            try
            {
                onAdFlowEnd?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }
    }
}
