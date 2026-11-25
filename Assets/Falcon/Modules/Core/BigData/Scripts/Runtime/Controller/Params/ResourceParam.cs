/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-01-08
 */
using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    [Serializable]
    public class ResourceParam : ALiveOpsParam
    {
        /// <summary>
        ///     Định danh MỘT giao dịch tài nguyên (§D10): "mua 3 booster bằng 500 gold" là hai
        ///     event rời (sink gold + source booster) — cùng exchangeId thì server mới ghép lại
        ///     được để tính giá thật per item, và bundle đa vế (gems → gold + booster + heart)
        ///     mới không vỡ. Dùng <c>FalconBigDataController.Resource.OnExchange</c> để SDK tự sinh + đóng
        ///     dấu; tự set tay cũng được (vd id giao dịch của game server) miễn mọi vế cùng giá trị.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string exchangeId;

        public FlowType flowType;
        [NotNull] public string itemType= UNKNOWN;
        [NotNull] public string currency= UNKNOWN;
        [NotNull] public string itemId= UNKNOWN;
        
        /// <summary>
        ///     VỊ TRÍ phát sinh — màn/panel nào (<c>shop_screen</c>, <c>level_end_popup</c>…).
        ///     Vocab riêng của từng game, SDK không khai hằng số.
        /// </summary>
        [NotNull] public string resourceWhere = UNKNOWN;

        /// <summary>
        ///     NGUYÊN NHÂN phát sinh — vì sao tài nguyên này chảy (<see cref="FResourceWhen"/>).
        ///     Cùng nghĩa "ngữ cảnh kích hoạt" với <see cref="AdViewParam.adWhen"/> /
        ///     <see cref="InAppParam.iapWhen"/>, không phải mốc đồng hồ.
        ///     <br/>⚠ Khác hẳn <c>playTurnId</c> mà SDK đóng tự động: id đó nói "xảy ra TRONG lúc
        ///     đang chơi màn", còn field này nói "xảy ra VÌ". Mua gói trong shop giữa ván vẫn mang
        ///     playTurnId nhưng nguyên nhân là <see cref="FResourceWhen.Shop"/>.
        /// </summary>
        [NotNull] public string resourceWhen = UNKNOWN;
        public long amount;

        [FKey(RemoveIfNull = true)] public int? currentLevel;
        [FKey(RemoveIfNull = true)] public long? valueBefore;
        [FKey(RemoveIfNull = true)] public long? valueAfter;

        /// <summary>
        ///     Nối vế thưởng về LẦN XEM AD đã đẻ ra nó (rewarded ad trả coin...) — loader ký
        ///     18/08. Đóng TẠI CHỖ PHÁT THƯỞNG: lấy <c>FalconBigDataController.Ad.CurrentViewId(type)</c>
        ///     ngay lúc grant reward. SDK CẤM auto-stamp (điều khoản ký): banner luôn "đang mở"
        ///     nên tự đóng là gán nhầm nguyên nhân gần như 100% — ĐỒNG THỜI ≠ NGUYÊN NHÂN.
        ///     Server JOIN sang <c>f_sdk_ads_data</c>.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string adViewId;

        /// <summary>
        ///     Nối vế thưởng về GIAO DỊCH IAP đã đẻ ra nó (mở gói gems...) — cùng phán quyết với
        ///     <see cref="adViewId"/>: đóng tại chỗ phát thưởng (transactionId từ purchase),
        ///     không auto-stamp. Server JOIN sang <c>f_sdk_in_app_data</c>.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string transactionId;
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> detail;

        /// <summary>
        ///     HẠNG của <see cref="currency"/> — tập đóng 9 giá trị <see cref="FCurrencyClass"/>,
        ///     loader ký cột <c>currency_class</c> 06/11.
        ///     <br/><b>Game gần như không phải điền field này.</b> Khai một lần ở chỗ định nghĩa ví
        ///     (<c>Resource.DeclareCurrency</c>) rồi SDK tra sổ và tự đóng lên mọi dòng — trăm chỗ gọi
        ///     mà bắt điền tay là trăm cơ hội gõ sai.
        ///     <br/>⚠ <b>Điền tay thì tay THẮNG sổ</b> (§2.2 ticket kho). Dùng khi một tên tiền thật
        ///     sự đổi hạng theo ngữ cảnh — ca hiếm; còn lại thì khai sổ, đừng rải hạng ra call site.
        ///     <br/>⚠ <b>Chưa khai thì VẮNG, không đoán, không mặc định</b>. Vắng thì kho tra theo tên
        ///     như cũ; còn một hạng SAI thì kho TIN, và nó đọc sai cả net-flow kinh tế. Giá trị ngoài
        ///     tập đóng cũng bị bỏ — kể cả <c>"unknown"</c>, vì chuỗi đó sẽ được đếm vào mẫu số
        ///     "đã có hạng" và làm độ phủ trông như đã cải thiện.
        ///     <br/>⚠ Nó <b>không thay</b> <see cref="itemType"/>: <c>itemType</c> là tập MỞ do game
        ///     đặt, hạng là tập ĐÓNG dùng chung toàn fleet.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string currencyClass;

        public override void CorrectValues()
        {
            base.CorrectValues();
            itemType = CheckNonBlank(itemType, nameof(itemType));
            currency = CheckNonBlank(currency, nameof(currency));
            itemId = CheckNonBlank(itemId, nameof(itemId));
            
            resourceWhere = CheckNonBlank(resourceWhere, nameof(resourceWhere));
            resourceWhen = CheckNonBlank(resourceWhen, nameof(resourceWhen));
            
            currentLevel = CheckNumberNonNegative(currentLevel, nameof(currentLevel));
            amount = CheckNumberNonNegative(amount, nameof(amount));

            StampCurrencyClass();
            DropBalanceOfUnlimited();
        }

        /// <summary>
        ///     Hạng <see cref="FCurrencyClass.Unlimited"/> là quyền dùng CÓ THỜI HẠN, không có số dư — bỏ
        ///     <see cref="valueBefore"/> / <see cref="valueAfter"/> khỏi payload (ký với loader 12/11,
        ///     <c>Request-Loader-AbsentNotZero.md</c> mục 3). Thực tế đã có game nhét <c>DateTime.Ticks</c> của
        ///     mốc hết hạn vào hai ô này — một số khổng lồ trông như số dư. Mốc hết hạn có chỗ riêng
        ///     (nhãn người chơi <c>lives_unlimited_end_ts</c>); <see cref="amount"/> giữ nguyên = số giây cộng/trừ.
        ///     <br/>⚠ Đi theo HẠNG của dòng: tên tiền chưa khai hạng ở client thì lọt — game phải khai hạng.
        /// </summary>
        private void DropBalanceOfUnlimited()
        {
            if (currencyClass != FCurrencyClass.Unlimited) return;
            if (valueBefore == null && valueAfter == null) return;

            AnalyticLogger.Instance.Warning(
                $"ResourceParam '{currency}' hạng unlimited mang valueBefore/valueAfter ({valueBefore}/{valueAfter}) — " +
                "BỎ cả hai: quyền có thời hạn không có số dư. amount = số giây cộng/trừ; mốc hết hạn gửi " +
                "qua nhãn người chơi lives_unlimited_end_ts.");
            valueBefore = null;
            valueAfter = null;
        }

        /// <summary>
        ///     Đóng hạng tiền lên dòng. Đặt ở <c>CorrectValues</c> vì đó là <b>cửa duy nhất mọi
        ///     đường tạo dòng tài nguyên đều đi qua</b> — cả ba ctor của <c>FResourceLog</c>, đường
        ///     <c>ResourceExchangeService</c>, và cả <see cref="OldCodeSupportResourceParam"/> của code
        ///     cũ. Đóng ở service thì đường ctor legacy lọt, mà đó đúng là đường phần lớn dữ liệu đang
        ///     đi.
        /// </summary>
        private void StampCurrencyClass()
        {
            // Tay thắng sổ — nhưng vẫn phải qua tập đóng: giá trị lạ thì BỎ, không giữ nguyên.
            if (!string.IsNullOrEmpty(currencyClass))
            {
                var canonical = FCurrencyClass.Canonical(currencyClass);
                if (canonical == null)
                    AnalyticLogger.Instance.Error(
                        $"ResourceParam.currencyClass = '{currencyClass}' không thuộc tập đóng 9 giá " +
                        "trị của FCurrencyClass — BỎ key. Vắng thì kho tra theo tên; một chuỗi lạ " +
                        "(kể cả 'unknown') thì bị đếm vào mẫu số 'đã có hạng' và làm độ phủ trông " +
                        "như đã cải thiện.");

                currencyClass = canonical;
                return;
            }

            // CheckNonBlank đã biến tên rỗng thành "Unknown" trước khi tới đây — đừng nhắc game đi
            // khai hạng cho một cái tên mà chính SDK vừa bịa ra.
            if (currency == UNKNOWN) return;

            currencyClass = CurrencyClassRegistry.ClassForLog(currency);
            FBoosterRegistry.WarnIfClassDisagrees(currency, currencyClass);

            // Nửa "dòng tài nguyên" của hợp đồng §2(a): món đang chảy LÀ một booster thì tên của nó
            // phải nằm trong sổ, không thì nó không nối được với boosters_used của dòng level.
            if (currencyClass == FCurrencyClass.Booster || resourceWhen == FResourceWhen.BoosterUse)
                FBoosterRegistry.WarnIfUnknown(currency, "dòng tài nguyên");
        }
    }

    [Serializable]
    public class OldCodeSupportResourceParam : ResourceParam
    {
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> extraMeta = null;

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this);
        }
    }
}