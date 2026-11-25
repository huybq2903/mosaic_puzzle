/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-22
 */

using System.Collections;
using Falcon.Helpers.Devkit;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// HÀNG RÀO trước khi init mediation: hai tầng ad đọc HAI nguồn remote config KHÁC NHAU và hai
    /// nguồn đó về độc lập với nhau —
    ///
    ///  - CMS (<c>Falcon.Modules.Core.RemoteConfigCms</c>): <c>MultiCallConfig</c> của MAX —
    ///    <c>moMulProviderIndexMax</c> quyết định provider nào chạy và lọc danh sách bid floor.
    ///  - data4game (<c>Falcon.Modules.Core.RemoteConfig</c>): <c>AdmobMetaAdsConfig</c> của AdMob —
    ///    thang ad unit bước C. CMS khai key <c>moMulAdmobMetaConfig</c> thì CMS thắng
    ///    (<c>AdmobMetaAdsConfigCms</c>) và KHÔNG cần chờ data4game nữa, xem
    ///    <see cref="IsAdmobConfigReady"/>.
    ///
    /// Init sớm hơn một trong hai nguồn là chạy bằng config rỗng: MAX chọn nhầm provider index 0 và
    /// AdMob không có ad unit bước C nào để xin. Cả hai lỗi đó đều âm thầm — ad vẫn chạy, chỉ là chạy
    /// sai cấu hình — nên phải chặn ở đây thay vì để phát hiện qua doanh thu.
    ///
    /// "Xong" tính cả FAIL: fetch hỏng thì config sẽ không bao giờ về, chờ tiếp chỉ tổ chặn ad. Kèm
    /// timeout để trạng thái <see cref="ExecState.NotStarted"/> (chưa ai gọi init) không treo vĩnh viễn.
    /// </summary>
    public static class AdsConfigGate
    {
        /// <summary>Trần thời gian chờ. Quá mốc này thì init bằng config đang có, còn hơn là không có ad.</summary>
        public const float DEFAULT_TIMEOUT_SECONDS = 15f;

        /// <summary>
        /// Trần chờ của các tầng đọc config "CMS trước, data4game sau" (AdMob, FSN) — dài hơn hẳn vì
        /// load ad bằng ID data4game rồi CMS mới về là ID cũ không gỡ được nữa. Chỉ để không treo vĩnh
        /// viễn khi CMS kẹt NotStarted (không ai gọi init).
        /// </summary>
        public const float CMS_FIRST_TIMEOUT_SECONDS = 60f;

        /// <summary>Key remote config của AdMob bước B + C — đọc ở CMS trước, CMS không khai mới tới data4game.</summary>
        public const string ADMOB_META_CONFIG_KEY = "moMulAdmobMetaConfig";

        /// <summary>Remote config data4game (nguồn của AdMob) đã hết đường chờ chưa.</summary>
        public static bool IsGameConfigSettled => IsSettled(RemoteConfig.FConfig.InitState);

        /// <summary>Remote config CMS (nguồn của MAX / MultiCall) đã hết đường chờ chưa.</summary>
        public static bool IsCmsConfigSettled => IsSettled(RemoteConfigCms.FConfig.InitState);

        /// <summary>
        /// CMS có khai <paramref name="key"/> không — xét KEY THÔ server trả về, kể cả giá trị rỗng (rỗng
        /// = cố ý, không phải "chưa có"). Không xét field sau khi bind: field string là null cả khi CMS
        /// không khai lẫn khi CMS khai null, không phân biệt được.
        /// </summary>
        public static bool CmsHasKey(string key)
        {
            var configs = RemoteConfigCms.FConfig.Configs;
            return configs != null && configs.ContainsKey(key);
        }

        /// <summary>CMS có khai key <see cref="ADMOB_META_CONFIG_KEY"/> không.</summary>
        public static bool CmsHasAdmobMetaConfig => CmsHasKey(ADMOB_META_CONFIG_KEY);

        /// <summary>
        /// Một tầng đọc config "CMS trước, data4game sau" đã CHỐT được nguồn chưa:
        ///  - CMS về + CMS khai đủ key tầng đó cần -> chốt CMS ngay, KHÔNG chờ data4game;
        ///  - CMS về + thiếu key -> chờ data4game về rồi lấy phần thiếu từ data4game;
        ///  - CMS chưa về -> chờ, kể cả khi data4game đã về (CMS về sau có key thì thắng).
        /// </summary>
        public static bool IsCmsFirstReady(bool cmsHasAllKeys) =>
            IsCmsConfigSettled && (cmsHasAllKeys || IsGameConfigSettled);

        /// <summary>Config của AdMob đã chốt nguồn chưa — xem <see cref="IsCmsFirstReady"/>.</summary>
        public static bool IsAdmobConfigReady => IsCmsFirstReady(CmsHasAdmobMetaConfig);

        /// <summary>
        /// Mọi nguồn mà hai tầng ad cần đã chốt — init được rồi. MAX cần CMS. AdMob (meta AdMob lẫn Lua) giờ lấy
        /// id từ <c>sc_ad_bid_floor</c> — MediationManager chờ riêng bản tin đó — nên KHÔNG chờ data4game nữa.
        /// </summary>
        public static bool IsReady => IsCmsConfigSettled;

        /// <summary>
        /// Chờ tới khi <see cref="IsReady"/> hoặc hết <paramref name="timeoutSeconds"/>.
        /// Dùng <see cref="Time.realtimeSinceStartup"/> chứ không phải Time.time: luồng init chạy lúc
        /// game có thể đang <c>timeScale = 0</c> (loading screen, popup consent).
        /// </summary>
        public static IEnumerator WaitUntilReady(float timeoutSeconds = DEFAULT_TIMEOUT_SECONDS)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!IsReady && Time.realtimeSinceStartup < deadline) yield return null;

            if (!IsReady)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"[AdsConfigGate] hết {timeoutSeconds}s chờ remote config -> init bằng config đang có. " +
                    Describe());
