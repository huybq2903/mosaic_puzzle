/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Sáu mốc của một event live-op. Bản gốc định nghĩa nằm ở
    ///     <c>docs/design/LiveOpsTrackingPattern.md</c> §2a (ký 17/09) — ở đây chỉ là hằng số.
    ///     <br/>⚠ Ba mốc đầu là BA MẪU SỐ KHÁC NHAU, đừng gộp một rổ: "event có đáng mở không" đo
    ///     <c>Open</c>/<c>Eligible</c>; "UI có thuyết phục không" đo <c>Join</c>/<c>Open</c>;
    ///     "thiết kế bên trong có gắt không" đo <c>Milestone</c>/<c>Join</c>.
    /// </summary>
    public static class FLiveOpsAction
    {
        /// <summary>
        ///     User ĐỦ ĐIỀU KIỆN tham gia — mẫu số gốc của mọi chart.
        ///     <br/>⚠ Gọi MỖI PHIÊN khi user còn đủ điều kiện và đợt đang chạy, KHÔNG phải một lần
        ///     lúc vượt ngưỡng mở khoá: van lọc đã nén còn một dòng mỗi đợt, còn bắn-một-lần thì từ
        ///     đợt thứ hai mẫu số rỗng (đúng bug Master League G1).
        /// </summary>
        public const string Eligible = "eligible";

        /// <summary>User mở UI của event (van nén còn một dòng mỗi đợt).</summary>
        public const string Open = "open";

        /// <summary>User đăng ký tham gia (event nào có bước tham gia).</summary>
        public const string Join = "join";

        /// <summary>Đạt một mốc — <c>stage</c> = số mốc.</summary>
        public const string Milestone = "milestone";

        /// <summary>Hoàn thành trọn event/đợt.</summary>
        public const string Complete = "complete";

        /// <summary>Nhận thưởng của một mốc — <c>stage</c> = mốc của quà đó.</summary>
        public const string Claim = "claim";
    }
}
