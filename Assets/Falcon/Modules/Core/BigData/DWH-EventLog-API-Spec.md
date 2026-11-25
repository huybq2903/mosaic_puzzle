# DWH Event-Log API — Đặc tả cấu trúc dữ liệu

Tài liệu mô tả **hợp đồng dữ liệu (wire contract)** để gửi log analytics lên DWH. Mục đích: một ứng dụng/SDK khác có thể tự dựng lại bản tin mà không cần đọc source Unity.

> Mọi tên trường nêu trong tài liệu là **key JSON thật** xuất hiện trên đường truyền.

---

## 1. Endpoint

| Mục đích | Method | URL |
|---|---|---|
| Gửi 1 log | `POST` | `https://dwhapi-v2.data4game.com/event-log-v2` |
| Gửi nhiều log (batch) | `POST` | `https://dwhapi-v2.data4game.com/batch/event-log-v2` |

- `Content-Type: application/json`
- Nếu danh sách batch chỉ có **1 phần tử**, phía gửi tự chuyển sang endpoint đơn (`/event-log-v2`).

---

## 2. Bao bì bản tin (Envelope)

Đơn vị log gửi đi luôn là một **envelope** gồm đúng 5 trường:

| Key | Kiểu | Mô tả |
|---|---|---|
| `data` | **string** | **Chuỗi JSON đã được encode** chứa toàn bộ nội dung event (xem mục 4). Đây là JSON lồng dạng string, KHÔNG phải object. |
| `clientSendTime` | long | Thời điểm client gửi, epoch milliseconds (UTC). |
| `event` | string | Định danh loại event, ví dụ `f_sdk_ads_data` (xem mục 5). |
| `packageName` | string | Package/bundle id của ứng dụng. |
| `platform` | string | Nền tảng, ví dụ `Android`, `iOS`. |

### 2.1. Request đơn — `/event-log-v2`

Body = **một** envelope:

```json
{
  "data": "{\"event\":\"f_sdk_ads_data\",\"adWhere\":\"main_menu\", ... }",
  "clientSendTime": 1750838400000,
  "event": "f_sdk_ads_data",
  "packageName": "com.falcon.mygame",
  "platform": "Android"
}
```

### 2.2. Request batch — `/batch/event-log-v2`

Body = **một mảng JSON các chuỗi**, mỗi phần tử là một envelope đã được serialize thành string (lồng escape nhiều lớp):

```json
[
  "{\"data\":\"{\\\"event\\\":\\\"f_sdk_ads_data\\\", ...}\",\"clientSendTime\":1750838400000,\"event\":\"f_sdk_ads_data\",\"packageName\":\"com.falcon.mygame\",\"platform\":\"Android\"}",
  "{\"data\":\"{\\\"event\\\":\\\"f_sdk_level_data\\\", ...}\",\"clientSendTime\":1750838400500,\"event\":\"f_sdk_level_data\",\"packageName\":\"com.falcon.mygame\",\"platform\":\"Android\"}"
]
```

> ⚠️ Lưu ý lồng escape: ở batch có **3 lớp**:
> 1. Mảng JSON ngoài cùng.
> 2. Mỗi phần tử là string của envelope.
> 3. Bên trong envelope, `data` lại là string của object event.

---

## 3. Quy ước serialize (BẮT BUỘC tuân thủ để khớp dữ liệu)

| Quy ước | Chi tiết |
|---|---|
| **Trường null bị loại bỏ** | Các trường optional khi null sẽ **không xuất hiện** trong JSON (không gửi `null`). Trường bắt buộc luôn có. |
| **Enum → string** | Mọi enum serialize bằng **tên** (ví dụ `Interstitial`, `Pass`, `Source`), không phải số. |
| **TimeSpan → giây (số nguyên)** | Các trường thời lượng (`duration`, `sessionTime`, `impressionDuration`) được quy đổi thành **tổng số giây** kiểu integer. |
| **Ngày giờ → chuỗi** | Định dạng `yyyy-MM-dd HH:mm:sszzz` (ví dụ `2026-06-26 14:30:00+07:00`). |
| **Ưu tiên khi trùng key** | Các trường **base + central user params** (mục 4.1, 4.2) được ghi vào trước theo cơ chế "không ghi đè". Nếu một trường riêng của event trùng tên thì **giá trị của lớp base/user-param thắng**. |
| **`event`** | Bên trong `data` cũng lặp lại key `event` = đúng giá trị của envelope. |

---

## 4. Cấu trúc bên trong `data`

Object trong `data` gồm **3 lớp** ghép lại. Lớp 1 và 2 có mặt ở **mọi** event; lớp 3 là phần riêng theo từng `event`.

### 4.1. Lớp BASE — luôn có ở mọi event

| Key | Kiểu | Mô tả |
|---|---|---|
| `event` | string | Định danh event (trùng envelope). |
| `logTypeCreateId` | int | Số thứ tự log loại này được **tạo** trên thiết bị (đếm tăng dần từ 0 theo từng `event`). |
| `logTypeSendId` | int | Số thứ tự log loại này được **gửi** (đếm tăng dần từ 0 theo từng `event`). |
| `uuid` | string | UUID sinh cho mỗi log (chống trùng). Server dedup theo `uuid` trong **time-window 1 tiếng** — client gửi lại (retry/re-enqueue) trong vòng 1 tiếng là an toàn; gửi lại muộn hơn có thể bị tính trùng. |
| `createdDateLocal` | string | Thời điểm tạo log theo giờ local, định dạng `yyyy-MM-dd HH:mm:sszzz`. |

### 4.2. Lớp CENTRAL USER PARAMS — luôn có ở mọi event

Khối thông tin user/thiết bị/phiên được chèn vào **mọi** event. Trường nào nguồn dữ liệu null thì bị bỏ (theo mục 3).

**General**

| Key | Kiểu | Mô tả |
|---|---|---|
| `accountId` | string | Id tài khoản người chơi. |
| `level` | int | Level cao nhất đã qua (alias của `maxPassedLevel`). |
| `maxPassedLevel` | int | Level cao nhất đã qua. |
| `installVersion` | string | Phiên bản app lúc cài đặt lần đầu. |
| `advertisingId` | string | Advertising ID (bỏ nếu null). |

**Session**

| Key | Kiểu | Mô tả |
|---|---|---|
| `installDay` | string | Ngày đăng nhập đầu tiên (UTC), `yyyy-MM-dd HH:mm:sszzz`. |
| `installDayLocal` | string | Ngày đăng nhập đầu tiên (local). |
| `activeDays` | int | Số ngày hoạt động. |
| `sessionId` | int/string | **SỐ ĐẾM** lượt chạy app — "đây là lần mở app thứ mấy". ⚠ Trong kho cột này tên là **`session_index`** (pipeline Alias đổi tên `session_id` → `session_index`), nên SQL phải viết `session_index`. ⚠ **KHÔNG phải khoá nhận diện phiên**, kể cả sau bản vá 1.5.8: xem luật đọc ngay dưới bảng. |
| `sessionUid` | string | Id **DUY NHẤT** của một lượt chạy app, bốc một lần mỗi process. **Đây là khoá nhận diện phiên** — gom theo phiên thì khoá cái này. |
| `firstLogin` | long | Mốc thời gian đăng nhập đầu (epoch ms). |
| `totalPlayTime` | long | Tổng thời gian chơi, **giây**. |
| `retentionDay` | int | Retention day. |

> **`sessionId` vs `sessionUid` — luật đọc (viết 1.5.8, sau án "một uid nhiều id"):**
>
> 1. **Gom theo phiên thì khoá `session_uid`, không khoá `session_id`.** `session_id` là một số đếm
>    và **không** duy nhất theo lượt chạy — ba ca trùng số mà client không tự biết: cài lại app (sổ
>    đếm về từ đầu nên lượt đó dùng lại số của một phiên cũ), hai thiết bị cùng account, và app chết
>    trước khi kịp đẩy sổ. Luật này đúng **vĩnh viễn**, không phải chỉ cho dữ liệu cũ.
> 2. ⚠ **Dữ liệu trước mốc vá đã nhiễm, theo CẢ HAI chiều.** Một `session_uid` mang **2–3**
>    `session_id` khác nhau; và chiều ngược — chiều làm sai số liệu — là log **khúc đầu** của lượt
>    chạy N+1 mang `session_id` của lượt N, nên mọi phép gộp khoá theo `session_id` **nhồi khúc đầu
>    lượt mới vào lượt cũ**: phiên cũ phình ra, phiên mới thiếu đầu.
> 3. **Vì sao nó xảy ra**: client đọc lại số đếm ở **mỗi** log, từ một sổ bị server ghi đè giữa
>    phiên. Ba đường: (a) user cũ — log khúc đầu mang số lượt trước, tới lúc SDK khởi tạo xong mới
>    `+1`; (b) máy mới — sổ nhà → bản server đè → `+1` ⇒ ba số; (c) reconnect / đa thiết bị — bản
>    server về sau có thể làm số **tụt ngược**.
> 4. **Mốc vá là `AnalyticParamsSync` 1.1.8** (số đếm chốt một lần cho cả lượt chạy).
>    ⚠ **Không lọc được mốc này bằng `sdk_version`** — cột đó chỉ mang version **BigData** (xem
>    `sdkVersion` ở mục **App** dưới), còn bản vá nằm ở module khác nên **vô hình trên dây**.
>    `sdk_version < '1.5.8'` thì chắc chắn **chưa** vá; từ 1.5.8 trở lên thì *có thể* đã vá, tuỳ game
>    có lấy kèm 1.1.8 hay không. Muốn biết chắc thì **đo trên dữ liệu**, đừng tin version:
>
>    ```sql
>    -- máy nào còn nhiễm: một uid mà ra nhiều số đếm
>    select count(*) from (
>      select session_uid from <event>
>      where created_day >= <mốc> group by 1 having count(distinct session_index) > 1
>    ) t;
>    -- chiều đau: một số đếm ứng nhiều uid
>    select account_id, session_index, count(distinct session_uid) n
>    from <event> group by 1,2 having n > 1;
>    ```
>
> 5. **Bảng nào tự lành, bảng nào không** (cho dữ liệu trước mốc vá): bảng gold lấy **MAX số đếm
>    trong phiên/trong ô** thì phần lớn đã đúng, vì ván chơi / lượt xem ad thường kéo dài qua lúc số
>    mới về. Bảng **1 dòng = 1 bản tin** (dòng kinh tế, dòng bước live-op) **không có gì để lấy MAX**
>    nên mang nguyên số cũ, lệch `−1` ở khúc đầu phiên. Đừng nấu lại hai bảng đó để chữa: ghi cảnh
>    báo vào desc cột là đủ, còn dữ liệu mới thì bản vá client chữa ở gốc.
> 6. **Nhóm đối chứng khi đo**: game **không** gắn `AnalyticParamsSync` dùng sổ đếm cục bộ (đứng im
>    suốt lượt chạy) nên phải ra **0**; ra số khác 0 là còn nguyên nhân thứ hai. Và nhớ lọc
>    `is_test IS NULL` — log editor có chuỗi khởi tạo chạy sau lượt stop nên làm phồng đếm.


**Ad**

| Key | Kiểu | Mô tả |
|---|---|---|
| `adLtv` | double | LTV quảng cáo tích lũy (bỏ nếu null). |
| `interAdCount` | int | Số interstitial đã xem. |
| `rewardAdCount` | int | Số reward đã xem. |

**IAP**

| Key | Kiểu | Mô tả |
|---|---|---|
| `firstInAppLv` | int | Level lúc IAP đầu tiên (bỏ nếu null). |
| `firstInAppDateStr` | string | Ngày IAP đầu tiên (bỏ nếu null). |
| `firstInAppProduct` | string | Product IAP đầu tiên (bỏ nếu null). |
| `lastInAppLv` | int | Level lúc IAP gần nhất (bỏ nếu null). |
| `lastInAppDateStr` | string | Ngày IAP gần nhất (bỏ nếu null). |
| `lastInAppProduct` | string | Product IAP gần nhất (bỏ nếu null). |
| `inAppCount` | int | Tổng số IAP. |
| `inAppMax` | number | Giá trị IAP lớn nhất (chỉ có khi có dữ liệu LTV). |
| `inAppTotal` | number | Tổng giá trị IAP (chỉ có khi có dữ liệu LTV). |
| `inAppCurrency` | string | Mã tiền tệ ISO của LTV IAP (chỉ có khi có dữ liệu LTV). |

**App**

| Key | Kiểu | Mô tả |
|---|---|---|
| `platform` | string | Nền tảng. |
| `appVersion` | string | Phiên bản app hiện tại. |
| `sdkVersion` | string | *(MỚI 1.5.0)* Phiên bản **module BigData** đã sinh ra dòng log này. **Đây là mốc cắt ERA chuẩn**: mọi câu "từ bản nào trường X đổi nghĩa / ngừng gửi" đọc trực tiếp bằng `WHERE sdk_version >= '…'`, không phải suy từ `appVersion` của game (một bản game có thể vá SDK giữa đường nên suy kiểu đó sai âm thầm). Phiên bản của module khác (Mediation, GameData…) **không** đi đường này. |
| `isTest` | bool | *(MỚI — bản sau 1.5.1)* Dòng log do **NGƯỜI LÀM GAME** sinh ra. **CHỈ gửi khi true**; vắng mặt = dữ liệu thật. Chỉ có hai nguồn, đều là LOẠI BUILD: editor play và development build. **Bản production KHÔNG BAO GIỜ mang field này** — không API, không remote config, không common param nào tạo được nó (tên key nằm trong sổ key-trung-tâm nên game set trùng tên bị từ chối). |
| `testSource` | string | *(MỚI — bản sau 1.5.1)* Vì sao là test: `editor` · `dev_build`. Đi kèm `isTest`, cùng luật vắng-mặt. |

> **Hai field `isTest` / `testSource` — luật đọc (chốt owner 16/10):**
>
> 1. **Mọi dashboard mặc định lọc `is_test IS NULL`.** Đừng để chart mới âm thầm trộn log của người
>    làm game vào số thật — đây là luật quan trọng nhất của hai field này.
> 2. **GIỮ các dòng đó, đừng drop.** Chúng là thứ dùng để nghiệm thu tích hợp: QA bắn log xong soi
>    được ngay trên kho. Muốn log test **không vào kho** thì bên mình có công tắc riêng
>    (`fCoreAnalyticShouldNotSendLogToServer` / URL testing) — hai mục đích khác nhau.
> 3. **Cờ chỉ nói "log của người làm game"**, không nói "log đáng nghi". Bên mình **không** lấy máy
>    root / emulator / nghi cheat làm nguồn: nhiều người chơi thật dùng máy root, đánh dấu test là
>    mất dữ liệu thật im lặng.
> 4. ⚠ **Giới hạn đã biết: build QA ở chế độ RELEASE trông như production.** Đây là cái giá của luật
>    "production tuyệt đối không mang cờ". Muốn tách nhóm đó thì đổi bundle id (`package_name` đã có
>    sẵn trên envelope), hoặc cho build đó khỏi gửi bằng config.
> 5. Cờ **không persist** ở client: nó là thuộc tính của BUILD, không của máy. Cài bản dev rồi cài
>    bản store lên cùng thiết bị thì dòng production **không** mang cờ.

**Device**

| Key | Kiểu | Mô tả |
|---|---|---|
| `deviceId` | string | Id thiết bị. |
| `deviceOs` | string | Hệ điều hành. |
| `deviceName` | string | Tên thiết bị. |
| `deviceModel` | string | Model thiết bị. |
| `screenWidth` | int | Rộng màn hình (px). |
| `screenHeight` | int | Cao màn hình (px). |
| `screenDpi` | number | DPI màn hình. |
| `deviceGpu` | string | Tên GPU. |
| `deviceCpu` | string | Loại CPU. |
| `deviceRam` | int | RAM (MB). |
| `deviceGpuRam` | int | VRAM GPU (MB). |
| `deviceCpuCount` | int | Số nhân CPU. |
| `deviceCpuFrequency` | int | Xung CPU. |
| `language` | string | Ngôn ngữ thiết bị. |
| `idFv` | string | IDFV (iOS). |

### 4.3. Lớp CUSTOM PARAMS — gắn từ MMP / AB-test / nguồn khác

Đây là các param **động**, được gộp thêm vào lớp central user params ở trên. Chúng **chỉ xuất hiện khi có dữ liệu** (ví dụ: chỉ có sau khi nhận được attribution callback từ MMP; chỉ có khi user nằm trong một AB-test). Các key có thể vắng mặt hoàn toàn ở những bản tin đầu phiên.

