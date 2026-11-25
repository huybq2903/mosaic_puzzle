# Tracking Ladder Streak — TODO cho dev + code dán vào

> **Dev chỉ cần đọc §1 (checklist) rồi làm theo §2–§6.** Tick `[x]` khi xong.
> Viết 06/11/2026. API: `FalconBigDataController.LiveOps` / `.Ui` / `.Resource`
> (BigData, hợp đồng `docs/design/LiveOpsEventContract.md`).
> Câu hỏi và tham số gốc do bên dữ liệu chốt — bản Ladder Streak của họ
> (`ladder_streak_log.md`, `ladder_streak_dataset.md`) chỉ để **tham khảo**; bản này là bản cầm
> tay, chỗ nào khác bản gốc thì ghi rõ lý do ngay tại chỗ.
> ✅ **Không chờ gì từ SDK** — van lọc đã khoá đúng trên bản SDK game đang chạy (§3.4).
> ✅ **Không còn quyết định nào chờ owner** (hai lựa chọn cuối đã loại, §10).
> ⚠ Còn **sáu câu hỏi cho dev** (§9) — không câu nào chặn ship.
> ⚠ Hai việc không phải của dev: §9 (server) và §10 (phía đọc số).
> 🔴 **Đây là doc THÊM VÀO, không phải doc THAY THẾ.** Event đang chạy và phần khung đợt đã đúng —
> code mẫu ở §2 là để đối chiếu, không phải để dán đè. Việc thật là **thêm key**, **thêm ba dòng
> tài nguyên**, **thêm hai bề mặt impression**, và **truyền mã đợt cho dòng nhận muộn**.

## 🟢 Event NÀY ĐANG CHẠY — đừng sửa thứ đã đúng

Bản thử đã lên kho và bên dữ liệu đã tra. Những thứ sau **đã đúng hợp đồng, giữ nguyên**:

| Đã đúng | Đừng làm gì |
|---|---|
| `live_ops_cycle_id` có mặt trên **100% dòng**, chung mọi user | Đừng đổi cách sinh mã đợt |
| `cycle_index` / `cycle_start_ts` / `cycle_end_ts` / `live_ops_enter_ts` đủ | Đừng gửi lại lịch đợt bằng key riêng |
| Đủ **sáu** bước live-op | Đừng thêm bước mới |
| `live_ops_stage` nhận đúng **1…10 liên tục**, chỉ ở `milestone` và `claim` | 🟢 Đây là `milestoneId`, **đúng rồi**. Đừng đổi sang số chuỗi |
| `live_ops_round_id` **rỗng 100%** | 🟢 **Cố ý.** Xem §0 câu 2 — đừng điền |
| Dòng level **không** mang `live_ops_event = ladder_streak` | 🟢 Đúng, vì event chạy chồng lên level thường. Đừng đóng dấu |
| Hai bề mặt impression đang gửi | Đừng đổi tên (§6) |

➡ Việc mới nằm ở **§5 (ba dòng tài nguyên)**, **§3.4 (`milestone` bỏ sổ riêng)**,
**§6 (hai bề mặt cảnh báo)** và các **key riêng** ở §3–§5.

---

## 0. Bốn câu phải nhớ trước khi gõ dòng nào

1. **Chuỗi KHÔNG BAO GIỜ là `live_ops_stage`.** `live_ops_stage` = **số thứ tự rương, 1–10**.
   Chuỗi là một **đại lượng có lên có xuống** ⇒ nó đi vào **dòng tài nguyên** (§5), nơi có
   `value_before` / `value_after` / `amount` / lý do. Lý do đầy đủ ở §8 bẫy 1.
2. **Ladder Streak KHÔNG dùng `live_ops_round_id`.** Leo lại sau khi tụt **không** phải lượt mới.
   Mốc rương là **lũy kế theo đợt**, không reset khi chuỗi đứt. Điền mã lượt vào là cho van lọc
   ghi lại **cả 10 rương mỗi lần đứt chuỗi** ⇒ "% user đạt rương n" phồng theo số lần đứt.
3. **MỘT hàm duy nhất** ghi dòng chuỗi **và** bắn `milestone` (§3.7). Mọi đường — phản hồi thắng,
   phản hồi thua, data event về, mở app — đều gọi **cùng** hàm đó. Hai đường ghi là hai lần đếm.
4. **Ladder Streak KHÔNG dùng trục tính năng.** Event solo, không bang hội/nhóm nào, nên ba cột
   `feature*` để trống. Đừng bắt chước doc Team Arena ở chỗ này.

---

## 1. Checklist

**Khung đợt + hàm chung** (§2)
- [ ] `DeclareCycle` mỗi lần data event về — 🟢 **giữ nguyên** hàm sinh mã đợt đang có
- [ ] Hàm `Step()` (§2) và `Row()` (§5) dán một lần
- [ ] **Mốc so sánh chuỗi** lưu theo đợt (§2) — thứ duy nhất game phải tự giữ cho phần chuỗi

**Sáu bước live-op** (§3)
- [ ] `eligible` — mỗi lần data về, đủ 4 điều kiện
- [ ] `open` — Intro **hoặc** Main popup hiện lên
- [ ] `join` — **hai** chỗ gọi, và `MarkJoined` phải gọi **TRƯỚC** lần refresh (§3.3)
- [ ] `milestone` — 🔴 **bỏ sổ mốc riêng**, chỉ loop + để van lọc chặn (§3.4)
- [ ] `claim` — mỗi mốc một dòng, mang **mã đợt được nhận**
- [ ] `complete` — popup kết quả cuối đợt, mang **mã đợt được nhận**
- [ ] **Một** hàm duy nhất ghi chuỗi + mốc, gọi ở **bốn** chỗ (§3.7)
- [ ] Bốn dòng `claim` / `complete` / quà mốc truyền **mã đợt được nhận** (§8 bẫy 4)

**Ba dòng tài nguyên** (§5)
- [ ] Chuỗi **tăng** — `source`, `currency = ladder_streak_count`
- [ ] Chuỗi **tụt** — `sink`, `OnSpent`, `amount` **số dương**
- [ ] Quà mốc n — `source`, có `live_ops_stage = n`

**Bề mặt impression** (§6)
- [ ] Hai bề mặt đang gửi — **giữ nguyên tên**
- [ ] `ladder_streak_lose_warning` — mới
- [ ] `ladder_streak_revive_panel` — mới

**Ba thứ phụ trợ — 🔴 CẢ BA LƯU ĐĨA, không phải biến static** (§3.7, §4)
- [ ] Cờ "đang chơi màn được tính"
- [ ] Bản ghi rời màn (`fail` / `retry` / `leave` + bề mặt cảnh báo), lưu **TRƯỚC** khi gửi kết quả
- [ ] Bộ đếm `LoseStreakPopup` trong lượt

---

## 2. Khung đợt + hàm chung

