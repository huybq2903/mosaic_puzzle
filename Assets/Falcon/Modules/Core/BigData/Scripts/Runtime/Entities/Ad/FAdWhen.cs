/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Danh sách chung của <c>adWhen</c> — VÌ SAO lần QC này được chiếu (hợp đồng đợt 3 B12, ký 23/11). Danh sách
    /// ĐÓNG, chữ thường; riêng <see cref="Unknown"/> giữ chữ hoa vì là giá trị cũ của mốc impression.
    /// <br/>Giá trị ngoài danh sách vẫn GỬI NGUYÊN (field là chuỗi, chữ tự do cũ của game không vỡ) — SDK chỉ cảnh
    /// báo ở log dev, kho tự quy về danh sách khi đọc. Thêm giá trị = sửa hợp đồng, báo loader trước khi ship.
    /// <br/>Đi cùng <c>triggerPlayTurnId</c> (lượt nào kích hoạt lần chiếu) — game khai cả hai tại chỗ gọi chiếu.
    /// </summary>
    public static class FAdWhen
    {
        /// <summary>Sau khi thắng level (Inter kết quả, thưởng x2 ở màn thắng…).</summary>
        public const string LevelWin = "level_win";

        /// <summary>Sau khi thua level.</summary>
        public const string LevelFail = "level_fail";

        /// <summary>Lúc vào level (trước hoặc ngay sau <c>Level.OnStart</c>).</summary>
        public const string LevelStart = "level_start";

        /// <summary>Lúc bấm chơi lại.</summary>
        public const string Retry = "retry";

        /// <summary>QC thưởng để hồi sinh — trong lượt đang mở.</summary>
        public const string Revive = "revive";

        /// <summary>QC thưởng GIỮA level để lấy booster / gợi ý / thêm nước — trong lượt đang mở.</summary>
        public const string Booster = "booster";

        /// <summary>Ở màn chính, ngoài level.</summary>
        public const string Home = "home";

        /// <summary>QC lúc mở app.</summary>
        public const string AppOpen = "app_open";

        /// <summary>Lý do khác không có trong danh sách.</summary>
        public const string Other = "other";

        /// <summary>Game không điền — giá trị mặc định cũ của mốc impression, giữ chữ hoa.</summary>
        public const string Unknown = FParam.UNKNOWN;

        private static readonly HashSet<string> kKnown = new(StringComparer.Ordinal)
        {
            LevelWin, LevelFail, LevelStart, Retry, Revive, Booster, Home, AppOpen, Other, Unknown
        };

        /// <summary>Giá trị có nằm trong danh sách chung không (phân biệt hoa thường).</summary>
        public static bool IsKnown(string adWhen) => adWhen != null && kKnown.Contains(adWhen);

        /// <summary>
        /// Câu cảnh báo cho log dev nếu <paramref name="adWhen"/> là lúc CHIẾU mà vẫn không rõ lý do (trống /
        /// <see cref="Unknown"/>) hoặc nằm ngoài danh sách; null nếu ổn. Không bao giờ chặn gửi.
        /// </summary>
        /// <param name="adWhen">Giá trị sẽ lên wire.</param>
        /// <param name="where">Tên mốc gọi (cho câu cảnh báo).</param>
        public static string WarningAtShow(string adWhen, string where)
        {
            if (string.IsNullOrEmpty(adWhen) || adWhen == Unknown)
                return $"{where}: adWhen trống / Unknown — kho không biết lần QC này chiếu vì sao. Điền FAdWhen.* tại " +
                       "chỗ gọi chiếu (hoặc Ad.UpdateContext trước khi chiếu).";
            return WarningIfOutsideList(adWhen, where);
        }

        /// <summary>Câu cảnh báo nếu giá trị (khác trống) nằm ngoài danh sách chung; null nếu ổn.</summary>
        public static string WarningIfOutsideList(string adWhen, string where)
        {
            if (string.IsNullOrEmpty(adWhen) || IsKnown(adWhen)) return null;
            return $"{where}: adWhen '{adWhen}' không thuộc danh sách chung FAdWhen — vẫn gửi nguyên, nhưng kho phải tự " +
                   "quy về danh sách. Dùng FAdWhen.* (thiếu giá trị thì xin sửa hợp đồng).";
        }
    }
}
