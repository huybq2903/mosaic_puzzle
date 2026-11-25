# Tracking Sky Path / Bee Path — TODO cho dev + tham chiếu chart

> **Dev chỉ cần đọc phần TODO ngay dưới đây.** Không có API mới, không có event mới — gắn thêm
> trường vào log game **đã có sẵn**. Tick `[x]` khi xong.
> Viết 09/10/2026 theo thiết kế mới: **3 chặng** (trước là 1 chặng 7 ván). Tiền tố key: **`path_`**.

Sky Path đua với bot **theo chuỗi thắng liên tiếp**, chơi trên **level thường**. Thiết kế mới:

| Chặng | Số level phải thắng liên tiếp | Số bot | Thưởng |
|---|---|---|---|
| 1 | 5 | 100 | 5.000 |
| 2 | 7 | 100 | 7.000 |
| 3 | 10 | 100 | 10.000 |

⚠ **Các con số trong bảng này KHÔNG đi vào log.** Số level cần thắng, số bot, giá trị thưởng đều là
**config** — log ghi số thật của từng lần chơi, còn ngưỡng thì phân bố trên chart tự phản ánh. Đổi
cân bằng thì log đứng yên.

# 🔁 LÀM THEO HỢP ĐỒNG LIVE-OP (event này CHƯA lên dữ liệu → không có pha song song)

> Chốt 15/10/2026. Hợp đồng: `docs/design/LiveOpsEventContract.md` · Khuôn:
> `docs/design/LiveOpsTrackingPattern.md`. SDK **BigData 1.5.0** đã có đủ API; chờ loader ký
> `f_sdk_live_ops_data` trước khi **game ship** (up package thì không chặn).
> **Khoá event của event này: `sky_path`** (để trần, không tiền tố `event_`).
>
> Event này **chưa có dòng log nào trên kho**, nên không phải giữ gì để tương thích: làm thẳng theo
> mục này, bỏ qua chỗ nào trong phần dưới trái với nó.
> ⚠ Ba event ĐANG CHẠY (Master League · Race · Endless Treasure) thì khác — chúng chạy **pha song
> song**, xem doc của chúng.


### [ ] L1 — Dòng live-ops (doc cũ nói "không funnel cho chặng" — **đảo lại**)

```csharp
FalconBigDataController.LiveOps.DeclareCycle(
    "sky_path", cycleId: cycleId, cycleIndex: cycleIndexNumber,
    cycleStartTs: startMs, cycleEndTs: endMs);

FalconBigDataController.LiveOps.OnStep("sky_path", FLiveOpsAction.Eligible);                 // MỖI PHIÊN
FalconBigDataController.LiveOps.OnStep("sky_path", FLiveOpsAction.Milestone, stage: stage);   // qua chặng 1/2/3
```

Vì sao đảo: lịch đợt + mẫu số + ba thước đo khoảng cách đều ở dòng live-ops; không có dòng thì event
vô hình trong catalog.

### [ ] L2 — Dòng thưởng chặng: `Stamp`

```csharp
var ctx = new ResourceParam { resourceWhen = "event_sky_path", resourceWhere = "stage_reward" };
FalconBigDataController.LiveOps.Stamp(ctx, "sky_path", stage: stage);
```

`detail.path_stage` + `detail.path_cycle_id` **không cần nữa** — `live_ops_stage` /
`live_ops_cycle_id` thay.

### [ ] L3 — `path_attempt`: bỏ, SDK đếm hộ

Số lần thử qua một chặng = `level_attempts_since_prev_step` trên dòng `milestone(N)` — SDK đếm
**lượt chơi thật** theo state machine. Nhờ vậy câu **D2** ("thua có mất tiến độ chặng không") không
còn ảnh hưởng tới con số.

### [ ] L4 — Log level: khai hay không tuỳ event có chạy chồng

| Tình huống | Làm gì |
|---|---|
| Sky Path chạy **cùng lúc** Adventure / Race trên level thường | **ĐỪNG khai.** Giữ `path_cycle_id`, `path_stage`, `path_streak` trên ván như thiết kế |
| Sky Path là event **duy nhất** chạy trên level thường | `LiveOps.Stamp(levelParam, "sky_path", stage: stage)` → `path_stage` thành dư, và "gãy ở ván thứ mấy của chặng" đọc từ cột chung |

