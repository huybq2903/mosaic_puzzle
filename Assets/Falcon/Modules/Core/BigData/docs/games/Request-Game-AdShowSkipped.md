# Game — ghi lần quảng cáo bị CHẶN theo BigData 1.7.5 (`OnShowSkipped`: No Ads + "ad đang chờ")

> **Đọc phần "Việc phải làm" là đủ để sửa.** Mỗi việc: sửa ở đâu, chép đoạn code nào, kiểm thế nào. Tick `[x]` khi xong.
> Vì sao phải làm nằm ở nửa dưới ("📖 Bối cảnh").
> Viết 13/11/2026, đối chiếu Mediation 1.4.42. Gửi: **mọi game** có dùng quảng cáo interstitial. Đi cùng ticket Mediation
> `BigData/docs/modules/Request-Mediation-ShowGate.md` (bản 2).

**Tóm tắt một câu:** từ BigData 1.7.5, mỗi lần interstitial bị chặn (do luật của game, luật MediationMO, hoặc vì user đã mua No Ads)
đều phải có **đúng một** dòng `f_sdk_ad_show_skipped`, kèm thông tin **ad đang nằm chờ** lúc đó. Game chỉ ghi phần **game tự chặn**;
phần Mediation chặn thì Mediation ghi.

**Game cần làm gì, tuỳ trường hợp:**

| Game của bạn | Việc |
|---|---|
| Không có luật bớt quảng cáo riêng, không tự kiểm No Ads (mọi thứ để MediationMO lo), **và** điểm chiếu nào cũng gọi `ShowInterstitial` | Chỉ **G0** (nâng bản). Không phải sửa code |
| Dùng `CanShowInterstitial` để quyết định **có gọi** `ShowInterstitial` hay không (mẫu trong README Mediation) | **G0** + **G1b**. ⚠ Rất hay gặp, hãy tìm `CanShowInterstitial` trong code game |
| Có luật riêng và đã gọi `OnShowSkipped` (theo BigData 1.7.4) | **G0** + **G1** |
| Tự kiểm No Ads rồi **không** gọi `ShowInterstitial` | **G0** + **G2** |
| Chưa từng gọi `OnShowSkipped` dù có luật riêng | **G0** + **G1** (thêm mới) + đọc mục B8 của `BigData/docs/design/Integration-NewGame.md` |

---

# ✅ Việc phải làm

## [ ] G0 — Nâng BigData lên 1.7.5

```json
"falcon.modules.core.bigdata": "1.7.5",
```

Code gọi `OnShowSkipped` kiểu 1.7.4 vẫn biên dịch. Chỉ là BigData sẽ in cảnh báo *"thiếu adAvailable"* cho tới khi làm G1.

## [ ] G1 — Chỗ game tự chặn: truyền thêm "ad đang chờ"

Tìm mọi chỗ gọi `OnShowSkipped` trong code game. Thêm hai tham số:

```csharp
// TRƯỚC (1.7.4)
FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.Cooldown);

// SAU (1.7.5)
FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.Cooldown,
    adAvailable: MediationManager.Instance.IsInterstitialReady(),
    readyAd: MediationManager.Instance.GetReadyInterstitialInfo());
```

- `adAvailable`: lúc bị chặn, Mediation có ad sẵn không. **Bắt buộc từ 1.7.5.**
- `readyAd`: ad đó là gì (network, unit, giá). Game **không** tự dựng, lấy từ Mediation. Hàm tự trả `null` khi không có ad, nên
  cứ truyền thẳng, không cần tự kiểm `adAvailable` trước.
- ⚠ `GetReadyInterstitialInfo()` chỉ có khi Mediation đã làm ticket ShowGate bản 2. Mediation **chưa** có hàm này thì **bỏ dòng
  `readyAd`**, chỉ truyền `adAvailable`. Khi Mediation có thì thêm vào sau.
- Game có **nhiều** luật cùng chặn: chỉ gọi **một lần**, với luật **đầu tiên** chặn (theo thứ tự game xét).

## [ ] G1b — Mẫu `CanShowInterstitial`: điểm chiếu nào cũng phải gọi `ShowInterstitial`

Tìm `CanShowInterstitial` trong code game. Code kiểu này (mẫu cũ trong README Mediation) làm **mọi** lần bị chặn thành vô hình, gồm
No Ads, luật MediationMO, Lua và cả lần không có ad:

```csharp
// TRƯỚC — bị chặn thì không gọi ShowInterstitial ⇒ Mediation không thấy, không ai ghi
if (MediationManager.Instance.CanShowInterstitial("level_end"))
{
    ShowAdBreakPopup();
    MediationManager.Instance.ShowInterstitial("level_end", OnContinue, OnContinue);
}
else
{
    OnContinue();
}
```

Sửa nhánh `else`, để `CanShowInterstitial` **chỉ** quyết định có hiện popup hay không:

