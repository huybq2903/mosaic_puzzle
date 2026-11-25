# Mediation — gọi Rule chọn giá sàn, và bảy cột nó đẻ ra trên log load

> **Dev Mediation chỉ cần đọc phần TODO dưới đây.** Mỗi việc: gọi gì, truyền gì vào SDK, kiểm ra
> sao. Tick `[x]` khi xong.
> Viết 29/10/2026, theo spec *First Interstitial eCPM Floor Rule* + bảng tham số của bên
> personalization (28/10).
> Phía SDK (BigData **1.5.7**) đã làm xong phần nhận: **mọi cột dưới đây đã có cửa**, bây giờ chỉ
> thiếu người truyền.

> 🔎 **Rà 23/11/2026 với code Mediation** (`FloorPersonalization/InterstitialFloorPersonalization.cs`, `FloorRuleClient.cs`,
> `FalconMaxService.cs`), sau khi Mediation tự rà và báo 6 điểm. Trạng thái từng việc ghi ngay dưới tiêu đề của việc đó.
>
> | Việc | Trạng thái |
> |---|---|
> | **W0** nối các cột vào BigData | 🔴 **chưa làm** — việc mới, lỗ lớn nhất (xem W0) |
> | W1 gọi Rule + ca lỗi | 🟡 gọi Rule có; ca lỗi chưa lên log. **Q2 đã chốt** |
> | W2 áp sàn + retry | ✅ |
> | W3 khai 3 cột lúc gửi | 🟡 tính đúng nhưng chưa truyền vào BigData (gộp vào W0) |
> | W4 cùng mã giữa hai bảng | 🔴 viết lại — bản cũ của phiếu sai |
> | W5 impression qua `Ad.OnImpression` | ✅ |
> | W6 ba giá trị mức user | 🔴 chưa làm |
> | W7 bump version | 🟡 khi xong W0 · W1 · W6 |

## 0. Kết luận trong 30 giây

| | |
|---|---|
| **Việc là gì** | Rule chạy ở **server** (client gọi HTTP) trả về `ecpm_floor` · `retry_times` · `ecpm_floor_type` · `ecpm_floor_percentile` · `device_tier`. Mediation **gọi, chấp hành, và khai lại** cho SDK |
| **SDK không làm gì** | Không gọi Rule, không tự tính bậc máy, không tự đếm attempt. SDK chỉ chở — và tự nhớ hai số eCPM theo phiên |
| **Mediation phải làm** | **8 việc**: **W0 truyền các cột vào `Ad.OnLoadResult` của BigData** · W1 gọi Rule + timeout + vai cho ca lỗi · W2 áp sàn và chạy `retry_times` · W3 khai 3 cột lúc **gửi** request · W4 dùng CHUNG `ad_candidate_id` giữa BigData và Databuckets · W5 impression phải qua `Ad.OnImpression` · W6 cấp 3 giá trị mức USER qua interface · W7 bump version |
| **Cái dễ sai nhất** | **W0** — ghi đủ cột sang Databuckets mà quên BigData: tự rà sẽ thấy "đã làm", nhưng kho BigData trống. Sau đó là **W3** — khai vai sàn ở mốc kết quả thay vì mốc gửi là **lệch một nhịp**, đúng lỗi V6 của phiếu `Request-Mediation-AdEcpmAndParams.md`, chỉ đổi cột |
| **Điều kiện để số có nghĩa** | Game phải chạy chế độ log **TÁCH** (`fAdLoadLogSplit = true`, mặc định). Bản GỘP chỉ chở được 2 trong 7 cột |
| **Phụ thuộc phiếu kia** | **V4** của `Request-Mediation-AdEcpmAndParams.md` (`adUnitId` ở mốc show) đang chặn 4 cột trên dòng **`f_sdk_ad_show_data`**: mốc đó không mang `ad_unit_id` nên SDK không tra được sổ theo unit. Làm V4 xong thì `ad_candidate_id` + 3 cột sàn tự có ở đó, **SDK không phải sửa gì** |

---

## [ ] W0 🔴 — Truyền các cột vào BigData, không chỉ sang Databuckets *(thêm 23/11)*

**Hiện trạng (rà 23/11):** workflow đã tính đủ và đúng các giá trị (vai, percentile, lần thử, mã candidate — chụp lúc gửi, đúng W3),
nhưng chúng chỉ đi sang module Databuckets qua `LoadContextParams` / `ShowContextParams`. `ReportLoad` gọi BigData **chỉ với
`ecpmFloor`**. Trên kho BigData vì vậy:
- `ecpm_floor_type` · `ecpm_floor_percentile` · `ad_load_attempt` **vắng** trên mọi dòng của workflow — kể cả `no_floor`: sàn 0 bị bỏ
  khỏi payload, nên dòng không sàn trông y như dòng quên khai;
