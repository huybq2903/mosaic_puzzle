using Falcon.Modules.Core.ThirdParty.Ump.Runtime;
using UnityEngine;

namespace SDK
{
    /// <summary>
    /// Tự init TikTok Business SDK lúc game chạy, bằng thông số THẬT trong asset
    /// <see cref="TikTokSettings"/> (menu <c>Falcon/Modules/ThirdParty/TiktokSettings</c>).
    ///
    /// Chưa điền thông số thì KHÔNG init và không dựng object gì cả — thà im lặng còn hơn init bằng id
    /// giả rồi mọi event rơi vào hư không mà không ai biết.
    ///
    /// Nằm trong namespace <c>SDK</c> như mọi file khác của TikTok SDK: từ lúc thư mục này gộp vào
    /// assembly Mediation.Runtime, để class ở global namespace là đụng tên với bất kỳ <c>AutoInit</c>
    /// nào của game bên Assembly-CSharp.
    /// </summary>
    public class AutoInit : AutoSingleton<AutoInit>
    {
        private const string _LOG_TAG = "[TikTok]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            //kiểm tra TRƯỚC khi tạo instance: không có config thì đừng dựng cả GameObject
            if (!TikTokSettings.HasConfig)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_LOG_TAG} BỎ QUA init — chưa có thông số trong TikTokSettings. " +
                                 $"Mở menu Falcon/Modules/ThirdParty/TiktokSettings để điền. " +
                                 $"{TikTokSettings.Describe()}");
#endif
                return;
            }

            _ = Instance;
        }

        protected override void Awake()
        {
            base.Awake();

            var config = TikTokSettings.BuildConfig();
            if (config == null)
            {
                //config biến mất giữa AutoCreate và Awake (asset bị gỡ) — không có gì để init
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"{_LOG_TAG} BỎ QUA init — TikTokSettings không dựng được config.");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"{_LOG_TAG} init SDK — {TikTokSettings.Describe()}");
#endif

            TikTokBusinessSDK.InitializeSdk(
                config,
                (success, code, message) =>
                {
                    TikTokSettings.MarkInitialized(success);

                    if (success)
                    {
#if ENABLE_DEBUG_LOG
                        Debug.Log($"{_LOG_TAG} init THÀNH CÔNG — bắt đầu gửi được event.");
#endif
                    }
                    else
                    {
#if ENABLE_DEBUG_LOG
                        Debug.LogError($"{_LOG_TAG} init THẤT BẠI: code={code}, message={message}. " +
                                       "Mọi event TikTok sẽ bị bỏ qua.");
#endif
                    }
                }
            );
        }
    }
}
