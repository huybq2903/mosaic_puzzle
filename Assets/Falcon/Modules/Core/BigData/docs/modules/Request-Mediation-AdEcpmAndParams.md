# Mediation — đưa eCPM và 6 tham số lên log quảng cáo

> **Dev Mediation chỉ cần đọc phần TODO dưới đây.** Mỗi việc: sửa file nào, đổi dòng nào, kiểm ra
> sao. Tick `[x]` khi xong.
> Viết 26/10/2026, theo yêu cầu của phía đọc số và luật mới của monetize cùng ngày.
> Phía SDK (BigData **1.5.5**) đã làm xong phần nhận: mọi field dưới đây **đã có cửa**, bây giờ chỉ
> thiếu người truyền.

## 0a. 🔴 CẬP NHẬT 28/10 — bản live đã làm V1–V3, và nó đẻ ra việc mới + bốn câu hỏi

Phía đọc số rà cột giá của `f_sdk_ad_load` trên dữ liệu production và **đã thấy `tier`,
`ecpm_floor`, `estimated_ecpm`, `ad_algorithm` có mặt** ⇒ bản mediation đang build ở game **đã làm
V1, V2, V3**. Trong khi đó bản mediation **nằm trong SDK repo vẫn là 1.4.31**, chưa có dòng nào set
`ecpmFloor`/`tier`/`adAlgorithm` (grep cả history: không có).

**Nên đọc phiếu này thế nào:** V1–V3 ở dưới giữ lại để tham chiếu, **số dòng của chúng là của 1.4.31
nên đã lỗi thời**; việc còn phải làm là **V4 · V5 · V6 · V7 · V8**.

| Câu cần dev mediation trả lời | Vì sao cần |
|---|---|
| H1 — bản mediation nào đang build ở game, và nó có **đẩy về SDK repo** không | Hiện SDK repo và bản live là hai cây khác nhau; mọi phân tích code từ repo đều phải đánh dấu *"chưa xác nhận trên bản live"*, và lần sau lặp lại đúng vòng này |
| H2 — nhánh multicall nào **không set bid floor** (xem V7) | Để phía đọc số loại đúng tập đó khỏi phép đo "bậc nào fill tốt" |
| H3 — công thức sàn động còn lấy nền = *doanh thu lần thắng đầu của unit không-sàn trong phiên* không, và nhánh live gán nền theo **unit không-sàn nào thắng trước** hay theo **unit ở index 0** (xem V8) | Bản ở repo có hai biến thể, chỉ một trong hai hở cửa sổ **sàn = 0** |
| H4 🔴 — ở nhánh **SUCCESS**, bản live đọc `ecpmFloor` từ ô nào, và ô đó có bị gán lại/reset **trước** lúc log không | Production: nhóm thiếu `ecpm_floor` **toàn là dòng success** — dòng fail của cùng unit vẫn có. Đó là dấu của lỗi **lệch một nhịp** đúng họ với V6, không phải dấu của sàn 0 |

## 0b. Phiếu ANH EM — flow chọn giá sàn *(29/10)*

Bảy cột mới của **flow chọn sàn** (`ecpm_floor_type`, `ad_candidate_id`, `device_tier`…) nằm ở phiếu
riêng: **`Request-Mediation-FirstInterFloorRule.md`**. Hai phiếu chạm nhau ở hai chỗ, đừng làm lệch:

- **V6/V7 ở đây và W3 ở đó là CÙNG một lỗi** — khai nhãn giá sàn theo state *hiện tại* thay vì ảnh
  chụp lúc **gửi request**. Sửa một chỗ thì sửa luôn chỗ kia, cùng một khuôn `floorUsed`/`tierUsed`.
- **`tier` và `ecpm_floor_type` trả lời hai câu khác nhau**: `tier` là bậc trong config của unit,
  `ecpm_floor_type` là **vai** của con số sàn trong flow. Một request có thể có cả hai, và từ 1.5.7
  thì `no_floor` là đường duy nhất khai *"cố ý không áp sàn"* — nên **V7 không còn cần một cột
  `is_bidding`** như đề xuất 28/10.

## 0. Kết luận trong 30 giây

