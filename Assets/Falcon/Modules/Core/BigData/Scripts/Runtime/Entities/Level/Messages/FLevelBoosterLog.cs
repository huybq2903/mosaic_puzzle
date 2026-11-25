/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Một lần dùng booster trong ván — ký với loader 10/11, cùng họ <c>f_sdk_level_action_*</c> với hồi sinh.
    /// </summary>
    [Serializable]
    public class FLevelBoosterLog : AFalconLog, IPerfPackCarrier
    {
        public LevelBoosterParam param;

        [Preserve]
        public FLevelBoosterLog()
        {
        }

        public FLevelBoosterLog(LevelBoosterParam param)
        {
            this.param = param;
            AnalyticLogger.Instance.Info($"{Event}:{this.param.ToJson()}");
            this.param.CorrectValues();
        }

        public override string Event => "f_sdk_level_action_booster";

        public override Dictionary<string, object> ToDictionary()
        {
            var result = base.ToDictionary();
            result.Remove(nameof(param));
            foreach (var (key, value) in param.ToDictionary())
                result.PutIfAbsentAndNotNull(key, value);
            return result;
        }
    }
}
