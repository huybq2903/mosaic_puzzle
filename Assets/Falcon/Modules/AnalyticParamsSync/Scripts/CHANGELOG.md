## 1.2.0
- 💥 **`ALevelHeartBeatService` thôi tự sinh dòng heartbeat** (hợp đồng BigData đợt 3 B5, ký 23/11): từ nay BigData tự bắn heartbeat lúc
  app xuống nền / `Level.OnPauseExit`, và hỏi lớp này lấy tham số tại lúc bắn. Lớp đổi từ `IAppPauseLogGenerator` sang
  `ILevelTurnParamsProvider` (`FillTurnParams`) — giữ lại đường cũ thì mỗi lần xuống nền ra HAI dòng. Với game kế thừa lớp này, số
  dòng heartbeat lúc xuống nền **không đổi**; property abstract giữ nguyên, game không phải sửa code.
  - Bỏ bộ định danh "lưới an toàn" (playTurnId / level / levelId / độ khó): BigData chỉ bắn khi có lượt mở và tự điền từ cache.
  - Luật mới cho property của game: **rẻ, không side effect** — SDK gọi chúng trên main thread: lúc xuống nền, và (BigData 1.8.0,
    hợp đồng B5 §5.3) **khoảng mỗi 10 giây giữa lượt** để làm mới bản chụp heartbeat khẩn — chỉ khi game bật van
    `fAppIssueEmergencyEnabled` (mặc định tắt). Một lần gọi quá 16 ms thì BigData ngừng
    làm mới tới hết phiên (`aps_refresh_disabled` trên `f_sdk_session_data`); lúc xuống nền vẫn gọi.
  - Pin `falcon.modules.core.bigdata` **1.7.4 → 1.8.0** (bản đầu tiên có `ILevelTurnParamsProvider`): APS này đi với BigData cũ thì
    không còn heartbeat nào.
  - Tín hiệu kiểm: compile file này sạch với BigData mới (stub `FLevelManager`); module không có test assembly.

## 1.1.9
- **Tự gắn nhãn người chơi `no_ads` theo cờ bỏ quảng cáo của Mediation** (`NoAdsLabelSync`, ký loader 12/11 —
  `BigData/docs/loader/Request-Loader-AdShowSkipped.md` mục 2). Game không phải tự gọi `Label.User`. Đọc
  `GameData4RemoveAds.removeAds` ở hai mốc: **đầu phiên sau khi đăng nhập** (cờ là FGameData đồng bộ server; chạy nền, không chặn
  chuỗi `IInit`) và **mỗi lần app pause** (bắt lần mua giữa phiên mà không móc vào luồng mua). Chỉ gửi **khi đổi** so với lần gửi
  trước (sổ ở `PlayerPrefs`): có quyền ⇒ `true`; mất quyền sau khi đã gửi `true` ⇒ `null` (gỡ nhãn); chưa từng có quyền ⇒ không gửi.
- Đặt ở module này vì nó **vốn đã** tham chiếu Mediation; BigData thì không được (Mediation phụ thuộc BigData).
- ⚠ Giới hạn: sổ "đã gửi" nằm ở máy — cài lại app rồi mất quyền ngay lượt đó thì không gỡ được nhãn cũ phía server. Kho **chưa
  dựng** đường nhãn người chơi (loader 12/11); dòng gửi trước vẫn được giữ raw — báo loader khi bật.
- Pin `falcon.modules.core.bigdata` 1.5.8 → **1.7.4** (bản đầu tiên có `FUserLabelKey.NO_ADS`). Mediation 1.4.31 đã có
  `GameData4RemoveAds`.
- Tín hiệu kiểm: module không có test assembly. Compile sạch 0 lỗi bằng csc: cả cây `Scripts/Runtime` của module, với BigData dựng
  từ nguồn hiện tại và `GameData4RemoveAds` / `GameData4AdInfo` thật của Mediation.
----------------------------------


