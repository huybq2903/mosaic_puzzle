# Tracking Key Collection — rà theo code + tham chiếu chart

> Viết 21/10/2026, **rà trên code thật** (`Modules/Events/KeyCollection` 1.1.0, pin `bigdata 1.5.4`).
> Khác mọi doc `Request-Game-*` còn lại: event này **đã tự tích hợp xong trước khi có doc**, nên đây
> là **biên bản rà soát + tham chiếu**, không phải danh sách việc.
> Hợp đồng: `design/LiveOpsEventContract.md` · Khuôn: `design/LiveOpsTrackingPattern.md`.

## 0. Kết luận trong 30 giây

| | |
|---|---|
| **Sáu mốc live-ops** | ✅ đủ cả sáu — `eligible` · `open` · `join` · `milestone` · `complete` · `claim` |
| **Lịch đợt** | ✅ `DeclareCycle` đủ 4 field, qua `EventLiveOpsTracker` dùng chung của `Events/Core` |
| **Stream funnel cũ** | ✅ **không bắn** — đúng, event mới thì làm thẳng cách mới, không có pha song song |
| **Dòng tài nguyên** | ✅ có `exchangeId`, có `Stamp` live-ops |
| **Phơi UI** | ✅ ba bề mặt `entry` · `popup` · `main` |
| **Việc còn phải làm** | **Hết việc** sau vòng rà 2 (21/10 15:30): K1 = cố ý, K2 **đã sửa** (`eligible` về tầng dữ liệu). K3 rà lại là **không có vấn đề**. Chỉ còn lưu ý khi đọc số: `open` gồm cả popup game tự bật — xem ⚠ ở §0c |

## 0b. 🟡 RÀ SỐ PRODUCTION 28/10 — `join` không có `open`, và `join` lặp trong một đợt

Hai mục phía đọc số báo, **đã truy tới cùng** (3 vòng, hai nhận định của mình bị kho loại).

**`join` mà không có `open`: đường bắn `join` KHÔNG QUA UI, và nó nằm ở CODE GAME.** Trong module,
`open` bắn ngay `OnEnable` của `PopupKeyCollection` **và** của `MainUIKeyCollection`, còn
`SendJoin()` chỉ có **một** chỗ gọi: nút Play trong popup — tức theo module thì `join` không thể đi
một mình. Nhưng `SendJoin()` là **`public static`**, nên code game gọi được từ bất kỳ đâu.

Kho đã chạy lại **toàn bộ dữ liệu, bỏ lọc ngày** (28/10) rồi chạy tiếp phép kiểm
`log_type_create_id`. Ba giả thuyết **đều bị loại**: "cửa sổ ngày của truy vấn" (sai), "`cycle_id`
đổi giữa đợt" (chỉ một phần nhỏ), "mất log ở pipeline" (sai — dãy id liên tục, gần như luôn bắt đầu
từ 0, nên **mọi log client sinh ra đều đã tới kho**). Kho cũng rút lại câu *"join là bản tin
live-op đầu tiên"*: ngay trước `join` gần như luôn là `eligible` của cùng đợt, **liền số, cách 0
giây** — trong khi nhóm có `open` thì `eligible → join` cách **3–5 phút**.

⚠ **Đừng đọc "eligible liền số, cách 0 giây" thành bằng chứng auto-join** — đó là **khuôn của
module**: `SendEvent` gọi `KeyCollectionTracking.OnDataUpdated()` (→ `eligible`, nếu đợt này chưa
từng log) **ngay trong callback thành công** của lời gọi server, rồi mới `onSuccess()` (→ `join`).
Nên MỌI `join` của KC đều có `eligible` sát trước, nếu đợt đó chưa log eligible lần nào.

Thông tin thật nằm ở chỗ khác: nhóm thiếu `open` là nhóm mà **trước lúc join, user chưa hề có nhịp
data nào đi qua cổng eligible, cũng không có `open`** ⇒ họ **chưa từng mở UI của event**. Cộng với
`eligible → join` cách 3–5 phút ở nhóm bình thường (user mở UI, xem, rồi mới bấm), kết luận là:
**có một đường gọi `SendJoin()` không qua UI.**

### [ ] K4 🟡 Việc của dev game: tìm và khai rõ đường auto-join

1. **Tìm**: grep `DeclareKeyCollection.SendJoin(` trong repo game (và đường nào gọi
   `PopupKeyCollection.Play()` bằng code). Module không có đường nào ngoài nút Play.
