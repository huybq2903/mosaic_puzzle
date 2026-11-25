# SDK BigData → loader: sửa hợp đồng `f_sdk_ad_show_skipped` — gửi cả user No Ads, kèm "ad lẽ ra đã chiếu" (13/11/2026)

> Gửi: loader. Từ: owner SDK BigData. Sửa thư `Request-Loader-AdShowSkipped.md` mục 1 (các bạn ký 12/11, SDK đã ship ở
> BigData 1.7.4).
> **Chưa code, chờ các bạn ký.** Có hai thay đổi, cả hai chỉ **thêm**: không đổi tên event, không đổi nghĩa field cũ.

## Tóm tắt

| # | Thay đổi | Lật lại điều đã ký? | Kho cần làm |
|---|---|---|---|
| 1 | User có quyền No Ads **có** dòng skip, với `skipReason = no_ads` (từ mới) | **Có**: câu 2 thư 12/11 ("không gửi dòng cho user No Ads") | Ký từ `no_ads`; tách nó ra khi tính tỉ lệ chặn theo luật |
| 2 | Thêm 6 field mô tả **ad đang nằm sẵn** lúc bị chặn: `adAvailable`, `adViewId`, `adMediation`, `adNetwork`, `adUnitId`, `estimatedEcpm` | Không, chỉ thêm | Thêm cột; đọc theo luật ở mục 2 |

**Vì sao đổi ý.** Thư 12/11 nói gửi dòng cho user No Ads "chỉ tốn dòng mà không thêm thông tin". Câu đó sai ở một điểm: bên mình
muốn đo **gói No Ads lấy đi bao nhiêu quảng cáo và bao nhiêu tiền**. Muốn đo thì phải thấy từng cơ hội chiếu của người mua, và
biết lúc đó có ad nào đang chờ, đáng giá bao nhiêu. Hôm nay kho chỉ ước lượng được bằng nhịp xem ad trước khi mua. Cách đó dùng
được, nhưng không tách được theo điểm chiếu và không biết lúc đó có ad sẵn hay không.

Mediation **vẫn load interstitial bình thường cho user No Ads** (chỉ chặn ở bước show). Vì vậy lúc bị chặn, phần lớn
các lần đều có một ad thật đang nằm chờ, với network và giá dự kiến. Thông tin đó hiện đang bị bỏ phí.

---

## 1. Lý do mới `no_ads`

| Giá trị | Nghĩa |
|---|---|
| `no_ads` | User đang có quyền bỏ quảng cáo (mua gói hoặc thuê bao còn hạn) |

- **Đứng đầu thứ tự xét.** User No Ads thì mọi cơ hội ghi `no_ads`, không ghi luật nào khác (cooldown, trần…), kể cả khi luật
  khác cũng chặn. Luật "một lý do, lý do đầu tiên" giữ nguyên.
- **Khác `segment_rule`.** Thư 12/11 ghi `segment_rule` gồm cả "đã trả tiền". Từ nay quyền No Ads có từ riêng. `segment_rule`
  vẫn dùng cho luật nhóm khác (campaign, nhóm IAP…).
- **Cùng nghĩa với nhãn `no_ads`** của `f_sdk_user_label`. Nhãn cho biết *ai* có quyền; dòng skip cho biết *mất bao nhiêu cơ
  hội*. Hai thứ bổ sung, không thay nhau.
- **Áp cho cả hai format game tự bật:** `Interstitial` và `AppOpen`. Rewarded không đổi gì: gói No Ads không bỏ rewarded.

**Hệ quả với công thức đã ký.**

```
cơ hội                = count(show_attempt) + count(show_skipped)                  (giữ nguyên)
tỉ lệ chặn do luật    = count(show_skipped where skip_reason != 'no_ads') / cơ hội của user KHÔNG có no_ads
cơ hội mất vì No Ads  = count(show_skipped where skip_reason  = 'no_ads')
```

Không tách `no_ads` ra thì tỉ lệ chặn của cả game nhảy lên ngay khi bản mới ra, vì người mua giờ đóng góp toàn dòng bị chặn.
Đây là thay đổi về độ phủ, không phải luật của game chặt hơn.

