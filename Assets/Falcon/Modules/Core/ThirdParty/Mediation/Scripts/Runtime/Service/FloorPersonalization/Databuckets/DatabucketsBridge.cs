/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-18
 */

using System;
using System.Collections.Generic;
using Databuckets.Analytics;
using Falcon.Helpers.Devkit;
using Falcon.Modules.Core.BigData;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Cầu nối Floor Personalization ↔ Databuckets Analytics SDK (4.0.1, thư mục
    /// <c>Mediation/Scripts/Runtime/DatabucketsAnalytics</c>). Không cần gắn vào scene; chỉ chạy khi phiên chốt
    /// chế độ DATABUCKETS (<see cref="InterstitialFloorPersonalization.Activated"/> — không Lua + có remote config):
    /// <br/>1. <c>DatabucketsTracker.Init(endpoint, apiKey)</c> theo <see cref="FloorPersonalizationConfig"/>.
    /// <br/>2. Rule dùng CÙNG uid / country / device model với analytics (UidProvider…).
    /// <br/>3. Response Rule hợp lệ -> <c>realtime_segments</c> / <c>realtime_variants</c> / <c>device_tier</c>.
    /// <br/>4. <c>ad_request</c> (mỗi lần load), <c>ad_impression</c> (is_show = 1 khi lên hình, 0 khi chiếu
    /// hỏng), interstitial kèm param personalization (<c>ad_opportunity_id</c>, <c>ad_candidate_id</c>…).
    /// <br/>Mediation lõi không biết lớp này — xoá thư mục FloorPersonalization là gỡ sạch.
    /// </summary>
    public static class DatabucketsBridge
    {
        private const string _LOG = "[Databuckets] ";

        public const string EVENT_AD_REQUEST = "ad_request";
        public const string EVENT_AD_IMPRESSION = "ad_impression";

        private const string _PROP_DEVICE_TIER = "device_tier";
        private const string _UNKNOWN_COUNTRY = "__";

        private static bool _initialized;

        /// <summary>SDK đã Init — các hàm Record / Set* chỉ gọi được sau đó.</summary>
        public static bool IsInitialized => _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            _initialized = false;
            //chỉ bật khi phiên chốt chế độ DATABUCKETS (không Lua + có remote config) — xem TryActivate
            InterstitialFloorPersonalization.Activated -= OnActivated;
            InterstitialFloorPersonalization.Activated += OnActivated;
        }

        private static void OnActivated(FloorPersonalizationConfig setting)
        {
            if (_initialized || setting == null) return;
            if (string.IsNullOrWhiteSpace(setting.analyticsEndpoint) || string.IsNullOrWhiteSpace(setting.apiKey))
            {
                Debug.LogWarning(_LOG + "tắt: config thiếu analyticsEndpoint / apiKey");
                return;
            }

            try
            {
                var initial = InitialUserProperties();
                DatabucketsTracker.Init(setting.analyticsEndpoint.Trim(), setting.apiKey.Trim(), initial);
                _initialized = true;
                FloorPersonalizationLog.Info($"{_LOG}Init endpoint={setting.analyticsEndpoint.Trim()} " +
                                             $"apiKey={Mask(setting.apiKey)} initialProps={Describe(initial)}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(_LOG + "Init lỗi: " + e.Message);
                return;
            }

            ObserveReady();

            InterstitialFloorPersonalization.UidProvider = UserPseudoId;
            InterstitialFloorPersonalization.CountryProvider = Country;
            InterstitialFloorPersonalization.DeviceModelProvider = DeviceModel;
            InterstitialFloorPersonalization.RuleEvaluated -= OnRuleEvaluated;
            InterstitialFloorPersonalization.RuleEvaluated += OnRuleEvaluated;

            FBigDataHooks.AdLoadResult -= OnAdLoadResult;
            FBigDataHooks.AdLoadResult += OnAdLoadResult;
            FBigDataHooks.LogEnqueued -= OnLogEnqueued;
            FBigDataHooks.LogEnqueued += OnLogEnqueued;
            FBigDataHooks.AdShowFailed -= OnAdShowFailed;
            FBigDataHooks.AdShowFailed += OnAdShowFailed;
        }

        private static async void ObserveReady()
        {
            try
            {
                await DatabucketsTracker.Ready;
                //uid/country/model ở đây chính là giá trị Rule sẽ nhận qua provider
                FloorPersonalizationLog.Info($"{_LOG}Ready — user_pseudo_id={DatabucketsTracker.GetUserPseudoId()} " +
                                             $"country={DatabucketsTracker.GetCountry()} " +
                                             $"model={DatabucketsTracker.GetDeviceModel()} " +
                                             $"lastError={SafeLastError() ?? "-"}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(_LOG + "khởi tạo lỗi: " + e.Message + " | LastError=" + SafeLastError());
            }
        }

        private static string SafeLastError()
        {
            try
            {
                return DatabucketsTracker.LastError;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>device_tier đã lưu từ phiên trước — có mặt ngay từ first_open / session_start.</summary>
        private static Dictionary<string, object> InitialUserProperties()
        {
            var tier = FloorPersonalizationUserInfo.Snapshot?.deviceTier;
            return string.IsNullOrEmpty(tier) ? null : new Dictionary<string, object> { [_PROP_DEVICE_TIER] = tier };
        }

        #region providers cho Rule

        private static string UserPseudoId() => Blank(DatabucketsTracker.GetUserPseudoId());

        /// <summary><c>"__"</c> = SDK chưa biết country -> null để Rule rơi về country của MAX.</summary>
        private static string Country()
        {
            var country = Blank(DatabucketsTracker.GetCountry());
            return country == _UNKNOWN_COUNTRY ? null : country;
        }

        private static string DeviceModel() => Blank(DatabucketsTracker.GetDeviceModel());

        #endregion

        #region rule -> user properties

        private static void OnRuleEvaluated(FloorRuleResponse response)
        {
            if (!_initialized || response == null) return;
            try
            {
                //cùng nghĩa với FloorPersonalizationUserInfo.Update: response hợp lệ thay toàn bộ segment/variant
                var segment = Blank(response.realtimeSegment);
                if (segment == null) DatabucketsTracker.ResetSegments();
                else DatabucketsTracker.SetSegments(segment);

                var variant = Blank(response.realtimeVariant);
                if (variant == null) DatabucketsTracker.ResetVariants();
                else DatabucketsTracker.SetVariants(variant);

                var tier = Blank(response.config?.deviceTier);
                if (tier != null) DatabucketsTracker.SetUserProperties((_PROP_DEVICE_TIER, tier));

                FloorPersonalizationLog.Info($"{_LOG}user props <- Rule: realtime_segments={segment ?? "null"} " +
                                             $"realtime_variants={variant ?? "null"} device_tier={tier ?? "(giữ cũ)"}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(_LOG + "ghi segment/variant lỗi: " + e.Message);
            }
        }

        #endregion

        #region ad events

        /// <summary>Mỗi lần load của một ad unit (kể cả retry) -> <c>ad_request</c>.</summary>
        private static void OnAdLoadResult(AdLoadResultParam param)
        {
            if (!_initialized || param == null) return;

            var fields = new Dictionary<string, object>
            {
                ["ad_format"] = Format(param.adType),
                ["ad_unit_id"] = param.adUnitId,
                ["ad_mediation"] = param.adMediation,
                ["ad_algorithm"] = param.adAlgorithm,
                ["ad_network"] = param.networkName,
                ["is_success"] = param.success ? 1 : 0,
                ["error_code"] = param.errorMess,
                ["load_latency_ms"] = param.loadingMs,
                ["ecpm_floor"] = param.ecpmFloor,
                ["estimated_ecpm"] = param.estimatedEcpm
            };

            //Gọi ĐỒNG BỘ trong FAdApi.OnLoadResult -> FloorPersonalization còn giữ candidate đang báo
            if (param.adType == AdType.Interstitial)
                Merge(fields, InterstitialFloorPersonalization.Instance.LoadContextParams(param.adUnitId));

            Record(EVENT_AD_REQUEST, fields);
        }

        /// <summary>Impression của ad fullscreen (log <c>f_sdk_ads_data</c> vừa vào hàng đợi) -> <c>ad_impression</c>.</summary>
        private static void OnLogEnqueued(IDataLog log)
        {
            if (!_initialized || log is not FAdLog { param: { } param }) return;
            if (!IsFullscreen(param.type)) return; //banner/native/MREC: không thuộc phạm vi

            var fields = new Dictionary<string, object>
            {
                ["is_show"] = 1,
                ["ad_format"] = Format(param.type),
                ["ad_where"] = param.adWhere,
                ["ad_unit_id"] = param.adUnitId,
                ["ad_mediation"] = param.adMediation,
                ["ad_algorithm"] = param.adAlgorithm,
                ["ad_network"] = param.adNetwork,
                ["ad_revenue"] = param.adRev,
                ["ad_precision"] = param.adPrecision,
                ["ecpm_floor"] = param.ecpmFloor,
                ["estimated_ecpm"] = param.estimatedEcpm
            };

            if (param.type == AdType.Interstitial)
                Merge(fields, InterstitialFloorPersonalization.Instance.ShowContextParams(param.adUnitId, true));

            Record(EVENT_AD_IMPRESSION, fields);
        }

        /// <summary>Ad đã load nhưng chiếu hỏng -> <c>ad_impression</c> với is_show = 0.</summary>
        private static void OnAdShowFailed(AdShowFailedInfo info)
        {
            if (!_initialized || info == null || !IsFullscreen(info.type)) return;

            var fields = new Dictionary<string, object>
            {
                ["is_show"] = 0,
                ["ad_format"] = Format(info.type),
                ["ad_unit_id"] = info.adUnitId,
                ["ad_network"] = info.networkName,
                ["error_code"] = info.errorCode,
                ["ecpm_floor"] = info.ecpmFloor,
                ["estimated_ecpm"] = info.estimatedEcpm
            };

            if (info.type == AdType.Interstitial)
                Merge(fields, InterstitialFloorPersonalization.Instance.ShowContextParams(info.adUnitId, false));

            Record(EVENT_AD_IMPRESSION, fields);
        }

        #endregion

        #region helpers

        private static void Record(string eventName, Dictionary<string, object> fields)
        {
            try
            {
                var values = WithoutNulls(fields);
                DatabucketsTracker.Record(eventName, values);
                FloorPersonalizationLog.Info($"{_LOG}Record {eventName} {Describe(values)}");
            }
            catch (Exception e)
            {
                Debug.LogWarning(_LOG + eventName + " lỗi: " + e.Message);
            }
        }

        private static void Merge(Dictionary<string, object> target, Dictionary<string, object> extra)
        {
            if (extra == null) return;
            foreach (var (key, value) in extra) target[key] = value;
        }

        /// <summary>Field null = vắng mặt (đỡ tốn byte của batch), không gửi JSON null.</summary>
        private static Dictionary<string, object> WithoutNulls(Dictionary<string, object> fields)
        {
            var result = new Dictionary<string, object>(fields.Count);
            foreach (var (key, value) in fields)
                if (value != null && !(value is string s && string.IsNullOrEmpty(s)))
                    result[key] = value;
            return result;
        }

        private static bool IsFullscreen(AdType type) =>
            type is AdType.Interstitial or AdType.Reward or AdType.AppOpen;

        private static string Format(AdType type) => type switch
        {
            AdType.Interstitial => "interstitial",
            AdType.Reward => "rewarded",
            AdType.AppOpen => "app_open",
            AdType.Banner => "banner",
            AdType.CollapsibleBanner => "collapsible_banner",
            AdType.Native => "native",
            AdType.MREC => "mrec",
            _ => type.ToString().ToLowerInvariant()
        };

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>Chỉ hiện 4 ký tự đầu/cuối của key trong log.</summary>
        private static string Mask(string key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key)) return "(trống)";
            return key.Length <= 8 ? "****" : key.Substring(0, 4) + "…" + key.Substring(key.Length - 4);
        }

        /// <summary>{k=v, …} gọn cho log — chỉ dùng trong lời gọi FloorPersonalizationLog (bị bỏ khi tắt define).</summary>
        private static string Describe(IDictionary<string, object> values)
        {
            if (values == null || values.Count == 0) return "{}";
            var sb = new System.Text.StringBuilder("{");
            foreach (var (key, value) in values)
            {
                if (sb.Length > 1) sb.Append(", ");
                sb.Append(key).Append('=').Append(value is IFormattable f
                    ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
                    : value);
            }

            return sb.Append('}').ToString();
        }

        #endregion
    }
}
