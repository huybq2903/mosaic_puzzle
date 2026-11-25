using System;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Bốn mốc KHOẢNG CÁCH trên funnel (yêu cầu owner 14/10): giờ CHƠI và số LEVEL, mỗi thước đo
    /// hai khoảng — "từ lúc VÀO PHỄU tới bước này" và "từ BƯỚC TRƯỚC tới bước này".
    /// <br/>Giờ chơi đo bằng <c>TotalPlayTime</c> (chỉ chạy lúc foreground), level đo bằng
    /// <c>MaxPassedLevel</c> — test giả lập bằng cách tự dịch hai con số, không dùng Thread.Sleep.
    /// </summary>
    public class FunnelPlayTimeTests
    {
        /// <summary>Đồng hồ đứng yên cho van lọc — [NoAutoCreate] theo luật FakeDataPool.</summary>
        [NoAutoCreate]
        private class FrozenTime : ITimeRepository
        {
            public long CurrentTimeMillis => 1_700_000_000_000L;
            public TimeSpan LocalDiffDelta => TimeSpan.Zero;
            public Task Init(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        /// <summary>Đồng hồ GIỜ CHƠI dịch bằng tay — mô phỏng "chỉ chạy khi foreground".</summary>
        [NoAutoCreate]
        private class FakePlayClock : IFPlayerSessionService
        {
            private double _playSec;

            public void Play(double seconds) => _playSec += seconds;
            public TimeSpan TotalPlayTime => TimeSpan.FromSeconds(_playSec);

            public DateTime SessionStartTime => DateTime.UnixEpoch;
            public long FirstLogInMillis => 0;
            public int ActiveDays => 1;
            public int SessionId => 1;
            public string SessionUid => "uid";
            public DateTime FirstLogInDateTimeLocal => DateTime.UnixEpoch;
            public DateTime FirstLoginDateLocal => DateTime.UnixEpoch;
            public int Retention => 0;
            public bool RetentionChanged => false;
            public TimeSpan TimeSinceLastPause => TimeSpan.Zero;
            public TimeSpan SessionTime => TimeSpan.FromSeconds(_playSec);
        }

        /// <summary>Tiến độ level dịch bằng tay — nguồn của hai mốc khoảng cách level.</summary>
        [NoAutoCreate]
        private class FakeProgress : IFPlayerGeneralRepository
        {
            public int MaxPassedLevel { get; set; }
            public string AccountID => "acc";
            public string InstallVersion => "1.0.0";
            public string AdvertisingID => null;
        }

        private const string FUNNEL = "event_my_feature";

        private static FunnelLogService Service(
            FakeDataPool disk, FakePlayClock clock, FunnelShape shape, FakeProgress progress = null)
        {
            var registry = new FunnelRegistry();
            registry.Register(FUNNEL, shape);
            return new FunnelLogService(disk, new FrozenTime(), registry, null, clock, progress);
        }

        private static AFunnelLog Step(string action, int priority, string cycleId)
        {
            return new AFunnelLog(new FunnelParam
                { funnelName = FUNNEL, action = action, priority = priority, cycleId = cycleId });
        }

        // ---- Bước đầu chu kỳ ----

        [Test]
        public void FirstStepOfCycle_IsTheEntryMark_AndHasNoPreviousStep()
        {
            var clock = new FakePlayClock();
            clock.Play(3600);                       // user đã chơi 1 tiếng trước khi phễu mở
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle);

            var first = Step("eligible", 10, "season_5");
            service.Check(first);

            Assert.AreEqual(0, first.playSecSinceEnter, "Bước đầu chính là mốc vào phễu → 0");
            Assert.IsNull(first.playSecSincePrevStep,
                "Chưa có bước trước thì VẮNG MẶT, không phải 0 — 0 là bịa 'hai bước sát nhau'");
        }

        [Test]
        public void EntryMark_IsNotTheJoinAction()
        {
            // Funnel mới phần lớn không có bước `join` (Card Collection: eligible/open, Endless
            // Treasure: chỉ open). Neo theo action là mấy event đó trắng số.
            var clock = new FakePlayClock();
            var disk = new FakeDataPool();
            var service = Service(disk, clock, FunnelShape.OncePerCycle);

            service.Check(Step("eligible", 10, "season_5"));
            clock.Play(120);
            var open = Step("open", 15, "season_5");
            service.Check(open);

            Assert.AreEqual(120, open.playSecSinceEnter, "Mốc vào phễu là bước ĐẦU TIÊN, không cần join");
        }

        // ---- Hai khoảng đo ----

        [Test]
        public void LaterSteps_MeasureFromEntry_AndFromPreviousStep()
        {
            var clock = new FakePlayClock();
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle);

            service.Check(Step("eligible", 10, "season_5"));
            clock.Play(60);
            service.Check(Step("open", 15, "season_5"));
            clock.Play(300);
            var milestone = Step("milestone", 20, "season_5");
            service.Check(milestone);

            Assert.AreEqual(360, milestone.playSecSinceEnter, "Từ mốc vào phễu: 60 + 300");
            Assert.AreEqual(300, milestone.playSecSincePrevStep, "Từ bước trước: 300");
        }

        [Test]
        public void BackgroundTime_DoesNotCount_BecauseClockOnlyRunsInForeground()
        {
            // Đồng hồ là TotalPlayTime: app ở background thì nó ĐÓNG BĂNG. Test mô phỏng bằng cách
            // không dịch đồng hồ — nếu ai đổi sang wall-clock, test này đổ.
            var clock = new FakePlayClock();
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle);

            service.Check(Step("eligible", 10, "season_5"));
            // ... user tắt app đi ngủ 8 tiếng: đồng hồ chơi KHÔNG chạy ...
            clock.Play(45);
            var open = Step("open", 15, "season_5");
            service.Check(open);

            Assert.AreEqual(45, open.playSecSinceEnter, "Chỉ tính thời gian thực sự chơi");
        }

        // ---- Ranh giới chu kỳ ----

        [Test]
        public void NewCycle_ResetsBothMarks()
        {
            var clock = new FakePlayClock();
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle);

            service.Check(Step("eligible", 10, "season_5"));
            clock.Play(100);
            service.Check(Step("open", 15, "season_5"));
            clock.Play(999_999);                    // user chơi cả tháng, rồi mùa mới mở

            var newCycle = Step("eligible", 10, "season_6");
            service.Check(newCycle);

            Assert.AreEqual(0, newCycle.playSecSinceEnter,
                "Mùa mới phải đo lại từ đầu — không thì mùa 2 đo từ mùa 1 (bug funnelDay cũ)");
            Assert.IsNull(newCycle.playSecSincePrevStep, "Bước đầu mùa mới không có bước trước");
        }

        [Test]
        public void OnceOrderedFunnel_HasNoCycle_SoMarksLiveForTheWholeInstall()
        {
            var clock = new FakePlayClock();
            var service = Service(new FakeDataPool(), clock, FunnelShape.OnceOrdered);

            service.Check(Step("scene_enter", 0, null));
            clock.Play(30);
            var step1 = Step("tutorial_start", 1, null);
            service.Check(step1);

            Assert.AreEqual(30, step1.playSecSinceEnter, "ftue không có chu kỳ → mốc giữ nguyên cả đời máy");
            Assert.AreEqual(30, step1.playSecSincePrevStep);
        }

        // ---- Bước bị van đánh trượt ----

        [Test]
        public void VetoedDuplicateStep_DoesNotMoveTheMarks()
        {
            var clock = new FakePlayClock();
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle);

            service.Check(Step("eligible", 10, "season_5"));
            clock.Play(50);
            service.Check(Step("open", 15, "season_5"));

            clock.Play(70);
            var duplicate = Step("open", 15, "season_5");   // trùng bước → van đánh trượt
            var verdict = service.Check(duplicate);
            Assert.IsFalse(verdict.valid, "Bước trùng trong chu kỳ phải bị đánh trượt");

            clock.Play(10);
            var next = Step("milestone", 20, "season_5");
            service.Check(next);

            Assert.AreEqual(80, next.playSecSincePrevStep,
                "Phải đo từ bước HỢP LỆ gần nhất (70 + 10), không đo từ bước bị đánh trượt");
            Assert.AreEqual(130, next.playSecSinceEnter);
        }

        // ---- Sổ lệch ----

        [Test]
        public void NegativeDelta_SendsAbsent_NotZero()
        {
            // Sổ mốc (pool) sống dai hơn sổ phiên: ca pool còn mốc cũ mà TotalPlayTime bị dựng lại
            // từ 0 (xoá data phiên, đổi máy đồng bộ...) cho ra delta ÂM.
            var disk = new FakeDataPool();
            var service = Service(disk, new FakePlayClock(), FunnelShape.OncePerCycle);
            disk.Save(FUNNEL + "_playMarkCycle", "season_5");
            disk.Save(FUNNEL + "_playMarkEnter", 5000d);
            disk.Save(FUNNEL + "_playMarkStep", 5000d);

            var step = Step("milestone", 20, "season_5");
            service.Check(step);

            Assert.IsNull(step.playSecSinceEnter, "Số âm = không biết, VẮNG MẶT chứ không gửi 0");
            Assert.IsNull(step.playSecSincePrevStep);
        }

        // ---- Khoảng cách LEVEL ----

        [Test]
        public void LevelDistance_CountsLevelsPassedBetweenSteps()
        {
            var clock = new FakePlayClock();
            var progress = new FakeProgress { MaxPassedLevel = 40 };
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle, progress);

            var first = Step("eligible", 10, "season_5");
            service.Check(first);
            Assert.AreEqual(0, first.levelsSinceEnter, "Bước đầu chu kỳ là mốc → 0");
            Assert.IsNull(first.levelsSincePrevStep, "Chưa có bước trước → VẮNG MẶT");

            progress.MaxPassedLevel = 52;
            var open = Step("open", 15, "season_5");
            service.Check(open);
            Assert.AreEqual(12, open.levelsSinceEnter);
            Assert.AreEqual(12, open.levelsSincePrevStep);

            progress.MaxPassedLevel = 55;
            var milestone = Step("milestone", 20, "season_5");
            service.Check(milestone);
            Assert.AreEqual(15, milestone.levelsSinceEnter, "Từ mốc vào phễu: 40 → 55");
            Assert.AreEqual(3, milestone.levelsSincePrevStep, "Từ bước trước: 52 → 55");
        }

        [Test]
        public void ReplayingTheSameLevel_IsZeroDistance_ButTimeStillMoves()
        {
            // Đây đúng là chỗ hai thước đo bù lỗ cho nhau: cày lại một màn thì level đứng yên,
            // giờ chơi vẫn chạy. Ngược lại mở app để đó thì giờ chơi chạy mà level đứng yên.
            var clock = new FakePlayClock();
            var progress = new FakeProgress { MaxPassedLevel = 40 };
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle, progress);

            service.Check(Step("eligible", 10, "season_5"));
            clock.Play(600);                        // thua lại màn 41 suốt 10 phút
            var open = Step("open", 15, "season_5");
            service.Check(open);

            Assert.AreEqual(0, open.levelsSinceEnter, "Không vượt được level nào → 0");
            Assert.AreEqual(600, open.playSecSinceEnter, "Giờ chơi vẫn chạy");
        }

        [Test]
        public void NewCycle_ResetsLevelMarksToo()
        {
            var clock = new FakePlayClock();
            var progress = new FakeProgress { MaxPassedLevel = 40 };
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle, progress);

            service.Check(Step("eligible", 10, "season_5"));
            progress.MaxPassedLevel = 90;

            var newCycle = Step("eligible", 10, "season_6");
            service.Check(newCycle);

            Assert.AreEqual(0, newCycle.levelsSinceEnter, "Mùa mới đo lại từ đầu");
            Assert.IsNull(newCycle.levelsSincePrevStep);
        }

        [Test]
        public void NoProgressSource_LeavesLevelFieldsAbsent_AndDoesNotBreakTimeFields()
        {
            // Dựng service không có sổ tiến độ (đường cũ / DI thiếu): hai field level vắng mặt,
            // hai field giờ chơi vẫn làm việc bình thường.
            var clock = new FakePlayClock();
            var service = Service(new FakeDataPool(), clock, FunnelShape.OncePerCycle);

            service.Check(Step("eligible", 10, "season_5"));
            clock.Play(30);
            var open = Step("open", 15, "season_5");
            service.Check(open);

            Assert.IsNull(open.levelsSinceEnter);
            Assert.IsNull(open.levelsSincePrevStep);
            Assert.AreEqual(30, open.playSecSinceEnter);
        }

        [Test]
        public void NegativeLevelDelta_SendsAbsent_NotZero()
        {
            // Pool còn mốc cũ mà tiến độ bị dựng lại thấp hơn (xoá data) → delta âm
            var disk = new FakeDataPool();
            var service = Service(disk, new FakePlayClock(), FunnelShape.OncePerCycle,
                new FakeProgress { MaxPassedLevel = 3 });
            disk.Save(FUNNEL + "_playMarkCycle", "season_5");
            disk.Save(FUNNEL + "_playMarkEnter", 0d);
            disk.Save(FUNNEL + "_levelMarkEnter", 80);
            disk.Save(FUNNEL + "_levelMarkStep", 80);

            var step = Step("milestone", 20, "season_5");
            service.Check(step);

            Assert.IsNull(step.levelsSinceEnter, "Số âm = không biết, VẮNG MẶT chứ không gửi 0");
            Assert.IsNull(step.levelsSincePrevStep);
        }

        // ---- Hợp đồng ----

        [Test]
        public void FieldNames_AreReservedAgainstGameKeys()
        {
            // Game đặt key trùng qua Common.Set/Label.* thì bị cột thật đè IM LẶNG (§H1)
            Assert.IsTrue(FWireReservedKeys.IsReserved("playSecSinceEnter"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("play_sec_since_prev_step"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("levelsSinceEnter"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("levels_since_prev_step"));
        }
    }
}
