# Tracking Team Quest — TODO cho dev + code dán vào

> **Dev chỉ cần đọc §1 (checklist) rồi làm theo §2–§6, và §11.** Tick `[x]` khi xong.
> Viết 06/11/2026. API: `FalconBigDataController.LiveOps` / `.Feature` / `.Resource`
> (BigData, hợp đồng `docs/design/LiveOpsEventContract.md` + `docs/design/FeatureContract.md`).
> Câu hỏi và tham số gốc do bên dữ liệu chốt — bản Team Quest của họ (`quest_log.md`,
> `quest_dataset.md`) chỉ để **tham khảo**; bản này là bản cầm tay, chỗ nào khác bản gốc thì ghi rõ
> lý do ngay tại chỗ.
> ✅ **Không chờ gì từ SDK, không còn câu hỏi mở nào chặn dev.**
> 🔴 **Đây là THIẾT KẾ ĐÚNG, viết trọn.** Nó không phải bản vá lên cái gì đang có: §2–§6 là toàn bộ
> những gì event này gửi, và **chỉ** những thứ đó.
> ⚠ Game đã có một bản log tự định nghĩa đang chạy. Dev tự so phần mình đã làm với §2–§6 — nhưng
> **đọc §11 trước khi so**, vì phần phải **BỎ ĐI** không hiện ra khi so hai danh sách.
> ⚠ Hai việc không phải của dev: §8 (server) và §9 (phía đọc số) — **§9 là phần dài nhất của doc
> này**, vì bốn log mà bản gốc định dùng lại đã bị hợp đồng Team thay.

## 0. Năm câu phải nhớ trước khi gõ dòng nào

1. **Mốc là của TEAM, quà là của CÁ NHÂN.** Team vượt mốc 200 thì team "đạt rương 1", nhưng chỉ
   người đã join **trước** lúc đó được nhận. Mọi phức tạp của event này sinh ra từ một chỗ đó, và
   `ScoreAtUserJoin` là cái quyết định.
2. **Round = MỘT LẦN ở MỘT team**, không phải "team nào". Đổi team, hay rời rồi vào lại team cũ,
   là **round mới**. `live_ops_round_id = <team_id>_<mốc vào team hiện tại>`.
   🔴 Mốc đó **lấy từ profile server** — đúng con số mà bản tin Team gửi ở `feature_joined_ts`.
   **Đừng** lấy `DateTime.UtcNow` lúc máy này thấy mình vào team: hai máy sẽ ra hai round khác
   nhau cho cùng một lần ở team (§7 bẫy 1).
3. **Team đi TRỤC TÍNH NĂNG, không đi tham số chung.** `feature = guild`,
   `featureInstanceId = <team_id>`. Bản gốc muốn `team_id` làm tham số chung — bỏ (§7 bẫy 2).
4. **`eligible` KHÔNG phải mẫu số của sheet.** Mẫu số "đủ điều kiện" đọc từ bản tin **`state`** của
   tính năng Team. `eligible` gửi để phễu live-op so được với event khác, thế thôi.
5. **Đúng HAI bề mặt impression, không ba** — và `team_quest_join` có việc thật, không phải cho
   đẹp: nó là **đường duy nhất tin được** cho bậc phễu *"đã thấy popup Join"*. Bước `open` không
   làm được việc đó, vì nó là **một dòng mỗi đợt** và chỉ mang popup **đầu tiên** (§3.2). Còn
   impression là **cụm có `count`**, nên ai thấy popup Join dù chỉ một lần đều có dòng.
   🔴 Đọc chúng bằng `sum(count)`, **không** `count(*)`. Và không bề mặt thứ ba — không câu hỏi nào
   cần.

---

## 1. Checklist

**Khung đợt + hàm chung** (§2)
- [ ] `DeclareCycle` mỗi lần data event về
- [ ] Hàm `RoundId()` — 🔴 mốc thời gian từ **server**, không phải giờ máy
- [ ] Hàm `Step()` và `Row()` dán một lần, **cả hai** đóng dấu trục tính năng

**Sáu bước live-op** (§3)
- [ ] `eligible` — mỗi lần data về, đủ 4 điều kiện. **Không** round
- [ ] `open` — bốn loại popup, mang `team_quest_popup`. **Không** round
- [ ] `join` — mỗi round một dòng, mang `team_quest_team_score_at_join` + sĩ số
- [ ] `milestone` — mỗi rương một dòng, **chỉ khi mốc > `ScoreAtUserJoin`**. **Không** key riêng
- [ ] `claim` — mỗi rương một dòng, round **MỞ KHOÁ** + mã **đợt được nhận**
- [ ] `complete` — popup kết quả, mang trạng thái **ba** rương

**Hai dòng tài nguyên** (§4)
- [ ] Treasure Map — mỗi lần game cộng map thật, 🔴 **không** ghi từ đồng bộ
- [ ] Quà rương n — có `live_ops_stage = n`

**Hai bề mặt impression**
- [ ] `team_quest_main` và `team_quest_join`, **không bề mặt thứ ba** (§0 câu 5)

**Soát lại** (§5, §6, §11)
- [ ] Đối chiếu đúng bảng key ở §5 — không thiếu, và **không thừa**
- [ ] 🔴 §11: **bỏ** mọi thứ bản log hiện tại gửi mà bảng §5 không có — theo từng bước

---

## 2. Khung đợt + hàm chung

```csharp
private const string EVENT = "team_quest";

/// Gọi MỖI LẦN data event HOẶC data team về.
public static void OnDataUpdated(TeamQuestData data)
{
    if (data == null) return;

    var cycleId = CycleId(data);
    if (string.IsNullOrEmpty(cycleId)) return;      // chưa biết mã đợt thì KHOAN gửi gì cả

    FalconBigDataController.LiveOps.DeclareCycle(
        EVENT, cycleId,
        cycleIndex:   data.cycleIndex,              // không có thì bỏ
        cycleStartTs: data.cycleStartMs,            // epoch MILLIS UTC — KHÔNG hardcode 84h
        cycleEndTs:   data.cycleEndMs);

    TryStepEligible(data);                          // §3.1
    SyncMissingSteps(data);                         // §3.7
}

/// 🔴 ID CỦA MỘT LẦN Ở MỘT TEAM. Mốc thời gian LẤY TỪ SERVER.
/// Trả null = KHÔNG được gửi bước mang round.
private static string RoundId(TeamSnapshot team)
{
    if (team == null || string.IsNullOrEmpty(team.Id)) return null;
    if (team.MyJoinedTsMs <= 0)                     // ← con số của PROFILE SERVER
    {
        Debug.LogError("[TeamQuest] Không có mốc vào team từ server — bước này KHÔNG gửi.");
        return null;
    }
    return $"{team.Id}_{team.MyJoinedTsMs}";
}

/// Mọi bước live-op đi qua đây — đóng dấu CẢ HAI TRỤC.
private static bool Step(string action, string teamId, int? stage = null,
                         string roundId = null, string cycleId = null,
                         Dictionary<string, object> extra = null)
{
    var param = new LiveOpsParam
    {
        liveOpsEvent   = EVENT,
        action         = action,
        liveOpsStage   = stage,
        liveOpsRoundId = roundId,     // eligible / open: KHÔNG truyền
        liveOpsCycleId = cycleId,     // CHỈ dòng nhận MUỘN — §7 bẫy 5
        extraMeta      = extra
    };

    // TRỤC TÍNH NĂNG — §0 câu 3.
    // 🔴 CHỈ đóng khi BIẾT team, và đóng team CỦA DÒNG NÀY (không phải team đang ở) — §7 bẫy 2.
    if (!string.IsNullOrEmpty(teamId))
        FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, teamId);

    return FalconBigDataController.LiveOps.OnStep(param);
}
```

