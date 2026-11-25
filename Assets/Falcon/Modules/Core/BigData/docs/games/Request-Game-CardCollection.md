# Tracking Card Collection — TODO cho dev + tham chiếu chart

> **Dev chỉ cần đọc phần TODO ngay dưới đây.** Không có event mới, không có API mới — dùng Funnel
> và Resource đã có sẵn. Tick `[x]` khi xong.
> Viết 12/10/2026. Tiền tố key: **`card_col_`**.

---

# 🟡 RÀ SỐ PRODUCTION 28/10 — `join` lặp trong một đợt: CC là chỗ phơi nhiều nhất

`LogJoined()` chạy từ `OnDataUpdated`, cờ chặn `_joinedCycleId` là **`static` trong RAM**, còn điều
kiện chỉ là `data.joined == true` — mà cờ đó là **state server, đúng suốt đợt**. Hệ quả: **mỗi
phiên** module lại thử bắn `join` một lần, và thứ duy nhất chặn là **van lọc của SDK**. Đây đúng
khuôn **K1** của Key Collection mà dev xác nhận "cố ý, dựa vào van của SDK" — nhưng nó cũng có
nghĩa CC **không có tầng tự vệ nào** nếu van mất khoá (máy bị kill trước lúc pool sync, cài lại máy
mà save đồng bộ về).

Cách tách nguyên nhân (cùng bảng với `Request-Game-KeyCollection.md` §0b): dòng trùng **cùng
`uuid`** = kho đếm trùng · **khác `cycle_id`** = mã đợt đổi giữa đợt · cùng `cycle_id` + khác
`uuid`= van mất khoá. Ra nhánh thứ ba thì việc sửa là **ghi cờ `joined` đã log xuống local data**
(CC vốn đã làm thế với `loggedTradeSeq`), chứ không phải vá van.

# 🟡 VÒNG RÀ 3 — 21/10 17:00: **C3 xong NỬA bên nhận**, bên bắn chưa đổi

| Mục | Dev khai | Kiểm trong code |
|---|---|---|
| **C3** cửa tin nhắn mất tên event tặng | "`EVENT_EXTERNAL_PACK` nhận payload `<source>\|<packId>`, `<source>` thành `resource_when` của dòng túi; payload chỉ có `packId` vẫn chạy, nguồn `external_event`" | ⚠ **bên nhận đúng, bên bắn chưa ai dùng**. Nhận: `DeclareCardCollection.cs:798-810` tách bằng `EXTERNAL_PACK_SOURCE_SEPARATOR = '\|'`, có separator → `GrantExternalPack(packId, source, null)` → `ExternalPackContext` đặt `resourceWhen = source`; không separator → vẫn `external_event` (**không phá game đang chạy**). Nhưng grep `Assets/Falcon` **không có chỗ nào emit** khoá này, và `nutsandbolts` cũng không → nơi bắn nằm trong **code game** (CHANGELOG 1.0.1: Daily Challenge / Daily Bonus). **Tới khi game đổi payload, mọi dòng túi vẫn ra `resource_when = external_event`** |
| Đường log túi | — | ✅ không đổi: `CardCollectionTracking.cs` còn nguyên bản 15:30, túi vẫn log ở `OnPackGranted` với `when = pack.trackWhen ?? pack.source`, một `exchangeId` mỗi túi |
| Các đường log khác | — | ✅ 14 lời gọi tracking từ `DeclareCardCollection` vẫn đủ (eligible · open · starter pack · pack granted/opened · set/phase/chest claim · cards received · data updated) — phần sửa 17:00 chỉ chạm parse payload và UI chuyền túi |

## 🟡 Hai việc còn lại của Card Collection

**1. 🔴 Việc chính còn lại: bên BẮN phải đổi payload (code game).** Module chỉ mở đường; chừng nào
Daily Challenge / Daily Bonus (và mọi chỗ khác tặng túi) còn bắn
`GameEvent<string>.Emit(CardCollectionConst.EVENT_EXTERNAL_PACK, packId)` kiểu cũ thì cột
`resource_when` **vẫn là `external_event`** và chart "túi đến từ event nào" **vẫn rỗng**. Payload mới
phải là **`event_<tên event>|<packId>`**, vd `event_daily_challenge|pack_rare`.

⚠ Và module **không chuẩn hoá** chuỗi đó: ai bắn gì thì cột ghi y vậy. Vocab của chính event này là
có tiền tố — `RESOURCE_WHEN = FFunnelName.EventPrefix + "card_collection"` = `event_card_collection`
— nên đừng bắn `daily_challenge` trần, cột sẽ có hai hệ tên lẫn nhau. (Nếu muốn chắc, dev thêm một
dòng chuẩn hoá trong `OnExternalPackEmitted`: thiếu tiền tố `event_` thì tự thêm.)

**2. Pin `core.gamedata 1.1.7` trỏ bản KHÔNG tồn tại.** Repo chỉ có `falcon.modules.core.gamedata`
**1.1.6** (`Modules/Core/GameData/package.json`). Treo từ vòng rà 1, vẫn chưa sửa. (Phụ: `Events/Core`
và `KeyCollection` pin `level.core 1.3.11` còn repo đã là `1.3.12` — không ảnh hưởng log, chỉ lệch pin.)

---

# ✅ VÒNG RÀ 2 — 21/10 15:30: **còn 1 chỗ (C3)** — C3 đã sửa 17:00, xem vòng rà 3 ở trên

Đã kiểm từng khẳng định của dev bằng code, không tin lời.

| Mục | Dev nói | Kiểm trong code |
|---|---|---|
| **C1** rương | bỏ `live_ops_round_id`, không gửi `claim`, đọc bằng 2 dòng `star_chest` chung `exchange_id` | ✅ **đúng hết**: helper `ChestRoundId` **đã xoá**; `ChestRewardContext` không còn truyền `roundId`; 3 lời gọi `Claim` còn lại đúng ba hình dạng họ mô tả; vế trừ sao `OnSpent` dùng **cùng** `exchangeId` với vế thưởng |
| **C2** `ExternalPackContext` | chỉ chở nguồn vào hàng chờ; dòng túi thật log ở `OnPackGranted` và **có** `Stamp` | ✅ đúng — `OnPackGranted` gọi `Resource.OnEarned(Tracker.Stamp(...))`. Mình đọc thiếu ngữ cảnh ở vòng 1, **C2 không phải lỗi** |
| **D1** | event phát túi không tự log, Card log thay | ✅ nhận quy ước. ⚠ Quy ước này phải nằm trong doc của **event phát túi**, không chỉ ở đây |
| **D3** | không có đường nhận thẻ lẻ | ✅ khớp code: chỉ `pack_open` + `friend_gift`/`teammate_gift`, không có `direct_grant` |
| **D4** | mở khi `level >= levelUnlock` (field config server) | ✅ data team dựng được nhóm lịch sử bằng `current_level` |
| **D5** | một trục; Gold = `card_col_tier` **6** | ✅ **đúng và trọn**: `enum CardRarity` có 6 giá trị `OneStar=0 … Gold=5`, mà `tier = rarity + 1` → Gold = 6. **Kim cương KHÔNG tồn tại trong code** — nên câu "vàng/kim cương" của GD chỉ có một nửa là thật |
| **Round** | round = phase (`phase_<p>`) | ✅ `PhaseRoundId(phase)` đi trên `milestone`/`claim` của set, claim quà phase, và dòng tài nguyên của phase đó |
| **Pin** | `bigdata 1.5.4` | ✅ cả 4 module, kèm `eventsliveops.core 1.1.0` |

## 🔴 C3 — cửa phát túi bằng `GameEvent` ghi cứng `external_event` → mất tên event tặng

Rà tiếp D1 thì ra chuyện này. Một dòng túi có **hai ô khác nhau**, đừng lẫn:

