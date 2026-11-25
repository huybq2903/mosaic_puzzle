# SDK BigData → loader: thư hợp đồng ĐỢT 3, đợt 1 (C#) + đợt 2 (native) — tên wire, enum, thư era (23/11/2026)

> Gửi: loader (+ access đọc). Từ: owner SDK BigData. Căn cứ: `hop-dong-sdk-client-3-final.md` (ba bên ký 23/11) §11.1 mục 3.
> **Đã code** trên nhánh BigData (**BigData 1.8.0**, APS 1.2.0, Devkit 1.4.0; **đã publish 23/11/2026**).
> ✅ **Loader đã ký 23/11** (`loader-v3-work/sdk-client-3/reply-loader-dot3-wave1.md`): nhận đủ; hai câu ❓ trả lời ở §3 và §5; rule
> mapping 9 event mới seed sẵn; **mốc era cắt theo `sdk_version ≥ 1.8.0`**, không chờ ngày publish (cột ngày §4 vẫn điền để lưu vết).
> Van theo game (`fAppIssueEmergencyEnabled`, `fDeviceSignalsEnabled`) và define iOS `FALCON_DEVKIT_IOS_NATIVE` không suy được từ
> `sdk_version` ⇒ SDK báo ngày bật / tắt theo từng game.
> Field gửi dạng camelCase thì đi qua `SqlNameService` (vd `triggerPlayTurnId` → `trigger_play_turn_id`); field có **chữ số**
> SDK gửi thẳng snake để ra đúng tên đã ký (`SqlNameService` giữ nguyên `_` và chữ số).

## 1. Event mới (tên wire chính xác)

| Event | Mục | Field (tên gửi) |
|---|---|---|
| `f_sdk_perf_screen` | B1 | `perfScreen` · `fpsAvg` · `frameWorstMs` · `frameDrops` · `memWarnings` · `perfTargetFps` (chỉ khi đổi trần) · `batteryPct` · `batteryCharging` |
| `f_sdk_ftue_load_step` | B2 | `loadKind` · `step` · `wentBackground` (chỉ app) · `networkType` · `assetBytes` · `errorCode` |
| `f_sdk_battery_charging_change` | B3 | `batteryPct` · `batteryCharging` |
| `f_sdk_setting_change` | B4 | `setting` · `valueBefore` · `valueAfter` · `muteReason` |
| `f_sdk_speed_action` | B7 | `action` · `animType` · `animName` · `secIntoAnim` (+ `perf_screen`) |
| `f_sdk_app_issue` | S2 | `kind` · `exceptionType` · `exceptionTopFrame` · `exceptionHash` · `isFatal` · `freezeMs` · `isLoading` · `heartbeatSkipped` · `suppressedCount` · `source` · `prevEndType` · `prevSessionUid` · `prevPlayTurnId` · `prevLastAliveAt` · `prevFreezeLong` · `prevFatalExceptionType` · `prevFatalExceptionTopFrame` · `prevFatalExceptionHash` · `prevExitReason` (2) (+ `perf_screen`) |
| `f_sdk_device_audio_change` | B4(c) (2) | `deviceMediaVolumePct` · `deviceRingerMode` · `audioOutput` — trạng thái **sau** khi qua mốc (trước = dòng trước / `app_open`) |
| `f_sdk_permission_prompt` | B14 | `permission` · `promptKind` · `trigger` · `softResult` · `systemResult` |
| `f_sdk_account_link` | B14 | `provider` · `action` · `initiatedBy` · `trigger` · `conflictChoice` · `localLevel` · `cloudLevel` |

## 2. Field mới trên log có sẵn