```csharp
// SAU
if (MediationManager.Instance.CanShowInterstitial("level_end"))
{
    ShowAdBreakPopup();
    MediationManager.Instance.ShowInterstitial("level_end", OnContinue, OnContinue);
}
else
{
    // Không chiếu gì: Mediation chỉ ghi lần này (bị chặn / không có ad) rồi gọi OnContinue.
    MediationManager.Instance.ShowInterstitial("level_end", OnContinue, OnContinue);
}
```

- **Phải truyền cả `onClosed` lẫn `onFail`**, cùng một hàm đi tiếp. Bị chặn thì Mediation gọi `onClosed`; không có ad hoặc Lua từ
  chối thì Mediation gọi `onFail`. Thiếu `onFail` thì game có thể bị **treo** ở màn đó.
- Game dùng **AdMob (GMA)** hoặc **IronSource**: chờ Mediation làm xong S5 của ticket ShowGate bản 2 rồi mới đổi. Bản cũ có chỗ
  không gọi callback nào khi chưa init. Game dùng MAX thì đổi được ngay.
- Đừng tự gọi `OnShowSkipped` ở nhánh `else`: game không biết lý do, Mediation mới biết.

## [ ] G2 — Game tự chặn user No Ads: ghi lý do `NoAds`

Chỉ làm nếu game có đoạn kiểu này ở điểm chiếu:

```csharp
if (UserHasNoAds()) return;                       // game tự chặn, KHÔNG gọi MediationManager.ShowInterstitial
MediationManager.Instance.ShowInterstitial("level_end", OnClosed);
```

🔴 **Trước tiên kiểm:** quyền No Ads của game có được báo cho Mediation không? Tức là lúc mua / khôi phục gói, game có gọi
`MediationManager.Instance.SetRemoveAds(true)` không (lúc hết hạn thì `SetRemoveAds(false)`)?

- **Có** ⇒ cách đơn giản nhất (khuyên dùng): bỏ dòng `if` đó, cứ gọi `ShowInterstitial`. Mediation tự bỏ qua user No Ads (gọi
  `OnClosed`), và từ ticket ShowGate bản 2 thì Mediation tự ghi dòng `no_ads`. Khi đó game không phải ghi gì.
- **Không / không chắc** (game giữ cờ riêng: IAP riêng, kiểm thuê bao, quyền từ server…) ⇒ **giữ** khối kiểm. Bỏ khối kiểm lúc này
  thì người đã mua gói sẽ **thấy quảng cáo**. Sửa khối kiểm thành:

```csharp
if (UserHasNoAds())
{
    FalconBigDataController.Ad.OnShowSkipped(AdType.Interstitial, "level_end", AdSkipReason.NoAds,
        adAvailable: MediationManager.Instance.IsInterstitialReady(),
        readyAd: MediationManager.Instance.GetReadyInterstitialInfo());   // bỏ dòng này nếu Mediation chưa có hàm
    return;
}
MediationManager.Instance.ShowInterstitial("level_end", OnClosed);
```

- `NoAds` đứng **đầu** thứ tự xét: user có No Ads thì ghi `NoAds`, **không** ghi luật nào khác, kể cả khi cooldown cũng đang chặn.
  ⇒ Đặt khối No Ads **trước** các luật khác của game.
- 🔴 Game **vẫn gọi** `ShowInterstitial` cho user No Ads (để Mediation tự bỏ qua) ⇒ **đừng** ghi gì. Mediation tự ghi (ticket ShowGate
  bản 2). Ghi cả hai là đếm đôi.

## [ ] G3 — Không ghi thay Mediation

**Đừng** gọi `OnShowSkipped` cho:
- luật cấu hình trong **MediationMO** (cooldown, trần ngày / phiên / level, level mở khoá…) và script **Lua**
  (`IsShowInterstitial`): chúng chặn **bên trong** `ShowInterstitial`, Mediation ghi;
- **app open**: lúc app quay lại là việc của Mediation, game không chạm tới;
- **rewarded**, **banner**: không thuộc event này.

## [ ] G4 — Kiểm

Bật log BigData (Devkit), chạy game:

| # | Làm | Phải thấy |
|---|---|---|
| 1 | Bị luật **của game** chặn ở `level_end`, Mediation có ad sẵn | Đúng **một** `f_sdk_ad_show_skipped`: lý do của game, `adAvailable = true`; có `adNetwork` / `adUnitId` / `estimatedEcpm` nếu đã truyền `readyAd`. **Không** có `f_sdk_ad_show_attempt` |
| 2 | Như ca 1 nhưng Mediation chưa có ad | `adAvailable = false`, **không** có network / unit / giá |
| 3 | User No Ads, game tự chặn (G2) | Đúng **một** dòng, `skipReason = no_ads` |
| 4 | User No Ads, game vẫn gọi `ShowInterstitial` | Game **không** ghi gì. Mediation đã làm ShowGate bản 2 ⇒ có **một** dòng `no_ads` từ Mediation; chưa làm ⇒ không có dòng nào (đúng, chờ Mediation) |
| 5 | Không bị chặn | Không có dòng skipped; có `show_attempt` (Mediation ghi) |
| 6 | Log có cảnh báo *"thiếu adAvailable"* | Còn chỗ gọi `OnShowSkipped` chưa làm G1 |
| 7 | (G1b) Đến điểm chiếu khi `CanShowInterstitial = false` (vd. còn cooldown của MediationMO) | Game đi tiếp bình thường, không treo; có **một** `show_skipped` (Mediation ghi) hoặc **một** `show_attempt` với `adAvailable = false` (không có ad) |

