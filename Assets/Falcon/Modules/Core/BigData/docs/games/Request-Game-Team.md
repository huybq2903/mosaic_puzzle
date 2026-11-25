# Tracking Team — TODO cho dev + code dán vào

> **Dev chỉ cần đọc §1 (checklist) rồi dán code ở §2–§3.** Tick `[x]` khi xong.
> Mọi quyết định đã chốt — còn **một** câu mở, và nó không chặn code (§6).
> Viết 02/11/2026. API: `FalconBigDataController.Feature` (BigData, hợp đồng
> `docs/design/FeatureContract.md`). Bản tin + tham số do bên dữ liệu chốt — bản Team của họ.

## 0. Ba câu phải nhớ trước khi gõ dòng nào

1. **Kho gọi tính năng này là `guild`**, không phải `team`. Dùng `FFeatureName.Guild`, đừng gõ
   chuỗi tay. Tên hiển thị trong game ("Team") là chuyện UI, không liên quan.
2. **Một log duy nhất `f_sdk_feature`** thay cả 8 log `f_sdk_custom_team_*` cũ. Không dùng custom
   event nữa.
3. **Bỏ `team_id` / `team_role` khỏi MỌI log khác** (level pass, ad, IAP…). Kho tự biết người chơi
   đang ở team nào nhờ bản tin `state` — xem §4.

---

## 1. Checklist

**Bản tin tính năng** (§2) — client gửi 11, server gửi 4 (§6):

- [ ] `unlock` — một lần, lúc mở khoá
- [ ] `state` — **mỗi login**, sau khi tải xong dữ liệu team **từ server**
- [ ] `create` — lập team thành công
- [ ] `join` — vào team **Public** (Private do **server** gửi `join_accepted`, client không gửi)
- [ ] `accept_request` — duyệt đơn của người khác
- [ ] `leave` — tự rời, gửi **trước** khi xoá team khỏi máy
- [ ] `kick_member` — người kick gửi
- [ ] `guild_disbanded` — **chính bạn** giải tán team
- [ ] `chat` — mỗi tin gửi thành công
- [ ] `help_request` ×2 — xin tim, xin thẻ
- [ ] `help_give` — tặng thẻ / giúp tim

**Dòng tài nguyên** (§3) — **sửa dòng đang có**, không viết log mới (giữ nguyên `itemType`/`itemId`):

- [ ] phí lập team (tiêu coin) — thêm `resourceWhen` + `Feature.Stamp`
- [ ] tim nhận được từ người giúp — đổi `resourceWhen`, thêm `Feature.Stamp`
- [ ] thẻ nhận được từ người tặng — chỉ thêm `Feature.Stamp`

**Dọn** (§4):

- [ ] xoá `team_id` / `team_role` khỏi mọi log khác
- [ ] xoá `cards_sent_today`, `previous_*_request_at`, `coin_balance_before_create`,
      `previous_team_id`, `team_unlocked`, `launch_type`, `team_tab_first_opened_at`

**Chờ quyết định, không phải việc dev client** (§6):

- [ ] ba tham số UI của lượt vào **Private** (`join_source`, `join_list_position`,
      `join_search_query_length`) có cứu không — §6, câu duy nhất còn mở

*(Đã chốt 02/11, dev không phải quyết: không đo bước `open` · Private do server gửi `join_accepted` ·
dùng `guild_disbanded` · `card_rarity` gửi nhãn chữ · chỉ đo team active · sai số "user active"
chấp nhận.)*

---

## 2. Các bản tin — code dán vào

Hai cách gọi, chọn theo bản tin:

| Cách | Khi nào |
|---|---|
| `Feature.Step(feature, step, instanceId:…, variant:…, extraMeta:…)` | đủ cho hầu hết |
| `Feature.Step(new FeatureParam { … })` | khi cần `featureJoinedTs` / `featureUnlockedTs` — hai field đó **không** có trong overload tiện tay |

🔴 **Đừng nhét hai mốc đó qua `extraMeta`.** Nó *chạy được*, nên không ai thấy gì sai — mà đó mới là
vấn đề: một cột hai nguồn, và ngày nào field thật được điền thì giá trị đi đường extras **mất im
lặng**. SDK sẽ log cảnh báo nếu bạn làm thế.

