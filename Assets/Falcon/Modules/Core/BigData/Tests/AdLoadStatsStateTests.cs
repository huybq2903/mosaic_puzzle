using System.Linq;
using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Van gộp telemetry load v2 (chốt với loader 23/09): trăm attempt thành một bản ghi unit,
    /// nhóm unit của cùng (adType × mediation) thành MỘT dòng mang mảng — bản cũ đẻ ~158
    /// dòng/user/ngày vì mỗi tổ hợp một dòng.
    /// </summary>
    public class AdLoadStatsStateTests
    {
        private static AdLoadResultParam Fail(string unit, double? tier, long loadingMs = 1000)
        {
            return new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = false, adMediation = "Max",
                adUnitId = unit, tier = tier, errorMess = "no fill", loadingMs = loadingMs
            };
        }

        private static AdLoadResultParam Ok(string unit, double? tier, long loadingMs = 800)
        {
            return new AdLoadResultParam
            {
                adType = AdType.Interstitial, success = true, adMediation = "Max",
                adUnitId = unit, tier = tier, networkName = "applovin", loadingMs = loadingMs
            };
        }

        [Test]
        public void HundredRetries_CollapseIntoOneUnitRecord()
        {
            var state = new AdLoadStatsState();
            for (var i = 0; i < 100; i++) state.Record(Fail("unit_a", 5.0));

            Assert.AreEqual(1, state.Count);
            var unit = state.TakeAll(64).Single().units.Single();
            Assert.AreEqual(100, unit.fail);
            Assert.AreEqual(100_000, unit.failMs);
            Assert.AreEqual(0, unit.ok);
        }

        [Test]
        public void SuccessAndFail_ShareOneRecord_NotTwo()
        {
            // Khác bản cũ: kết cục KHÔNG còn là khoá gộp — ok/fail nằm chung một bản ghi unit
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_a", 5.0));
            state.Record(Ok("unit_a", 5.0));

            Assert.AreEqual(1, state.Count);
            var unit = state.TakeAll(64).Single().units.Single();
            Assert.AreEqual(1, unit.ok);
            Assert.AreEqual(1, unit.fail);
            Assert.AreEqual(800, unit.okMs);
            Assert.AreEqual(1000, unit.failMs);
            Assert.AreEqual("applovin", unit.networkName, "Network của lần thắng không bị lần fail xoá");
        }

        [Test]
        public void DifferentTier_SplitsRecords_ButStaysOneLog()
        {
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_a", 5.0));
            state.Record(Fail("unit_b", 3.0));

            var batches = state.TakeAll(64);
            Assert.AreEqual(1, batches.Count, "Cùng (adType × mediation) thì chung MỘT dòng log");
            Assert.AreEqual(2, batches[0].units.Count, "Mỗi unit/bậc một phần tử trong mảng");
        }

        [Test]
        public void DifferentTypeOrMediation_SplitLogs()
        {
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_a", 5.0));

            var otherType = Fail("unit_a", 5.0);
            otherType.adType = AdType.Reward;
            state.Record(otherType);

            var otherMediation = Fail("unit_a", 5.0);
            otherMediation.adMediation = "Admob";
            state.Record(otherMediation);

            Assert.AreEqual(3, state.TakeAll(64).Count,
                "adType và mediation nằm ở cấp trên của bản tin nên phải tách dòng");
        }

        [Test]
        public void MissingTier_AndTierZero_BothMeanAbsent()
        {
            // Điều kiện loader 23/09 là "vắng tier = KHÔNG đặt giá sàn, khác hẳn đặt sàn 0" — nhưng
            // thực địa 26/10 cho thấy mediation gửi tier = 0 cho đúng mấy unit KHÔNG đặt sàn, nên
            // 1.5.6 chuẩn hoá số không dương về VẮNG ở cả ba đường. Phân biệt "có đặt sàn hay
            // không" từ nay đọc ở ecpmFloor, KHÔNG đọc ở tier.
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_admob", null));
            state.Record(Fail("unit_max", 0d));

            var units = state.TakeAll(64).Single().units;
            Assert.IsNull(units.Single(u => u.adUnitId == "unit_admob").tier);
            Assert.IsNull(units.Single(u => u.adUnitId == "unit_max").tier, "0 cũng là KHÔNG BIẾT");
            Assert.AreEqual(2, units.Count, "Hai UNIT khác nhau thì vẫn là hai bản ghi");
        }

        [Test]
        public void FloorUsd_IsNotAKey_AveragedInsteadOfSplitting()
        {
            // 1.4.1: floor (USD) là SỐ ĐO, không phải nhãn. AdMob tính floor = revenue × hệ số nên
            // mỗi attempt một giá trị — để trong khoá là nhánh đó không gộp được dòng nào.
            var state = new AdLoadStatsState();
            var a = Fail("unit_a", 3.0);
            a.floor = 1.0;
            var b = Fail("unit_a", 3.0);
            b.floor = 2.0;
            state.Record(a);
            state.Record(b);

            Assert.AreEqual(1, state.Count, "Giá sàn thật khác nhau KHÔNG được tách bản ghi");
            var unit = state.TakeAll(64).Single().units.Single();
            Assert.AreEqual(2, unit.fail);
            Assert.AreEqual(3.0, unit.tier);
            Assert.AreEqual(1.5, unit.FloorAverage, "Báo trung bình, cùng khuôn với okMs/failMs");
        }

        [Test]
        public void FloorUsd_StaysNull_WhenMediationNeverKnowsThePrice()
        {
            // MAX không biết giá sàn thật -> không lần nào có mẫu -> trường vắng, không phải 0
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_a", 3.0));
            state.Record(Ok("unit_a", 3.0));

            Assert.IsNull(state.TakeAll(64).Single().units.Single().FloorAverage);
        }

        [Test]
        public void FloorAverage_IgnoresAttemptsWithoutPrice()
        {
            // Lần không có giá không được tính là 0 — nó chỉ là "không biết"
            var state = new AdLoadStatsState();
            var withPrice = Fail("unit_a", 3.0);
            withPrice.floor = 4.0;
            state.Record(withPrice);
            state.Record(Fail("unit_a", 3.0));

            var unit = state.TakeAll(64).Single().units.Single();
            Assert.AreEqual(2, unit.fail);
            Assert.AreEqual(4.0, unit.FloorAverage, "Trung bình của đúng một mẫu có giá");
        }

        [Test]
        public void OverTheCap_SplitsIntoMoreLogs_NothingDropped()
        {
            // Vượt trần thì CHIA LÔ chứ không cắt bỏ: mọi số đều cộng được nên tổng giữ nguyên
            var state = new AdLoadStatsState();
            for (var i = 0; i < 10; i++) state.Record(Fail($"unit_{i}", i));

            var batches = state.TakeAll(4);

            Assert.AreEqual(3, batches.Count, "10 unit, trần 4 → 4 + 4 + 2");
            Assert.AreEqual(10, batches.Sum(b => b.units.Count), "Không phần tử nào bị bỏ");
            Assert.AreEqual(10, batches.Sum(b => b.units.Sum(u => u.fail)), "Tổng số lần load giữ nguyên");
        }

        [Test]
        public void LastError_Represents_TheRecord()
        {
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_a", 5.0));
            var last = Fail("unit_a", 5.0);
            last.errorMess = "timeout";
            state.Record(last);

            Assert.AreEqual("timeout", state.TakeAll(64).Single().units.Single().lastErrorMess,
                "Error CUỐI đại diện bản ghi — làm khoá gộp thì message dài tách vô hạn");
        }

        [Test]
        public void TakeAll_Clears_NextStretchStartsFresh()
        {
            var state = new AdLoadStatsState();
            state.Record(Fail("unit_a", 5.0));
            state.TakeAll(64);

            Assert.AreEqual(0, state.Count, "Flush xong kho phải sạch — mỗi stretch một thế hệ cụm");
        }
        // ---- 1.5.5: eCPM + thuật toán ----

        [Test]
        public void Ecpm_IsAveragedOverAttemptsThatHadIt()
        {
            var state = new AdLoadStatsState();
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, estimatedEcpm = 10
            });
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, estimatedEcpm = 20
            });
            // lần không có eCPM KHÔNG vào mẫu (nếu vào thì trung bình thành 10)
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, estimatedEcpm = null
            });

            var unit = state.TakeAll(64).Single().units.Single();
            Assert.AreEqual(15, unit.EcpmAverage);
        }

        [Test]
        public void NonPositiveEcpm_IsNotSampled()
        {
            var state = new AdLoadStatsState();
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Reward, adUnitId = "unit_a", success = true, estimatedEcpm = 0
            });

            Assert.IsNull(state.TakeAll(64).Single().units.Single().EcpmAverage);
        }

        [Test]
        public void TwoAlgorithms_DoNotMergeIntoOneRow()
        {
            // Nếu gộp thì chính câu hỏi "thuật toán nào load tốt hơn" không trả lời được
            var state = new AdLoadStatsState();
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, adAlgorithm = "multicall_3"
            });
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, adAlgorithm = "multicall_5"
            });

            var batches = state.TakeAll(64);
            Assert.AreEqual(2, batches.Count);
            CollectionAssert.AreEquivalent(
                new[] { "multicall_3", "multicall_5" }, batches.Select(b => b.adAlgorithm).ToList());
        }

        [Test]
        public void TierZeroAndNoTier_AreTheSameGroup_AndTheKeyStaysAbsent()
        {
            // Caller điền 0 cho unit không đặt sàn thì phải gộp cùng nhóm với "không gửi tier",
            // và khoá tier không được lên dây mang giá trị 0 (án máy test 26/10).
            var state = new AdLoadStatsState();
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, tier = 0
            });
            state.Record(new AdLoadResultParam
            {
                adType = AdType.Interstitial, adUnitId = "unit_a", success = true, tier = null
            });

            var batch = state.TakeAll(64).Single();
            Assert.AreEqual(1, batch.units.Count, "Hai lần phải nằm CÙNG một nhóm");
            Assert.IsNull(batch.units.Single().tier, "Khoá tier phải VẮNG, không phải 0");
            Assert.AreEqual(2, batch.units.Single().ok);
        }

        [Test]
        public void DifferentFloorRole_StaysInItsOwnRecord()
        {
            // Gộp hai vai vào một dòng là xoá đúng câu hỏi mà cột sinh ra để trả lời: vai nào fill
            // tốt hơn. Cùng vết sẹo của adAlgorithm ở 1.5.5.
            var state = new AdLoadStatsState();
            var baseFloor = Ok("unit_a", 5.0);
            baseFloor.ecpmFloorType = "base";
            var upgrade = Fail("unit_a", 5.0);
            upgrade.ecpmFloorType = "upgrade_higher";
            state.Record(baseFloor);
            state.Record(upgrade);

            var units = state.TakeAll(64).Single().units;
            Assert.AreEqual(2, units.Count);
            Assert.AreEqual(1, units.Count(u => u.ecpmFloorType == "base" && u.ok == 1));
            Assert.AreEqual(1, units.Count(u => u.ecpmFloorType == "upgrade_higher" && u.fail == 1));
        }

        [Test]
        public void BlankAndNullFloorRole_ShareOneRecord()
        {
            // Hai nhánh mediation, một để null một để "", là CÙNG một sự thật — hai dòng thì không
            // cộng được với nhau nữa.
            var state = new AdLoadStatsState();
            var blank = Ok("unit_a", 5.0);
            blank.ecpmFloorType = "  ";
            state.Record(blank);
            state.Record(Ok("unit_a", 5.0));

            var unit = state.TakeAll(64).Single().units.Single();
            Assert.IsNull(unit.ecpmFloorType);
            Assert.AreEqual(2, unit.ok);
        }

        [Test]
        public void Percentile_KeepsTheLastValue_WithoutSplittingTheGroup()
        {
            var state = new AdLoadStatsState();
            var first = Ok("unit_a", 5.0);
            first.ecpmFloorType = "base";
            first.ecpmFloorPercentile = "P10";
            var second = Ok("unit_a", 5.0);
            second.ecpmFloorType = "base";
            second.ecpmFloorPercentile = "P15";
            state.Record(first);
            state.Record(second);

            Assert.AreEqual(1, state.Count, "Percentile KHÔNG làm khoá: trong một vai nó là hằng");

            var unit = state.TakeAll(64).Single().units.Single();
            Assert.AreEqual("P15", unit.ecpmFloorPercentile);
            Assert.AreEqual(2, unit.ok);
        }
    }
}
