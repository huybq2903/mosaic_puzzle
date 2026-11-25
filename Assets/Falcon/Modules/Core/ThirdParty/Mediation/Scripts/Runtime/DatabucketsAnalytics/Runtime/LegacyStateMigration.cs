using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RealtimeEvents
{
    internal sealed class LegacyState
    {
        internal string UserId;
        internal long SessionNumber;
        internal long? FirstOpen;

        internal LegacyState Fallback(LegacyState older) => new LegacyState
        {
            UserId = UserId ?? older.UserId,
            SessionNumber = SessionNumber > 0 ? SessionNumber : older.SessionNumber,
            FirstOpen = FirstOpen ?? older.FirstOpen
        };
    }

    // Read old stores only. The current SDK's durable records always take precedence.
    internal static class LegacyStateMigration
    {
        internal const int MaxSlotBytes = 512 * 1024;

        // PlayerPrefs and Application must be accessed on Unity's thread; file/native I/O is deferred.
        internal static Func<LegacyState> CreateReader()
        {
            var directory = Application.persistentDataPath;
            var prefs = ReadPlayerPrefs(PlayerPrefs.GetString, PlayerPrefs.GetInt);
            return () => Read(directory, prefs, LegacyNativeState.Read());
        }

        internal static LegacyState ReadPlayerPrefs(Func<string, string, string> text, Func<string, int, int> integer)
        {
            return new LegacyState
            {
                UserId = UserId(text("user_pseudo_id", null)),
                SessionNumber = Number(text("session_number", null)),
                FirstOpen = Timestamp(text("first_open_timestamp", null)) ??
                    Timestamp(text("first_open_candidate_open_ts", null)) ??
                    (integer("is_sent_first_open", 0) == 1 ? Timestamp(text("install_timestamp", null)) : null)
            };
        }

        internal static LegacyState Read(string directory, LegacyState prefs, LegacyState native)
        {
            if (native.UserId != null && native.SessionNumber > 0 && native.FirstOpen.HasValue) return native;
            var a = ReadSlot(Path.Combine(directory, "databuckets_sdk_state_a.json"), out var generationA);
            var b = ReadSlot(Path.Combine(directory, "databuckets_sdk_state_b.json"), out var generationB);
            // A damaged field must not discard another field that still identifies an existing install.
            var latest = generationA >= generationB ? a.Fallback(b) : b.Fallback(a);
            return native.Fallback(latest).Fallback(prefs);
        }

        private static LegacyState ReadSlot(string path, out long generation)
        {
            generation = 0;
            try
            {
                string json;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > MaxSlotBytes) return new LegacyState();
                    using (var reader = new BinaryReader(stream)) json = DiskQueue.Utf8.GetString(reader.ReadBytes((int)stream.Length));
                }
                var envelope = Parse(json);
                if (envelope["format_version"]?.Type != JTokenType.Integer || (long)envelope["format_version"] != 1 ||
                    envelope["generation"]?.Type != JTokenType.Integer || (long)envelope["generation"] <= 0 ||
                    envelope["payload_json"]?.Type != JTokenType.String || envelope["content_sha256"]?.Type != JTokenType.String)
                    return new LegacyState();
                var payload = (string)envelope["payload_json"];
                if (DiskQueue.Utf8.GetByteCount(payload) > 256 * 1024) return new LegacyState();
                var version = "1\n" + ((long)envelope["generation"]).ToString(CultureInfo.InvariantCulture) + "\n" + payload;
                using (var hash = SHA256.Create())
                {
                    var checksum = BitConverter.ToString(hash.ComputeHash(DiskQueue.Utf8.GetBytes(version))).Replace("-", "");
                    if (!string.Equals(checksum, (string)envelope["content_sha256"], StringComparison.OrdinalIgnoreCase))
                        return new LegacyState();
                }
                var data = Parse(payload);
                var timestamp = Timestamp(IntegerText(data["first_open_ts_ms"]));
                var number = Number(IntegerText(data["session_number"]));
                var properties = data["sdk_persistent_properties"] as JObject;
                generation = (long)envelope["generation"];
                return new LegacyState { UserId = UserId(properties?["user_pseudo_id"]), FirstOpen = timestamp, SessionNumber = number };
            }
            catch (FileNotFoundException) { return new LegacyState(); }
            catch (DirectoryNotFoundException) { return new LegacyState(); }
            catch (JsonException) { return new LegacyState(); }
            catch (System.Text.DecoderFallbackException) { return new LegacyState(); }
            catch (OverflowException) { return new LegacyState(); }
        }

        internal static JObject Parse(string json)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 16 })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonReaderException("Unexpected trailing legacy state content.");
                return result;
            }
        }

        internal static string UserId(JToken value) => value?.Type == JTokenType.String ? UserId((string)value) : null;
        internal static string UserId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 ? value : null;
        internal static string IntegerText(JToken value) => value?.Type == JTokenType.Integer ? value.ToString(Formatting.None) : null;
        internal static long Number(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
            number > 0 && number < long.MaxValue ? number : 0;
        internal static long? Timestamp(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) &&
            timestamp > 0 && timestamp <= 253402300799999L ? timestamp : (long?)null;
    }
}
