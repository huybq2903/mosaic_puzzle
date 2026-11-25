using System.Collections.Generic;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Van gộp exposure UI (chốt owner 05/09 — per-lần-hiện CẤM lên wire): trăm lần hiện phải
    /// thành một cụm, tỉ lệ phía server vẫn chính xác vì chỉ là sum/sum.
    /// </summary>
    public class UiExposureStateTests
    {
        [Test]
        public void HundredImpressions_CollapseIntoOneCluster()
        {
            var state = new UiExposureState();
            for (var i = 0; i < 100; i++) state.Record("race_event_entry", UiTally.Impression, null);

            Assert.AreEqual(1, state.Count, "Cùng khoá phải về một cụm");
            Assert.AreEqual(100, state.TakeAll()[0].impressionCount);
        }

        [Test]
        public void DifferentSurfaces_SplitClusters()
        {
            var state = new UiExposureState();
            state.Record("race_event_entry", UiTally.Impression, null);
            state.Record("race_event_popup", UiTally.Impression, null);

            Assert.AreEqual(2, state.Count);
        }

        [Test]
        public void Extras_FirstRecordInClusterWins_WhateverTheAction()
        {
            // Một cụm giờ gồm cả hiện, bấm, đóng — extras là của LẦN GHI ĐẦU, kể cả khi lần đó là bấm.
            var state = new UiExposureState();
            state.Record("entry", UiTally.Click, new Dictionary<string, object> { ["race_id"] = "r1" });
            state.Record("entry", UiTally.Impression, new Dictionary<string, object> { ["race_id"] = "r2" });

            Assert.AreEqual("r1", state.TakeAll()[0].extraMeta["race_id"]);
        }

        [Test]
        public void TakeAll_Clears_NextStretchStartsFresh()
        {
            var state = new UiExposureState();
            state.Record("entry", UiTally.Impression, null);
            state.TakeAll();

            Assert.AreEqual(0, state.Count, "Flush xong kho phải sạch — mỗi stretch một thế hệ cụm");
        }

        [Test]
        public void BlankSurface_IsRejected_AndSaysSo()
        {
            // Giá trị trả về là thứ service dựa vào để quyết có đặt dấu "đang có cụm mở" hay không.
            var state = new UiExposureState();

            Assert.IsFalse(state.Record(null, UiTally.Impression, null));
            Assert.IsFalse(state.Record("", UiTally.Impression, null));
            Assert.IsTrue(state.Record("entry", UiTally.Impression, null), "Ca hợp lệ phải trả true");
            Assert.AreEqual(1, state.Count, "Lần ghi bị từ chối không được vào kho");
        }
    }
}