### 2.1 `unlock` — một lần, lúc đủ điều kiện mở khoá

```csharp
FalconBigDataController.Feature.Step(new FeatureParam
{
    feature = FFeatureName.Guild,
    featureStep = FFeatureStep.Unlock,
    featureUnlockedTs = unlockedTsMs   // mốc THẬT lúc mở khoá, epoch ms UTC
});
```

⚠ `featureUnlockedTs` gửi mốc thật chứ không phải `now()`: bản tin này có thể đi muộn (máy offline),
và kho lấy **mốc sớm nhất** từng thấy.

### 2.2 `state` — mỗi login, bản tin quan trọng nhất

```csharp
// GỌI SAU KHI tải xong dữ liệu team TỪ SERVER. Chỉ gửi khi đã mở khoá.
var inTeam = team != null;

var extra = new Dictionary<string, object>
{
    ["player_name_customized"] = player.NameCustomized,   // bool: LUÔN gửi cả true lẫn false
    ["join_pending_instance_id"] = pendingTeamId          // đơn đang chờ duyệt; null thì tự bỏ key
};
if (inTeam)
{
    extra["member_count"] = team.MemberCount;
    extra["guild_contribution"] = team.MyContribution;
}

FalconBigDataController.Feature.State(
    FFeatureName.Guild,
    instanceId: inTeam ? team.Id : "",                 // 🔴 "" khi KHÔNG ở team — đọc ô dưới
    joinedTs:   inTeam ? team.MyJoinedTsMs : null,     // bắt buộc khi ở team
    unlockedTs: savedUnlockedTsMs,                     // không lưu mốc thật thì truyền null
    variant:    inTeam ? team.MyRole : null,           // "leader" / "co_leader" / "member"
    extraMeta:  extra);
```

🔴 **Ba cái dễ sai nhất của cả tích hợp này, cả ba nằm ở đây:**

| | Đúng | Sai thì sao |
|---|---|---|
| Nguồn dữ liệu | **từ server**, vừa tải xong | Dựng từ cache: người bị kick lúc offline có cache còn id team cũ ⇒ bản `state` này **mới hơn** bản `kicked` của server ⇒ kho đưa họ **ngược vào team cũ** |
| Không ở team | `instanceId: ""` (chuỗi rỗng) | Truyền `null`: kho hiểu là "không chạm" ⇒ người đã rời team **vẫn ở team cũ** trong kho, im lặng |
| `joinedTs` | ngày vào **LẦN HIỆN TẠI** | Gửi ngày vào **đầu tiên**: thâm niên dài hơn thật. Vào lại đúng team cũ thì gửi **ngày vào lại**. SDK không kiểm được cái này |

⚠ `unlockedTs`: game **không lưu** mốc thật thì truyền `null`, **đừng** gửi `now()` cho đủ trường —
có bản `state` là kho đã biết "đã mở khoá".

⚠ `state` **không mang** số tim (tim không phải trạng thái team) — tim chỉ gửi ở `help_request`, §2.8.

### 2.3 `create` — server báo lập thành công

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, "create",
    instanceId: team.Id,
    variant: "leader",
    effect: FFeatureEffect.Enter,          // 🔴 bước RIÊNG của guild ⇒ GAME phải tự điền
    extraMeta: new Dictionary<string, object>
    {
        ["instance_created_ts"] = team.CreatedTsMs,
        ["member_count"] = 1,
        ["guild_type"] = isPublic ? "public" : "private",
        ["guild_required_level"] = team.RequiredLevel,
        ["guild_logo_id"] = team.LogoId,
        ["guild_name_length"] = team.Name.Length,
        ["guild_description_length"] = team.Description.Length
    });
