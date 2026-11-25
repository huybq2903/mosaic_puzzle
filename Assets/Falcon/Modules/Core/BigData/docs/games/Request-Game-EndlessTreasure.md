# Tracking Endless Treasure — TODO cho dev + tham chiếu log

> **Dev chỉ cần đọc phần TODO ngay dưới đây** — chép class mẫu, gọi đúng chỗ, kiểm theo hướng dẫn.
> Tick `[x]` khi xong. Giải thích "vì sao" và công thức cho team data nằm ở nửa dưới.
> Viết 22/09, rà lại code module **Endless Treasure 1.5.0** ngày 29/09.
> Cần BigData ≥ 1.3.9 (Ui API + hằng `FIapPlacement.EndlessTreasure`).
>
> **Module đã tích hợp ĐỦ** (rà 29/09, kể cả 4 việc ở mục E đã sửa xong): funnel `open` + meta chu
> kỳ, `IapOffer` 3 mốc kèm SKU bậc trả phí kế tiếp, `Ui` icon, thưởng đi `Grant(..., context)`
> không đếm đôi, `where = endless_treasure`, key có tiền tố, và luật **hoãn-gửi-khi-chưa-biết-đợt**.
> Việc còn lại chỉ nằm ở mục B (Packs) nếu bundle đi qua module đó.

Endless Treasure là **gói bán IAP dạng chuỗi** (Stage free → bundle trả phí → free…), không phải
event chơi level → **không** gắn gì lên log level. Trục chính: IAP Offer + IAP + Resource.

**Luật số một: MỌI con số đọc từ config / data của game.** Giờ bắt đầu/kết thúc đợt, đợt thứ mấy,
Stage nào, luật lặp Stage — dev lấy từ config/data thật rồi truyền vào. **Không** tự cộng 168h,
**không** tự tính luật lặp: mỗi game cấu hình một kiểu, server chỉ đọc lại số dev gửi.

# 🔁 THÊM HỢP ĐỒNG LIVE-OP — PHA 1: chỉ THÊM, không bỏ gì

> Chốt 15/10/2026. Hợp đồng: `docs/design/LiveOpsEventContract.md` · Khuôn:
> `docs/design/LiveOpsTrackingPattern.md`. SDK **BigData 1.5.0** đã có đủ API; chờ loader ký
> `f_sdk_live_ops_data` trước khi **game ship** (up package thì không chặn).
> **Khoá event của event này: `endless_treasure`** (để trần, không tiền tố `event_`).
>
> **Luật của pha này: THÊM, KHÔNG BỎ.** Mọi lời gọi đang chạy giữ **nguyên 100%** — dashboard hiện
> tại không được phép vỡ. Hai stream chạy song song tới khi dashboard mới thay thế xong.


### [ ] L1 — THÊM: khai đợt + bước `open` live-ops (song song funnel `open` cũ)

```csharp
FalconBigDataController.LiveOps.DeclareCycle(
    "endless_treasure", cycleId: cycle.id, cycleIndex: cycle.index,
    cycleStartTs: cycle.startMs, cycleEndTs: cycle.endMs);

// gói cả hai đường vào một hàm của EndlessTreasureTracking
FalconBigDataController.Funnel.OnStep(/* … như hiện tại … */);                      // CŨ, giữ
FalconBigDataController.LiveOps.OnStep("endless_treasure", FLiveOpsAction.Open);     // MỚI
```

Luật **hoãn-gửi-khi-chưa-biết-mã-đợt** (việc E2 đã làm) áp cho **cả hai** đường — `DeclareCycle`
chỉ gọi khi đã có mã đợt thật, tuyệt đối không bịa.

### [ ] L2 — THÊM: mốc bậc Stage (thêm thuần, không trùng gì)

```csharp
FalconBigDataController.LiveOps.OnStep("endless_treasure", FLiveOpsAction.Milestone, stage: position.stage);
```

Hiện "đi được tới bậc nào" chỉ suy được từ dòng nhận quà — tức lẫn với "đã nhận quà".

### [ ] L3 — THÊM: `Stamp` lên bốn chỗ đã có

```csharp
var liveOps = new LiveOpsRef();
FalconBigDataController.LiveOps.Stamp(liveOps, "endless_treasure");
FalconBigDataController.Ui.OnImpression(SURFACE_ICON, liveOps: liveOps);              // icon Lobby

FalconBigDataController.LiveOps.Stamp(offerParam, "endless_treasure", stage: nextPaidStage);
FalconBigDataController.LiveOps.Stamp(inAppParam, "endless_treasure", stage: boughtStage);
FalconBigDataController.LiveOps.Stamp(rewardCtx,  "endless_treasure", stage: position.stage);
```

