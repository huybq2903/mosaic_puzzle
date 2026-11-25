# Tracking Master League — TODO cho dev + tham chiếu log

> **Dev chỉ cần đọc phần TODO ngay dưới đây** — mỗi việc: sửa file nào, chép đoạn code nào, kiểm
> ra sao. Tick `[x]` khi xong. Giải thích "vì sao" và tài liệu cho team data nằm ở nửa dưới.
> Cập nhật 17/09/2026 theo module thực tế + đối soát dữ liệu knit.jam / bee.flow.

# ✅ RÀ SỐ PRODUCTION 28/10 — `live_ops_level_passes` > `level_passes`: **SỐ ĐÚNG, DOC SAI**

Phía đọc số báo ML vi phạm bất biến `live_ops_* ≤ *` ở các bước `milestone`/`claim`/`open`/
`complete`, và đoán là *"bộ đếm toàn cục bị đặt lại về 0 mà bộ đếm của event thì không"*. **Không
phải.** Hai cột đo hai thứ khác nhau:

| Cột | Nguồn client | Đếm gì |
|---|---|---|
| `level_passes_since_*` | `MaxPassedLevel` (= `LevelData.level − 1`) | **tiến độ MAP CHÍNH** |
| `live_ops_level_passes_since_*` | bộ đếm riêng, `+1` mỗi dòng level khai event | **số ván của event** đã thắng |

ML có **bộ level riêng** nên ván league **không nhích map chính** ⇒ số của event lớn hơn hẳn số
toàn cục là **đúng**. Và "reset" không tạo ra được hiện tượng này: sổ mốc lệch thì delta ra **âm**,
mà delta âm thì SDK **bỏ trường** (vắng) chứ không trả số nhỏ.

**Lỗi nằm ở doc của SDK** (sửa 28/10 ở `FLiveOpsLog` + hợp đồng §4): bất biến được hứa cho **cả
hai cặp**, trong khi chỉ **cặp attempts** đúng — hai bên cùng đếm LƯỢT CHƠI và mỗi ván của event
cũng cộng vào sổ toàn cục trong **cùng một** lời gọi `ReportLevel`.

- **Luật DQ dùng được, duy nhất**: `live_ops_level_attempts_* ≤ level_attempts_*`.
- **Việc của phía đọc số**: chạy lại check trên **cặp attempts**. Lệch ở đó mới là lỗi thật.
- **Về việc 1.5.6 chưa thấy cặp `live_ops_*` của ML**: đúng thiết kế bản vá — cờ "event này khai
  level" chỉ bật từ **dòng level khai event ĐẦU TIÊN sau khi nâng bản**, nên máy chưa chơi league
  thì cặp đó **vắng** (vắng = CHƯA BIẾT). ML có `Stamp` dòng level nên đường này thông; kiểm bằng:
  máy 1.5.6 **đã có** dòng level mang `live_ops_event = master_league` thì mọi dòng bước **sau đó**
  phải có cặp. Không có thì bản vá hỏng.

# ✅ VÒNG RÀ 3 — 21/10 17:00, dev sửa xong: **M6 + S2 HẾT VIỆC**, còn 2 chốt nhỏ

Kiểm bằng code, không tin CHANGELOG.

| Mục | Dev khai | Kiểm trong code |
|---|---|---|
| **M6** `eligible` về tầng dữ liệu | "gửi từ tầng dữ liệu (mỗi lần data về, khi `ShouldShowIcon()`), không còn từ icon; icon gửi impression `entry`" | ✅ **đúng trọn cả hai bước**: `MasterLeagueTracking.cs:113` `if (ShouldShowIcon()) OnEligible();` trong `OnDataUpdated()`; `MasterLeagueButton.cs:30` giờ gọi `OnIconShown()`; `OnEligible` **chỉ còn một chỗ gọi** duy nhất là tầng dữ liệu (grep cả module). ML giờ có đủ `impression entry` như CC/KC |
| **M6** mẫu số có còn phụ thuộc Home? | — | ⚠ **phiên online: hết phụ thuộc. Phiên offline: vẫn cần về Home** — xem bảng "6 chỗ phát `EVENT_DATA_UPDATED`" ngay dưới |
| **S2** vòng lặp mốc | "`milestone(n)` chỉ gửi khi điểm đạt ngưỡng step đầu của Stage n — vừa join không còn `milestone(1)`" | ✅ `LogReachedStages` giờ so ngưỡng: `for (stageIndex…) { if (config.GetThresholdAt(firstStep) > data.score) return; Tracker.Milestone(StageNumber(stageIndex)); }` — hết `index = 0` + `<=`. Ngưỡng dựng từ `BuildProgressionSteps()` (cộng dồn, `Math.Max` nên không tụt), cùng một bảng ngưỡng mà UI dùng (`GetProgressionStageIndexByScore`) — nhưng **hai nghĩa khác nhau**, xem ⚠ dưới bảng |
| **Pin** | `bigdata 1.5.4` · `eventsliveops.core 1.1.0` | ✅ cả hai |

### `eligible` giờ đi theo nhịp data của module — 6 chỗ phát, một chỗ vẫn gắn Home

`OnEligible` chỉ còn một chỗ gọi: `MasterLeagueTracking.OnDataUpdated()`, mà hàm đó chạy khi module
emit `EVENT_DATA_UPDATED`. Grep cả module ra **đúng 6 chỗ phát**:

| Chỗ phát | Khi nào | Cần về Home? |
|---|---|---|
| `DeclareMasterLeague.cs:765` (`SendEvent`) | mọi phản hồi action, kể cả `FetchData` — `FetchData` chạy từ `SubmitPendingOfflineResults` ← `HandleConnectionStatusChanged(true)` ← `MasterLeagueSessionListener` (lúc kết nối) | **Không** |
| `:826`, `:877` | kết thúc round | Không |
| `:993` | nhận thưởng hạng | Không |
| `:704` | **kích hoạt từ cache offline** — nhưng `TryShowFromOfflineCache()` chỉ được gọi từ `MasterLeagueHomeReactor.cs:87` (`Start`/`OnEnable`) | **CÓ** |
| `:667` | server bỏ event (dọn dữ liệu local) | Không |

Nên: phiên **online** có `eligible` không cần về Home (đường kết nối → `FetchData`); phiên **hoàn
toàn offline** vẫn phải về Home một lần, vì chỉ reactor của Home mới dựng event từ cache. Thứ tự an
toàn: `ActivateEvent` gọi `RegisterActionsEvent()` (`DeclareEventsManager.cs:75`) **trước** khi emit,
nên không có chuyện emit vào chỗ chưa ai nghe.

⚠ **Sửa lại nhận định trưa nay**: bản đầu của mục này ghi "phiên offline vẫn có `eligible`, mẫu số
rộng hơn bản cũ" — **sai một nửa**, vì đường offline nằm sau Home. Phần đúng: mẫu số **không còn
gắn vào icon**, và phiên online thì không cần Home.

### ⚠ `milestone(n)` ≠ "đang ở Stage n" — lệch nhau trong khoảng giữa hai Stage

Hai hàm dùng **cùng bảng ngưỡng** nhưng **khác nghĩa**:

| | Nghĩa |
|---|---|
| `milestone(n)` (log) | đã **đạt ngưỡng step đầu** của Stage n |
| `GetProgressionStageIndexByScore` (UI) | Stage của **step đầu tiên chưa đạt** |

Người chơi xong hết step của Stage n-1 mà chưa tới step đầu của Stage n: **UI đã hiện Stage n**,
còn log **chưa có** `milestone(n)`. Đem `max(live_ops_stage)` so với "người chơi báo đang ở Stage
mấy" sẽ lệch đúng một bậc trong khoảng đó — đó là **định nghĩa**, không phải lỗi.

## 🟡 Hai chốt nhỏ còn lại (không chặn đọc số, nhưng nên chốt)