```

🔴 `effect` **phải truyền tay** ở đây. SDK chỉ tự điền cho bước **chung** và bước **nhóm**
(`unlock`/`state`/`join`/`leave`/`variant_changed`); `create` là bước riêng của guild nên SDK không
được đoán hộ. Thiếu nó thì kho **không biết người này vừa vào một team**.

Danh sách bước riêng của guild và `effect` tương ứng:

| Bước | `effect` game phải truyền |
|---|---|
| `create` | `FFeatureEffect.Enter` |
| `guild_disbanded` (§2.11) | `FFeatureEffect.Exit` |
| `kick_member` | **bỏ** — người kick không đổi trạng thái của chính mình |

⚠ `leader_transferred` thì **server** gửi, không phải client.

### 2.4 `join` — vào team **Public** (chốt 02/11: Private do SERVER gửi)

🔴 **Chỉ gửi cho team Public (vào thẳng).** Team **Private** thì **server gửi `join_accepted`**, client
**KHÔNG** gửi gì — gửi cả hai là đếm đôi lượt vào, và Q5 đọc đúng từ đó.

```csharp
// CHỈ gọi ở đường vào thẳng team Public
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, FFeatureStep.Group.Join,     // effect = enter: SDK tự điền
    instanceId: team.Id,
    variant: "member",
    extraMeta: new Dictionary<string, object>
    {
        ["member_count"] = team.MemberCount,              // sau khi vào
        ["instance_created_ts"] = team.CreatedTsMs,       // tuổi team
        ["join_method"] = "instant",                      // đường client CHỈ có giá trị này
        ["join_source"] = fromSearch ? "search" : "suggest",
        ["join_list_position"] = listPosition,
        ["join_search_query_length"] = fromSearch ? query.Length : (int?)null,
        ["guild_required_level"] = team.RequiredLevel,
        ["guild_total_contribution"] = team.TotalContribution
    });
```

⚠ Ba tham số `join_request_*` và `join_method = request_accepted` **rời khỏi đường client** cùng với
quyết định trên — chúng thuộc bản `join_accepted` của server.

🔴 **Nhưng ba tham số UI thì server KHÔNG biết** — `join_source`, `join_list_position`,
`join_search_query_length` là việc người chơi làm trên máy họ (tab Gợi ý hay Tìm kiếm, bấm team thứ
mấy, gõ bao nhiêu ký tự). Chọn đường server cho Private nghĩa là **ba số đó chỉ còn cho lượt vào
Public**. Câu "tab nào hiệu quả hơn" / "người chơi có chọn team đầu danh sách không" sẽ **không phủ
lượt vào Private** — xem §6 để chốt có cứu hay không.

### 2.5 `accept_request` — bạn duyệt đơn của người khác

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, FFeatureStep.Group.AcceptRequest,
    instanceId: team.Id, variant: myRole,
    extraMeta: new Dictionary<string, object> { ["target_account_id"] = acceptedAccountId });
```

### 2.6 `leave` — gửi TRƯỚC khi xoá team khỏi máy

```csharp
FalconBigDataController.Feature.Step(new FeatureParam
{
    feature = FFeatureName.Guild,
    featureStep = FFeatureStep.Group.Leave,          // effect = exit: SDK tự điền
    featureInstanceId = team.Id,                     // team TRƯỚC khi rời
    featureVariant = team.MyRole,                    // vai TRƯỚC khi rời
    featureJoinedTs = team.MyJoinedTsMs,
    extraMeta = new Dictionary<string, object>
    {
        ["member_count"] = memberCountAfterLeave,    // 0 = team giải tán
        ["instance_created_ts"] = team.CreatedTsMs,
        ["guild_contribution"] = team.MyContribution,
        ["guild_total_contribution"] = team.TotalContribution,
        ["target_account_id"] = newLeaderAccountId   // chỉ khi bạn là Leader và team còn người
    }
});
```

### 2.7 `kick_member` — người kick gửi

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, "kick_member",
    instanceId: team.Id,
    variant: myRole,                                  // vai của NGƯỜI KICK
    extraMeta: new Dictionary<string, object>
    {
        ["target_account_id"] = victim.AccountId,
        ["target_variant"] = victim.Role,
        ["target_joined_ts"] = victim.JoinedTsMs,
        ["target_guild_contribution"] = victim.Contribution,
        ["target_last_online_ts"] = victim.LastOnlineTsMs,
        ["member_count"] = team.MemberCount,
        ["instance_created_ts"] = team.CreatedTsMs
    });
```

⚠ Không truyền `effect`. Người **bị** kick đổi trạng thái, không phải người kick — và bản tin cho
người bị kick là **server gửi hộ** (§6), không phải việc của client.

### 2.8 `help_request` — xin tim

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, FFeatureStep.Group.HelpRequest,
    instanceId: team.Id, variant: myRole,
    extraMeta: new Dictionary<string, object>
    {
        ["help_item"] = "lives",
        ["request_id"] = serverRequestId,
        ["lives_current"] = lives.Current,
        ["lives_max"] = lives.Max,                        // mức đang áp, xem ô dưới
        ["lives_pending_claim"] = lives.PendingClaim,
        ["lives_unlimited_end_ts"] = lives.UnlimitedEndTsMs   // null thì tự bỏ key
    });
```

