/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-27
 */

using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.DFPool
{
    /// <summary>
    /// Cầu JNI tới <c>com.falcon.dfpool.DFPoolBridge</c> (Android/DFPoolBridge.java). Callback native
    /// bắn trên Android main thread -> xếp hàng rồi <see cref="Drain"/> chạy chúng trên Unity main thread.
    /// Ngoài Android (Editor / iOS) mọi hàm là no-op.
    /// </summary>
    internal static class DFPoolNative
    {
        public const int FORMAT_INTERSTITIAL = 0;
        public const int FORMAT_REWARDED = 1;

        /// <summary>Cùng bộ số với DFPoolBridge.SLOT_*.</summary>
        public enum SlotState
        {
            None = 0,
            Pending = 1,
            Claimed = 2,
            HollowPending = 3,
            HollowClaimed = 4,
            Showing = 5
        }

        public static bool IsSupported => Application.platform == RuntimePlatform.Android;

#pragma warning disable 0067 // ngoài Android không bao giờ bắn
        /// <summary>handle, valueUsd, adSource, responseId (null nếu không có)</summary>
        public static event Action<int, double, string, string> AdLoaded;

        /// <summary>handle, errorCode, errorMessage, responseId (null nếu không có)</summary>
        public static event Action<int, int, string, string> AdFailed;

        /// <summary>slotId, handle, valueMicros, currency, precision, adSource</summary>
        public static event Action<string, int, long, string, int, string> AdPaid;
#pragma warning restore 0067

        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static void Drain()
        {
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

#if UNITY_ANDROID
        private const string _BRIDGE = "com.falcon.dfpool.DFPoolBridge";
        private static AndroidJavaClass _bridge;
        private static Listener _listener;

        private static AndroidJavaClass Bridge => _bridge ??= new AndroidJavaClass(_BRIDGE);

        private sealed class Listener : AndroidJavaProxy
        {
            public Listener() : base("com.falcon.dfpool.DFPoolListener")
            {
            }

            // tên + chữ ký phải khớp DFPoolListener.java
            public void onAdLoaded(int handle, double valueUsd, string adSource, string responseId)
                => _queue.Enqueue(() => AdLoaded?.Invoke(handle, valueUsd, adSource, responseId));

            public void onAdFailed(int handle, int errorCode, string errorMessage, string responseId)
                => _queue.Enqueue(() => AdFailed?.Invoke(handle, errorCode, errorMessage, responseId));

            public void onAdPaid(string slotId, int handle, long valueMicros, string currency, int precision,
                string adSource)
                => _queue.Enqueue(() => AdPaid?.Invoke(slotId, handle, valueMicros, currency, precision, adSource));
        }
#endif

        public static bool Init(bool debugLog, bool adValueInMicros)
        {
            if (!IsSupported) return false;
#if UNITY_ANDROID
            try
            {
                _listener ??= new Listener();
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                Bridge.CallStatic("init", activity, _listener, debugLog, adValueInMicros);
                return true;
            }
            catch (Exception e)
            {
                // thiếu class Java (chưa build kèm thư mục Android/) -> module tự tắt
                Debug.LogException(e);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// <paramref name="uaChannel"/> / <paramref name="daysSinceInstall"/>: extras multi-platform mediation
        /// của Google, cùng giá trị tầng meta AdMob gửi (null = không gửi key đó).
        /// </summary>
        public static void LoadAd(int format, int handle, string adUnitId, double floorEcpmUsd,
            string uaChannel, string daysSinceInstall)
        {
#if UNITY_ANDROID
            //null không đi qua được params object[] của JNI -> chuỗi rỗng, Java coi rỗng = không gửi
            if (IsSupported)
                Call("loadAd", format, handle, adUnitId, floorEcpmUsd, uaChannel ?? string.Empty,
                    daysSinceInstall ?? string.Empty);
#endif
        }

        public static void DestroyAd(int handle)
        {
#if UNITY_ANDROID
            if (IsSupported) Call("destroyAd", handle);
#endif
        }

        public static bool BindSlot(string slotId, int handle)
        {
#if UNITY_ANDROID
            if (!IsSupported) return false;
            try
            {
                return Bridge.CallStatic<bool>("bindSlot", slotId, handle);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Đổi ad mượn của slot khi waterfall CHƯA nhận tier (Pending) — không cần flush. false = waterfall
        /// đã nhận tier rồi (hoặc slot đã trả ad), phải flush thay vào đó.
        /// </summary>
        public static bool RebindPending(string slotId, int handle)
        {
#if UNITY_ANDROID
            if (!IsSupported) return false;
            try
            {
                return Bridge.CallStatic<bool>("rebindPending", slotId, handle);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
#else
            return false;
#endif
        }

        public static void ReleaseSlot(string slotId)
        {
#if UNITY_ANDROID
            if (IsSupported) Call("releaseSlot", slotId);
#endif
        }

        public static void ClearSlot(string slotId)
        {
#if UNITY_ANDROID
            if (IsSupported) Call("clearSlot", slotId);
#endif
        }

        public static SlotState GetSlotState(string slotId)
        {
#if UNITY_ANDROID
            if (!IsSupported) return SlotState.None;
            try
            {
                return (SlotState)Bridge.CallStatic<int>("getSlotState", slotId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return SlotState.None;
            }
#else
            return SlotState.None;
#endif
        }

#if UNITY_ANDROID
        private static void Call(string method, params object[] args)
        {
            try
            {
                Bridge.CallStatic(method, args);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
#endif
    }
}