- `live_ops_event` = *"đồ này thuộc event nào"* → luôn là `card_collection`, vì `Tracker.Stamp` của
  Card Collection đóng dấu (`EventLiveOpsTracker:134` dùng `??=`). **Đúng và hợp lý** — túi thẻ là đồ
  của Card Collection. Phía đọc số không phải chốt gì ở ô này.
- `resource_when` = *"do đâu mà có"* → đây mới là chỗ chở tên **event tặng túi**.

Có **hai cửa** để event khác phát túi, và một cửa ghi thiếu:

| Cửa | `resource_when` / `resource_where` | Đọc ra |
|---|---|---|
| `DeclareCardCollection.AddPack(packId, source, surface)` (gọi hàm trực tiếp) | đúng chuỗi người gọi truyền, vd `event_key_collection` | ✅ "Key Collection tặng 1 túi" |
| `GameEvent<string>.Emit("falcon.modules.events.cardcollection.open_pack", packName)` (bắn tin nhắn) | **ghi cứng `"external_event"`** — `DeclareCardCollection.cs:798` truyền `SOURCE_EXTERNAL_EVENT` và `surface = null` | ❌ "một event nào đó tặng" — **mất tên** |

Grep `Assets/Falcon/Modules/Events` 21/10: **không chỗ nào** emit khoá đó, cũng không chỗ nào gọi
`AddPack` ngoài chính module → cửa này do **game** dùng, doc không thấy được.

**Việc cho dev — chọn một:**

1. Cho cửa tin nhắn chở được tên event tặng (vd `GameEvent<PackGrantRequest>` hoặc payload
   `"<event>|<packName>"`), rồi truyền tiếp vào `ExternalPackContext(source, surface)` thay cho hằng
   `SOURCE_EXTERNAL_EVENT`; **hoặc**
2. Cam kết **không dùng** cửa tin nhắn, mọi nơi phát túi gọi
   `AddPack(packId, source: "event_<tên event>", surface)`.

**Kiểm:** Key Collection tặng 1 túi → dòng `item_type = pack` có `live_ops_event = card_collection`
**và** `resource_when = event_key_collection`. Thấy `external_event` là còn sót cửa cũ.

⚠ Quy ước đọc: muốn biết "túi đến từ event nào" thì lọc `live_ops_event = card_collection` +
`item_type = pack` rồi **group theo `resource_when`** — chứ không group theo `live_ops_event`, vì ô
đó luôn là `card_collection`. Nếu sau này muốn quy túi về **event phát** thì phải set
`liveOpsEvent` của event phát **trước** khi `Stamp` (nó dùng `??=` nên tôn trọng giá trị có sẵn), và
lúc đó **mất** cycle/round của Card Collection — không thể có cả hai trong một dòng.

## ⛔ Doc này từng ghi SAI một chốt — đã sửa

Mục **Q3** ở đầu doc ghi *"Thưởng set **tự nhận**"* → **sai**. Dev xác nhận quà set **phải bấm nhận**,
và code khớp: `OnSetRewardClaimed` được gọi từ đường claim ở `DeclareCardCollection.cs:1565`.

Hệ quả cho người đọc số, **nhớ kỹ chỗ này**:

| Câu hỏi | Đọc ở đâu |
|---|---|
| "Hoàn thành set" | `action = milestone` + `live_ops_stage` |
| "Đã **nhận** quà set" | `action = claim` có `live_ops_stage` · hoặc dòng `resource_where = set_reward` |
| "Xong set mà **quên nhận**" | milestone có mà claim cùng `stage` không có |

Ba hình dạng `claim` của event này (dev xác nhận, code khớp):

| `live_ops_stage` | `live_ops_round_id` | nghĩa |
|---|---|---|
| có | `phase_<p>` | quà **set** (mốc) |
| không | `phase_<p>` | quà **phase** |
| không | không | **starter pack** |

---

# ✅ RÀ THEO CODE 21/10 — dev ĐÃ TÍCH HỢP, hầu hết doc này đã xong

> Rà trên code thật: `Modules/Events/CardCollection` **1.1.0**, file
> `Scripts/Runtime/Analytic/CardCollectionTracking.cs` (405 dòng). Doc dưới đây viết lúc **chưa có
> dòng code nào**; giờ đọc theo code thì phần lớn **đã làm, và làm đúng**. Bảng này là trạng thái
> thật, đọc nó trước khi đọc phần TODO cũ.

| Mục | Trạng thái theo code | Bằng chứng trong code |
|---|---|---|
| **L1** dòng live-ops thay funnel | ✅ xong | 6 mốc qua `EventLiveOpsTracker`; **không** còn lời gọi funnel nào |
| **L2** mỗi set xong = 1 `milestone`, `stage` cộng dồn | ✅ xong | `SetStage(phase,setIndex)` = `setsBefore + position` → số set **cộng dồn qua các phase**; `LogCompletedSets()` bắn cho **mỗi** set mới xong |
| **L3** mọi dòng tài nguyên `Stamp` | ✅ xong (1 ngoại lệ có chủ ý) | `EventContext()` và `LogCardEarned()` đều qua `Tracker.Stamp`. Ngoại lệ: `ExternalPackContext()` **không** stamp — xem C2 |
| **L4** bỏ `card_col_season_id`, giữ 5 key | ✅ xong | key còn lại đúng 5: `card_col_phase` · `_set` · `_tier` · `_pack_type` · `_chest`. Không có `season_id` |
| **L5** không gắn gì lên log level | ✅ xong | không lời gọi level nào trong file tracking |
| **A1** hai mốc funnel | ⛔ **đã thay** bởi L1 — bỏ qua |  |
| **A2** mở túi: 1 `exchangeId` cho túi + n thẻ + 1 sao | ✅ xong, **và làm hơn doc** | `OnPackGranted` sinh `exchangeId` rồi `EnqueueTrackedPack`; `OnPackOpened` **dequeue đúng id đó** → túi và thẻ nối được **qua các phiên app**, không chỉ trong một lần |
| **A3** thẻ được tặng: dòng riêng, `where` nói ai tặng | ✅ xong | `friend_gift` / `teammate_gift`; `OnCardsReceived` chống trùng bằng `loggedTradeSeq` **ghi vào local data** |
| **A4** đổi rương: hai vế chung `exchangeId` | ✅ xong | `ChestRewardContext(chestId, exchangeId)` nhận id từ ngoài; vế trừ sao dùng **cùng** id |
| **A5** quà set/phase: mỗi set một `exchangeId` | ✅ xong | `SetRewardContext` / `PhaseRewardContext` → `EventContext` sinh id mới |
| **A7** pin phiên bản | ✅ **đã sửa 21/10**: `bigdata 1.5.2 → 1.5.4` | 1.5.2 thiếu **sổ hai ô** (1.5.3) mà `Events/Core` cần cho mốc của đợt đã đóng |

## Ba câu D đã có câu trả lời — trong code

| # | Trả lời theo code |
|---|---|
| **D1** | ✅ **Có cửa nhận-túi-kèm-nguồn**: `OnPackGranted(CardCollectionPendingPack)` đọc `pack.source` · `pack.trackWhen` · `pack.trackWhere` · `pack.trackDetail`, và **chính nó log dòng túi**. ⚠ Việc còn lại là **quy ước**: event phát túi **không được** log dòng túi của riêng nó, không thì đếm đôi. Cần một dòng trong doc của event phát túi |
| **D2** | ✅ Thẻ trùng **nằm lại album VÀ sinh sao**: dòng thẻ ghi `valueBefore = before`, `valueAfter = before + 1` (số bản **tăng**), rồi một dòng `card_star` riêng với `resource_where = duplicate_card` cho `result.StarsGained`. Đọc "trùng" bằng `value_before > 0` vẫn đúng như doc đã nói |
| **D5** | ✅ Code chọn **MỘT trục**: chỉ có `card_col_tier = rarity + 1`, **không có** `card_col_variant`. Nghĩa là "thẻ vàng/kim cương" (nếu có) đang được coi là **một giá trị của bậc** chứ không phải trục riêng. ⚠ GD từng đòi **hai chart riêng** — nếu vẫn muốn thì phải thêm key, còn không thì data team biết là một trục |

