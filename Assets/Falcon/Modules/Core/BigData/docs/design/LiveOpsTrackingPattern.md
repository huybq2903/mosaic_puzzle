# Khuôn tracking LIVE-OP — form mẫu cho mọi event

> **Viết lại 20/10/2026 cho dễ đọc** — nội dung không đổi, không bỏ luật nào, và **giữ nguyên số mục
> §0–§6** vì `LiveOpsEventContract.md` trỏ ngược về "Pattern §2a / §3 / §5".
> Bản nội dung 14/10/2026 theo `LiveOpsEventContract.md` (entity `LiveOps`, log
> `f_sdk_live_ops_data`). Bản 17/09 dựng trên funnel `event_*` — vẫn đúng cho **dữ liệu cũ**, nhưng
> **event mới đi hợp đồng này**.

---

# 📄 Đọc 2 phút (phần cho người duyệt)

**Vấn đề.** Sáu event live-op (Master League · Race · Adventure · Sky Path · Endless Treasure · Card
Collection) mỗi event tự đặt tên và tự chọn cách ghi log cho **cùng một thứ**. Hậu quả đã xảy ra, không
phải lo xa: mỗi event mới phải xin loader đặt tên, visualizer dựng dashboard lại từ đầu, và vài event
đã đo sai số liệu mà **nhìn vẫn hợp lý** (danh sách vết sẹo ở §3).

**Khuôn này là gì.** Một **form điền**: mỗi event mới chỉ khai *khoá event · đợt · lượt · mốc · tiền tố
key*, rồi gọi vài hàm có sẵn. Không phải thiết kế log nữa.

**Nó mua được gì.**

| | |
|---|---|
| Dev | học **một lần**, event sau là điền phiếu |
| Data team / visualizer | dựng **một** bộ dashboard dùng cho mọi event (công thức ở §5) |
| Event mới | **0 việc với loader** — danh tính event nằm ở *giá trị* của cột, không nằm ở *tên log* |

**Giá phải trả.** Mỗi event phải gọi tối thiểu 4 thứ (§6 tầng "Tối thiểu"). Dưới mức đó thì event
**vô hình** trong catalog — không ai dựng được chart cho nó.

**Hai nguyên tắc chi phối mọi luật bên dưới** (đọc hai cái này là hiểu 80% doc):

1. **Ghi KHOẢNH KHẮC, đừng ghi TRẠNG THÁI.** Client gửi *"vừa qua mốc 3"*; đừng gửi *"đang tham gia"*
   hay *"đã xong"*. Trạng thái là thứ server **tự suy** từ các khoảnh khắc. Lý do: suy sai thì sửa
   bằng một câu SQL, còn gửi sai trạng thái lên wire là **sai vĩnh viễn, không dấu vết**. Cần một cột
   tiện `GROUP BY` thì **xin kho tạo cột enrichment**, đừng đẻ param `eventXxx*` trên wire.
2. **Mỗi dòng log tự khai nó thuộc event / đợt / mốc nào.** SDK **không bao giờ** tự suy *"lúc đó đợt
   nào đang chạy"* — vì **đồng thời ≠ nguyên nhân**, và vì nhiều event chạy song song.

**Từ vựng** (dùng xuyên suốt):

| Từ | Nghĩa | Ví dụ |
|---|---|---|
| **khoá event** | tên event, để trần | `master_league`, `race` |
| **đợt** (`cycleId`) | một lần chạy **có lịch** mở/đóng | mùa 5, tuần 2026-10-20 |
| **lượt** (`roundId`) | chơi lại nhiều lần **trong** một đợt | một bảng đấu của Race |
| **mốc** (`stage`) | thang tiến độ bằng **số**, cộng dồn | chặng 1 → 2 → 3 |

> **Sếp có thể dừng ở đây.** Phần dưới là chi tiết cho dev và data team.

---

## 0. Phân loại mốc TRƯỚC KHI viết doc — TRẠNG THÁI vs KHOẢNH KHẮC

Đây là bài học đắt nhất, của Master League: mốc `eligible` bị trói vào **khoảnh khắc vừa xong chapter**.
Hậu quả: người chơi kỳ cựu (xong chapter từ lâu) **vô hình**, nhóm đối chứng sụp, và dev thì *làm đúng
doc nhưng số vẫn sai*.

