### CHANGELOG
# 1.4.48
- FloorPersonalization khai ca gọi Rule hỏng lên BigData (`Request-Mediation-FirstInterFloorRule.md` W1, Q2 chốt 23/11): opportunity rơi về load mặc định vì Rule lỗi thì MỌI dòng load interstitial mặc định (MultiCall / unit đơn) của opportunity đó mang `ecpmFloorType`, tới khi opportunity kế bắt đầu:
  - `rule_timeout`: quá ngân sách `requestTimeoutSeconds`
  - `rule_error`: lỗi mạng / HTTP, body không parse được, response không hợp lệ, exception lúc gọi
  - Control / `base_line` (flow rỗng) KHÔNG mang vai — phân biệt bằng `realtimeVariants` mức user
  - vai chụp LÚC GỬI request (`AdTypeMultiCall.requestFloorType`, cùng chỗ chụp `requestTier`; unit đơn chụp ở `LoadInterstitial`), dòng kết quả đọc lại — request bay qua ranh giới opportunity giữ vai của opportunity đã gửi nó
  - `IInterstitialPersonalization.DefaultLoadFloorType` (lõi), bản rỗng trả null
- Timeout Rule tự đếm theo realtime rồi `Abort()` thay cho `UnityWebRequest.timeout` (chỉ nhận giây nguyên, làm tròn lên, và báo timeout chung mã với lỗi mạng): `requestTimeoutSeconds` nhận số lẻ (0.8 = 800ms); `UnityWebRequest.timeout` giữ làm lưới an toàn = ngân sách làm tròn lên + 1s
- Era: trước bản này dòng load mặc định khi Rule lỗi KHÔNG mang vai, trộn với dòng của game không chạy flow
- `adWhen` + `triggerPlayTurnId` cho hàm chiếu QC (`Request-Mediation-AdWhenTrigger.md`, hợp đồng BigData đợt 3 B12):
  - `MediationManager.ShowInterstitial` / `ShowRewardedVideo` thêm 2 tham số tuỳ chọn ở CUỐI (code game cũ vẫn biên dịch)
  - đầu hàm (trước nhánh RemoveAds / luật / Lua / kho) gọi `Ad.UpdateContext(type, adWhen, triggerPlayTurnId)`; cả hai trống thì không gọi (giá trị trống không xoá gì bên SDK)
  - rewarded đổi sang inter (`showInterstitialInstead`): cửa `ShowInterstitial(showForRewarded = true)` của Max / GMA / LevelPlay và `MultiCallProvider.ShowInterstitial` chép ngữ cảnh của lượt rewarded sang interstitial (`MediationManager.ApplyRewardedContextToInterstitial`, chỉ trong phạm vi lời gọi `ShowRewardedVideo`)
  - README: ví dụ gọi với `FAdWhen.*` + `Level.LastEndedTurnId` / `Level.CurrentTurnId`
- pin `falcon.modules.core.bigdata` 1.8.0 (bản đầu có `triggerPlayTurnId` / `FAdWhen` / `Level.CurrentTurnId`)
# 1.4.47
- chế độ load MAX chốt cả phiên ở `FalconMaxService.Init` theo thứ tự **Lua → Databuckets → MultiCall → unit đơn**:
  - LUA: không Databuckets, không MultiCall, không id đơn (bỏ đường candidate FloorPersonalization trong chế độ Lua)
  - DATABUCKETS: interstitial do workflow floor lo; MultiCall / id đơn vẫn chạy cho rewarded / banner / AOA và là đường dự phòng interstitial khi Rule trả Control / API lỗi
- config FloorPersonalization chuyển lên remote config: key `moMulFloorPersonalizationConfig` (JSON, cùng field với SO cũ + `analyticsEndpoint`), đọc CMS trước, CMS không khai key thì data4game, không nguồn nào có thì không bật. CMS khai key (kể cả rỗng) là thắng. Bỏ `SOFloorPersonalizationSetting` (script, asset, inspector, menu DataBucket Settings). MediationManager chờ config chốt nguồn (trong trần 7s chung) khi phiên không Lua
- FloorPersonalization tách khỏi lõi: lõi chỉ gọi qua `IInterstitialPersonalization` / `InterstitialPersonalization.Current` (`Service/Personalization`), FloorPersonalization tự `Register` lúc `SubsystemRegistration`. Xoá cả thư mục `Service/FloorPersonalization` vẫn compile, phiên rơi về MultiCall / unit đơn
  - `AdSessionCounter` chuyển về lõi; lõi tự cộng interstitial đã chiếu trong `InterstitialPersonalization.OnInterstitialDisplayed` rồi mới chuyển cho module
  - `FloorAdCandidate` kế thừa `InterstitialAdCandidate` của lõi (`adUnitId`, `networkName`, `ecpmFloor`, `estimatedEcpm`)