**Còn treo đúng hai câu**, và cả hai **không đọc được từ code**:

| # | Vẫn cần trả lời |
|---|---|
| **D3** | Có đường **nhận thẻ lẻ** nào ngoài túi và ngoài tặng không? Code chỉ có `pack_open` + `friend_gift`/`teammate_gift`; **không có** `direct_grant`. Nếu tồn tại đường thứ ba thì thẻ đó đang **mất khỏi chart nguồn mà không có dấu hiệu gì** |
| **D4** | Điều kiện unlock event là gì (level? xong chương? cờ server?). Code gate `eligible` bằng `IsEventValid()` — không nói ngưỡng. Data team cần ngưỡng để dựng nhóm lịch sử cho chart trước/sau launch |

## Hai chỗ nên sửa, tìm ra khi rà code

### [ ] C1 — `roundId` đang bị dùng làm **mã phiên rương**

`ChestRewardContext` đặt `roundId = ChestRoundId(exchangeId)` → `"chest_<guid>"`. Nghĩa là **mỗi lần
đổi rương là một "vòng" mới**. Hai hệ quả: (1) van chống trùng của live-ops **không bao giờ chặn** gì
cho nhóm rương, (2) cột `live_ops_round_id` ở kho **nổ cardinality** — mỗi giao dịch một giá trị, nên
mọi chart group-by round sẽ vô dụng.
Đúng ra: rương không phải "vòng". Danh tính giao dịch **đã có** `exchange_id` lo. Đề xuất bỏ `roundId`
cho nhóm rương (để vắng mặt), còn muốn biết rương nào thì đã có `card_col_chest` trong `detail`.

### [ ] C2 — `ExternalPackContext()` không `Stamp` live-ops

Hàm này dựng `ResourceParam` cho túi **do event khác phát**, và **không** gọi `Tracker.Stamp` → dòng
đó ra **không có** 4 param `live_ops_*`, dù `detail` vẫn mang `card_col_phase`. Nếu là **cố ý** (túi
của event khác thì không tính là tài nguyên của Card Collection) thì ổn, nhưng phải nói ra, vì
`card_col_phase` có mặt mà `live_ops_event` vắng là một hình dạng dễ làm người đọc số hiểu sai.

---

Card Collection là event **nặng nhất** tới giờ, vì nó có **bốn dòng tài nguyên lồng nhau**:

```
túi thẻ  →  thẻ  →  (thẻ trùng) sao  →  quà rương / quà set / quà phase
```

Bốn câu hỏi quyết định hình dạng log đã chốt với dev 12/10:

| # | Câu | Chốt | Hệ quả |
|---|---|---|---|
| Q1 | Túi mở ngay hay nằm kho? | **Mở ngay khi nhận** | Thẻ nối về đúng nguồn (event nào / chương nào) bằng `exchangeId` — không cần nhớ nguồn theo từng túi |
| Q2 | Thẻ / sao / túi có đi qua kho tài nguyên? | **KHÔNG** | Module event **tự gọi** `FalconBigDataController.Resource.*`. Riêng **quà** (gold, booster…) vẫn đi qua kho như thường |
| Q3 | Thưởng set tự nhận? Set nào cũng có quà? | **Tự nhận, set nào cũng có** | "Hoàn thành set" đọc từ **dòng thưởng** — không cần mốc funnel cho từng set |
| Q4 | Mã mùa | **Server cấp** | id do server cấp, không phải ngày tự suy. Đi bằng **`live_ops_cycle_id`** (cột thật), không còn `card_col_season_id` trong `detail` — mục 🔁 L4 |

# 🔁 LÀM THEO HỢP ĐỒNG LIVE-OP (event này CHƯA lên dữ liệu → không có pha song song)

> Chốt 15/10/2026. Hợp đồng: `docs/design/LiveOpsEventContract.md` · Khuôn:
> `docs/design/LiveOpsTrackingPattern.md`. SDK **BigData 1.5.0** đã có đủ API, và
> ✅ **`f_sdk_live_ops_data` ĐÃ KÝ (20/10)** → **không còn gì chặn ship**.
> **Khoá event của event này: `card_collection`** (để trần, không tiền tố `event_`).
>
> Event này **chưa có dòng log nào trên kho**, nên không phải giữ gì để tương thích: làm thẳng theo
> mục này, bỏ qua chỗ nào trong phần dưới trái với nó.
> ⚠ Ba event ĐANG CHẠY (Master League · Race · Endless Treasure) thì khác — chúng chạy **pha song
> song**, xem doc của chúng.


### [ ] L1 — Dòng live-ops thay funnel (sửa việc A1)

```csharp
FalconBigDataController.LiveOps.DeclareCycle(
    "card_collection", cycleId: seasonId, cycleIndex: seasonIndex,
    cycleStartTs: seasonStartMs, cycleEndTs: seasonEndMs);

FalconBigDataController.LiveOps.OnStep("card_collection", FLiveOpsAction.Eligible);  // MỖI PHIÊN
FalconBigDataController.LiveOps.OnStep("card_collection", FLiveOpsAction.Open);      // mở album
```

Không `Register`, không chọn `priority`, không tự bê lịch đợt — ba thứ mục A1 đang yêu cầu đều bỏ.

### [ ] L2 — Mỗi set xong: một dòng `milestone`, `stage` là số set **CỘNG DỒN**

```csharp
var stage = (phase - 1) * setsPerPhase + setIndex;   // phase 1 set 7 → 7 | phase 2 set 7 → 22
FalconBigDataController.LiveOps.OnStep("card_collection", FLiveOpsAction.Milestone, stage: stage);
```

Nhờ cộng dồn mà **"số set hoàn thành" = `max(live_ops_stage)`** — thay quy ước đọc 5 cũ (đếm
distinct cặp `(phase, set)`), và phân vị tính được bằng một câu.

### [ ] L3 — Mọi dòng tài nguyên: `Stamp`

```csharp
FalconBigDataController.LiveOps.Stamp(param, "card_collection", stage: stage);  // stage: chỉ dòng thuộc một set
```

Áp cho cả bảy loại dòng ở A2–A5: túi, thẻ, sao, quà set, quà phase, quà rương, thẻ được tặng.

### [ ] L4 — Key nào bỏ, key nào giữ

| Key trong doc này | Làm gì |
|---|---|
| `card_col_season_id` | **Bỏ** — `live_ops_cycle_id` thay |
| `card_col_set` trên dòng **quà set** | **Bỏ** — `live_ops_stage` thay |
| `card_col_set` trên dòng **thẻ** | **Giữ** — cần biết thẻ thuộc set nào |
| `card_col_phase`, `card_col_tier`, `card_col_variant`, `card_col_pack_type`, `card_col_chest` | **Giữ** — chiều phân tích riêng |

### [ ] L5 — Log level: không liên quan

Card Collection không có ván chơi riêng → không khai gì trên log level.

⚠ **KHÔNG gọi thêm `Resource.OnEarned` để "chở ngữ cảnh live-ops"**. `Stamp` điền thêm cột vào
chính param đang gửi, **không sinh dòng mới** — gọi thêm là đếm đôi tiền (vết sẹo log-đôi của
Master League).

---

# ✅ TODO

## A. Dev module Card Collection

### [ ] A1 — Hai mốc funnel, không hơn