```csharp
private const string EVENT = "ladder_streak";

/// Gọi MỖI LẦN data event về. Gọi lại nhiều lần vô hại — DeclareCycle chỉ ghi state.
public static void OnDataUpdated(LadderStreakData data)
{
    if (data == null) return;

    var cycleId = CycleId(data);
    if (string.IsNullOrEmpty(cycleId)) return;      // chưa biết mã đợt thì KHOAN gửi gì cả

    FalconBigDataController.LiveOps.DeclareCycle(
        EVENT, cycleId,
        cycleIndex:   data.cycleIndex,              // không có thì bỏ
        cycleStartTs: data.timeStartMs,             // epoch MILLIS UTC — giây thì × 1000
        cycleEndTs:   data.timeEndMs);

    TryStepEligible(data);                          // §3.1
    TryStepJoin(data);                              // §3.3
    SyncStreak(data, witnessedWin: false, exit: null);   // §3.7 — hàm DUY NHẤT
}

/// 🔴 GIỮ NGUYÊN hàm sinh mã đợt game ĐANG CÓ. Nó đã chạy đúng (mã đợt có trên 100% dòng,
/// chung mọi user) — đổi cách sinh là tự tạo một mốc đổi nghĩa trong kho mà không được gì.
/// Chỉ một luật phải giữ: KHÔNG BAO GIỜ trả chuỗi rỗng (§8 bẫy 2).
private static string CycleId(LadderStreakData data);

/// Mọi bước live-op đi qua đây.
private static bool Step(string action, int? stage = null, string cycleId = null,
                         Dictionary<string, object> extra = null)
{
    return FalconBigDataController.LiveOps.OnStep(new LiveOpsParam
    {
        liveOpsEvent   = EVENT,
        action         = action,
        liveOpsStage   = stage,
        liveOpsCycleId = cycleId,     // CHỈ dòng nhận MUỘN mới truyền — xem §8 bẫy 4
        extraMeta      = extra
        // liveOpsRoundId: KHÔNG BAO GIỜ — §0 câu 2
    });
}
```

⚠ `OnStep` trả `false` khi van lọc chặn (bước đã log trong đợt này) — **bình thường, đừng xử lý
gì**. Đừng thử lại, đừng cảnh báo. §3.4 dựa hẳn vào tính chất này.

🔴 **Key riêng của BƯỚC đi `extraMeta`; key riêng của DÒNG TÀI NGUYÊN đi `detail`.** Bản gốc §1
của dataset ghi *"Game truyền vào `param`"* — `param` là field của **custom event**, và nó ra wire
thành **một chuỗi JSON** mà kho **không đọc**. Hai field đúng là `extraMeta` (bước) và `detail`
(resource).

ℹ️ Hai field đó về kho theo hai đường khác nhau nhưng **tra cứu y như nhau**: `extraMeta` trải
phẳng ngay ở client, `detail` đi nguyên khối rồi kho mở ra. Cuối cùng cả hai đều nằm ở
`event_extra_props`. ⚠ `ResourceParam` **không có** field `extraMeta` — đừng tìm.

### Thứ duy nhất game phải tự giữ: mốc so sánh chuỗi

```csharp
// Khoá theo ĐỢT. Reset khi đổi đợt. Đây là "chuỗi mà máy này biết server đang giữ".
// Chưa có (cài lại, máy mới, đợt mới) ⇒ LẤY LÀM MỐC, KHÔNG GHI DÒNG.
private static int? Baseline(string cycleId);
private static void SetBaseline(string cycleId, int streak);
```

🔴 **Chỉ CÁI NÀY.** Không cần sổ mốc rương, không cần sổ bước — §3.4 giải thích vì sao.

---

## 3. Sáu bước live-op — code dán vào

### 3.1 `eligible` — mẫu số gốc của mọi chart

```csharp
// Gọi MỖI LẦN config và data event về, khi đủ CẢ BỐN:
//   · có config
//   · giờ server trong [timeStart, timeEnd)
//   · không ở chế độ isEventEnd
//   · đã qua levelUnlock
// KHÔNG xét đã join hay chưa.
private static void TryStepEligible(LadderStreakData data)
{
    if (!data.hasConfig) return;
    if (data.serverNowMs < data.timeStartMs || data.serverNowMs >= data.timeEndMs) return;
    if (data.isEventEnd) return;
    if (data.maxPassedLevel < data.levelUnlock) return;

    Step(FLiveOpsAction.Eligible);
}
```

⚠ **Gọi mỗi lần, đừng tự nhớ "đã gọi rồi".** SDK nén còn một dòng mỗi đợt. Tự nhớ thì từ đợt thứ
hai mẫu số rỗng — đúng bug Master League G1.

⚠ Đây là **tầng dữ liệu**, không gắn với icon hay Intro.

### 3.2 `open` — Intro hoặc Main popup

```csharp
// Hàm OnEnable của PopupIntro HOẶC PopupMain.
Step(FLiveOpsAction.Open);
```

⚠ **Không mang key nào.** Bước này nén còn một dòng mỗi đợt, nên mọi thứ treo lên nó chỉ là "của
lần mở ĐẦU TIÊN". Đọc đúng nghĩa: *"người này có mở UI event trong đợt hay không"*.

ℹ️ Cùng khoảnh khắc này còn gọi **impression** (§6) — hai thứ khác nhau, **gọi cả hai**:
`open` = mốc phễu (một dòng/đợt), impression = **máy đếm** (hiện bao nhiêu lần).

### 3.3 `join` — một dòng mỗi đợt