`path_streak` **giữ trong cả hai trường hợp** — chuỗi thắng là chiều riêng của event, không cột
chung nào thay được.

⚠ **KHÔNG gọi thêm `Resource.OnEarned` để "chở ngữ cảnh live-ops"**. `Stamp` điền thêm cột vào
chính param đang gửi, **không sinh dòng mới** — gọi thêm là đếm đôi tiền (vết sẹo log-đôi của
Master League).

---

# 🔴 RÀ SỐ PRODUCTION 28/10 — hai việc đang treo đã lên số: R4 và R3

Phía đọc số báo đúng hai thứ doc này đã ghi là còn treo:

1. **Thiếu `cycle_index` + `cycle_start_ts`, trong khi `cycle_end_ts` có đủ** — đây là dấu vân tay
   của **R4**: `DeclareCycle` chỉ truyền `cycleEndTs`. SDK **chỉ điền chỗ game khai**, không bịa
   được. Với module đã chuyển sang `EventLiveOpsTracker`, hai cột này lấy từ **`version`** và
   **`timeStart`** của config event — hai field **không ai khác trong repo đọc**, nên ops không
   điền là mất im lặng.
2. **`join` bắn lặp trong một đợt** — hệ quả trực tiếp của **R3** (mã đợt lấy **ngày KẾT THÚC**):
   ops **gia hạn đợt là mã đợt đổi GIỮA đợt** ⇒ một đợt tách thành hai `cycle_id`, **van lọc sạch
   lại**, mọi bước (kể cả `eligible`/`join`) bắn lại. Doc này đã cảnh báo đúng câu đó ở R3.

Kiểm nhanh để chốt: đếm **số `cycle_id` phân biệt** của `sky_path` trong một khoảng, so với số đợt
thật đã chạy. Nhiều hơn ⇒ mã đợt đổi giữa đợt, và mọi chart "theo đợt" đang bị chẻ.
Van của SDK **không tự chống được** chuyện này: danh tính đợt là **lời khai của game**.

# ⚠ KẾT QUẢ RÀ CODE 16/10 — việc phải sửa trước khi ship

> Rà bản module dev game vừa import (16/10) đối chiếu hợp đồng
> `docs/design/LiveOpsEventContract.md` + mục 🔁 ở trên. 🔴 = làm sai số liệu hoặc lỗi hiệu năng,
> sửa trước khi ship · 🟡 = nên sửa · 🟢 = đã đúng, ghi nhận để khỏi sửa lại.

**♻ RÀ LẠI 19/10** (bản dev vừa chỉnh): **R1 · R2 · R5 · R6 xong** · R3 · R4 còn treo ·
thêm **R7 (lỗi SDK — mình đã vá)** và **R8**.

### [x] R1 🔴 `PlayerPrefs.Save()` nằm trên ĐƯỜNG LOG — ✅ SỬA XONG 19/10

> ✅ **19/10 — xong.** `SkyPathDailyStats` giờ chỉ **ĐỌC** trong getter (có cả comment ghim lý do),
> hai chỗ ghi chuyển sang `RegisterAttempt()` (lúc gửi `StartEvent`) và `MarkFinishedIfNeeded()` —
> cả hai gọi từ `DeclareSkyPath.SendAction`, tức có **sự kiện thật**. Đường log sạch `PlayerPrefs.Save()`.

`SkyPathCustomInfoRepository.GetInfo()` chạy **mỗi lần SDK dựng một dòng log** (nó là
`IFCustomInfoRepository`), và nó gọi `SkyPathDailyStats.AttemptInDay` / `FinishedInDay` → cả hai gọi
`Sync()` → `RollDayIfNeeded()` và nhánh "đã finished" có `PlayerPrefs.Save()`.

`PlayerPrefs.Save()` là **ghi đĩa đồng bộ**. Đặt nó trên đường log nghĩa là mỗi dòng log (level, ad,
iap, session…) đều có khả năng chặn luồng để ghi đĩa.

