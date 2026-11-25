# SDK BigData → loader: log "luật chặn quảng cáo", nhãn `no_ads`, nghi án app_open hot do quảng cáo, và bảng kê nhãn (12/11/2026)

> Gửi: loader. Từ: owner SDK BigData.
> Có bốn mục. **Chưa code, chờ các bạn ký.**
> - Mục 1 là event mới.
> - Mục 2 chỉ là báo trước một tên nhãn, kho không phải làm gì.
> - Mục 3 là một nghi án phía SDK. Bên mình nhờ các bạn đo giúp trước khi sửa.
> - Mục 4 là bảng kê mọi bản tin nhãn và mọi tên nhãn SDK đang gửi, nhờ duyệt một lượt.

## Tóm tắt

| # | Nội dung | Kho cần làm |
|---|---|---|
| 1 | Event mới `f_sdk_ad_show_skipped`: game có cơ hội chiếu interstitial / app open nhưng **luật của game chặn**, kèm lý do | Đăng ký event id, ký bộ từ `skipReason` |
| 2 | Nhãn người chơi đăng ký sẵn `no_ads` (đã có quyền bỏ quảng cáo) | Không có gì. Nhãn tự do đã chạy qua `f_sdk_user_label` |
| 3 | Trên Android, mỗi quảng cáo toàn màn từ 30 giây trở lên có lẽ sinh một dòng `app_open` hot giả | Đo giúp một câu SQL. Nếu đúng thì SDK sửa |
| 4 | Bảng kê nhãn: 6 bản tin nhãn, 2 bó nhãn đi ké, 7 tên nhãn đăng ký sẵn | Duyệt, xác nhận mapping đã mở hết |

---

## 1. `f_sdk_ad_show_skipped` — luật của game chặn quảng cáo

**Vì sao cần.** Game chia user thành nhóm để bớt hoặc thêm quảng cáo. Các kiểu hay gặp:
- user mới được miễn interstitial vài ngày đầu;
- vừa mua thì được miễn một khoảng;
- user đến từ campaign tối ưu IAP thì bớt interstitial;
- cooldown, trần theo ngày hoặc theo phiên.

Những luật này chạy **trong code game, trước khi gọi mediation**. Vì vậy hôm nay chúng không để lại dòng nào:
- `f_sdk_ad_show_attempt` chỉ bắn khi game **đã** gọi show;
- ca "đến điểm chiếu nhưng luật bảo đừng" thì vô hình.

Hệ quả là kho không trả lời được "nhóm X bị bớt bao nhiêu interstitial, vì luật nào".

**Vì sao là event mới, không thêm field vào `f_sdk_ad_show_attempt`.** Event đó đã có công thức `view_fill_rate = count(adAvailable = true) / count(*)` và bất biến "số dòng ≥ số `ad_show_data`". Nhét dòng bị luật chặn vào thì `count(*)` đổi nghĩa. Đổi hình dạng thì đổi tên, đúng luật nhà.

**Đơn vị: một dòng = một CƠ HỘI chiếu bị chặn.** Cơ hội là điểm trong luồng game mà interstitial / app open có thể hiện: hết màn, về Home, quay lại app. Game gọi **đúng một lần tại điểm đó**, không gọi mỗi lần kiểm tra luật. Ghép với show_attempt sẽ ra cơ hội đầy đủ:

```
cơ hội     = count(show_attempt) + count(show_skipped)          per type / adWhere
tỉ lệ chặn = count(show_skipped) / cơ hội                      per skipReason, per nhãn user
```

Chỉ áp cho format **game tự bật**: `Interstitial` và `AppOpen`. Rewarded do người chơi bấm, nên "không có ad" của nó đã nằm ở `show_attempt.adAvailable = false`. Banner không có điểm chiếu.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `type` | enum string | ✓ | `Interstitial` · `AppOpen` |
| `adWhere` | string | ✓ | Điểm chiếu bị chặn, cùng bộ từ với `adWhere` của `show_attempt` để ghép được |
| `adWhen` | string | — | Ngữ cảnh kích hoạt, như các mốc ad khác |
| `skipReason` | enum string | ✓ | Luật **đầu tiên** chặn, theo thứ tự game xét. Mỗi dòng một lý do, không mảng |

**Bộ từ `skipReason` (đóng, có `other`):**

