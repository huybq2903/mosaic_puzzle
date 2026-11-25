/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Từ vựng BƯỚC của hợp đồng kho <c>feature-contract.md</c> §4.1 — ở đây chỉ là hằng số, và
    ///     đó là một chỗ CÓ THỂ TRÔI nên có test đối chiếu danh sách.
    ///     <br/>Hợp đồng chia <b>BA BẬC</b>, và bậc nằm ngay ở chỗ gọi:
    ///     <br/>1. <b>Bước chung</b> — thẳng trên lớp này (<c>FFeatureStep.Open</c>): tính năng nào
    ///     cũng có.
    ///     <br/>2. <b>Bước nhóm</b> — trong <see cref="Group"/> (<c>FFeatureStep.Group.HelpGive</c>):
    ///     chỉ tính năng NHIỀU NGƯỜI mới có.
    ///     <br/>3. <b>Bước riêng của từng tính năng</b> — <b>không có hằng số</b>, game truyền chuỗi
    ///     tự do (guild: <c>create</c>, <c>kick_member</c>…). SDK không kiểm, nên tính năng mới
    ///     không phải chờ bản SDK. Đừng đặt bước riêng cho việc đã có bước chung/bước nhóm.
    ///     <br/>SDK khai hằng số cho hai bậc đầu vì chúng dùng chung và SDK cần biết chúng để tự
    ///     điền <c>featureEffect</c> (<see cref="FFeatureEffect.DefaultFor"/>).
    ///     <br/>⚠ Vì sao <c>const string</c> mà không phải <c>enum</c>: enum lên dây bằng <b>tên
    ///     thành viên C#</b> — bằng chứng là luật nạp của kho so <c>flow_type = 'Source'</c>, đúng
    ///     tên <c>FlowType.Source</c> — nên <c>SetVariant</c> sẽ lên dây thay cho <c>set_variant</c>
    ///     mà hợp đồng đòi. Thêm nữa tập bước là tập MỞ (bậc 3), enum thì đóng.
    /// </summary>
    public static class FFeatureStep
    {
        #region Bước chung — tính năng nào cũng có

        /// <summary>Đủ điều kiện dùng tính năng — gửi MỘT LẦN lúc mở khoá.</summary>
        public const string Unlock = "unlock";

        /// <summary>
        ///     Báo TRẠNG THÁI hiện tại — gửi lúc login, và cũng là bản gửi bù cho người đã mở khoá /
        ///     đã vào nhóm TRƯỚC ngày bật đo. Dùng <c>Feature.State</c> chứ không gọi tay.
        /// </summary>
        public const string State = "state";

        /// <summary>Mở màn hình tính năng — lối vào ở <c>featureWhere</c>.</summary>
        public const string Open = "open";

        /// <summary>Thao tác chung chưa có bước riêng.</summary>
        public const string Interact = "interact";

        /// <summary>Nhận thưởng của tính năng — dòng tài nguyên nhận về mang cùng <c>feature*</c>.</summary>
        public const string Claim = "claim";

        #endregion

        /// <summary>
        ///     Bước NHÓM — dùng chung cho mọi tính năng <b>NHIỀU NGƯỜI</b>: bang hội, bạn bè, co-op,
        ///     phòng chơi chung. Tách ra lớp lồng để chỗ gọi tự nói bậc:
        ///     <c>FFeatureStep.Group.HelpGive</c> đọc ra "bước của tính năng nhiều người", không đọc
        ///     ra "bước tính năng nào cũng có".
        ///     <br/>⚠ Mấy bước này <b>không phải của riêng bang hội</b> dù nghe như vậy: gửi tim cho
        ///     bạn bè cũng là <see cref="HelpGive"/>, mời vào phòng co-op cũng là
        ///     <see cref="AcceptRequest"/>. Đó là cả mục đích của việc để chúng ở từ vựng chung —
        ///     hai tính năng tự đặt <c>send_lives</c> và <c>gift_card</c> là chia đôi số liệu của
        ///     cùng một hành vi, và cross-feature thành mù.
        /// </summary>
        public static class Group
        {
            /// <summary>Vào thực thể (TỰ vào). Đơn được duyệt là bản SERVER gửi hộ, không đi đường này.</summary>
            public const string Join = "join";

            /// <summary>Duyệt đơn xin vào / lời mời của người khác — <c>target_account_id</c> ở extras.</summary>
            public const string AcceptRequest = "accept_request";

            /// <summary>TỰ rời thực thể. Bị kick là bản server gửi hộ.</summary>
            public const string Leave = "leave";

            /// <summary>Xin người khác giúp (tim, thẻ…) — <c>request_id</c>, <c>help_item</c> ở extras.</summary>
            public const string HelpRequest = "help_request";

            /// <summary>Giúp/tặng người khác — <c>help_item</c>, <c>request_id</c>, <c>target_account_id</c>.</summary>
            public const string HelpGive = "help_give";

            /// <summary>Gửi một tin nhắn (không tính tin hệ thống).</summary>
            public const string Chat = "chat";

            /// <summary>
            ///     <c>featureVariant</c> của người chơi đổi — dùng CẢ cho tính năng một người (quà
            ///     ngày free→vip, đổi nhánh nâng cấp), nên nó nằm đây chỉ vì hợp đồng kho xếp vào
            ///     bước nhóm. Người KHÁC đổi cho mình thì server gửi hộ.
            /// </summary>
            public const string VariantChanged = "variant_changed";
        }
    }
}
