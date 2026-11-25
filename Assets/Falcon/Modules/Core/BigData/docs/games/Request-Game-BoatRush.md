# Tracking Boat Rush — TODO cho dev + code dán vào

> **Dev chỉ cần đọc §1 (checklist) rồi dán code ở §2–§5.** Tick `[x]` khi xong.
> Viết 05/11/2026. API: `FalconBigDataController.LiveOps` / `.Ui` / `.Resource`
> (BigData, hợp đồng `docs/design/LiveOpsEventContract.md`).
> Câu hỏi và tham số gốc do bên dữ liệu chốt — bản Boat Rush của họ (`boat_rush_log.md`,
> `boat_rush_dataset.md`) chỉ để **tham khảo**; bản này là bản cầm tay, chỗ nào khác bản gốc thì
> ghi rõ lý do ngay tại chỗ.
> ✅ **Không chờ gì từ SDK**, không còn câu hỏi mở cho client — code được ngay toàn bộ.
> ⚠ Có **hai** việc không phải của dev: §8 (server) và §9 (phía đọc số).

## 0. Bốn câu phải nhớ trước khi gõ dòng nào

1. **Race = một lần Join một chặng.** Thua rồi Join lại cùng chặng là **race MỚI, id khác**.
   `live_ops_round_id` = id race.
2. **Id race KHÔNG BAO GIỜ VẮNG.** Ba tầng dự phòng ở §2. Không có cả ba thì **không ship** —
   mất id race là mất Q6, Q7, Q9, Q10.
3. **Chỉ BỐN chỗ mang id race**: `join`, `complete`, `claim` quà hạng, dòng quà hạng. Ngoài ra
   **không chỗ nào**. Gắn sai là đếm đôi (§7 bẫy 3).
4. **Boat Rush KHÔNG dùng trục tính năng.** Event solo, không có bang hội/nhóm nào, nên ba cột
   `feature*` để trống. Đừng bắt chước doc Team Arena ở chỗ này.

---

## 1. Checklist

**Khung đợt + hàm chung** (§2)
- [ ] `DeclareCycle` mỗi lần data event về
- [ ] Hàm `RaceId()` ba tầng dự phòng
- [ ] Sổ ghi local theo đợt (cho hàm ghi bù)

**Bảy bước live-op** (§3)
- [ ] `eligible` — mỗi lần data về, đủ 4 điều kiện
- [ ] `open` — Join popup hiện lên (**chỉ** Join popup)
- [ ] `join` — phản hồi Join thành công
- [ ] `milestone` — về đích ở chặng n
- [ ] `complete` — một race chốt lại
- [ ] `claim` **không** stage — sau khi trao quà hạng
- [ ] `claim` **có** stage — sau khi trao rương đảo (4 / 8 / 12)
- [ ] **Một** hàm ghi bù `sync`, gọi mỗi lần data về (§3.8)

**Ba dòng tài nguyên** (§4)
- [ ] Quà Join — **SỬA dòng mạng đang có**, không viết dòng mới
- [ ] Quà hạng — dòng mới
- [ ] Rương đảo — dòng mới

**Hai bề mặt impression** (§5)
- [ ] `boat_rush_join_popup`
- [ ] `boat_rush_tutorial`

---

## 2. Khai đợt + hai hàm dán một lần

