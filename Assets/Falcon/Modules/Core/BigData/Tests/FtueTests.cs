using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Họ <c>f_sdk_ftue_*</c> — ký với loader 10/11. Sổ <see cref="FtueLedger"/> là nơi luật "mỗi mốc một lần
    /// cho mỗi lần cài" sống; nó là lớp thuần trên IDataPool nên chạy được ngoài Unity. Mấy test dựng log thì
    /// cần Unity runtime.
    /// </summary>
    public class FtueTests
    {
        private static FtueLedger Fresh(out FakeDataPool pool)
        {
            pool = new FakeDataPool();
            return new FtueLedger(pool);
        }

        [Test]
        public void FirstScreen_IsOncePerInstall_AndSurvivesARelaunch()
        {
            var ledger = Fresh(out var pool);
            Assert.IsFalse(ledger.FirstScreenDone);

            ledger.MarkFirstScreen();

            Assert.IsTrue(new FtueLedger(pool).FirstScreenDone, "Mở app lần sau (sổ mới trên cùng pool) vẫn thấy dấu");
        }

        [Test]
        public void TutorialId_IsRememberedAcrossARelaunch()
        {
            // Bước gửi ở lần mở app sau vẫn phải mang đúng phiên bản tutorial đã khai.
            var ledger = Fresh(out var pool);
            Assert.IsNull(ledger.CurrentTutorialId, "Chưa start thì không có id — không đoán");

            ledger.MarkStart("tut_v3");

            Assert.AreEqual("tut_v3", new FtueLedger(pool).CurrentTutorialId);
        }

        [Test]
        public void AStartWithoutId_DoesNotBecomeTheCurrentTutorial()
        {
            var ledger = Fresh(out _);

            ledger.MarkStart(null);

            Assert.IsTrue(ledger.StartDone(null), "Start vắng id vẫn chống trùng, ở sổ riêng của nó");
            Assert.IsNull(ledger.CurrentTutorialId);
        }

        [Test]
        public void Step_IsOncePerTutorialAndIndex()
        {
            // Tắt app rồi tutorial chạy lại từ đầu: bước 1 không được đếm hai lần.
            var ledger = Fresh(out _);

            ledger.MarkStep("tut_v3", 1);

            Assert.IsTrue(ledger.StepDone("tut_v3", 1));
            Assert.IsFalse(ledger.StepDone("tut_v3", 2));
            Assert.IsFalse(ledger.StepDone("tut_v4", 1),
                "Phiên bản tutorial MỚI là sổ mới — đây là chỗ funnel OnceOrdered gãy khi game đánh số lại");
        }

        [Test]
        public void Steps_DoNotHaveToBeContiguous_AndMaxStepTracksTheFurthest()
        {
            // SDK không ép thứ tự: tutorial thật có bước tuỳ điều kiện.
            var ledger = Fresh(out _);
            Assert.IsNull(ledger.MaxStep("tut_v3"), "Chưa bước nào thì vắng, không phải 0");

            ledger.MarkStep("tut_v3", 1);
            ledger.MarkStep("tut_v3", 5);
            ledger.MarkStep("tut_v3", 3);

            Assert.AreEqual(5, ledger.MaxStep("tut_v3"), "Bước xa nhất, không phải bước gần nhất");
        }

        [Test]
        public void Outcome_IsRecordedPerTutorial()
        {
            var ledger = Fresh(out _);

            ledger.MarkOutcome("tut_v3", "skip");

            Assert.AreEqual("skip", ledger.Outcome("tut_v3"));
            Assert.IsNull(ledger.Outcome("tut_v4"));
        }

        [Test]
        public void TheFieldNames_AreReservedOnTheWire()
        {
            foreach (var key in new[] { "tutorialId", "step_index", "stepName", "last_step_index" })
                Assert.IsTrue(FWireReservedKeys.IsReserved(key), key);
        }

        [Test]
        public void TheFiveEvents_HaveTheSignedNames_AndOnlyTheirOwnFields()
        {
            // Cần Unity runtime (ctor log chạm singleton).
            Assert.AreEqual("f_sdk_ftue_first_screen", new FFtueFirstScreenLog().Event);
            Assert.AreEqual("f_sdk_ftue_tutorial_start", new FFtueTutorialStartLog().Event);
            Assert.AreEqual("f_sdk_ftue_tutorial_step", new FFtueTutorialStepLog().Event);
            Assert.AreEqual("f_sdk_ftue_tutorial_complete", new FFtueTutorialCompleteLog().Event);
            Assert.AreEqual("f_sdk_ftue_tutorial_skip", new FFtueTutorialSkipLog().Event);

            var step = new FFtueTutorialStepLog { tutorialId = "tut_v3", stepIndex = 2, stepName = "first_match" }
                .ToDictionary();
            Assert.AreEqual("tut_v3", step["tutorialId"]);
            Assert.AreEqual(2, step["stepIndex"]);
            Assert.AreEqual("first_match", step["stepName"]);

            var skipBeforeAnyStep = new FFtueTutorialSkipLog { tutorialId = "tut_v3" }.ToDictionary();
            Assert.IsFalse(skipBeforeAnyStep.ContainsKey("lastStepIndex"), "Bỏ qua trước bước đầu: vắng, không phải 0");

            var startWithoutId = new FFtueTutorialStartLog().ToDictionary();
            Assert.IsFalse(startWithoutId.ContainsKey("tutorialId"), "Không khai thì vắng, không phải \"Unknown\"");
        }
    }
}