Sửa: `GetInfo()` chỉ được **ĐỌC**. Cập nhật sổ ngày chuyển sang chỗ có **sự kiện thật** —
`RegisterAttempt()` khi bắt đầu lượt, và một lần khi state đổi sang `Finished`; đổi ngày thì kiểm ở
mốc vào app / resume, không kiểm trong hàm đọc.

### [x] R2 🔴 `eligible` / `open` KHÔNG được mang `roundId` — ✅ SỬA XONG 19/10

> ✅ **19/10 — xong.** Tách đúng yêu cầu: `CycleStep()` (eligible/open) truyền `roundId = null`,
> `Step()` (join/milestone/complete/claim) truyền `RoundId`. Mẫu số không còn phình theo số lượt.
> ⚠ Cách tách ĐÚNG này có chạm một **lỗi của SDK** làm hỏng số đo khoảng cách — **mình đã vá chiều
> 19/10**, xem **R7**. Lỗi ở SDK, không phải ở module: đừng sửa ngược lại R2.

```csharp
private static void Step(string action, int? stage = null)
{
    …
    FalconBigDataController.LiveOps.OnStep(LIVE_OPS_EVENT, action, stage: stage,
        cycleId: cycleId, roundId: RoundId);   // ← eligible và open cũng bị gắn lượt
}
```

Van lọc khoá theo `(event, cycle, round, action, stage)` → **mỗi lượt mới là van sạch lại** → một
user có **N dòng `eligible`** trong một đợt. `eligible` là **mẫu số** của mọi chart tham gia, phình
mẫu số thì mọi tỷ lệ tụt xuống mà vẫn trông hợp lý.

Sửa: tách `Step()` làm hai — bước **của đợt** (`eligible`, `open`) không truyền `roundId`; bước
**của lượt** (`join`, `milestone`, `complete`, `claim`) thì truyền.

### [ ] R3 🟡 `const int STAGE = 1` — thiết kế 3 chặng chưa vào — ⏳ CÒN TREO 19/10

> ⏳ **19/10 — chưa làm.** `const int STAGE = 1` vẫn nguyên, `milestone`/`claim`/dòng thưởng đều
> mang mốc 1. Chart *"chặng nào rụng người nhiều nhất"* vẫn chưa trả lời được.

Mọi `milestone` / `claim` / extras đều mang stage **1**, nên câu *"chặng nào rụng người nhiều
nhất"* — chart trung tâm của thiết kế 09/10 (3 chặng: 5 / 7 / 10 level) — **không trả lời được**.
Cần stage = chặng thật (1 / 2 / 3) ở `milestone`, `claim` và `path_stage` trên dòng level.

⚠ **Và một hậu quả nặng hơn, ghi thêm 19/10.** Van lọc khoá theo
`(event, đợt, lượt, action, **mốc**)`. Hai chặng khác nhau mà cùng ghi mốc `1` thì với SDK đó là
**CÙNG MỘT BƯỚC** → dòng của chặng sau **bị chặn, không gửi đi đâu cả**. Không phải "sai nhãn" mà là
**mất hẳn dòng**.

Hiện chưa lộ ra vì Sky Path chỉ bắn `milestone` lúc đi hết đường (một dòng mỗi lượt). Nhưng hôm nào
thêm mốc cho từng chặng mà `STAGE` vẫn cố định thì **mất im lặng** — log chạy, không lỗi, không
warning, chỉ là chặng 2 và chặng 3 không bao giờ tới kho.

### [ ] R4 🟡 Mã đợt lấy từ ngày **KẾT THÚC** — ⏳ CÒN TREO 19/10

> ⏳ **19/10 — chưa làm.** `CycleId` vẫn `GetTimeEnd()`. Rủi ro gia hạn đợt còn nguyên.

```csharp
CYCLE_ID_PREFIX + GetTimeEnd().ToString(CYCLE_ID_FORMAT)
```

**Ops gia hạn đợt là mã đợt đổi GIỮA đợt** → một đợt tách thành hai `cycle_id`, van lọc sạch lại,
phân vị tính trên hai nửa. Mục A3 của doc này viết fallback là **ngày MỞ đợt** chính vì lý do đó.
Thứ tự ưu tiên: id đợt do server cấp → không có thì `GetTimeStart`.