## 1.1.8
- **FIX `sessionId` NHẢY GIỮA MỘT LƯỢT CHẠY APP** — `ServerPlayerSessionRepository.SessionId` trước đây là getter đọc **sống** `PlaySessionData.Instance.sessionId`, mà blob đó bị **thay cả đối tượng** giữa phiên (`SCUpdateData` → `AccountManager.ClientData` setter → `GameDatas`). Trong khi đó `sessionUid` bốc một lần cho cả process và đứng im. Kết quả trên server: **một `sessionUid` ứng nhiều `sessionId`**. Ba đường tạo ra nó: (1) user cũ — log khúc đầu mang số của process TRƯỚC, tới `Init` mới `++` thành số thật, mà cửa sổ đó dài vì `InitService` chạy các `IInit` tuần tự với timeout 30s mỗi cái; (2) máy mới — sổ nhà → bản server đè → `++` ⇒ **ba** số trong một lượt chạy; (3) reconnect / đa thiết bị — bản server về sau chưa chứa lần `++` này ⇒ số **tụt ngược**.
- Từ nay số phiên **chốt một lần cho cả process** qua `LazyVal<int>` (`FreezeSessionId`): lấy sổ server `+ 1` nếu có sổ, không có thì dùng sổ nhà (`ReservePlayerSessionRepository` đã `++` trong ctor của chính nó). Getter trả field đã chốt và **không đọc lại blob**. Blob từ đây chỉ còn là sổ để ĐỒNG BỘ, không phải nguồn của con số đang dán lên log — hai cái tên của một lượt chơi (`sessionId` · `sessionUid`) lại đi chung một vòng đời.
- Chốt **lười** (lần đọc đầu) chứ không chốt trong ctor: ctor repository nằm trong lúc container dựng DI, còn `PlaySessionData` đi qua `AccountManager` (singleton tĩnh, tự nạp save lúc chạm) — đọc ở đó là buộc thứ tự khởi tạo của hai hệ vào nhau mà chẳng được gì, vì lần đọc đầu luôn sớm hơn log đầu.
- `UpdateRemoteData` **thôi `++`** (cú `++` đã nằm trong lúc chốt; `++` lần hai là đếm đôi) và ghi sổ bằng `Math.Max(sổ hiện có, số đã chốt)` — sổ chỉ được đi LÊN: bản server có thể đang cao hơn ta ở máy thứ hai của cùng account, ghi đè bằng số nhỏ hơn là hai máy đụng số mãi. Đọc `SessionId` ở đây cũng là mốc chốt muộn nhất, cho ca cả phiên không bắn log nào trước `Init`. `OverwriteRemoteData` đẩy **số đã chốt** thay vì sổ nhà thô (bỏ luôn một dòng gán `lastLoginInDateTimeLocal` lặp hai lần).
- ⚠ **Chiều ngược mới là chiều đau**: đường (1) làm log khúc đầu lượt N+1 mang `sessionId` của lượt N, nên **một `sessionId` ứng hai `sessionUid`** — mọi phép gộp khoá theo `sessionId` sẽ nhồi khúc đầu lượt mới vào lượt cũ. Bản vá bỏ hẳn nguyên nhân chính (số đổi GIỮA một lượt chạy) và kéo sổ lên ở **cả hai mốc** `Init` · `OnPostStop`, nhưng **không sửa được số đã ghi**.
- ⚠ **`sessionId` không phải khoá nhận diện lượt chạy, kể cả sau bản vá** — còn ba ca trùng số giữa hai lượt mà client không tự biết: cài lại app (sổ nhà về từ đầu, lượt đó dùng lại số của một phiên cũ), hai thiết bị cùng account chốt trùng số, và app chết trước khi kịp đẩy sổ. Phía kho nên khoá theo **`sessionUid`** — **vĩnh viễn**, không chỉ cho dữ liệu trước mốc này.
- ⚠ Đồng thời vá một hỏng hóc im lặng ở `BigData` 1.5.7: `AdSessionCounterState` so `sessionId !=` để xoá sổ đếm theo phiên, nên mỗi cú đổi số giữa phiên là **xoá oan** — `sessionAdLoadIndex` / `sessionAdLoadSuccessIndex` / `sessionAdShownBefore` về 1, `firstAdEcpm` / `previousAdEcpm` mất trắng. Mà interstitial ĐẦU TIÊN (đúng cái luật chọn sàn nhắm vào) là lượt hay rơi vào cửa sổ trước `Init` nhất. Vá nguồn ở đây là `BigData` **không phải sửa gì**.
- ⚠ Đánh đổi đã biết: máy **mới cài** thì lượt chạy đầu dán số của sổ nhà, có thể không khớp sổ server cho đúng lượt đó; lượt sau tự hội tụ (sổ server `+1`). Chấp nhận, vì số nhảy GIỮA hai lượt là vô hại còn nhảy TRONG một lượt là sai dữ liệu. App chết trước `Init` thì sổ chưa tiến và lượt sau dùng lại số cũ — y như trước bản này (cú `++` cũ cũng chỉ nằm trong `Init`), không phải hồi quy.
- Nâng pin `falcon.modules.core.bigdata` **1.5.5 → 1.5.8** (owner chốt 09/11). Sàn theo NGHĨA là
  **1.5.7** — đó là bản sinh ra `AdSessionCounterState`, thứ mà bản vá này bù cho (xem bullet ⚠ bên
  dưới) — nhưng owner chọn 1.5.8 để pin trùng với bản BigData đang dùng thật, khỏi hai số hiệu cho
  một thứ. ⚠ Pin là **sàn**, không phải "bản tôi đang chạy": ai dùng module này phải có BigData
  ≥ 1.5.8. Nếu 1.5.8 chưa lên registry thì pin không resolve được — kiểm trước khi publish.
