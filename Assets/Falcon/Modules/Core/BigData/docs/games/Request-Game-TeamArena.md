# Tracking Team Arena — TODO cho dev + code dán vào

> **Dev chỉ cần đọc §1 (checklist) rồi dán code ở §2–§6.** Tick `[x]` khi xong.
> Viết 05/11/2026. API: `FalconBigDataController.LiveOps` / `.Feature` / `.Ui` / `.Resource`
> (BigData, hợp đồng `docs/design/LiveOpsEventContract.md` + `docs/design/FeatureContract.md`).
> Câu hỏi và tham số gốc do bên dữ liệu chốt — bản Arena của họ (`arena_log.md`,
> `arena_dataset.md`) chỉ để **tham khảo**; bản này là bản cầm tay, và chỗ nào khác bản gốc thì
> đã ghi rõ lý do ngay tại chỗ (§3.2, §3.3, §3.6, §4, §7).
> ✅ Doc này **không chờ gì từ SDK**, và **không còn câu hỏi mở nào** — code được ngay toàn bộ.
> Chỉ một thứ tự công việc đã chốt: **tính năng Team ship trước hoặc cùng Arena** (§4, §9).

## 0. Bốn câu phải nhớ trước khi gõ dòng nào

1. **Arena là EVENT, guild là TÍNH NĂNG — hai trục khác nhau, một dòng mang CẢ HAI.**
   `live_ops_*` nói "dòng này thuộc đợt Arena nào"; `feature*` nói "dòng này của guild nào".
   Đừng gộp, đừng chọn một.
2. **`live_ops_round_id` của Arena = id BẢNG ĐẤU**, không phải guild. Guild đi
   `featureInstanceId`. Ô "lượt" đã có chủ rồi.
3. **Mỗi dòng Arena phải nói rõ GUILD NÀO.** Arena mà phát sinh một dòng không rõ guild nào là
   dòng vô dụng cho hơn nửa số câu hỏi. Dùng `Feature.Stamp(...)` — §2.
4. **Bỏ `team_id` / `team_role` khỏi mọi log.** Kho tự dán trạng thái từ bản tin `state` của
   tính năng Team. Chỉ khi dòng **do** Arena/guild gây ra thì mới tự điền cột `feature*` để khẳng
   định nhân quả — xem §7.

---

## 1. Checklist

**Khung đợt** (§2)
- [ ] `DeclareCycle` mỗi lần dữ liệu Arena về

**Bảy bước live-op** (§3)
- [ ] `eligible` — mỗi lần dữ liệu về, khi đủ 4 điều kiện
- [ ] `open` — popup hiện lên, **KHÔNG mang key nào**
- [ ] `join` — lần đầu thấy server báo đã join đợt này
- [ ] `milestone` — mỗi mốc cá nhân, mỗi mốc 1 dòng
- [ ] `claim` **có** stage — nhận quà mốc
- [ ] `complete` — màn kết quả cuối đợt hiện lần đầu
- [ ] `claim` **không** stage — bấm Claim cuối đợt

**Ba dòng tài nguyên** (§5)
- [ ] Điểm cờ
- [ ] Quà mốc n
- [ ] Thưởng hạng team

**Hai bề mặt impression** (§6) — **việc MỚI**, bản gốc không có
- [ ] `team_arena_join_popup`
- [ ] `team_arena_event_popup`

**Dọn dẹp** (§7)
- [ ] Xoá `team_id` / `team_role` khỏi mọi log
- [ ] Xoá 3 key khỏi bước `open`
- [ ] Xoá `team_arena_team_id` (dùng `featureInstanceId`)
- [ ] Không Stamp dòng level / ads / IAP

---

## 2. Khai đợt + hai dòng dán một lần

Dán vào lớp tracking của Arena. **Hai hàm này là chỗ duy nhất gõ tên event và guild id**, nên
không chỗ nào khác được gõ chuỗi tay.

