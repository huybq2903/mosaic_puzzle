/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System.Collections.Concurrent;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Sổ khai MÃ BOOSTER (<c>Resource.RegisterBooster</c>) — phương án (a) của phía đọc số, họ
    ///     chốt 06/11 kèm câu *"làm chung cơ chế với khai hạng tiền"*. Cùng khuôn
    ///     <see cref="CurrencyClassRegistry"/>: khai từ code, không persist.
    ///     <br/><br/>🔴 <b>Vấn đề nó giải</b>: mã booster xuất hiện ở <b>hai</b> stream do game tự
    ///     đặt tên — khoá của <c>boosters_used</c>/<c>pre_boosters_used</c> trên dòng level, và
    ///     <c>currency</c> trên dòng tài nguyên — mà không chỗ nào bắt trùng. Phía đọc số đo 04/11:
    ///     phần lớn game có booster nhưng chỉ một phần nhỏ lượt dùng là khớp đúng tên. Lệch tên thì
    ///     ba câu hỏi đóng lại: *booster đã dùng là mua hay được tặng*, *mua booster rồi có dùng
    ///     không*, *số dư booster lúc dùng*.
    ///     <br/>⚠ <b>Mã lạ vẫn GỬI</b>, chỉ cảnh báo ở log dev (hợp đồng đã ký). SDK không chặn dữ
    ///     liệu của game vì game gõ khác sổ — chặn là mất dòng, mà mất dòng tệ hơn lệch tên.
    ///     <br/><br/>⚠ <b>Khai booster KHÔNG tự khai hạng <c>booster</c></b>, cố ý. Loader §7.1 xin
    ///     *"một nguồn khai duy nhất cho mỗi game"*, mà tự khai hạng ở đây là mở <b>đường ghi thứ
    ///     hai</b> vào sổ hạng — rồi nó cãi nhau với <c>DeclareCurrency</c> và, với luật lần-đầu-thắng,
    ///     kết quả phụ thuộc thứ tự khởi động. Thay vào đó <see cref="WarnIfClassDisagrees"/> chỉ
    ///     ĐỌC và nhắc, không ghi.
    ///     <br/>⚠ Lớp STATIC cùng lẽ với <see cref="CurrencyClassRegistry"/> — xem ghi chú ở đó.
    /// </summary>
    public static class FBoosterRegistry
    {
        private static readonly ConcurrentDictionary<string, bool> kIds = new();
        private static readonly ConcurrentDictionary<string, bool> kWarnedUnknown = new();
        private static readonly ConcurrentDictionary<string, bool> kWarnedDisagree = new();

        /// <summary>
        ///     Khai một mã booster. Gọi ở chỗ định nghĩa booster, trước lần log đầu tiên. Khai lại thì
        ///     im (không phải lời khai có giá trị để cãi nhau, chỉ là một cái tên có trong sổ).
        /// </summary>
        public static void Register(string boosterId)
        {
            var key = Key(boosterId);
            if (key != null) kIds[key] = true;
        }

        public static bool IsRegistered(string boosterId)
        {
            var key = Key(boosterId);
            return key != null && kIds.ContainsKey(key);
        }

        /// <summary>Sổ còn rỗng = game chưa khai gì ⇒ im hẳn, đừng nhắc về từng mã.</summary>
        public static bool IsEmpty => kIds.IsEmpty;

        /// <summary>
        ///     Mã không có trong sổ thì nhắc MỘT lần cho mỗi mã mỗi phiên (đường nóng — đừng nhắc mỗi
        ///     dòng). Sổ rỗng thì im: game chưa dùng cơ chế này, nhắc là ồn vô ích.
        /// </summary>
        public static void WarnIfUnknown(string boosterId, string where)
        {
            var key = Key(boosterId);
            if (key == null || IsEmpty || kIds.ContainsKey(key)) return;
            if (!kWarnedUnknown.TryAdd(key, true)) return;

            AnalyticLogger.Instance.Warning(
                $"Booster '{boosterId}' ({where}) không có trong sổ đã khai. Vẫn gửi, nhưng mã lệch " +
                "giữa dòng level và dòng tài nguyên là lý do kho không nối được 'mua booster' với " +
                "'dùng booster'. Khai bằng FalconBigDataController.Resource.RegisterBooster(id), và " +
                "dùng ĐÚNG mã đó ở cả boosters_used lẫn currency của dòng tài nguyên.");
        }

        /// <summary>
        ///     Chỉ ĐỌC sổ hạng và nhắc khi một mã booster đã khai lại mang hạng khác
        ///     <see cref="FCurrencyClass.Booster"/> — hoặc chưa có hạng nào.
        /// </summary>
        public static void WarnIfClassDisagrees(string boosterId, string declaredClass)
        {
            if (!IsRegistered(boosterId)) return;
            if (declaredClass == FCurrencyClass.Booster) return;

            // Nhắc MỘT lần mỗi mã mỗi phiên. Thiếu cửa này thì mọi game khai booster trước khi khai
            // hạng sẽ nhận một cảnh báo cho MỖI DÒNG tài nguyên booster — đường nóng.
            if (!kWarnedDisagree.TryAdd(Key(boosterId) ?? string.Empty, true)) return;

            AnalyticLogger.Instance.Warning(
                $"'{boosterId}' nằm trong sổ booster nhưng hạng tiền của nó là " +
                $"'{declaredClass ?? "chưa khai"}' — hai lời khai không khớp. Sửa ở chỗ khai: sổ " +
                "booster và sổ hạng là hai câu hỏi khác nhau, nhưng một món nằm trong sổ booster thì " +
                $"hạng của nó gần như luôn là '{FCurrencyClass.Booster}'.");
        }

        /// <summary>
        ///     Soi mọi khoá của một bộ đếm booster trên dòng level (<c>boosters_used</c> /
        ///     <c>pre_boosters_used</c>) — đây là nửa mà hợp đồng §2(a) nói tới: mã ở dòng level và mã
        ///     ở dòng tài nguyên phải là CÙNG một chuỗi, không thì ba câu hỏi đóng lại (*booster đã
        ///     dùng là mua hay được tặng*, *mua rồi có dùng không*, *số dư lúc dùng*).
        /// </summary>
        public static void WarnIfAnyUnknown(IReadOnlyDictionary<string, int> boosters, string where)
        {
            if (boosters == null || IsEmpty) return;
            foreach (var key in boosters.Keys) WarnIfUnknown(key, where);
        }

        private static string Key(string boosterId)
        {
            return string.IsNullOrEmpty(boosterId) ? null : boosterId.ToLowerInvariant();
        }
    }
}
