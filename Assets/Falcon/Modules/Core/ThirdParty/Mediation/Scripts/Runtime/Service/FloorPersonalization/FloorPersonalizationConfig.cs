/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-18
 */

using System;
using System.Collections.Generic;
using Falcon.Modules.Core.RemoteConfig;
using Falcon.Modules.Core.RemoteConfigCms;
using Newtonsoft.Json;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Cấu hình Interstitial eCPM Floor Personalization (Databuckets Rule) + Databuckets Analytics — giá trị
    /// JSON của key remote config <see cref="FloorPersonalizationConfigSource.KEY"/>. Tên field = tên key JSON.
    /// </summary>
    [Serializable]
    public class FloorPersonalizationConfig
    {
        public bool enable = true;

        //Rule Evaluation API. Spec khuyên production đi qua proxy của game backend để không lộ key trong
        //build — đổi evalUrl sang proxy và để trống apiKey (header x-api-key chỉ gửi khi có key).
        public string evalUrl = "https://hub.databucket.io/eval";

        /// <summary>Dùng chung cho Rule (<c>x-api-key</c>) và Databuckets Analytics <c>/push</c>.</summary>
        public string apiKey;

        public string firstAdRule = "get_first_ad_ecpm_floor";
        public string subsequentAdRule = "get_sub_sequence_ad_ecpm_floor";

        /// <summary>
        /// Ngân sách gọi Rule (giây, nhận số lẻ: 0.8 = 800ms). Hết hạn mà chưa có response thì Abort, load
        /// mặc định, dòng load mặc định mang <c>ecpmFloorType = rule_timeout</c>.
        /// </summary>
        public float requestTimeoutSeconds = 5f;

        /// <summary>MAX interstitial ad unit dành riêng cho workflow. Mỗi unit giữ tối đa 1 ready candidate.</summary>
        public List<string> interstitialIdsAndroid = new();

        public List<string> interstitialIdsIOS = new();

        /// <summary>Nghỉ giữa 2 lần load tại cùng một floor (retry).</summary>
        public float retryDelaySeconds = 1f;

        /// <summary>Workflow kết thúc mà pool rỗng: lần show hụt kế tiếp chỉ chạy lại opportunity sau ngần này giây.</summary>
        public float restartCooldownSeconds = 30f;

        /// <summary>
        /// URL HTTPS <c>/push</c> của Databuckets Analytics. Dataset do server chọn theo API key; package id
        /// SDK tự gửi (user property <c>app_id</c>). Trống = không bật analytics.
        /// </summary>
        public string analyticsEndpoint = "https://orion.databuckets.com/push";

        [JsonIgnore]
        public List<string> InterstitialIds
        {
            get
            {
#if UNITY_IOS
                return interstitialIdsIOS;
#else
                return interstitialIdsAndroid;
#endif
            }
        }
    }

    /// <summary>
    /// Nguồn của <see cref="FloorPersonalizationConfig"/>: CMS trước, CMS không khai key thì data4game, cả hai
    /// không có thì KHÔNG bật (phiên rơi về MultiCall / unit đơn). Cùng luật "CMS khai key là thắng, kể cả giá
    /// trị rỗng" với FSN / AdMob meta (<see cref="AdsConfigGate.IsCmsFirstReady"/>).
    /// </summary>
    public static class FloorPersonalizationConfigSource
    {
        /// <summary>Key remote config (CMS và data4game), giá trị là JSON của <see cref="FloorPersonalizationConfig"/>.</summary>
        public const string KEY = "moMulFloorPersonalizationConfig";

        /// <summary>Đã chốt được nguồn chưa: CMS về và (CMS có key, hoặc data4game cũng đã về).</summary>
        public static bool IsReady => AdsConfigGate.IsCmsFirstReady(AdsConfigGate.CmsHasKey(KEY));

        /// <summary>
        /// Đọc config. Trả null khi không nguồn nào khai key, khi giá trị rỗng (cố ý tắt) hoặc JSON hỏng.
        /// </summary>
        /// <param name="source">"cms" / "data4game" / null — để log.</param>
        /// <param name="error">Lý do không dùng được (để log); null khi đọc được.</param>
        public static FloorPersonalizationConfig Resolve(out string source, out string error)
        {
            object raw;
            if (AdsConfigGate.CmsHasKey(KEY))
            {
                source = "cms";
                raw = RemoteConfigCms.FConfig.Configs[KEY];
            }
            else if (TryGetGameConfig(out raw))
            {
                source = "data4game";
            }
            else
            {
                source = null;
                error = $"cả CMS lẫn data4game đều không khai '{KEY}'";
                return null;
            }

            var json = raw as string ?? (raw == null ? null : JsonConvert.SerializeObject(raw));
            if (string.IsNullOrWhiteSpace(json))
            {
                error = $"'{KEY}' trên {source} để trống";
                return null;
            }

            try
            {
                var config = JsonConvert.DeserializeObject<FloorPersonalizationConfig>(json);
                error = config == null ? $"'{KEY}' trên {source} không phải JSON object" : null;
                return config;
            }
            catch (Exception e)
            {
                error = $"'{KEY}' trên {source} parse lỗi: {e.Message}";
                return null;
            }
        }

        private static bool TryGetGameConfig(out object raw)
        {
            raw = null;
            try
            {
                var configs = RemoteConfig.FConfig.Configs;
                return configs != null && configs.TryGetValue(KEY, out raw);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // Khai key cho hai module remote config (giá trị mặc định = chưa cấu hình). Luồng đọc dùng giá trị THÔ ở
    // FloorPersonalizationConfigSource để phân biệt "không khai" với "khai rỗng".
    public class FloorPersonalizationRemoteConfig : IFalconConfig
    {
        public string moMulFloorPersonalizationConfig;
    }

    public class FloorPersonalizationRemoteConfigCms : IFalconConfigCms
    {
        public string moMulFloorPersonalizationConfig;
    }
}