- thêm Databuckets Analytics SDK 4.0.1 (`Scripts/Runtime/DatabucketsAnalytics`, asmdef riêng `RealtimeEvents`; Mediation Runtime tham chiếu thêm) và cầu nối `FloorPersonalization/Databuckets/DatabucketsBridge`: chỉ `Init` khi phiên chốt chế độ Databuckets (`InterstitialFloorPersonalization.Activated`), dùng `apiKey` + `analyticsEndpoint` của config; Rule dùng uid / country / device model của SDK; response Rule -> `realtime_segments` / `realtime_variants` / `device_tier`; gửi `ad_request` (mỗi lần load) và `ad_impression` (`is_show` 1/0) kèm param personalization cho interstitial. ⚠ Tên cột của 2 event chưa đối chiếu tracking plan của Databuckets
- log luồng FloorPersonalization + Databuckets qua `FloorPersonalizationLog`, bật bằng define `FLOOR_PERSONALIZATION_LOG` (hoặc `ENABLE_DEBUG_LOG`)
# 1.4.46
- Đưa lại phần 1.4.42 bản release 1.4.45 bị thiếu (1.4.45 build từ một 1.4.42 cũ hơn), cộng thêm khai slot DFPool trước MAX init:
- `MaxPreInitAdUnits` (`Service/MultiCall/Interface/`): slot MAX của DFPool được khai TRƯỚC MAX init ở cả 3 phiên (MultiCall / Lua / unit đơn): có trong `disable_b2b_ad_unit_ids`, có trong mảng `MaxSdk.InitializeSdk`, và được set `disable_auto_retries` sau init (thiếu bước nào cũng có thể crash). Phiên unit đơn có slot DFPool chạy kèm thì id đơn cũng đi đủ 3 bước (xem mục unit đơn dưới). `MediationManager` chờ config DFPool chốt (trong trần 7s) trước khi init MAX; DFPool chỉ load slot đã khai, slot có trong config về muộn bị bỏ cả phiên (khi đó AdMob vẫn dùng id `mediation = admob` của `sc_ad_bid_floor`). Thêm `MultiCall.AddExtraRewardedIds`
- Lua: chốt danh sách id MAX lúc init (`LuaAdMonetize.MaxAdUnitIds`, gọi ngay trước `MaxSdk.InitializeSdk`). Sau init, `context.Max.Load...` với id MAX KHÔNG có trong danh sách đó (bid floor v1/v2 về muộn, script thay giữa phiên, id không ghi dạng chữ trong script) trả `false`, không load (log 1 lần / id): các id này không có trong `disable_b2b_ad_unit_ids` lẫn mảng init nên có thể crash. Id AdMob không bị giới hạn
- So giá lượt show MAX gom về một luật chung cho Lua / MultiCall / unit đơn (`FalconMaxService.PickInterstitial` / `InterBeatsRewarded`), giá đọc từ ad ĐANG SẴN SÀNG trong SDK:
  - Inter: chiếu inter đắt nhất trong bên chính (ad Lua / MAX MultiCall / unit đơn) | candidate FloorPersonalization | nguồn phụ (DFPool / meta AdMob). Hoà: FloorPersonalization > bên chính > nguồn phụ. Trước đây candidate FloorPersonalization luôn được chiếu TRƯỚC, không so giá (MultiCall / unit đơn), và ở phiên Lua chỉ được xét khi Lua không có ad
  - Rewarded, `showInterstitialInstead = true`: inter đắt nhất (mọi nguồn trên, gồm cả FloorPersonalization) đắt hơn hẳn rewarded đắt nhất (bên chính | nguồn phụ) -> chiếu inter (lượt inter chọn inter đắt nhất); hoà -> rewarded. Trước đây FloorPersonalization không tham gia lượt rewarded
  - Rewarded, `showInterstitialInstead = false`: inter KHÔNG tham gia so giá, chỉ chọn rewarded đắt nhất. Trước đây MultiCall bỏ qua cờ này (vẫn chiếu inter nếu đắt hơn). `IMultiCallProvider.ShowRewardedVideo` / `MultiCall.ShowRewardedVideo` thêm tham số `allowInterstitial` (mặc định `true`); GMA / LevelPlay truyền `showInterstitialInstead` xuống
  - Unit đơn: bỏ nhánh so `_eCpmInterstitial > _eCpmRewarded` (giá cache, không kiểm tra ad còn trong SDK -> có thể chọn inter đã mất rồi báo fail dù rewarded sẵn sàng)
  - `ReadyInterstitialInfo` theo đúng thứ tự chọn trên; thêm `IMultiCallProvider.BestMaxRevenue` / `MultiCall.BestMaxRevenue` (giá ad MAX đắt nhất đang sẵn sàng, không tính nguồn phụ, chỉ đọc)