2. **Quyết**: nếu auto-join là **hành vi đúng** (tự cho tham gia khi đủ điều kiện / theo trạng thái
   server) thì **ĐỪNG bắn `open` giả** — `open` nghĩa là *user mở UI*, bắn bù là làm hỏng luôn mẫu
   số "UI có thuyết phục không". Thay vào đó **khai nguồn ngay trên dòng `join`**:
   `Tracker.Join(extraMeta: new() { ["join_source"] = "auto" })` (UI thì `"ui"`) — `extraMeta` đã
   được merge sẵn vào dòng live-op, **không cần SDK sửa gì**.
3. **Hệ quả cho phía đọc số** (ghi vào hợp đồng): **`join` KHÔNG kéo theo `open`**. Với event có
   auto-join thì `join/open` không còn là tỷ lệ phễu; đọc `join/eligible` cho "bao nhiêu người vào
   cuộc" và `open/eligible` + `ui.impression` cho câu hỏi về UI.
4. **Việc kèm theo, bắt buộc**: module phải **tăng version mỗi lần đổi nghĩa mốc**. `CHANGELOG` chỉ
   có **một** mục `[1.1.0]` nhưng code trong 1.1.0 **đã đổi ít nhất hai lần** (21/10 chuyển
   `eligible` từ tầng UI về tầng dữ liệu = K2; 26/10 gửi lại module) — nên không ai truy được bản
   app nào chạy bản tracking nào, mà kho thì đo được **tỷ lệ khác nhau theo bản app** (đúng chốt
   còn treo của Master League).

**`join` lặp nhiều lần trong một đợt.** Van lọc khoá theo `(event, đợt, lượt, action, stage)` và
**ghi xuống đĩa** (sync lúc app pause + mỗi 5 phút), nên lặp chỉ xảy ra khi **khoá đổi** hoặc
**khoá mất**. Ba đường, tách bằng một truy vấn:

| Nghi phạm | Dấu hiệu |
|---|---|
| Kho đếm trùng (SDK gửi lại khi mạng lỗi) | các dòng trùng **cùng `uuid`** (mọi log đều mang `uuid`) ⇒ kho nên dedupe theo `uuid` |
| `cycle_id` đổi giữa đợt | các dòng trùng **khác `cycle_id`** |
| Van mất khoá (app bị kill trước lúc sync) | cùng `cycle_id`, **khác `uuid`** |

⚠ Nhắc lại việc **K1** mà dev đã xác nhận là cố ý: bộ đếm mốc của KC/CC nằm trong RAM và **dựa vào
van của SDK** để chống trùng. Số 28/10 là bằng chứng đầu tiên van đó rò ngoài thực địa — nên nếu
truy vấn chỉ ra nhánh "van mất khoá", việc sửa là **module tự chống trùng** (ghi cờ xuống local
data), không phải vá van.

## 0c. ✅ VÒNG RÀ 2 — 21/10 15:30: hết việc

| Mục | Dev nói | Kiểm trong code |
|---|---|---|
| **K1** bộ đếm mốc chỉ trong RAM | **cố ý**, dựa vào van chống trùng của SDK (ghi xuống đĩa), giống Card | ✅ nhận. Ghi lại để ai đổi khoá van sau này biết là có hai event đang dựa vào nó |
| **K2** mẫu số | đã chuyển `eligible` về **tầng dữ liệu** như Card; `impression entry` vẫn đếm lúc icon bật, và nút **chỉ bật khi icon thực sự hiện** | ✅ đúng: `Eligible` giờ nằm trong `LogEligible()` gọi từ `OnDataUpdated()` (`DeclareKeyCollection:434,442`); `OnIconShown` chỉ còn impression |
| **Pin** | `bigdata 1.5.4` | ✅ |

⚠ **Hệ quả còn lại — nhưng KHÔNG phải việc owner phải chốt** (sửa lại nhận định 21/10 trưa): hợp
đồng ký 17/09 đã định nghĩa `eligible` = **trạng thái đủ điều kiện**, bắn mỗi phiên
(`docs/design/LiveOpsTrackingPattern.md` §0 · §2a), và "đã thấy icon" có đường riêng là impression
§2d. Nên KC + CC **đang đúng**, còn **Master League là chỗ phải sửa** → việc **M6** trong
`Request-Game-MasterLeague.md`. Tới khi M6 xong thì **chưa đặt ba event cạnh nhau** trong một chart
`join/eligible`.

