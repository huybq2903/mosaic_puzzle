# Mediation — ghi lại lần quảng cáo bị CHẶN (`OnShowSkipped`) — bản 2: No Ads + "ad đang chờ"

> **Đọc phần "Việc phải làm" là đủ để sửa.** Mỗi việc: sửa file nào, chép đoạn code nào, kiểm thế nào. Tick `[x]` khi xong.
> Vì sao phải làm, và vì sao làm đúng kiểu này, nằm ở nửa dưới ("📖 Bối cảnh").
> Gửi owner `Core/ThirdParty/Mediation`. **MediationMO không phải sửa gì.**
>
> **Bản 2, viết lại 13/11/2026, đối chiếu code Mediation 1.4.42.** Bản 1 **đã làm xong** trong 1.4.42: `AdSkipReasonMap`,
> `ShowInterstitial` hỏi luật trước `OnShowAttempt`, cổng Lua ghi `Other`, `GetAppOpenSkipReason`, `AppOpenSkipReason`, `Wait02S`
> ghi lý do. Ticket này **chỉ** là phần thêm trên code đó. Số dòng lấy theo 1.4.42; lệch thì tìm theo tên hàm.
>
> ⚠ **Lật lại một điều của bản 1.** CHANGELOG 1.4.42 ghi *"User No Ads không bắn dòng nào"*. Ngày 13/11 kho ký đổi lại
> (`Request-Loader-AdShowSkipped-NoAds.md`): user No Ads **cũng phải có dòng** (lý do `NoAds`), và mọi dòng mang **ad đang chờ**
> (có ad không, network, unit, giá).

**Tóm tắt một câu:** mỗi lần Mediation chặn một interstitial / app open, dòng `f_sdk_ad_show_skipped` phải kèm *"nếu chiếu ngay lúc
đó thì chiếu ad nào, giá bao nhiêu"*. User No Ads cũng phải có dòng.

**Đụng 8 file**, khoảng 130 dòng:

| File | Việc |
|---|---|
| `package.json` | S0: pin BigData 1.7.5 |
| `Scripts/Runtime/Service/BigData/AdSkipReasonMap.cs` | S1: một dòng `LevelLimit` |
| `Scripts/Runtime/Service/FloorPersonalization/InterstitialFloorPersonalization.cs` | S1b ①: xem candidate mà không lấy ra |
| `Scripts/Runtime/Service/MultiCall/Providers/MultiCallProvider.cs` + `MultiCall.cs` | S1b ②: MultiCall sẽ chọn ad nào |
| `Scripts/Runtime/Service/Max/FalconMaxService.cs` | S1b ③ (ad sẽ chiếu, theo đúng thứ tự Lua → FloorPersonalization → MultiCall → unit đơn) + S3 (app open No Ads) |
| `Scripts/Runtime/MediationManager.cs` | S1b ④ + S2 (No Ads, truyền ad đang chờ) |
| `Scripts/Runtime/Service/GoogleMobileAds/FalconGmaService.cs` | S1b ⑤ (tuỳ chọn) + S5 (callback) |
| `Scripts/Runtime/Service/IronSource/FalconIronSourceService.cs` + `README.md` | S5: callback + sửa ví dụ `CanShowInterstitial` |

---

# ✅ Việc phải làm

## [ ] S0 — Pin BigData 1.7.5

`package.json` đang pin `"falcon.modules.core.bigdata": "1.7.4"`. `AdSkipReason.NoAds` / `LevelCap`, kiểu `AdReadyInfo` và hai tham
số mới của `Ad.OnShowSkipped` có từ **1.7.5**:

```json
"falcon.modules.core.bigdata": "1.7.5",
```

## [ ] S1 — `AdSkipReasonMap.cs`: `LevelLimit` → `LevelCap`

Kho ký từ `level_cap` ngày 13/11. Sửa đúng một dòng:

```csharp
case StatusShowAds.LevelLimit: return AdSkipReason.LevelCap;   // trước: Other
```

## [ ] S1b — Hàm "nếu chiếu ngay bây giờ thì chiếu ad nào"

Chỉ Mediation biết chắc ad nào sẽ được chiếu (xem "Bối cảnh" mục 8). BigData có sẵn kiểu `AdReadyInfo` để chở: `adMediation`,
`adNetwork`, `adUnitId`, `estimatedEcpm`. Field nào không biết thì để `null`, BigData gửi **vắng**.

**Ba luật bắt buộc:**
1. **Peek không được đổi trạng thái gì:** không lấy ad ra khỏi pool, không kích load, không xoá giá. Hàm này chạy ở **mỗi** lần bị
   chặn. Nếu nó có tác dụng phụ, nhịp load của MultiCall sẽ đổi chỉ vì có log.
