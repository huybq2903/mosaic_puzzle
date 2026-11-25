using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B6: sổ thời gian lượt chơi — 5 khối, luật QC §6.4, khoảng không thao tác, vắng theo cap.</summary>
    public class TurnLedgerStateTests
    {
        private static readonly TurnCaps All = new()
            { busy = true, actions = true, ineffectiveTap = true, speed = true, screen = true };

        private static TurnLedgerState Open(string turnId = "t1")
        {
            var ledger = new TurnLedgerState();
            ledger.Begin(turnId, 1, 0);
            return ledger;
        }

        [Test]
        public void FiveBlocks_AreDisjoint_AndSumToTotal()
        {
            var ledger = Open();
            ledger.SetBusy(true, 1_000);              // 0–1 s làm được
            ledger.SetBusy(false, 3_000);             // 1–3 s bận
            ledger.SetPopup(PopupKind.Offer, 4_000);  // 3–4 s làm được
            ledger.SetPopup(null, 6_000);             // 4–6 s popup offer
            ledger.AdShown(FAdWhen.Revive, null, 7_000);
            ledger.AdEnded(9_000);                    // 7–9 s QC hồi sinh (được tính)
            ledger.SetBackground(true, 10_000);
            ledger.SetBackground(false, 15_000);      // 10–15 s nền

            var s = ledger.Snapshot(16_000);
            Assert.AreEqual(5_000, s.backgroundMs);
            Assert.AreEqual(2_000, s.adMs);
            Assert.AreEqual(2_000, s.popupOfferMs);
            Assert.AreEqual(2_000, s.busyMs);
            Assert.AreEqual(5_000, s.readyMs);
            Assert.AreEqual(16_000, s.totalMs);
        }

        [Test]
        public void Priority_BackgroundOverAdOverPopupOverBusy()
        {
            var ledger = Open();
            ledger.SetBusy(true, 0);
            ledger.SetPopup(PopupKind.Other, 0);
            ledger.AdShown(FAdWhen.Booster, null, 0);
            ledger.SetBackground(true, 1_000);       // 0–1 s: QC thắng popup và bận
            var s = ledger.Snapshot(3_000);          // 1–3 s: nền thắng tất cả

            Assert.AreEqual(1_000, s.adMs);
            Assert.AreEqual(2_000, s.backgroundMs);
            Assert.AreEqual(0, s.popupOtherMs);
            Assert.AreEqual(0, s.busyMs);
        }

        [Test]
        public void ExcludedAd_StopsTheClock()
        {
            // §6.4: Inter sau thắng chiếu trước khi game gọi OnPass — không vào khối nào, không vào tổng
            var ledger = Open();
            ledger.AdShown(FAdWhen.LevelWin, "t1", 1_000);
            ledger.AdEnded(31_000);
            var s = ledger.Snapshot(32_000);

            Assert.AreEqual(0, s.adMs);
            Assert.AreEqual(2_000, s.totalMs);
        }

        [Test]
        public void AdRule_NeedsBothWhenAndTrigger()
        {
            Assert.IsTrue(TurnLedgerState.IsCountedAd(null, null, "t1"));
            Assert.IsTrue(TurnLedgerState.IsCountedAd(FAdWhen.Unknown, "t1", "t1"));
            Assert.IsTrue(TurnLedgerState.IsCountedAd(FAdWhen.Revive, null, "t1"));
            Assert.IsFalse(TurnLedgerState.IsCountedAd(FAdWhen.Revive, "t0", "t1"), "QC hồi sinh của lượt khác");
            Assert.IsFalse(TurnLedgerState.IsCountedAd(FAdWhen.LevelStart, null, "t1"));
            Assert.IsFalse(TurnLedgerState.IsCountedAd("new_value_later", null, "t1"), "giá trị thêm sau cũng bị loại");
        }

        [Test]
        public void SpeedOverlay_CountsOnlyInBusyAndReady()
        {
            var ledger = Open();
            ledger.SetSpeed(true, 0);
            ledger.SetBusy(true, 2_000);                // 0–2 làm được, x2
            ledger.SetPopup(PopupKind.Other, 3_000);    // 2–3 bận, x2
            var s = ledger.Snapshot(5_000);             // 3–5 popup: x2 không tính

            Assert.AreEqual(3_000, s.speedOnMs);
        }

        [Test]
        public void Actions_FirstAction_AndGaps_UseReadyTimeOnly()
        {
            var ledger = Open();
            ledger.SetBusy(true, 0);
            ledger.SetBusy(false, 5_000);   // 5 s bận đầu lượt không phải "nhìn rồi mới đi"
            ledger.Action(8_000);           // first_action = 3 s 🟢
            ledger.Action(20_000);          // khoảng 12 s > 10
            ledger.Action(21_000);

            var s = ledger.Snapshot(61_000); // khoảng đang dở 40 s > 10 và > 30

            Assert.AreEqual(3_000, s.firstActionMs);
            Assert.AreEqual(3, s.actions);
            Assert.AreEqual(12_000 + 40_000, s.readyGap10Ms);
            Assert.AreEqual(40_000, s.readyGap30Ms);
            Assert.AreEqual(0, s.readyGap120Ms);
            Assert.AreEqual(40_000, s.sinceLastActionMs);
        }

        [Test]
        public void IneffectiveTaps_SplitByBusy()
        {
            var ledger = Open();
            ledger.IneffectiveTap(1_000);
            ledger.SetBusy(true, 2_000);
            ledger.IneffectiveTap(3_000);
            ledger.IneffectiveTap(3_500);

            var s = ledger.Snapshot(4_000);
            Assert.AreEqual(1, s.ineffectiveTaps);
            Assert.AreEqual(2, s.tapsWhileBusy);
        }

        [Test]
        public void ClosedLedger_IgnoresEverything()
        {
            var ledger = Open();
            ledger.End(1_000);
            ledger.Action(2_000);
            ledger.OfferShown();
            Assert.IsNull(ledger.Snapshot(3_000));
        }

        [Test]
        public void NewSegment_ResetsTheBook()
        {
            var ledger = Open();
            ledger.Action(1_000);
            ledger.Begin("t1", 2, 10_000);
            var s = ledger.Snapshot(12_000);
            Assert.AreEqual(2, s.segmentNo);
            Assert.AreEqual(2_000, s.totalMs);
            Assert.AreEqual(0, s.actions);
        }

        [Test]
        public void Wire_AbsentByCaps_ZeroWhenDeclared()
        {
            var ledger = Open();
            var s = ledger.Snapshot(1_000);

            var none = s.ToWire(null);
            Assert.IsTrue(none.ContainsKey("t_total_sec"));
            Assert.IsFalse(none.ContainsKey("cap_busy"), "không khai thì không có cờ");
            Assert.IsFalse(none.ContainsKey("t_busy_sec"));
            Assert.IsFalse(none.ContainsKey("n_actions"));

            var all = s.ToWire(All);
            Assert.AreEqual(true, all["cap_busy"]);
            Assert.AreEqual(0.0, all["t_busy_sec"]);
            Assert.AreEqual(0, all["n_actions"]);
            Assert.AreEqual(0, all["n_offer_shown"]);
            Assert.IsFalse(all.ContainsKey("first_action_sec"), "chưa có thao tác thì mốc thao tác đầu vắng");
            Assert.AreEqual(1, all["turn_segment_no"]);
            Assert.AreEqual(1.0, all["t_total_sec"]);
        }

        [Test]
        public void Wire_SendsFractionalSeconds()
        {
            var ledger = Open();
            ledger.Action(400);
            Assert.AreEqual(0.4, ledger.Snapshot(1_000).ToWire(All)["first_action_sec"]);
        }

        [Test]
        public void Wire_Has18LedgerFieldsWithAllCaps()
        {
            var wire = Open().Snapshot(1_000).ToWire(All);
            foreach (var key in new[]
                     {
                         "t_total_sec", "t_background_sec", "t_ad_sec", "t_popup_offer_sec", "t_popup_tutorial_sec",
                         "t_popup_other_sec", "t_busy_sec", "t_ready_sec", "t_ready_gap10_sec", "t_ready_gap30_sec",
                         "t_ready_gap120_sec", "t_speed_on_sec", "n_actions", "n_taps_while_busy",
                         "n_ineffective_taps", "n_offer_shown"
                     })
                Assert.IsTrue(wire.ContainsKey(key), key);
            // first_action_sec / sec_since_last_action chỉ có sau thao tác đầu (2 field còn lại của 18)
        }
    }
}