### [x] R5 🟡 `SkyPathCustomInfoRepository` — GIỮ, nhưng đừng thêm field mới — ✅ ĐÚNG CAM KẾT 19/10

> ✅ **19/10 — giữ đúng.** Vẫn đúng **bốn** field, không thêm field mới. Tên thiếu chữ `t`
> (`eventSkyPathAttempInDay`) cũng giữ nguyên — đúng, đừng sinh era cho field sắp bỏ.

Bốn field `eventSkyPathJoining / WinStreak / AttempInDay / Finished` đi theo **mọi dòng log của
game**. Về nguyên tắc đây là **state-param trên wire** — dòng đầu bảng ĐỪNG-LÀM của khuôn, và vết
sẹo gốc ghi đúng tên event này (đề xuất Sky Path, bác 05/09): mọi giá trị đó derive được từ dòng
live-ops.

Nhưng **event đang chạy**, nên:

| | Việc |
|---|---|
| Pha 1 (bây giờ) | **Giữ nguyên bốn field** (chỉ sửa chỗ ghi đĩa ở R1). ⚠ **KHÔNG thêm field mới** |
| Pha 2 | Bỏ sau khi chart chuyển sang `live_ops_stage` + `path_streak` trên ván + `level_attempts_*`, và khớp số trọn một đợt |

⚠ Tên `eventSkyPathAttempInDay` thiếu chữ **t** (`Attemp`). Nếu đã lên dữ liệu thì **giữ nguyên tên
sai** — đổi là sinh era cho một field sắp bỏ, không đáng.

### [x] R6 🟡 `package.json` không pin `bigdata` — ✅ SỬA XONG 19/10

> ✅ **19/10 — xong.** `falcon.modules.core.bigdata: 1.5.1`.

Module WinStreak **không khai** `falcon.modules.core.bigdata` trong `dependencies` trong khi code gọi
`LiveOps.*`. Thêm pin **≥ 1.5.1**.

### [x] R7 🔴 LỖI CỦA SDK, không phải của module — sổ mốc bị **reset** khi hai thân phận bản ghi xen nhau — ✅ ĐÃ VÁ 19/10

> ✅ **19/10 (chiều) — mình vá xong ở SDK.** `LiveOpsLogService` giờ giữ **hai ô sổ mốc** mỗi event
> (đợt đang chạy + đợt vừa đóng) và **lượt rời khỏi nhãn ô** (vẫn nằm trong khoá van lọc). Module
> **không phải sửa gì**. Hai thứ phía đọc số cần biết: `*_since_enter` từ nay luôn là *"từ lúc vào
> ĐỢT"* (kể cả Race — muốn "từ lúc vào bảng này" thì đọc `*_since_prev_step`), và dòng của một đợt
> quá cũ (đã mất ô) gửi các field khoảng cách **vắng mặt**. Lên cùng bản **BigData 1.5.2**.
>
> ⚠ **Việc của module: nâng pin `falcon.modules.core.bigdata` lên `1.5.2`** (đang là `1.5.1`) —
> bản vá nằm ở 1.5.2, pin cũ thì vẫn chạy code cũ.

Van lọc chỉ chặn bước **lặp lại**. Bước **lần đầu** nào có `(đợt, lượt)` khác bản ghi đang lưu thì
`EnsureCycleRecord` **reset cả bản ghi**: `live_ops_enter_ts` = bây giờ, năm cặp mốc về 0, và dòng đó
được coi là "bước đầu đợt".

Sau khi **R2** được sửa đúng, `eligible`/`open` mang lượt **rỗng** còn
`join`/`milestone`/`complete`/`claim` mang lượt thật → hai thân phận xen nhau trong cùng một đợt:

1. `eligible(đợt, "")` → tạo bản ghi, `enter_ts` = lúc đủ điều kiện.
2. `join(đợt, lượt 1)` → lượt khác → **reset** → mất đúng thứ đáng giá nhất: khoảng cách
   **eligible → join** (bao lâu kể từ lúc mở cửa thì user mới chịu vào).
