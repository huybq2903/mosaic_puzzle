/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Ảnh chụp lượt mua đang mở — bản SAO để game đọc; sửa vào đây không đụng tới state SDK.
    /// </summary>
    [Serializable]
    public class PurchaseAttemptSnapshot
    {
        public string purchaseAttemptId;
        public string productId;
        public string where;
        public decimal? localizedPrice;
        public string isoCurrencyCode;
    }

    /// <summary>
    /// Vòng đời một LƯỢT MUA (xem EntityLifecycle-Design.md §4e): mở lúc gọi billing flow,
    /// đóng khi billing callback trả thành công hoặc thất bại. Id sinh lúc mở và đóng dấu lên
    /// CẢ hai nhánh kết thúc để phễu checkout khớp chính xác + đo được time-to-complete (§D4).
    /// <br/>KHÔNG persist qua process (§A3.4): app bị kill giữa dialog thì lượt mua đó là
    /// "start cụt đuôi" — chính là số đo abandonment, chấp nhận theo bẫy (2) của §D4.
    /// <br/>Pure class — không IO/DI/time. Thread-safety do <see cref="PurchaseAttemptService"/>
    /// lo (bên ghi là UI game, bên đọc là callback billing của Unity IAP).
    /// <br/>Dòng FAIL thừa hưởng TOÀN BỘ ngữ cảnh của lượt đang mở — <c>extraMeta</c> cộng cả bảy
    /// field ngữ cảnh (bốn <c>liveOps*</c>, ba <c>feature*</c>) — vì <see cref="FIapApi.OnFailed"/>
    /// cố ý không nhận param, nên nếu state không nhớ thì không ai điền được cho nhánh đó. Bảy field
    /// đó chép bằng <see cref="ALiveOpsParam.CopyContextFrom"/>, KHÔNG liệt kê tay ở đây.
    /// </summary>
    public class PurchaseAttemptState
    {
        private string _attemptId;
        private string _productId;
        private string _where;
        private decimal? _localizedPrice;
        private string _isoCurrencyCode;
        private Dictionary<string, object> _extraMeta;

        // CẢ HAI trục ngữ cảnh của lượt đang mở, giữ thành MỘT ảnh chụp. Giữ ở đây vì dòng FAIL
        // do SDK dựng lại (FIapApi.OnFailed cố ý không nhận param) nên không ai khác điền được
        // cho nó: thiếu thì phễu mua TRONG event / TRONG tính năng lệch đúng nhánh thất bại —
        // `started` gắn ngữ cảnh, `failed` rơi ra ngoài — và sai theo chiều "trông như không ai
        // thất bại" (rà 19/10, Endless Treasure R2). Trục tính năng còn dễ dính hơn trục event
        // vì tính năng LUÔN BẬT, không đợi đợt nào.
        // 🔴 Là MỘT ref chứ không phải 7 field rời (gộp 05/11): liệt kê tay bảy field ở đây là
        // chỗ mục thứ hai mỗi lần lớp nền mọc field — nay chép bằng ALiveOpsParam.CopyContextFrom,
        // chỗ duy nhất giữ danh sách đó.
        // ⚠ Ref này là của RIÊNG state, không giữ param của caller: game sửa param sau khi gọi
        // OnStarted thì ảnh chụp không được đổi theo.
        // Dòng `purchased` KHÔNG đi đường này (TryAttributeSuccess chỉ trả id): param của nó do
        // game truyền nên game tự đóng dấu — y khuôn trục live-op, không chở hộ.
        private LiveOpsRef _context;

        /// <summary>Id của lượt mua ĐANG mở (null nếu không có).</summary>
        public string OpenAttemptId => _attemptId;

        /// <summary>Ảnh chụp lượt mua đang mở (null nếu không có) — bản sao.</summary>
        public PurchaseAttemptSnapshot TakeSnapshot()
        {
            if (_attemptId == null) return null;
            return new PurchaseAttemptSnapshot
            {
                purchaseAttemptId = _attemptId,
                productId = _productId,
                where = _where,
                localizedPrice = _localizedPrice,
                isoCurrencyCode = _isoCurrencyCode
            };
        }

        /// <summary>Mở một lượt mua: sinh id của lượt và giữ state.</summary>
        /// <returns>
        /// (id vừa sinh — caller gắn lên log; replaced = true nếu có lượt cũ chưa kết thúc bị đè,
        /// caller nên cảnh báo lúc dev).
        /// </returns>
        public (string attemptId, bool replaced) Open(IapPurchaseAttemptParam param)
        {
            var replaced = _attemptId != null;

            _attemptId = Guid.NewGuid().ToString();
            _productId = param.productId;
            _where = param.where;
            _localizedPrice = param.localizedPrice;
            _isoCurrencyCode = param.isoCurrencyCode;
            _extraMeta = param.extraMeta;
            _context = new LiveOpsRef();
            _context.CopyContextFrom(param);

            return (_attemptId, replaced);
        }

        /// <summary>
        /// Lượt mua thất bại: dựng param cho log fail từ state đang mở rồi đóng lượt, kèm id để
        /// caller gắn lên log.
        /// <br/>Trả (null, null) nếu không có lượt nào đang mở — không bịa ra một lượt mua không tồn tại
        /// (ca thường gặp: app bị kill giữa dialog rồi callback mới về ở phiên sau, state đã chết
        /// cùng process; server đã thấy "start cụt đuôi" nên không mất thông tin).
        /// </summary>
        public (IapPurchaseFailParam param, string attemptId) TakeFail(IapPurchaseFailReason reason)
        {
            if (_attemptId == null) return (null, null);

            var param = new IapPurchaseFailParam
            {
                productId = _productId,
                where = _where,
                localizedPrice = _localizedPrice,
                isoCurrencyCode = _isoCurrencyCode,
                extraMeta = _extraMeta,
                failReason = reason
            };
            param.CopyContextFrom(_context);

            var attemptId = _attemptId;
            Clear();
            return (param, attemptId);
        }

        /// <summary>
        /// Giao dịch thành công vừa xảy ra: nếu khớp sản phẩm của lượt đang mở thì trả id để đóng
        /// dấu lên log mua và đóng lượt. Khớp theo productId để lượt mua sản phẩm A không ăn nhầm
        /// giao dịch của sản phẩm B.
        /// </summary>
        /// <returns>purchaseAttemptId nếu khớp, null nếu không.</returns>
        public string TryAttributeSuccess(string productId)
        {
            if (_attemptId == null || _productId != productId) return null;

            var attemptId = _attemptId;
            Clear();
            return attemptId;
        }

        private void Clear()
        {
            _attemptId = null;
            _productId = null;
            _where = null;
            _localizedPrice = null;
            _isoCurrencyCode = null;
            _extraMeta = null;
            _context = null;
        }
    }
}