> Lưu ý: tại một thời điểm thường **chỉ một MMP active** (Adjust *hoặc* AppsFlyer) tùy cấu hình build, nên thường chỉ một trong hai khối dưới đây xuất hiện.

**MMP — Adjust (attribution)** — các key dưới đây bắt nguồn từ payload attribution của Adjust SDK:

| Key | Kiểu | Mô tả |
|---|---|---|
| `network` | string | Ad network nguồn. |
| `campaign` | string | Campaign. |
| `adgroup` | string | Ad group. |
| `creative` | string | Creative. |
| `clickLabel` | string | Click label. |
| `trackerName` | string | Tên tracker. |
| `trackerToken` | string | Token tracker. |
| `costType` | string | Loại chi phí. |
| `costAmount` | number | Giá trị chi phí. |
| `costCurrency` | string | Tiền tệ chi phí. |
| `fbInstallReferrer` | string | Facebook install referrer. |

**MMP — AppsFlyer (conversion data)** — toàn bộ conversion data thô của AppsFlyer được trải phẳng vào; các key thường gặp:

| Key | Kiểu | Mô tả |
|---|---|---|
| `media_source` | string | Nguồn media. |
| `campaign` | string | Tên campaign. |
| `campaign_id` | string | Id campaign. |
| `adgroup` | string | Ad group. |
| `adgroup_id` | string | Id ad group. |
| `af_c_id` | string | Campaign id (AppsFlyer). |
| `af_ad` | string | Ad. |
| `af_status` | string | Trạng thái (`Organic`/`Non-organic`). |
| `is_first_launch` | bool | Lần mở app đầu tiên hay không. |
| `is_retargeting` | bool | Có phải retargeting hay không. |
| `advertising_id` | string | Advertising id. |
| `orig_cost` | string | Chi phí gốc. |
| `af_cost_value` | number | Giá trị chi phí. |
| `af_cost_currency` | string | Tiền tệ chi phí. |
| `af_cost_model` | string | Mô hình chi phí. |
| `cost_cents_USD` | number | Chi phí (cents USD). |

> AppsFlyer trả về dict conversion thô nên **mọi key khác** mà SDK gửi kèm cũng sẽ đi theo, không giới hạn ở danh sách trên.

**AB Testing**

| Key | Kiểu | Mô tả |
|---|---|---|
| `abTestingValue` | string | Giá trị/biến thể AB-test mà user thuộc về. |
| `abTestingVariable` | string | Campaign/biến AB-test đang chạy. |

**Khác**

| Key | Kiểu | Mô tả |
|---|---|---|
| `serverAccountId` | string | Account id phía server (sau khi đăng nhập). |

> Ngoài ra app có thể tự gắn thêm các custom param khác (key động, không cố định).

**Personalization *(MỚI 1.5.7, chốt 29/10)*** — ba key mức USER, do **tầng mediation cấp** qua kênh
inject động (`IFCustomInfoRepository`), không nằm trong danh sách cứng ở 4.2:

| Key | Kiểu | Mô tả |
|---|---|---|
| `deviceTier` | string | Bậc máy do bên quyết sàn phân: `low` · `medium` · `high` · `unknown`. ⚠ `unknown` là **giá trị hợp lệ** (Rule không map được device model), khác hẳn **vắng key** = chưa có ai cấp |
| `realtimeSegments` | array&lt;string&gt; | Các segment realtime của user (vd `["all_user"]`) |
| `realtimeVariants` | array&lt;string&gt; | Các variant realtime của user (vd `["all_user_2_1"]`) |

> **Vì sao ba key này ở lớp 4.3 chứ không phải 4.2:** giá trị do **mediation** biết, không phải SDK —
> SDK chỉ mở kênh. Kênh đó được gọi **mỗi lần dựng log**, nên bên cấp giá trị phải **tự giữ** giá trị
> trong RAM/đĩa chứ không gọi lại server trong đó. Hệ quả khi đọc số: ba key **vắng ở những bản tin
> đầu phiên** (trước lúc mediation có giá trị) — vắng = CHƯA BIẾT, đúng luật nhà.
> <br/>Ba key cũng đi vào `properties` của lần fetch remote config và payload CDN (cùng đường
> `GetUserParams()`), nên dùng được luôn làm điều kiện targeting.

### 4.4. Lớp PER-EVENT — riêng theo từng `event`

Xem mục 5.

---

## 5. Danh mục event và trường riêng

Có **12** giá trị `event`. Mỗi mục dưới đây chỉ liệt kê **phần trường riêng** (cộng thêm vào lớp 4.1 + 4.2).

### 5.1. `f_sdk_ads_data` — Quảng cáo

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `type` | enum string | ✓ | Loại ad: `Banner`, `Interstitial`, `Reward`, `AppOpen`, `Native`, `CollapsibleBanner`, `MREC`. Hai **HỌ**, luật đọc khác nhau *(ghi rõ 19/10)*: **ở lại trên màn** (`Banner`/`MREC`/`CollapsibleBanner`/`Native`) → **không có** mốc `close`, `adViewId` là id **instance** (banner tự refresh nên N impression chung một id, bản tin cụm mang `impressionCount`; MREC và collapsible thì tuỳ network — collapsible của AdMob **không** refresh); **chiếm cả màn rồi đóng** (`Interstitial`/`Reward`/`AppOpen`) → một id một lần chiếu, có `close`. ⚠ Hiện **chỉ `Banner` có dữ liệu thật**: `MREC` chưa game nào dùng, `Native` chưa provider nào phát. |
| `adWhere` | string | ✓ | Vị trí hiển thị ad. |
| `prevFullScreenAdFormat` | enum string | — | *(mới 19/10)* Quảng cáo **toàn màn** vừa ĐÓNG trước dòng này (`Interstitial`/`Reward`/`AppOpen`). CHỈ có trên dòng của họ **dính màn** đi per-impression (`Native`/`MREC`/`CollapsibleBanner`) — dòng gộp cụm của banner không mang (§4i), và banner là đồ đạc cố định nên câu hỏi "hiện sau ad nào" không áp dụng. SDK tự đóng dấu, game không truyền. Vắng = phiên này chưa có ad toàn màn nào đóng (sổ không sống qua phiên) |
| `prevFullScreenAdSec` | int | — | *(mới 19/10)* Số giây kể từ lúc quảng cáo đó **đóng**. ⚠ **Phải đọc kèm** `prevFullScreenAdFormat`: một mình cái format thì native hiện 40 phút sau vẫn trông như "liền sau reward". Client **không cắt ngưỡng** — ngưỡng *"trong vòng mấy giây mới tính là liền sau"* là câu hỏi phân tích, phía đọc tự chọn |
| `adWhen` | string | ✓ | Thời điểm/ngữ cảnh hiển thị. |
| `adRev` | double | ✓ | Doanh thu ad (≥ 0) — **trục tiền của dòng này**. ⚠ Dòng impression **không** chở eCPM: hai số lệch đúng ×1000 nên chở cả hai là thừa (chốt 26/10). eCPM nằm ở dòng **load** (§5.11f), nơi chưa có đồng doanh thu nào; so *"ước tính vs thật"* thì join bằng `adViewId` |
| `adCandidateId` | string | — | **MỚI 1.5.7** — id của nhát thử load đã mang về chính quảng cáo này. Có nó ở **cả** dòng load và dòng impression là join được *từng nhát thử* với *tiền thật nó mang về* — và đó là lý do dòng này vẫn **không** cần chở eCPM ước tính. Vắng khi lần load của quảng cáo này không đi qua `Ad.OnLoadResult` (nhánh mediation chưa khai, hoặc ad còn trong kho từ phiên trước) |
| `ecpmFloor` | double | — | **MỚI 1.5.7** — giá sàn eCPM đã gửi kèm request đã mang về quảng cáo này (= `ecpm_floor` của bảng partner: *"floor của candidate được chọn để show"*). SDK lấy từ sổ theo unit. ⚠ 1.5.5 mở → 26/10 rút → 1.5.7 mở lại: lý do rút (trùng trục tiền với `ad_rev`) **không áp dụng cho giá sàn**, sàn là cấu hình chứ không phải doanh thu. Khác `floor` = sàn thật ĐÃ ĐẶT của unit |
| `ecpmFloorType` | string | — | **MỚI 1.5.7** — vai của giá sàn đó (xem §5.11f-2) |
| `ecpmFloorPercentile` | string | — | **MỚI 1.5.7** — percentile nguồn của giá sàn đó |
| `impressionCount` | int | ✓ | Số impression (mặc định 1). |
| `adPrecision` | string | — | Độ chính xác doanh thu (bỏ nếu null). |
| `adCountry` | string | — | Quốc gia (bỏ nếu null). |
| `adNetwork` | string | — | Ad network trả ad về (bỏ nếu null). Mediation điền; bỏ trống thì **từ 1.5.5** SDK lấy từ sổ theo `adUnitId` |
| `adMediation` | string | — | Mediation (bỏ nếu null) — **giữ nguyên văn** chuỗi mediation truyền (`"Max"`, `"Admob"`...). *(1.5.5 có cân việc chuẩn hoá về `max`/`levelplay`/`admob` rồi **bỏ**: đổi giá trị một cột đang chạy thì mọi mapping lọc chữ cũ phải sửa.)* ⚠ Nhánh IronSource hiện tự khai là `"Max"` → kho **không có** LevelPlay, chờ Mediation sửa ở nguồn |
| `adAlgorithm` | string | — | **MỚI 1.5.5** — tên thuật toán load của đợt (`multicall_<index>` · `single`). Thuộc tính của cả đợt nên có ở mọi mốc ad, không riêng impression |
| `sessionAdImpressionIndex` | int | — | **MỚI 1.5.5** — lần hiển thị thứ mấy **của format này trong PHIÊN** (đếm từ 1; reset khi `sessionId` đổi). ⚠ Khác `typeCount` ngay cạnh: cái đó là sổ theo **ĐỜI** (persist, dùng cho LTV) — tiền tố `session` trên dây là để phân biệt đúng chỗ này. ⚠ **Vắng trên dòng gộp cụm banner**: một dòng ở đó gộp N impression nên "thứ mấy" vô nghĩa — đọc `impressionCount` |
| `tier` | double | — | **MỚI 1.5.5** — BẬC giá sàn trong config của unit đang chiếu (hệ số 1.5 / 3.0 / 5.0), SDK lấy từ sổ theo `adUnitId`. ⚠ Vắng = **không đặt giá sàn**, khác hẳn `0` |
| `floor` | double | — | **MỚI 1.5.5** — GIÁ SÀN THẬT đã đặt của unit đang chiếu (USD/1000 impression), từ sổ theo `adUnitId`. Khác `ecpmFloor`: cái kia là giá sàn mediation báo cho chính request này |
| `adUnitId` | string | — | Ad unit của ad đang chiếu — mediation điền (đường MAX và Admob bridge đã điền). Đây là **khoá** để SDK tra sổ sự thật theo unit, nên thiếu nó là mất luôn `tier`/`floor`/`ecpm` ở dòng này |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |
| `hasClick` | bool | — | Có click hay không (bỏ nếu null; thường có ở log ad mở rộng). |
| `timeShow` | double | — | Thời gian hiển thị (bỏ nếu null; thường có ở log ad mở rộng). |

Ngoài ra ở lớp envelope/log có thể kèm: `typeCount` (int), `adLtv` (double, bỏ nếu null).

### 5.1b. Bổ sung cho `f_sdk_ads_data` *(MỚI)*

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `waterfallPosition` | int | — | Tầng waterfall ad này thắng (§D2). |
| `fillLatencyMs` | int | — | *(MỚI)* Từ lúc xin ad tới lúc lên hình (ms) — SDK tự đo (§D2). Server tự trừ timestamp `ad_request`↔event này cũng ra; đây là bản fallback. |
| `adUnitId` | string | — | Ad unit id của mediation (trước đây chỉ gửi Firebase/MMP/game-server). |
| `adInstanceName` | string | — | Tên instance của network trong waterfall. |
| `adFormatRaw` | string | — | Chuỗi format THÔ từ mediation, không map. Dùng để phát hiện lệch: mediation map format→AdType bằng switch có nhánh default nên format lạ bị dán nhãn sai. |
| `adViewId` | string | — | Lần xem ad chứa impression này — nối với `f_sdk_ad_request_data`. Banner tự refresh: nhiều impression cùng một `adViewId` (một banner instance). Vắng mặt nếu đường load nằm ngoài SDK. |

### 5.2. `f_sdk_level_data` — Level

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `status` | enum string | ✓ | `Fail`, `Pass`, `Skip`, `Start`, `HeartBeat`. |
| `currentLevel` | int | ✓ | Level hiện tại. |
| `difficulty` | string | ✓ | Độ khó. |
| `playTurnId` | string | ✓ | Id của lượt chơi. |
| `duration` | long (giây) | — | Thời lượng lượt chơi. **Vắng khi game không truyền** (ký loader 12/11, `Request-Loader-AbsentNotZero.md`) — bản trước gửi `0`, lẫn với lượt thật dưới 1 giây. Giá trị truyền tường minh, kể cả `0`, vẫn gửi nguyên. Số âm ⇒ vắng. |
| `levelProgress` | int | ✓ | % hoàn thành level (0–100). `Pass`→100, `Start`→0. |
| `comeBackAfterFirstPass` | bool | ✓ | Có quay lại chơi sau khi đã pass lần đầu hay không. |
| `failCount` | int | ✓ | Số lần fail (ở lớp log). |
| `playCount` | int | ✓ | Số lần chơi (ở lớp log). |
| `currentLevelId` | string | — | **Danh tính BẢN THIẾT KẾ màn** — băm của `data + param`, do CLIENT tính. Đổi layout **hoặc** đổi tuning là id mới. Bỏ nếu null. |
| `levelDataId` | string | — | *(MỚI 19/10)* Danh tính của riêng **DATA** màn (không gồm param tuning) — **băm do GAME SERVER đặt**, client echo nguyên văn. Hai việc nó làm mà `currentLevelId` không: (1) kho **join thẳng sang catalog level của game backend** — trước đó hai hệ mang hai id khác nhau cho cùng một màn; (2) so hai id là biết **đổi layout** hay **đổi tuning**. Vắng mặt = client chưa có id của server (ván không đi qua bản tin level-ready). |
| `failReason` | enum string | — | CHỈ trên `Fail`: `OutOfMoves` / `OutOfTime` / `Died` / `Quit` (bỏ nếu null — hợp đồng §D). |
| `levelLabels` | object | — | Nhãn của BẢN THIẾT KẾ màn (§D11), kèm hai key đăng ký `moves_limit`/`time_limit_sec`. Bỏ nếu màn không có nhãn. |
| `movesUsed` | int | — | Số nước đi đã dùng (bỏ nếu null). |
| `score` | int | — | Điểm (bỏ nếu null). |
| `wave` | int | — | Wave (bỏ nếu null). |
| `boostersUsed` | object<string,int> | — | Map booster→số lượng dùng trong màn (bỏ nếu null). |
| `preBoostersUsed` | object<string,int> | — | Map pre-booster→số lượng (chỉ ở log Start; bỏ nếu null). |

> ⚠ **Vì sao KHÔNG có `levelData` / `levelParam` trên dòng log** (chốt owner 19/10): đó là cùng một
> chuỗi byte lặp lại trên mọi ván của mọi user — `levelData` cỡ 1–3 KB, nhân với 50–150 dòng level
> mỗi user mỗi ngày là 50–450 KB/user/ngày của **dữ liệu trùng nhau**. Nội dung nằm ở **catalog**
> (game backend đã có đủ data + param vì chính nó phát xuống), dòng log chỉ mang **ID** để join.

> Các giá trị booster âm sẽ bị loại khỏi map trước khi gửi.

