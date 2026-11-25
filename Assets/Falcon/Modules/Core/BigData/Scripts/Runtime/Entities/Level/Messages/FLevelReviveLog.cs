/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-09
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Một lần HỒI SINH ĐÃ DÙNG — ký với loader 09/11, thay <c>f_sdk_level_action</c> (chỉ có ở
    /// 1.7.0). Họ tên <c>f_sdk_level_action_*</c> để mở rộng: booster chưa có event riêng vì lượt
    /// dùng booster đã có ở dòng level và dòng tài nguyên.
    /// </summary>
    [Serializable]
    public class FLevelReviveLog : AFalconLog, IPerfPackCarrier
    {
        public LevelReviveParam param;

        [Preserve]
        public FLevelReviveLog()
        {
        }

        public FLevelReviveLog(LevelReviveParam param)
        {
            this.param = param;
            AnalyticLogger.Instance.Info($"{Event}:{this.param.ToJson()}");
            this.param.CorrectValues();
        }

        public override string Event => "f_sdk_level_action_revive";

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