```csharp
// HAI chỗ gọi, và THỨ TỰ quan trọng (xem lưu ý 🔴 dưới):
//   1. phản hồi MarkJoined thành công  → StepJoin(continue | close)   ← GỌI TRƯỚC
//   2. data event về đã mang joined    → StepJoin(auto | carry)       ← từ OnDataUpdated
private static void StepJoin(string via)
{
    Step(FLiveOpsAction.Join, extra: new Dictionary<string, object>
    {
        ["ladder_streak_join_via"] = via               // null ⇒ SDK bỏ key, đừng đoán
    });
}

// Đường 2 — gọi từ OnDataUpdated.
private static void TryStepJoin(LadderStreakData data)
{
    if (!data.joined) return;
    StepJoin(data.IsFirstDataOfCycle ? "carry" : "auto");
}

// Đường 1 — gọi NGAY trong callback MarkJoined, trước mọi lần refresh data.
public static void OnMarkJoinedSucceeded(bool fromContinueButton)
{
    StepJoin(fromContinueButton ? "continue" : "close");
}

| `ladder_streak_join_via` | Nghĩa |
|---|---|
| `continue` | Bấm **Tham gia** ở Intro |
| `close` | Đóng Intro bằng X mà **vẫn** join |
| `auto` | Data **giữa đợt** về đã `joined` — máy này không chứng kiến lúc join |
| `carry` | Data **đầu đợt** về đã `joined` từ đợt trước |

🔴 **Bắt buộc.** Đây là chỗ duy nhất tách "bấm tham gia thật" (`continue`) khỏi "join hộ"
(`close`), và là chỗ duy nhất loại `carry` khỏi cohort retention. Không rõ đường thì **bỏ key**,
đừng đoán.

🔴 **Van lọc là LẦN ĐẦU THẮNG, và `extraMeta` đi theo lần đầu đó.** Nên nếu một lần refresh data
chạy **trước** callback `MarkJoined` — rất dễ xảy ra, vì `MarkJoined` thành công thường kéo theo
một lần fetch — thì cú bấm **Tham gia** thật bị ghi là `auto`, và `join` thật biến mất khỏi mẫu số
"bấm tham gia Intro". Dòng sau bị van lọc chặn nên **không có cách sửa**, và nó **im lặng**.

➡ Hai cách, chọn một:
> **(i)** Gọi `StepJoin("continue"/"close")` **ngay trong callback** `MarkJoined`, **trước** khi
> kích hoạt bất kỳ lần refresh nào. Đường rẻ hơn, và đúng với mọi trường hợp bình thường.
> **(ii)** Đặt một cờ *"vừa bấm tham gia trong phiên này"* lúc user bấm, và `TryStepJoin` đọc cờ
> đó trước khi quyết `auto`. Chắc hơn khi không nắm được thứ tự callback.

❓ **Câu hỏi cho dev (§9 câu C):** thứ tự hai callback đó hiện ra sao? Nắm chắc thì dùng (i),
không thì dùng (ii).

ℹ️ `auto` cũng là **dấu "máy này không chứng kiến"** — nhờ nó mà bước `join` phân biệt được dòng
ghi bù xuyên máy. `milestone` **không** có dấu tương ứng; xem §10.

### 3.4 `milestone` — 🔴 BỎ sổ mốc riêng

```csharp
// Gọi TỪ TRONG hàm duy nhất ở §3.7, sau khi đã ghi dòng chuỗi.
// Loop từ 1 lên, KHÔNG so với "ngưỡng > max cũ", KHÔNG giữ sổ.
private static void StepMilestones(LadderStreakData data)
{
    foreach (var m in data.milestones)                     // đã sort theo milestoneId tăng dần
    {
        if (m.winStreak > data.maxStreakEver) continue;

        Step(FLiveOpsAction.Milestone, stage: m.milestoneId, extra: new()
        {
            ["ladder_streak_milestone_threshold"] = m.winStreak
        });
    }
}
```

🔴 **Khác bản gốc: KHÔNG giữ "sổ mốc local của đợt".** Bản gốc yêu cầu sổ đó để chặn ghi lặp —
nhưng van lọc của SDK **đã làm đúng việc đó**:

| | Sổ mốc của game | Van lọc của SDK |
|---|---|---|
| Khoá | đợt + mốc | `(event, đợt, —, milestone, mốc)` |
| Lưu ở | PlayerPrefs của game | pool lưu đĩa của SDK |
| Sống qua restart app | ✅ | ✅ |
| Mất khi cài lại / xoá data | ✅ | ✅ |

**Hai sổ giống nhau từng dòng** ⇒ sổ thứ hai chỉ là một chỗ nữa để sai.

🟢 **Và van lọc đã đúng trên bản SDK game đang chạy** — không phải chờ bản mới. Van lọc luôn dựng
khoá bằng `param.liveOpsRoundId` (**mã lượt trên chính dòng đang gửi**, kể cả `null`), từ lần đầu
entity live-op ra đời, và định dạng khoá chưa đổi lần nào. Hai hàm **từng** đoán lượt là
`LiveOps.Stamp` và `LiveOps.HasPassed` (đã sửa 06/11) — Ladder **không dùng** hàm nào trong hai,
và không truyền lượt bao giờ, nên khoá là `null` ở **cả** lúc ghi và lúc so. Không có việc gì phải
làm, không phải nâng SDK trước.

⚠ **Và đừng gọi `LiveOps.HasPassed` thay cho chuyện này.** Loop + để `OnStep` trả `false` là đường
ngắn hơn, mà `HasPassed` thì phải truyền lại `roundId` y hệt cái đã ghi — một chi tiết nữa để sai.

⚠ **Vẫn loop từ 1 lên và so bằng `winStreak <= maxStreakEver`**, chứ không so "ngưỡng > max cũ" —
để không sót mốc khi config đổi giữa đợt. Mốc đã gửi thì `OnStep` trả `false`, **vô hại**.

⚠ `ladder_streak_milestone_threshold` **nên gửi** (bản gốc ghi "Mở rộng"): nó là nhãn của mốc khi
chưa có bảng config server, và nó là **cách duy nhất** phát hiện config đổi giữa đợt.

### 3.5 `claim` — mỗi mốc một dòng

```csharp
// Trao quà mốc n thành công. MỘT dòng MỘT mốc, kể cả khi nhận gộp.
public static void OnMilestoneClaimed(int milestoneId, ClaimSnapshot snap)
{
    Step(FLiveOpsAction.Claim, stage: milestoneId, cycleId: snap.cycleId);
    GrantMilestoneRewards(milestoneId, snap);           // §5.3
}
```

🔴 **`snap.cycleId` là mã đợt ĐƯỢC NHẬN**, không phải đợt đang chạy. Nhận ở popup cuối đợt lúc đợt
sau đã mở ⇒ phải truyền tay. §8 bẫy 4.

⚠ Nhận gộp 3 mốc = **3** dòng `claim` + quà của **cả 3** mốc. Đừng gộp thành một dòng không stage.

### 3.6 `complete` — popup kết quả cuối đợt

```csharp
// Popup kết quả cuối đợt (isEventEnd) hiện LẦN ĐẦU trong đợt.
Step(FLiveOpsAction.Complete, cycleId: snap.cycleId);
```

⚠ Bước này **không** trả lời câu hỏi nào trong sheet; nó cho biết user có quay lại sau giờ kết
thúc. Vẫn gửi để phễu live-op đủ sáu bước.

### 3.7 Hàm DUY NHẤT — ghi dòng chuỗi và bắn mốc

```csharp
// Vocab của key ladder_streak_via — snake_case, KHÔNG truyền enum của code.
private static class StreakVia
{
    public const string Win     = "win";        // source: thắng mà MÁY NÀY chứng kiến
    public const string Sync    = "sync";       // source/sink: thay đổi máy này KHÔNG chứng kiến
    public const string Fail    = "fail";       // sink: thua, chấp nhận mất
    public const string Retry   = "retry";      // sink: bấm Retry
    public const string Leave   = "leave";      // sink: thoát màn
    public const string AppKill = "app_kill";   // sink: kill app giữa màn
}

