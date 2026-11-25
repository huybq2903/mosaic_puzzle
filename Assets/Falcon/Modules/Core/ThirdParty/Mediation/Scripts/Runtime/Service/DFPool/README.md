# DFPool — kho AdMob dùng chung cho các slot MAX

Chạy song song với MultiCall. **Android only.** Bật/tắt bằng remote config, xoá cả thư mục cũng
không ảnh hưởng dự án.

## Luồng

```
Bước 0  defaultAdId (nếu có): luôn giữ sẵn 1 ad AdMob xin KHÔNG giá sàn trong KHO, không chờ MAX
Bước 1  MultiCall load MAX như bình thường ──► ad MAX load xong, revenue R
Bước 2  với mỗi target trong config: AdMob(adUnitId) ở giá sàn R × multiplier   (n request)
        ad AdMob về ──► vào KHO (kèm giá lúc load + hạn dùng)

          KHO AdMob (sắp theo giá, tối đa poolCap / format)
          ┌──────────────────────────────────────┐
          │ ad#1 $14.2 │ ad#2 $11.4 │ ad#3 $9.1 │ ...
          └──────────────────────────────────────┘
                 │ mượn             │ mượn
                 ▼                  ▼
          Slot MAX-A           Slot MAX-B
Bước 3  slot trống mượn ad đắt nhất chưa ai mượn ──► MaxSdk.LoadX(slot)
        waterfall DFPoolNetwork nhận tier đầu tiên ≤ giá ad (chỉ đọc GIÁ, không đụng ad)
```

| Thao tác | Hành vi |
|---|---|
| Nhiều ad về gần như đồng thời | gom hết event trong frame, quyết định **một lần ở cuối frame** trên toàn kho → ad đắt nhất cả đợt được gắn trước, không phải ad về đầu tiên |
| Ad mới đắt hơn ad của slot **đang load** (waterfall chưa nhận tier) | đổi thẳng ad mượn ở native, **không cần reset** — tier kế tiếp so với giá ad mới |
| Ad mới đắt hơn ad của slot **Ready** (≥ `upgradeMinGain`) | slot rẻ nhất trả ad cũ về kho (**không xoá**), show giả để MAX nhả slot, rồi mượn ad mới. **Luôn chừa ít nhất 1 slot show được** — không bao giờ để cả 2 slot cùng reset |
| Ad về trong lúc slot **đang chờ reset** | nằm trong kho; slot nhả xong sẽ mượn ad đắt nhất có mặt **lúc nhả xong**, nên ad mới nhất không bị bỏ qua |
| Show | So giá slot đắt nhất (qua `SecondaryAdSource`) với ad bên chính rồi chiếu bên đắt nhất (hoà → bên chính): **MultiCall** (ad MAX bước 1 + tầng AdMob meta), **unit đơn** (ad MAX của id đơn), **Lua** (ad đắt nhất của script). Rewarded so 4 ứng viên như MultiCall (inter chỉ chiếu thay khi đắt hơn hẳn). Show xong slot trống, tự mượn ad tốt nhất tiếp theo |
| Phiên Lua | mốc giá bước 2 lấy từ ad MAX do Lua load (`ExternalMaxAdUnits.MarkPriceAnchor`); id `mediation = admob` của Lua ở format DFPool đã nhận **không được gửi** (tránh request AdMob gấp đôi) |
| Hết hạn | ad quá `ttlMinutes` bị loại khỏi kho; slot đang mượn ad đó bị flush |
| Kho đầy | bỏ ad rẻ nhất đang rảnh; request mới có giá sàn ≤ ad rẻ nhất thì không gửi |
| Slot bị network khác của MAX fill (vd AppLovin Exchange) | giữ ad đó tới lúc show (không flush được), revenue lấy từ MAX |

## Cài trên MAX dashboard

1. **MAX > Mediation > Manage > Networks > Add Custom Network**
   - Network Type: `SDK`
   - Name: `DFPoolNetwork`
   - Android Adapter Class Name: **`com.falcon.dfpool.DFPoolAdapter`**
2. Tạo **4 ad unit** Android: 2 interstitial + 2 rewarded (đây là các "slot").
3. Waterfall mỗi slot: **chỉ** gồm các tier `DFPoolNetwork`, giá từ cao xuống thấp. Mỗi tier khai
   **Custom Parameters** `{"floor": "12.5"}` (eCPM USD) — hoặc để số trong Placement ID.
   Tier thấp nhất thêm `{"floor": "0.01", "is_bottom": "true"}` để luôn nhận được.
   Tắt các network/bidder khác trên 4 ad unit này nếu dashboard cho phép.

## Remote config

Key **`moMulDfPoolConfig`** — CMS trước (CMS đã khai key, kể cả rỗng, thì CMS thắng), CMS không
khai mới đọc data4game. **Để rỗng = TẮT.**