```csharp
// Gọi MỘT lần khi module khởi tạo:
FalconBigDataController.Funnel.Register(
    FFunnelName.Event("card_collection"), FunnelShape.OncePerCycle);

// Helper dùng chung cho cả hai bước:
private void CardStep(string action, int priority) =>
    FalconBigDataController.Funnel.OnStep(
        funnelName:   FFunnelName.Event("card_collection"),
        action:       action,
        priority:     priority,
        cycleId:      seasonId,            // mã mùa từ server (Q4)
        cycleIndex:   seasonIndex,         // mùa thứ mấy — trục sort bằng SỐ
        cycleStartTs: seasonStartMs,       // epoch millis UTC, đúng số đồng hồ đếm ngược đang vẽ
        cycleEndTs:   seasonEndMs);

// Gọi MỖI PHIÊN / mỗi lần mở app, miễn user đang đủ điều kiện và mùa đang chạy:
CardStep("eligible", 10);
// Gọi MỖI LẦN mở UI album:
CardStep("open",     15);
```

⚠ `"eligible"` và `"open"` **không có hằng số trong `FFunnelAction`** (ở đó chỉ có `join` /
`milestone` / `claim` / `complete`) — nhưng hai chuỗi này đã là vocab đang chạy của Master League
và Race, nên gõ đúng y nguyên, đừng tự đổi thành `enter` / `view`.

Shape `OncePerCycle` lo phần lọc: mỗi bước tối đa **một dòng / user / mùa**, sang mùa mới tự sạch,
dev không cần nhớ đã bắn chưa.

⚠ **Chỉ hai bước này.** Đừng thêm mốc cho từng set — set nào cũng có quà và quà tự nhận (Q3) nên
dòng thưởng đã là mốc; thêm funnel là ghi hai lần cùng một việc.

⚠ **`eligible` KHÔNG phải "lúc user vượt ngưỡng mở khoá".** Bắn kiểu đó thì từ mùa 2 trở đi gần như
không ai có dòng `eligible` nữa (user đã mở khoá từ mùa 1, không vượt ngưỡng lần nào nữa) — mà đó là
**mẫu số của mọi chart**. Cứ gọi mỗi phiên, `OncePerCycle` tự lọc còn một dòng mỗi mùa. Đây đúng là
lỗi đã phải sửa ở Master League (việc G1).

**Kiểm 1:** user đủ điều kiện, chưa mở UI → đúng **một** dòng `eligible`, **không** có dòng `open`.
Đây chính là con số trả lời lo ngại "mở khoá rồi nhưng không thèm vào".
**Kiểm 2:** user mở khoá từ mùa trước, mùa này mở app giữa mùa → **vẫn có** dòng `eligible`.

### [ ] A2 — Mở túi: một `exchangeId`, một dòng túi + n dòng thẻ + một dòng sao

**Bước 0 — mọi chỗ phát túi phải đi qua một cửa và truyền NGUỒN.** Túi do module khác phát
(Master League, xong chương, rương sao…) nhưng không đi qua kho, nên Card Collection phải có một
hàm nhận túi và **nguồn đi kèm**:

```csharp
// Card Collection mở cửa này ra:
public void GrantPack(string packType, string source, string surface) { /* mở túi + log A2 */ }

// Mọi chỗ phát túi gọi vào, KÈM nguồn:
cardCollection.GrantPack("pack_red", FResourceWhen.Event("master_league"), "league_reward_popup");
cardCollection.GrantPack("pack_brown", FResourceWhen.LevelReward,          "chapter_end");
cardCollection.GrantPack("pack_gold", "event_card_collection",             "star_chest");
```

⚠ **Không có `source` thì chart "thẻ đến từ event nào" rỗng** — đó là lý do duy nhất mình hỏi Q1.
Đừng để mặc định `unknown`.

⚠ **Module phát túi KHÔNG được tự log dòng tài nguyên cho túi đó.** Túi chỉ có một dòng, do Card
Collection ghi (A2). Hai bên cùng ghi là đếm đôi — đúng lỗi của Master League.

Vì túi mở ngay (Q1), cả cụm dưới đây là **một khoảnh khắc**:

```csharp
var ex = Guid.NewGuid().ToString("N");   // CHUNG cho cả cụm — thứ duy nhất nối "túi nào ra thẻ gì"

var seasonCtx = new Dictionary<string, object>
{
    // ⛔ L4 (hợp đồng live-op): BỎ dòng này — `live_ops_cycle_id` thay, và `Stamp` đã điền hộ.
    // ["card_col_season_id"] = seasonId,
    ["card_col_phase"]     = phase,          // 1 / 2
};

// ─── Vế 1: cái túi vừa nhận ───
FalconBigDataController.Resource.OnEarned(new ResourceParam
{
    exchangeId    = ex,
    itemType      = "card_pack",
    currency      = "card_pack",
    itemId        = packType,            // loại túi: "pack_brown" / "pack_red" / …
    amount        = 1,
    resourceWhen  = source,              // NGUỒN, dùng hằng sẵn có: FResourceWhen.Event("master_league"),
                                         // FResourceWhen.LevelReward (xong chương), FResourceWhen.Gift…
                                         // Túi của chính event này (đổi bằng sao) = "event_card_collection"
    resourceWhere = surface,             // nơi phát: "chapter_end", "league_reward_popup", "star_chest"…
    detail        = new Dictionary<string, object>(seasonCtx),
});

// ─── Vế 2: từng thẻ ra khỏi túi ───
foreach (var card in cardsFromPack)
{
    FalconBigDataController.Resource.OnEarned(new ResourceParam
    {
        exchangeId    = ex,
        itemType      = "card",
        currency      = "card",
        itemId        = card.id,                 // mã thẻ
        amount        = 1,
        valueBefore   = card.countBefore,        // số bản ĐANG giữ trước khi nhận  ← BẮT BUỘC
        valueAfter    = card.countBefore + 1,    //                                  ← BẮT BUỘC
        resourceWhen  = "event_card_collection",
        resourceWhere = "pack_open",
        detail = new Dictionary<string, object>(seasonCtx)
        {
            ["card_col_set"]       = card.setIndex,  // set thẻ thuộc về
            ["card_col_tier"]      = card.tier,      // bậc sao của thẻ: 1..5
            ["card_col_variant"]   = card.variant,   // "normal" / "gold" / "diamond" — TRỤC KHÁC bậc sao
            ["card_col_pack_type"] = packType,
        },
    });
}

// ─── Vế 3: sao sinh ra từ thẻ trùng — MỘT dòng cho cả túi ───
if (starsFromDuplicates > 0)
    FalconBigDataController.Resource.OnEarned(new ResourceParam
    {
        exchangeId    = ex,
        itemType      = "card_star",
        currency      = "card_star",
        itemId        = "card_star",
        amount        = starsFromDuplicates,
        valueBefore   = starBefore,              // ← BẮT BUỘC
        valueAfter    = starBefore + starsFromDuplicates,
        resourceWhen  = "event_card_collection",
        resourceWhere = "duplicate_card",
        detail        = new Dictionary<string, object>(seasonCtx),
    });
```

⚠ **`card_col_variant` là trục RIÊNG, không phải một giá trị của `card_col_tier`.** GD đòi hai
chart khác nhau: "thẻ 1-5 sao" và "thẻ vàng/kim cương" — một thẻ vàng vẫn có bậc sao của nó, nên
nhét vàng/kim cương vào `tier` là mất một trong hai trục. Không có key này thì chart "% mới vs
trùng của thẻ vàng/kim cương" **không dựng được** trừ khi dựng bảng danh mục thẻ (thứ doc này
cố ý không làm — xem lý do ở bảng cuối). Nếu dev xác nhận game **không** có khái niệm biến thể
tách rời bậc sao (vàng/kim cương chính là `tier` 6/7) thì bỏ key này và nói lại với data team
(câu D5).

Ba chỗ dễ sai:

- **`valueBefore` / `valueAfter` của dòng thẻ là số bản của CHÍNH thẻ đó**, không phải tổng số
  thẻ đang có. Game chắc chắn biết số này — chính nó vào công thức trọng số gacha. Đây là thứ cho
  server biết **thẻ mới hay thẻ trùng** mà không cần thêm key nào.
