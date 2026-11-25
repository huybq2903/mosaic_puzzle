/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Một bước của một tính năng — MỘT log cho mọi tính năng
    ///     (<c>docs/design/FeatureContract.md</c>).
    ///     <br/>Log này không có field nào của riêng nó: mọi thứ nằm trong <see cref="param"/>, vì
    ///     khác trục live-op, ở đây SDK không đo hộ mốc/khoảng cách nào — tính năng luôn bật nên
    ///     không có "đợt" để làm gốc đo.
    ///     <br/>Dựng bằng <c>FalconBigDataController.Feature.Step/State</c> —
    ///     <see cref="FeatureLogService"/> là đường duy nhất, vì nó mới biết luật tự điền
    ///     <c>featureEffect</c>.
    /// </summary>
    [Serializable]
    public class FFeatureLog : AFalconLog, IPerfPackCarrier
    {
        public FeatureParam param;

        [Preserve]
        public FFeatureLog()
        {
        }

        public FFeatureLog(FeatureParam param)
        {
            this.param = param;
        }

        public override string Event => "f_sdk_feature";

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
