using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Hợp đồng đợt 3 S2 phần 2: luật canh đơ, nhịp làm mới APS (B5 §5.3), bản chụp + ghép dòng khẩn, file khẩn, dấu lỗi
    /// chết. Phần luồng / HTTP / API Unity không chạy được ngoài máy thật — chỉ phần thuần ở đây.
    /// </summary>
    public class EmergencyTests
    {
        // ---- Canh đơ ----

        [Test]
        public void FreezeWatch_FiresOnceAtFiveSeconds_PerStall()
        {
            var watch = new FreezeWatchState();
            Assert.IsFalse(watch.Observe(0, 10, false));
            Assert.IsFalse(watch.Observe(4_999, 10, false), "chưa tới mốc");
            Assert.IsTrue(watch.Observe(5_000, 10, false));
            Assert.IsFalse(watch.Observe(40_000, 10, false), "một lần mỗi đợt đứng");

            Assert.IsFalse(watch.Observe(40_500, 11, false), "khung chạy lại = hết đợt");
            Assert.IsFalse(watch.Fired);
            Assert.IsTrue(watch.Observe(45_500, 11, false), "đợt đứng mới bắn lại");
        }

        [Test]
        public void FreezeWatch_Suspended_ResetsBaseline()
        {
            var watch = new FreezeWatchState();
            watch.Observe(0, 1, false);
            Assert.IsFalse(watch.Observe(30_000, 1, true), "nằm nền / QC / mất focus: không đếm");
            Assert.IsFalse(watch.Observe(30_500, 1, false), "quay lại: không tính phần vắng mặt");
            Assert.IsFalse(watch.Observe(34_999, 1, false));
            Assert.IsTrue(watch.Observe(35_000, 1, false), "5 giây tính từ lần quan sát cuối lúc còn tạm dừng");
        }

        // ---- Làm mới APS ----

        [Test]
        public void ApsRefresh_EveryTenSeconds_OnlyWhenEligible()
        {
            var aps = new ApsRefreshState();
            Assert.IsFalse(aps.Due(0, false));
            Assert.IsTrue(aps.Due(0, true));
            aps.Record(0, 2.2);
            Assert.IsFalse(aps.Due(9_999, true));
            Assert.IsTrue(aps.Due(10_000, true));
            Assert.AreEqual(3, aps.MaxMs, "làm tròn lên");
            Assert.IsNull(aps.Disabled, "không ngừng ⇒ vắng, không phải false");
        }

        [Test]
        public void ApsRefresh_OverSixteenMs_StopsForTheSession()
        {
            var aps = new ApsRefreshState();
            aps.Record(0, 16.0);
            Assert.IsNull(aps.Disabled, "đúng 16 ms chưa vượt");
            aps.Record(10_000, 16.4);
            Assert.AreEqual(true, aps.Disabled);
            Assert.AreEqual(17, aps.MaxMs);
            Assert.IsFalse(aps.Due(100_000, true), "ngừng tới hết phiên");
        }

        [Test]
        public void ApsRefresh_NeverRan_MaxMsAbsent()
        {
            Assert.IsNull(new ApsRefreshState().MaxMs);
        }

        [Test]
        public void ParamsRefresh_AdoptsFilledParams_KeepsPushWhenNothingFilled()
        {
            var state = new LevelTurnParamsState();
            state.Push(new LevelHeartBeatParamV2 { score = 5 }, "t1", 1_000);

            var nothing = state.Refresh("t1", new ITurnParamsFiller[] { new Filler(false, 0) }, 2_000);
            Assert.AreEqual(1_000, nothing.paramsUpdatedAt, "không nguồn nào điền: mốc cũ");

            var filled = state.Refresh("t1", new ITurnParamsFiller[] { new Filler(true, 9) }, 3_000);
            Assert.AreEqual(9, filled.score);
            Assert.AreEqual(3_000, filled.paramsUpdatedAt);

            var later = state.Build("t1", null, 4_000);
            Assert.AreEqual(9, later.score, "bản làm mới thành bản đẩy mới (bản chụp dựng từ đây)");
            Assert.AreEqual(3_000, later.paramsUpdatedAt);
        }

        private sealed class Filler : ITurnParamsFiller
        {
            private readonly bool _fills;
            private readonly int _score;

            public Filler(bool fills, int score)
            {
                _fills = fills;
                _score = score;
            }

            public bool FillTurnParams(LevelHeartBeatParamV2 param)
            {
                if (_fills) param.score = _score;
                return _fills;
            }
        }

        // ---- Bản chụp ----

        [Test]
        public void RebuildPolicy_ImmediateOnTurnChange_ThrottledOnContent_AgedAtFive()
        {
            Assert.IsTrue(SnapshotRebuildPolicy.Due(0, null, false, false, false), "chưa có bản nào");
            Assert.IsTrue(SnapshotRebuildPolicy.Due(10, 0, true, false, false), "lượt đổi: ngay");
            Assert.IsTrue(SnapshotRebuildPolicy.Due(10, 0, false, true, false), "vừa làm mới APS: ngay");
            Assert.IsFalse(SnapshotRebuildPolicy.Due(999, 0, false, false, true), "sổ đổi: không dày hơn 1 giây");
            Assert.IsTrue(SnapshotRebuildPolicy.Due(1_000, 0, false, false, true));
            Assert.IsFalse(SnapshotRebuildPolicy.Due(4_999, 0, false, false, false));
            Assert.IsTrue(SnapshotRebuildPolicy.Due(5_000, 0, false, false, false), "không gì đổi: vẫn dựng mỗi 5 giây");
        }

        [Test]
        public void LedgerRunningKey_FollowsBlockPriority()
        {
            var ledger = new TurnLedgerState();
            Assert.IsNull(ledger.RunningKey, "không có đoạn mở");
            ledger.Begin("t1", 1, 0);
            Assert.AreEqual("t_ready_sec", ledger.RunningKey);
            ledger.SetSpeed(true, 0);
            Assert.IsTrue(ledger.SpeedRunning);
            ledger.SetBusy(true, 0);
            Assert.AreEqual("t_busy_sec", ledger.RunningKey);
            ledger.SetPopup(PopupKind.Tutorial, 0);
            Assert.AreEqual("t_popup_tutorial_sec", ledger.RunningKey);
            Assert.IsFalse(ledger.SpeedRunning, "x2 chỉ cộng trong bận / làm được");
            ledger.AdShown(FAdWhen.Revive, null, 0);
            Assert.AreEqual("t_ad_sec", ledger.RunningKey);
            ledger.AdShown("level_win", null, 0);
            Assert.IsNull(ledger.RunningKey, "QC bị loại: đồng hồ lượt dừng");
            ledger.SetBackground(true, 0);
            Assert.AreEqual("t_background_sec", ledger.RunningKey);
        }

        [Test]
        public void Stamp_FreshUuidAndDate_DropsSequenceNumbers_SourceUntouched()
        {
            var source = new Dictionary<string, object>
            {
                ["uuid"] = "old", ["createdDateLocal"] = "2026-01-01", ["logTypeCreateId"] = 3, ["logTypeSendId"] = 4,
                ["level"] = 7
            };
            var row = EmergencyRows.Stamp(source, "new", "2026-11-23");
            Assert.AreEqual("new", row["uuid"]);
            Assert.AreEqual("2026-11-23", row["createdDateLocal"]);
            Assert.IsFalse(row.ContainsKey("logTypeCreateId"));
            Assert.IsFalse(row.ContainsKey("logTypeSendId"));
            Assert.AreEqual(7, row["level"]);
            Assert.AreEqual("old", source["uuid"], "bản chụp bất biến");
            Assert.AreEqual(3, source["logTypeCreateId"]);
        }

        [Test]
        public void AccrueLedger_AddsElapsedToRunningBlockTotalSpeedAndSinceLastAction()
        {
            var row = new Dictionary<string, object>
            {
                ["t_total_sec"] = 10.0, ["t_ready_sec"] = 6.0, ["t_busy_sec"] = 4.0, ["t_speed_on_sec"] = 1.0,
                ["sec_since_last_action"] = 2.0, ["t_ready_gap10_sec"] = 0.0
            };
            EmergencyRows.AccrueLedger(row, 5_250, "t_ready_sec", true);
            Assert.AreEqual(15.25, row["t_total_sec"]);
            Assert.AreEqual(11.25, row["t_ready_sec"]);
            Assert.AreEqual(4.0, row["t_busy_sec"]);
            Assert.AreEqual(6.25, row["t_speed_on_sec"]);
            Assert.AreEqual(7.25, row["sec_since_last_action"]);
            Assert.AreEqual(0.0, row["t_ready_gap10_sec"], "khoảng không thao tác không đoán");
        }

        [Test]
        public void AccrueLedger_ClockStopped_OrFieldAbsent_AddsNothing()
        {
            var row = new Dictionary<string, object> { ["t_total_sec"] = 1.0 };
            EmergencyRows.AccrueLedger(row, 5_000, null, false);
            Assert.AreEqual(1.0, row["t_total_sec"], "QC bị loại: không cộng");
            EmergencyRows.AccrueLedger(row, 5_000, "t_busy_sec", false);
            Assert.IsFalse(row.ContainsKey("t_busy_sec"), "khả năng chưa khai: không thêm field");
            Assert.AreEqual(6.0, row["t_total_sec"]);
        }

        private static EmergencySnapshot Snapshot(string turnId, Dictionary<string, object> heartbeat) => new(
            turnId, 1_000, heartbeat,
            new Dictionary<string, object> { ["kind"] = AppIssueKind.FreezeLong, ["playTurnId"] = turnId, ["uuid"] = "tpl" },
            "t_ready_sec", false, "level", "f_sdk_level_data", "f_sdk_app_issue", "pkg", "android", false);

        [Test]
        public void Compose_IssueAndHeartbeat_OncePerTurn()
        {
            var snapshot = Snapshot("t1", new Dictionary<string, object> { ["t_total_sec"] = 1.0, ["t_ready_sec"] = 1.0 });
            var claimed = false;
            bool Claim()
            {
                if (claimed) return false;
                return claimed = true;
            }

            var n = 0;
            string Uuid() => "u" + ++n;
            var rows = EmergencyRows.Compose(snapshot, "t1",
                new Dictionary<string, object> { ["isLoading"] = null, ["suppressedCount"] = 2 }, Claim, 3_000, "d", Uuid);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("f_sdk_app_issue", rows[0].@event);
            Assert.AreEqual("u1", rows[0].row["uuid"]);
            Assert.AreEqual(2, rows[0].row["suppressedCount"]);
            Assert.IsFalse(rows[0].row.ContainsKey("isLoading"), "field null = vắng");
            Assert.IsFalse(rows[0].row.ContainsKey("heartbeatSkipped"));
            Assert.AreEqual("f_sdk_level_data", rows[1].@event);
            Assert.AreEqual("u2", rows[1].row["uuid"], "mỗi dòng một uuid");
            Assert.AreEqual(3.0, rows[1].row["t_total_sec"], "cộng nốt 2 giây từ lúc dựng");

            var again = EmergencyRows.Compose(snapshot, "t1", null, Claim, 9_000, "d", Uuid);
            Assert.AreEqual(1, again.Count, "heartbeat kèm sự cố tối đa một lần mỗi lượt");
        }

        [Test]
        public void Compose_SnapshotBehindTheOpenTurn_HeartbeatSkipped_IssueCarriesCurrentTurn()
        {
            var rows = EmergencyRows.Compose(Snapshot("old", new Dictionary<string, object>()), "t2", null, () => true, 0,
                "d", () => "u");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(true, rows[0].row["heartbeatSkipped"]);
            Assert.AreEqual("t2", rows[0].row["playTurnId"], "lượt đúng lúc sự cố, không theo mẫu");

            var noTurn = EmergencyRows.Compose(Snapshot(null, null), null, null, () => true, 0, "d", () => "u");
            Assert.AreEqual(1, noTurn.Count);
            Assert.IsFalse(noTurn[0].row.ContainsKey("heartbeatSkipped"), "không có lượt mở: không phải 'bỏ lỡ'");
            Assert.IsFalse(noTurn[0].row.ContainsKey("playTurnId"));
        }

        // ---- Gói hiệu năng của bản chụp ----

        [Test]
        public void PerfWindow_Peek_DoesNotResetSinceLastCounters()
        {
            var window = new PerfWindowState();
            window.AddFrame(0.016f);
            window.AddFrame(0.2f); // khung rớt
            window.AddMemoryWarning();
            var peek = window.PeekPack();
            Assert.AreEqual(1, peek.FrameDropsSinceLast);
            var take = window.TakePack();
            Assert.AreEqual(1, take.FrameDropsSinceLast, "peek không lấy mất của log thường");
            Assert.AreEqual(1, take.MemWarningsSinceLast);
            Assert.AreEqual(0, window.TakePack().FrameDropsSinceLast);
        }

        // ---- File khẩn ----

        [Test]
        public void EmergencyFile_RoundTrip_AndCorruptFileDropped()
        {
            var dir = Path.Combine(Path.GetTempPath(), "falcon_emergency_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new EmergencyFileStore(dir);
                var path = store.Write(new List<DataWrapper>
                {
                    DataWrapper.Quiet("{\"uuid\":\"a\"}", 42, "f_sdk_app_issue", "pkg", "ios"),
                    DataWrapper.Quiet("{\"uuid\":\"b\"}", 42, "f_sdk_level_data", "pkg", "ios")
                });
                Assert.IsNotNull(path);
                File.WriteAllText(Path.Combine(dir, "emergency_broken.json"), "{not json");

                var all = store.ReadAll();
                Assert.AreEqual(1, all.Count, "file hỏng bị bỏ");
                Assert.IsFalse(File.Exists(Path.Combine(dir, "emergency_broken.json")));
                var wrappers = all[0].wrappers;
                Assert.AreEqual(2, wrappers.Count);
                Assert.AreEqual("{\"uuid\":\"a\"}", wrappers[0].data, "giữ nguyên uuid");
                Assert.AreEqual("f_sdk_level_data", wrappers[1].@event);
                Assert.AreEqual(42, wrappers[1].clientSendTime);
                Assert.AreEqual("ios", wrappers[1].platform);

                store.Delete(all[0].path);
                Assert.AreEqual(0, store.ReadAll().Count);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // ---- Lỗi chết ----

        [Test]
        public void Mark_FatalRecorded_IsAbnormalEvenInBackground()
        {
            var mark = new LifecycleMark { sessionUid = "s", backgrounded = true, fatalExceptionHash = "h" };
            Assert.AreEqual(PrevEndType.Abnormal, LifecycleMark.Decide(mark));
            mark.fatalExceptionHash = null;
            Assert.IsNull(LifecycleMark.Decide(mark));
            mark.clean = true;
            mark.fatalExceptionHash = "h";
            Assert.IsNull(LifecycleMark.Decide(mark), "thoát sạch thắng");
        }

        [Test]
        public void Fingerprint_DotNetStackOfFatal_TopFrameWithoutAtPrefix()
        {
            const string stack =
                "  at Game.Save.Write (System.String path) [0x00012] in <8f2c>:0 \n" +
                "  at System.Threading.ThreadHelper.ThreadStart () [0x00000] in <aa>:0 ";
            var (type, top, _) = ExceptionFingerprint.From("IOException", stack);
            Assert.AreEqual("IOException", type);
            Assert.AreEqual("Game.Save.Write", top);
        }
    }
}
