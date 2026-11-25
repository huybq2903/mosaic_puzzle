/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine.Serialization;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    [Serializable]
    public abstract class AFalconLog : PlainLog
    {
        [FormerlySerializedAs("apiId")] public int logTypeCreateId;
        public int logTypeSendId;
        public string uuid = Guid.NewGuid().ToString();
        public string createdDateLocal;

        /// <summary>
        /// Field chụp MỘT lần lúc decor (gói hiệu năng B1…) rồi chỉ chép vào payload — để <c>ToDictionary</c> gọi bao nhiêu
        /// lần cũng không đặt lại bộ đếm "kể từ bản tin trước" lần nữa.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        internal System.Collections.Generic.Dictionary<string, object> decorFields;

        protected AFalconLog()
        {
            // Ctor thuần data — ids/date do BaseLogDecorService gán trong pipeline decor lúc Send
        }

        public override Dictionary<string, object> ToDictionary()
        {
            return BaseLogDecorService.Instance.InjectDictionary(this, base.ToDictionary());
        }

        [Conditional("FALCON_LOG_DEBUG")]
        protected void LogParams(params object[] parameters)
        {
            var builder = new StringBuilder(GetType().Name).Append(" : [ ");
            foreach (var parameter in parameters)
                if (parameter == null) builder.Append("null | ");
                else
                    builder.Append(parameter).Append(" | ");

            if (parameters.Length > 0) builder.Length -= 2;

            builder.Append("]");
            AnalyticLogger.Instance.Info(builder.ToString());
        }
        
        [Obsolete]
        protected double CheckNumberNonNegative(double i, string fieldName)
        {
            if (i >= 0) return i;
            UnityEngine.Debug.LogError(
                $"Dwh Log invalid field: the value of field {fieldName} of {GetType().Name} must be non-negative, input value '{i}'");
            return 0;
        }
    }
}