⚠ **Vì sao phải gửi số tim ở đây mà kho không tự đọc:** tim **tự hồi theo thời gian** và game không
ghi dòng tài nguyên cho lần hồi, nên `valueAfter` của dòng gần nhất **không phải** số tim hiện tại.
Tim vô hạn cũng vậy — nhiều lần nhận cộng dồn thời hạn, kho không tính lại được mốc hết.

⚠ `lives_max` gửi kèm vì **kho không giữ bảng cấu hình**: mức tối đa đổi theo bản app / remote
config, chỉ client biết giá trị đang áp cho người chơi lúc đó.

### 2.9 `help_request` — xin thẻ (bản tin mang **cả hai trục**)

```csharp
var param = new FeatureParam
{
    feature = FFeatureName.Guild,
    featureStep = FFeatureStep.Group.HelpRequest,
    featureInstanceId = team.Id,
    featureVariant = myRole,
    extraMeta = new Dictionary<string, object>
    {
        ["help_item"] = "card",
        ["request_id"] = serverRequestId,
        ["card_id"] = card.Id,
        ["card_rarity"] = RarityKey(card.Rarity),       // 🔴 KHÔNG truyền enum thẳng — đọc ô dưới
        ["card_set_id"] = card.SetId,
        ["card_set_total_count"] = card.SetTotalCount,
        ["card_set_owned_count"] = mySetOwnedCount
    }
};

// Xin thẻ VÌ tính năng team, mà thẻ thuộc event card collection ⇒ dán CẢ hai trục.
// Dùng hằng số của module, đừng gõ chuỗi tay.
FalconBigDataController.LiveOps.Stamp(param, CardCollectionTracking.LIVE_OPS_EVENT);
FalconBigDataController.Feature.Step(param);
```

⚠ Hai trục **trực giao**, không phải chọn một: event live-op loại trừ nhau, tính năng chạy **song
song** với event. Không cần thêm tham số mùa — `LiveOps.Stamp` điền mã đợt đang chạy.

🔴 **`card_rarity`: đừng truyền `CardRarity` thẳng vào `extraMeta`.** `CardRarity` là enum, mà enum đi
qua JSON của SDK thành **SỐ** (không có `StringEnumConverter`) — và số đó là **thứ tự, không phải số
sao**: `OneStar = 0`, `ThreeStar = 2`, còn `Gold = 5` không phải sao nào cả. Ai đọc cột đó như "số sao"
sẽ lệch một bậc trên **mọi** thẻ, im lặng.

Hợp đồng kho khai cột này là **String**, nên map tay:

```csharp
private static string RarityKey(CardRarity r) => r switch
{
    CardRarity.OneStar => "one_star",
    CardRarity.TwoStar => "two_star",
    CardRarity.ThreeStar => "three_star",
    CardRarity.FourStar => "four_star",
    CardRarity.FiveStar => "five_star",
    CardRarity.Gold => "gold",
    _ => "unknown"
};
```

✅ **Chốt 02/11: gửi NHÃN dạng chữ**, không gửi số sao. Yêu cầu gốc (C42) khai Int "số sao", nhưng
`Gold` không phải sao nào — nhãn chữ là cách duy nhất không nhập nhằng nó với thẻ 5 sao.
<br/>Em để snake_case (`three_star`, `gold`) cho khớp **mọi giá trị khác của hợp đồng**
(`help_received`, `set_variant`…); muốn đúng tên C# (`ThreeStar`) thì đổi một hàm.

### 2.10 `help_give` — người tặng gửi

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, FFeatureStep.Group.HelpGive,
    instanceId: team.Id, variant: myRole,
    extraMeta: new Dictionary<string, object>
    {
        ["help_item"] = isCard ? "card" : "lives",
        ["target_account_id"] = receiver.AccountId,
        ["request_id"] = answeredRequestId,      // bỏ nếu tặng không theo lượt xin
        // chỉ khi tặng thẻ:
        ["card_id"] = card?.Id,
        ["card_rarity"] = card == null ? null : RarityKey(card.Rarity),   // như §2.9
        ["card_set_id"] = card?.SetId,
        ["card_set_total_count"] = card?.SetTotalCount
    });
