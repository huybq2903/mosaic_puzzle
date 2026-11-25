using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Event <c>f_sdk_level_action_booster</c> — ký với loader 10/11: một dòng = một lần dùng booster TRONG ván.
    /// Test ở mức PARAM — chạy được ngoài Unity, trừ test dựng log.
    /// </summary>
    public class LevelBoosterTests
    {
        [Test]
        public void AFullRow_ReachesTheWire()
        {
            var param = new LevelBoosterParam
            {
                playTurnId = "turn_1", boosterId = "hammer", progressPct = 42f, payMethod = FPayMethod.Inventory
            };

            param.CorrectValues();
            var dict = param.ToDictionary();

            Assert.AreEqual("turn_1", dict["playTurnId"]);
            Assert.AreEqual("hammer", dict["boosterId"]);
            Assert.AreEqual(42f, dict["progressPct"]);
            Assert.AreEqual("inventory", dict["payMethod"]);
        }

        [Test]
        public void ABoosterRowWithoutId_IsNotSendable()
        {
            // Thiếu mã thì service BỎ DÒNG: "đã dùng một booster nào đó" không trả lời được câu nào.
            Assert.IsFalse(new LevelBoosterParam { progressPct = 10f }.HasBooster);
            Assert.IsFalse(new LevelBoosterParam { boosterId = " ", progressPct = 10f }.HasBooster);
            Assert.IsTrue(new LevelBoosterParam { boosterId = "hammer" }.HasBooster);
        }

        [Test]
        public void TheRowIsAUse_NotAProcess()
        {
            var dict = new LevelBoosterParam { boosterId = "hammer", progressPct = 10f }.ToDictionary();

            foreach (var key in new[] { "offered", "accepted", "action", "reviveType" })
                Assert.IsFalse(dict.ContainsKey(key), key);
        }

        [Test]
        public void ProgressOutOfRange_IsDropped_AndUnknownPayMethod_IsDropped()
        {
            // Cùng luật với hồi sinh — một chỗ duy nhất (LevelActionRules), nên hai event không lệch nhau.
            var param = new LevelBoosterParam { boosterId = "hammer", progressPct = -5f, payMethod = "gems" };

            param.CorrectValues();

            Assert.IsNull(param.progressPct, "Không kẹp về 0");
            Assert.IsNull(param.payMethod, "Không đổi thành 'free'");
        }

        [Test]
        public void ItDoesNotCarryTheSevenContextFields()
        {
            var dict = new LevelBoosterParam { boosterId = "hammer", progressPct = 10f }.ToDictionary();

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
            Assert.IsTrue(FWireReservedKeys.IsReserved("boosterId"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("revive_type"));
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(LevelBoosterParam), "booster_id"));
        }

        [Test]
        public void TheEventName_IsTheSignedOne()
        {
            // Cần Unity runtime (ctor log chạm singleton).
            Assert.AreEqual("f_sdk_level_action_booster",
                new FLevelBoosterLog(new LevelBoosterParam { boosterId = "hammer", progressPct = 1f }).Event);
        }
    }
}