- Tín hiệu kiểm: module này **không có test assembly** và chuỗi phụ thuộc của nó (Unity Purchasing · GoogleMobileAds · Firebase · Ump) không dựng được ngoài Unity, nên không có unit test cho nhánh này. Đã xác nhận bằng: compile **sạch 0 lỗi** ở file sửa với cây phụ thuộc thật (`PlaySessionData` · `FGameData` · `LazyVal` · `ReservePlayerSessionRepository` đều bind), và bộ test `BigData` giữ **357 pass / 0 fail**.
----------------------------------


## 1.1.7
- **Đọc được service remote config THỨ HAI cho chế độ log load ad** (`BigData` 1.5.5): thêm `ServerAdLoadLogConfig` gắn `[Primary]` ghi đè `IAdLoadLogConfig` của BigData, và `AdLoadLogConfigCms` (khoá `fAdLoadLogSplit` phía `Core.RemoteConfigCms`). Luật **OR**: chỉ cần MỘT trong hai service (`Core.RemoteConfig` của BigData · `Core.RemoteConfigCms` ở đây) gửi `false` là SDK quay về bản tin load GỘP, ngay giữa phiên; khoá vắng mặt tính là `true` nên không cấu hình gì trên server thì vẫn là chế độ TÁCH.
- Vì sao nửa CMS nằm ở module này mà không ở BigData: BigData là module **mọi game đều có** nên không được phụ thuộc cứng vào `Core.RemoteConfigCms` (game chạy `MediationMO` không cài CMS sẽ gãy compile). Module này thì **vốn đã** tham chiếu `Core.ThirdParty.Mediation`, mà Mediation tham chiếu cứng CMS — nên thêm reference CMS ở đây **không** thêm phụ thuộc mới cho hệ. Khuôn ghi đè giống `ServerTimeRepository` ↔ `ReserveTimeRepository` module này đã dùng 4 lần.
- ⚠ Luật DI (`DirectParamRequirement`): nhiều impl mà **không cái nào** `[Primary]` thì container NÉM. Bản cơ bản `ReserveAdLoadLogConfig` của BigData cố ý không đánh dấu, bản ở đây là `[Primary]` (ưu tiên 0) — game muốn tự ghi đè phải dùng `[Primary(1)]`.
- Pin: `falcon.modules.core.bigdata` **1.3.2 → 1.5.5** (interface `IAdLoadLogConfig` chỉ có từ 1.5.5 — pin cũ là pin sai, không phải pin cũ vô hại), thêm `falcon.modules.core.remoteconfigcms` 1.0.4.
----------------------------------