```csharp
private const string EVENT = "team_arena";

/// Gọi MỖI LẦN dữ liệu Arena về (gọi lại nhiều lần vô hại — nó chỉ ghi state, không bắn log).
public static void OnDataUpdated(TeamArenaData data)
{
    if (data == null || string.IsNullOrEmpty(data.cycleId)) return;   // chưa biết mã đợt thì KHOAN

    FalconBigDataController.LiveOps.DeclareCycle(
        EVENT, data.cycleId,
        cycleIndex:   data.cycleIndex,
        cycleStartTs: data.startTsMs,      // epoch MILLIS UTC
        cycleEndTs:   data.endTsMs);
}

/// Guild id hiện tại — null khi chưa có guild. KHÔNG bịa chuỗi rỗng.
private static string GuildId() => TeamService.Current?.TeamId;

/// Mọi bước live-op của Arena đi qua đây, nên không bước nào quên guild.
private static bool Step(string action, int? stage = null, string roundId = null,
                         Dictionary<string, object> extra = null)
{
    var param = new LiveOpsParam
    {
        liveOpsEvent     = EVENT,
        action           = action,
        liveOpsStage     = stage,
        liveOpsRoundId   = roundId,       // id BẢNG ĐẤU, chỉ ở chỗ §3 ghi ✓
        extraMeta        = extra
    };

    // Trục tính năng: dòng này của guild nào. Vắng guild = user chưa có guild, đúng và hợp lệ.
    FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, GuildId());

    return FalconBigDataController.LiveOps.OnStep(param);
}
```

⚠ `OnStep` trả `false` khi van lọc chặn (bước đã log trong đợt/lượt này) — **bình thường, đừng
xử lý gì**. Đừng thử lại, đừng log cảnh báo.

⚠ **Mã đợt chưa biết thì KHOAN gửi**, chờ dữ liệu về rồi gửi. Mã đợt rỗng làm van lọc gộp mọi
đợt thành một, và từ đợt thứ hai mọi bước bị nuốt — **im lặng, vĩnh viễn** (§8 bẫy 1).

---

## 3. Bảy bước live-op — code dán vào

### 3.1 `eligible` — mẫu số gốc của phễu

```csharp
// Gọi MỖI LẦN dữ liệu event hoặc dữ liệu team về, khi đủ CẢ BỐN:
//   · giờ server trong [cycle_start, cycle_end − 2h)   ← cắt 2h cuối vì lúc đó không join được
//   · trạng thái PreJoin / InProgress
//   · level ≥ level mở event
//   · GetEligibility() == Eligible
Step(FLiveOpsAction.Eligible, extra: new Dictionary<string, object>
{
    // NGƯỠNG sĩ số để guild đủ điều kiện — gửi SỐ THÔ, đừng gửi "đủ / không đủ".
    ["team_arena_min_members"] = config.minMembers
});
```

🔴 **Vì sao phải gửi ngưỡng:** kho suy "guild thiếu người" bằng `member_count < ngưỡng` (§4). Nếu
ngưỡng chỉ sống bên kho thì hôm nào game đổi config từ 10 xuống 5, kho vẫn chia theo 10 — **số sai
im lặng**. Gửi lên thì tự đúng, kể cả khi mỗi game một số hay đổi theo đợt.

⚠ **Gọi mỗi lần, đừng tự nhớ "đã gọi rồi".** SDK nén còn một dòng mỗi đợt. Tự nhớ thì từ đợt thứ
hai mẫu số rỗng — đúng bug Master League G1.

### 3.2 `open` — KHÔNG mang key nào

```csharp
// Popup Join HOẶC popup Event hiện lên (OnEnable), mở từ bất kỳ đâu.
Step(FLiveOpsAction.Open);
```

🔴 **Khác bản gốc.** Bản gốc treo 3 key lên bước này: `team_arena_popup`, `team_arena_eligibility`,
`team_arena_open_source`. **Bỏ cả ba.** Lý do: `open` bị nén còn **một dòng mỗi đợt**, nên ba key
đó là "của lần mở ĐẦU TIÊN", không phải phân bố.

Hỏng cụ thể: guild mới có 6 người → user mở event → thấy khung chặn → gửi `open` kèm
`not_enough_members`. Guild tuyển đủ 12 người → user mở lại → **bị van lọc nuốt**. Kho **mãi mãi**
thấy user này bị chặn.

Ba key đó về đâu:

| Key | Về đâu |
|---|---|
| `team_arena_eligibility` | **xoá luôn, không gửi ở đâu cả** — kho suy được từ bản tin `state` của Team (§4) |
| `team_arena_popup` | **tên surface** của impression (§6) |
| `team_arena_open_source` | **tên surface** của impression (§6) |

`open` giữ đúng nghĩa SDK định: *"user này có mở UI event trong đợt hay không"*.

### 3.3 `join` — một dòng mỗi đợt, KHÔNG mang mã lượt

