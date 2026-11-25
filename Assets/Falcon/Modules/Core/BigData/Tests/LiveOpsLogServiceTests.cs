using System;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Hợp đồng log live-op (<c>docs/design/LiveOpsEventContract.md</c>): van lọc theo
    /// (event, đợt, lượt, action, stage), lịch đợt khai một lần, <c>liveOpsEnterTs</c> và năm cặp
    /// mốc khoảng cách. Test gọi <c>BuildStep</c> để không phải dựng cả pipeline gửi.
    /// </summary>
    public class LiveOpsLogServiceTests
    {
        private const string EVENT = "card_collection";

        [NoAutoCreate]
        private class FrozenTime : ITimeRepository
        {
            public long Millis = 1_700_000_000_000L;
            public long CurrentTimeMillis => Millis;
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

        [NoAutoCreate]
        private class FakeProgress : IFPlayerGeneralRepository
        {
            public int MaxPassedLevel { get; set; }
            public string AccountID => "acc";
            public string InstallVersion => "1.0.0";
            public string AdvertisingID => null;
        }

        private FakeDataPool _disk;
        private FrozenTime _time;
        private FakePlayClock _clock;
        private FakeProgress _progress;
        private LiveOpsLogService _service;

        [SetUp]
        public void SetUp()
        {
            _disk = new FakeDataPool();
            _time = new FrozenTime();
            _clock = new FakePlayClock();
            _progress = new FakeProgress();
            _service = new LiveOpsLogService(_disk, _time, _clock, _progress, null);
        }

        private static LiveOpsParam Step(string action, int? stage = null, string cycleId = null,
            string roundId = null)
        {
            return new LiveOpsParam
            {
                liveOpsEvent = EVENT, action = action, liveOpsStage = stage,
                liveOpsCycleId = cycleId, liveOpsRoundId = roundId
            };
        }

        private FLiveOpsLog Build(string action, int? stage = null, string cycleId = null,
            string roundId = null)
        {
            return _service.BuildStep(Step(action, stage, cycleId, roundId));
        }

        // ---- Lịch đợt khai một lần ----

        [Test]
        public void DeclareCycle_FillsScheduleOnEveryStep()
        {
            _service.DeclareCycle(EVENT, "season_12", cycleIndex: 12,
                cycleStartTs: 1_700_000_000_000L, cycleEndTs: 1_700_600_000_000L);

            var log = Build(FLiveOpsAction.Eligible);

            Assert.AreEqual("season_12", log.param.liveOpsCycleId, "Mã đợt lấy từ bản khai");
            Assert.AreEqual(12, log.param.cycleIndex);
            Assert.AreEqual(1_700_600_000_000L, log.param.cycleEndTs,
                "Lịch đợt phải đi theo MỌI bước — không để mỗi event tự bê bốn field");
        }

        [Test]
        public void GamePassedCycle_Wins_OverDeclaredOne()
        {
            // Nhận thưởng mùa 5 trong mùa 6: game khai đợt mà dòng THUỘC VỀ, không phải đợt đang chạy
            _service.DeclareCycle(EVENT, "season_6");

            var log = Build(FLiveOpsAction.Claim, stage: 3, cycleId: "season_5");

            Assert.AreEqual("season_5", log.param.liveOpsCycleId);
        }

        [Test]
        public void MissingEvent_IsRefused()
        {
            Assert.IsNull(_service.BuildStep(new LiveOpsParam { action = FLiveOpsAction.Open }),
                "Không có liveOpsEvent thì dòng này không thuộc về đâu — từ chối");
        }

        // ---- Van lọc ----

        [Test]
        public void SameStepTwiceInACycle_IsFiltered()
        {
            _service.DeclareCycle(EVENT, "season_12");

            Assert.IsNotNull(Build(FLiveOpsAction.Milestone, stage: 3));
            Assert.IsNull(Build(FLiveOpsAction.Milestone, stage: 3), "Bước trùng phải bị chặn");
        }

        [Test]
        public void MilestoneAndClaim_SameStage_AreTwoDifferentSteps()
        {
            // Khoá van gồm cả ACTION: milestone(3) rồi claim(3) là hai bước thật
            _service.DeclareCycle(EVENT, "season_12");

            Assert.IsNotNull(Build(FLiveOpsAction.Milestone, stage: 3));
            Assert.IsNotNull(Build(FLiveOpsAction.Claim, stage: 3));
        }

        [Test]
        public void NewCycle_ClearsTheFilter()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Assert.IsNotNull(Build(FLiveOpsAction.Eligible));

            _service.DeclareCycle(EVENT, "season_13");
            Assert.IsNotNull(Build(FLiveOpsAction.Eligible), "Sang đợt mới van phải sạch");
        }

        [Test]
        public void NewRound_ClearsTheFilter_WithinTheSameCycle()
        {
            // Race: mỗi lần ấn Start là một bảng đấu mới trong cùng tuần
            _service.DeclareCycle(EVENT, "week_40");
            Assert.IsNotNull(Build(FLiveOpsAction.Milestone, stage: 1, roundId: "board_1"));
            Assert.IsNull(Build(FLiveOpsAction.Milestone, stage: 1, roundId: "board_1"));
            Assert.IsNotNull(Build(FLiveOpsAction.Milestone, stage: 1, roundId: "board_2"),
                "Bảng đấu mới là lượt mới — không được bị van của bảng cũ nuốt");
        }

        // ---- enterTs + mốc khoảng cách ----

        [Test]
        public void FirstStep_IsTheEntryMark()
        {
            _service.DeclareCycle(EVENT, "season_12");
            _clock.Play(3600);

            var log = Build(FLiveOpsAction.Eligible);

            Assert.AreEqual(_time.CurrentTimeMillis, log.liveOpsEnterTs, "Mốc user vào đợt");
            Assert.AreEqual(0, log.playSecSinceEnter);
            Assert.IsNull(log.playSecSincePrevStep, "Chưa có bước trước → VẮNG MẶT, không phải 0");
            Assert.IsNull(log.levelAttemptsSincePrevStep);
        }

        [Test]
        public void LaterStep_MeasuresPlayTime_LevelsAndAttempts()
        {
            _service.DeclareCycle(EVENT, "season_12");
            _progress.MaxPassedLevel = 40;
            Build(FLiveOpsAction.Eligible);

            _clock.Play(600);
            _progress.MaxPassedLevel = 42;
            for (var i = 0; i < 5; i++) _service.ReportLevel("turn_" + i, LevelStatus.Start, null);

            var log = Build(FLiveOpsAction.Milestone, stage: 1);

            Assert.AreEqual(600, log.playSecSinceEnter);
            Assert.AreEqual(2, log.levelPassesSinceEnter, "Chỉ 2 level mới vượt qua");
            Assert.AreEqual(5, log.levelAttemptsSinceEnter, "Nhưng mở 5 lượt chơi — đây là công sức");
            Assert.AreEqual(600, log.playSecSincePrevStep);
            Assert.AreEqual(5, log.levelAttemptsSincePrevStep);
        }

        [Test]
        public void DoubleFiredStart_DoesNotInflateAttempts()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);

            _service.ReportLevel("turn_1", LevelStatus.Start, null);
            _service.ReportLevel("turn_1", LevelStatus.Start, null);

            Assert.AreEqual(1, Build(FLiveOpsAction.Milestone, stage: 1).levelAttemptsSinceEnter,
                "Đếm theo LƯỢT, không đếm lời gọi — double-fire không được làm phồng");
        }

        [Test]
        public void NewCycle_ResetsAllMarks()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);
            _clock.Play(99_999);
            _progress.MaxPassedLevel = 300;

            _service.DeclareCycle(EVENT, "season_13");
            var log = Build(FLiveOpsAction.Eligible);

            Assert.AreEqual(0, log.playSecSinceEnter, "Mùa mới đo lại từ đầu — không thì mùa 2 đo từ mùa 1");
            Assert.AreEqual(0, log.levelPassesSinceEnter);
            Assert.IsNull(log.playSecSincePrevStep);
        }

        [Test]
        public void NegativeDelta_SendsAbsent_NotZero()
        {
            _service.DeclareCycle(EVENT, "season_12");
            _clock.Play(500);
            Build(FLiveOpsAction.Eligible);

            // Sổ mốc sống dai hơn sổ nguồn: xoá data phiên thì giờ chơi tụt về 0
            var reset = new LiveOpsLogService(_disk, _time, new FakePlayClock(), _progress, null);
            var log = reset.BuildStep(Step(FLiveOpsAction.Milestone, 1));

            Assert.IsNull(log.playSecSinceEnter, "Số âm = KHÔNG BIẾT, vắng mặt chứ không gửi 0");
        }

        // ---- Bộ đếm riêng của event (dòng level khai độc quyền) ----

        [Test]
        public void EventOwnedLevels_AreCountedSeparately()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);

            var mine = new LiveOpsRef { liveOpsEvent = EVENT, liveOpsCycleId = "season_12" };
            _service.ReportLevel("t1", LevelStatus.Start, mine);
            _service.ReportLevel("t1", LevelStatus.Pass, mine);
            _service.ReportLevel("t2", LevelStatus.Start, mine);
            _service.ReportLevel("chapter", LevelStatus.Start, null); // ván chapter, không thuộc event

            var log = Build(FLiveOpsAction.Milestone, stage: 1);

            Assert.AreEqual(2, log.liveOpsLevelAttemptsSinceEnter, "Chỉ ván KHAI event");
            Assert.AreEqual(1, log.liveOpsLevelPassesSinceEnter);
            Assert.AreEqual(3, log.levelAttemptsSinceEnter, "Toàn cục tính cả ván chapter");
            Assert.LessOrEqual(log.liveOpsLevelAttemptsSinceEnter, log.levelAttemptsSinceEnter,
                "Bất biến DQ: số của event luôn ≤ số toàn cục");
        }

        [Test]
        public void LevelRowWithoutCycle_DoesNotCount_AndDoesNotWipeMarks()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);
            _clock.Play(120);

            // Dòng level khai event nhưng QUÊN mã đợt — một dòng hỏng không được xoá sổ mốc
            _service.ReportLevel("t1", LevelStatus.Start, new LiveOpsRef { liveOpsEvent = EVENT });

            var log = Build(FLiveOpsAction.Milestone, stage: 1);

            Assert.AreEqual(120, log.playSecSinceEnter, "Sổ mốc của đợt phải còn nguyên");
            Assert.IsNull(log.liveOpsLevelAttemptsSinceEnter,
                "Dòng thiếu mã đợt thì không đếm — và không đặt cờ, nên cặp riêng VẮNG (1.5.6) " +
                "chứ không phải 0: chưa có bằng chứng nào nói event này khai level");
        }

        // ---- Hai ô sổ mốc: đợt đang chạy + đợt vừa đóng (lỗi 19/10) ----

        [Test]
        public void ClosedCycleRow_KeepsItsOwnClock_AndDoesNotWipeTheRunningOne()
        {
            // Master League: xem bảng xếp hạng mùa 5 trong lúc mùa 6 đang chạy. Trước 19/10 dòng
            // mùa 5 đạp mất sổ mốc của mùa 6 → enterTs mùa 6 thành "lúc tôi mở bảng mùa cũ".
            _service.DeclareCycle(EVENT, "season_5");
            Build(FLiveOpsAction.Join);
            _clock.Play(1000);

            _time.Millis += 60_000;
            _service.DeclareCycle(EVENT, "season_6");
            Assert.AreEqual(0, Build(FLiveOpsAction.Open).playSecSinceEnter, "Mùa 6 mở ô riêng");

            _clock.Play(300);
            var closed = Build(FLiveOpsAction.Complete, cycleId: "season_5");
            Assert.AreEqual(1300, closed.playSecSinceEnter, "Dòng mùa 5 đo từ mốc user vào MÙA 5");

            var running = Build(FLiveOpsAction.Milestone, stage: 1);
            Assert.AreEqual(300, running.playSecSinceEnter,
                "Sổ của mùa đang chạy không được dòng mùa cũ đạp mất");
        }

        [Test]
        public void ClosedCycleRow_CarriesItsOwnSchedule_NotTheRunningOne()
        {
            _service.DeclareCycle(EVENT, "season_5", cycleIndex: 5,
                cycleStartTs: 1_000L, cycleEndTs: 2_000L);
            Build(FLiveOpsAction.Join);

            _service.DeclareCycle(EVENT, "season_6", cycleIndex: 6,
                cycleStartTs: 3_000L, cycleEndTs: 4_000L);
            Build(FLiveOpsAction.Open);

            var closed = Build(FLiveOpsAction.Claim, stage: 2, cycleId: "season_5");

            Assert.AreEqual(5, closed.param.cycleIndex, "Mang số mùa của CHÍNH nó");
            Assert.AreEqual(1_000L, closed.param.cycleStartTs);
            Assert.AreEqual(2_000L, closed.param.cycleEndTs,
                "Một dòng mang mã mùa 5 mà giờ đóng của mùa 6 là thứ phía đọc số không gỡ được");
        }

        [Test]
        public void NewCycleDeclaredBeforeItsConfig_DoesNotInheritTheOldSchedule()
        {
            // Cả ba module event đang truyền null khi GetTimeEnd() chưa về. Đợt mới mà mang giờ
            // đóng của đợt cũ thì mọi nhóm countdown sai mà trông vẫn hợp lý.
            _service.DeclareCycle(EVENT, "season_5", cycleIndex: 5, cycleEndTs: 2_000L);
            Build(FLiveOpsAction.Join);

            _service.DeclareCycle(EVENT, "season_6");
            var row = Build(FLiveOpsAction.Open);

            Assert.IsNull(row.param.cycleEndTs, "Không thừa kế giờ đóng của mùa trước");
            Assert.IsNull(row.param.cycleIndex);
        }

        [Test]
        public void RedeclaringTheSameCycle_KeepsWhatWasAlreadyKnown()
        {
            _service.DeclareCycle(EVENT, "season_5", cycleIndex: 5, cycleEndTs: 2_000L);
            _service.DeclareCycle(EVENT, "season_5");

            Assert.AreEqual(2_000L, Build(FLiveOpsAction.Join).param.cycleEndTs,
                "Khai lại ĐÚNG đợt đó mỗi lần mở app không được xoá lịch đã biết");
        }

        [Test]
        public void RoundSteps_ShareOneClockWithCycleSteps()
        {
            // Lượt KHÔNG nằm trong nhãn ô: eligible (lượt rỗng) và join (bảng 1) chung một đồng hồ,
            // nếu không thì mất đúng khoảng cách "mở cửa → chịu vào".
            _service.DeclareCycle(EVENT, "week_40");
            Build(FLiveOpsAction.Eligible);

            _clock.Play(120);
            var join = Build(FLiveOpsAction.Join, roundId: "board_1");
            Assert.AreEqual(120, join.playSecSinceEnter, "Không bấm lại đồng hồ khi vào lượt đầu");
            Assert.AreEqual(120, join.playSecSincePrevStep, "Đo được đoạn eligible → join");

            _clock.Play(60);
            var milestone = Build(FLiveOpsAction.Milestone, stage: 1, roundId: "board_1");
            Assert.AreEqual(180, milestone.playSecSinceEnter);
            Assert.AreEqual(60, milestone.playSecSincePrevStep);

            _clock.Play(30);
            var nextBoard = Build(FLiveOpsAction.Join, roundId: "board_2");
            Assert.AreEqual(210, nextBoard.playSecSinceEnter,
                "Bảng đấu mới vẫn là ĐỢT đó — *_since_enter đo từ lúc vào đợt, kể cả Race");
            Assert.AreEqual("board_2", _service.CurrentRoundId(EVENT), "Lượt gần nhất vẫn nhớ được");
        }

        [Test]
        public void ThirdCycle_EvictsTheOldest_AndTheLostCycleSendsAbsent()
        {
            _service.DeclareCycle(EVENT, "season_1");
            Build(FLiveOpsAction.Join);

            _time.Millis += 1_000;
            _service.DeclareCycle(EVENT, "season_2");
            Build(FLiveOpsAction.Join);

            _time.Millis += 1_000;
            _service.DeclareCycle(EVENT, "season_3");
            Build(FLiveOpsAction.Join);

            var lost = Build(FLiveOpsAction.Claim, stage: 1, cycleId: "season_1");
            Assert.IsNotNull(lost, "Dòng vẫn được gửi — chỉ mấy field khoảng cách là vắng");
            Assert.IsNull(lost.liveOpsEnterTs, "Mất ô thì KHÔNG bịa 'vừa vào đợt lúc này'");
            Assert.IsNull(lost.playSecSinceEnter);
            Assert.IsNull(lost.levelAttemptsSinceEnter);

            Assert.IsNotNull(Build(FLiveOpsAction.Milestone, stage: 1).liveOpsEnterTs,
                "Và dòng của đợt cũ không được đạp ô của đợt đang chạy");
        }

        // ---- Sổ đọc ----

        [Test]
        public void ReadersExposeCycleAndStage()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Milestone, stage: 2);
            Build(FLiveOpsAction.Milestone, stage: 5);
            Build(FLiveOpsAction.Open);

            Assert.AreEqual("season_12", _service.CurrentCycleId(EVENT));
            Assert.AreEqual(5, _service.CurrentStage(EVENT), "Mốc cao nhất, và CHỈ tính dòng milestone");
            Assert.IsTrue(_service.HasPassed(EVENT, "season_12", FLiveOpsAction.Open));
            Assert.IsFalse(_service.HasPassed(EVENT, "season_12", FLiveOpsAction.Complete));
        }

        [Test]
        public void HasPassed_ReadsTheSameNameTheFilterWrote_WhenOnlySomeStepsCarryARound()
        {
            // Hình dạng Boat Rush: lượt trên join/complete/claim, KHÔNG trên milestone (Team Quest
            // cũng vậy ở eligible/open). Trước 05/11 HasPassed tự lấy "lượt đang lưu trong sổ" nên
            // dựng tên KHÁC cái van lọc đã ghi ⇒ báo "chưa log" cho bước ĐÃ log, và hàm ghi bù của
            // game ghi lại (van chặn nên không sinh dòng trùng, nhưng logic game thì rối).
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Join, roundId: "s5_a1");   // ghi ROUND = s5_a1 vào sổ
            Build(FLiveOpsAction.Milestone, stage: 5);      // bước này KHÔNG mang lượt

            Assert.IsTrue(_service.HasPassed(EVENT, "season_12", FLiveOpsAction.Milestone, 5),
                "Bước ghi không mang lượt ⇒ hỏi không mang lượt, phải thấy");
            Assert.IsFalse(_service.HasPassed(EVENT, "season_12", FLiveOpsAction.Milestone, 5, "s5_a1"),
                "Hỏi kèm lượt là câu hỏi KHÁC — bước đó không thuộc lượt nào");
            Assert.IsTrue(_service.HasPassed(EVENT, "season_12", FLiveOpsAction.Join, roundId: "s5_a1"),
                "Bước ghi mang lượt ⇒ hỏi kèm lượt, phải thấy");
        }

        // ---- Hợp đồng tên trên wire ----

        [Test]
        public void WireNamesAreContract()
        {
            var log = new FLiveOpsLog(Step(FLiveOpsAction.Eligible));

            Assert.AreEqual("f_sdk_live_ops_data", log.Event);
            Assert.AreEqual("eligible", FLiveOpsAction.Eligible);
            Assert.AreEqual("milestone", FLiveOpsAction.Milestone);
            Assert.IsTrue(FWireReservedKeys.IsReserved("live_ops_event"),
                "Game đặt key trùng cột hợp đồng phải bị chặn (§H1)");
            Assert.IsTrue(FWireReservedKeys.IsReserved("liveOpsStage"));
        }
        // ---- Cặp bộ đếm RIÊNG của event: vắng khi event không khai level (sửa 26/10) ----
        //
        // Án thật: phía đọc số thấy MỌI dòng f_sdk_live_ops của MỌI event đều mang cả
        // level_passes_since_enter (toàn cục) lẫn live_ops_level_passes_since_enter (riêng event),
        // nên tưởng SDK gửi đôi một khoá và đòi "chốt một tên". Thực tế hai số khác nghĩa — nhưng
        // cặp RIÊNG đang bị gửi với giá trị 0 cho cả event KHÔNG khai event lên dòng level, tức
        // khai "chơi 0 ván trong event" trong khi sự thật là "không áp dụng". Luật nhà: vắng ≠ 0.

        [Test]
        public void EventThatNeverDeclaresLevels_OmitsItsOwnCounters()
        {
            _clock.Play(60);
            Build(FLiveOpsAction.Eligible);          // mở đợt
            _clock.Play(60);
            _progress.MaxPassedLevel = 7;            // chơi level THƯỜNG, không khai event

            var log = Build(FLiveOpsAction.Join);

            Assert.IsNotNull(log.levelPassesSinceEnter, "Cặp TOÀN CỤC vẫn phải có");
            Assert.IsNull(log.liveOpsLevelPassesSinceEnter,
                "Event không khai event lên dòng level thì cặp RIÊNG phải VẮNG, không phải 0");
            Assert.IsNull(log.liveOpsLevelAttemptsSinceEnter);
            Assert.IsNull(log.liveOpsLevelPassesSincePrevStep);
            Assert.IsNull(log.liveOpsLevelAttemptsSincePrevStep);
        }

        [Test]
        public void OnceALevelDeclaresTheEvent_ItsOwnCountersAppear()
        {
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);

            // một ván CÓ khai event: mở lượt rồi thắng. ⚠ Dòng level phải khai ĐỦ CẶP
            // (event, cycle) — thiếu mã đợt là ReportLevel bỏ qua, không đếm và không đặt cờ.
            var mine = new LiveOpsRef { liveOpsEvent = EVENT, liveOpsCycleId = "season_12" };
            _service.ReportLevel("turn_1", LevelStatus.Start, mine);
            _service.ReportLevel("turn_1", LevelStatus.Pass, mine);

            var log = Build(FLiveOpsAction.Milestone, 1);

            Assert.AreEqual(1, log.liveOpsLevelPassesSinceEnter, "Một ván của event đã thắng");
            Assert.AreEqual(1, log.liveOpsLevelAttemptsSinceEnter);
        }

        [Test]
        public void EventRunningOnNormalLevels_StaysUnderTheGlobalPair()
        {
            // Event chạy TRÊN LEVEL THƯỜNG: ván của event cũng nhích map chính, nên cả hai cặp
            // đều bị cặp toàn cục chặn trên. Ca event có BỘ LEVEL RIÊNG thì khác — test dưới.
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);
            var mine = new LiveOpsRef { liveOpsEvent = EVENT, liveOpsCycleId = "season_12" };
            _progress.MaxPassedLevel = 5;                                   // 5 ván thường
            _service.ReportLevel("turn_1", LevelStatus.Start, mine);
            _service.ReportLevel("turn_1", LevelStatus.Pass, mine);

            var log = Build(FLiveOpsAction.Claim, 1);

            Assert.LessOrEqual(log.liveOpsLevelPassesSinceEnter, log.levelPassesSinceEnter);
            Assert.LessOrEqual(log.liveOpsLevelAttemptsSinceEnter, log.levelAttemptsSinceEnter);
        }

        [Test]
        public void OwnLevelSetEvent_PassesPairCanExceedTheGlobalOne()
        {
            // Án production 28/10: Master League gửi live_ops_level_passes = 45 cạnh
            // level_passes = 1, phía DQ đọc thành "bộ đếm toàn cục bị reset". KHÔNG phải:
            // level_passes_* đo TIẾN ĐỘ MAP CHÍNH (MaxPassedLevel), mà ván của league không nhích
            // map chính. Bất biến ≤ chỉ có ở cặp ATTEMPTS (cùng đếm lượt chơi).
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);
            var mine = new LiveOpsRef { liveOpsEvent = EVENT, liveOpsCycleId = "season_12" };

            for (var turn = 1; turn <= 3; turn++)
            {
                _service.ReportLevel("league_" + turn, LevelStatus.Start, mine);
                _service.ReportLevel("league_" + turn, LevelStatus.Pass, mine);
            }
            // map chính KHÔNG nhích: _progress.MaxPassedLevel giữ nguyên 0

            var log = Build(FLiveOpsAction.Milestone, 1);

            Assert.AreEqual(3, log.liveOpsLevelPassesSinceEnter, "Ba ván của event đã thắng");
            Assert.AreEqual(0, log.levelPassesSinceEnter, "Map chính không nhích");
            Assert.Greater(log.liveOpsLevelPassesSinceEnter, log.levelPassesSinceEnter,
                "Cặp passes KHÔNG có bất biến ≤ — đừng đem vào luật DQ");
            Assert.LessOrEqual(log.liveOpsLevelAttemptsSinceEnter, log.levelAttemptsSinceEnter,
                "Bất biến THẬT: ván của event cũng cộng vào sổ lượt toàn cục");
        }

        [Test]
        public void TheFlagSurvivesANewCycle_SoCountersStayVisible()
        {
            // Cờ "đã từng khai level" theo EVENT, không theo đợt — đợt sau vẫn thấy cặp riêng
            _service.DeclareCycle(EVENT, "season_12");
            Build(FLiveOpsAction.Eligible);
            _service.ReportLevel("turn_1", LevelStatus.Pass,
                new LiveOpsRef { liveOpsEvent = EVENT, liveOpsCycleId = "season_12" });

            // 🔴 Đợt mới PHẢI được khai: bước khai một đợt mà SDK không giữ ô sổ cho thì
            // ResolveSlot trả null và MỌI field khoảng cách vắng mặt (cố ý — thà vắng còn hơn
            // đo lại từ 0). Bỏ dòng này là test đo sai thứ nó định đo.
            _service.DeclareCycle(EVENT, "cycle_2");
            var next = Build(FLiveOpsAction.Eligible, cycleId: "cycle_2");

            Assert.IsNotNull(next.liveOpsLevelPassesSinceEnter,
                "Đợt mới: mốc vào đợt nên là 0, nhưng phải CÓ mặt vì event này có khai level");
            Assert.AreEqual(0, next.liveOpsLevelPassesSinceEnter);
        }

    }
}
