/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-07-31
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Phần chung của mọi mốc trong vòng đời MỘT LẦN XEM AD: request → show → impression → close
    /// (xem EntityLifecycle-Design.md §4f). Cả 4 mốc mang cùng <c>adViewId</c> — id đó do SDK sinh nên nằm trên log (<see cref="AAdLog"/>), không nằm ở đây.
    /// </summary>
    [Serializable]
    public class AdViewParam : ALiveOpsParam
    {
        public AdType type;

        /// <summary>
        /// Vị trí/ngữ cảnh — cùng tên với <see cref="AdParam.adWhere"/> để join dễ.
        /// Lúc request thường chưa biết (preload); lúc show thì biết.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string adWhere;

        /// <summary>
        /// Ngữ cảnh/thời điểm kích hoạt — cùng tên với <see cref="AdParam.adWhen"/> để join dễ.
        /// Như <see cref="adWhere"/>: preload thường chưa biết, lúc show mới biết.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string adWhen;

        /// <summary>
        /// <c>playTurnId</c> của lượt chơi đã KÍCH HOẠT lần chiếu này (hợp đồng đợt 3 B12, ký 23/11): QC trong lượt
        /// (hồi sinh, booster) = lượt đang mở; Inter sau thắng / thua = lượt vừa kết thúc (<c>Level.LastEndedTurnId</c>).
        /// <b>Vắng</b> khi không do lượt nào (mở app, home, shop…). Chỉ game biết — SDK không suy.
        /// <br/>Khác <c>playTurnId</c> SDK tự đính (lượt đang mở lúc ghi dòng). Cùng luật hai chiều với
        /// <see cref="adWhen"/>: khai ở mốc nào / qua <c>Ad.UpdateContext</c> thì các mốc sau của lần chiếu tự mang.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string triggerPlayTurnId;

        /// <summary>
        /// Mediation đang dùng (Max, IronSource, AdMob...) nếu biết — SDK gửi NGUYÊN VĂN chuỗi
        /// mediation truyền vào.
        /// <br/>⚠ 1.5.5 từng thêm bước chuẩn hoá về <c>max</c>/<c>levelplay</c>/<c>admob</c> rồi
        /// <b>RÚT LẠI</b> (owner chốt 26/10: bộ giá trị đó copy từ chỗ khác, giữ chữ cũ không có vấn
        /// đề lớn, mà đổi giá trị một cột đang chạy thì mọi dashboard lọc theo chữ cũ phải sửa).
        /// Nhánh nào khai sai mediation thì sửa ở nguồn — xem việc V5 của phiếu Mediation.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string adMediation;

        /// <summary>
        /// Tên THUẬT TOÁN load quảng cáo đang chạy (yêu cầu phía đọc số 26/10) — thuộc tính của cả
        /// ĐỢT xin ad, không phải của từng unit, nên nó đi theo luật hai chiều như
        /// <see cref="adMediation"/>: mốc nào điền thì thắng và cập nhật cache, mốc bỏ trống thì
        /// lấy từ cache của view.
        /// <br/>SDK KHÔNG tự biết giá trị này — tầng mediation phải khai (xem
        /// <c>FAdApi.UpdateContext</c>). Multicall của Max có 8 nhánh
        /// (<c>MultiCallMaxProvider0..7</c>, phân biệt bằng <c>IMultiCallProvider.GetIndex()</c>),
        /// nên giá trị đề xuất là <c>multicall_&lt;index&gt;</c>; đường một-unit thì <c>single</c>.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull] public string adAlgorithm;

        /// <summary>
        /// Loại kết nối lúc xin ad — SDK-core tự đo. Giải thích fill-fail: không có mạng thì
        /// mediation không trả ad được. Chỉ điền ở mốc request (show/close không cần).
        /// </summary>
        [FKey(RemoveIfNull = true)] public NetworkType? networkType;

        /// <summary>
        /// Key-value tuỳ ý của mediation/game cho MỐC này (waterfall, adUnitId...) — flatten vào
        /// payload, server lưu <c>event_extra_props</c>. Cột hợp đồng luôn thắng khi trùng key.
        /// Khai ở base nên cả ba mốc request/show/close đều dùng được.
        /// </summary>
        [FKey(RemoveIfNull = true)] [CanBeNull]
        public Dictionary<string, object> extraMeta;

        public override Dictionary<string, object> ToDictionary()
        {
            var dictionary = base.ToDictionary();
            dictionary.Remove(nameof(extraMeta));
            return dictionary.MergeExtras(extraMeta, this);
        }
    }

    /// <summary>Mốc gọi hiển thị ad (§4f).</summary>
    [Serializable]
    public class AdShowParam : AdViewParam
    {
        /// <summary>
        /// Ad unit sắp chiếu, nếu mediation biết. Mốc này KHÔNG được SDK đoán hộ: một đợt xin ad có
        /// nhiều unit load song song (nhiều floor), nên lấy "unit của lần load cuối" dán vào đây là
        /// bịa. Vắng = không biết; dòng impression thì luôn có vì mediation truyền.
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adUnitId;
    }

    /// <summary>Mốc ad đóng lại, người chơi quay về game (§4f).</summary>
    [Serializable]
    public class AdCloseParam : AdViewParam
    {

        /// <summary>Người chơi có bấm vào ad không (mediation điền nếu biết).</summary>
        [FKey(RemoveIfNull = true)] public bool? hasClick;

        /// <summary>
        /// Xem hết ad hay bỏ giữa chừng (rewarded: có được trả thưởng không) — mediation điền.
        /// Đây là <c>ad_completed</c> của hợp đồng §D2, đặt ở mốc close vì chỉ lúc đóng mới biết.
        /// </summary>
        [FKey(RemoveIfNull = true)] public bool? adCompleted;

        /// <summary>
        /// Ad unit vừa chiếu. Mediation điền thì thắng; bỏ trống thì SDK lấy unit đã ghi ở MỐC
        /// IMPRESSION của đúng view này — đó là sự thật, không phải suy đoán (chỉ một unit được
        /// chiếu, và nó vừa báo impression xong).
        /// </summary>
        [FKey(RemoveIfNull = true)] public string adUnitId;
    }
}
