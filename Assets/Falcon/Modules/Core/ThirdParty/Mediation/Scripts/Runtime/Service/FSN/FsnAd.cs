using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#if UNITY_IOS
using AOT;
#endif

public enum FsnNativeAdType
{
    Native = 0,
    NativeFakeClose = 1,
    NativeCountdown = 2,
    Collapsible = 3
}

public enum FsnNativeAdRatio
{
    Any = 0,
    Landscape = 1,
    Portrait = 2,
    Square = 3
}

public enum FsnOverlayPosition
{
    TopRight = 0,
    TopLeft = 1,
    BottomLeft = 2,
    BottomRight = 3
}

public class FsnAd
{
#if UNITY_ANDROID
    private AndroidJavaObject androidFsnAd;

    // Keep proxy references to avoid GC breaking Java callbacks.
    private AndroidOnFsnNativeLoadListener _nativeLoadListenerProxy;
    private AndroidOnFsnNativeCompletedListener _nativeCompletedListenerProxy;
#endif

    private static readonly object _lock = new object();
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static FsnMainThreadDispatcher _dispatcher;
    private static bool _isInitialized = false;

    private FsnNativeAdType _nativeType = FsnNativeAdType.Native;
    private FsnNativeAdRatio _nativeRatio = FsnNativeAdRatio.Any;
    private int _styleIndex = 2;
    private string _adUnitId = string.Empty;
    private int _countDownSec = 0;
    private long _delayForCountDown = 0;

    // Sau Destroy(): mọi lời gọi xuống native bị bỏ qua (object Java đã Dispose, gọi tiếp là crash JNI).
    private bool _destroyed;
    public bool IsDestroyed => _destroyed;

    public int DebugStyleIndex => _styleIndex;
    public string DebugStyleName => GetStyleDebugName(_styleIndex);
    public FsnNativeAdType DebugNativeType => _nativeType;
    public FsnNativeAdRatio DebugNativeRatio => _nativeRatio;
    public string DebugAdUnitId => _adUnitId;

    public string GetDebugSummary()
    {
        return $"Type={_nativeType}, Ratio={_nativeRatio}, Style={_styleIndex}({GetStyleDebugName(_styleIndex)}), AdUnitId={_adUnitId}, CountDown={_countDownSec}, DelayCountdown={_delayForCountDown}";
    }

    private static string GetStyleDebugName(int styleIndex)
    {
        return styleIndex switch
        {
            0 => "Style1-FullMediaDark",
            1 => "Style2-TopMediaWhite",
            2 => "Style3-FullscreenVideoBottomInfo",
            3 => "Style4-CenteredPopup",
            _ => "Unknown-FallbackStyle3"
        };
    }

    private static string GetPositionDebugName(int position)
    {
        return position switch
        {
            0 => "TopRight",
            1 => "TopLeft",
            2 => "BottomLeft",
            3 => "BottomRight",
            _ => "Unknown"
        };
    }

#if UNITY_IOS
    private static int _nextIOSRequestId = 0;
    private readonly int _iosRequestId;

    private static bool _iosCallbacksRegistered = false;

    private sealed class IOSNativeCallbacks
    {
        public Action<int, string> OnLoadingCompleted;
        public Action OnLoadingStarted;
        public Action<string, string, long, string> OnAdPaid;
        public Action<string> OnAdCompleted;

        public FsnNativeAdType NativeType;
        public FsnNativeAdRatio NativeRatio;
        public int StyleIndex;
        public string AdUnitId;

        public string DebugSummary =>
            $"Type={NativeType}, Ratio={NativeRatio}, Style={StyleIndex}({GetStyleDebugName(StyleIndex)}), AdUnitId={AdUnitId}";
    }

    private static readonly Dictionary<int, IOSNativeCallbacks> _iosCallbacksByRequestId =
        new Dictionary<int, IOSNativeCallbacks>();