// GỌI Ở ĐÚNG BỐN CHỖ, không chỗ nào khác:
//   1. phản hồi LevelResult  → witnessedWin = true khi phản hồi đó LÀ một màn THẮNG được tính
//   2. phản hồi LevelStart   → nếu phản hồi có mang data event (witnessedWin = false)
//   3. mỗi lần data event về → witnessedWin = false
//   4. mở app / vào lại game → qua đường 3
// KHÔNG có tham số `exit`: lý do tụt đọc từ BẢN GHI LƯU ĐĨA trong chính hàm này, vì đường cần
// nó nhất là đường MỞ LẠI APP — lúc đó bản ghi trong RAM đã mất.
private static void SyncStreak(LadderStreakData data, bool witnessedWin)
{
    var cycleId = CycleId(data);
    if (string.IsNullOrEmpty(cycleId)) return;

    var before = Baseline(cycleId);
    if (before == null)                                  // máy mới / cài lại / đợt mới
    {
        SetBaseline(cycleId, data.winStreak);            // LẤY LÀM MỐC, KHÔNG GHI DÒNG
        StepMilestones(data);                            // mốc thì vẫn bắn — van lọc chặn trùng
        return;
    }

    var from = before.Value;
    var to   = data.winStreak;

    // Thua đúng sàn ⇒ KHÔNG GHI DÒNG. Nhưng chỉ khi máy này KHÔNG chứng kiến một màn thắng:
    // thắng mà chuỗi về đúng mốc cũ nghĩa là đã có một bậc tụt ngoài máy này, vẫn phải ghi.
    if (to == from && !witnessedWin) { StepMilestones(data); return; }

    if (witnessedWin)
    {
        // Bậc CUỐI luôn là +1 do máy này chứng kiến. Phần chênh còn lại (nếu có) là thay đổi
        // máy này KHÔNG chứng kiến ⇒ sync, và KHÔNG mang warn_count (xem lưu ý dưới).
        if (to - 1 != from)
            WriteStreakRow(data, cycleId, from, to - 1, StreakVia.Sync, endOfTurn: false);

        WriteStreakRow(data, cycleId, to - 1, to, StreakVia.Win, endOfTurn: true);
    }
    else if (to > from)
    {
        WriteStreakRow(data, cycleId, from, to, StreakVia.Sync, endOfTurn: false);
    }
    else
    {
        // 🔴 ĐỌC TỪ ĐĨA, không nhận từ người gọi — đường cần nó nhất là đường MỞ LẠI APP,
        // lúc đó bản ghi trong RAM đã mất. Đọc một lần, dùng, rồi xoá.
        var exit = LoadExitRecord();                     // null nếu không có
        var via  = exit?.reason                          // fail | retry | leave
                   ?? (IsPlayingCountedLevel() ? StreakVia.AppKill : StreakVia.Sync);

        WriteStreakRow(data, cycleId, from, to, via, endOfTurn: via != StreakVia.Sync,
                       warnSurface: exit?.warnSurface);
        ClearExitRecord();
    }

    SetBaseline(cycleId, to);
    StepMilestones(data);                                // §3.4 — SAU dòng chuỗi
}
```

🔴 **`endOfTurn` phải TRUE ở ĐÚNG MỘT dòng mỗi lượt chơi.** `ladder_streak_warn_count` là số lần
`LoseStreakPopup` hiện **trong lượt**; gắn nó lên hai dòng của cùng một lượt là phép tính "lượt
hiện cảnh báo" **đếm đôi**. Nhánh `witnessedWin` đẻ ra tới hai dòng, nên dòng `sync` dẫn đầu
**không** mang nó.

⚠ **Dòng `sync` và `app_kill` không mang `warn_surface`.** `sync` thì máy này không chứng kiến,
`app_kill` thì không có bản ghi rời màn để đọc — cả hai đều **không biết** user đã thấy cảnh báo
nào, và **bịa thì tệ hơn thiếu**.

⚠ **Đọc bản ghi rời màn ĐÚNG MỘT LẦN**, truyền xuống, rồi `ClearExitRecord()` sau khi đã ghi
dòng. Đọc lại lần hai trong `StreakDetail` là mở cửa cho ca "đã xoá rồi mới đọc".

Bốn luật của mốc so sánh:

1. **Khoá theo đợt**, reset khi đổi đợt.
2. **Chưa có thì lấy làm mốc, KHÔNG ghi dòng** — không thì máy thứ hai ghi lại cả lịch sử chuỗi.
3. `value_before` / `value_after` luôn **lấy từ server**, không tự cộng trừ ở client.
4. **Thay đổi bằng 0 thì không ghi dòng** (thua đúng sàn). Số lần đó đọc từ log level nếu GD cần.

Ba thứ phụ trợ, **cả ba LƯU ĐĨA** (PlayerPrefs / save file của game), không phải biến static:

| | Đặt khi nào | Xoá khi nào | Dùng để |
|---|---|---|---|
| **Cờ đang chơi** | bắt đầu một màn được tính | có phản hồi kết quả | phân biệt `app_kill` với `sync` |
| **Bản ghi rời màn** | **ngay lúc** user rời màn: chấp nhận ở `LoseStreakPopup`, đóng popup hồi sinh, Retry / Leave không có popup | ngay sau khi nhánh tụt của `SyncStreak` đọc nó | `via` = `fail`/`retry`/`leave` và `warn_surface` |
| **Bộ đếm cảnh báo** | tăng mỗi lần `LoseStreakPopup` hiện | lượt chơi mới bắt đầu | `ladder_streak_warn_count` (§4) |

🔴 **Cả BA thứ trên phải LƯU ĐĨA, không để trong RAM.** Đây là chỗ dễ sai nhất của event này, vì
nó sai **đúng ở đường mà nó tồn tại để phục vụ**:

1. User chấp nhận mất chuỗi ở `LoseStreakPopup` → game lưu bản ghi rời màn (`fail`) và gửi kết quả
   thua lên server.
2. User **kill app** trước khi phản hồi về.
3. Mở lại app → data event về → `SyncStreak(witnessedWin: false)` → nhánh tụt đọc `LoadExitRecord()`.

Nếu bản ghi rời màn chỉ nằm trong RAM thì ở bước 3 nó **đã mất**, `SyncStreak` thấy cờ đang chơi còn
đó và trả `app_kill` — **đúng cái nó phải phân biệt**. Cũng vậy với bộ đếm cảnh báo: để trong RAM
thì **mọi** dòng `app_kill` mang `warn_count = 0`, và phép tính "ở lại sau cảnh báo" thiếu đúng
phần người chơi đã force-close.

🔴 **Lưu TRƯỚC khi gửi kết quả thua**, không phải sau. Gửi rồi mới lưu là có cửa sổ mất dữ liệu
đúng bằng độ trễ mạng.

🔴 **Xoá sau khi dùng.** Đọc rồi phải `ClearExitRecord()`, không thì dòng tụt kế tiếp
mượn lại lý do của lần trước.

---

## 4. Bộ đếm cảnh báo — LƯU ĐĨA, đừng quên reset

```csharp
// Tăng mỗi lần LoseStreakPopup hiện trong LƯỢT CHƠI hiện tại. Reset khi lượt mới bắt đầu.
// 🔴 LƯU ĐĨA, không phải biến static — xem §3.7, đường mở lại app.
private static int WarnCountInTurn { get; set; }
```

Giá trị này đi vào **dòng kết thúc lượt** (§5.1 source `win`, §5.2 mọi sink trừ `sync`) qua key
`ladder_streak_warn_count`, và **luôn gửi kể cả khi bằng 0** — đó là vế "đã ở lại" của phép tính.

---

## 5. Ba dòng tài nguyên

```csharp
private static ResourceParam Row(string where, string currency, string itemId, long amount)
    => new()
    {
        exchangeId    = Guid.NewGuid().ToString("N"),
        resourceWhen  = FResourceWhen.EventPrefix + EVENT,   // "event_ladder_streak"
        resourceWhere = where,
        itemType      = EVENT,                                // "ladder_streak"
        currency      = currency,
        itemId        = itemId,
        amount        = amount
    };
