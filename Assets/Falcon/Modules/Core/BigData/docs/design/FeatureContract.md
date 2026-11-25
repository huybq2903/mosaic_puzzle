# Hợp đồng log cho TÍNH NĂNG (feature) — v1

> **Trạng thái: ĐÃ CODE** (02/11/2026). Viết 30/10/2026.
> ✅ **Loader ĐÃ KÝ cả 8 mục §11 (30/10, doc kho commit `b76c8ab`).**
> Code: `Scripts/Runtime/Entities/Feature/` · test: `Tests/FeatureContractTests.cs`.
> **Nguồn hợp đồng phía kho:** `bigdata-loader-v3/docs_v2/design/feature-contract.md` (`b76c8ab`).
> Bản này **trỏ sang** các mục của file đó chứ không chép bảng — chép là sinh nguồn-sự-thật thứ hai
> rồi hai bên trôi khỏi nhau.

## 0. Phạm vi

Bản này là hợp đồng **chung cho mọi tính năng**, không phải cho một tính năng cụ thể. Bang hội
(`guild`) là **ví dụ đã áp xong** và bảng tham số riêng của nó nằm ở file Team của bên dữ liệu, không
nằm ở đây.

📄 **Dev game tích hợp Team thì đọc `docs/games/Request-Game-Team.md`** — mười bản tin, code dán vào,
ba dòng tài nguyên, danh sách phải dọn, và năm cái bẫy. Bản này là "vì sao", bản đó là "làm gì". Cái SDK cam kết là: một log mới, ba field gắn được lên stream khác, một API `state`, một
bảng từ vựng bước, và luật tự điền `featureEffect`.

Những gì bản này **không** làm: không giữ sổ "tính năng đang chạy", và **không kiểm `feature` /
`featureStep` theo danh sách** (án owner 30/10).

