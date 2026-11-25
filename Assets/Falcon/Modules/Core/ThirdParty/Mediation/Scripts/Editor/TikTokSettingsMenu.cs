#if TIKTOK_ENABLE
using System.IO;
using SDK;
using UnityEditor;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Editor
{
    /// <summary>
    /// Menu mở asset <see cref="TikTokSettings"/>. Chưa có thì TẠO LUÔN rồi ping — không bắt người
    /// dùng tự mò tạo ScriptableObject đúng chỗ.
    /// </summary>
    public static class TikTokSettingsMenu
    {
        private const string _RESOURCES_FOLDER =
            "Assets/FalconAssets/Modules/Core/ThirdParty/Mediation/Resources";

        private const string _ASSET_PATH = _RESOURCES_FOLDER + "/" + TikTokSettings.ResourceName + ".asset";

        [MenuItem("Falcon/Modules/ThirdParty/TiktokSettings")]
        public static void OpenTikTokSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TikTokSettings>(_ASSET_PATH);

            if (settings == null)
            {
                if (!Directory.Exists(_RESOURCES_FOLDER))
                {
                    Directory.CreateDirectory(_RESOURCES_FOLDER);
                    AssetDatabase.Refresh();
                }

                settings = ScriptableObject.CreateInstance<TikTokSettings>();
                AssetDatabase.CreateAsset(settings, _ASSET_PATH);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[TikTok] đã tạo {_ASSET_PATH} — điền 6 thông số TikTok cấp vào đây. " +
                          "Để trống thì TikTok SDK KHÔNG init và không bắn event nào.");
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
#endif
