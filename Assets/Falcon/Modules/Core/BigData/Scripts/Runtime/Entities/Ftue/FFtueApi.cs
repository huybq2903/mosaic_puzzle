/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-10
 */

using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// FTUE — <c>FalconBigDataController.Ftue</c>: hành trình của người chơi mới tới hết tutorial
    /// onboarding. Họ event <c>f_sdk_ftue_*</c>, ký với loader 10/11, thay funnel <c>ftue</c>.
    /// <br/>Mỗi mốc một hàm — không có số thứ tự, không phải khai shape. Mỗi mốc chỉ gửi <b>một lần cho
    /// mỗi lần cài</b>, nên cứ gọi ở đúng chỗ mỗi lần chạy qua, SDK lo chống trùng.
    /// <br/>Chỉ dành cho tutorial ONBOARDING. Tutorial của từng tính năng (battle pass, event…) không phải
    /// FTUE — đọc qua cụm Ui / live-op của tính năng đó.
    /// <br/>Phễu kho đọc: mở app lần đầu → <see cref="OnFirstScreen"/> → <see cref="OnTutorialStart"/> →
    /// <see cref="OnTutorialComplete"/> | <see cref="OnTutorialSkip"/> → Start / Pass màn 1 (dòng level).
    /// </summary>
    public class FFtueApi
    {
        private readonly FtueLogService _service;

        private readonly LoadStepLogService _loadStepService;

        internal FFtueApi(FtueLogService service, LoadStepLogService loadStepService)
        {
            _service = service;
            _loadStepService = loadStepService;
        }

        /// <summary>
        /// Một trạm tải của GAME ở lần mở đầu sau cài / lần vào level đầu tiên — <c>f_sdk_ftue_load_step</c> (hợp đồng đợt 3
        /// B2). Mỗi trạm một lần mỗi lần cài; ngoài hai giai đoạn đó SDK tự bỏ qua, cứ gọi mọi lần cũng được.
        /// <c>load_start</c> / <c>first_frame</c> / <c>config_ready</c> SDK tự báo.
        /// <code>
        /// FalconBigDataController.Ftue.OnLoadStep(LoadKind.Level, LoadStep.LevelLoadStart);
        /// FalconBigDataController.Ftue.OnLoadStep(LoadKind.Level, LoadStep.LevelReady); // lúc người chơi BẤM ĐƯỢC
        /// FalconBigDataController.Ftue.OnLoadStep(LoadKind.App, LoadStep.AssetDownloadDone, assetBytes: 12_345_678);
        /// </code>
        /// </summary>
        /// <param name="assetBytes">Chỉ <see cref="LoadStep.AssetDownloadDone"/>: số byte đã tải.</param>
        /// <param name="errorCode">Chỉ <see cref="LoadStep.LoadError"/>: <c>timeout</c> / <c>network</c> / <c>other</c>…</param>
        public void OnLoadStep(LoadKind kind, LoadStep step, long? assetBytes = null, string errorCode = null) =>
            _loadStepService.Report(kind, step, assetBytes, errorCode);

        /// <summary>
        /// Màn hình ĐẦU TIÊN người chơi tương tác được — home nếu game có home, màn chơi nếu vào thẳng.
        /// Không phải màn loading.
        /// </summary>
        public void OnFirstScreen() => _service.FirstScreen();

        /// <summary>Tutorial onboarding bắt đầu.</summary>
        /// <param name="tutorialId">
        /// Phiên bản tutorial (vd <c>"tut_v3"</c>). Đổi tutorial ở bản game sau thì đặt id MỚI — số liệu hai
        /// phiên bản không lẫn nhau. SDK nhớ id này cho các bước sau, kể cả qua lần mở app sau.
        /// </param>
        public void OnTutorialStart(string tutorialId) => _service.TutorialStart(tutorialId);

        /// <summary>
        /// Một bước tutorial vừa HIỆN RA (không phải lúc làm xong) — nên "rơi ở bước nào" = bước cuối cùng
        /// đã hiện.
        /// </summary>
        /// <param name="stepIndex">Thứ tự bước trong tutorial, ≥ 1. Không cần liền mạch; bỏ bước không bị chặn.</param>
        /// <param name="stepName">Tên bước, snake_case, ổn định ("first_swipe", "first_match").</param>
        public void OnTutorialStep(int stepIndex, [CanBeNull] string stepName = null) =>
            _service.TutorialStep(stepIndex, stepName);

        /// <summary>Làm hết tutorial. Loại trừ với <see cref="OnTutorialSkip"/> — cái gọi trước thắng.</summary>
        public void OnTutorialComplete() => _service.TutorialComplete();

        /// <summary>
        /// Người chơi bấm bỏ qua tutorial. SDK tự điền bước xa nhất đã hiện. Loại trừ với
        /// <see cref="OnTutorialComplete"/>.
        /// </summary>
        public void OnTutorialSkip() => _service.TutorialSkip();
    }
}
