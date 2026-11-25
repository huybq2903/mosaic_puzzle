using System;

namespace Falcon.Admob
{
    /// <summary>
    /// Tham số HÀNH VI của module. KHÔNG còn ad unit id nào ở đây: cả bước B (mức default) lẫn bước C
    /// (thang giá sàn) đều lấy từ remote config, xem <see cref="AdmobRemoteConfigStore"/>.
    ///
    /// Mọi giá trị đều có mặc định hợp lý nên bình thường không phải đụng tới; muốn đổi thì dựng object
    /// này bằng code rồi truyền vào <see cref="AdmobMediation.Initialize"/>.
    /// </summary>
    [Serializable]
    public class AdmobMediationConfig
    {
        /// <summary>Bật log chi tiết của toàn bộ module.</summary>
        public bool enableLog;

        /// <summary>Tắt = module không load gì và không tham gia so giá.</summary>
        public bool enableBidding = true;

        /// <summary>
        /// Bật nếu tài khoản trả extras "ad_value_string" theo MICROS thay vì theo đơn vị tiền tệ.
        /// Xem log "extras[...]" trên máy thật để xác định.
        /// </summary>
        public bool adValueInMicros;

        /// <summary>
        /// Bật = GMA marshal mọi callback về main thread. TẮT nếu project tự marshal (mặc định GMA bắn
        /// callback trên background thread của Android).
        /// </summary>
        public bool raiseAdEventsOnUnityMainThread;

        /// <summary>
        /// Publisher id dùng cho multi-platform mediation reporting. Để trống = tự suy ra từ ad unit id
        /// bước B trong remote config (phần trước dấu '/').
        /// </summary>
        public string publisherId;

        /// <summary>
        /// Publisher id để khai báo auction/impression với AdMob. Đọc LẠI mỗi lần gọi vì ad unit id đến
        /// từ remote config và có thể đổi giữa phiên. Trả chuỗi rỗng nếu chưa suy được.
        /// </summary>
        public string ResolvePublisherId()
        {
            return !string.IsNullOrEmpty(publisherId) ? publisherId : AdmobRemoteConfigStore.PublisherId;
        }

        /// <summary>Bản sao nông — module giữ config riêng, người gọi sửa tiếp cũng không ảnh hưởng.</summary>
        public AdmobMediationConfig Clone() => (AdmobMediationConfig)MemberwiseClone();
    }
}
