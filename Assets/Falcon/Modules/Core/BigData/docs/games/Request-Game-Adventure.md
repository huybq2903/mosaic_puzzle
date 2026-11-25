# Tracking Adventure — TODO cho dev + tham chiếu chart

> **Dev chỉ cần đọc phần TODO ngay dưới đây.** Không có API mới, không có event mới — chỉ gắn thêm
> trường vào hai loại log game **đã có sẵn**. Tick `[x]` khi xong.
> Viết 28/09/2026. Tiền tố key: **`adventure_`**.

Adventure chạy trên **level thường**, không có bộ level riêng, **không có mốc tham gia** (đủ level
là tự có), và **thưởng tự nhận** ngay sau khi thắng. Vì vậy phần log gọn hơn hẳn Master League.

# 🔁 LÀM THEO HỢP ĐỒNG LIVE-OP (event này CHƯA lên dữ liệu → không có pha song song)

> Chốt 15/10/2026. Hợp đồng: `docs/design/LiveOpsEventContract.md` · Khuôn:
> `docs/design/LiveOpsTrackingPattern.md`. SDK **BigData 1.5.0** đã có đủ API; chờ loader ký
> `f_sdk_live_ops_data` trước khi **game ship** (up package thì không chặn).
> **Khoá event của event này: `adventure`** (để trần, không tiền tố `event_`).
>
> Event này **chưa có dòng log nào trên kho**, nên không phải giữ gì để tương thích: làm thẳng theo
> mục này, bỏ qua chỗ nào trong phần dưới trái với nó.
> ⚠ Ba event ĐANG CHẠY (Master League · Race · Endless Treasure) thì khác — chúng chạy **pha song
> song**, xem doc của chúng.


### [ ] L1 — Dòng live-ops (doc cũ nói "không cần funnel" — **đảo lại**)

```csharp
FalconBigDataController.LiveOps.DeclareCycle(
    "adventure", cycleId: cycleId, cycleIndex: weekIndex,
    cycleStartTs: startMs, cycleEndTs: endMs);

FalconBigDataController.LiveOps.OnStep("adventure", FLiveOpsAction.Eligible);   // MỖI PHIÊN
FalconBigDataController.LiveOps.OnStep("adventure", FLiveOpsAction.Milestone, stage: milestoneIndex);
```

Vì sao đảo: **lịch đợt, mẫu số và ba thước đo khoảng cách** (mất bao lâu / bao nhiêu level / bao
nhiêu lượt chơi để tới mốc) đều nằm trên dòng live-ops. Không có dòng nào thì event này **vô hình
trong catalog** và không trả lời được "mốc 12 gắt cỡ nào".

Chi phí: một dòng `eligible` mỗi user mỗi đợt + một dòng mỗi mốc. Van lọc nén sẵn.

⚠ `eligible` gọi **mỗi phiên** khi user đủ điều kiện và đợt đang chạy — không phải một lần lúc mở
khoá. Bắn kiểu một-lần thì từ đợt thứ hai mẫu số rỗng (vết sẹo Master League G1).

### [ ] L2 — Dòng thưởng mốc: `Stamp` thay cho key riêng

```csharp
var ctx = new ResourceParam { resourceWhen = "event_adventure", resourceWhere = "milestone_reward" };
FalconBigDataController.LiveOps.Stamp(ctx, "adventure", stage: milestoneIndex);
```

`detail.adventure_milestone` và `detail.adventure_cycle_id` **không cần nữa** (chưa lên dữ liệu nên
bỏ không mất gì) — `live_ops_stage` / `live_ops_cycle_id` thay. Mọi luật khác của mục A2 giữ nguyên:
mỗi món một dòng, `exchangeId` riêng từng mốc, một món một dòng.

### [ ] L3 — Log level: **ĐỪNG** khai event

Adventure chạy **chồng** lên level thường và có thể cùng lúc với Sky Path / Race → một ván không
thuộc riêng event nào, khai là bịa. **Không** gọi `Stamp` cho param level.

Giữ nguyên trên log level: `adventure_cycle_id` (nhãn "ván này trong event" — chart win rate
trong/ngoài event đọc từ đó), `adventure_points`, `adventure_winstreak`, `adventure_units`,
`adventure_multiplier`.

⚠ **KHÔNG gọi thêm `Resource.OnEarned` để "chở ngữ cảnh live-ops"**. `Stamp` điền thêm cột vào
chính param đang gửi, **không sinh dòng mới** — gọi thêm là đếm đôi tiền (vết sẹo log-đôi của
Master League).

---

# ✅ TODO

## A. Dev game

### [ ] A1 — Gắn 4 trường vào log level (ván chơi)

Game đã gọi `Level.OnStart` / `OnPass` / `OnFail` rồi, chỉ thêm `extraMeta`:

```csharp
// ─── Bắt đầu ván ───
FalconBigDataController.Level.OnStart(new LevelStartParamV2
{
    currentLevel = level,
    difficulty = difficulty,              // đã là cột chuẩn của log level, KHÔNG cần key riêng
    extraMeta = new Dictionary<string, object>
    {
        ["adventure_cycle_id"]  = cycleId,          // mã đợt đang chạy
        ["adventure_winstreak"] = streakTruocVan    // chuỗi thắng TRƯỚC ván này
    }
});

// ─── Thắng ───
FalconBigDataController.Level.OnPass(new LevelPassParamV2
{
    // … các trường đang truyền
    extraMeta = new Dictionary<string, object>
    {
        ["adventure_cycle_id"]  = cycleId,
        ["adventure_winstreak"] = streakSauVan,     // mốc chuỗi đã dùng để nhân điểm
        ["adventure_points"]    = diemVanNay,       // số CUỐI CÙNG, đã nhân hết
        ["adventure_units"]     = soXeGoDuoc,
        ["adventure_multiplier"] = heSoX2           // 1 nếu không có x2
    }
});

// ─── Thua ───
FalconBigDataController.Level.OnFail(new LevelFailParamV2
{
    // … các trường đang truyền
    extraMeta = new Dictionary<string, object>
    {
        ["adventure_cycle_id"]  = cycleId,
        ["adventure_winstreak"] = streakSauKhiTut   // chuỗi SAU khi tụt mốc
    }
});
```

⚠ **Ván không thuộc event thì đừng gắn gì cả** (ngoài thời gian đợt, hoặc user chưa mở khoá).
Vắng `adventure_cycle_id` chính là dấu hiệu "ván này ngoài event" — và đó là cách tách nhóm cho
chart win rate trước/sau.

`adventure_points` phải là **số cuối cùng user thực nhận**, đã nhân đủ độ khó, chuỗi thắng và x2.
Đừng gửi điểm gốc rồi mong server tự nhân — server không biết ai đang có x2.

**Kiểm:** chơi một ván trong event → dòng level `Start` và `Pass` đều có `adventure_cycle_id`;
dòng `Pass` có `adventure_points` khớp con số hiện trên màn hình.

### [ ] A2 — Mỗi mốc thưởng nhận được: một dòng resource có ngữ cảnh

Mỗi **món quà** một dòng, đi qua kho tài nguyên kèm context:

```csharp
var ctx = new ResourceParam
{
    resourceWhen  = "event_adventure",
    resourceWhere = "milestone_reward",
    exchangeId    = Guid.NewGuid().ToString("N"),   // RIÊNG cho từng mốc
    detail = new Dictionary<string, object>
    {
        ["adventure_cycle_id"]  = cycleId,
        ["adventure_milestone"] = mocThuMay          // 1..25
    }
};

// cộng quà qua kho, kèm ctx:
ResourceCollector.Instance.ResourceAdd(resourceId, amount, data, "adventure", param: ctx);
// hoặc, nếu đi qua bộ phát thưởng của Events Core:
EventRewardGranter.Grant(reward, "adventure", context: ctx);
```

- Thắng một ván mà **qua nhiều mốc cùng lúc**: mỗi mốc **một `exchangeId` riêng**, đừng gộp — gộp
  thì không biết mốc nào cho ra món nào.
- ⚠ **Một món quà chỉ MỘT dòng.** Đã đi qua kho thì **không** gọi thêm
  `FalconBigDataController.Resource.OnEarned` — gọi thêm là đếm đôi (đúng lỗi của Master League).
- Cần GameData ≥ 1.1.7 (và Events Core bản có `Grant(..., context)` nếu dùng dòng thứ hai).

**Kiểm:** qua một mốc có 3 món quà → đúng 3 dòng, cùng `exchangeId`, cùng
`detail.adventure_milestone`, `resource_when = event_adventure`.

### [ ] A3 — Mã đợt lấy từ dữ liệu thật

`adventure_cycle_id` đọc từ **id đợt do server cấp**. Nếu server không có id thì dùng **ngày thứ
Hai mở đợt**, dạng `adventure_2026-09-28`.

⚠ Đừng để bên data tự suy "tuần bắt đầu thứ Hai": lịch trong spec và lịch chạy thật có thể lệch
(bài học Master League), và khi lệch thì không ai biết.

## B. Những thứ KHÔNG log

| Không log | Vì sao |
|---|---|
| Bước funnel cho 25 mốc | Mỗi mốc đã có dòng thưởng — heatmap, trung vị, funnel mốc đều đọc từ đó |
| Mốc "tham gia" | Event tự động, không có khoảnh khắc tham gia nào để ghi |
| Popup, anim, thanh tiến trình | Không chart nào dùng, volume thì lớn |
| Điểm tích luỹ hiện tại của user | Cộng dồn `adventure_points` là ra. Ghi trạng thái lên log là thừa và dễ lệch |
| Icon Home hiện / bấm | Chỉ cần khi nào muốn đo CTR icon. Hiện chưa chart nào hỏi |