```csharp
private const string EVENT = "boat_rush";

/// Gọi MỖI LẦN data event về. Gọi lại nhiều lần vô hại — DeclareCycle chỉ ghi state.
public static void OnDataUpdated(BoatRushData data)
{
    if (data == null) return;

    var cycleId = CycleId(data);
    if (string.IsNullOrEmpty(cycleId)) return;      // chưa biết mã đợt thì KHOAN gửi gì cả

    FalconBigDataController.LiveOps.DeclareCycle(
        EVENT, cycleId,
        cycleIndex:   data.cycleIndex,              // không có thì bỏ
        cycleStartTs: data.startTimeMs,             // epoch MILLIS UTC — giây thì × 1000
        cycleEndTs:   data.finishTimeMs);

    TryStepEligible(data);                          // §3.1
    SyncMissingSteps(data);                         // §3.8
}

/// Mã đợt: id server → dự phòng theo lịch. KHÔNG bịa chuỗi rỗng.
private static string CycleId(BoatRushData data)
{
    if (!string.IsNullOrEmpty(data.serverCycleId)) return data.serverCycleId;
    if (data.startTimeMs <= 0 || data.finishTimeMs <= 0) return null;

    return $"{EVENT}_{Utc(data.startTimeMs):yyyyMMddHH}_{Utc(data.finishTimeMs):yyyyMMddHH}";
}

/// Id race — BA TẦNG, theo đúng §2.1 bản gốc. Trả null = KHÔNG được gửi bước đó.
private static string RaceId(RaceSnapshot race)
{
    if (race == null) return null;
    if (!string.IsNullOrEmpty(race.serverRaceId)) return race.serverRaceId;
    if (race.joinTsSec > 0)      return $"s{race.stage}_{race.joinTsSec}";       // giờ Join, GIÂY
    if (race.attemptIndex > 0)   return $"s{race.stage}_a{race.attemptIndex}";   // STT lần thử

    Debug.LogError("[BoatRush] Không có id race — bước này KHÔNG gửi. Xem checklist trước khi ship.");
    return null;
}

/// Mọi bước live-op đi qua đây.
private static bool Step(string action, int? stage = null, string roundId = null,
                         string cycleId = null, Dictionary<string, object> extra = null)
{
    return FalconBigDataController.LiveOps.OnStep(new LiveOpsParam
    {
        liveOpsEvent   = EVENT,
        action         = action,
        liveOpsStage   = stage,
        liveOpsRoundId = roundId,     // CHỈ 4 chỗ — xem §0 câu 3
        liveOpsCycleId = cycleId,     // CHỈ dòng của đợt CỦA RACE — xem §7 bẫy 4
        extraMeta      = extra
    });
}
```

⚠ `OnStep` trả `false` khi van lọc chặn (bước đã log trong đợt/race này) — **bình thường, đừng xử
lý gì**. Đừng thử lại, đừng cảnh báo.

🔴 **Key riêng đi `extraMeta`, KHÔNG phải `param`.** Bản gốc §1 của dataset ghi *"Game truyền vào
`param`"* — đó là field của **custom event**, và nó ra wire thành **một chuỗi JSON** mà kho không
đọc. Bước live-op dùng `extraMeta`, và nó trải phẳng đúng chuẩn.

---

## 3. Bảy bước live-op — code dán vào

### 3.1 `eligible` — mẫu số so sánh của retention

```csharp
// Gọi MỖI LẦN data event về, khi đủ CẢ BỐN:
//   · config bật
//   · giờ server trong [startTime, finishTime − ngưỡng join)
//   · đã qua level mở event
//   · CHƯA xong 12 chặng
private static void TryStepEligible(BoatRushData data)
{
    if (!data.configEnabled) return;
    if (data.serverNowMs >= data.finishTimeMs - data.joinThresholdMs) return;
    if (!data.levelUnlocked) return;
    if (data.highestClearedStage >= 12) return;

    Step(FLiveOpsAction.Eligible);
}
```

⚠ **Gọi mỗi lần, đừng tự nhớ "đã gọi rồi".** SDK nén còn một dòng mỗi đợt. Tự nhớ thì từ đợt thứ
hai mẫu số rỗng — đúng bug Master League G1.

⚠ Đây là **tầng dữ liệu**, không gắn với icon hay popup.

### 3.2 `open` — chỉ Join popup

```csharp
// Hàm Show của JOIN POPUP, mở từ bất kỳ đâu.
// KHÔNG bắn cho popup làn đua, KHÔNG bắn cho tutorial.
Step(FLiveOpsAction.Open);
```

⚠ **Không mang key nào.** Bước này nén còn một dòng mỗi đợt, nên mọi thứ treo lên nó chỉ là "của
lần mở ĐẦU TIÊN". Đọc đúng nghĩa: *"người này có mở Join popup trong đợt hay không"*.