2. **Cùng thứ tự với `FalconMaxService.ShowInterstitial`**, tức là: Lua (phiên Lua) → candidate FloorPersonalization → MultiCall →
   unit đơn. Đổi luật chọn ở `ShowInterstitial` thì đổi cả ở đây.
3. **Đơn vị `estimatedEcpm` = USD / 1000 impression**, giống `f_sdk_ad_load.estimated_ecpm`. Mỗi nguồn giữ giá một kiểu:

   | Nguồn | Đang giữ | Đổi sang `estimatedEcpm` |
   |---|---|---|
   | `AdInfo.Revenue` (MAX), slot MultiCall `.eCpm`, `SecondaryAdSource.BestRevenue` | doanh thu **một** impression | `MultiCall.ToEcpm(x)` (×1000, ≤ 0 ⇒ `null`) |
   | `LuaAdRecord.eCpm` | đã là USD / 1000 | để nguyên (0 = không biết, BigData tự bỏ) |
   | `FloorAdCandidate.estimatedEcpm` | đã là USD / 1000, nullable | để nguyên |

   Đừng tự nhân `× 1000`. Hãy dùng `MultiCall.ToEcpm`, để giá khớp với dòng load theo đúng cùng một cách tính.

### ① `InterstitialFloorPersonalization.cs` — xem candidate tốt nhất mà KHÔNG lấy ra

`TakeBestCandidate()` rút candidate khỏi pool và giữ unit (`_showingAdUnitId`), nên không dùng được cho Peek. Thêm cạnh nó:

```csharp
/// <summary>Candidate SẼ được chiếu nếu chiếu ngay — như TakeBestCandidate nhưng KHÔNG rút khỏi pool.</summary>
public FloorAdCandidate PeekBestCandidate()
{
    PruneReady();   // giống HasReadyCandidate: chỉ bỏ candidate SDK đã huỷ
    return BestReady();
}
```

### ② `MultiCallProvider.cs` — MultiCall sẽ chọn ad nào

⚠ **Không** gọi `SyncMaxSlotsWithSdk` ở đây: hàm đó **kích load** lại slot rỗng (`LoadInterstitialAd`). Thay vào đó, slot nào SDK
không còn ad thì coi như không có giá. Đây đúng là kết quả mà `Sync` tạo ra cho `ShowInterstitial`, chỉ khác là Peek không ghi gì.

```csharp
/// <summary>
/// Ad inter SẼ được chiếu nếu chiếu ngay — CÙNG luật với ShowInterstitial (đắt nhất giữa MAX và nguồn phụ, hoà thì MAX
/// thắng). Không đổi trạng thái: không Sync, không load, không xoá giá. null = không có ad.
/// </summary>
public AdReadyInfo PeekInterstitial()
{
    var maxEntry = default(KeyValuePair<string, MultiCall.AdTypeMultiCall>);
    var maxRevenue = double.NegativeInfinity;
    foreach (var pair in MultiCall.Instance.dictionaryInterstitial)
    {
        if (pair.Value == null) continue;
        var revenue = IsInterstitialReady(pair.Key) ? pair.Value.eCpm : _NO_AD;   // = giá sau khi Sync xoá giá ma
        if (revenue <= maxRevenue) continue;
        maxRevenue = revenue;
        maxEntry = pair;
    }

    if (maxEntry.Value == null) maxRevenue = _NO_AD;
    var secondaryRevenue = SecondaryAdSource.BestRevenue(AdType.Interstitial);
    if (maxRevenue < 0 && secondaryRevenue < 0) return null;   // cùng phép kiểm "không bên nào có ad" của ShowInterstitial

    if (secondaryRevenue > maxRevenue)
        return new AdReadyInfo
        {
            adMediation = _SECONDARY_MEDIATION,   // cùng chữ CSAdStart của nhánh này gửi
            adNetwork = SecondaryAdSource.BestNetwork(AdType.Interstitial),
            adUnitId = SecondaryAdSource.BestAdUnitId(AdType.Interstitial),
            estimatedEcpm = MultiCall.ToEcpm(secondaryRevenue)
        };

    return new AdReadyInfo
    {
        // adMediation để null: BigData lấy của view đang mở (Max / IronSource), đúng với nhánh này
        adNetwork = maxEntry.Value.networkName,
        adUnitId = maxEntry.Key,
        estimatedEcpm = MultiCall.ToEcpm(maxRevenue)
    };
}
```

`MultiCall.cs`, thêm cạnh `IsInterstitialReady()` (file này chưa có `using Falcon.Modules.Core.BigData;`, thêm vào nếu
`AdReadyInfo` không nhận):

```csharp
public AdReadyInfo PeekInterstitial() => GetMultiCallProvider()?.PeekInterstitial();
```

### ③ `FalconMaxService.cs` — ad sẽ chiếu, theo đúng thứ tự của `ShowInterstitial`