**Khối lượng dòng.** Người mua sinh một dòng cho mỗi cơ hội chiếu (mỗi hết màn, mỗi lần quay lại app…), thay vì không có dòng
nào. Bên mình thấy chấp nhận được, vì event này nhỏ (vài field ngắn). Nếu các bạn thấy nặng, phương án dự phòng là SDK gộp thành
bản tin đếm theo phiên. Nhưng như thế mất chiều điểm chiếu, nên bên mình không đề xuất.

## 2. Sáu field mới: ad đang nằm sẵn lúc bị chặn

Tên và đơn vị lấy y nguyên từ `f_sdk_ad_show_attempt` và `f_sdk_ad_load`, để so trực tiếp được.

| Key | Kiểu | Có mặt khi | Ai điền | Mô tả |
|---|---|---|---|---|
| `adAvailable` | bool | luôn có (từ bản mới) | mediation | Lúc bị chặn, kho của mediation có ad sẵn để chiếu không. **Cùng nghĩa** với `show_attempt.adAvailable` |
| `adViewId` | string | `adAvailable = true` | SDK tự đọc | Lần xem ad đang chờ, như `show_attempt.adViewId`. Ghép được với dòng request / load / impression cùng id |
| `adMediation` | string | khi biết | mediation | Cùng giá trị với `adMediation` của các dòng ad khác |
| `adNetwork` | string | `adAvailable = true` và mediation biết | mediation | Network đã trả ad đang chờ (applovin · unity · admob…) |
| `adUnitId` | string | `adAvailable = true` và mediation biết | mediation | Ad unit đang giữ ad. MultiCall: unit **sẽ được chọn** nếu chiếu ngay lúc đó |
| `estimatedEcpm` | double, USD / 1000 impression | `adAvailable = true` và mediation biết | mediation | Giá dự kiến **lúc load** của ad đang chờ. Cùng đơn vị với `f_sdk_ad_load.estimated_ecpm` |

Luật "vắng ≠ 0" giữ như mọi nơi: field nào mediation không biết thì **vắng**, không gửi 0 hay chuỗi rỗng.

**Năm điều phải đọc đúng:**

1. **`estimatedEcpm` là giá dự kiến, không phải doanh thu thật.** Doanh thu thật chỉ có ở dòng impression khi ad được chiếu, và
   có thể lệch giá lúc load. Ước lượng "tiền mất" = Σ `estimatedEcpm` / 1000 trên các dòng bị chặn. Đó là ước lượng, nên ghi
   rõ khi đưa lên báo cáo.
2. **Không phải mạng nào cũng có giá trước khi chiếu.** MAX có (giá có ngay lúc load). AdMob trực tiếp **không** có: doanh thu
   chỉ đến khi đã chiếu ⇒ `estimatedEcpm` vắng, chỉ có `adNetwork`. IronSource tuỳ bản SDK. Dòng vắng giá thì kho tự lấp bằng
   eCPM trung bình của network / format / quốc gia, hoặc bỏ khỏi phép tính tiền và chỉ đếm số lần.
