/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-09-05
 */

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Tên bề mặt Ui ĐĂNG KÝ dùng chung mọi game — để kho tính được một tỉ lệ chéo game mà không
    /// phải học vocab riêng của từng game (cùng lẽ với các hằng số funnel dùng chung). Bề mặt
    /// không có ở đây thì game tự đặt tên. <b>Thêm giá trị = SỬA HỢP ĐỒNG</b> (§H2).
    /// </summary>
    public static class FUiSurface
    {
        /// <summary>
        ///     Popup MỜI HỒI SINH (ký với loader 09/11). Hiện = <see cref="FUiApi.OnImpression"/>,
        ///     nhận = <see cref="FUiApi.OnClicked"/>, từ chối = <see cref="FUiApi.OnDismissed"/>; lần
        ///     hồi sinh đã dùng ghi riêng bằng <c>Level.OnRevive</c>. Tỉ lệ nhận phía kho =
        ///     <c>sum(click_count) / sum(impression_count)</c> trên bề mặt này.
        /// </summary>
        public const string ReviveOffer = "revive_offer";

        /// <summary>
        ///     Popup GỢI Ý DÙNG BOOSTER (ký với loader 10/11). Hiện / bấm / đóng như <see cref="ReviveOffer"/>; lần dùng
        ///     booster ghi riêng bằng <c>Level.OnBooster</c>.
        /// </summary>
        public const string BoosterOffer = "booster_offer";
    }

    /// <summary>
    /// Entity EXPOSURE UI — <c>FalconBigDataController.Ui</c>: đếm bề mặt (icon/popup/banner) hiện
    /// ra, được bấm, bị đóng.
    /// <br/>Van gộp là BẮT BUỘC (chốt owner 05/09): per-lần-hiện không bao giờ lên wire — SDK gộp
    /// theo cụm và flush lúc app pause thành bản tin <c>f_sdk_ui_exposure</c>, mỗi cụm một dòng với
    /// bốn cột đếm; tỉ lệ phía server = sum/sum, client chỉ NÉN không tính.
    /// <br/>Mỗi hàm dưới là MỘT sự kiện cố định — không có tham số "hành vi" nào để chọn.
    /// <br/>KHÁC bước funnel <c>open</c>: open là MỐC phễu (một dòng/chu kỳ — "đã xem hay chưa"),
    /// còn đây là MÁY ĐẾM ("hiện bao nhiêu lần") — hai câu hỏi, hai grain, đừng dùng lẫn.
    /// </summary>
    public class FUiApi
    {
        private readonly UiExposureClusterService _clusterService;

        internal FUiApi(UiExposureClusterService clusterService)
        {
            _clusterService = clusterService;
        }

        /// <summary>
        /// Một bề mặt UI vừa THẬT SỰ hiện ra với người chơi (icon event xuất hiện trên home,
        /// popup bật lên...) — không phải mỗi frame, không phải lúc nằm ngoài màn hình.
        /// Cứ gọi mỗi lần, volume SDK lo.
        /// </summary>
        /// <param name="surfaceId">Bề mặt nào — hằng số ở <see cref="FUiSurface"/> nếu có, không thì vocab của game, đặt tên ổn định ("race_event_entry", "race_event_popup").</param>
        /// <param name="extraMeta">Ngữ cảnh BẤT BIẾN của surface trong stretch (vd race_id) — cụm giữ bản của LẦN GHI ĐẦU (hiện, bấm hay đóng), thứ đổi theo từng lần sẽ bị rơi.</param>
        /// <param name="liveOps">
        /// Ngữ cảnh của bề mặt — chở được CẢ HAI TRỤC: event live-op (hoặc để
        /// <c>LiveOps.Stamp(...)</c> điền) và tính năng (<c>Feature.Stamp(...)</c>). Cả bảy giá trị nằm
        /// trong KHOÁ GỘP, nên hai đợt — hay hai bang hội — không bị nén vào một dòng.
        /// </param>
        /// <param name="trigger">
        /// VÌ SAO bề mặt này hiện ra — vocab của game, snake_case, đặt tên ổn định
        /// ("auto_on_home", "after_level_win", "from_shop_button"). <b>Nằm trong KHOÁ GỘP</b>: cùng
        /// một popup bật tự động và bật do người chơi bấm là hai hành vi khác nhau. Truyền CÙNG
        /// trigger cho lần hiện và lần bấm/đóng tương ứng, không thì chúng rơi vào hai cụm.
        /// <br/>Vắng = game không khai. SDK không kiểm theo danh sách — cùng lẽ với <c>feature</c>.
        /// </param>
        public void OnImpression(
            string surfaceId, Dictionary<string, object> extraMeta = null, LiveOpsRef liveOps = null,
            string trigger = null)
        {
            _clusterService.Record(surfaceId, UiTally.Impression, extraMeta, liveOps, trigger);
        }

        /// <summary>
        /// Nút xem quảng cáo vừa hiện ra <b>trong lúc quảng cáo chưa sẵn</b>. Đây vẫn là MỘT lần
        /// hiện: hàm này cộng cả <c>impressionCount</c> lẫn <c>adNotReadyCount</c> — đừng gọi thêm
        /// <see cref="OnImpression"/> cho cùng lần hiện đó, sẽ đếm đôi.
        /// </summary>
        /// <inheritdoc cref="OnImpression"/>
        public void OnImpressionAdNotReady(
            string surfaceId, Dictionary<string, object> extraMeta = null, LiveOpsRef liveOps = null,
            string trigger = null)
        {
            _clusterService.Record(surfaceId, UiTally.ImpressionAdNotReady, extraMeta, liveOps, trigger);
        }

        /// <summary>Người chơi làm HÀNH ĐỘNG CHÍNH của bề mặt (bấm icon, bấm "Hồi sinh"...) — cùng <paramref name="surfaceId"/> và trigger với lần hiện tương ứng.</summary>
        /// <inheritdoc cref="OnImpression"/>
        public void OnClicked(
            string surfaceId, Dictionary<string, object> extraMeta = null, LiveOpsRef liveOps = null,
            string trigger = null)
        {
            _clusterService.Record(surfaceId, UiTally.Click, extraMeta, liveOps, trigger);
        }

        /// <summary>
        /// Người chơi ĐÓNG bề mặt mà không làm hành động chính: bấm X, bấm "Không, cảm ơn", bấm ra
        /// ngoài. <b>Không</b> gọi khi app bị tắt ngang (không ai biết lúc đó), và không gọi khi bề
        /// mặt đóng vì hành động chính đã xảy ra.
        /// </summary>
        /// <inheritdoc cref="OnImpression"/>
        public void OnDismissed(
            string surfaceId, Dictionary<string, object> extraMeta = null, LiveOpsRef liveOps = null,
            string trigger = null)
        {
            _clusterService.Record(surfaceId, UiTally.Dismiss, extraMeta, liveOps, trigger);
        }
    }
}