- FloorPersonalization (khi bật) ở phiên Lua / unit đơn: unit được set `disable_auto_retries` sau MAX init (trước đây chỉ phiên MultiCall có); phiên unit đơn truyền unit FloorPersonalization vào mảng `MaxSdk.InitializeSdk`
- Unit đơn: CHỈ có id đơn (không luồng nào chạy kèm) -> `MaxSdk.InitializeSdk()` không tham số, không `disable_b2b_ad_unit_ids`, không `disable_auto_retries` (như cũ). Có luồng chạy kèm (FloorPersonalization bật / slot DFPool) -> id inter + rewarded đơn của `SOMediationSetting` cũng đi đủ 3 bước: `disable_b2b_ad_unit_ids`, mảng init, `disable_auto_retries` sau init (luồng đơn tự load lại khi fail / khi đóng ad). Banner / AppOpen chỉ vào mảng init, như MultiCall / Lua. Trước đây id đơn không đi bước nào kể cả khi có FloorPersonalization chạy kèm
- Mọi API theo ad unit của MAX chỉ chạy SAU đủ 3 bước CallBeforeInit -> `MaxSdk.InitializeSdk` -> CallAfterInit: `FalconMaxService._isInitialized` bật SAU CallAfterInit (trước đây bật trước); thêm `MaxPreInitAdUnits.IsAfterInitDone`. DFPool chờ `IsAfterInitDone`, Lua chờ callback init MAX (`_maxInitDone`) thay cho `MaxSdk.IsInitialized()` — native có thể báo true trước khi callback init của Unity chạy, tức là trước CallAfterInit. `IsRewardedVideoReady` / `ShowRewardedVideo` / `ReadyInterstitialInfo` / `ShowAppOpenAdIfReady` kiểm tra init trước khi gọi `MaxSdk.Is...Ready` / load (trước đây gọi được trước init). Id MAX về sau init bị bỏ cả phiên: MultiCall (dictionary dựng 1 lần lúc init), Lua (`MaxAdUnitIds` chốt lúc init), DFPool (`MaxPreInitAdUnits`)
- MAX init chờ ĐỦ remote config để biết hết adUnitId MAX của mọi luồng (`MediationManager.MaxConfigsReady`): `sc_ad_monetize_lua_script` + `sc_ad_bid_floor_v2` (khi có script Lua), `sc_ad_bid_floor` (MultiCall), CMS (provider index MultiCall), `moMulDfPoolConfig` (DFPool). Trần 7s giờ tính từ SAU UMP bằng `Time.realtimeSinceStartup` (trước đây tính cả lúc chờ UMP và theo `Time.time` nên UMP lâu hơn 7s là init ngay, không chờ config nào; `timeScale = 0` thì đồng hồ đứng). Hết trần: init bằng cache, log các nguồn còn thiếu, id MAX về sau init bị bỏ cả phiên. Nhánh LevelPlay đổi đồng hồ tương tự
- `TikTokSettings.asset`: nếu bản update trước xoá file ở `Scripts/Runtime/TikTokBusinessSDK/Resources` mà không tạo ở `Assets/FalconAssets/Modules/Core/ThirdParty/Mediation/Resources`, đặt lại ở chỗ mới, giữ GUID `567563d98648af04e8187bfcc25db9db`
- DFPool cùng bộ mốc với meta AdMob (hai nhánh của cùng AdMob: show thẳng / show qua MAX), ở cả 3 phiên:
  - request AdMob gửi extras multi-platform mediation như meta AdMob: `auction_context=parallel`, `ua_channel`, `days_since_install` (Java `DFPoolBridge.buildRequest`; `loadAd` thêm 2 tham số)
  - `RecordAuctionEvent` (hob) cho mọi response (fill lẫn no-fill), `RecordImpressionEvent` lúc AdMob của DFPool thắng (response ad chiếu + response AdMob thua cùng lượt) và lúc MAX thắng (ad MAX mediation chính / network khác fill slot) — `DFPoolListener.onAdLoaded/onAdFailed` thêm `responseId`
  - MMP `af_inters_displayed` / `af_rewarded_displayed` lúc ad lên hình, kèm `af_level`
- MMP nhánh AdMob (meta AdMob + DFPool) hết đếm đôi / lệch mốc:
  - `af_inters_show` / `af_rewarded_show`: meta AdMob bắn thêm ở `ShowAd` trong khi `FalconMaxService` đã bắn cho mọi lượt show -> mỗi lượt ad AdMob đếm 2. Giờ chỉ `FalconMaxService` bắn; meta AdMob chỉ bắn ở đường show độc lập `AdmobFullscreenAds.Show`. Era: số `af_*_show` của bản trước bị phồng đúng bằng số lượt AdMob thắng giá
  - `af_rewarded_displayed` của meta AdMob: bắn lúc ad lên hình như MAX (trước đây lúc nhận thưởng -> rewarded bị tắt giữa chừng mất event)
  - event MMP của meta AdMob (`onTrackEvent`) kèm `af_level` như event MMP của FalconMaxService
  - load result: ad mặc định (không sàn) `floor` vắng thay vì 0, như meta AdMob
  - impression / show fail của ad mặc định: `ecpmFloor = 0` (trước đây trống), như meta AdMob
- meta AdMob: mốc shown BigData có `adUnitId` (hook `onAdShown` thêm tham số adUnitId), như MAX / Lua / DFPool
- Tầng phiên của lượt show nguồn phụ (meta AdMob / DFPool) giống nhau ở MultiCall / unit đơn / Lua và đủ như ad MAX: MultiCall thêm `start_`/`finish_`, Firebase `ad_inter_show` / `ads_reward_show_success`, `AdsClosed(Interstitial)` (trước đây lượt inter AdMob không tính vào cap / interval), GameEvent `MEDIATION_*_CLOSED`. `FalconMaxService.ShowSecondaryAd` KHÔNG bắn MMP displayed (nguồn phụ tự bắn)
- meta AdMob: close interstitial gửi `adCompleted = null` (trước đây `true`), cùng nghĩa MAX / Lua / DFPool. Era: `ad_completed` của inter AdMob meta trước bản này luôn true
- meta AdMob: no-fill khai báo `RecordAuctionEvent` bằng response của `LoadAdError` (trước đây đọc `ad?.GetResponseInfo()` = null nên không bao giờ gửi)
- hob (`BestExternalRevenue`): phiên Lua lấy ad MAX đắt nhất của Lua, phiên unit đơn lấy ad id đơn đang Ready (trước đây chỉ đọc kho MultiCall -> 2 phiên này luôn hob=0)
- Nguồn phụ (`SecondaryAdSource`: DFPool / meta AdMob) chiếu được ở phiên **unit đơn** và **Lua**, cùng luật so giá với MultiCall (trước đây chỉ MultiCall hỏi nguồn phụ -> ad DFPool / meta AdMob load về nằm chết ở 2 phiên này):
  - `FalconMaxService.ShowSecondaryAd`: log như nhánh nguồn phụ của MultiCall (`CSAdStart`/`CSAdFinish`/`CSAdInfo` mediation `Admob`, Firebase `meta_mediation_admob_impression` với uplift so ad bên chính bị bỏ lại) + start_/finish_, `AdsClosed`, GameEvent, đếm inter / rewarded
  - inter: nguồn phụ đắt hơn hẳn ad MAX đơn / ad Lua đắt nhất thì chiếu nguồn phụ (hoà -> bên chính). Lua: không bên nào có ad mới rơi về candidate FloorPersonalization như cũ
  - rewarded: 4 ứng viên (RW chính, RW phụ, inter chính, inter phụ), inter chỉ chiếu thay khi đắt hơn hẳn; nguồn phụ không có ad -> luồng cũ y nguyên
  - `IsInterstitialReady` / `IsRewardedVideoReady` / `ReadyInterstitialInfo` của unit đơn + Lua tính cả nguồn phụ
  - Lưu ý: phiên unit đơn có meta AdMob giờ cũng chiếu ad meta AdMob (trước đây load mà không chiếu)