> **`winStreak`/`loseStreak` đã NGHỈ HƯU (bỏ 14/10, chốt owner)** — hai cột **ngừng có dữ liệu**
> từ 1.5.0; dữ liệu cũ là era, giữ để tra chứ đừng JOIN lẫn với sau. Ba lý do:
>
> 1. **Suy lại được từ chính stream này**: chuỗi Pass/Fail là window function trên `f_sdk_level_data`
>    theo user × thời gian. Cột client chỉ thắng ở một điểm — nó chịu được MẤT LOG (dòng mất thì
>    chuỗi trên dòng sau vẫn đúng, còn suy từ stream thì lệch) — và mức mất log hiện tại không đủ
>    để trả giá hai cột trên stream dày nhất hệ.
> 2. **Không phủ được ván bỏ ngang**, và mọi cách phủ đều sai: "bỏ = thua" là CHÍNH SÁCH per-game
>    chứ không phải bằng chứng. Thêm nữa, drop do app bị kill **không phân biệt được** với user
>    chủ động bỏ, nên con số làm ra chỉ là "ván không có kết luận". Ai cần chuỗi tính cả drop thì
>    derive phía server: turn không terminal nhìn thấy ngay trong stream (`Start` không có
>    `Pass`/`Fail` cùng `play_turn_id`).
> 3. **Không ai dùng**: rà toàn bộ `Assets` 14/10 — không module nào đọc `Level.WinStreak`, không
>    chỗ nào gọi `ResetWinStreak`; module feature streak (`Events/WinStreak`) tự giữ `streak` lấy
>    từ server. Phía kho cũng xác nhận không chart nào dùng.
>
> **Chuỗi TÍNH NĂNG của game** (first-attempt-only, drop cũng cắt, có milestone…) là STATE của
> game: báo bằng `Label.User("streak_status", n)` + funnel milestone, hoặc key riêng có tiền tố
> trên log level — mỗi game một định nghĩa thì không có cột chung nào đúng được cả.
>
> ⚠ Hệ quả phụ, có lợi: tên `win_streak` / `lose_streak` **không còn bị cấm** ở kênh key mở
> (`Common.Set`, `Label.*`) — sổ cấm gom bằng reflection từ field của payload nên cột đi thì cấm
> cũng đi. Game dùng lại tên đó cho chuỗi của mình được, nhưng vẫn nên theo luật tiền tố.

> **`elo` đã RỜI event này** (1.3.0): nó tả NGƯỜI CHƠI nên đi bằng `f_sdk_user_label` key `elo`,
> gửi khi đổi — xem §D11. Cột elo cũ ngừng có dữ liệu từ bản này (chủ đích, không phải hỏng).

### 5.3. `f_sdk_in_app_data` — IAP (mua hàng)

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `productId` | string | ✓ | Id sản phẩm. |
| `where` | string | ✓ | Vị trí kích hoạt mua. |
| `iapWhen` | string | ✓ | Thời điểm/ngữ cảnh mua. |
| `isoCurrencyCode` | string | ✓ | Mã tiền tệ ISO. |
| `transactionId` | string | ✓ | Mã giao dịch. |
| `localizedPrice` | decimal | ✓ | Giá nội địa hóa (≥ 0). |
| `purchaseToken` | string | — | Token mua (bỏ nếu null). |
| `purchaseMethod` | string | — | Phương thức mua (bỏ nếu null). |
| `networkType` | string enum | — | *(MỚI)* Loại kết nối lúc mua — SDK tự đo (kế thừa từ họ param lượt mua). |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |
| `purchaseAttemptId` | string | — | *(MỚI)* Lượt mua (phễu checkout) dẫn tới giao dịch này — nối với `f_sdk_iap_start_purchase_data`. Vắng mặt khi giao dịch hoàn tất ở phiên khác (vd Google PENDING). |
| `offerImpressionId` | string | — | *(MỚI)* Lần hiển thị offer dẫn tới giao dịch này — SDK đóng dấu khi `productId` khớp `offerProductId` của offer đang hiển thị. Không quy được thì vắng mặt. |

### 5.4. `f_sdk_iap_offer_data` — Hiển thị/Tương tác offer IAP

> **Khi nào bắn:** MỘT log cho cả lượt hiển thị, bắn lúc offer **đóng** (không log per-frame).
> Kệ shop nhiều offer = một log mang mảng `offerProductId`.
> Các key `offerImpressionId` / `impressionDuration` / `isClicked` / `isPurchased` do SDK tự điền.
>
> `offerImpressionId` *(MỚI)*: id của lần hiển thị này, được đóng dấu lên `f_sdk_in_app_data`
> khi người chơi mua đúng sản phẩm trong `offerProductId` → đo conversion offer→purchase thật.
> `isPurchased` chỉ có giá trị khi game khai `offerProductId`; không khai thì key vắng mặt.
>
> `offerSurfaceType` *(MỚI)*: `popup` / `embedded_card` / `shop` — **bắt buộc tách kiểu thì chỉ số
> mới có nghĩa**: cùng event này đang trộn card nằm lì (49 lần/user/ngày, CTR 0.03%), popup chủ
> đích (CTR 0.3–5%) và kiểu chỉ-log-khi-bấm (CTR 100%). Không khai thì key vắng mặt.
>
> ⚠ **Đo conversion phải join theo `offerImpressionId`, KHÔNG dựa vào `isPurchased`**: giao dịch
> có thể hoàn tất SAU khi log impression đã gửi (app pause lúc mở store dialog, hoặc cửa sổ ân hạn
> sau khi offer đóng) — khi đó log mua vẫn mang đúng id nhưng `isPurchased` của log impression đã
> đi rồi nên là `false`/`null`. Khoá `offerImpressionId` nằm trên cả hai log nên join được từ cả
> hai phía.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `offerId` | string | ✓ | Id offer. |
| `offerCategory` | string | ✓ | Phân loại offer. |
| `triggerType` | enum string | ✓ | `UserInitiated`, `SystemTriggered`, `Contextual`, `ExternalLink`. |
| `offerLayout` | string | ✓ | Kiểu layout hiển thị. |
| `discountRate` | float | ✓ | Tỷ lệ giảm giá dạng thập phân (0.0–1.0). |
| `placementScene` | string | ✓ | Màn hình xuất hiện. |
| `triggerEvent` | string | ✓ | Điều kiện/thời điểm trigger. |
| `isClicked` | bool | ✓ | Có click vào offer hay không. |
| `impressionDuration` | long (giây) | ✓ | Thời gian offer hiển thị. |
| `offerProductId` | array<string> | — | Danh sách product id được offer (bỏ nếu null). |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |

### 5.5. `f_sdk_resource_data` — Dòng tài nguyên (currency)

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `exchangeId` | string | — | *(MỚI)* Định danh MỘT giao dịch (§D10): mọi vế của cùng một lần đổi mang chung giá trị này (`Sink` 500 gold + `Source` 3 booster). Vắng mặt ở dòng tài nguyên không thuộc giao dịch nào. SDK sinh khi game gọi `FalconBigDataController.Resource.OnExchange(sinks, sources)`; game tự set (id giao dịch của server) cũng được. |
| `flowType` | enum string | ✓ | `Source` (nhận vào) hoặc `Sink` (tiêu ra). |
| `itemType` | string | ✓ | Loại item. |
| `currency` | string | ✓ | Loại tiền/tài nguyên. |
| `itemId` | string | ✓ | Id item. |
| `resourceWhere` | string | ✓ | VỊ TRÍ phát sinh — màn/panel nào. Vocab riêng của từng game. |
| `resourceWhen` | string | ✓ | NGUYÊN NHÂN phát sinh — vì sao tài nguyên chảy. Vocab fleet, xem dưới. |
| `amount` | long | ✓ | Số lượng (≥ 0). |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |
| `valueBefore` | long | — | Số dư trước (bỏ nếu null; có ở log resource mở rộng). |
| `valueAfter` | long | — | Số dư sau (bỏ nếu null; có ở log resource mở rộng). |
| ↳ hạng `unlimited` | | | **SDK bỏ cả `valueBefore` lẫn `valueAfter`** (ký loader 12/11): quyền có thời hạn không có số dư; `amount` = số giây cộng/trừ, mốc hết hạn đi qua nhãn `lives_unlimited_end_ts`. Đi theo hạng của dòng — tên chưa khai hạng thì lọt. |
| `detail` | object | — | Chi tiết bổ sung dạng object (bỏ nếu null). |

> **Vocab `resourceWhen` (§D9, sửa cột đích lần 2 — 2026-08-12)** — hằng số `FResourceWhen`:
> `shop` · `battle_pass` · `daily_quest` · `piggy_bank` · `lucky_wheel` · `daily_bonus` ·
> `level_reward` · `reward` · `gift` · tiền tố `event_`. Tên feature trùng với `funnelName` tương
> ứng để join được hai mặt (tiến độ ↔ ví thưởng).
> Trong SDK này `*When` nghĩa là **ngữ cảnh KÍCH HOẠT** chứ không phải mốc đồng hồ — cùng khuôn
> `adWhen = "level_fail"` ("ad nổ VÌ thua màn"). Nhóm này từng bị xếp vào `itemType` (tả loại vật
> phẩm, đang có `release_cache`/`unlimited_time` của game) rồi `resourceWhere` (tả màn/panel);
> đúng chỗ là `resourceWhen` vì nó trả lời VÌ SAO.
> ⚠ Đừng đọc `playTurnId`/`adViewId` mà SDK đóng tự động như nguyên nhân — chúng chỉ nói ĐỒNG THỜI.
> `itemType` giữ nghĩa "loại vật phẩm/giao dịch" và mang vocab riêng của từng game, SDK chỉ khai
> `FResourceWhen.Iap` (dời từ itemType 18/08 — loader đọc 2 era cho `buy_count_by_class`).
> Vocab `placement` của log mua (`where` trên `f_sdk_in_app_data` / `f_sdk_iap_start_purchase_data`)
> dùng hằng số `FIapPlacement`: `shop` · `battle_pass` · `piggy_bank` · `event_shop` ·
> `starter_pack` · `remove_ads`.

> **Nghĩa của `elo`**: điểm trình độ NGƯỜI CHƠI. Độ khó của MÀN không có key riêng và client
> không gửi — nó là hàm của kết quả **cả fleet**, server giải ngược từ tỉ lệ pass quan sát được:
> `E = 1 / (1 + 10^((R_level − R_user)/400))`. Đầu vào đã có sẵn trong log: `currentLevelId`
> (danh tính bản thiết kế — đổi tune là id mới nên không trộn hai phiên bản màn), `status`
> Pass/Fail, `playCount`/`failCount`, và `elo` của người chơi từ `f_sdk_user_label` khi game có.
> Bản thân `elo` của người chơi cũng là thứ server tổng hợp được từ chính những tín hiệu trên;
> client chỉ gửi khi game đã có sẵn một hệ elo (hoặc nhận lại giá trị server đã tính).

### 5.6. `f_sdk_funnel_data` — Funnel

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `funnelName` | string | ✓ | Tên funnel. |
| `action` | string | ✓ | Hành động/bước. |
| `priority` | int | ✓ | Thứ tự ưu tiên/bước (≥ 0). |
| `cycleId` | string | — | *(MỚI)* Ranh giới chu kỳ của funnel lặp ("season_5", "2026-08-12"…) — server tách mùa trực tiếp thay vì suy từ `funnelDay`. Vắng mặt nếu game không truyền (§H4). Với shape client `OncePerCycle`, SDK lọc trùng theo (cycleId, action, priority) ngay tại client. |
| `cycleIndex` | int | — | *(MỚI 1.3.8)* Số thứ tự chu kỳ (mùa/giải thứ mấy) — trục sort bằng SỐ, khỏi parse tên cycleId ("season_9" > "season_10" khi sort chuỗi). Vắng mặt nếu game không truyền. |
| `cycleStartTs` / `cycleEndTs` | long | — | *(MỚI 1.3.8)* Mốc mở/đóng chu kỳ, epoch millis UTC, từ CONFIG event (đúng số UI countdown vẽ). Countdown-tại-log = `cycleEndTs − ts` — cắt nhóm theo countdown không cần bảng lịch vận hành. ⚠ Chu kỳ ĐỘNG: đọc theo giá-trị-tại-thời-điểm-log (extend giữa mùa thì các dòng sau mang giá trị mới — đó là lịch sử thật, không phải lỗi). Client giữ RAW kể cả khi end ≤ start (bug config phải thấy được để truy). |
| `funnelShape` | string | — | *(MỚI 1.3.8, SDK tự đóng)* Hình dạng van ĐANG ÁP tại client lúc log: `once_ordered` / `once_per_cycle` / `repeatable`. Vai DQ: catalog giữ shape CHUẨN theo thiết kế, field này chở shape THỰC TẾ — lệch nhau = client quên Register (funnel rơi về luật ftue, bước bị veto trắng phía client). Không phải hằng số nên mới đáng chở: nó thay đổi theo build/trạng thái đăng ký của client. |
| `funnelDay` | string | — | Ngày funnel (nếu có). |
| `playSecSinceEnter` | long | — | *(MỚI 1.5.0, SDK tự đóng)* **Giờ CHƠI** (giây) từ mốc **vào phễu** của chu kỳ này tới bước này. Mốc vào phễu = bước **đầu tiên hợp lệ** của funnel trong chu kỳ (KHÔNG phải bước `join` — funnel mới phần lớn chỉ có `eligible`/`open`); bước đầu chu kỳ mang **0**. `cycleId` đổi là mốc reset. |
| `playSecSincePrevStep` | long | — | *(MỚI 1.5.0, SDK tự đóng)* **Giờ CHƠI** (giây) từ **bước hợp lệ gần nhất** của funnel này trong cùng chu kỳ. Bước đầu chu kỳ: **vắng mặt**. |
| `levelsSinceEnter` | long | — | *(MỚI 1.5.0, SDK tự đóng)* Số **level mới vượt qua** từ mốc vào phễu tới bước này, đọc từ `MaxPassedLevel` của SDK. Bước đầu chu kỳ = **0**. |
| `levelsSincePrevStep` | long | — | *(MỚI 1.5.0, SDK tự đóng)* Số **level mới vượt qua** từ bước hợp lệ gần nhất. Bước đầu chu kỳ: **vắng mặt**. |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |

> Funnel kiểu cũ có thể kèm thêm các key động (extra meta) được trải phẳng vào cùng cấp.

**Vocab đăng ký (§D6 + §D9)** — hằng số `FFunnelName` / `FFunnelAction` trong SDK:

| `funnelName` | Hình dạng | `action` chuẩn |
|---|---|---|
| `ftue` | một lần, đúng thứ tự | `scene_enter` → `tutorial_start` → `tutorial_complete` |
| `battle_pass`, `daily_quest`, `piggy_bank`, `lucky_wheel`, `event_<type>` | lặp lại, được nhảy cóc mốc | `join` / `milestone` / `claim` / `complete` (`priority` = số tier/mốc) |

- Tên ngoài danh sách vẫn gửi được nhưng **phải đăng ký với loader trước**.
- `funnelDay` của funnel lặp lại = ngày `join` **gần nhất** (không phải lần đầu đời máy) —
  nếu không thì `event_duration` của battle pass mùa 5 tính từ mùa 1, thành số vô nghĩa.
  Chưa từng gửi `join` thì lấy ngày hiện tại.

**Hai mốc giờ-chơi (1.5.0)** — luật đọc:

- Đồng hồ là **thời gian chơi tích luỹ** (`TotalPlayTime`): monotonic, **chỉ chạy khi app ở
  foreground**, có persist. **Không phải wall-clock** — nên "8 tiếng ngủ giữa hai bước" không vào
  số, và user chỉnh giờ máy cũng không làm phồng (án 12/08).
- **Vắng mặt = KHÔNG BIẾT, không phải 0.** Hai ca vắng: bước đầu chu kỳ (`playSecSincePrevStep`),
  và sổ mốc lệch sổ phiên nên delta ra số âm (cả hai field, kèm warning ở client).
- `playSecSincePrevStep` tính theo **thời điểm log**, không theo thang `priority`: shape
  `once_per_cycle` cho phép **nhảy cóc mốc** (mua vượt tier là hợp lệ), neo theo "priority ngay
  dưới" thì đúng ca nhảy cóc lại trắng số.
- **Bước bị client veto không dịch mốc** — bước sau vẫn đo từ bước đã thật sự lên wire.
- ⚠ Mốc vào phễu khác nhau giữa các funnel (funnel có `join` thì bước đầu thường là `eligible`),
  nhưng **nhất quán trong cùng một `funnelName`** — mà mọi phép đọc đều cắt theo `funnelName`
  trước. Đừng trộn hai funnel khác nhau vào một con số trung bình.

**Hai mốc KHOẢNG CÁCH LEVEL (cùng chuyến)** — đi cặp với hai mốc giờ chơi vì chúng **bù lỗ cho
nhau**:

| Thước đo | Đếm cả | Đứng yên khi |
|---|---|---|
| Giờ chơi | Lúc app mở mà user không chơi (ngồi lobby) | — |
| Số level | — | User cày lại một màn (thua 10 lần vẫn là 0 level) |

- Nguồn là `MaxPassedLevel` **của SDK**, không phải `currentLevel` game truyền vào `OnStep` (field
  đó optional nên cột sẽ rỗng lỗ chỗ). Đếm level **vượt qua**, không đếm số ván chơi.
- Vắng mặt = KHÔNG BIẾT: client không đọc được tiến độ, hoặc delta ra âm (user xoá data). Cùng
  luật với cặp giờ chơi — **đừng COALESCE về 0**.


