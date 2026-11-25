/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-21
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Gọi Databuckets Rule Evaluation API: <c>GET {evalUrl}?rule=..&amp;uid=..&amp;...</c>, header
    /// <c>x-api-key</c>. Callback chạy trên main thread với (response, lỗi): response <c>null</c> = API lỗi /
    /// timeout / không parse được — caller tự rơi về load mặc định; lỗi = <see cref="FAIL_TIMEOUT"/> hoặc
    /// <see cref="FAIL_ERROR"/> (null khi có response).
    /// </summary>
    internal static class FloorRuleClient
    {
        private const string _API_KEY_HEADER = "x-api-key";

        /// <summary>Quá ngân sách <see cref="FloorPersonalizationConfig.requestTimeoutSeconds"/> — đã Abort.</summary>
        public const string FAIL_TIMEOUT = "rule_timeout";

        /// <summary>Lỗi mạng / HTTP / body không parse được.</summary>
        public const string FAIL_ERROR = "rule_error";

        public static void Evaluate(FloorPersonalizationConfig setting, string rule,
            IReadOnlyList<KeyValuePair<string, string>> parameters, Action<FloorRuleResponse, string> done)
        {
            var url = BuildUrl(setting.evalUrl, rule, parameters);
            MainGameObj.Instance.StartCoroutine(Request(url, setting, done));
        }

        private static string BuildUrl(string baseUrl, string rule,
            IReadOnlyList<KeyValuePair<string, string>> parameters)
        {
            //spec: URL-encode TOÀN BỘ query parameter
            var sb = new StringBuilder(baseUrl);
            sb.Append(baseUrl.Contains("?") ? '&' : '?');
            sb.Append("rule=").Append(Uri.EscapeDataString(rule));
            foreach (var (key, value) in parameters)
            {
                if (value == null) continue;
                sb.Append('&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            }

            return sb.ToString();
        }

        private static IEnumerator Request(string url, FloorPersonalizationConfig setting,
            Action<FloorRuleResponse, string> done)
        {
            FloorRuleResponse response = null;
            string failure = null;
            //Ngân sách tự đếm theo realtime (chính xác tới frame, nhận số lẻ như 0.8s): UnityWebRequest.timeout
            //chỉ nhận giây nguyên và báo timeout chung một mã với lỗi mạng -> không tách được rule_timeout.
            var budget = Math.Max(0.05f, setting.requestTimeoutSeconds);
            using (var request = UnityWebRequest.Get(url))
            {
                if (!string.IsNullOrEmpty(setting.apiKey)) request.SetRequestHeader(_API_KEY_HEADER, setting.apiKey);
                //lưới an toàn phía native, dài hơn ngân sách để bộ đếm dưới luôn về trước
                request.timeout = (int)Math.Ceiling(budget) + 1;

                FloorPersonalizationLog.Info("[FloorPersonalization] GET " + url);
                var startedAt = UnityEngine.Time.realtimeSinceStartup;
                var operation = request.SendWebRequest();
                var timedOut = false;
                while (!operation.isDone)
                {
                    if (UnityEngine.Time.realtimeSinceStartup - startedAt >= budget)
                    {
                        timedOut = true;
                        request.Abort();
                        break;
                    }

                    yield return null;
                }

                var elapsedMs = (int)((UnityEngine.Time.realtimeSinceStartup - startedAt) * 1000);

                if (timedOut)
                {
                    failure = FAIL_TIMEOUT;
                    FloorPersonalizationLog.Error(
                        $"[FloorPersonalization] TIMEOUT sau {elapsedMs}ms (ngân sách {budget * 1000:F0}ms) -> Abort");
                }
                else if (request.result == UnityWebRequest.Result.Success)
                {
                    var body = request.downloadHandler.text;
                    FloorPersonalizationLog.Info(
                        $"[FloorPersonalization] response {request.responseCode} sau {elapsedMs}ms: {body}");
                    try
                    {
                        response = JsonConvert.DeserializeObject<FloorRuleResponse>(body);
                    }
                    catch (Exception e)
                    {
                        FloorPersonalizationLog.Error("[FloorPersonalization] parse lỗi: " + e.Message);
                    }

                    if (response == null) failure = FAIL_ERROR;
                }
                else
                {
                    failure = FAIL_ERROR;
                    FloorPersonalizationLog.Error(
                        $"[FloorPersonalization] API lỗi {request.responseCode} sau {elapsedMs}ms: {request.error} " +
                        $"body={request.downloadHandler?.text}");
                }
            }

            try
            {
                done?.Invoke(response, failure);
            }
            catch (Exception e)
            {
                FloorPersonalizationLog.Error("[FloorPersonalization] callback lỗi: " + e);
            }
        }
    }
}