| Loại mốc | Định nghĩa | Luật bắn |
|---|---|---|
| **KHOẢNH KHẮC** | Một hành động tại một thời điểm | Bắn tại chỗ, một lần. *Bấm Start · thắng chặng · nhận thưởng* |
| **TRẠNG THÁI theo chu kỳ** | Một điều kiện **đúng trong suốt** chu kỳ | Bắn **MỖI PHIÊN** khi điều kiện đang đúng — **kể cả đã đúng từ lâu**. Van lọc của SDK tự nén về một dòng mỗi đợt. *Đủ tư cách · đã unlock* |

Mỗi mốc trong phiếu điền **phải khai nó thuộc loại nào**.

> **Câu hỏi tự kiểm, dùng một lần cho mỗi mốc:**
> *"User đạt điều kiện từ THÁNG TRƯỚC thì đợt này có dòng log không?"*
> Trả lời "không" = đang trói trạng thái vào khoảnh khắc = đang lặp lại lỗi Master League.

## 1. Khai báo event — mục 0 của mọi `Request-Game`

Năm dòng khai báo, và **lý do** của từng dòng (lý do mới là phần quan trọng — nó giải thích vì sao
không được làm kiểu khác):

| Khai gì | Viết thế nào | Vì sao phải vậy |
|---|---|---|
| **Khoá event** | tên trần: `card_collection`, `master_league`, `race` | **Không** tiền tố `event_` (cột đã tên `live_ops_*`). **Không** đăng ký với loader — danh tính nằm ở **giá trị**, không ở tên log. Nhờ vậy **event mới = 0 việc với loader** |
| **Đợt** (`cycleId`) | mùa? tuần? ngày? | Phải là thứ **CÓ LỊCH** (mở/đóng). Event cho chơi nhiều lượt trong một đợt thì **đợt vẫn là đợt** — lượt đi `roundId`. Nhét lượt vào `cycleId` là **vết sẹo Race**: vòng 2 bị van lọc nuốt |
| **Lượt** (`roundId`) | có / không | Chỉ event chơi lại nhiều lượt trong đợt (bảng đấu Race). Để trống = đợt chỉ có một lượt |
| **Mốc** (`stage`) | thang **số**, cộng dồn 1..N | Trong đợt (hoặc trong lượt nếu có lượt). Thiết kế lồng hai tầng (phase × set của Card Collection) thì **làm phẳng**: phase 1 set 7 = **7**, phase 2 set 7 = **22**. Dùng chữ (`"p1_s07"`) là **mất sort và mất phân vị** |
| **Tiền tố key** | `<prefix>_` | Cho **mọi** key extras/detail **riêng** của event (điểm, rank, bậc sao…). Vì extras trải **phẳng** theo luật *ai-ghi-trước-giữ*: hai event cùng gắn `stage` lên một ván level thường thì event sau **mất số IM LẶNG**; cùng tên khác kiểu thì **hỏng cột kho**. ⚠ **Không** áp tiền tố cho field chuẩn SDK, và **không** áp cho giá trị |

Hai việc kèm theo:

- **Khai đợt**: `LiveOps.DeclareCycle(khoá, cycleId, cycleIndex, cycleStartTs, cycleEndTs)` — gọi **khi
  data đợt về, TRƯỚC bước đầu tiên**. Không có `Register`, không có khai shape.
- **Catalog**: **không phải báo tên event với ai**. Chỉ báo data team bốn thứ: khoá event · ý nghĩa
  thang mốc · vocab `resourceWhen`/`resourceWhere` · các key extras riêng.

## 2. Form: bốn câu hỏi × bốn loại log

Bốn mục dưới đây trả lời bốn câu hỏi khác nhau. Một event có thể chỉ cần mục đầu.

### 2a. *Ai vào, đi tới đâu, ai về đích?* → `LiveOps.OnStep`

**Đây là bản gốc định nghĩa sáu `action`** — hợp đồng trỏ về đây.

