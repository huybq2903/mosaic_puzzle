/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    public class FunnelLogService : MySingleton<FunnelLogService>
    {
        private const string JOIN_DAY_SUFFIX = "_joinDay";

        /// <summary>Chu kỳ mà hai mốc giờ-chơi dưới đây đang thuộc về — đổi chu kỳ là reset mốc.</summary>
        private const string PLAY_MARK_CYCLE_SUFFIX = "_playMarkCycle";

        /// <summary>Mốc giờ-chơi lúc VÀO PHỄU của chu kỳ hiện tại (giây, <c>TotalPlayTime</c>).</summary>
        private const string PLAY_MARK_ENTER_SUFFIX = "_playMarkEnter";

        /// <summary>Mốc giờ-chơi của bước HỢP LỆ gần nhất trong chu kỳ hiện tại (giây).</summary>
        private const string PLAY_MARK_STEP_SUFFIX = "_playMarkStep";

        /// <summary>Mốc <c>MaxPassedLevel</c> lúc VÀO PHỄU của chu kỳ hiện tại.</summary>
        private const string LEVEL_MARK_ENTER_SUFFIX = "_levelMarkEnter";

        /// <summary>Mốc <c>MaxPassedLevel</c> của bước HỢP LỆ gần nhất trong chu kỳ hiện tại.</summary>
        private const string LEVEL_MARK_STEP_SUFFIX = "_levelMarkStep";

        private readonly IDataPool _dataPool;
        private readonly ITimeRepository _timeRepository;
        private readonly FunnelRegistry _funnelRegistry;
        private readonly LogScheduleService _logScheduleService;
        private readonly IFPlayerSessionService _sessionService;
        private readonly IFPlayerGeneralRepository _generalRepository;

        public FunnelLogService(
            IDataPool dataPool, ITimeRepository timeRepository,
            FunnelRegistry funnelRegistry, LogScheduleService logScheduleService,
            IFPlayerSessionService sessionService, IFPlayerGeneralRepository generalRepository)
        {
            _dataPool = dataPool;
            _timeRepository = timeRepository;
            _funnelRegistry = funnelRegistry;
            _logScheduleService = logScheduleService;
            _sessionService = sessionService;
            _generalRepository = generalRepository;
        }

        /// <summary>
        /// Game báo một bước funnel (§D6 + §D9). Service CHỈ nhận param — nén knob phẳng là việc
        /// của <see cref="FFunnelApi"/>. Dùng hằng số <see cref="FFunnelName"/> /
        /// <see cref="FFunnelAction"/> cho các funnel đã có trong hợp đồng.
        /// </summary>
        public void LogStep(FunnelParam param)
        {
            if (param == null) return;
            // API công khai nhận string tự do — phải soi như đường ctor cũ vẫn soi
            param.CorrectValues();

            // Hình dạng funnel quyết định LOẠI LOG: mỗi loại mang đúng luật của mình, thay vì một
            // class chung rồi rẽ nhánh ngầm bên trong (đọc call site là biết luật nào đang áp)
#pragma warning disable 618 // Repeatable nghỉ hưu nhưng vocab di sản còn chạy — plumbing phải giữ
            _logScheduleService.Enqueue(_funnelRegistry.ShapeOf(param.funnelName) switch
            {
                FunnelShape.Repeatable => new RepeatableFunnelLog(param),
                FunnelShape.OncePerCycle => new OncePerCycleFunnelLog(param),
                _ => (AFunnelLog)new OnceOrderedFunnelLog(param)
            });
#pragma warning restore 618
        }

        // ---- Đọc sổ tiến độ (cùng sổ mà van lọc trùng dùng) ----

        /// <summary>
        /// Mốc CAO NHẤT đã đi của funnel <see cref="FunnelShape.OnceOrdered"/> — null nếu chưa
        /// bước nào. Dò được vì luật shape này ép priority liền mạch từ 0 không hở.
        /// <br/>⚠ Sổ LOCAL đời máy (đúng cái van lọc trùng đọc/ghi) — không phải chân lý
        /// cross-device.
        /// </summary>
        public int? CurrentOrderedPriority(string funnelName)
        {
            var priority = 0;
            while (_dataPool.HasKey(funnelName + priority)) priority++;
            return priority > 0 ? priority - 1 : null;
        }

        /// <summary>Funnel OnceOrdered đã đi qua mốc này chưa (sổ local đời máy).</summary>
        public bool HasPassedOrdered(string funnelName, int priority)
        {
            return _dataPool.HasKey(funnelName + priority);
        }

        /// <summary>
        /// Funnel <see cref="FunnelShape.OncePerCycle"/> đã đi bước (action, priority) trong chu kỳ
        /// này chưa (sổ local đời máy). Key gồm cả ACTION — cùng bài học với van lọc.
        /// </summary>
        public bool HasPassedInCycle(string funnelName, string cycleId, string action, int priority)
        {
            return _dataPool.HasKey(CycleStepKey(funnelName, cycleId, action, priority));
        }

        /// <summary>
        /// Ngày <see cref="FFunnelAction.Join"/> GẦN NHẤT của funnel (giờ local) — cùng mốc mà
        /// <c>funnelDay</c> trên log dùng. Null nếu chưa từng join (game mới chỉ log milestone).
        /// </summary>
        public DateTime? LastJoinDayLocal(string funnelName)
        {
            var key = funnelName + JOIN_DAY_SUFFIX;
            if (!_dataPool.HasKey(key)) return null;
            return _dataPool.GetOrDefault(key, default(DateTime)).ToLocalTime();
        }

        /// <summary>
        ///     Decor the log with necessary params (does not contain params in BaseFalconLog)
        /// </summary>
        /// <param name="funnelLog"></param>
        /// <returns></returns>
        public DecorInfo Check(AFunnelLog funnelLog)
        {
            var info = CheckShapeRules(funnelLog);

            // Bước bị van đánh trượt KHÔNG dịch mốc giờ-chơi: nó không lên wire, dịch mốc là bước
            // sau đo từ một bước chưa từng tồn tại. Đặt ở đây (sau khi biết phán quyết) thay vì
            // trong từng nhánh shape — ba nhánh thì sớm muộn có nhánh quên.
            if (info.valid) StampDistanceMarks(funnelLog);
            return info;
        }

        /// <inheritdoc cref="Check"/>
        private DecorInfo CheckShapeRules(AFunnelLog funnelLog)
        {
            var logParams = funnelLog.param;

            // Loại log nói lên hình dạng (đường OnStep dựng đúng loại); registry chỉ dùng cho log
            // dựng bằng tay (đường cũ, class không nói được gì).
#pragma warning disable 618 // plumbing di sản — xem LogStep
            var shape = funnelLog switch
            {
                RepeatableFunnelLog => FunnelShape.Repeatable,
                OncePerCycleFunnelLog => FunnelShape.OncePerCycle,
                _ => _funnelRegistry.ShapeOf(logParams.funnelName)
            };
#pragma warning restore 618

            // Tự thú shape ĐANG ÁP lên wire — đóng cả cho log sắp bị veto (nó bị vứt, không sao);
            // xem doc AFunnelLog.funnelShape về vai DQ của field này.
            funnelLog.funnelShape = shape.ToWireName();

            // Funnel lặp tự do (daily quest, event mùa): mốc trùng/nhảy cóc/vào lại mùa mới đều
            // HỢP LỆ — luật một-lần-đúng-thứ-tự dưới kia chỉ đúng cho ftue.
#pragma warning disable 618 // nhánh di sản
            if (shape == FunnelShape.Repeatable)
#pragma warning restore 618
            {
                // Class hứa "lọc trùng hộ" mà funnel lại là loại lặp — lời hứa đó thành vô hiệu
                // một cách IM LẶNG, đúng loại hồi quy khó truy nhất
                if (funnelLog is FFilteredFunnelLog)
                    AnalyticLogger.Instance.Warning(
                        $"Funnel '{logParams.funnelName}' khai là Repeatable nhưng đang log bằng " +
                        "FFilteredFunnelLog — class đó lọc trùng hộ bạn, mà ở hình dạng lặp lại thì " +
                        "KHÔNG lọc gì cả. Dùng RepeatableFunnelLog (hoặc Funnel.OnStep) cho khỏi hiểu nhầm.");

                return CheckRepeatable(funnelLog);
            }

            if (shape == FunnelShape.OncePerCycle) return CheckOncePerCycle(funnelLog);

            var valid = true;
            var error = string.Empty;
            if (logParams.priority != 0 && !_dataPool.HasKey(logParams.funnelName + (logParams.priority - 1)))
            {
                error =
                    $"Dwh Log invalid logic : Funnel {logParams.funnelName} not created in order in this device instance";
                valid = false;
            }

            if (!valid)
            {
                return new DecorInfo(false, error);
            }

            _dataPool.Compute<DateTime>(logParams.funnelName + logParams.priority, value =>
            {
                if (!value.HasValue) return _timeRepository.UtcNow();
                error =
                    $"Dwh Log invalid logic : This device already joined the funnel {logParams.funnelName} of the priority {logParams.priority}";
                valid = false;
                return value;
            });
            
            var day = _dataPool.GetOrDefault(logParams.funnelName + 0, _timeRepository.UtcNow()).ToLocalTime();
            funnelLog.funnelDay = MyTime.DateToString(day);
            return new DecorInfo(valid, error);
        }

        /// <summary>
        /// Funnel lặp lại: không có gì để đánh trượt (mọi thứ tự đều hợp lệ), chỉ còn việc đóng
        /// <c>funnelDay</c>.
        /// </summary>
        private DecorInfo CheckRepeatable(AFunnelLog funnelLog)
        {
            StampJoinDay(funnelLog);
            return new DecorInfo(true, string.Empty);
        }

        /// <summary>
        /// Một lần mỗi CHU KỲ: trong một <c>cycleId</c>, mỗi cặp (action, priority) chỉ đi một lần
        /// — sang chu kỳ mới tự sạch. Không có luật thứ tự (mua vượt tier hợp lệ). Key lọc gồm cả
        /// ACTION: <c>milestone(3)</c> rồi <c>claim(3)</c> là hai bước thật cùng priority, lọc
        /// theo priority trần là tái tạo cái bug làm luật ftue gãy với battle pass.
        /// <br/>Thiếu <c>cycleId</c> thì KHÔNG lọc (cảnh báo + xử như Repeatable) — chặn dữ liệu
        /// thật vì thiếu tham số còn tệ hơn để lọt trùng.
        /// </summary>
        private DecorInfo CheckOncePerCycle(AFunnelLog funnelLog)
        {
            var logParams = funnelLog.param;
            if (string.IsNullOrEmpty(logParams.cycleId))
            {
                AnalyticLogger.Instance.Warning(
                    $"Funnel '{logParams.funnelName}' khai OncePerCycle nhưng bước này không mang " +
                    "cycleId — không có ranh giới chu kỳ thì không lọc được, bước đi qua KHÔNG lọc. " +
                    "Truyền cycleId vào OnStep (vd \"season_5\").");
                return CheckRepeatable(funnelLog);
            }

            var valid = true;
            var error = string.Empty;
            _dataPool.Compute<DateTime>(
                CycleStepKey(logParams.funnelName, logParams.cycleId, logParams.action, logParams.priority),
                value =>
                {
                    if (!value.HasValue) return _timeRepository.UtcNow();
                    valid = false;
                    error = "Dwh Log invalid logic : Funnel " + logParams.funnelName +
                            $" already logged '{logParams.action}' at priority {logParams.priority}" +
                            $" in cycle '{logParams.cycleId}'";
                    return value;
                });

            StampJoinDay(funnelLog);
            return new DecorInfo(valid, error);
        }

        /// <summary>
        /// Key state lọc trùng của một bước trong chu kỳ — public để test ghim được bài học
        /// "key phải gồm cả action".
        /// </summary>
        public static string CycleStepKey(string funnelName, string cycleId, string action, int priority)
        {
            return funnelName + "_" + cycleId + "_" + action + "_" + priority;
        }

        /// <summary>
        /// Đóng bốn mốc KHOẢNG CÁCH của bước funnel: hai mốc giờ chơi
        /// (<see cref="AFunnelLog.playSecSinceEnter"/> / <see cref="AFunnelLog.playSecSincePrevStep"/>)
        /// và hai mốc level (<see cref="AFunnelLog.levelsSinceEnter"/> /
        /// <see cref="AFunnelLog.levelsSincePrevStep"/>) — xem doc bốn field đó về lý do chọn đồng
        /// hồ <c>TotalPlayTime</c>, chọn <c>MaxPassedLevel</c>, chọn mốc vào-phễu thay vì bước
        /// <c>join</c>, và luật vắng-mặt-chứ-không-gửi-0.
        /// <br/>Hai thước đo đi cặp vì chúng bù lỗ cho nhau: giờ chơi đếm cả lúc app mở mà không
        /// chơi, level thì không; đổi lại level đứng yên khi user cày lại một màn, giờ chơi thì
        /// không.
        /// <br/>Sổ mốc là 5 key MỘT BẢN GHI mỗi funnel (chu kỳ + 2 mốc giờ + 2 mốc level), không
        /// sinh key mới mỗi chu kỳ: funnel mùa chạy vài năm thì pool phình vô hạn.
        /// </summary>
        private void StampDistanceMarks(AFunnelLog funnelLog)
        {
            // Đường dựng service thiếu dep (test tầng đọc truyền null): không có đồng hồ thì
            // không có số — vắng mặt, chứ không gửi 0.
            if (_sessionService == null) return;

            var funnelName = funnelLog.param.funnelName;
            var cycleId = funnelLog.param.cycleId ?? string.Empty;
            var nowSec = _sessionService.TotalPlayTime.TotalSeconds;
            var nowLevel = _generalRepository?.MaxPassedLevel;

            var cycleKey = funnelName + PLAY_MARK_CYCLE_SUFFIX;
            var enterKey = funnelName + PLAY_MARK_ENTER_SUFFIX;
            var stepKey = funnelName + PLAY_MARK_STEP_SUFFIX;
            var levelEnterKey = funnelName + LEVEL_MARK_ENTER_SUFFIX;
            var levelStepKey = funnelName + LEVEL_MARK_STEP_SUFFIX;

            var sameCycle = _dataPool.HasKey(enterKey) &&
                            _dataPool.GetOrDefault(cycleKey, string.Empty) == cycleId;

            if (sameCycle)
            {
                funnelLog.playSecSinceEnter =
                    Delta(nowSec, _dataPool.GetOrDefault(enterKey, nowSec), funnelName, "giờ chơi", "mốc vào phễu");
                funnelLog.playSecSincePrevStep = _dataPool.HasKey(stepKey)
                    ? Delta(nowSec, _dataPool.GetOrDefault(stepKey, nowSec), funnelName, "giờ chơi", "bước trước")
                    : null;

                if (nowLevel.HasValue)
                {
                    funnelLog.levelsSinceEnter = _dataPool.HasKey(levelEnterKey)
                        ? Delta(nowLevel.Value, _dataPool.GetOrDefault(levelEnterKey, nowLevel.Value),
                            funnelName, "số level", "mốc vào phễu")
                        : null;
                    funnelLog.levelsSincePrevStep = _dataPool.HasKey(levelStepKey)
                        ? Delta(nowLevel.Value, _dataPool.GetOrDefault(levelStepKey, nowLevel.Value),
                            funnelName, "số level", "bước trước")
                        : null;
                }
            }
            else
            {
                // Bước ĐẦU của chu kỳ: chính nó là mốc vào phễu, và chưa có bước trước
                _dataPool.Save(cycleKey, cycleId);
                _dataPool.Save(enterKey, nowSec);
                _dataPool.Delete(stepKey);
                _dataPool.Delete(levelStepKey);
                funnelLog.playSecSinceEnter = 0;
                funnelLog.playSecSincePrevStep = null;

                if (nowLevel.HasValue)
                {
                    _dataPool.Save(levelEnterKey, nowLevel.Value);
                    funnelLog.levelsSinceEnter = 0;
                }
                else
                {
                    // Không đọc được tiến độ level thì KHÔNG đóng mốc rỗng: mốc sai còn tệ hơn
                    // không có mốc, vì bước sau sẽ đo từ nó.
                    _dataPool.Delete(levelEnterKey);
                }

                funnelLog.levelsSincePrevStep = null;
            }

            _dataPool.Save(stepKey, nowSec);
            if (nowLevel.HasValue) _dataPool.Save(levelStepKey, nowLevel.Value);
        }

        /// <summary>
        /// Khoảng cách giữa hai mốc, làm tròn về số nguyên. Số ÂM = sổ mốc (pool) lệch sổ nguồn
        /// (<c>TotalPlayTime</c> / <c>MaxPassedLevel</c>) — hai sổ khác vòng đời nên có ca lệch
        /// (bài học SessionUid vs SessionId; hoặc user xoá data): trả VẮNG MẶT + cảnh báo, không
        /// trả 0. Gửi 0 là bịa "hai bước sát nhau", mà đó lại là con số người đọc tin nhất.
        /// </summary>
        private static long? Delta(double now, double mark, string funnelName, string measure, string what)
        {
            var delta = now - mark;
            if (delta >= 0) return (long)Math.Round(delta);

            AnalyticLogger.Instance.Warning(
                $"Funnel '{funnelName}': {measure} tới {what} ra số âm ({delta:F1}) — sổ mốc lệch " +
                "sổ nguồn, bỏ trường thay vì gửi 0.");
            return null;
        }

        /// <summary>
        /// Đóng <c>funnelDay</c> — server derive "vào được mấy ngày" từ nó. Mốc tính là lần
        /// <see cref="FFunnelAction.Join"/> GẦN NHẤT, không phải lần đầu đời máy: battle pass mùa 5
        /// mà mang ngày join mùa 1 thì con số "được mấy ngày" thành vô nghĩa. Chưa từng join
        /// (game chỉ log milestone) thì lấy hôm nay — không bịa cohort.
        /// </summary>
        private void StampJoinDay(AFunnelLog funnelLog)
        {
            var key = funnelLog.param.funnelName + JOIN_DAY_SUFFIX;
            var now = _timeRepository.UtcNow();
            if (funnelLog.param.action == FFunnelAction.Join)
                _dataPool.Compute<DateTime>(key, _ => now);

            funnelLog.funnelDay = MyTime.DateToString(_dataPool.GetOrDefault(key, now).ToLocalTime());
        }
    }
}