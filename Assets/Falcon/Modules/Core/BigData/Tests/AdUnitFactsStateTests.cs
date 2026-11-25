using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Sổ sự thật theo AD UNIT (1.5.5): mediation chỉ nói network thắng / bậc / giá sàn / eCPM ở mốc
    /// load, nên dòng impression phải lấy lại từ đây — nhưng CHỈ theo đúng unit của nó.
    /// <br/>Luật monetize 26/10 là nhiều floor load song song rồi GIỮ, chỉ một cái được chiếu, nên
    /// "lần load gần nhất" thường là unit KHÁC unit đang chiếu. Mấy test dưới canh đúng chỗ đó.
    /// </summary>
    public class AdUnitFactsStateTests
    {
        private const long T0 = 1_000L;

        private static AdLoadResultParam Ok(string unit, string net = "applovin", double? ecpm = 12.5,
            double? tier = 1.5, double? floor = 0.8) => new()
        {
            adType = AdType.Interstitial, adUnitId = unit, success = true,
            networkName = net, estimatedEcpm = ecpm, tier = tier, floor = floor
        };

        [Test]
        public void Record_IgnoresFailedAttempt()
        {
            // Lần fail không có network thắng và không có giá — ghi vào là xoá sự thật cũ bằng null
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_a"), T0);
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = false, errorMess = "no_fill"
            }, T0 + 1);

            Assert.AreEqual("applovin", state.Get("unit_a").adNetwork);
            Assert.AreEqual(12.5, state.Get("unit_a").estimatedEcpm);
        }

        [Test]
        public void Record_NullFromNewAttempt_DoesNotEraseOldFact()
        {
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_a"), T0);
            state.Record(Ok("unit_a", net: null, ecpm: null, tier: null, floor: null), T0 + 1);

            var facts = state.Get("unit_a");
            Assert.AreEqual("applovin", facts.adNetwork);
            Assert.AreEqual(12.5, facts.estimatedEcpm);
            Assert.AreEqual(1.5, facts.tier);
        }

        [Test]
        public void Record_NonPositiveEcpm_IsUnknownNotZero()
        {
            // Mediation trả 0 (hoặc -1) cho ca không có số liệu — ghi 0 lên dây là khai giá trị giả
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_a", ecpm: 0), T0);
            Assert.IsNull(state.Get("unit_a").estimatedEcpm);

            state.Record(Ok("unit_b", ecpm: -1), T0);
            Assert.IsNull(state.Get("unit_b").estimatedEcpm);
        }

        [Test]
        public void ApplyTo_FillsOnlyWhatTheMarkLeftBlank()
        {
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_a"), T0);

            var param = new AdParam { adUnitId = "unit_a", adNetwork = "admob" };
            Assert.IsTrue(state.ApplyTo(param));

            Assert.AreEqual("admob", param.adNetwork, "Mốc tự nhập thì THẮNG");
            Assert.AreEqual(1.5, param.tier, "Bỏ trống thì lấy từ sổ");
            Assert.AreEqual(0.8, param.floor);
            Assert.IsNull(param.estimatedEcpm,
                "Dòng impression KHÔNG chở eCPM: đã có adRev là tiền thật của đúng lượt đó");
        }

        [Test]
        public void ApplyTo_WithoutAdUnitId_GuessesNothing()
        {
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_a"), T0);

            var param = new AdParam();
            Assert.IsFalse(state.ApplyTo(param));
            Assert.IsNull(param.adNetwork);
            Assert.IsNull(param.tier);
        }

        [Test]
        public void ApplyTo_OtherUnitInSameWave_DoesNotLeak()
        {
            // Ca monetize: unit floor cao load xong sau, nhưng cái ĐANG CHIẾU là unit floor thấp
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_low", net: "unity", ecpm: 3.0), T0);
            state.Record(Ok("unit_high", net: "inmobi", ecpm: 30.0), T0 + 5);

            var shown = new AdParam { adUnitId = "unit_low" };
            state.ApplyTo(shown);

            Assert.AreEqual("unity", shown.adNetwork, "Không được lấy network của unit load sau");
            Assert.AreEqual(3.0, state.Get("unit_low").estimatedEcpm, "Sổ vẫn giữ eCPM riêng từng unit");
            Assert.AreEqual(30.0, state.Get("unit_high").estimatedEcpm);
        }

        [Test]
        public void Record_OverTheCap_DropsTheOldestFact()
        {
            var state = new AdUnitFactsState();
            for (var i = 0; i < 130; i++) state.Record(Ok($"unit_{i}"), T0 + i);

            Assert.LessOrEqual(state.Count, 128);
            Assert.IsNull(state.Get("unit_0"), "Bản ghi cũ nhất bị bỏ trước");
            Assert.IsNotNull(state.Get("unit_129"));
        }
        [Test]
        public void TierZero_DoesNotEnterTheBook_SoImpressionRowsStayClean()
        {
            // Sổ theo unit là nguồn của tier/floor trên dòng impression — nếu nhận 0 thì dòng
            // impression cũng mang 0 (án máy test 26/10: cả hai dòng đều có tier = 0).
            var state = new AdUnitFactsState();
            state.Record(Ok("unit_a", tier: 0, floor: 0), T0);

            Assert.IsNull(state.Get("unit_a").tier);
            Assert.IsNull(state.Get("unit_a").floor);

            var param = new AdParam { adUnitId = "unit_a" };
            state.ApplyTo(param);
            Assert.IsNull(param.tier, "Dòng impression không được nhận tier = 0");
        }

        [Test]
        public void CandidateId_ReachesTheImpressionRow()
        {
            var state = new AdUnitFactsState();
            var win = Ok("unit_a");
            win.adCandidateId = "cand-1";
            state.Record(win, T0);

            var param = new AdParam { adUnitId = "unit_a" };
            state.ApplyTo(param);

            Assert.AreEqual("cand-1", param.adCandidateId,
                "Có mã trên cả dòng load và dòng impression mới join được từng nhát thử với tiền thật");
        }

        [Test]
        public void CandidateId_IsOverwrittenByNull_UnlikeEveryOtherFact()
        {
            // NGOẠI LỆ cố ý của luật "null không đè": mã cũ là DANH TÍNH của một quảng cáo khác, để
            // lại là dán mã ad trước lên ad sau — một phép join khớp hoàn hảo mà sai. Vắng còn đọc
            // được là "không biết".
            var state = new AdUnitFactsState();
            var first = Ok("unit_a");
            first.adCandidateId = "cand-1";
            state.Record(first, T0);
            state.Record(Ok("unit_a"), T0 + 1);                             // lần sau không khai mã

            Assert.IsNull(state.Get("unit_a").adCandidateId);
            Assert.AreEqual("applovin", state.Get("unit_a").adNetwork,
                "Các field khác vẫn theo luật cũ: null của lần mới không xoá sự thật cũ");
        }

        [Test]
        public void CallerCandidateId_WinsOverTheBook()
        {
            var state = new AdUnitFactsState();
            var win = Ok("unit_a");
            win.adCandidateId = "cand-book";
            state.Record(win, T0);

            var param = new AdParam { adUnitId = "unit_a", adCandidateId = "cand-caller" };
            state.ApplyTo(param);

            Assert.AreEqual("cand-caller", param.adCandidateId, "Mốc tự khai thì THẮNG sổ");
        }

        [Test]
        public void FloorRole_ReachesTheImpressionRow_ButEstimatedEcpmDoesNot()
        {
            // Sàn + vai sàn là CẤU HÌNH của request nên chở sang dòng impression được (bên đọc số xin
            // 28/10). eCPM ước tính thì không: ở đó nó bằng adRev × 1000, phía đọc tự nhân.
            var state = new AdUnitFactsState();
            var win = Ok("unit_a");
            win.ecpmFloor = 2.5;
            win.ecpmFloorType = "base";
            win.ecpmFloorPercentile = "P10";
            state.Record(win, T0);

            var param = new AdParam { adUnitId = "unit_a" };
            state.ApplyTo(param);

            Assert.AreEqual(2.5, param.ecpmFloor);
            Assert.AreEqual("base", param.ecpmFloorType);
            Assert.AreEqual("P10", param.ecpmFloorPercentile);
            Assert.IsNull(param.estimatedEcpm, "eCPM ước tính KHÔNG được chở sang dòng impression");
        }

        [Test]
        public void FloorRole_IsNotInheritedByTheNextAdOfTheSameUnit()
        {
            // Án thật: flow chọn sàn chỉ chạy cho quảng cáo ĐẦU phiên, nên lần load sau của cùng unit
            // không khai vai. Nếu sổ giữ vai cũ (luật "null không đè") thì dòng impression của quảng
            // cáo thứ hai mang nhãn `upgrade_higher` của một request chưa từng áp sàn — nhãn bịa, rơi
            // đúng vào tập dòng dùng để chấm personalization.
            var state = new AdUnitFactsState();
            var first = Ok("unit_a");
            first.ecpmFloor = 8.0;
            first.ecpmFloorType = "upgrade_higher";
            first.ecpmFloorPercentile = "P75";
            state.Record(first, T0);
            state.Record(Ok("unit_a"), T0 + 1);                             // lần sau: không khai vai

            var param = new AdParam { adUnitId = "unit_a" };
            state.ApplyTo(param);

            Assert.IsNull(param.ecpmFloorType, "Không được mượn vai của quảng cáo trước");
            Assert.IsNull(param.ecpmFloorPercentile);
            Assert.IsNull(param.ecpmFloor);
            Assert.AreEqual(1.5, param.tier, "Còn CẤU HÌNH của unit thì vẫn giữ — bậc là của unit");
        }

        [Test]
        public void ConsumedAdFacts_LeaveTheSecondImpressionUnknown()
        {
            // Một mã = một quảng cáo = MỘT lượt chiếu. Mediation chiếu lần hai trên cùng unit mà
            // không báo lần load mới (ca V4) thì dòng thứ hai phải VẮNG mã — dùng lại mã cũ là một
            // nhát thử trông như mang về hai lần tiền, và không ai thấy sai.
            var state = new AdUnitFactsState();
            var win = Ok("unit_a");
            win.adCandidateId = "cand-1";
            state.Record(win, T0);

            var first = new AdParam { adUnitId = "unit_a" };
            state.ApplyTo(first);
            state.ConsumeAdFacts("unit_a");

            var second = new AdParam { adUnitId = "unit_a" };
            state.ApplyTo(second);

            Assert.AreEqual("cand-1", first.adCandidateId);
            Assert.IsNull(second.adCandidateId);
            Assert.AreEqual("applovin", state.Get("unit_a").adNetwork,
                "Chỉ mã bị tiêu thụ — network/bậc/giá của unit vẫn còn cho dòng sau");
        }
    }
}
