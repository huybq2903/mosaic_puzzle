using System;
using GoogleMobileAds.Api;
using UnityEngine;

namespace Falcon.Admob
{
    /// <summary>
    /// Gom việc bắn doanh thu AdMob về một chỗ. Mỗi ad object của AdMob có event OnAdPaid RIÊNG (khác
    /// MAX chỉ có 1 event global) nên mọi tầng trong module đều gọi vào đây thay vì tự log.
    ///
    /// Module KHÔNG biết Firebase/AppsFlyer/analytics của project: nó chỉ dựng
    /// <see cref="AdmobImpressionInfo"/> rồi đẩy qua <see cref="AdmobMediationHooks.onImpression"/>.
    /// </summary>
    public static class AdmobRevenueReporter
    {
        /// <summary>Nhãn mặc định khi không xác định được network thắng thầu.</summary>
        public const string AdMobSourceName = "AdMob";

        private const double _MICROS_PER_UNIT = 1_000_000.0;
        private const string _USD = "USD";

        /// <summary>Bắn doanh thu cho 1 impression AdMob. Gọi từ callback OnAdPaid của từng ad object.</summary>
        /// <param name="format">Format của ad.</param>
        /// <param name="adUnitId">Ad unit ID đã load ad này.</param>
        /// <param name="adValue">AdValue từ OnAdPaid — Value tính bằng MICROS.</param>
        /// <param name="responseInfo">ad.GetResponseInfo() để lấy tên network thắng thầu.</param>
        /// <param name="where">Placement game truyền vào lúc show.</param>
        /// <param name="floorEcpmUsd">Giá sàn eCPM đã xin cho ad này (0 = không set sàn).</param>
        /// <param name="estimatedEcpmUsd">eCPM ước tính lúc load của ad này (null = không có giá).</param>
        public static void Report(AdmobAdFormat format, string adUnitId, AdValue adValue,
            ResponseInfo responseInfo, string where, double? floorEcpmUsd = null, double? estimatedEcpmUsd = null)
        {
            try
            {
                if (adValue == null) return;

                var info = new AdmobImpressionInfo
                {
                    format = format,
                    adUnitId = adUnitId ?? string.Empty,
                    network = ResolveAdSourceName(responseInfo),
                    currency = string.IsNullOrEmpty(adValue.CurrencyCode) ? _USD : adValue.CurrencyCode,

                    // AdValue.Value là micro-units (1_000_000 micros = 1 đơn vị tiền tệ).
                    revenue = adValue.Value / _MICROS_PER_UNIT,
                    precision = adValue.Precision.ToString(),
                    where = string.IsNullOrEmpty(where) ? format.ToString() : where,
                    floorEcpmUsd = floorEcpmUsd,
                    estimatedEcpmUsd = estimatedEcpmUsd
                };

                AdmobMediationHooks.Impression(info);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        /// <summary>
        /// Tên network thắng thầu. Ad được fill trực tiếp bởi AdMob (không qua adapter) thì
        /// GetLoadedAdapterResponseInfo() có thể null -> fallback "AdMob".
        /// </summary>
        public static string ResolveAdSourceName(ResponseInfo responseInfo)
        {
            try
            {
                var adapter = responseInfo?.GetLoadedAdapterResponseInfo();
                var name = adapter?.AdSourceName;
                return string.IsNullOrEmpty(name) ? AdMobSourceName : name;
            }
            catch (Exception)
            {
                return AdMobSourceName;
            }
        }
    }
}
