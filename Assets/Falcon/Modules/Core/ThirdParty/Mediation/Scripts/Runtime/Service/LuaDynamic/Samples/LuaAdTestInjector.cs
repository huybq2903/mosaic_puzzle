/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

// CHỈ ĐỂ TEST LuaDynamic khi server chưa gửi sc_ad_monetize_lua_script.
// Bật bằng define LUA_AD_TEST (Player Settings > Scripting Define Symbols). Xoá cả thư mục Samples là gỡ sạch.
#if LUA_AD_TEST && MAX_ENABLE
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic.Samples
{
    /// <summary>
    /// Giả lập server gửi <see cref="SCAdMonetizeLuaScript"/> bằng script trong
    /// <c>Samples/Resources/LuaAdTest.txt</c>, rồi hiện bảng nút bấm để hỏi / show ad.
    /// Chạy ở BeforeSceneLoad để script có trước lúc init MAX -> phiên chốt chế độ LUA.
    /// </summary>
    public static class LuaAdTestInjector
    {
        private const string _RESOURCE = "LuaAdTest";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Inject()
        {
            var asset = Resources.Load<TextAsset>(_RESOURCE);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                Debug.LogWarning($"[LuaAd][TEST] không thấy Resources/{_RESOURCE}.txt -> không giả lập");
                return;
            }

            Debug.Log($"[LuaAd][TEST] giả lập sc_ad_monetize_lua_script ({asset.text.Length} ký tự)");
            new SCAdMonetizeLuaScript { luaScript = asset.text }.OnData();

            // var go = new GameObject("LuaAdTestPanel");
            // Object.DontDestroyOnLoad(go);
            // go.AddComponent<LuaAdTestPanel>();
        }
    }

    /// <summary>Bảng nút test góc trên trái màn hình (OnGUI).</summary>
    public sealed class LuaAdTestPanel : MonoBehaviour
    {
        private const string _WHERE = "lua_test";

        private bool _expanded = true;
        private int _level = 5;
        private string _status = "";
        private Vector2 _scroll;

        private void OnGUI()
        {
            var scale = Mathf.Max(1f, Screen.width / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale - 20f;

            GUILayout.BeginArea(new Rect(10f, 40f, width, Screen.height / scale - 60f));
            if (GUILayout.Button(_expanded ? "▲ LuaAd TEST" : "▼ LuaAd TEST", GUILayout.Height(36f)))
                _expanded = !_expanded;

            if (_expanded)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label($"Chế độ: {(LuaAdMonetize.IsLuaMode ? "LUA" : "chưa chốt / MULTICALL")}" +
                                $" | script chạy: {LuaAdMonetize.IsRunning}" +
                                $" | inter: {LuaAdMonetize.HasReady(AdType.Interstitial)}" +
                                $" | rw: {LuaAdMonetize.HasReady(AdType.Reward)}");

                GUILayout.BeginHorizontal();
                GUILayout.Label($"level param = {_level}", GUILayout.Width(150f));
                if (GUILayout.Button("-", GUILayout.Height(36f))) _level = Mathf.Max(0, _level - 1);
                if (GUILayout.Button("+", GUILayout.Height(36f))) _level++;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Hỏi IsShowInterstitial", GUILayout.Height(40f))) Ask();
                if (GUILayout.Button("Hỏi IsShowRewarded", GUILayout.Height(40f))) AskRewarded();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Hỏi rồi show Inter", GUILayout.Height(40f)))
                {
                    if (Ask()) ShowInter();
                }

                if (GUILayout.Button("Hỏi rồi show RW", GUILayout.Height(40f)))
                {
                    if (AskRewarded()) ShowRewarded(true);
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Show Inter", GUILayout.Height(40f))) ShowInter();
                if (GUILayout.Button("Show Rewarded", GUILayout.Height(40f))) ShowRewarded(true);
                if (GUILayout.Button("Show RW (không inter)", GUILayout.Height(40f))) ShowRewarded(false);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Log danh sách ad", GUILayout.Height(36f)))
                {
                    _status = LuaAdMonetize.DescribeCache();
                    LuaAdMonetize.LogCacheNow("nút test");
                }

                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(120f));
                GUILayout.Label(_status);
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            GUILayout.EndArea();
        }

        private bool Ask()
        {
            //level thật được LuaAdMonetize tự gắn; ghi đè bằng level của bảng test để thử nhánh của script
            var ok = MediationManager.Instance.IsShowInterstitial(_WHERE,
                new Dictionary<string, object> { { "level", _level }, { "fromTestPanel", true } });
            SetStatus($"IsShowInterstitial(level={_level}) -> {ok}");
            return ok;
        }

        private bool AskRewarded()
        {
            var ok = MediationManager.Instance.IsShowRewarded(_WHERE,
                new Dictionary<string, object> { { "level", _level }, { "fromTestPanel", true } });
            SetStatus($"IsShowRewarded(level={_level}) -> {ok}");
            return ok;
        }

        private void ShowInter()
        {
            SetStatus("ShowInterstitial ...");
            MediationManager.Instance.ShowInterstitial(_WHERE,
                () => SetStatus("Inter: ĐÓNG (onClosed)"),
                () => SetStatus("Inter: FAIL (onFail)"));
        }

        private void ShowRewarded(bool allowInter)
        {
            SetStatus($"ShowRewardedVideo (cho phép inter thay = {allowInter}) ...");
            MediationManager.Instance.ShowRewardedVideo(_WHERE,
                () => SetStatus("Rewarded: ĐƯỢC THƯỞNG (onRewardedComplete)"),
                () => SetStatus("Rewarded: FAIL (onFail)"),
                allowInter);
        }

        private void SetStatus(string message)
        {
            _status = message;
            Debug.Log("[LuaAd][TEST] " + message);
        }
    }
}
#endif