- `ad_candidate_id` là mã **BigData tự cấp**, khác mã Mediation gửi Databuckets ⇒ hai bảng không nối được (W4).

**Sửa — trong `ReportLoad`:**

```csharp
FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
{
    adType = AdType.Interstitial,
    adUnitId = candidate.adUnitId,
    success = success,
    adMediation = _MAX,
    networkName = network,
    ecpmFloor = candidate.ecpmFloor,
    estimatedEcpm = success ? candidate.estimatedEcpm : null,
    errorMess = error,
    loadingMs = latencyMs,
    ecpmFloorType = candidate.ecpmFloorType,             // ← THÊM (W3)
    ecpmFloorPercentile = candidate.ecpmFloorPercentile, // ← THÊM (W3)
    adLoadAttempt = candidate.loadAttempt,               // ← THÊM (W3)
    adCandidateId = candidate.candidateId                // ← THÊM (W4): CÙNG mã với Databuckets
});
```

- Dòng **impression** không phải sửa: SDK tự chép bốn giá trị này từ dòng load của cùng ad unit sang.
- Field đã có trong `AdLoadResultParam` từ BigData 1.5.7 — SDK không phải ra bản mới.

**Kiểm:** log editor, dòng `f_sdk_ad_load` của unit workflow có `ecpmFloorType` · `adLoadAttempt` · `adCandidateId`, và
`adCandidateId` trùng `ad_candidate_id` của dòng Databuckets cùng lần load.

## [ ] W1 🔴 — Gọi Rule: timeout và fallback là phần của việc, không phải phần thêm

> **Hiện trạng (rà 23/11):** gọi Rule có (First / Subsequent, đủ input). Ca lỗi rơi về load mặc định (`SwitchToDefault`) nhưng **không
> lên log**. Timeout dùng `UnityWebRequest.timeout` — chỉ nhận **giây nguyên** (làm tròn lên, tối thiểu 1 giây), mặc định 5 giây.
>
> ✅ **Q2 đã chốt (owner SDK, 23/11)** — giá trị lên log là từ vựng của kho nên SDK quyết, không chờ bên Rule:
> - `ecpmFloorType = "rule_error"`: API lỗi, response hỏng / không parse được / không hợp lệ, hoặc exception lúc gọi;
> - `ecpmFloorType = "rule_timeout"`: quá ngân sách thời gian (cần bộ đếm ms tự làm, dưới đây — `UnityWebRequest` không tách rõ
>   timeout khỏi lỗi mạng);
> - gắn lên **mọi dòng load mặc định** (MultiCall / unit đơn) của opportunity đó, tới khi opportunity kế bắt đầu;
> - **KHÔNG** gắn cho ca Control / `base_line` (flow rỗng): đó không phải lỗi. Control phân biệt bằng `realtimeVariant` (W6).
>
> Ba ca trên kho BigData sau khi làm W1 + W6: **có `realtime_variant`, không vai** = Control · **`rule_error` / `rule_timeout`** = gọi
> lỗi · **không cả hai** = game không chạy flow. SDK báo loader hai giá trị mới.
>
> **Timeout theo ms:** tự đếm bằng coroutine (`WaitForSecondsRealtime`) rồi `request.Abort()`, đánh dấu ca đó là `rule_timeout`. Ngân
> sách bao nhiêu ms là câu **Q1** của bên Rule — 5 giây khá dài cho first interstitial (người chơi đang chờ).

First interstitial là mốc **ăn latency**: người chơi đang chờ. Rule chạy ở server nên lần gọi nào
cũng có thể treo.

**Phải chốt trước khi code:** ngân sách timeout (ms), và **sàn nào dùng khi gọi lỗi**.

⚠ **Và phải khai được ca đó lên log.** Spec hiện không định nghĩa `ecpm_floor_type` cho ca gọi
lỗi/timeout ⇒ vắng vai sẽ mang hai nghĩa: *"game không chạy flow này"* và *"gọi Rule thất bại"*. Đó
đúng là bệnh hai-nghĩa vừa chữa cho `tier` (xem `DWH-EventLog-API-Spec.md` §5.11f-1). **Xin bên quyết
sàn một vai riêng** (`rule_error` / `rule_timeout`) rồi truyền thẳng — SDK không kiểm danh sách nên
không phải chờ bản SDK mới.