Hai field mới, cạnh `_eCpmInterstitial`:

```csharp
private AdReadyInfo _readyInterstitial;   // unit đơn: ad vừa load
private AdReadyInfo _readyAppOpen;
```

`OnInterstitialLoadedEvent`: gán ở **cuối** hàm, cạnh `_networkNameInterstitial = adInfo.NetworkName;` (dòng ~840). Chỗ đó chỉ
có unit đơn đi tới, vì unit MultiCall và unit FloorPersonalization đã `return` sớm, còn unit Lua / DFPool thì bị `ExternalMaxAdUnits`
chặn ngay đầu hàm:

```csharp
_networkNameInterstitial = adInfo.NetworkName;
_readyInterstitial = new AdReadyInfo                                        // ← MỚI
{
    adNetwork = adInfo.NetworkName, adUnitId = adUnitId, estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue)
};
```

⚠ Đừng đặt nó lên đầu hàm cạnh `_eCpmInterstitial`, vì ở đó unit FloorPersonalization sẽ ghi đè.

`OnAppOpenLoadedEvent` (dòng ~400), thêm:

```csharp
_readyAppOpen = new AdReadyInfo                                             // ← MỚI
{
    adNetwork = adInfo.NetworkName, adUnitId = adUnitId, estimatedEcpm = MultiCall.ToEcpm(adInfo.Revenue)
};
```

Hai hàm mới, đặt cạnh `IsInterstitialReady()`. Thứ tự **chép đúng** nhánh `OnClose()` trong `ShowInterstitial`:

```csharp
/// <summary>Ad inter SẼ được chiếu nếu chiếu ngay. Cùng thứ tự với ShowInterstitial. Không đổi trạng thái.</summary>
public AdReadyInfo ReadyInterstitialInfo()
{
#if MAX_ENABLE
    if (_useLua)
    {
        //phiên Lua: ad của Lua trước; Lua không có ad thì candidate FloorPersonalization
        var lua = LuaAdMonetize.PeekBest(AdType.Interstitial, false);
        if (lua != null)
            return new AdReadyInfo
            {
                adMediation = lua.mediation,     // "Max" / "Admob" — cùng chữ LuaAdMonetize gửi ở các dòng ad khác
                adNetwork = lua.networkName,
                adUnitId = lua.adUnitId,
                estimatedEcpm = lua.eCpm         // ĐÃ là USD / 1000
            };
        return ReadyInfoOf(_floorPersonalization.PeekBestCandidate());
    }
#endif
    var candidate = _floorPersonalization.PeekBestCandidate();
    if (candidate != null) return ReadyInfoOf(candidate);
    if (_useMultiCall) return MultiCall.Instance.PeekInterstitial();
    return _readyInterstitial;
}

private static AdReadyInfo ReadyInfoOf(FloorAdCandidate c) => c == null
    ? null
    : new AdReadyInfo { adNetwork = c.networkName, adUnitId = c.adUnitId, estimatedEcpm = c.estimatedEcpm };

public AdReadyInfo ReadyAppOpenInfo() => _readyAppOpen;
```

`_readyInterstitial` / `_readyAppOpen` không cần xoá khi ad được chiếu. Hàm ④ trả `null` khi không có ad, còn lần load sau thì ghi
đè.

### ④ `MediationManager.cs` — hàm công khai, thêm cạnh `IsInterstitialReady()`

```csharp
/// <summary>
/// Ad interstitial SẼ được chiếu nếu chiếu ngay lúc này. null = không có ad / không biết.
/// Game và Mediation truyền nguyên vào Ad.OnShowSkipped(readyAd: …).
/// </summary>
public AdReadyInfo GetReadyInterstitialInfo()
{
    if (!IsInterstitialReady()) return null;   // không có ad thì không có gì để mô tả (cache unit đơn không bị xoá)
    switch (GetAdsService())
    {
        case FalconMaxService max: return max.ReadyInterstitialInfo();
        case FalconGmaService gma: return gma.ReadyInterstitialInfo();   // ⑤, tuỳ chọn
        default:                   return null;                         // IronSource: chỉ gửi adAvailable
    }
}

/// <summary>Ad app open MAX sẽ được chiếu; null = không biết / không có.</summary>
public AdReadyInfo GetReadyAppOpenInfo() => (GetAdsService() as FalconMaxService)?.ReadyAppOpenInfo();
```

`using Falcon.Modules.Core.BigData;` đã có sẵn ở đầu file.

### ⑤ `FalconGmaService.cs` (tuỳ chọn) — AdMob không có giá trước khi chiếu, chỉ có network