ℹ️ Cùng khoảnh khắc này còn gọi **impression** (§5) — hai thứ khác nhau, **gọi cả hai**:
`open` = mốc phễu (một dòng/đợt), impression = **máy đếm** (hiện bao nhiêu lần).

### 3.3 `join` — mỗi race một dòng

```csharp
// Phản hồi Join thành công (log_via = live).
public static void OnJoinSucceeded(RaceSnapshot race, string openSource)
{
    var raceId = RaceId(race);
    if (string.IsNullOrEmpty(raceId)) return;      // §0 câu 2

    Step(FLiveOpsAction.Join, roundId: raceId, extra: new Dictionary<string, object>
    {
        ["boat_rush_race_stage"]  = race.stage,            // 1–12
        ["boat_rush_join_ts"]     = race.joinTsMs,         // giờ Join THEO SERVER, epoch ms
        ["boat_rush_join_source"] = openSource,            // bỏ key nếu không rõ
        ["boat_rush_log_via"]     = "live"
    });

    Ledger.MarkRace(raceId);                               // §3.8
}
```

🔴 **`boat_rush_join_source` nâng lên BẮT BUỘC** (khác bản gốc, nơi nó là "Mở rộng"). Lý do: owner
chốt **bỏ** `boat_rush_open_source` trên impression (§5), nên key này giờ là **chỗ duy nhất** trả
lời *"cửa nào đưa người chơi vào thật"* — câu duy nhất trong nhóm "nguồn" đổi được quyết định.

Giá trị: `intro` · `auto_win` · `icon` · `after_claim` · `after_continue`. Chỉ gửi ở dòng `live`.

⚠ `boat_rush_race_stage` **chỉ gửi ở `join`**. Các bước sau của race nối bằng **id race**, đừng
gửi lại chặng.

### 3.4 `milestone` — về đích ở chặng n

```csharp
// LẦN ĐẦU client biết từ SERVER rằng user về đích ở chặng n.
public static void OnStageCleared(int stage, RaceResult result)
{
    if (!Ledger.MarkStage(stage)) return;          // đã ghi chặng này trong đợt ⇒ thôi

    Step(FLiveOpsAction.Milestone, stage: stage, extra: new Dictionary<string, object>
    {
        ["boat_rush_rank"]      = result.rank,         // 1–3; CHƯA BIẾT thì bỏ key
        ["boat_rush_finish_ts"] = result.finishTsMs,   // không có thì bỏ key
        ["boat_rush_log_via"]   = "live"
    });
}
```

⚠ **Không mang id race.** Chặng được đánh số xuyên đợt nên `live_ops_stage` đã đủ phân biệt, và
gắn id race vào là mở đường cho **đếm đôi** (§7 bẫy 3).

### 3.5 `complete` — một race chốt lại

```csharp
// Phản hồi Claim HOẶC Continue thành công. MỘT race MỘT dòng.
// "complete" = kết thúc một RACE, KHÔNG phải hoàn thành đợt.
public static void OnRaceClosed(RaceSnapshot race, RaceResult result)
{
    var raceId = RaceId(race);
    if (string.IsNullOrEmpty(raceId)) return;

    Step(FLiveOpsAction.Complete, roundId: raceId, cycleId: race.cycleId, extra: new()
    {
        ["boat_rush_result"]       = result.isWin ? "win" : "lose",
        ["boat_rush_lose_reason"]  = result.isWin ? null : result.loseReason,  // slots_full | timeout
        ["boat_rush_race_end_ts"]  = result.raceEndTsMs,                       // không có thì bỏ key
        ["boat_rush_log_via"]      = "live"
    });
}
```

🔴 **`race.cycleId` là mã đợt CỦA RACE**, không phải đợt đang chạy. Race chốt lúc đợt sau đã mở thì
dòng này phải khai **đợt của race** — §7 bẫy 4.

### 3.6 + 3.7 Hai bước `claim`, và thứ tự trong một lần Claim