3. Nếu phiên nào `eligible` bắn **sau** `join` (lúc init config chưa về, data mới gọi lại
   `OnEligible`) thì còn reset thêm một nhịp nữa, và dòng `milestone` ngay sau đó ship
   `*_since_enter = 0` + `*_since_prev_step` **vắng** — đúng con số chính của thiết kế.

Lỗi nằm ở `LiveOpsLogService` của **module BigData**, không phải ở module event — và cách sửa cũng ở
SDK, hai luật:

| Luật | Nội dung |
|---|---|
| **Lượt rỗng** | Bước không mang `roundId` (`eligible`/`open`) **không được** đổi thân phận lượt của bản ghi, nên không reset |
| **Đợt đã đóng** | Dòng mang đợt **khác bản khai hiện tại** là **chỉ đọc**: không reset sổ, không mượn lịch (`cycle_start_ts`/`cycle_end_ts`) của đợt đang chạy, và các field khoảng cách **vắng mặt** thay vì bịa số |

Vá ở **BigData 1.5.2**. Module **không phải làm gì** — và **đừng** sửa ngược lại **R1/R2**.

### [x] R8 🟡 Bốn trường trên dòng level vẫn chưa có ai gắn (việc **A1**) — ✅ ĐÃ NỐI 19/10 (chiều)

> ✅ **19/10 chiều — xong.** `Assets/Falcon/Game/Core/Scripts/GameMode/DefaultGamePlayMode.cs` gắn cả hai đầu:
> `OnLevelStart` → `SkyPathTracking.LevelStartMeta()` (dòng 84), `OnLevelResult` →
> `SkyPathTracking.LevelResultMeta(result.Win)` (dòng 128). Có cả hàm `MergeMeta` để meta của game và
> của event không đè nhau.
>
> ⚠ Lượt rà trước mình báo "0 lời gọi" là **SAI** — mình grep trong `Modules/Events`, mà lời gọi nằm ở
> `Assets/Falcon/Game`. Xin lỗi đã báo nhầm.

`LevelStartMeta()` / `LevelResultMeta()` đã viết xong và đúng, nhưng **rà cả repo SDK không thấy ai
gọi** — Sky Path không tự log level (đúng, level là của game), nên việc gắn `extraMeta` nằm ở **phía
game**. Không kiểm được từ repo này; nhờ dev game xác nhận đã nối ở `Level.OnStart/OnPass/OnFail`.

Chưa nối thì mất cả ba nhãn `path_cycle_id` / `path_stage` / `path_streak` trên ván — tức *"ván này
chơi trong lúc đang đi Sky Path"* không lọc được.

⚠ Và ghi rõ cho phía đọc số, đây **không** phải lỗi: cặp `live_ops_level_passes_*` /
`live_ops_level_attempts_*` **luôn rỗng** với Sky Path, vì dòng level **cố ý không khai event**
(chạy chồng — xem mục 🟢). Chart phải dùng cặp **toàn cục** `level_passes_*` / `level_attempts_*`,
là **chặn trên**, và nói rõ điều đó trên nhãn.

### 🟢 Đã đúng — đừng sửa lại

- Dòng level **KHÔNG** khai event (`Stamp`) — đúng, vì Sky Path chạy **chồng** lên level thường và
  có thể cùng lúc Adventure/Race; khai là phải chọn bừa một event.
- Giữ `path_cycle_id` / `path_stage` / `path_streak` trên ván làm nhãn "ván trong event".
- Dùng `roundId` cho lượt chơi lại trong đợt — đúng khuôn, chỉ sai ở chỗ R2 (áp cả cho `eligible`).

---

# ✅ TODO

## A. Dev game / module Sky Path

### [ ] A1 — Gắn 4 trường vào log level (ván chơi)

Game đã gọi `Level.OnStart` / `OnPass` / `OnFail` rồi, chỉ thêm `extraMeta`:

```csharp
// ─── Bắt đầu ván ───
extraMeta = new Dictionary<string, object>
{
    ["path_cycle_id"] = cycleId,        // mã đợt đang chạy — đọc từ data, xem A3
    ["path_stage"]    = stage,          // 1 / 2 / 3  ← TRƯỜNG MỚI của thiết kế 3 chặng
    ["path_streak"]   = streakTruocVan, // chuỗi thắng TRƯỚC ván này, trong chặng
    ["path_attempt"]  = lanThuThuMay    // lần thử thứ mấy của chặng này (bắt đầu = 1)
};

// ─── Thắng ───  cùng 4 trường, `path_streak` là chuỗi SAU ván
// ─── Thua ───   cùng 4 trường, `path_streak` = chuỗi sau khi đứt (0 hoặc mốc tụt về)
```