```csharp
public AdReadyInfo ReadyInterstitialInfo()
{
    if (_useMultiCall) return MultiCall.Instance.PeekInterstitial();   // _interstitialAd không phải ad sẽ chiếu
#if GOOGLE_MOBILE_ADS_ENABLE
    var name = _interstitialAd?.GetResponseInfo()?.GetLoadedAdapterResponseInfo()?.AdSourceName;
    return name == null ? null : new AdReadyInfo { adMediation = "Admob", adNetwork = name };
#else
    return null;
#endif
}
```

## [ ] S2 — `MediationManager.ShowInterstitial` (dòng ~637): ghi No Ads, truyền ad đang chờ

**Không thay cả hàm.** Hàm 1.4.42 đã đúng thứ tự: luật MO → cổng Lua → `OnShowAttempt` → provider. Chỉ sửa bốn chỗ đánh dấu
`← MỚI`, còn lại giữ nguyên:

```csharp
var reportOpportunity = !IsRemoveAds();
var adAvailable = IsInterstitialReady();                                        // ← MỚI
var readyAd = GetReadyInterstitialInfo();                                       // ← MỚI (null khi không có ad)

if (!reportOpportunity)                                                         // ← MỚI (cả khối)
{
    // No Ads đứng ĐẦU thứ tự xét: ghi lý do này, không ghi luật MO / Lua nào nữa.
    // KHÔNG return: provider tự xử lý No Ads như cũ (gọi onInterstitialClosed) — hành vi với game không đổi.
    FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, placementId, AdSkipReason.NoAds,
        adAvailable: adAvailable, readyAd: readyAd);
}

if (reportOpportunity)
{
    var status = ...;                                                           // giữ nguyên
    if (status != StatusShowAds.Active)
    {
        DebugLog(...);                                                          // giữ nguyên
        FalconBigDataController.Ad.OnShowSkipped(
            AdType.Interstitial, placementId, AdSkipReasonMap.From(status),
            adAvailable: adAvailable, readyAd: readyAd);                       // ← MỚI: hai tham số
        onInterstitialClosed?.Invoke();
        return;
    }
}

if (!IsInterstitialAllowedByLua(placementId))
{
    DebugLog(...);                                                              // giữ nguyên
    if (reportOpportunity)
        FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, placementId, AdSkipReason.Other,
            adAvailable: adAvailable, readyAd: readyAd);                       // ← MỚI: hai tham số
    (onFail ?? onInterstitialClosed)?.Invoke();
    return;
}

if (reportOpportunity)
    FalconBigDataController.Ad.OnShowAttempt(
        AdType.Interstitial,
        adWhere: string.IsNullOrWhiteSpace(placementId) ? null : placementId,
        adAvailable: adAvailable);                                              // ← MỚI: dùng biến đã tính

provider.ShowInterstitial(placementId, onInterstitialClosed, onFail, needAdBreak);
```

User No Ads vẫn đi qua cổng Lua và `OnShowAttempt` như cũ. Hai chỗ đó đã có `if (reportOpportunity)` nên không ghi thêm dòng nào.
Kết quả là **đúng một** dòng `no_ads` cho mỗi lần gọi.

⚠ **Không** gọi `OnShowSkipped` ở tầng dưới (`MediationMOProvider`, `FalconMaxService.ShowInterstitial`, MultiCall). Gọi ở hai tầng
là đếm đôi.

## [ ] S3 — App open MAX: user No Ads cũng là một cơ hội

### `OnApplicationPause` (dòng ~688): bỏ `!IsRemoveAds()`

Lần quay lại app của user No Ads cũng là một cơ hội, nên phải tới được `Wait02S` để ghi:

```csharp
if (_settings.UseAppOpenMax && !pauseStatus)            // ← MỚI: bỏ `&& !IsRemoveAds()`
```

Không có nguy cơ chiếu nhầm: `ShowAppOpenAdIfReady()` tự chặn No Ads ngay dòng đầu, và khối No Ads dưới đây đã `yield break` từ
trước đó.

### `Wait02S` (dòng ~698): thêm khối No Ads, truyền ad đang chờ

```csharp
private IEnumerator Wait02S()
{
    yield return new WaitForSeconds(0.2f);
    if (_isShowingAds)
    {
        _isShowingAds = false;
        yield break;              // lần quay lại do ad / màn mua gây ra — không phải cơ hội, không log
    }

    var adAvailable = false;                                               // ← MỚI (Wait02S nằm NGOÀI #if MAX_ENABLE)
#if MAX_ENABLE
    var id = _settings.AppOpenIdMaxAndroid;
#if UNITY_IOS
    id = _settings.AppOpenIdMaxIOS;
#endif
    adAvailable = MaxSdk.IsAppOpenAdReady(id);
#endif
    var readyAd = adAvailable ? MediationManager.Instance.GetReadyAppOpenInfo() : null;   // ← MỚI

    if (IsRemoveAds())                                                     // ← MỚI (cả khối)
    {
        // User No Ads không được load app open ⇒ adAvailable thường = false. Đó là sự thật, cứ gửi.
        FalconBigDataController.Ad.OnShowSkipped(AdType.AppOpen, _APP_OPEN_WHERE, AdSkipReason.NoAds,
            adAvailable: adAvailable, readyAd: readyAd);
        yield break;
    }

    var skipReason = MediationManager.Instance.GetAppOpenSkipReason();    // giữ nguyên
    if (skipReason == null)
    {
        ShowAppOpenAdIfReady();
    }
    else
    {
        FalconBigDataController.Ad.OnShowSkipped(AdType.AppOpen, _APP_OPEN_WHERE, skipReason.Value,
            adAvailable: adAvailable, readyAd: readyAd);                  // ← MỚI: hai tham số
    }
}
```

