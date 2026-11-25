using System.Collections.Generic;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Luật của ctor ExtendResourceLog với context (chốt owner 05/09) — nền cho chuỗi
    /// grant-context: (1) MERGE detail từng key, đối số tại-giao-dịch thắng khi trùng;
    /// (2) CLONE chứ không mutate — một context dùng chung cả mẻ grant phải bất khả xâm phạm.
    /// </summary>
    public class ExtendResourceLogTests
    {
        private static ResourceParam Context()
        {
            return new ResourceParam
            {
                resourceWhen = "event_master_league",
                resourceWhere = "rank_reward",
                exchangeId = "claim_1",
                detail = new Dictionary<string, object> { ["league_id"] = "ml_5", ["rank"] = 3 }
            };
        }

        [Test]
        public void Clone_KeepsAllSevenContextFields_LiveOpsAndFeature()
        {
            // Báo lỗi 11/11: thưởng Sky Path / Race đi qua chuỗi grant (EventRewardGranter → ResourceLog → đây)
            // ra dòng resource không có live_ops_*. Rơi cả 3 field trục tính năng nữa — bảy field cùng lớp nền.
            var context = Context();
            context.liveOpsEvent = "sky_path";
            context.liveOpsCycleId = "cycle_12";
            context.liveOpsRoundId = "run_7";
            context.liveOpsStage = 3;
            context.feature = "guild";
            context.featureInstanceId = "G_42";
            context.featureStage = 2;

            var log = new ExtendResourceLog(
                FlowType.Source, "currency", "coin", "coin", 500, 0, 500, param: context);
            var param = (ResourceParam)log.param;

            Assert.AreEqual("sky_path", param.liveOpsEvent);
            Assert.AreEqual("cycle_12", param.liveOpsCycleId);
            Assert.AreEqual("run_7", param.liveOpsRoundId);
            Assert.AreEqual(3, param.liveOpsStage);
            Assert.AreEqual("guild", param.feature);
            Assert.AreEqual("G_42", param.featureInstanceId);
            Assert.AreEqual(2, param.featureStage);
        }

        [Test]
        public void Clone_DoesNotCarryTheContextCurrencyClass()
        {
            // Một context dùng chung cả mẻ grant nhiều loại tiền — hạng tiền của context không phải hạng
            // của TỪNG giao dịch. Clone để trống, StampCurrencyClass tự tra sổ theo currency.
            var context = Context();
            context.currencyClass = FCurrencyClass.Hard;

            // Tên tiền chỉ test này dùng: sổ hạng tiền là static, test khác khai "coin" thì kết quả sai lệch.
            var log = new ExtendResourceLog(
                FlowType.Source, "currency", "erl_clone_only_currency", "erl_clone_only_currency", 500, 0, 500,
                param: context);

            Assert.AreNotEqual(FCurrencyClass.Hard, ((ResourceParam)log.param).currencyClass);
        }

        [Test]
        public void NullDetailArg_KeepsContextDetail()
        {
            var log = new ExtendResourceLog(
                FlowType.Source, "currency", "coin", "coin", 500, 0, 500,
                detail: null, param: Context());

            var param = (ResourceParam)log.param;
            Assert.IsNotNull(param.detail, "Detail của context phải sống khi đối số vắng");
            Assert.AreEqual("ml_5", param.detail["league_id"]);
            Assert.AreEqual("event_master_league", param.resourceWhen,
                "when/where/exchangeId giữ nguyên như hợp đồng cũ");
            Assert.AreEqual("claim_1", param.exchangeId);
        }

        [Test]
        public void Merge_PerKey_ArgWinsOnCollision_BothSidesSurvive()
        {
            var context = Context();                       // detail: league_id=ml_5, rank=3
            var arg = new Dictionary<string, object> { ["rank"] = 99, ["note"] = "arg" };

            var log = new ExtendResourceLog(
                FlowType.Source, "currency", "coin", "coin", 500, 0, 500,
                detail: arg, param: context);

            var merged = ((ResourceParam)log.param).detail;
            Assert.AreEqual("ml_5", merged["league_id"], "Key chỉ context có phải sống");
            Assert.AreEqual(99, merged["rank"], "Trùng key thì đối số tại-giao-dịch thắng");
            Assert.AreEqual("arg", merged["note"], "Key chỉ đối số có phải sống");
            Assert.AreEqual(3, context.detail["rank"], "KHÔNG được mutate detail của context");
        }

        [Test]
        public void BatchReuse_SameContext_LogsIndependent_CallerUntouched()
        {
            // Khuôn thật của thư module: foreach Grant(reward, place, context) — MỘT context
            // cho cả mẻ. Mỗi log phải giữ số đo RIÊNG của vế mình, và context của caller
            // không được đổi một byte sau vòng lặp.
            var context = Context();
            var log1 = new ExtendResourceLog(
                FlowType.Source, "currency", "coin", "coin", 100, 0, 100, param: context);
            var log2 = new ExtendResourceLog(
                FlowType.Source, "booster", "propeller", "propeller", 1, 0, 1, param: context);

            var p1 = (ResourceParam)log1.param;
            var p2 = (ResourceParam)log2.param;
            Assert.AreNotSame(p1, p2, "Mỗi log một param riêng — không chung object");
            Assert.AreEqual(100, p1.amount, "Vế sau không được đè số đo vế trước");
            Assert.AreEqual("coin", p1.itemId);
            Assert.AreEqual(1, p2.amount);
            Assert.AreEqual("claim_1", p2.exchangeId, "Cả mẻ chung exchangeId từ context");

            Assert.AreEqual(0, context.amount, "Context của caller bất khả xâm phạm");
            Assert.AreEqual(FParam.UNKNOWN, context.itemType);
        }
    }
}