### 5.6b. `f_sdk_ui_exposure` — Exposure UI, MỘT CỤM MỘT DÒNG *(ký 09/11, BigData 1.7.1)*

Máy đếm bề mặt UI (icon/popup/banner) hiện ra, được bấm, bị đóng. Grain per-lần-hiện CẤM lên wire (volume
không chặn trên) — client gộp theo cụm, flush lúc app pause (và khi đủ 32 cụm).

Khoá cụm = `surfaceId` × `trigger` × 4 field `liveOps*` × 3 field `feature*`. **Hành vi KHÔNG ở trong khoá**: mỗi
hành vi là một cột đếm.

| Field | Kiểu | Ghi chú |
|---|---|---|
| `surfaceId` | string | Bề mặt UI — luôn có. Hằng số dùng chung ở `FUiSurface` (`revive_offer`), còn lại là vocab của game |
| `impressionCount` | int | Số lần hiện |
| `clickCount` | int | Số lần làm hành động chính |
| `dismissCount` | int | Số lần đóng mà KHÔNG làm hành động chính (không gồm app tắt ngang) |
| `adNotReadyCount` | int | Số lần hiện mà QC chưa sẵn — **tập con** của `impressionCount` |
| `trigger` | string | VÌ SAO bề mặt hiện — vocab MỞ của game, trong khoá |
| `liveOps*` · `feature*` | | Ngữ cảnh event live-op / tính năng, trong khoá |
| extras | flatten | Extras của **lần ghi đầu** vào cụm (hiện, bấm hay đóng) |

- **Bốn cột đếm luôn lên dây**, kể cả khi bằng 0 — trong một cụm, 0 là số thật của cửa sổ xả.
- **Tỉ lệ chỉ đúng khi cộng qua khoảng**: CTR = `sum(clickCount) / sum(impressionCount)`. Tỉ lệ trên từng dòng là SAI
  — hiện và bấm có thể rơi vào hai cửa sổ xả, nên dòng `impressionCount = 0`, `clickCount > 0` là bình thường.
- **Bất biến trên TỪNG dòng**: `adNotReadyCount ≤ impressionCount` (một lần gọi `OnImpressionAdNotReady` cộng cả hai cột
  trong cùng một lần ghi).
- **Không có cột level.** Tiến độ đọc `maxPassedLevel` chung của mọi dòng = tiến độ **lúc xả**, có thể cao hơn lúc hiện.
- API: `Ui.OnImpression` · `Ui.OnClicked` · `Ui.OnDismissed` · `Ui.OnImpressionAdNotReady` — mỗi hàm một sự kiện cố
  định, không có tham số hành vi.

KHÁC bước funnel `open`: open là MỐC phễu (đã xem/chưa), đây là MÁY ĐẾM (bao nhiêu lần).

**Tiền thân** `f_sdk_ui_impression` (1.3.9 → 1.7.0, đóng băng): một dòng một hành vi (`uiAction` + `count`); 1.7.0 thêm
`currentLevel` (= `MaxPassedLevel`) + `trigger` vào khoá và hai giá trị `impression_ad_not_ready` (phát kèm `impression`)
· `flush_lost` (dòng không `surfaceId`). Đọc bằng `sum(count)`, không cộng ngang `uiAction`. Đổi hình dạng thì đổi TÊN
event, không dựa vào `sdk_version` (luật chung kho ghi 09/11).

### 5.6c. `f_sdk_ui_flush_lost` — Phiên mất cụm UI chưa xả *(ký 09/11, BigData 1.7.1)*

Lần mở app sau một lần bị tắt ngang mà còn cụm UI chưa xả ⇒ **một dòng**. **Không field riêng nào.**
⚠ Ở event này **`count(*)` là ĐÚNG** — một dòng là một phiên mất (ngược luật của cụm UI). Là **CHẶN DƯỚI**: dấu ghi
đĩa có thể trượt.

### 5.6d. `f_sdk_level_action_revive` — Một lần hồi sinh ĐÃ DÙNG *(ký 09/11, BigData 1.7.1)*

| Field | Kiểu | Ghi chú |
|---|---|---|
| `playTurnId` | string | Lượt đang mở — SDK tự gắn. Vắng = game báo Fail TRƯỚC khi mời hồi sinh (phạm luật tích hợp 04/09); SDK không đoán id lượt đã đóng |
| `progressPct` | float `[0,100]` | % tiến độ ván lúc hồi sinh. Ngoài khoảng ⇒ vắng, không kẹp |
| `payMethod` | string | `ad` · `currency` · `inventory` · `free`. Không biết thì vắng |
| `reviveType` | string | *(ký 10/11)* Loại hồi sinh — vocab mở của game (`extra_moves`, `extra_time`). Không field số lượng: cần tách mức thì đưa vào tên loại (`extra_moves_5`) |
| extras | flatten | |

- Có dòng là đã dùng: không `offered` / `accepted` / `action` / `boosterId`. Hồi sinh nhiều lần trong một ván = nhiều dòng.
- Lời mời / nhận / từ chối: cụm UI trên `surfaceId = revive_offer` — tỉ lệ nhận = `sum(clickCount) / sum(impressionCount)`.
- Từ chối ở đoạn nào: `levelProgress` của dòng `Fail` cùng lượt. Tiền tiêu: dòng tài nguyên `resourceWhen = revive`.
- API: `Level.OnRevive(progressPct, payMethod, …)`.
- Họ `f_sdk_level_action_*`: xem §5.6f cho booster.
- **Tiền thân** `f_sdk_level_action` (CHỈ 1.7.0, đã ngừng — kho chưa có dòng nào): một dòng gói lời mời + quyết định +
  cách trả. `Level.OnAction` cũ còn build được (lỗi thời): hồi sinh đã nhận ⇒ một dòng revive; từ chối và booster ⇒
  không dòng nào, kèm cảnh báo.

### 5.6e. `f_sdk_ftue_*` — FTUE người chơi mới *(ký 10/11, BigData 1.7.1, thay funnel `ftue` của §D6)*

| Event | Field riêng | Bắn khi |
|---|---|---|
| `f_sdk_ftue_first_screen` | — | Màn hình ĐẦU TIÊN tương tác được (home nếu có, màn chơi nếu vào thẳng; không phải loading) |
| `f_sdk_ftue_tutorial_start` | `tutorialId` | Tutorial onboarding bắt đầu |
| `f_sdk_ftue_tutorial_step` | `tutorialId` · `stepIndex` (≥ 1) · `stepName` | Một bước HIỆN RA |
| `f_sdk_ftue_tutorial_complete` | `tutorialId` | Làm hết tutorial |
| `f_sdk_ftue_tutorial_skip` | `tutorialId` · `lastStepIndex` | Bấm bỏ qua — SDK điền bước xa nhất đã hiện |

- Mỗi mốc **một lần cho mỗi lần cài** (bước: một lần cho mỗi `tutorialId` × `stepIndex`). Hai dòng cùng mốc (cài lại / hai máy) ⇒ lấy
  dòng đầu.
- SDK **không ép thứ tự**. `complete` / `skip` loại trừ nhau, kết cục đầu thắng.
- `tutorialId` = phiên bản tutorial do game đặt, SDK nhớ từ mốc start (qua cả lần mở app sau). Thiếu ⇒ vắng, SDK không đoán.
- `stepName` là vocab của từng game — không so chéo game.
- Không cột thời gian: `totalPlayTime` + `client_sent_time` có sẵn trên mọi dòng. Thứ tự mốc theo `client_sent_time`; "lên kho từ khi nào"
  theo `server_receive_time`.
- API: `Ftue.OnFirstScreen` · `OnTutorialStart(tutorialId)` · `OnTutorialStep(stepIndex, stepName)` · `OnTutorialComplete` ·
  `OnTutorialSkip`. Chỉ cho tutorial ONBOARDING.
- **Thời kỳ cũ** (funnel `ftue` và các funnel tutorial tự đặt tên): đọc theo luật funnel, **không map** sang họ mới, **đừng cộng hai nguồn**.

### 5.6f. `f_sdk_level_action_booster` — Một lần dùng booster TRONG ván *(ký 10/11, BigData 1.7.1)*

| Field | Kiểu | Ghi chú |
|---|---|---|
| `playTurnId` | string | Lượt đang mở — SDK tự gắn |
| `boosterId` | string, **bắt buộc** | Theo sổ `Resource.RegisterBooster`. Thiếu ⇒ SDK bỏ dòng; mã lạ ⇒ cảnh báo, vẫn gửi |
| `progressPct` | float `[0,100]` | Ngoài khoảng ⇒ vắng |
| `payMethod` | string | Booster **có từ đâu**: `inventory` · `currency` (mua tại chỗ) · `ad` · `free` |

- Booster chọn TRƯỚC ván ở `preBoostersUsed` của dòng Start — event này không chở.
- 🔴 **Một câu hỏi, một nguồn**: *dùng bao nhiêu lần* ⇒ `boostersUsed` của dòng level; *dùng ở đoạn nào* / *có từ đâu* ⇒ event này;
  *tốn bao nhiêu* ⇒ dòng tài nguyên `resourceWhen = booster_use`. Đừng cộng hai nguồn.
- **`boostersUsed` (định nghĩa 10/11):** số LẦN DÙNG trong ván, KHÔNG gồm booster chọn trước ván. SDK không tính field này — game truyền;
  trước 10/11 hợp đồng chỉ ghi "số lượng dùng trong màn", nên dữ liệu cũ tuỳ game.
- Popup gợi ý dùng booster: cụm UI trên `surfaceId = booster_offer`. API: `Level.OnBooster(boosterId, progressPct, payMethod, …)`.

### 5.7. `f_sdk_event_data` — Event tùy biến

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `eventName` | string | ✓ | Tên event tùy biến. |
| `eventWhere` | string | ✓ | Vị trí. |
| `eventWhen` | string | ✓ | Thời điểm/ngữ cảnh. |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |
| `paramsStr` | string | — | **Tham số tùy biến đã serialize thành chuỗi JSON** (bỏ nếu null). Lưu ý: là string, không phải object. |

### 5.8. `f_sdk_session_data` — Phiên chơi

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `gameMode` | string | ✓ | Chế độ chơi. |
| `sessionTime` | long (giây) | ✓ | Thời lượng phiên. |
| `modeTotalTime` | long | ✓ | Tổng thời gian theo mode (ở lớp log). |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |
| `avgFps` | float | — | *(MỚI §D7)* FPS trung bình của **quãng mà bản tin này khai** (không phải cả phiên) — SDK tự đo. Gộp cả phiên thì cân theo `sessionTime` từng bản tin. |
| `frameDropCount` | int | — | *(MỚI §D7)* Số khung > 100ms trong quãng (ngưỡng cố định phía client). Khoảng > 2s coi là app không render (ads/background/load) nên KHÔNG tính. |
| `memoryWarningCount` | int | — | *(MỚI §D7)* Số lần OS cảnh báo thiếu bộ nhớ (`onTrimMemory` / `didReceiveMemoryWarning`). |

> 3 field perf chỉ có trên bản tin `gameMode = USER_TOTAL_TIME` (bản tin lúc app pause và bản tin
> bảo hiểm định kỳ). Mỗi bản tin mang số liệu của đúng quãng nó khai rồi reset ⇒ cộng lại
> **không đếm đôi**; nhờ có mặt cả trên bản tin định kỳ nên app bị kill cứng vẫn còn số liệu.
> Không đo được (chưa có frame nào) thì cả 3 vắng mặt, không gửi 0 giả.

### 5.9. `f_sdk_property_data` — Thuộc tính user

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `pName` | string | ✓ | Tên thuộc tính. |
| `pValue` | string | ✓ | Giá trị thuộc tính. |
| `priority` | int | ✓ | Ưu tiên (≥ 0). |
| `currentLevel` | int | — | Level hiện tại (bỏ nếu null). |

### 5.10. `f_sdk_retention_data` — Retention

> **Khi nào bắn:** log này được gửi lên **khi retention của người chơi thay đổi** — tức khi người chơi bước sang một ngày active mới (mốc retention day mới). Không bắn lặp lại trong cùng một ngày.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `localDate` | string | ✓ | Ngày local của mốc retention, định dạng `yyyy-MM-dd HH:mm:sszzz`. |

### 5.11. `f_sdk_ad_request_data` — Xin ad từ mediation *(MỚI — chờ loader xác nhận event id)*

> **Khi nào bắn:** ngay trước mỗi lần gọi load ad của mediation (mọi format, kể cả retry).
> Điểm vào phễu fill — request KHÔNG có impression mang cùng `adViewId` chính là view fill-fail,
> nên không cần event fail riêng.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `adViewId` | string | ✓ | Id của cả lần xem ad (request → show → impression → close), SDK sinh. Xuất hiện lại trên `f_sdk_ads_data` nếu ad lên hình. |
| `type` | string enum | ✓ | Format ad (`Banner`/`Interstitial`/`Reward`/`AppOpen`/...). |
| `adWhere` | string | — | Vị trí/ngữ cảnh nếu biết lúc load (bỏ nếu preload). |
| `adWhen` | string | — | *(MỚI)* Ngữ cảnh kích hoạt nếu biết lúc load — cùng tên với `adWhen` của `f_sdk_ads_data` để join. |
| `adMediation` | string | — | Mediation đang dùng (bỏ nếu null). |
| `networkType` | string enum | — | *(MỚI)* `wifi`/`cellular`/`none` lúc xin ad — SDK tự đo, giải thích fill-fail. |

> **MỚI 1.5.5:** `sessionAdRequestIndex` (int) — lần **xin ad** thứ mấy của format này **trong phiên**
> (đếm từ 1, SDK đếm). Mẫu số theo phiên cho phễu fill, và là trục mà eCPM/tỷ lệ fill đổi theo.

### 5.11b. `f_sdk_ad_show_data` — Gọi hiển thị ad *(MỚI — ngoài hợp đồng §B, cần loader xác nhận)*

> **Khi nào bắn:** mediation gọi hiển thị ad (inter/rewarded; banner không có mốc này).
> Show mà KHÔNG có impression mang cùng `adViewId` = hiển thị hỏng (display failure).

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `adViewId` | string | ✓ | Cùng id với `f_sdk_ad_request_data`. |
| `type` | string enum | ✓ | Format ad. |
| `adWhere` | string | — | Placement (lúc show thì đã biết, khác lúc request). SDK điền từ cache của lần xem nếu mốc này bỏ trống. |
| `adWhen` | string | — | *(MỚI)* Ngữ cảnh kích hoạt — như trên. |
| `adMediation` | string | — | Mediation đang dùng. SDK điền từ mốc request nếu bỏ trống (bất biến trong một lần xem). |
| `requestToShowMs` | int | — | Chờ bao lâu từ lúc xin ad tới lúc hiển thị — SDK tự đo. Bỏ nếu show không đi từ request nào. |

### 5.11c. `f_sdk_ad_close_data` — Ad đóng lại *(MỚI — ngoài hợp đồng §B, cần loader xác nhận)*

> **Khi nào bắn:** ad đóng, người chơi quay về game — mốc cuối của vòng đời một lần xem ad.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `adViewId` | string | ✓ | Cùng id với các mốc trước. |
| `type` | string enum | ✓ | Format ad. |
| `adWhere` | string | — | Placement. SDK điền từ cache của lần xem nếu bỏ trống. |
| `adWhen` | string | — | *(MỚI)* Ngữ cảnh kích hoạt — SDK điền từ cache nếu bỏ trống. |
| `adMediation` | string | — | Mediation đang dùng — SDK điền từ mốc request nếu bỏ trống. |
| `shownDurationSec` | int | — | Ad nằm trên màn hình bao lâu — SDK tự đo từ mốc show. Bỏ nếu chưa từng show. |
| `hasClick` | bool | — | Có bấm vào ad không (mediation điền). |
| `adCompleted` | bool | — | Xem hết hay bỏ giữa chừng — chính là `ad_completed` của §D2, đặt ở đây vì chỉ lúc đóng mới biết. |