⚠ `OnStep` trả `false` khi van lọc chặn (bước đã log trong đợt/round này) — **bình thường, đừng xử
lý gì**.

🔴 **`teamId` là tham số BẮT BUỘC, và nó là team CỦA DÒNG, không phải team đang ở.** Nhận rương mở
khoá ở team A trong lúc đã sang team B thì dòng `claim` phải mang **A**. Đóng `Team.Current` vào đó
là đặt hai cột nói ngược nhau lên một dòng — đúng cái lỗi đã bắt được ở Card Collection
(`detail.phase = 3` mà `live_ops_round_id = phase_2`).

🔴 **Chưa biết team thì BỎ KEY, đừng gửi chuỗi rỗng.** Hợp đồng phía kho ghi rõ cho tính năng
Team: *"mọi bản tin: `feature_instance_id` = id team (**bỏ khi chưa có team**; riêng `state` gửi
`""`)"*. Chuỗi rỗng là **một lời khai có tác dụng thật** — profile hiểu là *"đã mở khoá, không ở
thực thể nào"* và **xoá** thực thể. Dòng của Quest không có quyền khai chuyện đó; chỉ bản tin
`state` của Team có.

🔴 **Key riêng của BƯỚC đi `extraMeta`; key riêng của DÒNG TÀI NGUYÊN đi `detail`.** Bản gốc §1 của
dataset ghi *"Game truyền vào `param`"* — `param` là field của **custom event**, và nó ra wire
thành **một chuỗi JSON** mà kho **không đọc**. Gửi vào đó là **mất trắng key**.

ℹ️ Hai field đó về kho hai đường khác nhau nhưng tra cứu y như nhau: `extraMeta` trải phẳng ngay ở
client, `detail` đi nguyên khối rồi kho mở ra. Cuối cùng cả hai nằm ở `event_extra_props`.
⚠ `ResourceParam` **không có** field `extraMeta` — đừng tìm.

---

## 3. Sáu bước live-op — code dán vào

### 3.1 `eligible` — phễu live-op, KHÔNG phải mẫu số của sheet

```csharp
// Gọi MỖI LẦN data event hoặc data team về, khi đủ CẢ BỐN:
//   · đợt đang chạy theo giờ server
//   · chưa hết cửa sổ join (isJoinWindowEnded = false)
//   · đã qua level mở event
//   · ĐANG Ở TEAM
private static void TryStepEligible(TeamQuestData data)
{
    if (data.serverNowMs < data.cycleStartMs || data.serverNowMs >= data.cycleEndMs) return;
    if (data.isJoinWindowEnded) return;
    if (data.maxPassedLevel < data.levelUnlock) return;
    if (string.IsNullOrEmpty(Team.Current?.Id)) return;

    Step(FLiveOpsAction.Eligible, Team.Current.Id); // KHÔNG round
}
```

⚠ **Gọi mỗi lần, đừng tự nhớ "đã gọi rồi".** SDK nén còn một dòng mỗi đợt. Tự nhớ thì từ đợt thứ
hai mẫu số rỗng — đúng bug Master League G1.

🟢 **Và bản gốc làm đúng một chuyện quan trọng:** nó **không** dùng `eligible` làm mẫu số "đủ điều
kiện theo ngày", mà đọc từ log của tính năng Team. Đúng — `eligible` bị nén còn **một dòng mỗi
đợt** nên không ra số theo ngày được. Ba doc event khác đều từng mắc chỗ này.

⚠ Nghĩa của bước này là *"**còn join được**"* — hẹp hơn "đủ điều kiện" mà GD dùng. Đừng lẫn.

### 3.2 `open` — bốn loại popup

```csharp
// OnEnable của từng popup.
private static void StepOpen(string popup, TeamQuestData data)
{
    var extra = new Dictionary<string, object> { ["team_quest_popup"] = popup };

    // Hai ảnh chụp lúc mở popup.
    // 🔴 Chưa có team thì BỎ cả hai, đừng gửi 0: 0 là một giá trị TRÔNG HỢP LỆ nên không ai
    //   biết nó rỗng.
    if (!string.IsNullOrEmpty(Team.Current?.Id))
    {
        extra["team_quest_team_score"] = data.teamScore;   // điểm team LÚC MỞ popup
        extra["team_quest_my_maps"]    = data.myMaps;      // map của mình LÚC MỞ popup
    }

    Step(FLiveOpsAction.Open, Team.Current?.Id, extra: extra);  // null ⇒ BỎ trục tính năng
}

// Popup Join khi CÓ team     → StepOpen("join_team",    data);
// Popup Join khi KHÔNG team  → StepOpen("join_no_team", data);
// Popup chính                → StepOpen("main",         data);
// Popup kết quả              → StepOpen("result",       data);
```

🔴 **`open` là một dòng mỗi đợt**, và `team_quest_popup` đi theo **lần đầu**. Nên dòng này trả lời
*"trong đợt này người đó có thấy popup nào ĐẦU TIÊN"*, **không** phải "đã thấy những popup nào".

➡ Hệ quả thật: bậc *"thấy popup Join"* của Q2/Q8 **không** đọc được bằng
`team_quest_popup = join_team` một cách tin cậy — ai mở popup chính trước thì dòng duy nhất của họ
mang `main`.

🟢 **Nên bậc đó đọc bằng IMPRESSION của bề mặt `team_quest_join`** (§0 câu 5): cụm impression không
bị nén còn-một-dòng-đầu-tiên như bước, nên nó trả lời đúng *"có thấy popup Join trong đợt này
không"*. Vế *"hoặc đã có `join`"* mà bản gốc chừa ra vẫn giữ, làm đường đối soát (§9).

⚠ **Không** mang round, và người chưa có team thì **cũng không** mang trục tính năng — bước này
phải gửi được cho cả họ. Câu *"% user unlock mà không có team"* đọc từ bản tin **`state`** của Team,
không đọc từ dòng này (§9).

### 3.3 `join` — mỗi round một dòng

```csharp
// LẦN ĐẦU client thấy TỪ DATA SERVER rằng user đã join event TRONG ROUND NÀY.
// Mọi đường đều qua đây: bấm popup, join lúc offline gửi bù, server tự join,
// hoặc đã join trước khi lên bản app có log.
private static void StepJoin(TeamSnapshot team, TeamQuestData data, string via)
{
    var roundId = RoundId(team);
    if (string.IsNullOrEmpty(roundId)) return;      // §0 câu 2

    Step(FLiveOpsAction.Join, team.Id, roundId: roundId, extra: new Dictionary<string, object>
    {
        ["team_quest_team_score_at_join"] = data.scoreAtUserJoin,  // 🔴 NGUYÊN giá trị server trả
        ["member_count"]                  = team.MemberCount,      // tên của log Team, không đổi
        ["team_quest_join_via"]           = via                     // null ⇒ SDK bỏ key
    });
}
```

| `team_quest_join_via` | Nghĩa |
|---|---|
| `continue` | bấm **Join** ở popup |
| `close` | bấm **X** mà vẫn join |
| *(bỏ key)* | join không đi từ popup — đừng đoán |

🔴 **`team_quest_team_score_at_join` là field chịu lực nhất của event.** Toàn bộ luật "rương nào
mất vì join muộn / đổi team" đứng trên nó. Lấy **nguyên** giá trị server trả (`ScoreAtUserJoin`),
**không** tự tính từ cache điểm team.

🔴 **Key RIÊNG, không dùng lại `team_quest_team_score`** — dù trên đúng bước này hai con số bằng
nhau. Chúng là hai **đại lượng** khác nhau, và chỗ chúng khác nhau là chỗ quan trọng nhất:

