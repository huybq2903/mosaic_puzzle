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
    [Serializable]
    public class LevelParam : ALevelParamV2
    {
        public LevelStatus status;
        /// <summary>
        ///     Thời lượng lượt. <b>Vắng khi game không truyền</b> (ký với loader 12/11, <c>Request-Loader-AbsentNotZero.md</c>
        ///     mục 2) — trước đây không truyền thì wire nhận <c>0</c>, lẫn với lượt thật dưới 1 giây. Giá trị truyền
        ///     tường minh, kể cả <c>0</c>, vẫn gửi nguyên.
        /// </summary>
        [FKey(RemoveIfNull = true)] public TimeSpan? duration;

        [FKey(RemoveIfNull = true)] public int? movesUsed;
        [FKey(RemoveIfNull = true)] public int? score;
        [FKey(RemoveIfNull = true)] public int? wave;
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
        ///     Người chơi đã đi được bao nhiêu % của level (0–100). Start luôn 0, Pass luôn 100.
        ///     <br/><b>Vắng khi game không gán</b> ở các status khác (hợp đồng đợt 3 B13) — trước đây wire nhận <c>0</c>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? levelProgress;

        public override void CorrectValues()
        {
            currentLevel = CheckNumberNonNegative(currentLevel, "currentLevel");
            difficulty = CheckNonBlank(difficulty, "difficulty");
            duration = CheckTimeSpanNonNegative(duration, "duration");
            wave = CheckNumberNonNegative(wave, "wave");
            score = CheckNumberNonNegative(score, "score");
            movesUsed = CheckNumberNonNegative(movesUsed, "movesUsed");

            if (status.IsPass())
            {
                levelProgress = 100;
            } else if (status.IsStart())
            {
                levelProgress = 0;
            }
            else
            {
                levelProgress = CheckNumberNonNegative(levelProgress, "levelPercentage");
            }

            FBoosterRegistry.WarnIfAnyUnknown(boostersUsed, "boosters_used (lớp cũ)");

            if (boostersUsed == null) return;
            foreach (var (key, value) in boostersUsed.AsEnumerable().Where(e => e.Value < 0))
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
        
        public override LevelStatus Status => status;
    }

    [Serializable]
    /// <summary>
    /// Đường vào cho code cũ gọi <c>FLevelLog</c> bằng đối số rời — không thêm field nào, chỉ tồn
    /// tại để phân biệt "param do game dựng" với "param SDK dựng hộ từ đối số rời".
    /// <br/><c>extraMeta</c> nằm ở <see cref="ALevelParamV2"/> cho cả họ; khai lại ở đây thì
    /// <c>FKeyService</c> (quét field public, kể cả field bị che) thấy HAI field cùng tên và ghi đè
    /// nhau theo thứ tự <c>GetFields</c> — cùng lỗi đã dính với <c>purchaseAttemptId</c>.
    /// </summary>
    public class OldCodeSupportLevelParam : LevelParam
    {
    }
}