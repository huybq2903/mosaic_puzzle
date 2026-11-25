/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-11
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Nhãn NGƯỜI CHƠI đã đăng ký trong hợp đồng — tên do SDK cấp nên cả fleet gọi giống nhau và
    /// server so được chéo game. Nhãn tự do vẫn gõ tên tuỳ ý qua <c>Label.User(...)</c>.
    /// <br/>Đây là chữ trên dây, KHÔNG phải tên field C#.
    /// </summary>
    public static class FUserLabelKey
    {
        /// <summary>
        /// Điểm trình độ người chơi. Trước 1.3.0 nó là field phẳng trên mọi level event — sai chủ
        /// thể (§H6: elo tả NGƯỜI CHƠI, không tả lượt) và nhân bản một sự thật của user lên stream
        /// dày nhất hệ. Nay gửi KHI ĐỔI, server forward-fill lên các event sau.
        /// </summary>
        public const string ELO = "elo";

        /// <summary>
        /// Người chơi đang có quyền BỎ QUẢNG CÁO (mua gói No Ads, hoặc thuê bao Remove Ads còn hạn).
        /// <br/>Giá trị <c>true</c> lúc cấp quyền; <c>null</c> lúc MẤT quyền (thuê bao hết hạn, hoàn
        /// tiền) — null là lệnh gỡ nhãn. Không gửi <c>false</c>: "không có quyền" là trạng thái mặc
        /// định, gửi false cho mọi user chỉ tốn bản tin.
        /// <br/>Ai gửi: module AnalyticParamsSync TỰ gắn theo cờ <c>GameData4RemoveAds</c> của Mediation
        /// (<c>NoAdsLabelSync</c>: đầu phiên + mỗi lần pause, chỉ gửi khi đổi). Game không dùng module đó
        /// thì tự gọi <c>Label.User(FUserLabelKey.NO_ADS, true)</c> ở chỗ cấp / thu quyền. BigData không
        /// tự đọc được cờ của mediation (mediation phụ thuộc BigData, không có chiều ngược lại).
        /// <br/>Ngày hết hạn thuê bao KHÔNG gộp vào đây — một giá trị lúc là bool lúc là mốc thời gian
        /// là field bị chính nó điều khiển nghĩa. Cần thì là một tên riêng.
        /// <br/>Ký loader 12/11 (<c>Request-Loader-AdShowSkipped.md</c> mục 2). Kho dựng đường nhãn người
        /// chơi 13/11 (<c>user_properties.user_labels</c>, mang lên event sau qua snapshot hồ sơ).
        /// </summary>
        public const string NO_ADS = "no_ads";
    }
}