```

🔴 **Phải điền CẢ NĂM**: `resourceWhen` · `resourceWhere` · `itemType` · `currency` · `itemId`.
Bản gốc đánh `item_type`/`item_id` là **"Mở rộng"** — **sai**: năm field này mặc định là chuỗi
`"Unknown"`, không phải null. Bỏ trống là gửi `"Unknown"` lên kho, một giá trị **trông hợp lệ** nên
không ai biết nó thiếu. Dùng `Row(...)` để không quên.

### 5.1 + 5.2 Dòng chuỗi — tăng và tụt

```csharp
private static void WriteStreakRow(LadderStreakData data, string cycleId,
                                   int from, int to, string via, bool endOfTurn,
                                   string warnSurface = null)
{
    var isUp = to > from;

    var row = Row("level_end", "ladder_streak_count", "ladder_streak_count",
                  Math.Abs(to - from));                  // 🔴 LUÔN DƯƠNG, kể cả dòng tụt
    row.valueBefore    = from;
    row.valueAfter     = to;
    row.liveOpsCycleId = cycleId;                        // gán TRƯỚC Stamp — §8 bẫy 4
    row.detail         = StreakDetail(data, via, endOfTurn, warnSurface);

    FalconBigDataController.LiveOps.Stamp(row, EVENT);   // KHÔNG stage, KHÔNG mã lượt

    if (isUp) FalconBigDataController.Resource.OnEarned(row);
    else      FalconBigDataController.Resource.OnSpent(row);
}

private static Dictionary<string, object> StreakDetail(
    LadderStreakData data, string via, bool endOfTurn, string warnSurface)
{
    var d = new Dictionary<string, object> { ["ladder_streak_via"] = via };

    if (data.maxStreakEver > 0)                          // ≤ 0 hoặc chưa biết ⇒ BỎ key
        d["ladder_streak_max_streak"] = data.maxStreakEver;

    if (via is StreakVia.Fail or StreakVia.Retry or StreakVia.Leave)
        d["ladder_streak_warn_surface"] = warnSurface ?? "none";         // lose_popup | revive_panel | none

    if (endOfTurn)
        d["ladder_streak_warn_count"] = WarnCountInTurn;                 // LUÔN gửi, kể cả 0

    return d;
}
```

| | Chuỗi **tăng** | Chuỗi **tụt** |
|---|---|---|
| Hàm | `Resource.OnEarned` | `Resource.OnSpent` |
| `flow` | `source` | `sink` |
| `currency` / `item_id` | `ladder_streak_count` | `ladder_streak_count` |
| `amount` | số bậc tăng, **dương** | số bậc mất, **dương** |
| `value_before` / `value_after` | chuỗi trước / sau, **từ server** | như bên trái |
| `event_when` / `event_where` | `event_ladder_streak` / `level_end` | như bên trái |
| `ladder_streak_via` | `win` · `sync` | `fail` · `retry` · `leave` · `app_kill` · `sync` |
| `ladder_streak_warn_surface` | — | chỉ `fail`/`retry`/`leave` |
| `ladder_streak_warn_count` | dòng `win` | mọi dòng trừ `sync` |
| `live_ops_stage` | **không** | **không** |

🔴 **`amount` LUÔN LÀ SỐ DƯƠNG.** SDK có `CheckNumberNonNegative` trên `amount`, nên số âm bị
chặn lại — chiều đã nằm ở `flow` (`source`/`sink`), không nằm ở dấu của số.

🔴 **Dòng chuỗi KHÔNG mang `live_ops_stage`.** Mốc rương và chuỗi là hai thứ khác loại (§0 câu 1).
Gắn stage vào dòng chuỗi là nối hai thứ không nối được.

🔴 **Dùng `detail`, KHÔNG dùng `extraMeta`.** `ResourceParam` **không có** field `extraMeta` (chỉ
`OldCodeSupportResourceParam` mới có, và đó là đường tương thích code cũ). `detail` là field đúng
và nó trải phẳng ra `event_extra_props`.

### 5.3 Quà mốc n

```csharp
private static void GrantMilestoneRewards(int milestoneId, ClaimSnapshot snap)
{
    foreach (var reward in snap.RewardsOf(milestoneId))      // TỪNG MÓN một dòng
    {
        var row = Row("progression_reward", reward.currency,
                      $"ladder_streak_milestone_{milestoneId}", reward.amount);
        row.liveOpsCycleId = snap.cycleId;                   // đợt ĐƯỢC NHẬN
        FalconBigDataController.LiveOps.Stamp(row, EVENT, stage: milestoneId);
        FalconBigDataController.Resource.OnEarned(row);
    }
}
```

⚠ `currency` là **tên tài nguyên của game** (coin, booster, mạng, NoAds…), không phải
`ladder_streak_count`.

⚠ **Mạng tính bằng PHÚT** ở `amount`, không phải giây.

⚠ **Gói thẻ**: chỉ dòng **gói** mang nhãn Ladder Streak; dòng thẻ lúc mở gói giữ nguyên của Card
Collection.

### Và một luật chung cho cả ba

🔴 **Chuỗi KHÔNG phải tài nguyên.** Nó ngồi cùng bảng với coin và booster chỉ vì **hình dạng** của
dòng tài nguyên đúng với nó (có lên, có xuống, có trước/sau, có lý do). Phía đọc số **phải loại**
`currency = ladder_streak_count` khỏi mọi chart Source / Sink.

ℹ️ Luật này **dùng chung** với `team_quest_map` (Team Quest) và `team_arena_flag` (Team Arena) —
cùng một hạng "tiền tệ giả", cùng một luật loại trừ. Đừng viết ba ghi chú rời.

---

## 6. Bề mặt impression — hai cái đang có, hai cái mới

```csharp
// Hàm Show/OnEnable của popup. Cứ gọi MỖI LẦN hiện — volume SDK lo (gộp cụm, gửi kèm count).
private static void Impression(string surface)
{
    var ctx = new LiveOpsRef();
    FalconBigDataController.LiveOps.Stamp(ctx, EVENT);      // event + đợt
    FalconBigDataController.Ui.OnImpression(surface, liveOps: ctx);
}

