using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B1: gói hiệu năng 5 giây + màn hiện tại + dòng f_sdk_perf_screen.</summary>
    public class PerfPackSurfaceTests
    {
        private const float FRAME_60 = 1f / 60;

        // ---- Cửa sổ 5 giây ----

        [Test]
        public void Window_KeepsOnlyTheLast5Seconds()
        {
            var window = new PerfWindowState();
            window.AddFrame(0.5f);                          // khung chậm, sẽ trôi ra khỏi cửa sổ
            for (var i = 0; i < 60 * 6; i++) window.AddFrame(FRAME_60);

            var pack = window.TakePack();
            Assert.AreEqual(60f, pack.FpsAvg5s.Value, 0.5f);
            Assert.AreEqual(17, pack.FrameWorstMs5s, "khung 500 ms đã ra khỏi cửa sổ 5 giây");
        }

        [Test]
        public void Window_WorstFrame_IsTheMaxInsideTheWindow()
        {
            var window = new PerfWindowState();
            for (var i = 0; i < 30; i++) window.AddFrame(FRAME_60);
            window.AddFrame(0.25f);
            for (var i = 0; i < 30; i++) window.AddFrame(FRAME_60);

            Assert.AreEqual(250, window.TakePack().FrameWorstMs5s);
        }

        [Test]
        public void SinceLast_Counters_ResetOnEachPack_WindowDoesNot()
        {
            var window = new PerfWindowState();
            window.AddFrame(0.2f);
            window.AddMemoryWarning();

            var first = window.TakePack();
            Assert.AreEqual(1, first.FrameDropsSinceLast);
            Assert.AreEqual(1, first.MemWarningsSinceLast);

            var second = window.TakePack();
            Assert.AreEqual(0, second.FrameDropsSinceLast);
            Assert.AreEqual(0, second.MemWarningsSinceLast);
            Assert.AreEqual(200, second.FrameWorstMs5s, "cửa sổ vẫn giữ khung cũ");
        }

        [Test]
        public void Window_FirstFrameAfterResume_AndNotRenderingFrames_AreIgnored()
        {
            var window = new PerfWindowState();
            window.MarkResumed();
            window.AddFrame(0.8f);
            window.AddFrame(3f);
            window.AddFrame(FRAME_60);

            var pack = window.TakePack();
            Assert.AreEqual(0, pack.FrameDropsSinceLast);
            Assert.AreEqual(17, pack.FrameWorstMs5s);
        }

        [Test]
        public void Window_NoFrames_HasNoFps()
        {
            // PerfPackRepository.PutPack dựa vào đây để bỏ CẢ gói khi chưa có khung nào (vắng ≠ 0)
            Assert.IsNull(new PerfWindowState().TakePack().FpsAvg5s);
        }

        // ---- Màn hiện tại ----

        [Test]
        public void Popup_StacksOnTheBaseScreen_ExitReturnsToIt()
        {
            var surface = new SurfaceState();
            surface.Enter("level");
            surface.Enter("popup:revive_offer");
            Assert.AreEqual("popup:revive_offer", surface.Current);
            Assert.AreEqual(PopupKind.Offer, surface.CurrentPopupKind, "hồi sinh SDK tự xếp offer");

            surface.Exit();
            Assert.AreEqual("level", surface.Current);
            Assert.IsNull(surface.CurrentPopupKind);
        }

        [Test]
        public void PopupKind_DefaultsToOther_TutorialMustBeDeclared()
        {
            var surface = new SurfaceState();
            surface.Enter("level");
            surface.Enter("popup:pause");
            Assert.AreEqual(PopupKind.Other, surface.CurrentPopupKind);
            surface.Enter("popup:how_to_play", PopupKind.Tutorial);
            Assert.AreEqual(PopupKind.Tutorial, surface.CurrentPopupKind);
        }

        [Test]
        public void NonPopupEnter_ReplacesBase_AndClearsPopups()
        {
            var surface = new SurfaceState();
            surface.Enter("level");
            surface.Enter("popup:pause");
            surface.Enter("home");
            Assert.AreEqual("home", surface.Current);
            surface.Exit();
            Assert.IsNull(surface.Current);
        }

        [Test]
        public void AdOverlay_ShowsAd_AndResumeClearsIt()
        {
            var surface = new SurfaceState();
            surface.Enter("level");
            surface.SetAdOverlay(true);
            Assert.AreEqual("ad", surface.Current);
            Assert.IsNull(surface.CurrentPopupKind);

            surface.MarkResumed(); // mediation quên OnClosed: quay lại từ nền là QC đã đóng
            Assert.AreEqual("level", surface.Current);
        }

        // ---- Đoạn theo màn ----

        [Test]
        public void LeavingAScreen_ClosesItsSegment_WithItsOwnFrames()
        {
            var surface = new SurfaceState();
            surface.Enter("home");
            for (var i = 0; i < 60; i++) surface.AddFrame(FRAME_60);
            surface.Enter("level");
            surface.AddFrame(0.2f);

            var closed = surface.DrainClosed();
            Assert.AreEqual(1, closed.Count);
            Assert.AreEqual("home", closed[0].Screen);
            Assert.AreEqual(60f, closed[0].Perf.AvgFps.Value, 0.01f);
            Assert.AreEqual(0, closed[0].Perf.FrameDropCount, "khung rớt của level không tính cho home");
            Assert.AreEqual(17, closed[0].Perf.WorstFrameMs);
        }

        [Test]
        public void Background_ClosesTheOpenSegment_AndTheScreenContinues()
        {
            var surface = new SurfaceState();
            surface.Enter("level");
            surface.AddFrame(FRAME_60);
            surface.CloseForBackground();
            surface.MarkResumed();
            surface.AddFrame(FRAME_60);
            surface.Enter("home");

            var closed = surface.DrainClosed();
            Assert.AreEqual(2, closed.Count);
            Assert.AreEqual("level", closed[0].Screen);
            Assert.AreEqual("level", closed[1].Screen);
        }

        [Test]
        public void AdCausedPause_GivesOneAdSegment()
        {
            var surface = new SurfaceState();
            surface.Enter("level");
            surface.SetAdOverlay(true);   // đóng đoạn level
            surface.CloseForBackground(); // QC làm app pause — không đóng đoạn ad ở đây
            surface.MarkResumed();        // đóng đoạn ad

            var closed = surface.DrainClosed();
            Assert.AreEqual(2, closed.Count);
            Assert.AreEqual("level", closed[0].Screen);
            Assert.AreEqual("ad", closed[1].Screen);
        }

        [Test]
        public void UnknownScreen_ProducesNoSegment()
        {
            var surface = new SurfaceState();
            surface.AddFrame(FRAME_60);
            surface.CloseForBackground();
            Assert.AreEqual(0, surface.DrainClosed().Count);
        }

        [Test]
        public void SameScreenAgain_DoesNotCloseTheSegment()
        {
            var surface = new SurfaceState();
            surface.Enter("home");
            surface.Enter("home");
            Assert.AreEqual(0, surface.DrainClosed().Count);
        }
    }
}
