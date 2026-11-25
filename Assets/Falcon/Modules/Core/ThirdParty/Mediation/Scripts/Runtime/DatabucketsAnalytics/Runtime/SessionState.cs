using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RealtimeEvents
{
    // The pending boundary event and its state change share one atomic metadata record.
    internal sealed class SessionState
    {
        internal const long TimeoutMilliseconds = 30L * 60 * 1000;
        internal string Id;
        internal long Number;
        internal long LastActivity;
        internal bool Active;
        internal string PendingId;
        internal string PendingEvent;

        internal SessionState Copy() => (SessionState)MemberwiseClone();

        internal JsonFields Properties => JsonFields.Capture(new Dictionary<string, object>
        {
            ["session_id"] = Id, ["session_number"] = Number
        });

        internal string Json => JsonFields.Capture(new Dictionary<string, object>
        {
            ["session_id"] = Id, ["session_number"] = Number, ["last_activity"] = LastActivity,
            ["active"] = Active, ["pending_id"] = PendingId, ["pending_event"] = PendingEvent
        }, JsonFields.MaximumBytes * 6 + 4096).Json;

        internal static SessionState Load(string json)
        {
            if (json == null) return new SessionState();
            var data = JObject.Parse(JsonFields.Load(json).Json);
            if (data["session_id"]?.Type != JTokenType.String || string.IsNullOrEmpty((string)data["session_id"]) ||
                data["session_number"]?.Type != JTokenType.Integer || (long)data["session_number"] < 1 ||
                data["last_activity"]?.Type != JTokenType.Integer || data["active"]?.Type != JTokenType.Boolean ||
                !OptionalString(data["pending_id"]) || !OptionalString(data["pending_event"]) ||
                (data["pending_id"].Type == JTokenType.Null) != (data["pending_event"].Type == JTokenType.Null))
                throw new IOException("Invalid cached session.");
            var state = new SessionState
            {
                Id = (string)data["session_id"], Number = (long)data["session_number"],
                LastActivity = (long)data["last_activity"], Active = (bool)data["active"],
                PendingId = (string)data["pending_id"], PendingEvent = (string)data["pending_event"]
            };
            if (state.PendingEvent != null)
            {
                // The metadata limit includes JSON escaping; the recovered event must still
                // fit the queue's read limit, including when newer settings use smaller batches.
                if (state.PendingEvent.Length > JsonFields.MaximumBytes ||
                    DiskQueue.Utf8.GetByteCount(state.PendingEvent) > JsonFields.MaximumBytes)
                    throw new IOException("Cached session event exceeds the supported size limit.");
                // Invalid IDs cannot recover through storage retries. Fail before initialization succeeds.
                DiskQueue.ParseBatchId(state.PendingId);
                if (!state.PendingEvent.EndsWith("\n", StringComparison.Ordinal))
                    throw new IOException("Incomplete cached session event.");
                var line = state.PendingEvent.Substring(0, state.PendingEvent.Length - 1);
                var pending = JObject.Parse(JsonFields.Load(line).Json);
                var timestamp = state.Active ? state.LastActivity : checked(state.LastActivity + TimeoutMilliseconds);
                if (pending["event_name"]?.Type != JTokenType.String ||
                    (string)pending["event_name"] != (state.Active ? "session_start" : "session_end") ||
                    !JToken.DeepEquals(pending["session_id"], data["session_id"]) ||
                    !JToken.DeepEquals(pending["session_number"], data["session_number"]) ||
                    pending["_ts"]?.Type != JTokenType.Integer || (long)pending["_ts"] != timestamp ||
                    (pending["engagement_time_msec"] != null && (pending["engagement_time_msec"].Type != JTokenType.Integer ||
                        (long)pending["engagement_time_msec"] < 0 ||
                        (long)pending["engagement_time_msec"] > (state.Active ? 0 : TimeoutMilliseconds))))
                    throw new IOException("Invalid cached session event.");
            }
            return state;
        }

        private static bool OptionalString(JToken token) => token != null &&
            (token.Type == JTokenType.Null || (token.Type == JTokenType.String && !string.IsNullOrEmpty((string)token)));
    }
}
