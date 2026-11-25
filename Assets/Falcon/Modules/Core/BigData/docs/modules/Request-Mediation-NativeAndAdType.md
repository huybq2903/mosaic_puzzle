# Yêu cầu: quảng cáo native chưa có dòng nào, và một chỗ dán sai loại ad — gửi owner module `Core/ThirdParty/Mediation`

> **Người gửi:** owner module `Core/BigData`
> **Người nhận:** owner module `Core/ThirdParty/Mediation`
> **Ngày:** 09/11/2026
> **Căn cứ:** `docs/design/DWH-EventLog-API-Spec.md` §4 (họ event ad) · `AdType` (Devkit) đã có sẵn
> giá trị `Native` và kho đã ghi nó là một trong bảy giá trị hợp lệ
> **Việc cần làm:** 1 chỗ sửa một chữ · 1 chỗ thêm lời gọi · 3 chỗ chốt luật. Không đổi luồng
> hiển thị, không đổi UI, không thêm thư viện.

⚠ **Bên mình đọc code từ working tree, mà cả khối `Service/FSN`, `Service/MetaAdmob`,
`Service/MultiCall`, `Model/FAdCalledLog.cs` ở đây đều CHƯA commit.** Nếu bên anh đã sửa rồi thì bỏ
qua mục tương ứng — bên mình không thấy được. Anchor dưới đây là **tên hàm + đoạn code**, không phải
số dòng, vì số dòng hai bên gần như chắc chắn lệch.

## TODO — cầm tay chỉ việc

| # | Việc | Ở đâu | Cỡ |
|---|---|---|---|
| **N1** | 🔴 Đổi `AdType.Interstitial` → `AdType.Reward` trong `FAdCalledLog` của đường **show Rewarded** | `FalconGmaService` | 1 chữ |
| **N2** | 🔴 Gọi `Ad.OnImpression` với `type = AdType.Native` khi native **hiện ra** | `FsnAdManager` | ~10 dòng |
| **N3** | Chốt luật cho nhánh `default` của bảng map format IronSource — hiện nó dán `AppOpen` | `FalconIronSourceService` | 1 quyết định |
| **N4** | Đổi `_ => "fsn_rewarded"` thành nhánh nổ/log thay vì âm thầm | `FsnAdManager` | 1 dòng |
| **N5** | `LogFsn` của Firebase hardcode `ad_platform = "AdMob"` và không gửi placement | module `Firebase` | 1 quyết định |

Mục **N1** và **N2** là hai cái sinh ra số sai. N3–N5 là mìn chưa nổ.

---

## N1 🔴 — lượt gọi quảng cáo có thưởng của AdMob đang đếm thành interstitial

Trong `FalconGmaService`, hàm show **Rewarded**, hai dòng nằm sát nhau:

```csharp
new CSAdStart(_idAd, AdType.Reward.ToString(), where, adapterResponseInfo.AdSourceName,
    "Admob", _rewardedAd.GetAdUnitID()).Send();
new FAdCalledLog(AdType.Interstitial, where, "Admob", LevelData.Instance.level)   // ❌
    .Send();
```

Dòng trên ghi `Reward`, dòng dưới ghi `Interstitial`. Sửa thành:

```csharp
new FAdCalledLog(AdType.Reward, where, "Admob", LevelData.Instance.level)
    .Send();
```

**Vì sao chắc đây là lỗi chứ không phải quy ước.** Bên mình soi cả bốn chỗ gọi `FAdCalledLog` trong
module:

| chỗ gọi | `type` | |
|---|---|---|
| `FalconIronSourceService`, đường interstitial | `Interstitial` | ✅ |
| `FalconIronSourceService`, đường rewarded | `Reward` | ✅ |
| `FalconGmaService`, đường interstitial | `Interstitial` | ✅ |
| **`FalconGmaService`, đường rewarded** | **`Interstitial`** | ❌ |

Ba chỗ đúng, một chỗ sai, và chỗ sai là chỗ ở ngay dưới một dòng `Interstitial` của khuôn copy.

**Hậu quả trên dữ liệu:** `f_sdk_ads_called_data` ⇒ rổ `Interstitial` phồng, rổ `Reward` hụt, phồng
đúng bằng hụt, và **chỉ với AdMob** — nên nhìn tổng thể thì không ai thấy gì, phải break-by
`ad_mediation` mới lộ.