Không thêm gì ở `ShowAppOpenAdIfReady()`, ở lần chiếu app open lúc khởi động, và ở app open GMA / IronSource (xem "Bối cảnh" mục 5).

## [ ] S4 — Kiểm

Bật log BigData (Devkit) rồi chạy game dùng MediationMO:

| # | Làm | Phải thấy |
|---|---|---|
| 1 | Chiếu interstitial ở `level_end`, ad có sẵn | Đúng **một** `show_attempt`, **không** có `show_skipped` |
| 2 | Chiếu lại ngay (còn `intervalTime`) | Đúng **một** `show_skipped`, `skipReason = cooldown`, `adAvailable = true`, có `adUnitId` / `estimatedEcpm`; **không** có `show_attempt`; game vẫn nhận callback đóng |
| 3 | Placement có `LevelLimit` đã đầy | `skipReason = level_cap` |
| 4 | `SetRemoveAds(true)`, chiếu interstitial (ad có sẵn) | Đúng **một** `show_skipped`, `skipReason = no_ads`, `adAvailable = true`, có network / unit / giá; **không** có `show_attempt`; game vẫn nhận callback đóng |
| 5 | Ca 4 trong phiên Lua, script Lua từ chối | Vẫn đúng **một** dòng `no_ads` (không thêm `other`) |
| 6 | User No Ads xuống nền rồi quay lại (app open MAX) | Một `show_skipped`, `type = AppOpen`, `skipReason = no_ads`, thường `adAvailable = false`; **không** có app open nào hiện |
| 7 | Xem xong interstitial rồi app quay lại (cờ `_isShowingAds`) | **Không** có dòng nào cho app open |
| 8 | Ca 2 ở phiên MultiCall / phiên Lua / có FloorPersonalization | `adUnitId` / `estimatedEcpm` của dòng skipped **trùng** ad mà lần chiếu ngay sau đó (hết cooldown) thật sự chiếu, nếu không có ad mới load xen giữa |
| 9 | Ca 2 ở phiên MultiCall, đếm request load | Bị chặn **không** sinh thêm request load nào (Peek không được kích load) |
| 10 | AdMob thắng ở MultiCall (nguồn phụ đắt hơn) | `adMediation = Admob`, `adNetwork` của AdMob |

Bất biến, theo từng placement, cho **mọi user (kể cả No Ads)**: **số lần gọi `ShowInterstitial` = số `show_attempt` + số
`show_skipped`**. Thấy cảnh báo BigData *"thiếu adAvailable"* ⇒ còn một chỗ gọi `OnShowSkipped` chưa truyền tham số mới. Thấy cảnh
báo *"có readyAd nhưng adAvailable != true"* ⇒ có chỗ truyền `readyAd` mà không lấy qua `GetReadyInterstitialInfo()`.

## [ ] S5 — Game không bị treo khi luôn gọi `ShowInterstitial`; sửa ví dụ trong README

Ticket game (`BigData/docs/games/Request-Game-AdShowSkipped.md`, bước G1b) yêu cầu game **luôn** gọi `ShowInterstitial(placement,
onClosed, onFail)` ở mỗi điểm chiếu, kể cả khi `CanShowInterstitial` trả `false`. Lý do nằm ở "Bối cảnh" mục 9. Muốn vậy thì mỗi lượt
gọi phải trả về **đúng một** callback. Hiện còn bốn chỗ `return` mà không gọi callback nào:

| File | Chỗ | Sửa |
|---|---|---|
| `FalconGmaService.ShowInterstitial` (dòng ~470) | `_settings == null` · `!UseInterstitialGa` · chưa init | `onFail?.Invoke(); return;` (như `FalconMaxService`) |
| `FalconIronSourceService.ShowInterstitial` (dòng ~560) | `_settings == null` | `onFail?.Invoke(); return;` |

`README.md` mục 6: ví dụ hiện là `if (CanShowInterstitial) {…ShowInterstitial…} else OnContinue();`. Game viết theo kiểu này thì
mọi lần bị chặn (No Ads, MO, Lua, không có ad) đều **vô hình**. Sửa thành:

```csharp
var placementId = "IntertitialWinGame";
if (MediationManager.Instance.CanShowInterstitial(placementId))
{
    //chắc chắn chiếu được -> hiện popup ad break ... rồi
    MediationManager.Instance.ShowInterstitial(placementId, OnContinue, OnContinue);
}
else
{
    //KHÔNG chiếu gì — vẫn phải gọi để Mediation ghi lần này (bị chặn / không có ad). Gọi lại OnContinue như nhánh trên.
    MediationManager.Instance.ShowInterstitial(placementId, OnContinue, OnContinue);
}
```

Thêm một dòng vào bảng của mục đó: *"`CanShowInterstitial` chỉ để quyết định có hiện popup hay không. Mỗi điểm chiếu vẫn phải gọi
`ShowInterstitial`, nếu không thì lần đó không được ghi."*

❓ **Nhờ owner xác nhận một hệ quả.** Ở nhánh không có ad, `FalconMaxService.ShowInterstitial` chạy `_floorPersonalization.OnShowMiss()`
và `LoadInterstitial()`. Trước đây game theo mẫu README đi tiếp luôn, nên các lần đó không tới được đây. Sau G1b chúng tới được, và
FloorPersonalization sẽ thấy thêm lượt "đến điểm chiếu mà không có ad". Về mặt dữ liệu thì đó là sự thật, nhưng nó đổi đầu vào của
thuật toán. Nếu không muốn vậy, hãy cho biết để bên mình tính cách khác (vd. một hàm chỉ ghi, không chiếu).

---

# 📖 Bối cảnh (đọc khi cần hiểu mình đang làm gì)

## 1. `f_sdk_ad_show_skipped` là gì

Event mới của BigData 1.7.4, kho (loader) đã ký ngày 12/11/2026. **Một dòng = một cơ hội chiếu interstitial / app open bị luật chặn**,
kèm **một** lý do: lý do **đầu tiên** chặn nó. Bốn field: `type`, `adWhere` (bắt buộc), `adWhen` (tuỳ chọn), `skipReason`.
Ngày 13/11 kho ký thêm: lý do `no_ads` (user có quyền bỏ quảng cáo, **đứng đầu** thứ tự xét), `level_cap`, và 6 field **ad đang
chờ** (`adAvailable`, `adViewId`, `adMediation`, `adNetwork`, `adUnitId`, `estimatedEcpm`) — mục 8.

Kho dùng nó để trả lời câu mà hôm nay **không trả lời được**: *"mỗi luật bớt quảng cáo đang cắt bao nhiêu lần chiếu, ở đâu, với
nhóm user nào?"* Ví dụ: cooldown 60 giây chặn bao nhiêu phần trăm cơ hội ở `level_end`; tăng `levelUnlock` thì mất bao nhiêu lần
chiếu của user mới. Đây là câu hỏi của đội monetize khi chỉnh config MediationMO.

Công thức kho đã ký:

```
cơ hội     = count(show_attempt) + count(show_skipped)          theo type / adWhere
tỉ lệ chặn = count(show_skipped) / cơ hội                      theo skipReason
```

## 2. Vì sao phải là Mediation gọi, không phải game

Với game dùng MediationMO, **luật không nằm ở game**. Game chỉ gọi `MediationManager.ShowInterstitial(placement, onClosed)`.
Bên trong, `MediationMOProvider.ShowInterstitial` hỏi `GetStatusAdsFromPlacement` và khi bị chặn thì:

```csharp
if (!CanShowInterstitial(placementId))
{
    onInterstitialClosed?.Invoke();   // game thấy y như ad vừa chiếu xong
    return;
}
```

Game không phân biệt được *"ad vừa chiếu"* với *"luật chặn"*, càng không biết luật nào. Chỉ Mediation có `StatusShowAds` trong tay.

Game **tự viết** luật riêng (không qua MediationMO) thì vẫn tự gọi `OnShowSkipped`, theo ticket game
`BigData/docs/games/Request-Game-AdShowSkipped.md`. Hai đường không chồng nhau: luật của game chặn thì game không gọi
`MediationManager.ShowInterstitial`, nên Mediation không thấy lần đó.

## 3. Vì sao kiểm luật TRƯỚC `OnShowAttempt`

`show_attempt` (V4 của `Request-Mediation-AdEventV2.md`) đo **"game muốn chiếu, kho quảng cáo có ad không"**. Kho đã có công thức
`view_fill_rate = count(adAvailable = true) / count(*)` trên event đó.

Nếu `show_attempt` bắn cả cho lần bị luật chặn:
- cơ hội bị đếm **hai lần** (một `attempt` + một `skipped`);
- `view_fill_rate` lẫn cả những lần không hề định chiếu.

