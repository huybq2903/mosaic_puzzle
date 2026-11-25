# Tích hợp BigData cho game mới — cầm tay chỉ việc

> Dành cho: **dev game** cài `Core/BigData` + `AnalyticParamsSync` lần đầu.
> Mục tiêu: biết log nào **tự có**, log nào **phải tự gọi**, gọi **ở đâu**, và **kiểm** thế nào.
> Mục có dấu 🆕 cần **BigData ≥ 1.7.3** (bản publish 12/11/2026, gồm cả 1.7.1 · 1.7.2). Riêng **B8** cần **BigData ≥ 1.7.4** và
> **AnalyticParamsSync ≥ 1.1.9** (13/11/2026). Booster chọn trước ván (A2) cần **Level Core ≥ 1.3.14**; dòng tài nguyên qua ví
> (A4) cần **FGameData ≥ 1.1.7**. Kiểm version trước khi dùng.
> ⚠ Phần **No Ads + ad đang chờ** của B8 (`AdSkipReason.NoAds`, tham số `adAvailable` / `readyAd`) cần **BigData ≥
> 1.7.5**. `readyAd`, dòng `no_ads` do Mediation ghi, và callback của GMA / IronSource ở bước ① còn cần **Mediation đã làm ticket
> ShowGate bản 2** (sau 1.4.42).
> Mục có dấu **🆕3** (hợp đồng đợt 3, 23/11/2026) cần **BigData ≥ 1.8.0** + **AnalyticParamsSync ≥ 1.2.0** (hai bản phải lên
> cùng nhau — xem A3) + **Devkit ≥ 1.4.0** (BigData tự kéo).

---

## 0. Đọc trước: cái gì đã tự có

`AnalyticParamsSync` kéo theo **Mediation, InAppPurchase, Level Core** làm dependency. Với bộ đó, những log sau **không phải viết dòng
nào**:

| Log | Ai bắn |
|---|---|
| `f_sdk_app_open_data` · `f_sdk_session_data` · `f_sdk_retention_data` | BigData, theo vòng đời app |
| `f_sdk_mmp_info_data` | AnalyticParamsSync |
| Field chung trên **mọi** dòng (`maxPassedLevel`, `totalPlayTime`, `sessionId`, `sdkVersion`…) | BigData + AnalyticParamsSync |
| Stream quảng cáo (request / show / impression / close / load) | Mediation — **chỉ khi** game chiếu QC qua `MediationManager` |
| 🆕3 Gói hiệu năng (fps 5 giây, khung chậm nhất, khung rớt, nhiệt độ máy, tiết kiệm pin) đính lên dòng level / QC / UI / IAP / funnel… — trừ `perf_screen` (bảng dưới) | BigData |
| 🆕3 `f_sdk_app_issue`: lỗi C# (`Debug.LogException`, lỗi không ai bắt), đơ ≥ 2 giây, thiếu bộ nhớ, phiên trước kết thúc bất thường | BigData |
| 🆕3 Pin + sạc (`f_sdk_battery_charging_change`), âm thanh máy (`f_sdk_device_audio_change`), cấu hình máy + trạng thái quyền trên `app_open` | BigData (+ Devkit native) |
| 🆕3 Trạng thái quyền ATT / thông báo: SDK tự đọc và tự gửi `f_sdk_permission_*` khi đổi | BigData |
| 🆕3 Trạm tải lần mở đầu `load_start` · `first_frame` · `config_ready` (`f_sdk_ftue_load_step`) | BigData |

**Tự có MỘT PHẦN — SDK lo thời điểm / phần tự đo được, game VẪN PHẢI khai phần còn lại** (không khai thì log vẫn bắn nhưng thiếu số):

| Log | SDK tự lo | Game vẫn phải làm | Không làm thì |
|---|---|---|---|
| 🆕3 Heartbeat level | **Lúc bắn**: app xuống nền giữa ván, `Level.OnPauseExit` | **Tham số tiến độ ván** (điểm, số nước, thời gian, booster…): kế thừa `ALevelHeartBeatService` (**A3**) hoặc gọi `Level.OnHeartbeat` | Dòng heartbeat chỉ có định danh ván + sổ thời gian, **không có tiến độ** |
| 🆕3 `perf_screen` (màn đang hiện) trên gói hiệu năng + `f_sdk_perf_screen` | Màn `ad` khi QC toàn màn chiếu | Báo màn / popup bằng `Surface.Enter` / `Exit` (**B9**) — SDK không lấy tên scene Unity | `perf_screen` vắng, không có `f_sdk_perf_screen`: không đo được fps theo màn |
| 🆕3 Sổ thời gian lượt chơi trên dòng level | Khối nằm nền, khối QC, tổng thời gian, x2 tự đo | Khai `Level.Declare` + `SetBusy` · `OnPlayerAction` · `OnIneffectiveTap` · popup (**B10**, **B9**) | Chỉ có `t_total_sec` / nền / QC; thời gian chờ hoạt hình và popup bị tính thành "người chơi làm được" |

Mọi thứ còn lại nằm ở các mục dưới.