```json
{
  "interstitial": {
    "maxSlotIds": ["<max_inter_slot_A>", "<max_inter_slot_B>"],
    "defaultAdId": "ca-app-pub-xxx/000",
    "targets": [
      { "multiplier": 1.5, "adUnitId": "ca-app-pub-xxx/111" },
      { "multiplier": 1.2, "adUnitId": "ca-app-pub-xxx/222" },
      { "multiplier": 1.0, "adUnitId": "ca-app-pub-xxx/333" }
    ]
  },
  "rewarded": {
    "maxSlotIds": ["<max_rewarded_slot_A>", "<max_rewarded_slot_B>"],
    "defaultAdId": "ca-app-pub-xxx/999",
    "targets": [
      { "multiplier": 1.5, "adUnitId": "ca-app-pub-xxx/444" },
      { "multiplier": 1.0, "adUnitId": "ca-app-pub-xxx/555" }
    ]
  },
  "ttlMinutes": 55,
  "minBindSeconds": 120,
  "poolCap": 8,
  "upgradeMinGain": 0.05,
  "adValueInMicros": false
}
```

| Field | Mặc định | Ý nghĩa |
|---|---|---|
| `maxSlotIds` | — | ad unit MAX slot. Dùng 2 id để không có khoảng trống "chưa sẵn sàng" lúc một slot đang flush/load |
| `defaultAdId` | rỗng | ad unit AdMob xin **không giá sàn** (thay bước B của tầng Meta AdMob). Luôn giữ sẵn 1 ad trong kho: load ngay khi bật, load lại khi ad rời kho (show / hết hạn / bị waterfall từ chối); fail thì thử lại sau 5s, 10s… tối đa 120s. Không bị đẩy ra khi kho đầy. Đừng dùng trùng id với `targets` |
| `targets[].multiplier` | 1 | giá sàn AdMob = revenue MAX bước 1 × hệ số (gửi qua extra `rusd`) |
| `ttlMinutes` | 55 | tuổi thọ ad AdMob trong kho (AdMob cho ~60 phút) |
| `minBindSeconds` | 120 | ad còn sống ít hơn mốc này thì không đem gắn slot nữa |
| `poolCap` | 8 | số ad tối đa trong kho mỗi format |
| `upgradeMinGain` | 0.05 | chỉ đổi ad trong slot khi ad mới đắt hơn ≥ 5% (mỗi lần đổi tốn 1 show giả + 1 request MAX) |
| `adValueInMicros` | false | bật nếu tài khoản AdMob trả `ad_value` theo micros |
| `customNetworkName` | `DFPoolNetwork` | tên Custom Network trên MAX dashboard (**phải khớp**). Chỉ ad có `adInfo.NetworkName` đúng tên này mới được reset bằng show giả; ad AppLovin / network khác không bao giờ bị reset. Để rỗng = chỉ loại network có chữ "applovin" |

Bỏ một format (hoặc để `maxSlotIds` rỗng, hoặc cả `defaultAdId` lẫn `targets` rỗng) = tắt riêng
format đó. Chỉ có `defaultAdId` mà không có `targets` vẫn chạy (chỉ có ad không giá sàn).

**DFPool và Meta AdMob không chạy chồng nhau (tự động):** format nào DFPool BẬT thì tầng Meta AdMob
**bỏ** các id `mediation = admob` của `sc_ad_bid_floor` cho format đó — AdMob chỉ được xin bằng id
trong `moMulDfPoolConfig`, không bị request gấp đôi. Format DFPool không chạy (không khai, config hỏng,
thiếu Java) thì Meta AdMob vẫn dùng id `mediation = admob` như cũ. Meta AdMob chờ DFPool quyết định
xong mới init (qua `AdmobTakeover`, `Service/MultiCall/Interface/`); DFPool bật muộn giữa phiên thì
Meta AdMob đọc lại id và dừng format bị nhận. Lúc đó mọi ad AdMob của format đó đều show qua slot MAX
→ MAX ghi nhận impression.
Bản app cũ (mediation ≤ 1.4.34) đọc Meta AdMob từ key `moMulAdmobMetaConfig`, không đọc
`moMulDfPoolConfig` — **đừng** làm rỗng `moMulAdmobMetaConfig` khi bật DFPool, bản cũ sẽ mất AdMob.
Config đọc **một lần** khi MAX init xong; đổi giữa session thì áp dụng từ lần mở app sau
(trừ trường hợp lần đầu đọc ra rỗng rồi config mới về — lúc đó tự bật luôn).

## Gỡ / mang sang dự án khác

- **Tắt:** để `moMulDfPoolConfig` rỗng.
- **Gỡ:** xoá cả thư mục `DFPool/`. Không file nào ngoài thư mục gọi tới nó.
- **Mang sang dự án khác:** copy thư mục `DFPool/`, rồi cấu hình remote config. Cần:
  - module `falcon.modules.core.thirdparty.mediation` **≥ 1.4.39** (có `ExternalMaxAdUnits` và
    `SecondaryAdSource.Register`),
  - MAX SDK + Google Mobile Ads (`play-services-ads`) trong gradle,
  - show đi qua `MediationManager` / `FalconMaxService` — MultiCall, unit đơn và Lua đều so giá với
    `SecondaryAdSource` (unit đơn + Lua từ bản có `FalconMaxService.ShowSecondaryAd`),
  - nếu bật minify (R8), thêm vào `proguard-user.txt`:
    ```
    -keep class com.falcon.dfpool.** { *; }
    ```