**Input của Rule — ba trong bốn SDK đã có sẵn, đừng tự tính lại:**

| Input | Lấy ở đâu |
|---|---|
| `retention_day` | `IFPlayerSessionService.Retention` (Devkit) — **dùng đúng số này** để Rule chia nhóm trùng với số của kho |
| `device_model` | `IFDeviceInfoRepository.DeviceModel` (Devkit) |
| `local_hour` | giờ local của máy, 0–23 |
| `country` | ❌ **SDK không có cột country trung tâm.** Đề xuất: **để server tự suy từ IP** của request — client chỉ biết locale/SIM, số đó lệch với `country` của kho và lúc cần dựng lại *"vì sao máy này nhận sàn 2.5"* thì hai bên soi hai nước khác nhau |

## [x] W2 🟡 — Áp sàn và chạy `retry_times`

> ✅ **Rà 23/11:** áp sàn qua `MultiCall.KEY_SET_BID_FLOOR` trên unit riêng của workflow, tắt auto-retry của MAX, retry đúng
> `retry_times`. Unit workflow mở cửa với `adAlgorithm = ALGORITHM_FLOOR_PERSONALIZATION`, logic MultiCall bị **chặn** trong lúc
> workflow chạy ⇒ sàn Rule **thay** sàn MultiCall, không chạy song song (Q6 trả lời bằng code).

`ecpm_floor` của Rule là USD/1000 impression — **cùng thang** với sàn động của MultiCall
(`MultiCall.KEY_SET_BID_FLOOR`), nên đường áp sàn không phải làm mới.

⚠ **Chốt với bên quyết sàn:** sàn của Rule **đè** sàn động multicall ở first interstitial, hay chạy
song song? Và **`adAlgorithm` nào** tách riêng tập dòng đó, để kho khỏi trộn hai cơ chế chọn sàn vào
một phép đo (đúng việc H2 của phiếu kia).

`retry_times` **không lên log** — nó là giá trị thi hành. Cái lên log là `adLoadAttempt` (W3).

## [ ] W3 🔴 — Khai 3 cột, và khai ở mốc GỬI REQUEST

> 🟡 **Rà 23/11:** phần khó đã đúng — vai / percentile / lần thử chụp vào candidate **lúc gửi**, lần thử về 1 mỗi lần đổi step, vai =
> tên step (đúng định nghĩa phiếu). Còn thiếu: truyền vào BigData — làm ở **W0**. Cần bên Rule xác nhận tên step trùng đủ 8 vai và có
> step `no_floor` (field `ecpm_floor_type` trong response của Rule thực ra là nhãn percentile, Mediation đúng khi không dùng nó).

```csharp
FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
{
    ...
    ecpmFloor           = floorUsed,        // số THẬT đã gửi kèm request (đã có từ 1.5.5)
    ecpmFloorType       = roleUsed,         // ← THÊM: vai của LÚC GỬI
    ecpmFloorPercentile = pctUsed,          // ← THÊM
    adLoadAttempt       = attemptAtFloor,   // ← THÊM: đếm từ 1, retry lần 2 ⇒ 3
});
```

🔴 **Bẫy chính — `ecpmFloorType` phải là ảnh chụp LÚC GỬI.** Response mà Rule trả *sau* một kết quả
load là vai của lần **SAU** (`base` + fail → `base_half`). Lấy "state hiện tại" ở mốc kết quả về là
dán nhãn của lần sau lên dòng của lần này. Khuôn đúng — y như `floorUsed`/`tierUsed` ở V7 của phiếu
kia:

```csharp
// lúc GỬI request
var roleUsed = _currentFloorType;
var pctUsed = _currentFloorPercentile;
var attemptAtFloor = ++_attemptAtCurrentFloor;   // về 1 mỗi lần ĐỔI sàn

// khi kết quả về: dùng roleUsed/pctUsed/attemptAtFloor đã chụp, KHÔNG đọc lại state
```

Trong một thang retry thì sàn không đổi ⇒ mọi dòng của thang mang **cùng** vai, chỉ khác
`adLoadAttempt`.

✅ **Và nhớ `no_floor`:** `ecpmFloor` không có cách nào chở số `0` (số không dương bị bỏ khỏi payload
từ 1.5.5), nên **`ecpmFloorType = no_floor` là đường duy nhất** khai *"cố ý không áp sàn"*. Thiếu nó
thì dòng không sàn và dòng mediation-quên-khai trông y như nhau.

