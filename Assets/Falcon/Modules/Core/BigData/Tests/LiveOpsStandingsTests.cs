using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Bốn field kết quả bảng đấu trên dòng bước live-op (ký với loader 09/11).
    /// <br/>Viết ở mức PARAM như <c>LiveOpsStampTests</c>: ctor của <c>FLiveOpsApi</c> là
    /// <c>internal</c> và module không có <c>InternalsVisibleTo</c>, mà mọi test đi qua
    /// <c>LiveOpsLogService</c> thì rơi vào nhóm cần Unity runtime — nhóm chỉ được *compile*, chưa
    /// từng *chạy*. Mấy bất biến dưới đây sẽ mất người canh nếu viết ở đó.
    /// </summary>
    public class LiveOpsStandingsTests
    {
        private const string EVENT = "race";

        private static LiveOpsParam Done()
        {
            return new LiveOpsParam { liveOpsEvent = EVENT, action = FLiveOpsAction.Complete };
        }

        [Test]
        public void FourFields_ReachTheWire_UnderTheirPrefixedNames()
        {
            var param = Done();
            param.liveOpsRank = 3;
            param.liveOpsGroupSize = 30;
            param.liveOpsScore = 1840;
            param.liveOpsGroupId = "grp_77";

            param.CorrectValues();
            var dict = param.ToDictionary();

            Assert.AreEqual(3, dict["liveOpsRank"]);
            Assert.AreEqual(30, dict["liveOpsGroupSize"]);
            Assert.AreEqual(1840L, dict["liveOpsScore"]);
            Assert.AreEqual("grp_77", dict["liveOpsGroupId"]);

            // Tiền tố là lý do tồn tại của bốn cái tên này: `score` trần ĐÃ là field thật của dòng
            // level, nên tên trần trên dòng live-op là một cột mang hai nghĩa khi gộp hai stream.
            Assert.IsFalse(dict.ContainsKey("score"), "Tên trần `score` trùng field thật của dòng level");
            Assert.IsFalse(dict.ContainsKey("rank"));
        }

        [Test]
        public void NotSent_MeansAbsent_NotZero()
        {
            var param = Done();

            param.CorrectValues();
            var dict = param.ToDictionary();

            Assert.IsFalse(dict.ContainsKey("liveOpsRank"));
            Assert.IsFalse(dict.ContainsKey("liveOpsGroupSize"));
            Assert.IsFalse(dict.ContainsKey("liveOpsScore"));
            Assert.IsFalse(dict.ContainsKey("liveOpsGroupId"));
        }

        [Test]
        public void RankNotPositive_IsDropped_NotCoercedToZero()
        {
            // 🔴 Đây là lý do hai field hạng KHÔNG dùng CheckNumberNonNegative: hàm đó trả 0 khi
            // gặp số âm. Hạng 0 không tồn tại trong bất kỳ bảng đấu nào, nên 0 trên dây là một giá
            // trị SAI mà trông hợp lệ — kho sẽ cộng nó vào mọi phép đọc hạng. Cùng luật đã áp cho
            // tier/ecpmFloor: vắng còn hơn một số sai trông hợp lệ.
            foreach (var bad in new[] { 0, -1 })
            {
                var param = Done();
                param.liveOpsRank = bad;
                param.liveOpsGroupSize = bad;

                param.CorrectValues();
                var dict = param.ToDictionary();

                Assert.IsNull(param.liveOpsRank, $"hạng {bad} phải thành null, không phải 0");
                Assert.IsNull(param.liveOpsGroupSize, $"số người {bad} phải thành null, không phải 0");
                Assert.IsFalse(dict.ContainsKey("liveOpsRank"));
                Assert.IsFalse(dict.ContainsKey("liveOpsGroupSize"));
            }
        }

        [Test]
        public void ScoreZero_IsKept_BecauseItIsARealScore()
        {
            // Khác hai field hạng: vào bảng mà không ghi được điểm nào là một kết quả THẬT.
            var param = Done();
            param.liveOpsScore = 0;

            param.CorrectValues();

            Assert.AreEqual(0L, param.ToDictionary()["liveOpsScore"]);
        }

        [Test]
        public void RankAboveGroupSize_StillSendsBoth_SoTheWarehouseCanJudge()
        {
            // Một trong hai số đang đo sai chỗ, nhưng SDK không biết cái nào — nên gửi cả hai và
            // cảnh báo, chứ không bỏ bừa một bên.
            var param = Done();
            param.liveOpsRank = 50;
            param.liveOpsGroupSize = 30;

            param.CorrectValues();
            var dict = param.ToDictionary();

            Assert.AreEqual(50, dict["liveOpsRank"]);
            Assert.AreEqual(30, dict["liveOpsGroupSize"]);
        }

        [Test]
        public void StandingsOnANonTerminalStep_StillSend()
        {
            // Hạng ở bước `join` là khai một kết quả chưa tồn tại — SDK cảnh báo nhưng KHÔNG chặn:
            // chặn lời khai của game là tự quyết hộ game, mà luật nhà là SDK không đoán.
            var param = new LiveOpsParam
            {
                liveOpsEvent = EVENT, action = FLiveOpsAction.Join, liveOpsRank = 7
            };

            param.CorrectValues();

            Assert.AreEqual(7, param.ToDictionary()["liveOpsRank"]);
        }

        [Test]
        public void TheFourNames_AreReservedOnTheWire_SoGamesCannotStealThem()
        {
            // Sổ khoá dành riêng quét bằng reflection, nên bốn field mới tự vào sổ — test này canh
            // đúng chuyện đó: nếu ai đổi FWireReservedKeys sang chép tay thì nó đỏ.
            Assert.IsTrue(FWireReservedKeys.IsReserved("liveOpsRank"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("live_ops_group_size"), "So khớp phải bỏ dấu _");
            Assert.IsTrue(FWireReservedKeys.IsReserved("LIVE_OPS_SCORE"), "So khớp phải thường hoá");
            Assert.IsTrue(FWireReservedKeys.IsReserved("liveOpsGroupId"));
        }

        [Test]
        public void ExtraMetaKeyCollidingWithAStandingsField_IsStillDroppedFirstWins_ButNowWarned()
        {
            // FExtraMeta cảnh báo; luật merge không đổi (first-wins) nên field thật vẫn thắng.
            var param = Done();
            param.liveOpsScore = 100;
            param.extraMeta = new System.Collections.Generic.Dictionary<string, object>
            {
                ["live_ops_score"] = 999
            };

            param.CorrectValues();

            Assert.AreEqual(100L, param.ToDictionary()["liveOpsScore"],
                "Field thật phải thắng key extras trùng tên");
            Assert.IsTrue(FExtraMeta.StealsARealField(typeof(LiveOpsParam), "live_ops_score"),
                "Guard phải nhận ra key extras này lấy mất chỗ của field thật");
        }
    }
}
