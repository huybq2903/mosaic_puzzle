using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RealtimeEvents
{
    // Immutable, already-serialized fields. Merging never invokes caller code or reserializes values.
    internal sealed class JsonFields
    {
        internal const int MaximumBytes = 1024 * 1024;

        // Stop serialization as soon as the budget is exhausted, including lazy enumerables.
        private sealed class LimitedWriter : StringWriter
        {
            private readonly int limit;
            internal LimitedWriter(int limit) : base(CultureInfo.InvariantCulture) { this.limit = limit; }
            private void Check(int count)
            {
                if (count > limit - GetStringBuilder().Length) throw new ArgumentException("JSON exceeds the size limit.");
            }
            public override void Write(char value) { Check(1); base.Write(value); }
            public override void Write(string value) { Check(value?.Length ?? 0); base.Write(value); }
            public override void Write(char[] value, int index, int count) { Check(count); base.Write(value, index, count); }
        }

        private sealed class LimitedJsonWriter : JsonTextWriter
        {
            private readonly int limit;
            private int depth;
            internal LimitedJsonWriter(TextWriter writer, int limit) : base(writer) { this.limit = limit; }
            private void Check(string value)
            {
                // Json.NET scans strings for escaping before writing them to TextWriter.
                if (value != null && value.Length > limit) throw new ArgumentException("JSON string exceeds the size limit.");
            }
            private void Enter()
            {
                if (++depth > 32) throw new ArgumentException("JSON exceeds the nesting limit of 32.");
            }
            public override void WriteStartObject() { Enter(); base.WriteStartObject(); }
            public override void WriteStartArray() { Enter(); base.WriteStartArray(); }
            // JavaScript constructors are not JSON; reject before traversing potentially deep children.
            public override void WriteStartConstructor(string name) =>
                throw new JsonSerializationException("Properties must contain standard JSON values.");
            public override void WriteEndObject() { base.WriteEndObject(); depth--; }
            public override void WriteEndArray() { base.WriteEndArray(); depth--; }
            public override void WritePropertyName(string name) { Check(name); base.WritePropertyName(name); }
            public override void WritePropertyName(string name, bool escape) { Check(name); base.WritePropertyName(name, escape); }
            public override void WriteValue(string value) { Check(value); base.WriteValue(value); }
            public override void WriteRaw(string value) { Check(value); base.WriteRaw(value); }
            public override void WriteRawValue(string value) { Check(value); base.WriteRawValue(value); }
        }

        internal static void CheckSize(string json, int maxBytes)
        {
            if (json.Length > maxBytes || DiskQueue.Utf8.GetByteCount(json) > maxBytes)
                throw new ArgumentException("JSON exceeds the size limit.");
        }
        // Json.NET handles structure but accepts JavaScript extensions. Check token spelling too,
        // so cached fragments are standard JSON without rewriting numbers or escaped strings.
        private sealed class JsonLineReader : JsonTextReader
        {
            // Consume plain text in runs, without retaining a backtracking point for every character.
            private const string StringToken = @"""(?>[^""\\\x00-\x1f]+|\\(?:[""\\/bfnrt]|u[0-9a-fA-F]{4}))*""";
            private const string NumberToken = @"-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?";
            // JsonTextReader includes separators in each token's span. A comma may precede a
            // value/property, never a closing bracket; property-name spans include their colon.
            private static readonly Regex TokenSyntax = new Regex(
                @"\A[ \t]*(?:[}\]]|(?:,[ \t]*)?(?:[{\[]|true|false|null|" + NumberToken + "|" + StringToken + @"(?:[ \t]*:)?))\z",
                RegexOptions.CultureInvariant);
            private readonly string source;

            internal JsonLineReader(string source) : base(new StringReader(source))
            {
                this.source = source;
                DateParseHandling = DateParseHandling.None;
            }

            public override bool Read()
            {
                var start = LinePosition;
                var read = base.Read();
                var token = source.Substring(start, LinePosition - start);
                if (read ? !TokenSyntax.IsMatch(token) : token.Trim(' ', '\t').Length != 0)
                    throw new JsonSerializationException("Properties must contain standard JSON values.");
                return read;
            }
        }

        private const string TimeZoneOffsetKey = "device.time_zone_offset_seconds";
        private static readonly string[] ArrayPropertyKeys = { "realtime_variants", "realtime_segments", "realtime_personalizations" };
        private static readonly string[] DayOfWeekJson =
        {
            "\"0 - Sunday\"", "\"1 - Monday\"", "\"2 - Tuesday\"", "\"3 - Wednesday\"",
            "\"4 - Thursday\"", "\"5 - Friday\"", "\"6 - Saturday\""
        };
        private readonly Dictionary<string, string> fields;
        private readonly long? timeZoneOffsetSeconds;
        internal readonly string Members;
        internal string Json => "{" + Members + "}";

        private JsonFields(Dictionary<string, string> fields)
        {
            this.fields = fields;
            Members = string.Join(",", fields.Values);
            // Fields are immutable: parse the saved offset once, rather than on every event.
            if (GetValue(TimeZoneOffsetKey, JsonToken.Integer) is long seconds && seconds > -86400 && seconds < 86400)
                timeZoneOffsetSeconds = seconds;
        }

        internal static Dictionary<string, object> Dictionary((string Key, object Value)[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length > 1024) throw new ArgumentException("JSON has too many fields.");
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var value in values) result.Add(value.Key, value.Value);
            return result;
        }

        internal static JsonFields Capture(IDictionary<string, object> values, int maxBytes = MaximumBytes, string eventName = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var serializer = new JsonSerializer { Formatting = Formatting.None, TypeNameHandling = TypeNameHandling.None };
            // Copy before running getters/converters, which can mutate the caller's dictionary.
            var snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                if (snapshot.Count >= Math.Min(1024, maxBytes) || (pair.Key != null && pair.Key.Length > maxBytes)) throw new ArgumentException("JSON exceeds the size limit.");
                snapshot.Add(pair.Key, pair.Value);
            }
            if (eventName != null)
            {
                // Apply the name before serialization: the discarded value must not run caller code.
                if (snapshot.Count == 1024 && !snapshot.ContainsKey("event_name"))
                    throw new ArgumentException("JSON has too many fields.");
                snapshot["event_name"] = eventName;
            }
            var fieldEnds = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var writer = new LimitedWriter(maxBytes))
            using (var json = new LimitedJsonWriter(writer, maxBytes))
            {
                json.WriteStartObject();
                foreach (var pair in snapshot)
                {
                    json.WritePropertyName(pair.Key);
                    serializer.Serialize(json, pair.Value);
                    fieldEnds.Add(pair.Key, writer.GetStringBuilder().Length);
                }
                json.WriteEndObject();
                var text = writer.ToString();
                CheckSize(text, maxBytes);
                return Load(text, fieldEnds);
            }
        }

        internal static JsonFields Load(string json, Dictionary<string, int> fieldEnds = null)
        {
            if (json.IndexOf('\n') >= 0 || json.IndexOf('\r') >= 0)
                throw new ArgumentException("Properties must serialize to a single JSON line.");
            // Keep the original fragments: parsing and rewriting numbers could lose precision.
            using (var reader = new JsonLineReader(json))
            {
                // Raw values bypass the writer's container callbacks; enforce the same capture depth here.
                if (fieldEnds != null) reader.MaxDepth = 32;
                if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                    throw new JsonSerializationException("Saved user properties must be a JSON object.");
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                var start = reader.LinePosition;
                while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                {
                    var key = (string)reader.Value;
                    if (!reader.Read() || reader.TokenType == JsonToken.EndObject)
                        throw new JsonSerializationException("A property must contain a JSON value.");
                    reader.Skip();
                    var end = reader.LinePosition;
                    // Raw values/converters must not consume or inject adjacent fields, even
                    // when their combined output is valid JSON with the same number of keys.
                    if (fieldEnds != null && (!fieldEnds.TryGetValue(key, out var writtenEnd) || end > writtenEnd ||
                        (end < writtenEnd && json.Substring(end, writtenEnd - end).Trim(' ', '\t').Length != 0)))
                        throw new ArgumentException("A property must serialize to exactly one JSON value.");
                    fields.Add(key, json.Substring(start, end - start).TrimStart(',', ' ', '\t'));
                    start = end;
                }
                if (reader.TokenType != JsonToken.EndObject || reader.Read())
                    throw new JsonSerializationException("Incomplete or trailing content in user properties.");
                return new JsonFields(fields);
            }
        }

        internal string GetString(string key) => GetValue(key, JsonToken.String) as string;

        internal bool ContainsKey(string key) => fields.ContainsKey(key);

        internal JsonFields Remove(string key)
        {
            if (!fields.ContainsKey(key)) return this;
            var remaining = new Dictionary<string, string>(fields, StringComparer.Ordinal);
            remaining.Remove(key);
            return new JsonFields(remaining);
        }

        internal List<string> GetStringArray(string key)
        {
            var values = new List<string>();
            if (!fields.TryGetValue(key, out var field)) return values;
            // Load already checked token syntax; only decode this immutable fragment here.
            using (var reader = new JsonTextReader(new StringReader("{" + field + "}")) { DateParseHandling = DateParseHandling.None })
            {
                reader.Read(); // Object.
                reader.Read(); // Property name.
                reader.Read(); // Value.
                if (reader.TokenType == JsonToken.Null) return values;
                if (reader.TokenType != JsonToken.StartArray)
                    throw new InvalidOperationException(key + " must be a string array or null.");
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    if (reader.TokenType != JsonToken.String)
                        throw new InvalidOperationException(key + " must contain only strings.");
                    values.Add((string)reader.Value);
                }
            }
            return values;
        }

        private object GetValue(string key, JsonToken type)
        {
            if (!fields.TryGetValue(key, out var field)) return null;
            // Load already validated this fragment. Avoid repeating syntax checks on every getter.
            using (var reader = new JsonTextReader(new StringReader("{" + field + "}")) { DateParseHandling = DateParseHandling.None })
            {
                reader.Read(); // Object.
                reader.Read(); // Property name.
                reader.Read(); // Value.
                return reader.TokenType == type ? reader.Value : null;
            }
        }

        internal JsonFields Update(JsonFields update)
        {
            Dictionary<string, string> merged = null;
            foreach (var pair in update.fields)
            {
                if (fields.TryGetValue(pair.Key, out var previous) && previous == pair.Value) continue;
                // Preserve immutable snapshots and avoid rebuilding unchanged SDK/session properties.
                if (merged == null) merged = new Dictionary<string, string>(fields, StringComparer.Ordinal);
                merged[pair.Key] = pair.Value;
            }
            return merged == null ? this : new JsonFields(merged);
        }

        internal string EventLine(JsonFields userProperties, long timestamp, long engagementTime, long? firstOpenTimestamp = null)
        {
            var line = new StringBuilder(Members.Length + userProperties.Members.Length + 512).Append('{');
            foreach (var pair in fields)
                if (!IsTimingField(pair.Key) && !userProperties.fields.ContainsKey(pair.Key)) line.Append(pair.Value).Append(',');
            foreach (var pair in userProperties.fields)
                if (!IsTimingField(pair.Key)) line.Append(pair.Value).Append(',');
            // Unset array properties are explicit JSON nulls, including on automatic events.
            foreach (var key in ArrayPropertyKeys)
                if (!fields.ContainsKey(key) && !userProperties.fields.ContainsKey(key)) line.Append('"').Append(key).Append("\":null,");
            // Timing fields are SDK-owned, even when callers supply event fields or saved properties.
            line.Append("\"_ts\":").Append(Number(timestamp));
            line.Append(",\"engagement_time_msec\":").Append(Number(engagementTime));
            var properties = userProperties.fields.ContainsKey(TimeZoneOffsetKey) ? userProperties : this;
            var local = properties.LocalTime(timestamp);
            // Keep local-time field types stable when unknown; -1 cannot be a real day or hour.
            line.Append(",\"event_local_day_of_week\":").Append(local.HasValue ? DayOfWeekJson[(int)local.Value.DayOfWeek] : "\"-1 - Unknown\"");
            line.Append(",\"event_local_hour\":").Append(Number(local?.Hour ?? -1));
            line.Append(",\"event_local_hour_minute\":").Append(Number((local?.Hour * 100 + local?.Minute) ?? -1));
            var eventDate = UtcDate(timestamp);
            var installDay = firstOpenTimestamp.HasValue && firstOpenTimestamp != -1 ? UtcDate(firstOpenTimestamp.Value) : null;
            // Valid calendar timestamps are bounded, so their difference cannot overflow a long.
            long? elapsed = eventDate.HasValue && installDay.HasValue ? Math.Max(0, timestamp - firstOpenTimestamp.Value) : (long?)null;
            long? retentionDay = elapsed.HasValue
                ? Math.Max(0, UtcDayNumber(timestamp) - UtcDayNumber(firstOpenTimestamp.Value)) : (long?)null;
            line.Append(",\"event_date\":").Append(Number(eventDate));
            line.Append(",\"install_day\":").Append(Number(installDay));
            line.Append(",\"retention_day\":").Append(Number(retentionDay));
            line.Append(",\"retention_hour\":").Append(Number(elapsed / 3600000));
            line.Append(",\"retention_minute\":").Append(Number(elapsed / 60000));
            return line.Append("}\n").ToString();
        }

        private static bool IsTimingField(string key) => key == "_ts" || key == "engagement_time_msec" ||
            key == "event_local_day_of_week" || key == "event_local_hour" || key == "event_local_hour_minute" ||
            key == "event_date" || key == "install_day" || key == "retention_day" || key == "retention_hour" || key == "retention_minute";

        private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

        // Floor each timestamp to its UTC day, including dates before the Unix epoch.
        private static long UtcDayNumber(long timestamp) => timestamp / 86400000 - (timestamp % 86400000 < 0 ? 1 : 0);

        internal static int? UtcDate(long timestamp)
        {
            try
            {
                var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
                return date.Year * 10000 + date.Month * 100 + date.Day;
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private DateTimeOffset? LocalTime(long timestamp)
        {
            if (!timeZoneOffsetSeconds.HasValue) return null;
            try
            {
                // Use the event's captured UTC timestamp, never the time of serialization or sending.
                return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).AddSeconds(timeZoneOffsetSeconds.Value);
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }
}
