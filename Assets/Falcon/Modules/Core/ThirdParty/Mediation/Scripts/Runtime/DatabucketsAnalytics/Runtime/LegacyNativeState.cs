using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RealtimeEvents
{
    internal static class LegacyNativeState
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern IntPtr RTE_ReadLegacyState();
        [DllImport("__Internal")] private static extern void RTE_FreeDeviceProperties(IntPtr value);
#endif
        internal static LegacyState Read()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PlatformThread.Run(() =>
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var prefs = activity.Call<AndroidJavaObject>("getSharedPreferences", "databuckets_static_fields", 0))
                using (var values = prefs.Call<AndroidJavaObject>("getAll"))
                {
                    string ReadValue(string key)
                    {
                        using (var value = values.Call<AndroidJavaObject>("get", key))
                        {
                            if (value == null) return null;
                            // getAll preserves SharedPreferences types. Do not turn a damaged
                            // numeric ID or string counter into apparently valid migration data.
                            var expectedType = key == "user_pseudo_id" ? "java.lang.String" :
                                key == "session_number" || key == "rt2_anchor_ms" ? "java.lang.Long" : "java.lang.Boolean";
                            using (var type = value.Call<AndroidJavaObject>("getClass"))
                                return type.Call<string>("getName") == expectedType ? value.Call<string>("toString") : null;
                        }
                    }
                    return ReadAndroid(ReadValue, FirstOpen.ReadInstallTime);
                }
            });
#elif UNITY_IOS && !UNITY_EDITOR
            return ReadIos(RTE_ReadLegacyState, RTE_FreeDeviceProperties);
#else
            return new LegacyState();
#endif
        }

        internal static LegacyState ReadAndroid(Func<string, string> read, Func<long?> installTime)
        {
            var id = LegacyStateMigration.UserId(read("user_pseudo_id"));
            var number = LegacyStateMigration.Number(read("session_number"));
            var anchor = LegacyStateMigration.Timestamp(read("rt2_anchor_ms"));
            // 3.0.3 derives first_open from firstInstallTime; its WorkManager input is not a prefs key.
            // Require evidence of that SDK before consulting installation metadata.
            var installed = id != null || number > 0 || anchor.HasValue || read("first_open_fired_v2") == "true" || read("first_open_fired") == "true"
                ? installTime() : null;
            return new LegacyState
            {
                UserId = id, SessionNumber = number,
                FirstOpen = LegacyStateMigration.Timestamp(installed?.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? anchor
            };
        }

        internal static LegacyState ReadIos(Func<IntPtr> read, Action<IntPtr> free)
        {
            var pointer = read();
            if (pointer == IntPtr.Zero) throw new System.IO.IOException("Could not read legacy iOS state.");
            try
            {
                var data = LegacyStateMigration.Parse(Marshal.PtrToStringAnsi(pointer));
                return new LegacyState
                {
                    UserId = LegacyStateMigration.UserId(data["user_pseudo_id"]),
                    SessionNumber = LegacyStateMigration.Number(LegacyStateMigration.IntegerText(data["session_number"])),
                    FirstOpen = LegacyStateMigration.Timestamp(LegacyStateMigration.IntegerText(data["first_seen_ms"])) ??
                        LegacyStateMigration.Timestamp(LegacyStateMigration.IntegerText(data["rt2_anchor_ms"]))
                };
            }
            finally { free(pointer); }
        }
    }
}