> ⚠ **ERA — `ad_view_id` của dòng close trước 1.5.1 KHÔNG đáng tin** (án "close mồ côi",
> loader đo trên coin.sort 13/10). Mediation **preload** ad kế tiếp ngay sau khi ad hiện tại lên
> hình, mà SDK giữ MỘT slot view cho mỗi format → lúc user bấm đóng thì slot đã là view mới, nên
> close bị đóng dấu id của **một request chưa bao giờ hiện**. Tỉ lệ: **46% reward · 14% inter**;
> khoảng cách p50 từ impression tới close mồ côi đúng bằng thời gian xem (52s / 33s). Impression
> **không** thừa — đối chiếu AppLovin S2S lệch −0,6% / −1,6%, tức SDK còn ít hơn nhà mạng.
>
> Ba hệ quả trên dữ liệu cũ, đọc theo đó:
> 1. **Dòng close mồ côi nhận ra được**: `ad_view_id` không có impression nào, **và**
>    `shown_duration_sec` **vắng mặt** (view preload chưa qua mốc show nên SDK không bịa số 0).
>    Nên thời lượng xem của nhóm đó là *thiếu*, không phải *sai*.
> 2. **`has_click` của nhóm đó không tin được** → CTR tính từ dòng close bị lệch (cú click ghi lên
>    view này, dòng close đọc view kia).
> 3. **`ad_view_id` trên `f_sdk_resource_data` cũng dính**: game đọc `Ad.CurrentViewId(type)` lúc
>    phát thưởng rewarded — tức ngay sau close, sau khi đã preload — nên phần thưởng bị nối vào một
>    lần xem chưa từng xảy ra.
>
> **Đã sửa ở 1.5.1**: SDK tách sổ "view ĐANG CHIẾU" (đã show, chưa close) khỏi slot; close /
> click / impression / `CurrentViewId` đều đọc view đang chiếu, chỉ `OnRequested` ghi vào slot.
> Banner giữ nguyên ngữ nghĩa id-instance. Cắt era bằng `sdk_version`.

> **MỚI 1.5.5 trên dòng close:** `adUnitId` — ad unit VỪA CHIẾU. Mediation điền thì thắng; bỏ trống
> thì SDK lấy unit đã ghi ở mốc **impression** của đúng view đó (chỉ một unit được chiếu, và nó vừa
> báo impression → đây là sự thật, không phải suy đoán). Tiền/giá của lần xem đó đọc ở dòng
> impression cùng `adViewId` — dòng close **không** chở lại eCPM/floor để khỏi đếm đôi.
>
> ⚠ Dòng **request / show / show_attempt** KHÔNG có các field mức-unit (`adUnitId`, `adNetwork`,
> `tier`, `floor`, `ecpm`): lúc đó nhiều unit đang load song song nên SDK không biết cái nào sẽ
> chiếu, và dán "unit của lần load cuối" vào là bịa. `AdShowParam` có `adUnitId` **tuỳ chọn** để
> mediation tự điền khi biết.

### 5.11d. `f_sdk_ad_show_attempt` — Game MUỐN chiếu ad *(MỚI 23/09 — loader đã duyệt hình dạng, chờ seed rule)*

> **Khi nào bắn:** mediation vào hàm show, **TRƯỚC** nhánh kiểm tra kho (`IsReady`). Bắn ở CẢ hai
> nhánh (có ad / không có ad), chỉ khác `adAvailable`.
>
> **Vì sao cần:** từ bản mediation có van nạp sẵn, một `f_sdk_ad_request_data` phục vụ NHIỀU lần
> chiếu → `request` là "một đợt nạp hàng", không còn là "một lần user cần ad". Trước mốc này, ca
> "muốn chiếu mà kho rỗng" không có dòng nào trên wire.
>
> **Bất biến kiểm được:** số dòng event này ≥ số `f_sdk_ad_show_data`.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `type` | enum string | ✓ | Format ad |
| `adAvailable` | bool | ✓ | Lúc game hỏi thì kho có ad sẵn không — mediation truyền, SDK không đoán hộ |
| `adWhere` | string | — | Chỗ game định chiếu — có cả ở ca không có ad, nên đọc được "chỗ nào hay hụt ad" |
| `adWhen` | string | — | Ngữ cảnh kích hoạt |
| `adMediation` | string | — | Mediation đang dùng |
| `adViewId` | string | — | View sắp chiếu — **chỉ có khi `adAvailable = true`**; kho rỗng thì vắng. SDK tự đọc từ cache |

```
view_fill_rate = count(adAvailable = true) / count(*)     per adWhere / type
```

### 5.11d-2. `f_sdk_ad_show_skipped` — Luật của game chặn một cơ hội chiếu ad *(MỚI — ký loader 12/11, từ BigData 1.7.4; sửa 13/11: `no_ads` + 6 field ad đang chờ, từ BigData 1.7.5)*

> **Khi nào bắn:** game đến một **điểm chiếu** (hết màn, về Home, quay lại app) mà luật của chính
> game bảo đừng chiếu: cooldown, user mới, vừa mua, trần ngày/phiên, luật theo nhóm user… Game gọi
> `Ad.OnShowSkipped(type, adWhere, skipReason)` **đúng một lần tại điểm đó**, không gọi mỗi lần kiểm
> tra luật. Thư: `docs/loader/Request-Loader-AdShowSkipped.md` mục 1.
>
> **Vì sao event riêng, không thêm field vào `show_attempt`:** event đó có công thức
> `view_fill_rate = count(adAvailable) / count(*)` — nhét dòng bị chặn vào là đổi nghĩa `count(*)`.
>
> **Chỉ format game tự bật:** `Interstitial`, `AppOpen`. Rewarded do người chơi bấm — "không có ad"
> của nó đã ở `show_attempt.adAvailable = false`. Format khác hoặc thiếu `adWhere` ⇒ SDK cảnh báo và
> **không gửi**.
>
> **User đã có quyền No Ads: GỌI với `skipReason = no_ads`** (sửa 13/11, `Request-Loader-AdShowSkipped-NoAds.md`,
> lật luật 12/11). `no_ads` đứng **đầu** thứ tự xét. Ai chặn thì người đó ghi: game tự kiểm No Ads trước khi gọi
> mediation thì game ghi; để mediation chặn thì mediation ghi — không bao giờ cả hai.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `type` | enum string | ✓ | `Interstitial` · `AppOpen` |
| `adWhere` | string | ✓ | Điểm chiếu bị chặn — cùng bộ từ với `adWhere` của `show_attempt` |
| `adWhen` | string | — | Ngữ cảnh kích hoạt |
| `skipReason` | enum string | ✓ | Luật **đầu tiên** chặn, theo thứ tự game xét. Bộ từ đóng: `cooldown` · `level_interval` · `level_gate` · `new_user_grace` · `post_purchase_grace` · `post_ad_grace` · `daily_cap` · `session_cap` · `segment_rule` · `disabled` · `other` (SDK cảnh báo dev, vẫn gửi) · *(13/11)* `no_ads` (đứng đầu thứ tự xét) · `level_cap` (trần N lần trong một level) |
| `adAvailable` | bool | — *(13/11)* | Kho mediation có ad sẵn lúc bị chặn không — cùng nghĩa `show_attempt.adAvailable`. **Vắng = caller không truyền** (dòng 1.7.4), không phải false; SDK cảnh báo dev khi thiếu |
| `adViewId` | string | — *(13/11)* | Lần xem ad đang chờ — SDK tự đọc cache, chỉ khi `adAvailable = true` |
| `adMediation` | string | — *(13/11)* | Caller truyền, thiếu thì SDK lấy của view đang mở |
| `adNetwork` | string | — *(13/11)* | Network của ad đang chờ — mediation trả (`AdReadyInfo`), chỉ khi `adAvailable = true` |
| `adUnitId` | string | — *(13/11)* | Unit giữ ad đang chờ; MultiCall: unit sẽ được chọn — mediation trả, chỉ khi `adAvailable = true` |
| `estimatedEcpm` | double, USD/1000 | — *(13/11)* | Giá dự kiến lúc load — mediation trả, chỉ khi `adAvailable = true`; không dương ⇒ vắng. AdMob trực tiếp không có |

Không có field số đi theo từng lý do (vd giây cooldown còn lại) — đó là field bị field khác điều khiển nghĩa.
Phía kho (loader 12/11): `sub_event` = `type`, `event_where`/`event_when` = `adWhere`/`adWhen`,
`sub_event_2` = `skipReason`. 6 field 13/11 vào `event_extra_props` (snake_case: `ad_available`, `ad_view_id`, `ad_mediation`,
`ad_network`, `ad_unit_id`, `estimated_ecpm`). Dòng skip **không** vào hộp lượt xem QC dù mang `ad_view_id`.

```
cơ hội     = count(show_attempt) + count(show_skipped)          per type / adWhere
tỉ lệ chặn = count(show_skipped) / cơ hội                      per skipReason
tỉ lệ chặn do luật (13/11) = loại skipReason = no_ads ở tử số, và user no_ads ở mẫu số
tiền mất ước lượng        = Σ estimatedEcpm / 1000 trên dòng bị chặn
```

Đọc đúng (13/11): bị chặn **không** tiêu ad ⇒ nhiều dòng có thể cùng `adViewId`, đừng dedup theo id này. App open của
user No Ads luôn `adAvailable = false` (mediation không load) — không phải hụt fill.

⚠ Chỉ đo được game nào gọi, ở điểm chiếu nào — quên gọi thì tỉ lệ chặn thấp giả. Mediation cũng có chỗ
tự chặn không báo (vd cooldown app open trong mediation) — cần ticket riêng.

### 5.11e. `f_sdk_ad_load_stats` — Telemetry load, bản tin GỘP dạng mảng *(MỚI 23/09 — THAY `f_sdk_ad_load_success` + `f_sdk_ad_load_fail`)*

> **Khi nào bắn:** lúc app pause (hoặc khi kho cụm chạm 256 khoá). MỘT dòng cho mỗi
> (`adType` × `adMediation`), chi tiết từng ad unit nằm trong mảng `units[]`.
>
> **Không mang `adViewId`** — bản tin GỘP là telemetry per-attempt của từng unit, không thuộc vòng
> đời một lần xem ad. Nối với vòng đời qua `adUnitId`.
>
> ⚠ **Từ SDK 1.5.5 đây chỉ còn là MỘT trong hai chế độ, và KHÔNG phải mặc định.** Chế độ mặc định là
> `f_sdk_ad_load` (§5.11f) — tách mỗi lần gọi một dòng, CÓ `ad_view_id`. Hai chế độ loại trừ nhau
> theo từng lời gọi: một bản SDK gửi dòng nào thì không gửi dòng kia, nên **đừng cộng hai event**.
>
> Hai event cũ (`f_sdk_ad_load_success`, `f_sdk_ad_load_fail`) **ngừng gửi** từ cùng bản SDK; loader
> xác nhận không mapping/cook/counter/probe nào đọc chúng.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `adType` | enum string | ✓ | Format ad |
| `adMediation` | string | — | Mediation của nhóm unit này — từ vựng `max` · `levelplay` · `admob` từ 1.5.5 |
| `adAlgorithm` | string | — | **MỚI 1.5.5** — thuật toán load của nhóm. **NẰM TRONG KHOÁ NHÓM**: hai thuật toán không gộp một dòng, nếu không thì câu hỏi *"thuật toán nào load tốt hơn"* không trả lời được |
| `units` | array&lt;object&gt; | ✓ | Mỗi phần tử một ad unit — schema dưới. Trần **64 phần tử**; vượt thì CHIA LÔ thành nhiều dòng (không cắt bỏ — mọi số đều cộng được nên tổng giữ nguyên) |

Phần tử của `units[]` (tên khoá viết tắt để gói tin không phình — 32 unit ≈ 2,5 KB):

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `u` | string | ✓ | `adUnitId` |
| `ok` / `fail` | int | ✓ | Số lần load thành công / thất bại đã gộp |
| `okMs` / `failMs` | long | ✓ | Tổng thời gian load của mỗi nhóm — chia `ok`/`fail` ra trung bình |
| `tier` | double | — | **BẬC** giá sàn trong config (hệ số 1.5 / 3.0 / 5.0) — mediation nào cũng biết thật, là trục chính để so bậc nào fill tốt hơn. ⚠ **Unit không đặt giá sàn thì key VẮNG MẶT, không gửi `0`** — vắng-key là thứ duy nhất phân biệt "không đặt sàn" với "đặt sàn 0" |
| `floor` | double | — | **GIÁ SÀN THẬT**, USD trên 1000 lượt hiển thị — **TRUNG BÌNH các lần load trong bản ghi** (từ 1.4.1; cùng khuôn với `okMs`/`failMs`, làm tròn 4 chữ số thập phân). CHỈ mediation biết con số thật mới gửi (AdMob tự đặt sàn từng slot); MAX không biết (giá sàn nằm trên dashboard) nên vắng. ⚠ **Cấm** suy ra bằng cách nhân `tier` với doanh thu đo được — nền nhân là giá ad vừa xem nên cùng bậc ra số khác nhau theo máy/theo ngày (bài học công thức cũ, loader đo 22/09). Vắng ở đây = **không biết**, khác `tier` vắng = **không đặt sàn** |

> **Khoá gộp** (từ 1.4.1) = `adType` × `adMediation` × `u` × `tier` — toàn thứ RỜI RẠC. `floor` là
> số đo nên KHÔNG nằm trong khoá: AdMob tính `floor = revenue × hệ số × 1000` với `revenue` là giá
> của ad vừa load, tức số thực liên tục — để trong khoá thì nhánh AdMob mỗi attempt một dòng, mất
> hẳn tác dụng gộp. Bản 1.4.0 có lỗi này, 1.4.1 sửa.
| `net` | string | — | Network thắng — chỉ có khi `ok > 0` |
| `ecpm` | double | — | **MỚI 1.5.5** — eCPM ước tính **TRUNG BÌNH** của các lần có số (USD/1000 impression, làm tròn 4 chữ số). Lần mediation không báo, hoặc báo ≤ 0, **không vào mẫu** |
| `err` | string | — | Mã lỗi CUỐI của nhóm fail — không làm khoá gộp (message lạ sẽ tách bản ghi vô hạn) |
| `floorType` | string | — | **MỚI 1.5.7** — VAI của giá sàn (`base` · `base_half` · `upgrade_*` · `decay*` · `no_floor`). **NẰM TRONG KHOÁ GỘP**: hai vai không gộp một bản ghi, nếu không thì câu hỏi *"vai nào fill tốt hơn"* không trả lời được (cùng lẽ với `adAlgorithm`) |
| `floorPct` | string | — | **MỚI 1.5.7** — percentile nguồn, **KHÔNG** làm khoá (trong một vai nó là hằng): giữ bản cuối của cụm |

> **Đọc:** `arrayJoin` mảng ra từng dòng rồi cộng như bảng thường. `load_fill_rate = Σok / (Σok + Σfail)`.
> Phân biệt "vắng tier" với "tier = 0" phải hỏi `JSONType` trên từng phần tử — hàm trích JSON trả
> `0` cho cả hai. Đừng trả `Array(Tuple(...))` nguyên khối về client: driver JDBC vỡ.

### 5.11f. `f_sdk_ad_load` — Telemetry load, bản tin TÁCH *(MỚI 26/10, SDK 1.5.5 — CHẾ ĐỘ MẶC ĐỊNH, chờ loader đăng ký event id)*

