/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-14
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    ///     Một MỐC của event live-op — payload của <see cref="FLiveOpsLog"/>.
    ///     <br/>Khác funnel cũ ở ba chỗ: không có <c>funnelShape</c> (log này chỉ một hình dạng nên
    ///     không phải khai, cũng hết ca "quên Register là trượt trắng"), không có <c>funnelDay</c>
    ///     (vai của nó do <see cref="FLiveOpsLog.liveOpsEnterTs"/> gánh, chính xác hơn vì không phụ
    ///     thuộc user có bắn <c>join</c> hay không), và không có <c>priority</c> — số mốc nằm ở
    ///     <see cref="ALiveOpsParam.liveOpsStage"/> và CHỈ có trên dòng mốc.
    /// </summary>
    [Serializable]
    public class LiveOpsParam : ALiveOpsParam
    {
        /// <summary>Một trong sáu <see cref="FLiveOpsAction"/>.</summary>
        [NotNull] public string action = UNKNOWN;

        /// <summary>
        ///     Đợt thứ mấy trong lịch sử event — trục SORT bằng SỐ, để server khỏi parse tên
        ///     <see cref="ALiveOpsParam.liveOpsCycleId"/> (<c>season_9</c> &gt; <c>season_10</c> khi
        ///     sort chuỗi). Số nguyên tăng dần, KHÔNG phải timestamp.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? cycleIndex;

        /// <summary>
        ///     Mốc MỞ/ĐÓNG của ĐỢT (chung mọi user) — epoch millis UTC, lấy từ CONFIG event, đúng
        ///     con số UI countdown đang vẽ. Server tự tính "lúc log còn mấy ngày" =
        ///     <c>cycleEndTs − ts</c>, khỏi cần bảng lịch vận hành.
        ///     <br/>Game không truyền ở từng bước — <c>LiveOps.DeclareCycle</c> khai một lần rồi
        ///     SDK tự đóng.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? cycleStartTs;

        /// <inheritdoc cref="cycleStartTs"/>
        [FKey(RemoveIfNull = true)] public long? cycleEndTs;

        [FKey(RemoveIfNull = true)] public int? currentLevel;

        #region Kết quả bảng đấu — ký với loader 09/11

        /// <summary>
        ///     HẠNG của user khi CHỐT lượt/mùa — <c>1</c> = nhất. Chỉ có nghĩa ở bước kết thúc
        ///     (<see cref="FLiveOpsAction.Complete"/>, hoặc <c>claim</c> của mốc chốt bảng).
        ///     <br/>⚠ <b>Vì sao tên có tiền tố <c>live_ops_</c></b> (đề nghị của loader, SDK nhận):
        ///     <c>rank</c>/<c>score</c> trần là tên game rất hay tự đặt cho param, mà <c>score</c>
        ///     thì <b>đã là field thật</b> của dòng level (<c>LevelParam.score</c>,
        ///     <c>LevelProgressParamV2.score</c>). Theo luật flatten (field log &gt; central &gt;
        ///     custom &gt; param) trùng tên thì một bên bị nuốt mà không ai biết; và ai gộp hai stream
        ///     sẽ thấy một cột <c>score</c> mang hai nghĩa.
        ///     <br/>⚠ <b>Không dương = KHÔNG BIẾT, SDK bỏ key</b> (cùng luật đã áp cho
        ///     <c>tier</c>/<c>ecpmFloor</c>): hạng <c>0</c> không tồn tại trong bất kỳ bảng đấu nào,
        ///     nên để nó lên dây là đẩy một giá trị SAI mà trông hợp lệ vào kho. Cố ý KHÔNG dùng
        ///     <c>CheckNumberNonNegative</c> ở hai field hạng, vì hàm đó <b>ép về 0</b> khi sai —
        ///     đúng cái vết sẹo đang đi xoá.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? liveOpsRank;

        /// <summary>
        ///     Số người trong BẢNG ĐẤU của user ở lượt/mùa đó — mẫu số để đọc hạng. Không có nó thì
        ///     <c>liveOpsRank = 5</c> không phân biệt được "thứ 5 trong 5" với "thứ 5 trong 50".
        ///     <br/>⚠ Không dương = KHÔNG BIẾT, SDK bỏ key — xem <see cref="liveOpsRank"/>.
        /// </summary>
        [FKey(RemoveIfNull = true)] public int? liveOpsGroupSize;

        /// <summary>
        ///     ĐIỂM của user lúc chốt lượt/mùa. <c>0</c> là một điểm THẬT (vào bảng mà không ghi được
        ///     điểm nào) nên KHÔNG bị bỏ — khác hẳn hai field hạng ở trên.
        /// </summary>
        [FKey(RemoveIfNull = true)] public long? liveOpsScore;

        /// <summary>
        ///     Mã BẢNG ĐẤU (nhóm/league/shard) mà user thuộc về ở lượt đó — để gộp đúng những người
        ///     thật sự đấu với nhau.
        ///     <br/>⚠ Khác <see cref="ALiveOpsParam.liveOpsRoundId"/>: lượt là trục THỜI GIAN, bảng
        ///     đấu là trục NGƯỜI. Một lượt có nhiều bảng.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string liveOpsGroupId;

        #endregion

        /// <summary>
        ///     Key-value riêng của event — trải PHẲNG vào payload. Chiều phân tích riêng của từng
        ///     event (bậc sao, số thẻ, loại map…) đi đường này, kèm tiền tố theo luật live-op.
        ///     <br/>⚠ <b>Hạng/điểm/bảng đấu thì KHÔNG còn đi đường này</b> từ bản ký 09/11 — đã có
        ///     bốn field thật ở trên. Game còn gửi <c>live_ops_score</c> qua đây sẽ bị
        ///     <c>FExtraMeta</c> nhắc ngay lúc tích hợp, thay vì mất key im lặng theo luật first-wins.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public Dictionary<string, object> extraMeta;

        public override void CorrectValues()
        {
            base.CorrectValues();
            action = CheckNonBlank(action, nameof(action));
            cycleIndex = CheckNumberNonNegative(cycleIndex, nameof(cycleIndex));
            currentLevel = CheckNumberNonNegative(currentLevel, nameof(currentLevel));

            if (cycleStartTs.HasValue && cycleEndTs.HasValue && cycleEndTs.Value <= cycleStartTs.Value)
                AnalyticLogger.Instance.Warning(
                    $"LiveOps '{liveOpsEvent}': cycleEndTs ({cycleEndTs}) <= cycleStartTs " +
                    $"({cycleStartTs}) — lịch đợt ngược, soi lại config event.");

            liveOpsRank = DropIfNotPositive(liveOpsRank, nameof(liveOpsRank));
            liveOpsGroupSize = DropIfNotPositive(liveOpsGroupSize, nameof(liveOpsGroupSize));

            if (liveOpsRank.HasValue && liveOpsGroupSize.HasValue && liveOpsRank.Value > liveOpsGroupSize.Value)
                AnalyticLogger.Instance.Warning(
                    $"LiveOps '{liveOpsEvent}': liveOpsRank ({liveOpsRank}) > liveOpsGroupSize " +
                    $"({liveOpsGroupSize}) — hạng không thể lớn hơn số người trong bảng. Cả hai VẪN " +
                    "gửi (thà có cả hai số để kho tự loại còn hơn SDK đoán cái nào sai), nhưng một " +
                    "trong hai đang đo sai chỗ — rất hay là lấy hạng của bảng khác, hoặc lấy số " +
                    "người lúc chốt mà hạng lúc đang chạy.");

            // Hạng/điểm/bảng đấu chỉ có nghĩa khi lượt ĐÃ CHỐT. Gửi ở eligible/open/join là khai một
            // kết quả chưa tồn tại — vẫn gửi (SDK không chặn lời khai của game) nhưng phải nhắc, vì
            // dòng đó làm mọi phép "hạng trung bình theo bước" lệch về phía người mới vào.
            if (HasStandings && action != FLiveOpsAction.Complete && action != FLiveOpsAction.Claim)
                AnalyticLogger.Instance.Warning(
                    $"LiveOps '{liveOpsEvent}': bước '{action}' mang hạng/điểm/bảng đấu. Bốn field " +
                    $"đó là KẾT QUẢ lúc chốt lượt — đúng chỗ là '{FLiveOpsAction.Complete}' (hoặc " +
                    $"'{FLiveOpsAction.Claim}' của mốc chốt bảng). Vẫn gửi, nhưng kho đọc chúng là " +
                    "kết quả cuối lượt nên dòng này sẽ bị trộn vào đó.");
        }

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this);
        }

        private bool HasStandings =>
            liveOpsRank.HasValue || liveOpsGroupSize.HasValue ||
            liveOpsScore.HasValue || !string.IsNullOrEmpty(liveOpsGroupId);

        /// <summary>
        ///     Không dương ⇒ <c>null</c> (bỏ key) + cảnh báo. Cố ý KHÔNG dùng
        ///     <c>CheckNumberNonNegative</c>: hàm đó trả <b>0</b> khi gặp số âm, mà ở hai field hạng
        ///     thì <c>0</c> là một giá trị sai TRÔNG HỢP LỆ — kho sẽ cộng nó vào mọi phép đọc hạng.
        /// </summary>
        private int? DropIfNotPositive(int? value, string fieldName)
        {
            if (!value.HasValue || value.Value > 0) return value;

            AnalyticLogger.Instance.Warning(
                $"LiveOps '{liveOpsEvent}': {fieldName} = {value.Value} — không dương nghĩa là KHÔNG " +
                "BIẾT, SDK bỏ key thay vì gửi. Hạng bắt đầu từ 1; bảng đấu rỗng thì đừng gửi bước " +
                "chốt. (Cùng luật đã áp cho tier/ecpmFloor: vắng còn hơn một số sai trông hợp lệ.)");
            return null;
        }
    }
}
