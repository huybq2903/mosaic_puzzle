/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-27
 */

using System;
using System.Collections.Generic;
using Falcon.Modules.Core.RemoteConfig;
using Falcon.Modules.Core.RemoteConfigCms;
using UnityEngine;
using GameConfig = Falcon.Modules.Core.RemoteConfig.FConfig;
using CmsConfig = Falcon.Modules.Core.RemoteConfigCms.FConfig;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.DFPool
{
    /// <summary>Một mức của bước 2: request AdMob <see cref="adUnitId"/> ở giá sàn = revenue MAX × <see cref="multiplier"/>.</summary>
    [Serializable]
    public class DFPoolTarget
    {
        public double multiplier = 1d;
        public string adUnitId;
    }

    [Serializable]
    public class DFPoolFormatConfig
    {
        /// <summary>Các ad unit MAX "slot" (khuyến nghị 2) — waterfall chỉ có DFPoolNetwork.</summary>
        public string[] maxSlotIds;

        /// <summary>
        /// Ad unit AdMob "mặc định": xin KHÔNG set giá sàn, không phụ thuộc revenue MAX. Luôn giữ sẵn
        /// đúng 1 ad của ad unit này trong kho — load ngay khi DFPool bật, load lại khi ad rời kho
        /// (show / hết hạn / bị waterfall từ chối). Rỗng = không chạy luồng này.
        /// </summary>
        public string defaultAdId;

        /// <summary>Thang giá sàn AdMob, mỗi phần tử là một request khi MAX bước 1 load xong.</summary>
        public DFPoolTarget[] targets;

        public string DefaultAdUnitId => string.IsNullOrWhiteSpace(defaultAdId) ? null : defaultAdId.Trim();

        public List<string> ValidSlotIds()
        {
            var list = new List<string>();
            if (maxSlotIds == null) return list;
            foreach (var id in maxSlotIds)
                if (!string.IsNullOrWhiteSpace(id) && !list.Contains(id.Trim()))
                    list.Add(id.Trim());
            return list;
        }

        public List<DFPoolTarget> ValidTargets()
        {
            var list = new List<DFPoolTarget>();
            if (targets == null) return list;
            foreach (var t in targets)
                if (t != null && !string.IsNullOrWhiteSpace(t.adUnitId))
                    list.Add(t);
            return list;
        }

        /// <summary>Có slot MAX và có ít nhất một nguồn ad AdMob (mặc định hoặc thang giá sàn).</summary>
        public bool IsValid => ValidSlotIds().Count > 0 && (DefaultAdUnitId != null || ValidTargets().Count > 0);
    }

    /// <summary>
    /// JSON của key <see cref="DFPoolConfigReader.KEY"/>. Parse bằng JsonUtility -> tên field phải
    /// khớp CHÍNH XÁC; field vắng mặt giữ giá trị mặc định dưới đây.
    /// </summary>
    [Serializable]
    public class DFPoolConfigData
    {
        public DFPoolFormatConfig interstitial;
        public DFPoolFormatConfig rewarded;

        /// <summary>Tuổi thọ ad AdMob trong kho (phút). AdMob cho ~60 phút, để dư một chút.</summary>
        public float ttlMinutes = 55f;

        /// <summary>Ad còn sống ít hơn mốc này (giây) thì không đem gắn vào slot nữa.</summary>
        public float minBindSeconds = 120f;

        /// <summary>Số ad tối đa trong kho MỖI format (tính cả ad đang được slot mượn).</summary>
        public int poolCap = 8;

        /// <summary>Chỉ đổi ad trong slot khi ad mới đắt hơn ít nhất tỉ lệ này (0.05 = 5%).</summary>
        public float upgradeMinGain = 0.05f;

        /// <summary>
        /// Tên Custom Network khai trên MAX dashboard — MAX trả đúng tên này ở adInfo.NetworkName khi ad
        /// đến từ DFPoolAdapter. Dùng để phân biệt ad waterfall (reset được) với ad AppLovin (không
        /// reset được). Để rỗng = chỉ loại các network có chữ "applovin".
        /// </summary>
        public string customNetworkName = "DFPoolNetwork";

        /// <summary>Bật nếu tài khoản AdMob trả extras ad_value theo MICROS.</summary>
        public bool adValueInMicros;

        public bool HasAnyFormat =>
            (interstitial != null && interstitial.IsValid) || (rewarded != null && rewarded.IsValid);
    }

    public class DFPoolRemoteConfig : IFalconConfig
    {
        public string moMulDfPoolConfig;
    }

    public class DFPoolRemoteConfigCms : IFalconConfigCms
    {
        public string moMulDfPoolConfig;
    }

    /// <summary>
    /// Đọc config: CMS trước (đã khai key — kể cả rỗng — thì CMS thắng), CMS không khai mới tới
    /// data4game. Chuỗi rỗng = TẮT module.
    /// </summary>
    public static class DFPoolConfigReader
    {
        public const string KEY = "moMulDfPoolConfig";

        /// <summary>Nguồn config đã chốt chưa (cùng luật "CMS trước" với tầng AdMob / FSN).</summary>
        public static bool IsSettled => AdsConfigGate.IsCmsFirstReady(AdsConfigGate.CmsHasKey(KEY));

        public static string ReadJson(out string source)
        {
            try
            {
                if (AdsConfigGate.CmsHasKey(KEY))
                {
                    source = "cms";
                    return CmsConfig.Config<DFPoolRemoteConfigCms>()?.moMulDfPoolConfig;
                }

                source = "data4game";
                return GameConfig.Config<DFPoolRemoteConfig>()?.moMulDfPoolConfig;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                source = "error";
                return null;
            }
        }

        /// <summary>null = tắt (rỗng, JSON hỏng, hoặc không format nào hợp lệ).</summary>
        public static DFPoolConfigData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var data = JsonUtility.FromJson<DFPoolConfigData>(json);
                if (data == null || !data.HasAnyFormat) return null;
                if (data.ttlMinutes <= 0f) data.ttlMinutes = 55f;
                if (data.minBindSeconds < 0f) data.minBindSeconds = 0f;
                if (data.poolCap < 1) data.poolCap = 1;
                if (data.upgradeMinGain < 0f) data.upgradeMinGain = 0f;
                return data;
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError($"[DFPool] JSON '{KEY}' hỏng: {e.Message}");
#endif
                return null;
            }
        }
    }
}