```csharp
// Thứ tự BẮT BUỘC trong một phản hồi Claim:
//   complete  →  claim quà hạng  →  claim rương đảo
public static void OnClaimSucceeded(RaceSnapshot race, RaceResult result, int? islandStage)
{
    var raceId = RaceId(race);

    OnRaceClosed(race, result);                                       // §3.5

    GrantRankRewards(race, result);                                   // §4.2 — dòng quà hạng
    Step(FLiveOpsAction.Claim, roundId: raceId, cycleId: race.cycleId);   // 3.6: quà hạng

    if (islandStage is 4 or 8 or 12)
    {
        GrantIslandChest(race, islandStage.Value);                    // §4.3 — dòng rương
        Step(FLiveOpsAction.Claim, stage: islandStage, cycleId: race.cycleId);  // 3.7: rương đảo
    }
}
```

⚠ **Hai `claim` phân biệt bằng `stage`**: không stage = **quà hạng** (mang id race); có stage =
**rương đảo** (mang stage 4/8/12, **không** mang id race). Hai khoá van lọc khác nhau nên không
đụng nhau.

⚠ Stage, id race và mã đợt của `complete` / `claim` / dòng quà lấy từ **bản chụp race TRƯỚC KHI
gửi** Claim / Continue. Gửi xong rồi đọc là đọc trạng thái mới.

### 3.8 Hàm ghi bù `sync` — MỘT hàm duy nhất

```csharp
// Gọi MỖI LẦN data event về (từ OnDataUpdated). So trạng thái server với SỔ GHI LOCAL,
// chỉ ghi phần CÒN THIẾU, theo thứ tự join → milestone → complete.
private static void SyncMissingSteps(BoatRushData data)
{
    foreach (var race in data.races)                       // race server biết mà sổ chưa có
    {
        var raceId = RaceId(race);
        if (string.IsNullOrEmpty(raceId) || Ledger.HasRace(raceId)) continue;

        Step(FLiveOpsAction.Join, roundId: raceId, extra: new()
        {
            ["boat_rush_race_stage"] = race.stage,
            ["boat_rush_join_ts"]    = race.joinTsMs,
            ["boat_rush_log_via"]    = "sync"              // KHÔNG gửi join_source ở dòng sync
        });
        Ledger.MarkRace(raceId);
    }

    for (var stage = 1; stage <= data.highestClearedStage; stage++)
        if (Ledger.MarkStage(stage))
            Step(FLiveOpsAction.Milestone, stage: stage, extra: new()
            {
                ["boat_rush_log_via"] = "sync"             // rank / finish_ts KHÔNG biết ⇒ bỏ key
            });

    // ...rồi complete cho race server đã chốt mà sổ chưa ghi, cùng khuôn.
}
```

🔴 **Sổ ghi local là của RIÊNG game**, khoá theo `đợt + race` và `đợt + chặng`, **reset theo đợt**.
Đường `live` ở §3.3–§3.5 ghi vào **cùng sổ đó** để không ghi lặp.

⚠ **ĐỪNG dùng `LiveOps.HasPassed` của SDK thay cho sổ này.** Nó nhận `roundId` từ người gọi, nên
muốn đúng thì phải truyền lại y hệt cái đã ghi — tức là anh vẫn phải tự giữ sổ. Giữ sổ riêng vừa
rẻ hơn vừa không phụ thuộc chi tiết của van lọc.

⚠ Dòng `sync` **không gửi** `boat_rush_join_source`, `boat_rush_rank`, `boat_rush_finish_ts` — máy
này không biết, mà **bịa thì tệ hơn thiếu**.

---

## 4. Ba dòng tài nguyên

### 4.1 Quà Join — 🔴 SỬA dòng đang có, KHÔNG viết dòng mới

Quà Join là **30 phút mạng vô hạn**, và module mạng **đã** ghi một dòng grant cho nó. Việc của anh
là **gắn nhãn** lên dòng đó.