| | `team_quest_team_score` | `team_quest_team_score_at_join` |
|---|---|---|
| Nghĩa | điểm team **lúc gửi bản tin** | điểm team **lúc user vào team** |
| Trên bước `join` thật | trùng nhau — cùng một thời điểm | trùng nhau |
| Trên **dòng ghi bù** (§3.7) | điểm team **hiện tại** ⇒ **SAI** | vẫn đúng |

➡ Dùng chung một tên thì một dòng ghi bù điền đúng và một dòng ghi bù điền sai **trông y như nhau**
— không ai soát được. Cái tên `..._at_join` **tự nó là lời khai** về nghĩa.

ℹ️ `team_quest_team_score` vẫn sống, đúng nghĩa *"ảnh chụp lúc gửi"*, ở `open` (§3.2) và ở dòng map
(§4.1). Hai cái không đụng nhau.

🔴 **Key sĩ số tên là `member_count`, KHÔNG phải `team_member_count`.** Bản gốc chọn
`team_member_count` với lý do *"giữ đúng tên đang dùng ở log Team"* — mà log Team gửi
`member_count`. Lý do đúng, tên sai. Dùng `member_count` thì union hai nguồn là xong; dùng tên
khác là tự tạo hai tên cho một nghĩa.

### 3.4 `milestone` — và một điều kiện làm HAI việc

```csharp
// Điểm team của round đang chơi ≥ mốc rương n, VÀ user đã join round này,
// VÀ mốc n > ScoreAtUserJoin. Sau trận thắng hoặc sau khi tải data. Mỗi rương 1 dòng.
private static void StepMilestones(TeamSnapshot team, TeamQuestData data)
{
    var roundId = RoundId(team);
    if (string.IsNullOrEmpty(roundId) || !data.joined) return;

    foreach (var chest in data.chests)                      // sort theo chestId tăng dần
    {
        if (data.teamScore < chest.threshold) continue;
        if (chest.threshold <= data.scoreAtUserJoin) continue;   // 🔴 ĐIỀU KIỆN VÀNG

        Step(FLiveOpsAction.Milestone, team.Id, stage: chest.chestId, roundId: roundId);
    }
}
```

🔴 **Bước này là MỐC CỦA TEAM, không phải máy đếm.** Nó chỉ nói *"team đã vượt mốc rương n"* —
không chở số map, không chở độ khó, không chở điểm vừa góp. Thứ **đếm được** đi dòng tài nguyên
(§4.1). Một `sub_event` chở hai nghĩa là chỗ phía đọc không gỡ lại được (§11).

🟢 **Dòng `chest.threshold <= scoreAtUserJoin` là chỗ thiết kế tốt nhất trong cả bốn doc event.**
Nó làm **hai** việc cùng lúc:

| Việc | Vì sao nó đúng |
|---|---|
| ① Mã hoá **luật game** | người join sau mốc không được rương đó ⇒ không có dòng, khớp đúng dấu X của UI |
| ② Chặn **ghi lặp** ở ca khó nhất | rời team A rồi **vào lại team A** là round mới ⇒ van lọc cho phép ghi lại. Nhưng lúc đó `scoreAtUserJoin` đã là điểm hiện tại (ví dụ 500), nên mốc 200 và 400 **tự bị loại** |

➡ Một điều kiện nghiệp vụ **tình cờ là đúng** điều kiện chống trùng cần thiết. **Đừng ai "dọn cho
gọn" nó.** Bỏ nó đi là vừa sai luật game vừa đẻ ra rương trùng, cùng lúc.

⚠ **Không có key riêng.** Rương ở `live_ops_stage`, lần-ở-team ở `live_ops_round_id`. Hết.

⚠ Loop từ rương 1 lên và so bằng `teamScore >= threshold`, **không** so "ngưỡng > điểm cũ" — để
không sót rương khi điểm nhảy qua hai mốc trong một lần tải data.

### 3.5 `claim` — round MỞ KHOÁ, không phải round đang ở

```csharp
// Nhận rương n thành công: bấm CLAIM trong đợt, hoặc nhận gộp ở popup cuối đợt.
// MỖI RƯƠNG MỘT DÒNG, kể cả khi nhận gộp.
public static void OnChestClaimed(int chestId, ClaimSnapshot snap)
{
    Step(FLiveOpsAction.Claim,
         teamId:  snap.unlockTeamId,       // 🔴 team MỞ KHOÁ, không phải team đang ở
         stage:   chestId,
         roundId: snap.unlockRoundId,      // round MỞ KHOÁ rương — từ server; không biết thì null
         cycleId: snap.cycleId,            // đợt ĐƯỢC NHẬN
         extra:   new Dictionary<string, object>
         {
             // Key duy nhất ở đây: "nhận trong đợt" hay "nhận gộp ở popup cuối đợt".
             ["team_quest_grant_source"] = snap.grantSource
         });

    GrantChestRewards(chestId, snap);      // §4.2
}
```

🔴 **`roundId` ở đây là round đã MỞ KHOÁ rương, không phải round user đang ở.** Mở khoá ở team A,
nhận lúc đã sang team B thì dòng này mang round của **A**. Server không trả thì để `null` — thiếu
hơn bịa.

🔴 **`snap.cycleId` là mã đợt ĐƯỢC NHẬN**, không phải đợt đang chạy. §7 bẫy 5.

🔴 **Và `snap.unlockTeamId` phải khớp với `snap.unlockRoundId`.** Bước `claim` và các dòng quà của
chính nó (§4.2) phải khai **cùng một** team — lệch nhau là một dòng nói team A, dòng kế bên nói
team B, cho cùng một rương.

⚠ Nhận gộp 3 rương = **3** dòng `claim` + quà của **cả 3**. Đừng gộp thành một dòng không stage.

ℹ️ Round trên `claim` **không cần cho việc chống trùng** — `(đợt, claim, n)` đã đủ duy nhất cho một
user. Nó ở đây **chỉ** để trả lời *"rương này mở khoá ở team nào"*.

🔴 **`team_quest_grant_source` ở trên BƯỚC này, không phải trên dòng quà (§4.2).** Dòng quà nối về
bước `claim` bằng `(đợt, stage, round)` — chép key sang là tạo bản sao để sau này lệch.

### 3.6 `complete` — ba trạng thái rương

```csharp
// Popup kết quả cuối đợt hiện LẦN ĐẦU, SAU KHI đã có data kết quả từ server.
// Trạng thái đọc NGAY TRƯỚC khi bấm Claim.
public static void OnResultPopupShown(ResultSnapshot snap)
{
    Step(FLiveOpsAction.Complete,
         teamId:  snap.teamId,             // team của KẾT QUẢ đang hiện
         roundId: snap.roundId,            // round có kết quả đang hiện
         cycleId: snap.cycleId,            // đợt ĐƯỢC NHẬN
         extra: new Dictionary<string, object>
         {
             ["team_quest_chest_1_state"] = snap.ChestState(1),
             ["team_quest_chest_2_state"] = snap.ChestState(2),
             ["team_quest_chest_3_state"] = snap.ChestState(3)
         });
}
```

| Giá trị | Nghĩa |
|---|---|
| `claimed` | đã mở ở team này |
| `claimed_other_team` | đã mở ở team khác |
| `claimable` | sẽ nhận ở lần Claim này |
| `not_enough_progress` | team chưa đạt mốc |
| `not_claimable` | team đạt mà mình 0 map, hoặc hết giờ |
| `not_contributed` | team đạt mốc **trước** khi mình join |

🔴 **Đọc trạng thái TRƯỚC khi bấm Claim**, không phải sau — sau thì mọi ô thành `claimed` và dòng
này mất hết giá trị.

⚠ **Ba key này là con đường NGẮN NHẤT tới Q13–Q15** (lý do mất rương): đọc thẳng, khỏi phải suy từ
`join` + `milestone` + `claim`. Nhưng chỉ có với người **quay lại xem kết quả**. Nhóm không quay
lại thì vẫn phải suy — nên **cả hai đường đều cần**, và dòng này dùng để **đối soát** cách suy.