⚠ Icon: 4 giá trị `live_ops_*` vào **khoá gộp cụm**, nên **số dòng cụm tăng** (tách theo đợt) dù
**tổng số lần hiện không đổi**. Chart nào đếm `SUM(count)` thì đúng; chart đếm `COUNT(*)` sẽ vống
lên — nhắc data team một câu.

⚠ Dòng thưởng: `Stamp` vào **context** rồi truyền xuống `Grant(..., context)` như module đang làm.
**Không** gọi thêm `Resource.OnEarned`.

### Giữ nguyên — KHÔNG bỏ gì ở pha 1

| Thứ | Trạng thái |
|---|---|
| Funnel `event_endless_treasure` bước `open` + `cycleIndex`/`cycleStartTs`/`cycleEndTs` | **Giữ** |
| `endless_treasure_cycle_id`, `endless_treasure_stage` | **Giữ** (song song với `live_ops_cycle_id` / `live_ops_stage`) |
| `endless_treasure_progress` | **Giữ lâu dài** — độ sâu trong đợt là chiều riêng, không cột chung nào thay được |
| `offerId`, `where = endless_treasure`, `offerProductId`, `exchangeId`, `transactionId` | **Giữ** — không đụng |

### Hai tên một giá trị

| Cũ | Mới |
|---|---|
| `endless_treasure_cycle_id` | `live_ops_cycle_id` |
| `endless_treasure_stage` | `live_ops_stage` |

### Pha 2 — bao giờ mới được BỎ cái cũ

Đủ **cả bốn** mới bỏ, và bỏ riêng cho event này:

1. Dashboard mới dựng xong và **khớp số với dashboard cũ trong TRỌN một đợt** (không phải vài ngày).
2. ~~Loader đã ký `f_sdk_live_ops_data`~~ → ✅ **ĐÃ KÝ 20/10**. Còn lại: seed mapping rule.
3. Bỏ **đúng ranh giới đợt**. Bỏ giữa đợt là có một đợt nửa cũ nửa mới — mọi phân vị và mẫu số của
   đợt đó hỏng mà trông vẫn hợp lý. Đây là cái bẫy lớn nhất của cả đợt chuyển.
4. Đã ghi mốc era (phía kho đọc được từ `sdk_version` + `live_ops_event IS NOT NULL`).

### Hai chỗ dễ sai khi làm pha 1

- ⚠ **Gói hai đường vào MỘT hàm** của class tracking event này, đừng để call site tự gọi từng bên.
  Sớm muộn có người thêm một mốc ở một bên và quên bên kia — lệch kiểu đó không ai thấy.
- ⚠ **KHÔNG gọi thêm `Resource.OnEarned` để "chở ngữ cảnh live-ops"**. `Stamp` điền thêm cột vào
  chính param đang gửi, **không sinh dòng mới**. Gọi thêm là đếm đôi tiền — đúng vết sẹo log-đôi
  rank reward của Master League.

---

# ⚠ KẾT QUẢ RÀ CODE 16/10 — việc phải sửa trước khi ship

> Rà bản module dev game vừa import (16/10) đối chiếu hợp đồng
> `docs/design/LiveOpsEventContract.md` + mục 🔁 ở trên. 🔴 = làm sai số liệu hoặc lỗi hiệu năng,
> sửa trước khi ship · 🟡 = nên sửa · 🟢 = đã đúng, ghi nhận để khỏi sửa lại.

**♻ RÀ LẠI 19/10** (bản dev vừa chỉnh): **R1 xong → module này hết việc.**

### [x] R2 🔴 LỖI SDK (phần mình) — dòng IAP **THẤT BẠI** mất ngữ cảnh event — ✅ MÌNH VÁ XONG 20/10

> ✅ **20/10 — xong, ở phía SDK.** `PurchaseAttemptState` nhớ 4 param `liveOps*` lúc mở lượt và copy
> sang param của dòng fail — đúng khuôn `extraMeta` vẫn làm. `Clear()` xoá cả bốn, nên lượt mua shop
> thường ngay sau đó **không thừa kế** event của lượt trước (có test ghim riêng ca này).
> **Module Endless Treasure không phải sửa gì.** Lên **BigData 1.5.4**.

Tích hợp phía game **đúng**: `EndlessTreasureCustom_Wool.RequestPurchase` gọi
`BigdataLogger.CustomLiveOps(EndlessTreasureTracking.PurchaseLiveOps(stageConfig))` **trước** khi mở
lượt mua, nên dòng `started` và dòng `purchased` đều mang 4 param `live_ops_*`.
(Và `_liveOps` được **xoá ở cả hai nhánh kết thúc** — `Log()` lúc thành công, và hook
`IAPManager.onPurchaseFailed`. Nên không có chuyện ET "dính" sang giao dịch shop thường sau đó. Đây
đúng là cái bẫy sticky-static mình soi đầu tiên, và nó đã được xử đúng.)

