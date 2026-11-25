using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Số thứ tự trong phiên của các mốc ad (1.5.5) — *"eCPM đổi theo index này"*. Bốn sổ riêng,
    /// đếm theo (phiên × format), reset khi `SessionId` đổi.
    /// </summary>
    public class AdSessionCounterStateTests
    {
        private const int S1 = 7;
        private const int S2 = 8;

        [Test]
        public void EachMark_HasItsOwnCounter()
        {
            var state = new AdSessionCounterState();

            Assert.AreEqual(1, state.NextRequest(AdType.Interstitial, S1));
            Assert.AreEqual(1, state.NextLoad(AdType.Interstitial, S1));
            Assert.AreEqual(1, state.NextLoadSuccess(AdType.Interstitial, S1));
            Assert.AreEqual(1, state.NextImpression(AdType.Interstitial, S1),
                "Bốn sổ độc lập: gọi load không được đẩy số của impression");
        }

        [Test]
        public void Counters_StartAtOne_AndGoUp()
        {
            var state = new AdSessionCounterState();

            Assert.AreEqual(1, state.NextLoad(AdType.Reward, S1));
            Assert.AreEqual(2, state.NextLoad(AdType.Reward, S1));
            Assert.AreEqual(3, state.NextLoad(AdType.Reward, S1));
        }

        [Test]
        public void EachFormat_CountsSeparately()
        {
            // Banner tự refresh vài chục lần: một sổ chung là số của inter/reward thành vô nghĩa
            var state = new AdSessionCounterState();
            state.NextImpression(AdType.Banner, S1);
            state.NextImpression(AdType.Banner, S1);
            state.NextImpression(AdType.Banner, S1);

            Assert.AreEqual(1, state.NextImpression(AdType.Interstitial, S1));
        }

        [Test]
        public void NewSession_ResetsEveryCounter()
        {
            var state = new AdSessionCounterState();
            state.NextLoad(AdType.Interstitial, S1);
            state.NextLoad(AdType.Interstitial, S1);
            state.NextImpression(AdType.Interstitial, S1);

            // app vào nền rồi quay lại = phiên mới, mà singleton vẫn sống → so SessionId
            Assert.AreEqual(1, state.NextLoad(AdType.Interstitial, S2));
            Assert.AreEqual(1, state.NextImpression(AdType.Interstitial, S2));
            Assert.AreEqual(S2, state.SessionId);
        }

        [Test]
        public void LoadSuccessIndex_CountsOnlySuccesses()
        {
            // Đường thật: AdRequestService chỉ gọi NextLoadSuccess khi success = true
            var state = new AdSessionCounterState();

            state.NextLoad(AdType.Reward, S1);                              // fail
            state.NextLoad(AdType.Reward, S1);                              // fail
            var thirdCall = state.NextLoad(AdType.Reward, S1);              // ok
            var firstAdInStore = state.NextLoadSuccess(AdType.Reward, S1);

            Assert.AreEqual(3, thirdCall, "Gọi lần thứ 3");
            Assert.AreEqual(1, firstAdInStore, "Nhưng là ad ĐẦU TIÊN vào kho của phiên");
        }

        [Test]
        public void ImpressionEcpm_IsRevenueTimesThousand()
        {
            var state = new AdSessionCounterState();

            state.RecordImpressionRevenue(0.0542, S1);

            Assert.AreEqual(54.2, state.FirstAdEcpm(S1) ?? 0, 1e-9,
                "adRev là tiền của MỘT lượt, eCPM là USD/1000 lượt");
            Assert.AreEqual(54.2, state.PreviousAdEcpm(S1) ?? 0, 1e-9);
        }

        [Test]
        public void FirstAdEcpm_StaysOnTheFirstImpression()
        {
            var state = new AdSessionCounterState();

            state.RecordImpressionRevenue(0.01, S1);
            state.RecordImpressionRevenue(0.09, S1);

            Assert.AreEqual(10d, state.FirstAdEcpm(S1) ?? 0, 1e-9, "Lượt đầu đã chốt, không đổi");
            Assert.AreEqual(90d, state.PreviousAdEcpm(S1) ?? 0, 1e-9, "Lượt trước là lượt mới nhất");
        }

        [Test]
        public void FirstImpressionWithoutRevenue_DoesNotPromoteTheSecondOne()
        {
            // Chỗ này là lý do `_firstRecorded` phải tách khỏi giá trị: lọc theo "số dương" thì lượt
            // thứ hai leo lên làm lượt đầu, tức khai sai một SỰ KIỆN chứ không chỉ sai một con số.
            var state = new AdSessionCounterState();

            state.RecordImpressionRevenue(0d, S1);
            state.RecordImpressionRevenue(0.05, S1);

            Assert.IsNull(state.FirstAdEcpm(S1), "Lượt đầu không có tiền ⇒ VẮNG, không mượn lượt sau");
            Assert.AreEqual(50d, state.PreviousAdEcpm(S1) ?? 0, 1e-9);
        }

        [Test]
        public void PreviousAdEcpm_IsOverwrittenEvenByUnknown()
        {
            var state = new AdSessionCounterState();

            state.RecordImpressionRevenue(0.05, S1);
            state.RecordImpressionRevenue(0d, S1);

            Assert.IsNull(state.PreviousAdEcpm(S1),
                "Cột nói 'lượt NGAY TRƯỚC', không phải 'lượt có giá gần nhất'");
            Assert.AreEqual(50d, state.FirstAdEcpm(S1) ?? 0, 1e-9, "Lượt đầu không bị ảnh hưởng");
        }

        [Test]
        public void ImpressionsSoFar_ReadsWithoutCounting()
        {
            var state = new AdSessionCounterState();

            Assert.AreEqual(0, state.ImpressionsSoFar(AdType.Interstitial, S1), "Chưa chiếu gì");

            state.NextImpression(AdType.Interstitial, S1);
            state.NextImpression(AdType.Interstitial, S1);

            Assert.AreEqual(2, state.ImpressionsSoFar(AdType.Interstitial, S1));
            Assert.AreEqual(2, state.ImpressionsSoFar(AdType.Interstitial, S1), "Đọc không được tăng sổ");
            Assert.AreEqual(0, state.ImpressionsSoFar(AdType.Reward, S1), "Đếm riêng theo format");
            Assert.AreEqual(0, state.ImpressionsSoFar(AdType.Interstitial, S2), "Phiên khác thì về 0");
        }

        [Test]
        public void NewSession_ResetsBothEcpms()
        {
            var state = new AdSessionCounterState();
            state.RecordImpressionRevenue(0.05, S1);

            Assert.IsNull(state.FirstAdEcpm(S2), "Phiên khác thì đọc ra VẮNG, không trả số phiên cũ");
            Assert.IsNull(state.PreviousAdEcpm(S2));

            state.NextLoad(AdType.Interstitial, S2);                        // mốc nào cũng đổi phiên được

            Assert.IsNull(state.FirstAdEcpm(S2));
            Assert.IsNull(state.PreviousAdEcpm(S2));
        }
    }
}
