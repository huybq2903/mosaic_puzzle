/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Bản chụp cho đường gửi khẩn (hợp đồng đợt 3 S2 "Bản chụp và heartbeat kèm sự cố"). Main thread dựng rồi thay tham
    /// chiếu một lần (<c>Volatile.Write</c>); luồng canh / handler lỗi chết chỉ đọc. <b>Bất biến</b>: không ai sửa
    /// dictionary sau khi dựng — mọi bước ghép đều chép ra bản mới (<see cref="EmergencyRows"/>).
    /// </summary>
    public sealed class EmergencySnapshot
    {
        /// <summary>Lượt mà <see cref="Heartbeat"/> tả; null nếu lúc dựng không có lượt mở.</summary>
        public string TurnId { get; }

        /// <summary>Mốc đồng hồ đơn điệu (ms) lúc dựng — khối sổ đang chạy được cộng nốt từ đây.</summary>
        public long BuiltAtMs { get; }

        /// <summary>Payload heartbeat đã trang trí (chưa có uuid / giờ / số thứ tự); null nếu không có lượt mở.</summary>
        public IReadOnlyDictionary<string, object> Heartbeat { get; }

        /// <summary>Payload mẫu <c>f_sdk_app_issue</c> đã trang trí (field chung, màn, lượt) — field theo loại ghép lúc sự cố.</summary>
        public IReadOnlyDictionary<string, object> IssueTemplate { get; }

        /// <summary>Field sổ B6 của khối đang tính lúc dựng (vd <c>t_ready_sec</c>); null nếu đồng hồ lượt đang dừng.</summary>
        public string RunningLedgerKey { get; }

        /// <summary>x2 đang bật trong khối bận / làm được lúc dựng (cộng nốt cả <c>t_speed_on_sec</c>).</summary>
        public bool SpeedRunning { get; }

        /// <summary>Màn hiện tại lúc dựng (<c>is_loading</c> của <c>freeze_long</c>).</summary>
        public string Screen { get; }

        public string HeartbeatEvent { get; }
        public string IssueEvent { get; }
        public string PackageName { get; }
        public string Platform { get; }

        /// <summary>Cấu hình đang tắt gửi log (<c>fCoreAnalyticShouldNotSendLogToServer</c>) lúc dựng.</summary>
        public bool SendDisabled { get; }

        public EmergencySnapshot(
            string turnId, long builtAtMs, IReadOnlyDictionary<string, object> heartbeat,
            IReadOnlyDictionary<string, object> issueTemplate, string runningLedgerKey, bool speedRunning, string screen,
            string heartbeatEvent, string issueEvent, string packageName, string platform, bool sendDisabled)
        {
            HeartbeatEvent = heartbeatEvent;
            IssueEvent = issueEvent;
            TurnId = turnId;
            BuiltAtMs = builtAtMs;
            Heartbeat = heartbeat;
            IssueTemplate = issueTemplate;
            RunningLedgerKey = runningLedgerKey;
            SpeedRunning = speedRunning;
            Screen = screen;
            PackageName = packageName;
            Platform = platform;
            SendDisabled = sendDisabled;
        }
    }

    /// <summary>
    /// Lúc nào main thread dựng lại bản chụp khẩn. Dựng <b>ngay</b> khi lượt mở đổi (sự cố ngay sau <c>OnStart</c> vẫn có
    /// heartbeat — <c>heartbeat_skipped</c> chỉ khi chưa kịp tới khung kế) hoặc vừa làm mới APS; sổ / tham số / màn đổi thì
    /// dựng nhưng không dày hơn <see cref="MIN_REBUILD_MS"/>; không gì đổi thì vẫn dựng mỗi <see cref="MAX_AGE_MS"/> (field
    /// chung, gói hiệu năng). Thời gian trôi giữa hai lần dựng do <see cref="EmergencyRows.AccrueLedger"/> cộng nốt.
    /// </summary>
    public static class SnapshotRebuildPolicy
    {
        public const long MIN_REBUILD_MS = 1_000;
        public const long MAX_AGE_MS = 5_000;

        public static bool Due(long now, long? builtAt, bool turnChanged, bool refreshed, bool contentChanged)
        {
            if (!builtAt.HasValue || turnChanged || refreshed) return true;
            var age = now - builtAt.Value;
            return age >= MAX_AGE_MS || (contentChanged && age >= MIN_REBUILD_MS);
        }
    }

    /// <summary>
    /// Ghép dòng khẩn từ bản chụp, tại lúc sự cố. Pure — không IO, không API Unity, không khoá: chạy được trên luồng canh
    /// lúc main thread đang đứng (có thể đang giữ khoá của service khác).
    /// </summary>
    public static class EmergencyRows
    {
        public const string UUID = "uuid";
        public const string CREATED_DATE_LOCAL = "createdDateLocal";
        public const string CREATE_ID = "logTypeCreateId";
        public const string SEND_ID = "logTypeSendId";
        public const string TOTAL_SEC = "t_total_sec";
        public const string SPEED_ON_SEC = "t_speed_on_sec";
        public const string SINCE_LAST_ACTION_SEC = "sec_since_last_action";
        public const string HEARTBEAT_SKIPPED = "heartbeatSkipped";

        /// <summary>
        /// Các dòng khẩn của một sự cố: dòng <c>f_sdk_app_issue</c> (mẫu + <paramref name="issueFields"/>; field null = bỏ)
        /// và, nếu bản chụp có heartbeat của ĐÚNG lượt đang mở, một heartbeat (cộng nốt sổ tới <paramref name="nowMs"/>).
        /// <list type="bullet">
        /// <item>Có lượt mở mà bản chụp chưa có heartbeat của lượt đó (sự cố ngay sau <c>OnStart</c>) ⇒
        /// <c>heartbeat_skipped = true</c>.</item>
        /// <item>Heartbeat tối đa một lần mỗi lượt: <paramref name="claimHeartbeat"/> chỉ được gọi khi có heartbeat để gửi,
        /// trả false nếu lượt này đã gửi rồi.</item>
        /// <item><c>play_turn_id</c> của dòng sự cố lấy theo <paramref name="openTurnId"/> (đúng lúc sự cố), không theo mẫu.</item>
        /// </list>
        /// </summary>
        public static List<(string @event, Dictionary<string, object> row)> Compose(
            EmergencySnapshot snapshot, string openTurnId, IReadOnlyDictionary<string, object> issueFields,
            Func<bool> claimHeartbeat, long nowMs, string createdDateLocal, Func<string> newUuid)
        {
            var rows = new List<(string, Dictionary<string, object>)>(2);
            var issue = Stamp(snapshot.IssueTemplate, newUuid(), createdDateLocal);
            if (issueFields != null)
                foreach (var (key, value) in issueFields)
                    if (value == null) issue.Remove(key);
                    else issue[key] = value;
            if (openTurnId == null) issue.Remove(TurnCustomInfoRepository.PLAY_TURN_ID_KEY);
            else issue[TurnCustomInfoRepository.PLAY_TURN_ID_KEY] = openTurnId;

            var hasHeartbeat = openTurnId != null && snapshot.TurnId == openTurnId && snapshot.Heartbeat != null;
            if (openTurnId != null && !hasHeartbeat) issue[HEARTBEAT_SKIPPED] = true;
            rows.Add((snapshot.IssueEvent, issue));

            if (!hasHeartbeat || claimHeartbeat == null || !claimHeartbeat()) return rows;
            var heartbeat = Stamp(snapshot.Heartbeat, newUuid(), createdDateLocal);
            AccrueLedger(heartbeat, nowMs - snapshot.BuiltAtMs, snapshot.RunningLedgerKey, snapshot.SpeedRunning);
            rows.Add((snapshot.HeartbeatEvent, heartbeat));
            return rows;
        }

        /// <summary>
        /// Chép payload và đóng dấu của lúc sự cố: <c>uuid</c> mới, <c>createdDateLocal</c>. Hai số thứ tự
        /// (<c>logTypeCreateId</c> / <c>logTypeSendId</c>) bị <b>bỏ</b>: số đó nằm trong pool (có khoá, main thread có
        /// thể đang giữ), còn đặt trước mỗi lần dựng bản chụp thì "đốt" số ⇒ khoảng hở giả như mất log. Vắng = dòng khẩn.
        /// </summary>
        public static Dictionary<string, object> Stamp(
            IReadOnlyDictionary<string, object> source, string uuid, string createdDateLocal)
        {
            var row = new Dictionary<string, object>(source.Count + 4);
            foreach (var (key, value) in source) row[key] = value;
            row[UUID] = uuid;
            row[CREATED_DATE_LOCAL] = createdDateLocal;
            row.Remove(CREATE_ID);
            row.Remove(SEND_ID);
            return row;
        }

        /// <summary>
        /// Cộng nốt <paramref name="elapsedMs"/> (từ lúc dựng bản chụp tới lúc sự cố) vào khối sổ đang chạy, tổng, x2 và
        /// "giây từ thao tác cuối" — đúng như sổ tự cộng nếu được hỏi lúc đó. Field nào không có trên dòng (khả năng chưa
        /// khai) thì không thêm. Ba field khoảng không thao tác (mốc 10/30/120 giây) KHÔNG cộng nốt: khoảng đang dở có
        /// thể vượt mốc trong lúc đơ, cộng thì phải đoán.
        /// </summary>
        public static void AccrueLedger(Dictionary<string, object> row, long elapsedMs, string runningKey, bool speedRunning)
        {
            if (elapsedMs <= 0 || runningKey == null) return;
            var sec = elapsedMs / 1000.0;
            Add(row, TOTAL_SEC, sec);
            Add(row, runningKey, sec);
            if (speedRunning) Add(row, SPEED_ON_SEC, sec);
            Add(row, SINCE_LAST_ACTION_SEC, sec);
        }

        private static void Add(Dictionary<string, object> row, string key, double sec)
        {
            if (!row.TryGetValue(key, out var value) || value == null) return;
            row[key] = Math.Round(Convert.ToDouble(value) + sec, 2);
        }
    }

    /// <summary>
    /// Luật bắn <c>freeze_long</c> của luồng canh (S2 "Canh đơ"): khung Unity đứng tới mốc <see cref="FREEZE_LONG_MS"/>
    /// lúc app ở trước và không có QC toàn màn. Một lần mỗi đợt đứng; khung chạy lại là hết đợt. Lúc tạm dừng canh
    /// (nền, QC, mất focus) mốc đặt lại — quay lại không bị tính là đã đứng suốt lúc vắng.
    /// <br/>Pure — caller truyền giờ (ms, đồng hồ đơn điệu) và số khung; một luồng duy nhất gọi.
    /// </summary>
    public class FreezeWatchState
    {
        public const long FREEZE_LONG_MS = 5_000;

        private long _lastFrame = -1;
        private long _lastChangeAt;
        private bool _fired;

        /// <summary>Đang trong một đợt đứng đã bắn <c>freeze_long</c>.</summary>
        public bool Fired => _fired;

        /// <returns>true đúng một lần mỗi đợt đứng, ở lần quan sát đầu tiên vượt mốc.</returns>
        public bool Observe(long now, long frameCount, bool suspended)
        {
            if (suspended || frameCount != _lastFrame)
            {
                _lastFrame = frameCount;
                _lastChangeAt = now;
                _fired = false;
                return false;
            }

            if (_fired || now - _lastChangeAt < FREEZE_LONG_MS) return false;
            _fired = true;
            return true;
        }
    }

    /// <summary>
    /// Nhịp làm mới tham số APS cho bản chụp (hợp đồng đợt 3 B5 §5.3): khoảng mỗi <see cref="INTERVAL_MS"/> khi có lượt
    /// mở, app ở trước, không có QC toàn màn. Một lần gọi quá <see cref="MAX_CALL_MS"/> thì ngừng tới hết phiên (lúc
    /// xuống nền vẫn gọi APS như thường — không qua lớp này).
    /// <br/>Pure; main thread gọi.
    /// </summary>
    public class ApsRefreshState
    {
        public const long INTERVAL_MS = 10_000;
        public const int MAX_CALL_MS = 16;

        private long? _lastAt;

        /// <summary><c>aps_refresh_disabled</c>: true nếu đã ngừng, null nếu không (vắng ≠ false).</summary>
        public bool? Disabled { get; private set; }

        /// <summary><c>aps_refresh_max_ms</c>: lần gọi lâu nhất trong phiên; null nếu chưa làm mới lần nào.</summary>
        public int? MaxMs { get; private set; }

        public bool Due(long now, bool eligible)
        {
            if (!eligible || Disabled == true) return false;
            if (_lastAt.HasValue && now - _lastAt.Value < INTERVAL_MS) return false;
            return true;
        }

        /// <summary>Ghi một lần làm mới (thời gian của lần gọi lâu nhất trong lần làm mới này).</summary>
        public void Record(long now, double callMs)
        {
            _lastAt = now;
            var ms = (int)Math.Ceiling(Math.Max(0, callMs));
            MaxMs = MaxMs.HasValue ? Math.Max(MaxMs.Value, ms) : ms;
            if (callMs > MAX_CALL_MS) Disabled = true;
        }
    }
}