🔴 **Việc này gấp hơn trước.** Owner đã xác nhận `f_sdk_ads_called_data` **được mở lại** phía kho.
Nghĩa là từ lúc `FAdCalledLog` ship, dòng sai sẽ chảy vào một stream đang sống chứ không rơi vào
hư không nữa.

**Xin ghi kèm một dòng era cho kho** khi bump version, đại ý: *"trước bản X, lượt gọi rewarded của
AdMob mang `ad_type = Interstitial`"* — không có dòng đó thì ai so chuỗi thời gian sẽ thấy một bậc
nhảy ở cả hai rổ mà không biết vì sao.

---

## N2 🔴 — người chơi xem hai quảng cáo, BigData ghi một

Luồng `ShowRewardedThenNative` / `ShowInterstitialThenNative` chiếu **MAX trước, rồi một native
sau**. Phía BigData chỉ có **một** `Ad.OnImpression` (của MAX). Native đi ra ngoài qua `LogAll`, mà
`LogAll` chỉ gọi Firebase + AppsFlyer:

```csharp
private void LogAll(string adFormat, string adSource, string adUnitId, long valueMicros,
    string currencyCode, string placement = "")
{
    FalconFirebaseLog.LogFsn(adFormat, adSource, adUnitId, valueMicros, currencyCode);
    FalconMmpLog.LogFsn(adFormat, adSource, adUnitId, valueMicros, currencyCode, placement);
}
```

Bên mình grep cả `Service/FSN/`: **không một lời gọi `FalconBigDataController` nào.** Không
impression, không click, không close, không doanh thu.

**Vì sao đây là lỗi làm SAI SỐ chứ chỉ thiếu số.** Tiền của cả hai quảng cáo đều tới MMP. Lượt xem
thì BigData chỉ đếm một. Ai lấy *doanh thu ÷ số impression* là ra eCPM **phồng lên** — một con số
trông hoàn toàn hợp lý, không ai nghi.

### Thêm gì

Ở đúng chỗ native **hiện ra thật** (không phải chỗ load xong), thêm:

```csharp
FalconBigDataController.Ad.OnImpression(new AdParam
{
    type       = AdType.Native,
    adWhere    = <bề mặt UI THẬT mà native đang nằm>,   // BẮT BUỘC — xem ⚠ 1
    adWhen     = <mốc trong luồng game, vd "after_rewarded">,  // BẮT BUỘC — xem ⚠ 1
    adRev      = valueMicros / 1_000_000.0,
    adNetwork  = adSource,
    adUnitId   = adUnitId,
    adMediation = "Admob",
    currentLevel = LevelData.Instance.level
});
```

**Khuôn có sẵn để copy:** vùng `#region analytics` của `AdmobMediationFalconBridge` — đó là đường
mà module này đang đi cho AdMob (cộng sổ client **trước** rồi mới bắn `OnImpression`).

### ⚠ Bốn cái bẫy, xin đọc hết

**1. 🔴 `adWhere` + `adWhen` phải truyền TƯỜNG MINH, không thì hai cột đó VĨNH VIỄN là `"Unknown"`.**
`AdParam` khởi tạo cả hai bằng `"Unknown"`, và `AdLogService` có đọc **ad-view cache** thêm một nhịp
khi gửi — nhưng `AdViewState.ApplyContext(param, type)` tra cache **theo ĐÚNG format** (`Current(type)`).
Native chưa đi qua mốc request thì `Current(AdType.Native)` là `null`, ApplyContext **không làm gì**,
và hai cột ở lại `"Unknown"`. Cả hai field đều **bắt buộc** theo doc của `AdParam`.

> ⚠ **Đính chính của bên mình, 09/11.** Bản đầu của thư này viết rằng bỏ trống `adWhere` thì dòng
> native **thừa hưởng chỗ chiếu của rewarded** từ cache. **Sai** — cache có khoá theo format, không
> có đường nào để ngữ cảnh của `Reward` chảy sang dòng `Native`. Kết luận *"phải truyền tường minh"*
> không đổi, nhưng **lý do** là *"không truyền thì mất số"*, không phải *"không truyền thì sai số"*.
> Bên mình ghi lại thay vì sửa lặng, vì vừa có một án đúng kiểu này: một dòng XML-doc bên mình viết
> sai về `level_data_id` đã dắt phía kho dựng cả một luật đọc lên nó.

