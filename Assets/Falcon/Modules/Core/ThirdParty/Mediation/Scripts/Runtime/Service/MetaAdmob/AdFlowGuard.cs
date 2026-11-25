namespace Falcon.Admob
{
    using UnityEngine;

    // === Chặn App Open Ad (AOA) hiện chồng khi user xem RV/Inter hoặc click-out 1 FSN native ===
    // "Phiên quảng cáo" = từ lúc game gọi Show (RV/Inter/Aoa) cho tới khi flow kết thúc HẲN (FSN native
    // theo sau đã đóng). Trong suốt phiên đó, mọi lần app mất/được focus (đóng ad, hoặc click-out
    // native sang store rồi quay lại) đều KHÔNG được coi là "user chủ động rời app" -> không bật AOA.
    //
    // Logic này trước nằm trong MaxAds, nhưng MaxAds bị bọc trong #if MAX_DEPENDENCIES_INSTALLED
    // (và iOS không bật symbol đó) nên tầng AdMob không gọi được. Tách ra đây, KHÔNG gate define,
    // để cả MAX lẫn AdMob dùng CHUNG một bộ đếm — hai bộ đếm riêng thì mạng này show fullscreen mà
    // mạng kia vẫn tưởng rảnh và bật AOA đè lên.
    //
    // File nằm trong thư mục module chỉ để export một lần là đủ; nó KHÔNG phụ thuộc gì ngoài
    // UnityEngine. Lưu ý chiều ngược lại: MaxAds (ngoài module) cũng gọi class này, nên gỡ module
    // khỏi project thì phải mang theo file này.
    public static class AdFlowGuard
    {
        private static int _adFlowDepth;                  // số phiên ad đang chạy (bình thường 0 hoặc 1)
        private static float _adFlowDepthSinceRealtime;   // thời điểm depth chuyển 0 -> >0 (để chặn kẹt cờ)
        private static float _lastAdEndRealtime = -999f;  // thời điểm phiên ad gần nhất kết thúc

        // Một phiên ad không thể kéo dài hơn mốc này -> nếu quá thì coi như cờ bị kẹt và bỏ chặn (an toàn doanh thu).
        private const float _MAX_AD_FLOW_SECONDS = 600f;

        // Đánh dấu bắt đầu/kết thúc 1 phiên quảng cáo do game chủ động mở.
        public static void BeginAdFlow()
        {
            if (_adFlowDepth == 0) _adFlowDepthSinceRealtime = Time.realtimeSinceStartup;
            _adFlowDepth++;
        }

        public static void EndAdFlow()
        {
            if (_adFlowDepth > 0) _adFlowDepth--;
            if (_adFlowDepth == 0) _lastAdEndRealtime = Time.realtimeSinceStartup;
        }

        // true nếu đang trong 1 phiên ad (và chưa quá lâu -> chống kẹt cờ), HOẶC vừa có phiên ad kết thúc
        // trong guardSeconds gần đây (đệm chống đảo thứ tự event OnApplicationPause(false) vs callback đóng ad).
        public static bool ShouldSuppressAppOpen(float guardSeconds = 2f)
        {
            if (_adFlowDepth > 0 && Time.realtimeSinceStartup - _adFlowDepthSinceRealtime < _MAX_AD_FLOW_SECONDS)
                return true;
            return Time.realtimeSinceStartup - _lastAdEndRealtime < guardSeconds;
        }
    }
}