Vì vậy: **qua luật ⇒ `attempt`; bị chặn ⇒ `skipped`; không bao giờ cả hai.** V4 viết trước khi có event này nên đặt `attempt` ở
đầu hàm; bản 1 (đã có trong 1.4.42) sửa lại thứ tự cho interstitial. Với app open thì V4 đã đúng sẵn (`attempt` nằm trong `ShowAppOpenAdIfReady`, sau luật).

## 4. Hỏi luật hai lần có an toàn không

S2 hỏi `GetStatusAdsFromPlacement` một lần, rồi `provider.ShowInterstitial` của MediationMO hỏi lại lần nữa. An toàn, vì:
- hai lần gọi liền nhau, đồng bộ, không có gì đổi bộ đếm ở giữa;
- các luật theo thời gian chỉ **nới ra** khi thời gian trôi (cooldown hết dần, qua nửa đêm thì trần ngày reset), không bao giờ siết
  lại. Lần đầu nói "được chiếu" thì lần hai cũng nói "được chiếu";
- phần reset bộ đếm theo ngày bên trong hàm đó là idempotent — lần hai không reset thêm gì.

Nên không có ca "log skipped mà ad vẫn chiếu", hay ngược lại. Hành vi với game cũng không đổi: lần bị chặn vẫn nhận
`onInterstitialClosed` như trước, chỉ là gọi sớm hơn một tầng.

## 5. Những ca KHÔNG gọi, và vì sao

| Ca | Vì sao không gọi |
|---|---|
| **Rewarded** | Người chơi tự bấm, không phải "cơ hội game tự bật". Rewarded không có ad thì đã nằm ở `show_attempt.adAvailable = false` |
| **Banner** | Không có điểm chiếu |
| Lần quay lại có cờ `_isShowingAds` | Cờ bật khi chiếu interstitial / rewarded, khi mở màn mua IAP (`EVENT_START_PURCHASE`), hoặc khi game gọi `IgnoreNextAoa()`. Cả ba đều là lần app bị **che** (ad toàn màn, màn thanh toán của store) rồi hiện lại, không phải người chơi rời app rồi quay về. Không phải cơ hội |
| App open lúc **khởi động** | Đường này không có luật nào, chỉ có kiểm "ad có sẵn chưa" (thuộc `show_attempt`) |
| App open của **GMA** | `MediationManager.CanShowAppOpen` luôn trả `true` ngoài nhánh MAX, nên hôm nay không có luật nào chặn app open GMA. Không có gì để log. (`FalconGmaService.InternalCanShowAppOpen` có cooldown nhưng không ai gọi tới nó) |
| **IronSource** | Không có app open (`InternalCanShowAppOpen` luôn `false`). Interstitial IronSource vẫn đi qua `MediationManager.ShowInterstitial` nên S2 đã phủ |

## 6. `LevelLimit` → `LevelCap`

`LevelLimit` = *"đã chiếu đủ N lần trong level này"*. Bộ từ ký 12/11 chưa có từ cho ý này (`session_cap` / `daily_cap` là trần theo
phiên / ngày, `level_interval` là khoảng cách giữa các level), nên bản 1 của ticket để `other`. Kho ký từ `level_cap` ngày 13/11 ⇒
bản 2 map thẳng sang `AdSkipReason.LevelCap`.

## 7. Liên quan: `app_open` hot ảo ở GMA và IronSource (chưa cần làm trong ticket này)

BigData 1.7.4 sửa một lỗi: mỗi lần quảng cáo toàn màn (≥ 30 giây) đóng, SDK ghi nhầm một lần *"người chơi quay lại app"* (`app_open`
hot), và tính thời gian xem quảng cáo thành thời gian nằm ngoài app. Cách sửa: BigData nhớ thời điểm `Ad.OnShown` của quảng cáo toàn
màn; pause bắt đầu trong 10 giây sau đó thì không tính là "rời app".

Cách sửa chỉ chạy ở đường nào **gọi `Ad.OnShown` ngay trước khi gọi Show của SDK quảng cáo**: MAX, MultiCall, FSN, AdMob bridge đã có.
**`FalconGmaService` và `FalconIronSourceService` chưa gọi** (chúng chưa chuyển sang bộ log vòng đời — mục H5 của
`Request-Mediation-AdEventV2.md`), nên ở hai đường này `app_open` hot ảo vẫn còn. Kho **chưa** có bộ lọc bù phía server (đang
trình owner kho, 13/11).

Khi làm H5, nhớ hai điều cho riêng lỗi này:
- `FalconBigDataController.Ad.OnShown(type, adWhere, mediation)` phải gọi **đồng bộ, ngay trước** `_appOpenAd.Show()` /
  `_interstitialAd.Show()` / `_rewardedAd.Show()` (GMA) và `ShowAd()` (IronSource) — không đặt trong callback "đã hiện" của SDK
  quảng cáo, vì callback đó có thể về **sau** khi Unity đã pause;