```csharp
// LẦN ĐẦU client thấy từ dữ liệu SERVER rằng user đã join đợt này:
// bấm Continue / bấm X ở khung Eligible, join lúc offline gửi bù, hoặc đã join trước khi
// lên bản app có log. Join LỖI thì không gửi.
Step(FLiveOpsAction.Join, extra: new Dictionary<string, object>
{
    ["team_arena_join_via"]      = viaContinue ? "continue" : "close",  // bỏ key nếu không rõ
    ["team_member_count"]        = team.MemberCount,
    ["team_total_contribution"]  = team.TotalContribution,
    ["team_required_level"]      = team.RequiredLevel
});
```

🔴 **Khác bản gốc.** Bản gốc hứa *"đổi team giữa đợt rồi join lại thì có thêm 1 dòng"*. **Không
xảy ra**, và **không cần**:

- Van lọc khoá theo `(event, đợt, lượt, bước, mốc)`. `join` không mang lượt ⇒ lần thứ hai **trùng
  tên hoàn toàn** ⇒ bị nuốt.
- Nhưng thông tin đó **không mất**: guild của từng dòng nằm ở `featureInstanceId`; **thời điểm đổi
  guild** nằm ở cặp bản tin `exit` → `enter` (theo `feature_effect`) của tính năng Team.
- Và **mốc cá nhân không cày lại khi đổi guild** (lũy kế cả đợt), nên chuỗi *"join → mốc 1–5 ở
  guild A → đổi sang B → mốc 6–10"* dựng lại được đầy đủ từ `featureInstanceId` trên từng dòng
  `milestone`.
- Gốc thời gian của Q11 lấy `join` **đầu tiên** còn **đúng hơn**, vì mốc không reset.

⚠ Hệ quả đã chấp nhận: Q22 (thuộc tính guild lúc vào đợt) chỉ có của guild **đầu tiên** trong đợt.
Guild nào có thành viên join lúc đang ở guild đó thì vẫn có số; chỉ guild mà **toàn bộ** thành viên
đến giữa đợt là thiếu — lúc đó dùng snapshot server (§9).

⚠ **Phụ thuộc chéo**: cách trên chỉ đứng được nếu bản tin `enter` / `exit` của tính năng **Team đã
chạy** trong game này. Chưa chạy thì kho không biết user đổi guild lúc nào — xem §9.

### 3.4 `milestone` — mỗi mốc một dòng, KHÔNG mang mã lượt

```csharp
// Điểm cá nhân LŨY KẾ vừa chạm ngưỡng mốc n. Qua nhiều mốc cùng lúc thì mỗi mốc 1 dòng.
// So NGƯỠNG ĐIỂM (dùng đúng hàm thanh tiến trình UI dùng), KHÔNG so chỉ số mốc.
// Bắn ở trận thắng có điểm; KHÔNG bắn lúc join.
foreach (var n in newlyReachedMilestones)       // tăng dần
    Step(FLiveOpsAction.Milestone, stage: n);
```

⚠ **Tuyệt đối không cho mã lượt vào đây.** Mốc cá nhân lũy kế xuyên đợt; đổi guild ⇒ đổi bảng đấu
⇒ mã lượt mới ⇒ van lọc cho ghi **lại** các mốc đã có ⇒ **đếm đôi mốc 1–5**. Bản gốc nói đúng
chuyện này, giữ nguyên.

### 3.5 `claim` CÓ stage — nhận quà mốc

```csharp
Step(FLiveOpsAction.Claim, stage: n);           // không mang mã lượt
```

### 3.6 `complete` — màn kết quả cuối đợt

```csharp
// Màn kết quả hiện LẦN ĐẦU, SAU khi đã có dữ liệu kết quả từ server.
var param = new LiveOpsParam
{
    liveOpsEvent   = EVENT,
    action         = FLiveOpsAction.Complete,
    liveOpsCycleId = result.cycleId,            // ⚠ đợt ĐƯỢC NHẬN, không phải đợt đang chạy
    liveOpsRoundId = result.arenaId,            // ✓ có mã lượt
    extraMeta      = new Dictionary<string, object>
    {
        ["team_arena_team_rank"]       = result.teamRank,        // bỏ key nếu ≤ 0 / chưa biết
        ["team_arena_team_count"]      = result.teamCountInArena,
        ["team_arena_team_points"]     = result.teamPoints,
        ["team_arena_my_points"]       = result.myPoints,
        ["team_arena_can_contribute"]  = result.canContribute    // Bool: LUÔN gửi cả true/false
    }
};

// 🔴 Guild ĐÃ THI ĐẤU, không phải guild đang ở. Đây là nguyên nhân của dòng kết quả.
FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, result.competedTeamId);

FalconBigDataController.LiveOps.OnStep(param);
```

