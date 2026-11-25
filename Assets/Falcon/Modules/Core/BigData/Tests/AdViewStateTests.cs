using Falcon.Helpers.Devkit;
using NUnit.Framework;

// ReSharper disable once CheckNamespace
namespace Falcon.Modules.Core.BigData.Tests
{
    public class AdViewStateTests
    {
        private const long T0 = 1_000_000L;
        private static long Sec(int s) => T0 + s * 1000L;
        private static AdViewParam Request(AdType type) => new() { type = type };

        [Test]
        public void Open_GeneratesAdViewId()
        {
            // Id là thứ SDK sinh nên nó ĐI RA theo giá trị trả về, không ghi ngược vào param của
            // caller — param chỉ chứa cái game khai
            Assert.IsNotNull(new AdViewState().Open(Request(AdType.Interstitial), T0));
        }

        // ---- Án "ad_close mồ côi" (loader đo trên coin.sort 13/10) ----
        //
        // Mediation PRELOAD ad kế tiếp ngay sau khi ad hiện tại lên hình, nên lúc user bấm đóng thì
        // slot của format đã là view MỚI. Trước bản sửa 16/10, close bị đóng dấu id của một request
        // chưa bao giờ hiện — 46% với reward, 14% với interstitial; khoảng cách p50 đúng bằng thời
        // gian xem (52s / 33s). Impression KHÔNG thừa (lệch với AppLovin S2S chỉ −0,6% / −1,6%),
        // nên đây thuần là lỗi đóng dấu id.

        [Test]
        public void ClosePreloadRace_KeepsIdOfTheAdOnScreen()
        {
            var state = new AdViewState();
            var shownId = state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.StampImpression(AdType.Reward, Sec(1));

            // mediation preload ad kế tiếp TRONG LÚC user đang xem
            var preloadedId = state.Open(Request(AdType.Reward), Sec(3));

            var closed = state.MarkClosed(new AdCloseParam { type = AdType.Reward }, Sec(52));

            Assert.AreEqual(shownId, closed.adViewId,
                "Close phải giữ id của view ĐANG CHIẾU, không phải id ad vừa preload");
            Assert.AreNotEqual(preloadedId, closed.adViewId);
            Assert.AreEqual(51, closed.shownDurationSec, "Thời lượng tính từ mốc show của view đó");
        }

        [Test]
        public void ClickDuringPreload_BelongsToTheAdOnScreen()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(1));
            state.StampImpression(AdType.Interstitial, Sec(1));
            state.Open(Request(AdType.Interstitial), Sec(2));   // preload chen vào

            state.MarkClicked(AdType.Interstitial);
            var param = new AdCloseParam { type = AdType.Interstitial };
            state.MarkClosed(param, Sec(33));