    // Giữ delegate instance static để tránh bị GC khi native gọi ngược về.
    private static readonly IOSLoadingCompletedDelegate _iosLoadingCompletedDelegate = IOS_OnLoadingCompleted;
    private static readonly IOSLoadingStartedDelegate _iosLoadingStartedDelegate = IOS_OnLoadingStarted;
    private static readonly IOSAdPaidDelegate _iosAdPaidDelegate = IOS_OnAdPaid;
    private static readonly IOSAdCompletedDelegate _iosAdCompletedDelegate = IOS_OnAdCompleted;
#endif

    public FsnAd()
    {
        EnsureDispatcherExists();

#if UNITY_IOS
        lock (_lock)
        {
            _nextIOSRequestId++;
            _iosRequestId = _nextIOSRequestId;
        }

        EnsureIOSCallbacksRegistered();
#endif

#if UNITY_ANDROID
        if (Application.platform == RuntimePlatform.Android)
        {
            try
            {
                androidFsnAd = new AndroidJavaObject("falcon.modules.core.thirdparty.mediation.fsnads.FsnAd");
#if ENABLE_DEBUG_LOG
                Debug.Log("[FsnAd] AndroidJavaObject created successfully");
#endif
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"[FsnAd] Failed to create AndroidJavaObject: {e.Message}\n{e.StackTrace}");
#endif
            }
        }
#endif
    }

    private static void EnsureDispatcherExists()
    {
        if (_isInitialized && _dispatcher != null)
            return;

        _dispatcher = UnityEngine.Object.FindObjectOfType<FsnMainThreadDispatcher>();

        if (_dispatcher == null)
        {
            var go = new GameObject("FsnMainThreadDispatcher");
            _dispatcher = go.AddComponent<FsnMainThreadDispatcher>();
            UnityEngine.Object.DontDestroyOnLoad(go);
#if ENABLE_DEBUG_LOG
            Debug.Log("[FsnAd] FsnMainThreadDispatcher created");
#endif
        }

        _isInitialized = true;
    }

    public static void ExecuteOnMainThread(Action action)
    {
        if (action == null)
            return;

        lock (_lock)
        {
            _executionQueue.Enqueue(action);
        }
    }

    public static void ProcessQueue()
    {
        Action[] actionsToExecute;

        lock (_lock)
        {
            if (_executionQueue.Count == 0)
                return;

            actionsToExecute = _executionQueue.ToArray();
            _executionQueue.Clear();
        }

        foreach (var action in actionsToExecute)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"[FsnAd] Error executing action on main thread: {e.Message}\n{e.StackTrace}");
#endif
            }
        }
    }

#if UNITY_ANDROID
    private bool IsAndroidReady(string methodName, bool invokeFallbackCallback = false, Action fallbackCallback = null)
    {
        if (Application.platform != RuntimePlatform.Android)
            return false;

        if (_destroyed)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"[FsnAd] {methodName}() ignored - instance already destroyed");
#endif

            if (invokeFallbackCallback)
                ExecuteOnMainThread(() => fallbackCallback?.Invoke());

            return false;
        }

        if (androidFsnAd == null)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] {methodName}() failed - androidFsnAd is null");
#endif

            if (invokeFallbackCallback)
                ExecuteOnMainThread(() => fallbackCallback?.Invoke());

            return false;
        }

        return true;
    }

    private AndroidJavaObject GetCurrentActivity()
    {
        using AndroidJavaClass unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        return unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity");
    }
#endif

    // =========================
    // iOS External Methods
    // =========================