- ⚠ Một túi ra **hai bản cùng một thẻ** thì `countBefore` phải là số **đang chạy** trong vòng lặp
  (bản thứ hai có `valueBefore = countBefore + 1`), không phải số chụp trước khi mở túi — chụp một
  lần là bản thứ hai cũng bị tính là "thẻ mới".
- **Không có dòng Sink cho túi.** Túi không nằm trong ví nào (mở ngay) nên không có số dư để trừ;
  ghi thêm vế Sink chỉ là bút toán hình thức, lại dễ bị đếm đôi.
- **`resourceWhen` của dòng túi là nguồn thật**, không phải `event_card_collection`. Đó là cách
  duy nhất trả lời "túi đến từ event nào". Thẻ thì ngược lại: luôn `event_card_collection`, và
  nguồn của nó tra sang dòng túi **cùng `exchangeId`**.

**Kiểm:** mở một túi ra 4 thẻ trong đó 1 thẻ trùng → đúng 6 dòng cùng `exchangeId`: 1 túi, 4 thẻ,
1 sao. Thẻ trùng có `valueBefore > 0`.

### [ ] A3 — Thẻ được tặng: dòng thẻ riêng, `where` nói ai tặng

```csharp
FalconBigDataController.Resource.OnEarned(new ResourceParam
{
    exchangeId    = ex,                   // một id cho một lần nhận (nhận nhiều thẻ thì chung)
    itemType      = "card",
    currency      = "card",
    itemId        = card.id,
    amount        = 1,
    valueBefore   = card.countBefore,
    valueAfter    = card.countBefore + 1,
    resourceWhen  = "event_card_collection",
    resourceWhere = "friend_gift",        // hoặc "teammate_gift"
    detail = new Dictionary<string, object>
    {
        // ⛔ L4 (hợp đồng live-op): BỎ dòng này — `live_ops_cycle_id` thay, và `Stamp` đã điền hộ.
        // ["card_col_season_id"] = seasonId,
        ["card_col_phase"]     = phase,
        ["card_col_set"]       = card.setIndex,
        ["card_col_tier"]      = card.tier,
        ["card_col_variant"]   = card.variant,
    },
});
```

⚠ **Không log vế "gửi thẻ đi"** — không chart nào hỏi, và phía nhận đã đủ để đo nguồn thẻ.

⚠ **Thẻ nhận LẺ, không bọc trong túi và không do bạn/đồng đội tặng** (ví dụ một event khác
thưởng thẳng một cái thẻ) thì **chưa có `resourceWhere` nào phủ**: A2 lo thẻ-từ-túi, A3 lo
friend/teammate. Có đường đó thì dùng dòng thẻ y như trên, nhưng **nguồn đặt vào `resourceWhen`**
— đúng luật đã dùng cho dòng túi ở A2:

```csharp
resourceWhen  = FResourceWhen.Event("master_league"),   // hoặc FResourceWhen.LevelReward…
resourceWhere = "direct_grant",                         // hình thức nhận
```

Vì sao không thêm một key nguồn trong `detail`: `resource_when` là **cột thật** nên lọc/break-by
được ngay, còn key trong `detail` nằm trong blob JSON (xem mục C); và dòng thẻ lẻ **không có dòng
túi nào cùng `exchangeId`** để tra nguồn sang, nên nguồn phải nằm ngay trên dòng đó. Một luật dùng
cho cả túi và thẻ lẻ: **nguồn ở ngoài event thì `resourceWhen` mang nguồn đó**. Không có đường tặng
thẻ lẻ thì bỏ qua mục này (câu D3).

### [ ] A4 — Đổi rương bằng sao: hai vế chung một `exchangeId`

Sao do module tự quản (Q2) nên vế trả đi phải gọi tay; **quà** thì đi qua kho như mọi tài nguyên
khác, chỉ cần truyền context xuống:

```csharp
var ex = Guid.NewGuid().ToString("N");

// Vế TRẢ ĐI — sao:
FalconBigDataController.Resource.OnSpent(new ResourceParam
{
    exchangeId    = ex,
    itemType      = "card_star",
    currency      = "card_star",
    itemId        = "card_star",
    amount        = starCost,
    valueBefore   = starBefore,
    valueAfter    = starBefore - starCost,
    resourceWhen  = "event_card_collection",
    resourceWhere = "star_chest",
    detail = new Dictionary<string, object>
    {
        // ⛔ L4 (hợp đồng live-op): BỎ dòng này — `live_ops_cycle_id` thay, và `Stamp` đã điền hộ.
        // ["card_col_season_id"] = seasonId,
        ["card_col_chest"]     = chestIndex,   // rương thứ mấy
    },
});

// Vế NHẬN VỀ — quà, đi qua kho kèm context (kho tự ghi valueBefore/valueAfter):
var ctx = new ResourceParam
{
    exchangeId    = ex,                    // CÙNG id với vế sao
    resourceWhen  = "event_card_collection",
    resourceWhere = "star_chest",
    detail = new Dictionary<string, object>
    {
        // ⛔ L4 (hợp đồng live-op): BỎ dòng này — `live_ops_cycle_id` thay, và `Stamp` đã điền hộ.
        // ["card_col_season_id"] = seasonId,
        ["card_col_chest"]     = chestIndex,
    },
};
ResourceCollector.Instance.ResourceAdd(resourceId, amount, data, "card_collection", param: ctx);
```

⚠ Quà từ rương mà **là túi thẻ** thì đi theo A2 (dòng túi với `resourceWhen = "event_card_collection"`,
`resourceWhere = "star_chest"`) chứ không qua kho — túi không có ví.

### [ ] A5 — Quà hoàn thành set / phase: mỗi set một `exchangeId`

```csharp
var ctx = new ResourceParam
{
    resourceWhen  = "event_card_collection",
    resourceWhere = "set_reward",             // hoặc "phase_reward"
    exchangeId    = Guid.NewGuid().ToString("N"),   // RIÊNG cho từng set / từng phase
    detail = new Dictionary<string, object>
    {
        // ⛔ L4 (hợp đồng live-op): BỎ dòng này — `live_ops_cycle_id` thay, và `Stamp` đã điền hộ.
        // ["card_col_season_id"] = seasonId,
        ["card_col_phase"]     = phase,
        ["card_col_set"]       = setIndex,        // bỏ key này ở dòng phase_reward
    },
};
// mỗi MÓN quà một dòng, đi qua kho kèm ctx — hoặc qua bộ phát thưởng của Events Core:
EventRewardGranter.Grant(reward, "card_collection", context: ctx);
```

- **Một món quà chỉ MỘT dòng.** Đã đi qua kho thì **không** gọi thêm
  `FalconBigDataController.Resource.OnEarned` — gọi thêm là đếm đôi (đúng lỗi của Master League).
- Xong một set mà quà có 3 món → 3 dòng, **cùng** `exchangeId`, **cùng** `detail.card_col_set`.
- Cần GameData ≥ 1.1.7 (và Events Core bản có `Grant(..., context)` nếu dùng dòng trên).

**Kiểm:** hoàn thành set 7 của phase 1 → có dòng `set_reward` với `detail.card_col_set = 7`,
`detail.card_col_phase = 1`, `resource_when = event_card_collection`.

### [~] A6 — Mã mùa đi theo mọi dòng — ⛔ **ĐÃ THAY bằng mục 🔁 (L1 + L4)**

> **Đừng làm theo mục này nữa.** Mã mùa giờ đi bằng **`live_ops_cycle_id`**: khai một lần qua
> `LiveOps.DeclareCycle(..., cycleId: seasonId, ...)` rồi `Stamp` điền hộ cho mọi dòng. **Giá trị vẫn
> là id do server cấp** (Q4 không đổi) — chỉ đổi chỗ đứng: từ `detail` (blob, **không lọc được**) sang
> **cột thật**. Luật *hoãn-gửi-khi-chưa-biết-mã* bên dưới **vẫn nguyên giá trị**.