```csharp
// Ở chỗ module mạng đang dựng param cho 30 phút mạng vô hạn của quà Join:
param.resourceWhen  = FResourceWhen.EventPrefix + EVENT;   // "event_boat_rush"
param.resourceWhere = "join_reward";
FalconBigDataController.LiveOps.Stamp(param, EVENT);       // không stage, không id race

// ...rồi để module mạng gọi Resource.OnEarned như nó vẫn làm.
```

🔴 **KHÔNG gọi `Resource.OnEarned` lần nữa.** Một món = một đường grant. Viết dòng mới là kho thấy
**60 phút** mạng vô hạn thay vì 30.

🔴 **Đừng ĐÈ `currency` / `amount` / `itemType` / `itemId`** mà chỗ grant đã đặt — đè lên là xoá mất
món thật sự được trao. Bảng ở bản gốc liệt kê mấy field này nên dễ đọc thành *"phải set"*; không
phải.

⚠ **Nhưng PHẢI KIỂM, đừng tin là chúng đã có giá trị.** Bản trước của doc này viết *"dòng sẵn có thì
chúng đã có giá trị, không Unknown"* — đó là **suy, không kiểm**. Năm field này mặc định là chuỗi
`"Unknown"`, không phải null, nên một chỗ grant viết trước khi có hợp đồng kinh tế rất dễ đang gửi
`"Unknown"` mà không ai biết.

| Dòng grant đang có | Làm gì |
|---|---|
| `item_type` / `item_id` có giá trị thật | 🟢 chỉ **gắn nhãn**, không chạm bốn field kia |
| một trong hai là `"Unknown"` | 🔴 **điền nó**, cùng lúc gắn nhãn. Đây không phải "đè", mà là sửa một giá trị trông hợp lệ nhưng rỗng |

ℹ️ Và biết trước một chuyện: module `Lives` của SDK trong repo **không ghi dòng tài nguyên nào** —
nên dòng grant 30 phút mạng vô hạn là **code phía game**, không phải module SDK. Tìm đúng chỗ đó.
🔴 Nếu hoá ra **không có dòng nào** thì mục này đổi hẳn: viết dòng mới, và điền **cả năm** field.

### 4.2 Quà hạng — dòng mới, CÓ id race

```csharp
private static void GrantRankRewards(RaceSnapshot race, RaceResult result)
{
    foreach (var reward in result.rankRewards)             // TỪNG MÓN một dòng
    {
        var row = Row("rank_reward", reward.currency,
                      $"boat_rush_rank_reward_{race.stage}", reward.amount);
        row.liveOpsCycleId = race.cycleId;                 // đợt CỦA RACE
        FalconBigDataController.LiveOps.Stamp(row, EVENT, roundId: RaceId(race));
        FalconBigDataController.Resource.OnEarned(row);
    }
}
```

### 4.3 Rương đảo — dòng mới, CÓ stage, KHÔNG id race

```csharp
private static void GrantIslandChest(RaceSnapshot race, int islandStage)   // 4 / 8 / 12
{
    foreach (var reward in ChestRewards(islandStage))
    {
        var row = Row("progression_reward", reward.currency,
                      $"boat_rush_milestone_{islandStage}", reward.amount);
        row.liveOpsCycleId = race.cycleId;
        FalconBigDataController.LiveOps.Stamp(row, EVENT, stage: islandStage);
        FalconBigDataController.Resource.OnEarned(row);
    }
}
```

### Helper, và luật chung cho hai dòng mới

```csharp
private static ResourceParam Row(string where, string currency, string itemId, long amount)
    => new()
    {
        exchangeId    = Guid.NewGuid().ToString("N"),
        resourceWhen  = FResourceWhen.EventPrefix + EVENT,   // "event_boat_rush"
        resourceWhere = where,
        itemType      = EVENT,                                // "boat_rush"
        currency      = currency,
        itemId        = itemId,
        amount        = amount
    };
```

🔴 **Phải điền CẢ NĂM**: `resourceWhen` · `resourceWhere` · `itemType` · `currency` · `itemId`.
Bản gốc đánh `item_type`/`item_id` là **"Mở rộng"** — **sai**: năm field này mặc định là chuỗi
`"Unknown"`, không phải null. Bỏ trống là gửi `"Unknown"` lên kho, một giá trị **trông hợp lệ** nên
không ai biết nó thiếu. Dùng `Row(...)` để không quên.