| | |
|---|---|
| **Yêu cầu gốc** | *"Bản tin nào lấy được eCPM thì phải log eCPM — cái đó quan trọng nhất"*, và log quảng cáo phải có `ad_algorithm` · `time_loading_ms` · `floor` · `ad_mediation` · `ad_network` · `ad_unit_id` |
| **SDK đã làm** | Dòng load tách riêng (`f_sdk_ad_load`) mang `ad_view_id` + eCPM + số thứ tự trong phiên; sổ sự thật theo `adUnitId` để dòng impression lấy lại network/bậc/giá sàn; `adAlgorithm` đi theo cả đợt; `adUnitId` trên dòng close |
| **Mediation phải làm** | **9 việc** *(V1–V3 bản live đã làm — xem §0a)*: V1 eCPM lúc load · V2 `tier` thay vì `floor` (và đừng gửi `tier = 0`) · V3 khai `adAlgorithm` **ở chỗ mở van, không phải đầu phiên** · V4 `adUnitId` ở mốc show · V5 nhãn mediation của nhánh IronSource · **V6 `tier` trên dòng fail đang lệch một nhịp** · **V7 `tier` chỉ gửi khi THẬT SỰ áp sàn** · V8 nền sàn động *(chờ H3/H4)* · V9 bump version |
| **Việc gấp nhất** | **H4** (một câu trả lời, không phải sửa code) rồi **V7** *(chốt 28/10)* — V7 là cái đang làm phép đo bậc giá sai **im lặng**. V8 chờ H3/H4. V1–V3 bản live đã làm |

---

## [ ] V1 🔴 — eCPM lúc LOAD: số đã nằm trong tay, chỉ chưa truyền

`MaxSdkBase.AdInfo` của callback loaded **đã có** `Revenue`, và module **đã đọc nó rồi**:

```csharp
// FalconMaxService.cs:661 (interstitial) và :1306 (rewarded) — đọc để so inter vs rewarded
_eCpmInterstitial = adInfo.Revenue;
```

Nhưng `Ad.OnLoadResult(...)` ở ngay cạnh thì **không truyền** số đó, nên dòng load không có eCPM.

**Sửa:** thêm một dòng vào mọi lời gọi `OnLoadResult` của nhánh **thành công**
(`FalconMaxService.cs:313`, `:688`, `:1332`; `MultiCallMaxProviderParent.cs:287`, `:470`; bridge
Admob `AdmobMediationFalconBridge.cs:558`):

```csharp
FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam
{
    ...
    estimatedEcpm = Ecpm(adInfo.Revenue),   // ← THÊM
});

// helper dùng chung — đặt cạnh chỗ gọi:
private static double? Ecpm(double revenue) => revenue > 0 ? revenue * 1000d : null;
```

⚠ **Hai chỗ dễ sai, cả hai đều làm hỏng số:**

1. **Đơn vị lệch 1000 lần.** `adInfo.Revenue` là USD của **một lượt hiển thị**; `estimatedEcpm` trên
   dây là USD trên **1000 lượt**. Truyền thẳng `Revenue` là báo eCPM nhỏ đi 1000 lần. (Field nội bộ
   `_eCpmInterstitial` đang giữ `Revenue` chứ không phải eCPM — tên gọi dễ gây nhầm, đừng dựa vào nó.)
2. **Số không dương là "KHÔNG BIẾT", đừng truyền 0.** Module vốn đã dùng `-100` làm cờ không-biết
   (`FalconMaxService.cs:783`, `:1521`), nên quy tắc này khớp sẵn: `revenue <= 0` thì **để null**.
   SDK cũng chặn thêm một lớp (số ≤ 0 không lên dây, không vào trung bình), nhưng chặn ở nguồn thì
   log debug cũng sạch.

**Kiểm:** chơi một lượt → dòng `f_sdk_ad_load` của lần load thành công có `estimatedEcpm` > 0. Đối
chiếu với tiền thật: dòng `f_sdk_ads_data` cùng `adViewId` có `adRev`, và `estimatedEcpm ≈ adRev ×
1000` (dòng impression **không** chở lại eCPM — cố ý, khỏi chở hai lần một thứ).

## [ ] V2 🔴 — `tier` thay vì nhét hệ số vào `floor`

5 chỗ trong multicall đang gửi **hệ số bậc** vào field `floor`:

```csharp
// MultiCallMaxProviderParent.cs:201, :287, :422, :470 (+ comment ở :274)
floor = a.multiplier,   // ← SAI Ô: multiplier là BẬC, không phải giá sàn USD
```

