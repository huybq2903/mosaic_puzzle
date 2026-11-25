using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    /// <summary>
    /// Sổ tên cột hợp đồng — cửa chặn game đặt key trùng qua Common.Set / Label.* (§H1).
    /// Ca sinh ra nó: game đặt key trùng tên một cột hợp đồng (vd "fail_count") rồi mất số im lặng.
    /// <br/>Sổ gom bằng reflection từ chính field của payload, nên BỎ một cột là tên đó tự được
    /// GIẢI PHÓNG cho game dùng — test dưới ghim đúng ca đó với win_streak (cột bỏ 14/10).
    /// </summary>
    public class FWireReservedKeysTests
    {
        [Test]
        public void PopularColumnNames_AreReserved_InAnySpelling()
        {
            // So theo dạng chuẩn hoá: snake/camel/CONST đều phải bắt được
            Assert.IsTrue(FWireReservedKeys.IsReserved("failCount"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("fail_count"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("FAIL_COUNT"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("playCount"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("play_turn_id"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("currentLevel"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("score"));
        }

        [Test]
        public void RetiredColumnNames_AreReleasedBackToGames()
        {
            // winStreak/loseStreak BỎ khỏi hợp đồng 14/10 (chuỗi Pass/Fail suy lại được từ stream,
            // còn định nghĩa "phủ cả ván bỏ ngang" là chính sách per-game). Cột đi thì cấm cũng
            // phải đi — nếu test này đỏ nghĩa là có ai vừa thêm lại field tên đó vào payload.
            Assert.IsFalse(FWireReservedKeys.IsReserved("win_streak"));
            Assert.IsFalse(FWireReservedKeys.IsReserved("winStreak"));
            Assert.IsFalse(FWireReservedKeys.IsReserved("loseStreak"));
        }

        [Test]
        public void GameOwnNames_PassFreely()
        {
            Assert.IsFalse(FWireReservedKeys.IsReserved("streak_status"), "Tên được gợi ý thay thế phải đi lọt");
            Assert.IsFalse(FWireReservedKeys.IsReserved("guild_id"));
            Assert.IsFalse(FWireReservedKeys.IsReserved("vip_tier"));
        }

        [Test]
        public void SdkOwnLabelKeys_AreNotSelfBlocked()
        {
            // SDK tự gửi nhãn elo (SetUserElo) và clicked (bản tin bổ sung offer) — hai key này
            // không được nằm trong sổ, không thì SDK tự bắn vào chân
            Assert.IsFalse(FWireReservedKeys.IsReserved(FUserLabelKey.ELO));
            Assert.IsFalse(FWireReservedKeys.IsReserved("clicked"));
        }

        [Test]
        public void BlankKey_IsNotReserved()
        {
            Assert.IsFalse(FWireReservedKeys.IsReserved(null));
            Assert.IsFalse(FWireReservedKeys.IsReserved(string.Empty));
        }
    }
}