| Giá trị | Nghĩa |
|---|---|
| `cooldown` | Chưa đủ thời gian kể từ lần chiếu trước |
| `level_interval` | Chưa đủ số màn kể từ lần chiếu trước |
| `level_gate` | Chưa tới level mở quảng cáo |
| `new_user_grace` | Đang trong giai đoạn bảo vệ user mới (theo giờ / ngày cài, thời gian chơi, số phiên) |
| `post_purchase_grace` | Vừa mua, đang được miễn |
| `post_ad_grace` | Vừa xem quảng cáo khác (vd vừa xem rewarded), đang được miễn |
| `daily_cap` | Đã chạm trần trong ngày |
| `session_cap` | Đã chạm trần trong phiên |
| `segment_rule` | Luật theo nhóm user chặn hẳn (campaign, đã trả tiền…). Nhóm nào thì đọc ở nhãn người chơi |
| `disabled` | Config tắt quảng cáo ở điểm này |
| `other` | Không thuộc các mục trên. SDK cảnh báo dev, giá trị vẫn gửi |

Không có field số đi theo từng lý do (vd "còn bao nhiêu giây cooldown"). Một field chỉ có nghĩa khi `skipReason = cooldown` là field bị field khác điều khiển nghĩa.

**User đã có quyền No Ads: game KHÔNG gọi.** Mọi cơ hội của họ đều bị chặn, nên gửi chỉ tốn dòng mà không thêm thông tin. Trạng thái đó đọc ở nhãn `no_ads` (mục 2).

**Giới hạn phải nói trước:**
- Chỉ đo được game nào gọi API này, và gọi ở điểm chiếu nào. Game quên gọi thì tỉ lệ chặn thấp giả.
- Mediation cũng có chỗ tự chặn mà không báo, ví dụ cooldown của app open trong mediation. Chỗ đó cần ticket riêng cho module mediation, không chặn thư này.

## 2. Nhãn người chơi `no_ads` — báo trước, kho không phải làm gì

SDK thêm tên đăng ký sẵn `FUserLabelKey.NO_ADS = "no_ads"`, đi qua `f_sdk_user_label` như `elo`. Mục đích là mọi game gọi cùng một tên, để kho so được chéo game.

- **Giá trị:** `true` khi user đang có quyền bỏ quảng cáo (mua gói hoặc thuê bao còn hạn). Gửi `null` để **gỡ** khi mất quyền (thuê bao hết hạn, hoàn tiền).
- **Ai gọi:** game, ở chỗ game cấp hoặc thu quyền. BigData không tự đọc được cờ của mediation, vì mediation phụ thuộc BigData chứ không có chiều ngược lại.
- **Ngày hết hạn thuê bao** không gộp vào nhãn này. Gộp thì một giá trị lúc là bool, lúc là mốc thời gian. Nếu cần thì sẽ là một tên riêng, thư khác.

Hôm nay kho chỉ đoán được quyền No Ads qua `productId` trong lịch sử mua, và không biết lúc thuê bao hết hạn.

## 3. Nghi án: quảng cáo toàn màn trên Android sinh `app_open` hot giả

**Code SDK:**
- SDK coi app "xuống nền" khi Unity nhận `OnApplicationPause(true)` hoặc `OnApplicationFocus(false)`.
- Trên Android, quảng cáo toàn màn mở activity riêng đè lên Unity, nên chính việc chiếu quảng cáo đã là một lần pause. Code SDK đã ghi nhận điều này ở chỗ khác (cụm banner chốt lúc pause).
- Khi quảng cáo đóng, app "quay lại". Nếu quảng cáo dài từ `fAppOpenMinBackgroundSec` (mặc định 30 giây) trở lên, SDK bắn `app_open` hot với `backgroundDurationSec` ≈ thời lượng quảng cáo. Rewarded thường dài hơn 30 giây.
- Trên iOS, quảng cáo hiện trong cùng app, Unity thường không mất focus. Điều này cần kiểm trên máy, bên mình chưa thử.

Nếu đúng, thì trên Android:
- số lần mở hot bị phồng;
- *giây nằm nền mỗi phiên* bị cộng cả thời gian xem quảng cáo, nếu kho tính từ `backgroundDurationSec`.

**Nhờ đo:** `app_open` hot trên Android có bao nhiêu phần có một dòng `ad_close` (Interstitial / Reward / AppOpen) cùng `session_uid`, cách nhau vài giây? So với iOS.

```sql
-- phác thảo, các bạn chỉnh theo bảng thật
select platform,
       countIf(c.ad_view_id is not null) / count() as ty_le_hot_ngay_sau_ad
from app_open o
left join ad_close c
  on c.session_uid = o.session_uid
 and abs(c.event_ts - o.event_ts) <= 5000          -- ms
where o.launch_type = 'hot' and o.created_day = <ngày>
group by 1
```

**Nếu xác nhận, SDK đề xuất:** không bắn `app_open` hot khi lần pause bắt đầu lúc đang có quảng cáo toàn màn trên màn hình (giữa `OnShown` và `OnClosed`).
- Hệ quả là ca người chơi bấm Home *trong lúc* xem quảng cáo trên Android cũng bị bỏ. Từ Unity không tách được hai ca này.
- Thay đổi này cần mốc version, và có thư riêng sau khi có số.