🔴 **Khác bản gốc: bỏ key `team_arena_team_id`.** Nó chính là `featureInstanceId` — "guild mà điểm
của user được tính". Còn "guild đang ở" là **trạng thái**, kho tự dán. Một nghĩa, một cột.

Đây là dòng duy nhất mà hai guild có thể khác nhau, nên đọc kỹ: `featureInstanceId` = guild **đã
thi đấu**.

### 3.7 `claim` KHÔNG stage — Claim cuối đợt

```csharp
// API Claim trả thành công — kể cả khi thưởng hạng = 0. Claim LỖI thì không gửi.
var param = new LiveOpsParam
{
    liveOpsEvent   = EVENT,
    action         = FLiveOpsAction.Claim,
    liveOpsCycleId = result.cycleId,            // đợt ĐƯỢC NHẬN
    liveOpsRoundId = result.arenaId,
    extraMeta      = new Dictionary<string, object>
    {
        ["team_arena_elo_before"] = elo.before,
        ["team_arena_elo_after"]  = elo.after
    }
};
FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, result.competedTeamId);
FalconBigDataController.LiveOps.OnStep(param);
```

⚠ **Số coin thưởng hạng KHÔNG gửi ở đây** — đã có ở dòng tài nguyên (§5).

---

## 4. Mẫu số "theo ngày" lấy từ đâu — và vì sao KHÔNG có custom event

🔴 **Bản gốc có một custom event `team_arena_active` bắn mỗi login, chở 8 key. Bỏ hẳn.** Dev không
phải viết gì ở mục này — đọc để biết số đến từ đâu.

Lý do custom event được đẻ ra là đúng: bước `eligible` bị nén còn **một dòng mỗi đợt**, nên không
đếm được "mỗi ngày bao nhiêu người đủ điều kiện". Nhưng con số đó có đường khác, không cần log mới:

> **người active trong ngày** (bảng user daily) × **đã unlock** (`max_passed_level`, có trên mọi
> dòng) × **có guild + sĩ số** (bản tin **`state`** của tính năng Team, bắn **mỗi login**) ×
> **ngày đó thuộc đợt Arena** (`cycle_start_ts` / `cycle_end_ts`, đã có trên mọi bước live-op).

Và 8 key kia **suy ra được hết**:

| Key của bản gốc | Mức | Suy từ |
|---|---|---|
| `team_arena_eligibility` | Bắt buộc | bản tin `state`: `feature_instance_id = ''` ⇒ không có guild · `member_count < ngưỡng` ⇒ thiếu người · còn lại ⇒ đủ điều kiện |
| `team_arena_can_contribute` | Bắt buộc | `false` nghĩa là *đã góp điểm ở guild khác trong đợt* ⇒ người đó **buộc phải có** dòng điểm cờ mang `feature_instance_id` = guild cũ. So với guild hiện tại là ra |
| `team_arena_state` | Mở rộng | `claimed` ⇔ có dòng `claim`; phần còn lại suy được |
| `team_arena_my_points` | Mở rộng | `max(value_after)` của dòng điểm cờ |
| `team_arena_team_points` | Mở rộng | Σ `amount` theo guild |
| `team_arena_team_rank` | Mở rộng | đã có trên `complete` |
| `team_arena_team_count` | Mở rộng | đã có trên `complete` |
| `team_arena_milestone_pending_count` | Mở rộng | số `milestone` − số `claim` |

⚠ Và `team_arena_eligibility` là một **cờ suy ra được** — đúng thứ §1.1 mục 3 của bản gốc tự cấm.

### ✅ Điều kiện đã chốt: Team ship TRƯỚC hoặc CÙNG Arena (owner 05/11)

Cả mục này đứng trên bản tin `state` của tính năng Team (xem doc cầm tay của Team). Owner đã chốt
thứ tự: **Team ship trước, hoặc ít nhất cùng lúc với Arena.** Nên không còn là câu hỏi mở.

Vẫn giữ thành mục **nghiệm thu**, vì "sẽ ship" là kế hoạch — lúc test phải thấy dòng thật:

- [ ] Mở app một lần ⇒ có **1** bản tin `state` của `feature = guild`, mang `member_count`