**1. Version module KHÔNG tăng dù hành vi mốc đã đổi.** `MasterLeague/package.json` vẫn `1.0.4`,
`CardCollection` vẫn `1.1.0` — CHANGELOG sửa **tại chỗ** trong cùng mục. Hệ quả: hai build có nghĩa
`eligible`/`milestone` **khác nhau** cùng mang một số bản, phía đọc số không có cách nào phân biệt
"game này đã có bản sửa chưa". Mục *Phát hành module* của doc này đã ghi luật: mốc đổi nghĩa thì
**báo data team số bản**. → Đề nghị tăng `1.0.5` (ML) / `1.1.1` (CC), hoặc tối thiểu ghi ngày vào
CHANGELOG để data team có mốc.

**2. `milestone(1)` vẫn có thể ≡ `join` nếu step đầu của Stage 1 có `pointRequire = 0`.** Ngưỡng
step đầu = `Math.Max(stageOffset + pointRequire, previousThreshold)`, nên `pointRequire = 0` →
ngưỡng 0 → `0 > 0` sai → bắn `milestone(1)` ngay lúc join. Đây là **câu hỏi config server**, không
phải lỗi code: xác nhận step đầu tiên của mọi Stage đều cần > 0 điểm. Nếu cố ý có step 0 điểm (quà
tặng khi join) thì data team phải biết để đọc `milestone(1)` là "đã join", không phải "đã qua mốc".

---

# ✅ RÀ CODE 21/10 trưa (ML 1.0.4) — hai chỗ phải sửa (**cả hai đã xong**, xem vòng rà 3 ở trên)

Kiểm từng khẳng định của dev bằng code.

| Mục | Kiểm |
|---|---|
| **M1** `open` ở popup Join/Unlock | ✅ `MasterLeagueJoinPopup:39`, `MasterLeagueUnlockPopup:19` |
| **M3** `LeagueId` không còn tên dự phòng | ✅ `LeagueId => Tracker.CurrentCycle.id`, hết fallback |
| **M4/R1** có `cycleStartTs` | ✅ qua `BaseEventConfig.GetCycle` → `EventCycle.FromIndex(event, version, timeStart, end)` — **R1 treo từ 16/10 giờ đóng** |
| **G1** `eligible` gọi từ icon mỗi lần hiện | ✅ đúng như dev nói (`MasterLeagueButton:30,54` + `ShouldShowIcon()`) — nhưng **chỗ gọi** sai hợp đồng, xem **M6** ngay dưới |
| **G2** round league không qua `FLevelManager` | ✅ module có `OnLevelResult` **riêng của nó**, không gọi `FLevelManager.OnLevelStart/OnLevelResult` |
| **G3** revive dùng key `buyMoreTime` | ✅ key đó có thật, nhưng nó thuộc **đường level của SDK** (`Level/Core/FLevelManager`, `AnalyticParamsSync/ALevelHeartBeatService`), không phải của module ML |
| **R4** hai hình dạng claim | ✅ `Claim(null, cycleId: leagueId)` = quà **xếp hạng** · `Claim(stageNumber)` = quà **mốc** |
| **R5** `golden_yarn` không đếm đôi | ✅ chỉ xuất hiện trong `MasterLeagueTracking.cs`, không đi qua kho tài nguyên |
| **R2** giữ `MasterLeagueCustomInfoRepository` 4 field | ✅ đúng 4: `eventMasterLeagueJoining` · `_Round` · `_Stage` · `_Yarn` |
| **Pin** | ✅ `bigdata 1.5.4` + `eventsliveops.core 1.1.0` (đổi từ `.legacy 1.0.10`) |

## 🔴 M6 — `eligible` của ML đang đo mẫu số KHÁC hai event kia (hợp đồng đã chốt nghĩa từ 17/09)

Dev làm đúng như họ khai, nhưng **chỗ gọi** sai hợp đồng: `eligible` bắn từ icon
(`MasterLeagueButton:30,54`), nên ML đếm *"đủ điều kiện **và** đã tới màn có icon"*, còn Card
Collection / Key Collection đếm *"đủ điều kiện"* ngay khi data về.

Đây **không phải** chuyện owner/GD phải chọn định nghĩa. Ba câu hỏi khác nhau đã có ba đường riêng
(`docs/design/LiveOpsTrackingPattern.md` §0 · §2a · §2d, ký 17/09):

| Muốn trả lời | Đọc bằng | Trạng thái 3 event |
|---|---|---|
| "Đủ điều kiện mà **không tham gia**" | `join` / `eligible` | ML **sai mẫu số** · CC, KC đúng |
| "**Mở UI** mà không tham gia" | `join` / `open` | cả ba đã có `open` |
| "**Thấy icon** mà không mở" | impression `<event>_entry` / `open` (stream `f_sdk_ui_exposure`) | ML **chưa bắn** · CC `CardCollectionButton:22` + KC `KeyCollectionButton:32` đã bắn |

Dùng `eligible` để đo "đã thấy icon" là **mất hẳn câu thứ nhất**, và đúng vết sẹo trong bảng
ĐỪNG-LÀM (*"Trói mốc trạng thái vào khoảnh khắc"* — chính bug `eligible` ML cũ).

### Con số ML đang ghi **không bằng** cả hai tập — nó lọt giữa

```
{thấy icon thật}  ⊆  {eligible ML đang ghi}  ⊆  {thật sự đủ điều kiện}
```

Vì chỗ ghi nằm trong nút icon, muốn có dòng log thì **nút phải sống trước**. Điều kiện của dòng log
thì **giống y hệt** điều kiện hiện icon (`ShouldShowIcon()` ở cả `MasterLeagueButton:30,54` và `:56`),
nên phần lệch **không phải điều kiện mà là thời điểm**:

| Phần lệch | Điều kiện | Ở đâu |
|---|---|---|
| **Phải về Home** | `ShowIcons`: `place != PLACE_HOME → return` | `DeclareMasterLeague.cs:583` |
| *(game có thể thêm cửa riêng)* | `Custom?.CanAutoShow(place, elementType)` — game override được | `DeclareMasterLeague.cs:607`, `AMasterLeagueCustom.cs:10` |
| Điều kiện thật của mẫu số | `IsRemoteEnabled() && config != null && !HasEnded() && config.IsUnlocked()` | `ShouldShowIcon()` — `DeclareMasterLeague.cs:897` |

⚠ **Đã kiểm và KHÔNG áp dụng cho ML**: cửa `levelShowEvent` (`IEventConfig.IsIconShowable()`) chỉ
nằm trong bản mặc định của interface; ML **override** `CanAutoShow` và `DefaultCanAutoShow` trả
`ElementType.ICON => ShouldShowIcon()` (`DeclareMasterLeague.cs:607-626`), **không** kiểm
`levelShowEvent`. Nên **không** có dải người chơi nào bị mất vì hai field level lệch nhau. (Bản
trước của mục này ghi có — **sai**, đã bỏ.)

Vậy phần mất thật của mẫu số ML gọn lại còn một chuyện: **người đủ điều kiện mà cả mùa không lần nào
về Home khi đang đủ điều kiện** thì không có dòng. Ít, vì van lọc nén 1 dòng/mùa/máy nên chỉ cần về
Home **một lần** trong mùa là đủ — nhưng nó lệch **theo nhóm** (người chơi vào thẳng ván rồi tắt), và
đó đúng là nhóm mà chart *"đủ điều kiện mà không join"* muốn nhìn. Nếu game đặt icon ML là object
tĩnh trong prefab Home (không qua `ShowIcons`) thì cửa này cũng chỉ còn là "đã mở Home".

Chiều còn lại: dòng log bắn **lúc nút bật / nhận data**, trước khi biết icon có nằm trong tầm mắt —
icon có thể ở slot gộp (`IconPosition.ExpandCenter` / `UIIconSlot`) mà người chơi chưa bung.
⚠ `UIIconSlot` thuộc module UIModular, **không có trong repo này**, nên chỗ này doc chỉ khẳng định
"bắn lúc nút bật", **không** khẳng định mức hiển thị thật.

