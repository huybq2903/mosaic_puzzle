using System.Reflection;
using System.Runtime.Serialization;
using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    public class AdShowSkippedTests
    {
        [Test]
        public void SkipReason_HasExactlyTheSignedValues()
        {
            Assert.AreEqual(13, System.Enum.GetValues(typeof(AdSkipReason)).Length,
                "Thêm/bớt giá trị phải báo loader trước khi ship");
        }

        [Test]
        public void EventName_IsTheSignedOne()
        {
            Assert.AreEqual("f_sdk_ad_show_skipped", new FAdShowSkippedLog().Event);
        }

        [Test]
        public void InterstitialAndAppOpen_WithWhere_AreAccepted()
        {
            Assert.IsNull(FAdShowSkippedLog.RejectReason(AdType.Interstitial, "level_end"));
            Assert.IsNull(FAdShowSkippedLog.RejectReason(AdType.AppOpen, "resume"));
        }

        [Test]
        public void FormatsTheGameDoesNotTrigger_AreRejected()
        {
            // Rewarded do người chơi bấm: "không có ad" của nó nằm ở show_attempt.adAvailable.
            Assert.IsNotNull(FAdShowSkippedLog.RejectReason(AdType.Reward, "shop"));
            Assert.IsNotNull(FAdShowSkippedLog.RejectReason(AdType.Banner, "home"));
            Assert.IsNotNull(FAdShowSkippedLog.RejectReason(AdType.Native, "home"));
        }

        [Test]
        public void MissingWhere_IsRejected()
        {
            Assert.IsNotNull(FAdShowSkippedLog.RejectReason(AdType.Interstitial, null));
            Assert.IsNotNull(FAdShowSkippedLog.RejectReason(AdType.Interstitial, "  "));
        }

        [TestCase(AdSkipReason.Cooldown, "cooldown")]
        [TestCase(AdSkipReason.LevelInterval, "level_interval")]
        [TestCase(AdSkipReason.LevelGate, "level_gate")]
        [TestCase(AdSkipReason.NewUserGrace, "new_user_grace")]
        [TestCase(AdSkipReason.PostPurchaseGrace, "post_purchase_grace")]
        [TestCase(AdSkipReason.PostAdGrace, "post_ad_grace")]
        [TestCase(AdSkipReason.DailyCap, "daily_cap")]
        [TestCase(AdSkipReason.SessionCap, "session_cap")]
        [TestCase(AdSkipReason.SegmentRule, "segment_rule")]
        [TestCase(AdSkipReason.Disabled, "disabled")]
        [TestCase(AdSkipReason.Other, "other")]
        [TestCase(AdSkipReason.NoAds, "no_ads")]
        [TestCase(AdSkipReason.LevelCap, "level_cap")]
        public void SkipReason_WireValue_IsTheSignedSnakeCase(AdSkipReason reason, string wire)
        {
            // Wire đi qua StringEnumConverter (attribute trên enum) nên chữ lên dây chính là EnumMember.
            var member = typeof(AdSkipReason).GetField(reason.ToString())
                .GetCustomAttribute<EnumMemberAttribute>();
            Assert.IsNotNull(member);
            Assert.AreEqual(wire, member.Value);
        }

        [Test]
        public void Payload_CarriesTheFourSignedFields_AdWhenAbsentWhenNull()
        {
            var dict = new FAdShowSkippedLog
            {
                type = AdType.Interstitial, adWhere = "level_end", skipReason = AdSkipReason.PostPurchaseGrace
            }.ToDictionary();

            Assert.AreEqual(AdType.Interstitial, dict["type"]);
            Assert.AreEqual("level_end", dict["adWhere"]);
            Assert.AreEqual(AdSkipReason.PostPurchaseGrace, dict["skipReason"]);
            Assert.IsFalse(dict.ContainsKey("adWhen"));
        }

        [Test]
        public void SignedValues_KeepTheirOrdinals_NewOnesAppendedAfterOther()
        {
            // Thêm giá trị phải ĐẶT CUỐI: không xê dịch số của giá trị cũ.
            Assert.AreEqual(10, (int)AdSkipReason.Other);
            Assert.AreEqual(11, (int)AdSkipReason.NoAds);
            Assert.AreEqual(12, (int)AdSkipReason.LevelCap);
        }

        [Test]
        public void ReadyAd_WhenAvailable_CarriesAllSixFields()
        {
            var log = Skipped(AdSkipReason.NoAds);
            var misuse = log.ApplyReadyAd(true,
                new AdReadyInfo { adNetwork = "applovin", adUnitId = "unit_a", estimatedEcpm = 12.5 },
                "view_1", "Max");
            var dict = log.ToDictionary();

            Assert.IsFalse(misuse);
            Assert.AreEqual(true, dict["adAvailable"]);
            Assert.AreEqual("view_1", dict["adViewId"]);
            Assert.AreEqual("Max", dict["adMediation"]);
            Assert.AreEqual("applovin", dict["adNetwork"]);
            Assert.AreEqual("unit_a", dict["adUnitId"]);
            Assert.AreEqual(12.5, dict["estimatedEcpm"]);
        }

        [Test]
        public void ReadyAd_WhenNotAvailable_DropsAdMeta_ButKeepsFalseAndMediation()
        {
            var log = Skipped(AdSkipReason.NoAds);
            var misuse = log.ApplyReadyAd(false,
                new AdReadyInfo { adNetwork = "applovin", adUnitId = "unit_a", estimatedEcpm = 12.5 },
                "view_1", "Max");
            var dict = log.ToDictionary();

            Assert.IsTrue(misuse, "meta ad mà không có ad ⇒ caller phải được cảnh báo");
            Assert.AreEqual(false, dict["adAvailable"], "false là sự thật, phải lên dây");
            Assert.AreEqual("Max", dict["adMediation"]);
            Assert.IsFalse(dict.ContainsKey("adViewId"));
            Assert.IsFalse(dict.ContainsKey("adNetwork"));
            Assert.IsFalse(dict.ContainsKey("adUnitId"));
            Assert.IsFalse(dict.ContainsKey("estimatedEcpm"));
        }

        [Test]
        public void AdAvailableUnknown_IsAbsent_NotFalse()
        {
            // Dòng kiểu 1.7.4 (caller không truyền): kho đọc vắng = không biết.
            var log = Skipped(AdSkipReason.Cooldown);
            Assert.IsFalse(log.ApplyReadyAd(null, null, "view_1", null));
            var dict = log.ToDictionary();

            Assert.IsFalse(dict.ContainsKey("adAvailable"));
            Assert.IsFalse(dict.ContainsKey("adViewId"));
            Assert.IsFalse(dict.ContainsKey("adMediation"));
        }

        [Test]
        public void AvailableWithoutReadyAd_StillCarriesViewId_AndAbsentMeta()
        {
            // GMA / IronSource: biết có ad nhưng không có giá — meta vắng, không gửi 0.
            var log = Skipped(AdSkipReason.DailyCap);
            log.ApplyReadyAd(true, null, "view_2", "admob");
            var dict = log.ToDictionary();

            Assert.AreEqual(true, dict["adAvailable"]);
            Assert.AreEqual("view_2", dict["adViewId"]);
            Assert.AreEqual("admob", dict["adMediation"]);
            Assert.IsFalse(dict.ContainsKey("adNetwork"));
            Assert.IsFalse(dict.ContainsKey("estimatedEcpm"));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void NonPositiveOrNonFiniteEcpm_IsAbsent(double ecpm)
        {
            // MAX trả Revenue = -1 khi không có giá.
            var log = Skipped(AdSkipReason.NoAds);
            log.ApplyReadyAd(true, new AdReadyInfo { estimatedEcpm = ecpm }, "view_1", null);
            Assert.IsFalse(log.ToDictionary().ContainsKey("estimatedEcpm"));
        }

        [Test]
        public void BlankStrings_AreAbsent_AndCallerMediationWinsOverView()
        {
            var log = Skipped(AdSkipReason.NoAds);
            log.ApplyReadyAd(true, new AdReadyInfo { adMediation = "Max", adNetwork = " ", adUnitId = "" },
                "view_1", "admob");
            var dict = log.ToDictionary();

            Assert.AreEqual("Max", dict["adMediation"]);
            Assert.IsFalse(dict.ContainsKey("adNetwork"));
            Assert.IsFalse(dict.ContainsKey("adUnitId"));
        }

        private static FAdShowSkippedLog Skipped(AdSkipReason reason)
        {
            return new FAdShowSkippedLog { type = AdType.Interstitial, adWhere = "level_end", skipReason = reason };
        }

        [Test]
        public void NoAdsLabelKey_IsTheSignedName()
        {
            Assert.AreEqual("no_ads", FUserLabelKey.NO_ADS);
        }
    }
}