```

⚠ Bản tin này là thứ **thay** `cards_sent_today` — kho tự đếm theo ngày. Không gửi bước này thì
câu "ai tặng nhiều, tặng thẻ gì" **không trả lời được**, và `cards_sent_today` cũng đã bỏ.

### 2.11 `guild_disbanded` — CHÍNH bạn giải tán team (chốt 02/11)

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, "guild_disbanded",
    instanceId: team.Id,
    variant: team.MyRole,                  // "leader"
    effect: FFeatureEffect.Exit,           // 🔴 bước RIÊNG của guild ⇒ GAME phải tự điền
    extraMeta: new Dictionary<string, object>
    {
        ["member_count"] = memberCountBeforeDisband,
        ["instance_created_ts"] = team.CreatedTsMs,
        ["guild_total_contribution"] = team.TotalContribution
    });
```

⚠ **Thành viên CÒN LẠI thì server gửi hộ** — họ có thể đang offline, client họ không biết team vừa
chết. Không phải việc dev client.

⚠ **Ca người cuối cùng rời team**: vẫn gửi `leave` như §2.6 với `member_count = 0`, và **server gửi
bù** một bản `guild_disbanded` (án owner 02/11). Hai bản tin **không** đếm đôi lượt rời: Q5 đếm "rời
đi" từ `leave` + `kick_member`, còn `guild_disbanded` đếm **team chết**, hai con số khác nhau.

### 2.12 `chat`

```csharp
FalconBigDataController.Feature.Step(
    FFeatureName.Guild, FFeatureStep.Group.Chat,
    instanceId: team.Id, variant: myRole,
    extraMeta: new Dictionary<string, object> { ["chat_length"] = message.Length });
```

⚠ Chỉ tin **người chơi gửi thành công**. Không tính tin hệ thống.

---

## 3. Ba dòng tài nguyên — SỬA dòng đang có, không viết log mới

Ba dòng này **game đã log rồi** (phí lập team, tim nhận từ người giúp, thẻ nhận từ người tặng). Việc
của lượt này là tìm đúng chỗ đó và thêm **hai thứ**:

1. `resourceWhen = FResourceWhen.X` — đúng hằng số, đừng gõ chuỗi tay;
2. **một dòng** `Feature.Stamp(param, FFeatureName.Guild, team.Id)` **trước** khi gửi.

🔴 **Giữ nguyên `itemType` / `itemId` / `currency` / `valueBefore` / `valueAfter` của dòng hiện có.**
Đừng dựng `new ResourceParam` mới chỉ để thêm nhãn: `itemType` và `itemId` mặc định là `"Unknown"`,
mà `itemType` thành `sub_event_2` trong kho — dòng mới toanh sẽ ra `Unknown` và **mất chiều cắt** mà
dòng cũ vẫn có.

### 3.1 Phí lập team (dòng TIÊU coin đang có)

```csharp
// tại chỗ game đang log phí lập team:
param.resourceWhen = FResourceWhen.CreateFee;                      // thêm
FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, team.Id);  // thêm
FalconBigDataController.Resource.OnSpent(param);                   // đã có
```

⚠ **Không gửi** `coin_balance_before_create` nữa — kho đọc số dư trước phí từ `valueBefore` của chính
dòng này.

### 3.2 Tim nhận được vì người khác giúp (dòng NHẬN đang có)

Dòng này đang tồn tại với nguồn `clan_help`. Đổi `resourceWhen` sang hằng số của hợp đồng và thêm
nhãn tính năng:

```csharp
// param là dòng tim đang có — itemType giữ như cũ ("live_amount"), đừng đổi
param.resourceWhen = FResourceWhen.HelpReceived;                   // thay cho nguồn cũ
FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, team.Id);
FalconBigDataController.Resource.OnEarned(param);
```

Sau bước này, câu "tim đến từ team hay từ ad/shop" đọc được bằng `feature = guild` +
`event_when = help_received`, không cần thêm field nào.

### 3.3 Thẻ nhận được vì người khác tặng

