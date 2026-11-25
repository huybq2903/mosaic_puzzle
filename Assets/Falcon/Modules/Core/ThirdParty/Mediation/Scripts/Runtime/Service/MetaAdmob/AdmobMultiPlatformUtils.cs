using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Falcon.Admob
{
    /// <summary>
    /// Utility multi-platform mediation của Google, bản CHÉP RIÊNG cho module (namespace riêng để không
    /// đụng bản global <c>MultiPlatformUtils</c> nếu project đã có sẵn).
    ///
    /// Khác bản gốc đúng một điểm: chỉ log khi <see cref="LogEnabled"/> bật, để build release không spam.
    /// </summary>
    public static class AdmobMultiPlatformUtils
    {
        public const string AUCTION_CONTEXT_KEY = "auction_context";
        public const string USER_ACQUISITION_CHANNEL_KEY = "ua_channel";
        public const string DAYS_SINCE_INSTALL_KEY = "days_since_install";

        public const string AUCTION_CONTEXT_PARALLEL = "parallel";
        public const string AUCTION_CONTEXT_POST_BID_FIRST = "post_bid_first";
        public const string AUCTION_CONTEXT_POST_BID_SECOND = "post_bid_second";

        public static bool LogEnabled;

        public static class UserAcquisitionChannel
        {
            public const string GoogleAds = "google_ads";
            public const string Organic = "organic";
            public const string AppleSearchAds = "apple_search_ads";
            public const string Applovin = "applovin";
            public const string Unity = "unity";
            public const string Meta = "meta";
            public const string Mintegral = "mintegral";
            public const string Moloco = "moloco";
            public const string Liftoff = "liftoff";
            public const string Pangle = "pangle";

            public static string FromNetwork(string network)
            {
                if (string.IsNullOrEmpty(network)) return Organic;

                var lower = network.ToLowerInvariant();

                if (lower.Contains("google") || lower.Contains("adwords")) return GoogleAds;
                if (lower.Contains("meta") || lower.Contains("facebook")) return Meta;
                if (lower.Contains("applovin")) return Applovin;
                if (lower.Contains("organic")) return Organic;
                if (lower.Contains("apple")) return AppleSearchAds;
                if (lower.Contains("unity")) return Unity;
                if (lower.Contains("mintegral")) return Mintegral;
                if (lower.Contains("moloco")) return Moloco;
                if (lower.Contains("liftoff")) return Liftoff;
                if (lower.Contains("pangle")) return Pangle;

                // Network không khớp giá trị định sẵn -> giữ nguyên.
                return network;
            }
        }

        /// <summary>Khai báo với AdMob giá cao nhất đến từ nguồn KHÔNG phải AdMob của một response.</summary>
        public static void RecordAuctionEvent(string publisherId, string responseId, string highestOtherBid)
        {
            if (string.IsNullOrEmpty(publisherId) || string.IsNullOrEmpty(responseId)) return;

            var url = "https://pagead2.googlesyndication.com/pagead/gen_204/"
                      + $"?id=ampm&type=auction&response_id={UnityWebRequest.EscapeURL(responseId)}"
                      + $"&wp={UnityWebRequest.EscapeURL(publisherId)}"
                      + $"&hob={UnityWebRequest.EscapeURL(highestOtherBid)}";

            SendRequest(url);
        }

        /// <summary>
        /// Khai báo giá trị impression thực tế cho AdMob, kèm MỌI response AdMob tham gia lượt show đó:
        ///  - AdMob thắng -> response của ad vừa chiếu (đứng đầu) + response của các ad AdMob khác đã thua;
        ///  - mediation ngoài thắng -> response của mọi ad AdMob đang giữ (đều đã thua), piv là giá của
        ///    bên thắng.
        /// Nhiều response nối bằng dấu phẩy trong <c>response_id</c>.
        /// </summary>
        public static void RecordImpressionEvent(string publisherId, IReadOnlyList<string> responseIds,
            string adValue, string precision)
        {
            var responseIdParam = JoinResponseIds(responseIds);
            if (string.IsNullOrEmpty(publisherId) || string.IsNullOrEmpty(responseIdParam)) return;

            var url = "https://pagead2.googlesyndication.com/pagead/gen_204/"
                      + $"?id=ampm&type=impression&response_id={responseIdParam}"
                      + $"&wp={UnityWebRequest.EscapeURL(publisherId)}"
                      + $"&piv={UnityWebRequest.EscapeURL(adValue)}"
                      + $"&pip={UnityWebRequest.EscapeURL(precision)}";

            SendRequest(url);
        }

        // Escape TỪNG id rồi mới nối: escape cả chuỗi đã nối thì dấu phẩy phân cách cũng bị mã hoá thành
        // %2C. Id rỗng bị bỏ; không còn id nào -> null.
        private static string JoinResponseIds(IReadOnlyList<string> responseIds)
        {
            if (responseIds == null) return null;

            var sb = new StringBuilder();
            foreach (var responseId in responseIds)
            {
                if (string.IsNullOrEmpty(responseId)) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(UnityWebRequest.EscapeURL(responseId));
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        private static void SendRequest(string url)
        {
            var uwr = UnityWebRequest.Get(url);
            var op = uwr.SendWebRequest();
            op.completed += _ =>
            {
#if ENABLE_DEBUG_LOG
                if (LogEnabled)
                {
                    if (uwr.result == UnityWebRequest.Result.Success)
                        Debug.Log($"[AdmobMediation] multi-platform request -> {uwr.responseCode}");
                    else
                        Debug.LogWarning($"[AdmobMediation] multi-platform request lỗi: {uwr.error}");
                }
#endif

                uwr.Dispose();
            };
        }
    }
}