#endif
            }
            else
            {
#if ENABLE_DEBUG_LOG
                Debug.Log("[AdsConfigGate] remote config đã đủ -> init mediation. " + Describe());
#endif
            }
        }

        /// <summary>
        /// Chờ RIÊNG cho AdMob tới khi <see cref="IsAdmobConfigReady"/>. Tách khỏi
        /// <see cref="WaitUntilReady"/> vì MAX có thể đã init theo timeout ngắn của nó — AdMob thì không
        /// được init bằng ID data4game khi CMS còn chưa trả lời.
        /// </summary>
        public static IEnumerator WaitUntilAdmobConfigReady(float timeoutSeconds = CMS_FIRST_TIMEOUT_SECONDS)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!IsAdmobConfigReady && Time.realtimeSinceStartup < deadline) yield return null;

            if (!IsAdmobConfigReady)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning(
                    $"[AdsConfigGate] hết {timeoutSeconds}s chờ config AdMob -> init bằng config đang có. " +
                    Describe());
#endif
            }
            else
            {
#if ENABLE_DEBUG_LOG
                Debug.Log("[AdsConfigGate] config AdMob đã chốt nguồn -> init AdMob. " + Describe());
#endif
            }
        }

        public static string Describe()
            => $"data4game={RemoteConfig.FConfig.InitState} cms={RemoteConfigCms.FConfig.InitState} " +
               $"cmsHas{ADMOB_META_CONFIG_KEY}={CmsHasAdmobMetaConfig}";

        // Succeed / Failed / Cancelled đều là điểm dừng: chờ thêm cũng không đổi được gì.
        private static bool IsSettled(ExecState state)
            => state == ExecState.Succeed || state == ExecState.Failed || state == ExecState.Cancelled;
    }
}