⚠ **Khi so `join / open` giữa ba event, nhớ `open` của KC không thuần "người chơi tự mở"**:
`PopupKeyCollection:18` bắn `open` ngay trong `OnEnable`, mà popup đó **game tự bật** (có cờ
`DeclareKeyCollection.IsSessionPopupShowed` để khỏi bật lại trong phiên). Card Collection thì `open`
= mở album (`TrackAlbumOpened` từ `CardCollectionMainPopup:150` / `CardCollectionPageView:97`). Chỗ
KC **tách được** nhờ nó bắn impression riêng theo surface (`popup` vs `main`,
`KeyCollectionTracking:39-43`); Master League thì popup Unlock cũng bắn `open` trong `OnEnable`
(`MasterLeagueUnlockPopup:19`) mà **không** có impression để tách.

## 1. Event này log gì

Tất cả nằm trong **một file**: `Scripts/Runtime/Analytic/KeyCollectionTracking.cs` (73 dòng) — đây là
cách làm đúng, mọi lời gọi SDK tập trung một chỗ.

| Khoảnh khắc | Hàm game gọi | Dòng ra |
|---|---|---|
| Đủ điều kiện tham gia | `OnIconShown()` → `Tracker.Eligible()` khi `IsRemoteEnabled() && IsActive && IsUnlocked()` | `f_sdk_live_ops_data`, `action = eligible` |
| Icon hiện | `OnIconShown()` → `Impression("entry")` | `f_sdk_ui_exposure` |
| Mở event | `OnOpen(surface)` → `Open()` + `Impression(surface)` | `action = open` + impression |
| Tham gia | `OnJoin()` | `action = join` |
| Qua mốc sao | `OnDataUpdated()` → vòng lặp bù mốc → `Milestone(StageNumber(index))` | `action = milestone`, `live_ops_stage` = **1-based** |
| Hết mốc cuối | cùng vòng lặp → `Complete()` | `action = complete` |
| Nhận thưởng mốc | `OnStageClaimed(stageIndex)` → `Claim(StageNumber(idx))` | `action = claim` + `live_ops_stage` |
| Thưởng vào ví | `StageRewardContext(stageIndex)` | `f_sdk_resource_data`, `resource_where = stage_reward`, có `exchange_id` |

**Chỗ làm đúng đáng ghi nhận — vòng lặp bù mốc**:

```csharp
for (var index = _loggedStageCount; index < stages.Length && stages[index].pointRequire <= data.point; index++)
{
    Tracker.Milestone(StageNumber(index));
    _loggedStageCount = index + 1;
}
```

Nhảy nhiều mốc một lúc (nhận cục điểm lớn) thì **mốc nào cũng có dòng**, không chỉ mốc cuối. Đây đúng
là chỗ ba event trước từng làm sai.

## 2. Ba việc còn lại

### [ ] K1 — `_loggedStageCount` chỉ nằm trong RAM: xác nhận là CỐ Ý

`_loggedStageCount` và `_loggedCycleId` là `static` **không persist**. Mở lại app là về 0 → vòng lặp
bắn lại `milestone` cho mọi mốc đã qua.

Hiện tại **không hỏng**, vì **van lọc của SDK** chặn: một dòng cho mỗi
`(event, cycle, round, action, stage)` mỗi máy, và van đó **ghi xuống đĩa**. Nên dòng trùng bị bỏ ở
tầng SDK.

⚠ Nhưng nghĩa là code game đang **dựa vào van của SDK** chứ không tự chống trùng. Cần dev xác nhận là
cố ý — nếu sau này ai bỏ van đó đi (hoặc đổi khoá van) thì event này bắn lặp mà không ai biết. Nếu
muốn tự lo, ghi `_loggedStageCount` vào local data như `CardCollection` đang làm với `loggedTradeSeq`.

### [x] K2 — Mẫu số `eligible` bị gate bởi UI, không phải bởi dữ liệu — **ĐÃ SỬA 21/10**, giữ phần dưới để tham chiếu

```csharp
// KeyCollectionButton.OnEnable()  →  KeyCollectionTracking.OnIconShown()
public static void OnIconShown()
{
    Tracker.Impression(SURFACE_ENTRY);                                   // (1)
    if (IsRemoteEnabled() && IsActive && IsUnlocked()) Tracker.Eligible(); // (2)
}
```