⚠ Để biết mất gì nếu thứ tự bị đảo: người **không có guild** và người có **guild thiếu người**
không phát sinh dòng Arena nào cả — họ vô hình tuyệt đối. Nên **Q3** (% unlock mà không có guild)
và **Q8** (% bị chặn vì thiếu người) **chết hẳn**; **Q7** mất hai bậc đầu; **Q1** tụt từ "theo
ngày" xuống "theo đợt" (vì `eligible` một dòng mỗi đợt). Phần còn lại của Arena không ảnh hưởng.

## 5. Ba dòng tài nguyên

Luật chung cho cả ba:

```csharp
private static ResourceParam ArenaRow(string where, string currency, string itemId, long amount)
    => new()
    {
        exchangeId    = Guid.NewGuid().ToString("N"),
        resourceWhen  = FResourceWhen.EventPrefix + EVENT,   // "event_team_arena"
        resourceWhere = where,
        itemType      = EVENT,                                // "team_arena"
        currency      = currency,
        itemId        = itemId,
        amount        = amount
    };
```

🔴 **Phải điền CẢ NĂM**: `resourceWhen`, `resourceWhere`, `itemType`, `currency`, `itemId`. Năm
field này mặc định là chuỗi `"Unknown"`, **không** phải null — bỏ trống là gửi `"Unknown"` lên kho
và không ai biết nó thiếu (§8 bẫy 2).

| Dòng | `resourceWhere` | `currency` | `itemId` | Mã lượt | Mốc |
|---|---|---|---|---|---|
| **Điểm cờ** | `level_end` | `team_arena_flag` | `team_arena_flag` | ✓ | — |
| Quà mốc n | `progression_reward` | tên phần thưởng | `team_arena_milestone_<n>` | — | n |
| Thưởng hạng | `rank_reward` | tên phần thưởng (coin) | `team_arena_rank_<hạng>` | ✓ | — |

### 5.1 Điểm cờ — dòng quan trọng nhất

```csharp
// MỖI KHI game THẬT SỰ cộng điểm cờ sau một trận thắng, theo đúng luật game đang chạy.
// Game cộng ở trường hợp nào thì ghi ở trường hợp đó, KỂ CẢ lúc offline.
// KHÔNG ghi từ fetch / đồng bộ máy khác. delta ≤ 0 thì KHÔNG ra dòng.
var row = ArenaRow("level_end", "team_arena_flag", "team_arena_flag", gained);
row.valueBefore = pointsBefore;     // điểm cá nhân TRONG ĐỢT trước trận
row.valueAfter  = pointsAfter;      // sau trận

FalconBigDataController.LiveOps.Stamp(row, EVENT, roundId: arenaId);
FalconBigDataController.Feature.Stamp(row, FFeatureName.Guild, teamIdGettingThePoint);
FalconBigDataController.Resource.OnEarned(row);
```

🔴 `featureInstanceId` = **guild ĐƯỢC CỘNG ĐIỂM**. Bản gốc đặt việc này lên `team_id` — bỏ, dùng
cột thật.

⚠ **Điểm cờ không phải phần thưởng.** Bên dữ liệu sẽ loại `currency = team_arena_flag` khỏi mọi
chart Source. Vẫn phải ghi, vì mọi câu hỏi theo guild đứng trên dòng này.

### 5.2 Hai dòng thưởng

```csharp
// Quà mốc n — KHÔNG mang mã lượt
var row = ArenaRow("progression_reward", reward.name, $"team_arena_milestone_{n}", reward.amount);
FalconBigDataController.LiveOps.Stamp(row, EVENT, stage: n);
FalconBigDataController.Feature.Stamp(row, FFeatureName.Guild, GuildId());
FalconBigDataController.Resource.OnEarned(row);

// Thưởng hạng team — CÓ mã lượt, và nhận muộn thì khai đợt cũ
var row = ArenaRow("rank_reward", reward.name, $"team_arena_rank_{rank}", reward.amount);
row.liveOpsCycleId = result.cycleId;            // ⚠ đợt ĐƯỢC NHẬN
FalconBigDataController.LiveOps.Stamp(row, EVENT, roundId: result.arenaId);
FalconBigDataController.Feature.Stamp(row, FFeatureName.Guild, result.competedTeamId);
FalconBigDataController.Resource.OnEarned(row);
```

🔴 **Không có dòng "thưởng cá nhân" riêng** (sửa 13/11, team game xác nhận): thưởng hạng team và thưởng cá nhân là **cùng một
lần nhận quà**. Ghi đúng **một** dòng `rank_reward` ở trên. Đừng ghi thêm một dòng `personal_reward` cho cùng món đó — kho sẽ thấy
người chơi nhận **gấp đôi** mà không có lỗi nào báo ra. (Bản trước của doc này có dòng `personal_reward`; bỏ nếu đã code.)