| Chỗ | Field |
|---|---|
| Gói hiệu năng (level / booster / hồi sinh / mọi mốc QC + show_attempt / show_skipped / cụm Ui / offer / IAP checkout / funnel / live-ops / feature; không tài nguyên, không bản tin tổng hợp) | `perf_fps_avg_5s` · `perf_frame_worst_ms_5s` · `perf_frame_drops_since_last` · `perf_mem_warnings_since_last` · `perf_screen` (chưa có khung nào ⇒ cả gói vắng; `perf_screen` vắng nếu game chưa báo màn) |
| `f_sdk_app_open_data` | `perfTargetFps` · `displayRefreshHz` · `graphicsQuality` · `batteryPct` · `batteryCharging` · `settingSound` · `settingMusic` · `settingHaptic` · `settingIsDefault` · `settingLanguage` · `capBusy` · `capActions` · `capIneffectiveTap` · `capSpeed` · `capScreen` · `linkedProviders` |
| `f_sdk_level_data` Pass / Fail / Skip / HeartBeat | `t_total_sec` · `t_background_sec` · `t_ad_sec` · `t_popup_offer_sec` · `t_popup_tutorial_sec` · `t_popup_other_sec` · `t_busy_sec` · `t_ready_sec` · `t_ready_gap10_sec` · `t_ready_gap30_sec` · `t_ready_gap120_sec` · `first_action_sec` · `sec_since_last_action` · `t_speed_on_sec` · `n_actions` · `n_taps_while_busy` · `n_ineffective_taps` · `n_offer_shown` · `cap_busy` · `cap_actions` · `cap_ineffective_tap` · `cap_speed` · `cap_screen` · `turn_segment_no` |
| Gói hiệu năng (2) | `perf_thermal_state` · `perf_low_power` — có cả khi chưa có khung hình (không phụ thuộc fps) |
| `f_sdk_perf_screen` (2) | `thermalStateMax` (mức cao nhất trong đoạn) · `lowPower` (có lúc nào trong đoạn bật) · `batteryPlug` · `batteryTempC` |
| `f_sdk_app_open_data` (2) | `batteryPlug` · `batteryTempC` · `deviceMediaVolumePct` · `deviceRingerMode` · `audioOutput` · `deviceFontScale` · `deviceBoldText` · `deviceReduceMotion` · `deviceStorageFreeBucket` · `permPushStatus` · `permAttStatus` |
| `f_sdk_battery_charging_change` (2) | `batteryPlug` · `batteryTempC` |
| `f_sdk_ftue_load_step` `load_start` (2) | `msSinceProcessStart` — tiến trình bắt đầu → C# sống dậy (= giờ của dòng). Chỉ Android 7+ |
| `f_sdk_level_data` HeartBeat | `paramsUpdatedAt` (epoch ms) |
| `f_sdk_session_data` | `apsRefreshDisabled` (true / vắng) · `apsRefreshMaxMs` (vắng nếu chưa làm mới lần nào) |
| Mọi log một lần QC + `f_sdk_ad_show_attempt` / `_show_skipped` | `triggerPlayTurnId` · `adWhen` (danh sách §3) |

## 3. Danh sách enum (chữ thường, đóng)

- `adWhen`: `level_win` · `level_fail` · `level_start` · `retry` · `revive` · `booster` · `home` · `app_open` · `other` · `Unknown`.
- `perf_screen`: game đặt màn nền; popup = `popup:<surface_id>`; `ad` SDK tự đặt. `PopupKind`: `offer` · `tutorial` · `other`.
- `loadKind`: `app` · `level`. `step`: `load_start` · `first_frame` · `consent_shown` · `config_ready` · `asset_download_start` ·
  `asset_download_done` · `level_load_start` · `level_ready` · `load_error`.
- `setting`: `sound` · `music` · `haptic` · `language`. `muteReason`: `public_place` · `focus` · `dislike_sound` · `skip`.
- `action` (speed): `speed_on` · `speed_off` · `skip_anim` · `speed_tap_no_stock`. `animType`: `level_intro` · `gameplay` ·
  `level_result` · `reward` · `chest` · `other`.
- `kind` (app_issue): `exception` · `freeze` · `freeze_long` · `memory_critical` · `prev_exit`. `source`: `unity_low_memory` ·
  `activity_manager`. `prevEndType`: `abnormal` · `background_then_killed`. `prevExitReason`: `crash` · `crash_native` · `anr` ·
  `low_memory_foreground` · `low_memory_background` · `hang` (chỉ iOS) · `other`.
- (2) **`perf_thermal_state` / `thermalStateMax` — ✅ loader đồng ý 23/11**: `nominal` · `fair` · `serious` · `critical` (tên iOS).
  Android 7 mức gộp: NONE → nominal · LIGHT / MODERATE → fair · SEVERE → serious · CRITICAL / EMERGENCY / SHUTDOWN → critical.
- (2) `batteryPlug`: `ac` · `usb` · `wireless` · `none` (Android dock xếp `ac`). `deviceRingerMode`: `normal` · `vibrate` · `silent`.
  `audioOutput`: `speaker` · `wired` · `bluetooth`. `deviceStorageFreeBucket`: `lt_1gb` · `1_5gb` · `5_20gb` · `gt_20gb` (GiB).
- (2) `permPushStatus` / `f_sdk_permission_push`: thêm `not_determined`, iOS thêm `provisional`.
- (2) `deviceFontScale` iOS: `UIContentSizeCategory` → cỡ chữ Body / 17 pt: XS 0.82 · S 0.88 · M 0.94 · L 1.00 · XL 1.12 · XXL 1.24 ·
  XXXL 1.35 · AX1 1.65 · AX2 1.94 · AX3 2.35 · AX4 2.76 · AX5 3.12. Android: `Configuration.fontScale` nguyên.
