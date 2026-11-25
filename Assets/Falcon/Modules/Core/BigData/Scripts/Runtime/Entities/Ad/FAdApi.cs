/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-07
 */

using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Entity MỘT LẦN XEM AD — <c>FalconBigDataController.Ad</c>: request → show → impression →
    /// close, cả 4 mốc mang chung một <c>adViewId</c> (xem EntityLifecycle-Design.md §4f).
    /// </summary>
    public class FAdApi
    {
        private readonly AdRequestService _adRequestService;
        private readonly LabelLogService _labelLogService;

        internal FAdApi(AdRequestService adRequestService, LabelLogService labelLogService)
        {
            _adRequestService = adRequestService;
            _labelLogService = labelLogService;
        }

        // ---- Đọc / sửa cache ----

        /// <summary>
        /// Id của lần xem ad hiện tại cho format này — cùng id đã sinh lúc <see cref="OnRequested"/>
        /// và đang được đóng dấu lên log impression. Mediation lấy id này để gắn lên message
        /// show/close của mình (CSAdStart/CSAdFinish) nếu muốn nối dữ liệu DWH với game server
        /// thành trọn vòng đời request → show → impression → close.
        /// </summary>
        public string CurrentViewId(AdType type)
        {
            return _adRequestService.CurrentAdViewId(type);
        }

        /// <summary>
        /// Ảnh chụp lần xem ad hiện tại của format này: id, chỗ chiếu, ngữ cảnh, mediation và hai
        /// mốc thời gian. Null nếu format chưa từng đi qua <see cref="OnRequested"/>.
        /// <br/>Là bản SAO — sửa vào đó không đụng tới state của SDK.
        /// </summary>
        public AdViewSnapshot CurrentView(AdType type)
        {
            return _adRequestService.CurrentAdView(type);
        }

        /// <summary>
        /// Báo tham số cho lần xem ad hiện tại của format này (chỉ đối số khác rỗng mới ghi đè).
        /// Preload thì lúc <see cref="OnRequested"/> chưa biết sẽ chiếu ở đâu, trong ngữ cảnh nào
        /// — biết lúc nào gọi lúc đó, các mốc sau (show / impression / close) tự mang theo thay vì
        /// để trống hoặc bắt nhập lại từng mốc.
        /// <br/>Với log impression do mediation bắn: chỉ điền khi mediation bỏ trống — giá trị
        /// mediation tự đặt luôn thắng.
        /// </summary>
        /// <param name="adAlgorithm">
        /// Tên thuật toán load của đợt này (yêu cầu phía đọc số 26/10) — SDK không tự biết. Khai một
        /// lần cho mỗi đợt là mọi mốc sau tự mang theo. Multicall Max có 8 nhánh nên giá trị đề xuất
        /// là <c>multicall_&lt;IMultiCallProvider.GetIndex()&gt;</c>; đường một-unit thì <c>single</c>.
        /// </param>
        /// <param name="triggerPlayTurnId">
        /// Lượt chơi đã KÍCH HOẠT lần chiếu (hợp đồng đợt 3 B12): QC trong lượt = <c>Level.CurrentTurnId</c>, Inter sau
        /// thắng / thua = <c>Level.LastEndedTurnId</c>, không do lượt nào thì bỏ trống. Khai cùng <paramref name="adWhen"/>.
        /// </param>
        public void UpdateContext(
            AdType type, string adWhere = null, string adWhen = null, string adMediation = null,
            string adAlgorithm = null, string triggerPlayTurnId = null)
        {
            _adRequestService.SetAdContext(type, adWhere, adWhen, adMediation, adAlgorithm, triggerPlayTurnId);
        }

        // ---- Báo khoảnh khắc ----

        /// <summary>
        /// Mediation vừa gọi load một ad — gọi NGAY TRƯỚC <c>LoadAd()</c> của từng format.
        /// SDK sinh <c>adViewId</c>, log mốc request, rồi tự đóng dấu id đó lên log impression
        /// của cùng format khi ad lên hình → đo được phễu fill (request vs impression).
        /// <br/>Request không bao giờ có impression tương ứng chính là view fill-fail; không cần
        /// event fail riêng. Retry load = một request mới (đúng: mỗi lần xin là một lần xin).
        /// </summary>
        /// <param name="param">Tham số mốc request — <c>param.type</c> BẮT BUỘC. <c>networkType</c> SDK tự đo, stamp đè.</param>
        public void OnRequested(AdViewParam param)
        {
            _adRequestService.RequestAd(param);
        }

        /// <inheritdoc cref="OnRequested(AdViewParam)"/>
        /// <param name="type">Format ad đang xin.</param>
        /// <param name="adWhere">
        /// Thường ĐỂ TRỐNG ở mốc này: adWhere/adWhen là thuộc tính GAMEPLAY của lần HIỂN THỊ
        /// (phân tích placement performance), mà lúc load (preload) chưa có ngữ cảnh gameplay nào —
        /// điền là dán ngữ cảnh đoán. Khai ở <see cref="OnShown(AdShowParam)"/> hoặc
        /// <see cref="UpdateContext"/>, các mốc sau tự mang theo.
        /// </param>
        /// <param name="adMediation">Mediation đang dùng (Max, IronSource, AdMob...) — thuộc tính hạ tầng, biết từ lúc load nên truyền ở đây là đúng chỗ.</param>
        /// <param name="adWhen">Cùng luật với <paramref name="adWhere"/> — thường để trống ở mốc này.</param>
        /// <param name="extraMeta">Key-value tuỳ ý cho mốc này — server lưu <c>event_extra_props</c>; cột hợp đồng thắng khi trùng key.</param>
        /// <param name="adAlgorithm">Thuật toán load của đợt — xem <see cref="UpdateContext"/>.</param>
        public void OnRequested(
            AdType type, string adWhere = null, string adMediation = null, string adWhen = null,
            Dictionary<string, object> extraMeta = null, string adAlgorithm = null)
        {
            _adRequestService.RequestAd(new AdViewParam
            {
                type = type,
                adWhere = adWhere,
                adWhen = adWhen,
                adMediation = adMediation,
                adAlgorithm = adAlgorithm,
                extraMeta = extraMeta
            });
        }

        /// <summary>
        /// Mediation gọi hiển thị ad — mốc giữa request và impression. Show mà không có impression
        /// mang cùng <c>adViewId</c> nghĩa là hiển thị hỏng (display failure).
        /// SDK tự đo thời gian chờ từ lúc request.
        /// </summary>
        /// <param name="param">Tham số mốc show — <c>param.type</c> BẮT BUỘC.</param>
        public void OnShown(AdShowParam param)
        {
            _adRequestService.ShowAd(param);
        }

        /// <inheritdoc cref="OnShown(AdShowParam)"/>
        public void OnShown(
            AdType type, string adWhere = null, string adMediation = null, string adWhen = null,
            Dictionary<string, object> extraMeta = null, string triggerPlayTurnId = null)
        {
            _adRequestService.ShowAd(new AdShowParam
            {
                type = type,
                adWhere = adWhere,
                adWhen = adWhen,
                triggerPlayTurnId = triggerPlayTurnId,
                adMediation = adMediation,
                extraMeta = extraMeta
            });
        }

        /// <summary>
        /// Impression / paid event — mốc DOANH THU của lần xem ad (với inter/rewarded nó trùng
        /// thời điểm lên hình). SDK enqueue <c>f_sdk_ads_data</c>, decorator tự đóng
        /// <c>adViewId</c>/fillLatency/context và đọc sổ cái để đóng typeCount/adLtv; banner tự
        /// đi vào cụm gộp thay vì bắn per-impression.
        /// <br/>Đây là cửa impression CHUẨN cho mọi bên (kiến trúc chốt 13/08: Mediation giữ vai
        /// chủ sổ cái, log đi một cửa này — xem Request-Mediation-AdLifecycle.md). Bên giữ sổ
        /// phải CỘNG SỔ TRƯỚC rồi mới gọi, không thì typeCount/adLtv trên log trễ một impression.
        /// <br/>⚠ MỘT giao dịch MỘT đường: còn bắn <c>FAdLogMediation</c> song song là ĐẾM ĐÔI
        /// doanh thu ở DWH — đường đó khai tử, đừng chạy cả hai.
        /// </summary>
        public void OnImpression(AdParam param)
        {
            _adRequestService.LogImpression(param);
        }

        /// <summary>
        /// Load ad thất bại — gọi từ <c>OnAdLoadFailedEvent</c> với errorCode của mediation
        /// (no-fill / timeout / network...). Mốc THỐNG NHẤT về chữ ký, ruột hiện tại đi kênh nhãn
        /// <c>f_sdk_ad_view_label</c> ({<see cref="FAdViewLabelKey.LOAD_ERROR"/>: code}, tự mang
        /// <c>adViewId</c> của request đang mở) — lỗi hiếm nên rẻ, còn load-THÀNH-CÔNG cố ý không
        /// có mốc (suy ra được: requests − load_error). Sau này MO cần event load-result thật thì
        /// đổi ruột, chữ ký không đổi.
        /// <br/>Mapping <c>ad_view_label</c> phía kho đã chạy (loader xác nhận 12/11).
        /// <br/>Chưa có request nào của format thì SDK cảnh báo và bỏ qua (lỗi không thuộc về ai).
        /// </summary>
        public void OnLoadFailed(AdType type, string errorCode)
        {
            _labelLogService.LabelAdView(type, FAdViewLabelKey.LOAD_ERROR, errorCode);
        }

        /// <summary>
        /// Gọi hiển thị thất bại — từ <c>OnAdDisplayFailedEvent</c>. Cùng khuôn
        /// <see cref="OnLoadFailed"/>: nhãn {<see cref="FAdViewLabelKey.SHOW_ERROR"/>: code} lên
        /// view đang mở; shown-THÀNH-CÔNG không có mốc (chính là impression).
        /// <br/>adUnitId / networkName / ecpmFloor / estimatedEcpm: ad đang định chiếu — chỉ cho
        /// <see cref="FBigDataHooks.AdShowFailed"/> (Databuckets), KHÔNG lên BigData.
        /// </summary>
        public void OnShowFailed(AdType type, string errorCode, string adUnitId = null, string networkName = null,
            double? ecpmFloor = null, double? estimatedEcpm = null)
        {
            _adRequestService.ShowFailed(type);
            _labelLogService.LabelAdView(type, FAdViewLabelKey.SHOW_ERROR, errorCode);
            FBigDataHooks.RaiseAdShowFailed(new AdShowFailedInfo
            {
                type = type,
                errorCode = errorCode,
                adUnitId = adUnitId,
                networkName = networkName,
                ecpmFloor = ecpmFloor,
                estimatedEcpm = estimatedEcpm
            });
        }

        /// <summary>
        /// Kết quả MỘT lần gọi load của MỘT ad unit — chỉ số fill/floor win rate của MO. Cứ báo
        /// TỪNG attempt (kể cả retry), volume SDK lo: gộp cụm theo
        /// (unit × floor × network × kết cục), flush lúc app pause thành bản tin
        /// mang <c>count</c> — trăm attempt thành vài dòng, khuôn cụm banner. Kết cục quyết định
        /// EVENT ngay tại client: <c>f_sdk_ad_load_success</c> / <c>f_sdk_ad_load_fail</c>
        /// (không bắt loader gánh mapping split kiểu resource_source/sink — đó là nợ grandfather). Telemetry tầng mediation, KHÔNG thuộc vòng đời ad view.
        /// <br/>Game KHÔNG chạy multi-floor (máy yếu/3G tắt MultiCall, một unit đơn) VẪN gọi —
        /// <c>floor</c> để null; chính nhóm máy yếu/3G là nơi telemetry no-fill/timeout giá trị
        /// nhất, và retry dày cỡ nào cụm cũng nén về vài dòng.
        /// <br/>Thay hẳn đường <c>MAdLog</c>/<c>f_sdk_mo_multiple_floor_adunit</c> cũ — event đó
        /// đã bị CHẶN trên server vì spam per-attempt; module còn gửi chỉ tốn băng thông.
        /// Không nhận revenue/displayed/paid — double coverage với <c>f_sdk_ads_data</c>
        /// (eCPM per floor đọc bằng JOIN adUnitId bên đó).
        /// <br/>⚠ Hai event id mới chờ loader ký. Cụm KHÔNG persist qua kill — telemetry,
        /// mất stretch dở chấp nhận được.
        /// </summary>
        public void OnLoadResult(AdLoadResultParam param)
        {
            _adRequestService.LogLoadResult(param);
        }

        /// <inheritdoc cref="OnLoadResult"/>
        [System.Obsolete("Đổi tên thành OnLoadResult 18/08 — tên cũ gây hiểu nhầm \"không " +
                         "multicall thì không gọi\" trong khi event là kết quả load generic. " +
                         "Hàm này chỉ chuyển tiếp, hành vi y hệt.")]
        public void OnMultiFloorResult(AdMultiFloorParam param)
        {
            OnLoadResult(param);
        }

        /// <summary>
        /// GAME MUỐN CHIẾU AD — gọi ở ĐẦU hàm show của mediation, TRƯỚC nhánh kiểm tra kho
        /// (<c>IsReady</c>). Mốc của phía CẦU: từ khi mediation nạp sẵn ad, một
        /// <see cref="OnRequested"/> phục vụ nhiều lần chiếu nên nó không còn là "một lần user cần
        /// ad" — thiếu mốc này thì ca "muốn chiếu mà kho rỗng" không có dòng nào trên wire.
        /// <br/>Gọi CẢ hai nhánh (có ad và không có ad), chỉ khác giá trị
        /// <paramref name="adAvailable"/> — mốc chỉ-ghi-khi-hỏng là loại mốc quên gọi thì số liệu
        /// đẹp lên im lặng.
        /// </summary>
        /// <param name="adWhere">Chỗ định chiếu — khai cả ở ca không có ad để biết chỗ nào hay hụt.</param>
        /// <param name="adAvailable">Kho có ad sẵn lúc hỏi không (<c>IsReady</c> của mediation).</param>
        public void OnShowAttempt(
            AdType type, string adWhere, bool adAvailable, string adWhen = null, string adMediation = null,
            string triggerPlayTurnId = null)
        {
            _adRequestService.ShowAttempt(type, adWhere, adAvailable, adWhen, adMediation, triggerPlayTurnId);
        }

        /// <summary>
        /// LUẬT CỦA GAME CHẶN MỘT CƠ HỘI CHIẾU AD — game gọi ĐÚNG MỘT LẦN tại điểm chiếu (hết màn, về
        /// Home, quay lại app) khi luật của chính game bảo đừng chiếu: cooldown, user mới, vừa mua,
        /// trần ngày/phiên, luật theo nhóm user… Bắn <c>f_sdk_ad_show_skipped</c> (ký loader 12/11).
        /// <code>
        /// if (secondsSinceLastInter &lt; cooldown)
        /// {
        ///     FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.Cooldown);
        ///     return;
        /// }
        /// </code>
        /// <br/>Ghép với <see cref="OnShowAttempt"/> ra số cơ hội đầy đủ:
        /// <c>cơ hội = show_attempt + show_skipped</c> — nên gọi ở ĐÚNG điểm game sẽ gọi show, không
        /// gọi mỗi lần kiểm tra luật.
        /// <br/>Chỉ cho format GAME TỰ BẬT: <see cref="AdType.Interstitial"/>, <see cref="AdType.AppOpen"/>.
        /// Format khác hoặc thiếu <paramref name="adWhere"/> ⇒ SDK cảnh báo và không gửi.
        /// <br/>User đã có quyền No Ads: GỌI với <see cref="AdSkipReason.NoAds"/> (đứng đầu thứ tự xét) —
        /// ai chặn thì người đó ghi: game tự kiểm No Ads trước khi gọi mediation thì game ghi; để mediation
        /// chặn thì mediation ghi. Không bao giờ cả hai cho cùng một cơ hội.
        /// <br/>Từ 13/11 truyền thêm "ad đang chờ" để đo một cơ hội bị chặn lẽ ra chiếu được gì:
        /// <code>
        /// FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.NoAds,
        ///     adAvailable: MediationManager.Instance.IsInterstitialReady(),
        ///     readyAd: MediationManager.Instance.GetReadyInterstitialInfo());
        /// </code>
        /// </summary>
        /// <param name="adWhere">Điểm chiếu bị chặn — cùng bộ từ với adWhere của OnShowAttempt.</param>
        /// <param name="skipReason">Luật ĐẦU TIÊN chặn, theo thứ tự game xét.</param>
        /// <param name="adAvailable">
        /// Kho mediation có ad sẵn lúc bị chặn không (<c>IsReady</c>). Bỏ trống ⇒ SDK cảnh báo, dòng vắng
        /// field này (kho đọc là "không biết", không phải false).
        /// </param>
        /// <param name="readyAd">
        /// Ad đang chờ do MEDIATION trả (network, unit, eCPM lúc load). Chỉ gửi khi
        /// <paramref name="adAvailable"/> = true; <c>adViewId</c> SDK tự đọc.
        /// </param>
        public void OnShowSkipped(AdType type, string adWhere, AdSkipReason skipReason, string adWhen = null,
            bool? adAvailable = null, AdReadyInfo readyAd = null, string triggerPlayTurnId = null)
        {
            _adRequestService.ShowSkipped(type, adWhere, skipReason, adWhen, adAvailable, readyAd, triggerPlayTurnId);
        }

        /// <summary>
        /// Người chơi bấm vào ad — gọi từ callback click của mediation (OnAdClickedEvent...).
        /// <br/>Mốc THỐNG NHẤT về chữ ký nhưng hiện KHÔNG bắn bản tin riêng: SDK ghi cờ vào view
        /// hiện tại và log close của cùng <c>adViewId</c> tự mang <c>hasClick = true</c> — click
        /// chưa cần độ phân giải per-cú-bấm nên chưa tốn một dòng event. Sau này cần đếm số
        /// lần/timestamp thì đổi ruột (bắn kênh nhãn ad_view) mà không đổi chữ ký này.
        /// <br/>Giá trị <c>hasClick</c> truyền tường minh ở <see cref="OnClosed"/> vẫn thắng cờ này.
        /// </summary>
        public void OnClicked(AdType type)
        {
            _adRequestService.ClickAd(type);
        }

        /// <summary>
        /// Ad đóng lại, người chơi quay về game — mốc cuối của vòng đời một lần xem ad.
        /// SDK tự đo thời lượng hiển thị.
        /// </summary>
        /// <param name="hasClick">Người chơi có bấm vào ad không (bỏ trống nếu không biết).</param>
        /// <param name="adCompleted">Xem hết hay bỏ giữa chừng — rewarded thì là "có được thưởng không".</param>
        /// <param name="param">Tham số mốc close — <c>param.type</c> BẮT BUỘC.</param>
        public void OnClosed(AdCloseParam param)
        {
            _adRequestService.CloseAd(param);
        }

        /// <inheritdoc cref="OnClosed(AdCloseParam)"/>
        public void OnClosed(
            AdType type, bool? hasClick = null, bool? adCompleted = null, string adWhere = null,
            Dictionary<string, object> extraMeta = null)
        {
            _adRequestService.CloseAd(new AdCloseParam
            {
                type = type,
                adWhere = adWhere,
                hasClick = hasClick,
                adCompleted = adCompleted,
                extraMeta = extraMeta
            });
        }
    }
}
