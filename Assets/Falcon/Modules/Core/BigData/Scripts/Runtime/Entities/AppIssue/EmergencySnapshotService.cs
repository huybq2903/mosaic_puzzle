/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-11-23
 */

using System;
using System.Collections.Generic;
using System.Threading;
using Falcon.Helpers.Devkit;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Main thread dựng bản chụp cho đường gửi khẩn (hợp đồng đợt 3 S2 "Bản chụp và heartbeat kèm sự cố"): heartbeat
    /// của lượt đang mở + mẫu <c>f_sdk_app_issue</c>, <b>đã trang trí</b> (field chung, gói hiệu năng, sổ B6) bằng
    /// <see cref="LogDecorService.DecorPreview"/> — không side effect. Thay tham chiếu một lần (<c>Volatile.Write</c>);
    /// luồng canh / handler lỗi chết chỉ đọc <see cref="Current"/>, không khoá.
    /// <br/>Cũng là chỗ gọi làm mới APS (<see cref="LevelHeartbeatService.TryRefresh"/>) — cùng nhịp khung, cùng điều kiện
    /// "app ở trước, không có QC toàn màn".
    /// </summary>
    public class EmergencySnapshotService : MySingleton<EmergencySnapshotService>
    {
        private readonly LevelHeartbeatService _heartbeatService;
        private readonly TurnLedgerService _turnLedger;
        private readonly LogDecorService _decorService;
        private readonly IFAppInfoRepository _appInfoRepository;
        private readonly AnalyticConfigService _configService;

        private EmergencySnapshot _current;
        private long? _builtAt;
        private string _builtTurn;
        private string _builtScreen;
        private int _builtLedgerVersion = -1;
        private int _builtParamsVersion = -1;
        private bool _warned;

        public EmergencySnapshotService(
            LevelHeartbeatService heartbeatService, TurnLedgerService turnLedger,
            LogDecorService decorService, IFAppInfoRepository appInfoRepository, AnalyticConfigService configService)
        {
            _heartbeatService = heartbeatService;
            _turnLedger = turnLedger;
            _decorService = decorService;
            _appInfoRepository = appInfoRepository;
            _configService = configService;
        }

        /// <summary>Bản chụp mới nhất (null trước lần dựng đầu). Đọc được từ mọi luồng.</summary>
        public EmergencySnapshot Current => Volatile.Read(ref _current);

        /// <summary>Main thread, mỗi khung lúc app ở trước.</summary>
        /// <param name="openTurnId">Lượt đang mở (caller đã đọc).</param>
        /// <param name="screen">Màn hiện tại (<c>ad</c> = QC toàn màn đang chiếu).</param>
        public void Tick(string openTurnId, string screen)
        {
            try
            {
                var refreshed = _heartbeatService.TryRefresh(screen != SurfaceState.AD);
                var now = MonotonicClock.ElapsedMillis;
                var ledgerVersion = _turnLedger.Version;
                var paramsVersion = _heartbeatService.Version;
                var contentChanged = ledgerVersion != _builtLedgerVersion || paramsVersion != _builtParamsVersion ||
                                     screen != _builtScreen;
                if (!SnapshotRebuildPolicy.Due(now, _builtAt, openTurnId != _builtTurn, refreshed, contentChanged)) return;

                _builtAt = now;
                _builtTurn = openTurnId;
                _builtScreen = screen;
                _builtLedgerVersion = ledgerVersion;
                _builtParamsVersion = paramsVersion;
                Volatile.Write(ref _current, Build(now, screen));
            }
            catch (Exception e)
            {
                // Dựng hỏng thì giữ bản cũ — không được làm gãy khung hình của game
                if (_warned) return;
                _warned = true;
                AnalyticLogger.Instance.Warning("Dựng bản chụp khẩn (S2) lỗi — giữ bản cũ: " + e);
            }
        }

        private EmergencySnapshot Build(long now, string screen)
        {
            string turnId = null;
            Dictionary<string, object> heartbeat = null;
            string runningKey = null;
            var speedRunning = false;
            var heartbeatEvent = "f_sdk_level_data";

            var param = _heartbeatService.BuildForSnapshot();
            if (param != null)
            {
                // Không qua ctor FLevelLog(param): ctor đó ghi log mỗi lần — ở đây dựng mỗi vài giây
                param.CorrectValues();
                var log = new FLevelLog { param = param };
                _decorService.DecorPreview(log);
                var ledger = _turnLedger.SnapshotFor(param.playTurnId);
                if (ledger.HasValue)
                {
                    log.turnLedger = ledger.Value.wire;
                    runningKey = ledger.Value.runningKey;
                    speedRunning = ledger.Value.speedRunning;
                }

                turnId = param.playTurnId;
                heartbeat = Frozen(log.ToDictionary());
                heartbeatEvent = log.Event;
            }

            var issue = new FAppIssueLog { kind = AppIssueKind.FreezeLong };
            _decorService.DecorPreview(issue);

            return new EmergencySnapshot(
                turnId, now, heartbeat, Frozen(issue.ToDictionary()), runningKey, speedRunning, screen, heartbeatEvent, issue.Event,
                _appInfoRepository.PackageName, _appInfoRepository.Platform, _configService.ShouldNotSendLogToServer);
        }
        /// <summary>
        /// Chuỗi hoá NGAY trên main thread rồi đọc lại thành bản riêng: payload có thể giữ tham chiếu tới đối tượng sống
        /// (dictionary nhãn, extraMeta của game…) — luồng canh chuỗi hoá sau đó mà main thread sửa giữa chừng là "collection
        /// was modified", dòng khẩn mất im lặng. Bản đọc lại không chung tham chiếu nào với ai (hợp đồng: "chuỗi bất biến").
        /// </summary>
        private static Dictionary<string, object> Frozen(Dictionary<string, object> payload)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(payload.ToJson());
        }
    }
}