ℹ **`deviceTier` KHÔNG truyền ở đây** — nó là giá trị mức USER, đi đường riêng ở **W6**.

## [ ] W4 🟡 — `ad_candidate_id`: dùng CHUNG một mã giữa BigData và Databuckets

> ✏️ **Viết lại 23/11.** Bản cũ bảo dùng chung mã với `f_sdk_ad_full_data` / `FAdFullLog` — **lớp và event đó không tồn tại** trong
> framework (Mediation rà ra đúng). Phiếu sai, không phải Mediation thiếu.

SDK **tự cấp** mã nếu mediation không khai, nên cột không bao giờ trống. Nhưng mã tự cấp **khác** `candidateId` mà Mediation gửi sang
Databuckets ⇒ dòng load của kho BigData và dòng `ad_request` / `ad_impression` của Databuckets **không nối được** theo từng quảng cáo.

**Sửa:** mã đã được mint một lần ở Mediation (`FloorAdCandidate.candidateId`) — truyền **cùng** giá trị đó vào BigData:
`adCandidateId = candidate.candidateId` (đoạn code ở **W0**).

- Cấp mã cho **cả** dòng load thất bại (bên đọc số yêu cầu) — `candidateId` có từ lúc gửi request nên dòng fail cũng mang được.
- SDK dán mã đó lên **cả** dòng load và dòng impression của unit đó, nên `ad_candidate_id` nối được *nhát thử ở sàn X* với *tiền thật
  nó mang về* mà không cần chở eCPM sang dòng impression.
- Load **mặc định** (MultiCall, ngoài workflow) không có candidate ⇒ để SDK tự cấp như cũ.

## [x] W5 🔴 — Impression phải đi qua `Ad.OnImpression`, nếu không hai cột eCPM trống vĩnh viễn

> ✅ **Rà 23/11:** impression MAX đi qua `Ad.OnImpression` (`FalconMaxService`). Dòng impression của unit workflow **có** `ecpmFloor`
> (`MultiCallLoadedFloor` tra `ShownCandidate` trước) — điểm "impression workflow không mang ecpmFloor" trong bản rà của Mediation
> không đúng. ⚠ Phía Databuckets, `first_ad_ecpm` / `previous_ad_ecpm` do Mediation tự tính **chỉ interstitial**; phía BigData SDK tính
> **gộp mọi QC toàn màn** (Q10) — hai bảng cùng tên cột, khác định nghĩa: đừng so trực tiếp.

`firstAdEcpm`/`previousAdEcpm` là **SDK tự nhớ**, và thứ duy nhất nó nhớ được là `adRev` của dòng
impression. SDK đã đặt chỗ ghi ở **decorator** nên mọi đường dựng `FAdLog`/`ExtendAdLog` rồi `Send()`
đều được tính — kể cả hai nhánh đang tự dựng `ExtendAdLog` trong module
(`FalconIronSourceService.cs:282`, `FalconGmaService.cs:206`).

**Nhưng đường nào KHÔNG dựng một `FAdLog` nào thì SDK không thấy gì.** Dấu hiệu nhận ra trên dữ
liệu: `first_ad_ecpm` **vắng trên 100% dòng** của một máy/một bản app ⇒ impression của máy đó không
đi qua cửa nào của BigData, chứ không phải "cả phiên chỉ load mà chưa chiếu".

⚠ Họ **dính màn** (banner · MREC · collapsible · native) **cố ý không tính**: chúng tự refresh liên
tục nên sẽ luôn là "lượt ngay trước", đẩy chính quảng cáo toàn màn ra khỏi câu hỏi mà hai cột này
sinh ra để trả lời.

## [ ] W6 🔴 — Ba giá trị mức USER: cấp qua interface, và PHẢI tự giữ giá trị

> 🔴 **Rà 23/11: chưa làm.** Mediation chưa có `IFCustomInfoRepository` nào; event `InterstitialFloorPersonalization.RuleEvaluated`
> (mang `device_tier` / segment / variant) không có ai nghe trong framework. Ngoài mục đích riêng, W6 còn là thứ **phân biệt ca
> Control** với ca game không chạy flow trên kho BigData (xem W1). Giữ ảnh chụp bằng cách nghe `RuleEvaluated` trong Mediation (cùng
> assembly) — không cần sửa module Databuckets.

