using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>Hợp đồng đợt 3 S2 (đợt 1): dấu vân tay lỗi, chống ngập, dấu vòng đời.</summary>
    public class AppIssueTests
    {
        private const string STACK =
            "Game.Board.PlaceTile (System.Int32 x, System.Int32 y) (at Assets/Game/Board.cs:120)\n" +
            "Game.Board`1[T].Tick () (at Assets/Game/Board.cs:40)\n" +
            "UnityEngine.Events.InvokableCall.Invoke () [0x00010] in <abc123>:0\n";

        [Test]
        public void Fingerprint_TypeTopFrameAndStableHash()
        {
            var (type, top, hash) = ExceptionFingerprint.From("NullReferenceException: Object reference not set", STACK);
            Assert.AreEqual("NullReferenceException", type);
            Assert.AreEqual("Game.Board.PlaceTile", top, "bỏ tham số, đường dẫn, số dòng");
            Assert.AreEqual(16, hash.Length);

            // Cùng lỗi ở bản build khác (số dòng, đường dẫn, địa chỉ khác) ⇒ cùng hash
            const string otherBuild =
                "Game.Board.PlaceTile (System.Int32 x, System.Int32 y) (at Assets/Game/Board.cs:999)\n" +
                "Game.Board`1[T].Tick () (at Assets/Game/Board.cs:7)\n" +
                "UnityEngine.Events.InvokableCall.Invoke () [0x000ff] in <zzz>:0\n";
            Assert.AreEqual(hash, ExceptionFingerprint.From("NullReferenceException: other message", otherBuild).hash);

            Assert.AreNotEqual(hash, ExceptionFingerprint.From("ArgumentException: x", STACK).hash, "khác loại lỗi");
        }

        [Test]
        public void Fingerprint_TopFrame_SkipsUnityAndSystem()
        {
            const string stack = "System.Linq.Enumerable.First ()\nUnityEngine.Debug.Log ()\nShop.Buy () (at a.cs:1)\n";
            Assert.AreEqual("Shop.Buy", ExceptionFingerprint.From("E: m", stack).topFrame);
            Assert.IsNull(ExceptionFingerprint.From("E: m", "System.Threading.Thread.Run ()").topFrame);
        }

        [Test]
        public void Fnv1a64_KnownVector()
        {
            // Vector chuẩn FNV-1a 64: "a" ⇒ af63dc4c8601ec8c
            Assert.AreEqual("af63dc4c8601ec8c", ExceptionFingerprint.Fnv1a64("a"));
        }

        [Test]
        public void RateLimit_OnePer60s_TenPerSession_SuppressedGoesOnNextRow()
        {
            var limiter = new AppIssueRateLimiter();
            Assert.IsTrue(limiter.TryPass("freeze", 0).allowed);
            Assert.IsFalse(limiter.TryPass("freeze", 10_000).allowed);
            Assert.IsFalse(limiter.TryPass("freeze", 20_000).allowed);
            var next = limiter.TryPass("freeze", 61_000);
            Assert.IsTrue(next.allowed);
            Assert.AreEqual(2, next.suppressed);

            Assert.IsTrue(limiter.TryPass("exception:abc", 10_000).allowed, "khoá khác không bị chặn chung");

            var many = new AppIssueRateLimiter();
            var sent = 0;
            for (var i = 0; i < 20; i++)
                if (many.TryPass("k", i * 61_000L).allowed) sent++;
            Assert.AreEqual(10, sent);
        }

        [Test]
        public void PrevExit_OnlyAbnormal_InWave1()
        {
            Assert.IsNull(LifecycleMark.Decide(null), "lần đầu có dấu: không biết lần trước");
            Assert.AreEqual(PrevEndType.Abnormal, LifecycleMark.Decide(new LifecycleMark { sessionUid = "s" }));
            Assert.IsNull(LifecycleMark.Decide(new LifecycleMark { sessionUid = "s", backgrounded = true }),
                "nằm nền rồi bị giết: đợt 2 mới biết lý do");
            Assert.IsNull(LifecycleMark.Decide(new LifecycleMark { sessionUid = "s", clean = true }));
        }

        [Test]
        public void Enums_WireValues()
        {
            Assert.AreEqual("freeze_long", WireName.Of(AppIssueKind.FreezeLong));
            Assert.AreEqual("prev_exit", WireName.Of(AppIssueKind.PrevExit));
            Assert.AreEqual("memory_critical", WireName.Of(AppIssueKind.MemoryCritical));
            Assert.AreEqual("unity_low_memory", WireName.Of(MemorySignalSource.UnityLowMemory));
            Assert.AreEqual("background_then_killed", WireName.Of(PrevEndType.BackgroundThenKilled));
        }
    }
}
