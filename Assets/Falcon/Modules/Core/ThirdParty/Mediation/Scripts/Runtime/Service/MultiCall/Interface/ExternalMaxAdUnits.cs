/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-27
 */

using System.Collections.Generic;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Ad unit MAX do một module NGOÀI tự lo trọn vòng đời (load -> show -> hidden), vd các slot của
    /// DFPool (thư mục DFPool).
    ///
    /// MaxSdkCallbacks là event TOÀN CỤC: mọi ad unit đều bắn vào FalconMaxService, MultiCall và bridge
    /// AdMob. Ad unit nằm trong đây thì các handler đó BỎ QUA — module sở hữu tự log, tự báo
    /// callback cho game. Không bỏ qua thì một lượt "show giả" (để MAX nhả slot) cũng bị log thành
    /// show fail, revenue bị đếm đôi, reward bắn nhầm callback của lượt show trước.
    ///
    /// Không ai đăng ký -> tập rỗng -> mọi handler chạy y như cũ. Xoá module sở hữu đi cũng vậy.
    /// Chỉ gọi trên Unity main thread (cùng thread với MaxSdkCallbacks).
    /// </summary>
    public static class ExternalMaxAdUnits
    {
        private static readonly HashSet<string> _ids = new HashSet<string>();

        public static void Claim(string adUnitId)
        {
            if (!string.IsNullOrEmpty(adUnitId)) _ids.Add(adUnitId);
        }

        public static void Release(string adUnitId)
        {
            if (!string.IsNullOrEmpty(adUnitId)) _ids.Remove(adUnitId);
        }

        /// <summary>true = ad unit của module ngoài, handler chung phải bỏ qua.</summary>
        public static bool IsExternal(string adUnitId)
            => _ids.Count > 0 && !string.IsNullOrEmpty(adUnitId) && _ids.Contains(adUnitId);

        //unit ngoài nhưng là ad MAX THẬT của mediation chính (vd unit MAX do Lua load) -> revenue của nó vẫn
        //là mốc giá bước 1 cho nguồn phụ (DFPool). Slot của DFPool thì KHÔNG (giá đó là giá AdMob mượn).
        private static readonly HashSet<string> _priceAnchors = new HashSet<string>();

        /// <summary>Đánh dấu unit ngoài này là ad MAX thật -> nguồn phụ được lấy revenue của nó làm mốc giá.</summary>
        public static void MarkPriceAnchor(string adUnitId)
        {
            if (!string.IsNullOrEmpty(adUnitId)) _priceAnchors.Add(adUnitId);
        }

        /// <summary>true = revenue của unit này dùng được làm mốc giá bước 1 (kể cả khi nó là unit ngoài).</summary>
        public static bool IsPriceAnchor(string adUnitId)
            => _priceAnchors.Count > 0 && !string.IsNullOrEmpty(adUnitId) && _priceAnchors.Contains(adUnitId);
    }
}