⚠ **Mạng tính bằng PHÚT** ở `amount`, không phải giây.

⚠ **Gói thẻ**: chỉ dòng **gói** mang nhãn Boat Rush; dòng thẻ lúc mở gói giữ nguyên của Card
Collection.

---

## 5. Hai bề mặt impression

```csharp
// Hàm Show của popup. Cứ gọi MỖI LẦN hiện — volume SDK lo (gộp cụm, gửi kèm count).
private static void Impression(string surface)
{
    var ctx = new LiveOpsRef();
    FalconBigDataController.LiveOps.Stamp(ctx, EVENT);      // event + đợt, KHÔNG id race
    FalconBigDataController.Ui.OnImpression(surface, liveOps: ctx);
}

// Hàm Show của Join popup   → Impression("boat_rush_join_popup");
// Hàm Show của Info popup   → Impression("boat_rush_tutorial");
```

🔴 **Đúng HAI tên surface, KHÔNG ghép nguồn vào tên** (khác bản gốc). Owner chốt **bỏ**
`boat_rush_open_source`: phân rã impression theo cửa gần như đã biết trước câu trả lời (`auto_win`
bắn sau **mỗi** trận thắng nên nó áp đảo **do thiết kế trigger**, không do người chơi chọn). Câu
đổi được quyết định — *"cửa nào đưa người chơi vào thật"* — trả lời bằng
`boat_rush_join_source` ở §3.3.

⚠ **Phải đóng dấu `live_ops_*` lên impression** (dòng `Stamp` ở trên). Không đóng dấu thì
impression của đợt 1 và đợt 2 **gộp chung một dòng**, vì trục live-op nằm trong khoá gộp cụm.
ℹ️ Câu nghiệm thu của doc Team Arena viết *"không dòng `f_sdk_ui_exposure` nào mang
`live_ops_*`"* — câu đó viết quá tuyệt đối, ý nó là "đừng đóng dấu **bừa** theo event đang chạy".

---

## 6. Những thứ ĐỪNG gửi nữa

| Đừng gửi | Vì |
|---|---|
| `boat_rush_open_source` trên impression | §5 — owner chốt bỏ |
| Chặng của race ở các bước sau `join` | Nối bằng id race (§3.3) |
| Id race trên `milestone` và `claim` rương | Mở đường đếm đôi (§7 bẫy 3) |
| Ba cột `feature*` | Boat Rush không có thực thể nào — event solo |
| Cờ suy ra được: "race cuối", "race bị cắt", "số lần thử", "đang đua", "thời gian còn lại" | Kho tự suy. Ngoại lệ duy nhất là `milestone` — hợp đồng live-op yêu cầu |
| `rank` / `finish_ts` / `join_source` trên dòng `sync` | Máy này không biết. Bịa tệ hơn thiếu |
| Thuyền tiến trên làn, anim về đích, popup làn đua | Không câu hỏi nào cần |
| **Stamp** dòng level / dòng sink / ads / IAP | Không thuộc hẳn Boat Rush. Q11 lọc theo **cửa sổ đang đua**, không cần đóng dấu |

---

## 7. Sáu cái bẫy, xếp theo mức đau

### Bẫy 1 — mã đợt rỗng: mất sạch từ đợt thứ hai

Van lọc khoá theo `(event, đợt, race, action, stage)`. Mã đợt `''` ⇒ đợt 1 và đợt 2 ra **cùng
khoá** ⇒ từ đợt 2 mọi bước **bị nuốt**, im lặng, không ghi bù được. **Chưa biết mã đợt thì KHOAN
gửi** (§2 đã guard).

### Bẫy 2 — năm field resource mặc định `"Unknown"`

§4. Dùng helper `Row(...)`.

### Bẫy 3 — id race ở chỗ không nên có = đếm đôi