#if UNITY_IOS
    [DllImport("__Internal")]
    private static extern void FsnAdIOS_InitNative(int requestId,int type,int ratio,string adUnitId,int countDownSec,long delayForCountDown);

    [DllImport("__Internal")]
    private static extern void FsnAdIOS_LoadNativeAd(int requestId,int ratio);

    [DllImport("__Internal")]
    private static extern void FsnAdIOS_LoadNativeAdWithFloor(int requestId,int ratio,string bidFloorCents);

    [DllImport("__Internal")]
    private static extern double FsnAdIOS_GetLoadedRevenue(int requestId);

    [DllImport("__Internal")]
    private static extern void FsnAdIOS_ShowNativeAd(int requestId,int layoutType,int overlayOpenStorePos,int overlayClosePos);

    [DllImport("__Internal")]
    private static extern void FsnAdIOS_HideNativeAd(int requestId);

    [DllImport("__Internal")]
    private static extern bool FsnAdIOS_IsNativeAdReady(int requestId);

    [DllImport("__Internal")]
    private static extern void FsnAdIOS_DestroyNative(int requestId);

    public delegate void IOSLoadingCompletedDelegate(int requestId, int errorCode, string errorMessage);
    public delegate void IOSLoadingStartedDelegate(int requestId);
    public delegate void IOSAdPaidDelegate(int requestId, string adSource, string adUnitId, long valueMicros, string currencyCode);
    public delegate void IOSAdCompletedDelegate(int requestId, string errorMessage);

    [DllImport("__Internal")]
    private static extern void FsnAdIOS_SetCallbacks(IOSLoadingCompletedDelegate onCompleted,IOSLoadingStartedDelegate onStarted,IOSAdPaidDelegate onPaid,IOSAdCompletedDelegate onAdCompleted);

    private static void EnsureIOSCallbacksRegistered()
    {
        if (_iosCallbacksRegistered)
            return;

        // Thiếu symbol native (plugin chưa link) -> EntryPointNotFoundException. Không bọc thì ném ra
        // từ constructor FsnAd, kéo theo cả luồng load của FsnAdManager.
        try
        {
            FsnAdIOS_SetCallbacks(_iosLoadingCompletedDelegate,_iosLoadingStartedDelegate,_iosAdPaidDelegate,_iosAdCompletedDelegate);
            _iosCallbacksRegistered = true;
#if ENABLE_DEBUG_LOG
            Debug.Log("[FsnAd] iOS native callbacks registered");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS SetCallbacks exception: {e.Message}\n{e.StackTrace}");
#endif
        }
    }

    private static IOSNativeCallbacks GetOrCreateIOSCallbacks(int requestId)
    {
        if (!_iosCallbacksByRequestId.TryGetValue(requestId, out var callbacks))
        {
            callbacks = new IOSNativeCallbacks();
            _iosCallbacksByRequestId[requestId] = callbacks;
        }

        return callbacks;
    }

    private static bool TryGetIOSCallbacks(int requestId, out IOSNativeCallbacks callbacks)
    {
        return _iosCallbacksByRequestId.TryGetValue(requestId, out callbacks);
    }

    private static void RemoveIOSCallbacks(int requestId)
    {
        if (_iosCallbacksByRequestId.ContainsKey(requestId))
        {
            _iosCallbacksByRequestId.Remove(requestId);
        }
    }
