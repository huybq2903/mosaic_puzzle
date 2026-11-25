/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-18
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Một interstitial đã load sẵn của luồng personalization — phần lõi Mediation cần để chiếu / log.
    /// Module personalization kế thừa để mang thêm dữ liệu riêng.
    /// </summary>
    public class InterstitialAdCandidate
    {
        public string adUnitId;
        public string networkName;

        /// <summary>Giá sàn eCPM (USD/1000) đã xin cho lần load ra ad này.</summary>
        public double ecpmFloor;

        /// <summary>eCPM ước tính lúc load; null = mediation không trả giá.</summary>
        public double? estimatedEcpm;
    }

    /// <summary>
    /// Điểm cắm của luồng load interstitial riêng (vd Floor Personalization / Databuckets Rule).
    /// Lõi Mediation (FalconMaxService, MultiCall, Lua) CHỈ nói chuyện qua interface này, nên thư mục
    /// module cắm vào xoá đi được mà không lỗi compile — khi đó <see cref="InterstitialPersonalization.Current"/>
    /// là bản rỗng, interstitial chạy logic mặc định.
    /// </summary>
    public interface IInterstitialPersonalization
    {
        /// <summary>
        /// Remote config của module đã chốt nguồn chưa — MediationManager chờ cái này (trong trần timeout
        /// chung) trước khi init MAX, vì chế độ load được chốt ở <c>FalconMaxService.Init</c>.
        /// </summary>
        bool IsConfigReady { get; }

        /// <summary>
        /// Gọi ĐÚNG MỘT LẦN lúc <c>FalconMaxService.Init</c> chốt chế độ load, chỉ khi phiên KHÔNG chạy Lua.
        /// Đọc remote config; true = phiên này chạy chế độ personalization cho interstitial (ưu tiên trên
        /// MultiCall / unit đơn). Không gọi (phiên Lua) thì module tắt cả phiên.
        /// </summary>
        bool TryActivate();

        bool IsEnabled { get; }

        /// <summary>Ad unit dành riêng cho luồng này — cần có trong tham số init của MAX.</summary>
        IReadOnlyList<string> AdUnitIds { get; }

        bool IsOwnedUnit(string adUnitId);

        /// <summary>Logic load mặc định (MultiCall / unit đơn) phải đứng yên.</summary>
        bool BlocksDefaultLoading { get; }

        /// <summary>
        /// <c>ecpmFloorType</c> gắn lên dòng load interstitial MẶC ĐỊNH (MultiCall / unit đơn) gửi đi lúc này;
        /// null = không gắn. Vd Floor Personalization: <c>rule_error</c> / <c>rule_timeout</c> khi opportunity
        /// hiện tại rơi về load mặc định vì gọi Rule hỏng. Bên load đọc LÚC GỬI request rồi giữ lại cho dòng
        /// kết quả — đọc lúc kết quả về là dán nhãn của opportunity sau lên request của opportunity trước.
        /// </summary>
        string DefaultLoadFloorType { get; }

        /// <param name="resumeDefaultLoading">Kích lại logic load mặc định khi luồng nhả chặn.</param>
        void OnMaxInitialized(Action resumeDefaultLoading);

        /// <summary>
        /// Một interstitial (bất kỳ nguồn nào) vừa lên hình. Lõi gọi qua
        /// <see cref="InterstitialPersonalization.OnInterstitialDisplayed"/> — đã tăng
        /// <see cref="AdSessionCounter"/> trước khi tới đây.
        /// </summary>
        void OnInterstitialDisplayed(string adUnitId, double? actualEcpm);

        /// <summary>Ad của unit riêng đã rời màn hình / chiếu hỏng — unit rảnh để load lại.</summary>
        void OnOwnedAdFinished(string adUnitId);

        bool HasReadyCandidate();

        InterstitialAdCandidate PeekBestCandidate();

        /// <summary>Lấy candidate tốt nhất ra để chiếu; null = để logic mặc định chiếu.</summary>
        InterstitialAdCandidate TakeBestCandidate();

        /// <summary>Candidate đã đem chiếu trên unit này (impression / show fail đọc lại).</summary>
        InterstitialAdCandidate ShownCandidate(string adUnitId);

        /// <summary>Game muốn chiếu mà không có ad nào.</summary>
        void OnShowMiss();
    }

    /// <summary>
    /// Sổ đăng ký <see cref="IInterstitialPersonalization"/>. Module cắm vào gọi <see cref="Register"/>
    /// trong <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c>; chưa ai đăng ký thì
    /// <see cref="Current"/> là bản rỗng (tắt).
    /// </summary>
    public static class InterstitialPersonalization
    {
        private static Func<IInterstitialPersonalization> _factory;
        private static IInterstitialPersonalization _current;

        /// <summary>Factory gọi lười ở lần đầu cần tới — lúc đó MediationManager / Resources đã sẵn sàng.</summary>
        public static void Register(Func<IInterstitialPersonalization> factory)
        {
            _factory = factory;
            _current = null;
        }

        public static IInterstitialPersonalization Current
        {
            get
            {
                if (_current != null) return _current;
                if (_factory == null) return NullInterstitialPersonalization.Instance; //không cache: cho phép đăng ký muộn

                try
                {
                    _current = _factory();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                return _current ??= NullInterstitialPersonalization.Instance;
            }
        }

        /// <summary>
        /// Cửa duy nhất báo "một interstitial vừa lên hình": đếm <see cref="AdSessionCounter"/> rồi mới
        /// chuyển cho module — module đọc số đã chiếu SAU khi cộng ad này.
        /// </summary>
        public static void OnInterstitialDisplayed(string adUnitId, double? actualEcpm)
        {
            AdSessionCounter.OnInterstitialShown();
            Current.OnInterstitialDisplayed(adUnitId, actualEcpm);
        }
    }

    internal sealed class NullInterstitialPersonalization : IInterstitialPersonalization
    {
        public static readonly NullInterstitialPersonalization Instance = new();

        public bool IsConfigReady => true;
        public bool TryActivate() => false;
        public bool IsEnabled => false;
        public IReadOnlyList<string> AdUnitIds => Array.Empty<string>();
        public bool IsOwnedUnit(string adUnitId) => false;
        public bool BlocksDefaultLoading => false;
        public string DefaultLoadFloorType => null;

        public void OnMaxInitialized(Action resumeDefaultLoading)
        {
        }

        public void OnInterstitialDisplayed(string adUnitId, double? actualEcpm)
        {
        }

        public void OnOwnedAdFinished(string adUnitId)
        {
        }

        public bool HasReadyCandidate() => false;
        public InterstitialAdCandidate PeekBestCandidate() => null;
        public InterstitialAdCandidate TakeBestCandidate() => null;
        public InterstitialAdCandidate ShownCandidate(string adUnitId) => null;

        public void OnShowMiss()
        {
        }
    }
}
