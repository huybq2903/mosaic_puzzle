using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Đường đo phần mất của cụm exposure UI. Hai bất biến ở đây là HAI CÁI BẪY mà bản đầu của lượt
    /// 09/11 đã sập, và cả hai đều khiến phép đo <b>không bao giờ nổ</b> ở đúng ca nó sinh ra để đo:
    /// <br/>1. <c>FDataPool.Save</c> chỉ ghi cache RAM — đĩa chỉ được ghi ở <c>OnPostStop</c> và
    /// nhịp 5 phút, mà ca cần đo CHÍNH LÀ ca <c>OnPostStop</c> không chạy ⇒ phải sync tay.
    /// <br/>2. <c>IPioneer.OnPreContinue</c> KHÔNG chạy ở cold start (<c>AppFlowService</c> return
    /// sớm khi <c>!_gameStop</c>) ⇒ phải soi dấu ở lần chạm đầu tiên của tiến trình, không phải ở
    /// hook resume.
    /// <br/>"Phiên mới" ở đây = <c>UiFlushLossMarker</c> MỚI trên CÙNG một pool — pool đóng vai cái
    /// đĩa sống sót qua kill, đúng khuôn <see cref="FakeDataPool"/>.
    /// </summary>
    public class UiFlushLossMarkerTests
    {
        [Test]
        public void MarkingOpen_IsPushedToDisk_NotLeftInRam()
        {
            // 🔴 Bẫy 1. Không có TrySync ở đây thì dấu chết cùng tiến trình và flush_lost không bao
            // giờ nổ — tức cả tính năng này là một con số 0 im lặng.
            var pool = new FakeDataPool();
            var marker = new UiFlushLossMarker(pool);

            marker.MarkOpen();

            Assert.GreaterOrEqual(pool.SyncCount, 1,
                "Đặt dấu phải đẩy xuống đĩa: ca cần đo là ca OnPostStop không chạy");
        }

        [Test]
        public void MarkingOpenTwiceInOneGeneration_WritesOnce()
        {
            // Dấu đặt theo THẾ HỆ, không theo từng lần hiện — nếu không thì nó thành IO trên đường
            // nóng, đúng cái giá mà "không persist cụm" đã từ chối trả.
            var pool = new FakeDataPool();
            var marker = new UiFlushLossMarker(pool);

            marker.MarkOpen();
            var afterFirst = pool.SyncCount;
            for (var i = 0; i < 10; i++) marker.MarkOpen();

            Assert.AreEqual(afterFirst, pool.SyncCount, "Thế hệ đã đặt dấu thì không ghi lại");
        }

        [Test]
        public void AHardKillWithAnOpenGeneration_IsReportedOnceInTheNextProcess()
        {
            var pool = new FakeDataPool();
            new UiFlushLossMarker(pool).MarkOpen();   // phiên 1 mở cụm rồi bị kill: không ClearMark

            var next = new UiFlushLossMarker(pool);   // phiên 2

            Assert.IsTrue(next.TakeStaleOnce(), "Phiên trước để lại dấu ⇒ phải báo");
            Assert.IsFalse(next.TakeStaleOnce(), "Chỉ báo MỘT lần cho mỗi tiến trình");
        }

        [Test]
        public void ACleanFlush_LeavesNothingBehind_EvenAfterAKill()
        {
            // 🔴 Nửa sau của bẫy 1: xoá dấu cũng phải sync. Dấu đã xuống đĩa mà xoá chỉ trong cache
            // thì một lần kill SAU KHI xả thành công vẫn thấy dấu ⇒ dương tính giả.
            var pool = new FakeDataPool();
            var marker = new UiFlushLossMarker(pool);

            marker.MarkOpen();
            marker.ClearMark();

            Assert.IsFalse(new UiFlushLossMarker(pool).TakeStaleOnce(),
                "Xả sạch rồi thì phiên sau không được báo mất");
        }

        [Test]
        public void AProcessThatNeverOpenedACluster_ReportsNothing()
        {
            Assert.IsFalse(new UiFlushLossMarker(new FakeDataPool()).TakeStaleOnce());
        }

        [Test]
        public void ClearingWithoutMarking_DoesNotTouchDisk()
        {
            // GetLogsOnAppPause chạy ở MỌI lần pause, kể cả phiên không hiện bề mặt nào. Nếu
            // ClearMark cứ ghi đĩa thì entity này trả giá IO cho mọi lần pause của mọi game.
            var pool = new FakeDataPool();

            new UiFlushLossMarker(pool).ClearMark();

            Assert.AreEqual(0, pool.SyncCount);
        }

        [Test]
        public void TakingTheStaleMarkFirst_DoesNotEatTheMarkOfThisGeneration()
        {
            // Thứ tự trong service: TakeStaleOnce TRƯỚC MarkOpen. Đảo lại thì dấu vừa đặt bị chính
            // mình đọc thành dấu của phiên trước — một dòng flush_lost dương tính giả ngay ở lần
            // hiện đầu tiên của mọi phiên.
            var pool = new FakeDataPool();
            var marker = new UiFlushLossMarker(pool);

            Assert.IsFalse(marker.TakeStaleOnce());
            marker.MarkOpen();

            Assert.IsFalse(marker.TakeStaleOnce(), "Đã soi một lần rồi, dấu của chính mình không tính");
            Assert.IsTrue(new UiFlushLossMarker(pool).TakeStaleOnce(),
                "Nhưng dấu đó vẫn còn trên đĩa cho phiên sau — thế hệ này chưa xả");
        }
    }
}