- `permission`: `att` · `push` · `ads_consent`. `promptKind`: `soft` · `system`. `trigger` (prompt): `first_open` · `level_win` ·
  `level_start` · `settings` · `feature` · `reward` · `other`. `softResult`: `accept` · `decline` · `dismiss`.
  **`systemResult`** (tên field SDK chốt — kết quả lần xin của hàm bọc Android): `granted` · `denied` · `denied_dont_ask_again`.
- `provider`: `google_play_games` · `game_center` · `facebook` · `apple` · `google` · `other`. `action` (account): `prompt_shown` ·
  `success` · `cancel` · `fail` · `unlink` · `switch` · `save_conflict`. `initiatedBy`: `user` · `auto`. `trigger` (account):
  `first_open` · `settings` · `reward` · `other`. `conflictChoice`: `keep_local` · `keep_cloud` · `cancel`.

## 4. Thư era (bản 1.8.0, publish 23/11/2026)

| # | Thay đổi | Bản | Ngày |
|---|---|---|---|
| 1 | `level_progress` 0 → vắng trên Fail / HeartBeat (B13) | 1.8.0 | 23/11/2026 |
| 2 | Bỏ khung đầu sau resume (B1) | 1.8.0 | 23/11/2026 |
| 3 | SDK quyết lúc bắn heartbeat: xuống nền + `OnPauseExit`; `Level.OnHeartbeat` thành cập nhật tham số; APS thôi tự sinh dòng (B5) | 1.8.0 | 23/11/2026 |
| 4 | Bắt đầu gửi `f_sdk_app_issue` — `exception` · `freeze` · `memory_critical` · `prev_exit` (abnormal, có `prev_fatal_*`). `freeze_long`, dòng `is_fatal`, heartbeat kèm sự cố, `aps_refresh_*` chỉ có ở game **bật van** `fAppIssueEmergencyEnabled` (mặc định tắt; bật thử `is_test` trước, §11.1 mục 4) — ngày bật van theo game SDK báo riêng | 1.8.0 | 23/11/2026 |
| 5 | Trạng thái quyền tự đọc (B14(b)): `f_sdk_permission_push` / `_att` có thêm dòng do SDK đọc native (Android: mọi bản 1.8.0; iOS: chỉ game bật define `FALCON_DEVKIT_IOS_NATIVE`), giá trị mới `not_determined` / `provisional`; iOS tự bắn dòng `f_sdk_permission_prompt` `system` | 1.8.0 | 23/11/2026 |

Ghi thêm cho thư 3 (chi tiết theo game ở phép đếm của kho, không chép vào đây): (a) game gọi heartbeat định kỳ sẽ mất phần dòng
gọi ngoài lúc ra khỏi ngữ cảnh; (b) game đang không có heartbeat sẽ **bắt đầu có** — "có / không có heartbeat" qua mốc không so được; (c) QC toàn màn làm app
pause trên **cả** Android lẫn iOS (đã đo 11/11), nên mỗi lần chiếu QC toàn màn trong lượt có một dòng heartbeat ở cả hai nền tảng —
giống hành vi APS cũ, khác câu "trên iOS thì không" ở B5 của hợp đồng.

## 5. Chỗ code khác / chưa tới hợp đồng