~~`card_col_season_id` = id mùa do server cấp (Q4), gắn vào `detail` của **mọi** dòng event và vào
`cycleId` của funnel.~~ Mùa dài hai tháng nên không có mã mùa là **không cắt được mùa nào ra mùa
nào** khi mùa sau chạy.

⚠ Chưa biết mã mùa (data server về muộn) thì **giữ mốc lại rồi gửi khi có**; tới lúc hết cơ hội thì
gửi với mã **để trống**. Không được mất dữ liệu, cũng không được bịa mã (bài học Endless Treasure
E2).

### [ ] A7 — Pin phiên bản

Module Card Collection phụ thuộc `com.falcon.modules.core.bigdata` **≥ 1.3.9** và
`com.falcon.modules.core.gamedata` **≥ 1.1.7**.

⚠ **Đừng pin 1.4.x.** Mọi thứ doc này dùng đã có từ 1.3.8 (bộ meta chu kỳ `cycleIndex` /
`cycleStartTs` / `cycleEndTs` + luật trộn `detail`) và 1.3.9 là bản **đã publish**. 1.4.x là cụm
event quảng cáo, đang chờ loader seed mapping rule — pin vào đó là buộc Card Collection chờ một
việc chẳng liên quan.

## B. Những thứ KHÔNG log

| Không log | Vì sao |
|---|---|
| Số phase, số set mỗi phase, số thẻ mỗi set, độ dài mùa | Config. Phân bố trên chart tự phản ánh; đổi cân bằng thì log đứng yên |
| Trọng số gacha (`trọng số loại − số bản đang giữ`), nội dung pool | Luật. Suy lại được từ chuỗi dòng thẻ thật |
| Bộ đếm "chưa ra thẻ đỏ" (pity) | Không chart nào hỏi. Nó là trạng thái suy được từ lịch sử mở túi |
| Bảng thưởng set / phase / rương cho bao nhiêu | Dòng thưởng đã ghi `amount` thật |
| Tổng sao / tổng số set user đang có | Trạng thái. Đọc `valueAfter`, hoặc cộng dồn `amount` |
| Vế "gửi thẻ cho bạn" | Không chart nào hỏi; phía nhận đã đủ |
| Mở UI lần 2 trở đi, kéo-thả thẻ, popup, anim | Không chart nào dùng, volume thì lớn |
| Mốc funnel cho từng set / từng phase | Quà tự nhận và set nào cũng có quà (Q3) → dòng thưởng đã là mốc |

## C. Báo data team

- Funnel `event_card_collection`, shape `once_per_cycle`, action: `eligible` (10), `open` (15).
  `cycleId` = mã mùa server, kèm `cycleIndex` / `cycleStartTs` / `cycleEndTs`.
- `resourceWhen`: `event_card_collection` cho mọi dòng **trong** event. Dòng **nhận túi** mang
  `resourceWhen` của **nguồn**, dùng hằng sẵn có của `FResourceWhen`: `event_master_league`
  (`FResourceWhen.Event`), `level_reward` (quà xong chương), `gift`… Túi do **chính event này** phát
  (đổi bằng sao) thì nguồn vẫn là `event_card_collection`, phân biệt bằng `resourceWhere =
  star_chest`.
- `resourceWhere`: `pack_open`, `duplicate_card`, `friend_gift`, `teammate_gift`, `star_chest`,
  `set_reward`, `phase_reward` (+ `direct_grant` nếu có đường tặng thẻ lẻ — D3; dòng đó mang
  `resourceWhen` của **nguồn**, không phải `event_card_collection`).
- `currency` / `itemType`: `card_pack`, `card`, `card_star`. `itemId` = loại túi / mã thẻ /
  `card_star`.
- Key trong `detail`: `card_col_phase`, `card_col_set`, `card_col_tier`, `card_col_variant`,
  `card_col_pack_type`, `card_col_chest`.
  <br/>⛔ `card_col_season_id` **không còn** — mùa đọc ở **cột thật** `live_ops_cycle_id` (mục 🔁 L4).
- Bốn cột chung của mọi event live-op có trên các dòng này: `live_ops_event` (= `card_collection`) ·
  `live_ops_cycle_id` (mùa) · `live_ops_stage` (số set **cộng dồn**) · `live_ops_round_id` (event này
  không dùng).
- ⚠ **Quy ước đọc 1:** "hoàn thành set" đọc từ dòng `set_reward` (quà tự nhận nên đạt-set và
  nhận-quà là một). Hôm nào GD đổi sang **bấm nhận** thì phải báo data team — lúc đó ý nghĩa chart
  đổi mà số vẫn ra bình thường.
- ⚠ **Quy ước đọc 2:** "thẻ mới" = dòng thẻ có `value_before = 0`; "thẻ trùng" = `value_before > 0`.
  Không có key `is_new` riêng.
- ⚠ **Quy ước đọc 3:** nguồn của một **thẻ ra từ túi** nằm ở dòng **túi** cùng `exchange_id`, không
  nằm trên dòng thẻ. Ba trường hợp còn lại thì nguồn nằm ngay trên dòng thẻ: `friend_gift` /
  `teammate_gift` đọc `resource_where`, còn `direct_grant` đọc `resource_when`.
- ⚠ **Quy ước đọc 4:** **đừng chạy kiểm liên tục số dư** (`value_after` dòng này = `value_before`
  dòng sau) cho `currency = card`. Vế "gửi thẻ cho bạn" cố ý **không log** nên số dư thẻ sẽ có chỗ
  nhảy. Phép đọc "mới vs trùng" bằng `value_before = 0` vẫn đúng. Số dư **sao** thì liên tục, kiểm
  được.
- ⚠ **Quy ước đọc 5:** "số set đã xong" đếm distinct **cặp `(card_col_phase, card_col_set)`**, không
  phải `card_col_set` đơn lẻ. Đúng cả khi set index chạy 1..30 cả mùa lẫn khi nó reset về 1 ở
  phase 2 — khỏi phải chờ dev trả lời mới dựng được chart.
- ⚠ **Quy ước đọc 6:** "sao kiếm cả mùa" cộng **mọi** dòng Source `currency = card_star`, group theo
  `resource_where`, **không** lọc cứng `duplicate_card`. Đúng cả khi sao chỉ sinh từ thẻ trùng lẫn
  khi về sau sao thành một món quà của set/phase/event khác.
- ⚠ **Quy ước đọc 7:** **mọi phân vị tính trên TOÀN tập `eligible` của mùa, user không có dòng thì
  điền 0** — số set đã xong, sao đang giữ, thẻ theo nguồn, túi theo nguồn, tất cả. Chỉ lấy phân vị
  trên user *có* dòng thì p10–p50 đẹp giả trong khi phần lớn người đủ điều kiện đang là 0. Đây đúng
  nguyên lý neo-tập-gốc của retention, và nó áp cho cả chart số-set-hoàn-thành chứ không riêng sao.
- ⚠ **Quy ước đọc 8:** dùng `quantileExact(p)(x)`, **không** `quantile` / `median`. Hai cái sau lấy
  mẫu nên chạy hai lần ra hai số, probe hồi quy không bao giờ sạch.
