/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-01-08
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public abstract class LevelProgressParamV2 : ALevelParamV2
    {
        [FKey(RemoveIfNull = true)] public int? score;
        [FKey(RemoveIfNull = true)] public int? movesUsed;
        /// <summary>
        ///     Thời lượng lượt. <b>Vắng khi game không truyền</b> (ký với loader 12/11, <c>Request-Loader-AbsentNotZero.md</c>
        ///     mục 2) — trước đây không truyền thì wire nhận <c>0</c>, lẫn với lượt thật dưới 1 giây. Giá trị truyền
        ///     tường minh, kể cả <c>0</c>, vẫn gửi nguyên.
        /// </summary>
        [FKey(RemoveIfNull = true)] public TimeSpan? duration;
        /// <summary>
        ///     Booster đã dùng TRONG ván: mã → <b>số LẦN DÙNG</b> (không phải số món bị trừ), <b>KHÔNG gồm</b> booster
        ///     chọn trước ván (cái đó ở <c>preBoostersUsed</c> của dòng Start). Định nghĩa chốt với loader 10/11 —
        ///     trước đó hợp đồng chỉ ghi "số lượng dùng trong màn", nên dữ liệu cũ tuỳ cách từng game hiểu.
        ///     <br/>SDK KHÔNG tự tính field này — game truyền. Đây là nguồn của "dùng bao nhiêu lần"; từng lần dùng
        ///     (đoạn nào của ván, booster có từ đâu) đi ở <c>f_sdk_level_action_booster</c>, đừng cộng hai nguồn.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, int> boostersUsed;
        
        /// <summary>
        ///     Người chơi đã đi được bao nhiêu % của level (0–100). Pass luôn 100.
        ///     <br/><b>Vắng khi game không gán</b> trên Fail / HeartBeat (hợp đồng đợt 3 B13, ký 23/11) — trước đây
        ///     không gán thì wire nhận <c>0</c>, lẫn với "thua ngay từ đầu". Giá trị gán tường minh, kể cả <c>0</c>,
        ///     vẫn gửi nguyên. Nghĩa của "%" (của điểm, của số nước…) game khai trong tài liệu tích hợp.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? levelProgress;
        
        public override void CorrectValues()
        {
            base.CorrectValues();
            duration = CheckTimeSpanNonNegative(duration, nameof(duration));
            score = CheckNumberNonNegative(score, nameof(score));
            movesUsed = CheckNumberNonNegative(movesUsed, nameof(movesUsed));
            levelProgress = CheckNumberNonNegative(levelProgress, nameof(levelProgress));

            if (levelProgress > 100)
            {
                Debug.LogError(
                    $"Dwh Log invalid field: the value of levelProgress must be <=100, input value '{levelProgress}'");
                levelProgress = 100;
            }

            FBoosterRegistry.WarnIfAnyUnknown(boostersUsed, "boosters_used");

            if (boostersUsed == null) return;
            foreach (var (key, value) in boostersUsed.Where(e => e.Value < 0).ToList())
            {
                Debug.LogError(
                    $"Dwh Log invalid field: the value of booster {key} of {GetType().Name} must be non-negative, input value '{value}'");
                boostersUsed.Remove(key);
            }
        }
        
        private Dictionary<string, object> PutDuration(Dictionary<string, object> dictionary)
        {
            if (duration.HasValue) dictionary[nameof(duration)] = (long)duration.Value.TotalSeconds;
            else dictionary.Remove(nameof(duration));
            return dictionary;
        }

        public override Dictionary<string, object> ToDictionary()
        {
            return PutDuration(base.ToDictionary());
        }
    }

    public class LevelPassParamV2 : LevelProgressParamV2
    {
        public LevelPassParamV2()
        {
            levelProgress = 100;
        }
        public override LevelStatus Status => LevelStatus.Pass;

        public override void CorrectValues()
        {
            // Pass luôn có tiến độ (hợp đồng B13): game lỡ gán null thì vẫn là 100, không vắng.
            levelProgress ??= 100;
            base.CorrectValues();
        }
    }
    
    public class LevelFailParamV2 : LevelProgressParamV2
    {
        /// <summary>
        /// Thua VÌ SAO — hợp đồng §D, chỉ có trên Fail.
        /// Theo §H4: không biết thì để null (vắng mặt khỏi payload), KHÔNG điền bừa.
        /// </summary>
        [FKey(RemoveIfNull = true)] public LevelFailReason? failReason;

        public override LevelStatus Status => LevelStatus.Fail;
    }
    
    /// <summary>
    ///     Tham số của dòng heartbeat. Từ hợp đồng đợt 3 B5 (ký 23/11) <b>SDK quyết lúc bắn</b> (xuống nền, <c>Level.OnPauseExit</c>,
    ///     sắp chết): game truyền object này qua <c>Level.OnHeartbeat</c> chỉ để CẬP NHẬT tham số của lượt đang mở, không sinh dòng.
    /// </summary>
    public class LevelHeartBeatParamV2 : LevelProgressParamV2
    {
        public override LevelStatus Status => LevelStatus.HeartBeat;

        /// <summary>
        ///     Lúc tham số lượt được cập nhật lần cuối (epoch ms) — để biết tiến độ trên dòng này cũ bao lâu. SDK tự điền:
        ///     nguồn tham số (APS) điền lúc bắn ⇒ lúc bắn; không thì lúc game gọi <c>Level.OnHeartbeat</c> gần nhất. Vắng nếu
        ///     chưa lần nào trong lượt. Là MỐC GIỜ, không phải khoảng thời gian.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? paramsUpdatedAt;
    }
}