- `ExternalMaxAdUnits.MarkPriceAnchor` / `IsPriceAnchor`: unit MAX của Lua là ad MAX thật -> DFPool lấy revenue làm mốc giá bước 2 (trước đây bị bỏ vì là unit ngoài -> phiên Lua DFPool chỉ chạy `defaultAdId`)
- Lua + DFPool: Lua coi như MultiCall — format DFPool đã nhận thì Lua KHÔNG dùng AdMob: `context.Admob.Load...` trả `false` ngay (log 1 lần / id), lịch AdMob đặt trước lúc DFPool quyết định bị huỷ về Idle (`BỎ LOAD`); id AdMob chờ DFPool quyết định (`AdmobTakeover.IsDecided`) rồi mới gửi. Mỗi ad MAX của Lua load xong -> DFPool chạy bước C (thang `targets`) như với ad MAX của MultiCall. Format DFPool không nhận thì AdMob của Lua chạy như cũ
- DFPool nhận quyền AdMob: format nào DFPool bật (`moMulDfPoolConfig` hợp lệ + native init được) thì meta AdMob bỏ id `mediation = admob` của `sc_ad_bid_floor` cho format đó (tránh request AdMob gấp đôi); DFPool không chạy format đó thì meta AdMob như cũ. Thêm `AdmobTakeover` (`Service/MultiCall/Interface/`): meta AdMob chờ DFPool quyết định (trần `CMS_FIRST_TIMEOUT_SECONDS` + 5s) rồi mới init, DFPool bật muộn giữa phiên thì meta AdMob đọc lại id. Xoá thư mục DFPool vẫn compile, meta AdMob không chờ gì
# 1.4.45
- FloorPersonalization khai cột cho BigData (`Request-Mediation-FirstInterFloorRule.md`): dòng load của workflow mang `adCandidateId` (= `candidateId` của candidate, cũng là mã trên impression), `ecpmFloorType` / `ecpmFloorPercentile` / `adLoadAttempt` chụp lúc GỬI request, `adAlgorithm = floor_personalization`. Era: trước bản này 4 cột trống trên dòng load của workflow
- `FloorPersonalizationCustomInfo` (`IFCustomInfoRepository`): `deviceTier`, `realtimeSegments`, `realtimeVariants` mức user, cập nhật sau mỗi response Rule hợp lệ (ảnh chụp bất biến); `device_tier` lưu PlayerPrefs, nạp lại lúc khởi động. Chưa có response thì vắng key
- Rule `device_model` lấy `IFDeviceInfoRepository.DeviceModel` (chữ thường, cùng số với kho) thay `SystemInfo.deviceModel`
# 1.4.44
- `TikTokSettings.asset` chuyển sang `Assets/FalconAssets/Modules/Core/ThirdParty/Mediation/Resources` (giữ GUID) cạnh `SOAdsSetting` / `SOMediationSetting`: export module không mang file setting theo nên import sang project khác không ghi đè app id / token; menu `TiktokSettings` tạo asset ở đó
# 1.4.43
- TikTok SDK chuyển từ `Assets/TikTokBusinessSDK` vào `Scripts/Runtime/TikTokBusinessSDK` (giữ GUID, vẫn qua `TikTokBusinessSDK.asmref` vào asmdef Runtime); `TikTokDefine.WATCHED_FOLDER` theo đường mới. Project đã có `Assets/TikTokBusinessSDK` từ bản cũ: xoá thư mục cũ sau khi import, nếu không sẽ trùng type
# 1.4.42
- thêm `LuaDynamic`
- `DFPoolReporter` → `AdShowReporter` (`Service/BigData/`, namespace `...Mediation.Runtime`, giữ GUID): Lua không còn phụ thuộc thư mục `DFPool` — xoá nguyên thư mục `Service/DFPool/` vẫn compile
- `f_sdk_ad_show_skipped` (`Request-Mediation-ShowGate.md` bản 2), pin BigData 1.7.5:
  - `MediationManager.ShowInterstitial`: No Ads → đúng một `OnShowSkipped(..., NoAds)`, rồi vẫn đi qua cổng Lua + provider như cũ (không ghi thêm dòng); luật MediationMO chặn → `OnShowSkipped(lý do)` + `onInterstitialClosed`; Lua `IsShowInterstitial` chặn → `OnShowSkipped(..., Other)`; qua hết mới bắn `OnShowAttempt`. Mọi dòng skipped mang `adAvailable` + `readyAd` (`GetReadyInterstitialInfo` trả null khi không có ad)
  - `adMediation` của ad đang chờ: Lua = `mediation` của record (`Max`/`Admob`), MultiCall nguồn phụ thắng = `Admob`, GMA đơn = `Admob`; MAX để null (BigData lấy của view)
  - Mỗi lượt `ShowInterstitial` trả đúng một callback: GMA (`_settings` null / tắt / chưa init) và IronSource (`_settings` null) gọi `onFail` thay vì `return` im lặng. README mục 6: game luôn gọi `ShowInterstitial` ở mỗi điểm chiếu, `CanShowInterstitial` chỉ để quyết định popup
  - `AdSkipReasonMap` (`Service/BigData/`): `StatusShowAds` → `AdSkipReason`, `LevelLimit` → `LevelCap`
  - "Ad sẽ chiếu": `MediationManager.GetReadyInterstitialInfo()` / `GetReadyAppOpenInfo()`; MAX chọn theo đúng thứ tự show (Lua `PeekBest` → candidate FloorPersonalization `PeekBestCandidate` → `MultiCall.PeekInterstitial` → ad vừa load của unit đơn); GMA: MultiCall hoặc network của ad AdMob. `MultiCallProvider.ShowInterstitial` và `PeekInterstitial` dùng chung hàm so giá; Peek KHÔNG gọi `SyncMaxSlotsWithSdk` (không xoá giá / không kích load / không đụng thuật toán 8), chỉ bỏ slot SDK đã mất ad — cùng kết quả chọn. Single: `_readyInterstitial` gán ở cuối `OnInterstitialLoadedEvent` — chỉ unit đơn tới được (FloorPersonalization / MultiCall return sớm, Lua / DFPool bị `ExternalMaxAdUnits` chặn)
  - App open MAX: `MediationManager.GetAppOpenSkipReason()`, `FalconMaxService.AppOpenSkipReason()` (cooldown / level gate); `OnApplicationPause` bỏ `!IsRemoveAds()`; `Wait02S`: No Ads → `OnShowSkipped(AppOpen, "AppOpen", NoAds)`, luật chặn → lý do; kèm `adAvailable` + `readyAd`