- ⚠ **Hình dạng dữ liệu phía kho — và vì sao nó như vậy** (đo trên bee/knit 12/10). Kho đang có
  **hai đời hệ thống**:

  | | Đời cũ | Đời mới |
  |---|---|---|
  | Tham số gửi lên | **Mọi** tham số đều được cột-hoá | Chỉ **tham số chính (hợp đồng)** thành cột |
  | Tham số riêng của event | Cũng thành cột | Đẩy vào **blob JSON** (`detail` ở dòng tài nguyên, `event_extra_props` ở log khác) |
  | Đổi lại | Tiện cho người vẽ chart: cái gì cũng là cột | Thêm event mới **không đổi schema**, nhẹ hơn ở mức toàn hệ |
  | Giá phải trả | Mỗi event mới đẻ thêm cột → bảng phình theo số event | Tham số phụ phải bóc bằng `JSONExtract*` |

  Nên `card_col_*` nằm trong blob là **đúng thiết kế của đời mới, không phải thiếu sót**:
  `detail` xuống kho là **một cột `Nullable(String)` chứa JSON**, đọc bằng
  `JSONExtractInt(detail,'card_col_set')` / `JSONExtractString(detail,'card_col_variant')`.

  ⏸ **Đã cân nhắc và HOÃN (chốt owner 20/10): không promote độ hiếm thành cột.** `card_col_tier` /
  `card_col_variant` ở blob nên nhóm theo độ hiếm phải `JSONExtract` trên **stream dày nhất của event
  này** (mỗi túi mở ra n dòng thẻ) — biết là đắt. Vẫn không promote, vì key **riêng của một event**
  lên cột là bùng nổ số cột cho cả hệ. Nếu sau này nhu cầu to thật thì hướng hợp luật **không phải**
  thêm `card_col_tier`, mà là xét *"độ hiếm"* thành **cột fleet-wide** (khuôn `live_ops_*`) dùng cho
  mọi game có thẻ/đồ theo rarity. Chưa làm — ghi lại để khỏi bàn lại từ đầu.

  ⚠ **Đừng xin loader tách `card_col_*` thành cột riêng** (chốt owner 12/10): mỗi event xin ba cột
  thì số cột bùng nổ — đúng cái mà đời mới sinh ra để tránh. Hệ quả cho chart: `card_col_*` **không
  dùng được làm break-by / dropdown filter của widget thường** (cổng pushdown chỉ cho cột thật
  qua), nên ba cái "Filter" GD đòi — theo mùa, theo bậc sao, theo loại túi — làm bằng **tham số
  chart pipeline** (`{{season_id}}`, `{{tier}}`, `{{pack_type}}`). Không cần dev đổi gì, chỉ là
  data team phải biết trước để dựng đúng kiểu chart.

  ⚠ **Đừng lấy `league_id` của Master League làm kỳ vọng.** Key đó là cột thật vì nó lên dữ liệu
  từ **đời cũ** (bee.flow), thời mọi tham số còn được cột-hoá — không phải vì nó phẳng hay vì ai
  seed rule cho nó. Event mới chạy trên đời mới thì extras **luôn** vào blob.

  ⚠ **Hệ quả cho cách thiết kế log** (đã áp trong doc này): chiều lọc nào **quan trọng và lọc
  nhiều** thì đặt lên **cột hợp đồng sẵn có** thay vì nhét vào blob — nguồn tài nguyên vào
  `resourceWhen`, hình thức nhận vào `resourceWhere`, loại túi vào `itemId` của dòng túi, mã thẻ
  vào `itemId` của dòng thẻ. Blob chỉ chở thứ bổ nghĩa (`card_col_set`, `card_col_tier`,
  `card_col_variant`, `card_col_phase`…). Đây là lý do thẻ nhận lẻ ở A3 mang nguồn trên
  `resourceWhen` chứ không mang một key nguồn riêng trong `detail`.
- ⚠ **`exchange_id` có cột thật** trên `f_sdk_resource_data` của bee (android/ios), knit
  (android/ios), bus_flow (android) và đang được Master League / Race điền thật — đường đi thông
  suốt. **Không** có cột ở `knit_*_other` và bus_flow ios; Card Collection chạy trên hai chỗ đó thì
  báo data team trước. Lưu ý: cái đã chứng minh là *cột chở được một id dùng chung cho nhiều dòng*;
  cụm 6 dòng của A2 thì **phép kiểm trong A2 mới là bằng chứng**, đừng coi là đã xong.

## D. Năm câu cần dev trả lời (ảnh hưởng trực tiếp đoạn code trên)

| # | Câu hỏi | Vì sao cần |
|---|---|---|
| D1 | Khi event khác phát túi thì nó **gọi hàm nào** của Card Collection, và hàm đó (hoặc đường thưởng của chính nó) **đã log dòng tài nguyên** cho túi chưa? | Quyết định Bước 0 của A2. Chưa có cửa nhận-túi-kèm-nguồn thì chart "thẻ đến từ event nào" rỗng; cả hai bên cùng log thì đếm đôi |
| D2 | Thẻ trùng **nằm lại album** hay **biến thành sao ngay**? | Nếu biến thành sao ngay thì `valueAfter = countBefore + 1` ở A2 là sai — phải là `countBefore` (số bản không tăng), và "trùng" đọc bằng `value_before > 0` vẫn đúng |
| D3 | Có đường nào **nhận thẻ lẻ, không bọc trong túi và không do bạn/đồng đội tặng** không? | A2 phủ thẻ-từ-túi, A3 phủ friend/teammate. Còn đường thứ ba thì nó cần `resourceWhere = "direct_grant"` và `resourceWhen` = **nguồn thật** (xem ⚠ cuối A3) — thiếu thì thẻ đó mất khỏi chart nguồn mà không có dấu hiệu gì |
| D4 | **Điều kiện unlock Card Collection là gì** — level bao nhiêu, xong chương nào, hay cờ server? | Chart "session time / active day / paying rate **trước** vs **sau** launch" cần cùng một tập user, nhưng `eligible` chỉ bắn khi mùa **đang chạy** → trước ngày bật mùa không dòng log nào nói ai đủ điều kiện. Biết ngưỡng thì data team dựng nhóm lịch sử bằng `current_level`; không biết thì phép so đó không có nhóm (và A/B holdout vẫn là cách chắc hơn — xem ⚠ dưới bảng chart) |
| D5 | **Thẻ vàng / kim cương** là một **biến thể** tách rời bậc sao (thẻ vàng vẫn có 1..5 sao), hay chính nó là một giá trị của bậc sao? | Quyết định `card_col_variant` ở A2/A3 có cần hay không. GD đòi hai chart riêng ("thẻ 1-5 sao" và "thẻ vàng/kim cương") nên mặc định doc này coi là hai trục; nếu game chỉ có một trục thì bỏ key và nói lại với data team |

---

# 📖 Chart đọc từ đâu

| Chart | Cách tính |
|---|---|
| **Phân vị số set hoàn thành / user theo ngày, lọc theo mùa** | Dòng `set_reward`: mỗi user × mùa, đếm distinct **cặp `(card_col_phase, card_col_set)`** (quy ước đọc 5) tích tới ngày đó → phân vị 10/25/50/75/90/95/max bằng `quantileExact`. **Tập tính phân vị** = toàn bộ user có `eligible` trong mùa, ai chưa xong set nào **điền 0** (quy ước đọc 7) — không phải chỉ user có dòng `set_reward` |
| **%User/DAU đã unlock** | Distinct user có `eligible` trong mùa / DAU |
| **Source quà set / rương / phase, so với tổng và với event khác** | `SUM(amount)` Source rows `resource_when = event_card_collection`, group `resource_where` ∈ {`set_reward`, `star_chest`, `phase_reward`}. So với tổng Source toàn game cùng ngày, và với `resource_when` của event khác |
| **Sao đang tích trữ (phân vị) vs sao kiếm cả mùa** | Đang giữ = `value_after` của dòng sao gần nhất **có `created_date` ≤ hết ngày đó** (as-of), **không** phải "dòng mới nhất trong ngày" — user không mở túi hôm đó vẫn đang giữ sao, bỏ họ ra là phân vị lệch lên. Cả mùa = `SUM(amount)` mọi dòng Source `currency = card_star` group `resource_where` (quy ước đọc 6). Cả hai phân vị tính trên tập `eligible` điền 0 |
| **Thẻ theo nguồn, lọc theo bậc sao / biến thể** | Dòng thẻ group `detail.card_col_tier` (và `detail.card_col_variant`) × nguồn: `friend_gift` / `teammate_gift` đọc thẳng `resource_where`, `direct_grant` đọc `resource_when` của chính dòng thẻ; thẻ từ túi thì JOIN sang dòng túi cùng `exchange_id` lấy `resource_when` (event nào / chương nào). Phân vị số thẻ/user theo nguồn: tập `eligible` điền 0 |
| **Thẻ mới vs trùng (số và %)** | Dòng thẻ: `value_before = 0` là mới. Lọc thêm theo `card_col_tier` (1..5 sao) và `card_col_variant` (vàng / kim cương) — **hai trục riêng**, GD đòi cả hai |
| **Túi theo nguồn và theo loại túi** | Dòng túi: group `resource_when` (nguồn) × `item_id` (loại túi). Cắt mùa bằng cột `live_ops_cycle_id` (không còn `detail.card_col_season_id`) |
| **Session time / active days / session count / paying rate trước–sau launch** | **Không cần log gì thêm** — stream phiên và IAP đã có. Cần ngày bật mùa, **và ngưỡng unlock (D4)**: `eligible` chỉ bắn khi mùa đang chạy nên không dòng log nào dựng được tập "đủ điều kiện" ở giai đoạn *trước* launch — phải suy bằng `current_level` theo ngưỡng đó |