#endif

    // =========================
    // Init
    // =========================

    public void InitNative(FsnNativeAdType type, FsnNativeAdRatio ratio, string adUnitId, int countDownSec,
        int styleIndex, long delayForCountDown)
    {
        _nativeType = type;
        _nativeRatio = ratio;
        _styleIndex = styleIndex;
        _adUnitId = adUnitId ?? string.Empty;
        _countDownSec = countDownSec;
        _delayForCountDown = delayForCountDown;

#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(InitNative)))
            return;

        try
        {
            androidFsnAd.Call("InitNative", (int)type, (int)ratio, adUnitId, countDownSec, delayForCountDown);
#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Init] Android InitNative called. {GetDebugSummary()}");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] InitNative({type}) exception: {e.Message}\n{e.StackTrace}");
#endif
        }
#elif UNITY_IOS
        if (_destroyed) return;

        var callbacks = GetOrCreateIOSCallbacks(_iosRequestId);
        callbacks.NativeType = type;
        callbacks.NativeRatio = ratio;
        callbacks.StyleIndex = styleIndex;
        callbacks.AdUnitId = adUnitId ?? string.Empty;

        try
        {
            FsnAdIOS_InitNative(_iosRequestId,(int)type,(int)ratio,adUnitId ?? string.Empty,countDownSec,delayForCountDown);
#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Init] iOS InitNative called. RequestId={_iosRequestId}, {GetDebugSummary()}");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS InitNative({type}) exception: {e.Message}\n{e.StackTrace}");
#endif
        }
#endif
    }

    // =========================
    // Native
    // =========================

    public void SetNativeListener(Action<int, string> onLoadingCompleted, Action onLoadingStarted,
        Action<string, string, long, string> onAdPaid)
    {
#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(SetNativeListener)))
            return;

        try
        {
            _nativeLoadListenerProxy =
                new AndroidOnFsnNativeLoadListener(onLoadingCompleted, onLoadingStarted, onAdPaid);
            androidFsnAd.Call("SetNativeListener", _nativeLoadListenerProxy);
#if ENABLE_DEBUG_LOG
            Debug.Log("[FsnAd] SetNativeListener() Android called");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] SetNativeListener() exception: {e.Message}\n{e.StackTrace}");
#endif
        }
#elif UNITY_IOS
        if (_destroyed) return;

        EnsureIOSCallbacksRegistered();

        var callbacks = GetOrCreateIOSCallbacks(_iosRequestId);
        callbacks.OnLoadingCompleted = onLoadingCompleted;
        callbacks.OnLoadingStarted = onLoadingStarted;
        callbacks.OnAdPaid = onAdPaid;

        callbacks.NativeType = _nativeType;
        callbacks.NativeRatio = _nativeRatio;
        callbacks.StyleIndex = _styleIndex;
        callbacks.AdUnitId = _adUnitId;

#if ENABLE_DEBUG_LOG
        Debug.Log($"[FsnAd][Listener] iOS SetNativeListener called. RequestId={_iosRequestId}, {GetDebugSummary()}");