Chỉ **bốn** chỗ mang id race. Gắn lên `milestone` thì mỗi race lại thành một khoá mới ⇒ cùng một
chặng được ghi lại ở race sau ⇒ **thắng một chặng đếm thành nhiều lần**.

### Bẫy 4 — dòng chốt muộn phải khai **đợt của race**

Race join ở đợt N, Claim lúc đợt N+1 đã mở. `complete` / hai `claim` / dòng quà **phải** tự gán
`liveOpsCycleId = race.cycleId`. Không gán thì SDK điền **đợt đang chạy** ⇒ dòng của race đợt N
mang mã đợt N+1.

ℹ️ Mã **race** thì SDK **không** tra hộ nữa (sửa 05/11) — không truyền = dòng không có race. Nên
cứ truyền ở đúng 4 chỗ, chỗ khác để yên.

### Bẫy 5 — `eligible` / `open` là một dòng MỖI ĐỢT

Đừng treo phân bố, đừng treo **trạng thái** lên chúng.

🔴 **Luật một câu:** van lọc phá thứ **BIẾN ĐỔI** trong đợt; thứ **BẤT BIẾN** trong đợt thì một
dòng là đủ. (`cycle_start_ts`/`cycle_end_ts` thuộc loại thứ hai — chúng đi đúng đường này.)

### Bẫy 6 — impression không sống qua kill app cứng

Rổ gộp **không** ghi xuống đĩa (cố ý: zero IO trên đường nóng), flush lúc app pause hoặc khi vượt
32 rổ. Người chơi force-close là mất rổ đang dở. Chấp nhận được cho telemetry exposure — nhưng
**đừng** dùng impression làm mẫu số cho phép tính cần chính xác tuyệt đối.

---

## 8. Việc của server (không phải việc dev client)

### Phải có, không có thì chặn ship

| # | Cần gì | Không có thì |
|---|---|---|
| 1 | Phản hồi Join + data user có: **id race**, **giờ Join theo server**, lịch đợt chứa race đang giữ, cờ "đã Join" | Không có id race ⇒ dùng dự phòng §2. **Không có cả ba tầng ⇒ không ship.** Không có giờ Join ⇒ mất cửa sổ đang đua ⇒ mất Q11, Q19, Q24 |
| 2 | `startTime` / `finishTime` **chung mọi user**; id/STT đợt; đơn vị giây hay ms | Mã đợt dùng luật dự phòng §2, **chốt trước ship**. Lịch không chung mọi user thì luật đó không dùng được |
| 3 | Lúc thắng trả **hạng + giờ về đích**; lúc Claim/Continue trả **giờ race đóng + lý do thua** | Không đổi log (key bỏ trống), nhưng Q4, Q5, Q7, Q8 phải chuyển sang bảng kết quả race |

⚠ Mã đợt và id race của server phải **trùng ĐÚNG GIÁ TRỊ** client gửi ở `live_ops_cycle_id` /
`live_ops_round_id`. Lệch một ký tự là không nối được.

### Hai bảng cho nhóm câu hỏi cân bot

| Bảng | Cột | Trả lời |
|---|---|---|
| Kết quả race (1 dòng / user / race) | mã đợt, id race, `account_id`, chặng, giờ Join, giờ về đích, giờ đóng, kết quả, hạng, **lý do thua 4 loại**, điểm cuối user, điểm mục tiêu, luật điều chỉnh đã kích hoạt + hệ số, thành phần vai bot, nhịp user đầu vào | Q25, Q27; đối soát Q6–Q8, Q19 |
| Log bot (1 dòng / bot / race) | vai, nhịp thực, chuỗi (giờ, điểm) hoặc tính sẵn số lần vượt, khoảng dẫn lớn nhất mỗi bên | Q20–Q23; đối soát Q24 |

---

## 9. Việc phía đọc số (không phải việc dev client)

### 🔴 `milestone` CÓ THỂ nhân đôi xuyên máy — phải đọc bằng EXISTS

Sổ ghi local và van lọc đều là sổ **theo máy**. Nên:

