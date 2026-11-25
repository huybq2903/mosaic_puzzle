/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2026-10-06
 */

using System;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Điểm nghe cho module NGOÀI muốn nhận cùng dữ liệu với pipeline BigData (vd Databuckets) mà
    /// không bắt BigData phụ thuộc ngược vào module đó.
    /// <br/>Handler chạy đồng bộ trên thread vừa gọi; handler nào ném exception thì bị nuốt + cảnh
    /// báo — một bên nghe hỏng không được làm chết đường log của Falcon.
    /// </summary>
    public static class FBigDataHooks
    {
        /// <summary>
        /// Log vừa được decor xong và đưa vào hàng đợi gửi (Enqueue / EnqueueAll / SendNow).
        /// Lúc này mọi field do decorator đóng (typeCount, adLtv, adViewId…) đã có mặt.
        /// </summary>
        public static event Action<IDataLog> LogEnqueued;

        /// <summary>
        /// Kết quả MỘT lần load của MỘT ad unit (<see cref="FAdApi.OnLoadResult"/>) — bản per-attempt,
        /// TRƯỚC khi SDK gộp cụm thành <c>f_sdk_ad_load_stats</c>.
        /// </summary>
        public static event Action<AdLoadResultParam> AdLoadResult;

        /// <summary>Gọi hiển thị ad thất bại (<see cref="FAdApi.OnShowFailed"/>) — kèm ad đang định chiếu.</summary>
        public static event Action<AdShowFailedInfo> AdShowFailed;

        internal static void RaiseLogEnqueued(IDataLog log) => Raise(LogEnqueued, log);

        internal static void RaiseAdLoadResult(AdLoadResultParam param) => Raise(AdLoadResult, param);

        internal static void RaiseAdShowFailed(AdShowFailedInfo info) => Raise(AdShowFailed, info);

        private static void Raise<T>(Action<T> handler, T arg)
        {
            if (handler == null || arg == null) return;
            foreach (var invocation in handler.GetInvocationList())
                try
                {
                    ((Action<T>)invocation)(arg);
                }
                catch (Exception e)
                {
                    Warn(typeof(T).Name, e);
                }
        }

        private static void Warn(string hook, Exception e)
        {
            AnalyticLogger.Instance.Warning($"FBigDataHooks({hook}): handler lỗi, bỏ qua — {e}");
        }
    }

    /// <summary>
    /// Một lần gọi hiển thị hỏng — ad đã load sẵn nhưng mediation báo không chiếu được. Các field
    /// ngoài <see cref="type"/>/<see cref="errorCode"/> chỉ cho module ngoài (Databuckets), KHÔNG lên BigData.
    /// </summary>
    public sealed class AdShowFailedInfo
    {
        public AdType type;
        public string errorCode;
        public string adUnitId;
        public string networkName;

        /// <summary>Giá sàn eCPM (USD/1000 impression) đã xin cho ad này; null = không biết.</summary>
        public double? ecpmFloor;

        /// <summary>eCPM ước tính lúc load của ad này; null = mediation không trả giá.</summary>
        public double? estimatedEcpm;
    }
}