⚠ **Phép so "trước–sau launch" là phép so yếu**, không phải vì thiếu log: tính năng bật cho mọi
user đủ điều kiện nên không có nhóm đối chứng, số lẫn với mọi thay đổi khác cùng thời điểm. Muốn
kết luận chắc thì **A/B holdout** (tắt Card Collection cho 10–20% user đủ điều kiện, dùng nhãn AB
sẵn có trên mọi log).

## Bốn lo ngại của GD, đối chiếu với thiết kế

| Lo ngại | Log nào trả lời |
|---|---|
| Chưa rõ user có hào hứng không | Khoảng hụt `eligible` → `open` → phân vị số set hoàn thành. Cả ba đều có trong thiết kế này |
| Mùa dài hai tháng, sợ lạm phát tài nguyên | Toàn bộ Source `resource_when = event_card_collection`, tách theo ba `where` thưởng, so với tổng phát ra toàn game theo ngày |
| Tính năng lớn, sợ bug / xung đột với hệ thống khác | **Không log thêm.** Việc này thuộc QA và stream crash/ANR, không phải việc của tracking |
| Thẻ quá khó hoàn thành | % thẻ trùng theo bậc sao, phân vị số set xong, và `card_col_set` nào tắc ở dòng `set_reward` |

# 📖 Vì sao thiết kế như vậy

| Quyết định | Lý do |
|---|---|
| Chỉ 2 mốc funnel | Mọi mốc tiến độ khác **đã có dòng thưởng** (quà tự nhận, set nào cũng có quà). Hai mốc còn lại là hai khoảnh khắc **không dòng nào khác ghi được**: "đủ điều kiện" và "lần đầu mở UI" |
| Định danh đặt ở `currency` / `itemId`, ngữ cảnh ở `detail` | `ResourceLog.LogAdd` của GameData đổ `resourceId` vào `currency` và điền `itemType` bằng `where` — `itemType` là **cột legacy** (chốt owner GameData). Đặt mã thẻ vào đó là mất số |
| `valueBefore` / `valueAfter` thay cho key `is_new` | Số dư là cột chuẩn của dòng tài nguyên, kho tự ghi cho mọi tài nguyên khác. Thêm key riêng là ghi hai lần cùng một sự thật, và hai chỗ thì sẽ có ngày lệch nhau |
| Một `exchangeId` cho cả cụm mở túi | Thứ **duy nhất** nối "túi nào ra thẻ gì, sinh mấy sao". Không có nó thì bốn dòng tài nguyên lồng nhau thành bốn stream rời, không ai ghép lại được |
| Không có dòng Sink cho túi | Túi mở ngay (Q1), không nằm trong ví nào — không có số dư để trừ. Ghi thêm vế Sink là bút toán hình thức, lại mời gọi đếm đôi |
| `resourceWhen` của dòng túi là **nguồn**, của dòng thẻ là **event** | Hai câu hỏi khác nhau: "túi đến từ đâu" (nguồn) và "thẻ phát ra bởi event nào" (lạm phát). Nhét cùng một giá trị thì mất một câu |
| Log `card_col_tier` + `card_col_variant` dù đó là thuộc tính catalog | Bình thường **không log config**. Nhưng không ai dựng bảng danh mục thẻ (đúng vết xe bảng lịch mùa của Master League) mà chart lại đòi lọc theo bậc sao **và** theo vàng/kim cương — hai trục khác nhau, GD đòi hai chart riêng — nên gắn cả hai lên dòng log, chấp nhận trùng lặp. Không có `variant` thì chart "mới vs trùng của thẻ vàng" chỉ còn đường suy từ `item_id`, tức là phải có bảng danh mục |
| Chiều lọc quan trọng đặt lên **cột hợp đồng**, không nhét vào blob | Kho đời mới chỉ cột-hoá tham số hợp đồng, extras vào blob JSON (mục C) — và **không xin thêm cột cho event** vì mỗi event ba cột là bùng nổ số cột (chốt owner 12/10). Nên nguồn → `resourceWhen`, hình thức nhận → `resourceWhere`, loại túi / mã thẻ → `itemId`; blob chỉ chở thứ bổ nghĩa |
| Tiền tố `card_col_` | Luật tiền tố live-op (`LiveOpsTrackingPattern.md` §1): `detail` trải phẳng theo PutIfAbsent (ai-ghi-trước-giữ), key chung chung như `set` / `tier` là event sau ghi đè event trước, **mất số im lặng** |
| Không log pity, không log vế gửi thẻ | Không chart nào hỏi. Mốc thừa thì vẫn phải bảo trì, vẫn chiếm volume (owner đã cắt đúng loại này ở Adventure) |

## So với các event đã làm

| | Card Collection | Adventure / Sky Path | Endless Treasure |
|---|---|---|---|
| Chu kỳ | **Mùa 2 tháng** | Tuần / đợt | Tuần |
| Chơi trên | Không gắn với ván chơi | Level thường (extras trên log level) | Không |
| Dòng tài nguyên | **4 tầng lồng nhau** | 1 tầng (quà mốc) | 2 tầng (bậc → quà) |
| Funnel | `eligible` + `open` | Không (Adventure) / Không (Sky Path) | `open` |
| Điểm khó nhất | Nối thẻ về nguồn qua `exchangeId` | Không bịa mã đợt | Không bịa mã đợt |

⚠ Card Collection là event **đầu tiên** có tài nguyên **không đi qua kho** (Q2). Nghĩa là mọi
`valueBefore` / `valueAfter` của thẻ và sao do **module tự điền** — không có kho đỡ lưng. Sai số dư
ở đây thì chart "sao đang tích trữ" và "mới vs trùng" sai theo mà **không có dấu hiệu gì**: dòng
log vẫn đủ, vẫn đúng định dạng. Nên QA phải soát đúng hai số này trước khi ship.

## Hai chỗ spec GD cần chốt (không chặn log)

| # | Chỗ chưa khớp | Vì sao không chặn |
|---|---|---|
| 1 | Thẻ vàng có 8 bản trong pool, nhưng set cuối cần 7 thẻ vàng **khác nhau** — hai con số này không khớp nhau | Log ghi số thật của từng lần nhận, GD chốt kiểu nào thì log vẫn đứng yên |
| 2 | Chưa rõ thẻ Phase 1 còn tác dụng gì ở Phase 2 | `card_col_phase` đã có trên mọi dòng, nên dù GD chốt kiểu nào vẫn tách được |