⚠ Giá trị String viết `snake_case` chữ thường, **không** truyền enum của code.

### 3.7 Hàm ghi bù — gọi mỗi lần data về

```csharp
// So data server với SỔ GHI LOCAL của game, chỉ ghi phần CÒN THIẾU, theo thứ tự join → milestone.
private static void SyncMissingSteps(TeamQuestData data)
{
    var team = Team.Current;
    if (team == null || !data.joined) return;

    var roundId = RoundId(team);
    if (string.IsNullOrEmpty(roundId)) return;

    if (!Ledger.HasRound(roundId))
    {
        StepJoin(team, data, via: null);          // KHÔNG gửi join_via ở dòng ghi bù
        Ledger.MarkRound(roundId);
    }

    StepMilestones(team, data);                   // §3.4 — điều kiện vàng đã lo chống trùng
}
```

⚠ Dòng ghi bù **không gửi** `team_quest_join_via` — máy này không chứng kiến người chơi bấm gì, mà
**bịa thì tệ hơn thiếu**.

ℹ️ `milestone` **không cần sổ riêng**: van lọc khoá `(event, đợt, round, milestone, stage)` và điều
kiện vàng ở §3.4 đã chặn đủ. Chỉ `join` cần sổ, vì nó không có điều kiện tương tự.

---

## 4. Hai dòng tài nguyên

```csharp
private static ResourceParam Row(string where, string currency, string itemId, long amount)
    => new()
    {
        exchangeId    = Guid.NewGuid().ToString("N"),
        resourceWhen  = FResourceWhen.EventPrefix + EVENT,   // "event_team_quest"
        resourceWhere = where,
        itemType      = EVENT,                                // "team_quest"
        currency      = currency,
        itemId        = itemId,
        amount        = amount
    };
```

🔴 **Phải điền CẢ NĂM**: `resourceWhen` · `resourceWhere` · `itemType` · `currency` · `itemId`.
Bản gốc đánh `item_type`/`item_id` là **"Mở rộng"** — **sai**: năm field này mặc định là chuỗi
`"Unknown"`, không phải null. Bỏ trống là gửi `"Unknown"` lên kho, một giá trị **trông hợp lệ** nên
không ai biết nó thiếu.

### 4.1 Treasure Map — map là MỘT LẦN CỘNG, không phải một con số trạng thái

```csharp
// MỖI KHI game THẬT SỰ cộng map sau một trận thắng, theo đúng luật game đang chạy.
// Game cộng ở trường hợp nào thì ghi ở trường hợp đó, KỂ CẢ lúc offline hay chế độ chơi khác.
// 🔴 KHÔNG ghi từ fetch / đồng bộ máy khác. delta ≤ 0 thì KHÔNG ra dòng.
public static void OnMapsGained(TeamSnapshot team, int gained, int before, int after,
                                string difficulty, int teamScoreAfter)
{
    var row = Row("level_end", "team_quest_map", "team_quest_map", gained);
    row.valueBefore = before;        // map CỦA MÌNH trong ROUND NÀY, trước trận
    row.valueAfter  = after;         // sau trận
    row.detail      = new Dictionary<string, object>
    {
        // 🔴 `difficulty` là CHUỖI `snake_case` chữ thường, dùng ĐÚNG tên độ khó của game
        //   (`normal` / `hard` / `super_hard`), cùng vốn từ với log level.
        //   `team_quest_team_score` ở đây là điểm team SAU trận: một ảnh chụp, không phải mốc.
        ["team_quest_difficulty"] = difficulty,
        ["team_quest_team_score"] = teamScoreAfter
    };

    FalconBigDataController.LiveOps.Stamp(row, EVENT, roundId: RoundId(team));
    FalconBigDataController.Feature.Stamp(row, FFeatureName.Guild, team.Id);  // team ĐƯỢC CỘNG map
    FalconBigDataController.Resource.OnEarned(row);
}
```

🔴 **Map đi bằng DÒNG TÀI NGUYÊN, không bằng bước live-op.** Nó là thứ **đếm được**: có lên, có
mốc trước, có mốc sau, có lý do. Nhét nó vào một bước live-op thì **van lọc nén** — van khoá
`(event, đợt, round, bước, stage)`, nên trong một round, một bước chỉ sống **một dòng** cho mỗi
stage. Một máy đếm mà mỗi round chỉ đếm được một lần thì không phải máy đếm.

➡ Hợp đồng nói đúng một câu: **bước live-op là MỐC, dòng tài nguyên là MÁY ĐẾM.** Van nén bước tồn
tại chính là để chặn việc dùng bước làm máy đếm.

🔴 **Không gửi `team_quest_maps` / `team_quest_my_maps` kèm theo.** Dòng này đã có `amount` = map
vừa cộng và `value_after` = map của mình sau trận. Gửi thêm hai key cùng số là hai nguồn sự thật
trên **cùng một dòng**.

🔴 **`team_quest_difficulty` là CHUỖI đọc được** — và đây không phải ý riêng của doc này: bản gốc
bên dữ liệu khai nó **String** ngay từ đầu (field E5). Một con số thì không tự nói nó nghĩa gì: đọc
`0` cần bảng tra *"0 là độ khó nào"*, mà bảng đó **không tồn tại ở đâu cả** — nó nằm trong một `enum`
trong code game. Đúng luật doc này đặt cho ba key trạng thái rương ở §3.6.

🔴 **Dùng ĐÚNG tên độ khó của game**, `snake_case` chữ thường — `normal` / `hard` / `super_hard`
theo đúng vốn từ câu hỏi Q16 (*"% level Hard / Super Hard"*). Và dùng **cùng vốn từ với log level**
`f_sdk_level_pass`: Q16 đọc độ khó từ log đó, nên hai nguồn phải nói cùng một thứ tiếng mới đối
chiếu được.

🔴 **`featureInstanceId` = team ĐƯỢC CỘNG map.** Bản gốc đặt việc này lên tham số chung `team_id` —
bỏ, dùng cột thật (§7 bẫy 2).

🔴 **Không ghi từ đồng bộ / fetch.** Map là một **lần cộng thật**, không phải một con số trạng thái
— ghi từ sync là ghi đôi cho một lần cộng. Hệ quả phải biết thì ở §9 (Σ `amount` là số **của một
máy**).

⚠ `amount` = map **thật sự được cộng** (1 / 3 / 5, có thể **ít hơn** khi team chạm 800). Không
phải map lý thuyết của độ khó.

⚠ **Gói thẻ**: chỉ dòng **gói** mang nhãn Team Quest; dòng thẻ lúc mở gói giữ nguyên của Card
Collection.

### 4.2 Quà rương n

```csharp
private static void GrantChestRewards(int chestId, ClaimSnapshot snap)
{
    foreach (var reward in snap.RewardsOf(chestId))          // TỪNG MÓN một dòng
    {
        var row = Row("progression_reward", reward.currency, $"team_quest_chest_{chestId}",
                      reward.amount);
        row.liveOpsCycleId = snap.cycleId;                   // đợt ĐƯỢC NHẬN — gán TRƯỚC Stamp
        // KHÔNG key riêng: `team_quest_grant_source` nằm trên BƯỚC `claim` (§3.5), đúng chỗ bản
        // thử đang gửi. Dòng quà nối về bước đó bằng (đợt, stage, round).

        FalconBigDataController.LiveOps.Stamp(row, EVENT, stage: chestId,
                                              roundId: snap.unlockRoundId);
        if (!string.IsNullOrEmpty(snap.unlockTeamId))      // không biết ⇒ BỎ KEY, đừng gửi ""
            FalconBigDataController.Feature.Stamp(row, FFeatureName.Guild, snap.unlockTeamId);
        FalconBigDataController.Resource.OnEarned(row);
    }
}
```