| Mốc | `action` | Loại (§0) | Mang `stage`? | Ghi chú |
|---|---|---|---|---|
| Đủ tư cách | `Eligible` | **TRẠNG THÁI** | — | Bắn **MỖI PHIÊN** khi còn đủ điều kiện và đợt đang chạy. Thường **GAME** gọi, vì module không biết điều kiện → **ghi rõ chủ gọi trong phiếu** |
| Mở UI event | `Open` | khoảnh khắc (lặp — van nén về 1 dòng/đợt) | — | |
| Tham gia | `Join` | khoảnh khắc | — | Event tự động (không có nút tham gia) thì **bỏ bước này**, đừng bịa |
| Đạt mốc | `Milestone` | khoảnh khắc | ✅ **số mốc** | |
| Về đích | `Complete` | khoảnh khắc | — | extras: `<prefix>_rank` nếu có |
| Nhận thưởng mốc | `Claim` | khoảnh khắc | ✅ **mốc của quà đó** | Chỉ khi thưởng **bấm nhận**. Thưởng tự nhận thì bỏ — đọc từ dòng tài nguyên |

⚠ **Ba mốc đầu là BA MẪU SỐ KHÁC NHAU — đừng gộp một rổ.** Mỗi tỷ lệ trả lời một câu khác nhau:

| Câu hỏi | Tỷ lệ |
|---|---|
| Event có đáng mở không? | `open` / `eligible` |
| UI có thuyết phục không? | `join` / `open` |
| Thiết kế bên trong có gắt không? | `milestone` / `join` |

Mỗi chart **phải ghi tên mẫu số lên nhãn**. Phép tự kiểm: mỗi `(user, đợt)` phải thoả
`eligible ≥ open ≥ join`.

⚠ **Chỉ mốc mới mang `stage`.** Bước vòng đời không mang, vì `action` đã đủ định danh. Bản funnel cũ
gánh hai vai bằng `priority`, và hậu quả **đo được** trên bee: Race dùng `priority = 3` cho **cả**
`eligible` lẫn `milestone(3)` → hai thứ khác nhau nằm chung một số.

Cần số **ĐẾM** exposure (tổng impression theo ngày, CTR)? → **không** đi qua mốc (mốc không phải máy
đếm). Xem **2d**.

### 2b. *Từng ván chơi thế nào?* → Level thường + (có điều kiện) khai event

Ván trong event vẫn là `Level.OnStart/OnPass/OnFail` như mọi ván. Thêm hai việc:

**(1) Khai event lên dòng level — CHỈ KHI ĐỘC QUYỀN.**

```csharp
FalconBigDataController.LiveOps.Stamp(levelParam, "race", stage: stageIndex);
```

| Tình huống | Khai? |
|---|---|
| Event có **bộ level riêng** (Master League) | ✅ |
| Event chạy trên level thường nhưng là **event duy nhất** đang chạy ở đó | ✅ |
| Event chạy **chồng** lên level thường, có thể cùng lúc event khác (Adventure + Sky Path + Race) | ❌ **ĐỪNG** — một ván thuộc nhiều event thì khai là **bịa**. Giữ nhãn riêng `<prefix>_cycle_id` trên ván |

Và nếu khai thì **khai hết**: gắn cho một nửa số ván làm bộ đếm riêng của event **hụt mà vẫn trông hợp
lý**. Bất biến kiểm chất lượng: `live_ops_level_attempts_* ≤ level_attempts_*`.

**(2) Extras chỉ chở số mà game PHẢI đúng để chạy được event** — điểm, chuỗi thắng, vị trí trong chặng,
số xe… Lý do chọn tiêu chí này: sai nó thì event sai, nên **gameplay chính là bộ test** cho số đó.

⚠ **ĐỪNG tự đếm "lần thử"** (`<prefix>_attempt` của bản cũ). SDK đã đếm **ba thước đo** trên dòng mốc:
`play_sec_*` (giờ chơi) · `level_passes_*` (level vượt qua) · `level_attempts_*` (lượt chơi mở ra). Nên
*"số lần thử qua mốc N"* = `level_attempts_since_prev_step` trên dòng `milestone(N)`. Vì mỗi game định
nghĩa "attempt" một kiểu, để SDK đếm **một** thứ giống nhau cho mọi game.

### 2c. *Tiền vào ra vì event?* → Resource + `Stamp`

