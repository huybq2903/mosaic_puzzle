using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B12: danh sách chung <c>adWhen</c> + <c>triggerPlayTurnId</c> trên mọi mốc của lần chiếu.</summary>
    public class AdWhenTriggerTests
    {
        private const long T0 = 1_000_000L;

        [Test]
        public void AdWhen_SignedList_IsExactlyTheContractValues()
        {
            foreach (var value in new[]
                     {
                         "level_win", "level_fail", "level_start", "retry", "revive", "booster", "home", "app_open",
                         "other", "Unknown"
                     })
                Assert.IsTrue(FAdWhen.IsKnown(value), value);

            Assert.IsFalse(FAdWhen.IsKnown("unknown"), "Unknown giữ chữ hoa — chữ thường là giá trị lạ");
            Assert.IsFalse(FAdWhen.IsKnown("level_end"));
            Assert.IsFalse(FAdWhen.IsKnown(null));
        }

        [Test]
        public void WarningAtShow_FiresForEmptyUnknownAndOutsideList_ButNotForListValues()
        {
            Assert.IsNotNull(FAdWhen.WarningAtShow(null, "x"));
            Assert.IsNotNull(FAdWhen.WarningAtShow("Unknown", "x"));
            Assert.IsNotNull(FAdWhen.WarningAtShow("after_level", "x"));
            Assert.IsNull(FAdWhen.WarningAtShow(FAdWhen.Revive, "x"));
        }

        [Test]
        public void WarningIfOutsideList_IgnoresEmpty()
        {
            // UpdateContext / OnShowSkipped không bắt buộc adWhen — chỉ cảnh báo chữ lạ
            Assert.IsNull(FAdWhen.WarningIfOutsideList(null, "x"));
            Assert.IsNull(FAdWhen.WarningIfOutsideList(FAdWhen.LevelWin, "x"));
            Assert.IsNotNull(FAdWhen.WarningIfOutsideList("win", "x"));
        }

        [Test]
        public void Trigger_DeclaredViaContext_ReachesLaterMilestones()
        {
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Interstitial }, T0);
            state.SetContext(AdType.Interstitial, adWhen: FAdWhen.LevelWin, triggerPlayTurnId: "turn-20");

            var show = new AdShowParam { type = AdType.Interstitial };
            state.ApplyContext(show, AdType.Interstitial);

            Assert.AreEqual("turn-20", show.triggerPlayTurnId);
            Assert.AreEqual(FAdWhen.LevelWin, show.adWhen);
            Assert.AreEqual("turn-20", state.TakeSnapshot(AdType.Interstitial, T0).triggerPlayTurnId);
        }

        [Test]
        public void Trigger_GivenAtAMilestone_WinsAndUpdatesTheView()
        {
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Reward }, T0);
            state.SetContext(AdType.Reward, triggerPlayTurnId: "old");

            var show = new AdShowParam { type = AdType.Reward, triggerPlayTurnId = "new" };
            state.ApplyContext(show, AdType.Reward);
            var close = new AdShowParam { type = AdType.Reward };
            state.ApplyContext(close, AdType.Reward);

            Assert.AreEqual("new", show.triggerPlayTurnId);
            Assert.AreEqual("new", close.triggerPlayTurnId);
        }

        [Test]
        public void Trigger_IsPerShow_NewRequestDoesNotCarryIt()
        {
            // Như adWhen: lượt kích hoạt là của RIÊNG một lần chiếu — đợt load mới mang sang là dán ngữ cảnh đoán.
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Interstitial }, T0);
            state.SetContext(AdType.Interstitial, triggerPlayTurnId: "turn-20");
            state.Open(new AdViewParam { type = AdType.Interstitial }, T0 + 1000);

            Assert.IsNull(state.TakeSnapshot(AdType.Interstitial, T0 + 1000).triggerPlayTurnId);
        }

        [Test]
        public void Trigger_Absent_StaysAbsentOnPayload()
        {
            var show = new AdShowParam { type = AdType.Interstitial };
            Assert.IsFalse(show.ToDictionary().ContainsKey("triggerPlayTurnId"));
        }
    }
}
