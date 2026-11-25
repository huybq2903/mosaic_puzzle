/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-12
 */

using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Luật của game đã chặn một cơ hội chiếu ad — bộ từ ĐÓNG, ký với loader 12/11
    /// (<c>Request-Loader-AdShowSkipped.md</c> mục 1). Giá trị wire là snake_case qua EnumMember,
    /// dev không gõ chuỗi tay.
    /// <br/>Mỗi dòng MỘT lý do: luật ĐẦU TIÊN chặn, theo thứ tự game xét.
    /// <br/>User có No Ads: GỬI với <see cref="NoAds"/> (sửa hợp đồng 13/11,
    /// <c>Request-Loader-AdShowSkipped-NoAds.md</c>) — để đo gói No Ads lấy đi bao nhiêu cơ hội / tiền.
    /// <see cref="NoAds"/> đứng ĐẦU thứ tự xét: user No Ads thì mọi cơ hội ghi lý do này.
    /// <br/>Thêm giá trị: ĐẶT CUỐI danh sách và báo loader trước khi ship.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AdSkipReason
    {
        /// <summary>Chưa đủ thời gian kể từ lần chiếu trước.</summary>
        [EnumMember(Value = "cooldown")] Cooldown,

        /// <summary>Chưa đủ số màn kể từ lần chiếu trước.</summary>
        [EnumMember(Value = "level_interval")] LevelInterval,

        /// <summary>Chưa tới level mở quảng cáo.</summary>
        [EnumMember(Value = "level_gate")] LevelGate,

        /// <summary>Giai đoạn bảo vệ user mới (theo giờ/ngày cài, thời gian chơi, số phiên).</summary>
        [EnumMember(Value = "new_user_grace")] NewUserGrace,

        /// <summary>Vừa mua, đang được miễn.</summary>
        [EnumMember(Value = "post_purchase_grace")] PostPurchaseGrace,

        /// <summary>Vừa xem quảng cáo khác (vd vừa xem rewarded), đang được miễn.</summary>
        [EnumMember(Value = "post_ad_grace")] PostAdGrace,

        /// <summary>Đã chạm trần trong ngày.</summary>
        [EnumMember(Value = "daily_cap")] DailyCap,

        /// <summary>Đã chạm trần trong phiên.</summary>
        [EnumMember(Value = "session_cap")] SessionCap,

        /// <summary>Luật theo nhóm user chặn hẳn (campaign, đã trả tiền…) — nhóm nào thì gắn nhãn người chơi.</summary>
        [EnumMember(Value = "segment_rule")] SegmentRule,

        /// <summary>Config tắt quảng cáo ở điểm chiếu này.</summary>
        [EnumMember(Value = "disabled")] Disabled,

        /// <summary>Không thuộc các mục trên — SDK cảnh báo dev, giá trị vẫn gửi.</summary>
        [EnumMember(Value = "other")] Other,

        /// <summary>
        /// User đang có quyền bỏ quảng cáo (mua gói / thuê bao còn hạn). ĐỨNG ĐẦU thứ tự xét — có quyền
        /// thì ghi lý do này, không ghi luật nào khác. Ký loader 13/11.
        /// </summary>
        [EnumMember(Value = "no_ads")] NoAds,

        /// <summary>Đã chạm trần "tối đa N lần trong một level". Ký loader 13/11.</summary>
        [EnumMember(Value = "level_cap")] LevelCap
    }
}
