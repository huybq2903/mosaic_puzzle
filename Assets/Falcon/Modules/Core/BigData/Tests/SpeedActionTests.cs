using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B7: bộ giá trị đóng của f_sdk_speed_action.</summary>
    public class SpeedActionTests
    {
        [Test]
        public void SpeedAction_WireValues_AreTheSignedOnes()
        {
            Assert.AreEqual("speed_on", WireName.Of(SpeedAction.SpeedOn));
            Assert.AreEqual("speed_off", WireName.Of(SpeedAction.SpeedOff));
            Assert.AreEqual("skip_anim", WireName.Of(SpeedAction.SkipAnim));
            Assert.AreEqual("speed_tap_no_stock", WireName.Of(SpeedAction.SpeedTapNoStock));
        }

        [Test]
        public void AnimType_WireValues_AreTheSignedOnes()
        {
            Assert.AreEqual("level_intro", WireName.Of(AnimType.LevelIntro));
            Assert.AreEqual("gameplay", WireName.Of(AnimType.Gameplay));
            Assert.AreEqual("level_result", WireName.Of(AnimType.LevelResult));
            Assert.AreEqual("reward", WireName.Of(AnimType.Reward));
            Assert.AreEqual("chest", WireName.Of(AnimType.Chest));
            Assert.AreEqual("other", WireName.Of(AnimType.Other));
        }

        [Test]
        public void PopupKind_WireValues_AreTheSignedOnes()
        {
            Assert.AreEqual("offer", WireName.Of(PopupKind.Offer));
            Assert.AreEqual("tutorial", WireName.Of(PopupKind.Tutorial));
            Assert.AreEqual("other", WireName.Of(PopupKind.Other));
        }
    }
}