## Cùng bộ mốc với meta AdMob

DFPool và meta AdMob là hai nhánh của cùng AdMob (show thẳng / show qua slot MAX) nên bắn cùng một bộ:

| Mốc | Tầng nguồn (DFPool tự bắn, giống meta AdMob) | Tầng phiên (MultiCall / unit đơn / Lua, chung cho mọi nguồn phụ) |
|---|---|---|
| Request | BigData request (van `AdViewRequestGate`), extras `auction_context` / `ua_channel` / `days_since_install` / `rusd` | — |
| Load OK / fail | BigData load result, `RecordAuctionEvent` (hob) | — |
| Ready | `IsReady` / `BestRevenue` → so giá | `IsInterstitialReady` / `IsRewardedVideoReady` / `ReadyInterstitialInfo` |
| Show | BigData shown | `CSAdStart`, MMP `af_inters_show` / `af_rewarded_show` (một lần / lượt, như ad MAX), GameEvent displayed |
| Displayed | MMP `af_inters_displayed` / `af_rewarded_displayed` (kèm `af_level`), FSN | `start_`, Firebase show, đếm inter / rewarded, FloorPersonalization |
| Impression | Firebase / MMP revenue / Bamboo / LTV / BigData, `RecordImpressionEvent` | `CSAdInfo` |
| Click / show fail | BigData | — |
| Closed | BigData closed | `CSAdFinish`, `finish_`, `AdsClosed`, GameEvent closed, Firebase `meta_mediation_admob_impression` (uplift) |
| MAX thắng | `RecordImpressionEvent` cho response AdMob thua | — |

## Đọc log

Mọi dòng có dạng `[DFPool][<mã giai đoạn>] [inter|rewarded] ...` (mã ở `DFPoolStage.cs`).
- Unity log (cần define `ENABLE_DEBUG_LOG`): lọc `[DFPool]`, hoặc lọc đúng một mã, vd `[B3-READY]`.
- Logcat (Development Build): `adb logcat -s DFPool` — cùng bộ mã, chi tiết phía native.
- `[POOL]`: ảnh chụp 2 slot + toàn bộ kho, in mỗi khi có thay đổi.

| Mã | Giai đoạn | Bình thường / bất thường |
|---|---|---|
| `B0-INIT` | khởi động, đọc config | phải thấy `BẬT (...)`; thấy `TẮT` = config rỗng/sai hoặc thiếu Java |
| `B1-MAX-REVENUE` | ad MAX bước 1 load xong | mốc giá cho bước 2 |
| `B2-ADMOB-REQ` / `-OK` / `-FAIL` | n request AdMob theo multiplier, ad vào kho | `-FAIL` là no fill, bình thường |
| `B2-DEFAULT-REQ` / `-OK` / `-FAIL` | ad mặc định (`defaultAdId`, không giá sàn) | `-OK` phải có giá > $0; bằng $0 = tài khoản không trả `ad_value` → ad chỉ khớp tier đáy |
| `B3-BIND` / `B3-REBIND` / `B3-READY` | slot mượn ad, waterfall nhận tier | luồng chuẩn |
| `B3-WATERFALL` (logcat) | adapter bỏ / nhận từng tier | `CLAIM` = tier được nhận |
| `B3-FOREIGN` | slot bị network khác (AppLovin…) fill | không lỗi, nhưng ad đó không reset được |
| `B3-LOAD-FAIL` | waterfall slot không nhận ad | kiểm tra tier đáy `is_bottom` |
| `B4-UPGRADE` / `B4-WAIT` / `B4-FLUSH` / `B4-FLUSH-DONE` | có ad tốt hơn → trả ad cũ, show giả, nhả slot | luồng chuẩn |
| `B4-FLUSH-RETRY` | MAX từ chối show giả | thỉnh thoảng thì OK, lặp mãi = lỗi |
| `B5-SHOW` / `-DISPLAYED` / `-PAID` / `-REWARD` / `-HIDDEN` | show | luồng chuẩn |
| `B5-SHOW-FAIL` | show lỗi | bất thường |
| `B6-EXPIRE` / `B6-EVICT` | hết hạn / kho đầy | bình thường |

## File

| File | Vai trò |
|---|---|
| `DFPoolMediation.cs` | tự khởi động, đọc config, nhận event MAX, cắm vào `SecondaryAdSource` |
| `DFPoolFormat.cs` | kho + slot + luật mượn / đổi / hết hạn / show của một format |
| `DFPoolNative.cs` | cầu JNI tới Java |
| `DFPoolReporter.cs` | analytics (Firebase / MMP / BigData / LTV) |
| `DFPoolConfig.cs` | model + đọc remote config |
| `Android/DFPoolBridge.java` | kho object AdMob + bảng slot ↔ ad |
| `Android/DFPoolAdapter.java` | custom adapter MAX |
| `Android/DFPoolListener.java` | callback native → Unity |