⚠ **Một món = một đường grant.** Đừng gọi thêm `Resource.OnEarned` chỉ để chở ngữ cảnh.

⚠ **Gói thẻ**: chỉ dòng **gói** mang nhãn Arena; dòng thẻ lúc mở gói giữ nguyên của Card
Collection.

---

## 6. Hai bề mặt impression — việc MỚI

Bản gốc không có phần này. Nó thay ba key vừa bỏ khỏi `open` (§3.2).

```csharp
// Hàm Show của popup. Cứ gọi MỖI LẦN hiện — volume SDK lo (nó gộp cụm và gửi kèm count).
public static void OnPopupShown(string popup, string source)
{
    var ctx = new LiveOpsRef();
    FalconBigDataController.LiveOps.Stamp(ctx, EVENT);          // không truyền mã lượt
    FalconBigDataController.Feature.Stamp(ctx, FFeatureName.Guild, GuildId());

    // 🔴 Nguồn mở PHẢI nằm trong TÊN SURFACE, không nhồi vào extraMeta.
    FalconBigDataController.Ui.OnImpression($"team_arena_{popup}_popup_{source}", liveOps: ctx);
}
```

Gọi với:
- `popup` = `join` | `event`
- `source` = `auto_after_win` | `lobby_button` | `chat_banner`

🔴 **Vì sao nguồn phải vào tên surface:** SDK gộp cụm theo `(surface, hành vi, event, đợt, lượt,
mốc, 3 field feature)`. `extraMeta` **không** nằm trong khoá gộp, và cụm chỉ giữ **bản của lần
đầu**. Nhồi nguồn vào `extraMeta` ⇒ ba lần mở từ ba nguồn thành **một dòng mang nguồn của lần
đầu**, hai nguồn kia **rơi mất im lặng**.

⚠ Phía đọc số phải dùng `sum(count)`, **không** `count(*)` — một dòng = nhiều lần hiện.

⚠ Cụm **không sống qua kill app cứng**: mất cụm đang dở là cái giá đã chấp nhận cho telemetry
exposure.

---

## 7. Những thứ ĐỪNG gửi nữa

| Đừng gửi | Vì |
|---|---|
| `team_id` · `team_role` trên **mọi** log | Kho tự dán **trạng thái** từ bản tin `state` của tính năng Team. Dòng nào **do** Arena/guild gây ra thì điền `feature*` — đó là **nhân quả**, khác trạng thái |
| `team_arena_team_id` trên `complete` | Chính là `featureInstanceId` (§3.6) |
| 3 key trên bước `open` | §3.2 |
| Số coin thưởng hạng trên `claim` | Đã có ở dòng tài nguyên |
| Cờ suy ra được: "đang top 3", "là Contributor", bảng thưởng nào áp dụng | Kho tự suy. Ngoại lệ duy nhất là bước `milestone` — hợp đồng live-op yêu cầu |
| Hạng team theo giờ | Kho cộng dồn dòng điểm cờ |
| Fetch leaderboard mỗi 120s · chuyển tab · tooltip · toast · "+N" bay vào nút · popup cảnh báo khi rời guild | Không câu hỏi nào cần. Việc rời guild thật đã có ở bản tin Team |
| **Stamp** dòng level / ads / IAP bằng `team_arena` | Không thuộc hẳn Arena. ⚠ Catalog đang ghi gần 100% dòng `f_sdk_ui_impression` của game có cột `live_ops_*` — soi lại xem có chỗ nào đang đóng dấu theo "event đang chạy" không |

---

## 8. Năm cái bẫy, xếp theo mức đau

### Bẫy 1 — mã đợt rỗng: mất sạch từ đợt thứ hai

Van lọc khoá theo `(event, đợt, lượt, bước, mốc)`. Mã đợt `''` ⇒ đợt 1 và đợt 2 ra **cùng một
khoá** ⇒ từ đợt 2, `join` + mọi `milestone` + mọi `claim` **bị nuốt sạch**, im lặng, không ghi bù
được. **Chưa biết mã đợt thì KHOAN gửi.**

### Bẫy 2 — năm field resource mặc định `"Unknown"`

`resourceWhen` · `resourceWhere` · `itemType` · `currency` · `itemId` mặc định là chuỗi
`"Unknown"`, **không** phải null. Bỏ trống là gửi `"Unknown"` lên kho — một giá trị trông hợp lệ
nên không ai biết nó thiếu. Dùng helper `ArenaRow` ở §5 để không quên.