## 1.1.6
- ALevelHeartBeatService: CurrentLevelTotalScore/CurrentLevelBoosterUsed/CurrentLevelExtraParams đổi từ virtual-mặc-định về ABSTRACT — đảo một phần quyết định 1.1.4. Lý do: ba giá trị này chỉ GAME biết, SDK không cache được, nên mặc định 0/null là ĐIỀN HỘ chứ không phải suy ra — quên override là levelProgress/boosters mất im lặng và không có heuristic nào bắt được (null trông y như "không có thật"). Giờ "không có" phải là câu trả lời game tự gõ (=> 0 / => null), quên thì gãy compile chứ không sai im lặng. IsPlayingLevel giữ virtual vì SDK TỰ SUY được (playTurnId != null) — ranh giới bắt buộc/mặc định giờ đi theo đúng trục ai-biết. ⚠ BREAKING với subclass hiện có: thêm 3 dòng override là xong, compiler chỉ tận nơi
----------------------------------


## 1.1.5
- Patch statistic meta logic when conencting to server takes too many time
----------------------------------


## 1.1.4
- FIX chia cho 0 trong ALevelHeartBeatService: levelProgress = score*100/CurrentLevelTotalScore, game nào chưa có hệ điểm (hoặc màn chưa nạp xong) trả 0 là ném DivideByZeroException — mà hàm này chạy trong vòng lặp gom log lúc app pause nên nó giết luôn log của các generator còn lại, không riêng bản tin heartbeat. Giờ totalScore <= 0 thì bỏ qua levelProgress; phép chia cũng nâng lên long để score lớn không tràn khi nhân 100
- ALevelHeartBeatService giảm từ 7 xuống 3 hàm bắt buộc override: IsPlayingLevel mặc định đọc "có lượt nào đang mở không" từ chính SDK (playTurnId != null), còn CurrentLevelTotalScore/BoosterUsed/ExtraParams thành virtual với mặc định "không có". Ba cái còn bắt buộc (playTime/score/numberMove) là thứ chỉ game biết và cũng chính là nội dung của bản tin heartbeat — để mặc định thì game override 0 hết sẽ gửi bản tin rỗng, tệ hơn là không đăng ký service
- Không gửi số giả: playTime/score/movesUsed chỉ điền khi > 0, không thì để field vắng mặt (§H4) thay vì gửi 0 — 0 trông như giá trị hợp lệ nên không ai đi kiểm
- Dùng LevelHeartBeatParamV2 (tên mới sau khi sửa typo bên BigData) trong ALevelHeartBeatService
- ALevelHeartBeatService: bỏ elo = -1 (mỗi heartbeat bắn 1 Debug.LogError rồi ép elo về 0); elo để null nên bị loại hẳn khỏi payload theo FKey(RemoveIfNull)
----------------------------------


## 1.1.3
- add ALevelHeartBeatService
----------------------------------


## 1.1.2
-Giảm thời gian đợi server xuống còn 15s
----------------------------------


## 1.1.1
- Ngừng lưu non-test configs lên server
----------------------------------


## 1.1.0
- Make ServerPlayerAbTestRepository to reset _configs on Save new config (on update from net)
----------------------------------


## 1.0.9
- Điều chỉnh lại để không ném exception từ WaitInit nữa
----------------------------------


## 1.0.8
- Sửa lại theo cấu trúc của Devkit
----------------------------------


## 1.0.7
- Disable ReservePlayerAdRepository
- Disable ReservePlayerIapRepository
----------------------------------


## 1.0.6
- Chỉnh lỗi race condition khiên config không được cập nhật trên 1 số thiết bị
----------------------------------


## 1.0.5
- Thêm MmpInfoLog
----------------------------------


## 1.0.4
- Thêm header creator cho một số file còn thiếu
----------------------------------


## 1.0.3
- Sửa lại cấu trúc phụ thuộc của ServerTimeRepository do ghi đè nhầm trong phiên bản trước
----------------------------------


## 1.0.2
- Sửa bug không cập nhật RetentionChanged trong ServerPlayerSessionRepository
----------------------------------


## 1.0.1
- Khởi tạo module
----------------------------------