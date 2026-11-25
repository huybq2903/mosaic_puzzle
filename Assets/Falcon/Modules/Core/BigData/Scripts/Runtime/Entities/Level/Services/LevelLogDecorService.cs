/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Trang trí level log trong pipeline gửi: điền tham số lượt chơi từ cache
    /// (<see cref="LevelTurnService"/>) rồi tính các counter phái sinh (maxPassedLevel,
    /// failCount, playCount).
    /// <br/>⚠ KHÔNG còn đếm win/lose streak (bỏ 14/10, chốt owner): chuỗi Pass/Fail suy lại được
    /// từ chính stream level, mà mọi định nghĩa "phủ cả ván bỏ ngang" đều là chính sách per-game —
    /// nhét vào cột chung là mất so-chéo-game. Game cần chuỗi theo luật của mình thì tự giữ và
    /// gửi bằng key riêng có tiền tố.
    /// </summary>
    public class LevelLogDecorService : MySingleton<LevelLogDecorService>, ILogDecorator<FLevelLog>
    {
        private readonly LevelTurnService _turnService;
        private readonly EntityLabelService _entityLabelService;
        private readonly IFPlayerGeneralRepository _generalRepository;
        private readonly IDataPool _dataPool;
        private readonly ITimeRepository _timeRepository;
        private readonly TurnLedgerService _turnLedger;

        public LevelLogDecorService(
            LevelTurnService turnService, EntityLabelService entityLabelService,
            IFPlayerGeneralRepository generalRepository,
            IDataPool dataPool, ITimeRepository timeRepository, TurnLedgerService turnLedger)
        {
            _turnLedger = turnLedger;
            _turnService = turnService;
            _entityLabelService = entityLabelService;
            _generalRepository = generalRepository;
            _dataPool = dataPool;
            _timeRepository = timeRepository;
        }

        /// <summary>
        /// Điền tham số lượt chơi + tính counter cho một level log.
        /// Chạy trong pipeline decor (LogDecorService), decorate-once cho mỗi log instance.
        /// <br/>⚠ TUYỆT ĐỐI không tạo/gửi log bên trong hàm này (và trong LevelTurnState) —
        /// log mới sẽ lại đi qua đây và thành đệ quy vô hạn.
        /// </summary>
        public void Decor(FLevelLog log)
        {
            var logParams = log.param;

            // Điền tham số từ cache PHẢI chạy trước phần dưới vì LevelId phụ thuộc currentLevel/difficulty.
            _turnService.Apply(logParams);

            // Bản chụp khẩn (S2): chỉ đọc — không mở / đóng sổ, không ghi pool, không báo LiveOps.
            if (log.preview)
            {
                DecorPreview(log, logParams);
                return;
            }

            // Sổ thời gian lượt (hợp đồng đợt 3 B6): Start mở đoạn 1; Pass / Fail / Skip / HeartBeat mang số tích luỹ của
            // đoạn; dòng kết thúc đóng đoạn. Sổ chỉ mở cho đúng lượt đã Start (lượt "cụt đầu" không có sổ).
            if (logParams.Status == LevelStatus.Start)
                _turnLedger.Begin(logParams.playTurnId, 1);
            else
            {
                log.turnLedger ??= _turnLedger.WireFor(logParams.playTurnId);
                if (logParams.Status != LevelStatus.HeartBeat) _turnLedger.End();
            }

            // Nhãn của MÀN đi ké level event (§D11) — dùng currentLevel CỦA CHÍNH LOG NÀY, không
            // phải màn đang mở: heartbeat trễ sau khi sang màn mới phải mang nhãn của màn nó tả.
            // Chạy sau Apply nên currentLevel đã được điền từ cache.
            log.levelLabels ??= _entityLabelService.BundleFor(
                LabelEntityKind.Level, logParams.currentLevel.ToString());

            var passedBefore = false;
            _dataPool.Compute<long>("HasPassedLevel_" + logParams.LevelId,
                value =>
                {
                    passedBefore = value.HasValue;
                    if (value.HasValue) return value;

                    if (!logParams.Status.IsPass()) return null;
                    // Math.Max chứ không gán thô (loader audit 12/08): pass lần đầu một màn SỐ NHỎ
                    // (side/bonus level) khi max đã 250 mà gán thô thì central param tụt về giá
                    // trị nhỏ trên MỌI log sau.
                    _generalRepository.MaxPassedLevel =
                        Math.Max(_generalRepository.MaxPassedLevel, logParams.currentLevel);
                    return _timeRepository.CurrentTimeSec();
                });

            // Hợp đồng §E: Re* statuses (ReplayPass/ReplayFail) đã khai tử, replay CŨNG gửi Start —
            // nên cờ này là thứ DUY NHẤT phân biệt "chơi lại màn đã qua" với "chơi lần đầu".
            // SDK tự điền vì mốc pass đầu vốn đã nằm trong pool (luật §H3: máy suy được thì máy điền).
            if (logParams.autoCheckComeBackAfterFirstPass && passedBefore)
                logParams.comeBackAfterFirstPass = true;

            // ReSharper disable once PossibleInvalidOperationException
            log.failCount = _dataPool.Compute<int>("Fail_Count_Level_" + logParams.LevelId,
                val =>
                {
                    val ??= 0;
                    return logParams.Status.IsFail() ? val + 1 : val;
                }).Value;
            // ReSharper disable once PossibleInvalidOperationException
            log.playCount = _dataPool.Compute<int>( "Play_Count_Level_" + logParams.LevelId,
                val =>
                {
                    val ??= 0;
                    return logParams.Status == LevelStatus.Start ? val + 1 : val;
                }).Value;

            // Báo sang live-ops để nó cộng bộ đếm lượt chơi (toàn cục + riêng event nếu dòng này
            // KHAI event). Chỉ cộng số, không sinh log — sinh log ở đây là đệ quy vô hạn.
            // Lấy qua Instance thay vì tiêm vào ctor: LiveOpsLogService phụ thuộc
            // LogScheduleService, mà service đó lại gom các decorator — tiêm là vòng tròn DI.
            LiveOpsLogService.Instance.ReportLevel(logParams.playTurnId, logParams.Status, logParams);
        }

        /// <summary>
        /// Như <see cref="Decor"/> cho heartbeat của bản chụp khẩn, không side effect. Sổ B6 bản chụp tự đính (cần cả khối
        /// đang chạy — <see cref="TurnLedgerService.SnapshotFor"/>), ở đây không đụng.
        /// </summary>
        private void DecorPreview(FLevelLog log, ALevelParamV2 logParams)
        {
            log.levelLabels ??= _entityLabelService.BundleFor(LabelEntityKind.Level, logParams.currentLevel.ToString());
            if (logParams.autoCheckComeBackAfterFirstPass &&
                _dataPool.GetOrDefault<long?>("HasPassedLevel_" + logParams.LevelId, null).HasValue)
                logParams.comeBackAfterFirstPass = true;
            log.failCount = _dataPool.GetOrDefault<int?>("Fail_Count_Level_" + logParams.LevelId, null) ?? 0;
            log.playCount = _dataPool.GetOrDefault<int?>("Play_Count_Level_" + logParams.LevelId, null) ?? 0;
        }
    }
}