🔴 **Vì sao không kiểm, dù §3 khai `feature` là `LowCardinality`:** kiểm theo danh sách = **bắt game
đăng ký tên trước**, đúng cái ma sát mà hợp đồng live-op sinh ra để dẹp ("mỗi event mới là một lần
đăng ký `funnelName` với loader ⇒ dashboard không bao giờ tự động được"). Danh sách còn phải giữ đồng
bộ hai bên, và nếu lấy từ remote config thì **phiên đầu chưa có config ⇒ SDK chặn tên hợp lệ**, mất dữ
liệu đúng lúc cần nhất. `LowCardinality` cũng không đòi hỏi thế: thêm vài giá trị lỗi gõ chỉ là thêm
mục từ điển, không phải vấn đề hiệu năng.

Rủi ro thật không phải cardinality mà là **lỗi gõ sinh ra một tính năng song song** (`guild` vs
`guilds`) chia đôi số liệu — và chỗ bắt nó là **DQ bên kho** ("xuất hiện giá trị `feature` mới, đếm
theo game"), không phải cửa chặn ở client.

⚠ **Nhưng "không kiểm" ≠ "không gợi ý"** (sửa nhận định 02/11): bản đầu em gộp hai thứ đó làm một và
thành ra không khai hằng số nào cho `feature`, trong khi `FFunnelName` — đúng ca tương tự — vẫn có
lớp hằng số với câu "tên ngoài danh sách vẫn gửi được". Hằng số không chặn gì cả, nó chỉ làm
autocomplete dẹp lỗi gõ **ngay lúc gõ** thay vì đợi DQ phát hiện sau. Nên có `FFeatureName` (§3.2),
và nó **không** thêm một cửa chặn nào. Cái SDK làm được mà không cần danh sách nào: **cảnh báo**
(không chặn) nếu `feature` không phải snake_case — §4.1 yêu cầu snake_case, và chữ HOA còn bị bước
Parse xé từng ký tự (§9).

## 1. Tóm tắt việc

| Việc | Loại |
|---|---|
| Log mới `f_sdk_feature` + param của nó | mới |
| Ba field `feature` · `featureInstanceId` · `featureStage` lên **7 stream** đang có | lớp nền mới — 7 stream không phải sửa, **trừ một chỗ**: §4.1 |
| API `Feature.Step` / `Feature.State` / `Feature.Stamp` | mới |
| SDK tự điền `featureEffect` cho bước chung + bước nhóm gửi từ client | mới |
| Hai trục ngữ cảnh ở **MỘT** lớp nền `ALiveOpsParam`; `LiveOpsRef` chở cả hai ⇒ khoá gộp cụm UI thêm 3 ô | quyết định thiết kế (§4) — đổi 05/11 |

## 2. Hai trục độc lập

Trục **event** (`liveOps*`) và trục **tính năng** (`feature*`) **không gộp được**: tính năng chạy
song song với event, trong khi các event loại trừ nhau. Một dòng mang cả hai là chuyện bình thường
(xin thẻ trong bang hội giữa mùa card collection). Luật đầy đủ: hợp đồng kho §2.

Trục tính năng là **lời khai nhân quả của game**, giống hệt trục event: SDK không bao giờ tự đoán
dòng này thuộc tính năng nào (§7).

## 3. Log mới `f_sdk_feature`

Một log duy nhất chở mọi bước của mọi tính năng. Field trên dây (tên camelCase, pipeline tự đổi sang
snake_case — §9):

| Field | Kiểu | Luật |
|---|---|---|
| `feature` | string | **bắt buộc**. Tên tính năng theo từ vựng hợp đồng kho §4.1, snake_case — hằng số gợi ý ở `FFeatureName`. SDK **không kiểm** danh sách (§0) — chỉ cảnh báo nếu không phải snake_case |
| `featureStep` | string | **bắt buộc**. Bước (§3.1). Loader map `feature_step → sub_event` — §11 |
| `featureInstanceId` | string | id thực thể. Vắng / `""` có nghĩa khác nhau — §5.2 |
| `featureStage` | int? | tiến độ tính năng một người. Vắng = tính năng không có tiến độ |
| `featureVariant` | string | dạng/hạng của **người gửi** trong thực thể. Bắt buộc cho `set_variant` |
| `featureEffect` | string | tác động lên trạng thái. SDK điền được một phần — §6 |
| `featureWhere` | string | lối vào / vị trí. Loader map `feature_where → event_where` — §11 |
| `featureJoinedTs` · `featureUnlockedTs` | long? | mốc vào thực thể / mốc mở khoá. Luật ở §5.2 |
| tham số riêng của bước | | `extraMeta` |

**Chín field trên là field THẬT của `FeatureParam`, không đi `extraMeta`** (tám dòng bảng — dòng cuối
gộp hai field) — kể cả `featureVariant` và `featureEffect`. Lý do: extras trải phẳng bằng `PutIfAbsent` (ai-ghi-trước-giữ), nên
`extraMeta["feature_effect"]` và field `featureEffect` sau bước Parse **ra cùng một tên** — hai nguồn cho
một cột, mất số im lặng. Là field thật thì chúng cũng tự vào sổ `FWireReservedKeys` (§9) nên game
không đặt trùng được.

🔴 **Luật đặt tên (chốt owner 30/10): MỌI field của hợp đồng này mang tiền tố `feature`.** Lý do là
sổ cấm: `FWireReservedKeys` gom bằng reflection từ field của payload, nên một field tên trần là **lấy
mất** tên đó của mọi game ở `Common.Set` — `variant` (AB test), `joined_ts`, `unlocked_ts`, `step`,
`where` đều là tên game hay dùng. Tiền tố cũng làm cột trong kho **tường minh** (owner yêu cầu phía
loader đặt cột có tiền tố cho khớp). ⚠ Luật này chỉ áp cho **field THẬT của SDK**; tham số riêng của
từng tính năng đi `extraMeta` nên không chạm sổ cấm và giữ tên theo luật "chủ của thứ được đo"
(`guild_*`, `target_*`, `lives_*`, `card_*`) — hợp đồng kho §4.2.

⚠ **Vì sao `featureStep` chứ không phải `step`:** `FWireReservedKeys` gom sổ cấm bằng reflection từ
field của payload, nên đặt tên `step` là **lấy mất** một tên rất chung của game — `Common.Set("step")`
sẽ bị từ chối ở mọi game. `feature_step` vừa khỏi tranh tên, vừa không tốn gì: luật Mapping là **riêng
từng event** nên tên field nào thành `sub_event` cũng được, miễn ký (§11). Cùng lý lẽ cho
`featureWhere` — `where` **đã** là field wire của `IapPurchaseAttemptParam`.

### 3.1 Từ vựng bước — hằng số SDK

Hợp đồng kho §4.1 chia bước làm **ba bậc**, và **bậc phải đọc được ngay ở chỗ gọi** — nếu để cả 12
hằng số phẳng trong một lớp thì autocomplete hiện ra một danh sách như nhau và dev đọc
`FFeatureStep.HelpGive` ra "bước tính năng nào cũng có", sai bậc:

| Bậc | Ở đâu | Chỗ gọi | Ai có |
|---|---|---|---|
| Bước **chung** (5) | thẳng trên `FFeatureStep` | `FFeatureStep.Open` | tính năng nào cũng có |
| Bước **nhóm** (7) | lớp lồng `FFeatureStep.Group` | `FFeatureStep.Group.HelpGive` | chỉ tính năng **nhiều người** |
| Bước **riêng** của tính năng | **không có hằng số** | `"kick_member"` | một tính năng |

⚠ Bước nhóm **không phải của riêng bang hội** dù tên nghe như vậy: gửi tim cho bạn bè cũng là
`help_give`, mời vào phòng co-op cũng là `accept_request`. Đó là cả lý do chúng ở từ vựng **chung** —
hai tính năng tự đặt `send_lives` và `gift_card` là chia đôi số liệu của **cùng một hành vi**, và
cross-feature thành mù (đúng vết `FFunnelName` sinh ra để dẹp).

SDK khai hằng số cho hai bậc đầu vì chúng dùng chung và SDK cần biết chúng để tự điền `featureEffect`.
Bậc ba thì game truyền chuỗi tự do — SDK **không kiểm**, nên tính năng mới không phải chờ bản SDK.

Danh sách bước và nghĩa: hợp đồng kho §4.1. SDK chỉ giữ bản sao dưới dạng hằng số, và đó là một chỗ
có thể trôi — ghim bằng test đối chiếu **từng bậc một** (§10).

### 3.2 Tham số nào có từ vựng hằng số, tham số nào không

Câu hỏi đặt 02/11, và câu trả lời không phải "tuỳ": module này đã có luật, viết thẳng trong xml-doc
của `FResourceWhen` — **`*When` (vì sao) thì SDK khai hằng số, `*Where` (ở đâu) thì vocab riêng từng
game**.

| Field | Hằng số | Vì sao |
|---|---|---|
| `featureStep` | ✅ `FFeatureStep` (+`.Group`) | từ vựng hợp đồng, hai bậc đầu đóng |
| `featureEffect` | ✅ `FFeatureEffect` | từ vựng hợp đồng, **đóng** — đúng 5 giá trị |
| `feature` | ✅ `FFeatureName` *(thêm 02/11)* | §4.1 có danh sách; hằng số **không chặn** gì, chỉ dẹp lỗi gõ |
| `featureVariant` | ❌ | giá trị do **từng game** định nghĩa (`leader`/`member`, `free`/`ad`/`vip`) — kho cố ý không khai |
| `featureWhere` | ❌ | là `*Where` ⇒ **vị trí**, vocab riêng từng game (luật nhà, `FResourceWhen`) |
| `featureInstanceId` | ❌ | là một **id** |

⚠ `FIapPlacement` là ngoại lệ **có tên** của luật `*Where`: cửa thu tiền là tập đóng xuyên game, và
so doanh thu cross-game thì bắt buộc phải thống nhất. Lối vào UI của tính năng thì không — muốn có
danh sách chung cho nó thì phải **ký ở kho trước**, không phải SDK tự đặt.

🔴 **Vì sao `const string` mà không phải `enum`** — câu hỏi hợp lý vì module này có cả hai khuôn
(`FlowType`, `LevelFailReason`… là enum thật). Hai lý do, lý do đầu là chặn cứng:

1. **Enum lên dây bằng tên thành viên C#.** Không có `StringEnumConverter` nào được cài trong
   `JsonExtensions`, và bằng chứng từ phía kho: luật nạp resource so `flow_type = 'Source'` — đúng
   tên `FlowType.Source`, chữ HOA đầu. Nên `FFeatureEffect` là enum thì dây mang `SetVariant` trong
   khi hợp đồng đòi `set_variant`, và cột `feature_effect` nhận giá trị không có trong enum của kho.
2. **Tập bước là tập MỞ** (bậc ba), enum thì đóng. `featureEffect` tuy đóng nhưng vẫn phải chịu lý do
   1, nên cả hai cùng khuôn `const string` cho đỡ phải nhớ hai luật.

## 4. Cây lớp param — hai trục, MỘT lớp nền

```
FParam
└ ALiveOpsParam   TRỤC EVENT      liveOpsEvent · liveOpsCycleId · liveOpsRoundId · liveOpsStage
                  TRỤC TÍNH NĂNG  feature · featureInstanceId · featureStage
  ├ ResourceParam · AdViewParam · IapPurchaseAttemptParam · IapOfferParam
  │ EventParam · ALevelParamV2 · LiveOpsParam     ← 7 stream, chở được CẢ HAI trục
  ├ FeatureParam        param của f_sdk_feature — 6 field riêng, cộng 3 field của lớp nền:
  │                       featureStep · featureVariant · featureEffect
  │                       featureWhere · featureJoinedTs · featureUnlockedTs
  └ LiveOpsRef          ref trần cho stream không có param riêng (log UI gộp cụm)
```

### Vì sao MỘT lớp, dù hai trục trực giao

Đây là chỗ dễ đọc sai nhất của hợp đồng nên nói thẳng: **một lớp KHÔNG có nghĩa là trục này phụ
thuộc trục kia.** Gộp một lớp là hệ quả của **serializer**, không phải lời khai về nghiệp vụ.

Mô hình đúng là "hai trục tuỳ chọn, độc lập". C# diễn đạt thứ đó bằng interface. Ở đây không được:

| Cơ chế | Vì sao không dùng được |
|---|---|
| **Interface** | `FKeyService.Encode` chỉ duyệt **public instance FIELD**; property `=>` **không bao giờ** lên wire. Interface C# không khai được field |
| **Composition** (object con) | ra JSON **lồng**, mà bảy field này phải là **cột phẳng cấp một** ⇒ cả 9 param phải tự viết `ToDictionary` flatten = 9 chỗ để quên |
| **Hai lớp nền song song** | C# chỉ cho kế thừa MỘT lớp ⇒ tầng dưới buộc phải kế thừa tầng trên, tức là **khai một quan hệ không có thật** |

Nên chuỗi kế thừa tuyến tính là **cơ chế duy nhất** cho wire phẳng mà không phải viết code ở từng
param. Ai đọc cây lớp này như một lời khai về nghiệp vụ là đọc sai.

### Lịch sử: 02/11 hai tầng → 05/11 gộp lại

Bản đầu đặt trục tính năng vào một tầng riêng `AFeatureParam : ALiveOpsParam`, lý lẽ: `LiveOpsRef`
cũng kế thừa `ALiveOpsParam` và nó là ngữ cảnh của **log UI gộp cụm** — lúc đó khoá gộp của
`UiExposureState` **chỉ gồm trục event**, còn `FUiImpressionLog` chép **từng field một**. Để
`LiveOpsRef` nhận `feature*` khi đó là: đóng dấu tính năng lên ref UI vừa **không lên dây** (không ai
chép) vừa **gộp hai bang hội vào một dòng** (khoá thiếu `feature*`) — im lặng, tệ hơn báo lỗi.

Lý lẽ đó đúng về **hậu quả** nhưng sai về **cách chữa**: nó mua an toàn cho log UI bằng một tầng kế
thừa khai rằng *"ngữ cảnh tính năng LÀ MỘT ngữ cảnh event"*. Hai trục trực giao (§2) thì câu đó sai
hẳn. Và tầng đó sinh nợ ngay: `ALiveOpsParam.CorrectValues()` phải thêm lời gọi `base` kèm một dòng
chú thích giải thích vì sao — dấu hiệu của cấu trúc sai, thêm một tầng thì phải đi nhắc mọi tầng.

**05/11 gộp lại**, và trả đúng cái giá mà tầng kia đang né:

| Việc | Vì |
|---|---|
| 3 field `feature*` lên `ALiveOpsParam`; **xoá** `AFeatureParam` | tầng đó khai một quan hệ không có thật |
| **Xoá** `FeatureRef` | **0 người dùng** — hàng rào dựng quanh một cửa chưa ai đi qua |
| Khoá gộp `UiExposureState` → **9 thành phần** (thêm `feature`, `featureInstanceId`, `featureStage`) | **BẮT BUỘC**, không phải cho đủ bộ: `LiveOpsRef` giờ chở được trục tính năng |
| `FUiImpressionLog` khai + chép 3 field `feature*` | nó chép **từng field một**, không serialize cả ref |
| **Giữ tên** `ALiveOpsParam` dù nó chở cả hai trục | `EventLiveOpsTracker.Stamp<T>() where T : ALiveOpsParam` (module **Events**) và `InAppValidation` đang dùng — đổi tên là vỡ build game, mà hai module đó **ngoài vùng sửa** của BigData |

Lãi kèm theo: **dòng impression chở được `feature_instance_id`**, thứ trước đó không có đường nào.

**Wire của 9 param stream không đổi một byte**: `FKeyService.Encode` duyệt cả field thừa kế, nên gộp
hai lớp nền không thêm, không bớt, không đổi tên key nào. Chỗ duy nhất có key MỚI là
`f_sdk_ui_exposure` (trước đó `f_sdk_ui_impression`) — nó nhận thêm ba key `feature*` **tuỳ chọn**, vắng mặt trừ khi game đặt, nên
mọi lời gọi `Ui.OnImpression` đang có cho ra output y như cũ. Kho không phải làm gì, game không phải
sửa gì.

Một việc kỹ thuật kèm theo: kế thừa sâu **đã chạy thật** rồi
(`LevelStartParamV2 : ALevelParamV2 : ALiveOpsParam`) và bốn field live-op vẫn lên dây, vì
`FKeyService.Encode` dùng `GetFields(BindingFlags.Instance | BindingFlags.Public)` — flag này lấy cả
field public thừa kế. Vẫn ghim test cho tầng sâu nhất (§10).

### 4.1 Một chỗ PHẢI sửa: dòng mua THẤT BẠI

Lớp nền lo được mọi stream mà param đi thẳng từ tay game ra log. Chỗ **không** lo được là nơi SDK
**dựng lại param**, vì nó chỉ chở sang những field nó biết tên:

`PurchaseAttemptState` chụp ngữ cảnh lúc `Iap.OnStarted` rồi dựng `IapPurchaseFailParam` cho nhánh
thất bại (`FIapApi.OnFailed` **cố ý không nhận param**, nên không ai khác điền được cho nhánh đó) —
và nó chép **từng field một**. Để nguyên thì game gọi `Feature.Stamp` lúc mở lượt mua sẽ có tính năng
trên `started` mà **mất trên `failed`**: phễu mua trong tính năng lệch đúng nhánh thất bại, theo chiều
*"trông như không ai thất bại"*. Đúng vết đã sửa cho trục live-op 19/10 (Endless Treasure R2), và trục
tính năng **dễ dính hơn** vì tính năng luôn bật, không đợi đợt nào.

⚠ Dòng `purchased` thì khác và **cố ý không sửa**: param của nó do GAME truyền
(`PurchaseAttemptService.LogPurchase(InAppParam)`), SDK không dựng lại, nên game tự đóng dấu lên
`InAppParam` của chính nó — kiểu đã hỗ trợ sẵn vì `InAppParam : IapPurchaseAttemptParam`. Y khuôn trục
live-op: `TryAttributeSuccess` chỉ trả `purchaseAttemptId`, không chở ngữ cảnh.

⇒ `PurchaseAttemptState` nhớ thêm ba field `feature*` trong `Open`, chở sang trong `TakeFail`, xoá
trong `Clear`. Ghim bằng hai test (thừa hưởng được, và **không** thừa hưởng của lượt trước).

⚠ Đã soát cả module: ngoài hai chỗ chép-từng-field này (`FUiImpressionLog` — từ 05/11 chép **cả**
`feature*`, và `PurchaseAttemptState` — vừa sửa) **không còn** nơi nào khác **trong BigData** chép ngữ
cảnh theo kiểu đó (`grep -rE "liveOps(Event|CycleId|RoundId|Stage)\s*="`). Thêm chỗ mới thì phải chép
cả `feature*`.

🔴 **NGOÀI module còn một chỗ, và từ 05/11 nó đang rơi `feature*`.**
`InAppValidation/.../BigdataLogger.cs` có `ApplyLiveOps<T>(T param) where T : ALiveOpsParam` chép
**đúng bốn** field live-op từ một `LiveOpsRef` tĩnh mà game nạp qua `CustomLiveOps`. Ref đó giờ chở
được trục tính năng, nên game đặt `feature` lên nó rồi mua IAP là **mất trục tính năng, im lặng** —
cùng hạng lỗi với §4.1, chỉ khác là nằm ở module khác, và dòng `purchased` thì **không** dính (param
của nó do game truyền qua `CustomInAppParam`). Build **không vỡ** (ràng buộc
`where T : ALiveOpsParam` vẫn đúng vì lớp đó còn nguyên tên) nên **không có gì báo**.

⇒ Gốc bệnh không phải một chỗ chép sai mà là **ngữ cảnh bị liệt kê bằng tay ở ba nơi**. Lượt này
thêm `ALiveOpsParam.CopyContextFrom(source)` — **chỗ duy nhất** còn giữ danh sách bảy field —
và chuyển `PurchaseAttemptState` sang dùng nó (7 field private rời → **một** ảnh chụp `LiveOpsRef`).
Ghim bằng test reflection: thêm field thứ tám mà quên thêm dòng chép là **đỏ**. `FUiImpressionLog`
không dùng được hàm này (nó là `AFalconLog`, không phải param) nên vẫn là chỗ chép tay thứ ba, có
test riêng canh. Module `InAppValidation` **ngoài vùng sửa** ⇒ không tự sửa; ticket **2 dòng** ở
`docs/modules/Request-InAppValidation-FeatureAxis.md` (nâng pin — đang khai **1.1.8**, cũ hơn chính
hợp đồng live-op mà file đó dùng — và đổi thân `ApplyLiveOps`).

## 5. API

### 5.1 `Feature.Step`

```csharp
bool Step(string feature, string step, string instanceId = null,
          string variant = null, int? stage = null, string where = null,
          string effect = null,   // lên dây: featureVariant / featureWhere / …
          Dictionary<string, object> extraMeta = null);
bool Step(FeatureParam param);
```

⚠ **Lệch bản ký một chỗ, cố ý:** bản 30/10 ghi `void` cho overload tiện tay và `void` cho `State`.
Code trả `bool` cho **cả ba**, khớp `LiveOps.OnStep` — hai overload cùng tên mà khác kiểu trả về là
thứ không ai đoán được, và `bool` là tập cha (gọi xong bỏ qua giá trị vẫn đúng). `false` ở đây **chỉ**
có một nghĩa: bản tin bị bỏ vì thiếu `feature` — không có van lọc nào như live-op.

`featureEffect` để trống thì SDK tự điền **nếu** bước nằm trong từ vựng chung/nhóm (§6); bước riêng của
tính năng thì game phải truyền, vì SDK không được đoán hộ.

Không có van lọc "một lần mỗi đợt" như live-op: tính năng luôn bật, không có đợt để làm khoá van.
Bước dày đặc thì game tự gộp trước khi gọi.

### 5.2 `Feature.State` — `null` và `""` là hai nghĩa khác nhau

```csharp
bool State(string feature, string instanceId, long? joinedTs = null,
           long? unlockedTs = null, string variant = null, int? stage = null,
           //                        lên dây: featureJoinedTs · featureVariant · featureStage
           Dictionary<string, object> extraMeta = null);
```

`instanceId` là **tham số bắt buộc, không có giá trị mặc định** — bắt người gọi phải quyết định:

| Truyền | Lên dây | Kho hiểu |
|---|---|---|
| `null` | **vắng key** | tính năng không có khái niệm thực thể ⇒ profile không chạm |
| `""` | `""` | đã mở khoá, **không ở thực thể nào** ⇒ profile xoá thực thể |
| `"T_8812"` | `"T_8812"` | đang ở thực thể đó |

🔴 **`featureInstanceId` KHÔNG được chuẩn hoá `""` → null.** BigData có khuôn ngược lại ở
`FAdLoadLog` (hạ `""` về null để `RemoveIfNull` bỏ khỏi payload) và khuôn đó **cấm** ở field này: `""`
là **ngoại lệ có tên** của luật "không có giá trị thì bỏ key" (hợp đồng kho §4.2). Mất `""` thì mọi
bản `state` của người không ở thực thể thành "vắng key = không chạm", và người bị tác động lúc offline
quay về thực thể cũ — **im lặng, không chỗ nào báo**. Ghi chú ngay tại field, và ghim bằng test (§10).

**`joinedTs` (lên dây `featureJoinedTs`) thiếu khi `instanceId` không rỗng**: ghi **cảnh báo**, **không**
veto — nhưng lý do đã đổi so với bản đầu. Trước, nó là mốc kho dùng để **xếp thứ tự**, nên thiếu là
hỏng tính đúng. Từ bản vá 30/10 lần 2, thứ tự xếp bằng `(server_receive_time, client_sent_time)` và
`featureJoinedTs` **chỉ còn là giá trị thâm niên** (`since_ts` → Q5 tuổi, Q8 "tính từ lúc vào team").
Nên chặn ở client càng vô nghĩa: mất một con số báo cáo, không phải mất tính đúng.

🔴 **Định nghĩa game phải tuân — SDK chỉ chở, không kiểm được:** `featureJoinedTs` là **ngày vào lần
hiện tại**, không phải lần vào đầu tiên. Vào lại đúng thực thể cũ thì gửi **ngày vào lại** (doc kho,
bản Team §2.2). Gửi ngày vào đầu tiên là báo thâm niên dài hơn thật, và không chỗ nào bắt được.
(Khác với van veto `IsSendable` dùng cho bước funnel sai luật.)

**`stage` có trên `State`** vì luật `sync` **ghi đè `stage` nếu bản tin có** (doc kho §7.2) — tính năng
một người (quà hằng ngày, chuỗi nâng cấp) báo tiến độ ngay trong bản `state`. Ngoài ra **mọi** bản tin
`f_sdk_feature` mang `featureStage` đều cập nhật tiến độ, không riêng `state`.

**Khi nào game gọi `State`**: ngay sau khi tải xong dữ liệu tính năng **từ server**, không dựng từ
cache. Một bản mỗi lượt chạy app, thêm một bản mỗi lần đồng bộ lại. SDK **không** tự gọi hộ — SDK
không biết lúc nào game có sự thật từ server. Luật đầy đủ: hợp đồng kho §4.3.

### 5.3 `Feature.Stamp`

```csharp
void Stamp(ALiveOpsParam param, string feature, string instanceId = null, int? stage = null);
```

Cùng khuôn `LiveOps.Stamp`: chỉ điền chỗ đang **trống** (`??=`), game truyền tay thì tay thắng.

Khác `LiveOps.Stamp` một điểm: không có `CurrentCycleId` tương đương. `instanceId` **phải truyền ở
chỗ gọi**, vì SDK không giữ sổ "thực thể đang ở" — xem §7.

## 6. `featureEffect` — SDK điền phần nào

SDK điền bằng `??=` (tay thắng), và **chỉ** cho bước chung + bước nhóm **gửi từ client**:

| SDK tự điền | Giá trị |
|---|---|
| `unlock` | `unlock` |
| `state` | `sync` |
| `join` | `enter` |
| `leave` | `exit` |
| `variant_changed` (tự đổi) | `set_variant` |
| `open` · `interact` · `claim` · `accept_request` · `help_request` · `help_give` · `chat` | **bỏ key** |

SDK **không** điền cho: bước riêng của từng tính năng (game điền), và **mọi bản tin do server gửi
hộ** (server không chạy SDK — server tự điền). Bảng đầy đủ ba cột: hợp đồng kho §4.2. Kho có DQ đếm
cặp (`sub_event`, `feature_effect`) lệch, nên bên nào điền sai thì DQ bắt.

### 6.1 `request_id` KHÔNG lên dòng tài nguyên (án owner 30/10)

§6 của hợp đồng kho từng đọc ra là dòng tài nguyên phải mang `request_id` (*"cùng luật
`ad_view_id`/`transaction_id`"*). **Không phải** — doc đã sửa. `ResourceParam` **không** thêm field nào:

- *tài nguyên này từ đâu ra* đọc được rồi, bằng `feature = guild` + `event_when = help_received`;
- *lượt xin có ai giúp không, sau bao lâu* thì kho nối `help_request` ↔ `help_give` **trong bảng bước**
  — hai bản tin đó đều mang `request_id`;
- chỉ câu *"người nhận có bấm nhận không"* mới cần nối sang dòng tài nguyên, mà **chưa ai hỏi**. Lúc
  nào cần thì game tự đặt `request_id` qua `extraMeta` và kho đọc từ extras — **không đổi hợp đồng**.

Ghi lại vì nó là một luật dễ bị "sửa cho chắc": thêm một field vào `ResourceParam` là thêm một tên vào
sổ cấm và một cột vào stream nặng nhất, cho một câu hỏi chưa ai đặt.

## 7. Không có scope ngầm

SDK **không** giữ sổ "người chơi đang ở tính năng nào / thực thể nào" để tự dán lên dòng khác. Lý do
là án đã chốt 05/09 cho trục event và nó áp y nguyên ở đây: `GameEvent.Emit` chạy **đồng bộ**, và
listener của `ResourceCollector` chạy **trước** lúc dựng log, nên một chuỗi phản ứng sẽ đóng dấu sai
lên log của hệ khác. **ĐỒNG THỜI ≠ NGUYÊN NHÂN, không có ngoại lệ "cửa sổ ngắn".**

Hệ quả thực tế: dòng tài nguyên / ad / iap phát sinh **vì** một tính năng thì game phải gọi `Stamp`
tại chỗ phát sinh.

🔴 **Nhưng "không gọi `Stamp`" chỉ đọc được là "không thuộc tính năng nào" TỪ ngày game áp hợp đồng.**
Trước ngày đó `feature` vắng trên **mọi** dòng, và vắng ở đó nghĩa là **"chưa khai"**, không phải
"không có nguyên nhân tính năng". Trục **trạng thái** đã có `effective_from` theo game (hợp đồng kho
§7.1 mục 2); trục **nhân quả** thì chưa, nên nó là một mục phải ký (§11). Thiếu mốc đó thì mọi phép
"bao nhiêu % tài nguyên chảy vì tính năng X" lấy cả quãng chưa khai vào mẫu số và ra số thấp giả.

⚠ Ngược lại, **trạng thái** (đang ở bang nào) thì kho tự dán từ profile lên mọi dòng gold — đó là
trục thứ ba, không phải nhân quả, và nó **không** cần gì từ SDK ngoài bản `state`.

## 8. Hàng đợi: không dùng `SendNow`

Mọi bước `f_sdk_feature` đi **hàng đợi thường**, kể cả bước đổi thực thể.

Lý do, dù nghe ngược: `LogScheduleService.SendNow` chạy `Task.Run` cho **từng** bản tin, nên hai lời
gọi liền nhau là hai HTTP request độc lập — tới cổng theo thứ tự nào thì tuỳ mạng. Hàng đợi thì có
`_flushLock` và đi `PeekAll → SendBatch → Drain`, tức **giữ đúng thứ tự trước-sau**. Mà phía kho xếp
thứ tự bản tin để dựng profile, nên **thứ tự quan trọng hơn độ trễ**. Chu kỳ batch mặc định
`fBatchLogSendSec` không ảnh hưởng gì: không có gì ở đây cần real-time.

✅ **Đã chốt phía kho (30/10): khoá xếp = (`server_receive_time`, `client_sent_time`).** Bản tin đi
**chung một lô** tới cổng trong **một** request nên mốc tới cổng **hoà nhau**, đúng bằng cửa sổ chu kỳ
lô — vừa đủ cho cặp `leave` A rồi vào lại A. `client_sent_time` phá hoà: trong một lô thì mọi dòng cùng
một máy, một đồng hồ, nên thứ tự tương đối đúng kể cả khi đồng hồ lệch. `server_receive_time` xử chéo
nguồn (khác lô thì không hoà). Hệ quả: hàng rào `last_exit_*` cũ **bỏ hẳn**, nay chỉ còn phục vụ DQ.

## 9. Tên field qua pipeline nạp

Bước Parse đổi camelCase → snake_case, và có hai bẫy: **chữ HOA liên tiếp bị xé từng ký tự**
(`campaignID` → `campaign_i_d`), **key mở đầu bằng `_` mất cả field**. Tên của bản này đã đối chiếu:

| Field SDK | Cột kho |
|---|---|
| `feature` | `feature` |
| `featureInstanceId` | `feature_instance_id` |
| `featureStage` | `feature_stage` |
| `featureEffect` | `feature_effect` |
| `featureStep` | `feature_step` → map sang `sub_event` |
| `featureVariant` | `feature_variant` |
| `featureJoinedTs` · `featureUnlockedTs` | `feature_joined_ts` · `feature_unlocked_ts` |
| `featureWhere` | `feature_where` → map sang `event_where` |

Không tên nào có hai chữ HOA liền nhau. Bảng Alias của pipeline hiện **không** có luật đổi tên cho
nhóm field này — nhưng luật Alias nằm trong Mongo và đổi được không cần deploy, nên đây là một mục
phải ký (§11), không phải một sự thật vĩnh viễn.

⚠ **Cho game:** `FWireReservedKeys` quét bằng reflection nên chín field trên **tự vào sổ cấm** của
`Common.Set` (khớp theo dạng chuẩn hoá: `feature_variant`/`featureVariant`/`FEATURE_VARIANT` đều bị
bắt). Cùng vết với `tier` ở 1.5.5 và 6 tên của 1.5.7 — **nhưng** nhờ luật tiền tố, hợp đồng này
**không lấy một tên trần nào**: `variant`, `joined_ts`, `unlocked_ts`, `step`, `where` vẫn để trống cho
game. Tên duy nhất game có thể đã dùng là `feature` (§11 mục 8 đếm).

## 10. Test phải ghim

| Ghim gì | Vì sao |
|---|---|
| `""` của `featureInstanceId` **lên được dây** | mất nó là bug im lặng tệ nhất của cả hợp đồng (§5.2) |
| `null` của `featureInstanceId` ⇒ **vắng key** | hai nghĩa phải tách được |
| `feature*` lên dây từ **tầng kế thừa thứ tư** | `FKeyService` lấy field thừa kế — ghim kẻo ai đổi flag |
| Cả **7** field ngữ cảnh có trên `ALiveOpsParam` **và** `LiveOpsRef`; lớp nền nằm **ngay dưới** `FParam` (reflection) | ghim ca hồi quy của §4 sau khi gộp 05/11: ai đó tách lại hai trục, hoặc chen một tầng vào giữa mà không chuyền tiếp `CorrectValues` |
| Hai `featureInstanceId` khác nhau ⇒ **hai cụm** impression | cái GIÁ của việc gộp: `LiveOpsRef` chở được trục tính năng nên khoá gộp phải chở đủ, không thì hai bang hội thành một dòng |
| `FUiImpressionLog` **tự khai** 3 field `feature*` (reflection) | nó chép từng field một, không serialize cả ref ⇒ thêm field vào lớp nền mà quên chép là mất trục tính năng của mọi dòng impression |
| `Stamp` **không** đè giá trị game truyền tay | khuôn `??=`, giống `LiveOps.Stamp` |
| `featureEffect` **không** bị điền cho bước riêng | SDK không được đoán hộ tính năng của game |
| Hằng số `FFeatureStep` khớp danh sách hợp đồng kho §4.1 | bản sao từ vựng là chỗ dễ trôi |
| `featureJoinedTs` thiếu ⇒ **vẫn gửi**, chỉ cảnh báo | chặn ở client là giấu DQ của kho |

🔴 **Ba dòng đầu — `""` lên dây, `null` vắng key, kế thừa tầng thứ tư — phải viết ở mức PARAM, không
dựng log**: `FKeyService.Encode(new FeatureParam{…})` là lời gọi static thuần, không chạm DI ⇒ **chạy
thật** trong `Tools~/TestHarness`. Dựng log là rơi vào nhóm ~70 test cần Unity runtime — nhóm chưa từng
*chạy*, chỉ *compile*, đúng số phận của ba test wire `FAdLoadLogTests` ở 1.5.7. Bất biến quan trọng nhất
của hợp đồng này không được ghim "bằng mắt".

⚠ Ba dòng về `LiveOpsRef` / `FUiImpressionLog` ghim bằng **reflection**: NUnit không assert được "cái
này không biên dịch", và nửa "ctor có chép không" thì phải **dựng log** ⇒ rơi vào nhóm cần Unity
runtime (`FeatureAxis_ReachesTheLog`, chỉ *compile*). Nên nửa "log có **khai** field không" — nửa hay
bị quên — được ghim riêng bằng reflection để nó **chạy thật**.

✅ **Đã ghim đủ (02/11 — bảng lúc đó 8 dòng)**: `Tests/FeatureContractTests.cs` (19 test) + 2 test trong
`Tests/PurchaseAttemptStateTests.cs` cho §4.1 — 378 xanh, 0 đỏ, nhóm cần Unity runtime đúng 73. Hai
bất biến được ghim thêm vì tìm ra lúc code: chuỗi rỗng sống qua **cổng thứ hai**
(`PutIfAbsentAndNotNull` lúc trải param vào log, không chỉ `FKeyService`), và `extraMeta` không đè
được field thật.

✅ **Cập nhật sau lượt gộp lớp nền (05/11)**: test `LiveOpsRef_HasNoFeatureAxis` **đảo chiều** thành
`BothAxes_LiveOnOneBase_RightUnderFParam`, thêm `ImpressionLog_DeclaresTheFeatureAxisItself` và hai
test khoá gộp ở `Tests/UiLiveOpsClusterTests.cs`. **380 xanh, 0 đỏ**; nhóm cần Unity runtime 73 → 74,
tăng đúng một test (`FeatureAxis_ReachesTheLog` — dựng log nên không tránh được), và nửa bất biến của
nó đã có người canh chạy thật như ⚠ trên.

## 11. Loader phải ký trước khi SDK publish

Bước Mapping của pipeline điền các cột chung **theo luật riêng từng event**, và event **không có luật
mapping** vẫn vào kho nhưng với `sub_event = 'UNKNOWN'` và mọi field nằm trong `event_extra_props`.
Bảng đổi tên event hiện **chưa có dòng** cho `f_sdk_feature`. Cần ký:

1. **Tên event trong kho** cho `f_sdk_feature` (các event khác đều bị đổi tên: `f_sdk_ads_data` →
   `f_sdk_ad_impression`…).
2. **Field nào thành `sub_event`** — đề xuất `feature_step` (field SDK `featureStep`). ⚠ JSON ví dụ trong file Team viết
   `"sub_event":"join"` là **ảnh trong kho**, không phải tên trên dây; tên trên dây là mục ký này.
3. **Field nào thành `event_where`** — đề xuất `featureWhere`.
4. Ba field `feature` · `feature_instance_id` · `feature_stage` thành **cột silver thật** (không nằm
   trong `event_extra_props`). `feature_effect`, `feature_variant`, `feature_joined_ts`,
   `feature_unlocked_ts` **ở lại `event_extra_props`** — profile đọc extras bình thường, không cần cột.
5. 🔴 **Đổi `state_effect` → `feature_effect` trong doc kho.** Tên field chốt 30/10 là `featureEffect`
   (cũ là `stateEffect`, đổi vì `feature_state_*` đã là họ tên của khối mảng trạng thái trên gold —
   `feature_state_effect` sẽ đọc như thành viên thứ 6 của họ đó mà nó không phải). `feature-contract.md`
   còn **18** chỗ và file Team còn **8** chỗ viết `state_effect`.

   ⚠ Nguyên nhân hỏng là **lệch tên**, không phải "nằm trong extras" (mục 4: extras là chỗ đúng của nó).
   Để nguyên thì SDK gửi key `feature_effect`, còn profile đi đọc key `state_effect` — **không có key
   đó** nên mọi bản tin đọc ra "vắng = không đổi trạng thái". Không bản tin nào ném lỗi, log vẫn về đủ,
   và DQ hiện có cũng không thấy: nó bắt "cặp `sub_event`/`feature_effect` **lệch nhau**", không bắt
   "thiếu hẳn tên".
6. **Xác nhận không có luật Alias / Trim nào chạm nhóm field này** — nhất là không có luật dọn chuỗi
   rỗng, vì `""` của `feature_instance_id` phải sống (§5.2). (Soát bảng Alias 30/10: chưa có luật nào
   chạm `feature*`, `feature_variant`, `feature_effect`, `feature_step`, `feature_where`.)
7. **`effective_from` theo game cho cột nhân quả `feature`** — y như hợp đồng kho §7.1 mục 2 đã làm
   cho trục trạng thái. Trước ngày khai, `feature` vắng phải đọc là "không mang", không phải "không
   có" (§7).
8. **Một truy vấn chạy TRƯỚC khi ký**, vì SchemaCheck cho field có cột đi vào cột đó: khi **ba** field
   của mục 4 thành cột silver, **mọi key sẵn có cùng tên** trong `event_extra_props` của mọi `f_sdk_*`
   sẽ bị hút vào cột mới — kể cả key game tự đặt với nghĩa khác. Đếm trước đúng ba tên đó:

```sql
select event, key, count(*) n
from <silver>
array join JSONExtractKeys(event_extra_props) as key
where created_day >= today() - 30
  and key in ('feature','feature_instance_id','feature_stage')
group by 1,2 order by n desc;
```

   `feature_step` và `feature_where` **không** vào danh sách: luật Mapping là **riêng từng event**, nên
   nó chỉ tiêu thụ hai key đó trên `f_sdk_feature` — event chưa tồn tại, không có dòng cũ nào. Bốn field
   ở lại extras (mục 4) cũng không: không có cột thì không có gì hút chúng. 🔴 **Nếu sau này loader cấp
   cột cho `feature_variant` / `feature_effect` thì chạy lại truy vấn với hai tên đó** — lúc đó chúng mới
   thành đích hút.

   ⚠ Luật tiền tố `feature` (§3) **đã dẹp gần hết** rủi ro này: trước khi đổi tên, hợp đồng sẽ chiếm
   `variant` · `joined_ts` · `unlocked_ts` · `step` · `where` — toàn tên game hay dùng, và `variant` thì
   trùng đúng chỗ AB test. Sau khi đổi, tên duy nhất game còn có thể vô tình đặt trùng là **`feature`**.
   Vẫn chạy một lần cho chắc.

   Ra khác 0 thì cột mới nạp rác cũ ngay từ dòng đầu, và phải quyết trước: Trim key đó, hay đổi tên
   field của SDK.

## 12. Còn mở

1. **Bước `open` có vào danh sách bản tin của từng game không.** SDK làm `open` bất kể, vì nó nằm
   trong từ vựng bước chung. Nhưng nếu game không gửi thì câu "ai còn **dùng** tính năng" (khác hẳn
   "ai đang ở trong") không trả lời được. Quyết định của GD/dữ liệu, không phải của SDK.
2. **Cổng nạp cho bản tin server gửi hộ** — ngoài repo này, chờ game đầu tiên làm.
3. ✅ **DQ "thâm niên rơi về giờ cổng" — kho đã nhận, đã vào doc kho** (`feature-contract.md`, mục
   DQ của §7.3). Phạm vi kho chốt: đếm bản tin `sync` đưa người chơi vào **thực thể mới** (lần đầu kho
   thấy, hoặc đổi thực thể) mà thiếu `featureJoinedTs` ⇒ `since_ts` rơi về `server_receive_time`. Tỉ lệ
   theo game, trên tổng số `sync` mang thực thể mới. **Không đếm** `sync` cùng thực thể (giữ `since_ts`)
   và **không đếm** `enter` từ client. Cảnh báo tại máy của SDK giữ nguyên; kho bổ sung bức tranh fleet.

   Bỏ `enter` ra là đúng, và lý do còn chắc hơn câu doc kho viết ("phát ngay lúc vào, chỉ lệch độ trễ
   lô"): overload tiện tay `Feature.Step(...)` (§5.1) **không có tham số `joinedTs`** — chỉ
   `Feature.State` có. Nên mọi `enter` đi đường ngắn đều thiếu field **theo thiết kế**; đếm nó thì DQ
   đọc ~100% và không nói lên gì.
4. ❓ **`t_in` của dòng client gửi: nên fallback `created_date` thay vì `server_receive_time`?**
   Hệ quả còn lại sau mục 3, hẹp hơn thoạt nghe. Doc kho §7.2 đặt `t_in = feature_joined_ts` nếu có,
   không thì `server_receive_time` — tức **giờ tới cổng**, và độ lệch đó **không bị chặn bởi chu kỳ lô**:

   | Mốc | Dán lúc nào | Bằng chứng |
   |---|---|---|
   | `clientCreateDate` → `clientSendTime` (envelope) | lúc **dựng log**, tức đúng lúc game gọi API | `PlainLog.cs:19` là **field initializer**; `DataWrapper(log, …) : this(…, log.CreatedTime, …)`. Doc kho đã ghi luật này: `event-catalog.md` §3 và `sdk-event-inventory.md` mục **S2** |
   | `server_receive_time` | lúc bản tin **tới cổng** | hàng đợi **bền** (log nằm chờ qua phiên), re-enqueue ở mức wrapper nên không decor lại |

   Nên máy offline/ngủ lâu thì `enter` nằm trong hàng đợi bao lâu, thâm niên lệch bấy nhiêu — `dq-health.md`
   chia dải trễ `server_receive_time − created_date` tới **7 ngày**, tức kho đã thiết kế cho mức trễ đó.

   **Tự lành được một phần**, phải nói cho đúng: luật `sync` cùng thực thể là `since_ts` =
   `feature_joined_ts` **nếu có** (doc kho §7.3: *"giờ server sửa được mốc cũ"*), và field này **bắt buộc
   khi đang ở thực thể** ⇒ game làm đủ thì bản `state` phiên sau **sửa lại** con số của `enter`. Lỗ còn
   lại là đúng ca **`enter` tới muộn + `sync` sau đó vắng `featureJoinedTs`**: nhánh cùng thực thể thì
   "không có thì **giữ**", nên giá trị sai **đứng tới lần đổi thực thể** — mà DQ mục 3 chỉ đếm nhánh
   **thực thể mới**, nên đúng ca này nó **không thấy**.

   Đề xuất: `t_in` = `featureJoinedTs` → **`created_date`**. Không phải `client_sent_time` (mốc thô,
   `<=0` thì kho bỏ qua, không patch), mà `created_date` — cột **NOT NULL** đã *"resolve
   client→server→now"* qua DeriveTime với cửa sổ 10 ngày (`schema-reference.md:121`), nên không cần
   thêm mắt canh null lẫn mắt canh đồng hồ lệch: chuỗi hai bậc, không phải ba.

   ⚠ Chỗ này **không** mượn được lý lẽ của khoá xếp: `client_sent_time` ở đó chỉ phá hoà **thứ tự tương
   đối trong một lô của một máy**, đồng hồ lệch bao nhiêu cũng không sai. Thâm niên là một **giá trị
   tuyệt đối** nên lệch là sai thật — vì vậy đề xuất này đi qua `created_date` (đã canh) chứ không qua
   mốc thô. Xếp thứ tự cần **một** đồng hồ cho mọi người; một giá trị thì cần **đúng** đồng hồ.
