/*
 * Author: Bui Quang Huy
 * Email: huybq@falcongames.com
 * Company: Falcon Games
 * Date: 2026-09-10
 */

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EditorScene -> GameView maximize + Free Aspect. StartScene/GameScene -> thu gọn + 1080x1920.
// Lúc play chỉ đổi aspect, không đụng maximize. Chơi thử từ Level Editor (play bắt đầu ở EditorScene) thì KHÔNG đổi gì: đổi sang
// 1080x1920 rồi về Free Aspect làm Unity đổi mức zoom của Game view, giao diện tool to / nhỏ khác đi sau mỗi lần chơi thử.
[InitializeOnLoad]
internal static class EditorSceneGameViewSetup
{
    private const string EditorSceneName = "EditorScene";
    private const int PortraitWidth = 1080;
    private const int PortraitHeight = 1920;
    private const string PortraitLabel = "Portrait";
    private const int FreeAspectIndex = 0; // Free Aspect luôn đứng đầu mọi size group

    private static readonly Assembly EditorAsm = typeof(EditorWindow).Assembly;
    private static readonly Type GameViewType = EditorAsm.GetType("UnityEditor.GameView");

    /// <summary>
    /// Play mode bắt đầu ở EditorScene: các lần đổi scene trong lúc play là chơi thử / quay lại editor. Lưu trong SessionState vì vào
    /// Play có thể nạp lại domain (biến static về mặc định).
    /// </summary>
    private const string PlayFromEditorKey = "EditorSceneGameViewSetup.PlayFromEditor";

    /// <summary>Zoom của Game view lúc bấm Play, lưu trong SessionState vì vào Play có thể nạp lại domain.</summary>
    private const string ZoomKey = "EditorSceneGameViewSetup.Zoom";

    private const double RestoreDelay = 0.5; // giây chờ sau khi đổi chế độ play trước khi trả zoom

    private static bool PlayFromEditor
    {
        get => SessionState.GetBool(PlayFromEditorKey, false);
        set => SessionState.SetBool(PlayFromEditorKey, value);
    }

    static EditorSceneGameViewSetup()
    {
        EditorSceneManager.sceneOpened += (scene, mode) =>
        {
            if (mode == OpenSceneMode.Single) ApplyFor(scene.name);
        };

        // Play/Back To Editor đổi scene bằng SceneManager runtime, sceneOpened không bắn
        SceneManager.activeSceneChanged += (_, next) =>
        {
            if (!PlayFromEditor) ApplyFor(next.name);
        };

        // Thoát play mode không bắn sceneOpened -> tự khôi phục theo scene đang mở
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                PlayFromEditor = SceneManager.GetActiveScene().name == EditorSceneName;
                SessionState.SetFloat(ZoomKey, ReadZoom(FindView()));
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                PlayFromEditor = false;
                ApplyFor(SceneManager.GetActiveScene().name);
            }

