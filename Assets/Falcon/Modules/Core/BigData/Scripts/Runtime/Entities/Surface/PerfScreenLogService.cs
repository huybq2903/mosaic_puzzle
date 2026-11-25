/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System.Collections.Generic;
using System.Linq;
using Falcon.Helpers.Devkit;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Màn hiện tại + dòng <c>f_sdk_perf_screen</c> (hợp đồng đợt 3 B1). Game báo màn qua <see cref="FSurfaceApi"/>; lớp
    /// QC toàn màn SDK tự đặt (<see cref="FullscreenAdPresence"/>). Đoạn đóng do đổi màn gửi ngay; đoạn đóng lúc xuống
    /// nền đi cùng batch lúc pause.
    /// </summary>
    public class PerfScreenLogService : MySingleton<PerfScreenLogService>, IAppPauseLogGenerator
    {
        private readonly PerfPackRepository _repository;
        private readonly LogScheduleService _logScheduleService;
        private int? _lastSentTargetFps;

        private readonly TurnLedgerService _turnLedger;
        private readonly BatteryLogService _batteryLogService;

        public PerfScreenLogService(PerfPackRepository repository, LogScheduleService logScheduleService,
            TurnLedgerService turnLedger, BatteryLogService batteryLogService)
        {
            _turnLedger = turnLedger;
            _batteryLogService = batteryLogService;
            _repository = repository;
            _logScheduleService = logScheduleService;
        }

        /// <summary>Trần fps lúc mở app — <c>AppOpenLogService</c> ghi lại để dòng màn chỉ gửi khi game ĐỔI trần.</summary>
        public void NoteTargetFpsAtOpen(int targetFps)
        {
            _lastSentTargetFps = targetFps;
        }

        public void Enter(string name, PopupKind? kind)
        {
            if (string.IsNullOrEmpty(name))
            {
                AnalyticLogger.Instance.Warning("Surface.Enter bỏ qua: tên màn trống.");
                return;
            }

            if (name == SurfaceState.AD)
                AnalyticLogger.Instance.Warning("Surface.Enter(\"ad\"): màn QC do SDK tự đặt — game không cần báo.");
            if (kind.HasValue && !name.StartsWith(SurfaceState.POPUP_PREFIX))
                AnalyticLogger.Instance.Warning(
                    $"Surface.Enter(\"{name}\", {kind}): popupKind chỉ có nghĩa với màn \"popup:<surface_id>\" — bỏ qua loại.");

            _repository.Enter(name, kind);
            _turnLedger.OnPopupChanged(_repository.CurrentPopupKind);
            Flush();
        }

        public void Exit()
        {
            _repository.Exit();
            _turnLedger.OnPopupChanged(_repository.CurrentPopupKind);
            Flush();
        }

        public IEnumerable<IDataLog> GetLogsOnAppPause()
        {
            _repository.CloseForBackground();
            return Drain().Cast<IDataLog>().ToList();
        }

        private void Flush()
        {
            foreach (var log in Drain()) _logScheduleService.Enqueue(log);
        }

        private List<FPerfScreenLog> Drain()
        {
            var segments = _repository.DrainClosed();
            var logs = new List<FPerfScreenLog>(segments.Count);
            if (segments.Count == 0) return logs;

            // Main thread (API màn / batch lúc pause) — đọc được API Unity
            var targetFps = Application.targetFrameRate;
            int? changed = _lastSentTargetFps.HasValue && _lastSentTargetFps.Value != targetFps ? targetFps : null;
            if (changed.HasValue) _lastSentTargetFps = targetFps;

            var battery = _batteryLogService.Last;
            foreach (var segment in segments)
            {
                logs.Add(FPerfScreenLog.From(segment, changed, battery));
                changed = null; // chỉ dòng đầu tiên sau lần đổi mang trần mới
            }

            return logs;
        }
    }

    /// <summary>
    /// Màn hiện tại — <c>FalconBigDataController.Surface</c> (hợp đồng đợt 3 B1 / B6). Giá trị đi lên <c>perf_screen</c>
    /// của gói hiệu năng, sinh dòng <c>f_sdk_perf_screen</c> mỗi lần rời màn, và chia khối popup 🟡 của sổ lượt chơi.
    /// <code>
    /// FalconBigDataController.Surface.Enter("home");
    /// FalconBigDataController.Surface.Enter("popup:" + FUiSurface.ReviveOffer); // tự xếp loại offer
    /// FalconBigDataController.Surface.Enter("popup:how_to_play", PopupKind.Tutorial);
    /// FalconBigDataController.Surface.Exit(); // đóng popup trên cùng
    /// </code>
    /// Tên màn nền: <c>loading</c> · <c>home</c> · <c>level</c> · <c>shop</c>… (game đặt). Popup dùng lại <c>surface_id</c> của
    /// cụm Ui. Màn <c>ad</c> SDK tự đặt khi QC toàn màn đang chiếu.
    /// </summary>
    public class FSurfaceApi
    {
        private readonly PerfScreenLogService _service;

        internal FSurfaceApi(PerfScreenLogService service)
        {
            _service = service;
        }

        /// <summary>
        /// Vào một màn. <c>"popup:…"</c> đẩy popup lên trên màn đang mở; tên khác THAY màn nền (đóng luôn mọi popup).
        /// </summary>
        /// <param name="name">Tên màn: <c>home</c>, <c>level</c>, <c>shop</c>… hoặc <c>popup:&lt;surface_id&gt;</c>.</param>
        /// <param name="popupKind">
        /// Chỉ cho popup: <see cref="PopupKind.Offer"/> (offer IAP — hồi sinh / booster SDK tự xếp),
        /// <see cref="PopupKind.Tutorial"/> (hướng dẫn — phải khai), bỏ trống = <see cref="PopupKind.Other"/>.
        /// </param>
        public void Enter(string name, PopupKind? popupKind = null) => _service.Enter(name, popupKind);

        /// <summary>Rời màn trên cùng: đóng popup trên cùng; không còn popup thì rời màn nền (màn thành không biết).</summary>
        public void Exit() => _service.Exit();
    }
}