- Lua: mốc `OnShown` bắn NGAY TRƯỚC `loader.Show` (trước đây ở callback Displayed). iOS MAX bắn RevenuePaid của ad toàn màn trên thread nền TRƯỚC Displayed → impression đóng dấu view trước, show tới sau bị xoay `adViewId` mới (show/close lệch id impression, `request_to_show_ms` null) và mất show khi app bị kill lúc ad đang chiếu. Show không lên được (`not_ready_at_show`) bắn `OnShowFailed`. Era: `ad_view_id` show/close của Lua trên iOS trước bản này không ghép được với impression
- Lua: impression MAX có thêm `waterfallPosition`, `adInstanceName`, `adFormatRaw` của MAX, `extraMeta` (`creative_id`, `dsp`) và `ecpmFloor` (floor của lượt load đang chiếu) — trước đây vắng, cùng bộ cột với đường MAX/multicall
- Lua + DFPool: close của Interstitial gửi `adCompleted = null` (trước đây luôn `true`), cùng nghĩa với multicall; Reward giữ "đã nhận thưởng". Era: `ad_completed` của Interstitial Lua/DFPool trước bản này không dùng được
- Lua: `CSAdStart.adId` lấy view của format thật đang chiếu (inter chiếu thay rewarded trước đây đọc view Reward)
- Lua log cùng bộ với multicall8:
  - load: `tier` = multiplier của id, `floor` chỉ AdMob (MAX để trống như multicall), `errorMess` = mã lỗi MAX (trước đây "mã + message"), `networkName` chỉ khi thành công; Firebase `ad_inter_load_success` / `ad_inter_load_fail` / `ads_reward_load`; `CSAdRequestLog` (`decisionPolicy` mặc định `client-lua`); nhãn `LOAD_ERROR` khi unit fail 6 lần liên tiếp mà format không còn ad
  - show: `OnShown` có `adUnitId`; displayed thêm MMP `af_inters_displayed` / `af_rewarded_displayed`; start_/finish_, `CSAdStart`/`CSAdFinish`, `AdsClosed`, đếm inter theo format THẬT đang chiếu (inter chiếu thay rewarded = Interstitial)
  - impression MAX: Firebase / MMP / client LTV / Bamboo / TikTok / hoãn log ad đầu tiên đi chung đường `FalconMaxService` với multicall (format MAX `INTER`/`REWARDED`); BigData giữ ở `AdShowReporter`
  - click: Firebase `ad_inter_click` / `ads_reward_click`; show hỏng: Firebase `ads_reward_show_fail`, BigData có floor + eCPM ước tính; nhận thưởng: Firebase `ads_reward_complete`
  - `CSAdFinish` / `CSAdInfo`: mediation `MAX`, placement rỗng → `Unknown`, như multicall
  - nhãn `LOAD_ERROR`: unit fail liên tiếp đủ số bậc `timeConfig` của nó (thang retry của `sc_ad_bid_floor`, cùng luật multicall) mà format không còn ad; id không có `timeConfig` thì không bắn
  - AdMob: load `ecpmFloor` = floor đã xin (0 khi không đặt sàn), `errorMess` = mã lỗi; impression `adFormatRaw` = `Interstitial` / `Rewarded` như `AdmobMediationFalconBridge`; Firebase `meta_mediation_admob_impression` (`uplift_revenue` = giá AdMob − giá MAX đắt nhất bị bỏ lại) lúc ad đóng, như nhánh AdMob của multicall
- Lua API mới (cho script giống thuật toán 8):
  - `context.Max.CancelInterstitial(adUnitId)` / `CancelRewarded(adUnitId)`, `context.Admob.Cancel...(adUnitId [, linkedAdUnitId])`: huỷ lịch load đang chờ (Waiting -> Idle), trả `true` nếu đã huỷ. Trước đây script chỉ "đánh dấu huỷ", request vẫn được gửi với floor của base cũ
  - `context.isOffline` (theo `Application.internetReachability`), đồng bộ trước mỗi lượt gọi vào Lua
  - `OnNetworkBack()`: Unity gọi khi mất mạng rồi có mạng lại (kiểm tra mỗi `networkCheckIntervalSeconds` của `moMulMaxProvider8Config`), chỉ sau khi MAX init xong
