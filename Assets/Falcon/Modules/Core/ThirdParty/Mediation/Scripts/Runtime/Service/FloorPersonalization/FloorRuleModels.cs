/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-21
 */

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>Response của <c>GET /eval</c> — dùng chung cho First Ad Rule và Subsequent Ad Rule.</summary>
    public class FloorRuleResponse
    {
        [JsonProperty("realtime_segment")] public string realtimeSegment;
        [JsonProperty("realtime_variant")] public string realtimeVariant;
        [JsonProperty("config")] public FloorRuleConfig config;

        /// <summary>
        /// Response hợp lệ: có config, và nếu flow không rỗng thì start step phải trỏ tới một step có thật.
        /// Không hợp lệ -> client load mặc định nhưng KHÔNG ghi segment/variant (spec: không tự coi lỗi
        /// là Control Variant).
        /// </summary>
        public bool IsValid(string defaultStartStep)
        {
            if (config == null) return false;
            var flow = config.ecpmFloorFlow;
            if (flow == null || flow.IsEmpty) return true;
            var start = flow.StartStepOr(defaultStartStep);
            return !string.IsNullOrEmpty(start) && flow.steps.ContainsKey(start);
        }

        /// <summary><c>ecpm_floor_flow = {}</c> (Control / base_line) -> load mặc định.</summary>
        [JsonIgnore]
        public bool HasFlow => config?.ecpmFloorFlow != null && !config.ecpmFloorFlow.IsEmpty;
    }

    public class FloorRuleConfig
    {
        [JsonProperty("device_tier")] public string deviceTier;
        [JsonProperty("ecpm_floor_flow")] public FloorFlow ecpmFloorFlow;
    }

    public class FloorFlow
    {
        //server trả số thực ("1.0") -> khai double, khai int là Newtonsoft ném lỗi parse cả response
        [JsonProperty("flow_version")] public double? flowVersion;
        [JsonProperty("start_step")] public string startStep;
        [JsonProperty("steps")] public Dictionary<string, FloorStep> steps;

        [JsonIgnore] public bool IsEmpty => steps == null || steps.Count == 0;

        /// <summary>Subsequent Ad mặc định bắt đầu ở <c>decay</c> khi response không ghi start_step.</summary>
        public string StartStepOr(string defaultStartStep) =>
            string.IsNullOrEmpty(startStep) ? defaultStartStep : startStep;
    }

    public class FloorStep
    {
        [JsonProperty("ecpm_floor")] public double ecpmFloor;
        [JsonProperty("ecpm_floor_percentile")] public string ecpmFloorPercentile;
        [JsonProperty("ecpm_floor_type")] public string ecpmFloorType;
        //server trả số thực ("2.0") -> khai double, đọc qua MaxAttempts
        [JsonProperty("retry_times")] public double retryTimes;

        /// <summary>Tổng số lần load tại floor này: 1 lần đầu + retry_times lần bổ sung.</summary>
        [JsonIgnore] public int MaxAttempts => 1 + Math.Max(0, (int)Math.Round(retryTimes));

        /// <summary>key = <c>success</c> / <c>fail</c>, value = id step kế tiếp. Thiếu key = hết workflow.</summary>
        [JsonProperty("transitions")] public Dictionary<string, string> transitions;
    }
}