Chỗ hở nằm ở **SDK của mình**: `PurchaseAttemptState.TakeFail()` dựng lại param cho dòng fail từ
state lượt đang mở và mang `productId` / `where` / giá / tiền tệ / `extraMeta` — nhưng **không mang
4 param live-ops**. Mà `FIapApi.OnFailed` **cố ý không nhận param** (payload do SDK dựng lại), nên
phía game cũng không chèn vào được.

→ Phễu mua trong event **lệch một nhánh**: `started` và `purchased` gắn event, `failed` thì không.
Câu *"mua trong Endless Treasure thất bại bao nhiêu %"* ra sai, và sai theo chiều **trông như không
ai thất bại**.

Sửa ở phía mình: `PurchaseAttemptState` nhớ 4 giá trị đó lúc mở lượt rồi copy vào fail param — đúng
khuôn `extraMeta` đang làm. **Module ET không phải sửa gì.**

### 🟢 Module này đạt chuẩn — chỉ còn một việc nhỏ

Đây là module **làm đúng nhất** trong bốn module rà ngày 16/10:

| Hạng mục | Kết quả |
|---|---|
| `DeclareCycle` đủ **cả bốn** field (`id` · `index` · `startTs` · `endTs`) | ✅ **Module duy nhất đủ** — ba module kia đều thiếu `cycleStartTs` |
| Giữ funnel `open` cũ **và** thêm live-ops `open` | ✅ Đúng pha 1 |
| Thêm dòng `milestone` cho bậc Stage | ✅ Trước đây "đi tới bậc nào" phải suy từ dòng nhận quà, tức lẫn với "đã nhận quà" |
| Icon Lobby truyền `LiveOpsRef` | ✅ Bốn giá trị vào khoá gộp cụm → hai đợt không bị nén một dòng |
| `PurchaseLiveOps` cho log mua + `Stamp` cho offer | ✅ |
| Dòng thưởng `Stamp(stage)` qua context, không gọi thêm `Resource.OnEarned` | ✅ Không đếm đôi |
| Luật **hoãn-gửi-khi-chưa-biết-mã-đợt** (việc E2) | ✅ Giữ nguyên ở cả hai đường |
| Mã đợt dùng **id server** (`cycle.id`) | ✅ Không bịa — khác hai module Race/Sky Path đang lấy ngày kết thúc |

### [x] R1 🟡 Pin `package.json` sai — ✅ SỬA XONG 19/10

> ✅ **19/10 — xong.** `falcon.modules.core.bigdata: 1.5.1`. **Module này hết việc** — rà lại 19/10
> không thấy điểm nào phải sửa.
>
> ⚠ Một việc nhỏ thêm chiều 19/10: nâng pin lên **`1.5.2`**. Module này không chạm lỗi sổ mốc
> rõ như Race/Sky Path, nhưng bản vá còn gồm *"đổi đợt thì xoá lịch đợt cũ"* — mà `DeclareCycle`
> của Endless Treasure luôn truyền đủ bốn field nên nó chỉ được lợi, không mất gì.

`bigdata: 1.3.9` → **≥ 1.5.1** (code gọi `LiveOps.*`, `LiveOpsRef`).

⚠ Một việc nhắc lại cho data team, không phải lỗi code: khi icon bắt đầu mang ngữ cảnh event thì
**số dòng** cụm `f_sdk_ui_exposure` tăng (tách theo đợt) dù **tổng số lần hiện không đổi** — chart
phải `SUM(count)`, không `COUNT(*)`.

---

# ✅ TODO

## A. Dev Endless Treasure (module hoặc game — ai viết tính năng này)

### [ ] A1 — Chép class tracking vào project

Chép nguyên class dưới đây (không cần sửa gì). Dev chỉ **gọi** các hàm của nó ở các bước sau.