- `LuaAdTest.txt`: retry theo `timeConfig` của từng id (rỗng mới dùng `CONFIG`); huỷ lịch thật khi có base mới / bậc bị đẩy khỏi nhóm; `attemptCount` đếm lần GỬI như `countLoadAttempt`; offline chặn request, fail lúc offline không tính, `OnNetworkBack` nạp lại id default (thay lịch đang chờ) rồi lấp bậc. Bản app cũ không có API mới vẫn chạy theo cách cũ (offline theo mã lỗi, retry 2s; huỷ = đánh dấu)
- `LuaAdTest.txt`: bậc floor fail đủ `maxFails` thì CHẶN mọi bậc cao hơn (huỷ lịch retry đang chờ của chúng, không gửi request mới) tới base mới — trước đây bậc kế tiếp được kéo lên thay chỗ (vd ×1.0 dừng thì ×1.8 bắt đầu load). Khác thuật toán 8 C# (vẫn kéo bậc kế tiếp lên)
- DFPool: `OnShown` bắn NGAY TRƯỚC `ShowMax` (trước đây ở `OnMaxDisplayed` → cùng lỗi xoay `adViewId` như Lua), có `adUnitId`; show hỏng có eCPM ước tính; impression ad foreign MAX có `waterfallPosition` / `adInstanceName` / `adFormatRaw` / `extraMeta` và log Firebase / MMP / TikTok qua `FalconMaxService` như multicall; impression AdMob `adFormatRaw` = `Interstitial` / `Rewarded`
- `FalconMaxService.LogExternalMaxRevenue`: cửa chung cho impression MAX của module ngoài (Lua, DFPool) — client LTV, Bamboo, Firebase, MMP, hoãn log ad đầu tiên, TikTok
- Lua: `shown_duration_sec` = 0 ở close (iOS ~99%, Android ~24%) hết theo cùng fix `OnShown` dưới đây — mốc show trước đây chỉ ghi khi Displayed về, sát lúc đóng
- Lua lấy ad unit từ `sc_ad_bid_floor_v2`; v2 không có id nào thì lấy ĐỦ id của `sc_ad_bid_floor` (v1 thô, không lọc `algorithmIds`) — giống bản chưa có v2
- thêm `MediationManager.IsShowInterstitial` / `IsShowRewarded(placementId, param, defaultValue)`: hỏi Lua có nên show không
- `MediationManager.CanShowInterstitial` và `ShowInterstitial` tự hỏi Lua `IsShowInterstitial` (chỉ khi phiên LUA và ad sắp chiếu là ad của Lua); bị chặn → `onFail` (thiếu thì `onInterstitialClosed`), không bắn `OnShowAttempt`
- AdMob luôn init SAU khi MAX init thành công, kể cả phiên MultiCall: `AdmobMediationFalconBridge` vẫn cài hook trước MAX nhưng `AdmobMediation.Initialize` chờ `NotifyMaxInitialized()` từ callback init MAX (`FalconMaxService`) — trước đây phiên MultiCall init AdMob song song với MAX. MAX init không thành công thì AdMob không init
- thêm `GameData4Mediation` (`mediation_data`): `mediationVersion` gửi lên server khi version đổi (lúc `MediationManager.Start` và khi server ghi đè bản cũ); log debug `Mediation module version x.y.z`
- thêm `MediationVersion.VALUE` — sinh tự động từ `package.json` bởi `MediationVersionSync` (Editor), đừng sửa tay; bump version chỉ sửa `package.json`
- thêm `MediationManager.SecondsSinceLastInterstitial`; `context.params` của lượt hỏi có thêm `sinceLastInterSec`, `interShownCount`
- thêm MoonSharp 2.0.0 (`LuaDynamic/Plugins/MoonSharp.Interpreter.dll`, C# thuần, chạy IL2CPP) + `link.xml` giữ assembly khỏi bị strip
- GMA: lượt show rewarded gửi `FAdCalledLog` với `AdType.Reward` (trước đây `Interstitial`) — era: trước bản này `f_sdk_ads_called_data` của AdMob đếm rewarded vào rổ Interstitial
- GMA: show interstitial khi bật multicall không chạy tiếp `_interstitialAd.Show()` nữa (thêm `else`)
- FSN: impression native lên BigData mang `type = Native` (trước map sang Interstitial/Reward/AppOpen/Banner), `adWhere` = chỗ chiếu của lượt show trước, `adWhen = after_interstitial`/`after_rewarded`, `adFormatRaw` giữ loại FSN; cộng sổ LTV client `Native` — lần đầu `f_sdk_ads_data` có `sub_event = Native`
- FSN: `ShowRewardedThenNative` / `ShowInterstitialThenNative` nhận thêm `where`
- FSN: tiền tệ paid khác USD thì bỏ dòng BigData; placement của `FsnAdType` lạ thành `fsn_unknown` + LogError
- IronSource: format lạ không còn bị dán `AppOpen`, chỉ gửi Firebase + MMP, LogError
- FSN: native gửi `CSAdInfo` (`adType = Native`, `adPlacement` = `fsn_*`) — lần đầu server có ad_log native; Firebase `ad_impression` của FSN có thêm `ad_placement`
- FSN: watchdog 8 s (`SetFullScreenAnswerTimeout`) cho `ShowRewardedThenNative` / `ShowInterstitialThenNative` — ad toàn màn không trả callback nào và chưa hiện thì tắt overlay đen, trả fail; thêm `FsnAdManager.NotifyFullScreenAdDisplayed`, hook `AdmobMediationHooks.onAdDisplayed`

# 1.4.41
- multicall MAX thêm thuật toán 8
- thêm `DFPool` (Service/DFPool): kho AdMob dùng chung cho các slot MAX qua custom adapter `com.falcon.dfpool.DFPoolAdapter`, bật bằng remote config `moMulDfPoolConfig` (rỗng = tắt), xoá thư mục là gỡ sạch
- thêm `ExternalMaxAdUnits`: ad unit MAX do module ngoài tự lo -> FalconMaxService / MultiCallMaxProviderParent / bridge AdMob bỏ qua event của chúng
- `SecondaryAdSource`: thêm `Register` / `Unregister` cho nhiều nguồn phụ chạy song song (`Current` giữ nguyên), so giá / show lấy nguồn đắt nhất

# 1.4.40
- TikTok: code dùng TikTok SDK (`LogTikTok`, menu TikTokSettings) rào `#if TIKTOK_ENABLE`; thêm `TikTokDefine` (Editor, asmdef riêng) tự thêm/gỡ define theo thư mục `Assets/TikTokBusinessSDK` — không có SDK vẫn compile

# 1.4.39
- multicall MAX: Editor show lại được inter/reward (popup giả lập MAX) — `LoadedRevenue` coi `AdInfo.Revenue` âm của stub Editor (`-1`, không có `revenue`) là `0` cho `eCpm` và `valueDefault*`, nếu không so giá `eCpm < 0` hiểu là không có ad; build thật giữ nguyên

# 1.4.38
- gỡ code test: rewarded load/show lại như cũ (bỏ chuyển rewarded sang inter)
- multicall: `tier` của dòng load lấy bậc chụp lúc gọi LoadAd (`AdTypeMultiCall.requestTier`) — dòng fail hết lệch một nhịp; bậc <= 0 gửi trống thay vì `tier = 0` (cả bridge AdMob)

# 1.4.37
- bổ sung param cho BigData 1.5.5

# 1.4.36
- FSN: đọc `moMulFsnAdsConfigsVer2` / `moMulEnableCampaignFilter` / `moMulListCampaignIdsConfigs` từ CMS trước (`FsnAdsConfigCms`), từng key CMS không khai mới lấy data4game; chờ config chốt nguồn (thay timeout 7s chỉ chờ data4game) rồi mới chuẩn bị ad; nghe `OnUpdateFromNet` của cả CMS
- `AdsConfigGate`: thêm `CmsHasKey`, `IsCmsFirstReady` dùng chung cho AdMob và FSN; đổi `ADMOB_TIMEOUT_SECONDS` -> `CMS_FIRST_TIMEOUT_SECONDS`

# 1.4.35
- thêm Interstitial eCPM Floor Personalization (Databuckets Rule), cấu hình ở `SOFloorPersonalizationSetting` (Resources):
  - gọi First Ad Rule lúc MAX init xong, Subsequent Ad Rule mỗi khi một interstitial lên hình
  - thực thi workflow floor (`jC7Fp`, retry_times, transitions success/fail) trên các ad unit riêng của workflow
  - quản lý Ready Candidate Pool, khi show chọn candidate có estimated eCPM cao nhất
  - flow rỗng (Control / base_line) hoặc API lỗi -> chạy logic load mặc định như cũ
  - step chờ ad unit rảnh (ad đang chiếu đóng) thay vì bỏ step -> 3 ad unit là đủ
- MultiCall / unit đơn đứng chờ khi personalization đang chờ Rule hoặc đang chạy workflow; thêm `IMultiCallProvider.ResumeInterstitialLoading` để nạp lại khi nhả chặn
- thêm `AdSessionCounter`: số ad đã chiếu trong session, tách interstitial / rewarded (nguồn của `ad_index_session`)
- meta AdMob: đọc `moMulAdmobMetaConfig` từ CMS trước (`AdmobMetaAdsConfigCms`), CMS không khai key mới lấy data4game; nghe `OnUpdateFromNet` của cả CMS
- `AdsConfigGate`: AdMob chờ CMS trả lời rồi mới init (CMS có key thì init ngay, không chờ data4game); thêm `IsAdmobConfigReady`, `WaitUntilAdmobConfigReady`
- rào `Debug.Log` / `LogWarning` / `LogError` bằng `#if ENABLE_DEBUG_LOG` (giữ `Debug.LogException` cho Crashlytics)

# 1.4.34
- tối ưu algo7
- fsn v11

# 1.4.33
- thêm decisionPolicy cho UnityLevelPlay + Admob

# 1.4.32
- bộ event ad v2: đổi `floor` -> `tier` (bậc trong config), `floor` giờ chỉ dành cho mediation biết giá sàn thật (AdMob)
- truyền `adMediation` ở mọi mốc OnLoadResult (Max / MultiCall Max / Admob)
- AdMob gửi kèm `tier` cạnh `floor`
- thêm mốc `OnShowAttempt` ở 3 cửa show (interstitial, rewarded, app open) để đo view fill rate
- pin bigdata 1.4.0

# 1.4.31
- thêm log cho meta admob

# 1.4.30
- thêm log cho multicall (yêu cầu của databucket)

# 1.4.29
- sửa bug logic multicall

# 1.4.28
- sửa ad_where rỗng/Unknown: bỏ reset placement ở mốc request, truyền where tường minh vào Ad.OnClosed

# 1.4.27
- sửa logic where = null
- thêm logic update uplift cho admob

# 1.4.26
- sửa logic multicall

# 1.4.25
- sửa logic where = null
- thêm Tiktok SDK

# 1.4.24
- thêm meta admob

# 1.4.23
- sửa logic bị where = null

# 1.4.21, 1.4.22
- thêm logic anti fraude user (MO)

# 1.4.20
- thêm algorithm 7 cho multicall

# 1.4.19
- sửa luồng CSAdRequestLog estimate ecpm = 0

# 1.4.18
- sửa CSAdFinish, CSAdInfo, CSAdStart, CSAdRequestLog vào queue 

# 1.4.17
- thêm algorithm 6 cho multicall

# 1.4.16
- thêm template cho FSN
- thêm callback onFail khi gọi hàm showInter, showReward sớm.

# 1.4.15
- fix lỗi FSN policy

# 1.4.14
- fix lỗi build android
- cập nhật lại luồng cho FSN ios

# 1.4.13
- fix lỗi khi build Ios

# 1.4.12
- thêm init FSN trong manual

# 1.4.11
- cập nhật lại luồng manual init, có thể gọi bất cứ lúc nào

# 1.4.10
- sửa lại luồng cho collapsible fsn

# 1.4.8, 1.4.9
- fix lỗi fsn IOS

# 1.4.7
- lưu cache low end device

# 1.4.6
- lưu cache campaignId cho appsflyer

# 1.4.5
- làm lại luồng cho fsn
- hỗ trợ filter theo campaignId lấy từ appsflyer
- sử dụng remote config từ d4g cho fsn
- thêm các template cho fsn

# 1.4.4
- fix lỗi ko hiển thị được interstitial trên editor

# 1.4.3
- thêm param level cho BannerLogService

# 1.4.2
- fix lỗi load interstitial fsn

# 1.4.1
- thêm dependencies cho fsn
- fix lỗi build cho fsn
- chuyển từ remote config từ data4game sang cms

# 1.4.0
- thêm fsn

# 1.3.38
- thêm logic load ads

# 1.3.37
- thêm check null fix lỗi show inter

# 1.3.36
- thêm logic load ads mới (4 và 5)

# 1.3.35
- thêm hàm get banner height

# 1.3.34
- bỏ multicall trong setting local

# 1.3.33
- thêm logic load ads mới

# 1.3.32
- thêm event bus lắng nghe remove ads

# 1.3.31
- fix lỗi scripting define

# 1.3.30
- update remote config mới cms

# 1.3.28, 1.3.29
- fix log bamboo/taichi

# 1.3.25, 1.3.26, 1.3.27
- fix log bamboo 3 cho iron source

# 1.3.24
- update logic bamboo 3 dùng thêm cho appopen, rewarded, interstitial

# 1.3.23
- update logic bamboo 3

# 1.3.22
- thêm Dontdestroy cho LogInstance

# 1.3.21
- fix bug check null ad request

# 1.3.20
- update logic bamboo 2

# 1.3.19
- update logic taichi/bamboo
- fix minor bug

# 1.3.18
- fix log placement banner = null

# 1.3.17
- fix log placement id = null

# 1.3.16
- fix log placement id = null

# 1.3.15
- thêm chức năng manual Init
- hỗ trợ app open monetize

# 1.3.14
- fix lỗi script define

# 1.3.13
- thêm action bannerLoaded
- thêm hàm check IsBannerReady

# 1.3.12
- fix lỗi khi build Ios

# 1.3.10, 1.3.11
- fix lỗi khi build exe

# 1.3.9
- cập nhật log ltv

# 1.3.8
- cập nhật log ltv v2

# 1.3.7
- fix lỗi log ltv

# 1.3.6
- cập nhật logic taichi/bamboo

# 1.3.5
- thêm param level khi log mmp

# 1.3.4
- bỏ gma khỏi setting

# 1.3.3
- bỏ script define gma

# 1.3.2
- thêm config cho giới hạn level thấp nhất có thể show AOA

# 1.3.1
- cập nhật document.

# 1.3.0
- Hỗ trợ multi call cho ironsource, song song với Max
- tối ưu giảm crash/anr cho máy yếu
- thêm log cho bamboo/taichi (phục vụ cho segment user AI)
- thêm log CSAdInfo

# 1.2.41
- fix lỗi banner tự show sau khi hide

# 1.2.40
- fix lỗi log ads display với multicall

# 1.2.39
- thêm button close trên banner

# 1.2.38
- thêm hàm log display và displayFailed

# 1.2.37
- fix lỗi khi log ads lên bigdata

# 1.2.36
- fix lỗi khi log ads lên bigdata

# 1.2.35
- fix lỗi khi log ads lên bigdata

# 1.2.34
- Thêm Cooldown time cho AppOpenAds
- Thêm hàm InogreNextTimeAoa
- sửa log lên GameData4AdInfo

# 1.2.33
- sửa lỗi class CSAdStart và CSAdFinish

# 1.2.32
- fix lỗi delay multi call.

# 1.2.31
- fix lỗi null khi gọi quá sớm.

# 1.2.30
- thêm log ads
- update ironsource (levelplay) lên 9.0.0

# 1.2.29
- fix symbol script

# 1.2.28
- thêm callback banner show/hide
- fix lỗi show inter thay vì show rewarded

# 1.2.27
- fix lỗi symbol script

# 1.2.26
- fix lỗi thời gian finish ads
- thêm FActionLog khi start và finish ads

# 1.2.25
- fix lỗi thừa param gma service

# 1.2.24
- thêm log ltv

# 1.2.23
- fix lỗi revenue = -1 của max

# 1.2.22
- thêm scripting define hỗ trợ window (build map editor)
- thêm hàm log khi bắt đầu và sau khi xem xong ads

# 1.2.21
- tách module add-on mediation MO từ module chính mediation

# 1.2.20
- fix lỗi build exe

# 1.2.19
- chỉnh lại thứ tự param khi gọi show quảng cáo, ưu tiên placementId phải khác rỗng, để so sánh với trên remote config
- thêm param placementId cho banner

# 1.2.18
- chỉnh sửa hàm lấy trạng thái của quảng cáo tại từng vị trí

# 1.2.17
- thêm hàm lấy trạng thái của quảng cáo tại từng vị trí

# 1.2.15
- thêm param thứ 4, là thời gian delay của từng id quảng cáo

# 1.2.12
- fix minor bug

# 1.2.11
- Cập nhật logic tối ưu load quảng cáo mới
