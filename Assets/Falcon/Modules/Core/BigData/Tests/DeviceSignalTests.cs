using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Hợp đồng đợt 3, các field "(2)" (native, Devkit): quy đổi số đọc thô, luật qua mốc âm thanh, bảng trạng thái quyền
    /// push, tự đọc quyền, lý do thoát của OS. JNI / .mm không chạy được ngoài máy — chỉ phần thuần ở đây.
    /// </summary>
    public class DeviceSignalTests
    {
        // ---- Quy đổi (Devkit) ----

        [Test]
        public void Thermal_AndroidSevenLevels_FoldIntoFour()
        {
            Assert.AreEqual("nominal", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromAndroid(0)));
            Assert.AreEqual("fair", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromAndroid(1)));
            Assert.AreEqual("fair", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromAndroid(2)));
            Assert.AreEqual("serious", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromAndroid(3)));
            Assert.AreEqual("critical", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromAndroid(4)));
            Assert.AreEqual("critical", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromAndroid(6)));
            Assert.IsNull(DeviceSignalMaps.ThermalOrdinalFromAndroid(7));
            Assert.AreEqual("serious", DeviceSignalMaps.ThermalName(DeviceSignalMaps.ThermalOrdinalFromIos(2)));
            Assert.IsNull(DeviceSignalMaps.ThermalName(null));
        }

        [Test]
        public void Battery_PlugAndTemperature()
        {
            Assert.AreEqual("none", DeviceSignalMaps.PlugFromAndroid(0));
            Assert.AreEqual("ac", DeviceSignalMaps.PlugFromAndroid(1));
            Assert.AreEqual("usb", DeviceSignalMaps.PlugFromAndroid(2));
            Assert.AreEqual("wireless", DeviceSignalMaps.PlugFromAndroid(4));
            Assert.AreEqual("ac", DeviceSignalMaps.PlugFromAndroid(8), "dock xếp ac");
            Assert.IsNull(DeviceSignalMaps.PlugFromAndroid(3));
            Assert.AreEqual(31.5f, DeviceSignalMaps.BatteryTempFromAndroid(315));
            Assert.IsNull(DeviceSignalMaps.BatteryTempFromAndroid(5000), "ngoài dải hợp lý");
        }

        [Test]
        public void Volume_ZeroMeansMutedExactly_NonZeroRoundsUp()
        {
            Assert.AreEqual(0, DeviceSignalMaps.VolumeBucket(0, 15));
            Assert.AreEqual(10, DeviceSignalMaps.VolumeBucket(1, 15), "1/15 ≈ 7 % ⇒ 10, không về 0");
            Assert.AreEqual(100, DeviceSignalMaps.VolumeBucket(15, 15));
            Assert.AreEqual(50, DeviceSignalMaps.VolumeBucket(0.5));
            Assert.AreEqual(60, DeviceSignalMaps.VolumeBucket(0.51));
            Assert.IsNull(DeviceSignalMaps.VolumeBucket(3, 0));
        }

        [Test]
        public void AudioOutput_Android_BluetoothOverWiredOverSpeaker()
        {
            Assert.AreEqual("speaker", DeviceSignalMaps.OutputFromAndroid(new[] { 2 }));
            Assert.AreEqual("wired", DeviceSignalMaps.OutputFromAndroid(new[] { 2, 3 }));
            Assert.AreEqual("bluetooth", DeviceSignalMaps.OutputFromAndroid(new[] { 2, 4, 8 }));
            Assert.AreEqual("bluetooth", DeviceSignalMaps.OutputFromAndroid(new[] { 26 }));
            Assert.IsNull(DeviceSignalMaps.OutputFromAndroid(new int[0]));
            Assert.AreEqual("bluetooth", DeviceSignalMaps.OutputFromIos("BluetoothA2DPOutput"));
            Assert.AreEqual("wired", DeviceSignalMaps.OutputFromIos("Headphones"));
            Assert.AreEqual("speaker", DeviceSignalMaps.OutputFromIos("Speaker"));
            Assert.IsNull(DeviceSignalMaps.OutputFromIos("AirPlay"), "ngoài ba giá trị ⇒ vắng");
        }

        [Test]
        public void Display_FontScaleBoldStorage()
        {
            Assert.AreEqual(1.0f, DeviceSignalMaps.FontScaleFromIos("UICTContentSizeCategoryL"));
            Assert.AreEqual(3.12f, DeviceSignalMaps.FontScaleFromIos("UICTContentSizeCategoryAccessibilityXXXL"));
            Assert.IsNull(DeviceSignalMaps.FontScaleFromIos("UICTContentSizeCategoryUnspecified"));
            Assert.AreEqual(true, DeviceSignalMaps.BoldFromAndroid(300));
            Assert.AreEqual(false, DeviceSignalMaps.BoldFromAndroid(0));
            Assert.IsNull(DeviceSignalMaps.BoldFromAndroid(int.MaxValue), "UNDEFINED");
            const long gib = 1024L * 1024 * 1024;
            Assert.AreEqual("lt_1gb", DeviceSignalMaps.StorageBucket(gib - 1));
            Assert.AreEqual("1_5gb", DeviceSignalMaps.StorageBucket(gib));
            Assert.AreEqual("5_20gb", DeviceSignalMaps.StorageBucket(5 * gib));
            Assert.AreEqual("gt_20gb", DeviceSignalMaps.StorageBucket(20 * gib));
        }

        // ---- Âm thanh qua mốc ----

        [Test]
        public void AudioChange_OnlyOnCrossings()
        {
            var state = new AudioChangeState();
            Assert.IsFalse(state.Record(new AudioReading(50, "normal", "speaker")), "lần đọc đầu không phải đổi");
            Assert.IsFalse(state.Record(new AudioReading(30, "normal", "speaker")), "đổi âm lượng không qua 0");
            Assert.IsTrue(state.Record(new AudioReading(0, "normal", "speaker")), "về 0");
            Assert.IsTrue(state.Record(new AudioReading(10, "normal", "speaker")), "rời 0");
            Assert.IsTrue(state.Record(new AudioReading(10, "vibrate", "speaker")));
            Assert.IsTrue(state.Record(new AudioReading(10, "vibrate", "bluetooth")));
            Assert.IsFalse(state.Record(new AudioReading(null, null, "bluetooth")), "không đọc được ≠ đổi");
        }

        // ---- Trạng thái quyền push ----

        private static PushPermissionReading Android33(bool granted, bool rationale) =>
            new(33, granted, rationale, null, null);

        [Test]
        public void Push_Android13Table()
        {
            Assert.AreEqual("granted", PushStatusResolver.Resolve(Android33(true, false), false, true));
            Assert.AreEqual("denied", PushStatusResolver.Resolve(Android33(false, true), false, null), "còn hỏi lại được");
            Assert.AreEqual("denied", PushStatusResolver.Resolve(Android33(false, false), true, false), "từ chối hẳn");
            Assert.AreEqual("not_determined", PushStatusResolver.Resolve(Android33(false, false), false, true), "cài mới");
            Assert.IsNull(PushStatusResolver.Resolve(Android33(false, false), false, false), "cập nhật từ bản cũ ⇒ vắng");
            Assert.IsNull(PushStatusResolver.Resolve(Android33(false, false), false, null), "chưa biết ⇒ vắng");
        }

        [Test]
        public void Push_AndroidBelow13_AndIos()
        {
            Assert.AreEqual("denied", PushStatusResolver.Resolve(new PushPermissionReading(30, null, null, false, null), false, null));
            Assert.AreEqual("granted", PushStatusResolver.Resolve(new PushPermissionReading(26, null, null, true, null), false, null));
            Assert.IsNull(PushStatusResolver.Resolve(new PushPermissionReading(23, null, null, null, null), false, null));
            Assert.AreEqual("not_determined", PushStatusResolver.Resolve(new PushPermissionReading(null, null, null, null, 0), false, null));
            Assert.AreEqual("provisional", PushStatusResolver.Resolve(new PushPermissionReading(null, null, null, null, 3), false, null));
            Assert.AreEqual("authorized", PushStatusResolver.Att(3));
            Assert.AreEqual("restricted", PushStatusResolver.Att(1));
        }

        [Test]
        public void AutoRead_ComparesWithLastNative_AutoPromptOnlyWhenLeavingNotDetermined()
        {
            Assert.AreEqual((true, false), PermissionAutoRead.Decide(null, "not_determined", true, null), "lần đọc đầu");
            Assert.AreEqual((false, false), PermissionAutoRead.Decide("not_determined", "not_determined", true, null));
            Assert.AreEqual((true, true), PermissionAutoRead.Decide("not_determined", "granted", true, null));
            Assert.AreEqual((true, false), PermissionAutoRead.Decide("not_determined", "granted", true, 3_000),
                "game vừa tự báo dòng system ⇒ không bắn thêm");
            Assert.AreEqual((true, true), PermissionAutoRead.Decide("not_determined", "denied", true, 60_000));
            Assert.AreEqual((true, false), PermissionAutoRead.Decide("not_determined", "provisional", true, null),
                "provisional có được không qua popup");
            Assert.AreEqual((true, false), PermissionAutoRead.Decide("not_determined", "denied", false, null),
                "Android: hàm bọc bắn, SDK không tự bắn");
            Assert.AreEqual((true, false), PermissionAutoRead.Decide("granted", "denied", true, null), "tắt trong Cài đặt");
            Assert.AreEqual((false, false), PermissionAutoRead.Decide("granted", null, true, null), "không đọc được");
        }

        // ---- prev_exit đợt 2 ----

        private static LifecycleMark Mark(bool backgrounded, long backgroundAt = 0, int pid = 0) => new()
        {
            sessionUid = "s", lastAliveAt = 1_000_000, backgrounded = backgrounded, backgroundAt = backgroundAt, pid = pid
        };

        [Test]
        public void ExitReason_MapsAndSplitsLowMemoryByImportance()
        {
            Assert.AreEqual("crash", PrevExitDecision.Reason(new ExitRecord(1, 0, 4, 100)));
            Assert.AreEqual("crash_native", PrevExitDecision.Reason(new ExitRecord(1, 0, 5, 100)));
            Assert.AreEqual("anr", PrevExitDecision.Reason(new ExitRecord(1, 0, 6, 100)));
            Assert.AreEqual("low_memory_foreground", PrevExitDecision.Reason(new ExitRecord(1, 0, 3, 100)));
            Assert.AreEqual("low_memory_background", PrevExitDecision.Reason(new ExitRecord(1, 0, 3, 400)));
            Assert.AreEqual("other", PrevExitDecision.Reason(new ExitRecord(1, 0, 10, 100)));
        }

        [Test]
        public void ExitMatch_ByPid_ElseByTime_SkipsReported()
        {
            var records = new List<ExitRecord>
            {
                new(7, 2_000_000, 4, 100), new(9, 1_500_000, 6, 100), new(7, 900_000, 3, 400)
            };
            Assert.AreEqual(9, PrevExitDecision.Match(records, Mark(false, pid: 9), 0)?.Pid);
            Assert.IsNull(PrevExitDecision.Match(records, Mark(false, pid: 9), 1_500_000), "mục đã báo");
            Assert.AreEqual(2_000_000, PrevExitDecision.Match(records, Mark(false), 0)?.TimestampMs,
                "dấu cũ không có pid: mục mới nhất sau lần ghi dấu cuối");
            Assert.IsNull(PrevExitDecision.Match(records, Mark(false, pid: 42), 0));
        }

        [Test]
        public void EndType_AbnormalByMark_BackgroundKilledByReason()
        {
            Assert.AreEqual(PrevEndType.Abnormal, PrevExitDecision.EndType(Mark(false), "other", null), "dấu thắng");
            Assert.AreEqual(PrevEndType.BackgroundThenKilled, PrevExitDecision.EndType(Mark(true), "crash", 0));
            Assert.AreEqual(PrevEndType.BackgroundThenKilled, PrevExitDecision.EndType(Mark(true), "hang", 0));
            Assert.IsNull(PrevExitDecision.EndType(Mark(true), "other", 0));
            Assert.IsNull(PrevExitDecision.EndType(Mark(true), null, null));

            var bg = Mark(true, backgroundAt: 1_000_000);
            Assert.AreEqual(PrevEndType.BackgroundThenKilled,
                PrevExitDecision.EndType(bg, "low_memory_background", 1_000_000 + 4 * 60_000));
            Assert.IsNull(PrevExitDecision.EndType(bg, "low_memory_background", 1_000_000 + 6 * 60_000),
                "bị dọn sau 5 phút nằm nền: bình thường");

            var clean = Mark(true);
            clean.clean = true;
            Assert.IsNull(PrevExitDecision.EndType(clean, "crash", 0), "thoát sạch");
        }

        [Test]
        public void IosDiagnostic_MatchedAfterLastAlive_ReportedOnce()
        {
            var mark = Mark(true);
            Assert.AreEqual("hang", PrevExitDecision.IosReason(new IosDiagnostic("hang", 1_010_000), mark, 0));
            Assert.IsNull(PrevExitDecision.IosReason(new IosDiagnostic("crash", 500_000), mark, 0), "của phiên cũ hơn");
            Assert.IsNull(PrevExitDecision.IosReason(new IosDiagnostic("crash", 1_010_000), mark, 1_010_000), "đã báo");
            Assert.IsNull(PrevExitDecision.IosReason(null, mark, 0));
        }

        [Test]
        public void Fingerprint_JavaFrames_SkipPlatformPrefixes()
        {
            const string frames = "java.util.ArrayList.get(ArrayList.java:437)\n" +
                                  "com.game.shop.Store.buy(Store.java:88)\n" +
                                  "android.os.Handler.dispatchMessage(Handler.java:106)\n";
            var (type, top, hash) = ExceptionFingerprint.From("IndexOutOfBoundsException", frames);
            Assert.AreEqual("IndexOutOfBoundsException", type);
            Assert.AreEqual("com.game.shop.Store.buy", top);
            Assert.AreEqual(16, hash.Length);
        }

        // ---- perf_screen ----

        [Test]
        public void Surface_SegmentCarriesThermalMaxAndLowPower()
        {
            var surface = new SurfaceState();
            surface.Enter("home");
            surface.NoteDevice(0, false);
            surface.NoteDevice(2, true);
            surface.NoteDevice(1, false);
            surface.Enter("level");
            var closed = surface.DrainClosed();
            Assert.AreEqual(1, closed.Count);
            Assert.AreEqual(2, closed[0].ThermalMax, "mức cao nhất trong đoạn");
            Assert.AreEqual(true, closed[0].LowPower, "có lúc bật");

            surface.Enter("shop");
            var next = surface.DrainClosed();
            Assert.AreEqual(1, next[0].ThermalMax, "đoạn mới bắt đầu từ trạng thái hiện tại");
            Assert.AreEqual(false, next[0].LowPower);
        }
    }
}