```csharp
using System;
using System.Collections.Generic;
using Falcon.Modules.Core.BigData;

public static class EndlessTreasureTracking
{
    public const string FUNNEL_NAME   = "event_endless_treasure";
    public const string RESOURCE_WHEN = "event_endless_treasure";
    public const string OFFER_ID      = "endless_treasure";
    public const string SURFACE_ICON  = "endless_treasure_icon";

    public const string WHERE_FREE_STEP = "free_step";
    public const string WHERE_PAID_STEP = "paid_step";

    public const string KEY_CYCLE_ID = "endless_treasure_cycle_id";
    public const string KEY_PROGRESS = "endless_treasure_progress";
    public const string KEY_STAGE    = "endless_treasure_stage";

    /// <summary>Thông tin ĐỢT — mọi số đọc từ config/data của game.</summary>
    public struct Cycle
    {
        public string id;       // id đợt do game/server cấp; không có thì dùng ngày bắt đầu đợt "yyyy-MM-dd"
        public int index;       // đợt thứ mấy của user (1, 2, 3…)
        public long startTs;    // giờ đợt bắt đầu, epoch ms UTC — đọc từ data, KHÔNG tự tính
        public long endTs;      // giờ đợt kết thúc, epoch ms UTC — đọc từ data, KHÔNG tự tính
    }

    /// <summary>Vị trí của user trong chuỗi.</summary>
    public struct Position
    {
        public int progress;    // bậc thứ mấy trong ĐỢT (1, 2, …, 40, 41…) — tăng mãi, về 1 khi reset đợt
        public int stage;       // Stage trong CONFIG (vd lặp 34→39 thì bậc 41 có stage = 35)
    }

    // ── Popup ─────────────────────────────────────────────────────────────

    /// <summary>Popup hiện ra (MỖI lần hiện).</summary>
    public static void OnPopupShown(Cycle cycle, Position pos, string nextPaidProductId,
        string triggerEvent, OfferTriggerType triggerType)
    {
        FalconBigDataController.Funnel.OnStep(new FunnelParam
        {
            funnelName = FUNNEL_NAME,
            cycleId = cycle.id,
            action = "open",
            priority = 10,
            cycleIndex = cycle.index,
            cycleStartTs = cycle.startTs,
            cycleEndTs = cycle.endTs
        });

        FalconBigDataController.IapOffer.OnShown(new IapOfferParam
        {
            offerId = OFFER_ID,
            offerCategory = "progressive",
            offerLayout = "chain",
            offerSurfaceType = OfferSurfaceType.Popup,
            placementScene = "lobby",
            triggerType = triggerType,
            triggerEvent = triggerEvent,
            discountRate = 0f,
            offerProductId = string.IsNullOrEmpty(nextPaidProductId) ? null : new[] { nextPaidProductId },
            extraMeta = Meta(cycle, pos)
        });
    }

    /// <summary>User bấm nút mua trên popup.</summary>
    public static void OnPopupClicked() => FalconBigDataController.IapOffer.OnClicked();

    /// <summary>Popup đóng (tắt, mua xong, rời màn hình).</summary>
    public static void OnPopupClosed() => FalconBigDataController.IapOffer.OnClosed();

    // ── Icon Lobby ────────────────────────────────────────────────────────

    public static void OnIconShown(Cycle cycle) =>
        FalconBigDataController.Ui.OnImpression(SURFACE_ICON, CycleMeta(cycle));

    public static void OnIconClicked(Cycle cycle) =>
        FalconBigDataController.Ui.OnClicked(SURFACE_ICON, CycleMeta(cycle));

    // ── Nhận quà ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ngữ cảnh cho MỘT lần nhận quà của một Stage — dùng chung cho mọi món của Stage đó.
    /// isPaid = true với nội dung bundle trả phí; transactionId của giao dịch IAP nếu lấy được.
    /// </summary>
    public static ResourceParam RewardContext(Cycle cycle, Position pos, bool isPaid,
        string transactionId = null)
    {
        return new ResourceParam
        {
            resourceWhen = RESOURCE_WHEN,
            resourceWhere = isPaid ? WHERE_PAID_STEP : WHERE_FREE_STEP,
            exchangeId = Guid.NewGuid().ToString("N"),
            transactionId = transactionId,
            detail = Meta(cycle, pos)
        };
    }

    // ── Nội bộ ────────────────────────────────────────────────────────────

    private static Dictionary<string, object> Meta(Cycle cycle, Position pos) => new()
    {
        [KEY_CYCLE_ID] = cycle.id,
        [KEY_PROGRESS] = pos.progress,
        [KEY_STAGE] = pos.stage
    };

    private static Dictionary<string, object> CycleMeta(Cycle cycle) => new()
    {
        [KEY_CYCLE_ID] = cycle.id
    };
}
```

### [ ] A2 — Popup: gọi 3 hàm

```csharp
// Lúc popup HIỆN (mọi nguồn: lần đầu sau lv21, reset đợt, đầu session, sau khi thắng mỗi N giây, bấm icon)
EndlessTreasureTracking.OnPopupShown(cycle, pos,
    nextPaidProductId: skuCuaBacTraPhiKeTiep,        // SKU bundle user đang đứng trước
    triggerEvent: "icon_click",                      // bảng giá trị bên dưới
    triggerType: OfferTriggerType.SystemTriggered);  // bấm icon mở → UserInitiated

// User bấm nút MUA trên popup
EndlessTreasureTracking.OnPopupClicked();

// Popup ĐÓNG (tắt / mua xong / rời màn)
EndlessTreasureTracking.OnPopupClosed();
```

| Popup hiện vì | `triggerEvent` | `triggerType` |
|---|---|---|
| Lần đầu sau khi mở khoá | `unlock` | `SystemTriggered` |
| Đợt mới bắt đầu (tự bật lại) | `cycle_reset` | `SystemTriggered` |
| User bấm icon Lobby | `icon_click` | `UserInitiated` |