Hai hệ quả, **không phải lỗi, nhưng phải biết khi đọc số**:

1. `eligible` chỉ bắn khi người chơi **tới được màn có cái icon đó** (`OnEnable` của button). Người
   đủ điều kiện mà không vào màn ấy thì **không có trong mẫu số**. So với `CardCollection` — nó gọi
   `OnEligible()` từ tầng dữ liệu (`DeclareCardCollection` khi `IsEventValid()`) — nên **mẫu số của
   hai event này KHÔNG cùng định nghĩa**, đừng đặt cạnh nhau trong một chart so sánh.
2. `Impression("entry")` đếm theo **lần button được enable**, không hẳn là "icon thật sự hiện". Nếu có
   đường nào enable button rồi mới ẩn đi thì CTR của bề mặt `entry` **thấp hơn thật**.

Cần dev xác nhận: `OnEnable` của button có đồng nghĩa "icon hiện" không? Và mẫu số nên là "đủ điều
kiện" hay "đủ điều kiện + đã thấy icon"? (Chọn cái nào cũng được, miễn ba event **chọn giống nhau**.)

### ✅ K3 — `Join` nối đúng chỗ (sửa lại: lượt rà đầu mình đếm sai)

Lượt rà đầu mình báo "`Join` gọi từ 3 chỗ, nên gom một cửa" — **sai**. Hai trong ba kết quả grep là
`.Join(...)` của thư viện animation (`LMotion` sequence), không liên quan live-ops. Thực tế đúng một
call site, và nó nối vào **hành động join thật**, không phải vào UI:

```csharp
// DeclareKeyCollection.cs:456
SendEvent(KeyCollectionEventAction.Join, KeyCollectionTracking.OnJoin);
```

Mọi cửa khác cũng đều có người gọi (`OnOpen` 2 · `OnStageClaimed` 1 · `OnIconShown` 1 ·
`OnDataUpdated` 2 · `StageRewardContext` 1) — không có cửa nào viết ra rồi bỏ quên, tức không gặp
đúng cái bệnh của Master League lượt trước (`OnEligible` không ai gọi).

## 3. Những thứ cố ý KHÔNG log

- **Không có `round`**: event này không chia vòng, nên `live_ops_round_id` **vắng mặt** — đúng hợp
  đồng, đừng điền giá trị giả để "cho đủ cột".
- **Không log level**: event không dùng ván chơi làm tiến độ (điểm đến từ nguồn khác), nên không gắn
  gì lên `f_sdk_level_data`.
- **Không có IAP**: không gắn `liveOps*` lên dòng mua — nếu sau này bán pack trong event thì phải
  thêm (xem `LiveOpsTrackingPattern.md` mục IAP).

## 4. Chart đọc từ đâu

| Câu hỏi | Đọc ở đâu |
|---|---|
| Mẫu số: bao nhiêu user đủ điều kiện | `f_sdk_live_ops_data` `action = eligible`, `live_ops_event = key_collection` |
| Tỉ lệ mở / tham gia | `open` ÷ `eligible`, `join` ÷ `open` |
| Rơi ở mốc nào | đếm `milestone` theo `live_ops_stage` (1-based), so với `complete` |
| Nhận thưởng hay quên nhận | `claim` so với `milestone` cùng `stage` |
| Thưởng ra những gì | `f_sdk_resource_data` `resource_when = event_key_collection`, `resource_where = stage_reward`, join hai vế bằng `exchange_id` |
| CTR từng bề mặt | `f_sdk_ui_exposure` `surface_id = key_collection_{entry,popup,main}` |

## 5. Vì sao pin phải là `bigdata 1.5.4`

Đã sửa 21/10 (trước đó pin `1.5.2`). Lý do: `Events/Core` dùng `Complete(cycleId:)`/`Claim(cycleId:)`
để ghi mốc cho **đợt đã đóng** — ca đó cần **sổ hai ô** của BigData **1.5.3**. Trên 1.5.2 sổ chỉ có
một ô nên ghi cho đợt đã đóng sẽ **chiếm ô của đợt đang chạy** → `liveOpsEnterTs` và năm cặp mốc
khoảng cách của đợt hiện tại bị reset, lặng lẽ. KeyCollection hiện chưa dùng đường đó, nhưng nó ăn
chung `Events/Core` nên pin phải đủ.
