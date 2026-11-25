/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-13
 */

using System;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Ad ĐANG NẰM SẴN trong kho mediation — "nếu chiếu ngay bây giờ thì chiếu ad này". Chỉ mediation
    /// biết chắc (MultiCall chọn ad đắt nhất giữa nhiều unit + nguồn phụ; ad hết hạn không báo ra
    /// ngoài), nên SDK KHÔNG tự đoán: mediation trả object này, ai gọi
    /// <see cref="FAdApi.OnShowSkipped"/> thì truyền nguyên vào.
    /// <br/>Field nào mediation không biết thì để null — SDK gửi VẮNG, không gửi 0.
    /// </summary>
    [Serializable]
    public class AdReadyInfo
    {
        /// <summary>Cùng giá trị với <c>adMediation</c> của các dòng ad khác. Null ⇒ SDK lấy của view đang mở.</summary>
        [CanBeNull] public string adMediation;

        /// <summary>Network đã trả ad đang chờ (applovin · unity · admob…).</summary>
        [CanBeNull] public string adNetwork;

        /// <summary>Ad unit giữ ad đang chờ. MultiCall: unit SẼ ĐƯỢC CHỌN nếu chiếu ngay lúc đó.</summary>
        [CanBeNull] public string adUnitId;

        /// <summary>
        /// Giá dự kiến LÚC LOAD của ad đang chờ, USD/1000 impression — cùng đơn vị với
        /// <c>f_sdk_ad_load.estimatedEcpm</c> (MAX: <c>AdInfo.Revenue × 1000</c>). Không dương ⇒ vắng.
        /// AdMob trực tiếp không có giá trước khi chiếu ⇒ để null.
        /// </summary>
        public double? estimatedEcpm;
    }
}
