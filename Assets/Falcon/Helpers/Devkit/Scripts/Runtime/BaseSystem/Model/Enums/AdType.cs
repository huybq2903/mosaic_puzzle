/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-12
 */

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
// ReSharper disable once CheckNamespace
namespace Falcon.Helpers.Devkit
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AdType
    {
        Banner,
        Interstitial,
        Reward,
        AppOpen,
        Native,
        CollapsibleBanner,
        MREC

        // ĐÃ CÂN NHẮC VÀ BỎ (19/10): `RewardedInterstitial` — interstitial tự nhảy ra nhưng có màn
        // hình hỏi trước và có quà (AdMob/AppLovin đều có format này). Thêm rồi lại bỏ vì đội
        // Mediation xác nhận không dùng và không nắm format này, mà một giá trị không provider nào
        // phát được chỉ làm phình từ vựng trên wire — loader phải học một nhãn không bao giờ xuất
        // hiện. Lúc nào có người bật thật thì thêm lại (ĐẶT CUỐI danh sách, đừng chèn giữa: chèn
        // giữa là xê dịch giá trị int của các entry cũ) và báo loader trước khi ship.
    }
}