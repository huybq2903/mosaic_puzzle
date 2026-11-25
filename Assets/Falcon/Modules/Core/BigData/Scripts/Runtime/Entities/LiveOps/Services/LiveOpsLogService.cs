/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Đường duy nhất sinh <see cref="FLiveOpsLog"/> — giữ ba thứ mà game không nên tự làm:
    ///     <br/>1. <b>Lịch đợt</b>: khai một lần qua <see cref="DeclareCycle"/>, mọi bước sau tự
    ///     mang <c>cycleIndex</c>/<c>cycleStartTs</c>/<c>cycleEndTs</c> — hết cảnh mỗi event tự
    ///     viết helper rồi quên một field (Master League M4 treo vì đúng chuyện này).
    ///     <br/>2. <b>Van lọc trùng</b>: một dòng cho mỗi
    ///     <c>(event, cycle, round, action, stage)</c> mỗi thiết bị. Chỉ MỘT hình dạng nên không
    ///     phải khai shape, cũng không có ca "quên Register là trượt trắng".
    ///     <br/>3. <b>Năm cặp mốc khoảng cách</b> + <c>liveOpsEnterTs</c>: xem doc từng field trên
    ///     <see cref="FLiveOpsLog"/>.
    ///     <br/><br/><b>Sổ mốc: HAI Ô mỗi event</b> — đợt <i>đang chạy</i> và đợt <i>vừa đóng</i>.
    ///     Nhờ ô thứ hai, dòng tổng kết mùa 5 gửi trong lúc mùa 6 đang chạy vẫn đo được từ mốc user
    ///     vào mùa 5, thay vì đạp mất sổ của mùa 6 (lỗi 19/10). Hai ô là trần cứng: event mùa chạy
    ///     vài năm vẫn chỉ hai ô, không sinh key mới mỗi đợt. Đợt thứ ba xuất hiện thì ô **cũ nhất**
    ///     bị đạp ra, và dòng của một đợt đã mất ô thì các field khoảng cách <b>vắng mặt</b> — thà
    ///     thiếu hơn bịa "vừa vào đợt lúc này".
    ///     <br/><b>LƯỢT (round) KHÔNG nằm trong nhãn ô.</b> Nó ở trong khoá van lọc (mỗi bảng đấu là
    ///     một lượt riêng), nhưng bước-của-đợt (<c>eligible</c>/<c>open</c>, lượt rỗng) và
    ///     bước-của-lượt (<c>join</c>/<c>milestone</c>) phải dùng CHUNG một đồng hồ: để riêng thì
    ///     hai bên đạp nhau mỗi nhịp và mất đúng khoảng cách <c>eligible → join</c>. Hệ quả cho phía
    ///     đọc số: <c>*_since_enter</c> luôn là "từ lúc vào ĐỢT", kể cả Race — muốn "từ lúc vào bảng
    ///     này" thì đọc <c>*_since_prev_step</c>.
    /// </summary>
    public class LiveOpsLogService : MySingleton<LiveOpsLogService>
    {
        private const string PREFIX = "liveOps_";

        // Lịch đợt do DeclareCycle khai — con trỏ "đợt đang chạy", không thuộc ô nào
        private const string DECLARED_CYCLE = "_declaredCycle";
        private const string DECLARED_INDEX = "_declaredIndex";
        private const string DECLARED_START = "_declaredStart";
        private const string DECLARED_END = "_declaredEnd";

        // Hai ô sổ mốc mỗi event
        private const string SLOT_A = "_a";
        private const string SLOT_B = "_b";

        // Các field TRONG một ô
        private const string CYCLE = "_cycle";
        private const string ROUND = "_round";
        private const string ENTER_TS = "_enterTs";
        private const string MAX_STAGE = "_maxStage";
        private const string LAST_COUNTED = "_loLastCounted";

        // Lịch đợt chụp lại lúc mở ô — để dòng của đợt đã đóng không phải mượn lịch đợt đang chạy
        private const string SCHED_INDEX = "_schedIndex";
        private const string SCHED_START = "_schedStart";
        private const string SCHED_END = "_schedEnd";

        // Mốc lúc VÀO đợt (toàn cục) và lúc BƯỚC TRƯỚC
        private const string PLAY_ENTER = "_playEnter";
        private const string PLAY_STEP = "_playStep";
        private const string PASS_ENTER = "_passEnter";
        private const string PASS_STEP = "_passStep";
        private const string ATTEMPT_ENTER = "_attemptEnter";
        private const string ATTEMPT_STEP = "_attemptStep";

        // Bộ đếm ván CỦA RIÊNG event (chỉ ván có khai event) — bắt đầu từ 0 ở mốc vào đợt
        /// <summary>
        ///     Cờ "event này ĐÃ TỪNG khai event lên dòng level" — khoá theo EVENT, không theo ô, nên
        ///     sống qua các đợt. Chỉ cần SỰ TỒN TẠI nên đọc bằng <c>HasKey</c>, không so giá trị
        ///     (pool so KIỂU khi đọc; dùng HasKey là miễn nhiễm chuyện đó).
        ///     <br/>Vì sao cần cờ riêng thay vì xem bộ đếm có khoá hay không: <see cref="Open"/> ghi
        ///     sẵn <c>0</c> cho hai bộ đếm lúc mở đợt, nên "có khoá" luôn đúng và không phân biệt
        ///     được event KHÔNG khai level với event khai mà chưa chơi ván nào (án 26/10: cặp
        ///     <c>liveOpsLevel*</c> lên dây với giá trị 0 ở MỌI dòng của mọi event, khiến phía đọc số
        ///     tưởng SDK gửi đôi khoá — mà `0` ở đó đang nói "chơi 0 ván trong event" trong khi sự
        ///     thật là "không áp dụng").
        /// </summary>
        private const string LO_DECLARED_EVER = "_loDeclaredEver";

        private const string LO_PASS_COUNT = "_loPassCount";
        private const string LO_PASS_STEP = "_loPassStep";
        private const string LO_ATTEMPT_COUNT = "_loAttemptCount";
        private const string LO_ATTEMPT_STEP = "_loAttemptStep";

        // Bộ đếm lượt chơi TOÀN CỤC + id lượt đã đếm gần nhất (chống đếm đôi khi double-fire Start)
        private const string GLOBAL_ATTEMPTS = "liveOps_globalAttempts";
        private const string GLOBAL_ATTEMPTS_LAST_TURN = "liveOps_globalAttemptsLastTurn";

        private static readonly string[] SlotFieldsClearedOnOpen =
        {
            ROUND, MAX_STAGE, LAST_COUNTED, SCHED_INDEX, SCHED_START, SCHED_END,
            PLAY_STEP, PASS_STEP, ATTEMPT_STEP, LO_PASS_STEP, LO_ATTEMPT_STEP
        };

        private readonly IDataPool _dataPool;
        private readonly ITimeRepository _timeRepository;
        private readonly IFPlayerSessionService _sessionService;
        private readonly IFPlayerGeneralRepository _generalRepository;
        private readonly LogScheduleService _logScheduleService;
        private readonly object _lock = new();

        public LiveOpsLogService(
            IDataPool dataPool, ITimeRepository timeRepository,
            IFPlayerSessionService sessionService, IFPlayerGeneralRepository generalRepository,
            LogScheduleService logScheduleService)
        {
            _dataPool = dataPool;
            _timeRepository = timeRepository;
            _sessionService = sessionService;
            _generalRepository = generalRepository;
            _logScheduleService = logScheduleService;
        }

        // ---------------- Khai đợt ----------------

        /// <summary>
        ///     Khai đợt đang chạy của một event — gọi khi data đợt về (gọi lại mỗi lần mở app cũng
        ///     được, ghi đè bằng giá trị mới nhất). KHÔNG bắn log: nó chỉ ghi state để các bước sau
        ///     tự mang lịch đợt.
        ///     <br/>Mốc <paramref name="cycleStartTs"/>/<paramref name="cycleEndTs"/> lấy từ CONFIG
        ///     event — đúng con số UI countdown đang vẽ, nên gần như không thể sai.
        ///     <br/>Ops gia hạn đợt (đổi <c>cycleEndTs</c> giữa đợt) thì gọi lại là ô sổ của đợt đó
        ///     cũng được cập nhật theo — không để lại bản chụp cũ.
        /// </summary>
        public void DeclareCycle(
            string liveOpsEvent, string cycleId,
            int? cycleIndex = null, long? cycleStartTs = null, long? cycleEndTs = null)
        {
            if (string.IsNullOrEmpty(liveOpsEvent))
            {
                AnalyticLogger.Instance.Warning("LiveOps.DeclareCycle bỏ qua: thiếu liveOpsEvent.");
                return;
            }

            lock (_lock)
            {
                // Đổi đợt là XOÁ lịch cũ trước khi ghi: game khai lại đợt lúc config chưa về (cả ba
                // module event đang truyền null khi GetTimeEnd() chưa có) thì đợt mới KHÔNG được
                // thừa kế giờ đóng của đợt trước — thừa kế là sai mà trông vẫn hợp lý.
                if (DeclaredCycle(liveOpsEvent) != (cycleId ?? string.Empty))
                {
                    _dataPool.Delete(Global(liveOpsEvent, DECLARED_INDEX));
                    _dataPool.Delete(Global(liveOpsEvent, DECLARED_START));
                    _dataPool.Delete(Global(liveOpsEvent, DECLARED_END));
                }

                _dataPool.Save(Global(liveOpsEvent, DECLARED_CYCLE), cycleId ?? string.Empty);
                if (cycleIndex.HasValue) _dataPool.Save(Global(liveOpsEvent, DECLARED_INDEX), cycleIndex.Value);
                if (cycleStartTs.HasValue) _dataPool.Save(Global(liveOpsEvent, DECLARED_START), cycleStartTs.Value);
                if (cycleEndTs.HasValue) _dataPool.Save(Global(liveOpsEvent, DECLARED_END), cycleEndTs.Value);

                var slot = FindSlot(liveOpsEvent, cycleId);
                if (slot != null) SnapshotSchedule(liveOpsEvent, slot, cycleId);
            }
        }

        /// <summary>
        ///     Mã đợt đang chạy của event (do <see cref="DeclareCycle"/> khai) — để game truyền
        ///     sang các stream khác mà không phải tự giữ state. Null nếu chưa khai.
        ///     <br/>⚠ Đây là "đợt đang chạy", KHÔNG phải "đợt mà dòng log thuộc về": nhận thưởng
        ///     mùa 5 trong mùa 6 thì game phải truyền tay mùa 5.
        /// </summary>
        [CanBeNull]
        public string CurrentCycleId(string liveOpsEvent)
        {
            if (string.IsNullOrEmpty(liveOpsEvent)) return null;
            var value = DeclaredCycle(liveOpsEvent);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        ///     Lượt (round) gần nhất SDK thấy trên một bước của event — null nếu event chưa bước nào
        ///     mang lượt.
        ///     <br/>🔴 <b>Đây là LỊCH SỬ LOG, không phải "lượt user đang mở".</b> Giá trị = lượt của
        ///     bước cuối cùng đã log trong đợt ĐANG CHẠY, nên nó <b>sai</b> ở đúng hai ca mà người ta
        ///     hay muốn dùng nó nhất:
        ///     <br/>· dòng <b>nhận muộn</b> (thuộc đợt cũ) — nó trả lượt của đợt đang chạy;
        ///     <br/>· <b>sau khi đổi thực thể</b> (đổi guild, vào bảng đấu mới) mà lượt mới chưa log
        ///       bước nào — nó còn trả lượt CŨ.
        ///     <br/>Dùng được khi game <b>biết chắc</b> lượt đang mở trùng lượt cuối đã log. Ngoài ca
        ///     đó thì game tự giữ lượt của mình và truyền vào; đọc hàm này rồi truyền vào
        ///     <c>Stamp</c> <b>không</b> biến cái đoán thành lời khai.
        /// </summary>
        [CanBeNull]
        public string CurrentRoundId(string liveOpsEvent)
        {
            if (string.IsNullOrEmpty(liveOpsEvent)) return null;
            return RoundOf(liveOpsEvent, DeclaredCycle(liveOpsEvent));
        }

        /// <summary>Mốc CAO NHẤT đã log trong đợt hiện tại (sổ local đời máy) — null nếu chưa mốc nào.</summary>
        public int? CurrentStage(string liveOpsEvent)
        {
            if (string.IsNullOrEmpty(liveOpsEvent)) return null;

            var slot = FindSlot(liveOpsEvent, DeclaredCycle(liveOpsEvent));
            if (slot == null) return null;

            var value = _dataPool.GetOrDefault(Key(liveOpsEvent, slot, MAX_STAGE), -1);
            return value >= 0 ? value : null;
        }

        /// <summary>
        ///     Bước này đã log chưa — đọc ĐÚNG sổ mà van lọc ghi, nên <paramref name="roundId"/>
        ///     phải truyền y như dòng đã mang (event không có lượt thì để null).
        ///     <br/>🔴 Trước 05/11 hàm này tự lấy mã lượt bằng <c>RoundOf</c>, trong khi van lọc
        ///     dùng <c>param.liveOpsRoundId</c> — <b>hai nguồn khác nhau cho cùng một ô</b>, nên câu
        ///     trả lời sai với mọi event đặt mã lượt lên MỘT SỐ bước mà không đặt lên số còn lại:
        ///     van ghi tên với lượt rỗng, hàm này dựng tên với lượt đang lưu trong sổ ⇒ báo "chưa
        ///     log" cho bước đã log. Đúng hình dạng Boat Rush (lượt trên <c>join</c>/<c>complete</c>
        ///     /<c>claim</c>, không trên <c>milestone</c>) và Team Quest (không trên
        ///     <c>eligible</c>/<c>open</c>). Và xml-doc cũ còn hứa thẳng là "đúng sổ van lọc đọc" —
        ///     lời hứa sai là chỗ nguy hiểm nhất, vì nó mời người ta bỏ sổ local tự viết.
        /// </summary>
        public bool HasPassed(string liveOpsEvent, string cycleId, string action, int? stage = null,
            [CanBeNull] string roundId = null)
        {
            return _dataPool.HasKey(StepKey(liveOpsEvent, cycleId, roundId, action, stage));
        }

        // ---------------- Bắn một mốc ----------------

        /// <summary>
        ///     Ghi một mốc. Trả về false nếu bước bị van lọc đánh trượt (đã log trong đợt/lượt này)
        ///     hoặc param thiếu <c>liveOpsEvent</c> — khi đó KHÔNG có log nào được gửi.
        /// </summary>
        public bool LogStep(LiveOpsParam param)
        {
            var log = BuildStep(param);
            if (log == null) return false;

            _logScheduleService.Enqueue(log);
            return true;
        }

        /// <summary>
        ///     Phần ruột của <see cref="LogStep"/>: van lọc + đóng dấu, KHÔNG gửi. Trả null nếu
        ///     bước bị chặn. Tách ra để test ghim được từng luật mà không phải dựng cả pipeline
        ///     gửi (cùng khuôn <c>FunnelLogService.Check</c>).
        /// </summary>
        [CanBeNull]
        public FLiveOpsLog BuildStep(LiveOpsParam param)
        {
            if (param == null) return null;
            param.CorrectValues();

            if (string.IsNullOrEmpty(param.liveOpsEvent))
            {
                AnalyticLogger.Instance.Warning(
                    "LiveOps.OnStep bỏ qua: thiếu liveOpsEvent — không có event thì dòng này không " +
                    "thuộc về đâu cả.");
                return null;
            }

            var log = new FLiveOpsLog(param);

            lock (_lock)
            {
                StampCycleMeta(param);

                var stepKey = StepKey(param.liveOpsEvent, param.liveOpsCycleId, param.liveOpsRoundId,
                    param.action, param.liveOpsStage);
                if (_dataPool.HasKey(stepKey))
                {
                    AnalyticLogger.Instance.Info(
                        $"LiveOps '{param.liveOpsEvent}': bước '{param.action}' " +
                        $"(stage {param.liveOpsStage?.ToString() ?? "-"}) đã log trong đợt " +
                        $"'{param.liveOpsCycleId}' — van lọc chặn, không gửi lại.");
                    return null;
                }

                _dataPool.Save(stepKey, _timeRepository.UtcNow());
                StampDistances(log);
            }

            return log;
        }

        // ---------------- Đếm ván chơi ----------------

        /// <summary>
        ///     Level stream báo về: một lượt chơi vừa MỞ hoặc vừa THẮNG. Gọi từ decor của level
        ///     (<see cref="LevelLogDecorService"/>) — service này chỉ cộng bộ đếm, TUYỆT ĐỐI không
        ///     sinh log (sinh log trong decor là đệ quy vô hạn).
        ///     <br/>Bộ đếm toàn cục đếm theo <paramref name="playTurnId"/> nên double-fire
        ///     <c>OnStart</c> cùng một lượt không làm phồng; <c>StartWhileOpen</c> sinh id mới nên
        ///     được tính — đúng, vì một lượt mới thật sự đã mở.
        ///     <br/>Bộ đếm RIÊNG của event chỉ cộng khi dòng level KHAI event (cam kết độc quyền,
        ///     xem hợp đồng §3).
        /// </summary>
        public void ReportLevel(
            [CanBeNull] string playTurnId, LevelStatus status, [CanBeNull] ALiveOpsParam liveOps)
        {
            lock (_lock)
            {
                if (status == LevelStatus.Start && !string.IsNullOrEmpty(playTurnId) &&
                    _dataPool.GetOrDefault(GLOBAL_ATTEMPTS_LAST_TURN, string.Empty) != playTurnId)
                {
                    _dataPool.Save(GLOBAL_ATTEMPTS_LAST_TURN, playTurnId);
                    _dataPool.Save(GLOBAL_ATTEMPTS, _dataPool.GetOrDefault(GLOBAL_ATTEMPTS, 0) + 1);
                }

                var liveOpsEvent = liveOps?.liveOpsEvent;
                if (string.IsNullOrEmpty(liveOpsEvent)) return;

                // Dòng level THIẾU mã đợt thì chỉ cảnh báo, KHÔNG reset sổ mốc: một dòng hỏng
                // không được quyền xoá tiến độ đo của cả đợt.
                if (string.IsNullOrEmpty(liveOps.liveOpsCycleId))
                {
                    AnalyticLogger.Instance.Warning(
                        $"Dòng level khai liveOpsEvent '{liveOpsEvent}' nhưng thiếu liveOpsCycleId — " +
                        "không đếm được vào bộ đếm riêng của event. Khai thì khai đủ cặp " +
                        "(event, cycle).");
                    return;
                }

                // Dòng level là bằng chứng đầu tiên của đợt mới thì mở ô theo nó
                var slot = ResolveSlot(liveOpsEvent, liveOps.liveOpsCycleId, out _);
                if (slot == null) return;

                // Mỗi (lượt, kết cục) chỉ đếm một lần — double-fire terminal không làm phồng
                var countKey = Key(liveOpsEvent, slot, LAST_COUNTED);
                var stamp = (playTurnId ?? string.Empty) + "_" + status;
                if (_dataPool.GetOrDefault(countKey, string.Empty) == stamp) return;
                _dataPool.Save(countKey, stamp);

                // Dòng level này KHAI event ⇒ từ nay event được coi là "có bộ đếm riêng", và cặp
                // liveOpsLevel* được phép lên dây. Ghi theo EVENT nên cờ sống qua các đợt.
                _dataPool.Save(KeyOfEvent(liveOpsEvent, LO_DECLARED_EVER), 1);

                if (status == LevelStatus.Pass)
                    Bump(liveOpsEvent, slot, LO_PASS_COUNT);

                if (status == LevelStatus.Start)
                    Bump(liveOpsEvent, slot, LO_ATTEMPT_COUNT);
            }
        }

        // ---------------- Ruột: lịch đợt ----------------

        /// <summary>
        ///     Điền lịch đợt — chỉ điền chỗ game BỎ TRỐNG (khuôn <c>PutIfAbsent</c> của cả hệ):
        ///     game truyền tay thì game thắng, vì chỉ game biết dòng này thuộc đợt nào.
        ///     <br/>Dòng của đợt ĐÃ ĐÓNG lấy lịch từ <b>bản chụp trong ô sổ của chính đợt đó</b>,
        ///     tuyệt đối không mượn lịch của đợt đang chạy: một dòng mang mã mùa 5 mà giờ mở–đóng
        ///     của mùa 6 là thứ phía đọc số không gỡ được (lỗi 19/10).
        /// </summary>
        private void StampCycleMeta(LiveOpsParam param)
        {
            var ev = param.liveOpsEvent;
            param.liveOpsCycleId ??= CurrentCycleId(ev);

            var declared = DeclaredCycle(ev);
            if (string.IsNullOrEmpty(declared) || declared == (param.liveOpsCycleId ?? string.Empty))
            {
                param.cycleIndex ??= Num<int>(Global(ev, DECLARED_INDEX));
                param.cycleStartTs ??= Num<long>(Global(ev, DECLARED_START));
                param.cycleEndTs ??= Num<long>(Global(ev, DECLARED_END));
                return;
            }

            var slot = FindSlot(ev, param.liveOpsCycleId);
            if (slot == null) return;

            param.cycleIndex ??= Num<int>(Key(ev, slot, SCHED_INDEX));
            param.cycleStartTs ??= Num<long>(Key(ev, slot, SCHED_START));
            param.cycleEndTs ??= Num<long>(Key(ev, slot, SCHED_END));
        }

        private void SnapshotSchedule(string ev, string slot, [CanBeNull] string cycleId)
        {
            if (DeclaredCycle(ev) != (cycleId ?? string.Empty)) return;

            Copy(Global(ev, DECLARED_INDEX), Key(ev, slot, SCHED_INDEX), 0);
            Copy(Global(ev, DECLARED_START), Key(ev, slot, SCHED_START), 0L);
            Copy(Global(ev, DECLARED_END), Key(ev, slot, SCHED_END), 0L);
        }

        private void Copy<T>(string from, string to, T sample)
        {
            if (_dataPool.HasKey(from)) _dataPool.Save(to, _dataPool.GetOrDefault(from, sample));
        }

        private T? Num<T>(string key) where T : struct
        {
            return _dataPool.HasKey(key) ? _dataPool.GetOrDefault(key, default(T)) : null;
        }

        // ---------------- Ruột: hai ô sổ mốc ----------------

        /// <summary>
        ///     Ô sổ mốc của một đợt, mở ô mới nếu cần. Null = dòng này không có ô và KHÔNG được mở:
        ///     các field khoảng cách phải vắng mặt.
        ///     <br/>Chỉ <b>đợt đang chạy</b> (hoặc event chưa khai lịch bao giờ) được mở ô. Dòng của
        ///     một đợt đã đóng mà ô đã bị đạp ra thì mở ô là vừa bịa "vừa vào đợt lúc này", vừa đạp
        ///     mất sổ của đợt đang chạy — nên thà không có số.
        /// </summary>
        [CanBeNull]
        private string ResolveSlot(string ev, [CanBeNull] string cycleId, out bool opened)
        {
            opened = false;

            var slot = FindSlot(ev, cycleId);
            if (slot != null) return slot;

            var declared = DeclaredCycle(ev);
            if (!string.IsNullOrEmpty(declared) && declared != (cycleId ?? string.Empty))
            {
                AnalyticLogger.Instance.Info(
                    $"LiveOps '{ev}': đợt '{cycleId}' không còn ô sổ mốc (SDK chỉ giữ đợt đang chạy " +
                    "+ đợt vừa đóng) — các field khoảng cách gửi VẮNG MẶT thay vì đo lại từ 0.");
                return null;
            }

            opened = true;
            return Open(ev, cycleId);
        }

        [CanBeNull]
        private string FindSlot(string ev, [CanBeNull] string cycleId)
        {
            if (Holds(ev, SLOT_A, cycleId)) return SLOT_A;
            return Holds(ev, SLOT_B, cycleId) ? SLOT_B : null;
        }

        private bool Holds(string ev, string slot, [CanBeNull] string cycleId)
        {
            return _dataPool.HasKey(Key(ev, slot, ENTER_TS)) &&
                   _dataPool.GetOrDefault(Key(ev, slot, CYCLE), string.Empty) == (cycleId ?? string.Empty);
        }

        /// <summary>
        ///     Mở ô cho một đợt mới: mốc vào đợt là BÂY GIỜ, hai bộ đếm riêng về 0, vạch "bước
        ///     trước" xoá sạch. Hết ô trống thì đạp ô <b>cũ nhất</b> (đợt lâu không ai chạm) ra.
        /// </summary>
        private string Open(string ev, [CanBeNull] string cycleId)
        {
            var slot = Evictable(ev);

            _dataPool.Save(Key(ev, slot, CYCLE), cycleId ?? string.Empty);
            _dataPool.Save(Key(ev, slot, ENTER_TS), _timeRepository.CurrentTimeMillis);
            _dataPool.Save(Key(ev, slot, PLAY_ENTER), PlaySec());
            _dataPool.Save(Key(ev, slot, PASS_ENTER), MaxPassedLevel());
            _dataPool.Save(Key(ev, slot, ATTEMPT_ENTER), (double)GlobalAttempts());
            _dataPool.Save(Key(ev, slot, LO_PASS_COUNT), 0);
            _dataPool.Save(Key(ev, slot, LO_ATTEMPT_COUNT), 0);
            foreach (var field in SlotFieldsClearedOnOpen) _dataPool.Delete(Key(ev, slot, field));

            SnapshotSchedule(ev, slot, cycleId);
            return slot;
        }

        /// <summary>Ô sẽ bị đạp ra khi cần mở đợt mới: ưu tiên ô trống, rồi đợt user vào SỚM nhất.</summary>
        private string Evictable(string ev)
        {
            if (!_dataPool.HasKey(Key(ev, SLOT_A, ENTER_TS))) return SLOT_A;
            if (!_dataPool.HasKey(Key(ev, SLOT_B, ENTER_TS))) return SLOT_B;

            // Không bao giờ đạp ô của ĐỢT ĐANG CHẠY — nó là sổ mọi chart đang đọc
            var declared = DeclaredCycle(ev);
            if (Holds(ev, SLOT_A, declared)) return SLOT_B;
            if (Holds(ev, SLOT_B, declared)) return SLOT_A;

            return _dataPool.GetOrDefault(Key(ev, SLOT_A, ENTER_TS), 0L) <=
                   _dataPool.GetOrDefault(Key(ev, SLOT_B, ENTER_TS), 0L)
                ? SLOT_A
                : SLOT_B;
        }

        private void Bump(string ev, string slot, string field)
        {
            _dataPool.Save(Key(ev, slot, field), _dataPool.GetOrDefault(Key(ev, slot, field), 0) + 1);
        }

        [CanBeNull]
        private string RoundOf(string ev, [CanBeNull] string cycleId)
        {
            var slot = FindSlot(ev, cycleId);
            if (slot == null) return null;

            var value = _dataPool.GetOrDefault(Key(ev, slot, ROUND), string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        // ---------------- Ruột: mốc khoảng cách ----------------

        /// <summary>Đóng <c>liveOpsEnterTs</c> + năm cặp mốc khoảng cách lên log.</summary>
        private void StampDistances(FLiveOpsLog log)
        {
            var param = log.param;
            var ev = param.liveOpsEvent;

            var slot = ResolveSlot(ev, param.liveOpsCycleId, out var opened);
            if (slot == null) return;

            if (!string.IsNullOrEmpty(param.liveOpsRoundId))
                _dataPool.Save(Key(ev, slot, ROUND), param.liveOpsRoundId);
            RememberStage(ev, slot, param);

            log.liveOpsEnterTs = _dataPool.GetOrDefault(Key(ev, slot, ENTER_TS), 0L);

            var playNow = PlaySec();
            var passNow = MaxPassedLevel();
            var attemptNow = GlobalAttempts();
            var loPassNow = _dataPool.GetOrDefault(Key(ev, slot, LO_PASS_COUNT), 0);
            var loAttemptNow = _dataPool.GetOrDefault(Key(ev, slot, LO_ATTEMPT_COUNT), 0);

            // Cặp RIÊNG của event chỉ có nghĩa với event khai event lên dòng level. Event chạy chồng
            // lên level thường (hợp đồng §3: KHÔNG được khai) thì cặp này phải VẮNG — gửi 0 là khai
            // "chơi 0 ván trong event" trong khi sự thật là "không áp dụng" (sửa 26/10).
            var declaresLevels = _dataPool.HasKey(KeyOfEvent(ev, LO_DECLARED_EVER));

            if (opened)
            {
                log.playSecSinceEnter = 0;
                log.levelPassesSinceEnter = 0;
                log.levelAttemptsSinceEnter = 0;
                if (declaresLevels)
                {
                    log.liveOpsLevelPassesSinceEnter = 0;
                    log.liveOpsLevelAttemptsSinceEnter = 0;
                }
            }
            else
            {
                log.playSecSinceEnter = Delta(playNow, Mark(ev, slot, PLAY_ENTER, playNow), ev, "giờ chơi", "mốc vào đợt");
                log.levelPassesSinceEnter = Delta(passNow, Mark(ev, slot, PASS_ENTER, passNow), ev, "số level", "mốc vào đợt");
                log.levelAttemptsSinceEnter = Delta(attemptNow, Mark(ev, slot, ATTEMPT_ENTER, attemptNow), ev, "số lượt", "mốc vào đợt");

                log.playSecSincePrevStep = StepDelta(ev, slot, PLAY_STEP, playNow, "giờ chơi");
                log.levelPassesSincePrevStep = StepDelta(ev, slot, PASS_STEP, passNow, "số level");
                log.levelAttemptsSincePrevStep = StepDelta(ev, slot, ATTEMPT_STEP, attemptNow, "số lượt");

                if (declaresLevels)
                {
                    log.liveOpsLevelPassesSinceEnter = loPassNow;
                    log.liveOpsLevelAttemptsSinceEnter = loAttemptNow;
                    log.liveOpsLevelPassesSincePrevStep = StepDelta(ev, slot, LO_PASS_STEP, loPassNow, "số level của event");
                    log.liveOpsLevelAttemptsSincePrevStep = StepDelta(ev, slot, LO_ATTEMPT_STEP, loAttemptNow, "số lượt của event");
                }
            }

            // Mốc luôn lưu dạng double: pool so KIỂU khi đọc, lưu int rồi đọc double là rơi
            // về giá trị mặc định (delta hoá 0) — sai im lặng.
            _dataPool.Save(Key(ev, slot, PLAY_STEP), playNow);
            _dataPool.Save(Key(ev, slot, PASS_STEP), passNow);
            _dataPool.Save(Key(ev, slot, ATTEMPT_STEP), (double)attemptNow);
            _dataPool.Save(Key(ev, slot, LO_PASS_STEP), (double)loPassNow);
            _dataPool.Save(Key(ev, slot, LO_ATTEMPT_STEP), (double)loAttemptNow);
        }

        private void RememberStage(string ev, string slot, LiveOpsParam param)
        {
            if (param.action != FLiveOpsAction.Milestone || !param.liveOpsStage.HasValue) return;

            var key = Key(ev, slot, MAX_STAGE);
            if (_dataPool.GetOrDefault(key, -1) >= param.liveOpsStage.Value) return;
            _dataPool.Save(key, param.liveOpsStage.Value);
        }

        private long? StepDelta(string ev, string slot, string field, double now, string measure)
        {
            var key = Key(ev, slot, field);
            return _dataPool.HasKey(key)
                ? Delta(now, _dataPool.GetOrDefault(key, now), ev, measure, "bước trước")
                : null;
        }

        private double Mark(string ev, string slot, string field, double fallback)
        {
            return _dataPool.GetOrDefault(Key(ev, slot, field), fallback);
        }

        /// <summary>
        ///     Khoảng giữa hai mốc, làm tròn về số nguyên. Số ÂM = sổ mốc (pool) lệch sổ nguồn
        ///     (giờ chơi / tiến độ level / bộ đếm) — hai sổ khác vòng đời nên có ca lệch (xoá data,
        ///     bài học SessionUid vs SessionId): trả VẮNG MẶT + cảnh báo, KHÔNG trả 0. Gửi 0 là bịa
        ///     "hai bước sát nhau", mà đó lại là con số người đọc tin nhất.
        /// </summary>
        private static long? Delta(double now, double mark, string ev, string measure, string what)
        {
            var delta = now - mark;
            if (delta >= 0) return (long)Math.Round(delta);

            AnalyticLogger.Instance.Warning(
                $"LiveOps '{ev}': {measure} tới {what} ra số âm ({delta:F1}) — sổ mốc lệch sổ " +
                "nguồn, bỏ trường thay vì gửi 0.");
            return null;
        }

        private double PlaySec()
        {
            return _sessionService?.TotalPlayTime.TotalSeconds ?? 0;
        }

        private double MaxPassedLevel()
        {
            return _generalRepository?.MaxPassedLevel ?? 0;
        }

        private int GlobalAttempts()
        {
            return _dataPool.GetOrDefault(GLOBAL_ATTEMPTS, 0);
        }

        private string DeclaredCycle(string ev)
        {
            return _dataPool.GetOrDefault(Global(ev, DECLARED_CYCLE), string.Empty);
        }

        private static string Global(string liveOpsEvent, string suffix)
        {
            return PREFIX + liveOpsEvent + suffix;
        }

        private static string Key(string liveOpsEvent, string slot, string field)
        {
            return PREFIX + liveOpsEvent + slot + field;
        }

        /// <summary>
        ///     Khoá theo EVENT (không theo ô) — dùng cho thứ sống qua các đợt. Không đụng khoá theo ô
        ///     vì ô luôn là <c>_a</c>/<c>_b</c>, còn field của họ này chỉ dùng ở đây.
        /// </summary>
        private static string KeyOfEvent(string liveOpsEvent, string field)
        {
            return PREFIX + liveOpsEvent + field;
        }

        /// <summary>
        ///     Khoá van lọc của MỘT bước — public để test ghim được bài học "khoá phải gồm cả
        ///     action VÀ stage": <c>milestone(3)</c> rồi <c>claim(3)</c> là hai bước thật cùng số
        ///     mốc, lọc theo stage trần là nuốt mất một bước.
        ///     <br/>⚠ <c>roundId</c> nằm ở đây (mỗi bảng đấu là một lượt riêng) nhưng KHÔNG nằm
        ///     trong nhãn ô sổ mốc — xem doc của class.
        /// </summary>
        public static string StepKey(
            string liveOpsEvent, string cycleId, string roundId, string action, int? stage)
        {
            return PREFIX + "step_" + liveOpsEvent + "_" + (cycleId ?? string.Empty) + "_" +
                   (roundId ?? string.Empty) + "_" + action + "_" + (stage?.ToString() ?? "-");
        }
    }
}
