/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-12
 */

using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Trả lời một câu: "app vừa pause có phải vì một ad toàn màn vừa lên hình không?" — để
    /// <c>app_open</c> hot không đếm lần đóng ad thành một lần quay lại app.
    /// <br/>Án (loader đo xác nhận 11/11, <c>Reply-Loader-AdShowSkipped.md</c> mục 3): chiếu ad toàn
    /// màn làm Unity pause trên CẢ Android lẫn iOS; ad ≥ ngưỡng nền thì lúc đóng SDK bắn một hot giả
    /// với <c>backgroundDurationSec</c> ≈ thời lượng ad.
    /// <br/>Luật: pause BẮT ĐẦU trong <see cref="ONSET_WINDOW_MILLIS"/> sau mốc <c>OnShown</c> của
    /// một ad toàn màn chưa đóng / chưa hỏng ⇒ pause do ad. Cửa sổ chặt hơn "giữa OnShown và
    /// OnClosed" (bản đã ký) ở hai chỗ, cả hai đều về phía an toàn:
    /// <br/>· mediation quên OnClosed thì cờ không kẹt — mọi pause về sau vẫn là pause thật;
    /// <br/>· người chơi bấm Home GIỮA lúc xem (iOS: ad không giữ Unity pause suốt) vẫn ra hot thật.
    /// <br/>Mốc <c>OnShown</c> dùng được làm mốc khởi phát khi mediation gọi nó ĐỒNG BỘ quanh lệnh show
    /// (trước khi app pause) — KHÔNG phải mọi đường đều thế:
    /// <br/>Đường đã gọi <c>Ad.OnShown</c> trước lúc pause (soát 12/11): MAX gọi thẳng, MAX MultiCall, MAX qua
    /// FSN, AdMob bridge (<c>AdmobBidAd</c>, ngay sau lệnh show trong cùng frame). Đường CHƯA gọi
    /// <c>Ad.OnShown</c> nào — <c>FalconGmaService</c> (app open GMA), <c>FalconIronSourceService</c> — thì
    /// lần đóng ad vẫn ra hot giả; muốn phủ thì ticket module Mediation.
    /// <br/> Callback hiện/đóng của mediation thì đi qua main thread của
    /// Unity, có thể chỉ về SAU khi app resume — không dựa vào chúng để quyết lúc pause.
    /// <br/>Pure class — caller truyền mốc thời gian (đồng hồ đơn điệu) và tự khoá.
    /// </summary>
    public class FullscreenAdPresenceState
    {
        /// <summary>
        /// Ad lên hình chậm nhất ngần này sau lệnh show. Khoảng thật dưới một giây; để rộng cho máy
        /// yếu. Người chơi rời app trong ngần ấy sau lệnh show thì bị tính là pause do ad — chấp nhận.
        /// </summary>
        public const long ONSET_WINDOW_MILLIS = 10_000;

        private long? _shownAtMillis;

        /// <summary>Mediation vừa gọi show một ad toàn màn.</summary>
        public void MarkShown(long nowMillis)
        {
            _shownAtMillis = nowMillis;
        }

        /// <summary>Ad toàn màn đã đóng hoặc hiển thị hỏng — pause sau đó không còn là do ad.</summary>
        public void MarkEnded()
        {
            _shownAtMillis = null;
        }

        /// <summary>Pause đang bắt đầu lúc này có phải do ad toàn màn vừa lên hình không.</summary>
        public bool IsAdOnset(long nowMillis)
        {
            if (!_shownAtMillis.HasValue) return false;
            var elapsed = nowMillis - _shownAtMillis.Value;
            return elapsed >= 0 && elapsed <= ONSET_WINDOW_MILLIS;
        }

        /// <summary>Format chiếm toàn màn (che Unity) — banner / native / MREC thì không.</summary>
        public static bool IsFullscreen(AdType type)
        {
            return type == AdType.Interstitial || type == AdType.Reward || type == AdType.AppOpen;
        }
    }
}
