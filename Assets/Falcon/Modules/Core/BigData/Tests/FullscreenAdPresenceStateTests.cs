using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    public class FullscreenAdPresenceStateTests
    {
        private const long T0 = 5_000L;

        [Test]
        public void NeverShown_IsNotAdOnset()
        {
            Assert.IsFalse(new FullscreenAdPresenceState().IsAdOnset(T0));
        }

        [Test]
        public void PauseRightAfterShow_IsAdOnset()
        {
            var state = new FullscreenAdPresenceState();
            state.MarkShown(T0);
            Assert.IsTrue(state.IsAdOnset(T0 + 400));
            Assert.IsTrue(state.IsAdOnset(T0 + FullscreenAdPresenceState.ONSET_WINDOW_MILLIS));
        }

        [Test]
        public void PauseLongAfterShow_IsNotAdOnset_SoAMissingCloseDoesNotStick()
        {
            // Mediation quên OnClosed: mọi pause về sau vẫn phải là pause thật.
            var state = new FullscreenAdPresenceState();
            state.MarkShown(T0);
            Assert.IsFalse(state.IsAdOnset(T0 + FullscreenAdPresenceState.ONSET_WINDOW_MILLIS + 1));
        }

        [Test]
        public void AfterCloseOrShowFailure_IsNotAdOnset()
        {
            var state = new FullscreenAdPresenceState();
            state.MarkShown(T0);
            state.MarkEnded();
            Assert.IsFalse(state.IsAdOnset(T0 + 100));
        }

        [Test]
        public void ClockGoingBackwards_IsNotAdOnset()
        {
            var state = new FullscreenAdPresenceState();
            state.MarkShown(T0);
            Assert.IsFalse(state.IsAdOnset(T0 - 1));
        }

        [Test]
        public void OnlyInterstitialRewardAppOpen_AreFullscreen()
        {
            Assert.IsTrue(FullscreenAdPresenceState.IsFullscreen(AdType.Interstitial));
            Assert.IsTrue(FullscreenAdPresenceState.IsFullscreen(AdType.Reward));
            Assert.IsTrue(FullscreenAdPresenceState.IsFullscreen(AdType.AppOpen));
            Assert.IsFalse(FullscreenAdPresenceState.IsFullscreen(AdType.Banner));
            Assert.IsFalse(FullscreenAdPresenceState.IsFullscreen(AdType.Native));
            Assert.IsFalse(FullscreenAdPresenceState.IsFullscreen(AdType.MREC));
            Assert.IsFalse(FullscreenAdPresenceState.IsFullscreen(AdType.CollapsibleBanner));
        }
    }
}
