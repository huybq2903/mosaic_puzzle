using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEngine;

namespace RealtimeEvents
{
    internal static class DeviceProperties
    {
        // Capture engine/UIKit values on Unity's thread. Android platform calls are deferred.
        internal static Func<IDictionary<string, object>> CreateReader()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var appId = Application.identifier;
            var version = Application.version;
            var model = SystemInfo.deviceModel;
            return () => PlatformThread.Run(() => Read(appId, version, model));
#else
            var values = Read();
            return () => new Dictionary<string, object>(values);
#endif
        }
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern IntPtr RTE_ReadDeviceProperties();
        [DllImport("__Internal")] private static extern void RTE_FreeDeviceProperties(IntPtr value);
#endif

        internal static IDictionary<string, object> Read() => Read(Application.identifier, Application.version, SystemInfo.deviceModel);

        private static IDictionary<string, object> Read(string appId, string version, string modelFallback)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            var values = Snapshot(appId, version, null, modelFallback,
#if UNITY_IOS
                "iOS",
#else
                "Android",
#endif
                null, null, null, null);
#if UNITY_IOS
            ReadIos(values, RTE_ReadDeviceProperties, RTE_FreeDeviceProperties);
#else
            values["device.mobile_brand_name"] = Brand(AndroidField("android.os.Build", "BRAND"));
            var model = AndroidField("android.os.Build", "MODEL");
            if (!string.IsNullOrEmpty(model))
            {
                values["device.mobile_model_name"] = model;
                values["device.mobile_os_hardware_model"] = model;
                values["device.mobile_marketing_name"] = model;
            }
            var release = AndroidField("android.os.Build$VERSION", "RELEASE");
            values["device.operating_system_version"] = release == null ? null : "Android " + release;
            values["device.user_default_language"] = ReadSafely(() =>
            {
                using (var type = new AndroidJavaClass("java.util.Locale"))
                using (var locale = type.CallStatic<AndroidJavaObject>("getDefault"))
                    return Language(locale.Call<string>("toLanguageTag"));
            });
            values["device.category"] = ReadSafely(() =>
            {
                // Resources.getSystem() is not configured for the current screen.
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var resources = context.Call<AndroidJavaObject>("getResources"))
                using (var configuration = resources.Call<AndroidJavaObject>("getConfiguration"))
                {
                    var width = configuration.Get<int>("smallestScreenWidthDp");
                    return width <= 0 ? null : width >= 600 ? "tablet" : "mobile";
                }
            });
            // Java's current timezone observes changes made in Settings while the app was backgrounded.
            values["device.time_zone_offset_seconds"] = ReadSafely(() =>
            {
                using (var type = new AndroidJavaClass("java.util.TimeZone"))
                using (var zone = type.CallStatic<AndroidJavaObject>("getDefault"))
                    return (object)(zone.Call<int>("getOffset", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000);
            });
#endif
            return values;
#else
            // Editor and desktop runs must not persist fabricated mobile metadata.
            return new Dictionary<string, object>();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static string AndroidField(string className, string field) => ReadSafely(() =>
        {
            using (var type = new AndroidJavaClass(className)) return type.GetStatic<string>(field);
        });
#endif

        internal static void ReadIos(IDictionary<string, object> values, Func<IntPtr> read, Action<IntPtr> free)
        {
            var pointer = IntPtr.Zero;
            try
            {
                pointer = read();
                if (pointer == IntPtr.Zero) return;
                using (var text = new StringReader(Marshal.PtrToStringAnsi(pointer)))
                using (var json = new JsonTextReader(text))
                {
                    // A host's JsonConvert.DefaultSettings must not change native metadata parsing.
                    var native = new JsonSerializer().Deserialize<Dictionary<string, object>>(json);
                    foreach (var property in native) values[property.Key] = property.Value;
                }
            }
            catch (Exception) { /* Retain available properties if native collection fails. */ }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    try { free(pointer); }
                    catch (Exception) { /* A cleanup interop failure must not escape into game code. */ }
                }
            }
        }

        internal static Dictionary<string, object> Snapshot(string appId, string appVersion, string brand,
            string model, string os, string osVersion, string category, string language, int? timeZoneOffsetSeconds)
        {
            return new Dictionary<string, object>
            {
                ["app_id"] = appId,
                ["app_version"] = appVersion,
                ["device.category"] = category,
                ["device.user_default_language"] = Language(language),
                ["device.mobile_brand_name"] = Brand(brand),
                ["device.mobile_marketing_name"] = model,
                ["device.mobile_model_name"] = model,
                ["device.mobile_os_hardware_model"] = model,
                ["device.operating_system"] = os,
                ["device.operating_system_version"] = osVersion == null ? null : os + " " + osVersion,
                ["device.time_zone_offset_seconds"] = timeZoneOffsetSeconds
            };
        }

        private static string Language(string language) => language?.Replace('_', '-').ToLowerInvariant();
        private static string Brand(string brand) => string.IsNullOrEmpty(brand) ? brand : char.ToUpperInvariant(brand[0]) + brand.Substring(1);

        internal static T ReadSafely<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception) { return null; }
        }
    }
}
