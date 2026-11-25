/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Phần cộng thêm vào một cụm cho MỘT sự kiện Ui. Chỉ có đúng bốn giá trị dựng sẵn — ctor
    /// <c>private</c> — mỗi hàm công khai của <see cref="FUiApi"/> dùng một giá trị cố định, nên
    /// không có tham số "hành vi" nào chạy xuyên qua kho cụm để rẽ nhánh.
    /// <br/>🔴 Bất biến <c>adNotReady ≤ impressions</c> trên TỪNG dòng (ký với loader 09/11) đứng
    /// được là nhờ chỗ này: giá trị duy nhất có <c>adNotReady</c> là
    /// <see cref="ImpressionAdNotReady"/>, và nó cộng cả hai cột trong CÙNG một lần ghi — nên hai
    /// số không bao giờ rơi vào hai cụm khác nhau, kể cả khi van xả sớm cắt giữa chừng.
    /// </summary>
    public readonly struct UiTally
    {
        public readonly int impressions;
        public readonly int clicks;
        public readonly int dismisses;
        public readonly int adNotReady;

        private UiTally(int impressions, int clicks, int dismisses, int adNotReady)
        {
            this.impressions = impressions;
            this.clicks = clicks;
            this.dismisses = dismisses;
            this.adNotReady = adNotReady;
        }

        /// <summary>Bề mặt hiện ra.</summary>
        public static readonly UiTally Impression = new(1, 0, 0, 0);

        /// <summary>Bề mặt hiện ra trong lúc quảng cáo chưa sẵn — vẫn là MỘT lần hiện.</summary>
        public static readonly UiTally ImpressionAdNotReady = new(1, 0, 0, 1);

        /// <summary>Người chơi làm hành động chính.</summary>
        public static readonly UiTally Click = new(0, 1, 0, 0);

        /// <summary>Người chơi đóng mà không làm hành động chính.</summary>
        public static readonly UiTally Dismiss = new(0, 0, 1, 0);
    }
}
