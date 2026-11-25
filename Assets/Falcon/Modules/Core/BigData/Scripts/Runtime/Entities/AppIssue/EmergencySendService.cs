/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Đường gửi khẩn (hợp đồng đợt 3 S2 §0.4) cho dòng báo app sắp chết: <c>freeze_long</c>, <c>exception</c> có
    /// <c>is_fatal</c> và heartbeat đi kèm.
    /// <list type="number">
    /// <item>Dòng đã dựng sẵn từ bản chụp (<see cref="EmergencySnapshotService"/>); ở đây chỉ đóng dấu giờ gửi và chuỗi hoá.</item>
    /// <item><b>Ghi file khẩn trước</b> rồi mới gửi (gửi được thì xoá): app chết giữa lúc gửi vẫn còn file. Hợp đồng ghi
    /// "gửi hỏng thì ghi file" — ghi trước là rộng hơn; giá phải trả là gửi xong mà chết trước khi kịp xoá thì lần mở sau
    /// gửi lại cùng <c>uuid</c> (loader dedup trong 1 giờ, luật đọc §12 S2 lo phần còn lại).</item>
    /// <item>Gửi bằng <see cref="LogSendService.TrySendQuiet"/>: <b>cùng endpoint, cùng envelope</b> với hàng đợi thường.</item>
    /// <item>Lần mở sau (<see cref="Init"/>): file còn lại vào hàng đợi thường, nguyên <c>uuid</c>.</item>
    /// </list>
    /// Gọi được từ mọi luồng: không khoá chung với main thread, không logger, không API Unity (đường dẫn chụp ở Init).
    /// </summary>
    public class EmergencySendService : MySingleton<EmergencySendService>, IInit
    {
        public const string DIR = "falcon_bigdata_emergency";
        public static readonly TimeSpan SEND_TIMEOUT = TimeSpan.FromSeconds(5);

        private readonly LogScheduleService _logScheduleService;
        private readonly ITimeRepository _timeRepository;
        private volatile EmergencyFileStore _store;

        public EmergencySendService(LogScheduleService logScheduleService, ITimeRepository timeRepository)
        {
            _logScheduleService = logScheduleService;
            _timeRepository = timeRepository;
        }

        public Task Init(CancellationToken cancellationToken = default)
        {
            var store = new EmergencyFileStore(Path.Combine(Application.persistentDataPath, DIR));
            _store = store;
            foreach (var (path, wrappers) in store.ReadAll())
            {
                if (wrappers.Count > 0)
                {
                    AnalyticLogger.Instance.Info($"Gửi lại {wrappers.Count} dòng khẩn của lần chạy trước");
                    _logScheduleService.EnqueueAll(wrappers);
                }

                store.Delete(path);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gửi các dòng khẩn, chờ tối đa <paramref name="wait"/> (handler lỗi chết: tiến trình chết khi handler trả về,
        /// nên phải chờ đồng bộ). Hết giờ chờ mà sau đó gửi được thì file vẫn được xoá.
        /// </summary>
        /// <returns>true nếu server đã nhận trong lúc chờ.</returns>
        public bool Dispatch(EmergencySnapshot snapshot, IReadOnlyList<(string @event, Dictionary<string, object> row)> rows,
            TimeSpan wait)
        {
            if (snapshot == null || snapshot.SendDisabled || rows == null || rows.Count == 0) return false;
            try
            {
                var sentAt = _timeRepository.CurrentTimeMillis;
                var wrappers = new List<DataWrapper>(rows.Count);
                foreach (var (@event, row) in rows)
                    wrappers.Add(DataWrapper.Quiet(row.ToJson(), sentAt, @event, snapshot.PackageName, snapshot.Platform));

                var store = _store;
                var path = store?.Write(wrappers);
                // Task.Run: không bắt SynchronizationContext của main thread (handler lỗi chết có thể chạy trên main
                // thread — chờ đồng bộ ở đây mà continuation đòi main thread là treo).
                var send = Task.Run(() => LogSendService.TrySendQuiet(wrappers, SEND_TIMEOUT));
                send.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion && t.Result) store?.Delete(path);
                }, TaskScheduler.Default);
                return send.Wait(wait) && send.Result;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
