/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 */
// 2025-03-10

using System.Collections.Generic;
using Falcon.Modules.Core.Network;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    [FAMessage("sc_ad_bid_floor")]
    public class SCAdBidFloor : SCMessage
    {
        public List<AdBidFloor> adBidFloors;

        public override void OnData()
        {
            MediationManager.Instance.DebugLog(
                $"[BidFloor] nhận sc_ad_bid_floor (v1 -> MultiCall): {adBidFloors?.Count ?? 0} id");
            if (adBidFloors != null && adBidFloors.Count > 0)
            {
                MultiCall.Instance.ListAdBidFloors = adBidFloors;
            }
            else
            {
                //Payload rỗng vẫn là ĐÃ trả lời. Không mở cờ ở đây thì hàng rào init trong
                //MediationManager.StartProcess phải chờ hết 7s timeout mới chịu init mediation,
                //trong khi câu trả lời đã về từ lâu. Cố ý KHÔNG gán list: rỗng nghĩa là không có gì
                //để cập nhật, không phải lệnh xoá cấu hình đang cache.
                MultiCall.Instance.MarkAdBidFloorResponded();
            }

            //Meta AdMob (phiên MULTICALL) cũng lấy id AdMob bước B + C từ đây, không còn từ remote config
            Falcon.Admob.AdmobMediationFalconBridge.OnBidFloorsChanged();

#if MAX_ENABLE
            //Lua: v2 không có id nào thì lấy id từ v1 -> v1 về muộn cũng phải đồng bộ lại
            LuaDynamic.LuaAdMonetize.OnBidFloorsChanged();
#endif
        }
    }

    public class AdBidFloor
    {
        public string adUnitId; //ad unit id
        public bool rewarded; //true : là rewarded, false : là interstitial
        public float multiplier; //hệ số nhân cho floor
        public string timeConfig;
        public List<int> algorithmIds;

        //LuaDynamic: loại ad unit "default_1", "default_2", "h1", "h2", "h3", ... (đưa sang Lua qua context)
        public string adUnitIdType;

        //LuaDynamic: "max" | "admob" (không phân biệt hoa thường). Rỗng = tự nhận theo id (id AdMob bắt đầu bằng "ca-app-pub-")
        public string mediation;

        // giá trị mediation sau khi đưa về chữ thường (sau này có thể thêm "levelplay")
        public const string MEDIATION_MAX = "max";
        public const string MEDIATION_ADMOB = "admob";
        private const string _ADMOB_ID_PREFIX = "ca-app-pub-";

        /// <summary>
        /// Mediation của id: <see cref="MEDIATION_MAX"/> hoặc <see cref="MEDIATION_ADMOB"/>. Field mediation trống /
        /// giá trị lạ -> tự nhận theo id (id AdMob bắt đầu bằng "ca-app-pub-").
        /// </summary>
        public string MediationKind() => ResolveMediation(mediation, adUnitId);

        public static string ResolveMediation(string mediation, string adUnitId)
        {
            switch (mediation?.Trim().ToLowerInvariant())
            {
                case MEDIATION_MAX: return MEDIATION_MAX;
                case MEDIATION_ADMOB: return MEDIATION_ADMOB;
            }

            return adUnitId != null && adUnitId.Trim().StartsWith(_ADMOB_ID_PREFIX, System.StringComparison.Ordinal)
                ? MEDIATION_ADMOB
                : MEDIATION_MAX;
        }
    }
}