/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Tham số tiến độ của lượt đang mở, để SDK tự dựng dòng heartbeat (hợp đồng đợt 3 B5, ký 23/11).
    /// <br/>Hai nguồn, gộp theo thứ tự:
    /// <list type="number">
    /// <item>bản game đẩy gần nhất qua <c>Level.OnHeartbeat</c> (từ bản này là "cập nhật tham số", không sinh dòng);</item>
    /// <item>các <see cref="ITurnParamsFiller"/> (APS) gọi tại lúc bắn — mới hơn nên ghi đè field chúng điền.</item>
    /// </list>
    /// Bản đẩy chỉ dùng cho ĐÚNG lượt lúc đẩy: lượt đổi thì bỏ, không dán tiến độ lượt cũ lên lượt mới.
    /// <br/>Pure class — không IO/DI/time; caller truyền id lượt đang mở và mốc giờ, tự lo thread-safety.
    /// </summary>
    public class LevelTurnParamsState
    {
        private LevelHeartBeatParamV2 _pushed;
        private string _pushedTurnId;
        private long _pushedAtMillis;

        /// <summary>
        /// Game đẩy tham số của lượt đang mở. Không có lượt mở thì bỏ (tham số chỉ có nghĩa trong lượt).
        /// </summary>
        /// <returns>false nếu bị bỏ vì không có lượt mở.</returns>
        public bool Push(LevelHeartBeatParamV2 param, string openTurnId, long nowMillis)
        {
            if (param == null || string.IsNullOrEmpty(openTurnId)) return false;
            _pushed = Copy(param);
            _pushedTurnId = openTurnId;
            _pushedAtMillis = nowMillis;
            return true;
        }

        /// <summary>
        /// Dựng dòng heartbeat cho lượt đang mở. Null nếu không có lượt mở (SDK không bắn heartbeat ngoài lượt).
        /// <br/><c>paramsUpdatedAt</c> = lúc tham số được cập nhật lần cuối: filler điền được ⇒ <paramref name="nowMillis"/>;
        /// không thì lúc game đẩy; chưa có lần nào trong lượt ⇒ vắng.
        /// </summary>
        public LevelHeartBeatParamV2 Build(string openTurnId, IEnumerable<ITurnParamsFiller> fillers, long nowMillis)
        {
            if (string.IsNullOrEmpty(openTurnId)) return null;

            var fromPush = _pushed != null && _pushedTurnId == openTurnId;
            var param = fromPush ? Copy(_pushed) : new LevelHeartBeatParamV2();
            long? updatedAt = fromPush ? _pushedAtMillis : null;

            if (fillers != null)
                foreach (var filler in fillers)
                    if (filler != null && filler.FillTurnParams(param))
                        updatedAt = nowMillis;

            // Định danh lượt là của SDK (cache) — bản đẩy cũ có thể mang id khác, đừng để nó thắng.
            param.playTurnId = openTurnId;
            param.paramsUpdatedAt = updatedAt;
            return param;
        }

        /// <summary>Lượt đang mở đổi / đóng — bỏ bản đẩy (không bắt buộc: <see cref="Build"/> đã tự lọc theo id).</summary>
        /// <summary>
        /// Làm mới tham số giữa lượt (APS, hợp đồng đợt 3 B5 §5.3): dựng như <see cref="Build"/> rồi, nếu có nguồn điền
        /// được, giữ kết quả làm bản đẩy mới — bản chụp khẩn và lần bắn sau bắt đầu từ đây. Không nguồn nào điền thì bản
        /// đẩy cũ giữ nguyên (cả mốc <c>paramsUpdatedAt</c>).
        /// </summary>
        public LevelHeartBeatParamV2 Refresh(string openTurnId, IEnumerable<ITurnParamsFiller> fillers, long nowMillis)
        {
            var param = Build(openTurnId, fillers, nowMillis);
            if (param?.paramsUpdatedAt == null || param.paramsUpdatedAt.Value != nowMillis) return param;
            _pushed = Copy(param);
            _pushedTurnId = openTurnId;
            _pushedAtMillis = nowMillis;
            return param;
        }

        public void Clear()
        {
            _pushed = null;
            _pushedTurnId = null;
            _pushedAtMillis = 0;
        }

        private static LevelHeartBeatParamV2 Copy(LevelHeartBeatParamV2 source)
        {
            return new LevelHeartBeatParamV2
            {
                playTurnId = source.playTurnId,
                currentLevel = source.currentLevel,
                currentLevelId = source.currentLevelId,
                levelDataId = source.levelDataId,
                difficulty = source.difficulty,
                score = source.score,
                movesUsed = source.movesUsed,
                duration = source.duration,
                levelProgress = source.levelProgress,
                boostersUsed = source.boostersUsed == null ? null : new Dictionary<string, int>(source.boostersUsed),
                extraMeta = source.extraMeta == null ? null : new Dictionary<string, object>(source.extraMeta),
                comeBackAfterFirstPass = source.comeBackAfterFirstPass,
                autoCheckComeBackAfterFirstPass = source.autoCheckComeBackAfterFirstPass,
                liveOpsEvent = source.liveOpsEvent,
                liveOpsCycleId = source.liveOpsCycleId,
                liveOpsRoundId = source.liveOpsRoundId,
                liveOpsStage = source.liveOpsStage,
                feature = source.feature,
                featureInstanceId = source.featureInstanceId,
                featureStage = source.featureStage
            };
        }
    }
}