`device_tier` · `realtime_segments` · `realtime_variants` là giá trị **của user**, không phải của một
lần load, nên chúng không nằm trên dòng load. SDK **không** nhét chúng vào danh sách tham số trung tâm
— mediation **tự cấp** qua kênh inject động của Devkit (owner chốt 29/10).

**Cách làm:** implement `IFCustomInfoRepository` trong assembly của module (DI tự tìm, cùng khuôn mà
`FalconAppsflyerService`/`FalconAdjustService` đang dùng — không phải đăng ký gì thêm):

```csharp
// Ảnh chụp BẤT BIẾN — đổi giá trị là tạo cái mới rồi gán MỘT tham chiếu (xem cảnh báo thread dưới).
public class PersonalizationSnapshot
{
    public readonly string deviceTier;
    public readonly string[] segments;
    public readonly string[] variants;

    public PersonalizationSnapshot(string deviceTier, string[] segments, string[] variants)
    {
        this.deviceTier = deviceTier;
        this.segments = segments;
        this.variants = variants;
    }
}

// Giữ state ở service sẵn có của module (thứ đang gọi Rule), đừng dựng singleton mới.
public partial class FalconMaxService
{
    private volatile PersonalizationSnapshot _personalization;

    public PersonalizationSnapshot Personalization => _personalization;

    // gọi sau MỖI lần Rule trả về — copy sang mảng rồi gán một lần
    public void UpdatePersonalization(string deviceTier, List<string> segments, List<string> variants)
    {
        _personalization = new PersonalizationSnapshot(deviceTier, segments?.ToArray(), variants?.ToArray());
    }
}

// Lớp cấp giá trị: CLASS TRƠN implement interface — DI tự dựng, đúng khuôn AppsFlyerInfoService.
public class MediationPersonalizationInfo : IFCustomInfoRepository
{
    public Dictionary<string, object> GetInfo()
    {
        var info = new Dictionary<string, object>();
        var snapshot = FalconMaxService.Instance.Personalization;      // đọc MỘT tham chiếu
        if (snapshot == null) return info;                             // chưa gọi Rule lần nào ⇒ vắng key

        if (!string.IsNullOrEmpty(snapshot.deviceTier)) info["deviceTier"] = snapshot.deviceTier;
        if (snapshot.segments is { Length: > 0 }) info["realtimeSegments"] = snapshot.segments;
        if (snapshot.variants is { Length: > 0 }) info["realtimeVariants"] = snapshot.variants;
        return info;
    }
}
```

⚠ **Hai lý do cho cái ảnh chụp bất biến đó, đừng bỏ:**
1. `UpdatePersonalization` chạy trên **thread callback HTTP của Rule**, còn `GetInfo()` chạy ở chỗ log
   vào hàng chờ. Ba field rời thì đọc được **nửa cũ nửa mới**; một tham chiếu thì không.
2. Giữ nguyên `List<string>` của caller là ai đó `Add` vào nó sau đó sẽ **đổi luôn những log chưa
   serialize**. Copy sang mảng là xong, và đó cũng là cách rẻ nhất cho mỗi log.

🔴 **`GetInfo()` được gọi MỖI LẦN dựng log** (nó nằm trên đường trang trí của mọi bản tin, cỡ trăm
dòng/user/ngày). Nên trong đó:

- **chỉ đọc field đã lưu** — tuyệt đối không gọi Rule, không đọc đĩa, không `JsonConvert`, không `new`
  list mỗi lần nếu tránh được;
- **tự giữ giá trị** sau mỗi lần gọi Rule (`Update(...)`). Rule trả `device_tier` ở **mọi** response,
  nhưng kênh này không có ai nhớ hộ;
- **giá trị chưa có thì ĐỪNG đưa key vào dict** — vắng key đọc ra "chưa biết", còn `""`/`[]` là một
  lời khai sai.

⚠ **Ba key này vắng ở những bản tin đầu phiên** (trước lần gọi Rule đầu tiên) — đó là đúng, và phía
đọc số đã được ghi rõ ở `DWH-EventLog-API-Spec.md` §4.3. Muốn có ngay từ log đầu thì phải persist
`device_tier` xuống đĩa và nạp lại lúc khởi động — **nên làm**, vì bậc máy gần như không đổi.

ℹ Tên key dùng **camelCase** (`deviceTier`, `realtimeSegments`, `realtimeVariants`); server tự map
sang snake_case. Hai cái sau là **mảng chuỗi**, cứ truyền `List<string>`.
⚠ Ba key này cũng đi vào `properties` của lần fetch remote config và payload CDN (cùng đường
`GetUserParams()`) — tiện cho targeting, nhưng nghĩa là **đừng nhét thứ nhạy cảm vào đó**.