Hợp đồng 23/09 tách hai trục: **`tier`** = hệ số bậc trong config (số rời rạc, dùng làm khoá gộp),
**`floor`** = giá sàn THẬT bằng USD/1000 impression (số đo liên tục, chỉ mediation biết giá thật mới
gửi). SDK đã cảnh báo đúng ca này tại chỗ từ 1.4.1 (`floor > 0` mà `tier == null`), nên trong log
Unity hiện có warning mỗi lần load.

**Sửa:** đổi tên ô, một ký tự:

```csharp
tier = a.multiplier,
```

Hệ quả tốt kèm theo: `tier` nằm trong **khoá gộp** nên bản tin gộp tách được theo bậc; và từ 1.5.5
`tier` còn được SDK dán sang dòng impression qua sổ theo unit.

⚠ Đừng "sửa" bằng cách nhân hệ số với doanh thu để ra giá sàn: nền nhân là giá của ad vừa xem nên
cùng một bậc ra số khác nhau theo máy/theo ngày (loader đã đo 22/09). Không biết giá thật thì **để
`floor` trống**.

**Kiểm:** warning `OnLoadResult(...): có floor = ... nhưng không có tier` **mất khỏi log Unity**.

⚠ **Và đừng gửi `tier = 0`** cho unit không đặt sàn — để **trống**. Hợp đồng 23/09: vắng = *không đặt
sàn*, khác hẳn *đặt sàn 0*. (Máy test 26/10 có 3 unit gửi `tier = 0` mà không có `ecpm_floor`. Từ
1.5.6 SDK **tự bỏ** số không dương cho chắc, nhưng gửi đúng ở nguồn thì log debug cũng sạch.)

## [ ] V3 🟡 — khai `adAlgorithm` một lần cho mỗi đợt

Phía đọc số cần biết *"thuật toán load nào hiệu quả hơn"*. Multicall Max có 8 nhánh
(`MultiCallMaxProvider0..7`), phân biệt bằng `IMultiCallProvider.GetIndex()`, mà **không có gì trên
log nói nhánh nào đang chạy**.

SDK không tự biết giá trị này. Khai **một lần cho mỗi đợt** là mọi mốc sau tự mang theo (request ·
show · impression · close · load), vì nó đi theo luật hai chiều như `adMediation`.

**Gọi ở ĐÂU — chốt 26/10 sau khi rà code (đừng đoán):**

| Chỗ gọi | Có ăn không |
|---|---|
| **Đầu phiên / lúc init mediation** | ❌ **KHÔNG** — `UpdateContext` ghi vào view của format, mà lúc đó format chưa đi qua `OnRequested` nào nên **không có view**: SDK bỏ qua và in warning *"chưa đi qua OnAdRequested"* |
| **Ở chỗ mở van / mốc request** (`AdViewRequestGate.Open`) | ✅ **Đúng chỗ.** Và từ **1.5.6** chỉ cần **một lần cho mỗi format**: thuật toán được giữ sang các đợt sau (trước đó mỗi đợt `Open` dựng view mới và **xoá sạch**, nên khai một lần là mất từ đợt thứ hai) |

**Sửa:** chỗ nào đang mở đợt (`AdViewRequestGate.Open`) thì khai luôn. Hai cách, chọn một:

```csharp
// (a) ngay sau khi mở van, một dòng cho mỗi format:
FalconBigDataController.Ad.UpdateContext(type, adAlgorithm: $"multicall_{GetIndex()}");

// (b) hoặc truyền thẳng ở mốc request, nếu sửa được chỗ gọi OnRequested:
FalconBigDataController.Ad.OnRequested(type, adMediation: m, adAlgorithm: $"multicall_{GetIndex()}");
```

> ✅ **Từ BigData 1.5.6 việc này rẻ hơn**: dòng `f_sdk_ad_load` tự lấy thuật toán từ cache của đợt
> (`param.adAlgorithm ?? AdViewCache.CurrentAlgorithm(adType)`), nên **khai một lần** ở chỗ mở van là
> đủ cho cả dòng load — không phải nhắc lại trong từng lời gọi `OnLoadResult`. (Ở 1.5.5 thì phải
> nhắc lại: field có trên bản tin nhưng dòng load chỉ đọc param.)

Giá trị đề xuất: `multicall_<index>` cho đường multicall, **`single`** cho đường một-unit
(`FalconMaxService` thường, bridge Admob). Chuỗi gì cũng được, miễn **ổn định** — phía đọc số
group theo nó.