⚠ Bảng trên là **hai đường mở popup đang có thật** (bản 1.5.0). Game bật thêm đường nào — đầu
session, sau khi thắng level… — thì thêm giá trị `triggerEvent` mới; **đừng để rơi vào `unknown`**
(xem E4).

⚠ `nextPaidProductId` **bắt buộc đúng SKU** của bundle đang bày — SDK dựa vào nó để tự nối lần
popup này với giao dịch mua (conversion). Stage đang là quà free thì truyền SKU của bundle trả phí
kế tiếp; không còn bundle nào thì `null`.
**Kiểm:** mở popup rồi tắt → một dòng `f_sdk_iap_offer_data` `offerId = endless_treasure`, có
`endless_treasure_stage`; mua trong popup → dòng `f_sdk_in_app_data` mang `offerImpressionId`.

### [ ] A3 — Icon Lobby: gọi 2 hàm

```csharp
// Icon HIỆN RA với user (OnEnable của icon, khi đang trong đợt) — gọi thoải mái, SDK tự gộp cụm
EndlessTreasureTracking.OnIconShown(cycle);

// User BẤM icon (rồi mở popup → A2 với triggerEvent = "icon_click")
EndlessTreasureTracking.OnIconClicked(cycle);
```

Chưa biết đợt (config chưa về) thì **đừng gọi** — log thiếu `endless_treasure_cycle_id` là không
quy được về đợt nào.

### [ ] A4 — Mua bundle: truyền `where`

Chỗ gọi hàm mua của module IAP, truyền placement:

```csharp
// where / placement của lượt mua:
FIapPlacement.EndlessTreasure        // = "endless_treasure"
```

Module IAP tự log `Iap.OnStarted` / `OnPurchased` như mọi gói khác — dev Endless Treasure **không**
gọi thêm. Đợt / Stage của giao dịch server lấy qua `offerImpressionId` (A2), không cần gắn lên log
mua.
**Kiểm:** dòng `f_sdk_in_app_data` của bundle có `where = endless_treasure`.

### [ ] A5 — Nhận quà: chọn đúng trường hợp

Tạo context **một lần cho một Stage**, dùng chung cho mọi món quà của Stage đó:

```csharp
var ctx = EndlessTreasureTracking.RewardContext(cycle, pos, isPaid: false);          // Stage free
var ctx = EndlessTreasureTracking.RewardContext(cycle, pos, isPaid: true, txId);     // bundle trả phí
```

Rồi cộng quà theo **đúng một** trong hai trường hợp:

**Trường hợp 1 — Endless Treasure tự cộng quà vào kho** (quà free, hoặc bundle mà code Endless
Treasure tự cộng sau khi mua xong):

```csharp
// Cộng thẳng vào kho:
ResourceCollector.Instance.ResourceAdd(resourceId, amount, data, "endless_treasure", param: ctx);

// Hoặc qua bộ phát thưởng của Events Core:
EventRewardGranter.Grant(reward, "endless_treasure", context: ctx);
```

Cần GameData ≥ 1.1.7 (và Events Core legacy bản có `Grant(..., context)` nếu dùng dòng thứ hai).

**Trường hợp 2 — Nội dung bundle do module Packs cộng** (bundle làm bằng `APackElement`): Packs
hiện **không có chỗ nhận context** → phải chờ owner Packs làm thư
`docs/modules/Request-Packs-RewardContext.md`. Sau khi Packs có bản mới, pack Endless Treasure
override:

```csharp
protected override ResourceParam GetRewardContext() =>
    EndlessTreasureTracking.RewardContext(cycle, pos, isPaid: true);
```

⚠ **Một món quà chỉ MỘT dòng resource.** Đã đi qua kho (trường hợp 1 hoặc 2) thì **không** gọi
thêm `FalconBigDataController.Resource.OnEarned` — gọi thêm là đếm đôi.
**Kiểm:** nhận Stage free có 2 món → đúng 2 dòng `f_sdk_resource_data`, mỗi dòng
`resourceWhen = event_endless_treasure`, `resourceWhere = free_step`, có `detail.endless_treasure_stage`.

### [ ] A6 — Tự kiểm lại luật "số đọc từ config"

- `cycle.startTs` / `cycle.endTs` lấy từ data đợt thật, **không** `now + 168h`.
- `pos.stage` lấy từ config Stage đang hiển thị, **không** tự tính `34 + (n − 34) % 6`.
- `pos.progress` về **1** khi đợt reset; `cycle.index` tăng 1.

## E. Bốn việc từ đợt rà code 29/09 — ✅ ĐÃ XONG CẢ BỐN

Client sửa trong cùng ngày, mình rà lại và xác nhận. Giữ mục này để tra lý do, **không còn việc
phải làm**.