→ Nên `eligible` ML hiện đọc đúng là *"đã về Home trong mùa và icon đã bật"*, không phải *"đủ điều
kiện"*. Dời vào tầng dữ liệu thì mẫu số **không còn phụ thuộc Home**, còn thứ ML đang vô tình đo
(icon đã bật) chuyển sang `OnIconShown()` — đo đúng chỗ của nó.


**Sửa 2 chỗ, không đổi contract, không mất số nào:**

**(1)** `MasterLeagueTracking.OnDataUpdated()` — thêm một dòng. Điều kiện giữ **y nguyên**
`ShouldShowIcon()`, vì hàm đó đã thuần dữ liệu (`DeclareMasterLeague.cs:897`:
`IsRemoteEnabled() && config != null && !HasEnded() && config.IsUnlocked()`):

```csharp
public static void OnDataUpdated()
{
    Tracker.OnDataUpdated();
    RememberSeasonCycle();
    if (DeclareMasterLeague.ShouldShowIcon()) OnEligible();   // ← THÊM: mẫu số theo dữ liệu, mỗi phiên
    LogReachedStages();
}
```

**(2)** `MasterLeagueButton.cs:30,54` — **thêm** impression icon cạnh đó, để cái ML đang đo (icon đã
bật) không bị mất mà còn đo giống hai event kia. Hai lời gọi `OnEligible()` cũ **giữ hay bỏ đều
được** (van lọc nén 1 dòng/mùa nên không đếm đôi) — đúng luật *THÊM, KHÔNG BỎ* của pha này:

```csharp
// MasterLeagueTracking — copy y hệt KeyCollectionTracking:11,30
public const string SURFACE_ENTRY = "entry";
public static void OnIconShown() => Tracker.Impression(SURFACE_ENTRY);
```

```csharp
// MasterLeagueButton — thay cả hai chỗ
if (DeclareMasterLeague.ShouldShowIcon()) MasterLeagueTracking.OnIconShown();
```

**Kiểm:** user đủ điều kiện mà **chưa về Home** (app mở thẳng vào ván) → vẫn có `eligible`, và mỗi
phiên tối đa một dòng (van lọc SDK nén). Icon hiện n lần → `f_sdk_ui_exposure` surface
`master_league_entry` ra **một cụm**, không phải n dòng.

⚠ Việc **G1** ở phần B vẫn còn giá trị **chỉ khi** điều kiện đủ-tư-cách thật của game khác
`config.IsUnlocked()` (vd knit.jam: *thắng hết level chapter đang có*). Lúc đó điều kiện đó phải vào
**tầng dữ liệu của module** (hook custom), **không** quay lại gate bằng icon.

## 🔴 S2 — câu hỏi hỏi server, nhưng vấn đề thật nằm ở CLIENT và sửa được ngay

Dev hỏi: *"`progressionStage` là 'số chặng đã xong' hay 'chặng đang ở'?"*. Đọc code thì **câu hỏi đó
không quyết định vòng lặp milestone**, vì vòng lặp **không dùng `progressionStage`**:

```csharp
// MasterLeagueTracking.LogReachedStages()
var stageIndex = DeclareMasterLeague.GetStageIndex();   // = GetStageIndexByScore(data.score)
for (var index = 0; index <= stageIndex; index++)
    Tracker.Milestone(StageNumber(index));
```

`GetStageIndex()` suy từ **điểm** (`score`) + ngưỡng trong config, tức nó là **"chặng đang ở"**. Mà
vòng lặp bắt đầu `index = 0` và dùng `<=`, nên **vừa `joined` là `milestone(1)` bắn ngay** dù chưa
qua mốc nào. Hệ quả: `milestone(1)` ≡ `join`, và cả phễu mốc **lệch một bậc** — `milestone(k)` đang
mang nghĩa *"đang ở chặng k"* chứ không phải *"đã xong chặng k"*.

**Sửa được ngay, không cần chờ server** — làm đúng như Key Collection đang làm: so **ngưỡng**, config
đã có `GetThresholdAt(globalStepIndex)`:

```csharp
// chỉ bắn cho chặng ĐÃ ĐẠT NGƯỠNG
for (var index = 0; index < stepCount && config.GetThresholdAt(index) <= data.score; index++)
    Tracker.Milestone(StageNumber(index));
```

Khi đó `milestone(k)` = *"đã đạt ngưỡng chặng k"* — nghĩa rành rọt, **so được với Key Collection**, và
không phụ thuộc server hiểu `progressionStage` thế nào.

⚠ **Câu hỏi server vẫn còn giá trị, nhưng cho chỗ khác**: `progressionStage` được dùng ở
`MasterLeagueCustomInfoRepository` → nó lên **mọi dòng log** dưới tên `eventMasterLeagueStage`. Data
team cần biết cột đó là "đã xong" hay "đang ở" để đọc đúng. Đó là câu duy nhất cần server trả lời.

---

# 🔁 THÊM HỢP ĐỒNG LIVE-OP — PHA 1: chỉ THÊM, không bỏ gì

> Chốt 15/10/2026. Hợp đồng: `docs/design/LiveOpsEventContract.md` · Khuôn:
> `docs/design/LiveOpsTrackingPattern.md`. SDK **BigData 1.5.0** đã có đủ API; chờ loader ký
> `f_sdk_live_ops_data` trước khi **game ship** (up package thì không chặn).
> **Khoá event của event này: `master_league`** (để trần, không tiền tố `event_`).
>
> **Luật của pha này: THÊM, KHÔNG BỎ.** Mọi lời gọi đang chạy giữ **nguyên 100%** — dashboard hiện
> tại không được phép vỡ. Hai stream chạy song song tới khi dashboard mới thay thế xong.


### [ ] L1 — THÊM: khai đợt một lần

```csharp
// Khi data mùa về (gọi lại mỗi lần mở app cũng được — ghi đè bằng giá trị mới nhất)
FalconBigDataController.LiveOps.DeclareCycle(
    "master_league", cycleId: leagueId, cycleIndex: season,
    cycleStartTs: seasonStartMs, cycleEndTs: seasonEndMs);
```

Helper `CycleStep(...)` và việc M2 **giữ nguyên** — funnel cũ vẫn cần nó.

✅ Chỗ này cũng gỡ việc **M4** đang treo: `cycleStartTs` chỉ cần đưa vào **một chỗ** là
`DeclareCycle`, không phải bê qua từng bước.

### [ ] L2 — THÊM: năm bước live-ops, **song song** với funnel cũ

```csharp
// Trong class MasterLeagueTracking — gói CẢ HAI đường vào một hàm:
private void Step(string funnelAction, string liveOpsAction, int priority, int? stage = null)
{
    FalconBigDataController.Funnel.OnStep(CycleStep(leagueId, funnelAction, priority)); // CŨ, giữ
    FalconBigDataController.LiveOps.OnStep("master_league", liveOpsAction, stage: stage); // MỚI
}
```

Năm mốc map 1-1: `eligible` (mỗi phiên) · `open` · `join` · `complete` · `claim` (kèm `stage`).

### [ ] L3 — THÊM: mốc Stage (cái này là **thêm thuần**, không trùng gì)

```csharp
FalconBigDataController.LiveOps.OnStep("master_league", FLiveOpsAction.Milestone, stage: leagueStage);
```

Hiện Master League **không có dòng mốc nào** — "Max Stage" phải đọc vòng qua dòng thưởng nên lẫn
với "đã nhận quà". Thêm dòng này thì `max(live_ops_stage)` là xong, và **không đụng** chart nào
đang chạy.

### [ ] L4 — THÊM: `Stamp` lên dòng level và dòng thưởng

```csharp
FalconBigDataController.LiveOps.Stamp(levelParam, "master_league");          // ML có bộ level riêng
FalconBigDataController.LiveOps.Stamp(rewardCtx, "master_league", stage: leagueStage);
```