**Kiểm:** mọi dòng ad của một phiên có cùng một `adAlgorithm`; đổi provider thì đổi giá trị.

## [ ] V4 🟡 — `adUnitId` ở mốc show

`AdShowParam` từ 1.5.5 **có** field `adUnitId` (tuỳ chọn). SDK **không** tự đoán ô này: một đợt có
nhiều unit load song song nên "unit của lần load cuối" thường không phải unit sắp chiếu — dán vào là
bịa. Mediation thì biết chắc mình đang gọi `ShowAd` của unit nào.

```csharp
FalconBigDataController.Ad.OnShown(new AdShowParam
{
    type = type, adWhere = where, adMediation = _MAX,
    adUnitId = adUnitId,   // ← THÊM
});
```

Dòng **close** thì không cần: SDK lấy unit đã ghi ở mốc impression của đúng view đó.

**Kiểm:** dòng `f_sdk_ad_show_data` có `adUnitId`, và nó **trùng** với `adUnitId` của dòng impression
cùng `adViewId`.

## [ ] V5 🔴 — nhánh IronSource đang tự khai là "Max"

```csharp
// MultiCallIronSourceProviderParent.cs:299 và :573
adMediation = "Max",
adNetwork = a.networkName
```

Nhánh IronSource báo mediation là **Max**. Hệ quả: trong kho **không tồn tại** mediation
`levelplay`, và mọi so sánh "Max vs LevelPlay" đang so Max với chính nó.

SDK **không** chuẩn hoá chuỗi này (có cân rồi bỏ — đổi giá trị một cột đang chạy thì mọi dashboard
lọc chữ cũ phải sửa). Nên **chỉ có chỗ này sửa được**: SDK gửi nguyên văn chuỗi mediation truyền vào.

**Sửa:** `adMediation = "IronSource"` (hoặc `"LevelPlay"`) ở hai chỗ đó và mọi chỗ khác của nhánh
IronSource.

**Kiểm:** game đang chạy IronSource → dòng ad có `adMediation = "IronSource"` (không còn `"Max"`).

## [ ] V6 🔴 — `tier` trên dòng load FAIL đang là bậc của lần SAU (lệch một nhịp)

Phía đọc số bắt được trên máy test 26/10: cùng một unit mà `tier` đổi **1,8 → 1,7 → 1,6** trong một
phiên, trong khi `ecpm_floor` đứng yên. Rà code thì đúng là **lệch một nhịp, và chỉ ở dòng fail**:

```csharp
// MultiCallMaxProviderParent.OnRewardedAdLoadFailedEvent
InternalLoadRewardedLoadFailed(adUnitId);        // ← (1) thang hạ bậc: a.multiplier -= 0.1f
...
FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam {
    floor = a.multiplier,                        // ← (2) log SAU, nên đọc ra bậc MỚI
});
```

Thang hạ bậc nằm ở các provider (`MultiCallMaxProvider2.cs:107` và tương tự):
`a.multiplier = Mathf.Max(a.multiplier - 0.1f, 1.1f)` mỗi `_periodDecreaseFloor` lần fail — khớp
đúng chuỗi 1,8 → 1,7 → 1,6 mà bên đọc số thấy.

**Dòng SUCCESS thì đúng** (ít nhất ở provider2): child gọi `base.OnRewardedAdLoadedEvent(...)` —
tức log — **trước** khi chỉnh `multiplier`.

**Sửa:** chụp giá trị bậc **đã dùng cho lần load đó** ngay lúc gọi `LoadAd` và truyền chính giá trị
đó, thay vì đọc field sống lúc log:

```csharp
var tierUsed = a.multiplier;          // chụp lúc gọi LoadAd
...                                    // thang có hạ bậc sau đó cũng không ảnh hưởng
FalconBigDataController.Ad.OnLoadResult(new AdLoadResultParam { tier = tierUsed, ... });
```

⚠ Việc này **phải kiểm cho cả 8 provider** (`MultiCallMaxProvider0..7` + nhánh IronSource): thứ tự
gọi `Internal...Failed` vs log là của từng lớp, không phải luật chung.

**Kiểm:** cho một unit fail liên tiếp 3 lần → ba dòng load mang **ba bậc khác nhau nhưng đúng bậc đã
dùng**, và bậc của dòng fail cuối cùng **khớp** với bậc của dòng load thành công ngay sau đó (nếu
thang chưa hạ thêm).