| # | Việc | Cách client sửa |
|---|---|---|
| E1 | Popup không bật lại mỗi đợt | Cờ bool đổi thành **cờ theo mã đợt**; `AutoShowTrigger` trả `unlock` cho lần đầu đời máy và `cycle_reset` cho đợt mới |
| E2 | Bịa mã đợt khi chưa có data | Bỏ hẳn nhánh bịa. Icon **đếm dồn rồi gửi bù**; popup **giữ cờ chờ**, tới lúc đóng mà vẫn chưa có mã thì gửi với mã **để trống** |
| E3 | Nhịp đếm icon | Dev xác nhận `OnEnable` của lớp cha gọi lại `SetView`, nên icon được đếm mỗi lần hiện ra. Giữ nguyên chỗ gọi |
| E4 | Lý do mở popup rơi vào `unknown` | Thêm **cảnh báo lúc chạy** kèm câu chỉ thẳng phải gọi `ShowMainPopup(trigger)` |


### [x] E1 🔴 — Popup có bật lại ở đợt mới không?

**Hiện trạng trong code.** `DeclareEndlessTreasure.CanAutoShowMain` chặn bằng
`HasMainBeenShown`, mà cờ đó lưu trong `PlayerPrefs` (`MAIN_AUTO_SHOWN_PREF_KEY`) và **không bao
giờ reset**. Tức popup tự bật **đúng một lần trong đời máy**, không phải mỗi đợt. Hằng
`TRIGGER_CYCLE_RESET` khai ra nhưng **không chỗ nào gọi**.

**Hậu quả nếu đây là lỗi:**
- Từ đợt thứ hai trở đi, user **không được mời** — muốn biết có đợt mới phải tự bấm icon. Đây là
  chuyện doanh thu, không chỉ là tracking.
- Số "đã xem gói bán" mỗi đợt tụt mạnh. Ai nhìn số mà tưởng popup vẫn bật hằng tuần sẽ kết luận
  nhầm là "gói bán kém hấp dẫn".

👉 **Câu hỏi cho dev:** cố ý (chỉ mời một lần đời máy) hay thiếu reset theo đợt?

Nếu là thiếu, cách sửa: thay cờ bool bằng **mã đợt đã từng tự bật**, và truyền trigger tương ứng:

```csharp
// thay HasMainBeenShown (bool) bằng: đợt nào đã tự bật popup
var shownCycle = PlayerPrefs.GetString(MAIN_AUTO_SHOWN_CYCLE_KEY, "");
var canAutoShow = shownCycle != currentCycleId;      // đợt mới -> bật lại

// và khi tự bật:
DeclareEndlessTreasure.ShowMainPopup(
    shownCycle == "" ? EndlessTreasureTracking.TRIGGER_UNLOCK       // lần đầu đời máy
                     : EndlessTreasureTracking.TRIGGER_CYCLE_RESET); // đợt mới
```

### [x] E2 🟡 — Chưa biết mã đợt thì HOÃN gửi, đừng bịa mã

**Hiện trạng.** `EndlessTreasureTracking.DefaultCycle()` có nhánh dự phòng: chưa có dữ liệu đợt
thì lấy **ngày kết thúc** (`yyyy-MM-dd`) làm mã, trong khi lúc có dữ liệu thì mã là **số**
(`cycleId`). Cùng một đợt mang hai mã khác nhau.

**Hậu quả:** dòng mở popup mang mã ngày, dòng mua mang mã số → **không nối được**, đọc ra thành
"mở popup xong không ai mua". Đếm người tham gia mỗi đợt cũng thiếu. Đợt nào mạng chậm nhiều thì
lệch nhiều, mà lệch bao nhiêu thì không đo được.

**Luật đúng — 3 bước, theo thứ tự:**

1. **Bỏ nhánh bịa mã theo ngày kết thúc.**
2. **Chưa có mã thì GIỮ LẠI mốc đó** (không vứt), gửi ngay khi dữ liệu đợt về trong cùng phiên.
   Icon thì nhớ số lần đã hiện rồi gửi bù; popup thì giữ cờ "đang chờ" rồi bắn `OnShown` khi có mã.
3. **Tới lúc đóng popup / thoát app mà mã vẫn chưa về** thì **vẫn gửi**, nhưng để **trống**
   `endless_treasure_cycle_id`. Trống nghĩa là "không biết đợt" — đọc được; còn bịa mã là **sai
   âm thầm** và trộn hai loại mã vào một cột.

👉 Nguyên tắc: **không được mất dữ liệu, cũng không được bịa.** Hoãn trước, thiếu-key sau, bịa thì không.

### [x] E3 🟡 — Icon có được đếm mỗi lần user nhìn thấy không?