`Stamp` chỉ **điền thêm cột** vào param đang gửi — số dòng không đổi.
**Không truyền `stage`** cho dòng level: ML có hai thang (ván = `league_round`, bậc thưởng =
Stage); thang ván giữ ở `league_round` như cũ.

### Giữ nguyên — KHÔNG bỏ gì ở pha 1

| Thứ | Trạng thái |
|---|---|
| Funnel `event_master_league` (5 bước, kèm `cycleIndex`/`cycleEndTs`/`cycleStartTs`) | **Giữ** |
| `league_id` trên extras/detail | **Giữ** (song song với `live_ops_cycle_id`) |
| `detail.league_stage` | **Giữ** (song song với `live_ops_stage`) |
| `league_round`, `league_rank`, `league_multiplier`, `league_difficulty`, `eventMasterLeague*` | **Giữ lâu dài** — chiều phân tích riêng, không cột chung nào thay được |

### Hai tên một giá trị (dùng để kiểm chéo)

| Cũ | Mới | Phải bằng nhau |
|---|---|---|
| `league_id` | `live_ops_cycle_id` | ✓ |
| `detail.league_stage` | `live_ops_stage` | ✓ trên dòng thưởng |

Lệch nhau = module gắn sai — bắt được bằng một câu query **trước khi** dashboard mới lên thật.

### Pha 2 — bao giờ mới được BỎ cái cũ

Đủ **cả bốn** mới bỏ, và bỏ riêng cho event này:

1. Dashboard mới dựng xong và **khớp số với dashboard cũ trong TRỌN một đợt** (không phải vài ngày).
2. ~~Loader đã ký `f_sdk_live_ops_data`~~ → ✅ **ĐÃ KÝ 20/10**. Còn lại: seed mapping rule.
3. Bỏ **đúng ranh giới đợt**. Bỏ giữa đợt là có một đợt nửa cũ nửa mới — mọi phân vị và mẫu số của
   đợt đó hỏng mà trông vẫn hợp lý. Đây là cái bẫy lớn nhất của cả đợt chuyển.
4. Đã ghi mốc era (phía kho đọc được từ `sdk_version` + `live_ops_event IS NOT NULL`).

### Hai chỗ dễ sai khi làm pha 1

- ⚠ **Gói hai đường vào MỘT hàm** của class tracking event này, đừng để call site tự gọi từng bên.
  Sớm muộn có người thêm một mốc ở một bên và quên bên kia — lệch kiểu đó không ai thấy.
- ⚠ **KHÔNG gọi thêm `Resource.OnEarned` để "chở ngữ cảnh live-ops"**. `Stamp` điền thêm cột vào
  chính param đang gửi, **không sinh dòng mới**. Gọi thêm là đếm đôi tiền — đúng vết sẹo log-đôi
  rank reward của Master League.

---

# ⚠ KẾT QUẢ RÀ CODE 16/10 — việc phải sửa trước khi ship

> Rà bản module dev game vừa import (16/10) đối chiếu hợp đồng
> `docs/design/LiveOpsEventContract.md` + mục 🔁 ở trên. 🔴 = làm sai số liệu, sửa trước khi ship ·
> 🟡 = nên sửa · 🟢 = đã đúng, ghi nhận để khỏi sửa lại.

**♻ RÀ LẠI 19/10** (bản dev vừa chỉnh): **R2 · R3 xong** · R1 · R4 · R5 còn treo ·
thêm **R6 🔴 (`eligible` không ai gọi)** và **R7 (lỗi SDK — mình đã vá)**.

### [ ] R1 🟡 `DeclareCycle` còn thiếu `cycleStartTs` — ⏳ CÒN TREO 19/10

> ⏳ **19/10 — chưa làm.** Vẫn `cycleIndex` + `cycleEndTs`, thiếu `cycleStartTs`. Vẫn là việc **M4**,
> vẫn một dòng.

```csharp
FalconBigDataController.LiveOps.DeclareCycle(LIVE_OPS_EVENT, leagueId,
    cycleIndex: season > 0 ? season : null,
    cycleEndTs: endTs > 0 ? endTs : null);     // ← thiếu cycleStartTs
```

Đây vẫn là việc **M4** treo từ 17/09, nhưng giờ nó rút còn **một dòng**: server cấp `timeStart` thì
thêm `cycleStartTs:` vào đúng chỗ này, không phải bê qua từng bước nữa. Thiếu nó thì mọi chart "vào
đợt được mấy ngày" và nhóm countdown phải suy từ `cycleEndTs` — mà độ dài mùa là thứ **không được
hardcode** (chốt owner 17/09).

### [x] R2 🟡 `MasterLeagueCustomInfoRepository` — GIỮ, nhưng đừng thêm field mới — ✅ ĐÚNG CAM KẾT 19/10

> ✅ **19/10 — giữ đúng.** Vẫn đúng **bốn** field `Joining/Round/Stage/Yarn`, không thêm field mới.

Bốn field `eventMasterLeagueJoining/Round/Stage/Yarn` đi theo **mọi dòng log của game** (session,
ad, iap, level…). Về nguyên tắc đây là **state-param trên wire** — dòng đầu bảng ĐỪNG-LÀM của khuôn
(`LiveOpsTrackingPattern.md` §3): suy được hết từ dòng live-ops, mà dán lên wire thì sai âm thầm.

Nhưng **event đang chạy và chart có thể đang dùng**, nên:

| | Việc |
|---|---|
| Pha 1 (bây giờ) | **Giữ nguyên bốn field.** ⚠ **KHÔNG thêm field mới** vào repository này |
| Pha 2 | Bỏ sau khi chart chuyển sang đọc `live_ops_stage` (mốc) + extras trên ván, và sau khi khớp số trọn một đợt |

### [x] R3 🟡 Pin `package.json` sai — ✅ SỬA XONG 19/10

> ✅ **19/10 — xong.** `falcon.modules.core.bigdata: 1.5.1`.

Đang pin `falcon.modules.core.bigdata: 1.3.9`, nhưng code gọi `LiveOps.*` và `ALiveOpsParam` —
chỉ có từ **1.5.0**. Pin lên **≥ 1.5.1**.

### [ ] R4 🟡 Hai nghĩa của `claim` — báo data team — ⏳ CÒN TREO 19/10

> ⏳ **19/10 — code vẫn vậy (và đúng), chỉ chờ báo phía đọc số.**

`OnProgressionStepClaimed` bắn `claim` **có** `stage` (nhận quà mốc), còn `OnRankRewardsClaimed` bắn
`claim` **không có** `stage` (nhận quà xếp hạng). Van lọc phân biệt được (khoá có `stage`), nhưng
phía đọc số phải biết luật: **`claim` có stage = quà mốc, `claim` trống stage = quà rank.**

### [ ] R5 🟡 Kiểm `OnYarnEarned` có đếm đôi không — ⏳ CÒN TREO 19/10

> ⏳ **19/10 — chưa có kết quả QA.** Code không đổi: vẫn gọi thẳng `Resource.OnEarned`.

`OnYarnEarned` gọi thẳng `Resource.OnEarned`. Nếu len vàng **cũng** đi qua kho tài nguyên
(`ResourceCollector`) thì đây là **hai dòng cho một giao dịch** — đúng vết sẹo log-đôi rank reward
của chính event này. Kiểm một lần trong QA: thắng một ván → đếm số dòng resource có
`currency = golden_yarn`, phải là **một**.

### [x] R6 🔴 `OnEligible()` KHÔNG CÓ AI GỌI — mẫu số rỗng — ✅ ĐÃ NỐI 19/10 (chiều)

