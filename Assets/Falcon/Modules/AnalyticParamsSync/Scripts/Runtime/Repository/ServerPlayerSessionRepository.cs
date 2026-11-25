/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2025-10-21
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Falcon.Helpers.Devkit;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.AnalyticParamsSync
{
    [Primary]
    public class ServerPlayerSessionRepository : IFPlayerSessionRepository, ITerminal, IInit
    {
        private readonly ReservePlayerSessionRepository _reserveRepository;
        private readonly ITimeRepository _timeRepository;

        // Số phiên CHỐT MỘT LẦN cho cả process. Trước 1.1.8 đây là getter đọc SỐNG
        // PlaySessionData.Instance.sessionId, mà blob đó bị THAY CẢ ĐỐI TƯỢNG giữa phiên
        // (SCUpdateData → AccountManager.ClientData setter → GameDatas), nên một process bắn ra
        // 2–3 sessionId khác nhau trong khi sessionUid (bốc 1 lần/process) đứng im:
        //   · user cũ    : log khúc đầu mang số của process TRƯỚC, tới Init mới ++ thành số thật
        //   · máy mới    : sổ nhà → bản server đè → ++  ⇒ BA số trong một lượt chạy
        //   · reconnect  : bản server về sau chưa chứa lần ++ này ⇒ số TỤT NGƯỢC
        // Chiều ngược mới là chiều đau: một sessionId ứng HAI sessionUid, nên mọi phép gộp khoá
        // theo sessionId sẽ nhồi khúc đầu lượt mới vào lượt cũ. Chưa kể AdSessionCounterState
        // (BigData) so sessionId != để xoá sổ đếm theo phiên ⇒ đổi số giữa phiên là xoá oan:
        // sessionAdLoadIndex về 1, firstAdEcpm/previousAdEcpm mất trắng.
        // Blob từ nay chỉ là sổ để ĐỒNG BỘ, KHÔNG phải nguồn của con số đang dán lên log.
        private readonly LazyVal<int> _sessionId;

        public ServerPlayerSessionRepository(
            ReservePlayerSessionRepository reserveRepository, ITimeRepository timeRepository)
        {
            _reserveRepository = reserveRepository;
            _timeRepository = timeRepository;
            RetentionChanged = _reserveRepository.RetentionChanged;
            _sessionId = new LazyVal<int>(FreezeSessionId);
        }

        public long GetModeTotalSec(string gameMode)
        {
            var reserveTotal = _reserveRepository.GetModeTotalSec(gameMode);
            var serverModes = PlaySessionData.Instance.gameModeToTotalSec;
            // TryGetValue chứ KHÔNG GetValueOrDefault-rồi-??: bản cũ coi dict RỖNG (account mới,
            // sync trả sổ trắng) là "có sổ, tổng 0" nên fallback về sổ nhà chết hẳn — án
            // total_play_time 29→0 giữa phiên (log 13/08). Thiếu DÒNG cũng phải mở sổ nhà,
            // không riêng gì thiếu SỔ.
            if (serverModes == null || !serverModes.TryGetValue(gameMode, out var serverTotal))
                return reserveTotal;

            // Counter chỉ được đi lên → sổ nào số LỚN HƠN là sổ đúng: reserve chỉ vượt được sổ
            // server khi sổ server từng bị reset (bug cũ / sổ trắng đè). Max ở đây là máy TỰ LÀNH
            // cho các máy ngoài fleet đã dính — không cần migration. Chiều đa-thiết-bị vẫn đúng:
            // server hợp lệ lớn hơn reserve (chơi máy khác) thì max giữ nguyên server.
            return Math.Max(serverTotal, reserveTotal);
        }

        public long IncreaseModeTotalSec(string gameMode, long seconds)
        {
            var reserveTotal = _reserveRepository.IncreaseModeTotalSec(gameMode, seconds);
            PlaySessionData.Instance.gameModeToTotalSec ??= new Dictionary<string, long>();
            var serverModes = PlaySessionData.Instance.gameModeToTotalSec;
            // Seed/hàn từ SỔ NHÀ chứ không từ 0: bản cũ tạo entry mới = 0 + seconds, vứt lịch sử
            // reserve — hai sổ lệch vĩnh viễn và wire mang số nhỏ. Max với reserve (đã cộng
            // seconds ở trên) thì entry thiếu hay tụt đều hội tụ về sự thật ở lần ghi kế.
            var serverTotal = serverModes.TryGetValue(gameMode, out var time) ? time + seconds : 0;
            return serverModes[gameMode] = Math.Max(serverTotal, reserveTotal);
        }

        public long FirstLogInMillis =>
            PlaySessionData.Instance.firstLogInMillis ?? _reserveRepository.FirstLogInMillis;

        // Max chứ không ??: activeDays phía server chỉ được cộng SAU login (UpdateRemoteData) —
        // login không xong (editor, mạng xấu) thì blob đứng im trong khi sổ nhà vẫn tự cộng mỗi
        // ngày chơi; blob khác-null đè sổ nhà là activeDays kẹt 1 giữa retentionDay 13 (log 13/08).
        public int ActiveDays => PlaySessionData.Instance.activeDays is { } serverDays
            ? Math.Max(serverDays, _reserveRepository.ActiveDays)
            : _reserveRepository.ActiveDays;
        public int SessionId => _sessionId.Value;

        public bool RetentionChanged { get; private set; }

        public async Task Init(CancellationToken cancellationToken = default)
        {
            if (RemoteUncertain())
            {
                if(!await AccountLoginListener.WaitLogin()) return;
                if (RemoteUncertain())
                    OverwriteRemoteData();
                else
                    UpdateRemoteData();
            }
            else
            {
                UpdateRemoteData();
            }
        }

        public void OnPostStop()
        {
            if (RemoteUncertain()) return;

            // Kéo sổ lên lần nữa trước khi đẩy: cú Max trong Init chỉ chặn được những gì đã về
            // TRƯỚC nó, mà SCUpdateData.OnData() đè blob VÔ ĐIỀU KIỆN và còn về sau (login xong
            // muộn, hoặc reconnect bắn CSLogin lại). Bản server về sau chưa chứa lần ++ của lượt
            // này là sổ TỤT: lượt sau chốt lại đúng số cũ ⇒ hai lượt chạy dùng chung một
            // sessionId, đúng cái chiều làm kho gộp sai. Dòng này idempotent, và cũng là mốc chốt
            // số cho ca mở app rồi tắt ngay, không kịp bắn log nào.
            PlaySessionData.Instance.sessionId = Math.Max(PlaySessionData.Instance.sessionId ?? 0, SessionId);
            PlaySessionData.Instance.UpdateToServer();
        }

        private static bool RemoteUncertain()
        {
            var playerGeneralData = PlaySessionData.Instance;
            return playerGeneralData.gameModeToTotalSec == null
                   || playerGeneralData.firstLogInMillis == null
                   || playerGeneralData.activeDays == null
                   || playerGeneralData.sessionId == null
                   || playerGeneralData.lastLoginInDateTimeLocal == null;
        }

        /// <summary>
        /// Số phiên của LƯỢT CHẠY này, tính MỘT lần ở lần đọc đầu tiên rồi đứng im tới lúc tắt app
        /// — cùng vòng đời với <c>sessionUid</c>, đó là cả mục đích của hàm này (xem chú thích
        /// <see cref="_sessionId"/>). Chốt lười chứ không chốt trong ctor: ctor của repository nằm
        /// trong lúc container dựng DI, còn <see cref="PlaySessionData"/> đi qua
        /// <c>AccountManager</c> (singleton tĩnh, tự nạp save lúc chạm) — đọc ở đó là buộc thứ tự
        /// khởi tạo của hai hệ vào nhau mà chẳng được gì, vì lần đọc đầu LUÔN sớm hơn log đầu.
        /// <br/>Nguồn: sổ server + 1 nếu có sổ; không có (máy mới / sổ thiếu field) thì dùng sổ nhà
        /// — <see cref="ReservePlayerSessionRepository"/> đã ++ trong ctor của chính nó.
        /// <br/>⚠ Máy mới: lượt chạy đầu dán số của SỔ NHÀ, có thể không khớp sổ server cho đúng
        /// lượt đó. Đổi lấy "một lượt chạy một con số" — số nhảy GIỮA hai lượt thì vô hại, nhảy
        /// TRONG một lượt mới là sai dữ liệu.
        /// <br/>⚠ App chết trước <see cref="Init"/> thì sổ chưa tiến, lượt sau dùng lại số cũ —
        /// y như trước 1.1.8 (cú ++ cũ cũng chỉ nằm trong Init), không phải hồi quy của bản này.
        /// </summary>
        private int FreezeSessionId()
        {
            var stored = PlaySessionData.Instance.sessionId;
            return stored.HasValue ? stored.Value + 1 : _reserveRepository.SessionId;
        }

        private void UpdateRemoteData()
        {
            var lastLoginInDateTimeLocal = PlaySessionData.Instance.lastLoginInDateTimeLocal;
            if (!lastLoginInDateTimeLocal.HasValue)
                RetentionChanged = true;
            else
                RetentionChanged =
                    DateTime.Compare(_timeRepository.LocalNow().Date, lastLoginInDateTimeLocal.Value.Date) > 0;
            if (RetentionChanged) PlaySessionData.Instance.activeDays++;
            PlaySessionData.Instance.lastLoginInDateTimeLocal = _timeRepository.LocalNow();
            // KHÔNG ++ ở đây nữa: cú ++ đã nằm trong FreezeSessionId, ++ lần hai là đếm đôi. Max
            // để sổ chỉ đi LÊN — bản server có thể đang cao hơn ta (máy thứ hai của cùng account),
            // ghi đè bằng số nhỏ hơn là hai máy đụng số mãi. Đọc SessionId cũng là chốt số muộn
            // nhất tại đây, cho ca cả phiên không log gì trước Init.
            PlaySessionData.Instance.sessionId = Math.Max(PlaySessionData.Instance.sessionId ?? 0, SessionId);

            PlaySessionData.Instance.UpdateToServer();
        }

        private void OverwriteRemoteData()
        {
            // ModeTimes là index RAM LƯỜI — chỉ chứa mode nào ĐÃ được đọc/ghi trong phiên này;
            // chép nó lúc đầu phiên là seed server bằng sổ trắng (nửa còn lại của án 29→0).
            // Materialize mode SDK-own trước khi chép; mode riêng của game nếu vẫn thiếu thì từ
            // nay vô hại — đường đọc/ghi ở trên tự fallback + tự hàn theo reserve.
            _reserveRepository.GetModeTotalSec(PlayerSessionService.TOTAL_TIME_MODE);

            Dictionary<string, long> gameModeToTotalSec = new();
            foreach (var (key, basicPoolData) in _reserveRepository.ModeTimes)
                gameModeToTotalSec[key] = basicPoolData.Value;
            PlaySessionData.Instance.gameModeToTotalSec = gameModeToTotalSec;
            PlaySessionData.Instance.firstLogInMillis = _reserveRepository.FirstLogInMillis;
            PlaySessionData.Instance.activeDays = _reserveRepository.ActiveDays;
            PlaySessionData.Instance.sessionId = SessionId;   // số đã chốt, không phải sổ nhà thô
            PlaySessionData.Instance.lastLoginInDateTimeLocal = _reserveRepository.LastLoginInDateTimeLocal;
            RetentionChanged = _reserveRepository.RetentionChanged;
            PlaySessionData.Instance.UpdateToServer();
        }
    }
}