### Và một luật chung cho cả hai

🔴 **Treasure Map KHÔNG phải phần thưởng.** Phía đọc số **phải loại** `currency = team_quest_map`
khỏi mọi chart Source.

ℹ️ Luật này **dùng chung** với `ladder_streak_count` (Ladder Streak) và `team_arena_flag` (Team
Arena) — cùng hạng "tiền tệ giả", cùng một luật loại trừ. Đừng viết ba ghi chú rời.

---

## 5. Bảng key — TOÀN BỘ những gì event này gửi

Mục này là bảng đối chiếu. Thiếu một dòng ở đây là thiếu dữ liệu; **thừa** một dòng cũng là lỗi, và
lỗi thừa khó thấy hơn lỗi thiếu (§11).

### 5.1 Các cột TRỤC — không phải key riêng, nên đừng gửi bằng key riêng

| Bước | `live_ops_stage` | `live_ops_round_id` | `live_ops_cycle_id` | trục tính năng |
|---|---|---|---|---|
| `eligible` | — | — | đợt đang chạy | team đang ở |
| `open` | — | — | đợt đang chạy | team đang ở — **bỏ** khi chưa có team |
| `join` | — | ✅ round vừa vào | đợt đang chạy | team vừa vào |
| `milestone` | ✅ `chestId` | ✅ round đang ở | đợt đang chạy | team đang ở |
| `claim` | ✅ `chestId` | ✅ round **MỞ KHOÁ** | 🔴 đợt **ĐƯỢC NHẬN** | team **mở khoá** |
| `complete` | — | ✅ round của kết quả | đợt được nhận | team của kết quả |

🔴 Mã team đi `feature_instance_id`. Lần-ở-team đi `live_ops_round_id`. Rương đi `live_ops_stage`.
**Không key riêng nào mang ba thứ đó** — §6 và §7 bẫy 2.

### 5.2 Key riêng trên bước live-op

| Bước | Key | Kiểu | Nghĩa |
|---|---|---|---|
| `eligible` | *(không key nào)* | | |
| `open` | `team_quest_popup` | String | `main` · `join_team` · `join_no_team` · `result` |
| `open` | `team_quest_team_score` | Int | điểm team lúc mở popup **lần đầu trong đợt** |
| `open` | `team_quest_my_maps` | Int | map của mình lúc đó |
| `join` | `team_quest_team_score_at_join` | Int | 🔴 `ScoreAtUserJoin` **của server** — §3.3 |
| `join` | `member_count` | Int | sĩ số lúc join. Tên của log Team, **không** đổi |
| `join` | `team_quest_join_via` | String | `continue` · `close` · **bỏ key** nếu không đi từ popup |
| `milestone` | *(không key nào)* | | rương ở `live_ops_stage`, hết |
| `claim` | `team_quest_grant_source` | String | nhận trong đợt / nhận gộp cuối đợt |
| `complete` | `team_quest_chest_1_state` … `_3_state` | String | sáu giá trị ở §3.6 |

⚠ **Hai ảnh chụp trên `open` yếu, biết trước để đừng dựng câu hỏi lên chúng.** `open` là **một dòng
mỗi đợt** (§7 bẫy 6), nên hai số đó là trạng thái lúc **lần đầu** mở popup — với phần lớn user là
gần 0. Chúng dùng để **đối soát**, không dùng làm số chính.

### 5.3 Hai dòng tài nguyên

| Dòng | `currency` | `item_id` | `amount` | `detail` |
|---|---|---|---|---|
| Treasure Map | `team_quest_map` | `team_quest_map` | map **thật sự** được cộng | `team_quest_difficulty` (**String**) · `team_quest_team_score` (Int, sau trận) |
| Quà rương n | của chính món quà | `team_quest_chest_{n}` | số lượng món | *(không key riêng)* |

🔴 Dòng map có `value_before` / `value_after` = map **của mình trong round này**. Nên **không** gửi
thêm key nào mang lại hai con số đó (§4.1).

### 5.4 Impression

Đúng **hai** bề mặt, qua API cụm của SDK: `team_quest_main` và `team_quest_join`. Không bề mặt thứ
ba (§0 câu 5).

---

## 6. Những thứ ĐỪNG gửi nữa

| Đừng gửi | Vì |
|---|---|
| `team_id` / `team_role` làm **tham số chung** | §7 bẫy 2. Team đi `featureInstanceId`, vai đi `featureVariant` của bản tin Team |
| `team_quest_team_code` | 🔴 Không phải mã team. Mã team đi `feature_instance_id` (§5.1) — và nếu đang gửi thì **bỏ**, §11 |
| **Số enum của code** trong bất kỳ key nào | Gửi chuỗi `snake_case` đọc được. Luật này ở §3.6, và áp cho `team_quest_difficulty` ở §4.1 |
| Hai key cho **cùng một đại lượng** | Hai nguồn sự thật, và sẽ có ngày lệch. ⚠ `team_quest_team_score` và `..._at_join` **không** phải trường hợp đó — hai đại lượng khác nhau (§3.3) |
| Round trên `eligible` / `open` | Hai bước này phải gửi được cho cả người **chưa có team** |
| Key riêng trên `milestone` | Rương ở `live_ops_stage`, round ở `live_ops_round_id`. Hết — §3.4 |
| `team_quest_join_via` trên dòng ghi bù | Máy này không chứng kiến. Bịa tệ hơn thiếu |
| Số map / map của mình làm **key riêng** trên dòng tài nguyên | Dòng đó đã có `amount` và `value_after`. Hai nguồn trên **cùng một dòng** (§4.1) |
| Thứ **đếm được** gửi bằng **bước live-op** | Van lọc nén bước ⇒ mỗi round chỉ còn một dòng. Bước là **MỐC**, dòng tài nguyên là **MÁY ĐẾM** (§4.1) |
| Cờ suy ra được: Contributor, vai trò top 1 / top 2–3, điểm team theo giờ, "mất rương vì join muộn" | Kho tự suy. Ngoại lệ duy nhất là `milestone` — hợp đồng live-op yêu cầu |
| Lịch đợt bằng key riêng | `DeclareCycle` đã đóng `cycle_start_ts` / `cycle_end_ts`. **Và đừng hardcode 84h** |
| Dòng khi team đã chạm 800 | Game không cộng map nên không có dòng. **Vắng là đúng** |
| Bề mặt impression thứ ba | §0 câu 5 — đúng hai bề mặt, không câu hỏi nào cần thêm |
| **Stamp** dòng level / ads / IAP / log của tính năng Team | Không thuộc hẳn Team Quest. Độ khó (Q16) lọc theo **cửa sổ đợt**, không cần đóng dấu |
| Cuộn map bay, huy hiệu, anim thanh, popup Info, tooltip, dải thông báo | Không câu hỏi nào cần |

---

## 7. Bảy cái bẫy, xếp theo mức đau

### Bẫy 1 — 🔴 mốc thời gian trong `round_id` phải là của SERVER

`round_id = <team_id>_<mốc vào team>`. Nếu mốc đó là `DateTime.UtcNow` lúc **máy này** thấy mình
vào team thì hai máy ra **hai** round khác nhau cho **cùng một lần ở team**:

| Hỏng gì | Hậu quả |
|---|---|
| `join` | hai dòng ⇒ Q1, Q2, Q8 phồng |
| `milestone` | hai dòng mỗi rương |
| **Q15** | đếm một lần **"đổi team" KHÔNG TỒN TẠI** mỗi lần người chơi mở máy khác |
| Luật xét rương mất | hai `scoreAtUserJoin` cho một lần join ⇒ luật **mâu thuẫn với chính nó** |

