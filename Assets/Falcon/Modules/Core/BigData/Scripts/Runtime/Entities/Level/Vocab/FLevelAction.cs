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
    ///     Hành động trong một lượt chơi — vocab của <c>f_sdk_level_action</c> (CHỈ 1.7.0, đã ngừng).
    ///     Còn dùng để <see cref="LevelActionAdapter"/> chuyển lời gọi <c>Level.OnAction</c> cũ.
    ///     <b>Tập ĐÓNG, thêm giá trị = sửa hợp đồng</b> (§H2).
    ///     <br/>⚠ Bảng ký có dấu <i>"…"</i> sau hai giá trị này. Dấu đó <b>không phải giấy phép tự
    ///     thêm</b> — bản này gửi đúng hai từ đã ký, ai cần từ thứ ba thì đi qua hợp đồng. (Lượt soát
    ///     09/11 nhắc đúng chỗ này, vì repo đã một lần tự đặt ra vocab <c>easy/normal/hard</c> trong
    ///     khi spec ghi <i>Hard / Super Hard</i>.)
    /// </summary>
    public static class FLevelAction
    {
        /// <summary>Dùng vật phẩm trợ giúp trong ván.</summary>
        public const string Booster = "booster";

        /// <summary>Hồi sinh / mua thêm lượt, thêm giờ để chơi tiếp.</summary>
        public const string Revive = "revive";

        private static readonly HashSet<string> kAll = new() { Booster, Revive };

        public static bool IsKnown(string action)
        {
            return !string.IsNullOrEmpty(action) && kAll.Contains(action);
        }
    }

    /// <summary>
    ///     TRẢ BẰNG GÌ cho một lần hồi sinh đã dùng (<see cref="LevelReviveParam.payMethod"/>) — đề nghị của loader, SDK nhận 09/11. Tập ĐÓNG.
    ///     <br/>🔴 <b>Vì sao nó không trùng dòng tài nguyên</b> (lý do nhận chứ không gạt): hai giá
    ///     trị <see cref="Ad"/> và <see cref="Free"/> <b>không sinh dòng tài nguyên nào</b>. Thiếu
    ///     field này thì <i>hồi sinh bằng xem QC</i>, <i>hồi sinh bằng vàng</i> và <i>hồi sinh miễn
    ///     phí</i> là ba thứ không phân biệt được từ kho — hai cái sau join được qua
    ///     <c>playTurnId</c>, cái đầu không có gì để join. Và đường join lý thuyết qua
    ///     <c>ad_view_id</c> trên dòng tài nguyên thì kho vừa đo: gần như chưa tồn tại.
    ///     <br/>⚠ <see cref="Ad"/> <b>không bảo đảm có dòng <c>f_sdk_ad_view</c></b>: người chơi bấm
    ///     "xem QC để hồi sinh" và được hồi sinh là một dòng <c>ad</c>, nhưng QC có thể hỏng giữa đường ở
    ///     nhánh game tự thưởng bù. Đếm <i>số lần hồi sinh bằng QC</i> thì đọc field này; đếm <i>số QC
    ///     đã chiếu</i> thì đọc stream ad. Đừng suy cái này ra cái kia.
    /// </summary>
    public static class FPayMethod
    {
        public const string Ad = "ad";
        public const string Currency = "currency";
        public const string Inventory = "inventory";

        /// <summary>Game cho không — <b>khác hẳn</b> "game không khai", ca đó thì VẮNG.</summary>
        public const string Free = "free";

        private static readonly HashSet<string> kAll = new() { Ad, Currency, Inventory, Free };

        public static bool IsKnown(string payMethod)
        {
            return !string.IsNullOrEmpty(payMethod) && kAll.Contains(payMethod);
        }
    }
}