            // Vào và thoát play Unity đặt lại zoom về hệ số DPI (1.25) khá muộn, nên chờ rồi trả về mức đã chọn trước khi play
            if (state is PlayModeStateChange.EnteredPlayMode or PlayModeStateChange.EnteredEditMode)
                RunAfter(RestoreDelay, () => RestoreZoom(FindView()));
        };
    }

    private static void ApplyFor(string sceneName)
    {
        switch (sceneName)
        {
            case EditorSceneName:
                // delayCall: đổi layout ngay trong callback bị layout rebuild nuốt mất
                EditorApplication.delayCall += () => Apply(FreeAspectIndex, true);
                break;
            case "StartScene":
            case "GameScene":
                EditorApplication.delayCall += () =>
                    Apply(FindOrAddSize(PortraitWidth, PortraitHeight, PortraitLabel), false);
                break;
        }
    }

    private static void Apply(int sizeIndex, bool maximize)
    {
        var view = FindView();
        if (view == null) return;

        var selected = SelectedIndex(view);

        // Chỉ ép về portrait khi đang Free Aspect; không đọc được index thì cứ ép như cũ.
        if (sizeIndex == FreeAspectIndex || selected < 0 || selected == FreeAspectIndex)
            Select(view, sizeIndex);

        // Đổi maximized lúc play làm Unity dựng lại GameView -> canvas layout lại một nhịp
        if (EditorApplication.isPlaying) return;

        if (view.maximized != maximize) view.maximized = maximize;

        // Maximize xong Unity mới tính lại zoom nên đặt lại ở nhịp sau
        EditorApplication.delayCall += () => ShrinkZoom(view);
    }

    private static void RunAfter(double seconds, Action action)
    {
        var due = EditorApplication.timeSinceStartup + seconds;

        void Tick()
        {
            if (EditorApplication.timeSinceStartup < due) return;

            EditorApplication.update -= Tick;
            action();
        }

        EditorApplication.update += Tick;
    }

    private static float ReadZoom(EditorWindow view)
    {
        var prop = view != null ? new SerializedObject(view).FindProperty("m_ZoomArea.m_Scale") : null;
        return prop != null ? prop.vector2Value.x : -1f;
    }

    private static void RestoreZoom(EditorWindow view)
    {
        var zoom = SessionState.GetFloat(ZoomKey, -1f);
        if (view != null && zoom > 0f && !Mathf.Approximately(ReadZoom(view), zoom)) SnapZoom(view, zoom);
    }

    // Size cố định (portrait) thì kéo zoom về nhỏ nhất; Free Aspect để Unity tự quyết.
    private static void ShrinkZoom(EditorWindow view)
    {
        if (view == null || SelectedIndex(view) == FreeAspectIndex) return;

        SnapZoom(view, MinScale(view));
    }

    // Scale nhỏ nhất Unity cho phép ở size hiện tại; -1 nếu không đọc được.
    private static float MinScale(EditorWindow view)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var prop = GameViewType.GetProperty("minScale", flags);
        if (prop != null && prop.PropertyType == typeof(float)) return (float)prop.GetValue(view);

        var field = GameViewType.GetField("minScale", flags);
        return field != null && field.FieldType == typeof(float) ? (float)field.GetValue(view) : -1f;
    }

    // Đặt Scale của Game view; API không thấy thì bỏ qua.
    private static void SnapZoom(EditorWindow view, float zoom)
    {
        if (view == null) return;

        if (zoom <= 0f)
        {
            Debug.LogWarning($"{nameof(EditorSceneGameViewSetup)}: không đọc được GameView.minScale, bỏ qua đặt lại scale.");
            return;
        }

        var snap = GameViewType.GetMethod("SnapZoom",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(float) }, null);

        if (snap == null)
        {
            Debug.LogWarning($"{nameof(EditorSceneGameViewSetup)}: không thấy GameView.SnapZoom, bỏ qua đặt lại scale.");
            return;
        }

        snap.Invoke(view, new object[] { zoom });
        view.Repaint();
    }

    // Không dùng GetWindow: nó TẠO GameView mới, cướp play mode view của Simulator.
    private static EditorWindow FindView()
    {
        if (GameViewType == null) return null;

        var views = Resources.FindObjectsOfTypeAll(GameViewType);
        return views.Length > 0 ? (EditorWindow)views[0] : null;
    }

    // -1 nếu không đọc được
    private static int SelectedIndex(EditorWindow view)
    {
        var prop = GameViewType?.GetProperty("selectedSizeIndex",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return prop == null || view == null ? -1 : (int)prop.GetValue(view);
    }

    private static void Select(EditorWindow view, int sizeIndex)
    {
        if (view == null || sizeIndex < 0) return;

        // Method public nhưng nằm trong type internal -> phải có cả Public lẫn NonPublic
        var setSize = GameViewType.GetMethod("SizeSelectionCallback",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int), typeof(object) }, null);

        if (setSize == null)
        {
            Debug.LogWarning($"{nameof(EditorSceneGameViewSetup)}: không thấy GameView.SizeSelectionCallback, bỏ qua đổi aspect.");
            return;
        }

        setSize.Invoke(view, new object[] { sizeIndex, null });
    }

    // Tìm size w x h trong group hiện tại, chưa có thì tự thêm custom size.
    private static int FindOrAddSize(int width, int height, string label)
    {
        var sizesType = EditorAsm.GetType("UnityEditor.GameViewSizes");
        var sizeType = EditorAsm.GetType("UnityEditor.GameViewSize");
        var sizeTypeEnum = EditorAsm.GetType("UnityEditor.GameViewSizeType");
        if (sizesType == null || sizeType == null || sizeTypeEnum == null) return -1;

        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var instance = singleton.GetProperty("instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
        var group = sizesType.GetProperty("currentGroup")?.GetValue(instance);
        if (group == null) return -1;

        var groupType = group.GetType();
        var count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
        var getSize = groupType.GetMethod("GetGameViewSize");
        var widthProp = sizeType.GetProperty("width");
        var heightProp = sizeType.GetProperty("height");

        for (var i = 0; i < count; i++)
        {
            var size = getSize.Invoke(group, new object[] { i });
            if ((int)widthProp.GetValue(size) == width && (int)heightProp.GetValue(size) == height)
                return i;
        }

        var fixedResolution = Enum.Parse(sizeTypeEnum, "FixedResolution");
        var created = Activator.CreateInstance(sizeType, fixedResolution, width, height, label);
        groupType.GetMethod("AddCustomSize").Invoke(group, new[] { created });
        sizesType.GetMethod("SaveToHDD").Invoke(instance, null);

        return (int)groupType.GetMethod("IndexOf").Invoke(group, new[] { created });
    }
}