- `Ad.OnClosed` / `Ad.OnShowFailed` ở callback đóng / lỗi, để BigData biết quảng cáo đã hết.

## 8. No Ads và "ad đang chờ" — vì sao thêm, vì sao Mediation phải trả ad

**Mục đích:** đo gói No Ads lấy đi bao nhiêu quảng cáo và bao nhiêu tiền. Mediation **vẫn load interstitial bình thường cho user No
Ads** (chỉ chặn ở bước show), nên lúc bị chặn thường có một ad thật đang nằm chờ, với network và giá. Ghi lại thì kho cộng được
"tiền lẽ ra thu" theo từng điểm chiếu. Bản 1 không ghi gì cho No Ads vì hợp đồng 12/11 nói vậy; kho đã ký lật lại ngày 13/11.

**Ai chặn thì người đó ghi, không bao giờ cả hai:**
- Game gọi `MediationManager.ShowInterstitial` cho cả user No Ads ⇒ Mediation ghi (S2).
- Game tự kiểm No Ads rồi **không** gọi `ShowInterstitial` ⇒ Mediation không thấy, game tự ghi (ticket game, G2).

**Vì sao BigData không tự điền network / unit / giá:** BigData chỉ nghe Mediation báo *"vừa load xong ad ở unit X, giá Y"*. Nó không
biết (a) trong kho **đang còn** những ad nào — ad hết hạn hay bị bỏ không được báo, (b) MultiCall sẽ **chọn** ad nào trong nhiều unit
và nguồn AdMob, hay phiên Lua / FloorPersonalization sẽ đi trước MultiCall. Tự đoán thì phải chép lại logic kho và logic chọn của
Mediation, và hai bản code sẽ lệch nhau. Chỉ Mediation trả lời chắc được, nên S1b là một hàm của Mediation. Peek phải dùng **cùng** thứ
tự với `ShowInterstitial`; nếu không, dòng skipped sẽ trỏ vào một ad khác với ad thật sự sẽ chiếu.

**Đọc số cho đúng** (đã ghi trong hợp đồng):
- `estimatedEcpm` là giá **dự kiến lúc load**, không phải doanh thu thật; AdMob trực tiếp không có giá.
- Bị chặn **không** tiêu ad: năm lần chặn liên tiếp trỏ về cùng một ad (cùng `adViewId`, cùng giá). Đúng như thế.
- App open của user No Ads thường `adAvailable = false` vì không được load — không phải hụt fill.

## 9. Vì sao game phải luôn gọi `ShowInterstitial` (S5)

`CanShowInterstitial` trả `false` khi: user No Ads, luật MO chặn, Lua từ chối, **hoặc** chưa có ad. README 1.4.42 dạy game viết
`if (CanShowInterstitial) Show… else OnContinue();`. Game viết đúng như thế thì ở tất cả các ca trên, `ShowInterstitial` **không bao
giờ được gọi**: Mediation không thấy lần đó, không có `show_skipped` (No Ads / MO / Lua), cũng không có `show_attempt` với
`adAvailable = false` (không có ad). Bất biến *"số lần đến điểm chiếu = attempt + skipped"* gãy, và fill rate bị đếm thiếu.

Cách sửa ít nhất: `CanShowInterstitial` chỉ dùng để quyết định **có hiện popup hay không**; điểm chiếu nào cũng gọi `ShowInterstitial`.
Khi bị chặn hay không có ad, hàm đó không chiếu gì, chỉ ghi lại rồi gọi callback, và game đi tiếp qua callback. Điều kiện để làm
được: mỗi lượt gọi phải trả về **đúng một** callback, nên S5 vá bốn chỗ `return` im lặng của GMA / IronSource.

## Tham chiếu

- Hợp đồng event: `BigData/docs/loader/Request-Loader-AdShowSkipped.md` (kho ký 12/11/2026) và
  `BigData/docs/loader/Request-Loader-AdShowSkipped-NoAds.md` (kho ký 13/11/2026: `no_ads`, `level_cap`, 6 field ad đang chờ).
- Spec: `BigData/docs/design/DWH-EventLog-API-Spec.md` mục 5.11d-2.
- API: `FalconBigDataController.Ad.OnShowSkipped(AdType type, string adWhere, AdSkipReason skipReason, string adWhen = null,
  bool? adAvailable = null, AdReadyInfo readyAd = null)`.
- Mốc `show_attempt`: `Request-Mediation-AdEventV2.md` mục V4 (Chỗ 1 đã được thay trong 1.4.42).
- Ticket game đi cùng: `BigData/docs/games/Request-Game-AdShowSkipped.md`.
