/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using UnityEngine.Scripting;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Một mốc của event live-op — xương sống tiến độ, thay funnel <c>event_*</c>
    ///     (<c>docs/design/LiveOpsEventContract.md</c>).
    ///     <br/>Mọi field ngoài <see cref="param"/> do SDK TỰ ĐÓNG: game không truyền được, nên
    ///     chúng là số máy suy ra chứ không phải lời khai (luật §H3).
    ///     <br/>Dựng log này bằng <c>FalconBigDataController.LiveOps.OnStep</c> —
    ///     <see cref="LiveOpsLogService"/> là đường duy nhất, vì nó mới biết van lọc trùng và các
    ///     mốc khoảng cách.
    /// </summary>
    [Serializable]
    public class FLiveOpsLog : AFalconLog, IPerfPackCarrier
    {
        public LiveOpsParam param;

        /// <summary>
        ///     Mốc USER NÀY vào đợt (epoch millis UTC) = thời điểm bước hợp lệ ĐẦU TIÊN của
        ///     ĐỢT. Khác <see cref="LiveOpsParam.cycleStartTs"/> (lịch chung của đợt).
        ///     <br/>Lượt (round) KHÔNG dịch mốc này: một đợt nhiều bảng đấu vẫn một mốc vào đợt —
        ///     muốn "từ lúc vào bảng này" thì đọc <c>*_since_prev_step</c> (sửa 19/10).
        ///     <br/>VẮNG MẶT khi dòng thuộc một đợt mà SDK không còn sổ mốc (chỉ giữ đợt đang chạy
        ///     + đợt vừa đóng) — vắng là "không biết", đừng COALESCE.
        ///     <br/>Có nó thì <c>cycleEndTs − liveOpsEnterTs</c> ra ngay nhóm "lúc vào còn mấy
        ///     ngày" trên MỌI dòng, khỏi join ngược về dòng <c>join</c> của chính user. Đây cũng là
        ///     thứ gánh vai cũ của <c>funnelDay</c>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? liveOpsEnterTs;

        /// <summary>
        ///     Giờ CHƠI (giây) từ mốc vào đợt tới bước này. Đồng hồ là <c>TotalPlayTime</c>:
        ///     monotonic, CHỈ chạy khi foreground, có persist — không phải wall-clock (chỉnh giờ
        ///     máy là wall-clock phồng vĩnh viễn, án loader 12/08).
        ///     <br/>Vắng mặt = KHÔNG BIẾT (sổ lệch, delta âm), không phải 0.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? playSecSinceEnter;

        /// <inheritdoc cref="playSecSinceEnter"/>
        [FKey(RemoveIfNull = true)] public long? playSecSincePrevStep;

        /// <summary>
        ///     Số level MỚI VƯỢT QUA (toàn cục, nguồn <c>MaxPassedLevel</c>) từ mốc vào đợt tới
        ///     bước này. Bù lỗ cho <see cref="playSecSinceEnter"/>: giờ chơi đếm cả lúc app mở mà
        ///     không chơi, số này thì không.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? levelPassesSinceEnter;

        /// <inheritdoc cref="levelPassesSinceEnter"/>
        [FKey(RemoveIfNull = true)] public long? levelPassesSincePrevStep;

        /// <summary>
        ///     Số LƯỢT CHƠI MỞ RA (toàn cục) từ mốc vào đợt tới bước này — đây là "công sức":
        ///     thua 16 lần rồi qua mốc thì số này là 17 còn số level vượt qua chỉ là 1.
        ///     <br/>Đếm lượt theo state machine, KHÔNG đếm lời gọi <c>OnStart</c>, nên double-fire
        ///     không làm phồng.
        ///     <br/>⚠ Toàn cục: ván chapter chen giữa hai mốc cũng tính. Với event chơi trên level
        ///     thường thì gần như trùng; với event có bộ level riêng (Master League) thì đây là
        ///     CHẶN TRÊN — dùng cặp <see cref="liveOpsLevelAttemptsSinceEnter"/> khi có.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? levelAttemptsSinceEnter;

        /// <inheritdoc cref="levelAttemptsSinceEnter"/>
        [FKey(RemoveIfNull = true)] public long? levelAttemptsSincePrevStep;

        /// <summary>
        ///     Như hai cặp trên nhưng CHỈ tính ván có khai <c>liveOpsEvent</c> của event này —
        ///     chính xác thay vì chặn trên. Vắng khi event không khai event lên dòng level (event
        ///     chạy chồng lên level thường thì KHÔNG được khai, xem hợp đồng §3).
        ///     <br/>Bất biến để DQ tự kiểm — <b>CHỈ cho cặp attempts</b>:
        ///     <c>liveOpsLevelAttempts* ≤ levelAttempts*</c>, vì hai bên cùng đếm LƯỢT CHƠI và mỗi
        ///     ván của event cũng cộng vào sổ toàn cục trong cùng một lời gọi <c>ReportLevel</c>.
        ///     <br/>⚠ <b>Cặp passes KHÔNG có bất biến đó</b> (sửa nhận định 28/10, án production):
        ///     <see cref="levelPassesSinceEnter"/> đo <c>MaxPassedLevel</c> — TIẾN ĐỘ MAP CHÍNH,
        ///     không phải số ván thắng. Event có BỘ LEVEL RIÊNG (Master League) thắng 45 ván của
        ///     mình mà map chính không nhích ⇒ <c>live_ops_level_passes</c> lớn hơn hẳn
        ///     <c>level_passes</c> là <b>ĐÚNG</b>, không phải "bộ đếm toàn cục bị reset" — reset
        ///     cho delta ÂM, mà delta âm thì <see cref="LiveOpsLogService"/> trả VẮNG, không trả
        ///     số nhỏ.
        ///     <br/>⚠ <b>1.5.6 mới đúng lời hứa "vắng"</b>: từ 1.5.0 đến 1.5.5, cặp này lên dây với
        ///     giá trị <c>0</c> ở MỌI dòng của MỌI event (kể cả event không khai level), vì
        ///     <c>LiveOpsLogService.Open</c> ghi sẵn 0 cho bộ đếm lúc mở đợt. Hệ quả đo được trên
        ///     production 25/10: phía đọc số thấy hai họ khoá cùng xuất hiện 100% dòng nên tưởng SDK
        ///     <b>gửi đôi một khoá</b> và đòi chốt một tên — trong khi hai số khác nghĩa. Giờ cặp này
        ///     chỉ lên dây khi event ĐÃ TỪNG khai event lên một dòng level (cờ theo event, sống qua
        ///     các đợt).
        ///     <br/>⚠ Hệ quả của cách sửa: trên máy đã cài sẵn, event có khai level vẫn <b>vắng</b>
        ///     cặp này cho tới dòng level khai event đầu tiên sau khi nâng bản — vắng = CHƯA BIẾT,
        ///     đúng luật, không phải 0.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? liveOpsLevelPassesSinceEnter;

        /// <inheritdoc cref="liveOpsLevelPassesSinceEnter"/>
        [FKey(RemoveIfNull = true)] public long? liveOpsLevelPassesSincePrevStep;

        /// <inheritdoc cref="liveOpsLevelPassesSinceEnter"/>
        [FKey(RemoveIfNull = true)] public long? liveOpsLevelAttemptsSinceEnter;

        /// <inheritdoc cref="liveOpsLevelPassesSinceEnter"/>
        [FKey(RemoveIfNull = true)] public long? liveOpsLevelAttemptsSincePrevStep;

        [Preserve]
        public FLiveOpsLog()
        {
        }

        public FLiveOpsLog(LiveOpsParam param)
        {
            this.param = param;
        }

        public override string Event => "f_sdk_live_ops_data";

        public override Dictionary<string, object> ToDictionary()
        {
            var result = base.ToDictionary();
            result.Remove(nameof(param));
            foreach (var (key, value) in param.ToDictionary())
                result.PutIfAbsentAndNotNull(key, value);
            return result;
        }
    }
}