1. Máy A: thắng chặng 5 → bắn `milestone(5)`, ghi vào sổ **máy A**
2. Mở máy B: sổ máy B **rỗng** → hàm ghi bù thấy chặng đã qua > 5 → bắn **`milestone(5)` lần nữa**

Không chữa được ở client (trừ khi bắt server giữ sổ — việc lớn hơn nhiều). Luật đọc số:

| Câu | An toàn chưa |
|---|---|
| Q3 — chặng cao nhất | ✅ `max(live_ops_stage)` |
| Q6 — race thắng | ✅ doc đã viết "**có** `milestone` chặng đó" (EXISTS) |
| Q9 — số lần thử | ✅ đếm `join`, không đếm milestone |
| **Q8 — phân bố hạng** | ⚠ hai dòng (một `live` có hạng, một `sync` không) ⇒ **ưu tiên dòng `log_via = live`**, đừng lấy bừa |

➡ Và mục nghiệm thu *"không ghi lặp"* phải đọc là **"không ghi lặp trên cùng một máy"**.

### Ba câu đổi cách đọc

| Câu | Bản gốc | Đổi thành |
|---|---|---|
| **Q1** | mẫu số = user có `open`, **theo ngày** | `open` một dòng/đợt nên không ra số theo ngày. Mẫu số: **active hôm đó** (user daily) × **đã unlock** (`max_passed_level`) × **hôm đó trong đợt** (`cycle_start_ts`/`cycle_end_ts`). Client **không sửa gì** |
| **Q2** | lượt hiện Join popup **theo nguồn** | **tổng** lượt hiện, theo ngày — `sum(count)`, không `count(*)` |
| **Q12** | lượt xem tutorial **theo nguồn** | như Q2 |

ℹ️ Thay cho Q2 theo nguồn: *"cửa nào đưa người chơi vào thật"* = đếm `join` theo
`boat_rush_join_source` (§3.3).

⚠ Thứ **không** suy lại được: `open` chỉ nói *"đã thấy UI đâu đó trong đợt"*. **Ngày nào** thì
thông tin đó chưa bao giờ rời khỏi máy — không khâu xử lý nào lấy lại được. Nên đừng đặt mẫu số
theo ngày lên bước bị nén.

---

## 10. Tự kiểm trước khi bàn giao

| Kiểm | Đạt khi |
|---|---|
| Join 1 chặng | Đúng **1** `join` `live`, có `live_ops_round_id`, `boat_rush_race_stage`, `boat_rush_join_ts`, `boat_rush_join_source` |
| Thắng chặng rồi Claim | `milestone(n)` → `complete` (`win`) → `claim` quà hạng → dòng quà hạng. **Đúng thứ tự đó** |
| Thắng chặng 4 / 8 / 12 | Thêm `claim` **có stage** và dòng rương đảo |
| Thua hết suất rồi Continue | `complete` (`lose`, `slots_full`), rồi `join` race mới **cùng chặng, id race KHÁC** |
| Quà Join | Đúng **1** dòng mạng 30 phút, mang `event_when = event_boat_rush` + `event_where = join_reward`. **Không** có dòng mạng thứ hai |
| Ba dòng quà | Không dòng nào có `item_type` hay `item_id` = `"Unknown"` |
| Mở Join popup 3 lần | **1** dòng impression `boat_rush_join_popup`, `count = 3`, có `live_ops_event` + `live_ops_cycle_id` |
| Cài lại / máy thứ 2 giữa race | Dòng `sync` cho phần còn thiếu; **trên cùng một máy** không ghi lặp (xuyên máy thì xem §9) |
| Chốt muộn | Race của đợt N, Claim lúc đợt N+1 đã mở ⇒ `complete`, 2 `claim`, dòng quà mang **mã đợt N** |
| Đợt thứ hai | Mọi bước lên log bình thường (không bị nuốt vì mã đợt rỗng) |
| Đóng dấu đúng chỗ | **Không** dòng level / sink / ads / IAP nào mang `live_ops_event = boat_rush` |
| Không có id race | Có `Debug.LogError` ở editor — và **sửa trước khi ship**, không ship kèm |