Dòng này do module Card Collection log, và nó **đã** mang `resourceWhen = event_card_collection` +
trục event (module tự `Stamp` qua tracker của nó). Việc cần thêm **chỉ là** trục tính năng:

```csharp
FalconBigDataController.Feature.Stamp(param, FFeatureName.Guild, team.Id);
```

`Feature.Stamp` chỉ điền chỗ trống (`??=`) nên nó **không** đụng gì tới `resourceWhen` hay trục event
mà module đang gửi — chart của Card Collection không đổi một số nào.

❓ **Chưa chốt:** kho có muốn dòng thẻ này mang `resourceWhen = help_received` (như dòng tim) thay cho
`event_card_collection` không. Đổi là **đổi số của chart Card Collection**, nên chờ bên dữ liệu trả
lời — ghi ở §6. Trong lúc chờ thì **giữ nguyên**: trục tính năng đã đủ để biết thẻ này đến từ team.

### Và một luật chung cho cả ba

⚠ `Feature.Stamp` **phải gọi tại chỗ phát sinh**. SDK **không** giữ sổ "đang ở team nào" để tự dán:
`GameEvent.Emit` chạy đồng bộ nên một chuỗi phản ứng sẽ đóng dấu sai lên log của hệ khác —
**đồng thời ≠ nguyên nhân**. Không gọi `Stamp` = "dòng này không phát sinh vì tính năng nào", và đó
là một lời khai **đúng**, không phải thiếu dữ liệu.

⚠ **Không** thêm `request_id` lên dòng tài nguyên (án 30/10). Nối lượt xin ↔ lượt tặng làm ở **bảng
bước** (`help_request` ↔ `help_give` đều mang `request_id`). Dòng tài nguyên chỉ cần `feature` +
`resourceWhen` là đã trả lời được "tài nguyên này từ đâu ra".

## 4. Những thứ ĐỪNG gửi nữa

| Bỏ | Kho lấy ở đâu |
|---|---|
| `team_id`, `team_role` trên **mọi** log | bản `state` → kho dán trạng thái lên mọi dòng, đọc ra `guild_id`/`guild_role` |
| `team_unlocked` | có `unlock`/`state` nghĩa là đã mở khoá |
| `launch_type` | đã có trên bản tin mở app |
| `team_tab_first_opened_at` | **không có gì thay** — chốt 02/11 không đo bước `open` (§6.1); bậc giữa của phễu thay bằng "có tương tác" |
| `cards_sent_today` | kho đếm `help_give` theo ngày |
| `previous_lives_request_at`, `previous_card_request_at` | bản `help_request` liền trước của chính người đó |
| `coin_balance_before_create` | `valueBefore` của dòng phí (§3.1) |
| `previous_team_id` | bước `leave`/`kicked` gần nhất |
| số tim trên `state` | gửi ở `help_request` lives (§2.8) |

Luật chung: **đừng gửi thứ kho đếm được từ log khác.** Ngoại lệ là thứ kho **không** đọc được —
số tim (tự hồi, không có dòng tài nguyên) và cấu hình đang áp (`lives_max`, `card_rarity`…).

---

## 5. Năm cái bẫy, xếp theo mức đau

1. 🔴 **`state` dựng từ cache** → người bị kick lúc offline quay về team cũ, im lặng. (§2.2)
2. 🔴 **`instanceId: null` thay vì `""`** khi không ở team → kho không xoá team cũ. (§2.2)
3. 🔴 **`create` thiếu `effect`** → kho không biết người này vừa vào một team. (§2.3)
4. 🔴 **`leave` gửi sau khi đã xoá team khỏi máy** → không còn `instanceId`/`variant` để gửi.
5. ⚠ **`joinedTs` là ngày vào đầu tiên** thay vì lần hiện tại → thâm niên dài hơn thật, không chỗ
   nào bắt được. (§2.2)

Thêm một cái không đau nhưng hay gặp: **gọi `Feature.Step` dày đặc**. Không có van lọc trùng như
live-op (tính năng luôn bật, không có đợt để làm khoá van) — chat 50 tin là 50 bản tin. Cần gộp thì
game tự gộp trước khi gọi.

---

## 6. Việc của server, và một câu còn mở

### Server gửi hộ — không phải việc dev client

