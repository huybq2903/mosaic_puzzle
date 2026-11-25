using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Ngữ cảnh phải nằm trong KHOÁ GỘP của cụm exposure UI — không thì hai đợt (hay hai mốc, hay
    /// hai bang hội) bị nén vào một dòng và mất số im lặng, đúng bài học khoá gộp cụm quảng cáo
    /// 1.4.1. Từ 05/11 khoá gồm CẢ HAI TRỤC, vì LiveOpsRef chở được trục tính năng.
    /// </summary>
    public class UiLiveOpsClusterTests
    {
        private const string SURFACE = "card_collection_icon";

        [Test]
        public void SameSurface_DifferentCycle_DoesNotMerge()
        {
            var state = new UiExposureState();

            state.Record(SURFACE, UiTally.Impression, null,
                new LiveOpsRef { liveOpsEvent = "card_collection", liveOpsCycleId = "season_12" });
            state.Record(SURFACE, UiTally.Impression, null,
                new LiveOpsRef { liveOpsEvent = "card_collection", liveOpsCycleId = "season_13" });

            Assert.AreEqual(2, state.Count, "Hai đợt phải là hai cụm");
        }

        [Test]
        public void SameSurfaceAndCycle_Merges_AndCarriesContextToTheLog()
        {
            var state = new UiExposureState();
            var context = new LiveOpsRef
            {
                liveOpsEvent = "card_collection", liveOpsCycleId = "season_12", liveOpsStage = 3
            };

            state.Record(SURFACE, UiTally.Impression, null, context);
            state.Record(SURFACE, UiTally.Impression, null, context);

            Assert.AreEqual(1, state.Count);
            var log = new FUiExposureLog(state.TakeAll()[0]);
            Assert.AreEqual(2, log.impressionCount);
            Assert.AreEqual("card_collection", log.liveOpsEvent);
            Assert.AreEqual("season_12", log.liveOpsCycleId);
            Assert.AreEqual(3, log.liveOpsStage);
        }

        [Test]
        public void FeatureAxis_IsPartOfTheClusterKey()
        {
            // Điều kiện BẮT BUỘC của việc gộp lớp nền 05/11: LiveOpsRef chở được trục tính năng,
            // nên khoá gộp phải chở đủ. Thiếu thì impression của hai bang hội thành MỘT dòng mang
            // id của bang hội ĐẦU TIÊN — im lặng, cùng hạng lỗi "extraMeta lần đầu thắng".
            var state = new UiExposureState();

            state.Record(SURFACE, UiTally.Impression, null,
                new LiveOpsRef { feature = "guild", featureInstanceId = "T_1" });
            state.Record(SURFACE, UiTally.Impression, null,
                new LiveOpsRef { feature = "guild", featureInstanceId = "T_2" });

            Assert.AreEqual(2, state.Count, "Hai bang hội phải là hai cụm");
        }

        [Test]
        public void FeatureAxis_ReachesTheLog()
        {
            var state = new UiExposureState();

            state.Record(SURFACE, UiTally.Impression, null,
                new LiveOpsRef { feature = "guild", featureInstanceId = "T_8812", featureStage = 4 });

            var log = new FUiExposureLog(state.TakeAll()[0]);
            Assert.AreEqual("guild", log.feature);
            Assert.AreEqual("T_8812", log.featureInstanceId);
            Assert.AreEqual(4, log.featureStage);
        }

        [Test]
        public void SurfaceWithoutEvent_StillWorks()
        {
            var state = new UiExposureState();

            state.Record("shop_banner", UiTally.Click, null);

            var log = new FUiExposureLog(state.TakeAll()[0]);
            Assert.IsNull(log.liveOpsEvent, "Vắng event = dòng này không của event nào");
        }
    }
}
