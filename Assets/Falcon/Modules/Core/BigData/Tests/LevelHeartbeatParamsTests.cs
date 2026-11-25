using System;
using System.Collections.Generic;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 B5: SDK tự bắn heartbeat, game / APS chỉ khai tham số.</summary>
    public class LevelHeartbeatParamsTests
    {
        private const long T0 = 1_700_000_000_000L;

        /// <summary>Filler giả — KHÔNG phải singleton (không rò vào container, xem unity-di-test-leak).</summary>
        private sealed class FakeFiller : ITurnParamsFiller
        {
            public bool InLevel = true;
            public int Calls;

            public bool FillTurnParams(LevelHeartBeatParamV2 param)
            {
                Calls++;
                if (!InLevel) return false;
                param.score = 42;
                param.levelProgress = 60;
                return true;
            }
        }

        [Test]
        public void NoOpenTurn_NoHeartbeat()
        {
            var state = new LevelTurnParamsState();
            Assert.IsNull(state.Build(null, new[] { new FakeFiller() }, T0));
        }

        [Test]
        public void Pushed_IsUsedForTheSameTurn_WithItsPushTime()
        {
            var state = new LevelTurnParamsState();
            state.Push(new LevelHeartBeatParamV2 { movesUsed = 7, levelProgress = 30 }, "turn-1", T0);

            var heartbeat = state.Build("turn-1", null, T0 + 5000);
            Assert.AreEqual(7, heartbeat.movesUsed);
            Assert.AreEqual(30, heartbeat.levelProgress);
            Assert.AreEqual(T0, heartbeat.paramsUpdatedAt, "mốc cập nhật = lúc game đẩy, không phải lúc bắn");
            Assert.AreEqual("turn-1", heartbeat.playTurnId);
        }

        [Test]
        public void Pushed_ForAnotherTurn_IsDropped()
        {
            // Không dán tiến độ lượt cũ lên lượt mới
            var state = new LevelTurnParamsState();
            state.Push(new LevelHeartBeatParamV2 { movesUsed = 7 }, "turn-1", T0);

            var heartbeat = state.Build("turn-2", null, T0 + 5000);
            Assert.IsNull(heartbeat.movesUsed);
            Assert.IsNull(heartbeat.paramsUpdatedAt);
            Assert.IsFalse(heartbeat.ToDictionary().ContainsKey("paramsUpdatedAt"));
        }

        [Test]
        public void Push_WithoutOpenTurn_IsRejected()
        {
            Assert.IsFalse(new LevelTurnParamsState().Push(new LevelHeartBeatParamV2(), null, T0));
        }

        [Test]
        public void Filler_FillsAtFireTime_AndWinsOverPushed()
        {
            var state = new LevelTurnParamsState();
            state.Push(new LevelHeartBeatParamV2 { score = 1, movesUsed = 7 }, "turn-1", T0);

            var heartbeat = state.Build("turn-1", new[] { new FakeFiller() }, T0 + 9000);
            Assert.AreEqual(42, heartbeat.score, "nguồn hỏi lúc bắn mới hơn bản đẩy");
            Assert.AreEqual(7, heartbeat.movesUsed, "field nguồn không điền thì giữ bản đẩy");
            Assert.AreEqual(T0 + 9000, heartbeat.paramsUpdatedAt);
        }

        [Test]
        public void Filler_NotInLevel_LeavesParamsAbsent()
        {
            var state = new LevelTurnParamsState();
            var heartbeat = state.Build("turn-1", new[] { new FakeFiller { InLevel = false } }, T0);
            Assert.IsNull(heartbeat.score);
            Assert.IsNull(heartbeat.levelProgress, "B13: không đo được thì vắng");
            Assert.IsNull(heartbeat.paramsUpdatedAt);
        }

        [Test]
        public void Build_DoesNotMutateThePushedCopy()
        {
            var state = new LevelTurnParamsState();
            var pushed = new LevelHeartBeatParamV2 { boostersUsed = new Dictionary<string, int> { ["hammer"] = 1 } };
            state.Push(pushed, "turn-1", T0);
            pushed.boostersUsed["hammer"] = 99; // game sửa object của mình sau khi đẩy

            var first = state.Build("turn-1", null, T0);
            first.boostersUsed["hammer"] = 5;   // decor / CorrectValues sửa dòng đã dựng
            var second = state.Build("turn-1", null, T0);

            Assert.AreEqual(1, second.boostersUsed["hammer"]);
        }

        [Test]
        public void PushedPlayTurnId_NeverOverridesTheCache()
        {
            var state = new LevelTurnParamsState();
            state.Push(new LevelHeartBeatParamV2 { playTurnId = "stale" }, "turn-1", T0);
            Assert.AreEqual("turn-1", state.Build("turn-1", null, T0).playTurnId);
        }
    }
}