#endif
#endif
    }

    // Revenue của FSN = doanh thu MỘT impression (USD), còn giá sàn AdMob (extras "rusd") theo eCPM tính
    // bằng CENT -> ×1000 (eCPM) ×100 (cent). Cùng quy ước với AdmobBidAd.FormatBidFloor.
    private const double _ImpressionsPerEcpm = 1000d;
    private const double _CentsPerUsd = 100d;
    private const double _MinBidFloorCents = 0.005d;

    /// <summary>Giá sàn USD/impression -> chuỗi cent eCPM cho "rusd"; null nếu không cần set sàn.</summary>
    public static string FormatBidFloorCents(double floorUsd)
    {
        if (double.IsNaN(floorUsd) || double.IsInfinity(floorUsd) || floorUsd <= 0d) return null;

        var cents = floorUsd * _ImpressionsPerEcpm * _CentsPerUsd;
        if (cents < _MinBidFloorCents) return null;

        return cents.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <param name="floorUsd">
    /// Giá sàn theo USD / impression (cùng thang với <see cref="GetLoadedRevenue"/>). &lt;= 0 = không set sàn.
    /// </param>
    public void LoadNativeAd(double floorUsd = 0d)
    {
        var floorCents = FormatBidFloorCents(floorUsd);

#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(LoadNativeAd)))
            return;

        try
        {
            using AndroidJavaObject unityActivity = GetCurrentActivity();

            if (floorCents == null)
            {
                androidFsnAd.Call("LoadNativeAd", unityActivity, (int)_nativeType, (int)_nativeRatio);
            }
            else
            {
                try
                {
                    androidFsnAd.Call("LoadNativeAd", unityActivity, (int)_nativeType, (int)_nativeRatio,
                        floorCents);
                }
                catch (Exception e)
                {
                    // fsnlib.aar cũ chưa có overload có floor -> vẫn load, chỉ là không có sàn.
#if ENABLE_DEBUG_LOG
                    Debug.LogError($"[FsnAd] LoadNativeAd có floor không gọi được (aar cũ?) -> load KHÔNG floor. {e.Message}");
#endif
                    androidFsnAd.Call("LoadNativeAd", unityActivity, (int)_nativeType, (int)_nativeRatio);
                }
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Load] Android LoadNativeAd called. rusd={floorCents ?? "none"}, {GetDebugSummary()}");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] LoadNativeAd({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
        }
#elif UNITY_IOS
        if (_destroyed) return;

        try
        {
            if (floorCents == null) FsnAdIOS_LoadNativeAd(_iosRequestId, (int)_nativeRatio);
            else FsnAdIOS_LoadNativeAdWithFloor(_iosRequestId, (int)_nativeRatio, floorCents);

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Load] iOS LoadNativeAd called. RequestId={_iosRequestId}, rusd={floorCents ?? "none"}, {GetDebugSummary()}");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS LoadNativeAd({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif

            // Không gọi được xuống native -> báo load fail để FsnAdManager nhả cờ isLoading.
            int requestId = _iosRequestId;
            string message = e.Message;
            ExecuteOnMainThread(() =>
            {
                if (TryGetIOSCallbacks(requestId, out var callbacks))
                {
                    callbacks.OnLoadingCompleted?.Invoke(-1, message);
                    RemoveIOSCallbacks(requestId);
                }
            });
        }
#endif
    }

    /// <param name="closePositionWeights">
    /// closePositionRatio theo thứ tự TopRight, TopLeft, BottomLeft, BottomRight.
    /// Android dùng để chọn lại vị trí Close theo tỉ lệ nếu Close đè lên OpenStore/Skip.
    /// </param>
    public void ShowNativeAd(Action<string> onAdCompleted, int overlayPositionOpenStoreValue = 0,
        int overlayPositionCloseValue = 0, int[] closePositionWeights = null)
    {
#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(ShowNativeAd), true, () => onAdCompleted?.Invoke("")))
            return;

        try
        {
            using AndroidJavaObject unityActivity = GetCurrentActivity();
            _nativeCompletedListenerProxy = new AndroidOnFsnNativeCompletedListener(onAdCompleted);

            bool hasCloseWeights = closePositionWeights != null && closePositionWeights.Length == 4;

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Layout] Android ShowNativeAd requested. {GetDebugSummary()}, OpenStorePos={overlayPositionOpenStoreValue}({GetPositionDebugName(overlayPositionOpenStoreValue)}), ClosePos={overlayPositionCloseValue}({GetPositionDebugName(overlayPositionCloseValue)}), CloseWeights=[{(hasCloseWeights ? string.Join(",", closePositionWeights) : "none")}]");
#endif

            if (hasCloseWeights)
            {
                androidFsnAd.Call("ShowNativeAd", unityActivity, (int)_nativeType, _styleIndex,
                    overlayPositionOpenStoreValue, overlayPositionCloseValue, closePositionWeights,
                    _nativeCompletedListenerProxy);
            }
            else
            {
                androidFsnAd.Call("ShowNativeAd", unityActivity, (int)_nativeType, _styleIndex,
                    overlayPositionOpenStoreValue, overlayPositionCloseValue, _nativeCompletedListenerProxy);
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Layout] Android ShowNativeAd bridge called. {GetDebugSummary()}");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] ShowNativeAd({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
            ExecuteOnMainThread(() => onAdCompleted?.Invoke(e.Message));
        }
#elif UNITY_IOS
        if (_destroyed)
        {
            // Show trên instance đã hủy vẫn phải trả callback đóng, không thì flow treo với overlay đen.
#if ENABLE_DEBUG_LOG
            Debug.LogWarning("[FsnAd] ShowNativeAd() ignored - instance already destroyed");
#endif
            ExecuteOnMainThread(() => onAdCompleted?.Invoke("destroyed"));
            return;
        }

        EnsureIOSCallbacksRegistered();

        var callbacks = GetOrCreateIOSCallbacks(_iosRequestId);
        callbacks.OnAdCompleted = onAdCompleted;

        callbacks.NativeType = _nativeType;
        callbacks.NativeRatio = _nativeRatio;
        callbacks.StyleIndex = _styleIndex;
        callbacks.AdUnitId = _adUnitId;

