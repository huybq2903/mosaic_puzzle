/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Helpers.Devkit
{
    /// <summary>
    /// Đọc native Android bằng JNI từ C# (cùng cách <c>ReserveDeviceInfoRepository</c> — không thêm file Java). Service hệ
    /// thống lấy một lần; mỗi hàm bọc try/catch, lỗi hay API thấp hơn mức cần ⇒ null. Chỉ main thread.
    /// </summary>
    internal sealed class AndroidDeviceSignalSource : IDeviceSignalSource
    {
        private const string POST_NOTIFICATIONS = "android.permission.POST_NOTIFICATIONS";

        private readonly AndroidJavaObject _activity;
        private readonly int _sdk;
        private readonly AndroidJavaObject _power;
        private readonly AndroidJavaObject _audio;
        private readonly AndroidJavaObject _activityManager;
        private readonly AndroidJavaObject _notificationManager;

        public AndroidDeviceSignalSource()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                _sdk = version.GetStatic<int>("SDK_INT");
            _power = Service("power");
            _audio = Service("audio");
            _activityManager = Service("activity");
            _notificationManager = Service("notification");
        }

        private AndroidJavaObject Service(string name)
        {
            try
            {
                return _activity.Call<AndroidJavaObject>("getSystemService", name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public int? ThermalOrdinal()
        {
            if (_sdk < 29 || _power == null) return null;
            try
            {
                return DeviceSignalMaps.ThermalOrdinalFromAndroid(_power.Call<int>("getCurrentThermalStatus"));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool? LowPowerMode()
        {
            if (_power == null) return null;
            try
            {
                return _power.Call<bool>("isPowerSaveMode");
            }
            catch (Exception)
            {
                return null;
            }
        }

        public (string plug, float? tempC) Battery()
        {
            try
            {
                // Sticky broadcast: receiver null ⇒ chỉ lấy intent gần nhất, không đăng ký gì
                using var filter = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED");
                using var intent = _activity.Call<AndroidJavaObject>("registerReceiver", null, filter);
                if (intent == null) return (null, null);
                var plugged = intent.Call<int>("getIntExtra", "plugged", -1);
                var tenths = intent.Call<int>("getIntExtra", "temperature", int.MinValue);
                return (plugged < 0 ? null : DeviceSignalMaps.PlugFromAndroid(plugged),
                    tenths == int.MinValue ? null : DeviceSignalMaps.BatteryTempFromAndroid(tenths));
            }
            catch (Exception)
            {
                return (null, null);
            }
        }

        public AudioReading? Audio()
        {
            if (_audio == null) return null;
            try
            {
                using var audioClass = new AndroidJavaClass("android.media.AudioManager");
                var music = audioClass.GetStatic<int>("STREAM_MUSIC");
                var volume = DeviceSignalMaps.VolumeBucket(
                    _audio.Call<int>("getStreamVolume", music), _audio.Call<int>("getStreamMaxVolume", music));
                var ringer = DeviceSignalMaps.RingerFromAndroid(_audio.Call<int>("getRingerMode"),
                    audioClass.GetStatic<int>("RINGER_MODE_SILENT"), audioClass.GetStatic<int>("RINGER_MODE_VIBRATE"),
                    audioClass.GetStatic<int>("RINGER_MODE_NORMAL"));

                string output = null;
                if (_sdk >= 23)
                {
                    var devices = _audio.Call<AndroidJavaObject[]>("getDevices",
                        audioClass.GetStatic<int>("GET_DEVICES_OUTPUTS"));
                    var types = new List<int>();
                    if (devices != null)
                        foreach (var device in devices)
                            using (device)
                                types.Add(device.Call<int>("getType"));
                    output = DeviceSignalMaps.OutputFromAndroid(types);
                }

                return new AudioReading(volume, ringer, output);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public DisplayReading? Display()
        {
            try
            {
                float? fontScale = null;
                bool? bold = null;
                using (var resources = _activity.Call<AndroidJavaObject>("getResources"))
                using (var config = resources.Call<AndroidJavaObject>("getConfiguration"))
                {
                    var scale = config.Get<float>("fontScale");
                    if (scale > 0) fontScale = (float)Math.Round(scale, 2);
                    if (_sdk >= 31) bold = DeviceSignalMaps.BoldFromAndroid(config.Get<int>("fontWeightAdjustment"));
                }

                bool? reduceMotion = null;
                using (var resolver = _activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var global = new AndroidJavaClass("android.provider.Settings$Global"))
                    reduceMotion = global.CallStatic<float>("getFloat", resolver, "animator_duration_scale", 1f) == 0f;

                string storage = null;
                using (var environment = new AndroidJavaClass("android.os.Environment"))
                using (var dataDir = environment.CallStatic<AndroidJavaObject>("getDataDirectory"))
                using (var stat = new AndroidJavaObject("android.os.StatFs", dataDir.Call<string>("getPath")))
                    storage = DeviceSignalMaps.StorageBucket(stat.Call<long>("getAvailableBytes"));

                return new DisplayReading(fontScale, bold, reduceMotion, storage);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public long? MsSinceProcessStart()
        {
            if (_sdk < 24) return null;
            try
            {
                using var clock = new AndroidJavaClass("android.os.SystemClock");
                using var process = new AndroidJavaClass("android.os.Process");
                var ms = clock.CallStatic<long>("elapsedRealtime") - process.CallStatic<long>("getStartElapsedRealtime");
                return ms >= 0 ? ms : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public int? ProcessId()
        {
            try
            {
                using var process = new AndroidJavaClass("android.os.Process");
                return process.CallStatic<int>("myPid");
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool? SystemLowMemory()
        {
            if (_activityManager == null) return null;
            try
            {
                using var info = new AndroidJavaObject("android.app.ActivityManager$MemoryInfo");
                _activityManager.Call("getMemoryInfo", info);
                return info.Get<bool>("lowMemory");
            }
            catch (Exception)
            {
                return null;
            }
        }

        public PushPermissionReading? PushPermission()
        {
            try
            {
                if (_sdk >= 33)
                {
                    var granted = _activity.Call<int>("checkSelfPermission", POST_NOTIFICATIONS) == 0; // PERMISSION_GRANTED
                    var rationale = _activity.Call<bool>("shouldShowRequestPermissionRationale", POST_NOTIFICATIONS);
                    return new PushPermissionReading(_sdk, granted, rationale, null, null);
                }

                bool? enabled = _sdk >= 24 && _notificationManager != null
                    ? _notificationManager.Call<bool>("areNotificationsEnabled")
                    : null;
                return new PushPermissionReading(_sdk, null, null, enabled, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public int? IosAttStatus() => null;

        public List<ExitRecord> HistoricalExits(int max)
        {
            if (_sdk < 30 || _activityManager == null) return null;
            try
            {
                var package = _activity.Call<string>("getPackageName");
                using var list = _activityManager.Call<AndroidJavaObject>("getHistoricalProcessExitReasons", package, 0, max);
                var result = new List<ExitRecord>();
                if (list == null) return result;
                var count = list.Call<int>("size");
                for (var i = 0; i < count; i++)
                    using (var info = list.Call<AndroidJavaObject>("get", i))
                        result.Add(new ExitRecord(info.Call<int>("getPid"), info.Call<long>("getTimestamp"),
                            info.Call<int>("getReason"), info.Call<int>("getImportance")));
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public IosDiagnostic? LatestIosDiagnostic() => null;

        public bool InstallJavaUncaughtHandler(Action<string, string> onFatal)
        {
            if (onFatal == null) return false;
            try
            {
                using var thread = new AndroidJavaClass("java.lang.Thread");
                var previous = thread.CallStatic<AndroidJavaObject>("getDefaultUncaughtExceptionHandler");
                thread.CallStatic("setDefaultUncaughtExceptionHandler", new JavaUncaughtProxy(previous, onFatal));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Handler ngoại lệ Java không ai bắt. Ghi chép (callback) rồi <b>luôn</b> chuyển tiếp handler cũ trong
        /// <c>finally</c> — Crashlytics / handler giết app của hệ thống không được mất vì lỗi ở đây.
        /// </summary>
        [Preserve]
        private sealed class JavaUncaughtProxy : AndroidJavaProxy
        {
            private const int MAX_FRAMES = 20;

            private readonly AndroidJavaObject _previous;
            private readonly Action<string, string> _onFatal;

            [Preserve]
            public JavaUncaughtProxy(AndroidJavaObject previous, Action<string, string> onFatal)
                : base("java.lang.Thread$UncaughtExceptionHandler")
            {
                _previous = previous;
                _onFatal = onFatal;
            }

            [Preserve]
            // ReSharper disable once InconsistentNaming — tên phải khớp method Java
            public void uncaughtException(AndroidJavaObject thread, AndroidJavaObject throwable)
            {
                try
                {
                    string type;
                    using (var cls = throwable.Call<AndroidJavaObject>("getClass"))
                        type = cls.Call<string>("getSimpleName");
                    var frames = new StringBuilder();
                    var elements = throwable.Call<AndroidJavaObject[]>("getStackTrace");
                    if (elements != null)
                        for (var i = 0; i < elements.Length && i < MAX_FRAMES; i++)
                            using (elements[i])
                                frames.Append(elements[i].Call<string>("toString")).Append('\n');
                    _onFatal(type, frames.ToString());
                }
                catch (Exception)
                {
                    // đang chết — không được ném thêm
                }
                finally
                {
                    _previous?.Call("uncaughtException", thread, throwable);
                }
            }
        }
    }
}
#endif