⚠ **Ván ngoài event thì đừng gắn gì** (chưa mở khoá, hoặc ngoài thời gian đợt). Vắng
`path_cycle_id` chính là dấu hiệu "ván này ngoài event", và đó là cách tách nhóm khi so win rate
trong/ngoài event.

**Kiểm:** đang ở chặng 2, thắng ván thứ 3 → dòng level `Pass` có `path_stage = 2`,
`path_streak = 3`.

### [ ] A2 — Mỗi chặng thắng được: dòng thưởng có ngữ cảnh

Mỗi **món quà** một dòng, đi qua kho tài nguyên kèm context:

```csharp
var ctx = new ResourceParam
{
    resourceWhen  = "event_sky_path",
    resourceWhere = "stage_reward",
    exchangeId    = Guid.NewGuid().ToString("N"),   // RIÊNG cho từng chặng
    detail = new Dictionary<string, object>
    {
        ["path_cycle_id"] = cycleId,
        ["path_stage"]    = stage,                   // 1 / 2 / 3
        ["path_attempt"]  = lanThuDaDungDeQuaChang   // qua chặng sau bao nhiêu lần thử
    }
};
```

- `amount` là **số thực nhận** (5.000 / 7.000 / 10.000 theo config) — đừng log số gốc rồi mong
  server tự nhân.
- ⚠ **Một món quà chỉ MỘT dòng.** Đã đi qua kho thì **không** gọi thêm `Resource.OnEarned`.

**Kiểm:** qua chặng 2 → dòng thưởng có `detail.path_stage = 2`, `resource_when = event_sky_path`.

### [ ] A3 — Mã đợt đọc từ dữ liệu thật

`path_cycle_id` lấy từ **id đợt do server cấp**. Không có id thì dùng **ngày mở đợt**, dạng
`sky_path_2026-10-12`.

⚠ Đừng để bên data tự suy lịch đợt, và **đừng bịa mã khi chưa có dữ liệu**: chưa biết mã thì
**giữ mốc lại rồi gửi khi dữ liệu về**; tới lúc hết cơ hội thì gửi với mã **để trống**. Không được
mất dữ liệu, cũng không được bịa (bài học Endless Treasure E2).

## B. Những thứ KHÔNG log

| Không log | Vì sao |
|---|---|
| Số level cần thắng của mỗi chặng (5 / 7 / 10) | Config. Suy được từ `path_streak` cao nhất mỗi chặng |
| Số bot mỗi chặng (100) | Config, không chart nào dùng |
| Giá trị thưởng (5k / 7k / 10k) | Dòng thưởng đã ghi `amount` thật |
| Bước funnel cho từng chặng | Mỗi chặng thắng đã có dòng thưởng — đọc từ đó |
| Thứ hạng hiện tại so với bot | Suy được từ `path_streak`; log thêm là trạng thái, dễ lệch |
| Popup, anim, thanh tiến trình | Không chart nào dùng, volume lớn |

## C. Báo data team

- `resourceWhen`: `event_sky_path`. `resourceWhere`: `stage_reward`.
- Key trên log level: `path_cycle_id`, `path_stage`, `path_streak`, `path_attempt`.
- Key trong `detail` dòng thưởng: `path_cycle_id`, `path_stage`, `path_attempt`.
- ⚠ **Quy ước đọc:** "qua chặng" đọc từ **dòng thưởng chặng**. Nếu sau này game cho user tự bấm
  nhận thưởng thì phải báo data, vì lúc đó "qua chặng" và "nhận thưởng" tách làm hai thời điểm mà
  số vẫn ra bình thường.

## D. Câu cần GD / dev trả lời