**Hiện trạng.** `EndlessTreasureButton` gọi `OnIconShown()` trong `SetView` — tức lúc **dựng icon
và gắn dữ liệu**, không phải lúc icon xuất hiện trên màn hình.

**Hậu quả nếu `SetView` chỉ chạy một lần mỗi phiên:** số lần "user thấy gói bán" bị hụt, trong khi
số lần bấm vẫn đếm đủ → **tỉ lệ bấm cao giả tạo**. Đây đúng vết xe Go-Kart: 67 người mở event mà
chỉ 4 người được ghi là thấy nút.

✅ **Dev đã xác nhận (29/09): `OnEnable` của lớp cha gọi lại `SetView`**, nên icon được đếm mỗi
lần hiện ra với user. Giữ nguyên chỗ gọi.

⚠ Nếu sau này đổi lớp cha hoặc tự dựng icon riêng thì kiểm lại điểm này — đây là chỗ Go-Kart từng
sai (67 người mở event, 4 người được ghi là thấy nút).

### [x] E4 🟢 — Mọi đường mở popup phải khai lý do

**Hiện trạng.** `ConsumePopupTrigger` rơi về `unknown` khi không ai set trigger. Hiện chỉ có hai
đường mở và cả hai đều khai, nên chưa sao.

**Hậu quả nếu sau này thêm đường mở mà quên khai:** câu "cách mời nào hiệu quả nhất" không trả lời
được, vì một phần dữ liệu nằm trong rổ `unknown` — mà **không có gì báo lỗi**, số vẫn ra bình
thường. Đây là loại lỗi tích tụ: hôm nay 0%, nửa năm sau 40%.

👉 **Luật:** thêm đường mở popup thì thêm một giá trị `triggerEvent` mới, gọi
`SetPopupTrigger(...)` ngay trước khi mở.

## B. Owner module Packs — chỉ khi trường hợp 2 ở A5

### [ ] P1 — Làm thư `docs/modules/Request-Packs-RewardContext.md`

Thêm hook `GetRewardContext()` vào `APackElement` + payload mới chở context + `GameDataConnector`
truyền `param` xuống kho. Chi tiết từng dòng trong thư.

## C. BigData — đã xong

- [x] Hằng `FIapPlacement.EndlessTreasure = "endless_treasure"` (BigData 1.3.9).

## D. Báo data team / loader

### [ ] D1 — Đăng ký vocab

- Funnel `event_endless_treasure` (shape `once_per_cycle`, action `open`).
- `offerId = endless_treasure`, `offerCategory = progressive`, `offerLayout = chain`.
- Placement IAP `endless_treasure` (thêm giá trị vào hợp đồng placement — loader ký).
- `resourceWhen = event_endless_treasure`; `resourceWhere = free_step` / `paid_step`.
- Surface Ui `endless_treasure_icon`.
- Key: `endless_treasure_cycle_id`, `endless_treasure_progress`, `endless_treasure_stage`.

### [ ] D2 — Ba điều dặn người đọc số (hiện trạng 1.5.0, không phải lỗi)

1. **Bấm icon lúc đang khoá hoặc mất mạng vẫn tính là click.** "Click" nghĩa là *bấm*, không phải
   *mở được popup*.
2. **Một lần mở popup có thể sinh nhiều dòng offer.** Mua xong một bậc thì bậc trả phí kế tiếp
   đổi, module đóng offer cũ rồi mở offer mới. Đừng đọc số dòng offer thành "số lần mở popup".
3. **Dòng thiếu `endless_treasure_cycle_id`** nghĩa là mốc xảy ra khi app chưa nhận được dữ liệu
   đợt (xem E2). Đếm nó vào tổng, nhưng đừng quy về đợt nào.
4. **Ca hiếm: có dòng offer nhưng KHÔNG có bước funnel `open`.** Xảy ra đúng khi popup mở và đóng
   trước lúc dữ liệu đợt về — offer vẫn gửi (thiếu mã đợt), còn bước funnel thì bỏ vì không có mã
   thì van lọc trùng theo chu kỳ mất nghĩa. Nên số "đã mở UI" đếm theo funnel hụt đúng phần đó;
   muốn đếm đủ thì đếm theo dòng offer.

---

# 📖 Giải thích & tham chiếu (không cần đọc để sửa)

## Vì sao thiết kế như vậy

