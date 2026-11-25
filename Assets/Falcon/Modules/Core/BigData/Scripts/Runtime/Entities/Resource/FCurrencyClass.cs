/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     HẠNG của một đồng tiền — <b>tập ĐÓNG, đúng 9 giá trị</b>, ký với loader 06/11.
    ///     Thêm hạng mới = sửa hợp đồng (§H2), đừng gõ chuỗi tay.
    ///     <br/>Vì sao hạng phải do GAME khai chứ không để kho đoán: người duy nhất biết chắc
    ///     <c>screw</c> hay <c>energy</c> là gì là người định nghĩa nó trong game, ở đúng lúc định
    ///     nghĩa. Kho đoán theo tên thì luôn đi sau game một nhịp — tên mới ra là dữ liệu không có
    ///     hạng cho tới khi có người rà.
    ///     <br/>⚠ <see cref="Unlimited"/> và <see cref="EventCounter"/> là <b>PHI KINH TẾ</b>: báo
    ///     cáo kinh tế loại chúng khỏi net-flow. Luật đọc của kho đi theo HẠNG, không liệt kê tên.
    /// </summary>
    public static class FCurrencyClass
    {
        /// <summary>Tiền cứng — chủ yếu có được bằng tiền thật (gem, ruby).</summary>
        public const string Hard = "hard";

        /// <summary>Tiền mềm — kiếm được khi chơi, tiêu được ở shop (gold, coin).</summary>
        public const string Soft = "soft";

        /// <summary>Mạng / lượt chơi có hồi theo giờ (heart, stamina).</summary>
        public const string Live = "live";

        /// <summary>Vật phẩm trợ giúp trong ván (hammer, hint).</summary>
        public const string Booster = "booster";

        /// <summary>Quyền dùng vô hạn CÓ THỜI HẠN — không phải số dư. PHI KINH TẾ.</summary>
        public const string Unlimited = "unlimited";

        /// <summary>Nguyên liệu để chế / nâng cấp (card, fragment, blueprint).</summary>
        public const string Material = "material";

        /// <summary>Điểm / đếm của sự kiện live-op, không mua bán được. PHI KINH TẾ.</summary>
        public const string EventCounter = "event_counter";

        /// <summary>Điểm tiến trình dùng để mở khoá, không tiêu (exp, star).</summary>
        public const string Progress = "progress";

        /// <summary>Vé vào chế độ / lượt quay (key, ticket).</summary>
        public const string Ticket = "ticket";

        private static readonly HashSet<string> kAll = new()
        {
            Hard, Soft, Live, Booster, Unlimited, Material, EventCounter, Progress, Ticket
        };

        /// <summary>
        ///     Có thuộc tập đóng không. So khớp **chỉ thường hoá, KHÔNG bỏ <c>_</c>** — ký với loader
        ///     06/11 (§2.3), và loader cũng chỉ thường hoá.
        ///     <br/>⚠ Cố ý KHÔNG dùng <c>FExtraMeta</c>/<c>FWireReservedKeys.Normalize</c>: hai hàm đó
        ///     còn bỏ dấu <c>_</c>, nên <c>eventcounter</c> sẽ lọt — mà <c>event_counter</c> là một
        ///     trong hai hạng PHI KINH TẾ, lọt một biến thể là mở đường cho hai chuỗi cùng nghĩa trên
        ///     cùng một cột.
        /// </summary>
        public static bool IsValid(string value)
        {
            return !string.IsNullOrEmpty(value) && kAll.Contains(value.ToLowerInvariant());
        }

        /// <summary>Dạng chuẩn để lên dây, hoặc <c>null</c> nếu không thuộc tập đóng.</summary>
        public static string Canonical(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var lowered = value.ToLowerInvariant();
            return kAll.Contains(lowered) ? lowered : null;
        }
    }
}
