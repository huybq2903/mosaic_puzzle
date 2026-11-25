# LuaDynamic — điều khiển load ad bằng Lua script từ server

Server gửi bản tin `sc_ad_monetize_lua_script` (`SCAdMonetizeLuaScript.luaScript`). Client chạy script đó bằng
MoonSharp (trình thông dịch Lua viết bằng C# thuần, chạy được trên IL2CPP, không cần thư viện native).
Unity và Lua nói chuyện với nhau qua biến toàn cục `context`.

Script điều khiển việc load interstitial / rewarded trên **bộ ad unit riêng**.

**Luồng phiên LUA:**

1. `sc_ad_bid_floor`: danh sách ad unit (`adUnitId`, `rewarded`, `multiplier`, `timeConfig`, **`adUnitIdType`**,
   `mediation`). Client tạo sẵn từng id trong `context.listInterstitialAd` / `listRewardedAd` (status `Idle`).
   - `adUnitIdType`: `"default_1"`, `"default_2"`, `"h1"`, `"h2"`, `"h3"`, ... do server gán.
   - `mediation`: `"max"` / `"admob"` (không phân biệt hoa thường). Bỏ trống hoặc giá trị khác thì client tự nhận: id bắt đầu bằng `ca-app-pub-` là AdMob.
2. `sc_ad_monetize_lua_script` (chỉ gửi một lần đầu game): `luaScript` + **`md5LuaScript`**. md5 trùng script
   đang chạy thì bỏ qua, không phải so cả chuỗi dài.
3. Init MAX → init AdMob **chỉ khi danh sách bid floor có id AdMob** (tuần tự, sau MAX).
4. Client báo Lua đúng **một** trong hai hàm:
   - có id AdMob → `OnInitAdmobSuccess()` khi AdMob init xong;
   - không có id AdMob → `OnInitMaxSuccess()` khi MAX init xong.
5. Script bắt đầu load các id default trong hàm đó. Từ đó mỗi kết quả load / show là một lượt Lua ra lệnh load tiếp.

## Chọn Lua hay MultiCall: chốt một lần cho cả phiên

| Server trả về lúc init MAX | Chế độ phiên |
|---|---|
| `sc_ad_monetize_lua_script` có script **hợp lệ** (khác rỗng, không lỗi cú pháp) | **LUA**. MultiCall không bật, id đơn của SOMediationSetting không load. Inter + rewarded chỉ load / show bằng Lua |
| script lỗi / không về kịp (7s), **có** script đã lưu | **LUA** bằng script đã lưu (xem "Cache script") |
| script rỗng, hoặc lỗi / không về kịp mà **không** có script đã lưu | **MULTICALL** theo `sc_ad_bid_floor`, như trước (không có bid floor thì unit đơn) |

- **Hàng rào init:** hàng rào init MAX trong `MediationManager.StartProcess` chờ **cả bản tin Lua lẫn
  `sc_ad_bid_floor`** (Lua lấy danh sách ad unit từ bid floor).
  - Vẫn timeout 7s. Hết giờ mà bid floor chưa về thì dùng danh sách cache của phiên trước.
  - ⚠ **Server phải luôn gửi `sc_ad_monetize_lua_script`, kể cả khi không dùng Lua (gửi `luaScript` rỗng).** Không gửi thì mọi user chờ đủ 7s mới init MAX.
- **Chốt chế độ:** chế độ được chốt ở `FalconMaxService.Init` (`LuaAdMonetize.LockMode`). Sau đó:
  - **Phiên MULTICALL:** script về muộn không được nạp, chỉ lưu (nếu hợp lệ) cho phiên sau.
  - **Phiên LUA:** script mới hợp lệ **thay ngay** script đang chạy (real-time), xem mục dưới. Script rỗng hoặc lỗi bị bỏ qua (giữ script đang chạy).

### Cache script

- **Lưu:** mỗi lần nạp script từ server **thành công** thì lưu `luaScript` + md5 xuống máy (`SaveLoadHandler`, key
  `FALCON_LUA_AD_SCRIPT` / `FALCON_LUA_AD_SCRIPT_MD5`). Server không gửi md5 thì client tự tính (md5 hex thường, UTF-8).
  Script lỗi thì không lưu, bản đã lưu vẫn giữ.
- **Mở app:** nạp ngay script đã lưu (lúc bootstrap), trước khi server trả lời. Log: `nạp script Lua ĐÃ LƯU md5=...`.
  - Server gửi md5 trùng → giữ nguyên. Server chỉ gửi md5 (script rỗng) mà trùng bản đã lưu → coi như không đổi.
  - md5 khác + script mới hợp lệ → thay (như "Cập nhật script real-time" bên dưới), rồi lưu đè.
  - Script mới lỗi → giữ bản đang chạy, không lưu.
  - Bản đã lưu bị lỗi (vd client đổi hợp đồng) → xoá cache.
- **Server tắt Lua** (`luaScript` rỗng, md5 không trùng bản đã lưu) → xoá cache, phiên sau quay về MultiCall. Phiên
  hiện tại giữ chế độ đã chốt, như trước.
- **Hàng rào init MAX:** vẫn chờ bản tin Lua + bid floor tới 7s. Hết giờ mà có script đã lưu thì chốt LUA bằng bản đó
  (log `CHỐT chế độ phiên: LUA ... server CHƯA trả lời bản tin Lua -> chạy bằng script ĐÃ LƯU`).
- **Phiên đã chốt MULTICALL** mà server gửi script hợp lệ → không nạp, chỉ chạy thử để kiểm lỗi rồi lưu, phiên sau
  chạy Lua.

### Cập nhật script real-time (phiên LUA)

Server gửi lại `sc_ad_monetize_lua_script` với `md5LuaScript` khác script đang chạy (không có md5 thì so cả nội
dung). md5 trùng → bỏ qua, kể cả khi `luaScript` rỗng:

1. **Biên dịch + chạy thân script mới.** Lỗi thì giữ nguyên script cũ, log `script #... lỗi, KHÔNG nạp`.
2. **Huỷ lệnh load đang chờ (Waiting) của script cũ.** Lệnh do thân script mới vừa xin được giữ lại.
3. **Bảng `context` mới thay bảng cũ.** Trạng thái thuật toán của script cũ bỏ hết, script mới bắt đầu trắng.
4. **Gọi lại `OnInitMaxSuccess()` / `OnInitAdmobSuccess()`** nếu mediation đã init từ trước, để script mới tự khởi động.
5. **Báo lại ad đang giữ.** Với mỗi unit đang giữ ad (`Ready`), C# gọi lại `On...LoadSuccess(adUnitId, linkedAdUnitId)` để script mới dựng lại trạng thái (base, bậc đã có ad), rồi script ra lệnh load tiếp theo config mới.
   - Unit đang `Loading`: kết quả tự về script mới.
   - Unit đang chiếu: script mới tự xin lại qua lệnh load của nó, lệnh này chờ tới khi ad đóng.
6. **Ad đã load được giữ nguyên** (không bỏ ad nào), vẫn được chọn show theo eCpm. Unit script mới không còn dùng thì không được load thêm.

Log: `THAY script #cũ -> #mới ...` rồi `báo lại N unit đang giữ ad cho script mới: ...`.
- **FloorPersonalization (Databuckets)** vẫn theo `enable` trong `SOFloorPersonalizationSetting`:
  - tắt → phiên LUA chỉ dùng ad của Lua;
  - bật → candidate của workflow chỉ được chiếu khi Lua chưa có inter sẵn.
- **Banner / AppOpen / MREC** ở phiên LUA đi theo nhánh unit đơn của SOMediationSetting.
- Script mới trùng script đang chạy → bỏ qua. Script mới lỗi cú pháp → giữ script đang chạy.

## Sơ đồ luồng load

`[C# gọi]` = Unity gọi một trong 8 hàm của Lua · `[Lua gọi]` = Lua ra lệnh load ·
`[C# lưu]` / `[Lua lưu]` = ai giữ dữ liệu đó.

```
 SERVER            C# (Unity, client)                                   LUA (script = code từ server)
   │                       │                                                       │
   │ sc_ad_monetize_       │                                                       │
   │ lua_script ─────────► │ nạp script, tạo bảng context                          │
   │                       │ ── chạy THÂN script ────────────────────────────────► │ [Lua lưu] CONFIG: danh sách unit,
   │                       │                                                       │   thứ tự, hệ số, lịch retry...
   │                       │ ◄──────── [Lua gọi] context.Max.LoadInterstitial ──── │ xin load id DEFAULT
   │                       │ ◄──────── [Lua gọi] context.Max.LoadRewarded ──────── │   (floor 0, delay 0 / 5 / 10s)
   │                       │ [C# lưu] lệnh chờ: unit, floor, delay (Waiting)       │
   │                       │                                                       │
   │                       │ ...MAX init xong + hết delay -> gửi request tới       │
   │                       │    mạng quảng cáo (Loading)                           │
   │                       │ [C# lưu] ad object đã load (SDK giữ, không qua Lua)   │
   │                       │ [C# ghi vào context] eCpm, loadingTime, status...     │
   │                       │                                                       │
   │                       │ ── [C# gọi] OnInterstitialLoadSuccess(id) ──────────► │ [Lua lưu] base eCPM, base#n,
   │                       │      hoặc   OnInterstitialLoadFail(id)                │   hệ số, số lần thử, số fail,
   │                       │                                                       │   bậc bị dừng...
   │                       │ ◄──────── [Lua gọi] context.Max.LoadInterstitial ──── │ quyết định: load bậc floor nào,
   │                       │ ◄──────── [Lua gọi] context.Admob.LoadInterstitial ── │   floor bao nhiêu, delay bao lâu
   │                       │              (0..n lệnh — 0 lệnh = DỪNG)              │   (retry = delay > 0)
   │                       │        ... lặp lại tới khi Lua không ra lệnh nữa ...  │
   │                       │                                                       │
   │      GAME ──────────► │ MediationManager.IsShowInterstitial(where, param)     │
   │                       │ [C# ghi vào context] context.params                   │
   │                       │ ── [C# gọi] IsShowInterstitial() ───────────────────► │ đọc context -> true / false
   │                       │ ◄────────────────────────────────────── true/false ── │
   │                       │                                                       │
   │      GAME ──────────► │ MediationManager.ShowInterstitial(where)              │
   │                       │ C# chọn ad eCpm cao nhất (client, Lua không tham gia) │
   │                       │ ad lên màn hình -> [C# ghi vào context] reset floor,   │
   │                       │   eCpm, startLoadTime, loadingTime, numRetry về 0     │
   │                       │ ── [C# gọi] OnInterstitialShowSuccess(id) ──────────► │ quyết định nạp lại
   │                       │ ◄──────── [Lua gọi] context.*.LoadInterstitial ────── │   (giống sau một lần load)
   │                       │ (lệnh cho unit đang chiếu: đếm delay từ lúc ad đóng)  │
```

Rewarded giống hệt:
- C# gọi `OnRewardedLoadSuccess` / `OnRewardedLoadFail` / `OnRewardedShowSuccess` / `IsShowRewarded`;
- Lua ra lệnh `context.Max.LoadRewarded` / `context.Admob.LoadRewarded`.

### Ai lưu gì

| Dữ liệu | Ai lưu | Ghi chú |
|---|---|---|
| Thuật toán: danh sách unit, thứ tự, hệ số, base eCPM, base#n, số lần thử, số fail, bậc bị dừng, lịch retry | **Lua** | biến trong script, hoặc field Lua tự gắn vào entry của `context.list...` (C# không xoá) |
| Bảng `context.listInterstitialAd` / `listRewardedAd` | **Lua** giữ bảng | C# chỉ **gắn** giá trị của spec vào entry: `mediation, adUnitType, adUnitIdType, adUnitName, adUnitId, floor, eCpm, startLoadTime, loadingTime, numRetry` (+ `networkName, status, lastError`). Lua đừng dùng trùng các tên field này cho dữ liệu riêng |
| `context.params` | C# ghi, chỉ trong lượt `IsShow...` | |
| Ad object đã load | **C#** | nằm trong SDK MAX / AdMob, không đưa qua Lua được |
| Lệnh load đang chờ (floor, delay) | **C#** | trạng thái thực thi lệnh Lua vừa ra. Lua gọi lại cho unit đang chờ = thay lệnh |
| Lượt show đang chạy, callback của game | **C#** | |
| Script đang chạy, chế độ phiên (LUA / MULTICALL) | **C#** | |

### C# tự quyết (không hỏi Lua)

- **Chọn ad để show:** eCpm cao nhất; rewarded xét cả inter.
- **Khi nào thật sự gửi request:** sau MAX init, hết delay, unit không đang chiếu.
- **Phát hiện ad hết hạn / chiếu hỏng:** báo Lua qua `On...LoadFail`, `lastError` = `expired` / `show_failed ...`.
- **Reset giá trị lúc ad lên màn hình** (đúng spec).
- **Chặn id trùng** với MultiCall / FloorPersonalization / DFPool.

## Hợp đồng Unity ↔ Lua (đúng spec)

### Unity gọi vào Lua

**Init (gọi đúng một trong hai, một lần):**

| Hàm | Khi nào |
|---|---|
| `OnInitAdmobSuccess()` | Danh sách `sc_ad_bid_floor` **có** id AdMob: AdMob init xong (AdMob chỉ init sau MAX) |
| `OnInitMaxSuccess()` | Danh sách `sc_ad_bid_floor` **không có** id AdMob: MAX init xong |

Script nên khởi động trong cả hai hàm (chỉ một lần): nếu bid floor có id AdMob về muộn, sau khi đã báo
`OnInitMaxSuccess()`, client init AdMob rồi gọi thêm `OnInitAdmobSuccess()`.

**4 hàm mỗi format:**

| Interstitial | Rewarded | Khi nào |
|---|---|---|
| `OnInterstitialLoadSuccess(adUnitId, linkedAdUnitId)` | `OnRewardedLoadSuccess(adUnitId, linkedAdUnitId)` | Load xong. Record trong list đã có `eCpm`, `loadingTime` |
| `OnInterstitialLoadFail(adUnitId, linkedAdUnitId)` | `OnRewardedLoadFail(adUnitId, linkedAdUnitId)` | Load lỗi, `numRetry` đã +1, `lastError` có lý do. Cũng được gọi khi ad hết hạn (`expired`) hoặc chiếu hỏng (`show_failed ...`, `not_ready_at_show`) để script xin lại |
| `OnInterstitialShowSuccess(adUnitId, linkedAdUnitId)` | `OnRewardedShowSuccess(adUnitId, linkedAdUnitId)` | Ad đã lên màn hình. `floor, eCpm, startLoadTime, loadingTime, numRetry` đã về 0. Script trả lại các lệnh load, như sau một lần load |
| `IsShowInterstitial()` | `IsShowRewarded()` | **Game hỏi** bất cứ lúc nào. Lua đọc `context` rồi trả `true` / `false` |

`linkedAdUnitId` = `nil`, trừ bản load AdMob có truyền tham số thứ 4 (xem dưới). Script viết theo spec
(`function OnInterstitialLoadSuccess(adUnitId)`) vẫn chạy, chỉ là không nhận tham số thứ 2.

Script thiếu hàm nào thì Unity bỏ qua hàm đó.

### Lua gọi vào Unity

```lua
context.Max.LoadInterstitial(adUnitId, floor, delayTime)                        -- trả true nếu nhận
context.Max.LoadRewarded(adUnitId, floor, delayTime)
context.Admob.LoadInterstitial(adUnitId, floor, delayTime [, linkedAdUnitId])
context.Admob.LoadRewarded(adUnitId, floor, delayTime [, linkedAdUnitId])
```

- **`context.Max`** đúng 3 tham số như spec. Tham số thừa bị bỏ qua.
- **`linkedAdUnitId`** (chỉ AdMob, không bắt buộc): **id MAX đầy đủ** mà bản load AdMob này gắn vào, dùng cho
  thác giá AdMob theo giá MAX (bước C thời MetaAdmob).
  - Cùng một `adUnitId` AdMob, mỗi `linkedAdUnitId` là **một bản load riêng**: trạng thái riêng, ad riêng, báo
    kết quả riêng. Vì vậy mỗi id MAX xin được đủ các mức floor AdMob mà không giẫm lên nhau.
  - Key trong `context.list...` của bản load này là `adUnitId .. "@" .. linkedAdUnitId`.
  - SDK AdMob và dữ liệu gửi đi (bigdata, doanh thu) vẫn dùng `adUnitId` thật.
  - Không truyền = bản load thường, key là `adUnitId`.
  - Ví dụ: `context.Admob.LoadInterstitial("ca-app-pub-xxx/3516404259", 2.4, 0, "c99a0850961f734d")`.

**`OnInitMaxSuccess` / `OnInitAdmobSuccess`** là nơi xin load các id **default** đầu tiên (đọc danh sách id từ
`context`). Từ đó mỗi kết quả load / show lại là một lượt Lua trả các lệnh load tiếp theo.

- **`floor`** là eCPM USD:
  - Max gửi qua `jC7Fp`.
  - AdMob gửi qua extras `rusd`, tính bằng cent eCPM.
  - `0` = không đặt sàn.
- **`delayTime`** tính bằng giây.
- **Unit đang `Waiting`** (chưa tới giờ load): gọi lại sẽ **thay lịch cũ** (floor + delay mới). Đây là cách để "huỷ" một lịch retry cũ.
- **Mốc bắt đầu đếm delay:**
  - Lệnh xin **trước lúc mediation của id đó init xong** (Max: MAX SDK; Admob: MobileAds) tính delay từ lúc init xong. Ví dụ: id default cách nhau 5s tính từ lúc mediation sẵn sàng.
  - Lệnh xin **khi ad của unit đó đang chiếu** tính delay từ lúc ad đóng. Ví dụ: "1s sau khi đóng ad".
- **Trả `false`** khi:
  - thiếu `adUnitId`;
  - unit đang `Loading` hoặc `Ready`;
  - id đã dùng cho mediation khác hoặc format khác;
  - **unit Max đang được MultiCall / FloorPersonalization / DFPool dùng.** Lua bắt buộc dùng **bộ id riêng**.
- **Request của mediation nào chỉ bắn sau khi chính mediation đó init xong.** Lệnh load gọi lúc chưa init chỉ là
  xếp lịch (`Waiting`), log `[LuaAd]` ghi `CHƯA gửi: chờ ... init xong`. Request thật là dòng `[LuaAd] LOAD ...`.
  - **Chung:** chưa chốt chế độ LUA (lúc bắt đầu init MAX, đã qua consent) thì không mediation nào được bắn.
  - **Max:** chờ `MaxSdk.IsInitialized()`.
  - **Admob:** chờ MobileAds init xong. Lua tự init AdMob, **chỉ khi danh sách `sc_ad_bid_floor` có id AdMob**:
    - Log lúc nhận bid floor: `bid floor (...): ... | CÓ id AdMob -> init AdMob sau MAX` / `KHÔNG có id AdMob`.
    - Có -> `MobileAds.Initialize` trong **callback MAX init thành công** (`FalconMaxService`, `OnSdkInitializedEvent`
      gọi `LuaAdMonetize.OnMaxSdkInitialized`). Tuần tự: MAX init xong rồi mới init AdMob, **không** song song.
      Xong thì gọi `OnInitAdmobSuccess()`. Log: `INIT ADMOB (...)` → `MobileAds.Initialize xong` → `gọi OnInitAdmobSuccess() sang Lua`.
    - Không có -> không init AdMob, gọi `OnInitMaxSuccess()` ngay trong callback MAX init. Lệnh load AdMob (nếu
      script vẫn xin) chờ mãi, trừ khi module khác đã init MobileAds.
    - MAX đã init xong mà bid floor có id AdMob mới về -> init AdMob ngay rồi gọi `OnInitAdmobSuccess()`.
    - MobileAds đã được module khác init rồi thì dùng luôn.
- **Retry do script tự quyết.** MAX tự retry đã bị tắt bằng `disable_auto_retries`.
- **Log từ Lua:** dùng `print(...)`, ra log Unity với tag `[LuaAd][lua]`.

### `context`

```lua
-- key = adUnitId, hoặc adUnitId .. "@" .. linkedAdUnitId (bản load AdMob gắn id MAX)
context.listInterstitialAd[key] = {
  mediation,      -- "Max" | "Admob"
  adUnitType,     -- "Interstitial" (listRewardedAd: "Reward")
  adUnitIdType,   -- "default_1" | "default_2" | "h1" | "h2" | ... theo sc_ad_bid_floor, nil nếu id không có trong đó
  adUnitName,     -- = adUnitId (giữ field cho đúng bảng của spec)
  adUnitId,
  linkedAdUnitId, -- id MAX mà bản load AdMob gắn vào, nil nếu không có  (thêm so với spec)
  multiplier,     -- hệ số của id trong sc_ad_bid_floor, 0 nếu không có  (thêm so với spec)
  timeConfig,     -- timeConfig của id trong sc_ad_bid_floor, nil nếu không có. Script mẫu (thuật toán 8) KHÔNG dùng  (thêm so với spec)
  floor,          -- giá sàn eCPM (USD) của lần load gần nhất
  eCpm,           -- eCPM (USD) của ad đang giữ, 0 = chưa có / không biết giá
  startLoadTime,  -- giây unix lúc bắt đầu load gần nhất, 0 = chưa load từ lần show trước
  loadingTime,    -- số giây của lần load gần nhất
  numRetry,       -- số lần load lỗi liên tiếp, về 0 khi load được ad hoặc khi show
  networkName,    -- network thắng (nil nếu chưa có ad)            (thêm so với spec)
  status,         -- "Idle" | "Waiting" | "Loading" | "Ready" | "Showing"   (thêm so với spec)
  lastError,      -- lý do lỗi gần nhất (nil nếu không có)         (thêm so với spec)
}
context.listRewardedAd[key] = { ...cùng các field... }
context.params   -- chỉ có trong lượt IsShowInterstitial / IsShowRewarded
```

- **Id của `sc_ad_bid_floor` có sẵn trong list** (status `Idle`) trước cả khi script xin load. Bản load AdMob
  gắn id MAX (`adUnitId@linkedAdUnitId`) lấy `adUnitIdType` / `multiplier` / `timeConfig` của id gốc.
- **Bảng thuộc về Lua.** Trước mỗi lần gọi vào Lua, C# ghi đè **tại chỗ** đúng các field trên của từng unit. C# không xoá bảng, không thay entry.
- Lua gắn thêm field riêng vào entry được (vd `e.myTier = 3`), field đó còn nguyên qua các lần gọi sau.
- Lua sửa các field của spec thì sẽ bị C# ghi đè ở lần gọi kế tiếp.

### Game hỏi Lua

```csharp
// gọi được từ bất cứ đâu, bất cứ lúc nào (main thread)
bool okInter = MediationManager.Instance.IsShowInterstitial("level_end",
    new Dictionary<string, object> { { "isBoss", true } });
bool okRw = MediationManager.Instance.IsShowRewarded("revive");
```

- `context.params` = `{ where, level }` + mọi cặp trong dictionary. Bảng này cũng được truyền làm tham số đầu của hàm Lua.
- Đây **chỉ là câu hỏi**: hàm không show gì cả, game tự quyết định có gọi `ShowInterstitial` / `ShowRewardedVideo` hay không.
- Khi chưa có script, script không có hàm, hoặc hàm ném lỗi, Unity trả về `defaultValue` (mặc định `true`).

## Lúc show (phía client)

Game vẫn gọi `MediationManager.ShowInterstitial` / `ShowRewardedVideo` như cũ. Ở phiên LUA:

| Lượt show | Ad được chọn |
|---|---|
| Interstitial | inter có eCpm cao nhất trong `listInterstitialAd`. Lua chưa có inter → candidate FloorPersonalization (nếu SO bật). Không có gì → fail |
| Rewarded | ad có eCpm cao nhất trong **cả** `listRewardedAd` lẫn `listInterstitialAd`; hoà giá thì RW thắng. Inter chỉ được xét khi `showInterstitialInstead = true`. Người chơi xem inter thay RW thì đóng ad là được thưởng. Không có gì → fail |

- **Remove-ads và popup ad break** của interstitial vẫn chạy như cũ.

## Script mẫu

Xem [Samples/Resources/LuaAdTest.txt](Samples/Resources/LuaAdTest.txt): load **theo thuật toán 8 của MultiCall**, cho cả inter và rewarded.

## File

| File | Vai trò |
|---|---|
| `SCAdMonetizeLuaScript.cs` | Bản tin server → `LuaAdMonetize.ApplyScript` |
| `LuaAdMonetize.cs` | Điểm vào, tự khởi động. Giữ danh sách ad, gọi hàm Lua, API show / hỏi `IsShow...`, chốt chế độ phiên |
| `LuaScriptHost.cs` | Môi trường Lua: sandbox, giới hạn số lệnh, bắt lỗi |
| `LuaAdContext.cs` | Bảng `context` trong Lua |
| `LuaAdLoaders.cs` | Load / show interstitial + rewarded của Max và AdMob |
| `LuaAdRecord.cs` | Một dòng của `context.listInterstitialAd` / `context.listRewardedAd` |
| `Plugins/MoonSharp.Interpreter.dll` + `Plugins/link.xml` | MoonSharp 2.0.0, giữ khỏi bị IL2CPP strip |
| `Samples/` | Bộ test (define `LUA_AD_TEST`). Xoá được |

Code ngoài thư mục:
- `MediationManager`: `IsShowInterstitial`, `IsShowRewarded`, hàng rào init.
- `FalconMaxService`: chế độ LUA, `ShowLuaAd`.

Toàn bộ code chạy (trừ bản tin) nằm trong `#if MAX_ENABLE`.

## Log / analytics

- **Log:** tag `[LuaAd]`, các mốc LOAD, LOAD OK, LOAD FAIL, SHOW, DISPLAYED, REWARD, HIDDEN, PAID, SHOW FAIL, IsShow.... Log thường chỉ có khi bật `ENABLE_DEBUG_LOG`; lỗi của script luôn được log.
- **Lượt show:**
  - CSAdStart/CSAdFinish, action log, GameEvent, đếm cap placement: `FalconMaxService.ShowLuaAd`.
  - BigData load result / shown / closed / impression (Firebase, MMP, BigData, LTV): `LuaAdMonetize`, qua `DFPoolReporter`.
- **Event của unit Lua:** FalconMaxService / MultiCall / DFPool / bridge AdMob bỏ qua (`ExternalMaxAdUnits`).
- **`disable_b2b_ad_unit_ids`** (set TRƯỚC `MaxSdk.InitializeSdk`, ở `FalconMaxService.EventStartInit`) và mảng init MAX
  lấy từ `LuaAdMonetize.MaxAdUnitIds()`: **id MAX của `sc_ad_bid_floor`** + id MAX script đã xin load + mọi id MAX
  ghi dạng chữ trong script (chuỗi đúng 16 ký tự hex trong dấu nháy). Script tự dựng id MAX lúc chạy (không ghi
  dạng chữ, không có trong bid floor) thì id đó không vào được danh sách.
  Log: `[LuaAd] id MAX của Lua (n, ...)` và `CallBeforeInit (LUA) : disable_b2b_ad_unit_ids - ...` (log vàng).
- **`disable_auto_retries`**: set ngay sau khi MAX init cho mọi unit MAX của bid floor (`LuaAdMonetize.CallAfterInit`, log vàng `CallAfterInit (LUA) interstitial|rewarded: <id> - disable_auto_retries`); id chỉ ghi trong script thì set ở lần load đầu (`LuaMaxLoader.Load`). Mỗi unit chỉ set một lần.

## An toàn

- **Sandbox `Preset_SoftSandbox` + `LimitedPlatformAccessor`:**
  - không có `io`, `os.execute`, `load` / `require` / `dofile`, `debug`;
  - vẫn có `string`, `table`, `math`, `os.time`, `os.clock`, `os.date`, `coroutine`, `json`.
- **Giới hạn lệnh:** mỗi lần gọi hàm Lua (kể cả thân script) tối đa 500.000 lệnh. Quá mức thì lượt gọi bị ngắt, game không treo.
- **Không đưa object C# nào vào Lua** (không UserData / reflection).
- **Chưa kiểm chữ ký script.** Script đi qua kênh socket của game server.

## Server cấu hình id cho Lua trong `sc_ad_bid_floor`

Để id của Lua không làm hỏng bản cũ (bản cũ không biết `mediation` / `adUnitIdType`, coi mọi id là id MAX):

- `mediation = "admob"` cho id AdMob.
- `multiplier` **khác 0** cho mọi id của Lua, kể cả id default (bản 1.4.7+ luôn giữ id có `multiplier = 0`). Lua nhận
  id default bằng `adUnitIdType` chứa `default`, không bằng multiplier.
- `algorithmIds = [10]` cho mọi id của Lua. Chưa có provider MultiCall nào dùng index 10 (bản đã phát hành có 0–7,
  bản này có 0–8), nên bộ lọc của bản 1.4.7+ bỏ các id này. **CMS không được đặt `moMulProviderIndexMax = 10`**:
  client không có provider 10 sẽ rơi về provider 0 và load cả id của Lua bằng MAX. Lua không đọc `algorithmIds`.
- Bản mới: phiên LUA đọc danh sách gốc, không lọc theo `algorithmIds`. Phiên MULTICALL (MAX / LevelPlay) bỏ qua id
  AdMob (`AdBidFloor.MediationKind`).
- Bản ≤ 1.3.37 không lọc gì: id nào gửi xuống cũng bị load bằng MAX. Chỉ gửi id của Lua cho client đủ version.