## C. Báo data team

- Vocab `resourceWhen`: `event_adventure`. `resourceWhere`: `milestone_reward`.
- Key trên log level: `adventure_cycle_id`, `adventure_winstreak`, `adventure_points`,
  `adventure_units`, `adventure_multiplier`.
- Key trong `detail` của dòng thưởng: `adventure_cycle_id`, `adventure_milestone`.
- ⚠ **Quy ước đọc**: mọi chart theo mốc là **"mốc ĐÃ NHẬN QUÀ"**, không phải "mốc đã đạt". Hiện
  hai thứ trùng nhau vì thưởng tự nhận. **Hôm nào GD đổi sang cho user tự bấm nhận thì phải báo
  data team**, vì lúc đó ý nghĩa của chart đổi theo mà số vẫn ra bình thường.

---

# 📖 Chart đọc từ đâu

| Chart | Cách tính |
|---|---|
| **Heatmap mốc đạt được** | Dòng thưởng: distinct user theo `detail.adventure_milestone`, cắt theo `adventure_cycle_id` |
| **Trung vị mốc hoàn thành** | Mỗi user × đợt: `max(adventure_milestone)` → lấy trung vị |
| **Funnel pass mốc (cohort)** | Distinct user ở từng mốc 1→25. Mẫu số = user có ván chơi mang `adventure_cycle_id` trong đợt. Cohort chia theo đợt, theo level lúc vào event, hoặc theo ngày đầu tiên có điểm |
| **Mốc có gắt không** | Cộng dồn `adventure_points` của mỗi user tới lúc mốc nổ → ra ngưỡng thực tế. Đối chiếu với chỗ tụt người trong heatmap |
| **Phân bổ win streak theo ngày (biểu đồ chồng)** | Đếm ván theo `adventure_winstreak` và theo ngày. ⚠ Dùng key này. Cột `winStreak` của SDK **đã bỏ** (14/10) nên không còn chỗ nào lẫn: chuỗi duy nhất trên dữ liệu là con số game thật sự dùng để nhân điểm |
| **Win rate trước / sau event, cùng level** | Stream level: `Pass` / `Fail` theo `currentLevel`, so ván **có** `adventure_cycle_id` với ván **không có**. Không cần log thêm |
| **Điểm đến từ đâu** | Tách theo `difficulty` (cột chuẩn), `adventure_winstreak`, `adventure_multiplier`, `adventure_units` |
| **Revive / first-try win rate** | `boostersUsed` và `failCount` / `playCount` — SDK đã có sẵn trên log level |

⚠ **"Win rate trước/sau" là phép so yếu, không phải vì thiếu log.** Event bật cho mọi user đủ điều
kiện nên không có nhóm đối chứng; kết quả lẫn với mọi thay đổi khác cùng thời điểm. Muốn kết luận
chắc thì cần **A/B holdout** (tắt Adventure cho 10–20% user đủ điều kiện, dùng nhãn AB sẵn có trên
mọi log).

# 📖 Vì sao thiết kế như vậy

| Quyết định | Lý do |
|---|---|
| Không có funnel | Không có mốc tham gia (event tự động), và 25 mốc đã hiện diện qua dòng thưởng. Log thêm là trùng |
| Điểm gắn trên ván thắng, không gắn trên dòng thưởng | Điểm sinh ra ở ván chơi. Gắn ở đó thì tách được từng yếu tố (xe, độ khó, chuỗi, x2); gắn ở thưởng thì chỉ còn tổng |
| Win streak gắn trên **cả** ván thắng và ván thua | Cú tụt chuỗi là khoảnh khắc đáng giá nhất của event — nó là thứ đẩy user hồi sinh. Chỉ log lúc thắng thì chỉ thấy nửa câu chuyện |
| Tiền tố `adventure_` | Luật tiền tố live-op (`LiveOpsTrackingPattern.md` §1): Adventure gắn extras lên **level thường**, nơi event khác cũng gắn. Key chung chung là event sau ghi đè event trước, mất số im lặng |
| Mốc đọc từ dòng thưởng | Thưởng tự nhận nên "đạt mốc" và "nhận quà" là một. Xem cảnh báo ở mục C |

## So với Master League

Gần giống, nên dùng lại được phần lớn cách nghĩ:

| | Adventure | Master League |
|---|---|---|
| Chu kỳ | Tuần, lịch chung | Tuần, lịch chung |
| Điểm mỗi ván thắng | Xe × độ khó × chuỗi thắng × x2 | Len vàng theo độ khó × x2 |
| Chơi trên | Level thường | Bộ level riêng của mùa |
| Tham gia | Tự động | Phải bấm |
| Nhận thưởng | Tự động | Bấm nhận |
| Xếp hạng / bot | Không | Có |
