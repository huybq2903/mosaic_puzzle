/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-26
 */

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Sổ sự thật theo AD UNIT — những gì mediation chỉ nói MỘT LẦN lúc load (network thắng, bậc,
    /// giá sàn, eCPM ước tính) để các mốc sau còn chở được.
    /// <br/><b>Vì sao khoá là <c>adUnitId</c> chứ không phải view</b> (chốt 26/10): luật monetize là
    /// nhiều floor load SONG SONG trong một đợt, cái nào xong thì GIỮ, và chỉ một cái được chiếu.
    /// Nhớ theo view thì "lần load cuối của đợt" lại là unit KHÁC với unit đang chiếu → dán
    /// network/ecpm của unit khác lên dòng impression, tức bịa. Khoá theo unit thì mỗi dòng chỉ lấy
    /// đúng sự thật của chính unit nó.
    /// <br/>⚠ Phân tầng: <c>adMediation</c> và <c>adAlgorithm</c> là <b>của cả ĐỢT</b> (mọi unit
    /// trong đợt giống nhau) nên chúng nằm ở <see cref="AdViewState"/>; sổ này chỉ giữ thứ
    /// <b>của riêng unit</b>.
    /// <br/><see cref="MAX_UNITS"/> là trần bộ nhớ: game nhiều floor thì vài chục unit mỗi format,
    /// quá trần thì bỏ bản ghi CŨ NHẤT (sự thật cũ ít giá trị hơn sự thật mới).
    /// <br/>Pure class — không IO/khoá; <see cref="AdUnitFactsCache"/> lo thread-safety.
    /// </summary>
    public class AdUnitFactsState
    {
        private const int MAX_UNITS = 128;

        /// <summary>Sự thật của một ad unit, theo lần load THÀNH CÔNG gần nhất của nó.</summary>
        public class UnitFacts
        {
            public string adNetwork;
            public double? tier;
            public double? floor;
            public double? estimatedEcpm;

            /// <summary>
            /// ⚠ <b>BỐN field dưới đây là sự thật của MỘT QUẢNG CÁO, không phải cấu hình của unit</b>
            /// (<c>adCandidateId</c> · <c>ecpmFloor</c> · <c>ecpmFloorType</c> ·
            /// <c>ecpmFloorPercentile</c>) nên chúng KHÔNG theo luật "null không đè" của sổ này, và bị
            /// <see cref="ConsumeAdFacts"/> dọn sau khi đã dán lên một dòng impression. Xem
            /// <see cref="Record"/> để biết vì sao.
            /// </summary>
            public string adCandidateId;

            public double? ecpmFloor;

            public string ecpmFloorType;

            public string ecpmFloorPercentile;

            public long atMillis;
        }

        private readonly Dictionary<string, UnitFacts> _byUnit = new();

        public int Count => _byUnit.Count;

        /// <summary>
        /// Ghi sự thật của một lần load. CHỈ nhận lần THÀNH CÔNG: lần fail không có network thắng và
        /// không có giá, ghi vào là xoá sự thật cũ bằng một rổ null.
        /// <br/>Giá trị <c>null</c> của lần mới KHÔNG đè giá trị cũ đang có — mediation điền thiếu
        /// một field ở lần sau không được phép làm mất field đó.
        /// </summary>
        public void Record(AdLoadResultParam param, long nowMillis)
        {
            if (param is not { success: true } || string.IsNullOrEmpty(param.adUnitId)) return;

            if (!_byUnit.TryGetValue(param.adUnitId, out var facts))
            {
                TrimIfNeeded();
                _byUnit[param.adUnitId] = facts = new UnitFacts();
            }

            if (!string.IsNullOrEmpty(param.networkName)) facts.adNetwork = param.networkName;
            if (Positive(param.tier)) facts.tier = param.tier;
            if (Positive(param.floor)) facts.floor = param.floor;
            if (Positive(param.estimatedEcpm)) facts.estimatedEcpm = param.estimatedEcpm;

            // NGOẠI LỆ của luật "null không đè giá trị cũ" — bốn field tả MỘT QUẢNG CÁO, luôn ghi đè:
            // mã nhát thử là DANH TÍNH của chính quảng cáo đó, còn giá sàn + vai sàn là lời khai của
            // ĐÚNG request đã mang nó về. Để giá trị cũ lại thì lần load sau (không khai vai, vì flow
            // chọn sàn chỉ chạy cho quảng cáo đầu phiên) sẽ mượn vai của quảng cáo trước — dòng
            // impression mang nhãn `upgrade_higher` cho một request chưa từng áp sàn đó, và cái nhãn
            // bịa ấy rơi đúng vào tập dòng mà bên đọc số dùng để chấm personalization.
            facts.adCandidateId = param.adCandidateId;
            facts.ecpmFloor = Positive(param.ecpmFloor) ? param.ecpmFloor : null;
            facts.ecpmFloorType = Blank(param.ecpmFloorType) ? null : param.ecpmFloorType;
            facts.ecpmFloorPercentile = Blank(param.ecpmFloorPercentile) ? null : param.ecpmFloorPercentile;

            facts.atMillis = nowMillis;
        }

        /// <summary>
        /// Điền những field của RIÊNG UNIT mà mốc này bỏ trống — mốc tự nhập thì THẮNG (cùng luật
        /// hai chiều với <c>AdViewState.ApplyContext</c>). Không có <c>adUnitId</c> thì KHÔNG đoán
        /// gì: một đợt có nhiều unit load song song nên "unit gần nhất" không phải unit đang chiếu.
        /// </summary>
        /// <returns>true nếu có sổ của unit đó để lấy.</returns>
        public bool ApplyTo(AdParam param)
        {
            if (param == null || string.IsNullOrEmpty(param.adUnitId)) return false;

            var facts = Get(param.adUnitId);
            if (facts == null) return false;

            param.adNetwork ??= facts.adNetwork;
            param.tier ??= facts.tier;
            param.floor ??= facts.floor;
            param.adCandidateId ??= facts.adCandidateId;

            // Giá sàn + VAI của sàn thì CÓ điền (1.5.7, bên đọc số xin trên cả dòng impression): sàn
            // là cấu hình của request, không phải trục tiền, nên không trùng adRev cái gì.
            param.ecpmFloor ??= facts.ecpmFloor;
            param.ecpmFloorType ??= facts.ecpmFloorType;
            param.ecpmFloorPercentile ??= facts.ecpmFloorPercentile;

            // KHÔNG điền eCPM ƯỚC TÍNH: trên dòng impression nó bằng adRev × 1000 nên phía đọc tự
            // nhân được (owner chốt 26/10, chốt lại 29/10 khi bên đọc số xin lần hai). Sổ vẫn GIỮ số
            // đó vì dòng load dùng.
            return true;
        }

        /// <summary>
        /// Dọn sự thật của MỘT QUẢNG CÁO khỏi sổ (mã nhát thử + giá sàn + vai sàn + percentile) — gọi
        /// NGAY SAU khi đã dán chúng lên một dòng impression.
        /// <br/>Vì sao phải tiêu thụ: một quảng cáo = MỘT lượt chiếu. Nếu mediation chiếu lần thứ hai
        /// trên cùng unit mà không báo lần load mới (ca V4 của phiếu Mediation), cả bốn giá trị cũ sẽ
        /// dán lên dòng impression thứ hai ⇒ một nhát thử trông như mang về hai lần tiền, dưới một
        /// nhãn sàn không thuộc về nó. Vắng thì đọc ra "không biết", sai thì không ai thấy.
        /// <br/>Cấu hình của unit (network, bậc, sàn thật) KHÔNG bị dọn — chúng vẫn đúng cho dòng sau.
        /// <br/>Cùng lẽ với <c>Consume</c> của sổ mã bên tầng mediation.
        /// </summary>
        public void ConsumeAdFacts(string adUnitId)
        {
            var facts = Get(adUnitId);
            if (facts == null) return;

            facts.adCandidateId = null;
            facts.ecpmFloor = null;
            facts.ecpmFloorType = null;
            facts.ecpmFloorPercentile = null;
        }

        /// <summary>Sự thật của unit này, null nếu chưa từng có lần load thành công nào được ghi.</summary>
        public UnitFacts Get(string adUnitId)
        {
            return string.IsNullOrEmpty(adUnitId) ? null : _byUnit.GetValueOrDefault(adUnitId);
        }

        /// <summary>
        /// eCPM/bậc/giá sàn <b>không dương là KHÔNG BIẾT</b>, không phải "bằng 0": mediation trả 0 (hoặc
        /// -1) cho ca không có số liệu, mà ghi 0 lên dây là khai một giá trị giả — cùng luật với
        /// "tier vắng ≠ tier 0" đã ký với loader 23/09.
        /// </summary>
        public static bool Positive(double? value) => value is > 0;

        /// <summary>Chuỗi rỗng/trắng cũng là KHÔNG BIẾT — cùng luật với số không dương.</summary>
        private static bool Blank(string value) => string.IsNullOrWhiteSpace(value);

        private void TrimIfNeeded()
        {
            if (_byUnit.Count < MAX_UNITS) return;

            var oldestKey = (string)null;
            var oldestAt = long.MaxValue;
            foreach (var (key, facts) in _byUnit)
                if (facts.atMillis < oldestAt)
                {
                    oldestAt = facts.atMillis;
                    oldestKey = key;
                }

            if (oldestKey != null) _byUnit.Remove(oldestKey);
        }
    }
}