> ✅ **19/10 chiều — xong.** Dev nối ở `Assets/Falcon/Game/Core/Scripts/MasterLeague/Home/MasterLeagueAccessGate.cs:223`,
> trong `Available()` — tức **đúng cổng quyết định League có nhìn thấy được hay không**, sau cả hai
> điều kiện (event đang chạy + đã qua chương). Chỗ này đúng hơn `OnDataUpdated()` mình gợi ý, vì nó
> là định nghĩa của "đủ điều kiện" chứ không phải "data vừa về".
>
> ⚠ Lượt rà trước mình báo "vẫn 0 lời gọi" là **SAI**: mình grep trong `Modules/Events`, mà lời gọi
> nằm ở `Assets/Falcon/Game`. Xin lỗi đã báo nhầm.
>
> ⚪ Góp ý nhỏ, không phải lỗi: `Available()` là một **predicate có tác dụng phụ** — tên hàm không nói
> là nó ghi log. Nhờ `_eligibleLoggedFor` nên gọi nhiều lần chỉ còn một string alloc, rẻ; nhưng ai
> gọi nó trong vòng lặp UI sẽ không biết mình đang chạm đường log. Tách `Available()` thuần +
> `NotifyEligible()` gọi ở chỗ Refresh thì đọc code đỡ giật mình.

```
$ grep -rn "MasterLeagueTracking.OnEligible" Assets --include=*.cs
0
```

Hàm viết đúng (có cả `_eligibleLoggedFor` để một lần mỗi phiên mỗi đợt) nhưng **không một chỗ nào
gọi** → Master League **không bao giờ có dòng `eligible`**, ở cả funnel cũ lẫn log live-ops. Đây
đúng là vết sẹo **G1**: không có mẫu số thì mọi chart tỷ lệ phải rơi về `open` hoặc `join` làm mẫu
số, mà rơi kiểu đó là *"ai đã vào thì gần như ai cũng vào"* — số đẹp, vô nghĩa.

Sai cỡ nào, bằng số. Giả sử thật ra: **10.000** người được mở cửa → **1.000** mở màn hình event →
**300** chơi thật.

| Mẫu số | Ra tỷ lệ tham gia | Người đọc hiểu thành |
|---|---|---|
| `eligible` = 10.000 (đúng) | **3%** | event gần như không mời được ai |
| `open` = 1.000 (phải xài tạm) | **30%** | một event rất ăn khách |

Sai **mười lần**, và sai **một chiều**: luôn đẹp hơn thật, vì mẫu số chỉ còn người **đã** quan tâm.
Mà 30% là con số nhìn hợp lý nên không ai nghi.

Bản 16/10 mình ghi 🟢 cho `_eligibleLoggedFor` là **sai**: mình chấm logic bên trong hàm mà không
kiểm hàm có ai gọi. Xin lỗi đã để lọt.

Sửa: gọi `MasterLeagueTracking.OnEligible()` ở chỗ **biết người chơi đủ điều kiện**, mỗi phiên —
rẻ nhất là trong `OnDataUpdated()` (data mùa về mỗi phiên) sau khi đã kiểm điều kiện mở event, hoặc
ở chỗ icon/entry point hiện ra như Race đang làm (`GoKartRaceButton` → `OnEligible()`). Van lọc của
SDK tự chặn trùng, cứ gọi thoải mái.

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

Master League chạm luật này **mỗi lần sang mùa**, không cần điều kiện gì lạ.
`MasterLeagueLeaderboard` gọi `OnOpen()` (mùa **đang chạy**, N+1) rồi `OnSeasonComplete(rank)` (mùa
**vừa đóng**, N — `ResultsLeagueId()` lấy theo `LeaderboardSeason`):

1. `open(N+1, "")` → bản ghi là mùa N+1.
2. `complete(N)` / `claim(N)` → đợt khác → **reset** → sổ mốc của mùa N+1 bị xoá, `enter_ts` thành
   *"lúc tôi mở xem bảng mùa cũ"*.
3. Bước hoặc dòng level kế tiếp của mùa N+1 → reset lần nữa.

→ Toàn bộ 10 field khoảng cách + `live_ops_enter_ts` của **mùa đang chạy** sai, mỗi mùa một lần, với
đúng những user chịu vào xem kết quả mùa trước.

Kèm một triệu chứng thứ hai cùng gốc, nhẹ hơn nhưng dễ gây tranh cãi khi đối chiếu số: ở nhánh
`else` của `Step()`, dòng `complete`/`claim` mang `cycleId` của mùa **đã đóng** và `cycleIndex` của
mùa đã đóng, nhưng `cycleStartTs`/`cycleEndTs` vẫn do SDK điền từ **bản khai của mùa đang chạy** →
một dòng mang lịch của hai mùa.

Lỗi nằm ở `LiveOpsLogService` của **module BigData**, không phải ở module event — và cách sửa cũng ở
SDK, hai luật:

| Luật | Nội dung |
|---|---|
| **Lượt rỗng** | Bước không mang `roundId` (`eligible`/`open`) **không được** đổi thân phận lượt của bản ghi, nên không reset |
| **Đợt đã đóng** | Dòng mang đợt **khác bản khai hiện tại** là **chỉ đọc**: không reset sổ, không mượn lịch (`cycle_start_ts`/`cycle_end_ts`) của đợt đang chạy, và các field khoảng cách **vắng mặt** thay vì bịa số |

Vá ở **BigData 1.5.2**. Module **không phải làm gì** — và **đừng** sửa ngược lại cách đang viết ở `Step()`.

### 🟢 Đã đúng — đừng sửa lại

- `Step()` **gói cả hai đường** (funnel cũ + live-ops) vào một hàm → không có đường để hai bên lệch
  nhau. Đây đúng là cách mục 🔁 yêu cầu.
- Dòng level `Stamp` event + đợt nhưng **không** truyền `stage` — đúng, vì ML có hai thang
  (`league_round` cho ván, Stage cho bậc thưởng).
- `RankRewardContext` trả **context** cho bộ phát thưởng → không sinh dòng thứ hai.
- ~~`_eligibleLoggedFor` → `eligible` một lần mỗi phiên mỗi đợt~~ — **rút 19/10**: logic trong hàm
  đúng, nhưng **hàm không có ai gọi** (xem **R6**).
- **Thêm dòng `milestone`** cho mốc Stage — thứ ML chưa từng có; từ nay "Max Stage" đọc
  `max(live_ops_stage)` thay vì vòng qua dòng thưởng.
- Prefix `league_*` đã xong (việc **M5**).

---

# ✅ TODO

## A. Module Master League — owner module làm

File chính: `Scripts/Runtime/Analytic/MasterLeagueTracking.cs`

### [ ] M5 — Đổi tên 6 hằng key (dễ nhất, làm trước)

Trong `MasterLeagueTracking.cs`, sửa **giá trị chuỗi** của 6 hằng (tên hằng giữ nguyên):

```csharp
public const string KEY_ROUND      = "league_round";       // cũ: "round"
public const string KEY_MULTIPLIER = "league_multiplier";  // cũ: "multiplier"
public const string KEY_DIFFICULTY = "league_difficulty";  // cũ: "difficulty"
public const string KEY_RANK       = "league_rank";        // cũ: "rank"
public const string KEY_STAGE      = "league_stage";       // cũ: "stage"
public const string KEY_STEP       = "league_step";        // cũ: "step"
```

Không đổi: `KEY_LEAGUE_ID`, `KEY_LEAGUE_START_DAY`.
**Kiểm:** log level Start có `league_round`; dòng thưởng có `detail.league_stage`.

### [ ] M3 — Bỏ tên mùa dự phòng

Thay **toàn bộ** property `LeagueId` bằng:

```csharp
public static string LeagueId
{
    get
    {
        var data = DeclareMasterLeague.GetData();
        return data is { season: > 0 } ? LEAGUE_ID_PREFIX + data.season : null;
    }
}
```

Xoá hằng `SEASON_FALLBACK_FORMAT` (không còn dùng).
**Kiểm:** tắt mạng, mở app → không có dòng funnel nào mang `cycleId` dạng `master_league_2026…`.

### [ ] M2 — Gắn thông tin mùa lên eligible / open / join