```csharp
var ctx = new ResourceParam { resourceWhen = "event_<tên>", resourceWhere = "<nguồn>" };
FalconBigDataController.LiveOps.Stamp(ctx, "<khoá event>", stage: milestoneIndex,
                                      roundId: myRoundId);   // event có lượt thì PHẢI truyền
```

🔴 **Mã lượt do game KHAI, `Stamp` không tra hộ** *(sửa 05/11)*. Trước đó SDK tự điền bằng "lượt cuối
nó tình cờ thấy trên một bước nào đó" — một cái **đoán**, và nó vừa gán sai lượt cho dòng của đợt cũ,
vừa khiến game **không có cách nào** khai "dòng này không thuộc lượt nào" (vì `??=` coi `null` là ô
trống). Nay **không truyền = dòng không có lượt**, đúng ý. Mã **đợt** thì `Stamp` vẫn điền, vì
`DeclareCycle` là **game đã nói** (dòng nhận muộn vẫn phải khai đợt cũ bằng tay).

⚠ **`LiveOps.CurrentRoundId(event)` KHÔNG phải đường tắt.** Nó trả **lịch sử log** — lượt của bước
cuối cùng đã log trong đợt đang chạy — nên nó sai ở đúng hai ca hay cần nó nhất: dòng **nhận muộn**
(trả lượt của đợt đang chạy) và **sau khi đổi thực thể** khi lượt mới chưa log bước nào (còn trả lượt
cũ). Đọc nó rồi truyền vào `Stamp` **không** biến cái đoán thành lời khai. Lượt là thứ game đang giữ
trong state của mình — truyền thẳng cái đó.

Bốn luật chung:

- `amount` = số **THỰC nhận** (đã nhân x2/boost) — server không biết ai đang có boost.
- Các vế của **cùng một mẻ claim** chung một `exchangeId`.
- **Một giao dịch = MỘT đường log** (vết sẹo log-đôi rank reward của Master League: DWH đếm đôi tiền).
- `valueBefore`/`valueAfter` điền khi tài nguyên có ví.

⚠ **Mã đợt KHÔNG nhét vào `detail` nữa.** `live_ops_cycle_id` là **cột thật**; nhét vào `detail` là rơi
vào blob JSON và **không lọc được** (đo 13/10). `stage` cũng vậy.

**Hai chế độ tích hợp** — chọn theo chỗ đứng của code grant:

| Chế độ | Khi nào | Làm gì |
|---|---|---|
| **A — event tự cộng sổ** | code grant nằm trong event | Gọi thẳng `Resource.OnEarned/OnSpent(ResourceParam)` với đầy đủ when/where/detail/`exchangeId` + `Stamp` |
| **B — grant qua hệ chung** | `EventRewardGranter`/`ResourceCollector` (hệ này **tự log một dòng**) | **KHÔNG** log thêm dòng thứ hai. Đường đúng: **context đi CÙNG lời gọi** — dựng `ResourceParam` (đã `Stamp`) rồi truyền vào tham số `context` của `Grant(...)` |

> ⚠ **Phương án AMBIENT SCOPE đã bị BÁC (05/09)** — tức *"bọc grant trong `using`, decorator dán ngữ
> cảnh lên mọi log đi qua trong scope"*. Lý do: `GameEvent.Emit` là **đồng bộ** và listener của
> `ResourceCollector` bắn **TRƯỚC** khi log, nên một lời grant kích cascade (piggy bank, quest tự
> thưởng) và **log của bên khác lọt vào scope**, bị dán nhầm nguyên nhân. Scope thời-gian dù ngắn vẫn
> là stamp thời-gian — **ĐỒNG THỜI ≠ NGUYÊN NHÂN**, không có ngoại lệ "cửa sổ ngắn".

Chi tiêu (sink) trong ván event: log như thường, `playTurnId` tự nối. **Cấm** `Stamp` lên chi tiêu shop
chung (cùng lý do trên). Ngoại lệ duy nhất: gói bày **trong UI event** → placement của Iap đặt theo
event.

⚠ **Trả tài nguyên TRƯỚC khi gọi tổng kết level.** `Level.OnPass/OnFail` **đóng lượt ngay**, nên dòng
thưởng bắn sau đó **mất `playTurnId`**. Grant xong rồi mới `OnPass`.