3. **Nhiều dòng có thể cùng một `adViewId`.** Bị chặn không làm tiêu ad: ad vẫn nằm chờ cho cơ hội sau. Năm lần chặn liên tiếp
   sẽ trỏ về cùng một ad, cùng một giá. Phép cộng theo **từng dòng** vẫn đúng ("mỗi cơ hội lẽ ra chiếu được một ad trị giá ≈
   X"). Chỉ đừng `count(distinct ad_view_id)` để đếm cơ hội, và đừng dedup theo `adViewId`.
4. **App open của user No Ads luôn `adAvailable = false`.** Mediation không load app open cho người đã có quyền. Ở đây `false`
   nghĩa là "không load vì No Ads", **không** phải "hụt fill". Khi tính fill rate, loại các dòng `skip_reason = 'no_ads'` của
   `AppOpen` ra.
5. **Dòng của bản 1.7.4 không có các field này.** `adAvailable` vắng = dòng cũ (không biết), không phải `false`. Cắt theo
   `sdk_version`; số bản cụ thể bên mình gửi trong thư "bản + ngày" khi publish.

## Giới hạn phải nói trước

- **Ai chặn thì người đó ghi.** Mediation ghi khi chính nó chặn (luật MediationMO, cờ No Ads trong mediation). Game nào tự kiểm
  No Ads **trước** khi gọi mediation thì game phải tự gọi `OnShowSkipped(…, NoAds)`, nếu không thì cơ hội đó vô hình. Bên mình
  sẽ ghi rõ trong hướng dẫn tích hợp.
- **Dòng `no_ads` chỉ có từ khi game lên bản mediation đã sửa.** Trước đó không có dòng **không** có nghĩa là không ai mua.
  Đọc nhãn `no_ads` (APS 1.1.9) để biết ai có quyền.
- Ad đang chờ có thể hết hạn rồi được load lại với giá khác. Dòng nào thì mang giá của ad đang chờ **đúng lúc đó**.

## Câu hỏi cho các bạn

1. Ký từ `no_ads` và việc gửi dòng cho user No Ads (lật câu 2 thư 12/11)?
2. Ký 6 field mới, tên và đơn vị như bảng mục 2?
3. Khối lượng dòng của người mua có ổn không, hay các bạn muốn bản đếm theo phiên?
4. Gộp luôn câu `level_cap` đang chờ trong thư `Request-Loader-Publish-1.7.4.md`? Ký cả hai thì SDK ra một bản.

Ký xong bên mình làm theo thứ tự: BigData thêm từ và field (kèm test) → sửa ticket Mediation (`Request-Mediation-ShowGate.md`,
đã gửi nhưng bên đó chưa chắc đã làm) → gửi **bản + ngày** khi publish.

---

## Kho trả lời 13/11 (`loader-v3-work/ticket9/Reply-Loader-AdShowSkipped-NoAds.md`) — ĐÃ KÝ cả 4 câu + `level_cap`, đã code

- ✅ `no_ads` (đứng đầu thứ tự xét, khác `segment_rule`) và gửi dòng cho user No Ads.
- ✅ 6 field nguyên tên / kiểu / đơn vị / luật vắng ≠ 0. Phía kho: `event_extra_props` snake_case (`ad_available`, `ad_view_id`,
  `ad_mediation`, `ad_network`, `ad_unit_id`, `estimated_ecpm`). Dòng skip **không** vào hộp lượt xem QC dù mang `ad_view_id`.
  Dòng vắng giá: tạm bỏ khỏi phép tính tiền, chỉ đếm số lần.
- ✅ Khối lượng dòng ổn, không cần bản đếm theo phiên.
- ✅ `level_cap` = trần "tối đa N lần trong một level".

**Code SDK** (BigData **1.7.5**; 627/627 test):
- `AdSkipReason.NoAds` / `LevelCap` (đặt cuối enum, không xê dịch số cũ).
- `Ad.OnShowSkipped(…, adWhen, bool? adAvailable, AdReadyInfo readyAd)` — hai tham số mới tuỳ chọn, code gọi kiểu 1.7.4 vẫn biên dịch;
  thiếu `adAvailable` ⇒ SDK cảnh báo, field vắng.
- **Ai điền:** `adAvailable` + `readyAd` (network / unit / giá) do **mediation** trả — chỉ mediation biết chắc ad nào sẽ chiếu
  (MultiCall chọn đắt nhất giữa nhiều unit + AdMob). SDK tự điền `adViewId` (cache) và `adMediation` khi caller không truyền (view đang
  mở). Meta ad chỉ gửi khi `adAvailable = true`; eCPM không dương / không hữu hạn ⇒ vắng.
- Ticket Mediation `Request-Mediation-ShowGate.md` sửa bản 2: nhánh `no_ads` cho interstitial + app open MAX, hàm "ad sẽ chiếu",
  `LevelLimit` → `level_cap`.

**Còn nợ kho:** bản + ngày khi publish; báo game đầu tiên ship AnalyticParamsSync 1.1.9 (kho bật luật nhãn 13/11, dòng tới trước đó
cần bù từ silver).

