using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Event <c>f_sdk_level_action_revive</c> — ký với loader 09/11: một dòng = một lần hồi sinh ĐÃ
    /// DÙNG. Kèm bảng chuyển <c>Level.OnAction</c> cũ (1.7.0) đúng như đã gửi loader.
    /// <br/>Test ở mức PARAM và hàm chuyển thuần — chạy được ngoài Unity.
    /// </summary>
    public class LevelReviveTests
    {
        [Test]
        public void AFullRow_ReachesTheWire()
        {
            var param = new LevelReviveParam
            {
                playTurnId = "turn_1", progressPct = 72f, payMethod = FPayMethod.Ad
            };

            param.CorrectValues();
            var dict = param.ToDictionary();

            Assert.AreEqual("turn_1", dict["playTurnId"]);
            Assert.AreEqual(72f, dict["progressPct"]);
            Assert.AreEqual("ad", dict["payMethod"]);
        }

        [Test]
        public void ReviveType_ReachesTheWire_AndBlankMeansAbsent()
        {
            // Ký 10/11: vocab mở của game. Không field nào đổi nghĩa theo nó, và cố ý không có field số lượng.
            var typed = new LevelReviveParam { progressPct = 60f, payMethod = FPayMethod.Ad, reviveType = "extra_moves" };
            var blank = new LevelReviveParam { progressPct = 60f, payMethod = FPayMethod.Ad, reviveType = "  " };

            typed.CorrectValues();
            blank.CorrectValues();

            Assert.AreEqual("extra_moves", typed.ToDictionary()["reviveType"]);
            Assert.IsFalse(blank.ToDictionary().ContainsKey("reviveType"), "Chuỗi trắng là 'không khai', không phải một loại");
            Assert.IsFalse(typed.ToDictionary().ContainsKey("amount"));
        }

        [Test]
        public void TheRowIsAUse_NotAProcess()
        {
            // Có dòng là đã dùng. Lời mời / nhận / từ chối là sự kiện của popup (cụm Ui) — không cờ
            // nào ở đây được điều khiển field khác nữa.
            var dict = new LevelReviveParam { progressPct = 50f, payMethod = FPayMethod.Free }.ToDictionary();

            foreach (var key in new[] { "offered", "accepted", "action", "boosterId" })
                Assert.IsFalse(dict.ContainsKey(key), key);
        }

        [Test]
        public void ProgressOutOfRange_IsDropped_NotClamped()
        {
            var param = new LevelReviveParam { progressPct = 150f, payMethod = FPayMethod.Ad };

            param.CorrectValues();

            Assert.IsNull(param.progressPct, "Kẹp 150 về 100 là bịa ra một ván 'gần xong'");
        }

        [Test]
        public void ProgressAtTheBoundaries_IsKept()
        {
            var zero = new LevelReviveParam { progressPct = 0f };
            var hundred = new LevelReviveParam { progressPct = 100f };

            zero.CorrectValues();
            hundred.CorrectValues();

            Assert.AreEqual(0f, zero.progressPct);
            Assert.AreEqual(100f, hundred.progressPct);
        }

        [Test]
        public void AnUnknownPayMethod_IsDropped_NotTurnedIntoFree()
        {
            var param = new LevelReviveParam { progressPct = 10f, payMethod = "gems" };

            param.CorrectValues();

            Assert.IsNull(param.payMethod, "'free' là game cho không — khác hẳn 'game không khai'");
        }

        [Test]
        public void ItDoesNotCarryTheSevenContextFields()
        {
            // Ngữ cảnh event/tính năng đã nằm trên dòng level cùng lượt, nối bằng playTurnId.
            var dict = new LevelReviveParam { progressPct = 10f }.ToDictionary();

            foreach (var key in new[]
                     {
                         "liveOpsEvent", "liveOpsCycleId", "liveOpsRoundId", "liveOpsStage",
                         "feature", "featureInstanceId", "featureStage"
                     })
                Assert.IsFalse(dict.ContainsKey(key), key);
        }

        [Test]
        public void TheFieldNames_AreReservedOnTheWire()
        {
            Assert.IsTrue(FWireReservedKeys.IsReserved("payMethod"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("progress_pct"));
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(LevelReviveParam), "pay_method"));
        }

        [Test]
        public void TheEventName_IsTheSignedOne()
        {
            // Cần Unity runtime (ctor log chạm singleton).
            Assert.AreEqual("f_sdk_level_action_revive",
                new FLevelReviveLog(new LevelReviveParam { progressPct = 1f }).Event);
        }

        // ---- Bảng chuyển Level.OnAction cũ — đúng bảng đã gửi loader ----

#pragma warning disable 618
        [Test]
        public void OldCall_AcceptedRevive_BecomesOneReviveRow()
        {
            var (revive, warning) = LevelActionAdapter.ToRevive(new LevelActionParam
            {
                action = FLevelAction.Revive, accepted = true, offered = true, progressPct = 60f,
                payMethod = FPayMethod.Currency, playTurnId = "turn_9"
            });

            Assert.IsNull(warning);
            Assert.IsNotNull(revive);
            Assert.AreEqual(60f, revive.progressPct);
            Assert.AreEqual("currency", revive.payMethod);
            Assert.AreEqual("turn_9", revive.playTurnId);
        }

        [Test]
        public void OldCall_RefusedRevive_MakesNoRow_ButSaysWhereItWent()
        {
            var (revive, warning) = LevelActionAdapter.ToRevive(new LevelActionParam
            {
                action = FLevelAction.Revive, accepted = false, offered = true, progressPct = 60f
            });

            Assert.IsNull(revive, "Từ chối thuộc cụm Ui, không phải dòng hồi sinh");
            StringAssert.Contains(FUiSurface.ReviveOffer, warning);
        }

        [Test]
        public void OldCall_Booster_MakesNoRow_AndWarns()
        {
            var (revive, warning) = LevelActionAdapter.ToRevive(new LevelActionParam
            {
                action = FLevelAction.Booster, boosterId = "hammer", accepted = true, progressPct = 30f
            });

            Assert.IsNull(revive);
            Assert.IsNotNull(warning, "Mất mát đã biết phải nói ra, không im lặng");
        }

        [Test]
        public void OldCall_UnknownActionOrNull_MakesNoRow()
        {
            Assert.IsNull(LevelActionAdapter.ToRevive(new LevelActionParam { action = "skip" }).revive);
            Assert.IsNull(LevelActionAdapter.ToRevive(null).revive);
        }
#pragma warning restore 618
    }
}
