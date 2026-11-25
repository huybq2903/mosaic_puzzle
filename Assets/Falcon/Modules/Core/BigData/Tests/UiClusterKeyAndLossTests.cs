using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Hợp đồng cụm Ui ký với loader 09/11 (<c>f_sdk_ui_exposure</c> + <c>f_sdk_ui_flush_lost</c>):
    /// <b>một cụm một dòng</b>, mỗi hành vi một cột đếm; bất biến <c>adNotReady ≤ impression</c>
    /// trên từng dòng; dấu đo phần mất là event riêng không field nào.
    /// <br/>Mấy test dựng log thì cần Unity runtime (ctor log chạm singleton qua field initializer).
    /// </summary>
    public class UiClusterKeyAndLossTests
    {
        private const string SURFACE = "race_event_popup";

        [Test]
        public void DifferentActions_SameKey_AreONERow()
        {
            // Đây là toàn bộ lý do của hình dạng mới: hiện/bấm/đóng không còn là ba dòng lặp khoá.
            var state = new UiExposureState();

            state.Record(SURFACE, UiTally.Impression, null, null, "auto_on_home");
            state.Record(SURFACE, UiTally.Impression, null, null, "auto_on_home");
            state.Record(SURFACE, UiTally.Click, null, null, "auto_on_home");
            state.Record(SURFACE, UiTally.Dismiss, null, null, "auto_on_home");

            Assert.AreEqual(1, state.Count);
            var entry = state.TakeAll()[0];
            Assert.AreEqual(2, entry.impressionCount);
            Assert.AreEqual(1, entry.clickCount);
            Assert.AreEqual(1, entry.dismissCount);
            Assert.AreEqual(0, entry.adNotReadyCount);
        }

        [Test]
        public void SameSurface_DifferentTrigger_DoesNotMerge()
        {
            // "popup bật tự động" và "popup do người chơi bấm" là hai hành vi; gộp một dòng thì tỉ
            // lệ của cả hai đều vô nghĩa.
            var state = new UiExposureState();

            state.Record(SURFACE, UiTally.Impression, null, null, "auto_on_home");
            state.Record(SURFACE, UiTally.Impression, null, null, "from_shop_button");

            Assert.AreEqual(2, state.Count, "Hai trigger phải là hai cụm");
        }

        [Test]
        public void AdNotReady_IsOneRecord_ThatCountsBothColumns()
        {
            // 🔴 Bất biến ký 09/11: ad_not_ready_count ≤ impression_count trên TỪNG dòng. Nó đứng được
            // vì OnImpressionAdNotReady là MỘT lần ghi cộng cả hai cột — hai lần ghi riêng thì van
            // xả sớm có thể cắt giữa và đẩy hai số vào hai dòng.
            var state = new UiExposureState();

            state.Record(SURFACE, UiTally.ImpressionAdNotReady, null);

            var entry = state.TakeAll()[0];
            Assert.AreEqual(1, entry.impressionCount, "Nút chưa sẵn QC vẫn là MỘT lần hiện");
            Assert.AreEqual(1, entry.adNotReadyCount);
        }

        [Test]
        public void AdNotReady_NeverExceedsImpression_WhateverTheMix()
        {
            var state = new UiExposureState();
            var tallies = new[]
            {
                UiTally.ImpressionAdNotReady, UiTally.Click, UiTally.Impression, UiTally.Dismiss,
                UiTally.ImpressionAdNotReady, UiTally.Click, UiTally.Click
            };
            foreach (var tally in tallies) state.Record(SURFACE, tally, null);

            foreach (var entry in state.TakeAll())
                Assert.LessOrEqual(entry.adNotReadyCount, entry.impressionCount);
        }

        [Test]
        public void ReviveOffer_IsTheRegisteredSurfaceName()
        {
            // Tên dùng chung mọi game — kho tính "tỉ lệ nhận hồi sinh" chéo game trên đúng chữ này.
            Assert.AreEqual("revive_offer", FUiSurface.ReviveOffer);
            Assert.AreEqual("booster_offer", FUiSurface.BoosterOffer);
        }

        [Test]
        public void TheFourCounts_AndTrigger_AreReservedOnTheWire()
        {
            foreach (var key in new[] { "impressionCount", "click_count", "dismissCount", "ad_not_ready_count", "trigger" })
                Assert.IsTrue(FWireReservedKeys.IsReserved(key), key);
        }

        [Test]
        public void ExposureLog_SendsZeroCounts_AndNoLevelColumn()
        {
            // Cần Unity runtime. Một cụm chỉ có lần bấm (lần hiện rơi vào cửa sổ xả trước) là bình
            // thường — impressionCount = 0 phải LÊN DÂY: 0 là số thật của cửa sổ xả, không phải vắng.
            var state = new UiExposureState();
            state.Record(SURFACE, UiTally.Click, null, null, "after_level_win");

            var log = new FUiExposureLog(state.TakeAll()[0]);
            var dict = log.ToDictionary();

            Assert.AreEqual("f_sdk_ui_exposure", log.Event);
            Assert.AreEqual(0, dict["impressionCount"]);
            Assert.AreEqual(1, dict["clickCount"]);
            Assert.AreEqual(0, dict["dismissCount"]);
            Assert.AreEqual(0, dict["adNotReadyCount"]);
            Assert.AreEqual("after_level_win", dict["trigger"]);
            Assert.AreEqual(SURFACE, dict["surfaceId"]);
            Assert.IsFalse(dict.ContainsKey("currentLevel"),
                "Tiến độ đọc maxPassedLevel chung của mọi dòng — dòng Ui không mang cột level thứ hai");
            Assert.IsFalse(dict.ContainsKey("uiAction"), "Hành vi là cột đếm, không còn là một field");
        }

        [Test]
        public void FlushLostLog_IsItsOwnEvent_WithNoFieldsOfItsOwn()
        {
            // Cần Unity runtime. Dòng này là "một phiên mất" — count(*) là đúng ở event này, nên nó
            // không được mang count, surface hay hành vi nào.
            var log = new FUiFlushLostLog();
            var dict = log.ToDictionary();

            Assert.AreEqual("f_sdk_ui_flush_lost", log.Event);
            foreach (var key in new[] { "count", "surfaceId", "uiAction", "impressionCount", "trigger" })
                Assert.IsFalse(dict.ContainsKey(key), key);
        }
    }
}