✅ **Lấy đúng con số của profile server** — cùng giá trị mà bản tin Team gửi ở `feature_joined_ts`.
Server giữ profile theo `account_id` nên hai máy đọc ra cùng số.

➡ Giữ thành mục nghiệm thu: **đăng nhập máy thứ 2 giữa đợt ⇒ `live_ops_round_id` GIỐNG HỆT máy 1.**

ℹ️ Khi round id **giống nhau**, dòng trùng xuyên máy trở thành **nhiễu vô hại** (mọi phép đọc là
EXISTS hoặc `min()`). Khi nó **khác nhau**, cùng những dòng đó thành **chất độc**. Một con số quyết
cả hai kịch bản.

### Bẫy 2 — 🔴 `team_id` không phải tham số chung

Bản gốc ghi `team_id` / `team_role` là *"tham số chung **có sẵn** trên mọi log"*, mức **Bắt buộc**,
dev *"không làm gì"*. **Không đúng** — không có chỗ nào trong SDK hay code game đặt hai cột đó.

Và cách chữa **không phải** `Common.Set("team_id", …)`:

| | `Common.Set` | **Trục tính năng** |
|---|---|---|
| Có trên dòng level / ads / IAP | 🔴 **có** | ✅ chỉ dòng mình đóng dấu |
| Nghĩa | *"lúc gửi dòng này tôi đang ở team X"* | *"dòng này DO team X gây ra"* |
| Nối được với log Team | ❌ | ✅ cùng `feature_instance_id` |

Mà luật §1.1-9 của **chính bản gốc** viết: cột ngữ cảnh nghĩa là *"**thuộc về**"*, không phải
*"**đang diễn ra lúc**"*. `Common.Set` làm đúng vế sau.

➡ `feature = guild` + `featureInstanceId = <team_id>`, đóng ở `Step()` và `Row()` (§2, §4). Vai
trò thì đọc từ `featureVariant` của bản tin Team, không gửi lại.

### Bẫy 3 — mã đợt rỗng: mất sạch từ đợt thứ hai

Van lọc khoá theo `(event, đợt, round, action, stage)`. Mã đợt `''` ⇒ đợt 1 và đợt 2 ra **cùng
khoá** ⇒ từ đợt 2 mọi bước **bị nuốt**, im lặng. **Chưa biết mã đợt thì KHOAN gửi** (§2 đã guard).

### Bẫy 4 — năm field resource mặc định `"Unknown"`

§4. Dùng helper `Row(...)`.

### Bẫy 5 — dòng nhận muộn phải khai **đợt được nhận**

Đợt N hết, đợt N+1 mở, user mới mở app và nhận rương còn sót. `claim`, `complete` và dòng quà rương
**phải** khai mã đợt N. Không khai thì SDK điền **đợt đang chạy**.

🔴 Trên **dòng tài nguyên** phải gán `liveOpsCycleId` **TRƯỚC** khi gọi `Stamp` — `Stamp` dùng
`??=`, nên gán sau là **vô tác dụng**, im lặng.

ℹ️ Mã **round** thì SDK **không** tra hộ nữa (sửa 06/11) — không truyền = dòng không có round. Và
**đừng** dùng `LiveOps.CurrentRoundId` làm đường tắt: nó trả **lịch sử log**, nên sai đúng ở hai ca
hay cần nó nhất là **dòng nhận muộn** và **ngay sau khi đổi team**.

### Bẫy 6 — `eligible` / `open` là một dòng MỖI ĐỢT

Đừng treo phân bố, đừng treo **trạng thái** lên chúng. `team_quest_popup` là một ngoại lệ **đã
được cân**: nó chỉ tả *lần mở đầu tiên*, và §9 có luật đọc tương ứng.

🔴 **Luật một câu:** van lọc phá thứ **BIẾN ĐỔI** trong đợt; thứ **BẤT BIẾN** trong đợt thì một
dòng là đủ.

### Bẫy 7 — đừng "dọn gọn" điều kiện vàng ở §3.4

`chest.threshold <= scoreAtUserJoin` nhìn như một dòng dư. Bỏ nó là **vừa sai luật game vừa đẻ ra
rương trùng**, cùng lúc. Xem §3.4.

---

## 8. Việc của server (không phải việc dev client)

### Phải có, không có thì chặn ship

| # | Cần gì | Không có thì |
|---|---|---|
| 1 | **Mốc vào team hiện tại** trong profile (epoch ms), đọc ra cùng giá trị trên mọi máy | Bẫy 1. Không có thì `round_id` không dựng được, và không có tầng dự phòng nào an toàn |
| 2 | `ScoreAtUserJoin` của round hiện tại | Mất **toàn bộ** nhóm câu hỏi "rương mất vì join muộn / đổi team" |
| 3 | Lịch đợt **chung mọi user**: mã đợt, STT, giờ bắt đầu, giờ kết thúc | Mã đợt rỗng = bẫy 3 |
| 4 | Lúc Claim trả **round đã mở khoá** từng rương | `claim` và dòng quà không biết rương mở ở team nào ⇒ mất phép tách "đã nhận ở team cũ" |

⚠ Mã đợt, team id và mốc vào team của server phải **trùng ĐÚNG GIÁ TRỊ** client gửi ở
`live_ops_cycle_id`, `feature_instance_id` và phần sau của `live_ops_round_id`. Lệch một ký tự là
không nối được.

### Một việc nhỏ của KHO

Lấy **danh sách bản app** của bản log Team Quest hiện tại, để rào bằng `app_version` khi đọc —
🔴 rào ở **mọi** câu đọc Team Quest, không chỉ lúc đọc `milestone`, vì dòng cũ không có round và
không có mã team (§11). Không chặn ai, và không cần era.

### Hai bảng cho các số "tại cuối sự kiện"

Client chỉ thấy user online, nên mọi số của **mọi** team và người 0 map không mở app cần server:

| Bảng | Cột | Trả lời |
|---|---|---|
| Kết quả team (1 dòng / team / đợt) | mã đợt, team id, điểm cuối, giờ đạt rương 1/2/3, sĩ số đầu và cuối; **kèm tổng số team đang tồn tại** | Mẫu số đầy đủ cho nhóm câu "theo team"; **số chính thức** của điểm cá nhân (§9) |
| Lần ở team (1 dòng / user / round) | mã đợt, team id, `account_id`, giờ vào, giờ rời, **map**, `ScoreAtUserJoin`, rương đã nhận và nhận ở team nào | Người 0 map không mở app; đối chiếu luật xét rương mất |

---

## 9. Việc phía đọc số (không phải việc dev client)

### 🔴 Bốn log mà bản gốc định "dùng lại" đã bị hợp đồng Team THAY

Bản gốc §2.3 ghi dùng lại `team_active_unlock`, `team_join`, `team_leave`, `team_kick`. **Bốn log
đó không còn** — hợp đồng tính năng thay cả tám log `f_sdk_custom_team_*` bằng **một** log
`f_sdk_feature`. Dev client **không phải làm gì**, nhưng ai viết query phải biết:

| Bản gốc đọc | Giờ đọc ở |
|---|---|
| `team_active_unlock` | bước **`state`** (`feature = guild`, `sub_event = 'state'`), bắn **mỗi login**. "Có / không có team" đọc từ `feature_instance_id` |
| sĩ số lúc login | `member_count` trong extras của **chính bước `state`** đó |
| vai trò trong team | `feature_variant` |
| mốc vào team | `feature_joined_ts` |
| **`team_join`** | 🔴 **`feature_effect = 'enter'`**, KHÔNG phải `sub_event = 'join'` |
| **`team_leave`** | 🔴 **`feature_effect = 'exit'`**, KHÔNG phải `sub_event = 'leave'` |
| `team_kick` | `sub_event = 'kick_member'` (người **kick** gửi — chính họ không rời team) |

