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
    /// Sổ FTUE của MÁY — mỗi mốc một lần cho mỗi lần cài (ký với loader 10/11). Sổ nằm ở dataPool nên
    /// sống qua các lần mở app; cài lại app là sổ mới — kho đọc "hai dòng cùng mốc thì lấy dòng đầu".
    /// <br/>Pure class trên <see cref="IDataPool"/> — không log, không singleton — để test chạy được
    /// ngoài Unity. <see cref="FtueLogService"/> quyết định gửi gì; sổ chỉ trả lời "đã có chưa".
    /// <br/>Khoá của bước là (<c>tutorialId</c>, <c>stepIndex</c>): <c>tutorialId</c> là phiên bản tutorial,
    /// nên game đổi tutorial ở bản sau thì sổ mới không đụng sổ cũ — không có chuyện đánh số lại như funnel
    /// <c>OnceOrdered</c> (sổ khoá theo <c>priority</c> do game tự đánh).
    /// </summary>
    public class FtueLedger
    {
        private const string PREFIX = "Analytic_Ftue_";
        private const string FIRST_SCREEN = PREFIX + "first_screen";
        private const string CURRENT_TUTORIAL = PREFIX + "tutorial_id";

        private readonly IDataPool _dataPool;

        public FtueLedger(IDataPool dataPool)
        {
            _dataPool = dataPool;
        }

        public bool FirstScreenDone => _dataPool.HasKey(FIRST_SCREEN);

        public void MarkFirstScreen() => _dataPool.Save(FIRST_SCREEN, true);

        /// <summary>
        ///     Tutorial đã khai ở mốc start gần nhất — sống qua lần mở app sau. Null nếu chưa start lần nào:
        ///     bên gọi để <c>tutorialId</c> VẮNG chứ không đoán.
        /// </summary>
        [CanBeNull]
        public string CurrentTutorialId => _dataPool.GetOrDefault<string>(CURRENT_TUTORIAL, null);

        public bool StartDone([CanBeNull] string tutorialId) => _dataPool.HasKey(StartKey(tutorialId));

        public void MarkStart([CanBeNull] string tutorialId)
        {
            _dataPool.Save(StartKey(tutorialId), true);
            if (!string.IsNullOrEmpty(tutorialId)) _dataPool.Save(CURRENT_TUTORIAL, tutorialId);
        }

        public bool StepDone([CanBeNull] string tutorialId, int stepIndex) =>
            _dataPool.HasKey(StepKey(tutorialId, stepIndex));

        public void MarkStep([CanBeNull] string tutorialId, int stepIndex)
        {
            _dataPool.Save(StepKey(tutorialId, stepIndex), true);
            var max = MaxStep(tutorialId);
            if (!max.HasValue || stepIndex > max.Value) _dataPool.Save(MaxStepKey(tutorialId), stepIndex);
        }

        /// <summary>Bước xa nhất đã gửi của tutorial này — null nếu chưa bước nào.</summary>
        public int? MaxStep([CanBeNull] string tutorialId)
        {
            var key = MaxStepKey(tutorialId);
            return _dataPool.HasKey(key) ? _dataPool.GetOrDefault(key, 0) : null;
        }

        /// <summary>Kết cục đã ghi của tutorial này (<c>complete</c> / <c>skip</c>) — null nếu chưa kết thúc.</summary>
        [CanBeNull]
        public string Outcome([CanBeNull] string tutorialId) =>
            _dataPool.GetOrDefault<string>(OutcomeKey(tutorialId), null);

        public void MarkOutcome([CanBeNull] string tutorialId, string outcome) =>
            _dataPool.Save(OutcomeKey(tutorialId), outcome);

        // Tutorial vắng id vẫn có sổ riêng (chuỗi rỗng) — không gộp nhầm vào tutorial nào có id.
        private static string Scope(string tutorialId) => tutorialId ?? string.Empty;
        private static string StartKey(string tutorialId) => PREFIX + "start_" + Scope(tutorialId);
        private static string StepKey(string tutorialId, int stepIndex) => PREFIX + "step_" + Scope(tutorialId) + "_" + stepIndex;
        private static string MaxStepKey(string tutorialId) => PREFIX + "max_step_" + Scope(tutorialId);
        private static string OutcomeKey(string tutorialId) => PREFIX + "outcome_" + Scope(tutorialId);
    }
}