**Bước 1.** Thêm hàm này vào `MasterLeagueTracking`:

```csharp
private static FunnelParam CycleStep(string leagueId, string action, int priority)
{
    var param = new FunnelParam
    {
        funnelName = FUNNEL_NAME,
        cycleId = leagueId,
        action = action,
        priority = priority
    };

    var season = DeclareMasterLeague.GetData()?.season ?? 0;
    if (season > 0) param.cycleIndex = season;

    var endTs = SeasonEndTs();
    if (endTs > 0) param.cycleEndTs = endTs;

    return param;
}
```

**Bước 2.** Trong 3 hàm, thay đoạn `FalconBigDataController.Funnel.OnStep(...)` bằng một dòng:

```csharp
// OnEligible():
FalconBigDataController.Funnel.OnStep(CycleStep(leagueId, ACTION_ELIGIBLE, PRIORITY_ELIGIBLE));

// OnOpen():
FalconBigDataController.Funnel.OnStep(CycleStep(leagueId, ACTION_OPEN, PRIORITY_OPEN));

// OnJoin() — xoá luôn đoạn tự set cycleIndex / cycleEndTs cũ:
FalconBigDataController.Funnel.OnStep(CycleStep(leagueId, FFunnelAction.Join, PRIORITY_JOIN));
```

**Kiểm:** dòng `eligible`, `open`, `join` đều có `cycleIndex` và `cycleEndTs`.

### [ ] M1 — Ghi "đã mở" ở popup trước khi join

Thêm **một dòng** vào `OnEnable()` của 2 file:

```csharp
// Scripts/Runtime/UI/Join/MasterLeagueJoinPopup.cs
protected override void OnEnable()
{
    base.OnEnable();
    DeclareMasterLeague.IsSessionPopupUnlockShowed = true;
    MasterLeagueTracking.OnOpen();          // ← THÊM
    Behaviour?.OnEnable();
}

// Scripts/Runtime/UI/Unlock/MasterLeagueUnlockPopup.cs
protected override void OnEnable()
{
    base.OnEnable();
    DeclareMasterLeague.IsSessionPopupUnlockShowed = true;
    MasterLeagueTracking.OnOpen();          // ← THÊM
}
```

Giữ nguyên lời gọi `OnOpen()` đang có trong `MasterLeagueLeaderboard`.
**Kiểm:** user **chưa join** mở popup → có dòng `open`.

### [ ] M4 — Gắn giờ bắt đầu mùa (làm sau khi server xong S1)

Khi config Master League đã có `timeStart` (epoch ms UTC, cùng kiểu `timeEnd`), thêm vào
`CycleStep` ở M2, ngay dưới dòng `cycleEndTs`:

```csharp
var start = DeclareMasterLeague.GetConfig()?.timeStart ?? default;
if (start != default)
    param.cycleStartTs = (long)(DateTime.SpecifyKind(start, DateTimeKind.Utc) - UnixEpoch).TotalMilliseconds;
```

**Kiểm:** dòng `eligible`, `open`, `join` có `cycleStartTs`.

### [ ] Phát hành module

- `package.json`: thêm dependency `"falcon.modules.core.bigdata": "1.3.8"` (hoặc bản game đang
  dùng nếu cao hơn); tăng version module.
- CHANGELOG module ghi: "đổi tên key extras sang `league_*`" — **báo data team số bản** để họ đọc
  dữ liệu cũ/mới đúng mốc.

## B. Game — dev từng game làm

### [ ] G1 — Gọi `OnEligible()` mỗi lần về Home

Chỗ game xử lý **vào Home / mở app** (chỗ nào chạy mỗi phiên là được):

```csharp
// Gọi lặp thoải mái — SDK tự nén còn 1 dòng/mùa.
var config = DeclareMasterLeague.GetConfig();
if (config != null && !DeclareMasterLeague.HasEnded() && DaThangHetLevelChapterHienCo())
    MasterLeagueTracking.OnEligible();
```

`DaThangHetLevelChapterHienCo()` = hàm game tự có: user đã thắng **hết level chapter đang có**.

⚠ **KHÔNG** gọi ở khoảnh khắc "vừa xong chapter" — user xong chapter từ mùa trước sẽ không bao giờ
có dòng này.
**Kiểm:** user xong chapter từ lâu, mở app giữa mùa → có dòng `eligible`.

### [ ] G2 — Round league chỉ log một lần (bee.flow đang lỗi)

Module **đã tự log** level cho round. Tìm chỗ game gọi `FLevelManager.OnLevelStart(...)` và
`FLevelManager.OnLevelResult(...)`, bọc lại:

```csharp
if (!DeclareMasterLeague.IsPlayingRound)     // ← round league thì KHÔNG gọi
    levelManager.OnLevelStart(...);

if (!DeclareMasterLeague.IsPlayingRound)
    levelManager.OnLevelResult(...);
```

⚠ `FLevelManager.OnLevelResult(win: true)` còn **tăng level chapter** (`LevelData.level++`). Round
league đi qua đó là thắng round = lên level chapter — kiểm luôn. Nếu game đang dựa vào
`FLevelManager` cho việc khác ngoài log (vd gửi kết quả lên server) thì báo team BigData trước khi
sửa.
**Kiểm:** chơi 1 round → đúng **1** dòng Start + **1** dòng Pass/Fail, dòng Start có `league_id`.

### [ ] G3 — Báo tên key revive

Nhắn data team: trong `boostersUsed`, revive/hồi sinh dùng key tên gì. (knit.jam đã báo:
`revive`. bee.flow: còn thiếu.)

### [ ] Nâng phiên bản (theo từng game)

| Game | Cần nâng |
|---|---|
| knit.jam | Master League (bản có M1–M5), BigData ≥ 1.3.8, Events Core legacy bản có `Grant(..., context)`, GameData ≥ 1.1.7 — **nâng đủ cả 4**, chỉ nâng Master League thì dòng thưởng vẫn thiếu thông tin |
| bee.flow | Master League (bản có M1–M5) + làm G1, G2, G3 |

## C. Server / backend event

### [ ] S1 — Thêm `timeStart` vào config Master League

Epoch ms UTC, cùng định dạng `timeEnd`. Cần cho M4.

### [~] S2 — Chốt nghĩa `progressionStage` — phần **log mốc đã xong** (vòng rà 3), chỉ còn field ambient

> **Cập nhật 21/10 17:00:** vòng lặp `milestone` **không còn dùng `progressionStage`** (nó so ngưỡng
> điểm — xem vòng rà 3), nên câu hỏi này **không còn chặn phễu mốc**. Chỗ duy nhất còn đọc trực tiếp
> field đó là **trạng thái ambient** `eventMasterLeagueStage`
> (`MasterLeagueCustomInfoRepository.cs:21` = `data?.progressionStage ?? 0`) — nên câu hỏi với server
> thu hẹp lại: *field đó là "số chặng đã xong" hay "chặng đang ở"*, để data team biết đọc cột ambient
> ấy thế nào. Không ảnh hưởng `live_ops_stage`.

Trả lời một câu: số này là **số chặng đã hoàn thành** (0 = chưa qua chặng nào) hay **chặng đang
ở** (0 = đang ở Gold)? Và 1/2/3 ứng với chặng nào.

---

# 📖 Giải thích & tham chiếu (không cần đọc để sửa)

## 1. Vì sao từng việc

