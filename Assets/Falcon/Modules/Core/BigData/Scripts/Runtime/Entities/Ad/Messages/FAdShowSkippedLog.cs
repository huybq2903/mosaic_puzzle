/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-12
 */

using System;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// LUẬT CỦA GAME CHẶN MỘT CƠ HỘI CHIẾU AD — ký với loader 12/11
    /// (<c>Request-Loader-AdShowSkipped.md</c> mục 1), sửa 13/11
    /// (<c>Request-Loader-AdShowSkipped-NoAds.md</c>: lý do <c>no_ads</c> + 6 field ad đang chờ).
    /// <br/>Vì sao cần: luật bớt ad theo nhóm user (user mới, vừa mua, campaign IAP, cooldown, trần)
    /// chạy trong code game TRƯỚC khi gọi mediation, nên <c>f_sdk_ad_show_attempt</c> không thấy —
    /// ca "đến điểm chiếu nhưng luật bảo đừng" vô hình trên wire.
    /// <br/>Đơn vị: MỘT dòng = MỘT cơ hội chiếu bị chặn (điểm trong luồng game mà ad có thể hiện:
    /// hết màn, về Home, quay lại app), không phải mỗi lần kiểm tra luật. Ghép với show_attempt:
    /// <c>cơ hội = count(show_attempt) + count(show_skipped)</c> per type / adWhere.
    /// <br/>Vì sao event riêng, không thêm field vào show_attempt: event đó có công thức
    /// <c>view_fill_rate = count(adAvailable) / count(*)</c> — nhét dòng bị chặn vào là đổi nghĩa
    /// <c>count(*)</c>.
    /// <br/>Chỉ format GAME TỰ BẬT (<see cref="AdType.Interstitial"/>, <see cref="AdType.AppOpen"/>):
    /// rewarded do người chơi bấm — "không có ad" của nó đã nằm ở <c>show_attempt.adAvailable</c>.
    /// <br/>Field "ad đang chờ" (<see cref="adViewId"/>, <see cref="adNetwork"/>, <see cref="adUnitId"/>,
    /// <see cref="estimatedEcpm"/>) chỉ có khi <see cref="adAvailable"/> = true. Bị chặn KHÔNG tiêu ad:
    /// nhiều dòng liên tiếp có thể trỏ cùng một <c>adViewId</c> — kho không dedup theo id này.
    /// </summary>
    [Serializable]
    public class FAdShowSkippedLog : AFalconLog, IPerfPackCarrier
    {
        public AdType type;

        /// <summary>Điểm chiếu bị chặn — cùng bộ từ với <c>adWhere</c> của show_attempt để ghép.</summary>
        public string adWhere;

        /// <summary>Ngữ cảnh kích hoạt (bỏ trống nếu không biết).</summary>
        [FKey(RemoveIfNull = true)] public string adWhen;

        /// <summary>Lượt chơi kích hoạt cơ hội chiếu bị chặn — xem <see cref="AdViewParam.triggerPlayTurnId"/>.</summary>
        [FKey(RemoveIfNull = true)] public string triggerPlayTurnId;

        /// <summary>Luật ĐẦU TIÊN chặn, theo thứ tự game xét (<see cref="AdSkipReason.NoAds"/> đứng đầu).</summary>
        public AdSkipReason skipReason;

        /// <summary>
        /// Lúc bị chặn, kho mediation có ad sẵn không — cùng nghĩa với <c>show_attempt.adAvailable</c>.
        /// Vắng = caller không truyền (dòng kiểu 1.7.4), KHÔNG phải false.
        /// </summary>
        [FKey(RemoveIfNull = true)] public bool? adAvailable;

        /// <summary>Lần xem ad đang chờ — SDK tự đọc từ cache, chỉ khi <see cref="adAvailable"/>.</summary>
        [FKey(RemoveIfNull = true)] public string adViewId;

        [FKey(RemoveIfNull = true)] public string adMediation;

        /// <inheritdoc cref="AdReadyInfo.adNetwork"/>
        [FKey(RemoveIfNull = true)] public string adNetwork;

        /// <inheritdoc cref="AdReadyInfo.adUnitId"/>
        [FKey(RemoveIfNull = true)] public string adUnitId;

        /// <inheritdoc cref="AdReadyInfo.estimatedEcpm"/>
        [FKey(RemoveIfNull = true)] public double? estimatedEcpm;

        [Preserve]
        public FAdShowSkippedLog()
        {
        }

        public override string Event => "f_sdk_ad_show_skipped";

        /// <summary>
        /// Điền phần "ad đang chờ" theo luật hợp đồng 13/11. Pure để test được.
        /// <br/>- <paramref name="available"/> != true ⇒ KHÔNG gửi meta ad (không có ad thì không có gì
        /// để mô tả; meta lọt vào đây là của ad cũ / đoán).
        /// <br/>- <c>adMediation</c> không phụ thuộc có ad: lấy của caller, thiếu thì của view đang mở.
        /// <br/>- Chuỗi trắng / eCPM không dương hoặc không hữu hạn ⇒ vắng (vắng ≠ 0).
        /// </summary>
        /// <returns>true nếu caller truyền meta ad trong khi không có ad (caller cảnh báo dev).</returns>
        public bool ApplyReadyAd(bool? available, AdReadyInfo readyAd, string currentViewId, string viewMediation)
        {
            adAvailable = available;
            adMediation = Text(readyAd?.adMediation) ?? Text(viewMediation);
            if (available != true)
                return readyAd != null && (Text(readyAd.adNetwork) != null || Text(readyAd.adUnitId) != null ||
                                           readyAd.estimatedEcpm != null);

            adViewId = Text(currentViewId);
            adNetwork = Text(readyAd?.adNetwork);
            adUnitId = Text(readyAd?.adUnitId);
            var ecpm = readyAd?.estimatedEcpm;
            estimatedEcpm = AdUnitFactsState.Positive(ecpm) && !double.IsInfinity(ecpm.Value) ? ecpm : null;
            return false;
        }

        /// <summary>
        /// Lý do bỏ bản tin, hoặc null nếu hợp lệ. Pure để test được: format không phải game tự bật,
        /// hoặc thiếu điểm chiếu (field bắt buộc theo hợp đồng) ⇒ không gửi.
        /// </summary>
        public static string RejectReason(AdType type, string adWhere)
        {
            if (type != AdType.Interstitial && type != AdType.AppOpen)
                return $"type {type} không thuộc format game tự bật (chỉ Interstitial / AppOpen) — " +
                       "rewarded không có ad thì báo qua OnShowAttempt(adAvailable: false)";
            if (string.IsNullOrWhiteSpace(adWhere))
                return "thiếu adWhere (bắt buộc) — không biết điểm chiếu nào bị chặn thì không ghép được với show_attempt";
            return null;
        }

        private static string Text(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