### Bẫy 3 — mã lượt trên bước mốc = đếm đôi

Đã nói ở §3.4. Chỉ những chỗ sau được mang mã lượt: `complete`, `claim` không stage, và 2 dòng tài nguyên
có ✓ ở bảng §5. Ngoài ra: không.

### Bẫy 4 — `eligible` / `open` là một dòng MỖI ĐỢT

Đừng treo phân bố, đừng treo **trạng thái** lên chúng. Muốn số theo **ngày** thì xem §4; muốn
**đếm số lần** thì dùng impression (§6).

🔴 **Luật một câu để lần sau không treo sai:**

> **Van lọc phá thứ BIẾN ĐỔI trong đợt. Thứ BẤT BIẾN trong đợt thì một dòng là đủ.**

| Treo lên bước nén | Được không |
|---|---|
| `team_arena_eligibility` — guild tuyển thêm người là **đổi** | ❌ chỉ còn giá trị lần đầu, các lần sau mất im lặng |
| `team_arena_min_members` — **config của đợt**, không đổi trong đợt | ✅ một dòng là đủ, vì mọi dòng đều mang cùng giá trị |

`cycle_start_ts` / `cycle_end_ts` cũng thuộc loại thứ hai — chúng đang đi đúng đường này rồi.

### Bẫy 5 — dòng nhận muộn phải tự khai đợt cũ

`complete`, `claim` cuối, thưởng hạng thường được nhận **khi đợt mới đã chạy**.
`Stamp` điền mã **đợt đang chạy**, nên ba chỗ đó phải tự gán `liveOpsCycleId = đợt được nhận`.

ℹ️ Mã **lượt** thì SDK **không** tra hộ nữa (sửa 05/11) — không truyền = dòng không có lượt. Nên
cứ truyền `roundId` ở đúng 4 chỗ cần, các chỗ khác để yên.

---

## 9. Việc của server, và một phụ thuộc chéo

### Hai bảng cần từ server

Client chỉ thấy user online và chỉ thấy leaderboard arena của chính mình, nên số "tại cuối sự
kiện" của **mọi** guild phải do server xuất:

| Bảng | Cột |
|---|---|
| Kết quả arena (1 dòng / guild / đợt) | mã đợt, mã arena, guild id, số guild trong arena, điểm cuối, hạng cuối, điểm tại giờ 24, ELO trước / sau, đủ điều kiện tại cuối đợt |
| Snapshot guild lúc mở đợt (1 dòng / guild / đợt) | mã đợt, guild id, sĩ số, Team Score, Required Level, đủ điều kiện, arena được ghép; **tổng số guild** |

⚠ Mã đợt, mã arena, guild id của server phải **trùng đúng định dạng** với `live_ops_cycle_id`,
`live_ops_round_id`, `feature_instance_id` trên log client. Lệch một ký tự là không nối được.

### 🔴 Bốn log mà bản gốc định "dùng lại" đã bị hợp đồng Team THAY

Bản gốc §2.4 ghi dùng lại `team_active_unlock`, `team_join`, `team_leave`, `team_kick` — **bốn log
đó không còn**. Hợp đồng tính năng thay cả tám log `f_sdk_custom_team_*` bằng **một** log
`f_sdk_feature`. Đây là **việc phía đọc số**, dev client không phải làm gì thêm, nhưng ai viết
query phải biết:

| Bản gốc đọc | Giờ đọc ở |
|---|---|
| `team_active_unlock` | bản tin **`state`** (`feature = guild`, `sub_event = state`), bắn **mỗi login**. "Có / không có guild" đọc từ `feature_instance_id` — chuỗi **rỗng** `''` nghĩa là *đã mở khoá, không ở guild nào* |
| sĩ số lúc login | `member_count` trong extras của **chính bản tin `state`** đó |
| **`team_join`** | 🔴 **`feature_effect = 'enter'`** — KHÔNG phải `sub_event = 'join'` |
| **`team_leave`** | 🔴 **`feature_effect = 'exit'`** — KHÔNG phải `sub_event = 'leave'` |
| vai trò trong guild | `feature_variant` |
| mốc vào guild | `feature_joined_ts` |
| `team_kick` | `sub_event = 'kick_member'` (người **kick** gửi — chính họ không rời guild) |

