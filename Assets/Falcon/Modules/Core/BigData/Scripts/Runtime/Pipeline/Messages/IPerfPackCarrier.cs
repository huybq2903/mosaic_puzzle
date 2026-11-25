/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Đánh dấu log mang GÓI HIỆU NĂNG (hợp đồng đợt 3 B1(a), ký 23/11): <c>perf_fps_avg_5s</c>, <c>perf_frame_worst_ms_5s</c>,
    /// <c>perf_frame_drops_since_last</c>, <c>perf_mem_warnings_since_last</c>, <c>perf_screen</c>.
    /// <br/>Phạm vi đã chốt: level / QC / cụm Ui / offer / IAP / funnel / live-ops. KHÔNG gắn lên log tài nguyên (dòng dày
    /// nhất) và không lên bản tin tổng hợp (<c>IsSpanRecord</c> — SDK tự bỏ qua dù lớp có đánh dấu).
    /// </summary>
    public interface IPerfPackCarrier
    {
    }

    /// <summary>
    /// Đánh dấu log chỉ mang <c>perf_screen</c> (không cả gói) — vd <c>f_sdk_speed_action</c>, <c>f_sdk_app_issue</c>.
    /// </summary>
    public interface IPerfScreenCarrier
    {
    }
}
