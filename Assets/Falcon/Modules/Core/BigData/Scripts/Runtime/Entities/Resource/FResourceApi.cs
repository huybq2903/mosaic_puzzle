/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-07
 */

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Giao dịch TÀI NGUYÊN nhiều vế — <c>FalconBigDataController.Resource</c> (§D10).
    /// Không phải entity: giao dịch là một khoảnh khắc, không có vòng đời để giữ state.
    /// </summary>
    public class FResourceApi
    {
        private readonly ResourceExchangeService _resourceExchangeService;

        internal FResourceApi(ResourceExchangeService resourceExchangeService)
        {
            _resourceExchangeService = resourceExchangeService;
        }

        /// <summary>
        ///     Khai HẠNG của một tên tiền — một lần cho mỗi tên, ở chỗ định nghĩa ví / định nghĩa
        ///     resource, trước lần log đầu tiên (thường lúc khởi động). SDK tra sổ này rồi tự đóng
        ///     <c>currencyClass</c> lên mọi dòng tài nguyên.
        ///     <code>
        ///     Resource.DeclareCurrency("gold",  FCurrencyClass.Soft);
        ///     Resource.DeclareCurrency("gem",   FCurrencyClass.Hard);
        ///     Resource.DeclareCurrency("heart", FCurrencyClass.Live);
        ///     </code>
        ///     <br/>Người duy nhất biết chắc <c>screw</c> hay <c>energy</c> là gì là người định nghĩa
        ///     nó trong game. Kho đoán theo tên thì luôn đi sau game một nhịp.
        ///     <br/>⚠ <b>Lần khai đầu THẮNG</b> + cảnh báo nếu khai lại khác hạng — xem
        ///     <see cref="CurrencyClassRegistry"/>. <b>Không chắc thì đừng khai</b>: vắng hạng thì kho
        ///     tra theo tên, còn hạng sai thì kho TIN.
        /// </summary>
        public void DeclareCurrency(string currency, string currencyClass)
        {
            CurrencyClassRegistry.Declare(currency, currencyClass);
        }

        /// <summary>
        ///     Khai một MÃ BOOSTER — để mã trên dòng level (<c>boosters_used</c>) và mã trên dòng tài
        ///     nguyên (<c>currency</c>) là **cùng một** chuỗi. Mã lạ: cảnh báo log dev, <b>vẫn gửi</b>.
        ///     <br/>Phía đọc số đo được rằng lệch tên giữa hai stream làm đóng ba câu hỏi: *booster đã
        ///     dùng là mua hay được tặng*, *mua rồi có dùng không*, *số dư lúc dùng*.
        ///     <br/>⚠ Khai booster <b>không</b> tự khai hạng <c>booster</c> — xem
        ///     <see cref="FBoosterRegistry"/> (một nguồn khai duy nhất).
        /// </summary>
        public void RegisterBooster(string boosterId)
        {
            FBoosterRegistry.Register(boosterId);
        }

        /// <summary>
        /// Một giao dịch tài nguyên có nhiều vế (đổi 500 gold lấy 3 booster; mở bundle gems →
        /// gold + booster + heart): gọi MỘT lần cho cả giao dịch, SDK sinh <c>exchangeId</c>
        /// chung và bắn đúng những log resource như cũ kèm id đó.
        /// <br/>Không có id chung thì các vế nằm rời nhau: không tính được giá thật per item và
        /// bundle đa vế thì vỡ hẳn (§D10). <see cref="ResourceParam.flowType"/> do SDK set theo vế.
        /// <br/>Giao dịch chỉ có một vế (thưởng cho không) thì cứ dùng <c>FResourceLog</c> như cũ.
        /// </summary>
        /// <param name="sinks">Các vế TRẢ ĐI.</param>
        /// <param name="sources">Các vế NHẬN VỀ.</param>
        public void OnExchange(IEnumerable<ResourceParam> sinks, IEnumerable<ResourceParam> sources)
        {
            _resourceExchangeService.LogExchange(sinks, sources);
        }

        /// <inheritdoc cref="OnExchange(IEnumerable{ResourceParam},IEnumerable{ResourceParam})"/>
        public void OnExchange(ResourceParam sink, ResourceParam source)
        {
            _resourceExchangeService.LogExchange(sink, source);
        }

        /// <summary>
        /// NHẬN tài nguyên một chiều — thưởng hoàn thành level, quà đăng nhập, đền bù... Không có
        /// vế trả đi nên không phải trao đổi: không sinh <c>exchangeId</c>, chỉ một log Source.
        /// <br/><c>flowType</c> SDK tự set — game không cần điền. Context tuỳ ý đi qua
        /// <see cref="ResourceParam.detail"/>.
        /// </summary>
        public void OnEarned(ResourceParam param)
        {
            _resourceExchangeService.LogSingle(param, FlowType.Source);
        }

        /// <summary>
        /// TIÊU tài nguyên một chiều mà thứ nhận về KHÔNG phải tài nguyên đếm được — coin để
        /// revive, gold để skip timer... Cái nhận về (lượt hồi sinh, thời gian) không có ví để
        /// đếm nên không có vế source: chỉ một log Sink, không sinh <c>exchangeId</c>.
        /// <br/>Còn nếu thứ nhận về LÀ tài nguyên đếm được (gold → booster) thì đó là trao đổi —
        /// dùng <see cref="OnExchange(ResourceParam,ResourceParam)"/> để hai vế join được.
        /// </summary>
        public void OnSpent(ResourceParam param)
        {
            _resourceExchangeService.LogSingle(param, FlowType.Sink);
        }
    }
}
