/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json.Converters;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>Loại sự cố của <c>f_sdk_app_issue</c> (hợp đồng đợt 3 S2, ký 23/11). Danh sách ĐÓNG.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AppIssueKind
    {
        [EnumMember(Value = "exception")] Exception,
        [EnumMember(Value = "freeze")] Freeze,
        [EnumMember(Value = "freeze_long")] FreezeLong,
        [EnumMember(Value = "memory_critical")] MemoryCritical,
        [EnumMember(Value = "prev_exit")] PrevExit
    }

    /// <summary>Nguồn tín hiệu thiếu bộ nhớ (bắt buộc trên <c>memory_critical</c>).</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MemorySignalSource
    {
        [EnumMember(Value = "unity_low_memory")] UnityLowMemory,
        [EnumMember(Value = "activity_manager")] ActivityManager
    }

    /// <summary>Cách phiên trước kết thúc (trên dòng <c>prev_exit</c>).</summary>
    /// <summary>
    /// <c>prev_exit</c> đợt 2: lý do thoát của OS (Android 11+ <c>ApplicationExitInfo</c>, iOS MetricKit) ghép với dấu vòng
    /// đời. Pure.
    /// </summary>
    public static class PrevExitDecision
    {
        // ApplicationExitInfo.REASON_* (API 30)
        public const int REASON_LOW_MEMORY = 3;
        public const int REASON_CRASH = 4;
        public const int REASON_CRASH_NATIVE = 5;
        public const int REASON_ANR = 6;

        /// <summary>RunningAppProcessInfo.IMPORTANCE_VISIBLE (200): ≤ mức này là "đang ở trước mặt".</summary>
        public const int IMPORTANCE_VISIBLE = 200;

        /// <summary>Bị dọn vì thiếu bộ nhớ lúc nằm nền chỉ tính là bất thường nếu trong 5 phút sau lúc xuống nền (đề xuất, loader đo rồi chốt).</summary>
        public const long LOW_MEMORY_BACKGROUND_WINDOW_MS = 5 * 60_000;

        /// <summary>Báo cáo iOS phải tới sau lần ghi dấu cuối trừ dung sai này.</summary>
        public const long IOS_MATCH_SLACK_MS = 60_000;

        public static string Reason(ExitRecord record) => record.Reason switch
        {
            REASON_CRASH => "crash",
            REASON_CRASH_NATIVE => "crash_native",
            REASON_ANR => "anr",
            REASON_LOW_MEMORY => record.Importance <= IMPORTANCE_VISIBLE ? "low_memory_foreground" : "low_memory_background",
            _ => "other"
        };

        /// <summary>
        /// Mục của phiên trước: khớp <c>pid</c> (nếu dấu có), không thì mục có giờ ≥ lần ghi dấu cuối. Mục đã báo
        /// (giờ ≤ <paramref name="lastReportedMs"/>) bỏ — mỗi mục báo một lần.
        /// </summary>
        public static ExitRecord? Match(
            System.Collections.Generic.IEnumerable<ExitRecord> records, LifecycleMark previous, long lastReportedMs)
        {
            if (records == null || previous == null) return null;
            ExitRecord? best = null;
            foreach (var record in records)
            {
                if (record.TimestampMs <= lastReportedMs) continue;
                var matches = previous.pid > 0 ? record.Pid == previous.pid : record.TimestampMs >= previous.lastAliveAt;
                if (!matches) continue;
                if (best == null || record.TimestampMs > best.Value.TimestampMs) best = record;
            }

            return best;
        }

        /// <summary>Báo cáo MetricKit của phiên trước: tới sau lần ghi dấu cuối (trừ dung sai) và chưa báo.</summary>
        public static string IosReason(IosDiagnostic? diagnostic, LifecycleMark previous, long lastReportedMs)
        {
            if (diagnostic is not { } d || previous == null) return null;
            if (d.TimestampMs <= lastReportedMs || d.TimestampMs < previous.lastAliveAt - IOS_MATCH_SLACK_MS) return null;
            return d.Kind;
        }

        /// <summary>
        /// Loại kết thúc: <c>abnormal</c> theo dấu (luật đợt 1); không thì <c>background_then_killed</c> khi lý do OS bất
        /// thường — <c>crash</c> · <c>crash_native</c> · <c>anr</c> · <c>low_memory_foreground</c> · <c>hang</c>, hoặc
        /// <c>low_memory_background</c> trong 5 phút sau lúc xuống nền. Thoát sạch / không có gì ⇒ null.
        /// </summary>
        public static PrevEndType? EndType(LifecycleMark previous, string reason, long? exitAtMs)
        {
            var byMark = LifecycleMark.Decide(previous);
            if (byMark != null) return byMark;
            if (previous == null || string.IsNullOrEmpty(previous.sessionUid) || previous.clean || reason == null)
                return null;
            switch (reason)
            {
                case "crash":
                case "crash_native":
                case "anr":
                case "low_memory_foreground":
                case "hang":
                    return PrevEndType.BackgroundThenKilled;
                case "low_memory_background":
                    return previous.backgroundAt > 0 && exitAtMs is { } at &&
                           at - previous.backgroundAt <= LOW_MEMORY_BACKGROUND_WINDOW_MS
                        ? PrevEndType.BackgroundThenKilled
                        : null;
                default:
                    return null;
            }
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PrevEndType
    {
        [EnumMember(Value = "abnormal")] Abnormal,
        [EnumMember(Value = "background_then_killed")] BackgroundThenKilled
    }

    /// <summary>
    /// Dấu vân tay của một lỗi C# — <c>exception_type</c> · <c>exception_top_frame</c> · <c>exception_hash</c>, không gửi stack
    /// trace (hợp đồng S2). <b>Cách chuẩn hoá và hàm băm là hợp đồng — đổi thì cần thư era.</b>
    /// <list type="number">
    /// <item>Mỗi khung: bỏ phần <c>(at …)</c> và <c>in &lt;…&gt;:N</c> (đường dẫn, số dòng), bỏ địa chỉ <c>[0x…]</c>, bỏ danh sách tham số <c>(…)</c>,
    /// bỏ tham số generic (<c>[…]</c>, <c>`N</c>), bỏ khoảng trắng thừa.</item>
    /// <item><c>exception_top_frame</c> = khung chuẩn hoá ĐẦU TIÊN không thuộc Unity / hệ thống (tiền tố
    /// <see cref="kNonGamePrefixes"/>); không có thì vắng.</item>
    /// <item><c>exception_hash</c> = FNV-1a 64 bit (hex thường, 16 ký tự) của <c>type</c> + 5 khung chuẩn hoá đầu (mọi khung),
    /// nối bằng <c>\n</c>. Cùng lỗi luôn cùng hash, giữa máy và giữa bản build.</item>
    /// </list>
    /// </summary>
    public static class ExceptionFingerprint
    {
        public const int HASH_FRAMES = 5;

        private static readonly string[] kNonGamePrefixes =
        {
            "UnityEngine.", "UnityEditor.", "Unity.", "System.", "Mono.", "Microsoft.", "Newtonsoft.",
            // Khung Java của ngoại lệ Java không ai bắt (đợt 2)
            "java.", "javax.", "android.", "androidx.", "kotlin.", "kotlinx.", "dalvik.", "com.android.", "com.unity3d."
        };

        private static readonly Regex kAtPrefix = new(@"^at\s+", RegexOptions.Compiled);
        private static readonly Regex kAt = new(@"\s*\(at [^)]*\)\s*$", RegexOptions.Compiled);
        private static readonly Regex kAddress = new(@"\s*\[0x[0-9a-fA-F]+\]", RegexOptions.Compiled);
        private static readonly Regex kIn = new(@"\s+in\s+\S+:\d+\s*$", RegexOptions.Compiled);
        private static readonly Regex kArgs = new(@"\s*\([^()]*\)", RegexOptions.Compiled);
        private static readonly Regex kGeneric = new(@"\[[^\[\]]*\]|`\d+", RegexOptions.Compiled);

        public static (string type, string topFrame, string hash) From(string condition, string stackTrace)
        {
            var type = ExceptionType(condition);
            var frames = NormalizedFrames(stackTrace);
            string top = null;
            foreach (var frame in frames)
            {
                if (IsNonGame(frame)) continue;
                top = frame;
                break;
            }

            var hashInput = new StringBuilder(type ?? "");
            for (var i = 0; i < frames.Count && i < HASH_FRAMES; i++) hashInput.Append('\n').Append(frames[i]);
            return (type, top, Fnv1a64(hashInput.ToString()));
        }

        /// <summary>"NullReferenceException: Object reference…" ⇒ "NullReferenceException". Không có dấu ':' thì cả câu (cắt 128).</summary>
        public static string ExceptionType(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return null;
            var colon = condition.IndexOf(':');
            var type = (colon > 0 ? condition.Substring(0, colon) : condition).Trim();
            return type.Length > 128 ? type.Substring(0, 128) : type;
        }

        public static List<string> NormalizedFrames(string stackTrace)
        {
            var frames = new List<string>();
            if (string.IsNullOrEmpty(stackTrace)) return frames;
            foreach (var raw in stackTrace.Split('\n'))
            {
                var frame = raw.Trim();
                if (frame.Length == 0) continue;
                frame = kAtPrefix.Replace(frame, ""); // stack kiểu .NET (Exception.StackTrace của lỗi chết)
                frame = kAt.Replace(frame, "");
                frame = kIn.Replace(frame, "");
                frame = kAddress.Replace(frame, "");
                frame = kArgs.Replace(frame, "");
                frame = kGeneric.Replace(frame, "");
                frame = Regex.Replace(frame, @"\s+", " ").Trim();
                if (frame.Length > 0) frames.Add(frame);
            }

            return frames;
        }

        public static string Fnv1a64(string input)
        {
            unchecked
            {
                var hash = 14695981039346656037UL;
                foreach (var b in Encoding.UTF8.GetBytes(input))
                {
                    hash ^= b;
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16");
            }
        }

        private static bool IsNonGame(string frame)
        {
            foreach (var prefix in kNonGamePrefixes)
                if (frame.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Chống ngập <c>f_sdk_app_issue</c> (hợp đồng S2): theo khoá (<c>kind</c>, với <c>exception</c> là <c>kind</c> +
    /// <c>exception_hash</c>), tối đa 1 dòng / 60 giây và 10 dòng / phiên. Phần bị chặn cộng vào <c>suppressed_count</c> của
    /// dòng KẾ TIẾP cùng khoá. Pure — caller truyền mốc giờ (ms) và khoá.
    /// </summary>
    public class AppIssueRateLimiter
    {
        public const long MIN_INTERVAL_MS = 60_000;
        public const int MAX_PER_SESSION = 10;

        private readonly Dictionary<string, (long lastAt, int sent, int suppressed)> _byKey = new();

        /// <returns>Cho gửi không, và số dòng đã bị chặn trước đó (null nếu 0) để đính lên dòng này.</returns>
        public (bool allowed, int? suppressed) TryPass(string key, long now)
        {
            if (!_byKey.TryGetValue(key, out var entry))
            {
                _byKey[key] = (now, 1, 0);
                return (true, null);
            }

            if (entry.sent >= MAX_PER_SESSION || now - entry.lastAt < MIN_INTERVAL_MS)
            {
                _byKey[key] = (entry.lastAt, entry.sent, entry.suppressed + 1);
                return (false, null);
            }

            _byKey[key] = (now, entry.sent + 1, 0);
            return (true, entry.suppressed > 0 ? entry.suppressed : null);
        }
    }

    /// <summary>
    /// Dấu vòng đời của một lần chạy (hợp đồng S2 <c>prev_exit</c>), ghi xuống đĩa mỗi ~15 giây và lúc xuống nền / quay lại /
    /// thoát sạch. Lần chạy sau đọc dấu cũ để biết lần trước kết thúc thế nào. Không dùng chung dấu <c>flush_lost</c> của Ui.
    /// </summary>
    [Serializable]
    public class LifecycleMark
    {
        public string sessionUid;
        public string playTurnId;
        public long lastAliveAt;
        public bool backgrounded;
        public bool clean;
        public bool freezeLong;

        /// <summary>pid của tiến trình (Android) — ghép mục <c>ApplicationExitInfo</c> ở lần mở sau.</summary>
        public int pid;

        /// <summary>Lần xuống nền gần nhất (epoch ms); 0 nếu đang ở trước.</summary>
        public long backgroundAt;
        public string fatalExceptionType;
        public string fatalExceptionTopFrame;
        public string fatalExceptionHash;

        public bool HasFatal => !string.IsNullOrEmpty(fatalExceptionHash);

        /// <summary>
        /// Đợt 1 (chỉ C#): <c>abnormal</c> khi dấu cũ còn "đang chạy ở trước" (không xuống nền, không thoát sạch) — văng / ANR /
        /// bị buộc dừng lúc đang mở. Xuống nền rồi bị giết thì chỉ gửi khi biết lý do bất thường (đợt 2, Android 11+) ⇒ null.
        /// Không có dấu cũ (lần đầu lên bản có dấu) ⇒ null.
        /// </summary>
        public static PrevEndType? Decide(LifecycleMark previous)
        {
            if (previous == null || string.IsNullOrEmpty(previous.sessionUid)) return null;
            if (previous.clean) return null;
            // Lỗi C# IsTerminating đã ghi dấu = chắc chắn chết bất thường, kể cả lúc đang nằm nền
            if (previous.HasFatal) return PrevEndType.Abnormal;
            if (previous.backgrounded) return null;
            return PrevEndType.Abnormal;
        }
    }
}