| Việc | Thiếu thì sao |
|---|---|
| M1 | `open` hiện chỉ gọi ở leaderboard — chỉ vào được sau khi join → tỉ lệ join/open luôn ≈ 100%, vô nghĩa (chỉ số 2) |
| M2 | Không chia được nhóm countdown cho người xem mà không tham gia (chỉ số 2) |
| M3 | Chưa biết mùa, module tự đặt tên `master_league_<yyyyMMdd>` → không khớp `master_league_<season>` của dòng join, không nối được |
| M4 | Không có giờ mở mùa trên log → không tính được mốc bắt đầu mùa, ngày launch (chỉ số 1). Phía data **không hardcode** độ dài mùa — dòng thiếu `cycleStartTs` là thiếu dữ liệu. Config hiện chỉ có `timeEnd` + `duration` (mẫu `2.0`, không rõ đơn vị, không code nào dùng) → cần S1 |
| M5 | Key chung chung (`stage`, `round`, `rank`…) đụng event khác gắn lên cùng loại log: extras trải phẳng theo luật ai-ghi-trước-giữ → event sau mất số im lặng; cùng tên khác kiểu (`stage` chữ ở đây, số ở Race) làm hỏng cột kho |
| G1 | Mất tệp gốc "user đã hoàn thành chapter" của chỉ số 2. Module không tự biết điều kiện chapter |
| G2 | Mỗi round hai bộ dòng level; Pass/Fail rơi vào lượt chơi của dòng Start không có `league_id` → mất liên kết round (chỉ số 3, 6). Data đo: 86% round trên bee.flow |
| G3 | Không tính được revive rate (chỉ số 6) |
| S2 | Client đánh chỉ số chặng từ 0 theo chặng đang cày (GOLD = 0). Nếu server cùng quy ước thì "0 = chưa đạt" là đọc sai, Max Stage lệch một bậc |

G2 còn cách thứ hai (chỉ chọn khi game cần field riêng của `FLevelManager`, vd key `buyMoreTime`
nó tự thêm vào `boostersUsed`): giữ `FLevelManager`, **tắt** log round trong module, và tự truyền
`league_id` / `league_round` / `league_start_day` vào `extraMeta` của `OnLevelStart`.

**Tình trạng dữ liệu thật (data đối soát 17/09):**

| Game | Đang lệch |
|---|---|
| knit.jam | Module cũ: funnel tên `master_league`, không có `cycleEndTs` (android + ios); thưởng progression đi đường GameData cũ → mất `resourceWhere` + `detail`; chưa thấy `open` trước join |
| bee.flow | Round log hai lần qua FLevelManager (86% round); chưa thấy `open` trước join và `eligible` |

**Lưu ý chất lượng dữ liệu đang biết:**

- Dòng thưởng rank/progression đi qua GameData: `itemType` và `itemId` đang bị điền bằng
  `resourcePlace` (mặc định `event_master_league`) — **lỗi phía GameData, đừng dùng hai cột này**
  cho thưởng; dùng `currency` (= tên phần thưởng) + `resourceWhere` + `detail`.
- Phần thưởng dạng **thời gian x2 Collectible** không đi qua kho tài nguyên → **không có dòng
  resource**.

Tên funnel **`event_master_league`**. Tên cũ `master_league` là dữ liệu lỗi đời trước — **không
JOIN lẫn** với tên mới.

## 2. Khoá chung

| Khoá | Giá trị | Ở đâu |
|---|---|---|
| `league_id` | `master_league_<season>` (vd `master_league_12`) | `cycleId` của funnel; key `league_id` trên level Start và `detail` của resource |
| `league_start_day` | `yyyy-MM-dd` — ngày user **join** mùa này | level Start, `detail` resource. Funnel dùng cột `funnelDay` (SDK tự đóng, cùng nghĩa) |
| `playTurnId` | SDK tự sinh | Nối Start ↔ Pass/Fail của cùng một round |

### 2b. Tiền tố key — luật và bảng đổi tên (việc M5)

Mọi key extras/detail **riêng của event** mang tiền tố `league_` — tránh đụng với event khác gắn
lên cùng loại log (luật chung: `LiveOpsTrackingPattern.md` §1). Key đã có tiền tố giữ nguyên
(`league_id`, `league_start_day`, `eventMasterLeague*`).

| Log | Key cũ | Key mới |
|---|---|---|
| Level Start (phẳng) | `round` | `league_round` |
| Funnel complete (phẳng) | `rank` | `league_rank` |
| Resource `detail` | `multiplier` | `league_multiplier` |
| Resource `detail` | `difficulty` | `league_difficulty` |
| Resource `detail` | `rank` | `league_rank` |
| Resource `detail` | `stage` | `league_stage` |
| Resource `detail` | `step` | `league_step` |

Dữ liệu đã lên với key cũ (bee.flow) là **era cũ** — data đọc hai tên theo mốc bản module có M5.
`difficulty` trên dòng level là field CHUẨN của level, **không** đổi.

## 3. Danh mục log

### 3.1 Funnel — `f_sdk_funnel_data`

`funnelName = event_master_league`, `cycleId = league_id`, `funnelShape = once_per_cycle`
→ **mỗi bước tối đa 1 dòng / user / mùa**.

| `action` | `priority` | Bắn khi | Trường thêm |
|---|---|---|---|
| `eligible` | 10 | Game gọi — user đang đủ điều kiện trong mùa *(việc G1)* | `cycleIndex`, `cycleEndTs` *(M2)*, `cycleStartTs` *(M4)* |
| `open` | 15 | User mở UI league *(việc M1: phải có ở popup trước join)* | `cycleIndex`, `cycleEndTs` *(M2)*, `cycleStartTs` *(M4)* |
| `join` | 20 | Server xác nhận join | `cycleIndex` = số mùa, `cycleEndTs` = giờ kết thúc mùa (epoch ms UTC), `cycleStartTs` = giờ bắt đầu mùa *(M4)* |
| `complete` | 30 | User **mở leaderboard thấy kết quả** mùa đã đóng | `league_rank` (vắng nếu chưa biết rank) |
| `claim` | 40 | User nhận thưởng xếp hạng | — |

⚠ `complete` = "đã xem kết quả", **không phải** "mùa kết thúc" — user không mở leaderboard thì
mùa đó không có dòng complete.

### 3.2 Level (mỗi Round) — `f_sdk_level_data`

| `status` | Trường liên quan |
|---|---|
| `Start` | `currentLevel` = **vị trí trong bộ level của mùa** (không phải số level chapter); `difficulty` = `normal` / `hard` / `very_hard`; key phẳng `league_id`, `league_round`, `league_start_day` |
| `Pass` | `score`, `duration`, `movesUsed`, `boostersUsed` |
| `Fail` | như Pass + `levelProgress` (= score/totalScore×100); quit giữa round: `failReason = Quit` |

- `league_round` = số thứ tự round hiện tại, **chỉ tăng khi thắng** (thua thì chơi lại round đó).
- Pass/Fail **không lặp lại** `league_id` — lấy qua `playTurnId` từ dòng Start.
- Dòng level **có `league_id`** là round league; **không có** là level chapter.

### 3.3 Resource — `f_sdk_resource_data`

Chung: `flowType = Source`, `resourceWhen = event_master_league`.

| `resourceWhere` | Bắn khi | `currency` / `amount` | `detail` | `exchangeId` |
|---|---|---|---|---|
| `level_end` | Thắng round | `golden_yarn` / số len **đã nhân** độ khó và x2 | `league_id`, `league_start_day`, `league_multiplier` (hệ số x2 Collectible; 1 = không có), `league_difficulty` | — |
| `rank_reward` | Nhận thưởng xếp hạng | tên phần thưởng / số lượng | `league_id`, `league_start_day`, `league_rank` | chung cho cả lần nhận |
| `progression_reward` | Nhận thưởng một step progression | tên phần thưởng / số lượng | `league_id`, `league_start_day`, `league_stage` (`gold`/`emerald`/`diamond`), `league_step` (1..N, đếm xuyên chặng) | chung cho các món của một step |

Thua / quit round: **không có dòng resource** (vắng mặt chính là dữ liệu).

### 3.4 Trạng thái gắn trên MỌI log (custom params)

Module gắn thêm 4 trường vào lớp custom params của **mọi event** (đọc tươi lúc log):

| Key | Nghĩa |
|---|---|
| `eventMasterLeagueJoining` | `true` nếu user đã join và mùa còn chạy |
| `eventMasterLeagueRound` | round hiện tại |
| `eventMasterLeagueStage` | chặng progression — ⚠ **nghĩa chưa chốt**, xem ghi chú dưới |
| `eventMasterLeagueYarn` | tổng len vàng mùa này |