| Mục | Ghi chú |
|---|---|
| S2 đường gửi khẩn | **Xác nhận §0.4: cùng endpoint, cùng envelope** với hàng đợi thường — 1 dòng gửi endpoint đơn (thân là wrapper `data` · `clientSendTime` · `event` · `packageName` · `platform`), nhiều dòng gửi endpoint batch (mảng wrapper đã chuỗi hoá), y như `LogSendService`. `uuid` · `createdDateLocal` · `clientSendTime` gán lúc sự cố |
| S2 dòng khẩn **không có** `logTypeCreateId` / `logTypeSendId` — **✅ loader 23/11: cứ để vắng, KHÔNG gửi `-1`** (Filter chỉ bắt buộc 5 khoá chung, hai cột silver cho phép NULL; `-1` lọt vào phép đếm lỗ `max − min + 1` và đẻ lỗ giả) | số thứ tự nằm trong pool có khoá (luồng canh không được đụng — main thread có thể đang đứng giữa khoá); đặt trước mỗi lần dựng bản chụp thì đốt số ⇒ lỗ giả trong phép "dãy có lỗ = mất khi gửi". Type registry khai hai cột là `RequireNumCol` trên `f_sdk_level_data`: **dòng vắng hai cột này có qua Parse không?** Không qua thì SDK đổi sang giá trị canh (vd `-1`) — loader chọn |
| S2 file khẩn | SDK **ghi file trước** rồi mới gửi (gửi được thì xoá) — rộng hơn "gửi hỏng thì ghi file". Chết sau khi gửi xong mà trước khi xoá ⇒ lần mở sau gửi lại cùng `uuid` (đã tính trong luật đọc §12 S2) |
| S2 gói hiệu năng trên dòng khẩn | của lúc dựng bản chụp (≤ 5 giây trước sự cố); chỉ `perf_fps_avg_5s` · `perf_frame_worst_ms_5s` · `perf_screen` — hai bộ đếm `_since_last` **vắng** (đếm ở dòng khẩn rồi log thường kế tiếp đếm lại là đếm đôi) |
| S2 sổ B6 trên heartbeat khẩn | khối đang chạy + `t_total_sec` + `t_speed_on_sec` + `sec_since_last_action` cộng nốt tới lúc sự cố; `t_ready_gap10/30/120_sec` là của lúc dựng bản chụp (không đoán khoảng đang dở có vượt mốc trong lúc đơ) |
| S2 `is_fatal` | `exception_hash` của dòng chết lấy từ `Exception.StackTrace` (định dạng .NET), dòng `exception` thường lấy từ stack của Unity ⇒ **cùng lỗi có thể khác hash** giữa hai loại. Unity có thể đưa cùng lỗi qua kênh log ⇒ có thể thêm một dòng `exception` thường cạnh dòng chết. `prev_exit` có `prev_fatal_*` ⇒ `abnormal` kể cả khi phiên trước đang nằm nền |
| S2 `is_loading` | SDK chỉ xét `perf_screen == loading` (cả `freeze` lẫn `freeze_long`), **chưa** xét "đang ở trạm tải B2" như hợp đồng ghi — game tải mà không báo màn `loading` thì `is_loading` vắng / `false` |
| S2 canh đơ | ngoài nền + QC toàn màn, SDK còn **tạm dừng canh lúc mất focus** (hộp thoại hệ thống) — đề phòng, chờ đo máy thật |
| S2 chưa đo máy thật | ngưỡng, `IsTerminating` trên IL2CPP, hộp thoại hệ thống — §11.1 mục 4: bật `is_test` trước |
| Đợt 2 — iOS | Phần native iOS (Devkit 1.4.0) **sau define `FALCON_DEVKIT_IOS_NATIVE`, mặc định tắt** — chưa build trên Mac. Tắt thì mọi field "(2)" vắng trên iOS. Bật rồi vẫn vắng trên iOS: `batteryPlug` · `batteryTempC` · `deviceRingerMode` · `deviceStorageFreeBucket` (⚖) · `msSinceProcessStart` (chờ soát "required reason API") · nguồn `activity_manager` |
| Đợt 2 — tự đọc quyền | "Chỉ bắn khi đổi" = đổi so với lần **đọc native** trước (lưu qua restart), không so với giá trị đã gửi — tránh lật qua lại khi game báo `denied` mà native đọc `not_determined`. Đọc lúc mở app, quay lại, lấy lại focus và mỗi ~10 giây (iOS đọc quyền thông báo bất đồng bộ ⇒ giá trị mới tới ở lần đọc kế, ≤ 10 giây; `permPushStatus` lần mở đầu có thể vắng) |
| Đợt 2 — van | Remote config `fDeviceSignalsEnabled` (mặc định **bật**): tắt thì mọi field "(2)" đọc định kỳ vắng (nhiệt độ, pin native, âm thanh, cấu hình, tự đọc quyền, `activity_manager`). SDK báo ngày tắt / bật theo game nếu có |
| Đợt 2 — dòng `system` tự bắn, trường hợp không có popup | push iOS `not_determined` → `provisional` **không** bắn (provisional có được không qua popup). ATT rời `not_determined` khi người dùng tắt công tắc toàn máy "cho phép app hỏi theo dõi" cũng không có popup — SDK không phân biệt được, vẫn bắn (hiếm) |
| Đợt 2 — `audioOutput` Android | suy từ thiết bị ra đang có (`getDevices`), ưu tiên bluetooth > wired > speaker — gần đúng, không đọc được đường đang phát thật |
| Đợt 2 — `prev_exit` | Android: mục `ApplicationExitInfo` khớp `pid` của phiên trước (dấu cũ không có pid thì mục có giờ ≥ `prev_last_alive_at`); foreground = importance ≤ 200 (VISIBLE). iOS: báo cáo MetricKit có `timeStampEnd` ≥ `prev_last_alive_at` − 60 giây; SDK chờ tối đa 10 giây sau khi mở app (hoặc tới lúc xuống nền) rồi mới quyết dòng `prev_exit` — chỉ khi nguồn iOS native bật; chết trong lúc chờ thì lần mở sau quyết (dòng tới trễ một lần mở). Mỗi mục / báo cáo báo một lần |
| Đợt 2 — `memory_critical` | chống ngập khoá theo `kind` **+ `source`** (một nguồn không chặn nguồn kia) |
| Đợt 2 — ngoại lệ Java | cùng đường `is_fatal` (ghi dấu → gửi khẩn), sau van `fAppIssueEmergencyEnabled`. Chuẩn hoá `exception_hash` thêm tiền tố không thuộc game: `java.` · `javax.` · `android.` · `androidx.` · `kotlin.` · `kotlinx.` · `dalvik.` · `com.android.` · `com.unity3d.` (stack C# không đổi ⇒ hash cũ giữ nguyên). Khung Java dạng `lớp.hàm(File.java:N)` |
| B6 | Giây là số thực 2 chữ số lẻ. `first_action_sec` / `sec_since_last_action` **vắng khi chưa có thao tác nào** (đã khai `cap_actions`). Chưa khai `busy` / `screen` thì SDK bỏ qua `SetBusy` / popup — thời gian rơi vào khối làm được, giữ bất biến tổng |
| B6 `n_offer_shown` | đếm `Ui.OnImpression` của `revive_offer` / `booster_offer` và `IapOffer.OnShown` khi có lượt mở |
| B14 cửa sổ bỏ dòng `system` tự bắn | **10 giây**: game / hàm bọc đã báo dòng `system` cùng `permission` trong 10 giây trước lúc SDK phát hiện đổi thì SDK không bắn. Dòng tự bắn: không `trigger`, không `systemResult` (kết quả đọc ở `f_sdk_permission_*`), chỉ iOS (push, ATT), khi native rời `not_determined` |
| B14 `push` | đính chính: module **Notification** có xin quyền thông báo (ticket `Request-Ump-Notification-ConsentPrompt.md` §B) |
| S2 `exception_hash` | FNV-1a 64 bit (hex 16 ký tự) của loại + 5 khung đầu sau chuẩn hoá (bỏ tiền tố `at ` của stack kiểu .NET, `(at …)`, `in <…>:N`, `[0x…]`, tham số, generic). `exception_top_frame` bỏ khung có tiền tố `UnityEngine.` · `UnityEditor.` · `Unity.` · `System.` · `Mono.` · `Microsoft.` · `Newtonsoft.` |
| S2 | Không chạy trong Editor. `prev_exit` không gửi ở lần đầu lên bản (chưa có dấu vòng đời) |
| B2 | "Cài mới" = lần đầu SDK có sổ mà phiên ≤ 1; máy cập nhật không có trạm nào. `config_ready` mất nếu remote config xong trước lúc SDK khởi tạo |

## 6. Việc sau khi ký (theo thư loader §5)

| Bên | Việc | Trạng thái |
|---|---|---|
| SDK | Sửa câu B5 "trên iOS thì không" trong bản chốt (loader mục 4.2) | ✅ 23/11 — `docs/semantic/hop-dong-sdk-client-3-final.md` §5.2 |
| SDK | Điền cột ngày §4 lúc publish 1.8.0 | ✅ 23/11/2026 (BigData 1.8.0 · APS 1.2.0 · Devkit 1.4.0) |
| SDK | Báo loader ngày bật / tắt van `fAppIssueEmergencyEnabled` / `fDeviceSignalsEnabled` và define iOS theo từng game | khi bật |
| SDK | Báo loader khi máy `is_test` bắt đầu gửi log 1.8.0 | khi bật thử |
| SDK | Giữ nguyên hai đường băm `exception_hash` (dòng chết / dòng thường); đổi thì gửi thư era | ghi nhận |
| SDK | Nối `is_loading` với trạm tải B2 ⇒ gửi thư era (tỉ lệ đơ lúc tải nhảy qua mốc) | khi làm |
| Loader | Đẩy seed rule lên Mongo trước khi SDK publish; đo dung lượng / RAM, `low_memory_background` máy yếu, DQ khi có log `is_test` | loader |
| Access | `ticket-access-blob-float-absent.md` để mở cột ảo sổ B6 | access |