Bất biến, theo từng điểm chiếu: **số lần đến điểm chiếu = số `show_attempt` + số `show_skipped`.** Không lần nào có cả hai.

`adWhere` (`"level_end"`…) phải **trùng** chữ game truyền làm `placementId` khi gọi `ShowInterstitial`.

---

# 📖 Bối cảnh (đọc khi cần hiểu mình đang làm gì)

## 1. Event này để làm gì

`f_sdk_ad_show_skipped`: **một dòng = một lần interstitial / app open lẽ ra được chiếu nhưng bị chặn**, kèm **một** lý do. Kho
dùng nó để trả lời *"mỗi luật bớt quảng cáo cắt bao nhiêu lần chiếu, ở đâu"*. Từ 1.7.5 thêm câu *"gói No Ads lấy đi bao nhiêu quảng
cáo và bao nhiêu tiền"*.

## 2. Vì sao từ 1.7.5 phải ghi cả user No Ads

Bản 1.7.4 dặn **không** ghi cho user No Ads. Ngày 13/11 kho ký đổi lại, vì muốn đo tác động của gói No Ads theo từng điểm chiếu.
Mediation **vẫn load** interstitial cho user No Ads (chỉ chặn ở bước chiếu), nên lúc bị chặn thường có một ad thật đang chờ, có
network và giá. Ghi lại thì kho cộng được "tiền lẽ ra thu".

## 3. Vì sao lấy "ad đang chờ" từ Mediation

Chỉ Mediation biết chắc *"nếu chiếu ngay bây giờ thì chiếu ad nào"*: MultiCall load nhiều unit cùng lúc và chọn ad đắt nhất; ad hết
hạn thì không báo ra ngoài. Game và BigData đều chỉ hỏi lại Mediation. Vì vậy `readyAd` là một hàm của Mediation, game chỉ chuyển
nguyên vào.

## 4. Ai ghi cái gì

| Ai chặn | Ai gọi `OnShowSkipped` |
|---|---|
| Luật riêng của game | **Game** (G1) |
| Luật MediationMO, Lua (kể cả khi game đã hỏi `CanShowInterstitial`) | Mediation, **miễn là game gọi `ShowInterstitial`** (G1b). Không có ad thì Mediation ghi `show_attempt` với `adAvailable = false` |
| Game tự kiểm No Ads, không gọi `ShowInterstitial` | **Game** (G2) |
| Game gọi `ShowInterstitial`, user có No Ads | Mediation |
| App open lúc quay lại app | Mediation |

## 5. Vì sao `CanShowInterstitial` làm mất dòng

`CanShowInterstitial` trả `false` cho cả bốn ca: No Ads, luật MO, Lua từ chối, không có ad. Chỉ `ShowInterstitial` mới ghi lại lần
đó. Game gặp `false` mà đi tiếp luôn thì kho thấy như thể **người chơi chưa từng tới điểm chiếu**: tỉ lệ chặn bị hụt, và fill rate bị
đếm thiếu. G1b không đổi gì người chơi thấy, vì trong cùng một frame `ShowInterstitial` cũng sẽ không chiếu gì.

## 6. Đọc số cho đúng

- `estimatedEcpm` là giá **dự kiến lúc load**, không phải doanh thu thật. AdMob trực tiếp không có giá trước khi chiếu.
- Bị chặn **không** tiêu ad: chặn năm lần liên tiếp thì năm dòng trỏ cùng một ad (cùng `adViewId`, cùng giá). Đúng như thế.

## Tham chiếu

- Hợp đồng: `BigData/docs/loader/Request-Loader-AdShowSkipped.md` (12/11) và `Request-Loader-AdShowSkipped-NoAds.md` (13/11).
- Hướng dẫn game mới: `BigData/docs/design/Integration-NewGame.md` mục B8.
- Ticket Mediation: `BigData/docs/modules/Request-Mediation-ShowGate.md` (bản 2).
- API: `FalconBigDataController.Ad.OnShowSkipped(AdType type, string adWhere, AdSkipReason skipReason, string adWhen = null,
  bool? adAvailable = null, AdReadyInfo readyAd = null)`.