| # | Câu hỏi | Vì sao cần |
|---|---|---|
| D1 | Thua một ván thì chuỗi về **0** hay **tụt một mốc**? | Quyết định cách đọc `path_streak`; không ảnh hưởng cách log (vẫn ghi số thật) |
| D2 | Thua thì **mất tiến độ chặng** hay chỉ chơi lại ván đó? | Nếu không mất thì `path_attempt` luôn bằng 1 → **bỏ trường đó** |
| D3 | Qua chặng thì thưởng **tự nhận** hay user bấm nhận? | Quyết định quy ước đọc ở mục C |
| D4 | Có giới hạn **số lần thử mỗi ngày** không? | Nếu có thì cần biết để đọc đúng drop-off; số lần thì đã có trong `path_attempt` |
| D5 | Chuỗi và tiến độ chặng có **mang sang đợt sau** không? | Chỉ ảnh hưởng cách đọc, không ảnh hưởng cách log |

---

# 📖 Chart đọc từ đâu

| Chart | Cách tính |
|---|---|
| **Bao nhiêu người qua từng chặng** | Distinct user có dòng thưởng theo `detail.path_stage`, cắt theo `path_cycle_id` |
| **Chặng nào rụng người nhiều nhất** | So số người qua chặng 1 → 2 → 3 |
| **Gãy ở ván thứ mấy của chặng** | Dòng level `Fail` có `path_stage`: phân bố theo `path_streak` lúc gãy. Đây là số trực tiếp để cân độ khó bot |
| **Số lần thử để qua một chặng** | `max(path_attempt)` mỗi user × chặng × đợt — tổng và phân vị |
| **Tài nguyên phát ra theo chặng** | `SUM(amount)` dòng thưởng, group `detail.path_stage` |
| **Win rate trong / ngoài event, cùng level** | Stream level: so ván **có** `path_cycle_id` với ván **không có**. Không cần log thêm |
| **Revive / first-try win rate** | `boostersUsed` và `failCount` / `playCount` — SDK đã có trên log level |

⚠ **"Win rate trong/ngoài event" là phép so yếu**, vì event bật cho mọi user đủ điều kiện nên không
có nhóm đối chứng. Muốn kết luận chắc thì cần **A/B holdout** (tắt event cho 10–20% user đủ điều
kiện, dùng nhãn AB sẵn có trên mọi log).

# 📖 Vì sao thiết kế như vậy

| Quyết định | Lý do |
|---|---|
| Thêm `path_stage` | **Chặng là một chiều phân tích mới.** Trước đây 1 chặng nên không cần; giờ 3 chặng, không có trường này thì mọi câu hỏi "chặng nào rụng người" đều không tách được. Đây là lý do duy nhất phải sửa log khi GD đổi thiết kế |
| Không log số level / số bot / giá trị thưởng | Đổi *số lượng* thì log đứng yên; chỉ khi thêm *chiều* mới phải thêm trường. Ngưỡng suy được từ dữ liệu |
| Không funnel cho chặng | Mỗi chặng thắng đã có dòng thưởng. Log thêm là trùng |
| `path_streak` gắn **cả ván thua** | Khoảnh khắc đứt chuỗi là thứ đáng giá nhất của event. Chỉ log lúc thắng là thấy nửa câu chuyện |
| Tiền tố `path_` | Luật tiền tố live-op (`LiveOpsTrackingPattern.md` §1): Sky Path gắn extras lên **level thường**, nơi event khác cũng gắn. Key chung chung là event sau ghi đè event trước, mất số im lặng |

## So với Go-Kart Race

Hai event giờ gần giống nhau — 3 chặng, chơi trên level thường, đua với bot — nên dùng được cùng
cách nghĩ, chỉ khác một điểm cốt lõi:

| | Sky Path | Go-Kart Race |
|---|---|---|
| Điều kiện qua chặng | **Thắng liên tiếp** 5 / 7 / 10 ván | Thắng đủ số ván, **không cần liên tiếp** (đổi 09/10) |
| Trường vị trí trong chặng | `path_streak` (chuỗi đang giữ) | `race_level_pos` (ván thứ mấy) |
| Bot | 100 mỗi chặng | Bảng đấu riêng mỗi vòng |
| Chu kỳ | Đợt | Đợt, và mỗi lần ấn Start là một bảng đấu mới |
