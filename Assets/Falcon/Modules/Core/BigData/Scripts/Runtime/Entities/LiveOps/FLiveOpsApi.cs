/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System.Collections.Generic;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     EVENT LIVE-OP — <c>FalconBigDataController.LiveOps</c>. Một hợp đồng cho MỌI event
    ///     (hợp đồng đầy đủ: <c>docs/design/LiveOpsEventContract.md</c>).
    ///     <br/>Ba việc của dev game, không hơn:
    ///     <br/>1. <see cref="DeclareCycle"/> khi data đợt về — một lần mỗi đợt.
    ///     <br/>2. <see cref="OnStep"/> ở từng mốc: <c>eligible</c> (MỖI PHIÊN), <c>open</c>,
    ///     <c>join</c>, <c>milestone</c>, <c>complete</c>, <c>claim</c>.
    ///     <br/>3. <see cref="Stamp"/> lên param của dòng tài nguyên / ad / iap / offer / ui /
    ///     custom / level thuộc event — để mọi stream nối được về đúng (event, đợt, mốc).
    ///     <br/>Không phải đăng ký tên event với ai: danh tính nằm ở giá trị, không nằm ở tên log.
    /// </summary>
    public class FLiveOpsApi
    {
        private readonly LiveOpsLogService _service;

        internal FLiveOpsApi(LiveOpsLogService service)
        {
            _service = service;
        }

        /// <inheritdoc cref="LiveOpsLogService.DeclareCycle"/>
        public void DeclareCycle(
            string liveOpsEvent, string cycleId,
            int? cycleIndex = null, long? cycleStartTs = null, long? cycleEndTs = null)
        {
            _service.DeclareCycle(liveOpsEvent, cycleId, cycleIndex, cycleStartTs, cycleEndTs);
        }

        /// <summary>
        ///     Ghi một mốc. <paramref name="stage"/> chỉ dành cho <c>milestone</c>/<c>claim</c> —
        ///     bước vòng đời (<c>eligible</c>/<c>open</c>/<c>join</c>/<c>complete</c>) KHÔNG truyền,
        ///     vì <paramref name="action"/> đã đủ định danh.
        ///     <br/>Trả false khi van lọc chặn (bước đã log trong đợt/lượt này) — bình thường, game
        ///     không cần xử lý gì.
        ///     <br/>Mã đợt để trống thì SDK lấy bản đã <see cref="DeclareCycle"/>; truyền tay thì
        ///     tay thắng (nhận thưởng mùa 5 trong mùa 6 thì khai mùa 5).
        /// </summary>
        public bool OnStep(
            string liveOpsEvent, string action, int? stage = null,
            [CanBeNull] string cycleId = null, [CanBeNull] string roundId = null,
            int? currentLevel = null, [CanBeNull] Dictionary<string, object> extraMeta = null)
        {
            return _service.LogStep(new LiveOpsParam
            {
                liveOpsEvent = liveOpsEvent,
                liveOpsCycleId = cycleId,
                liveOpsRoundId = roundId,
                liveOpsStage = stage,
                action = action,
                currentLevel = currentLevel,
                extraMeta = extraMeta
            });
        }

        /// <inheritdoc cref="OnStep(string,string,int?,string,string,int?,System.Collections.Generic.Dictionary{string,object})"/>
        public bool OnStep(LiveOpsParam param)
        {
            return _service.LogStep(param);
        }

        /// <summary>
        ///     Đóng ngữ cảnh event lên param của một stream khác (resource / ad / iap / offer / ui /
        ///     custom / level): điền <c>liveOpsEvent</c>, mã đợt đang chạy, và
        ///     <paramref name="stage"/>/<paramref name="roundId"/> nếu dòng đó thuộc một mốc/lượt.
        ///     <br/>Chỉ điền chỗ đang TRỐNG — game truyền tay thì tay thắng.
        ///     <br/>⚠ Gọi hàm này là một LỜI KHAI: "dòng này thuộc event đó". Ván chơi của event
        ///     chạy CHỒNG lên level thường (hai event cùng lúc) thì ĐỪNG khai — xem hợp đồng §3.
        ///     <br/><br/>🔴 <b>MÃ LƯỢT do game KHAI, SDK không tra hộ</b> (sửa 05/11). Trước đó hàm
        ///     này điền <c>liveOpsRoundId</c> bằng <see cref="CurrentRoundId"/>, và đó là một cái
        ///     ĐOÁN: giá trị ấy là "lượt cuối SDK tình cờ thấy trên một bước nào đó", không phải lời
        ///     khai về dòng đang gửi. Hai hậu quả đã xảy ra thật:
        ///     <br/>· dòng của đợt CŨ (game khai mã đợt tay cho dòng nhận muộn) nhận mã lượt của đợt
        ///       ĐANG CHẠY — một dòng mang hai đợt, kho không gỡ được;
        ///     <br/>· dòng <b>cố ý không có lượt</b> bị điền bừa, vì <c>??=</c> coi <c>null</c> là "ô
        ///       trống" nên game KHÔNG CÓ cách nào khai "dòng này không thuộc lượt nào".
        ///     <br/>Nay im lặng = không có lượt, đúng ý game. Mã đợt thì vẫn tra, vì
        ///     <c>DeclareCycle</c> là <b>game đã nói</b> và mỗi event chỉ có một đợt đang chạy — SDK
        ///     nhớ hộ, không đoán. Cùng lý lẽ đã áp cho <c>Feature.Stamp</c> (bắt truyền
        ///     <c>instanceId</c> tại chỗ gọi, §7 — không có scope ngầm).
        ///     <br/>⚠ <b>Đừng lấy <see cref="CurrentRoundId"/> làm đường tắt.</b> Nó là LỊCH SỬ
        ///     LOG ("lượt của bước cuối đã log trong đợt đang chạy"), nên đọc nó rồi truyền vào đây
        ///     <b>không</b> biến cái đoán thành lời khai — vẫn sai ở dòng nhận muộn và sau khi đổi
        ///     thực thể. Chỉ dùng khi game biết chắc lượt đang mở trùng lượt cuối đã log; còn lại
        ///     thì game tự giữ lượt của mình và truyền vào.
        /// </summary>
        public void Stamp(ALiveOpsParam param, string liveOpsEvent, int? stage = null,
            [CanBeNull] string roundId = null)
        {
            if (param == null || string.IsNullOrEmpty(liveOpsEvent)) return;

            param.StampLiveOps(liveOpsEvent, _service.CurrentCycleId(liveOpsEvent), stage, roundId);
        }

        /// <inheritdoc cref="LiveOpsLogService.CurrentCycleId"/>
        [CanBeNull]
        public string CurrentCycleId(string liveOpsEvent)
        {
            return _service.CurrentCycleId(liveOpsEvent);
        }

        /// <inheritdoc cref="LiveOpsLogService.CurrentRoundId"/>
        [CanBeNull]
        public string CurrentRoundId(string liveOpsEvent)
        {
            return _service.CurrentRoundId(liveOpsEvent);
        }

        /// <inheritdoc cref="LiveOpsLogService.CurrentStage"/>
        public int? CurrentStage(string liveOpsEvent)
        {
            return _service.CurrentStage(liveOpsEvent);
        }

        /// <inheritdoc cref="LiveOpsLogService.HasPassed"/>
        public bool HasPassed(string liveOpsEvent, string cycleId, string action, int? stage = null,
            [CanBeNull] string roundId = null)
        {
            return _service.HasPassed(liveOpsEvent, cycleId, action, stage, roundId);
        }
    }
}