| Bản tin | Khi nào | `feature_effect` |
|---|---|---|
| `join_accepted` | đơn Private được duyệt (**chốt 02/11**: đường duy nhất cho Private) | `enter` |
| `kicked` | lúc kick, gửi cho **người bị kick** | `exit` |
| `guild_disbanded` | gửi cho **thành viên còn lại**, và **gửi bù** khi người cuối `leave` với `member_count = 0` | `exit` |
| `leader_transferred` | chuyển quyền, gửi cho người **được** chuyển | `set_variant` (mang `feature_variant = leader`) |

Mọi bản server gửi hộ phải mang `account_id` **và `device_id` của người chịu** (máy gần nhất server
biết — thiếu `device_id` là bản tin **vào bảng lỗi**), giờ là **giờ server lúc việc xảy ra**, và
**server tự điền `feature_effect`** (server không chạy SDK nên không có ai điền hộ).

`join_accepted` nên mang các tham số server biết: `member_count`, `instance_created_ts`,
`guild_required_level`, `guild_total_contribution`, `join_method = request_accepted`,
`join_request_sent_ts`, `join_request_accepted_ts`.

❓ **Hai câu cho bên dữ liệu + server, ghi lại để không ai quên:**

1. **Ba tham số UI của lượt vào Private có cứu không** (§2.4): `join_source`,
   `join_list_position`, `join_search_query_length` là việc người chơi làm **trên máy họ**, server
   không biết. Ba cách: (a) chấp nhận chúng chỉ phủ lượt vào **Public**; (b) client **chuyền kèm** ba
   số đó trong lời gọi gửi đơn, server lưu rồi echo lại lên `join_accepted` — cứu được, tốn một field
   ở API gửi đơn; (c) thêm một bước riêng lúc **gửi đơn**, nhưng thế là thêm một bản tin cho một câu
   hỏi phụ. **Đề xuất: (b)** nếu API gửi đơn còn chỗ, không thì (a).
2. **Bản `guild_disbanded` server gửi bù cho người VỪA `leave`** thì mang `feature_effect` nào? Người
   đó **đã** `exit` bằng chính bản `leave` của họ, nên một `exit` thứ hai là vô hại nhưng dư; mà kho
   có DQ soi cặp (`sub_event`, `feature_effect`) lệch nhau. Để kho chốt: `exit` như mọi
   `guild_disbanded`, hay **bỏ key** cho đúng ca này.

### 6.1 Bước `open`: CHỐT KHÔNG ĐO (owner 02/11)

Lý do: mở màn hình tính năng **không quan trọng bằng** việc có tương tác thật, mà các bước tương tác
(`chat`, `help_request`, `help_give`, `claim`…) **đã có** và đếm đơn giản.

Hệ quả, nói rõ để không ai đi tìm sau này:

- Câu "ai còn **dùng** tính năng" đọc từ **các bước tương tác**, không từ `open`.
- `team_tab_first_opened_at` (C8) của yêu cầu gốc **mất hẳn** — phễu *đã mở khoá → đã mở tab → đã vào
  team* không còn bậc giữa. Bậc giữa thay bằng "có tương tác".
- Hằng số `FFeatureStep.Open` vẫn còn trong SDK cho tính năng khác dùng; Team **chỉ là không gửi**.

### 6.2 Phạm vi đã chốt: chỉ đo TEAM ACTIVE

✅ **Án owner 02/11: chỉ tính team active, team không active thì bỏ qua.** Nhờ vậy ba câu Q1 / Q3 /
Q5 **không còn thiếu gì từ phía client** — chúng đổi *định nghĩa*, không đổi *đường log*:

| Câu | Đọc trong phạm vi "team active" |
|---|---|
| **Q1** | số team có ít nhất một người chơi active trong kỳ |
| **Q3** | phân bố sĩ số của các team active (`member_count` gần nhất trong kỳ) |
| **Q5** | tử số = `join` + `leave` + `kick_member`; mẫu số = số team **active** ở tuổi d |

Việc của dev client: **không có gì thêm.** Đủ `instance_created_ts` trên `join`/`leave`/`kick_member`
(§2) là Q5 tính được tuổi team.

