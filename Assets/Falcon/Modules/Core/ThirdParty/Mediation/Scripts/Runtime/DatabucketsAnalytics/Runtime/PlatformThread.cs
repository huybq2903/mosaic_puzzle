using System;

namespace RealtimeEvents
{
    internal static class PlatformThread
    {
        internal static T Run<T>(Func<T> read)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.AndroidJNI.AttachCurrentThread() != 0)
                throw new InvalidOperationException("Could not attach metadata worker to the Android VM.");
            try { return read(); }
            finally { UnityEngine.AndroidJNI.DetachCurrentThread(); }
#else
            return read();
#endif
        }
    }
}
