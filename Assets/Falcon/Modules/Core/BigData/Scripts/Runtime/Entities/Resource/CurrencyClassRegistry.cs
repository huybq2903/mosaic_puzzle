/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System.Collections.Concurrent;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Sổ khai HẠNG của từng tên tiền (<c>Resource.DeclareCurrency</c>) — ký với loader 06/11.
    ///     Khai một lần ở chỗ định nghĩa ví, SDK tự đóng lên mọi dòng tài nguyên; không bắt caller
    ///     điền hạng ở từng cú log, vì trăm chỗ gọi là trăm cơ hội gõ sai.
    ///     <br/>Cùng khuôn <see cref="FunnelRegistry"/>: khai từ CODE mỗi lần khởi động,
    ///     <b>không persist</b> — persist là đẻ ra nguồn chân lý thứ hai có thể cãi nhau với code
    ///     sau khi game update.
    ///     <br/><br/>🔴 <b>LẦN KHAI ĐẦU THẮNG</b> — khác <see cref="FunnelRegistry"/> (lần sau thắng),
    ///     và khác cả bản đầu của chính ticket kho. Đây là chỗ bên mình đề nghị sửa spec và loader
    ///     đồng ý (§2.2 bản ký): hạng của một đồng tiền là một SỰ THẬT về game, không phải một cấu
    ///     hình để ghi đè. Hai chỗ khai hai hạng khác nhau nghĩa là <b>có một chỗ sai</b>; để lần sau
    ///     thắng thì hạng phụ thuộc thứ tự khởi động — cùng một build có thể gửi hai hạng khác nhau
    ///     giữa hai phiên, và không ai soi ra. Lần đầu thắng + cảnh báo thì ít nhất nó ổn định và có
    ///     người được nhắc.
    ///     <br/>⚠ So khớp tên tiền <b>chỉ thường hoá, KHÔNG bỏ dấu <c>_</c></b> (§2.3 bản ký; loader
    ///     cũng vậy). Dùng lại <c>Normalize</c> của <c>FExtraMeta</c> là sai, hàm đó bỏ <c>_</c>.
    ///     <br/><br/>⚠ <b>Lớp STATIC, không phải <c>MySingleton</c></b> như <see cref="FunnelRegistry"/>
    ///     — cùng khuôn <c>FWireReservedKeys</c>/<c>FExtraMeta</c>. Hai lý do: (1) nó không giữ STATE
    ///     của ai cả, chỉ giữ một lời KHAI từ code, process-wide; (2) <c>ResourceParam.CorrectValues</c>
    ///     là nơi đóng dấu, mà param không có DI — đi qua singleton ở đó thì **mọi test của đường đóng
    ///     dấu đều cần Unity runtime**, tức nhóm chỉ được compile chứ chưa từng chạy. Đổi sang static
    ///     thì bốn bất biến quan trọng nhất của cột này chạy được trong ~30 giây.
    /// </summary>
    public static class CurrencyClassRegistry
    {
        private static readonly ConcurrentDictionary<string, string> kClasses = new();

        /// <summary>Tên đã nhắc "chưa khai hạng" — nhắc MỘT lần mỗi tên mỗi phiên, không mỗi dòng.</summary>
        private static readonly ConcurrentDictionary<string, bool> kWarnedUndeclared = new();

        /// <summary>
        ///     Khai hạng cho một tên tiền. Gọi ở chỗ định nghĩa ví / định nghĩa resource, trước lần
        ///     log đầu tiên.
        ///     <br/>Hạng ngoài tập đóng thì BỎ + error log (không xếp bừa). Khai lại cùng hạng thì im;
        ///     khai lại khác hạng thì <b>giữ lần đầu</b> + cảnh báo.
        /// </summary>
        public static void Declare(string currency, string currencyClass)
        {
            var key = Key(currency);
            if (key == null) return;

            var canonical = FCurrencyClass.Canonical(currencyClass);
            if (canonical == null)
            {
                AnalyticLogger.Instance.Error(
                    $"DeclareCurrency('{currency}', '{currencyClass}'): hạng không thuộc tập đóng 9 " +
                    "giá trị của FCurrencyClass — BỎ lời khai này. Không chắc thì đừng khai: vắng hạng " +
                    "thì kho tra theo tên, còn hạng sai thì kho TIN và đọc sai cả net-flow kinh tế.");
                return;
            }

            if (kClasses.TryGetValue(key, out var existing))
            {
                if (existing == canonical) return;

                AnalyticLogger.Instance.Warning(
                    $"DeclareCurrency('{currency}'): đã khai hạng '{existing}', nay khai '{canonical}' " +
                    "— GIỮ lần khai đầu. Hai hạng khác nhau cho một đồng tiền nghĩa là có một chỗ sai; " +
                    "sửa ở chỗ khai, đừng khai lại để đè.");
                return;
            }

            kClasses[key] = canonical;
        }

        /// <summary>
        ///     Hạng đã khai của một tên tiền, hoặc <c>null</c> nếu chưa khai — <b>không đoán, không
        ///     có giá trị mặc định</b>. Chưa khai thì SDK bỏ key và kho tra theo tên như cũ.
        /// </summary>
        public static string ClassOf(string currency)
        {
            var key = Key(currency);
            if (key == null) return null;
            return kClasses.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        ///     Hạng để đóng lên một dòng log. Chưa khai thì nhắc dev MỘT lần cho mỗi tên mỗi phiên —
        ///     để dev thấy thiếu ngay lúc chơi thử, thay vì kho phát hiện sau vài tuần.
        ///     <br/>⚠ Nhắc một lần mỗi TÊN, không mỗi DÒNG: đây là đường nóng.
        /// </summary>
        public static string ClassForLog(string currency)
        {
            var key = Key(currency);
            if (key == null) return null;
            if (kClasses.TryGetValue(key, out var value)) return value;

            if (kWarnedUndeclared.TryAdd(key, true))
                AnalyticLogger.Instance.Warning(
                    $"Tài nguyên '{currency}' chưa khai hạng — dòng log sẽ VẮNG currencyClass và kho " +
                    "phải tự đoán theo tên. Khai một dòng ở chỗ định nghĩa ví: " +
                    "FalconBigDataController.Resource.DeclareCurrency(\"" + currency +
                    "\", FCurrencyClass.<hạng>). Không chắc hạng thì đừng khai.");

            return null;
        }

        private static string Key(string currency)
        {
            // Chỉ thường hoá. KHÔNG bỏ dấu "_" — xem ghi chú lớp.
            return string.IsNullOrEmpty(currency) ? null : currency.ToLowerInvariant();
        }
    }
}
