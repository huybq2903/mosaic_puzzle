/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-11
 */

using System;
using System.Collections.Generic;
using Falcon.Modules.Core.Network;
using Falcon.Modules.Core.SaveLoad.Runtime;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    [FAMessage("sc_ad_bid_floor_v2")]
    public class SCAdBidFloorV2 : SCMessage
    {
        public List<AdBidFloorV2> adBidFloors;

        public override void OnData()
        {
            MediationManager.Instance.DebugLog(
                $"[BidFloor] nhận sc_ad_bid_floor_v2 (v2 -> Lua): {adBidFloors?.Count ?? 0} id");
            if (adBidFloors != null && adBidFloors.Count > 0)
                AdBidFloorsV2.Set(adBidFloors);
            else
                AdBidFloorsV2.MarkResponded();

#if MAX_ENABLE
            LuaAdMonetize.OnBidFloorsChanged();
#endif
        }
    }

    public class AdBidFloorV2
    {
        public string adUnitId;
        public bool rewarded;
        public float multiplier;
        public string timeConfig;
        public string mediation;
        public string adUnitIdType;

        public string MediationKind() => AdBidFloor.ResolveMediation(mediation, adUnitId);
    }

    public static class AdBidFloorsV2
    {
        private const string _KEY_CACHE = "FALCON_LIST_AD_BID_FLOORS_V2";

        private static readonly object _lock = new object();
        private static List<AdBidFloorV2> _list;
        private static volatile bool _responded;

        public static bool HasResponded => _responded;

        public static List<AdBidFloorV2> List
        {
            get
            {
                lock (_lock)
                {
                    if (_list != null) return _list;
                    try
                    {
                        _list = SaveLoadHandler.Load(_KEY_CACHE, new List<AdBidFloorV2>());
                        MediationManager.Instance.DebugLog(
                            $"[BidFloor] đọc cache sc_ad_bid_floor_v2: {_list?.Count ?? 0} id");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogException(e);
                    }

                    return _list ??= new List<AdBidFloorV2>();
                }
            }
        }

        public static void Set(List<AdBidFloorV2> list)
        {
            lock (_lock)
            {
                _list = list ?? new List<AdBidFloorV2>();
                SaveLoadHandler.Save(_KEY_CACHE, _list);
            }

            _responded = true;
        }

        public static void MarkResponded() => _responded = true;
    }
}