### 2d. *Hiện ra bao nhiêu lần, bán được gì?* → Ui / Offer / Iap / Ad + `Stamp`

```csharp
var liveOps = new LiveOpsRef();
FalconBigDataController.LiveOps.Stamp(liveOps, "<khoá event>");
FalconBigDataController.Ui.OnImpression("<surface>", liveOps: liveOps);   // cụm, flush lúc pause
```

- **Ui**: exposure per-lần-hiện **không có đường lên wire** — van gộp cụm là đường **duy nhất** (vết sẹo
  volume: 5–30 lần/user/ngày × số surface × DAU). Bốn giá trị `live_ops_*` nằm **trong khoá gộp**, nên
  hai đợt không bị nén vào một dòng.
- **Offer / Iap**: `Stamp` cả `IapOfferParam` **và cả hai** log mua (`start_purchase` + `in_app`) —
  thiếu một đầu là phễu mua hụt.
- **Ad**: `adWhen` = khoá event khi ad nổ **VÌ** event, kèm `Stamp`. Ad xem để bù lượt / nhận quà event
  là **của** event; ad giữa ván chơi bình thường thì **không**.

## 3. Bảng ĐỪNG-LÀM — mỗi dòng là một vết sẹo thật

> Đọc bảng này như *"những cách đã thử và đã trả giá"*. Cột phải là nơi đã mất tiền hoặc mất số liệu.

| Đừng | Vết sẹo gốc |
|---|---|
| Dùng funnel `event_*` cho event mới | Mỗi event mới phải xin loader đặt tên → dashboard không bao giờ tự động được. Hợp đồng mới: danh tính ở **giá trị** |
| State-param `eventXxxJoining/Finished…` trên wire | Đề xuất Sky Path — derive được, sai im lặng, vocab nở theo số event |
| Nhét mã đợt / mốc vào `detail` | Blob JSON không lọc được (đo 13/10) — dùng `live_ops_cycle_id` / `live_ops_stage` |
| `cycleId` = id lượt khi event cho chơi nhiều lượt | Vòng 2 bị van nuốt (Race) — giờ có `roundId` riêng |
| Tự đếm `<prefix>_attempt` | Mỗi game một định nghĩa; SDK đếm `level_attempts_*` giống nhau cho mọi game |
| Khai event lên ván của event chạy **chồng** | Một ván thuộc nhiều event → khai là chọn bừa một cái, tức bịa |
| Khai event lên ván nhưng chỉ **một nửa** số ván | Bộ đếm riêng hụt mà vẫn trông hợp lý |
| Gộp `milestone` với `claim` thành một action | Mất phân biệt "đạt mốc" vs "đã nhận quà" (vết sẹo Adventure/Card, và Daily Challenge đang mắc) |
| `stage` dạng chữ (`"p1_s07"`) | Mất sort, mất phân vị — họ bài học `season_9 > season_10` |
| Mốc vòng đời cũng mang số mốc | `priority` gánh hai vai: Race dùng 3 cho cả `eligible` lẫn `milestone(3)` |
| Funnel/mốc per ván chơi | Double coverage với stream level |
| Log qua CẢ hai đường cho một mẻ thưởng | Log đôi rank reward ML — DWH đếm đôi tiền |
| Trói mốc trạng thái vào khoảnh khắc một-lần-đời | `eligible` ML — nhóm đối chứng sụp |
| Hàm tracking không ghi chủ gọi | `OnEligible` viết xong mồ côi, zero call site |
| Log cosmetic ăn theo tiến độ (badge/skin) | Biết tiến độ là tự biết — log là thừa |
| `amount` chưa nhân boost / đơn vị tuỳ hứng | Server không biết ai có x2; cột thành nồi lẩu |
| Đếm hộ server (CTR, retry, percentile ở client) | Client chở sự kiện, mọi phép tính là việc kho |
| Ambient scope / ThreadStatic quanh grant | Bác 05/09 — cascade grant của bên khác lọt vào scope |
| Exposure per-lần-hiện raw lên wire | Volume không chặn trên — Ui API gộp cụm là đường duy nhất |
| Grant tài nguyên **sau** `OnPass` | Lượt đã đóng → dòng thưởng mất `playTurnId` |
| Bịa mã đợt khi chưa biết | Hai mã một đợt, không nối được (vết sẹo Endless Treasure E2) — **hoãn gửi**, hết cơ hội thì gửi rỗng |

