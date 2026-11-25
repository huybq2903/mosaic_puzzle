/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-22
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Falcon.Helpers.Devkit;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Nguồn ad PHỤ chạy song song với mediation chính, hiện tại là tầng AdMob meta
    /// (<c>Falcon.Admob</c>, thư mục MetaAdmob) — bước B (id default) + bước C (các mức floor bám theo
    /// revenue của MAX).
    ///
    /// Lúc show, <see cref="MultiCallProvider{T}"/> so giá ad của MAX với giá ở đây rồi mới quyết định
    /// chiếu bên nào. Đi qua interface thay vì gọi thẳng <c>AdmobMediation</c> để MultiCall KHÔNG phụ
    /// thuộc ngược vào module AdMob: xoá cả thư mục MetaAdmob thì <see cref="SecondaryAdSource.Current"/>
    /// đơn giản là null và mọi thứ quay về đúng luồng MAX cũ.
    ///
    /// Thang giá dùng CHUNG với MAX: doanh thu USD của ĐÚNG MỘT impression. Mọi giá trị &lt; 0 nghĩa là
    /// "không có ad" (MAX dùng -100, tầng AdMob dùng -1); 0 = có ad nhưng chưa đọc được giá, thắng
    /// "không có gì" nhưng thua mọi mức giá thật.
    /// </summary>
    public interface ISecondaryAdSource
    {
        /// <summary>Có ad nào của format này sẵn sàng chiếu không.</summary>
        bool IsReady(AdType type);

        /// <summary>Giá của ad đắt nhất đang giữ; &lt; 0 = không có ad nào.</summary>
        double BestRevenue(AdType type);

        /// <summary>Ad unit của ad sẽ được chiếu nếu nguồn này thắng giá. Chỉ để log.</summary>
        string BestAdUnitId(AdType type);

        /// <summary>Network thắng thầu của ad sẽ được chiếu. Chỉ để log.</summary>
        string BestNetwork(AdType type);

        /// <summary>
        /// Liệt kê MỌI ad đang giữ của format này kèm revenue, mỗi slot một dòng (đã thụt lề sẵn).
        /// Chỉ để log lúc so giá — muốn biết vì sao bên này thắng/thua thì đọc dòng này.
        /// </summary>
        string DescribeCache(AdType type);

        /// <summary>
        /// Chiếu ad đắt nhất của format này. Trả false nếu đúng lúc gọi không còn ad nào — người gọi tự
        /// fallback, KHÔNG có callback nào được bắn trong trường hợp đó.
        ///
        /// Bắn ĐÚNG MỘT trong hai callback <paramref name="onHidden"/> / <paramref name="onFail"/>.
        /// <paramref name="onReward"/> chỉ có nghĩa với rewarded.
        /// </summary>
        bool Show(AdType type, string where, Action onReward, Action onHidden, Action onFail,
            SecondaryAdPaid onPaid);
    }

    public delegate void SecondaryAdPaid(string mediation, string network, double revenue);

    /// <summary>
    /// Chỗ cắm <see cref="ISecondaryAdSource"/>. Có hai cửa:
    ///  - <see cref="Current"/>: nguồn chính — module AdMob tự gán trong
    ///    <c>AdmobMediationFalconBridge.Install()</c> (giữ nguyên như cũ).
    ///  - <see cref="Register"/>: nguồn THÊM chạy song song (vd DFPool). Không ai đăng ký = không có.
    ///
    /// Có nhiều nguồn thì mọi hàm ở đây gộp lại thành MỘT nguồn: giá = giá cao nhất trong các nguồn,
    /// show = show nguồn đang giữ giá cao nhất (hoà thì nguồn chính / nguồn đăng ký trước thắng).
    /// Không nguồn nào = mọi hàm trả về "không có ad" và luồng show chạy y như cũ.
    ///
    /// Mọi lời gọi đều bọc try/catch: nguồn phụ hỏng KHÔNG được phép làm chết lượt show của MAX.
    /// </summary>
    public static class SecondaryAdSource
    {
        /// <summary>Không có ad — cùng quy ước "&lt; 0 là không có" với eCpm của MultiCall.</summary>
        public const double NO_AD = -1d;

        /// <summary>Nguồn chính (tầng AdMob meta).</summary>
        public static ISecondaryAdSource Current;

        private static readonly List<ISecondaryAdSource> _extra = new List<ISecondaryAdSource>();

        public static bool HasSource => Current != null || _extra.Count > 0;

        /// <summary>Thêm một nguồn chạy song song với <see cref="Current"/>. Gọi lại nhiều lần vô hại.</summary>
        public static void Register(ISecondaryAdSource source)
        {
            if (source != null && !_extra.Contains(source)) _extra.Add(source);
        }

        public static void Unregister(ISecondaryAdSource source)
        {
            if (source != null) _extra.Remove(source);
        }

        //thứ tự = thứ tự ưu tiên khi hoà giá
        private static IEnumerable<ISecondaryAdSource> All()
        {
            if (Current != null) yield return Current;
            for (var i = 0; i < _extra.Count; i++)
                if (_extra[i] != null && _extra[i] != Current) yield return _extra[i];
        }

        private static double SafeRevenue(ISecondaryAdSource source, AdType type)
        {
            try
            {
                return source.BestRevenue(type);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return NO_AD;
            }
        }

        /// <summary>Nguồn đang giữ ad đắt nhất của format; null = không nguồn nào có ad.</summary>
        private static ISecondaryAdSource Best(AdType type)
        {
            ISecondaryAdSource best = null;
            var bestRevenue = NO_AD;
            foreach (var source in All())
            {
                var revenue = SafeRevenue(source, type);
                if (revenue < 0d) continue;
                //> chứ không >=: hoà thì nguồn đứng trước thắng
                if (best == null || revenue > bestRevenue)
                {
                    best = source;
                    bestRevenue = revenue;
                }
            }

            return best;
        }

        public static bool IsReady(AdType type)
        {
            foreach (var source in All())
            {
                try
                {
                    if (source.IsReady(type)) return true;
                }
                catch (Exception e)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError(e);
#endif
                }
            }

            return false;
        }

        public static double BestRevenue(AdType type)
        {
            var best = Best(type);
            return best == null ? NO_AD : SafeRevenue(best, type);
        }

        public static string BestAdUnitId(AdType type)
        {
            try
            {
                return Best(type)?.BestAdUnitId(type);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return null;
            }
        }

        public static string BestNetwork(AdType type)
        {
            try
            {
                return Best(type)?.BestNetwork(type);
            }
            catch (Exception e)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogError(e);
#endif
                return null;
            }
        }

        public static string DescribeCache(AdType type)
        {
            if (!HasSource) return "    (không có nguồn phụ — module AdMob chưa Install hoặc đã gỡ)";

            var sb = new StringBuilder();
            foreach (var source in All())
            {
                if (sb.Length > 0) sb.Append('\n');
                try
                {
                    if (_extra.Count > 0)
                        sb.Append("    <").Append(source.GetType().Name).Append("> best=")
                            .Append(SafeRevenue(source, type).ToString("0.######", CultureInfo.InvariantCulture))
                            .Append('\n');
                    sb.Append(source.DescribeCache(type));
                }
                catch (Exception e)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError(e);
#endif
                    sb.Append("    (lỗi khi đọc cache nguồn phụ)");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Chiếu ad đắt nhất trong mọi nguồn. Nguồn đắt nhất trả false (vừa hết ad) thì thử nguồn kế
        /// tiếp; trả false khi không nguồn nào chiếu được — KHÔNG callback nào được bắn trường hợp đó.
        /// </summary>
        public static bool Show(AdType type, string where, Action onReward, Action onHidden, Action onFail,
            SecondaryAdPaid onPaid)
        {
            var tried = new List<ISecondaryAdSource>();
            while (true)
            {
                ISecondaryAdSource best = null;
                var bestRevenue = NO_AD;
                foreach (var source in All())
                {
                    if (tried.Contains(source)) continue;
                    var revenue = SafeRevenue(source, type);
                    if (revenue < 0d) continue;
                    if (best == null || revenue > bestRevenue)
                    {
                        best = source;
                        bestRevenue = revenue;
                    }
                }

                if (best == null) return false;
                tried.Add(best);

                try
                {
                    if (best.Show(type, where, onReward, onHidden, onFail, onPaid)) return true;
                }
                catch (Exception e)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError(e);
#endif
                }
            }
        }
    }
}