| Quyết định | Lý do |
|---|---|
| Không log level, không funnel tiến độ | Endless Treasure không có ván chơi riêng. Tiến độ chuỗi suy từ dòng nhận quà (Stage free) và dòng mua (bundle) — log thêm funnel là trùng |
| Funnel chỉ có bước `open` | Chỗ duy nhất ghi **lịch đợt** bằng field chuẩn (`cycleIndex` / `cycleStartTs` / `cycleEndTs`) — một lần mỗi đợt (SDK tự nén). Các log khác chỉ cần `endless_treasure_cycle_id` để nối về |
| Popup đi `IapOffer`, icon đi `Ui` | Popup là lần hiển thị chủ đích → mỗi lần một dòng, có thời lượng, tự nối giao dịch. Icon nằm yên trên màn hình → log mỗi lần hiện là quá nhiều dòng; `Ui` gộp cụm theo ngày |
| Hai số `progress` + `stage` | `progress` = độ sâu (user đi được bao xa, dừng ở đâu). `stage` = món nào trong config. Luật lặp (34→39 hay kiểu khác) nằm hết trong config game, server không cần biết |
| Mọi số đọc từ config/data | Mỗi game một lịch, một luật lặp. Hardcode ở server hay ở class tracking là lệch câm khi game đổi config |
| `where` trên log mua, không gắn đợt/Stage | Module IAP log giao dịch; đợt/Stage đã nằm trên dòng popup, nối qua `offerImpressionId`. SKU dùng riêng nên `productId` cũng tự nhận ra gói |
| Tiền tố `endless_treasure_` | Luật tiền tố live-op (`LiveOpsTrackingPattern.md` §1) — tránh đụng key event khác |

**`itemType` / `itemId` trên dòng resource là cột legacy** — GameData đang điền bằng tham số
`where`, không dùng để phân tích. "Type" của quà đọc từ `currency` (= id tài nguyên).

## Chỉ số GD → log

| # | Chỉ số | Cách tính |
|---|---|---|
| 1 | Impression + CTR theo ngày | Popup: đếm dòng `f_sdk_iap_offer_data` `offerId = endless_treasure`; CTR = dòng `isClicked = true` / tổng. Icon: `f_sdk_ui_exposure` `surfaceId = endless_treasure_icon`, `sum(count | click) / sum(count | impression)`. Tách popup theo `triggerEvent` để biết nguồn nào hiệu quả |
| 2a | Số lượng + % từng gói lẻ theo đợt | `f_sdk_in_app_data` `where = endless_treasure` GROUP BY `productId`, đợt (qua `offerImpressionId` → `endless_treasure_cycle_id`, hoặc tuần của giao dịch) |
| 2b | Rev từng gói lẻ theo đợt | Như trên, `SUM(localizedPrice)` quy USD |
| 2c | Rev Endless Treasure so với tổng IAP tuần | Rev `where = endless_treasure` / Rev toàn bộ `f_sdk_in_app_data` cùng tuần |
| 3 | Source (theo type) so với toàn bộ source | `f_sdk_resource_data` `flowType = Source`: phần `resourceWhen = event_endless_treasure` / toàn bộ, GROUP BY `currency`. Tách free / trả phí bằng `resourceWhere` |
| 4 | First-time purchase so với gói khác | Giao dịch đầu đời của mỗi user rơi vào `where` nào. Conversion = distinct user mua / distinct user có popup (dòng offer) |
| 5 | Tỉ lệ mua lại theo tuần | User mua ở đợt N có mua ở đợt N+1 không (đợt qua `endless_treasure_cycle_id` / `cycleIndex`) |
| 6 | Tỉ lệ mua gói value cao hơn theo tuần | Theo user qua các tuần: giá (`localizedPrice`) hoặc `endless_treasure_stage` cao nhất đã mua |

## Main concern → cách đo

| Concern | Đo | Giới hạn |
|---|---|---|
| 1. Payrate thấp do thiết kế / value | Phễu popup hiện → bấm (`isClicked`) → bắt đầu mua (`f_sdk_iap_start_purchase_data`) → mua xong; kèm `max(endless_treasure_progress)` mỗi đợt = user dừng ở bậc nào | Tách được "không bấm" với "bấm mà không mua"; tách thiết kế với value cần GD thử biến thể |
| 2. High value payer chờ gói rẻ | Theo user qua các tuần: giá trung bình / cao nhất của **toàn bộ** giao dịch, trước và sau khi mở khoá | Cần vài tuần dữ liệu |
| 3. Cannibal gói khác | Doanh thu gói khác của user đã thấy Endless Treasure, trước / sau mở khoá | ⚠ **Không kết luận được nếu thiếu nhóm đối chứng** — user qua lv mở khoá tự nhiên chi khác. Đề xuất **A/B holdout** (tắt Endless Treasure cho 10–20% user đủ điều kiện, dùng nhãn AB sẵn có trên mọi log) |

## Thông tin GD đã chốt (22/09)

- Đợt dài một tuần; mỗi đợt reset về Stage 1, quà free nhận lại được.
- Chuỗi vô tận, lặp lại một đoạn Stage (bản hiện tại: 34 → 39).
- SKU các bundle dùng riêng, không chung với gói khác.
- Lobby có icon riêng.

Các số trên là **cấu hình của bản hiện tại** — tracking không dựa vào chúng, dev luôn đọc từ config
(A6).