⚠ Một lưu ý khi **đọc** Q5, không phải việc dev: mẫu số là tập team **đổi theo kỳ** — một team im
lặng cả tuần rơi khỏi mẫu số tuần đó rồi quay lại tuần sau. Nên trung bình "vào/rời theo tuổi" là
trung bình **trên team còn sống**, không phải trên mọi team từng lập. So hai kỳ với nhau thì nhớ mẫu
số không cùng một tập.

### 6.3 "User active": sai số đã chấp nhận

✅ **Án owner 02/11: chấp nhận.** Yêu cầu gốc định nghĩa user active = **mỗi** lần login kể cả quay
lại từ nền; nguồn mới (bản tin mở app của SDK) **lọc** lần quay lại có thời gian ở nền dưới ngưỡng
(mặc định 30 giây). Lý do chấp nhận: hai số không lệch nhiều, và người đã mở khoá Team thường active
lâu nên sai số càng nhỏ.

❓ **Một chỗ cần dev client trả lời, vì nó quyết sai số thật lớn hơn hay nhỏ hơn 30 giây kia:** bản
`state` phải dựng từ **dữ liệu team vừa tải từ server** (§2.2). Vậy khi người chơi quay lại từ nền
sau 5 giây, **game có gọi lại server lấy dữ liệu team không?**

- **Có** ⇒ có một dòng `state` cho mỗi lần login thật, và đếm người theo dòng `state` khớp **đúng**
  định nghĩa gốc, không chịu cái lọc 30 giây nào.
- **Không** ⇒ dòng `state` **thưa hơn** số lần login, và thưa bao nhiêu thì tuỳ luật đồng bộ của
  game — chứ không phải tuỳ ngưỡng 30 giây.

Đằng nào cũng **không phải sửa code**; chỉ là bên dữ liệu cần biết con số họ đang đọc được dựng thế
nào. Và dù trả lời là gì thì luật §2.2 **không đổi**: thà gửi muộn còn hơn gửi từ cache.

### 6.4 Hai chỗ hợp đồng CỐ Ý đi khác yêu cầu gốc

Không phải lỗi, nhưng ai đọc yêu cầu gốc sẽ thấy khác:

1. **`heart_request_id` + `card_request_id` → một `request_id`.** Quy tắc đặt tên §1.2.2 của yêu cầu
   gốc **cố ý tách hai key** ("không dùng chung một key cho hai loại id"). Hợp đồng gộp lại một key,
   phân biệt bằng `help_item` (`lives`/`card`) — vẫn tách được, và đổi lại là nối
   `help_request` ↔ `help_give` chỉ cần một khoá.
2. **`team_id`/`team_role` không còn đi theo mọi log.** Gốc dán hai tham số chung lên **mọi** log để
   Q8 gom được level pass theo team. Nay kho dán trạng thái từ profile lên mọi dòng gold, nên Q8
   **không cần tham số nào từ game** — nhưng nó **phụ thuộc khối trạng thái của kho đã chạy**, và chỉ
   đúng **từ ngày game gửi `state`**. Trước ngày đó, không dòng nào biết team.

Bản server gửi hộ phải mang `account_id` **và `device_id` của người chịu**, và **server tự điền
`feature_effect`** (server không chạy SDK). Thiếu `device_id` là bản tin vào bảng lỗi.

---

## 7. Tự kiểm trước khi bàn giao

- [ ] Grep `team_id`, `team_role` trong code tracking — còn chỗ nào gửi lên log khác không?
- [ ] Mở log dev, làm một lượt: `unlock` → `state` → `join` → `chat` → `help_request` → `leave`.
      Mỗi bản tin có `feature = guild` và `feature_instance_id` đúng không?
- [ ] Đăng xuất / vào lại: có **đúng một** `state` mỗi login, và nó đi **sau** khi data team về?
- [ ] Rời team rồi login lại: `state` có `feature_instance_id = ""` (rỗng, **không** phải vắng key)?
- [ ] Quay lại app sau **5 giây** ở nền: game có gọi lại server lấy dữ liệu team, và có một dòng
      `state` mới không? Trả lời câu nào cũng được, nhưng phải **nói lại cho bên dữ liệu** — nó quyết
      con số "user active" họ đọc (§6.3)
- [ ] Console có cảnh báo nào của SDK không (`extraMeta` trùng tên field thật, `feature` không
      snake_case, `state` thiếu `featureJoinedTs`)? Cảnh báo nào cũng là một chỗ sai thật.