## 4. Checklist nghiệm thu — chạy trước khi ship event

1. Mọi hàm tracking có **call site** (grep — vết sẹo `OnEligible` mồ côi).
2. Console QA **sạch warning BigData** — van lọc, mốc khoảng cách, dòng level thiếu mã đợt… đều tự cảnh báo.
3. `DeclareCycle` gọi **trước** bước đầu tiên của đợt → dòng nào cũng có `cycle_index` + `cycle_start_ts` + `cycle_end_ts`.
4. User *"đủ điều kiện từ lâu"* đăng nhập **giữa đợt** → **có** dòng `eligible` của đợt đó (§0).
5. Một mẻ thưởng → số dòng resource trong QA **= số vế**, không hơn (soi log đôi).
6. Mọi key extras/detail riêng của event mang **tiền tố** đã khai (grep `KEY_*` của module).
7. Event có nhiều lượt: **lượt thứ hai** trong cùng đợt **vẫn lên log** (soi van lọc theo `roundId`).
8. Nếu khai event lên ván: `live_ops_level_attempts_* ≤ level_attempts_*` trên **mọi** dòng mốc.
9. Data team **đã nhận**: khoá event + ý nghĩa thang mốc + vocab `resourceWhen`/`resourceWhere` + key extras riêng.

## 5. Data team đọc gì — công thức generic, dashboard dựng MỘT lần

Chi tiết + SQL: `LiveOpsEventContract.md` §5. Bản rút gọn:

```
catalog event/đợt       = DISTINCT live_ops_event, live_ops_cycle_id, cycle_index,
                          cycle_start_ts, cycle_end_ts  FROM f_sdk_live_ops_data
tham gia / %DAU         = ba số riêng: eligible → open → join (ĐỪNG gộp một rổ)
tiến độ cao nhất        = max(live_ops_stage) WHERE action='milestone', neo trên tập eligible,
                          user không chạm mốc nào thì ĐIỀN 0 (nguyên lý neo-tập-gốc)
rụng người giữa mốc     = số user có max(live_ops_stage) >= N  (dạng "từ mốc N trở lên" —
                          đếm đúng-từng-mốc cho phễu ĐI LÊN vì event cho nhảy cóc mốc)
số lần thử qua mốc N    = level_attempts_since_prev_step trên dòng milestone(N)
                          (có live_ops_level_attempts_* thì dùng nó — chính xác hơn)
nhóm countdown lúc vào  = cycle_end_ts − live_ops_enter_ts
source do event phát    = SUM(amount) WHERE live_ops_event=… GROUP BY live_ops_stage, currency
exposure + CTR          = f_sdk_ui_exposure: sum(click_count)/sum(impression_count) per surface (≤ 1.7.0: f_sdk_ui_impression)
doanh thu event         = f_sdk_in_app_data WHERE live_ops_event=…
quay lại đợt sau        = live_ops_cycle_id + cycle_index
cột "đang tham gia"     = ENRICHMENT phía kho từ cặp join → complete/end — KHÔNG phải param
```

## 6. Ba tầng tích hợp — điền phiếu theo tầng

| Tầng | Gồm | Khi nào |
|---|---|---|
| **Tối thiểu** | `DeclareCycle` + `eligible` mỗi phiên + `milestone` mỗi mốc + `Stamp` dòng thưởng | **Mọi event.** Dưới mức này thì event **vô hình trong catalog** và không có thước đo khoảng cách |
| **Chuẩn** | + `open` / `join` / `complete` / `claim` theo thiết kế thật + extras riêng trên ván | Event có phễu tham gia, có ván chơi |
| **Đầy đủ** | + `Ui` exposure + `Offer`/`Iap` + `Ad` + khai event lên ván (nếu độc quyền) + `roundId` | Event có cửa bán, cần tuning bot, hoặc chơi nhiều lượt mỗi đợt |

Điền phiếu **từ tầng thấp lên**. Mỗi mốc thêm vào phải trả lời được *"metric nào cần nó?"* — không có
câu trả lời thì **đừng thêm**.