## 4. Bảng kê nhãn hiện có — nhờ duyệt một lượt

Nhãn đi theo hai đường. Mục này kê cả hai, theo code SDK hiện tại (bản 1.7.3).
- **Đường LẦN:** gán nhãn cho một thứ đang mở (người chơi, phiên, lượt chơi…). Mỗi lần gán là một bản tin riêng.
- **Đường VẬT:** gán nhãn cho một bản thiết kế (màn, offer). Không có bản tin riêng; bó nhãn đi ké event của chính thứ đó.

### 4a. Sáu bản tin nhãn (đường LẦN)

Field chung:
- `labelKey`: tên nhãn, tự do.
- `labelValue`: JSON giữ nguyên kiểu. `null` = gỡ nhãn, nên key này luôn có mặt.
- `adViewId` / `offerImpressionId` / `purchaseAttemptId`: mỗi cái chỉ có ở bản tin nhãn của đúng loại đó.

| Bản tin | Gán cho | API cho game | Hiệu lực | Tên SDK tự bắn |
|---|---|---|---|---|
| `f_sdk_user_label` | Người chơi | `Label.User(key, value)` | Từ mốc gán trở đi, forward-fill lên event sau | `elo` (qua `Level.SetUserElo`). Sắp thêm `no_ads` (mục 2) |
| `f_sdk_session_label` | Phiên đang mở | `Label.Session(key, value)` | Trọn phiên | — |
| `f_sdk_turn_label` | Lượt chơi level đang mở | `Label.LevelPlayTurn(key, value)` | Trọn lượt; không có lượt mở thì SDK cảnh báo và bỏ | — |
| `f_sdk_ad_view_label` | Lần xem ad đang mở | `Label.AdView(type, key, value)` | Trọn lần xem | `load_error`, `show_error`: mã lỗi của mediation khi load / show hỏng |
| `f_sdk_iap_offer_label` | Lần hiện offer | `Label.IapOfferImpression(key, value)` | Trọn lần hiện | `clicked` = `true`: người chơi bấm sau khi bản tin impression đã chốt lúc app pause |
| `f_sdk_iap_purchase_label` | Lượt mua đang mở | `Label.IapPurchase(key, value)` | Trọn lượt mua | — |

### 4b. Hai bó nhãn đi ké (đường VẬT)

| Field | Đi trên | API cho game | Tên đăng ký sẵn |
|---|---|---|---|
| `levelLabels` | Mọi `f_sdk_level_*` của màn đó | `Label.Level(levelId, key, value)` | `moves_limit`, `time_limit_sec`: SDK tự đặt khi game truyền `Level.OnStart(movesLimit:, timeLimitSec:)` |
| `offerLabels` | `f_sdk_iap_offer_data` của offer đó | `Label.IapOffer(offerId, key, value)` | — |

### 4c. Bảy tên đăng ký sẵn trong SDK

Game gõ tên khác thì mất khả năng so chéo game.

| Tên | Đường | Giá trị | Hằng số trong SDK |
|---|---|---|---|
| `elo` | `f_sdk_user_label` | số nguyên; `null` = gỡ | `FUserLabelKey.ELO` |
| `no_ads` *(mới, mục 2)* | `f_sdk_user_label` | `true`; `null` = gỡ | `FUserLabelKey.NO_ADS` |
| `load_error` | `f_sdk_ad_view_label` | mã lỗi của mediation | `FAdViewLabelKey.LOAD_ERROR` |
| `show_error` | `f_sdk_ad_view_label` | mã lỗi của mediation | `FAdViewLabelKey.SHOW_ERROR` |
| `clicked` | `f_sdk_iap_offer_label` | `true` | chữ viết thẳng trong code; game không gọi được |
| `moves_limit` | `levelLabels` | số nguyên | `FLevelLabelKey.MOVES_LIMIT` |
| `time_limit_sec` | `levelLabels` | số nguyên, giây | `FLevelLabelKey.TIME_LIMIT_SEC` |

### 4d. Van giới hạn

SDK chặn sớm ở client và cảnh báo dev, cùng ngưỡng với van của kho:
- tối đa **20** tên nhãn người chơi trên một user;
- chuỗi giá trị dài tối đa **64** ký tự;
- bó nhãn đi ké tối đa **256 byte** sau serialize. Vượt thì SDK giữ bó cũ.

### 4e. Chỗ bên mình sẽ dọn (kho không phải làm gì)

