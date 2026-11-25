/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Luật chung của họ <c>f_sdk_level_action_*</c> (hồi sinh, booster) — một chỗ duy nhất, để hai event
    /// không lệch luật nhau. Sai thì BỎ KEY kèm cảnh báo, không bao giờ bịa một giá trị trông hợp lệ.
    /// </summary>
    internal static class LevelActionRules
    {
        /// <summary>
        /// <c>progressPct</c> phải trong <c>[0, 100]</c>. Ngoài khoảng: bỏ key, KHÔNG kẹp — kẹp 150 về 100 là
        /// biến một phép tính sai của game thành "gần xong", một số trông hợp lệ.
        /// </summary>
        public static float? Progress(float? value, string context)
        {
            if (!value.HasValue)
            {
                AnalyticLogger.Instance.Warning(
                    $"{context}: thiếu progressPct — không có nó thì không đọc được 'xảy ra ở đoạn nào của ván'.");
                return null;
            }

            if (value.Value >= 0f && value.Value <= 100f) return value;

            AnalyticLogger.Instance.Warning(
                $"{context}: progressPct = {value.Value} ngoài khoảng [0, 100] — BỎ KEY, không kẹp về biên.");
            return null;
        }

        /// <summary>
        /// <c>payMethod</c> thuộc tập đóng <see cref="FPayMethod"/>. Lạ: bỏ key — đừng thay bằng <c>free</c>:
        /// <c>free</c> nghĩa là GAME CHO KHÔNG, khác hẳn "game không khai".
        /// </summary>
        [CanBeNull]
        public static string PayMethod([CanBeNull] string value, string context)
        {
            if (string.IsNullOrEmpty(value) || FPayMethod.IsKnown(value)) return value;

            AnalyticLogger.Instance.Warning(
                $"{context}: payMethod '{value}' không thuộc tập đóng — BỎ KEY. Không biết thì để vắng.");
            return null;
        }

        /// <summary>Vocab mở của game: chuỗi trắng là "không khai" ⇒ vắng.</summary>
        [CanBeNull]
        public static string OpenVocab([CanBeNull] string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