#if ENABLE_DEBUG_LOG
        Debug.Log($"[FsnAd][Layout] iOS ShowNativeAd requested. RequestId={_iosRequestId}, {GetDebugSummary()}, OpenStorePos={overlayPositionOpenStoreValue}({GetPositionDebugName(overlayPositionOpenStoreValue)}), ClosePos={overlayPositionCloseValue}({GetPositionDebugName(overlayPositionCloseValue)}), IsReady={IsNativeAdReady()}");
#endif

        try
        {
            FsnAdIOS_ShowNativeAd(_iosRequestId,_styleIndex,overlayPositionOpenStoreValue,overlayPositionCloseValue);
#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Layout] iOS ShowNativeAd bridge called. RequestId={_iosRequestId}, Style={_styleIndex}({DebugStyleName})");
#endif
        }
        catch (Exception e)
        {
            // Giống nhánh Android: show lỗi vẫn phải trả callback đóng, không thì flow treo với overlay đen.
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS ShowNativeAd({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
            RemoveIOSCallbacks(_iosRequestId);
            string message = e.Message;
            ExecuteOnMainThread(() => onAdCompleted?.Invoke(message));
        }
#endif
    }

    public void HideNativeAd()
    {
#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(HideNativeAd), true))
            return;

        try
        {
            using AndroidJavaObject unityActivity = GetCurrentActivity();
            androidFsnAd.Call("HideNativeAd", (int)_nativeType);
#if ENABLE_DEBUG_LOG
            Debug.Log("[FsnAd] HideNativeAd() Android called.");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] HideNativeAd({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
        }
#elif UNITY_IOS
        if (_destroyed) return;

        try
        {
            FsnAdIOS_HideNativeAd(_iosRequestId);
#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][Hide] iOS HideNativeAd called. RequestId={_iosRequestId}, {GetDebugSummary()}");
#endif
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS HideNativeAd({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
        }
#endif
    }

    public bool IsNativeAdReady()
    {
#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(IsNativeAdReady)))
            return false;

        try
        {
            return androidFsnAd.Call<bool>("IsNativeAdReady", (int)_nativeType);
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] IsNativeAdReady({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
            return false;
        }
#elif UNITY_IOS
        if (_destroyed) return false;

        try
        {
            return FsnAdIOS_IsNativeAdReady(_iosRequestId);
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS IsNativeAdReady({_nativeType}) exception: {e.Message}\n{e.StackTrace}");
#endif
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Revenue (USD / impression) của ad ĐÃ LOAD XONG, đọc từ response extras "ad_value_string" của
    /// AdMob. 0 nếu chưa có ad / không đọc được (tài khoản chưa bật ad value, khác USD, native cũ...).
    /// </summary>
    public double GetLoadedRevenue()
    {
        double revenue = 0d;

#if UNITY_ANDROID
        if (!IsAndroidReady(nameof(GetLoadedRevenue)))
            return 0d;

        try
        {
            revenue = androidFsnAd.Call<double>("GetLoadedRevenue", (int)_nativeType);
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] GetLoadedRevenue({_nativeType}) exception (aar cũ?): {e.Message}");
#endif
            return 0d;
        }
#elif UNITY_IOS
        if (_destroyed) return 0d;

        try
        {
            revenue = FsnAdIOS_GetLoadedRevenue(_iosRequestId);
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogError($"[FsnAd] iOS GetLoadedRevenue exception: {e.Message}");
#endif
            return 0d;
        }
#endif

        return double.IsNaN(revenue) || double.IsInfinity(revenue) || revenue < 0d ? 0d : revenue;
    }

    /// <summary>
    /// Giải phóng ad native + object bridge khi FsnAdManager bỏ instance này (ad chưa show, load lỗi,
    /// bị thay, hoặc đã show và đóng xong). Mỗi FsnAd chỉ dùng cho MỘT lượt load/show nên hủy xong là
    /// xong. Ad đang hiển thị thì native bỏ qua, để luồng đóng tự dọn. Gọi nhiều lần không sao.
    /// </summary>
    public void Destroy()
    {
        if (_destroyed) return;
        _destroyed = true;

#if UNITY_ANDROID
        if (Application.platform != RuntimePlatform.Android || androidFsnAd == null)
            return;

        try
        {
            androidFsnAd.Call("DestroyNativeAd");
        }
        catch (Exception e)
        {
            // fsnlib.aar cũ chưa có DestroyNativeAd -> vẫn Dispose phía C#.
#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"[FsnAd] DestroyNativeAd không gọi được (aar cũ?): {e.Message}");
#endif
        }

        try
        {
            androidFsnAd.Dispose();
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"[FsnAd] Dispose AndroidJavaObject exception: {e.Message}");
#endif
        }

        // Giữ nguyên các proxy listener: Java có thể vẫn gọi về (ad đang hiện báo paid/đóng).
        androidFsnAd = null;
#elif UNITY_IOS
        try
        {
            FsnAdIOS_DestroyNative(_iosRequestId);
        }
        catch (Exception e)
        {
#if ENABLE_DEBUG_LOG
            Debug.LogWarning($"[FsnAd] iOS DestroyNative exception: {e.Message}");
#endif
        }

        // Còn chờ callback đóng (đang show) thì giữ lại để flow vẫn kết thúc được.
        if (TryGetIOSCallbacks(_iosRequestId, out var callbacks) && callbacks.OnAdCompleted == null)
        {
            RemoveIOSCallbacks(_iosRequestId);
        }
#endif
    }

    // =========================
    // iOS MonoPInvokeCallbacks
    // =========================

#if UNITY_IOS
    [MonoPInvokeCallback(typeof(IOSLoadingCompletedDelegate))]
    private static void IOS_OnLoadingCompleted(int requestId, int errorCode, string errorMessage)
    {
        ExecuteOnMainThread(() =>
        {
            if (!TryGetIOSCallbacks(requestId, out var callbacks))
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"[FsnAd] iOS OnLoadingCompleted ignored. Unknown RequestId={requestId}, ErrorCode={errorCode}, Msg={errorMessage}");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][iOSCallback] OnLoadingCompleted. RequestId={requestId}, ErrorCode={errorCode}, Msg={errorMessage}, {callbacks.DebugSummary}");
#endif

            callbacks.OnLoadingCompleted?.Invoke(errorCode, errorMessage);

            // Nếu load fail thì instance này không còn dùng nữa.
            // Load success thì giữ callback lại để còn nhận paid/completed.
            if (errorCode != 0 || !string.IsNullOrEmpty(errorMessage))
            {
                RemoveIOSCallbacks(requestId);
            }
        });
    }

    [MonoPInvokeCallback(typeof(IOSLoadingStartedDelegate))]
    private static void IOS_OnLoadingStarted(int requestId)
    {
        ExecuteOnMainThread(() =>
        {
            if (!TryGetIOSCallbacks(requestId, out var callbacks))
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"[FsnAd] iOS OnLoadingStarted ignored. Unknown RequestId={requestId}");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][iOSCallback] OnLoadingStarted. RequestId={requestId}, {callbacks.DebugSummary}");
#endif

            callbacks.OnLoadingStarted?.Invoke();
        });
    }

    [MonoPInvokeCallback(typeof(IOSAdPaidDelegate))]
    private static void IOS_OnAdPaid(int requestId,string adSource,string adUnitId,long valueMicros,string currencyCode)
    {
        ExecuteOnMainThread(() =>
        {
            if (!TryGetIOSCallbacks(requestId, out var callbacks))
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"[FsnAd] iOS OnAdPaid ignored. Unknown RequestId={requestId}");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][iOSCallback] OnAdPaid. RequestId={requestId}, Source={adSource}, Unit={adUnitId}, ValueMicros={valueMicros}, Currency={currencyCode}, {callbacks.DebugSummary}");
#endif

            callbacks.OnAdPaid?.Invoke(adSource, adUnitId, valueMicros, currencyCode);
        });
    }

    [MonoPInvokeCallback(typeof(IOSAdCompletedDelegate))]
    private static void IOS_OnAdCompleted(int requestId, string errorMessage)
    {
        ExecuteOnMainThread(() =>
        {
            if (!TryGetIOSCallbacks(requestId, out var callbacks))
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"[FsnAd] iOS OnAdCompleted ignored. Unknown RequestId={requestId}, Msg={errorMessage}");
#endif
                return;
            }

#if ENABLE_DEBUG_LOG
            Debug.Log($"[FsnAd][iOSCallback] OnAdCompleted. RequestId={requestId}, Msg={errorMessage}, {callbacks.DebugSummary}");
#endif

            callbacks.OnAdCompleted?.Invoke(errorMessage);

            // Show xong/đóng xong thì dọn callback của request này.
            RemoveIOSCallbacks(requestId);
        });
    }
