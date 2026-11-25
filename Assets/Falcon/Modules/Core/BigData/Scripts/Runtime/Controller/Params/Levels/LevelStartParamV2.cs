/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-01-08
 */

using System.Collections.Generic;
using System.Linq;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class LevelStartParamV2 : ALevelParamV2
    {
        /// <summary>
        ///     Booster chọn TRƯỚC khi vào ván: mã → số món. Tách khỏi <c>boostersUsed</c> (chỉ đếm lần dùng TRONG ván)
        ///     và khỏi <c>f_sdk_level_action_booster</c> (cũng chỉ trong ván).
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, int> preBoostersUsed;

        public override void CorrectValues()
        {
            // 🔴 Thiếu dòng này từ đầu: bản cũ return sớm khi preBoostersUsed == null, nên validate
            // của lớp cha (currentLevel, difficulty) KHÔNG BAO GIỜ chạy trên dòng Start — stream dày
            // nhất hệ. Phát hiện 09/11 lúc nối sổ booster vào đúng hàm này.
            base.CorrectValues();

            FBoosterRegistry.WarnIfAnyUnknown(preBoostersUsed, "pre_boosters_used");

            if (preBoostersUsed == null) return;
            foreach (var (key, value) in preBoostersUsed.Where(e => e.Value < 0).ToList())
            {
                Debug.LogError(
                    $"Dwh Log invalid field: the value of pre-booster {key} of {GetType().Name} must be non-negative, input value '{value}'");
                preBoostersUsed.Remove(key);
            }
        }
        
        public override LevelStatus Status => LevelStatus.Start;
    }
}