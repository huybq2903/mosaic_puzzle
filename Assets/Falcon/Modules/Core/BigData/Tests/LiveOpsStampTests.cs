using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Luật đóng dấu trục event live-op lên param của stream khác.
    /// <br/>🔴 Viết ở mức PARAM, không dựng <c>FLiveOpsApi</c>: ctor của nó <c>internal</c> và module
    /// KHÔNG có <c>InternalsVisibleTo</c>, nên dựng API trong test (assembly riêng) sẽ compile được
    /// trong <c>Tools~/TestHarness</c> — vốn gộp một assembly — nhưng VỠ build Unity Editor. Và mọi
    /// test đi qua <c>LiveOpsLogService</c> đều rơi vào nhóm cần Unity runtime (nhóm chỉ *compile*,
    /// chưa từng *chạy*), nên mấy bất biến dưới đây sẽ mất người canh nếu viết ở đó.
    /// </summary>
    public class LiveOpsStampTests
    {
        private const string EVENT = "card_collection";

        [Test]
        public void RoundId_ComesOnlyFromTheArgument_SdkNeverGuessesIt()
        {
            // Ca hồi quy 05/11. Trước đó FLiveOpsApi.Stamp điền liveOpsRoundId bằng CurrentRoundId
            // = "lượt cuối SDK tình cờ thấy trên một bước nào đó" — không phải lời khai về dòng
            // này. Hai hậu quả thật: dòng của đợt CŨ nhận lượt của đợt ĐANG CHẠY (một dòng hai
            // đợt), và dòng cố ý không có lượt bị điền bừa vì ??= coi null là "ô trống" nên game
            // không có cách nào khai "không thuộc lượt nào".
            var param = new ResourceParam();

            param.StampLiveOps(EVENT, cycleId: "season_12", stage: 3);

            Assert.AreEqual(EVENT, param.liveOpsEvent);
            Assert.AreEqual("season_12", param.liveOpsCycleId);
            Assert.AreEqual(3, param.liveOpsStage);
            Assert.IsNull(param.liveOpsRoundId,
                "Không truyền lượt ⇒ dòng KHÔNG có lượt. Im lặng là lời khai, không phải ô trống");
        }

        [Test]
        public void RoundId_IsCarriedWhenTheGameDeclaresIt()
        {
            var param = new ResourceParam();

            param.StampLiveOps(EVENT, cycleId: "season_12", roundId: "phase_3");

            Assert.AreEqual("phase_3", param.liveOpsRoundId);
        }

        [Test]
        public void HandPassedValuesWin()
        {
            var param = new ResourceParam
            {
                liveOpsCycleId = "season_5", liveOpsRoundId = "phase_1", liveOpsStage = 9
            };

            // Đúng ca dòng nhận muộn: game khai đợt 5 trong lúc đợt 6 đang chạy.
            param.StampLiveOps(EVENT, cycleId: "season_6", stage: 1, roundId: "phase_9");

            Assert.AreEqual("season_5", param.liveOpsCycleId, "Chỉ game biết dòng này thuộc đợt nào");
            Assert.AreEqual("phase_1", param.liveOpsRoundId);
            Assert.AreEqual(9, param.liveOpsStage);
            Assert.AreEqual(EVENT, param.liveOpsEvent, "Ô trống thì mới điền");
        }

        [Test]
        public void BlankEvent_IsANoOp()
        {
            var param = new ResourceParam();

            param.StampLiveOps(null, cycleId: "season_12", roundId: "phase_3");

            Assert.IsNull(param.liveOpsCycleId, "Không có event thì dòng này không thuộc về đâu cả");
            Assert.IsNull(param.liveOpsRoundId);
        }
    }
}
