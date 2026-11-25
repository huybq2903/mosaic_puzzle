/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    [Serializable]
    public class FAdLog : AAdLog
    {
        public int typeCount;
        public AdParam param;
        public double? adLtv;

        /// <summary>
        /// Độ trễ fill: từ lúc xin ad tới lúc ad lên hình (ms) — SDK-core tự đo (§D2). Server cũng
        /// tự trừ timestamp giữa <c>ad_request</c> và event này được; đây là bản client đo sẵn làm
        /// fallback. Vắng mặt khi impression không đi từ request nào của SDK.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? fillLatencyMs;

        /// <summary>
        /// Số impression mà bản tin này gộp (§G) — chỉ khác 1 ở dòng banner đã nén
        /// (<see cref="BannerLogService"/>). Server đếm impression banner phải dùng
        /// <c>sum(impressionCount)</c> chứ không phải <c>count(*)</c>.
        /// </summary>
        public int impressionCount = 1;

        /// <summary>
        /// Lần HIỂN THỊ thứ mấy của format này TRONG PHIÊN (đếm từ 1) — SDK đếm.
        /// <br/>Tên <c>session_ad_*_index</c> là CỐ Ý (owner chốt 26/10): <c>session</c> nói PHẠM VI
        /// (reset mỗi phiên — cạnh nó là <see cref="typeCount"/>, sổ theo ĐỜI, hai thứ chỉ khác nhau
        /// ở đó), <c>ad</c> nói MIỀN, rồi mới tới mốc nào. Bốn sổ vì thế đứng liền nhau khi liệt kê
        /// cột, và chữ "index" không còn đứng một mình gây hiểu lầm.
        /// <br/>Khác <see cref="typeCount"/>: cái đó là sổ theo ĐỜI (lifetime, có persist), cái này
        /// reset mỗi phiên. eCPM đi theo thứ tự trong phiên nên đây là trục phân tích chính.
        /// <br/>⚠ Vắng trên dòng GỘP CỤM của banner: một dòng ở đó gộp N impression nên "thứ mấy"
        /// không có nghĩa — dùng <see cref="impressionCount"/>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? sessionAdImpressionIndex;

        [Preserve]
        public FAdLog()
        {
        }

        public FAdLog(AdParam param)
        {
            AnalyticLogger.Instance.Info($"{GetType().Name}:{param.ToJson()}");
            param.CorrectValues();
            this.param = param;
        }

        public FAdLog(AdType type, string adWhere, string adPrecision, string adCountry, double adRev,
            string adNetwork, string adMediation, int? currentLevel = null, FParam param = null)
        {
            if (param is AdParam adParam)
            {
                adParam.type = type;
                adParam.adWhere = adWhere;
                adParam.adPrecision = adPrecision;
                adParam.adCountry = adCountry;
                adParam.adRev = adRev;
                adParam.adNetwork = adNetwork;
                adParam.adMediation = adMediation;
                adParam.currentLevel = currentLevel;
                this.param = adParam;
            }
            else
            {
                this.param = new OldCodeSupportAdParam
                {
                    type = type,
                    adWhere = adWhere,
                    adPrecision = adPrecision,
                    adCountry = adCountry,
                    adRev = adRev,
                    adNetwork = adNetwork,
                    adMediation = adMediation,
                    currentLevel = currentLevel,
                    extraMeta = param?.ToDictionary()
                };
            }

            AnalyticLogger.Instance.Info($"{GetType().Name}:{this.param.ToJson()}");
            this.param.CorrectValues();
        }

        public override string Event => "f_sdk_ads_data";

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