Chạm tới: Q1, Q2, Q3, Q7 (DAU đã unlock, có / không guild), Q15 (đổi guild giữa hai sự kiện),
Q24 (sĩ số thay đổi trong sự kiện).

🔴 **Sửa 06/11 — "vào / rời guild" đọc bằng `feature_effect`, không bằng tên bước.** Bản trước của
mục này ghi `enter` / `exit` như thể là tên bản tin; chúng là giá trị của **`feature_effect`**.
Và đọc theo tên bước thì **mất dòng**:

| Lọc `sub_event` | Mất gì |
|---|---|
| `= 'join'` | **`create`** (lập guild mới — hợp đồng ghi *"`create` là `enter`"*) và **`join_accepted`** (guild Private, **server gửi hộ**) |
| `= 'leave'` | **bị kick** và **`guild_disbanded`**, cả hai **server gửi hộ** |

Hợp đồng phía kho nói thẳng: *"Profile không lưu `sub_event`... Tên bước không vào code: tính năng /
bước mới chỉ cần gửi đúng `feature_effect`."*

⚠ `feature_effect` hiện ở `event_extra_props`, **chưa được cấp cột** — đọc bằng
`jsonb_extract_string(event_extra_props, '$.feature_effect')`.
⚠ Và `enter` / `exit` **không bao giờ** là giá trị `sub_event` (`sub_event` chở **tên bước**), nên
lọc `sub_event = 'enter'` trả về **rỗng**.

⚠ Và `feature_instance_id = ''` **khác** `NULL`: rỗng = "không ở guild nào", NULL/vắng = "dòng này
không do tính năng guild gây ra". Đọc lẫn hai cái là đếm sai người không có guild.

### Phụ thuộc chéo tính năng — đã chốt thứ tự ship

Arena dựa vào **hai** bản tin khác nhau của Team, cho hai việc khác nhau:

| Bản tin Team | Arena dùng để | Thiếu thì mất |
|---|---|---|
| **`state`** (mỗi login) | có guild hay không + sĩ số | mẫu số theo ngày — Q1, Q3, Q7, Q8 (§4) |
| **`enter` / `exit`** | **thời điểm** đổi guild | chuỗi "mốc nào ở guild nào theo thời gian"; chạm Q15, Q24 và cả lý lẽ `join` một dòng ở §3.3 |

✅ **Owner chốt 05/11: Team ship trước, hoặc ít nhất cùng lúc với Arena.** Không còn câu hỏi mở.

Giữ thành mục nghiệm thu:

- [ ] Đổi guild một lần ⇒ có bản tin `feature_effect = `**`exit`** rồi **`enter`** của `feature = guild`

Thiếu cặp này thì kho biết guild của **từng dòng** (nhờ `feature_instance_id`) nhưng **không biết
thời điểm chuyển** — lúc đó phải quay lại bàn `join`.

---

## 10. Tự kiểm trước khi bàn giao

| Kiểm | Đạt khi |
|---|---|
| Thắng 1 màn Hard khi đã join | Đúng **1** dòng `team_arena_flag`, `amount = 3`, có `value_before`/`value_after`, có `feature_instance_id` |
| **Mọi** dòng Arena | Có `feature = guild` và `feature_instance_id` — trừ dòng của user **chưa có guild** |
| Đăng nhập máy thứ 2 | **0** dòng điểm cờ mới |
| Σ kiểm chất lượng | Trên từng máy, Σ `amount` dòng điểm cờ = `max(value_after)` |
| Mở popup Join 3 lần từ 3 nguồn | **3** cụm impression khác nhau (vì nguồn nằm trong tên surface), Σ `count` = 3 |
| Mở event khi guild thiếu người, rồi đủ người mở lại | Bước `open` chỉ **1** dòng — **đúng**, đừng sửa. Trạng thái guild đọc từ bản tin `state` của Team |
| Bước `eligible` | Có `team_arena_min_members` — thiếu nó là kho không suy được "guild thiếu người" |
| Đạt mốc 1–5 ở guild A, đổi sang B, đạt mốc 6–10 | **10** dòng `milestone`, `feature_instance_id` đổi đúng chỗ. Chỉ **1** dòng `join` — đúng |
| Claim đợt N khi đợt N+1 đã chạy | `complete`, `claim`, dòng thưởng hạng mang **mã đợt N** |
| Đóng dấu đúng chỗ | **Không** dòng level / ads / IAP nào mang `live_ops_event = team_arena` |
| Đợt thứ hai | Mọi bước lên log bình thường (không bị van lọc nuốt vì mã đợt rỗng) |