// LoseStreakPopup OnEnable          → Impression("ladder_streak_lose_warning");
// Popup hồi sinh hiện panel Ladder  → Impression("ladder_streak_revive_panel");
```

🔴 **Hai bề mặt đang gửi thì GIỮ NGUYÊN TÊN** — nếu chúng phủ một UI mà có câu hỏi cần. Đổi tên một
bề mặt đang đúng là tự tạo một mốc đổi nghĩa trong kho **mà không đổi lại được gì**.

⚠ **Nhưng lý do không phải "vì nó đang có".** *"Không xoá key đang có"* là luật bảo vệ **người đang
đọc số**, nên nó chỉ đúng khi cái key đó **trả lời một câu**. Đây là chỗ doc này còn hở: câu B ở §9
tự thừa nhận **chưa biết hai tên đó phủ UI nào**.

| Hoá ra hai tên đó là | Thì |
|---|---|
| màn chính + popup event — hai UI có câu hỏi cần | 🟢 giữ nguyên tên, và **đừng** thêm bề mặt thứ ba cho cùng UI |
| một UI mà **không câu hỏi nào** cần | 🔴 **bỏ**, đừng giữ. Một bề mặt không ai đọc vẫn tốn một dòng mỗi đợt mỗi user, và vẫn bị người sau dùng để trả lời sai |
| cùng UI với `LoseStreakPopup` / panel hồi sinh ở trên | dùng **tên cũ**, bỏ tên mới — một UI một tên |

➡ Đây đúng lỗi đã bắt được ở Team Quest: giữ một thứ **vì nó có trước**, trong khi chưa biết nó
**là gì**.

🔴 **Hai bề mặt ở trên là MỚI**, và là hai UI **khác**: `LoseStreakPopup` (cảnh báo sắp mất chuỗi)
và panel Ladder trong popup hồi sinh. Chúng không phải "màn chính" hay "popup event".

❓ **Câu hỏi cho dev (§9 câu B):** hai tên đang gửi phủ **đúng những UI nào**? Nếu một trong hai
tên đó **đã** là `LoseStreakPopup` thì đừng thêm bề mặt thứ ba cho cùng một UI — dùng lại tên cũ.

⚠ **Phải đóng dấu `live_ops_*` lên impression** (dòng `Stamp` ở trên). Không đóng dấu thì
impression của đợt 1 và đợt 2 **gộp chung một dòng**, vì trục live-op nằm trong khoá gộp cụm.

---

## 7. Những thứ ĐỪNG gửi nữa

| Đừng gửi | Vì |
|---|---|
| `live_ops_round_id` — bất kỳ giá trị nào | §0 câu 2. Mỗi lần leo lại thành một lượt ⇒ van lọc cho ghi lại cả 10 rương |
| Số chuỗi ở `live_ops_stage` | §0 câu 1. Chuỗi lặp lại ⇒ bị van lọc nuốt; chuỗi tụt ⇒ phá nghĩa "mốc không lùi" |
| Ba cột `feature*` | Ladder không có thực thể nào — event solo |
| `live_ops_stage` trên dòng chuỗi | Chuỗi và mốc là hai thứ khác loại |
| Cờ suy ra được: "đang bị đe doạ", "số bậc sắp mất", "đã mất mốc", "tỉ lệ ở lại" | Kho tự suy từ `value_before`/`value_after` + config sàn. Ngoại lệ duy nhất là `milestone` — hợp đồng live-op yêu cầu |
| Lịch đợt bằng key riêng | `DeclareCycle` đã đóng `cycle_start_ts` / `cycle_end_ts` |
| Chuỗi trước/sau, số bậc bằng key riêng | Đã ở `value_before` / `value_after` / `amount` |
| Dòng khi **thua đúng sàn** (chuỗi không đổi) | Không có thay đổi. Số lần đó đọc từ log level |
| **Stamp** dòng level / rewarded ad / coin hồi sinh / IAP | Event chạy **chồng** lên level thường ⇒ không thuộc hẳn Ladder. 🟢 Bản đang chạy làm đúng chỗ này |
| Nhân vật leo bậc, anim rương, badge, tooltip | Không câu hỏi nào cần |

---

## 8. Bảy cái bẫy, xếp theo mức đau

### Bẫy 1 — 🔴 vì sao chuỗi KHÔNG được vào `live_ops_stage`

Câu này đáng đọc hết, vì nó là cái bẫy mà bản thiết kế gốc đã đi đúng và dễ bị "sửa lại cho gọn".

`live_ops_stage` nằm **trong khoá van lọc** `(event, đợt, lượt, action, stage)`. Nếu chuỗi là
stage thì có **ba** thứ vỡ cùng lúc:

| | Vỡ thế nào |
|---|---|
| ① Van lọc nén theo stage | Chuỗi đi `3 → 4 → 5 → 4 → 5 → 6`: nó **quay lại giá trị cũ**. Lần chạm 5 thứ hai **bị nuốt**. Mất sạch mọi lần leo lại — mà "leo lại" là thứ cả event này đang đo |
| ② Stage theo hợp đồng **không lùi** | Hợp đồng định nghĩa stage là thang **cộng dồn 1..N**, và SDK có `CurrentStage` = mốc **cao nhất** đã log. Chuỗi thì **tụt**. Nhét số biết lùi vào ô không được lùi là phá nghĩa cột đó ở **mọi event khác** |
| ③ Stage là **một** con số | Chuỗi cần **bốn**: trước (`value_before`), sau (`value_after`), mất mấy bậc (`amount`), vì sao (`ladder_streak_via`). `live_ops_stage` không có chỗ cho ba thứ sau |

Và sâu hơn — luật của cả hệ, không riêng Ladder:

> **Bước live-op là MỐC** — trả lời *"đã tới đây chưa"*, một dòng mỗi đợt.
> **Dòng tài nguyên là MÁY ĐẾM** — trả lời *"bao nhiêu, từ đâu đến đâu, vì sao"*.

Chuỗi đổi **vài chục lần một đợt**. Đó là grain của máy đếm. Van lọc nén bước **chính là để** ngăn
người ta dùng bước làm máy đếm.

➡ Nên thiết kế đúng là: **rương 1–10 → `live_ops_stage`**; **chuỗi → dòng tài nguyên**.

### Bẫy 2 — mã đợt rỗng: mất sạch từ đợt thứ hai

Van lọc khoá theo `(event, đợt, lượt, action, stage)`. Mã đợt `''` ⇒ đợt 1 và đợt 2 ra **cùng
khoá** ⇒ từ đợt 2 mọi bước **bị nuốt**, im lặng, không ghi bù được. **Chưa biết mã đợt thì KHOAN
gửi** (§2 đã guard).

🟢 Bản đang chạy **đã đúng** chỗ này — giữ nguyên.

### Bẫy 3 — năm field resource mặc định `"Unknown"`

§5. Dùng helper `Row(...)`.

### Bẫy 4 — dòng nhận muộn phải khai **đợt được nhận**

Đợt N kết thúc, đợt N+1 mở, user mới mở app và nhận quà ở popup cuối đợt. `claim`, `complete` và
dòng quà **phải** tự gán `liveOpsCycleId = snap.cycleId`. Không gán thì SDK điền **đợt đang chạy**
⇒ quà của đợt N mang mã đợt N+1.

🔴 Trên **dòng tài nguyên** phải gán **TRƯỚC** khi gọi `Stamp` — `Stamp` dùng `??=`, nên giá trị
đã có thì nó không đè. Gán sau là vô tác dụng.

❓ **Câu hỏi cho dev (§9 câu D):** data của popup cuối đợt **đã** mang mã đợt **mới** chưa? Nếu
rồi thì phải **chụp lại** mã đợt cũ trước khi data mới về, không đọc trực tiếp từ data hiện tại.

### Bẫy 5 — `eligible` / `open` là một dòng MỖI ĐỢT

Đừng treo phân bố, đừng treo **trạng thái** lên chúng.

🔴 **Luật một câu:** van lọc phá thứ **BIẾN ĐỔI** trong đợt; thứ **BẤT BIẾN** trong đợt thì một
dòng là đủ. (`cycle_start_ts` / `cycle_end_ts` thuộc loại thứ hai — chúng đi đúng đường này.)

### Bẫy 6 — hai đường ghi dòng chuỗi = đếm đôi

§0 câu 3 và §3.7. Nếu phản hồi thắng ghi một dòng **và** `OnDataUpdated` sau đó ghi một dòng nữa
cho cùng thay đổi, kho thấy chuỗi tăng **hai** bậc. Mốc so sánh là thứ chặn chuyện này — nên
**mọi** đường phải cập nhật **cùng** mốc đó, và chỉ **một** hàm được quyền ghi.

### Bẫy 7 — impression không sống qua kill app cứng

Rổ gộp **không** ghi xuống đĩa (cố ý: zero IO trên đường nóng), flush lúc app pause hoặc khi vượt
32 rổ. Người chơi force-close là mất rổ đang dở. Chấp nhận được cho telemetry exposure — nhưng
**đừng** dùng impression làm mẫu số cho phép tính cần chính xác tuyệt đối. Đối soát
`ladder_streak_warn_count` (§4) là đường chính xác hơn.

---

## 9. Việc của server, và sáu câu hỏi cho dev

### Hai bảng cần từ server

| Bảng | Cột | Không có thì |
|---|---|---|
| Lịch đợt (1 dòng / đợt) | mã đợt, STT, giờ bắt đầu, giờ kết thúc | Không đổi log. Giờ kết thúc cần cho phép đọc "nhận trước / sau khi hết đợt" |
| Config mốc mỗi đợt | mã đợt, `milestone_id`, `win_streak`, `is_checkpoint`, quà, `valid_from` | Nhãn mốc dùng `ladder_streak_milestone_threshold` thay được; nhưng **không biết sàn** ⇒ không tách được "chuỗi đang bị đe doạ" |

⚠ Mã đợt của server phải **trùng ĐÚNG GIÁ TRỊ** client gửi ở `live_ops_cycle_id`. Lệch một ký tự
là không nối được.

### Sáu câu hỏi — không câu nào chặn ship

| # | Hỏi ai | Câu hỏi | Không trả lời thì |
|---|---|---|---|
| **A** | Dev | Key hồi sinh của game trong "booster đã dùng" của log level là gì, và `ad_when` / `placement` của rewarded ad hồi sinh là gì? | Không đổi log, nhưng không nhận ra lượt hồi sinh ⇒ không trả lời được nhóm câu hỏi hồi sinh |
| **B** | Dev | Hai tên surface đang gửi phủ **đúng những UI nào**? | §6 — có thể thêm bề mặt thứ ba cho cùng một UI |
| **C** | Dev | Thứ tự callback `MarkJoined` ↔ lần refresh data ngay sau đó: cái nào chạy trước? | §3.3 — chạy sai thứ tự thì cú bấm **Tham gia** thật bị ghi là `auto`, im lặng, không sửa được sau |
| **D** | Dev | Data của popup cuối đợt đã mang mã đợt **mới** chưa? | §8 bẫy 4 — nếu rồi mà không chụp lại thì dòng nhận muộn mang nhầm đợt |
| **E** | Dev | Ladder đếm **màn của map chính**, hay có bộ level riêng? | 🟢 Dữ liệu đang chạy **khớp với "map chính"** (không dòng level nào mang `live_ops_event = ladder_streak`), và như vậy là **đúng** hợp đồng. Nếu thực ra là bộ level riêng thì dòng level **phải** được đóng dấu để có cặp `live_ops_level_*` |
| **F** | Dev | Phản hồi `LevelResult` có mang **`winStreak` mới** luôn, hay chuỗi chỉ về ở lần fetch data sau đó? | §3.7 chỗ gọi số 1 giả định là **có**. Nếu không thì **mọi** màn thắng ghi `via = sync` thay vì `win` ⇒ mất tử số của "% user có ≥ 1 màn được tính" và mất dòng mang `warn_count`. Chữa được: chờ lần fetch ngay sau đó rồi gọi với `witnessedWin: true` nếu lần fetch đó là hệ quả của phản hồi thắng |

### Câu đã đóng

| Câu gốc | Kết quả |
|---|---|
| Server thêm `cycleId` / `cycleIndex` / `timeStart` vào config được không? (bản gốc đánh **chặn ship**) | ✅ **Đóng.** Dữ liệu đang chạy cho thấy mã đợt đã có mặt trên 100% dòng và chung mọi user. Không chờ gì |
| SDK truyền mã đợt cũ cho dòng nhận muộn thế nào? | ✅ **Đóng.** Truyền tay `cycleId` vào `OnStep`, hoặc gán `liveOpsCycleId` **trước** `Stamp` — tay thắng mặc định. §8 bẫy 4 |
| `live_ops_stage` đang là `milestoneId` hay số chuỗi? | ✅ **Đóng — là `milestoneId` 1…10.** Không phải sửa gì, không cần mốc đổi nghĩa |

---

## 10. Việc phía đọc số (không phải việc dev client)

### 🔴 `milestone` CÓ THỂ nhân đôi — chống trùng Ở ĐÂY, không ở client

Van lọc là sổ **theo máy**. Nên:

1. **Máy A**: chuỗi chạm ngưỡng rương 3 → bắn `milestone(3)`, ghi vào van lọc **của A**
2. Sổ của một máy khác (hay của chính máy đó sau khi cài lại) **rỗng** cho đợt này → §3.4 thấy
   `winStreak <= maxStreakEver` → bắn **`milestone(3)` lần nữa**

Kho có **hai dòng**: cùng người, cùng đợt, cùng `live_ops_stage = 3`. Dòng thứ hai có thể cách
dòng thật **vài ngày**.

**Ba đường dẫn tới ca này**, và chỉ một đường là multi-device:

| Đường | Tần suất |
|---|---|
| Máy thứ hai | ít |
| **Cài lại app / xoá data** | thường xuyên |
| **Lên bản app lần đầu có log này** lúc đang giữa đợt | mỗi user một lần |

🔴 **Vì sao KHÔNG chữa ở client, và cũng KHÔNG chữa ở server.** Thứ bị lệch **không phải trạng
thái game**: `winStreak` / `maxStreakEver` / rương nào đã mở đều do server giữ và **đã đồng bộ
đúng** — chính vì đồng bộ đúng mà máy thứ hai mới *biết* rương 3 đã mở để đi ghi bù. Thứ lệch là
**sổ kế toán "dòng log này đã gửi chưa"**, mà client thì **không thể** biết máy khác gửi gì, còn
server thì **không nên** giữ — đó là dựng một service chống trùng log bên trong game server, cho
một event, để cứu một câu hỏi.

➡ Kho là chỗ **duy nhất** thấy được cả ba đường (mọi máy, mọi lần cài lại, mọi bản app), và chống
trùng lúc đọc ở đây **tốn 0 đồng**. Luật đọc số:

| Câu | An toàn chưa |
|---|---|
| **% user đạt rương n** | ✅ **có** dòng `milestone` stage n (EXISTS) — trùng không ảnh hưởng |
| **% nhận quà sau khi đạt** | ✅ user có `claim` n ÷ user có `milestone` n (EXISTS) |
| **Thời gian `milestone` → `claim`** | 🔴 lấy **`min(created_date)`** theo (user, đợt, stage) |
| **Tốc độ đạt mốc** (phân tích mở rộng) | 🔴 như trên |

🔴 **`min(created_date)` là đúng, không phải tạm.** `created_date` là giờ **TẠO** log, không phải
giờ log tới server. Máy nào **chứng kiến** thì tạo log **trước**. Nên dòng sớm nhất luôn là dòng
thật, kể cả khi nó tới kho muộn hơn.

🔴 **Bỏ phép lọc `level_attempts_since_enter = 0`** mà bản gốc đề xuất để loại dòng ghi bù. Nó
không tin được: bộ đếm đó là **"từ lúc vào ĐỢT"** trên **máy đang gửi**, nên máy B vừa cài có thể
đã chơi vài ván trước khi data về (đếm > 0 mà vẫn là dòng ghi bù), còn máy A có thể bắn mốc ngay
ván đầu (đếm = 0 mà là dòng thật). Dùng `min(created_date)`.

### ✅ Hai lựa chọn đã cân và đã LOẠI (owner 06/11) — đừng mở lại

**① Thêm key `ladder_streak_log_via` = `live`/`sync` lên bước `milestone`** (khuôn
`boat_rush_log_via`). **Loại.**

🔴 Lý do chính không phải "nó là cờ suy ra được" (dù luật §1.1-3 của bản gốc đúng là cấm thế).
Lý do chính là: **`via` không SỬA cái trùng, nó chỉ DÁN NHÃN lên cái trùng.** Có nó thì kho **vẫn**
có hai dòng và **vẫn** phải có một luật chọn dòng — chỉ đổi từ *"lấy `min(created_date)`"* thành
*"lấy dòng `live`"*. Cùng lượng việc phía đọc, cộng thêm một key và một lần dev sửa. Nó mua được
đúng **một bit** mà `min()` không có — *"chưa máy nào từng chứng kiến mốc này"*, ca mốc đạt
**trước** khi bản app có log — mà bit đó **`app_version` đã cho rồi**.

**② Để SERVER gửi bước `milestone`.** **Loại** — dù đây là cách duy nhất chữa tận gốc (server là
chỗ duy nhất biết "mốc này đã công bố chưa", độc lập máy ⇒ một dòng mỗi (user, đợt, mốc), mãi
mãi). Giá là một đường gửi log phía server cho riêng event này; mua được **một** câu hỏi, mà câu
đó `min(created_date)` đã trả lời đúng và miễn phí.

➡ **Chốt: chống trùng nằm ở phía đọc, không ở đường ghi.** Client không thêm gì, server không
thêm gì.

ℹ️ Bước `join` **không** có vấn đề này: `ladder_streak_join_via = auto` / `carry` đã là dấu "máy
này không chứng kiến".

➡ Và mục nghiệm thu *"không ghi lặp"* phải đọc là **"không ghi lặp trên cùng một máy, trong cùng
một lần cài"**.

### Hai mẫu số đổi cách đọc

| Câu | Bản gốc | Đổi thành |
|---|---|---|
| **Q1** — % user unlock có joined, **theo ngày** | mẫu số = user có `eligible`, theo ngày | `eligible` là **một dòng mỗi đợt** nên không ra số theo ngày. Mẫu số: **active hôm đó** (bảng user daily) × **đã unlock** (`max_passed_level`) × **hôm đó nằm trong** `cycle_start_ts`/`cycle_end_ts`. Client **không sửa gì** |
| **Q2** — % user có ≥ 1 màn được tính, **theo ngày** | như trên | như trên. Tử số thì đã theo ngày đúng (dòng chuỗi `via = win` có `created_day`) |

⚠ Thứ **không** suy lại được: `open` chỉ nói *"đã thấy UI đâu đó trong đợt"*. **Ngày nào** thì
thông tin đó chưa bao giờ rời khỏi máy — không khâu xử lý nào lấy lại được. Nên đừng đặt mẫu số
theo ngày lên bước bị nén.

⚠ **Đừng hardcode độ dài đợt.** Độ dài đợt là config và **đã từng đổi**. Mọi phép tính theo đợt
đọc `cycle_start_ts` / `cycle_end_ts` của chính dòng đó.

### Một phép kiểm chất lượng miễn phí

Với mỗi (user, đợt), sắp dòng chuỗi theo `created_date`: `value_before` của dòng k phải bằng
`value_after` của dòng k − 1. Lệch ở đâu là **mốc so sánh của client sai** ở đó — và nhờ
`value_before`/`value_after` mà chuỗi tự xếp được thứ tự, không cần trông vào giờ gửi.

---

## 11. Tự kiểm trước khi bàn giao

| Kiểm | Đạt khi |
|---|---|
| Thắng 1 màn được tính khi đã join | Đúng **1** dòng chuỗi `source`, `via = win`, `amount = 1`, có `value_before`/`value_after`, có `ladder_streak_warn_count` |
| Thua khi chuỗi > sàn | Đúng **1** dòng chuỗi `sink`, `value_after` = sàn, `amount` **dương**, có `via` và `warn_surface` |
| Thua đúng sàn | **0** dòng chuỗi |
| Kill app giữa màn rồi mở lại | Đúng **1** dòng `sink`, `via = app_kill`, và `warn_count` = số lần `LoseStreakPopup` đã hiện (🔴 **không** phải 0 — nếu ra 0 thì bộ đếm còn trong RAM, §3.7) |
| Chấp nhận mất ở `LoseStreakPopup` rồi **kill app ngay**, mở lại | `via = `**`fail`** và có `warn_surface = lose_popup` — 🔴 **không** phải `app_kill`. Ra `app_kill` thì bản ghi rời màn còn trong RAM |
| Bấm **Tham gia** ở Intro | `join` mang `join_via = `**`continue`** — 🔴 ra `auto` nghĩa là lần refresh data chạy trước callback (§3.3) |
| Cài lại / máy thứ 2 | **0** dòng chuỗi mới cho tới thay đổi kế tiếp (mốc so sánh lấy từ server, không ghi dòng) |
| Chuỗi vượt 2 ngưỡng trong một lần đồng bộ | **2** dòng `milestone`, `live_ops_stage` tăng dần, mỗi dòng có `ladder_streak_milestone_threshold` |
| Mốc đã gửi rồi, data về lại | **0** dòng `milestone` mới (van lọc chặn — `OnStep` trả `false`, không log lỗi) |
| Nhận gộp 3 mốc | **3** dòng `claim`, mỗi dòng một `live_ops_stage`, kèm quà của cả 3 mốc |
| Ba loại dòng tài nguyên | Không dòng nào có `item_type` hay `item_id` = `"Unknown"` |
| Dòng chuỗi | **Không** dòng nào mang `live_ops_stage`; **không** dòng nào mang `live_ops_round_id` |
| Mở LoseStreakPopup 3 lần trong một lượt | **1** dòng impression `ladder_streak_lose_warning` với `count = 3`, có `live_ops_event` + `live_ops_cycle_id`; và dòng kết thúc lượt mang `ladder_streak_warn_count = 3` |
| Nhận quà ở popup cuối đợt khi đợt mới đã chạy | `claim`, `complete` và dòng quà mang **mã đợt CŨ** |
| Đợt thứ hai | Mọi bước lên log bình thường |
| Đóng dấu đúng chỗ | **Không** dòng level / ads / IAP nào mang `live_ops_event = ladder_streak` |
| Khắp nơi | **Không** dòng nào của event mang `live_ops_round_id`, và **không** dòng nào mang `feature` / `feature_instance_id` / `feature_stage` |
