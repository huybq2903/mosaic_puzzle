using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    public class LevelParamV2Tests
    {
        private static LevelPassParamV2 NewPassParam()
        {
            return new LevelPassParamV2
            {
                playTurnId = "turn-1",
                currentLevel = 5,
                difficulty = "Hard",
                duration = TimeSpan.FromSeconds(90)
            };
        }

        // ---- Hai id của bản thiết kế màn (thêm 19/10) ----

        [Test]
        public void TwoDesignIds_AreSeparateColumns()
        {
            // currentLevelId = băm data + param (client tính) · levelDataId = băm data (SERVER đặt,
            // client echo). Giữ hai cột để (1) nối được sang catalog level của game backend và
            // (2) tách "đổi layout" với "đổi tuning".
            var param = NewPassParam();
            param.currentLevelId = "aB3dE";
            param.levelDataId = "srv42";

            var dict = param.ToDictionary();

            Assert.AreEqual("aB3dE", dict["currentLevelId"]);
            Assert.AreEqual("srv42", dict["levelDataId"]);
        }

        [Test]
        public void LevelDataId_IsAbsentWhenUnknown_NotEmptyString()
        {
            // Game chưa có id của server (ván offline, hoặc màn không đi qua bản tin level-ready)
            // thì cột VẮNG MẶT — "vắng = không biết", đừng gửi chuỗi rỗng rồi bắt kho đoán.
            Assert.IsFalse(NewPassParam().ToDictionary().ContainsKey("levelDataId"));
        }

        [Test]
        public void LevelDataId_IsReservedAgainstGameKeys()
        {
            Assert.IsTrue(FWireReservedKeys.IsReserved("levelDataId"));
            Assert.IsTrue(FWireReservedKeys.IsReserved("level_data_id"));
        }

        [Test]
        public void ToDictionary_StatusAlwaysEmitted_WhenExtraMetaNull()
        {
            var dict = NewPassParam().ToDictionary();
            Assert.IsTrue(dict.ContainsKey("status"));
            Assert.AreEqual(LevelStatus.Pass, dict["status"]);
        }

        [Test]
        public void ToDictionary_StatusNotOverridableByExtraMeta()
        {
            var param = NewPassParam();
            param.extraMeta = new Dictionary<string, object> { ["status"] = "Hacked" };
            var dict = param.ToDictionary();
            Assert.AreEqual(LevelStatus.Pass, dict["status"]);
        }

        [Test]
        public void ToDictionary_ExtraMetaMerged_ButExistingFieldsNotOverridden()
        {
            var param = NewPassParam();
            param.extraMeta = new Dictionary<string, object>
            {
                ["coinSpend"] = 100,
                ["playTurnId"] = "fake-turn",
                ["nullValueKey"] = null
            };
            var dict = param.ToDictionary();
            Assert.AreEqual(100, dict["coinSpend"]);
            Assert.AreEqual("turn-1", dict["playTurnId"]);
            Assert.IsFalse(dict.ContainsKey("nullValueKey"));
        }

        [Test]
        public void ToDictionary_DurationEmittedAsTotalSeconds()
        {
            var dict = NewPassParam().ToDictionary();
            Assert.AreEqual(90L, dict["duration"]);
        }

        [Test]
        public void ToDictionary_IgnoredAndNullRemovedFieldsAbsent()
        {
            var param = NewPassParam();
            param.currentLevelId = null;
            var dict = param.ToDictionary();
            Assert.IsFalse(dict.ContainsKey("autoCheckComeBackAfterFirstPass"), "FKey(Ignore) field must not be encoded");
            Assert.IsFalse(dict.ContainsKey("currentLevelId"), "FKey(RemoveIfNull) null field must not be encoded");
        }

        [Test]
        public void LevelId_CombinesLevelAndDifficulty()
        {
            var param = NewPassParam();
            Assert.AreEqual("5_Difficulty_Hard", param.LevelId);
        }

        [Test]
        public void StatusPerParamType_IsCorrect()
        {
            Assert.AreEqual(LevelStatus.Start, new LevelStartParamV2().Status);
            Assert.AreEqual(LevelStatus.Pass, new LevelPassParamV2().Status);
            Assert.AreEqual(LevelStatus.Fail, new LevelFailParamV2().Status);
            Assert.AreEqual(LevelStatus.HeartBeat, new LevelHeartBeatParamV2().Status);
        }

        [Test]
        public void LevelPassParamV2_DefaultsProgressTo100()
        {
            Assert.AreEqual(100, new LevelPassParamV2().levelProgress);
        }

        [Test]
        public void ComeBackAfterFirstPass_DefaultsFalse_AndIsAutoCheckedByDefault()
        {
            // Cờ này là thứ DUY NHẤT phân biệt replay với lượt chơi đầu (§E: Re* đã khai tử).
            // Mặc định để SDK tự điền — game không phải nhớ set.
            var param = NewPassParam();
            Assert.IsFalse(param.comeBackAfterFirstPass);
            Assert.IsTrue(param.autoCheckComeBackAfterFirstPass);
        }

        [Test]
        public void ComeBackAfterFirstPass_IsEmittedInPayload()
        {
            var param = NewPassParam();
            param.comeBackAfterFirstPass = true;
            Assert.AreEqual(true, param.ToDictionary()["comeBackAfterFirstPass"]);
        }

        [Test]
        public void LevelDesignLimits_AreNotFlatOnTheTurnEvent()
        {
            // Amendment hợp đồng 2026-08-11 (§H6): moves_limit/time_limit_sec tả BẢN THIẾT KẾ MÀN
            // chứ không tả lượt chơi, nên chúng đi trong bundle level_labels dưới tên hợp đồng.
            // Thả flat lên bản tin lượt là trộn chủ thể — cook không biết field nào argMax lên trục nào.
            var dict = NewPassParam().ToDictionary();

            Assert.IsFalse(dict.ContainsKey("movesLimit"));
            Assert.IsFalse(dict.ContainsKey("timeLimitSec"));
            Assert.IsFalse(dict.ContainsKey(FLevelLabelKey.MOVES_LIMIT));
            Assert.IsFalse(dict.ContainsKey(FLevelLabelKey.TIME_LIMIT_SEC));
        }

        [Test]
        public void RegisteredLabelKeys_UseTheContractSpelling()
        {
            // Đây là chữ trên dây, không phải tên field C# — loader đọc level_labels.<key> đúng
            // tên này và cả fleet phải giống nhau thì mới so chéo game được
            Assert.AreEqual("moves_limit", FLevelLabelKey.MOVES_LIMIT);
            Assert.AreEqual("time_limit_sec", FLevelLabelKey.TIME_LIMIT_SEC);
        }

        [Test]
        public void FailReason_OnlyOnFailParam_AbsentWhenNull()
        {
            var noReason = new LevelFailParamV2().ToDictionary();
            Assert.IsFalse(noReason.ContainsKey("failReason"));

            var param = new LevelFailParamV2 { failReason = LevelFailReason.OutOfMoves };
            Assert.AreEqual(LevelFailReason.OutOfMoves, param.ToDictionary()["failReason"]);
        }

        [Test]
        public void CorrectValues_ClampsLevelProgressOver100()
        {
            var param = NewPassParam();
            param.levelProgress = 150;
            LogAssert.Expect(LogType.Error, new Regex("levelProgress"));
            param.CorrectValues();
            Assert.AreEqual(100, param.levelProgress);
        }

        [Test]
        public void CorrectValues_NegativeDuration_BecomesAbsent_NotZero()
        {
            // Vắng còn hơn một số sai trông hợp lệ (AbsentNotZero 12/11): trước đây kẹp về 0.
            var param = NewPassParam();
            param.duration = TimeSpan.FromSeconds(-10);
            LogAssert.Expect(LogType.Error, new Regex("duration"));
            param.CorrectValues();
            Assert.IsNull(param.duration);
            Assert.IsFalse(param.ToDictionary().ContainsKey("duration"));
        }

        [Test]
        public void Duration_NotGiven_IsAbsentFromPayload()
        {
            // Ký với loader 12/11 (AbsentNotZero mục 2): game không truyền thì wire KHÔNG nhận 0.
            var pass = new LevelPassParamV2 { playTurnId = "t", currentLevel = 1, difficulty = "Easy" };
            var fail = new LevelFailParamV2 { playTurnId = "t", currentLevel = 1, difficulty = "Easy" };
            var legacy = new LevelParam { currentLevel = 1, difficulty = "Easy", status = LevelStatus.Fail };
            pass.CorrectValues();
            fail.CorrectValues();
            legacy.CorrectValues();

            Assert.IsFalse(pass.ToDictionary().ContainsKey("duration"));
            Assert.IsFalse(fail.ToDictionary().ContainsKey("duration"));
            Assert.IsFalse(legacy.ToDictionary().ContainsKey("duration"));
        }

        [Test]
        public void LevelProgress_NotGiven_IsAbsentOnFailAndHeartbeat()
        {
            // Hợp đồng đợt 3 B13: game không gán thì VẮNG, không còn 0 (lẫn với "thua ngay từ đầu").
            var fail = new LevelFailParamV2 { playTurnId = "t", currentLevel = 1, difficulty = "Easy" };
            var heartbeat = new LevelHeartBeatParamV2 { playTurnId = "t", currentLevel = 1, difficulty = "Easy" };
            var legacy = new LevelParam { currentLevel = 1, difficulty = "Easy", status = LevelStatus.Fail };
            fail.CorrectValues();
            heartbeat.CorrectValues();
            legacy.CorrectValues();

            Assert.IsFalse(fail.ToDictionary().ContainsKey("levelProgress"));
            Assert.IsFalse(heartbeat.ToDictionary().ContainsKey("levelProgress"));
            Assert.IsFalse(legacy.ToDictionary().ContainsKey("levelProgress"));
        }

        [Test]
        public void LevelProgress_ExplicitZero_IsStillSent()
        {
            var fail = new LevelFailParamV2 { playTurnId = "t", currentLevel = 1, difficulty = "Easy", levelProgress = 0 };
            fail.CorrectValues();
            Assert.AreEqual(0, fail.ToDictionary()["levelProgress"]);
        }

        [Test]
        public void LevelProgress_PassIsAlways100_StartOfLegacyIsZero()
        {
            var pass = NewPassParam();
            pass.levelProgress = null;
            pass.CorrectValues();
            Assert.AreEqual(100, pass.ToDictionary()["levelProgress"]);

            var legacyStart = new LevelParam { currentLevel = 1, difficulty = "Easy", status = LevelStatus.Start };
            legacyStart.CorrectValues();
            Assert.AreEqual(0, legacyStart.ToDictionary()["levelProgress"]);
        }

        [Test]
        public void Duration_ExplicitZero_IsStillSent()
        {
            // 0 truyền tường minh (vd đường Level Core luôn truyền time) vẫn là 0 — SDK không đoán.
            var param = NewPassParam();
            param.duration = TimeSpan.Zero;
            param.CorrectValues();
            Assert.AreEqual(0L, param.ToDictionary()["duration"]);
        }

        [Test]
        public void CorrectValues_NegativeBoosterCount_RemovedFromDictionary()
        {
            var param = NewPassParam();
            param.boostersUsed = new Dictionary<string, int> { ["hammer"] = 2, ["bomb"] = -1 };
            LogAssert.Expect(LogType.Error, new Regex("bomb"));
            param.CorrectValues();
            Assert.IsTrue(param.boostersUsed.ContainsKey("hammer"));
            Assert.IsFalse(param.boostersUsed.ContainsKey("bomb"));
        }
    }
}
