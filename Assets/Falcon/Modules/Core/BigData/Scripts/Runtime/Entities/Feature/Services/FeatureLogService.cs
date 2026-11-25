/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Đường duy nhất sinh <see cref="FFeatureLog"/>. Mỏng hơn
    ///     <see cref="LiveOpsLogService"/> đúng ba việc, và mỏng là CỐ Ý:
    ///     <br/>- <b>Không van lọc trùng</b>: tính năng luôn bật nên không có đợt để làm khoá van.
    ///     Bước dày đặc (chat, interact) thì game tự gộp trước khi gọi — SDK nén hộ là tự quyết
    ///     thay game xem bước nào đáng giữ.
    ///     <br/>- <b>Không sổ ngữ cảnh</b>: không giữ "đang ở thực thể nào" để tự dán lên dòng khác
    ///     (§7 — ĐỒNG THỜI ≠ NGUYÊN NHÂN).
    ///     <br/>- <b>Không <c>SendNow</c></b>, kể cả bước đổi thực thể: <c>SendNow</c> chạy
    ///     <c>Task.Run</c> cho TỪNG bản tin nên hai lời gọi liền nhau là hai HTTP request độc lập,
    ///     tới cổng theo thứ tự tuỳ mạng. Hàng đợi thường có <c>_flushLock</c> + <c>PeekAll →
    ///     SendBatch → Drain</c> nên GIỮ ĐÚNG THỨ TỰ — mà kho xếp bản tin để dựng profile, nên thứ
    ///     tự quan trọng hơn độ trễ.
    /// </summary>
    public class FeatureLogService : MySingleton<FeatureLogService>
    {
        private readonly LogScheduleService _logScheduleService;

        public FeatureLogService(LogScheduleService logScheduleService)
        {
            _logScheduleService = logScheduleService;
        }

        /// <summary>
        ///     Gửi một bước. Trả false khi bản tin bị bỏ (thiếu <c>feature</c>) — ca lỗi lập trình,
        ///     không phải van lọc, nên luôn kèm cảnh báo.
        /// </summary>
        public bool LogStep(FeatureParam param)
        {
            var log = BuildStep(param);
            if (log == null) return false;

            _logScheduleService.Enqueue(log);
            return true;
        }

        /// <summary>
        ///     Phần ruột của <see cref="LogStep"/>: soát + tự điền, KHÔNG gửi. Trả null nếu bản tin
        ///     bị bỏ. Tách ra để test ghim được từng luật mà không phải dựng pipeline gửi (cùng
        ///     khuôn <see cref="LiveOpsLogService.BuildStep"/>).
        /// </summary>
        [CanBeNull]
        public FFeatureLog BuildStep(FeatureParam param)
        {
            if (param == null) return null;
            param.CorrectValues();

            if (string.IsNullOrEmpty(param.feature))
            {
                AnalyticLogger.Instance.Warning(
                    $"Feature.Step bỏ qua bước '{param.featureStep}': thiếu feature — không có tên " +
                    "tính năng thì bản tin này không thuộc về đâu cả, và kho không nối được nó với " +
                    "trạng thái của người chơi.");
                return null;
            }

            // ??= nên tay thắng: game truyền effect cho bước riêng của tính năng thì SDK không đụng.
            param.featureEffect ??= FFeatureEffect.DefaultFor(param.featureStep);

            return new FFeatureLog(param);
        }
    }
}