> 🔴🔴 **GAME TỰ VIẾT MODULE THAY CHO MODULE FALCON** (quảng cáo, consent / ATT, thông báo, IAP, ví tài nguyên, level, đăng nhập…):
> bảng "tự có" ở trên **KHÔNG còn đúng** cho phần đó. Module Falcon đang gọi BigData thay cho game; bỏ module là bỏ luôn các lời gọi
> đó. **Game PHẢI tự gọi đủ** theo mục **[F](#f-game-tự-viết-module-thay-cho-module-falcon--bắt-buộc-đọc)** và phiếu tương ứng ở
> `docs/modules/`. **Thiếu dữ liệu vì không gọi là LỖI TÍCH HỢP CỦA GAME**, không phải lỗi SDK / kho — kho không bù được số đã mất.

## Checklist

| # | Mục | Bắt buộc? | Làm ở đâu |
|---|---|---|---|
| A1 | Khai loại tiền + mã booster | ✅ | Lúc khởi động, một lần |
| A2 | Level start / kết quả | ✅ | `FLevelManager` (Level Core) |
| A3 | Heartbeat level (log khi app bị vuốt giữa ván) | ✅ | Kế thừa `ALevelHeartBeatService` |
| A4 | Dòng tài nguyên (nhận / tiêu) | ✅ | Ví FGameData, hoặc `Resource.*` |
| A5 | FTUE 🆕 | ✅ | `Ftue.*` |
| A6 | IAP: offer + lượt mua | ✅ nếu game có IAP | Module `InAppLog-BigData` + `IapOffer.*` |
| B1 | Hồi sinh 🆕 | nếu có | `Ui.*` + `Level.OnRevive` |
| B2 | Booster giữa ván 🆕 | nếu có | `Ui.*` + `Level.OnBooster` |
| B3 | Popup / icon cần đo CTR | nếu cần | `Ui.*` |
| B4 | Mở app từ push / deeplink | nếu có | `App.ReportOpenSource` |
| B5 | Phễu riêng của game | nếu cần | `Funnel.*` |
| B6 | Tính năng có trạng thái / event live-op | nếu có | ⚠ **Hỏi DA trước** — mục B6 |
| B7 | Nhãn, tham số chung, event tuỳ biến | tuỳ chọn | `Label.*` · `Common.Set` · custom event |
| A7 | Rời ván dở / chơi tiếp 🆕3 | ✅ nếu game cho chơi tiếp ván dở | `Level.OnPauseExit` / `Level.OnResume` |
| B8 | Điểm chiếu interstitial, luật chặn quảng cáo của game, quyền No Ads 🆕 | ✅ mọi game có interstitial | `ShowInterstitial` ở mọi điểm chiếu · `Ad.OnShowSkipped` · `SetRemoveAds` |
| B9 | Màn + popup đang hiện 🆕3 | ✅ khuyên làm | `Surface.Enter` / `Exit` |
| B10 | Sổ thời gian lượt chơi 🆕3 | khuyên làm | `Level.Declare` · `SetBusy` · `OnPlayerAction` · `OnIneffectiveTap` |
| B11 | x2 / bỏ qua hoạt hình 🆕3 | nếu có | `Level.OnSpeedAction` |
| B12 | Cài đặt âm thanh / nhạc / rung / ngôn ngữ 🆕3 | ✅ nếu có màn cài đặt | `Setting.*` |
| B13 | Màn hỏi quyền của game, xin quyền thông báo 🆕3 | nếu có | `Consent.OnPrompt` · `Consent.RequestPush` |
| B14 | Liên kết tài khoản 🆕3 | nếu có | `Account.*` |
| B15 | Trạm tải của game, mức đồ hoạ, lý do chiếu QC 🆕3 | nếu có | `Ftue.OnLoadStep` · `App.SetGraphicsQuality` · tham số `adWhen` |
| **F** | **Game tự viết module thay module Falcon** | 🔴 **✅ bắt buộc nếu có** | Mục F + phiếu ở `docs/modules/` |

Viết tắt dưới đây: `BD = FalconBigDataController`.

---

## A. Bắt buộc

### A1. Khai loại tiền + mã booster

**Khi nào:** một lần lúc khởi động, **trước** dòng tài nguyên đầu tiên.

```csharp
BD.Resource.DeclareCurrency("gem",   FCurrencyClass.Hard);
BD.Resource.DeclareCurrency("coin",  FCurrencyClass.Soft);
BD.Resource.DeclareCurrency("heart", FCurrencyClass.Live);
BD.Resource.DeclareCurrency("hammer", FCurrencyClass.Booster);
BD.Resource.RegisterBooster("hammer");
```

- Hạng tiền là **tập đóng 9 giá trị** `FCurrencyClass`: `hard` · `soft` · `live` · `booster` · `unlimited` · `material` ·
  `event_counter` · `progress` · `ticket`.
- Mỗi booster: **khai cả hai** — `DeclareCurrency(id, Booster)` (hạng tiền) **và** `RegisterBooster(id)` (sổ mã booster). Khai booster
  không tự khai hạng, cố ý.
- Tên phải **giống hệt** chữ game dùng ở dòng tài nguyên và ở `boostersUsed` của dòng level.
- **Mạng / quyền vô hạn có thời hạn** (vd "vô hạn mạng 30 phút"): khai hạng `FCurrencyClass.Unlimited`. Dòng tài nguyên của nó ghi
  `amount` = **số giây** được cộng / bị trừ. Từ BigData 1.7.3, SDK tự **bỏ** `value_before` / `value_after` của hạng này (đó không phải
  số dư). ⚠ Không khai hạng thì SDK không nhận ra, và nếu game đang nhét **mốc hết hạn** vào ô số dư thì số rác đó vẫn lên dây.

**Đừng:** khai ở nhiều chỗ (khai trùng: giá trị đầu thắng, kèm cảnh báo).
**Kiểm:** gửi một dòng tài nguyên → log editor có `currencyClass` đúng hạng.

### A2. Level start / kết quả — qua `FLevelManager`

**Khi nào:** đầu mỗi ván và lúc ván kết thúc.

```csharp
FLevelManager.Instance.OnLevelStart(movesLimit: 30);

FLevelManager.Instance.OnLevelResult(
    win: true, time: playSec, booster2Number: boostersUsedInLevel, score: score, totalScore: total,
    coinSpend: coins, numberBuyMoreTime: buyMore, numberMove: moves);
```

- `booster2Number` (lên dây là `boostersUsed`): mã booster → **số LẦN DÙNG trong ván**, **KHÔNG gồm** booster chọn trước ván.
- **Booster chọn TRƯỚC ván:** truyền object level param — từ **Level Core 1.3.14**:
  ```csharp
  FLevelManager.Instance.OnLevelStart(new LevelStartParamV2 { preBoostersUsed = new() { ["hammer"] = 1 } }, movesLimit: 30);
  ```
  `preBoostersUsed` đi vào field thật của dòng Start (qua kiểm hợp lệ). Các field khác của object (`currentLevel`, `difficulty`…)
  bị bỏ — Level Core tự tính. Level Core 1.3.13 trở về trước: key chỉ lọt lên dây qua extras, không qua kiểm hợp lệ.
- `time` (giây chơi của ván) là số **bắt buộc** ở `FLevelManager`, nên `0` lên dây là `0` **thật**. Truyền đúng thời gian chơi; đừng
  truyền `0` cho có. (Gọi thẳng `BD.Level.OnPass/OnFail` thì bỏ trống `duration` là **vắng** — từ BigData 1.7.3.)
- 🔴 **Hồi sinh:** chỉ gọi kết quả thua khi người chơi đã **hết đường hồi sinh** (từ chối popup, hoặc hết lượt hồi sinh). Báo thua trước
  rồi mới mời hồi sinh là lượt chơi đã đóng — dòng hồi sinh sau đó không nối được về ván nào.

**Kiểm:** log editor có `f_sdk_level_data` với cùng `playTurnId` cho Start và kết quả.

### A3. Heartbeat level — kế thừa `ALevelHeartBeatService`

**Vì sao:** người chơi vuốt tắt app giữa ván thì không có dòng kết quả. Heartbeat gửi một dòng lúc app pause để ván đó không biến mất.

🆕3 **Từ BigData 1.8.0 + AnalyticParamsSync 1.2.0, SDK tự quyết lúc bắn heartbeat:** lúc app xuống nền khi đang có ván mở, và lúc
`Level.OnPauseExit()` (A7). Lớp dưới đây chỉ còn là **nguồn tham số** — SDK đọc các property của nó tại lúc bắn. Code game không đổi.
- 🔴 **Lên hai bản cùng nhau.** AnalyticParamsSync 1.2.0 đi với BigData cũ hơn 1.8.0 thì **mất hết heartbeat** (package.json của APS
  1.2.0 đã pin BigData 1.8.0).
- 🔴 **Property phải rẻ và không có side effect** (đọc biến, không tính toán nặng, không ghi gì): SDK gọi chúng trên main thread.
- Game tự gọi `FLevelManager.LevelHeartBeat` / `BD.Level.OnHeartbeat(param)` thì lời gọi đó **chỉ cập nhật tham số**, không sinh dòng.
  Muốn số trên heartbeat mới thì gọi mỗi khi tham số đổi.

```csharp
public class MyLevelHeartBeat : ALevelHeartBeatService
{
    public MyLevelHeartBeat(FLevelManager levelManager) : base(levelManager) { }

    public override TimeSpan CurrentLevelPlayTime => GamePlay.ElapsedInLevel;
    public override int CurrentLevelScore => GamePlay.Score;
    public override int CurrentLevelNumberMove => GamePlay.MovesUsed;
    public override int CurrentLevelTotalScore => GamePlay.TotalScore;
    public override Dictionary<string, int> CurrentLevelBoosterUsed => GamePlay.BoostersUsedInLevel;
    public override Dictionary<string, object> CurrentLevelExtraParams => null;
}
```

- 🔴 **Đặt lớp này ở assembly có chữ `Falcon` trong tên, hoặc ở `Assembly-CSharp`** (không asmdef). SDK tìm lớp bằng FReflection, mà
  FReflection **chỉ quét** hai loại assembly đó. Đặt trong asmdef tên khác (vd `Game.Runtime`) thì SDK **không thấy** — không lỗi, không
  cảnh báo, chỉ là không có heartbeat.
- Không có giá trị nào thì trả `0` / `null` — đó là câu trả lời của game; SDK không điền hộ.
- `CurrentLevelBoosterUsed`: cùng định nghĩa với A2.

**Kiểm:** vào ván, đưa app xuống nền → log editor có **một** dòng heartbeat với đúng `playTurnId` của ván (không phải hai).

### A4. Dòng tài nguyên — nhận / tiêu tiền và vật phẩm

**Cách 1 (khuyên dùng): ví FGameData.** Game cộng / trừ qua `ResourceCollector` thì dòng tài nguyên **tự có**, kèm số dư trước / sau:

```csharp
// FGameData ≥ 1.1.7: truyền ngữ cảnh qua tham số `param` (một ResourceParam)
ResourceCollector.Instance.ResourceAdd("coin", 50, data, where: "level_complete",
    param: new ResourceParam { resourceWhere = "level_complete", resourceWhen = FResourceWhen.LevelReward });

ResourceCollector.Instance.ResourceRemove("coin", 100, data, where: "shop",
    param: new ResourceParam { resourceWhere = "shop", resourceWhen = FResourceWhen.Shop });
```

- 🔴 **Có truyền `param` thì phải điền `resourceWhere` TRONG `param`**, cùng giá trị với `where`. Khi có `param`, cột `resourceWhere` lấy
  từ `param`, **không** lấy từ đối số `where` — bỏ trống là lên dây `"Unknown"`, không báo lỗi. (Không truyền `param` thì FGameData tự
  dựng từ `where`, nhưng khi đó **không có** `resourceWhen`.)
- `param` phải là `ResourceParam` (hoặc lớp con). Kiểu `FParam` khác thì bị nhét nguyên vào `extraMeta`, không vào cột nào.
- Những gì `param` chở được xuống dòng log: `resourceWhen`, `resourceWhere`, `exchangeId`, `adViewId`, `transactionId`, ngữ cảnh event
  (`LiveOps.Stamp`) và tính năng (`Feature.Stamp`). Số lượng, số dư, tên tiền vẫn lấy từ giao dịch của ví. Ngữ cảnh event / tính năng
  cần **BigData ≥ 1.7.2** (bản cũ hơn làm rơi bảy field đó).
- Một `param` dùng chung cho cả mẻ (vd trao nhiều phần thưởng cùng lúc) là được: SDK chép ra bản riêng cho từng dòng, không sửa object
  của game.
- ⚠ `itemId` bỏ trống thì FGameData điền bằng `where`. Truyền `itemId` thật nếu có.
- `where` bỏ trống thì FGameData điền tên scene hiện tại.
- FGameData **1.1.6** trở về trước không có tham số `param`: `resourceWhen` / `exchangeId` / ngữ cảnh event đều vắng hoặc `Unknown`.

**Cách 2: gọi tay** — khi không dùng ví FGameData, hoặc cho giao dịch hai vế:

```csharp
// Nhận một chiều (thưởng, quà)
BD.Resource.OnEarned(new ResourceParam { currency = "coin", amount = 50,
    resourceWhere = "level_complete", resourceWhen = FResourceWhen.LevelReward });

// Tiêu một chiều, thứ nhận về không đếm được (coin để hồi sinh)
BD.Resource.OnSpent(new ResourceParam { currency = "coin", amount = 200,
    resourceWhere = "revive_popup", resourceWhen = FResourceWhen.Revive });

// Đổi tài nguyên lấy tài nguyên (gem → booster): MỘT lời gọi, hai vế
BD.Resource.OnExchange(
    sink:   new ResourceParam { currency = "gem",    amount = 30, resourceWhen = FResourceWhen.Shop },
    source: new ResourceParam { currency = "hammer", amount = 1,  resourceWhen = FResourceWhen.Shop });
```

- `resourceWhen` dùng hằng số `FResourceWhen` (`shop` · `iap` · `level_reward` · `daily_bonus` · `reward` · `gift` · `play_cost` ·
  `booster_use` · `upgrade` · `revive`… và `event_<tên>` cho event).
- Đổi tài nguyên lấy tài nguyên **phải** đi `OnExchange` (hai vế mới nối được). Gọi `OnSpent` + `OnEarned` riêng là mất liên kết.
- 🔴 **Đừng log hai lần**: đã đi ví FGameData thì không gọi `Resource.*` cho cùng giao dịch đó.

**Kiểm:** log editor có `f_sdk_resource_data`, `currency` đúng tên đã khai ở A1.

### A5. FTUE 🆕

**Chỉ cho tutorial ONBOARDING.** Tutorial của từng tính năng (battle pass, event…) không phải FTUE.

```csharp
BD.Ftue.OnFirstScreen();                  // màn hình đầu tiên người chơi bấm được — không phải màn loading
BD.Ftue.OnTutorialStart("tut_v1");        // id phiên bản tutorial
BD.Ftue.OnTutorialStep(1, "first_tap");   // gọi khi bước HIỆN RA, không phải khi làm xong
BD.Ftue.OnTutorialStep(2, "first_match");
BD.Ftue.OnTutorialComplete();             // hoặc:
BD.Ftue.OnTutorialSkip();                 // người chơi bấm bỏ qua
```

- Cứ gọi mỗi lần chạy qua: **mỗi mốc SDK chỉ gửi một lần cho mỗi lần cài**.
- Đổi tutorial ở bản game sau → đổi `tutorialId` (`tut_v2`). Không đánh số lại bước của bản cũ.
- `stepIndex` ≥ 1, không cần liền mạch. Bỏ bước không bị chặn.
- `stepName`: snake_case, ổn định.
- **Đừng** dùng `Funnel` với tên `ftue` nữa (`FFunnelName.Ftue` đã lỗi thời). Game đang có funnel tutorial cũ: chuyển hẳn sang `Ftue.*`,
  **đừng gửi song song** — kho không cộng hai nguồn.

**Kiểm:** cài mới → chạy hết tutorial → log editor có đủ `f_sdk_ftue_*`; chạy lại tutorial → **không** có dòng mới.

### A6. IAP

**Bước 1 — cài module `InAppLog-BigData`.** Không có nó thì **không có dòng mua thành công** (`f_sdk_in_app_data`). Module này không nằm
trong dependency của `AnalyticParamsSync`, phải cài riêng.

**Bước 2 — lượt mua bắt đầu.** Gọi ngay **trước** khi mở cửa sổ thanh toán:

```csharp
new BigdataLogger().OnPurchaseStarted(product, where: "shop", why: "starter_pack");
```

Đi qua `BigdataLogger` để `productId` được dựng **cùng cách** với dòng mua thành công (`IsContainWhyInProductId`). Tự dựng chuỗi khác là
hai dòng không khớp nhau. Thiếu bước này thì phễu mua **không có mẫu số** và dòng thất bại không có ngữ cảnh.

**Bước 3 — offer hiện / bấm / đóng:**

```csharp
BD.IapOffer.OnShown(new IapOfferParam { offerId = "starter_pack", offerProductId = new[] { "com.x.starter" },
    offerCategory = "bundle", triggerType = OfferTriggerType.SystemTriggered, placementScene = "home" });
BD.IapOffer.OnClicked();   // bấm mua
BD.IapOffer.OnClosed();    // đóng offer, bất kể có mua hay không
```

**Kiểm:** mua thử ở editor → có `f_sdk_iap_offer_data`, `f_sdk_iap_start_purchase_data`, rồi `f_sdk_in_app_data` hoặc
`f_sdk_iap_purchase_fail_data`.

### A7. Rời ván dở / chơi tiếp 🆕3

**Khi nào:** game cho người chơi **thoát giữa ván để chơi tiếp sau** (về Home, đóng app rồi mở lại vào đúng ván cũ).

```csharp
// Người chơi bấm Home giữa ván, ván được lưu để chơi tiếp
var snapshot = BD.Level.OnPauseExit();      // trả null nếu không có ván mở
Save.PendingTurn = snapshot;                // lưu cùng save ván dở của game (serialize được, qua restart)

// Người chơi vào lại đúng ván đó
BD.Level.OnResume(Save.PendingTurn);        // KHÔNG gọi OnLevelStart cho ván đang chơi dở
```

- `OnPauseExit` bắn **một** heartbeat của đoạn vừa chơi rồi đóng đoạn; `OnResume` mở **đoạn mới** cùng `playTurnId`. Kết quả cuối
  (`OnLevelResult`) vẫn mang đúng `playTurnId` của ván.
- **Bỏ ván hẳn** (không chơi tiếp được) thì **đừng** dùng `OnPauseExit`: báo thua với lý do bỏ cuộc (`LevelFailReason.Quit`).
- Không có snapshot (save mất) thì cứ `OnLevelStart` như ván mới.

**Kiểm:** vào ván → Home → vào lại → thắng: có heartbeat lúc về Home, dòng Pass cùng `playTurnId`, `turn_segment_no = 2`.

---

## B. Theo tính năng

### B1. Hồi sinh 🆕

```csharp
// Popup mời hồi sinh: hiện / nhận / từ chối
BD.Ui.OnImpression(FUiSurface.ReviveOffer);
BD.Ui.OnClicked(FUiSurface.ReviveOffer);     // bấm nút hồi sinh
BD.Ui.OnDismissed(FUiSurface.ReviveOffer);   // bấm "không", đóng popup

// Lần hồi sinh ĐÃ XẢY RA
BD.Level.OnRevive(progressPct: 72f, payMethod: FPayMethod.Ad, reviveType: "extra_moves");
```

- `OnRevive` chỉ gọi khi hồi sinh **thật sự xảy ra**. Từ chối thì chỉ có `OnDismissed`.
- `payMethod`: `ad` · `currency` · `inventory` · `free`. Hồi sinh bằng tiền thì **thêm** dòng tài nguyên (A4, `resourceWhen = revive`).
- `reviveType`: tên loại do game đặt. Có nhiều mức thì đưa mức vào tên (`extra_moves_5`).
- Nhớ luật ở A2: chỉ báo thua **sau** khi hết đường hồi sinh.

### B2. Booster giữa ván 🆕

```csharp
BD.Ui.OnImpression(FUiSurface.BoosterOffer);   // popup gợi ý dùng booster (nếu có)
BD.Level.OnBooster(boosterId: "hammer", progressPct: 40f, payMethod: FPayMethod.Inventory);
```

- `payMethod` ở đây nghĩa là **booster có từ đâu**: `inventory` (có sẵn) · `currency` (mua tại chỗ) · `ad` · `free`.
- **Vẫn** đưa booster đó vào `boostersUsed` lúc kết thúc ván (A2). Hai chỗ trả lời hai câu khác nhau; kho dùng chúng để kiểm chéo.
- Booster chọn **trước** ván: không gọi `OnBooster` — đó là `preBoostersUsed` của dòng Start (xem ⚠ ở A2).

### B3. Popup / icon cần đo CTR

```csharp
BD.Ui.OnImpression("daily_gift_popup", trigger: "auto_on_home");   // bề mặt hiện ra
BD.Ui.OnClicked("daily_gift_popup", trigger: "auto_on_home");      // làm hành động chính
BD.Ui.OnDismissed("daily_gift_popup", trigger: "auto_on_home");    // 🆕 đóng không làm gì
```

- Gọi `OnImpression` khi bề mặt **thật sự hiện ra**, không phải mỗi frame. Gọi mỗi lần — SDK tự gộp cụm.
- **Cùng `trigger`** cho lần hiện và lần bấm / đóng tương ứng, không thì chúng rơi vào hai cụm.
- Nút xem QC hiện ra lúc QC chưa sẵn: `OnImpressionAdNotReady` (thay cho `OnImpression`, **không** gọi cả hai).
- App bị tắt ngang lúc bề mặt đang mở: **không** gọi gì.

### B4. Mở app từ push / deeplink

```csharp
BD.App.ReportOpenSource(OpenSource.Push, pushCampaignId: campaignId);
```

- 🔴 Phải gọi **trước** lúc dòng `app_open` bắn. Mở nguội (cold): gọi trong pha init (`RuntimeInitializeOnLoadMethod`). Quay lại từ nền
  (hot): gọi ngay trong callback nhận notification. Gọi muộn thì SDK bỏ qua, không dán sang lần mở sau.
- Không chắc nguồn thì **không gọi**. SDK không mặc định `icon`.

### B5. Phễu riêng của game

```csharp
BD.Funnel.Register("my_feature_onboarding", FunnelShape.OncePerCycle);       // một lần lúc khởi động
BD.Funnel.OnStep("my_feature_onboarding", "open", priority: 1, cycleId: "season_3");
```

- Dùng `OncePerCycle` + `cycleId`. `Repeatable` đã nghỉ hưu; `OnceOrdered` khoá theo số thứ tự, chèn bước là chặn trắng.
- Phễu là **MỐC** (đã tới bước này chưa), không phải máy đếm số lần — đếm thì dùng `Ui.*` (B3).
- Tên funnel mới phải **đăng ký với loader** trước, không thì kho không nhặt.

### B6. Tính năng có trạng thái / event live-op — ⚠ hỏi DA trước

> 🔴 **Hai mục này phức tạp và rất dễ sai im lặng.** Trước khi tự làm: **hỏi DA**, hoặc tìm một event / tính năng tương tự đã có doc ở
> `docs/games/` và làm theo đúng doc đó. Module event có sẵn (CardCollection, MasterLeague, KeyCollection…) đã tự log — đừng log thêm.

**Hợp đồng chung — live-op** (`docs/design/LiveOpsEventContract.md`, khuôn `docs/design/LiveOpsTrackingPattern.md`):
- Một event riêng `f_sdk_live_ops_data` cho mọi live-op; danh tính event nằm ở **giá trị** `liveOpsEvent`, không ở tên event.
- Sáu mốc: `eligible` · `open` · `join` · `milestone` · `complete` · `claim` (`FLiveOpsAction`).
- Khai lịch đợt **một lần** bằng `LiveOps.DeclareCycle(...)`; các mốc sau tự mang mã đợt, mốc mở / đóng.
- Dòng của stream khác (level, tài nguyên, IAP…) thuộc về event thì đóng ngữ cảnh bằng `LiveOps.Stamp(param, event, …)`.
- Key riêng của event phải có **tiền tố** tên event.

**Hợp đồng chung — tính năng** (`docs/design/FeatureContract.md`):
- `f_sdk_feature`: `Feature.State(...)` cho trạng thái (đang ở bang hội nào, tiến độ nào), `Feature.Step(...)` cho mốc.
- `Feature.Stamp(...)` đóng ngữ cảnh tính năng lên dòng của stream khác.
- `featureInstanceId`: `null` ("không có khái niệm thực thể") và `""` ("không ở thực thể nào") là **hai nghĩa khác nhau**.

### B7. Tuỳ chọn

`Label.*` (gán nhãn phiên / lượt / người chơi) · `Common.Set` (tham số chung trên mọi dòng) · custom event. **Tra hợp đồng trước khi
đặt tên key** — trùng tên một cột sẵn có là mất số im lặng (SDK cảnh báo ở log dev).

**Tên nhãn đăng ký sẵn** — có hằng số thì dùng hằng số, đừng gõ chữ:

| Hằng số | Chữ trên dây | Gắn lên | Ai gửi |
|---|---|---|---|
| `FUserLabelKey.ELO` | `elo` | người chơi | game |
| `FUserLabelKey.NO_ADS` | `no_ads` | người chơi | AnalyticParamsSync tự gửi (B8) |
| `FLevelLabelKey.MOVES_LIMIT` · `TIME_LIMIT_SEC` | `moves_limit` · `time_limit_sec` | lượt level | BigData tự gắn từ `movesLimit` / `timeLimitSec` của `OnLevelStart` (A2) |
| `FAdViewLabelKey.LOAD_ERROR` · `SHOW_ERROR` | `load_error` · `show_error` | lượt xem QC | BigData tự gắn khi Mediation báo `Ad.OnLoadFailed` / `Ad.OnShowFailed` |

⚠ Kho hôm nay mới **đọc** nhãn lượt level (`levelLabels`) và nhãn lượt xem QC. Nhãn **phiên / lượt / người chơi / IAP** gửi lên vẫn được
giữ nguyên, nhưng kho chưa dựng đường đọc — muốn dùng số đó thì báo loader trước.

### B8. Điểm chiếu interstitial, luật chặn quảng cáo của game, quyền No Ads 🆕

Kho cần biết **mọi** lần game tới một điểm chiếu interstitial (hết màn, về Home…): hoặc ad được chiếu (`f_sdk_ad_show_attempt`),
hoặc bị chặn và vì sao (`f_sdk_ad_show_skipped`, kèm ad đang nằm chờ lúc đó). Mỗi lần tới điểm chiếu có **đúng một** trong hai dòng,
không bao giờ cả hai. Phần lớn do Mediation ghi. Game chỉ phải làm đúng ba việc dưới đây.

**① Điểm chiếu nào cũng gọi `ShowInterstitial`**, kể cả khi `CanShowInterstitial` trả `false`:

```csharp
if (MediationManager.Instance.CanShowInterstitial("level_end"))
{
    ShowAdBreakPopup();                                                    // CanShowInterstitial CHỈ để quyết định popup
    MediationManager.Instance.ShowInterstitial("level_end", OnContinue, OnContinue);
}
else
{
    // Không chiếu gì: Mediation chỉ ghi lần này (bị chặn / không có ad) rồi gọi OnContinue.
    MediationManager.Instance.ShowInterstitial("level_end", OnContinue, OnContinue);
}
```

- ❌ **Đừng** viết `else OnContinue();` (mẫu cũ trong README Mediation). Viết thế thì lần bị chặn (No Ads, luật MO, Lua) hay lần không
  có ad đều **vô hình**: Mediation không được gọi nên không ghi gì.
- Truyền **cả** `onClosed` lẫn `onFail`, cùng một hàm đi tiếp. Bị chặn thì Mediation gọi `onClosed`; không có ad hoặc Lua từ chối thì
  gọi `onFail`. Thiếu `onFail` thì game có thể bị treo.
- Game dùng AdMob (GMA) hoặc IronSource: cần Mediation đã làm S5 của ticket ShowGate bản 2. Bản cũ có chỗ không gọi callback nào khi
  chưa init. Game dùng MAX thì làm được ngay.

**② Luật bớt quảng cáo của chính game** (không cấu hình trong MediationMO): game tự ghi, **trước** lời gọi `ShowInterstitial`:

```csharp
if (secondsSinceLastInter < cooldown)                                       // luật của GAME
{
    BD.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.Cooldown,
        adAvailable: MediationManager.Instance.IsInterstitialReady(),           // BigData ≥ 1.7.5
        readyAd: MediationManager.Instance.GetReadyInterstitialInfo());        // Mediation ShowGate bản 2
    OnContinue();
    return;
}
MediationManager.Instance.ShowInterstitial("level_end", OnContinue, OnContinue);
```

- Đang ở BigData 1.7.4 thì bỏ hai tham số cuối. BigData mới mà Mediation chưa có `GetReadyInterstitialInfo` thì chỉ truyền
  `adAvailable`, và thêm `readyAd` sau.
- `readyAd` tự trả `null` khi không có ad, nên cứ truyền thẳng. Game **không** tự dựng `AdReadyInfo`.
- Gọi **một lần mỗi cơ hội chiếu**, không gọi mỗi lần kiểm tra luật. Nhiều luật cùng chặn thì lấy luật **đầu tiên** theo thứ tự game xét.
- `adWhere` phải **trùng** chữ game truyền làm `placementId` khi gọi `ShowInterstitial`, để kho ghép được với `show_attempt`.
- Chỉ dùng cho `Interstitial` (app open do Mediation lo). Rewarded và banner không thuộc event này.

**③ Quyền No Ads:** báo cho Mediation, đừng tự chặn.

```csharp
MediationManager.Instance.SetRemoveAds(true);    // mua gói / khôi phục / thuê bao còn hạn
MediationManager.Instance.SetRemoveAds(false);   // thuê bao hết hạn, hoàn tiền
```

- Có `SetRemoveAds` thì cứ gọi `ShowInterstitial` cho user No Ads. Mediation tự bỏ qua (gọi `onClosed`) và tự ghi dòng `no_ads`
  (ShowGate bản 2). Có thêm AnalyticParamsSync thì nhãn `no_ads` của người chơi cũng tự gửi.
- Game **buộc** phải giữ cờ riêng mà Mediation không biết thì tự chặn và tự ghi, đặt **đầu tiên**, trước mọi luật khác:

  ```csharp
  if (UserHasNoAds())
  {
      BD.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.NoAds,
          adAvailable: MediationManager.Instance.IsInterstitialReady(),
          readyAd: MediationManager.Instance.GetReadyInterstitialInfo());
      OnContinue();
      return;
  }
  ```

  Khi đó, nếu không có AnalyticParamsSync, game tự gắn nhãn: `BD.Label.User(FUserLabelKey.NO_ADS, true)` lúc cấp quyền, `null`
  lúc thu quyền.
- 🔴 Không bao giờ ghi **cả hai**: game đã tự ghi `NoAds` thì không gọi `ShowInterstitial` nữa.

**🔴 Đừng ghi thay Mediation.** Không gọi `OnShowSkipped` cho:
- luật cấu hình trong **MediationMO** (cooldown, trần ngày / phiên / level, level mở khoá…) và script **Lua** (`IsShowInterstitial`):
  chúng chặn **bên trong** `ShowInterstitial`, Mediation ghi. Game ghi nữa là đếm đôi;
- **app open**: lúc app quay lại là việc của Mediation;
- **rewarded**, **banner**.

**Kiểm:** bật log BigData, rồi xem bảng sau.

| Làm | Phải thấy |
|---|---|
| Tới điểm chiếu, ad có sẵn, không bị chặn | Đúng **một** `f_sdk_ad_show_attempt` |
| Luật **của game** chặn | Đúng **một** `f_sdk_ad_show_skipped` với lý do của game, `adAvailable`; có `adNetwork` / `adUnitId` / `estimatedEcpm` khi có ad. **Không** có `show_attempt` |
| `CanShowInterstitial = false` (vd. còn cooldown của MO) | Game đi tiếp, không treo; có **một** `show_skipped` (Mediation ghi) hoặc **một** `show_attempt` với `adAvailable = false` |
| User No Ads | Đúng **một** dòng `skipReason = no_ads` (từ Mediation, hoặc từ game nếu game giữ cờ riêng) |
| Log có cảnh báo *"thiếu adAvailable"* | Còn chỗ gọi `OnShowSkipped` chưa truyền `adAvailable` |

Bất biến theo từng điểm chiếu: **số lần tới điểm chiếu = số `show_attempt` + số `show_skipped`.**
Chi tiết và bối cảnh: `games/Request-Game-AdShowSkipped.md`.

### B9. Màn + popup đang hiện 🆕3

SDK **không** lấy tên scene của Unity (nhiều game một scene nhiều màn; popup không phải scene). Game báo màn bằng tên chung:

```csharp
BD.Surface.Enter("loading");                          // màn nền: loading · home · level · shop · … (snake_case, ổn định)
BD.Surface.Enter("home");                             // vào màn nền mới = bỏ cả chồng popup cũ
BD.Surface.Enter("popup:daily_gift");                 // popup: "popup:<surface_id>", cùng id với Ui.* (B3)
BD.Surface.Enter("popup:tutorial_hint", PopupKind.Tutorial);  // popup hướng dẫn: PHẢI khai loại
BD.Surface.Exit();                                    // đóng popup trên cùng
```

- Dùng cho `perf_screen` (fps theo màn, dòng `f_sdk_perf_screen` mỗi lần rời màn) và khối popup của sổ lượt (B10).
- `PopupKind`: `Offer` (mời hồi sinh / booster / offer IAP — `popup:revive_offer` / `popup:booster_offer` SDK tự xếp) · `Tutorial`
  (game phải khai) · `Other` (mặc định: pause, settings…).
- Màn `ad` (QC toàn màn) SDK tự đặt — game **không** báo.
- Không gọi gì thì `perf_screen` vắng: các phép đo theo màn không dùng được.

### B10. Sổ thời gian lượt chơi 🆕3

SDK tự chia thời gian một ván thành: nằm nền · QC · popup · **game bận** · **người chơi làm được**, cộng các khoảng không thao tác.
Game chỉ báo những gì SDK không tự thấy.

```csharp
// Một lần lúc khởi động: khai game SẼ báo gì (khai true mà không báo thì số sai — khai false thì field vắng)
BD.Level.Declare(busy: true, actions: true, ineffectiveTap: false, speed: true, screen: true);

BD.Level.SetBusy(true);       // bắt đầu lúc người chơi phải CHỜ (hoạt hình rơi khối, combo, không có gì để bấm)
BD.Level.SetBusy(false);      // hết chờ, người chơi bấm được
BD.Level.OnPlayerAction();    // mỗi thao tác CÓ TÁC DỤNG (đặt khối, nối ô, đi một nước)
BD.Level.OnIneffectiveTap();  // chạm không có tác dụng (nếu game bắt được)
```

- `screen: true` nghĩa là game làm B9 cho popup trong ván. `speed: true` nghĩa là game làm B11.
- Thời gian **làm được** là phần còn lại — game không báo. Thiếu `SetBusy` thì thời gian chờ bị tính thành "làm được".
- QC, nằm nền: SDK tự thấy (QC qua Mediation).
- Ghi vào tài liệu của game **một thao tác là gì** (DA cần để đọc `n_actions`).

**Kiểm:** chơi một ván → dòng Pass có `t_total_sec` ≈ thời gian thật, các `t_*_sec` cộng lại bằng `t_total_sec`, có `cap_*` đúng như
`Declare`.

### B11. x2 / bỏ qua hoạt hình 🆕3

```csharp
BD.Level.OnSpeedAction(SpeedAction.SpeedOn, AnimType.Gameplay);            // bật x2
BD.Level.OnSpeedAction(SpeedAction.SpeedOff, AnimType.Gameplay);           // tắt x2
BD.Level.OnSpeedAction(SpeedAction.SkipAnim, AnimType.Reward, "chest_open", secIntoAnim: 1.2f);  // bỏ qua hoạt hình
BD.Level.OnSpeedAction(SpeedAction.SpeedTapNoStock, AnimType.Gameplay);    // bấm x2 khi hết lượt dùng
```

`AnimType`: `LevelIntro` · `Gameplay` · `LevelResult` · `Reward` · `Chest` · `Other`.

### B12. Cài đặt âm thanh / nhạc / rung / ngôn ngữ 🆕3

```csharp
BD.Setting.DeclareDefaults(sound: true, music: true, haptic: true);       // một lần lúc khởi động: mặc định của game
BD.Setting.Sync(sound: save.Sound, music: save.Music, haptic: save.Haptic, language: "vi");  // trạng thái đọc từ save, mỗi lần mở app

BD.Setting.Set(FSetting.Music, false);                                     // người chơi tắt nhạc trong màn cài đặt
BD.Setting.Set(FSetting.Sound, false, MuteReason.PublicPlace);             // nếu game có hỏi lý do tắt tiếng
BD.Setting.SetLanguage("en");                                              // người chơi đổi ngôn ngữ
```

- `Sync` không sinh dòng (không phải người chơi đổi). `Set` / `SetLanguage` mới sinh `f_sdk_setting_change`.
- Âm lượng / chế độ chuông / tai nghe của **máy**: SDK tự đọc, game không làm gì.

### B13. Màn hỏi quyền của game, xin quyền thông báo 🆕3

```csharp
// Màn "hỏi trước" của game (trước popup OS)
BD.Consent.OnPrompt(PromptPermission.Push, PromptKind.Soft, PromptTrigger.LevelWin, SoftResult.Accept);

// Android: xin quyền thông báo QUA HÀM BỌC — SDK tự ghi dòng popup OS + kết quả
BD.Consent.RequestPush(PromptTrigger.LevelWin, granted => { /* granted: true / false / null (không cần xin) */ });
```

- Popup OS mà game gọi **không** qua `RequestPush` (Android) thì game tự báo:
  `BD.Consent.OnPrompt(PromptPermission.Push, PromptKind.System, PromptTrigger.FirstOpen)`.
- iOS (push, ATT): SDK tự thấy lần đổi trạng thái và tự ghi dòng popup OS. Game báo thêm chỉ khi muốn có `trigger`.
- Trạng thái quyền (`f_sdk_permission_*`): SDK tự đọc. `App.ReportAttStatus` / `ReportPushPermission` cũ vẫn chạy, không cần gọi thêm.

### B14. Liên kết tài khoản 🆕3

```csharp
BD.Account.OnLink(AccountProvider.GooglePlayGames, AccountLinkAction.Success, AccountInitiatedBy.User,
    AccountLinkTrigger.Settings);
BD.Account.SetLinkedProviders(AccountProvider.GooglePlayGames, AccountProvider.Facebook);   // danh sách đang liên kết, mỗi lần đổi
```

Module AccountData / AccountBinding sẽ tự gọi (ticket `modules/Request-AccountData-Link.md`) — game dùng module đó thì chờ, đừng gọi trùng.

### B15. Trạm tải của game, mức đồ hoạ, lý do chiếu QC 🆕3

```csharp
// Trạm tải (chỉ lần mở đầu sau cài + lần vào level đầu; SDK tự bỏ lần sau)
BD.Ftue.OnLoadStep(LoadKind.App, LoadStep.AssetDownloadStart);
BD.Ftue.OnLoadStep(LoadKind.App, LoadStep.AssetDownloadDone, assetBytes: bytes);
BD.Ftue.OnLoadStep(LoadKind.Level, LoadStep.LevelLoadStart);
BD.Ftue.OnLoadStep(LoadKind.Level, LoadStep.LevelReady);            // lúc người chơi BẤM ĐƯỢC
BD.Ftue.OnLoadStep(LoadKind.App, LoadStep.LoadError, errorCode: "cdn_timeout");

// Mức đồ hoạ người chơi chọn (nếu game có chỉnh) — mỗi lần đổi
BD.App.SetGraphicsQuality("high");
```

**Lý do chiếu QC** (sau khi Mediation làm ticket `modules/Request-Mediation-AdWhenTrigger.md`): truyền thêm hai tham số cuối khi chiếu.

```csharp
MediationManager.Instance.ShowInterstitial("level_end", onClosed, onFail,
    adWhen: FAdWhen.LevelWin, triggerPlayTurnId: BD.Level.LastEndedTurnId);
MediationManager.Instance.ShowRewardedVideo("revive", onReward, onFail,
    adWhen: FAdWhen.Revive);                                         // trong ván: không cần triggerPlayTurnId
```

- `FAdWhen`: `LevelWin` · `LevelFail` · `LevelStart` · `Retry` · `Revive` · `Booster` · `Home` · `AppOpen` · `Other`.
- QC **sau** khi ván kết thúc: `triggerPlayTurnId = BD.Level.LastEndedTurnId`. Để kho không tính nhầm thời gian QC đó vào ván sau.
- Mediation chưa có hai tham số: gọi `BD.Ad.UpdateContext(AdType.Interstitial, adWhen: …, triggerPlayTurnId: …)` ngay trước khi chiếu.

---

## C. Không phải việc của game (đang chờ module khác)

| Log | Chờ ai |
|---|---|
| `f_sdk_ad_show_attempt` (game muốn chiếu QC) | Mediation — `modules/Request-Mediation-AdEventV2.md` |
| `f_sdk_ad_show_skipped` cho luật của **MediationMO** + `no_ads` khi game gọi `ShowInterstitial` cho user No Ads + app open MAX + hàm "ad đang chờ" (`GetReadyInterstitialInfo`) | Mediation — `modules/Request-Mediation-ShowGate.md` (bản 2, 13/11) |
| `f_sdk_permission_att` · `_ads_consent` · `_push` | Ump + Notification — `modules/Request-Permission-AttConsentPush.md` |
| 🆕3 `adWhen` + `triggerPlayTurnId` trên `ShowInterstitial` / `ShowRewardedVideo` (B15) | Mediation — `modules/Request-Mediation-AdWhenTrigger.md` |
| 🆕3 Trạm `consent_shown`, dòng popup form đồng ý quảng cáo, dòng popup xin quyền thông báo của module Notification | Ump + Notification — `modules/Request-Ump-Notification-ConsentPrompt.md` |
| 🆕3 Dòng liên kết tài khoản (B14) | AccountData / AccountBinding — `modules/Request-AccountData-Link.md` |

## D. Kiểm tích hợp

1. **Log editor:** mỗi dòng gửi đi in `<event>:{json}` qua `AnalyticLogger`. Cảnh báo của SDK (sai vocab, thiếu field…) cũng in ở đó —
   **đọc cảnh báo**, SDK không chặn dữ liệu nên lỗi chỉ lộ ở đây.
2. Chạy trong editor / bản dev: dòng mang `isTest` và vào bảng riêng của kho — kiểm thoải mái, không làm bẩn số thật.
3. Sau khi lên bản thật: nhờ **DA** kiểm DQ (mỗi người mới có `f_sdk_ftue_first_screen`, dòng booster khớp `boostersUsed`, tài nguyên
   có `currencyClass`…).
4. 🆕3 Kiểm nhanh đợt 3 trong editor:
   - xuống nền giữa ván → **một** heartbeat (A3); về Home rồi vào lại → dòng kết quả có `turn_segment_no = 2` (A7);
   - đổi màn / mở popup → `perf_screen` trên log đổi theo (B9); rời màn → có `f_sdk_perf_screen`;
   - dòng Pass / Fail có `t_*_sec` và `cap_*` (B10);
   - `Debug.LogException(new Exception("test"))` → dòng `f_sdk_app_issue` `kind = exception` **chỉ trên máy thật** (editor tắt phần này).
   - Field native (nhiệt độ máy, âm thanh máy, cấu hình máy, trạng thái quyền) **vắng trong editor** — kiểm trên máy Android.

## E. Van cấu hình (việc của owner SDK / live-ops, không phải code game) 🆕3

| Van | Mặc định | Bật / tắt gì |
|---|---|---|
| Remote config `fAppIssueEmergencyEnabled` | **tắt** | đơ ≥ 5 giây gửi ngay, lỗi làm chết app, heartbeat gửi khẩn, làm mới tham số A3 mỗi 10 giây. Bật thử trên máy test trước |
| Remote config `fDeviceSignalsEnabled` | bật | đọc native định kỳ (nhiệt độ, pin, âm thanh, cấu hình, trạng thái quyền, bộ nhớ hệ thống) |
| Scripting define `FALCON_DEVKIT_IOS_NATIVE` | **tắt** | toàn bộ phần native iOS — cần build thử trên Mac trước khi bật |

Bật / tắt van nào cho game nào thì **báo loader ngày bật** (kho không suy được từ version SDK).

## F. Game tự viết module thay cho module Falcon — BẮT BUỘC ĐỌC

> 🔴 **Phiếu ở `docs/modules/` viết cho owner module Falcon, nhưng là đặc tả CHÍNH XÁC những lời gọi BigData mà module đó phải có.**
> Game tự viết module cùng chức năng thì **game là owner** của các lời gọi đó: phải đọc phiếu, gọi **đủ, đúng chỗ, đúng thời điểm**
> như phiếu ghi. Phiếu nói "sửa file X dòng Y của module Falcon" thì game tìm chỗ tương ứng trong code của mình.
>
> **Thiếu dữ liệu vì không gọi / gọi sai = LỖI TÍCH HỢP CỦA GAME.** SDK không chặn, không tự bù, kho không dựng lại được số đã mất;
> bản phát hành mang lỗi đó là mất số vĩnh viễn cho mọi người chơi của bản đó. Không chắc thì **hỏi owner BigData trước khi phát hành**.

Spec chung của mọi event (tên field, kiểu, nghĩa): `docs/design/DWH-EventLog-API-Spec.md`. Hướng dẫn tổng: `docs/design/TrackingGuide.md`.

| Thay module Falcon | Module Falcon đang làm hộ game | Game tự viết thì PHẢI gọi | Phiếu PHẢI đọc (`docs/modules/`) | Thiếu thì mất |
|---|---|---|---|---|
| **Mediation** (`Core/ThirdParty/Mediation`) — chiếu QC MAX / AdMob / IronSource | Cả stream QC: request · load · show attempt · show · impression · close · show fail · bị chặn; eCPM, sàn, mạng, ad unit; ngữ cảnh lần chiếu | `BD.Ad.*`: `OnRequested` · `OnLoadResult` · `OnShowAttempt` · `OnShown` · `OnImpression` · `OnClosed` · `OnShowFailed` · `OnShowSkipped` · `UpdateContext(adWhen, triggerPlayTurnId)` | **Bắt đầu từ** `Request-Mediation-AdLifecycle.md` (khung chung), rồi `Request-Mediation-AdEventV2.md` · `Request-Mediation-AdEcpmAndParams.md` · `Request-Mediation-NativeAndAdType.md` · `Request-Mediation-ShowGate.md` · `Request-Mediation-AdWhenTrigger.md`; có chọn giá sàn theo Rule thì thêm `Request-Mediation-FirstInterFloorRule.md`. Mục B8 của tài liệu này | Doanh thu QC theo người chơi (ad LTV), tỉ lệ chiếu, lần bị chặn, `first_ad_ecpm`, thời gian QC trong sổ lượt chơi (B10), QC theo lượt chơi |
| **Ump** (`Core/ThirdParty/Ump`) — form đồng ý quảng cáo (CMP / GDPR) + popup ATT | Trạng thái `ads_consent` / `att`; dòng popup hiện ra; trạm tải `consent_shown` | `BD.App.ReportAdsConsent` · `BD.App.ReportAttStatus` · `BD.Consent.OnPrompt(AdsConsent / Att, System, …)` · `BD.Ftue.OnLoadStep(App, ConsentShown)` | `Request-Permission-AttConsentPush.md` §A (trạng thái) + `Request-Ump-Notification-ConsentPrompt.md` §A (popup) | Tỉ lệ đồng ý consent / ATT, phễu tải lần mở đầu (đoạn chờ form CMP) |
| **Notification** (`Modules/Notification`) — xin quyền thông báo + hẹn giờ thông báo | Xin quyền thông báo lúc mở app | **Android:** xin quyền qua `BD.Consent.RequestPush(trigger, cb)` (không tự gọi `PermissionRequest`) — hoặc tối thiểu `BD.Consent.OnPrompt(Push, System, trigger)` ngay trước khi xin. **iOS:** `OnPrompt` trước `AuthorizationRequest` | `Request-Permission-AttConsentPush.md` §B + `Request-Ump-Notification-ConsentPrompt.md` §B | 🔴 Android 13+: người chơi **đã từ chối hẳn** bị đọc thành **"chưa được hỏi"** (`not_determined`) — SDK chỉ tách được hai ca nhờ lời gọi này. Tỉ lệ đồng ý popup |
| **Push / deeplink** — mở app từ thông báo, link | Nguồn mở app | `BD.App.ReportOpenSource(...)` **trước** lúc `app_open` bắn (mục B4) | `Request-Push-OpenSource.md` | Push / deeplink kéo về bao nhiêu người chơi |
| **InAppPurchase + `InAppLog-BigData` + `InAppValidation`** — mua IAP | Dòng mua thành công / bắt đầu / thất bại, `purchaseAttemptId`, `productId` dựng thống nhất | Theo mục A6 + phễu checkout `start → success / fail`, chung `purchaseAttemptId` | `Request-Iap-CheckoutFunnel.md` + mục A6 | Doanh thu IAP, phễu mua (bao nhiêu người mở thanh toán rồi bỏ), offer nào ra tiền |
| **FGameData / Inventory / Shop / Lives / Packs** — ví tiền, vật phẩm, mạng, quà pack | Dòng tài nguyên nhận / tiêu kèm số dư, `resourceWhen` / `resourceWhere`, ngữ cảnh pack | `BD.Resource.OnEarned` · `OnSpent` · `OnExchange` (mục A4 cách 2) + khai hạng tiền (A1) | `Request-Inventory-ResourceFlow.md` · `Request-Packs-RewardContext.md` + mục A1 · A4 | Toàn bộ kinh tế trong game (nguồn / tiêu tiền, lạm phát, booster) |
| **Level Core** (`FLevelManager`) — vòng đời ván | Dòng Start / kết quả, `playTurnId`, booster chọn trước ván, nhãn màn | `BD.Level.OnStart` · `OnPass` · `OnFail` · `OnPauseExit` / `OnResume` · `OnRevive` · `OnBooster` (mục A2 · A7 · B1 · B2) | Mục A2 · A7 của tài liệu này | Mọi số theo ván: tỉ lệ thắng, độ khó, thời gian ván, mọi log khác gắn theo lượt |
| **AnalyticParamsSync** — heartbeat, nhãn No Ads, MMP | Tham số heartbeat (A3), nhãn người chơi `no_ads`, `f_sdk_mmp_info_data` | Heartbeat: cài `ILevelTurnParamsProvider` hoặc gọi `BD.Level.OnHeartbeat(param)` mỗi khi tham số ván đổi. No Ads: `BD.Label.User(FUserLabelKey.NO_ADS, …)` (B8 ③) | Mục A3 · B8 của tài liệu này | Tiến độ ván lúc người chơi bỏ giữa chừng, tách người chơi có / không QC |
| **AccountData / AccountBinding** — đăng nhập / liên kết tài khoản | (chưa — phiếu đang chờ module) | `BD.Account.OnLink(...)` · `BD.Account.SetLinkedProviders(...)` (mục B14) | `Request-AccountData-Link.md` | Tỉ lệ liên kết tài khoản sớm (tín hiệu "định chơi lâu") |
| **Mmp** (AppsFlyer / Adjust) | Tham số attribution trên mọi dòng (qua `IFCustomInfoRepository`: `AppsFlyerInfoService` / `AdjustInfoService`) | Tự viết lớp `IFCustomInfoRepository` cấp cùng key (xem hai lớp đó trong `Core/ThirdParty/Mmp`) — hỏi owner BigData danh sách key trước | *(chưa có phiếu — hỏi owner BigData)* | Nối người chơi với nguồn cài (campaign, media source) |
| **Event live-op** (`Modules/Events/*`) | `f_sdk_live_ops_data`, ngữ cảnh event trên log khác | `BD.LiveOps.*` (mục B6) | `docs/design/LiveOpsEventContract.md` + `LiveOpsTrackingPattern.md` + doc event tương tự ở `docs/games/` | Số tham gia / hoàn thành / nhận thưởng của event |

**Cách kiểm khi game tự viết module:** chạy cùng kịch bản với một game dùng module Falcon (nếu có), so log editor từng event — **cùng
event, cùng field, cùng thời điểm**. Lệch chỗ nào là thiếu chỗ đó. Cảnh báo của SDK trong log dev (`AnalyticLogger`) phải được đọc hết.