**2. ⚠ Đừng dùng `fsn_rewarded` / `fsn_interstitial` làm `adWhere`.** Hai tên đó trả lời *"native
này đứng sau loại ad nào"*, không phải *"native này nằm ở đâu"* — mà `adWhere` là câu thứ hai. Dùng
chúng là bê đúng kiểu trộn đang có ở AppsFlyer vào BigData, **lần này do tay mình dán** chứ không
phải do cơ chế. Hãy lấy tên bề mặt thật: popup kết quả, màn home, popup nhận thưởng… Còn câu
*"đứng sau loại ad nào"* thì đã có hai cột riêng lo (xem ⚠ 3) — khỏi nhét vào `adWhere`.

**3. ✅ Câu "native sau loại ad nào thì ăn hơn" thì KHÔNG phải làm gì cả.** `AdLogService` tự đóng
dấu `prevFullScreenAdFormat` + `prevFullScreenAdSec` cho mọi format **dính màn** (native/MREC/
collapsible) — chở sẵn *"vừa xem loại nào, cách đây mấy giây"*. Chỉ cần `type = AdType.Native` là
hai cột đó tự có. Đừng tự nhét quan hệ đó vào `adWhere` hay `extraMeta`.

**4. Đừng gọi lại Firebase/MMP.** `LogAll` đã làm. Chỉ thêm đúng một lời gọi BigData.

### Hai thứ tuỳ chọn, không chặn

- **`ad_view_id` + `fill_latency_ms` của dòng native sẽ VẮNG** nếu không đi qua mốc request
  (`AdViewRequestGate` / `Ad.OnRequested`). Kho **đã biết và đã ghi** là dòng `Native` không mang
  hai cột này, nên vắng là đúng hợp đồng hiện tại, không phải lỗi. Muốn có thì mở mốc request cho
  native, nhưng đó là việc riêng, làm sau.
- **Click của native**: `Ad.OnClicked(AdType.Native, ...)` nếu FSN có callback click. Không có
  callback thì bỏ — kho cũng đã ghi *"CTR native không có trong kho"*.

### Một câu hỏi bên mình không tự trả lời được

`LogAll` nhận `currencyCode`. Hợp đồng `adRev` của BigData là **USD**. Nếu `currencyCode` của AdMob
có thể khác USD thì xin nói lại — bên mình sẽ mở thêm đường cho nó, chứ chia micros ra rồi gửi như
USD là bịa tỉ giá.

---

## N3 — nhánh `default` của IronSource dán `AppOpen`, mà IronSource không có AppOpen

Trong `OnImpressionDataReadyEvent` của `FalconIronSourceService`:

```csharp
switch (adInfo.AdFormat.ToLower())
{
    case _INTERSTITIAL_IRS: type = AdType.Interstitial; break;
    case _BANNER_IRS:       type = AdType.Banner;       break;
    case _REWARDED_IRS:     type = AdType.Reward;       break;
    default:                type = AdType.AppOpen;      break;   // ⚠
}
```

Ba case phủ `interstitial` / `banner` / `rewarded_video`. Mọi chuỗi khác — native, format mới,
hay chỉ là LevelPlay đổi tên — rơi vào `AppOpen`.

🔴 **Và `AppOpen` ở đây là bất khả thi theo cấu trúc**: `InternalCanShowAppOpen` của chính service
này `return false` cứng. Nên mọi dòng rơi vào `default` **sai 100%**, không phải "có thể sai".

Nhánh đó còn nuôi cả `LogBambooTaichi` (hàm này đếm `AppOpen`), nên không chỉ là chuyện nhãn.

**Luật bên mình đề nghị, owner chốt:** chuỗi lạ thì **log lỗi rồi KHÔNG gửi dòng nào**. Nguyên tắc
chung của hợp đồng: *vắng mặt còn hơn một giá trị sai trông hợp lệ* — vắng thì người đọc đi tìm, sai
thì người đọc dùng luôn.

> Hiện nhánh này đang **ngủ**: không có SDK IronSource trong project và `IRONSOURCE_ENABLE` không
> bật. Nên đây là việc dọn trước khi có người bật lại, không gấp.

Liên quan: `Request-Mediation-AdEcpmAndParams.md` mục **V5** cũng là IronSource (nhánh tự khai
`ad_mediation = Max` nên kho không có LevelPlay). Hai việc nên làm một lượt.

---

## N4 — `_ => "fsn_rewarded"` là mìn

Bảng đặt tên placement trong `FsnAdManager`:

