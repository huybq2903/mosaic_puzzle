/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-16
 */

using System;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Module ngoài (vd DFPool) NHẬN QUYỀN load AdMob của một format -> tầng meta AdMob bỏ các id
    /// <c>mediation = admob</c> của <c>sc_ad_bid_floor</c> cho format đó, để AdMob không bị request gấp đôi.
    /// Format không ai nhận thì meta AdMob chạy như cũ.
    ///
    /// Đặt ở đây (không nằm trong thư mục DFPool) để meta AdMob không phụ thuộc DFPool: xoá thư mục DFPool
    /// thì không ai gọi <see cref="Expect"/> -> <see cref="IsDecided"/> luôn đúng, không format nào bị nhận.
    ///
    /// Luồng:
    ///  - Module ngoài gọi <see cref="Expect"/> thật sớm (lúc bootstrap) -> meta AdMob CHỜ trước khi init.
    ///  - Module ngoài đọc config xong gọi <see cref="Resolve"/> (kể cả khi tắt -> cả hai false).
    ///  - Đổi giữa phiên (config về muộn, module bật sau) -> <see cref="OnChanged"/> để meta AdMob đọc lại id.
    /// Chỉ dùng trên main thread.
    /// </summary>
    public static class AdmobTakeover
    {
        private static bool _expected;
        private static bool _resolved;
        private static bool _interstitial;
        private static bool _rewarded;

        /// <summary>Quyền load AdMob vừa đổi (sau lần <see cref="Resolve"/> đầu tiên hoặc khi giá trị đổi).</summary>
        public static event Action OnChanged;

        /// <summary>Có module ngoài sẽ quyết định -> meta AdMob phải chờ <see cref="IsDecided"/>.</summary>
        public static void Expect() => _expected = true;

        /// <summary>Đã biết format nào bị nhận chưa. Không ai <see cref="Expect"/> = luôn đúng.</summary>
        public static bool IsDecided => !_expected || _resolved;

        /// <summary>Module ngoài báo nó đang load AdMob cho format nào.</summary>
        public static void Resolve(bool interstitial, bool rewarded)
        {
            var changed = !_resolved || interstitial != _interstitial || rewarded != _rewarded;
            _resolved = true;
            _interstitial = interstitial;
            _rewarded = rewarded;
            if (!changed) return;

#if ENABLE_DEBUG_LOG
            Debug.Log($"[AdmobTakeover] module ngoài nhận AdMob: inter={interstitial} rewarded={rewarded}" +
                      " -> meta AdMob bỏ id mediation=admob của format bị nhận");
#endif
            try
            {
                OnChanged?.Invoke();
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
            }
        }

        /// <summary>Format này đã có module ngoài load AdMob -> meta AdMob KHÔNG load.</summary>
        public static bool IsClaimed(bool rewarded) => rewarded ? _rewarded : _interstitial;
    }
}