## [ ] V7 🔴 — `tier` phải là sự thật của REQUEST, không phải nhãn trong config

Phía đọc số cần trả lời *"bậc nào fill tốt hơn"*. Hiện `tier` được đọc từ config và gửi **kể cả khi
request đó không hề được áp sàn** — nên cột đó đang trộn hai loại dòng, và phép đo bị nhiễm **im
lặng** (không lỗi, chỉ ra số sai).

Hai nguồn nhiễm, nhìn rõ trong code multicall của bản 1.4.31 (cần H2/H3 xác nhận trên bản live):

| Nguồn | Chuyện gì xảy ra |
|---|---|
| Nhánh provider **không có bid floor** | `InternalLoadRewarded`/`InternalLoadInterstitial` của nhánh đó **không gọi** `MaxSdk.Set*AdExtraParameter(KEY_SET_BID_FLOOR, …)`, chỉ `LoadAd` theo thang retry. Mọi dòng của nhánh vẫn mang `tier` của config ⇒ nhãn cho một cái sàn **chưa bao giờ tồn tại** |
| **Cửa sổ khởi động** *(chỉ nếu H3 nói nhánh live gán nền theo `index == 0` — xem V8)* | Sàn tính ra `0` nên thực tế **không có sàn**, mà dòng log vẫn mang `tier` |

**Sửa:** chỉ truyền `tier` khi request đó **thực sự** được áp một mức sàn **dương**:

```csharp
// trong InternalLoad... : chụp cả hai thứ ĐÃ DÙNG cho đúng lần gọi này
var floorUsed = a.multiplier * MultiCall.Instance.valueDefaultRewarded * MultiCall.MULTIPLIER;
var applied   = floorUsed > 0;                       // nhánh không set floor: applied = false
if (applied) MaxSdk.SetRewardedAdExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR, ...);
a.tierUsed  = applied ? a.multiplier : (double?)null;   // ← chụp, xem V6
a.floorUsed = applied ? floorUsed    : (double?)null;

// lúc log (cả nhánh success và fail):
tier      = a.tierUsed,
ecpmFloor = a.floorUsed,
```

⚠ **Đừng** gửi `tier = 0` cho ca không áp sàn: từ 1.5.6 SDK bỏ số không dương nên nó ra **vắng key**
y như null — gửi 0 chỉ làm log debug khó đọc, không thêm thông tin nào lên dây.

**Kiểm:** chạy nhánh không-bid-floor → dòng load **không có** `tier` và **không có** `ecpm_floor`;
chạy nhánh có bid floor sau khi nền đã có số → **có cả hai**, và `ecpm_floor` khớp con số vừa
`SetExtraParameter`.

## [ ] V8 🟡 — *CHỜ H3/H4 trước khi làm* — nền của công thức sàn động có lúc bằng 0

```csharp
// MultiCall.cs:54-55  — nền của công thức sàn, RAM, KHÔNG persist
public double valueDefaultRewarded = 0;
public double valueDefaultInterstitial = 0;

// provider:  sàn = bậc × nền × 1000  → nền = 0 thì SÀN = 0
var currentFloor = a.multiplier * MultiCall.Instance.valueDefaultRewarded * MultiCall.MULTIPLIER;
MaxSdk.SetRewardedAdExtraParameter(adUnitId, MultiCall.KEY_SET_BID_FLOOR, currentFloor.ToString(...));
```

Nền chỉ có số **sau** lần load thắng đầu tiên của unit không-sàn trong phiên. Nếu có request của
unit **có bậc** bay ra trước lúc đó thì nó xin ad với `SetBidFloor("0")`, tức **thang bậc bị tắt** —
đây không chỉ là chuyện log mà là chuyện **doanh thu**.

🔴 **NHƯNG bản trong repo đã phòng ca đó, nên đừng sửa vội.** Rà lại 28/10:

- `LoadRewarded()` **chỉ** xếp hàng load cho các unit **không-sàn** (`i < _defaultCountRewarded`);
  nhóm có bậc được load **bên trong `OnRewardedAdLoadedEvent`** của một unit không-sàn, mà nền được
  gán **ngay dòng đầu** của chính callback đó ⇒ **không có cửa sổ nền = 0** ở nhánh này.
