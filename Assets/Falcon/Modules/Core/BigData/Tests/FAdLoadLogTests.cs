using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Bản tin load TÁCH (chế độ mặc định của <see cref="IAdLoadLogConfig"/>): chở đúng những gì
    /// caller đưa, không bịa, và giữ nguyên luật "tier vắng ≠ tier 0" của bản gộp.
    /// </summary>
    public class FAdLoadLogTests
    {
        private static AdLoadResultParam Param() => new()
        {
            adType = AdType.Interstitial,
            adMediation = "Max",
            adUnitId = "unit_floor_1_5",
            success = true,
            loadingMs = 1234,
            tier = 1.5,
            floor = 0.0123,
            networkName = "applovin",
            estimatedEcpm = 12.5,
            adAlgorithm = "multicall_3",
        };

        [Test]
        public void Event_IsTheSplitStream()
        {
            Assert.AreEqual("f_sdk_ad_load", new FAdLoadLog(Param()).Event);
        }

        [Test]
        public void Ctor_CopiesEveryFieldOfTheAttempt()
        {
            var log = new FAdLoadLog(Param());

            Assert.AreEqual(AdType.Interstitial, log.adType);
            Assert.AreEqual("Max", log.adMediation);
            Assert.AreEqual("unit_floor_1_5", log.adUnitId);
            Assert.IsTrue(log.success);
            Assert.AreEqual(1234, log.timeLoadingMs);
            Assert.AreEqual(1.5, log.tier);
            Assert.AreEqual(0.0123, log.floor);
            Assert.AreEqual("applovin", log.adNetwork);
            Assert.AreEqual(12.5, log.estimatedEcpm, "eCPM lúc load là thứ quan trọng nhất của dòng này");
            Assert.AreEqual("multicall_3", log.adAlgorithm);
        }

        [Test]
        public void MissingTier_StaysNull_NotZero()
        {
            // Hợp đồng với loader 23/09: vắng = KHÔNG đặt giá sàn, khác hẳn "đặt sàn 0". Điền 0 cho
            // gọn là xoá vĩnh viễn phân biệt này (căn cứ của chẩn đoán nhóm AdMob-không-đặt-floor).
            var log = new FAdLoadLog(new AdLoadResultParam { adType = AdType.Reward });

            Assert.IsNull(log.tier);
            Assert.IsNull(log.floor);
            Assert.IsNull(log.adNetwork);
            Assert.IsNull(log.estimatedEcpm);
        }

        [Test]
        public void FailedAttempt_CarriesItsOwnError()
        {
            // Khác bản gộp: ở đây error là của ĐÚNG lần này, không phải "bản cuối của cụm"
            var log = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Reward, success = false, errorMess = "no_fill", loadingMs = 77
            });

            Assert.IsFalse(log.success);
            Assert.AreEqual("no_fill", log.errorMess);
            Assert.AreEqual(77, log.timeLoadingMs);
        }

        [Test]
        public void AdViewId_IsNotSetByTheLogItself()
        {
            // Id do AdRequestService đóng từ AdViewCache.PendingViewId — bản tin không tự đoán
            Assert.IsNull(new FAdLoadLog(Param()).adViewId);
        }
        [Test]
        public void NonPositiveEcpm_IsNotSent()
        {
            // 0 / -1 của mediation nghĩa là "không biết" — gửi 0 lên dây là khai một giá trị giả
            var zero = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Reward, success = true, estimatedEcpm = 0, ecpmFloor = -1
            });

            Assert.IsNull(zero.estimatedEcpm);
            Assert.IsNull(zero.ecpmFloor);
        }

        [Test]
        public void TierZero_IsNotSent_BecauseItErasesTheNoFloorDistinction()
        {
            // Án máy test 26/10: ba unit gửi tier = 0 mà không có ecpm_floor (tức KHÔNG đặt sàn).
            // Luật ký với loader 23/09: vắng = không đặt sàn, KHÁC "đặt sàn 0" — nên 0 phải biến
            // thành vắng ngay ở SDK, không trông vào caller.
            var log = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = true, tier = 0, floor = 0
            });

            Assert.IsNull(log.tier);
            Assert.IsNull(log.floor);
        }

        [Test]
        public void PositiveTier_StillGoesThrough()
        {
            var log = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = true, tier = 1.6, floor = 0.42
            });

            Assert.AreEqual(1.6, log.tier);
            Assert.AreEqual(0.42, log.floor);
        }

        [Test]
        public void FloorRoleColumns_GoThroughAsGiven()
        {
            var log = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = true,
                adCandidateId = "cand-1", ecpmFloorType = "upgrade_initial", ecpmFloorPercentile = "P50",
                adLoadAttempt = 3
            });

            Assert.AreEqual("cand-1", log.adCandidateId);
            Assert.AreEqual("upgrade_initial", log.ecpmFloorType);
            Assert.AreEqual("P50", log.ecpmFloorPercentile);
            Assert.AreEqual(3, log.adLoadAttempt);
        }

        [Test]
        public void BlankStrings_BecomeAbsentKeys()
        {
            // RemoveIfNull chỉ bỏ null: "" lên dây là một giá trị GIẢ mà mọi phép group theo vai sàn
            // phải tự lọc — và không ai nhớ lọc.
            var log = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = true,
                adCandidateId = "", ecpmFloorType = "   ", ecpmFloorPercentile = ""
            });

            Assert.IsNull(log.adCandidateId);
            Assert.IsNull(log.ecpmFloorType);
            Assert.IsNull(log.ecpmFloorPercentile);
        }

        [Test]
        public void NonPositiveAttempt_IsUnknown_NotZero()
        {
            // Nhát thử đếm từ 1, nên 0 không phải một lần thử nào — cùng luật "không dương = không biết"
            var log = new FAdLoadLog(new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = false, adLoadAttempt = 0
            });

            Assert.IsNull(log.adLoadAttempt);
        }
    }
}