⚠ `eventMasterLeagueStage` do **server** gửi xuống (`progressionStage`), client chỉ chép lại.
Dữ liệu thật: bắt đầu từ 0. Nhưng config phía client đánh chỉ số chặng **từ 0 theo chặng đang
cày** (GOLD = 0, EMERALD = 1, DIAMOND = 2) — nếu server cùng quy ước thì `0` là "đang ở Gold",
không phải "chưa đạt chặng nào", và Max Stage lệch một bậc. **Người viết API server cần chốt**:
số này là *số chặng đã hoàn thành* hay *chặng đang ở*. Chưa chốt thì dùng `detail.league_stage` của
dòng `progression_reward` (chỉ số 4).

Đây là **ảnh chụp trạng thái lúc log**, không phải nguyên nhân: một giao dịch IAP có
`eventMasterLeagueJoining = true` chỉ nói "mua **trong lúc** đang tham gia", không nói "mua **vì**
league".

## 4. Công thức cho 7 chỉ số GD

**Nhóm countdown** (dùng ở chỉ số 2, 3, 4, 5, 6, 7):

```
countdown_h = (cycleEndTs − ts) / 3 600 000
  ≥ 96h        → nhóm "≥ 4 ngày"
  24h – 96h    → nhóm "1–4 ngày"
  < 24h        → nhóm "< 1 ngày"   (thực tế 4h–24h: luật không cho bắt đầu khi countdown < 4h)
```

- Người **tham gia**: `ts` = dòng `join`.
- Người **xem mà không tham gia**: `ts` = dòng `open`. Van chỉ giữ dòng `open` ĐẦU TIÊN của mùa →
  nhóm của họ là nhóm tại **lần xem đầu** (mở popup ngày 1 rồi ngày 6 vẫn thuộc "≥ 4 ngày").
- Giờ bắt đầu mùa = `cycleStartTs` (việc M4). Không có thì để trống — không suy ra.

GD viết lẫn "≥ 4 ngày" và "> 4 ngày" giữa các chỉ số — cần chốt một mốc, đề xuất `≥ 96h`.

**"Khi tham gia vs khi không tham gia"** (chỉ số 5, 6, 7) — hai cách so, đều không cần log thêm:
- **Cùng user, trước/sau**: tuần event (từ `ts_join`) so với tuần liền trước của chính user đó.
- **Cùng tệp, join vs không join**: user có `eligible` + có `join` so với user có `eligible`
  nhưng không `join` trong cùng mùa (cần việc G1).

| # | Chỉ số | Cách tính |
|---|---|---|
| 1 | Churn tệp end-game sau launch | Tệp = user có dòng level **không có `league_id`** đạt `currentLevel` = level chapter cao nhất tại ngày launch. Churn = không còn `f_sdk_session_data` sau N ngày kể từ ngày launch. So với tệp tương đương trước launch. Ngày launch ≈ giờ bắt đầu của mùa sớm nhất có trong log (`min(cycleStartTs)` — cần M4) — lệch nếu event bật giữa mùa hoặc bản app lên store muộn, nên đối chiếu lịch release |
| 2 | % tham gia sau khi xem popup (trong tệp đã hoàn thành chapter) | Theo `cycleId`: tệp = distinct user có `eligible`; đã xem = có `eligible` **và** `open`; tham gia = có thêm `join`. Tỉ lệ = tham gia / đã xem. Nhóm countdown: người join theo dòng `join`, người chỉ xem theo dòng `open` (mục trên). Mẫu DAU: distinct user có session trong tuần event. **Phụ thuộc việc M1, M2, M3, G1** (M4 cho giờ bắt đầu mùa) |
| 3 | Max Round (phân vị 0.1 / 0.25 / 0.5, nhóm ≥ 4 ngày) | Mỗi user × `league_id`: `max(league_round)` trên dòng level `Start` có `league_id`. Lọc user thuộc nhóm countdown ≥ 4 ngày, lấy phân vị |
| 4 | Max Stage (phân vị, nhóm ≥ 4 ngày) | Mỗi user × mùa: stage cao nhất **đã nhận thưởng** = max `detail.league_stage` trên `progression_reward` (thứ tự gold < emerald < diamond). Stage **đã đạt** (kể cả chưa nhận) = max `eventMasterLeagueStage` — ⚠ chỉ dùng sau khi server chốt nghĩa số này (mục 3.4); chưa chốt thì dùng `detail.league_stage` |
| 5 | Source / Sink | `f_sdk_resource_data` group theo `flowType`, `currency`, nhóm countdown, trước/sau hoặc join/không join. Muốn tách phần do league phát: lọc `resourceWhen = 'event_master_league'` |
| 6 | Session time / count, play time, revive rate | Session: `f_sdk_session_data`. Play time: tổng `duration` dòng level Pass/Fail. Revive rate = dòng Pass/Fail có `boostersUsed[<key revive>] > 0` / tổng dòng Pass/Fail — key theo game ở bảng dưới. Tách round league / level chapter bằng `league_id` (qua `playTurnId`) |
| 7 | Paying rate, ARPDAU, ARPPDAU | `f_sdk_in_app_data` theo nhóm countdown, trước/sau hoặc join/không join |

**Key revive trong `boostersUsed` theo game** (chỉ số 6):

| Game | Key | Nguồn |
|---|---|---|
| knit.jam | `revive` | data đối soát 17/09 |
| bee.flow | chưa biết — đọc lại sau khi sửa G2 | nếu round đi đường FLevelManager, ứng viên là `buyMoreTime` (FLevelManager tự thêm key này) — cần game xác nhận |

Game mới: thêm dòng vào bảng trước khi dựng biểu đồ.

Số liệu phụ cho GD:

```
len vàng / user / mùa    = SUM(amount) WHERE resourceWhere='level_end' GROUP BY user, detail.league_id
hiệu ứng x2 Collectible  = so sánh dòng level_end có detail.league_multiplier > 1 vs = 1
win-rate theo độ khó     = Pass / (Pass + Fail) trên round league GROUP BY difficulty
giá trị thưởng rank      = SUM(amount) WHERE resourceWhere='rank_reward' GROUP BY detail.league_rank, currency
giá trị thưởng progress  = SUM(amount) WHERE resourceWhere='progression_reward' GROUP BY detail.league_stage, currency
event retention ngày N   = ngày log − league_start_day (funnel: funnelDay)
```

## 5. Không log (cố ý)

| Không log | Vì sao |
|---|---|
| Chuyển qua lại giữa UI Chapter ↔ League | Volume cao, gần như không có thông tin |
| Funnel step cho từng round | Round đã có log level |
| Cosmetic khi thắng chặng (màu profile, icon, giao diện) | Biết stage là biết cosmetic |
| Số bot trong bảng đấu | Không chỉ số nào dùng |
| Thưởng đi **cả** `Resource.OnEarned` lẫn `EventRewardGranter` | Một món thưởng hai dòng → đếm đôi. Thưởng chỉ đi đường `Grant(..., context)` |

## 6. Đăng ký với data team

- Funnel `event_master_league`, shape `once_per_cycle`, action: `eligible`, `open`, `join`,
  `complete`, `claim`.
- `resourceWhen`: `event_master_league`. `resourceWhere`: `level_end`, `rank_reward`,
  `progression_reward`.
- Key phẳng trên level: `league_id`, `league_round`, `league_start_day`. Key phẳng trên funnel
  complete: `league_rank`. Key trong `detail` resource: `league_id`, `league_start_day`,
  `league_multiplier`, `league_difficulty`, `league_rank`, `league_stage`, `league_step`.
- Custom params: `eventMasterLeagueJoining`, `eventMasterLeagueRound`, `eventMasterLeagueStage`,
  `eventMasterLeagueYarn`.