#endif

    // =========================
    // Android Java proxies
    // =========================

#if UNITY_ANDROID
    private sealed class AndroidOnFsnNativeLoadListener : AndroidJavaProxy
    {
        private readonly Action<int, string> onLoadingCompleted;
        private readonly Action onLoadingStarted;
        private readonly Action<string, string, long, string> onAdPaid;

        public AndroidOnFsnNativeLoadListener(
            Action<int, string> onLoadingCompleted,
            Action onLoadingStarted,
            Action<string, string, long, string> onAdPaid)
            : base("falcon.modules.core.thirdparty.mediation.fsnads.OnFsnNativeLoadListener")
        {
            this.onLoadingCompleted = onLoadingCompleted;
            this.onLoadingStarted = onLoadingStarted;
            this.onAdPaid = onAdPaid;
        }

        public void OnLoadingCompleted(int errorCode, string errorMessage)
        {
            ExecuteOnMainThread(() => { onLoadingCompleted?.Invoke(errorCode, errorMessage); });
        }

        public void OnLoadingStarted()
        {
            ExecuteOnMainThread(() => { onLoadingStarted?.Invoke(); });
        }

        public void OnAdPaid(string adSource, string adUnitId, long valueMicros, string currencyCode)
        {
            ExecuteOnMainThread(() => { onAdPaid?.Invoke(adSource, adUnitId, valueMicros, currencyCode); });
        }
    }

    private sealed class AndroidOnFsnNativeCompletedListener : AndroidJavaProxy
    {
        private readonly Action<string> onAdCompleted;

        public AndroidOnFsnNativeCompletedListener(Action<string> onAdCompleted)
            : base("falcon.modules.core.thirdparty.mediation.fsnads.OnFsnNativeCompletedListener")
        {
            this.onAdCompleted = onAdCompleted;
        }

        public void OnAdCompleted(string errorMessage)
        {
            ExecuteOnMainThread(() => { onAdCompleted?.Invoke(errorMessage); });
        }
    }
#endif
}

public class FsnMainThreadDispatcher : MonoBehaviour
{
    private void Awake()
    {
#if ENABLE_DEBUG_LOG
        Debug.Log("[FsnMainThreadDispatcher] Awake");
#endif
    }

    private void Update()
    {
        FsnAd.ProcessQueue();
    }

    private void OnDestroy()
    {
#if ENABLE_DEBUG_LOG
        Debug.Log("[FsnMainThreadDispatcher] OnDestroy");
#endif
    }
}