            Assert.IsTrue(param.hasClick, "Cú click thuộc về ad đang trên màn hình");
        }

        [Test]
        public void CurrentViewId_WhileAdOnScreen_IsTheAdOnScreen()
        {
            // Game đọc id này lúc phát thưởng rewarded (Resource.adViewId) — phát thưởng xảy ra
            // ngay sau close, tức sau khi mediation đã preload. Trả id preload là nối doanh thu
            // vào một lần xem chưa từng xảy ra.
            var state = new AdViewState();
            var shownId = state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.Open(Request(AdType.Reward), Sec(3));

            Assert.AreEqual(shownId, state.CurrentId(AdType.Reward));
        }

        [Test]
        public void ImpressionDuringPreload_StampsTheAdOnScreen()
        {
            var state = new AdViewState();
            var shownId = state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.Open(Request(AdType.Reward), Sec(2));   // preload chen giữa show và impression

            var stamped = state.StampImpression(AdType.Reward, Sec(3));

            Assert.AreEqual(shownId, stamped.adViewId);
            Assert.IsFalse(stamped.rotated, "Không phải ca thiếu OnRequested — đừng xoay id");
        }

        [Test]
        public void AfterClose_NextShowUsesThePreloadedView()
        {
            // Đóng xong thì lần chiếu khép lại: ad vừa preload mới là view của lần sau
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(1));
            state.StampImpression(AdType.Interstitial, Sec(1));
            var preloadedId = state.Open(Request(AdType.Interstitial), Sec(2));
            state.MarkClosed(new AdCloseParam { type = AdType.Interstitial }, Sec(33));

            var shown = state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(60));

            Assert.AreEqual(preloadedId, shown.adViewId);
            Assert.IsFalse(shown.rotated, "View preload chưa có impression — không có gì để xoay");
        }

        [Test]
        public void MissingClose_SelfHealsOnNextShow()
        {
            // Mediation quên bắn close (hoặc app bị kill giữa chừng): sổ "đang chiếu" không được
            // phép kẹt vĩnh viễn
            var state = new AdViewState();
            state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.StampImpression(AdType.Reward, Sec(1));
            var nextId = state.Open(Request(AdType.Reward), Sec(5));

            var shown = state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(90));

            Assert.AreEqual(nextId, shown.adViewId, "Lần chiếu mới dùng view mới, không kẹt ở view cũ");
            Assert.AreEqual(nextId,
                state.MarkClosed(new AdCloseParam { type = AdType.Reward }, Sec(120)).adViewId);
        }

        [Test]
        public void BannerKeepsInstanceSemantics_NotAffectedByTheFix()
        {
            // Banner: id là id INSTANCE theo hợp đồng, và không có mốc close — request mới là
            // instance mới, các impression sau thuộc về nó
            var state = new AdViewState();
            state.Open(Request(AdType.Banner), T0);
            state.MarkShown(new AdShowParam { type = AdType.Banner }, Sec(1));
            state.StampImpression(AdType.Banner, Sec(1));
            var newInstance = state.Open(Request(AdType.Banner), Sec(30));

            Assert.AreEqual(newInstance, state.CurrentId(AdType.Banner));
        }

        [Test]
        public void WholeLifecycle_SharesOneId()
        {
            // request → show → impression → close: mọi mốc đọc ra cùng một id
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Reward), T0);

            Assert.AreEqual(id, state.CurrentId(AdType.Reward), "impression");
            Assert.AreEqual(id, state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(2)).adViewId, "show");
            Assert.AreEqual(id, state.MarkClosed(new AdCloseParam { type = AdType.Reward }, Sec(30)).adViewId,
                "close");
        }

        [Test]
        public void BannerRefreshImpressions_ShareIdOfTheirRequest()
        {
            // Banner tự refresh trong SDK: N impression thuộc CÙNG một banner instance
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Banner), T0);

            Assert.AreEqual(id, state.CurrentId(AdType.Banner));
            Assert.AreEqual(id, state.CurrentId(AdType.Banner));
        }

        [Test]
        public void Impression_WithoutAnyRequest_GetsNothing()
        {
            Assert.IsNull(new AdViewState().CurrentId(AdType.Interstitial));
        }

        [Test]
        public void FormatsAreIndependent()
        {
            // Các format chạy song song: preload inter + preload rewarded cùng lúc
            var state = new AdViewState();
            var inter = state.Open(Request(AdType.Interstitial), T0);
            var reward = state.Open(Request(AdType.Reward), T0);

            Assert.AreEqual(reward, state.CurrentId(AdType.Reward));
            Assert.AreEqual(inter, state.CurrentId(AdType.Interstitial),
                "Impression của format này không được nuốt id của format kia");
        }

        [Test]
        public void RetryRequest_ReplacesPending_PreviousIsFillFail()
        {
            // Load fail → retry: request cũ không bao giờ lên hình = fill-fail (server thấy
            // request không có impression mang id đó), request mới chiếm slot.
            var state = new AdViewState();
            var first = state.Open(Request(AdType.Interstitial), T0);
            var retry = state.Open(Request(AdType.Interstitial), Sec(2));

            Assert.AreNotEqual(first, retry);
            Assert.AreEqual(retry, state.CurrentId(AdType.Interstitial));
        }

        [Test]
        public void Show_CarriesIdAndMeasuresWaitFromRequest()
        {
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Interstitial), T0);

            var shown = state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(3));

            Assert.AreEqual(id, shown.adViewId);
            Assert.AreEqual(3000, shown.requestToShowMs);
        }

        [Test]
        public void Show_WithoutRequest_LeavesIdAndWaitEmpty()
        {
            // Đường load nằm ngoài SDK → không bịa số
            var shown = new AdViewState().MarkShown(new AdShowParam { type = AdType.Interstitial }, T0);

            Assert.IsNull(shown.adViewId);
            Assert.IsNull(shown.requestToShowMs);
        }

        [Test]
        public void Close_CarriesIdAndMeasuresShownDuration()
        {
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(2));

            var closed = state.MarkClosed(new AdCloseParam { type = AdType.Reward, adCompleted = true }, Sec(32));

            Assert.AreEqual(id, closed.adViewId);
            Assert.AreEqual(30, closed.shownDurationSec);
        }

        [Test]
        public void Close_WithoutShow_LeavesDurationEmpty()
        {
            // Request rồi đóng mà chưa từng hiển thị (huỷ preload) → không có thời lượng để đo
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);

            var closed = state.MarkClosed(new AdCloseParam { type = AdType.Interstitial }, Sec(5));

            Assert.IsNotNull(closed.adViewId);
            Assert.IsNull(closed.shownDurationSec);
        }

        [Test]
        public void MillisSinceRequest_MeasuresFillLatency()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Reward), T0);

            Assert.AreEqual(4000, state.MillisSinceRequest(AdType.Reward, Sec(4)));
        }

        [Test]
        public void MillisSinceRequest_WithoutRequest_IsNull()
        {
            // Impression không đi từ request nào của SDK → không bịa 0
            Assert.IsNull(new AdViewState().MillisSinceRequest(AdType.Interstitial, T0));
        }

        [Test]
        public void Placement_SetAfterRequest_FlowsToShowAndClose()
        {
            // Preload lúc chưa biết chiếu ở đâu; biết lúc nào ghi lúc đó
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);

            Assert.IsTrue(state.SetContext(AdType.Interstitial, adWhere: "level_end"));

            var show = new AdShowParam { type = AdType.Interstitial };
            state.MarkShown(show, Sec(2));
            var close = new AdCloseParam { type = AdType.Interstitial };
            state.MarkClosed(close, Sec(20));

            Assert.AreEqual("level_end", show.adWhere);
            Assert.AreEqual("level_end", close.adWhere);
            Assert.AreEqual("level_end", state.CurrentPlacement(AdType.Interstitial));
        }

        [Test]
        public void PlacementOnShow_WinsAndUpdatesTheSlot()
        {
            // Mốc nào tự nhập thì giá trị đó thắng — và các mốc sau theo luôn
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.SetContext(AdType.Interstitial, adWhere: "level_end");

            var show = new AdShowParam { type = AdType.Interstitial, adWhere = "shop" };
            state.MarkShown(show, Sec(2));
            var close = new AdCloseParam { type = AdType.Interstitial };
            state.MarkClosed(close, Sec(9));

            Assert.AreEqual("shop", show.adWhere);
            Assert.AreEqual("shop", close.adWhere);
        }

        [Test]
        public void Placement_WithoutRequest_IsRejected()
        {
            var state = new AdViewState();

            Assert.IsFalse(state.SetContext(AdType.Reward, adWhere: "level_end"));
            Assert.IsNull(state.CurrentPlacement(AdType.Reward));
        }

        [Test]
        public void Placement_DoesNotSurviveIntoTheNextRequest()
        {
            // Request mới là lần xem ad khác — chỗ chiếu cũ không được dính sang
            var state = new AdViewState();
            state.Open(Request(AdType.Reward), T0);
            state.SetContext(AdType.Reward, adWhere: "double_coin");

            state.Open(Request(AdType.Reward), Sec(60));

            Assert.IsNull(state.CurrentPlacement(AdType.Reward));
        }

        [Test]
        public void Mediation_CachedFromRequest_FlowsToLaterMarks()
        {
            // adMediation bất biến trong cả vòng đời một view — nhập một lần lúc request là đủ
            var state = new AdViewState();
            var request = new AdViewParam { type = AdType.Reward, adMediation = "IronSource" };
            state.Open(request, T0);

            var show = new AdShowParam { type = AdType.Reward };
            state.MarkShown(show, Sec(2));
            var close = new AdCloseParam { type = AdType.Reward };
            state.MarkClosed(close, Sec(30));

            Assert.AreEqual("IronSource", show.adMediation);
            Assert.AreEqual("IronSource", close.adMediation);
        }

        [Test]
        public void Context_CachedFromRequest_FlowsToLaterMarks()
        {
            var state = new AdViewState();
            var request = new AdViewParam { type = AdType.Interstitial, adWhen = "level_fail" };
            state.Open(request, T0);

            var show = new AdShowParam { type = AdType.Interstitial };
            state.MarkShown(show, Sec(1));

            Assert.AreEqual("level_fail", show.adWhen);
            Assert.AreEqual("level_fail", state.CurrentContext(AdType.Interstitial));
        }

        [Test]
        public void Snapshot_ReportsWholeContextAndTimings()
        {
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Reward, adMediation = "Max" }, T0);
            state.SetContext(AdType.Reward, adWhere: "shop", adWhen: "out_of_life");
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(3));

            var snapshot = state.TakeSnapshot(AdType.Reward, Sec(10));

            Assert.AreEqual(state.CurrentId(AdType.Reward), snapshot.adViewId);
            Assert.AreEqual("shop", snapshot.adWhere);
            Assert.AreEqual("out_of_life", snapshot.adWhen);
            Assert.AreEqual("Max", snapshot.adMediation);
            Assert.AreEqual(10000, snapshot.sinceRequestMs);
            Assert.AreEqual(7000, snapshot.sinceShownMs);
        }

        [Test]
        public void Snapshot_BeforeShow_HasNoShownDuration()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Banner), T0);

            Assert.IsNull(state.TakeSnapshot(AdType.Banner, Sec(5)).sinceShownMs);
        }

        [Test]
        public void Snapshot_WithoutRequest_IsNull()
        {
            Assert.IsNull(new AdViewState().TakeSnapshot(AdType.AppOpen, T0));
        }

        [Test]
        public void RequestParam_OmitsNullOptionalFields()
        {
            var state = new AdViewState();
            var param = Request(AdType.AppOpen);
            state.Open(param, T0);

            var dict = param.ToDictionary();
            Assert.AreEqual(AdType.AppOpen, dict["type"]);
            Assert.IsFalse(dict.ContainsKey("adViewId"), "adViewId là của SDK nên nằm trên log, không nằm ở param");
            Assert.IsFalse(dict.ContainsKey("adWhere"), "null thì vắng mặt khỏi payload (§H4)");
            Assert.IsFalse(dict.ContainsKey("adMediation"));
        }

        [Test]
        public void UnknownContext_CountsAsBlank_DoesNotWipeTheCache()
        {
            // AdParam.CorrectValues() điền sẵn "Unknown" cho log impression trước khi decor chạy;
            // coi đó là giá trị thật thì cache mất luôn chỗ chiếu game đã báo lúc request
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Banner, adWhere = "main_menu" }, T0);

            var impression = new AdParam { type = AdType.Banner, adWhere = FParam.UNKNOWN, adWhen = FParam.UNKNOWN };
            state.ApplyContext(impression, AdType.Banner);

            Assert.AreEqual("main_menu", impression.adWhere);
            Assert.AreEqual("main_menu", state.CurrentPlacement(AdType.Banner));
        }

        // ── OnClicked: mốc thống nhất, ruột là cờ đóng lên close ────────────────

        [Test]
        public void Clicked_StampsHasClickOnClose()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);

            Assert.IsTrue(state.MarkClicked(AdType.Interstitial));
            var close = new AdCloseParam { type = AdType.Interstitial };
            state.MarkClosed(close, Sec(30));

            Assert.IsTrue(close.hasClick, "Cờ click phải tự chảy vào hasClick của log close");
        }

        [Test]
        public void ExplicitHasClick_BeatsTheClickedFlag()
        {
            // Mediation tự truyền hasClick (kể cả false) thì giá trị tường minh thắng cờ
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkClicked(AdType.Interstitial);

            var close = new AdCloseParam { type = AdType.Interstitial, hasClick = false };
            state.MarkClosed(close, Sec(30));

            Assert.IsFalse(close.hasClick);
        }

        [Test]
        public void NoClickSeen_SendsHasClickFalse_Explicitly()
        {
            // Sửa 23/09 (đo của loader: 0 dòng nào mang false): cờ click là số đo thật của SDK nên
            // chưa thấy click phải gửi false TƯỜNG MINH — để trống thì bên đọc số không phân biệt
            // được "không bấm" với "không biết", và phải tự COALESCE.
            var state = new AdViewState();
            state.Open(Request(AdType.Reward), T0);

            var close = new AdCloseParam { type = AdType.Reward };
            state.MarkClosed(close, Sec(30));

            Assert.IsFalse(close.hasClick);
        }

        [Test]
        public void ClickedFlag_DoesNotLeakIntoTheNextView()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkClicked(AdType.Interstitial);
            state.MarkClosed(new AdCloseParam { type = AdType.Interstitial }, Sec(30));

            state.Open(Request(AdType.Interstitial), Sec(60)); // request mới = view mới
            var close = new AdCloseParam { type = AdType.Interstitial };
            state.MarkClosed(close, Sec(90));

            Assert.IsFalse(close.hasClick, "Cờ click của view trước không được dây sang view sau (false, không phải true)");
        }

        [Test]
        public void Click_WithoutAnyView_IsRejected()
        {
            Assert.IsFalse(new AdViewState().MarkClicked(AdType.Interstitial));
        }

        // ---- Luật xoay id khi view đã tiêu thụ (án dup 5% của loader, 03/09) ----

        [Test]
        public void FirstImpression_KeepsRequestId_AndMeasuresFill()
        {
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Interstitial), T0);

            var stamp = state.StampImpression(AdType.Interstitial, Sec(3));

            Assert.AreEqual(id, stamp.adViewId, "Impression đầu phải giữ id của request — phễu fill nguyên vẹn");
            Assert.AreEqual(3000, stamp.fillLatencyMs);
            Assert.IsFalse(stamp.rotated);
        }

        [Test]
        public void SecondImpression_SameView_RotatesToFreshId()
        {
            // Mediation quên OnRequested đợt mới: hai impression đổ vào cùng view — SDK tự xoay
            // (khuôn play_turn_id) thay vì để một id bị tính tiền hai lần
            var state = new AdViewState();
            var id1 = state.Open(Request(AdType.Interstitial), T0);
            state.StampImpression(AdType.Interstitial, Sec(3));

            var second = state.StampImpression(AdType.Interstitial, Sec(120));

            Assert.IsNotNull(second.adViewId, "Vẫn đủ tham số — id sinh mới chứ không vắng mặt");
            Assert.AreNotEqual(id1, second.adViewId);
            Assert.IsTrue(second.rotated, "Caller cần biết để cảnh báo thiếu OnRequested");
            Assert.IsNull(second.fillLatencyMs, "View tự sinh không đi từ request nào — không bịa số đo");
        }

        [Test]
        public void ShowOnConsumedView_RotatesForTheWholeNextDisplay()
        {
            // Có gọi OnShown: xoay ngay tại show để cả bộ show/impression/close của lần chiếu mới
            // đi chung MỘT id mới — không nửa nạc show-id-cũ imp-id-mới
            var state = new AdViewState();
            var id1 = state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(2));
            state.StampImpression(AdType.Reward, Sec(2));
            state.MarkClosed(new AdCloseParam { type = AdType.Reward }, Sec(30));

            var shown2 = state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(60));
            var imp2 = state.StampImpression(AdType.Reward, Sec(60));
            var close2 = state.MarkClosed(new AdCloseParam { type = AdType.Reward }, Sec(90));

            Assert.IsTrue(shown2.rotated);
            Assert.AreNotEqual(id1, shown2.adViewId);
            Assert.IsNull(shown2.requestToShowMs, "Không đi từ request nào — null, không bịa");
            Assert.AreEqual(shown2.adViewId, imp2.adViewId, "Impression của lần chiếu mới đi id mới");
            Assert.IsFalse(imp2.rotated, "Đã xoay ở show rồi — impression đầu của view mới không xoay nữa");
            Assert.AreEqual(shown2.adViewId, close2.adViewId, "Close cũng theo id mới");
        }

        [Test]
        public void CloseAfterImpression_NeverRotates()
        {
            // Vòng đời chuẩn request → show → imp → close: close chạm view đã tiêu thụ là HỢP LỆ
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Interstitial), T0);
            state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(2));
            state.StampImpression(AdType.Interstitial, Sec(2));

            Assert.AreEqual(id, state.MarkClosed(new AdCloseParam { type = AdType.Interstitial }, Sec(30)).adViewId);
        }

        [Test]
        public void Banner_NeverRotates_InstanceIdByContract()
        {
            // Id banner là id INSTANCE — N impression chung id là hợp đồng (bản tin cụm mang
            // impressionCount), xoay là phá ngữ nghĩa cụm
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Banner), T0);

            var first = state.StampImpression(AdType.Banner, Sec(30));
            var second = state.StampImpression(AdType.Banner, Sec(60));

            Assert.AreEqual(id, first.adViewId);
            Assert.AreEqual(id, second.adViewId);
            Assert.IsFalse(second.rotated);
        }

        // ---- Hai HỌ format (rà 19/10) ----
        //
        // MREC (Max phát) và CollapsibleBanner (GMA phát) ĐANG chạy thật mà cư xử y như banner: ở
        // lại trên màn, tự refresh, không có mốc close. Trước 19/10 code kiểm "có phải banner"
        // bằng so sánh bằng `type == AdType.Banner` nên chúng rơi vào họ fullscreen.

        [TestCase(AdType.Banner)]
        [TestCase(AdType.MREC)]
        [TestCase(AdType.CollapsibleBanner)]
        [TestCase(AdType.Native)]
        public void StayOnScreenFormats_NeverRotate_AndNeedNoClose(AdType type)
        {
            var state = new AdViewState();
            var id = state.Open(Request(type), T0);
            state.MarkShown(new AdShowParam { type = type }, Sec(1));

            var first = state.StampImpression(type, Sec(1));
            // Banner tự refresh; MREC/collapsible/native thì tuỳ network — luật phải đúng cho CẢ
            // HAI ca, nên ở đây cứ bắn impression thứ hai và đòi nó KHÔNG sinh view mới
            var refreshed = state.StampImpression(type, Sec(31));

            Assert.IsTrue(AdViewState.StaysOnScreen(type));
            Assert.AreEqual(id, first.adViewId);
            Assert.AreEqual(id, refreshed.adViewId, "Refresh KHÔNG sinh view mới — id là id instance");
            Assert.IsFalse(refreshed.rotated);

            // Không vào sổ "đang chiếu": ad preload sau đó là view hiện hành ngay, không ai phải
            // chờ một mốc close không bao giờ đến
            var preloaded = state.Open(Request(type), Sec(60));
            Assert.AreEqual(preloaded, state.CurrentId(type));
        }

        // ---- Native: nhiều ô một màn nên KHÔNG phát id (chốt owner 19/10) ----

        [Test]
        public void Native_NeverGetsAViewId_BecauseSdkCannotKnowWhichSlot()
        {
            var state = new AdViewState();

            Assert.IsTrue(AdViewState.CanHaveManyOnScreen(AdType.Native));
            Assert.IsNull(state.Open(Request(AdType.Native), T0),
                "Dòng request cũng không mang id: mang id mà không bao giờ có impression khớp thì " +
                "mọi native thành fill-fail giả");
            Assert.IsNull(state.CurrentId(AdType.Native));

            var impression = state.StampImpression(AdType.Native, Sec(1));
            Assert.IsNull(impression.adViewId, "Vắng mặt = không biết ô nào, đừng dán id ô mới nhất");
            Assert.IsNull(impression.fillLatencyMs, "Không biết thuộc ô nào thì cũng không biết xin từ lúc nào");
            Assert.IsFalse(impression.rotated);
        }

        [Test]
        public void NativeClick_IsNotAnIntegrationError()
        {
            // Không có sổ để ghi cờ, nhưng cũng không được bắt caller bắn warning mỗi cú bấm
            Assert.IsTrue(new AdViewState().MarkClicked(AdType.Native));
        }

        [TestCase(AdType.Banner)]
        [TestCase(AdType.MREC)]
        [TestCase(AdType.CollapsibleBanner)]
        [TestCase(AdType.Interstitial)]
        [TestCase(AdType.Reward)]
        public void OtherFormats_StillGetIds(AdType type)
        {
            Assert.IsFalse(AdViewState.CanHaveManyOnScreen(type));
            Assert.IsNotNull(new AdViewState().Open(Request(type), T0));
        }

        // ---- "Vừa xem loại ad nào trước đó" (yêu cầu owner 19/10) ----

        [Test]
        public void LastFullScreenAd_RemembersFormatAndHowLongAgo()
        {
            var state = new AdViewState();
            Assert.IsNull(state.LastFullScreenAd(T0).format, "Chưa có ad nào đóng → vắng mặt");
            Assert.IsNull(state.LastFullScreenAd(T0).secAgo);

            state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.StampImpression(AdType.Reward, Sec(1));
            state.MarkClosed(new AdCloseParam { type = AdType.Reward }, Sec(31));

            var (format, secAgo) = state.LastFullScreenAd(Sec(34));

            Assert.AreEqual(AdType.Reward, format, "Native hiện sau cái này");
            Assert.AreEqual(3, secAgo, "Đếm từ mốc ĐÓNG, không phải mốc show");
        }

        [Test]
        public void StayOnScreenFormats_DoNotCountAsThePreviousFullScreenAd()
        {
            // Banner/MREC/native không "đóng" theo nghĩa người chơi xem xong — chúng không được
            // trở thành mốc tham chiếu, không thì native nào cũng báo "vừa xem banner"
            var state = new AdViewState();
            state.Open(Request(AdType.MREC), T0);
            state.MarkShown(new AdShowParam { type = AdType.MREC }, Sec(1));
            state.MarkClosed(new AdCloseParam { type = AdType.MREC }, Sec(5));

            Assert.IsNull(state.LastFullScreenAd(Sec(6)).format);
            Assert.IsNull(state.LastFullScreenAd(Sec(6)).secAgo);
        }

        [Test]
        public void LastFullScreenAd_KeepsTheMostRecentOne()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkClosed(new AdCloseParam { type = AdType.Interstitial }, Sec(10));
            state.Open(Request(AdType.AppOpen), Sec(20));
            state.MarkClosed(new AdCloseParam { type = AdType.AppOpen }, Sec(25));

            Assert.AreEqual(AdType.AppOpen, state.LastFullScreenAd(Sec(26)).format);
        }

        [TestCase(AdType.Interstitial)]
        [TestCase(AdType.Reward)]
        [TestCase(AdType.AppOpen)]
        public void FullscreenFormats_RotateOnReuse_AndCloseKeepsTheAdOnScreen(AdType type)
        {
            var state = new AdViewState();
            var shownId = state.Open(Request(type), T0);
            state.MarkShown(new AdShowParam { type = type }, Sec(1));
            state.StampImpression(type, Sec(1));

            Assert.IsFalse(AdViewState.StaysOnScreen(type));

            var preloadedId = state.Open(Request(type), Sec(3));
            var closed = state.MarkClosed(new AdCloseParam { type = type }, Sec(40));

            Assert.AreEqual(shownId, closed.adViewId, "Close giữ id của view đang chiếu");
            Assert.AreNotEqual(preloadedId, closed.adViewId);

            // Ad vừa preload trở thành view hiện hành ngay khi close khép lại
            var firstOfPreloaded = state.StampImpression(type, Sec(80));
            Assert.AreEqual(preloadedId, firstOfPreloaded.adViewId);
            Assert.IsFalse(firstOfPreloaded.rotated);

            // Lần chiếu thứ hai trên CÙNG view (mediation quên request mới) phải xoay id
            var reused = state.StampImpression(type, Sec(120));
            Assert.IsTrue(reused.rotated, "Một id không được tính tiền hai lần với họ fullscreen");
            Assert.AreNotEqual(preloadedId, reused.adViewId);
        }

        [Test]
        public void RotatedView_DoesNotInheritClick()
        {
            // Cờ click của lần chiếu trước không được lây sang close của lần chiếu sau khi xoay
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.StampImpression(AdType.Interstitial, Sec(2));
            state.MarkClicked(AdType.Interstitial);

            state.StampImpression(AdType.Interstitial, Sec(60)); // xoay
            var close = new AdCloseParam { type = AdType.Interstitial };
            state.MarkClosed(close, Sec(90));

            Assert.IsFalse(close.hasClick, "View xoay bắt đầu sạch cờ click — false, không phải kế thừa true");
        }
        // ---- PendingViewId: id cho DÒNG LOAD (bản 1.5.5, chế độ tách) ----
        //
        // Ô view giữ "view xin gần nhất" và không ai xoá khi ad chiếu xong, nên hỏi CurrentId lúc
        // load có thể nhận id của ad VỪA CHIẾU. PendingViewId chỉ trả id khi view chưa tiêu thụ.

        [Test]
        public void PendingViewId_BeforeImpression_ReturnsOpenViewId()
        {
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Interstitial), T0);

            Assert.AreEqual(id, state.PendingViewId(AdType.Interstitial),
                "Đợt xin ad đang mở thì mọi dòng load của đợt phải mang id của đợt đó");
        }

        [Test]
        public void PendingViewId_AfterImpression_IsNullForFullScreen()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.StampImpression(AdType.Reward, Sec(1));

            Assert.IsNull(state.PendingViewId(AdType.Reward),
                "Ad đã chiếu rồi: dòng load sau đó mà mang id này là dán vào ad vừa chiếu");
        }

        [Test]
        public void PendingViewId_NeverRequested_IsNull()
        {
            Assert.IsNull(new AdViewState().PendingViewId(AdType.Interstitial));
        }

        [Test]
        public void PendingViewId_Banner_KeepsInstanceIdAfterImpression()
        {
            // Banner: một instance đẻ N impression và không có mốc close — các lần load/refill
            // tiếp theo vẫn thuộc instance đó, nên KHÔNG áp luật "đã tiêu thụ".
            var state = new AdViewState();
            var id = state.Open(Request(AdType.Banner), T0);
            state.StampImpression(AdType.Banner, Sec(1));

            Assert.AreEqual(id, state.PendingViewId(AdType.Banner));
        }

        [Test]
        public void PendingViewId_PreloadDuringWatch_BelongsToTheNewWave()
        {
            // Mediation preload ad kế tiếp NGAY TRONG LÚC user đang xem (án close-mồ-côi). Dòng
            // load lúc này thuộc ĐỢT MỚI đang nằm trong ô, không phải view đang chiếu.
            var state = new AdViewState();
            var watchingId = state.Open(Request(AdType.Reward), T0);
            state.MarkShown(new AdShowParam { type = AdType.Reward }, Sec(1));
            state.StampImpression(AdType.Reward, Sec(1));

            var preloadedId = state.Open(Request(AdType.Reward), Sec(3));

            Assert.AreEqual(preloadedId, state.PendingViewId(AdType.Reward),
                "Load trong lúc user đang xem là của đợt vừa mở, không phải view đang chiếu");
            Assert.AreNotEqual(watchingId, state.PendingViewId(AdType.Reward));
        }

        [Test]
        public void PendingViewId_NextWave_ReturnsNewId()
        {
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(1));
            state.StampImpression(AdType.Interstitial, Sec(1));
            state.MarkClosed(new AdCloseParam { type = AdType.Interstitial }, Sec(10));

            var secondWave = state.Open(Request(AdType.Interstitial), Sec(11));

            Assert.AreEqual(secondWave, state.PendingViewId(AdType.Interstitial),
                "Đợt sau mở van là dòng load thuộc view mới");
        }

        // ---- 1.5.5: adAlgorithm (của cả ĐỢT) + unit đã chiếu ----

        [Test]
        public void Algorithm_FollowsTheWave_LikeMediation()
        {
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Reward, adAlgorithm = "multicall_3" }, T0);

            // mốc sau bỏ trống thì lấy từ cache của view
            var show = new AdShowParam { type = AdType.Reward };
            state.MarkShown(show, Sec(1));
            state.ApplyContext(show, AdType.Reward);

            Assert.AreEqual("multicall_3", show.adAlgorithm);
        }

        [Test]
        public void Algorithm_MarkThatFillsIt_Wins_AndUpdatesTheCache()
        {
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Reward, adAlgorithm = "multicall_3" }, T0);

            var show = new AdShowParam { type = AdType.Reward, adAlgorithm = "single" };
            state.ApplyContext(show, AdType.Reward);
            Assert.AreEqual("single", show.adAlgorithm);

            var close = new AdCloseParam { type = AdType.Reward };
            state.ApplyContext(close, AdType.Reward);
            Assert.AreEqual("single", close.adAlgorithm, "Giá trị mốc tự nhập cập nhật luôn cache");
        }

        [Test]
        public void ImpressionUnit_IsRememberedForTheCloseMark()
        {
            // Close thuộc đúng view đã chiếu, nên unit ghi ở mốc impression là SỰ THẬT — khác hẳn
            // "unit của lần load cuối" (một đợt có nhiều unit load song song).
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(1));

            Assert.IsTrue(state.SetImpressionUnit(AdType.Interstitial, "unit_shown"));
            Assert.AreEqual("unit_shown", state.ImpressionUnitId(AdType.Interstitial));
        }

        [Test]
        public void ImpressionUnit_WithoutAnyView_IsIgnored()
        {
            var state = new AdViewState();

            Assert.IsFalse(state.SetImpressionUnit(AdType.Interstitial, "unit_x"));
            Assert.IsNull(state.ImpressionUnitId(AdType.Interstitial));
        }

        [Test]
        public void CurrentAlgorithm_SurvivesTheShow_SoLoadRowsKeepIt()
        {
            // Dòng LOAD đọc thuật toán từ ô (không qua Current()), nên vẫn có sau khi ad đã chiếu:
            // thuật toán là thuộc tính của tầng mediation đang chạy, không phải của một lần chiếu.
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Interstitial, adAlgorithm = "multicall_3" }, T0);
            state.MarkShown(new AdShowParam { type = AdType.Interstitial }, Sec(1));
            state.StampImpression(AdType.Interstitial, Sec(1));

            Assert.AreEqual("multicall_3", state.CurrentAlgorithm(AdType.Interstitial));
        }

        [Test]
        public void CurrentAlgorithm_WithoutAnyRequest_IsNull()
        {
            Assert.IsNull(new AdViewState().CurrentAlgorithm(AdType.Reward));
        }

        // ---- Đợt mới không được xoá thuật toán / mediation (sửa 26/10) ----

        [Test]
        public void Algorithm_DeclaredOnce_SurvivesTheNextWave()
        {
            // Mediation khai MỘT LẦN (vd lúc init), rồi van mở đợt bằng OnRequested KHÔNG mang
            // thuật toán. Trước bản sửa: đợt thứ hai mất sạch giá trị đã khai.
            var state = new AdViewState();
            state.Open(Request(AdType.Interstitial), T0);
            state.SetContext(AdType.Interstitial, adAlgorithm: "multicall_3");

            state.Open(Request(AdType.Interstitial), Sec(10));   // đợt mới, param không khai gì

            Assert.AreEqual("multicall_3", state.CurrentAlgorithm(AdType.Interstitial));
        }

        [Test]
        public void Algorithm_NewWaveThatDeclaresItsOwn_Wins()
        {
            var state = new AdViewState();
            state.Open(new AdViewParam { type = AdType.Reward, adAlgorithm = "multicall_3" }, T0);
            state.Open(new AdViewParam { type = AdType.Reward, adAlgorithm = "single" }, Sec(10));

            Assert.AreEqual("single", state.CurrentAlgorithm(AdType.Reward));
        }

        [Test]
        public void Mediation_IsCarriedToo_ButWhereAndWhenAreNot()
        {
            // Where/When là của RIÊNG một lần chiếu: mang sang đợt sau là dán ngữ cảnh đoán
            var state = new AdViewState();
            state.Open(new AdViewParam
            {
                type = AdType.Interstitial, adMediation = "Max", adWhere = "end_level", adWhen = "win"
            }, T0);

            state.Open(Request(AdType.Interstitial), Sec(10));

            var next = new AdShowParam { type = AdType.Interstitial };
            state.ApplyContext(next, AdType.Interstitial);

            Assert.AreEqual("Max", next.adMediation, "Mediation thuộc tầng hạ tầng ⇒ giữ");
            Assert.IsNull(next.adWhere, "Chỗ chiếu của đợt trước KHÔNG được mang sang");
            Assert.IsNull(next.adWhen);
        }

    }
}
