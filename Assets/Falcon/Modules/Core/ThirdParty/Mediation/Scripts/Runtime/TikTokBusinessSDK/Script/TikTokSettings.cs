using UnityEngine;

namespace SDK
{
    /// <summary>
    /// Nơi nhập 6 thông số thật của TikTok Business SDK, thay cho đám placeholder "YOUR_..." hard-code
    /// trong <c>AutoInit</c>.
    ///
    /// Mở bằng menu <c>Falcon/Modules/ThirdParty/TiktokSettings</c> — chưa có asset thì menu tự tạo.
    /// Asset nằm trong <c>Resources</c> nên đọc được lúc runtime mà không cần kéo tham chiếu vào scene.
    ///
    /// KHÔNG điền = <see cref="HasConfig"/> false = KHÔNG init TikTok và KHÔNG bắn event nào. Cố tình
    /// như vậy: init bằng id giả thì SDK vẫn chạy nhưng mọi event rơi vào hư không, và không ai biết.
    /// </summary>
    public class TikTokSettings : ScriptableObject
    {
        /// <summary>Tên asset trong Resources.</summary>
        public const string ResourceName = "TikTokSettings";

        [Header("iOS")]
        [Tooltip("iOS access token (App Secret) do TikTok cấp.")]
        public string iosAccessToken;

        [Tooltip("iOS App ID — id app trên App Store.")]
        public string iosAppId;

        [Tooltip("iOS TikTok App ID — id app bên TikTok Events Manager.")]
        public string iosTiktokAppId;

        [Header("Android")]
        [Tooltip("Android access token (App Secret) do TikTok cấp.")]
        public string androidAccessToken;

        [Tooltip("Android App ID — package name trên Google Play.")]
        public string androidAppId;

        [Tooltip("Android TikTok App ID — id app bên TikTok Events Manager.")]
        public string androidTiktokAppId;

        private static TikTokSettings _cached;

        /// <summary>
        /// Config đã init thành công chưa. <see cref="HasConfig"/> chỉ nói "có điền id"; cái này mới nói
        /// "SDK thật sự dùng được" — bên ngoài phải hỏi cái này trước khi gửi event.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        internal static void MarkInitialized(bool value) => IsInitialized = value;

        /// <summary>Asset trong Resources; null nếu chưa tạo. Cache lại vì runtime hỏi nhiều lần.</summary>
        public static TikTokSettings Load()
        {
            if (_cached != null) return _cached;

            _cached = Resources.Load<TikTokSettings>(ResourceName);
            return _cached;
        }

        /// <summary>
        /// Có đủ thông số để init TikTok trên platform ĐANG BUILD không.
        ///
        /// Bắt buộc appId + tiktokAppId; accessToken để trống vẫn chạy được vì
        /// <see cref="TikTokConfig"/> chỉ nhét param nào khác rỗng — thiếu nó thì mất phần server-side
        /// của TikTok chứ SDK không chết.
        /// </summary>
        public static bool HasConfig
        {
            get
            {
                var settings = Load();
                if (settings == null) return false;

#if UNITY_ANDROID
                return !string.IsNullOrEmpty(settings.androidAppId) &&
                       !string.IsNullOrEmpty(settings.androidTiktokAppId);
#elif UNITY_IOS
                return !string.IsNullOrEmpty(settings.iosAppId) &&
                       !string.IsNullOrEmpty(settings.iosTiktokAppId);
#else
                return false;
#endif
            }
        }

        /// <summary>Dựng <see cref="TikTokConfig"/> từ 6 trường; null nếu chưa đủ thông số.</summary>
        public static TikTokConfig BuildConfig()
        {
            if (!HasConfig) return null;

            var s = Load();
            return new TikTokConfig(
                s.iosAccessToken,
                s.iosAppId,
                s.iosTiktokAppId,
                s.androidAccessToken,
                s.androidAppId,
                s.androidTiktokAppId);
        }

        /// <summary>Một dòng mô tả để log lúc init — KHÔNG in access token ra console.</summary>
        public static string Describe()
        {
            var settings = Load();
            if (settings == null) return "(chưa tạo asset TikTokSettings trong Resources)";

#if UNITY_ANDROID
            return $"android appId={Show(settings.androidAppId)} " +
                   $"tiktokAppId={Show(settings.androidTiktokAppId)} " +
                   $"accessToken={(string.IsNullOrEmpty(settings.androidAccessToken) ? "(trống)" : "(đã điền)")}";
#elif UNITY_IOS
            return $"ios appId={Show(settings.iosAppId)} " +
                   $"tiktokAppId={Show(settings.iosTiktokAppId)} " +
                   $"accessToken={(string.IsNullOrEmpty(settings.iosAccessToken) ? "(trống)" : "(đã điền)")}";
#else
            return "(platform này không chạy TikTok)";
#endif
        }

        private static string Show(string value) => string.IsNullOrEmpty(value) ? "(trống)" : value;
    }
}
