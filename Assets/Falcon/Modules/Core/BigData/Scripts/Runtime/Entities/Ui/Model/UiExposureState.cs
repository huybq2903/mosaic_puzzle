/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-09-05
 */

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Kho gộp cụm exposure UI (icon/popup/banner hiện ra, được bấm, bị đóng) — cùng triết lý cụm
    /// banner/multi-floor: per-lần-hiện là volume không chặn trên (chốt owner 05/09 CẤM raw lên
    /// wire), gộp lại thì mỗi stretch chỉ còn vài dòng, mà tỉ lệ theo ngày phía server vẫn chính
    /// xác tuyệt đối (sum/sum — server vẫn là người tính, client chỉ NÉN).
    /// <br/><b>MỘT CỤM = MỘT DÒNG</b> (ký với loader 09/11): mỗi hành vi là một CỘT ĐẾM của cụm,
    /// không còn là một dòng riêng. Tập hành vi là tập đóng nên cột rẻ ngang giá trị, mà một dòng
    /// mỗi hành vi thì lặp lại cả khoá và đẻ ra bẫy "đừng cộng ngang hành vi".
    /// <br/>Pure class — không IO/DI/khoá; <see cref="UiExposureClusterService"/> lo thread-safety.
    /// </summary>
    public class UiExposureState
    {
        /// <summary>Một cụm đã gộp — mỗi cụm thành một dòng <see cref="FUiExposureLog"/>.</summary>
        public class Entry
        {
            public string surfaceId;

            /// <summary>VÌ SAO bề mặt hiện ra — vocab game, trong KHOÁ GỘP. Xem <c>FUiApi.OnImpression</c>.</summary>
            public string trigger;

            /// <summary>
            /// Ngữ cảnh của cụm — CẢ HAI TRỤC (event live-op và tính năng) đều nằm trong KHOÁ GỘP,
            /// không phải trong extras: hai đợt, hai mốc, hay hai bang hội khác nhau mà gộp chung
            /// một dòng là mất số im lặng, đúng bài học khoá gộp của cụm quảng cáo 1.4.1.
            /// </summary>
            public LiveOpsRef liveOps;

            /// <summary>
            /// Extras của LẦN GHI ĐẦU vào cụm — bất kể lần đó là hiện, bấm hay đóng. Đại diện cho cả
            /// cụm, nên chỉ nên chở ngữ cảnh BẤT BIẾN của bề mặt (như race_id).
            /// </summary>
            public Dictionary<string, object> extraMeta;

            public int impressionCount;
            public int clickCount;
            public int dismissCount;

            /// <summary>Tập con của <see cref="impressionCount"/> — xem <see cref="UiTally"/>.</summary>
            public int adNotReadyCount;

            internal void Add(UiTally tally)
            {
                impressionCount += tally.impressions;
                clickCount += tally.clicks;
                dismissCount += tally.dismisses;
                adNotReadyCount += tally.adNotReady;
            }
        }

        // Khoá gộp = surface × TRỤC EVENT (4) × TRỤC TÍNH NĂNG (3) × trigger. Hành vi KHÔNG ở trong
        // khoá: nó là cột đếm.
        // ⚠ Ba ô feature* có mặt ở đây là BẮT BUỘC, không phải cho đủ bộ: LiveOpsRef kế thừa lớp
        // nền đã gộp nên nó CHỞ ĐƯỢC trục tính năng. Thiếu chúng trong khoá thì hai bang hội gộp
        // thành một dòng mang id của bang hội ĐẦU TIÊN — im lặng.
        // ⚠ trigger vào khoá (ký 06/11): "popup bật tự động" và "popup do người chơi bấm" gộp một
        // dòng thì tỉ lệ của cả hai đều vô nghĩa, mà gộp xong thì KHÔNG tách lại được.
        // ⚠ Tiến độ KHÔNG vào khoá (09/11, owner chốt): mọi dòng log đã mang maxPassedLevel do
        // BaseLogDecorService đóng chung. Đánh đổi đã biết: dòng dựng lúc XẢ, nên đó là tiến độ lúc
        // xả (≥ lúc hiện).
        private readonly
            Dictionary<(string surface, string liveOpsEvent, string cycleId, string roundId, int? stage,
                string feature, string instanceId, int? featureStage, string trigger), Entry> _entries = new();

        public int Count => _entries.Count;

        /// <summary>
        ///     Trả <c>false</c> khi lần ghi bị TỪ CHỐI (thiếu surface) — người gọi cần biết, vì nó
        ///     quyết định có đặt dấu "đang có cụm mở" hay không. Suy bằng <c>Count == 0</c> là sai,
        ///     vì <c>Count</c> còn đếm những cụm của các lần ghi TRƯỚC.
        /// </summary>
        public bool Record(
            string surfaceId, UiTally tally, Dictionary<string, object> extraMeta,
            LiveOpsRef liveOps = null, string trigger = null)
        {
            if (string.IsNullOrEmpty(surfaceId)) return false;

            var key = (surfaceId, liveOps?.liveOpsEvent, liveOps?.liveOpsCycleId, liveOps?.liveOpsRoundId,
                liveOps?.liveOpsStage, liveOps?.feature, liveOps?.featureInstanceId, liveOps?.featureStage,
                trigger);
            if (!_entries.TryGetValue(key, out var entry))
                _entries[key] = entry = new Entry
                {
                    surfaceId = surfaceId,
                    extraMeta = extraMeta,
                    liveOps = liveOps,
                    trigger = trigger
                };

            entry.Add(tally);
            return true;
        }

        /// <summary>Rút toàn bộ cụm và làm sạch kho — mỗi lần flush là một thế hệ cụm mới.</summary>
        public List<Entry> TakeAll()
        {
            var result = new List<Entry>(_entries.Values);
            _entries.Clear();
            return result;
        }
    }
}
