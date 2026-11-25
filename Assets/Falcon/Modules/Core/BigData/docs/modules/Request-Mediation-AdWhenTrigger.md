# Mediation — thêm `adWhen` + `triggerPlayTurnId` vào hàm chiếu QC

> Gửi owner `Core/ThirdParty/Mediation` (đối chiếu 1.4.42). Người gửi: owner `Core/BigData`. Căn cứ: hợp đồng BigData đợt 3, B12
> (`hop-dong-sdk-client-3-final.md`, ba bên ký 23/11). **Sửa logic trong module hiện có, không mang code từ hệ module mới sang.**

## Vì sao

- `ad_when` (QC chiếu vì sao) đang là `Unknown` trên gần như mọi dòng QC. Gốc: `ShowInterstitial(placementId, …)` /
  `ShowRewardedVideo(placementId, …)` **không có tham số nào** để game nói lý do. Đường vòng hiện có (`Ad.UpdateContext` trước khi
  chiếu) gần như không game nào biết.
- Kho cần thêm **lượt chơi nào kích hoạt lần chiếu** (`trigger_play_turn_id`), để nối Inter sau thắng / thua về đúng lượt — server không
  suy được (thứ tự gọi lệch theo game).
- SDK BigData **1.8.0** **đã có** sẵn: hằng `FAdWhen.*`, tham số `triggerPlayTurnId` trên `Ad.OnShowAttempt` / `Ad.OnShown` /
  `Ad.OnShowSkipped` / `Ad.UpdateContext`, và `Level.CurrentTurnId` / `Level.LastEndedTurnId`.

## Việc phải làm

- [ ] **M1.** Thêm hai tham số tuỳ chọn **ở CUỐI** (code game cũ vẫn biên dịch):

  ```csharp
  public void ShowInterstitial(string placementId, Action onInterstitialClosed = null, Action onFail = null,
      bool needAdBreak = false, string adWhen = null, string triggerPlayTurnId = null)

  public void ShowRewardedVideo(string placementId, Action onRewardedComplete = null, Action onFail = null,
      bool showInterstitialInstead = true, string adWhen = null, string triggerPlayTurnId = null)
  ```

- [ ] **M2.** Ngay đầu hai hàm (trước nhánh kiểm tra luật / kho), khai ngữ cảnh cho lần chiếu:

  ```csharp
  FalconBigDataController.Ad.UpdateContext(AdType.Interstitial, adWhen: adWhen, triggerPlayTurnId: triggerPlayTurnId);
  ```

  (`AdType.Reward` với hàm rewarded.) Mọi mốc sau của lần chiếu (show_attempt, show_skipped, show, impression, close) SDK tự mang hai
  giá trị này — **không** phải truyền lại ở từng chỗ gọi `OnShowAttempt` / `OnShown`.

- [ ] **M3.** Nhánh `showInterstitialInstead` (rewarded không sẵn, chiếu Inter thay): gọi `UpdateContext(AdType.Interstitial, …)` với
  cùng hai giá trị trước khi chuyển sang Inter.

- [ ] **M4.** Pin `falcon.modules.core.bigdata` lên bản BigData đầu tiên có `triggerPlayTurnId` (**1.8.0**).

- [ ] **M5.** README: ví dụ gọi chiếu dùng hằng có sẵn:

  ```csharp
  MediationManager.Instance.ShowInterstitial("level_end", onClosed, onFail,
      adWhen: FAdWhen.LevelWin, triggerPlayTurnId: FalconBigDataController.Level.LastEndedTurnId);
  MediationManager.Instance.ShowRewardedVideo("revive", onReward, onFail,
      adWhen: FAdWhen.Revive, triggerPlayTurnId: FalconBigDataController.Level.CurrentTurnId);
  ```

## Kiểm

- Chiếu Inter với `adWhen = FAdWhen.LevelWin`: dòng `f_sdk_ad_show_attempt`, `f_sdk_ad_show_data`, `f_sdk_ad_impression`,
  `f_sdk_ad_close_data` cùng mang `adWhen = level_win` và `triggerPlayTurnId`.
- Không truyền: hành vi như cũ (`Unknown` trên impression, log dev BigData cảnh báo).

## Bối cảnh

- Danh sách `FAdWhen`: `level_win` · `level_fail` · `level_start` · `retry` · `revive` · `booster` · `home` · `app_open` · `other` ·
  `Unknown`. Ngoài danh sách vẫn gửi nguyên (chỉ cảnh báo).
- QC trong lượt (hồi sinh, booster): `triggerPlayTurnId = Level.CurrentTurnId`. Inter sau thắng / thua:
  `Level.LastEndedTurnId`. QC không do lượt nào (mở app, home, shop): để trống.
- Sổ thời gian lượt chơi (B6) dùng hai giá trị này để quyết QC nào được tính vào lượt — truyền sai là sai cả khối QC của lượt.
