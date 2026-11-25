using System;
using UnityEngine;

namespace RealtimeEvents
{
    internal static class FirstOpen
    {
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern long RTE_ReadDocumentsCreationDate();
#endif

        internal static long? ReadInstallTimeOnWorker()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Installation metadata is optional, including failure to attach the worker to JNI.
            try { return PlatformThread.Run(ReadInstallTime); }
            catch (Exception) { return null; }
#else
            return PlatformThread.Run(ReadInstallTime);
#endif
        }

        internal static long? ReadInstallTime()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            try
            {
#if UNITY_IOS
                return RTE_ReadDocumentsCreationDate();
#else
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var package = manager.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0))
                    return package.Get<long>("firstInstallTime");
#endif
            }
            catch (Exception)
            {
#if UNITY_IOS
                // Managed interop failures must not invent a first-open time.
                // The native symbol must still be linked successfully in an iOS build.
                return -1;
#else
                return null;
#endif
            }
#else
            // There is no portable install timestamp. Never use an APK/bundle build or update time.
            return null;
#endif
        }
    }
}