- Comment trong code SDK còn ghi `f_sdk_ad_view_label` "chờ loader mở mapping" và `offerLabels` "ngoài hợp đồng". Catalog của các bạn đã có cả hai, nên bên mình sẽ sửa comment, sau khi các bạn xác nhận ở câu hỏi 4.
- Spec phía SDK còn mục cũ `f_sdk_label_assign_data` / `labelScope`, thứ không tồn tại trong code. Mục `levelLabels` cũng còn câu "đừng gửi moves / time qua nhãn", mâu thuẫn với `moves_limit` / `time_limit_sec`. Đây là S13 và S20 trong inventory của các bạn. Bên mình sẽ dọn cả hai.

---

## Câu hỏi cho các bạn

1. Mục 1: ký tên `f_sdk_ad_show_skipped`, bốn field và bộ từ `skipReason`? Muốn thêm hay bớt giá trị nào?
2. Mục 1: đồng ý không gửi dòng cho user có No Ads không?
3. Mục 3: đo giúp câu trên. Và cột "giây nằm nền" của dòng phiên có lấy từ `backgroundDurationSec` không?
4. Mục 4: duyệt bảng kê. Cả 6 bản tin nhãn và 2 bó nhãn đi ké đã có mapping chạy thật chưa? Có tên nào trong 4c các bạn muốn đổi không?

Ký xong bên mình code mục 1 và 2, kèm test, rồi gửi **bản + ngày** khi publish.

---

## Kho trả lời 12/11 (`loader-v3-work/ticket9/Reply-Loader-AdShowSkipped.md`) — ĐÃ KÝ mục 1–3, đã code

- ✅ **Mục 1** ký nguyên: tên, 4 field, bộ từ `skipReason`, "một lý do, lý do đầu tiên". Phía kho: `sub_event` = `type`,
  `event_where` / `event_when` = `adWhere` / `adWhen`, `sub_event_2` = `skipReason`. Đồng ý không gửi dòng cho user No Ads.
- ✅ **Mục 2** ký tên `no_ads` và luật `true` / `null`. **Đính chính:** "kho không phải làm gì" là sai — kho **chưa có đường
  nào** cho nhãn người chơi (không mapping, không luật profile, không forward-fill; chưa game nào gọi, kể cả `elo`). Kho dựng
  khi game đầu tiên gọi `Label.User` ⇒ **bên mình phải báo**.
- ⚠ **Mục 3 xác nhận, và iOS cũng bị:** hot nằm sát một lần đóng ad chiếm phần lớn, ở cả Android lẫn iOS;
  `backgroundDurationSec` ≈ thời lượng ad. `background_sec` của phiên = Σ `backgroundDurationSec` của hot, `resume_count` = số hot ⇒ cả hai bị phồng.
  Kho đồng ý đề xuất, kể cả cái giá mất ca bấm Home giữa lúc xem ad. Cần **thư mốc version** để ghi era.
- ❌ **Mục 4:** chỉ `f_sdk_ad_view_label` có mapping chạy. `f_sdk_iap_offer_label` vào silver nhưng chưa có luật. User / session
  / turn / purchase label chưa mapping. `levelLabels` kho đọc; `offerLabels` chưa. Kho **không có** van nhãn nào (20 / 64 / 256
  chỉ ở SDK). Không đổi tên nào trong 7 tên đăng ký sẵn.

**Code SDK** (BigData **1.7.4** (AnalyticParamsSync **1.1.9** cho `no_ads`), +26 test, 615/615):
- Mục 1: `Ad.OnShowSkipped(AdType, adWhere, AdSkipReason, adWhen)`.
- Mục 2: `FUserLabelKey.NO_ADS`.
- Mục 3: pause bắt đầu trong **10 giây sau `OnShown`** của một ad toàn màn chưa đóng ⇒ không gửi hot. Chặt hơn bản đã ký
  ("giữa OnShown và OnClosed") ở hai chỗ, cả hai về phía an toàn: mediation quên `OnClosed` thì cờ không kẹt; bấm Home giữa lúc
  xem trên iOS vẫn ra hot thật. Báo kho cùng thư mốc version.
  ⚠ Chỉ phủ đường mediation có gọi `Ad.OnShown` quanh lệnh show: MAX (gọi thẳng, MultiCall, FSN) và AdMob bridge. Đường
  GMA app open và IronSource **không** gọi `Ad.OnShown` ⇒ vẫn ra hot giả. Thư mốc version phải nói rõ điều này và đề nghị kho
  **giữ phép lọc phía server** (bỏ hot trong 5 giây sau `ad_close` cùng phiên) cho các đường đó.
- Mục 4: spec và comment code sửa theo trạng thái thật phía kho.

**Còn nợ kho:**
1. Bản + ngày khi publish (mục 1, 2, và thư mốc version cho mục 3).
2. Bản + ngày của `f_sdk_level_action_revive` / `f_sdk_level_action_booster` (1.7.1) — kho đã thấy dòng thật và đang chờ.
3. Báo khi game đầu tiên gọi `Label.User`.
