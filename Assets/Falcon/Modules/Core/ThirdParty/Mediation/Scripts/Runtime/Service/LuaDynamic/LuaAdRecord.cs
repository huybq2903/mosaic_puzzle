/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-02
 */

using System;
using Falcon.Helpers.Devkit;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime.LuaDynamic
{
    /// <summary>Vòng đời load của một ad unit do Lua điều khiển.</summary>
    public enum LuaAdStatus
    {
        /// <summary>Không có ad, không có request nào.</summary>
        Idle,

        /// <summary>Lua đã gọi Load..., đang chờ hết delayTime (hoặc chờ MAX init / ad cũ đóng).</summary>
        Waiting,

        /// <summary>Request đang bay tới mạng quảng cáo.</summary>
        Loading,

        /// <summary>Có ad sẵn sàng chiếu.</summary>
        Ready
    }

    /// <summary>
    /// Một dòng của <c>context.listInterstitialAd[key]</c> / <c>context.listRewardedAd[key]</c>, xem <see cref="Key"/>.
    /// C# là chủ của dữ liệu này: trước MỖI lần gọi vào Lua, toàn bộ record được chép sang bảng Lua (xem
    /// <see cref="LuaAdContext"/>). Lua sửa bảng đó cũng không ảnh hưởng gì ngược lại C#.
    /// </summary>
    public sealed class LuaAdRecord
    {
        public const string MEDIATION_MAX = "Max";
        public const string MEDIATION_ADMOB = "Admob";
        public const string TYPE_INTERSTITIAL = "Interstitial";
        public const string TYPE_REWARD = "Reward";

        /// <summary>Nối adUnitId và linkedAdUnitId thành key của bản load AdMob gắn với một id MAX.</summary>
        public const string KEY_SEPARATOR = "@";

        /// <summary>Format thật của ad unit: <see cref="AdType.Interstitial"/> hoặc <see cref="AdType.Reward"/>.</summary>
        public AdType format;

        public string mediation;
        public string adUnitId;

        /// <summary>
        /// Chỉ AdMob: id MAX mà bản load này gắn vào (tham số thứ 4 của <c>context.Admob.Load...</c>). Cùng một
        /// adUnitId AdMob, mỗi id MAX là một bản load riêng (trạng thái riêng, ad riêng). null = bản load thường.
        /// </summary>
        public string linkedAdUnitId;

        /// <summary>
        /// Key của record trong danh sách và trong <c>context.list...</c>: <c>adUnitId</c>, hoặc
        /// <c>adUnitId@linkedAdUnitId</c> khi có <see cref="linkedAdUnitId"/>.
        /// </summary>
        public string Key => MakeKey(adUnitId, linkedAdUnitId);

        /// <summary>
        /// Loại ad unit do server gán trong <c>sc_ad_bid_floor</c>: "default_1", "default_2", "h1", "h2", ...
        /// null = id không có trong danh sách bid floor (script tự xin). Bản load AdMob gắn id MAX lấy theo
        /// record gốc cùng adUnitId.
        /// </summary>
        public string adUnitIdType;

        /// <summary>Hệ số floor của id trong <c>sc_ad_bid_floor</c>. 0 = id default / không có.</summary>
        public double multiplier;

        /// <summary>timeConfig của id trong <c>sc_ad_bid_floor</c> (vd "10,15,20"), null = không có.</summary>
        public string timeConfig;

        /// <summary>Record được tạo / cập nhật từ danh sách <c>sc_ad_bid_floor</c>.</summary>
        internal bool fromBidFloor;

        public static string MakeKey(string adUnitId, string linkedAdUnitId)
            => string.IsNullOrEmpty(linkedAdUnitId) ? adUnitId : adUnitId + KEY_SEPARATOR + linkedAdUnitId;

        /// <summary>Giá sàn eCPM (USD / 1000 impression) của lần load gần nhất. 0 = không set sàn.</summary>
        public double floor;

        /// <summary>eCPM (USD / 1000 impression) của ad đang giữ. 0 = chưa có / không biết giá.</summary>
        public double eCpm;

        /// <summary>Mốc bắt đầu request gần nhất (UTC). null = chưa load lần nào từ lần show trước.</summary>
        public DateTime? startLoadUtc;

        public string adAlgorithm;

        /// <summary>Thời gian (giây) của request gần nhất, từ lúc gửi tới lúc có kết quả.</summary>
        public double loadingTime;

        /// <summary>
        /// Số lần load lỗi liên tiếp. Về 0 khi load được ad (OnAdLoaded) và khi show (ResetAfterShow).
        /// KHÔNG về 0 khi script đổi base — bộ đếm của thuật toán nằm trong Lua.
        /// </summary>
        public int numRetry;

        public string networkName;
        public string lastError;

        public LuaAdStatus status = LuaAdStatus.Idle;

        /// <summary>Ad của unit này đang trên màn hình. Load mới phải chờ tới lúc ad đó đóng.</summary>
        public bool isShowing;

        // request đang chờ (status = Waiting): chạy lúc waitFromUtc + delaySeconds. waitFromUtc được dời tới lúc
        // MAX init xong / lúc ad của unit này đóng nếu Lua xin load trước các mốc đó.
        internal DateTime waitFromUtc;
        internal double delaySeconds;
        internal double pendingFloor;

        // số thứ tự lần xin load gần nhất — để biết lịch nào của script cũ, lịch nào của script mới
        internal long requestSeq;

        internal DateTime DueUtc => waitFromUtc.AddSeconds(delaySeconds);

        public string adUnitType => format == AdType.Reward ? TYPE_REWARD : TYPE_INTERSTITIAL;

        /// <summary>Chép thông tin bid floor (adUnitIdType, multiplier, timeConfig) từ record gốc.</summary>
        internal void CopyBidFloorInfo(LuaAdRecord source)
        {
            adUnitIdType = source.adUnitIdType;
            multiplier = source.multiplier;
            timeConfig = source.timeConfig;
        }

        /// <summary>Doanh thu USD của MỘT impression.</summary>
        public double Revenue => eCpm / 1000d;

        public bool IsMax => mediation == MEDIATION_MAX;

        /// <summary>
        /// Sau khi ad đã lên màn hình: floor, eCpm, startLoadTime, loadingTime, numRetry về 0 (đúng như spec).
        /// </summary>
        internal void ResetAfterShow()
        {
            floor = 0d;
            eCpm = 0d;
            startLoadUtc = null;
            loadingTime = 0d;
            numRetry = 0;
            networkName = null;
            lastError = null;
        }
    }
}
