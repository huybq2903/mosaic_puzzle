/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Gửi họ <c>f_sdk_ftue_*</c> — mỗi mốc một lần cho mỗi lần cài, theo <see cref="FtueLedger"/>.
    /// <br/>Thứ tự trong mỗi hàm: <b>gửi trước, ghi sổ sau</b> (cùng khuôn <see cref="PermissionLogService"/>):
    /// ngược lại thì máy chết giữa chừng là mốc coi như đã gửi và VĨNH VIỄN không gửi lại; thứ tự này xấu
    /// nhất là thừa một dòng trùng — kho lấy dòng đầu.
    /// <br/>SDK KHÔNG ép thứ tự giữa các mốc: tutorial thật có bước tuỳ điều kiện, và chặn là đúng cái lỗi
    /// làm funnel <c>OnceOrdered</c> phải bỏ.
    /// </summary>
    public class FtueLogService : MySingleton<FtueLogService>
    {
        internal const string OUTCOME_COMPLETE = "complete";
        internal const string OUTCOME_SKIP = "skip";

        private readonly LogScheduleService _logScheduleService;
        private readonly FtueLedger _ledger;
        private readonly object _lock = new();

        public FtueLogService(LogScheduleService logScheduleService, IDataPool dataPool)
        {
            _logScheduleService = logScheduleService;
            _ledger = new FtueLedger(dataPool);
        }

        public void FirstScreen()
        {
            lock (_lock)
            {
                if (_ledger.FirstScreenDone) return;
                _logScheduleService.Enqueue(new FFtueFirstScreenLog());
                _ledger.MarkFirstScreen();
            }
        }

        public void TutorialStart([CanBeNull] string tutorialId)
        {
            if (string.IsNullOrWhiteSpace(tutorialId))
            {
                AnalyticLogger.Instance.Warning(
                    "Ftue.OnTutorialStart: thiếu tutorialId — các bước sau sẽ VẮNG id, và kho không cắt được " +
                    "theo phiên bản tutorial. Đặt một id ổn định (vd \"tut_v3\"), đổi id khi đổi tutorial.");
                tutorialId = null;
            }

            lock (_lock)
            {
                if (_ledger.StartDone(tutorialId)) return;
                _logScheduleService.Enqueue(new FFtueTutorialStartLog { tutorialId = tutorialId });
                _ledger.MarkStart(tutorialId);
            }
        }

        public void TutorialStep(int stepIndex, [CanBeNull] string stepName)
        {
            if (stepIndex < 1)
            {
                AnalyticLogger.Instance.Warning(
                    $"Ftue.OnTutorialStep: stepIndex = {stepIndex} — phải ≥ 1. BỎ dòng: bước không có số thì " +
                    "không đọc được 'rơi ở bước nào', và cũng không chống trùng được.");
                return;
            }

            if (string.IsNullOrWhiteSpace(stepName)) stepName = null;

            lock (_lock)
            {
                var tutorialId = CurrentTutorialIdOrWarn("OnTutorialStep");
                if (_ledger.StepDone(tutorialId, stepIndex)) return;
                _logScheduleService.Enqueue(new FFtueTutorialStepLog
                {
                    tutorialId = tutorialId, stepIndex = stepIndex, stepName = stepName
                });
                _ledger.MarkStep(tutorialId, stepIndex);
            }
        }

        public void TutorialComplete()
        {
            lock (_lock)
            {
                var tutorialId = CurrentTutorialIdOrWarn("OnTutorialComplete");
                if (AlreadyEnded(tutorialId, OUTCOME_COMPLETE)) return;
                _logScheduleService.Enqueue(new FFtueTutorialCompleteLog { tutorialId = tutorialId });
                _ledger.MarkOutcome(tutorialId, OUTCOME_COMPLETE);
            }
        }

        public void TutorialSkip()
        {
            lock (_lock)
            {
                var tutorialId = CurrentTutorialIdOrWarn("OnTutorialSkip");
                if (AlreadyEnded(tutorialId, OUTCOME_SKIP)) return;
                _logScheduleService.Enqueue(new FFtueTutorialSkipLog
                {
                    tutorialId = tutorialId, lastStepIndex = _ledger.MaxStep(tutorialId)
                });
                _ledger.MarkOutcome(tutorialId, OUTCOME_SKIP);
            }
        }

        [CanBeNull]
        private string CurrentTutorialIdOrWarn(string caller)
        {
            var tutorialId = _ledger.CurrentTutorialId;
            if (tutorialId == null)
                AnalyticLogger.Instance.Warning(
                    $"Ftue.{caller}: chưa có Ftue.OnTutorialStart(tutorialId) nào — dòng này VẮNG tutorialId. " +
                    "SDK không đoán.");
            return tutorialId;
        }

        /// <summary>
        ///     <c>complete</c> và <c>skip</c> loại trừ nhau: kết cục đầu tiên thắng. Gọi lại cùng kết cục là
        ///     chuyện bình thường (tutorial chạy lại sau khi mở app) — im lặng; gọi kết cục KHÁC thì cảnh báo.
        /// </summary>
        private bool AlreadyEnded([CanBeNull] string tutorialId, string outcome)
        {
            var existing = _ledger.Outcome(tutorialId);
            if (existing == null) return false;

            if (existing != outcome)
                AnalyticLogger.Instance.Warning(
                    $"Ftue: tutorial '{tutorialId}' đã ghi kết cục '{existing}' — bỏ '{outcome}'. Xong và bỏ qua " +
                    "loại trừ nhau; kết cục đầu tiên thắng.");
            return true;
        }
    }
}