🔴 **"Vào team" và "rời team" đọc bằng `feature_effect`, không bằng tên bước.** Lọc theo tên bước
là **mất dòng**, và mất đúng những nhóm câu hỏi cần:

| Lọc `sub_event` | Mất gì |
|---|---|
| `= 'join'` | **`create`** (lập team mới — hợp đồng ghi *"`create` là `enter`"*) và **`join_accepted`** (vào team Private, **server gửi hộ**) |
| `= 'leave'` | **bị kick** và **`guild_disbanded`**, cả hai **server gửi hộ** |

➡ Câu *"ai rời team trong 3 ngày sau sự kiện"* và *"số người vào / rời của team"* mà lọc theo tên
bước thì **không thấy người bị kick** — đúng nhóm người câu hỏi đang nhắm tới.

ℹ️ Hợp đồng phía kho nói thẳng: *"Profile không lưu `sub_event`... Tên bước không vào code: tính
năng / bước mới chỉ cần gửi đúng `feature_effect`."* Đọc theo `feature_effect` thì bước mới của
tương lai tự vào đúng rổ.

⚠ **`feature_effect` hiện ở `event_extra_props`, CHƯA được cấp cột** (hợp đồng kho: *"`feature_effect`,
`feature_variant`, `feature_joined_ts`, `feature_unlocked_ts` **ở lại `event_extra_props`**"*). Nên
đọc bằng `jsonb_extract_string(event_extra_props, '$.feature_effect')`, không phải một cột.

⚠ Và `enter` / `exit` **không bao giờ** là giá trị của `sub_event` — `sub_event` chở **tên bước**
(`feature_step` → `sub_event`). Lọc `sub_event = 'enter'` trả về **rỗng**.

⚠ **`feature_instance_id = ''` KHÁC `NULL`**: rỗng = *"đã mở khoá, không ở team nào"*; vắng/NULL =
*"dòng này không do tính năng team gây ra"*. Đọc lẫn hai cái là **đếm sai người không có team** —
mà đó đúng là câu *"% user unlock nhưng không có team"*.

➡ Chạm tới **tám** câu hỏi của sheet. Và kéo theo một điều kiện: **tính năng Team phải ship trước,
hoặc ít nhất cùng lúc với Team Quest** — đúng thứ tự đã chốt cho Team Arena.

### 🔴 Σ `amount` của Treasure Map là số CỦA MỘT MÁY

§4.1 **cố ý** không ghi dòng map từ fetch/đồng bộ — và đó là **quyết định đúng** cho loại dòng
này. Hệ quả: **map kiếm ở máy khác không bao giờ vào kho.**

| Câu | Ảnh hưởng |
|---|---|
| Điểm cá nhân theo phân vị | 🔴 **thiếu** với user nhiều máy |
| Độ tập trung đóng góp (top 1 / top 3) | 🔴 thiếu theo |
| Nhịp điểm team theo giờ | 🔴 thiếu theo |
| Team có active không | ✅ an toàn (EXISTS) |
| Giờ đóng góp đầu tiên | ✅ an toàn (`min`) |
| Độ khó trận thắng | ✅ an toàn |

➡ **Số chính thức của điểm cá nhân / độ tập trung / nhịp lấy từ bảng "Lần ở team" của server**
(cột `map`). Dòng resource dùng cho **thời điểm** và **độ khó** — hai thứ server không có.

ℹ️ Bản gốc đã tự biết một nửa: phép kiểm chất lượng của nó viết *"**trên từng máy**, trong từng
round, Σ `amount` = `max(value_after)`"*. Đúng — nhưng rồi điểm cá nhân vẫn đọc Σ `amount` như số
tuyệt đối.

ℹ️ Khác Ladder Streak: ở đó dòng chuỗi **có** ghi từ đồng bộ (`via = sync`) nên số là toàn cục. Hai
lựa chọn khác nhau, **cả hai đều đúng** cho loại dòng của mình — nhưng phải biết mình đang đọc loại
nào.

### ⚠ Ba luật đọc khác

**① Bỏ phép lọc `level_attempts_since_enter = 0`** để loại dòng ghi bù. Không tin được: bộ đếm đó
là *"từ lúc vào **ĐỢT**"* trên **máy đang gửi**, không phải từ lúc vào round. Máy vừa cài có thể đã
chơi vài ván trước khi data về (đếm > 0 mà **vẫn là** ghi bù), còn máy thật có thể bắn ngay ván đầu
(đếm = 0 mà **là** dòng thật). Dùng `min(created_date)` theo (user, đợt, round, mốc) + `app_version`
để tách đợt rollout.

**② `milestone` gom theo team: đọc CỘT TEAM, đừng cắt chuỗi `round_id`.** `milestone` là sự thật
của **team** nhưng **mỗi thành viên tự bắn một dòng**, và mỗi người có round id **khác nhau** (giờ
vào team khác nhau). Team 20 người ⇒ tới 20 dòng cùng một rương.

| Câu | Đọc đúng |
|---|---|
| % team đạt rương n | **EXISTS** theo (đợt, team) |
| Giờ team đạt rương n | `min(created_date)` theo (đợt, team) − `cycle_start_ts` |

⚠ `min()` ở đây là *"thành viên **online** đầu tiên nhận ra"*. Ai lúc đó offline sẽ bắn muộn hàng
giờ. Với phân vị thì chấp nhận được; đừng dùng làm mốc chính xác.

ℹ️ Tiền tố `team_id` trong `round_id` **không cần cắt ra** — `feature_instance_id` đã là cột thật
(§7 bẫy 2). Giữ tiền tố đó làm **phép kiểm miễn phí**: nó phải khớp `feature_instance_id`, lệch là
biết có chỗ sai.

**③ Bậc "thấy popup Join" đọc bằng vế HOẶC.** `open` là một dòng mỗi đợt và `team_quest_popup` đi
theo **lần mở đầu tiên**, nên ai mở popup chính trước thì dòng duy nhất của họ mang `main`. Giữ
đúng công thức của bản gốc: **có `open` với `popup = join_team` HOẶC đã có `join`**.

⚠ Thứ **không** suy lại được: `open` chỉ nói *"đã thấy UI đâu đó trong đợt"*. **Ngày nào** thì
thông tin đó chưa bao giờ rời khỏi máy. Đừng đặt mẫu số theo ngày lên bước bị nén.

---

## 10. Tự kiểm trước khi bàn giao

| Kiểm | Đạt khi |
|---|---|
| Thắng 1 màn Hard khi đã join | Đúng **1** dòng `team_quest_map`, `amount = 3` (ít hơn nếu team chạm 800), có `value_before`/`value_after`, có `feature_instance_id` |
| Thắng khi team đã chạm 800 | **0** dòng map (game không cộng ⇒ vắng là đúng) |
| **Đăng nhập máy thứ 2 giữa đợt** | 🔴 `live_ops_round_id` **GIỐNG HỆT** máy 1; và **0** dòng Treasure Map mới |
| Đổi team giữa đợt rồi join lại | Thêm **1** dòng `join` với `live_ops_round_id` **mới** và `feature_instance_id` **mới** |
| **Rời team A rồi vào LẠI team A** khi điểm đã 500 | `join` mới, nhưng **0** dòng `milestone` cho rương 1 và 2 (điều kiện vàng §3.4 loại) |
| Team vượt 2 mốc trong một lần tải data | **2** dòng `milestone`, `live_ops_stage` tăng dần |
| Join lúc điểm team đã 250 | **0** dòng `milestone` rương 1; `team_quest_team_score_at_join = 250` trên dòng `join` |
| Nhận gộp 3 rương ở popup cuối đợt | **3** dòng `claim` (mỗi dòng một stage) + quà của cả 3, tất cả mang mã **đợt cũ** |
| Mở popup kết quả | **1** dòng `complete` mang **ba** key trạng thái rương, đọc **trước** khi Claim |
| Người chưa có team mở popup Join | `open` với `popup = join_no_team`, **VẮNG** cả `feature` / `feature_instance_id` / `live_ops_round_id`. 🔴 Có `feature_instance_id = ''` là **sai** — chỉ bản tin `state` của Team được khai chuỗi rỗng |
| Nhận rương mở khoá ở team A trong lúc đã sang team B | `claim` **và** dòng quà của nó cùng mang `feature_instance_id = A` và round của A. Mang B là sai |
| Hai dòng tài nguyên | Không dòng nào có `item_type` hay `item_id` = `"Unknown"` |
| Đóng dấu đúng chỗ | **Không** dòng level / ads / IAP / log tính năng Team nào mang `live_ops_event = team_quest` |
| Impression | Vẫn đúng **hai** `surface_id`: `team_quest_main` và `team_quest_join`. Không bề mặt thứ ba, và không mất bề mặt nào |
| Bảng key §5 | Gửi **đủ** mọi dòng trong bảng, và **không** gửi gì ngoài bảng |
| Phần phải bỏ (§11) | **0** dòng mang `team_quest_team_code`; **0** dòng `join` mang `team_quest_team_score` hay `team_quest_my_maps`; **0** dòng `milestone` mang key riêng nào |
| `team_quest_difficulty` | Là **chuỗi** đúng tên độ khó của game, và **không** còn trên bước `milestone` |
| `team_quest_grant_source` | Trên **bước `claim`**, không chép sang dòng quà |
| Ghi bù cho người join từ trước | `team_quest_team_score_at_join` = `ScoreAtUserJoin` **của server**, không phải điểm team hiện tại |
| Đợt thứ hai | Mọi bước lên log bình thường (không bị nuốt vì mã đợt rỗng) |
| Không có mốc vào team từ server | Có `Debug.LogError` ở editor — và **sửa trước khi ship**, không ship kèm |

---

## 11. Phụ lục — phần bản log hiện tại phải BỎ, soát theo TỪNG BƯỚC

Game đã có một bản log Team Quest **tự định nghĩa** đang chạy. So §2–§6 với phần đã làm là ra hết
phần **phải thêm**.

🔴 **Phần phải BỎ thì không hiện ra khi so hai danh sách.** Thiếu một key thì sẽ có người hỏi; còn
**để lại** một key thừa thì không ai thấy — nó cứ chảy vào kho, trông hợp lệ, và rồi được dùng để
trả lời câu hỏi. Nên soát theo **từng bước**, và luật chỉ một câu:

> 🔴 **Trên mỗi bước, bộ key đúng là đúng bảng §5.2. Mọi key khác của bản log cũ: BỎ.**

| Bước | Bản log hiện tại đang gửi | Phải bỏ |
|---|---|---|
| `eligible` | *(không key nào)* | — |
| `open` | `team_code`, `team_score`, `my_maps` | **`team_code`** |
| `join` | `team_code`, `team_score`, `my_maps` | 🔴 **cả ba** — xem (2) |
| `milestone` | `team_code`, `team_score`, `my_maps`, `maps`, `difficulty` | 🔴 **cả năm, và bỏ luôn cách gửi bước này** — xem (3) |
| `claim` | `team_code`, `grant_source` | **`team_code`** |
| `complete` | *(bản log hiện tại chưa có bước này)* | — |

*(tên key viết tắt phần `team_quest_` cho vừa bảng)*

### (1) `team_quest_team_code` — bỏ ở MỌI bước

Nó là **Int** và nhận **một giá trị duy nhất** ⇒ không phải mã team. Mã team đi `feature_instance_id`
(§5.1).

🔴 Giữ một key *trông như* mã team **tệ hơn bỏ**: người đọc sau sẽ `GROUP BY` nó, ra **một nhóm**, và
tin rằng cả game chỉ có một team. Một ô rỗng thì người ta đi tìm; một ô sai thì người ta dùng.

### (2) `team_quest_team_score` trên `join` — chỗ nguy nhất của cả phụ lục

Thiết kế đổi sang `team_quest_team_score_at_join` (§3.3). Nếu dev **thêm key mới mà để lại key cũ**
thì trên **dòng ghi bù** (§3.7) sẽ có một số đúng và một số sai **cạnh nhau, trên cùng một bản tin**
— và cái sai trông y như cái đúng. Đây đúng là chuyện §3.3 tạo key mới để tránh; để lại key cũ là
hỏng đúng chỗ đó.

⚠ `team_quest_my_maps` trên `join` cũng bỏ: lúc vừa vào team nó **luôn bằng 0**, nên nó không trả
lời gì. Nó chỉ có nghĩa ở `open` (§5.2).

### (3) Bước `milestone` kiểu cũ là *"vừa cộng map"*, không phải *"team đạt mốc"*

Nó mang độ khó, số map vừa được, và một điểm một chữ số — ba thứ của **một trận đấu**, không phải của
một **mốc team**. Chuyện nó ghi đã chuyển thành **dòng tài nguyên** ở §4.1.

🔴 **Không để cả hai.** Để cả hai thì một lần cộng map ra **hai** bản tin, và tệ hơn: một `sub_event`
chở **hai nghĩa** trong cùng một bản app — chỗ phía đọc không gỡ lại được.

🟢 Và bỏ nó **gần như không mất gì**. Bản log hiện tại không gửi `live_ops_round_id`, nên van lọc
`(event, đợt, round, bước, stage)` chỉ cho sống **một dòng cho mỗi giá trị stage** trong một đợt:

| Nếu bước cũ | Thì |
|---|---|
| không đặt `live_ops_stage`, hoặc đặt một giá trị cố định | các lần cộng map sau lần đầu **đã bị nuốt từ đầu** — tháo ra không mất số nào |
| đặt `live_ops_stage` theo số map | các dòng còn sống, nhưng **vẫn bỏ**: chúng không có round, không có mã team, nên không trả lời được câu nào |

➡ Quyết định **không phụ thuộc** vào việc stage đang được đặt thế nào. Nên không cần tra.

### Hai chỗ bản log hiện tại làm ĐÚNG — giữ nguyên

- impression gửi qua **đúng API cụm** của SDK, hai bề mặt `team_quest_main` / `team_quest_join`;
- `team_quest_grant_source` đặt đúng trên bước `claim`.

### Và phần nó KHÔNG có thì là làm MỚI, không phải sửa

`live_ops_round_id` (§2), trục tính năng (§0 câu 3), và hai dòng tài nguyên (§4). Thiếu ba thứ đó thì
mọi câu *"theo team"*, *"điểm cá nhân"*, *"rời rồi vào lại"* **không trả lời được** — nên đừng coi
chúng là tuỳ chọn.

### Phía đọc: rào bản app cũ khỏi MỌI câu đọc Team Quest

🔴 Không chỉ rào lúc đọc `milestone`. Dòng của bản log cũ **không có round, không có mã team**, nên
bất kỳ câu *"theo team"* nào cũng sẽ lôi vào một mớ dòng team rỗng. Luật một câu: **loại các
`app_version` của bản log cũ ra khỏi mọi câu đọc Team Quest.**

Rào được là vì bản mới **bỏ hẳn** bước `milestone` kiểu cũ ⇒ trong một bản app, mỗi bước chỉ có một
nghĩa. Giữ cả hai trong cùng một bản app mới là chỗ không ai gỡ được.

⚠ **Giả định, nói ra để ai biết thì chặn:** chưa có sheet hay chart nào đọc bản log hiện tại. Nếu có
thì nói ngay, lúc đó mới cần era thật.
