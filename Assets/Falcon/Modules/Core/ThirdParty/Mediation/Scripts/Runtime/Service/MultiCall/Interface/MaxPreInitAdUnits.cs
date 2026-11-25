/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-19
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Mediation.Runtime
{
    /// <summary>
    /// Ad unit MAX của module NGOÀI (vd slot DFPool) phải đi đủ 3 bước như id của MultiCall / Lua, thiếu
    /// bước nào cũng có thể crash app:
    ///  1. nằm trong <c>disable_b2b_ad_unit_ids</c> (set TRƯỚC <c>MaxSdk.InitializeSdk</c>);
    ///  2. nằm trong mảng ad unit truyền cho <c>MaxSdk.InitializeSdk</c>;
    ///  3. được set <c>disable_auto_retries</c> sau khi MAX init (trước lượt load đầu).
    ///
    /// Module ngoài <see cref="Register"/> thật sớm (bootstrap). FalconMaxService gọi <see cref="Lock"/> ngay
    /// trước khi init MAX -> hỏi mọi provider, chốt danh sách cho cả phiên. Sau đó module ngoài CHỈ được
    /// load các id có trong danh sách đã chốt (<see cref="IsRegistered"/>) — id lạ xuất hiện sau init (config
    /// về muộn) thì bỏ, không load.
    ///
    /// Đặt ở đây (không nằm trong thư mục DFPool) để FalconMaxService không phụ thuộc DFPool: xoá DFPool thì
    /// không ai đăng ký -> danh sách rỗng -> mọi thứ chạy như cũ. Chỉ dùng trên main thread.
    /// </summary>
    public static class MaxPreInitAdUnits
    {
        public delegate void Collector(ICollection<string> interstitial, ICollection<string> rewarded);

        private sealed class Provider
        {
            public string owner;
            public Func<bool> isReady;
            public Collector collect;
        }

        private static readonly List<Provider> _providers = new List<Provider>();
        private static readonly HashSet<string> _interstitial = new HashSet<string>();
        private static readonly HashSet<string> _rewarded = new HashSet<string>();

        /// <summary>Danh sách đã chốt (MAX đã/đang init) — từ giờ không thêm được id nào nữa.</summary>
        public static bool IsLocked { get; private set; }

        /// <summary>
        /// Đã xong ĐỦ 3 bước: CallBeforeInit -> MaxSdk.InitializeSdk -> CallAfterInit (FalconMaxService bật trong
        /// callback init, ngay sau CallAfterInit). Module ngoài chỉ được gọi API theo ad unit của MAX khi true.
        /// KHÔNG dùng <c>MaxSdk.IsInitialized()</c>: native có thể báo true trước khi callback init của Unity chạy,
        /// tức là trước CallAfterInit.
        /// </summary>
        public static bool IsAfterInitDone { get; private set; }

        /// <summary>FalconMaxService gọi ngay sau CallAfterInit.</summary>
        public static void MarkAfterInitDone() => IsAfterInitDone = true;

        /// <summary>
        /// Đăng ký nguồn id. <paramref name="isReady"/> = nguồn đã đủ dữ liệu (vd remote config đã chốt) —
        /// MediationManager chờ nó (trong trần timeout init MAX) trước khi init. <paramref name="collect"/> được gọi
        /// MỘT lần lúc <see cref="Lock"/>.
        /// </summary>
        public static void Register(string owner, Func<bool> isReady, Collector collect)
        {
            if (collect == null) return;
            if (IsLocked)
            {
#if ENABLE_DEBUG_LOG
                Debug.LogWarning($"[MaxPreInitAdUnits] {owner} đăng ký SAU khi MAX init -> bỏ qua");
#endif
                return;
            }

            _providers.Add(new Provider { owner = owner, isReady = isReady, collect = collect });
        }

        /// <summary>Mọi provider đã sẵn sàng cung cấp id (không có provider = luôn đúng).</summary>
        public static bool IsReady
        {
            get
            {
                foreach (var p in _providers)
                {
                    try
                    {
                        if (p.isReady != null && !p.isReady()) return false;
                    }
                    catch (Exception e)
                    {
#if ENABLE_DEBUG_LOG
                        Debug.LogError(e);
#endif
                    }
                }

                return true;
            }
        }

        /// <summary>Hỏi mọi provider rồi chốt danh sách. Gọi lại nhiều lần vô hại (chỉ lần đầu có tác dụng).</summary>
        public static void Lock()
        {
            if (IsLocked) return;
            IsLocked = true;
            foreach (var p in _providers)
            {
                var inter = new List<string>();
                var rewarded = new List<string>();
                try
                {
                    p.collect(inter, rewarded);
                }
                catch (Exception e)
                {
#if ENABLE_DEBUG_LOG
                    Debug.LogError(e);
#endif
                    continue;
                }

                foreach (var id in inter.Where(Valid)) _interstitial.Add(id.Trim());
                foreach (var id in rewarded.Where(Valid)) _rewarded.Add(id.Trim());
#if ENABLE_DEBUG_LOG
                Debug.Log($"[MaxPreInitAdUnits] {p.owner}: inter=[{string.Join(",", inter)}] " +
                          $"rewarded=[{string.Join(",", rewarded)}]");
#endif
            }
        }

        public static IReadOnlyCollection<string> Interstitial => _interstitial;
        public static IReadOnlyCollection<string> Rewarded => _rewarded;
        public static IEnumerable<string> All => _interstitial.Union(_rewarded);
        public static bool HasAny => _interstitial.Count > 0 || _rewarded.Count > 0;

        /// <summary>Id này đã đi đủ 3 bước init cho đúng format chưa -> mới được load.</summary>
        public static bool IsRegistered(string adUnitId, bool rewarded)
        {
            if (string.IsNullOrWhiteSpace(adUnitId)) return false;
            return (rewarded ? _rewarded : _interstitial).Contains(adUnitId.Trim());
        }

        private static bool Valid(string id) => !string.IsNullOrWhiteSpace(id);
    }
}