```csharp
string placementName = adType switch
{
    FsnAdType.Interstitial => "fsn_interstitial",
    ...
    _ => "fsn_rewarded"     // ⚠
};
```

Hiện 5/5 giá trị enum đã phủ nên nhánh này không chạm tới. Nhưng thêm một `FsnAdType` mới là nó
**âm thầm thành rewarded** — không warning, không lỗi build. Xin đổi sang `throw` hoặc
`Debug.LogError` + một tên trung tính.

---

## N5 — `LogFsn` của Firebase

```csharp
new Parameter("ad_platform", "AdMob"),     // hardcode
new Parameter("ad_format", adFormat),      // ✅ "native", đúng
```

`ad_format` đúng. Nhưng `ad_platform` hardcode `"AdMob"` (nếu FSN có thể chạy nguồn khác thì sai), và
**không gửi `placement`** dù `LogAll` đã có biến đó trong tay. Việc này nằm ở module `Firebase`,
bên mình chỉ báo.

---

## Phần đã ĐÚNG, xin đừng sửa

Để khỏi sửa oan:

- `LogAll(adFormat: "native", ...)` — **đúng**. Format gửi ra ngoài là `native`, không bị dán thành
  reward/inter.
- AppsFlyer nhận `AD_TYPE = "native"` — **đúng**.
- MAX: `type` lấy theo callback nào nổ (`OnInterstitialAdRevenuePaid` → `Interstitial`, …), và
  `adFormatRaw = impressionData.AdFormat` giữ nguyên chuỗi thô — **đúng cả hai**.
- `AdmobMediationFalconBridge.ToFalconAdType` — `_ => AdType.AppOpen` ở đó **an toàn**, vì
  `AdmobAdFormat` chỉ có 4 giá trị và `AppOpen` là giá trị thứ tư. Khác hẳn ca IronSource (ở đó đầu
  vào là **chuỗi tự do**).

⚠ **Một chỗ "lẫn" thật, nhưng không phải lỗi của module này:** ở AppsFlyer, dòng native mang
`PLACEMENT = "fsn_rewarded"`. Nên ai đọc *doanh thu ad theo placement* trong MMP sẽ thấy tiền native
ngồi trong rổ rewarded. Nhãn loại thì vẫn `native` đúng, nên đây là chuyện **luật đọc ở MMP**, bên
mình ghi lại để không ai đi sửa `LogAll` cho hỏng.

---

## Ngoài phạm vi log — một dòng báo cho đủ

`FalconGmaService`, hàm show interstitial, nhánh `OnClose()`:

```csharp
if (_useMultiCall)
    MultiCall.Instance.ShowInterstitial(where, onInterstitialClosed, onFail);
#if GOOGLE_MOBILE_ADS_ENABLE
var adapterResponseInfo = _interstitialAd.GetResponseInfo().GetLoadedAdapterResponseInfo();
...
_interstitialAd.Show();
```

`if` **không có `else`**, rồi vẫn chạy tiếp xuống `_interstitialAd.Show()`. Đường rewarded có `else`,
đường inter không. Nghĩa là khi bật multicall thì show hai lần, hoặc NRE nếu ad GMA chưa load. Không
phải việc của BigData, bên mình chỉ thấy lúc đọc nên báo.

---

## Nghiệm thu

Không có bước bật công tắc nào.

| Mục | Kiểm thế nào |
|---|---|
| N1 | `f_sdk_ads_called_data` break-by (`ad_mediation`, `ad_type`): rổ `Reward` của AdMob từ chỗ **không có dòng nào** thành có |
| N2 | `f_sdk_ads_data` có `sub_event = Native` — giá trị kho đã khai hợp lệ mà **chưa từng có dữ liệu**. Kiểm nhanh ở editor: `AnalyticLogger` in `f_sdk_ads_data` với `type: Native` đúng một lần mỗi native hiện |
| N2 (cột quà) | Dòng native có `prev_full_screen_ad_format` = `Reward`/`Interstitial` và `prev_full_screen_ad_sec` nhỏ ⇒ đúng là native-sau-toàn-màn |
| N3 | Không còn dòng `AppOpen` nào mang `ad_mediation` của IronSource |

Xin bump version module kèm một dòng CHANGELOG cho N1 (có era) và N2 (event mới có dữ liệu lần
đầu) — kho cần hai mốc đó để đọc chuỗi thời gian.