- Ngoại lệ duy nhất tìm được: một nhánh cũ gán nền bằng `if (a.index == 0)` thay vì
  `if (a.multiplier == 0)`. Nhiều hơn một unit không-sàn mà unit ở **index 0 không fill** thì nền
  đứng 0 **trong khi nhóm có bậc đã bắt đầu xin ad**. Nhánh mới hơn đã đổi điều kiện này — nên nếu
  nhánh cũ còn chạy ở game nào thì đây là bug thật, và cách sửa là **đồng bộ điều kiện**.
- Và nếu network/ad test trả `Revenue ≤ 0` thì nền **đứng 0 cả phiên**.

⚠ **Dữ liệu production đang PHẢN CHỨNG cả hai ca trên**: nhóm dòng thiếu `ecpm_floor` **toàn là
dòng success**, còn sàn-gửi-đi-bằng-0 thì phải kéo theo **cả dòng fail** (không sàn chỉ fill khoảng
một nửa). Vì vậy việc này **xếp sau H3/H4**, và giả thuyết dẫn đầu giờ là **H4** (đường đọc
`ecpmFloor` ở nhánh success).

**Sửa — chọn một, cần monetize chốt:**

| Cách | Nội dung |
|---|---|
| (a) **Hoãn** | Chưa có nền thì **đừng gọi** `SetBidFloor`, cũng đừng load unit có bậc; load unit không-sàn trước, có nền rồi mới mở thang |
| (b) **Nền danh nghĩa** | Khai trong config một nền mặc định cho mỗi format, dùng khi nền phiên chưa có |
| (c) **Nền sống qua phiên** | Lưu nền của phiên trước xuống đĩa và dùng lại lúc mở app (vẫn nên có trần/sàn để một phiên lạ không khoá nền sai) |

⚠ Cách nào cũng **phải kèm V7**: lần nào không áp được sàn thì **đừng** gắn `tier` cho dòng đó.

**Kiểm:** mở app lạnh → dòng load đầu phiên **không** có unit nào mang `tier` mà `ecpm_floor` vắng.

## [ ] V9 🟡 — tăng version mỗi lần đổi nghĩa mốc log

Cùng chốt với module event (án Key Collection 28/10): bản live đã làm V1–V3 mà **CHANGELOG của
mediation trong SDK repo vẫn 1.4.31**, nên phía đọc số thấy cột đổi hành vi theo bản app mà **không
truy được bản nào mang bản tracking nào**. Đổi field/đổi nghĩa mốc ⇒ **bump version + một dòng
CHANGELOG**, và bản đã lên production thì **đẩy về SDK repo**.

---

# 📖 Những thứ Mediation KHÔNG phải làm

- **Không phải đổi chữ `"Max"`/`"Admob"`** thành chữ thường hay thành bộ `max`/`levelplay`/`admob`:
  SDK gửi nguyên văn và phía đọc số giữ chữ cũ. Chỉ cần khai **đúng mediation** (việc V5).
- **Không phải truyền `tier`/`floor`/`adNetwork` lại ở mốc impression**: SDK giữ sổ theo `adUnitId`
  từ mốc load và tự điền những ô mediation bỏ trống. Truyền thì thắng, bỏ trống thì SDK lo.
- **Không phải truyền eCPM ở mốc impression**: dòng đó đã có `adRev` (tiền thật của đúng lượt), chở
  thêm eCPM là hai lần một thứ. eCPM chỉ cần ở mốc **load** (việc V1).
- **Không phải đếm "lần thứ mấy trong phiên"**: SDK có sổ riêng cho request/load/load thành
  công/impression.
- **Không phải tự tính trung bình, CTR hay đếm gì**: client chở sự kiện, mọi phép tính là việc kho.
- **Không phải gộp cụm dòng load**: từ 1.5.5 chế độ mặc định là **tách** (một dòng mỗi lần gọi), và
  chuyển chế độ bằng remote config `fAdLoadLogSplit` ở cả hai service config — không phải việc của
  mediation.

# 📖 Vì sao SDK không tự làm hết

| Field | Vì sao SDK không tự biết |
|---|---|
| `estimatedEcpm` lúc load | Nằm trong `adInfo` của callback mediation — SDK không có đường nào chạm vào SDK của MAX |
| `adAlgorithm` | Thuật toán là lựa chọn của tầng mediation, SDK không thấy provider nào đang active |
| `adUnitId` ở mốc show | Nhiều unit load song song; chỉ người gọi `ShowAd` biết mình chiếu unit nào |
| `tier` vs `floor` | Hai trục, chỉ mediation biết con nào là hệ số con nào là USD |