> **Khi nào bắn:** NGAY mỗi lần mediation báo kết quả một lần gọi load (`Ad.OnLoadResult`), một dòng
> một lần gọi. Đây là **chế độ mặc định** của SDK 1.5.5; bản gộp §5.11e chỉ chạy khi remote config
> tắt chế độ tách.
>
> **Vì sao có bản này:** phía đọc số cần nối **load ↔ show**. Bản gộp không mang `ad_view_id` nên
> chỉ nối được bằng `ad_unit_id`; bản tách mang id của **ĐỢT xin ad** nên mọi lần gọi load của một
> đợt dùng chung id với dòng impression mà đợt ấy sinh ra.
>
> **Chuyển chế độ:** remote config, khoá `fAdLoadLogSplit` ở **cả hai** service
> (`Core.RemoteConfig` và `Core.RemoteConfigCms`). Luật **OR**: chỉ cần MỘT bên gửi `false` là SDK
> quay về bản gộp **ngay giữa phiên**; khoá vắng mặt tính là `true`. Mặc định `true` để bật được mà
> không phải cấu hình gì trên server.
>
> ⚠ **Volume**: đo 22/09 thì bản per-attempt ~**158 dòng/user/ngày**, bản gộp ~**20**. Chế độ tách
> là cái giá đã biết trước, và khoá trên là van tắt.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `adType` | enum string | ✓ | Format ad |
| `adMediation` | string | — | Mediation của lần gọi này — từ vựng `max` · `levelplay` · `admob` (chuẩn hoá ở SDK từ 1.5.5) |
| `adAlgorithm` | string | — | **MỚI 1.5.5** — tên thuật toán load (`multicall_<index>` · `single`). SDK không tự biết, mediation khai |
| `adUnitId` | string | — | Ad unit của lần gọi này — **1 unit ↔ 1 floor** theo luật monetize 26/10, nên đây cũng là khoá để biết bậc giá |
| `success` | bool | ✓ | Lần gọi này có ad về hay không |
| `timeLoadingMs` | long | ✓ | Thời gian của ĐÚNG lần gọi này (mediation đo) — **chỉ có ở dòng load** |
| `sessionAdLoadIndex` | int | — | **MỚI 1.5.5** — lần gọi load thứ mấy của format này **trong phiên**, kể cả lần fail (đếm từ 1) |
| `sessionAdLoadSuccessIndex` | int | — | **MỚI 1.5.5** — ad thứ mấy **vào kho** trong phiên (chỉ đếm lần thành công). **Vắng trên dòng fail.** eCPM đi theo trục này |
| `tier` | double | — | **BẬC** giá sàn trong config. ⚠ Vắng key = **không đặt giá sàn**, khác hẳn `0` — cùng hợp đồng với §5.11e |
| `floor` | double | — | **GIÁ SÀN THẬT** (USD/1000 impression) của lần gọi này — không phải trung bình như bản gộp. Vắng = mediation không biết |
| `estimatedEcpm` | double | — | **eCPM ước tính của lần load này**, USD/**1000** impression — thứ quan trọng nhất của dòng này. MAX đưa sẵn trong `adInfo` của callback loaded. ⚠ Lệch 1000 lần với `adRev` của dòng impression (`estimatedEcpm ≈ adRev × 1000`). ⚠ Số **không dương = không biết**, SDK không gửi |
| `ecpmFloor` | double | — | Giá sàn eCPM mà mediation báo cho lần gọi này |
| `adNetwork` | string | — | Network trả ad về (`applovin` · `unity` · `inmobi` · `admob`...) — chỉ có khi `success = true`. Cùng tên với dòng impression để join |
| `errorMess` | string | — | Lỗi của ĐÚNG lần này (bản gộp chỉ giữ lỗi cuối của cụm) |
| `adViewId` | string | — | Id **ĐỢT xin ad** mà lần load này thuộc về — xem cảnh báo dưới |
| `adCandidateId` | string | — | **MỚI 1.5.7** — id của CHÍNH nhát thử này. Một đợt có nhiều nhát song song ở nhiều sàn, nên đây là thứ duy nhất nối *"nhát thử ở sàn X"* với *"nhát đó mang về bao nhiêu"*. Mediation không khai thì **SDK tự cấp** và dán cùng mã lên dòng impression của unit đó |
| `ecpmFloorType` | string | — | **MỚI 1.5.7** — VAI của giá sàn trong flow chọn sàn (`base` · `base_half` · `upgrade_initial` · `upgrade_higher` · `upgrade_fallback` · `decay` · `decay_half` · `no_floor`; từ 23/11 thêm `rule_error` · `rule_timeout` — gọi Rule lỗi / quá giờ, dòng load mặc định, xem `loader/Request-Loader-FloorTypeRuleError.md`). SDK **không** kiểm danh sách. ⚠ Phải là vai của **lúc gửi request**, xem §5.11f-2 |
| `ecpmFloorPercentile` | string | — | **MỚI 1.5.7** — percentile nguồn của giá sàn (`P10`/`P25`/`P50`/`P75`…); vai `no_floor`/`decay*` thì vắng |
| `adLoadAttempt` | int | — | **MỚI 1.5.7** — nhát thử thứ mấy **tại cùng một giá sàn**, đếm từ 1 (retry lần 2 ⇒ `3`). Không dương = không biết, SDK không gửi |
| `sessionAdShownBefore` | int | ✓ | **MỚI 1.5.7** — số quảng cáo của format này **đã chiếu** trong phiên, tính TRƯỚC lần load này. Là **SỐ ĐẾM** nên `0` là sự thật ("chưa chiếu lượt nào") và luôn được gửi. ⚠ **KHÔNG phải `ad_index_session`** — xem cảnh báo lệch-một-nhịp dưới |
| `firstAdEcpm` | double | — | **MỚI 1.5.7** — eCPM THẬT của quảng cáo chiếu **đầu tiên trong phiên** (USD/1000 imp). **SDK tự nhớ**, mediation không truyền. Vắng khi phiên chưa chiếu gì (đang load quảng cáo đầu) |
| `previousAdEcpm` | double | — | **MỚI 1.5.7** — eCPM THẬT của quảng cáo chiếu **ngay trước** lần load này. **SDK tự nhớ**. Vắng khi chưa có lượt nào trước đó |

> ⚠ **`adViewId` CÓ THỂ VẮNG, và vắng là CỐ Ý.** SDK chỉ đóng dấu khi view trong ô request **chưa
> tiêu thụ** (chưa có impression). Lần load rơi vào khoảng *"ad vừa chiếu xong – đợt sau chưa mở
> van"* (callback về muộn, mediation tự refill, banner tự refresh) thì **không có id** — thà vắng
> còn hơn dán id của ad vừa chiếu. Lúc đó nối bằng `ad_unit_id` như bản gộp.
> Ca preload-giữa-lúc-xem thì **có** id, và là id của đợt MỚI (đúng: load đó thuộc đợt mới).

```
load_fill_rate   = count(success = true) / count(*)                 per ad_unit_id / tier
load_to_show     = số ad_view_id có cả dòng load thành công và dòng impression / số ad_view_id có dòng load thành công
floor_waterfall  = count(success = true) theo tier, sắp theo tier    (bậc nào thật sự fill)
```

> ⚠ `floor_waterfall` CHỈ đúng trên tập đã lọc theo cảnh báo dưới: `tier` hiện là **nhãn trong
> config**, không phải bằng chứng request đó được áp sàn.

#### 5.11f-1. Đọc `tier` / `ecpmFloor`: luật vắng-key, và mốc version của nó *(28/10)*

**Luật trên dây — một câu:** số **không dương bị SDK bỏ khỏi payload**, nên trên `f_sdk_ad_load`
**không tồn tại giá trị 0** cho bốn cột giá: `tier` · `floor` · `ecpmFloor` · `estimatedEcpm`.
Hệ quả: *"không áp sàn"* và *"không biết"* **cùng** ra một thứ là **vắng key** — đừng dựng luật DQ
nào phân biệt hai cái đó bằng giá trị.

⚠ **Luật này KHÔNG cùng tuổi ở cả bốn cột** — đây là mốc phải ghi vào sổ đọc số:

| Cột | Từ bản nào SDK bỏ số không dương |
|---|---|
| `ecpmFloor`, `estimatedEcpm` | **1.5.5** (bản sinh ra event này) |
| `tier`, `floor` | **1.5.6** — trước đó truyền **nguyên văn**, nên `tier = 0` **CÓ** trên dây ở 1.5.5 |

**1.5.6 ĐÃ LÊN PRODUCTION**, nên luật trên là **chốt**: trên dây sẽ không có `tier = 0` nữa, và muốn
đọc được "sàn 0" thì phải là **một cột mới** chứ không phải đổi nghĩa cột đang chạy (đổi là đế thêm
một đời dự liệu cho cùng một cột — bài học `floor` 1.4.0).

✅ **Án 26/10 giải thích trọn bằng đúng mốc này**, không cần giả định gì thêm: máy test hôm ấy chạy
bản ≤ 1.5.5 nên `tier = 0` đi được lên dây; production chạy 1.5.6 nên chính giá trị đó bị bỏ ⇒ **cùng
một mediation gửi cùng một số, hai bản SDK ra hai kết quả khác nhau**. Đừng đọc chênh lệch đó thành
"client đổi cách gửi".

⇒ **Mọi phép đếm "thiếu tier" phải chẻ hai chiều**, không thì trộn hai hành vi khác nhau vào một số:

```sql
-- chẻ theo GIÁ TRỊ và theo BẢN SDK; làm cùng lúc, vì cột đổi hành vi ở 1.5.6
countIf(tier IS NULL)  vs  countIf(tier = 0)   group by sdk_version, app_version
```

- Thấy `tier = 0` ⇒ bản đó **chưa** tới 1.5.6 **và** mediation đang gửi 0 cho unit không sàn.
- Chỉ thấy NULL ở bản ≥ 1.5.6 ⇒ có thể SDK **đã xoá một số 0 thật** — không suy ra được mediation
  gửi gì, phải soi ở máy test.
- Query dùng điều kiện `tier > 0` để tính "có tier" sẽ **đếm `tier = 0` thành thiếu** (đúng cái bẫy
  K4 của phiếu floor cũ) — dùng `IS NOT NULL`.

**`tier` có mà `ecpmFloor` vắng — bốn giả thuyết, tất cả ở phía mediation, CHƯA cái nào xác nhận.**
`tier` đọc từ config nên có mặt dù sàn **không hề được áp**; `ecpmFloor` là con số thật đã gửi kèm
request nên nó mới là thứ nói "có áp sàn hay không":

| # | Nguyên nhân | Dấu nhận trên dữ liệu |
|---|---|---|
| N1 | **Thuật toán đó không áp sàn** — trong bản ở repo có nhánh multicall chỉ gọi load N unit theo thang retry, **không set bid floor bao giờ** | `ecpmFloor` vắng **100%** các dòng của đúng một `adAlgorithm`, mọi unit, mọi bản |
| N2 | **Cửa sổ khởi động của công thức sàn động** — sàn = bậc × *doanh thu nền*, nền lấy từ lần load thắng đầu tiên của unit KHÔNG-sàn trong phiên, giữ trong RAM và **reset mỗi phiên** ⇒ trước lần thắng đó sàn tính ra **0** | Dòng thiếu dồn ở `sessionAdLoadIndex` **nhỏ**, và đứng **trước** dòng success đầu tiên của unit không-tier trong cùng phiên |
| N3 | **Ad test trả doanh thu ≤ 0** ⇒ nền đứng 0 **cả phiên** ⇒ mọi dòng của phiên đều thiếu | Dòng thiếu dồn ở `is_test`, và **cùng dòng cũng vắng `estimatedEcpm`** (cùng nguồn `Revenue`) |
| N4 | **Đường đọc `ecpmFloor` ở nhánh SUCCESS của bản live** đọc một ô đã bị reset/chưa gán — họ hàng với lỗi lệch-một-nhịp của `tier` trên dòng fail (việc V6) | Dòng thiếu **toàn là `success`**, trong khi dòng **fail** của cùng unit/phiên **có** `ecpmFloor` |

> ⚠ **Cả bốn đều là giả thuyết**, suy từ code multicall của bản module trong SDK repo, mà bản đó
> **cũ hơn bản mediation đang build ở game** — không đối chiếu được từ đây.
>
> 🔴 **Và dữ liệu hiện có đang PHẢN CHỨNG N2/N3**: nhóm thiếu `ecpmFloor` **toàn là dòng success**.
> Sàn gửi đi bằng 0 nghĩa là *không có sàn*, mà unit không sàn chỉ fill khoảng một nửa ⇒ phải kéo
> theo **cả dòng fail**. Vậy **N4 mới là giả thuyết dẫn đầu**, và trong bản module ở repo thì nhóm
> unit có bậc **chỉ được load bên trong callback thành công của unit không-sàn** — đúng lúc nền vừa
> được gán — nên "cửa sổ nền = 0" của N2 **không tồn tại theo thiết kế** ở nhánh đó. Ngoại lệ duy
> nhất tìm được: một nhánh cũ gán nền theo *unit ở index 0* thay vì *unit không-sàn nào thắng trước*,
> nên nếu unit index 0 không fill thì nền đứng 0 trong khi nhóm có bậc đã bắt đầu xin ad.
>
> **Phép kiểm quyết định (một query):** trong mỗi nhóm thiếu, chẻ theo `success` — dòng **fail** của
> cùng unit + cùng phiên **có** `ecpm_floor` không?
> - **Có** ⇒ N2/N3 chết, lỗi nằm ở đường đọc nhánh success (N4).
> - **Không có** ⇒ bản live không gửi `ecpmFloor` ở nhánh fail, và N2/N3 mới đáng soi tiếp.
>
> Phép kiểm rẻ thứ hai, cho N3: dòng `is_test` thiếu sàn có **cùng lúc vắng `estimatedEcpm`** không
> (cùng nguồn `Revenue`) — cùng vắng ⇒ nền bằng 0.

**Luật đọc chốt lại** *(không phụ thuộc nguyên nhân nào đúng)*: `tier` **không** phải bằng chứng đã
áp sàn. Muốn đo *"bậc nào fill tốt"* thì mẫu số phải là dòng **có `ecpmFloor`** (sàn thật đã gửi),
hoặc đợi mediation làm việc V7 — chỉ gửi `tier` cho request thực sự được áp sàn, lúc đó `tier` vắng
có **đúng một** nghĩa.

#### 5.11f-2. Bảy cột của flow chọn sàn *(MỚI 1.5.7, 29/10)*

> Nguồn: bảng tham số của bên personalization (28/10) + spec *First Interstitial eCPM Floor Rule*
> (29/10). Rule chạy ở **server**, client gọi qua HTTP rồi áp số trả về vào request load. SDK
> **chỉ chở**, không gọi Rule và không tự tính bậc máy.

**Ai điền gì** — đọc sai chỗ này là mọi cột dưới đều vô nghĩa:

| Cột | Ai biết giá trị | Vắng nghĩa là |
|---|---|---|
| `ecpmFloorType` · `ecpmFloorPercentile` · `adLoadAttempt` | **mediation** (từ response của Rule) | mediation chưa khai — **không** phải "không áp sàn" |
| `adCandidateId` | mediation khai thì thắng, **không khai thì SDK cấp** | lần load không đi qua `Ad.OnLoadResult` |
| `firstAdEcpm` · `previousAdEcpm` · `sessionAdShownBefore` | **SDK tự nhớ** (từ `adRev` / sổ đếm của dòng impression) | phiên chưa chiếu quảng cáo nào, hoặc lượt đó không có doanh thu (`sessionAdShownBefore` thì luôn có, 0 = chưa chiếu gì) |
| `deviceTier` · `realtimeSegments` · `realtimeVariants` | **mediation**, qua kênh mức USER (§4.3) — **không** phải cột của dòng load | chưa có ai cấp giá trị |

⚠ **`ecpmFloorType` phải là vai của LÚC GỬI REQUEST.** Response mà Rule trả *sau* một kết quả load
là vai của lần **SAU** (`base` + fail → `base_half`). Lấy "state hiện tại" ở mốc kết quả là **lệch
một nhịp** — đúng lỗi V6 của phiếu Mediation, chỉ đổi cột. Trong một thang retry thì sàn không đổi,
nên mọi dòng attempt của thang đó mang **cùng** một vai, chỉ khác `adLoadAttempt`.

✅ **`no_floor` là đường duy nhất khai "không áp sàn".** `ecpmFloor` không có cách nào chở số `0`
(§5.11f-1), nên vai `no_floor` + `ecpmFloorPercentile` vắng là cách tường minh duy nhất. Nhờ đó
**vắng `ecpmFloor`** từ 1.5.7 đọc được rành rọt: có `ecpmFloorType = no_floor` ⇒ *cố ý không sàn*;
không có vai nào ⇒ *mediation chưa khai*.

🔴 **Chưa có giá trị cho ca gọi Rule LỖI/TIMEOUT.** Spec không định nghĩa, nên vắng vai sẽ mang hai
nghĩa: *không nằm trong flow* và *gọi Rule thất bại* — đúng bệnh hai-nghĩa vừa chữa cho `tier`. Cần
bên quyết sàn cấp một vai riêng (`rule_error`/`rule_timeout`); first interstitial là mốc ăn latency
nên timeout + fallback là **bắt buộc**, tức ca này chắc chắn xảy ra. SDK không chặn chuỗi lạ nên
thêm vai mới **không** cần bản SDK mới.

**`adCandidateId` — ba luật, cả ba đều cần cho một phép join đúng:**
1. **Mediation khai thì thắng.** Bảng riêng của tầng mediation (vd `ad_uuid` của
   `f_sdk_ad_full_data`) truyền mã của nó vào là hai bảng join được per-quảng-cáo; mỗi bên tự cấp mã
   thì **không bao giờ** join được.
2. **Dòng fail cũng có mã** (bên đọc số yêu cầu: *"bao gồm load_success/load_fail"*).
3. **Dùng được MỘT lần, và bị ghi đè kể cả bằng vắng** — luật này áp cho **cả bốn** cột tả *một
   quảng cáo* (`ad_candidate_id` · `ecpm_floor` · `ecpm_floor_type` · `ecpm_floor_percentile`), khác
   hẳn cấu hình của unit (`ad_network`, `tier`, `floor`) vẫn theo luật *"null không đè"*. Dán lên một
   dòng impression là **tiêu thụ** luôn, nên lượt chiếu thứ hai của cùng unit mà không có lần load
   mới sẽ **vắng** cả bốn (ca V4).
   <br/>Vì sao phải thế: mã cũ là **danh tính của một quảng cáo khác**, và vai sàn cũ là lời khai của
   **một request khác**. Flow chọn sàn chỉ chạy cho quảng cáo đầu phiên, nên lần load sau của cùng
   unit **không khai vai** — giữ giá trị cũ là dán nhãn `upgrade_higher` lên một request chưa từng áp
   sàn, và cái nhãn bịa đó rơi đúng vào tập dòng dùng để chấm personalization.

**`firstAdEcpm` / `previousAdEcpm` — hai luật NGƯỢC nhau, cố ý:**
- **first** chốt ở lượt chiếu đầu phiên và không đổi nữa, **kể cả khi lượt đó không có doanh thu**
  (giá trị vắng, nhưng chỗ "lượt đầu" đã bị chiếm). Lọc theo "số dương" là đẩy lượt **thứ hai** lên
  làm lượt đầu — khai sai một sự kiện, không chỉ sai một con số.
- **previous** thì lượt mới **luôn đè**, kể cả đè bằng vắng: cột nói *"lượt ngay trước"*, không phải
  *"lượt có giá gần nhất"*.
- **Gộp chung mọi format TOÀN MÀN** (inter + rewarded + app open), **bỏ họ dính màn** (banner · MREC
  · collapsible · native — chúng tự refresh liên tục nên sẽ luôn là "lượt ngay trước", đẩy quảng cáo
  toàn màn ra khỏi chính câu hỏi cột này sinh ra để trả lời). ⚠ Gộp inter với rewarded là cách đọc
  **mặt chữ** của yêu cầu — muốn tách theo format thì nói, và dựng lại được từ dòng impression nên
  sửa muộn không mất dữ liệu.
- ⚠ **Điều kiện cần**: impression phải dựng một `FAdLog` (mọi cửa của BigData đều dựng, kể cả
  `ExtendAdLog` của nhánh IronSource/GMA). `first_ad_ecpm` **vắng trên 100% dòng** của một máy là dấu
  hiệu impression của máy đó không đi qua cửa nào, **không** phải "cả phiên chưa chiếu gì".
- Reset theo `SessionId` (app vào nền rồi quay lại = phiên mới, luật `app_open`).

🔴 **`sessionAdShownBefore` ≠ `ad_index_session`, và CỐ Ý khác tên.** Trên dòng impression,
`session_ad_impression_index` là *"lượt chiếu thứ mấy của CHÍNH lượt này"*; trên dòng load,
`session_ad_shown_before` là *"đã chiếu mấy lượt TRƯỚC lần load này"* — cùng một quảng cáo thì dòng
load mang `2` mà dòng impression của nó mang `3`. Đặt chung một tên là mọi `JOIN`/`GROUP BY` giữa hai
bảng ghép **lệch một nhịp, im lặng** (đúng họ lỗi V6). Và **không chữa được bằng +1**: multicall giữ
ad trong kho nên unit load trước lượt N−1 vẫn có thể là cái được chiếu ở lượt N — lúc load thì KHÔNG
biết mình sẽ thành lượt thứ mấy. Muốn "slot kế tiếp" thì phía đọc tự cộng 1 và tự chịu sai số đó.

⚠ **Bản GỘP (§5.11e) chỉ chở được hai trong bảy cột** (`floorType` làm khoá, `floorPct` bản cuối).
`adCandidateId` (mã riêng từng attempt — làm khoá thì cụm nở bằng số attempt, đúng vụ `floor` liên
tục ở 1.4.0), `adLoadAttempt` (cụm đã có `ok`/`fail`), `firstAdEcpm`/`previousAdEcpm`/
`sessionAdShownBefore` (số của **phiên**, trung bình hoá là vô nghĩa) **không có ở đó và sẽ không
bao giờ có** ⇒
**đo flow chọn sàn thì game phải chạy chế độ TÁCH** (`fAdLoadLogSplit = true`, mặc định).

**Hai thứ bên đọc số xin mà SDK CỐ Ý không gửi** — ghi ở đây để khỏi hỏi lại:

| Xin gì | Vì sao không, và đọc bằng gì |
|---|---|
| `estimated_ecpm` trên dòng **impression** | Ở đó nó **bằng `ad_rev` × 1000** (owner chốt 26/10, chốt lại 29/10) — phía đọc tự nhân là xong, chở thêm một cột chỉ để nhân 1000 là mời một bản ghi lệch đơn vị vào kho. Số có giá trị riêng ở mốc **load** (nơi chưa có đồng doanh thu nào) và **vẫn** được gửi ở đó |
| `ecpm_floor = 0` | Số không dương **không tồn tại trên dây** (§5.11f-1, đã live từ 1.5.5). Query `ecpm_floor = 0` ra **0 dòng** — đọc bằng `ecpm_floor_type = 'no_floor'` |

```
floor_role_fill  = count(success = true) / count(*)   per ecpm_floor_type        (vai nào fill)
floor_role_price = avg(estimated_ecpm)                per ecpm_floor_type        (vai nào đáng tiền)
retry_decay      = count(success = true) / count(*)   per (ecpm_floor_type, ad_load_attempt)
candidate_join   = f_sdk_ad_load ⋈ f_sdk_ads_data ON ad_candidate_id            (ước tính vs tiền thật)
```

> ⚠ Mẫu số của `floor_role_fill` phải là dòng **có `ecpm_floor_type`**. Trộn dòng vắng vai (mediation
> chưa khai, hoặc game không chạy flow này) vào là pha hai chế độ chọn sàn khác nhau — cùng cái bẫy
> `tier` ở §5.11f-1.

### 5.12. `f_sdk_iap_start_purchase_data` — Mở lượt mua *(MỚI — chờ loader xác nhận event id)*

> **Khi nào bắn:** ngay trước khi gọi billing flow (`launchBillingFlow` / StoreKit payment).
> Đây là điểm vào phễu checkout — không có event này thì abandonment mù hoàn toàn.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `purchaseAttemptId` | string | ✓ | Id lượt mua, SDK sinh. Xuất hiện lại trên log fail hoặc log mua thành công tương ứng. |
| `productId` | string | ✓ | Sản phẩm đang mua. |
| `where` | string | — | Vị trí/ngữ cảnh kích hoạt mua (bỏ nếu null). |
| `localizedPrice` | decimal | — | *(MỚI)* Giá nội địa hoá — đo giá trị bị bỏ giỏ mà không phải join catalog. |
| `isoCurrencyCode` | string | — | *(MỚI)* Mã tiền tệ của giá trên. |
| `networkType` | string enum | — | *(MỚI)* `wifi`/`cellular`/`none` lúc mở billing flow — SDK tự đo. |

### 5.13. `f_sdk_iap_purchase_fail_data` — Lượt mua không hoàn tất *(MỚI — chờ loader xác nhận event id)*

> **Khi nào bắn:** billing callback trả lỗi/huỷ/treo. Mang cùng `purchaseAttemptId` với log mở lượt.
>
> ⚠ `failReason = pending` **KHÔNG phải fail thật** (Google PENDING): giao dịch còn treo, log mua
> vẫn sẽ bắn khi hoàn tất sau — có thể ở phiên khác, khi đó log mua KHÔNG mang `purchaseAttemptId`.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `purchaseAttemptId` | string | ✓ | Cùng id với log mở lượt. |
| `productId` | string | ✓ | Sản phẩm. |
| `where` | string | — | Vị trí/ngữ cảnh (bỏ nếu null). |
| `localizedPrice` | decimal | — | *(MỚI)* Giá nội địa hoá. |
| `isoCurrencyCode` | string | — | *(MỚI)* Mã tiền tệ. |
| `networkType` | string enum | — | *(MỚI)* Loại kết nối **tại mốc thất bại** (không phải lúc mở) — giải thích `failReason = network`. |
| `failReason` | string enum | ✓ | `user_canceled` / `item_unavailable` / `billing_unavailable` / `network` / `developer_error` / `pending` / `unknown` *(giá trị `unknown` là bổ sung của client khi không map được responseCode — cần loader xác nhận)*. |

### 5.14. `f_sdk_app_open_data` — Mốc mở app *(MỚI — chờ loader xác nhận event id)*

> **Khi nào bắn:** mỗi lần app vào foreground. Bản `cold` (process vừa khởi tạo) đồng thời là
> **mốc mở phiên** — hợp đồng v-next §B liệt kê `session_start` và `app_open` riêng, nhưng trong
> SDK này session = 1 process nên hai mốc trùng khít 1:1; client chỉ gửi `app_open` với
> `launch_type=cold`. **Cần loader xác nhận** id `f_sdk_app_open_data` và việc không có event
> `session_start` riêng.
>
> Lần quay lại từ background có thời lượng nền dưới `fAppOpenMinBackgroundSec` (remote config,
> default 30s) thì KHÔNG gửi — lọc nhiễu kiểu thoát ra vài giây. `openIndex` vẫn tăng cho những
> lần bị lọc, nên khoảng nhảy của nó cho biết có bao nhiêu lần quay lại ngắn.
>
> ⚠ **ERA — hot sau ad toàn màn** *(ký loader 12/11, chưa publish — mốc là bản BigData chứa thay đổi
> này)*: chiếu interstitial / rewarded / app open làm Unity pause trên **cả Android lẫn iOS**, nên
> trước mốc này mỗi ad ≥ 30s sinh một dòng `hot` giả với `backgroundDurationSec` ≈ thời lượng ad
> (loader đo xác nhận 11/11). Từ mốc: pause **bắt đầu trong 10 giây sau `OnShown`** của một ad
> toàn màn chưa đóng ⇒ lần quay lại sau nó **không gửi**; `openIndex` vẫn tăng. Cái giá: người chơi rời
> app trong 10 giây đầu của ad cũng bị bỏ. Hệ quả cho kho: `resume_count` / `background_sec` của dòng
> phiên đổi nghĩa theo bản — đọc theo era.
> <br/>Chỉ phủ đường mediation gọi `Ad.OnShown` quanh lệnh show (soát 12/11): MAX gọi thẳng, MAX MultiCall,
> MAX qua FSN, AdMob bridge. **Chưa phủ** `FalconGmaService` (app open GMA) và `FalconIronSourceService` — hai đường này không
> gọi `Ad.OnShown` nên lần đóng ad vẫn ra hot giả; era vì vậy là **theo bản × đường mediation**, không chỉ theo `sdk_version`.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `launchType` | string enum | ✓ | `cold` = process mới (≡ mốc mở phiên, tuyệt đối không false-positive) / `hot` = quay lại foreground cùng process. Unity KHÔNG sinh `warm` (activity/scene dựng lại) — giá trị đó dành cho SDK native. |
| `backgroundDurationSec` | int | — | Thời lượng nằm dưới background. Bỏ khi `cold` hoặc khi không đo được (không bịa 0). |
| `openIndex` | int | ✓ | Thứ tự lần vào foreground trong phiên, bắt đầu từ 1, đếm cả lần bị lọc. |
| `networkType` | string enum | — | *(MỚI)* Loại kết nối lúc mở app — SDK tự đo. |
| `openSource` | string enum | — | *(MỚI §B4)* `icon` / `push` / `deeplink` / `widget` / `other`. Unity không đọc được intent (Android) / launchOptions (iOS) nên **module push/deeplink phải gọi `App.ReportOpenSource` trước khi bản tin bắn**; không ai gọi thì field vắng mặt — SDK KHÔNG mặc định `icon`. |
| `pushCampaignId` | string | — | *(MỚI §B4)* Campaign của push đã kéo người chơi vào. Chỉ có khi `openSource = push`. |
| `startupDurationMs` | int | — | *(MỚI §B4)* **Chỉ ở bản `cold`**, và **vắng nếu app đã từng xuống nền trước bản tin cold** (OS mở process ngầm rồi người chơi mở sau — quãng chờ bị đếm vào; ký loader 12/11). Từ lúc code C# sống dậy (`SubsystemRegistration`) tới lúc dựng bản tin này. ⚠ **THIẾU native bootstrap** (OS tạo process → Unity runtime lên) vì Unity không nhìn thấy khoảng đó ⇒ đây là **số tương đối** để so giữa các bản/các máy, không phải cold-start tuyệt đối. Đo bằng đồng hồ đơn điệu (`Stopwatch`), không phải wall-clock. |

> `startupDurationMs` là **ngoại lệ duy nhất** client được tự đo khoảng thời gian: đoạn này nằm
> trước khi hệ thống event sống nên server mù. Từ mốc `app_open` trở đi, mọi khoảng cách thời gian
> server tự trừ giữa các event — client không cộng hộ. Mốc đo cố định trong SDK; nếu đổi sẽ báo loader.

### 5.18. `levelLabels` — nhãn của MÀN, đi ké level event *(§D11, viết lại lần 3 ngày 2026-08-11)*

> **Không có event riêng.** Game khai qua `Label.Level(id, key, value)` →
> giá trị vào kho nhãn của SDK → SDK-core đóng dấu bundle lên **CHỈ level events**, y khuôn
> `playTurnId` đi ké mọi log.

| Key | Kiểu | Mô tả |
|---|---|---|
| `levelLabels` | object PHẲNG | `{"cluster":"hard_1","hand_tuned":true,"tier":3}` — giữ nguyên kiểu JSON. Vắng mặt khi màn chưa có nhãn. |

**Vì sao không bắn event riêng** (đường cũ đã HỦY): bridge `user_level_daily` có grain theo NGÀY,
mà bắn-một-lần-đời-cài thì ngày người chơi quay lại màn cũ không có bản tin nào → hộp ngày đó rỗng,
trừ khi server nuôi bảng state cỡ tỷ dòng để forward-fill. Stamp thì ngày nào chơi là ngày đó có
nhãn tươi, và **ván vắt qua nửa đêm UTC tự lành** vì heartbeat của ngày sau cũng mang bundle.

Bốn luật đi kèm:
1. **Chỉ level events.** Stamp sang stream ad/iap/resource là vi phạm hợp đồng — "rev ads theo cụm
   khó" đã với được qua `playTurnId`, không đáng trả byte trên stream khác.
2. **Bundle tả màn CỦA CHÍNH EVENT ĐÓ** (`currentLevel` trên event), không phải màn đang mở —
   heartbeat trễ sau khi sang màn mới phải mang nhãn của màn nó tả.
3. **Gỡ nhãn = thôi kèm key.** Cook đọc "hôm nay không thấy key" là *không còn nhãn ngày đó*, không
   giữ giá trị cũ. `labelValue: null` chỉ còn nghĩa cho họ label event-based (user/instance).
4. **Nhãn khai trước cho màn chưa ai chơi không bao giờ phát ra** — đúng thiết kế, không phải bug.

> **Van 256B/bundle** (đếm BYTE, không đếm key — đếm key thì phạt oan nhãn số). Vượt: client cảnh
> báo kèm số cụ thể và **giữ nguyên bundle cũ**; loader vượt thì DROP BUNDLE, không drop record.
> Van này bảo vệ WIRE, khác van ≤20 key/≤64 ký tự vốn bảo vệ namespace của nhãn user — hai van hai việc.

> Đừng gửi qua đây thứ đã có kênh riêng: `difficulty` (đã ở level event), `currentLevelId`. Nhãn để
> dành cho thứ CHƯA có chỗ.
>
> **Tên đăng ký sẵn** (`FLevelLabelKey`): `moves_limit`, `time_limit_sec`. Hai cái này ĐÚNG là nhãn
> màn và đi trong bó này, nhưng game không gõ tay: SDK tự đặt khi game truyền
> `Level.OnStart(movesLimit:, timeLimitSec:)` hoặc `Level.SetLevelConfig(...)`. Gõ tên khác là mất
> khả năng so chéo game. *(Sửa 12/11: câu cũ bảo "đừng gửi moves / time qua đây", mâu thuẫn với code.)*

### 5.17. `f_sdk_permission_*` — Trạng thái quyền/consent *(MỚI §D8)*

> **Một event cho MỖI loại quyền**, không phải một event chung mang key:
> `f_sdk_permission_att` · `f_sdk_permission_ads_consent` · `f_sdk_permission_push`.
> Luật phân xử của hợp đồng: *vocab ĐÓNG toàn-fleet mà analyst lọc trực tiếp thì nằm trong TÊN
> EVENT; vocab MỞ per-game thì nằm trong param.* Phí migrate bất đối xứng chốt án — gộp rồi tách
> là cả chiến dịch convert (án `f_sdk_level`), tách rồi gộp thì `event LIKE 'f_sdk_permission_%'`
> là xong.
>
> **Khi nào bắn:** khi ĐỔI giá trị + lần đăng nhập đầu. SDK nhớ giá trị đã gửi nên game gọi lại
> cùng giá trị bao nhiêu lần cũng không đẻ log. "Trạng thái hiện tại" phía server =
> `argMax(sub_event, created_date)` per event.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `permissionValue` | string | ✓ | Giá trị trạng thái → server đưa vào `sub_event`. |
| `currentLevel` | int | — | Level lúc đổi trạng thái (bỏ nếu null). |

| Event | `permissionValue` hợp lệ |
|---|---|
| `f_sdk_permission_att` | `authorized` / `denied` / `restricted` / `not_determined` |
| `f_sdk_permission_ads_consent` | `granted` / `denied` |
| `f_sdk_permission_push` | `granted` / `denied` |

> API phía client kiểu hoá sẵn nên không gõ sai vocab được: `App.ReportAttStatus(AttStatus)`,
> `App.ReportAdsConsent(bool)`, `App.ReportPushPermission(bool)`; `App.ReportPermission(type, string)`
> chỉ là cửa thoát cho quyền mới chưa kịp có hàm riêng.
>
> `sub_event_2` client **để trống** — hợp đồng dành trục đó cho nguồn trigger (`prompt` /
> `settings` / `login_check`) khi nào có nhu cầu.
>
> ⚠ **KHÔNG dùng `property_data`** cho mấy trạng thái này: đó là log di sản thời hệ sơ khai dùng
> sinh biểu đồ động (dấu vết còn nguyên ở field `priority` = "thứ tự step"), phía server nó thành
> event `f_sdk_property`. Mượn kênh legacy là trộn ngữ nghĩa với dữ liệu biểu đồ đời cũ.

### 5.16. `f_sdk_<entity>_label` — Họ bản tin gán nhãn *(MỚI §D11)*

> **Một format, mỗi entity một event**: `f_sdk_user_label` · `f_sdk_session_label` ·
> `f_sdk_turn_label` · `f_sdk_iap_offer_label` · `f_sdk_iap_purchase_label` · `f_sdk_ad_view_label`
> (ba cái sau hợp thức hoá 2026-08-11; id đi trong param vì — khác `sessionUid`/`playTurnId` — ba
> id đó không được đóng tự động lên mọi log).
>
> ⚠ **Nhãn của MÀN KHÔNG nằm trong họ này** — level đi đường STAMP, xem mục 5.18.
> Entity vào TÊN EVENT (vocab đóng), `labelKey` ở param (vocab MỞ per-game) — đúng luật phân xử §D8.
>
> **Một cửa cho cả "nhãn" lẫn "tham số động"** — dev không phải phân biệt; ranh giới đó là việc của
> data team.

| Key | Kiểu | Bắt buộc | Mô tả |
|---|---|---|---|
| `labelKey` | string | ✓ | Loại nhãn — **TỰ DO, không đăng ký trước** (registry đã bỏ 2026-08-10). Server đưa vào `sub_event`. |
| `labelValue` | **JSON nguyên vẹn** | ✓ | bool / số / chữ — KHÔNG ép về string, không nhét vào slot typed. **`null` = GỠ nhãn**, nên field này luôn có mặt kể cả khi null. |
| `adViewId` / `offerImpressionId` / `purchaseAttemptId` | string | — | Mỗi cái CHỈ có ở event instance tương ứng. |

**Ngữ nghĩa thời gian khác nhau theo entity** — đây là chỗ dễ nhầm nhất:

| Event | Server làm gì | Hiệu lực |
|---|---|---|
| `f_sdk_user_label` | merge vào bucket `user_properties` (chung chỗ + cùng ngữ nghĩa last-wins với `falcon_user_filter`) → snapshot lên mọi event SAU | **từ-mốc-trở-đi**, quá khứ MIỄN NHIỄM |
| `f_sdk_session_label` / `_turn_label` / `_ad_view_label` / `_offer_label` / `_purchase_label` | cook nhặt SQL (argMax theo instance) vào hộp tương ứng, KHÔNG qua profile | trọn instance; chỉ gán được khi instance còn MỞ |

> **Van chỉ có ở SDK** (sửa 12/11 — kho xác nhận **không** có van nào cho nhãn): ≤20 label key/user ·
> value chữ ≤64 ký tự · bó nhãn đi ké ≤256 byte. SDK chặn ở client kèm cảnh báo dev.
>
> **Trạng thái phía kho (loader đo 12/11, `Reply-Loader-AdShowSkipped.md` mục 4):** chỉ
> `f_sdk_ad_view_label` có mapping chạy thật (`label_key` → `sub_event`). `f_sdk_iap_offer_label` vào
> silver nguyên tên nhưng chưa có luật. `f_sdk_user_label` / `_session_label` / `_turn_label` /
> `_iap_purchase_label` **chưa có mapping** và chưa có dòng thật — kho dựng khi game đầu tiên gọi, nên
> **báo loader** trước khi bật cho game nào. Bó `levelLabels` kho đã đọc; `offerLabels` chưa đọc.
> Riêng `f_sdk_user_label`: ngữ nghĩa "merge vào profile, forward-fill lên event sau" ở bảng trên là
> hợp đồng — phía kho **chưa dựng** bước đó.
>
> **AB testing KHÔNG đi đường này** — đã có kênh riêng `ab_campaigns` (first-wins).

**Tên đăng ký sẵn** (game gõ tên khác là mất khả năng so chéo game):

| Tên | Bản tin | Giá trị | Ai bắn |
|---|---|---|---|
| `elo` | `f_sdk_user_label` | số nguyên; `null` = gỡ | game, qua `Level.SetUserElo` |
| `no_ads` *(mới 12/11)* | `f_sdk_user_label` | `true`; `null` = gỡ (mất quyền) | AnalyticParamsSync tự gắn theo cờ bỏ quảng cáo của Mediation (đầu phiên + mỗi lần pause, chỉ gửi khi đổi); game không dùng module đó thì tự gọi `Label.User` |
| `load_error` / `show_error` | `f_sdk_ad_view_label` | mã lỗi của mediation | SDK, qua `Ad.OnLoadFailed` / `OnShowFailed` |
| `clicked` | `f_sdk_iap_offer_label` | `true` | SDK, khi bấm sau lúc bản tin impression đã chốt |

### 5.12. (Mục dự phòng)

Các biến thể log "mở rộng" (ad mở rộng, resource mở rộng, puzzle level) **không tạo `event` mới** — chúng tái sử dụng các `event` ở trên (`f_sdk_ads_data`, `f_sdk_resource_data`, `f_sdk_level_data`) nhưng điền thêm các trường optional đã liệt kê (ví dụ `hasClick`, `timeShow`, `valueBefore`, `valueAfter`, `detail`, `boostersUsed`...).

---

## 6. Response trả về

### 6.1. Endpoint đơn — `/event-log-v2`

Trả về **plain text** (không phải JSON). Thành công khi body đúng bằng chuỗi:

```
Request processed successfully.
```

(chấp nhận cả biến thể có ký tự xuống dòng `\n` ở cuối). Mọi giá trị khác coi là **thất bại**.

### 6.2. Endpoint batch — `/batch/event-log-v2`

Trả về **JSON**:

```json
{
  "successCount": 10,
  "failCount": 2,
  "errors": [
    { "exception": "mô tả lỗi", "data": "payload của message bị lỗi" }
  ]
}
```

| Key | Kiểu | Mô tả |
|---|---|---|
| `successCount` | int | Số message xử lý thành công. |
| `failCount` | int | Số message lỗi. |
| `errors` | array | Danh sách lỗi; rỗng nếu không có lỗi. |
| `errors[].exception` | string | Mô tả/loại lỗi. |
| `errors[].data` | string | Nội dung message gây lỗi. |

Quy ước phía client hiện tại: nếu `errors` rỗng coi như cả batch thành công; nếu `errors` có phần tử thì log lỗi tương ứng.

### 6.3. Quy ước XOÁ HÀNG CHỜ sau khi gửi — và một lỗ còn lại *(28/10)*

> ⚠ **Mục này đã viết SAI một lần trong cùng ngày và được kho sửa lại.** Bản đầu kết luận
> *"pipeline xoá log âm thầm nên mất `eligible`/`open` của Key Collection"*. Kho phản biện bằng ba
> dữ kiện, và **họ đúng**:
> 1. Thống kê của họ: **99% log đã gửi đều lên tới server** — không có chuyện mất hàng loạt ở đường truyền.
> 2. Client gửi **tuần tự**.
> 3. Đầu nhận **chủ yếu chỉ kiểm FORMAT**. Nên message bị từ chối là **sai vĩnh viễn**: gửi lại
>    bao nhiêu lần cũng lỗi y như vậy ⇒ **gửi batch xong drop cả batch là ĐÚNG**, giữ lại chỉ kẹt
>    hàng chờ ở một message không bao giờ hợp lệ. Đề xuất "retry 3 lần rồi bỏ" của bản đầu **rút lại**.

Vậy phần nào còn đứng? Hai thứ, và cả hai là *tính chất cần biết*, không phải nguyên nhân của án
Key Collection:

**(1) `Drain` chạy cả khi KHÔNG gửi gì.** `LogScheduleService.TryFlush` xoá hàng chờ ngay sau khi
`SendBatch` trả về không ném exception, mà `Send`/`SendBatch` **return ở dòng đầu** khi cờ remote
`fCoreAnalyticShouldNotSendLogToServer` bật. Bật cờ = log sinh ra trong quãng đó **bị xoá, không
phải hoãn**. ⚠ Nhưng cờ này **không giải thích được** án KC: game bật cờ thì kho **không có dòng
nào** của game đó trong quãng, mà dữ liệu KC thì có.

**(2) Van lọc ghi khoá TRƯỚC khi log rời máy.** `LiveOpsLogService.BuildStep` ghi
*"bước này đã log"* xuống đĩa rồi mới `Enqueue`; gửi là việc xảy ra sau. Nên **bất kỳ** đường bỏ
log vĩnh viễn (format sai, cờ ở (1), hàng chờ đầy 500 segment × 200 message) đều để lại một **lỗ
không bao giờ lấp được** trên máy đó: SDK tin là đã gửi. Đây là tính chất phải nhớ khi đọc mẫu số
`eligible`/`open`, không phải bug để vá bằng retry.

**(3) 🔑 Cách phân biệt "mất khi gửi" với "chưa bao giờ sinh ra" — dùng `log_type_create_id`.**
Mỗi bản tin mang `log_type_create_id`: **bộ đếm theo TÊN EVENT, theo MÁY, ghi xuống đĩa, tăng 1 mỗi
lần log được đưa vào hàng chờ** (`BaseLogDecorService`). Nên với một máy, dãy
`log_type_create_id` của `f_sdk_live_ops` phải **liên tục**:

| Kho thấy gì | Nghĩa |
|---|---|
| dãy có **lỗ** (0,1,**_**,**_**,4) | log **đã sinh ra** mà không tới kho ⇒ mất ở đường truyền/bỏ log |
| **không có lỗ**, dòng đầu tiên của máy đó có id nhỏ nhất | client **chưa bao giờ sinh** mấy bước kia ⇒ việc của module event, không phải pipeline |

Đây là phép kiểm quyết định cho mọi tranh luận "thiếu dòng" về sau, và nó dùng dữ liệu kho đã có.

### 6.3b. (Bản đầu, giữ để tham chiếu) ba đường bỏ log ở client

`LogScheduleService.TryFlush` **xoá hàng chờ ngay sau khi `SendBatch` trả về không ném exception**:

```csharp
_sendService.SendBatch(unsent).Wait();
_repository.Drain(unsent.Count);        // ⚠ xoá CẢ những message chưa hề đi
```

Ba đường làm `SendBatch` "trả về bình thường" mà log **không tới kho**, và cả ba đều mất **vĩnh
viễn** chứ không phải chậm:

| Đường | Chỗ trong code | Hệ quả |
|---|---|---|
| Cờ remote `fCoreAnalyticShouldNotSendLogToServer` | `LogSendService.Send`/`SendBatch` return ngay ở dòng đầu | bật cờ một quãng = **toàn bộ log sinh ra trong quãng đó bị xoá** |
| Batch bị server từ chối từng message (`errors` có phần tử) | chỉ `Debug.LogError`, không giữ lại | mất đúng mấy message server không nhận |
| Endpoint đơn trả body khác `"Request processed successfully."` | chỉ `Debug.LogError`, không giữ lại | mất message đó |

(Thêm một đường đã biết và có cảnh báo: hàng chờ đầy **500 segment × 200 message** thì bỏ segment
**cũ nhất** — `TieredUnsentLogRepository`.)

⚠ **Vì sao nó tệ hơn "mất vài dòng": nó nói dối sổ CHỐNG TRÙNG.** Van lọc live-ops ghi khoá
*"bước này đã log"* xuống đĩa **TRƯỚC** khi log rời máy (`LiveOpsLogService.BuildStep`), nên log bị
xoá ở một trong ba đường trên = bước đó **không bao giờ** được gửi lại trên máy ấy. Mất một dòng
`eligible`/`open` kiểu này là mất **vĩnh viễn**, và chart mẫu số không có cách nào biết.

**Án thực địa (phiếu kho 28/10)**: Key Collection có rất nhiều cặp (user, đợt) mang `join` mà
**không có** `open`. ✅ **Kho đã chạy phép kiểm `log_type_create_id` ở (3) và pipeline VÔ CAN**:
dãy id liên tục, gần như luôn bắt đầu từ 0 trên mọi máy (một máy thiếu đúng một id) ⇒ **mọi log
client tạo ra đều đã tới kho**. Hai nhận định trước đó **đều bị loại**: "cửa sổ ngày của truy vấn"
(kho xét toàn bộ dữ liệu) và "mất log ở pipeline" (của mình). Kho cũng **rút lại** câu *"join là
bản tin live-op đầu tiên của user"* — theo id thì ngay trước `join` gần như luôn có `eligible` của
cùng đợt.

⚠ **Và `eligible` liền số, cách `join` 0 giây là KHUÔN CỦA MODULE, không phải bằng chứng auto-join**
— `DeclareKeyCollection.SendEvent` gọi `KeyCollectionTracking.OnDataUpdated()` (bắn `eligible`)
**trong chính callback thành công** của lời gọi server, rồi mới `onSuccess()` (bắn `join`):

```csharp
UpdateEvent(..., onSuccess: () => {
    KeyCollectionTracking.OnDataUpdated();   // → eligible, nếu đợt này chưa từng log
    onDataUpdated?.Invoke();
    onSuccess?.Invoke();                     // → OnJoin() → join
});
```

Nên **mọi** `join` của KC đều có `eligible` ngay trước nó **nếu đợt đó chưa từng log eligible**.
Thông tin thật nằm ở chỗ khác: nhóm thiếu `open` là nhóm mà **trước lúc join, user chưa hề có một
nhịp data nào đi qua cổng eligible, và cũng không có `open`** — tức họ **chưa từng mở UI của
event**. Mà trong module chỉ có **một** cửa gọi `SendJoin()`: nút Play trong `PopupKeyCollection`.
`SendJoin()` lại là `public static` ⇒ đường bắn join không-qua-UI nằm ở **code game**, ngoài repo
này. Việc cho dev game: grep `DeclareKeyCollection.SendJoin(` trong repo game.

**Việc cho loader** (rẻ, giúp client sửa triệt): thêm **`uuid`** của message vào từng phần tử
`errors[]`. Hiện chỉ có `errors[].data` (payload thô) nên client muốn giữ-lại-đúng-message-lỗi thì
phải so chuỗi payload — làm được nhưng dễ vỡ.

---

## 7. Ví dụ `data` đã giải mã (đọc cho dễ)

Ví dụ nội dung object **bên trong** `data` của một event `f_sdk_ads_data` (đã un-escape để dễ đọc — trên wire nó là một string):

```json
{
  "event": "f_sdk_ads_data",
  "logTypeCreateId": 12,
  "logTypeSendId": 12,
  "uuid": "0f4c2b9a-1d3e-4f56-8a7b-9c0d1e2f3a4b",
  "createdDateLocal": "2026-06-26 14:30:00+07:00",

  "accountId": "acc_123456",
  "level": 47,
  "maxPassedLevel": 47,
  "installVersion": "1.0.0",
  "installDay": "2026-06-01 00:00:00+00:00",
  "activeDays": 25,
  "sessionId": 8,
  "totalPlayTime": 36000,
  "platform": "Android",
  "appVersion": "1.4.2",
  "deviceModel": "SM-G991B",

  "type": "Interstitial",
  "adWhere": "level_complete",
  "adWhen": "after_win",
  "adRev": 0.0123,
  "impressionCount": 1,
  "adNetwork": "applovin",
  "currentLevel": 47
}
```

(Các trường central user params được rút gọn trong ví dụ; thực tế đầy đủ như mục 4.2.)