## [ ] W7 🟡 — Bump version của module mediation

Cùng luật V9 của phiếu kia: đổi **nghĩa** của một mốc log thì bump version, không thì kho không có
cách nào biết một dãy số đọc theo luật nào.

---

## Câu hỏi gửi bên quyết sàn (Rule owner)

| # | Câu | Vì sao cần |
|---|---|---|
| Q1 | Host, auth, và **ngân sách timeout** (ms)? | W1 không code được nếu thiếu |
| Q2 | Gọi lỗi/timeout thì dùng sàn nào, và **log `ecpm_floor_type` bằng giá trị gì**? | ✅ **Phần log đã chốt 23/11**: `rule_error` / `rule_timeout` (W1). Sàn khi lỗi: đang dùng load mặc định (MultiCall) — bên Rule xác nhận |
| Q3 | Cái gì **kết thúc** "First Interstitial flow"? (hết phiên · sau N lần · sau khi `upgrade_higher` success) | Quyết định khi nào ngừng gọi Rule |
| Q4 | `current_load_ad_status` là kết quả **từng attempt** hay **sau khi hết `retry_times`**? | Hai cách cho hai state machine khác nhau |
| Q5 | Sáu cột còn lại có cần trên dòng **show/impression** không? | SDK đã chở `ad_candidate_id` sang dòng impression (nối được *nhát thử* ↔ *tiền thật*). Dòng **show** thì chưa: mốc đó hiện không mang `ad_unit_id` nên không tra được sổ theo unit — đúng việc **V4** của phiếu `Request-Mediation-AdEcpmAndParams.md`, làm V4 xong thì cột này tự có |
| ~~Q6~~ | ~~Sàn của Rule có **đè** sàn động multicall…?~~ | ✅ **Trả lời bằng code (rà 23/11)**: đè — MultiCall bị chặn khi workflow chạy; tách bằng `adAlgorithm = ALGORITHM_FLOOR_PERSONALIZATION` |
| ~~Q7~~ | ~~`country` lấy từ IP server hay client gửi?~~ | ✅ **Đóng 23/11**: client gửi, lấy từ `CountryProvider` do chính Databuckets (bên Rule) cấp theo định dạng của họ; không có provider thì rơi về country của MAX. Lệch với `country` của kho là lựa chọn của bên Rule |
| Q8 | `retention_day` của Rule có cùng định nghĩa với `retentionDay` của SDK? | Lệch định nghĩa thì Rule chia nhóm khác kho |
| ~~Q9~~ | ~~6 vai hay 8 vai?~~ | ✅ **ĐÃ CHỐT 29/10**: đủ **8 vai** — bảng tham số có cả `decay`/`decay_half`, spec *First Interstitial* chỉ phủ 6 vì nó chỉ nói về quảng cáo đầu. SDK đã nhận cả 8 |
| Q10 | `first_ad_ecpm`/`previous_ad_ecpm` tính **gộp mọi format** hay **tách inter/rewarded**? | SDK đang gộp mọi format TOÀN MÀN (bỏ họ dính màn) theo cách đọc mặt chữ — dựng lại được từ dòng impression nên sửa muộn không mất dữ liệu |
| Q11 | Dòng **load** không có `ad_index_session` — nó có `session_ad_shown_before` (**đã chiếu mấy lượt trước lần load này**, 0 = chưa lượt nào). Đọc như *"slot kế tiếp"* thì map `shown_before + 1`, chịu không? | **Không đặt chung tên** là có chủ ý: trên dòng impression, `session_ad_impression_index` là lượt thứ mấy của CHÍNH lượt đó ⇒ cùng một quảng cáo thì dòng load mang 2, dòng impression mang 3. Chung tên là mọi `JOIN` lệch một nhịp im lặng. Và +1 cũng **không** chính xác tuyệt đối: multicall giữ ad trong kho nên lúc load không biết mình sẽ là lượt thứ mấy |

> ✅ **Đã chốt, không hỏi lại:** `estimated_ecpm` **không** lên dòng impression — ở đó nó bằng
> `ad_rev` × 1000, phía đọc tự nhân (owner 26/10, chốt lại 29/10). Và `ecpm_floor = 0` **không tồn
> tại trên dây**: đọc bằng `ecpm_floor_type = 'no_floor'`.
