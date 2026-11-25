/*
 * Author: leehuyyhoangg
 * Email: hoanglh@falcongames.com
 * Company: Falcon Games
 * Date: 2026-08-07
 */

using System;
using System.Collections.Generic;
using Falcon.Helpers.Devkit;
using JetBrains.Annotations;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData
{
    /// <summary>
    /// Entity LƯỢT CHƠI — <c>FalconBigDataController.Level</c> (xem EntityLifecycle-Design.md §2).
    /// <br/>Tham số định danh của lượt (playTurnId, currentLevel, currentLevelId, difficulty,
    /// movesLimit, timeLimitSec) được cache từ <see cref="OnStart"/>, các khoảnh khắc sau KHÔNG
    /// phải nhập lại — chỉ nhập phần mới phát sinh.
    /// </summary>
    public class FLevelApi
    {
        private readonly LevelLogService _logService;
        private readonly LevelTurnService _turnService;
        private readonly LevelLogDecorService _decorService;
        private readonly LabelLogService _labelLogService;
        private readonly LevelReviveLogService _reviveLogService;
        private readonly LevelBoosterLogService _boosterLogService;
        private readonly LevelHeartbeatService _heartbeatService;
        private readonly TurnLedgerService _turnLedger;

        internal FLevelApi(
            LevelLogService logService, LevelTurnService turnService, LevelLogDecorService decorService,
            LabelLogService labelLogService, LevelReviveLogService reviveLogService,
            LevelBoosterLogService boosterLogService, LevelHeartbeatService heartbeatService,
            TurnLedgerService turnLedger)
        {
            _heartbeatService = heartbeatService;
            _turnLedger = turnLedger;
            _logService = logService;
            _turnService = turnService;
            _decorService = decorService;
            _labelLogService = labelLogService;
            _reviveLogService = reviveLogService;
            _boosterLogService = boosterLogService;
        }

        // ---- Đọc / sửa cache của lượt ----

        /// <summary>playTurnId của lượt chơi ĐANG MỞ (null nếu không có).</summary>
        public string CurrentTurnId => _turnService.OpenPlayTurnId;

        /// <summary>
        /// playTurnId của lượt KẾT THÚC gần nhất (Pass / Fail / Skip) — vẫn giữ sau khi lượt kế đã mở. Dùng điền
        /// <c>triggerPlayTurnId</c> cho Inter chiếu sau thắng / thua (hợp đồng đợt 3 B12). Null nếu chưa có lượt nào
        /// kết thúc trong lần chạy này.
        /// </summary>
        public string LastEndedTurnId => _turnService.LastEndedPlayTurnId;

        /// <summary>
        ///     Một lần HỒI SINH ĐÃ DÙNG — event <c>f_sdk_level_action_revive</c>, ký với loader 09/11.
        ///     Chỉ gọi khi hồi sinh THẬT SỰ xảy ra. Lời mời / nhận / từ chối là sự kiện của popup:
        ///     <c>Ui.OnImpression</c> / <c>Ui.OnClicked</c> / <c>Ui.OnDismissed</c> với bề mặt
        ///     <see cref="FUiSurface.ReviveOffer"/>.
        ///     <br/>🔴 Luật tích hợp (chốt 04/09): chỉ báo <c>Fail</c> khi người chơi đã HẾT đường hồi
        ///     sinh. Trong lúc popup còn hiện, lượt vẫn mở nên <c>playTurnId</c> tự gắn đúng; báo Fail
        ///     trước thì dòng này vắng id (SDK không đoán id lượt đã đóng).
        /// </summary>
        public void OnRevive(LevelReviveParam param)
        {
            _reviveLogService.LogRevive(param);
        }

        /// <inheritdoc cref="OnRevive(LevelReviveParam)"/>
        /// <param name="progressPct">Tiến độ trong ván lúc hồi sinh, <c>[0, 100]</c>.</param>
        /// <param name="payMethod">
        ///     Trả bằng gì — một trong <see cref="FPayMethod"/>. Không có giá trị mặc định, cố ý: game luôn
        ///     biết người chơi vừa trả bằng gì; thật sự không biết thì truyền <c>null</c> (vắng), đừng
        ///     truyền <c>free</c>.
        /// </param>
        /// <param name="reviveType">
        ///     Loại hồi sinh — vocab của game, snake_case (<c>extra_moves</c>, <c>extra_time</c>). Cần tách mức thì
        ///     đưa vào tên loại (<c>extra_moves_5</c>), không có field số lượng.
        /// </param>
        public void OnRevive(
            float progressPct, [CanBeNull] string payMethod, [CanBeNull] string reviveType = null,
            [CanBeNull] string playTurnId = null, [CanBeNull] Dictionary<string, object> extraMeta = null)
        {
            _reviveLogService.LogRevive(new LevelReviveParam
            {
                progressPct = progressPct,
                payMethod = payMethod,
                reviveType = reviveType,
                playTurnId = playTurnId,
                extraMeta = extraMeta
            });
        }

        /// <summary>
        ///     Một lần DÙNG BOOSTER TRONG VÁN — event <c>f_sdk_level_action_booster</c>, ký với loader 10/11. Booster
        ///     chọn TRƯỚC ván thì khai ở <c>preBoostersUsed</c> của <see cref="OnStart"/>, không phải ở đây.
        ///     <br/>⚠ Vẫn truyền <c>boostersUsed</c> lúc kết thúc ván như cũ: đó là nguồn của "dùng bao nhiêu lần";
        ///     event này trả lời "dùng ở đoạn nào" và "booster có từ đâu". Kho đọc mỗi câu từ MỘT nguồn.
        ///     Popup gợi ý dùng booster: <c>Ui.*</c> với bề mặt <see cref="FUiSurface.BoosterOffer"/>.
        /// </summary>
        public void OnBooster(LevelBoosterParam param)
        {
            _boosterLogService.LogBooster(param);
        }

        /// <inheritdoc cref="OnBooster(LevelBoosterParam)"/>
        /// <param name="boosterId">Mã theo sổ <c>Resource.RegisterBooster</c>. Thiếu thì SDK bỏ dòng.</param>
        /// <param name="progressPct">Tiến độ trong ván lúc dùng, <c>[0, 100]</c>.</param>
        /// <param name="payMethod">
        ///     Booster CÓ TỪ ĐÂU — <see cref="FPayMethod.Inventory"/> (có sẵn), <see cref="FPayMethod.Currency"/> (mua
        ///     tại chỗ), <see cref="FPayMethod.Ad"/>, <see cref="FPayMethod.Free"/>. Không có giá trị mặc định, cố ý.
        /// </param>
        public void OnBooster(
            string boosterId, float progressPct, [CanBeNull] string payMethod, [CanBeNull] string playTurnId = null,
            [CanBeNull] Dictionary<string, object> extraMeta = null)
        {
            _boosterLogService.LogBooster(new LevelBoosterParam
            {
                boosterId = boosterId,
                progressPct = progressPct,
                payMethod = payMethod,
                playTurnId = playTurnId,
                extraMeta = extraMeta
            });
        }

#pragma warning disable 618
        /// <summary>
        ///     ⚠ LỖI THỜI — <c>f_sdk_level_action</c> (1.7.0) đã ngừng. Giữ để code cũ build được; lời gọi
        ///     được chuyển theo bảng đã gửi loader (<see cref="LevelActionAdapter"/>): hồi sinh ĐÃ NHẬN
        ///     thành một dòng <c>f_sdk_level_action_revive</c>, còn hồi sinh bị từ chối và booster không
        ///     sinh dòng nào (kèm cảnh báo).
        /// </summary>
        [Obsolete("Dùng Level.OnRevive(...) cho hồi sinh đã dùng, và Ui.* với FUiSurface.ReviveOffer cho lời mời.")]
        public void OnAction(LevelActionParam param)
        {
            var (revive, warning) = LevelActionAdapter.ToRevive(param);
            if (warning != null) AnalyticLogger.Instance.Warning(warning);
            if (revive != null) _reviveLogService.LogRevive(revive);
        }

        /// <inheritdoc cref="OnAction(LevelActionParam)"/>
        [Obsolete("Dùng Level.OnRevive(...) cho hồi sinh đã dùng, và Ui.* với FUiSurface.ReviveOffer cho lời mời.")]
        public void OnAction(
            string action, float progressPct, bool accepted, bool offered = true,
            [CanBeNull] string boosterId = null, [CanBeNull] string payMethod = null,
            [CanBeNull] string playTurnId = null,
            [CanBeNull] Dictionary<string, object> extraMeta = null)
        {
            OnAction(new LevelActionParam
            {
                action = action,
                progressPct = progressPct,
                offered = offered,
                accepted = accepted,
                boosterId = boosterId,
                payMethod = payMethod,
                playTurnId = playTurnId,
                extraMeta = extraMeta
            });
        }
#pragma warning restore 618

        /// <summary>
        /// Bộ tham số của ván ĐANG CHƠI, đọc thuần — không đóng ván, không tắt stamp playTurnId.
        /// Dùng cho autosave giữa ván (<see cref="OnPauseExit"/> mang nghĩa "rời ván" nên không
        /// dùng cho việc này được). Null nếu không có ván nào đang mở.
        /// </summary>
        public LevelTurnSnapshot CurrentTurn => _turnService.CurrentSnapshot();

        /// <summary>
        /// Cập nhật tham số CẤU HÌNH của ván đang chơi khi nó đổi giữa chừng: mua thêm lượt đi,
        /// revive đổi độ khó... Chỉ đối số khác null mới ghi đè.
        /// <br/>⚠ <paramref name="movesLimit"/>/<paramref name="timeLimitSec"/> là config của BẢN
        /// THIẾT KẾ MÀN (hợp đồng xếp chúng chủ thể LEVEL), nên sửa ở đây là sửa cho MỌI lần chơi
        /// màn đó về sau, không riêng ván này. Số lượt CÒN LẠI của ván thì đừng gửi qua đây.
        /// <br/>Trước đây muốn đổi phải gọi lại <see cref="OnStart"/> — mà thế là ĐÈ TURN, sai
        /// vòng đời (một ván thành hai lượt trong dữ liệu).
        /// <br/>Cố tình không cho đổi <c>currentLevel</c>/<c>currentLevelId</c>: đó là danh tính
        /// của lượt, đổi giữa chừng nghĩa là lượt khác — mà lượt khác thì mở turn mới.
        /// </summary>
        public void UpdateTurn(string difficulty = null, int? movesLimit = null, int? timeLimitSec = null)
        {
            _turnService.UpdateTurn(difficulty, movesLimit, timeLimitSec);
        }

        /// <summary>
        /// Khai config của BẢN THIẾT KẾ MÀN (giới hạn lượt đi / thời gian) — nhãn của MÀN, đúng cho
        /// mọi lần chơi màn đó, nên khai một lần là đủ và SDK tự lưu qua restart.
        /// <br/>Bình thường KHÔNG cần gọi: <see cref="OnStart"/> đã nhận hai số này. Hàm này dành
        /// cho đường tự dựng <c>FLevelLog</c> (vd FLevelManager) hoặc lúc game biết config trước khi
        /// vào màn. Đối số null = không nhắc tới, KHÔNG phải gỡ.
        /// </summary>
        public void SetLevelConfig(int currentLevel, int? movesLimit = null, int? timeLimitSec = null)
        {
            _turnService.SetLevelConfig(currentLevel, movesLimit, timeLimitSec);
        }

        /// <summary>
        /// Khai điểm trình độ NGƯỜI CHƠI — gọi lại mỗi khi nó đổi; gọi lại cùng giá trị thì SDK tự
        /// bỏ qua nên không sợ tốn bản tin.
        /// <code>
        /// FalconBigDataController.Level.SetUserElo(PlayerRating.Current);
        /// </code>
        /// Trước 1.3.0 đây là provider và elo được đóng phẳng lên MỌI level event. Sai chủ thể
        /// (§H6: elo tả người chơi, không tả lượt) và nhân bản một sự thật của user lên stream dày
        /// nhất hệ; nay nó là nhãn user, gửi khi ĐỔI, server forward-fill lên các event sau.
        /// <br/>Chỉ game biết con số này nên game phải cấp. Ngược lại, ĐỘ KHÓ CỦA MÀN thì đừng gửi:
        /// nó là hàm của kết quả cả fleet, server tự giải ra từ tỉ lệ pass + elo người chơi;
        /// client gửi lên chỉ là đóng băng một giá trị cũ hơn bản server tự tính.
        /// <br/>Truyền null để GỠ nhãn.
        /// </summary>
        public void SetUserElo(int? elo)
        {
            _labelLogService.LabelUser(FUserLabelKey.ELO, elo);
        }


        /// <summary>
        /// Người chơi RỜI ván dở (thoát ra menu / save-and-quit): trả về snapshot bộ tham số của
        /// lượt chơi để game persist vào save (serializable), đồng thời tắt stamp playTurnId cho
        /// các log ngoài gameplay (ad ở menu không mang id của lượt nữa).
        /// Trả về null nếu không có lượt nào đang mở.
        /// <br/>Đây là "ra khỏi ngữ cảnh" (hợp đồng đợt 3 B5): SDK bắn MỘT heartbeat của đoạn ngay tại đây rồi mới đóng
        /// đoạn — thoát lượt bằng nút mà không xuống nền vẫn còn dòng; lần xuống nền sau không còn lượt mở nên không trùng.
        /// Muốn tính là bỏ ván hẳn thì gọi <see cref="OnFail"/> với <see cref="LevelFailReason.Quit"/> thay vì hàm này.
        /// </summary>
        public LevelTurnSnapshot OnPauseExit()
        {
            _heartbeatService.FireNow();
            var snapshot = _turnService.SuspendTurn();
            _turnLedger.End();
            return snapshot;
        }

        /// <summary>
        /// Người chơi mở lại VÁN DỞ (cùng phiên hoặc sau restart app): trả lại snapshot đã lấy từ
        /// <see cref="OnPauseExit"/> — heartbeat/terminal sau đó mang đúng playTurnId + bundle
        /// của ván cũ (1 ván = 1 turn dù qua restart). KHÔNG phát event nào.
        /// Snapshot null/mất id thì bỏ qua — cứ <see cref="OnStart"/> lượt mới, chấp nhận vỡ theo
        /// hợp đồng A3.4.
        /// </summary>
        public void OnResume(LevelTurnSnapshot snapshot)
        {
            if (snapshot == null) return;
            // Không phải vác movesLimit/timeLimitSec qua save nữa: chúng là nhãn của MÀN, SDK tự
            // lưu và tự nạp lại.
            Resume(snapshot.playTurnId, snapshot.currentLevel, snapshot.difficulty, snapshot.currentLevelId,
                null, null, Math.Max(1, snapshot.turnSegmentNo) + 1);
        }

        /// <inheritdoc cref="OnResume(LevelTurnSnapshot)"/>
        public void OnResume(
            string playTurnId, int currentLevel, string difficulty = null, string currentLevelId = null,
            int? movesLimit = null, int? timeLimitSec = null)
        {
            // Không có bản chụp thì không biết đoạn trước là đoạn mấy — chơi tiếp nên ít nhất là đoạn 2
            Resume(playTurnId, currentLevel, difficulty, currentLevelId, movesLimit, timeLimitSec, 2);
        }

        private void Resume(
            string playTurnId, int currentLevel, string difficulty, string currentLevelId,
            int? movesLimit, int? timeLimitSec, int segmentNo)
        {
            _turnService.RestoreTurn(playTurnId, currentLevel, currentLevelId, difficulty,
                movesLimit, timeLimitSec, segmentNo);
            // Sổ thời gian B6: chơi tiếp = đoạn mới, sổ đặt lại (chỉ khi mở lại được lượt)
            if (!string.IsNullOrEmpty(playTurnId) && _turnService.OpenPlayTurnId == playTurnId)
                _turnLedger.Begin(playTurnId, segmentNo);
        }

        // ---- Sổ thời gian lượt chơi (hợp đồng đợt 3 B6) ----

        /// <summary>
        /// Khai MỘT LẦN lúc khởi tạo game những gì game sẽ báo cho sổ thời gian lượt chơi (hợp đồng đợt 3 B6). SDK lưu qua
        /// restart và gửi 5 cờ <c>cap_*</c> trên mọi dòng lượt + <c>app_open</c> (lần mở Cold mang giá trị lần khai trước).
        /// Khả năng chưa khai thì các field của nó VẮNG (không phải 0) — kho lọc theo cờ trước khi đọc.
        /// </summary>
        /// <param name="busy">Game gọi <see cref="SetBusy"/> quanh lúc người chơi phải chờ.</param>
        /// <param name="actions">Game gọi <see cref="OnPlayerAction"/> cho mỗi thao tác có tác dụng.</param>
        /// <param name="ineffectiveTap">Game gọi <see cref="OnIneffectiveTap"/> khi bắt được chạm không có tác dụng.</param>
        /// <param name="speed">Game có x2 và gọi <c>OnSpeedAction</c>.</param>
        /// <param name="screen">Game gọi <c>Surface.Enter</c> / <c>Exit</c> cho popup trong level.</param>
        public void Declare(bool busy, bool actions, bool ineffectiveTap, bool speed, bool screen)
        {
            _turnLedger.Declare(new TurnCaps
            {
                busy = busy, actions = actions, ineffectiveTap = ineffectiveTap, speed = speed, screen = screen
            });
        }

        /// <summary>
        /// Một thao tác chơi CÓ TÁC DỤNG (đặt khối, nối ô, điền số… — game tự định nghĩa và ghi vào tài liệu tích hợp).
        /// SDK tự tính <c>n_actions</c>, <c>first_action_sec</c>, <c>sec_since_last_action</c>, các khoảng không thao tác.
        /// O(1). Tên khác <c>OnAction</c> cũ (đã ngừng) có chủ đích.
        /// </summary>
        public void OnPlayerAction() => _turnLedger.OnPlayerAction();

        /// <summary>
        /// Người chơi KHÔNG làm được thao tác nào có tác dụng (hoạt hình, nổ dây chuyền, rơi khối, chuyển cảnh) — dù game có
        /// chặn chạm hay không. Gọi <c>true</c> lúc bắt đầu, <c>false</c> lúc hết. Game cho thao tác chồng lên hoạt hình
        /// thì lúc đó không bận; <b>hết nước đi KHÔNG tính bận</b> (người chơi đang nghĩ / kẹt). Cần khai <c>busy</c>.
        /// </summary>
        public void SetBusy(bool busy) => _turnLedger.SetBusy(busy);

        /// <summary>
        /// Chạm KHÔNG có tác dụng (tuỳ chọn): lúc bận ⇒ <c>n_taps_while_busy</c> (sốt ruột), lúc không bận ⇒
        /// <c>n_ineffective_taps</c> (bấm trượt / không hiểu). Game không bắt được thì đừng gọi và đừng khai.
        /// </summary>
        public void OnIneffectiveTap() => _turnLedger.OnIneffectiveTap();

        /// <summary>
        /// Người chơi bật / tắt x2 hoặc bỏ qua hoạt hình — <c>f_sdk_speed_action</c> (hợp đồng đợt 3 B7), trong lẫn ngoài level.
        /// <c>SpeedOn</c> / <c>SpeedOff</c> cũng là nguồn của <c>t_speed_on_sec</c> trên sổ lượt (cần khai <c>speed</c>).
        /// </summary>
        /// <param name="action">Hành động.</param>
        /// <param name="animType">Loại đoạn hoạt hình.</param>
        /// <param name="animName">Tên riêng của game (tuỳ chọn).</param>
        /// <param name="secIntoAnim">Chỉ <see cref="SpeedAction.SkipAnim"/>: đã xem bao nhiêu giây. Hành động khác thì bỏ qua.</param>
        public void OnSpeedAction(SpeedAction action, AnimType animType, string animName = null, float? secIntoAnim = null)
        {
            if (secIntoAnim.HasValue && action != SpeedAction.SkipAnim)
            {
                AnalyticLogger.Instance.Warning($"OnSpeedAction({action}): secIntoAnim chỉ dành cho SkipAnim — bỏ qua.");
                secIntoAnim = null;
            }

            if (secIntoAnim is < 0 || secIntoAnim.HasValue && (float.IsNaN(secIntoAnim.Value) || float.IsInfinity(secIntoAnim.Value)))
                secIntoAnim = null;

            if (action == SpeedAction.SpeedOn) _turnLedger.SetSpeed(true);
            else if (action == SpeedAction.SpeedOff) _turnLedger.SetSpeed(false);

            _logService.LogSpeedAction(new FSpeedActionLog
            {
                action = action,
                animType = animType,
                animName = string.IsNullOrEmpty(animName) ? null : animName,
                secIntoAnim = secIntoAnim
            });
        }

        // ---- Báo khoảnh khắc (phát event) ----

        /// <summary>
        /// Người chơi bắt đầu một lượt chơi level. Mở turn mới (đè turn cũ nếu còn mở) —
        /// từ đây mọi log (ad/iap/resource...) tự mang playTurnId cho tới khi lượt kết thúc.
        /// </summary>
        /// <param name="currentLevel">Số level đang chơi (bắt buộc — seed cho cache của cả lượt).</param>
        /// <param name="difficulty">Độ khó của màn.</param>
        /// <param name="currentLevelId">Danh tính BẢN THIẾT KẾ level (đổi tune/AB = id mới).</param>
        /// <param name="movesLimit">Giới hạn lượt đi của màn (config màn). Không biết thì bỏ trống, đừng điền bừa.</param>
        /// <param name="timeLimitSec">Giới hạn thời gian màn (giây). Không biết thì bỏ trống, đừng điền bừa.</param>
        /// <param name="param">Tham số của LƯỢT — <c>param.currentLevel</c> bắt buộc.</param>
        /// <param name="movesLimit">
        /// Giới hạn lượt đi của màn. CỐ Ý nằm ngoài param: đây là config của BẢN THIẾT KẾ MÀN
        /// (§H6 — chủ thể LEVEL), SDK đưa vào kho nhãn chứ không đi flat trên bản tin lượt.
        /// </param>
        /// <param name="timeLimitSec">Giới hạn thời gian màn (giây) — cùng lý do nằm ngoài param.</param>
        public void OnStart(LevelStartParamV2 param, int? movesLimit = null, int? timeLimitSec = null)
        {
            _logService.LogLevelStart(param, movesLimit, timeLimitSec);
        }

        /// <inheritdoc cref="OnStart(LevelStartParamV2,int?,int?)"/>
        /// <param name="currentLevel">Số level đang chơi (bắt buộc — seed cho cache của cả lượt).</param>
        /// <param name="difficulty">Độ khó của màn.</param>
        /// <param name="currentLevelId">Danh tính BẢN THIẾT KẾ level (đổi tune/AB = id mới).</param>
        /// <param name="preBoostersUsed">Booster chọn trước khi vào màn.</param>
        /// <param name="extraMeta">Tham số game-specific CHƯA có trong hợp đồng (được lưu event_extra_props, xin vào hợp đồng sau).</param>
        public void OnStart(
            int currentLevel, string difficulty = null, string currentLevelId = null,
            int? movesLimit = null, int? timeLimitSec = null,
            Dictionary<string, int> preBoostersUsed = null, Dictionary<string, object> extraMeta = null)
        {
            var param = new LevelStartParamV2
            {
                currentLevel = currentLevel,
                currentLevelId = currentLevelId,
                preBoostersUsed = preBoostersUsed,
                extraMeta = extraMeta
            };
            if (difficulty != null) param.difficulty = difficulty;
            _logService.LogLevelStart(param, movesLimit, timeLimitSec);
        }

        /// <summary>
        /// CẬP NHẬT tham số tiến độ của lượt đang mở (điểm, số nước, thời lượng, booster, <c>levelProgress</c>…) —
        /// <b>không sinh dòng</b> (hợp đồng đợt 3 B5, ký 23/11: khi nào ghi heartbeat là việc của SDK). SDK tự bắn heartbeat
        /// lúc app xuống nền, lúc <see cref="OnPauseExit"/>, và mang bản cập nhật gần nhất này (kèm <c>paramsUpdatedAt</c>).
        /// <br/>Game tự quản tham số thì gọi mỗi khi tham số đổi. Game dùng APS (<c>ALevelHeartBeatService</c>) thì không cần
        /// gọi: SDK hỏi APS lúc bắn. Không có lượt mở thì bỏ qua (cảnh báo log dev).
        /// <br/>⚠ Đổi hành vi từ bản trước: lời gọi này từng tự sinh một dòng heartbeat.
        /// </summary>
        public void OnHeartbeat(LevelHeartBeatParamV2 param)
        {
            _heartbeatService.UpdateParams(param);
        }

        /// <summary>
        /// Người chơi thắng lượt hiện tại. Định danh lượt tự lấy từ cache (từ <see cref="OnStart(LevelStartParamV2,int?,int?)"/>).
        /// Sau khoảnh khắc này các log khác (ad chuyển màn...) thôi mang playTurnId.
        /// </summary>
        public void OnPass(LevelPassParamV2 param)
        {
            _logService.LogLevelPass(param);
        }

        /// <inheritdoc cref="OnPass(LevelPassParamV2)"/>
        public void OnPass(
            int? score = null, TimeSpan? duration = null,
            Dictionary<string, int> boostersUsed = null, int? movesUsed = null,
            Dictionary<string, object> extraMeta = null)
        {
            _logService.LogLevelPass(new LevelPassParamV2
            {
                score = score,
                duration = duration,
                boostersUsed = boostersUsed,
                movesUsed = movesUsed,
                extraMeta = extraMeta
            });
        }

        /// <summary>
        /// Người chơi thua lượt hiện tại. Định danh lượt tự lấy từ cache (từ <see cref="OnStart(LevelStartParamV2,int?,int?)"/>).
        /// </summary>
        public void OnFail(LevelFailParamV2 param)
        {
            _logService.LogLevelFail(param);
        }

        /// <inheritdoc cref="OnFail(LevelFailParamV2)"/>
        /// <param name="failReason">Thua VÌ SAO (hợp đồng §D). Không biết thì bỏ trống, đừng điền bừa.</param>
        /// <param name="levelProgress">Đi được bao nhiêu % màn lúc thua (0-100). Không đo được thì bỏ trống — field vắng, không gửi 0.</param>
        public void OnFail(
            LevelFailReason? failReason = null, int? levelProgress = null,
            int? score = null, TimeSpan? duration = null,
            Dictionary<string, int> boostersUsed = null, int? movesUsed = null,
            Dictionary<string, object> extraMeta = null)
        {
            var param = new LevelFailParamV2
            {
                failReason = failReason,
                score = score,
                duration = duration,
                boostersUsed = boostersUsed,
                movesUsed = movesUsed,
                levelProgress = levelProgress,
                extraMeta = extraMeta
            };
            _logService.LogLevelFail(param);
        }
    }
}
