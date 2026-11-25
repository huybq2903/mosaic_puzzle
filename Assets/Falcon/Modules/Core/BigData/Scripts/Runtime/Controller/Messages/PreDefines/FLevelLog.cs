/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    [Serializable]
    public class FLevelLog : AFalconLog, IPerfPackCarrier
    {
        public ALevelParamV2 param;
        public int failCount;
        public int playCount;

        /// <summary>
        /// Nhãn của MÀN này (§D11) — SDK-core tự đóng dấu từ kho nhãn vật, game khai qua
        /// <c>FalconBigDataController.Label.Level(...)</c> chứ không set ở đây.
        /// <br/>Object PHẲNG, giữ nguyên kiểu JSON: <c>{"cluster":"hard_1","tier":3}</c>.
        /// Vắng mặt khi màn chưa có nhãn; gỡ nhãn = thôi kèm key (không có "bản tin gỡ").
        /// Trần 256B — nó đi kèm MỌI level event, stream dày nhất hệ.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> levelLabels;

        /// <summary>
        /// Sổ thời gian lượt chơi (hợp đồng đợt 3 B6) — SDK tự đóng lúc decor; field phẳng, tên snake theo hợp đồng.
        /// Null trên Start và trên lượt không có sổ.
        /// </summary>
        [FKey(Ignore = true)] [CanBeNull]
        public Dictionary<string, object> turnLedger;

        [Preserve]
        public FLevelLog()
        {
        }

        public FLevelLog(ALevelParamV2 param)
        {
            this.param = param;
            AnalyticLogger.Instance.Info($"{GetType().Name}:{this.param.ToJson()}");
            this.param.CorrectValues();
        }

        public FLevelLog(
            int currentLevel, string difficulty, LevelStatus status, TimeSpan duration,
            int? wave = null, FParam param = null
        )
        {
            if (param is LevelParam levelParam)
            {
                levelParam.currentLevel = currentLevel;
                levelParam.difficulty = difficulty;
                levelParam.status = status;
                levelParam.duration = duration;
                levelParam.wave = wave;
                this.param = levelParam;
            }
            else
            {
                this.param = new OldCodeSupportLevelParam
                {
                    currentLevel = currentLevel,
                    difficulty = difficulty,
                    status = status,
                    duration = duration,
                    wave = wave,
                    extraMeta = param?.ToDictionary()
                };
            }

            AnalyticLogger.Instance.Info($"{GetType().Name}:{this.param.ToJson()}");
            this.param.CorrectValues();
        }

        public override string Event => "f_sdk_level_data";

        public override Dictionary<string, object> ToDictionary()
        {
            var result = base.ToDictionary();
            result.Remove(nameof(param));
            foreach (var (key, value) in param.ToDictionary())
                result.PutIfAbsentAndNotNull(key, value);
            if (turnLedger != null)
                foreach (var (key, value) in turnLedger)
                    result.PutIfAbsentAndNotNull(key, value);
            return result;
        }
    }
}