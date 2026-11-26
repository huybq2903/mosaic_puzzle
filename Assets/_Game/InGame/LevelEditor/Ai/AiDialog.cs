using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Falcon.InGame.LevelEditor.Ai;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using Newtonsoft.Json;
using SFB;
using UnityEngine;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Cửa sổ "Tạo ảnh bằng AI" (giao diện theo Studio web): tuỳ chọn, đang vẽ, chọn ảnh. Chỉ nạp ảnh làm nguồn, tách mảnh vẫn phải bấm Generate.</summary>
    public sealed class AiDialog
    {
        private enum Screen { Config, Run, Results }

        private const float PollSeconds = 1f;
        private const int MaxPixels = 2048, MaxExtra = 400;
        private const string PrefsKey = "levelEditor.ai";

        private static readonly Dictionary<string, string> Viet = new()
        {
            ["Subject"] = "Chủ thể", ["Scene"] = "Cảnh", ["Detail"] = "Độ chi tiết (số mảnh)", ["Subject size"] = "Cỡ chủ thể",
            ["Colors"] = "Tông màu", ["Background"] = "Kiểu chia nền", ["Extra instructions"] = "Yêu cầu thêm (tuỳ chọn)",
            ["Simple"] = "Đơn giản", ["Standard"] = "Vừa", ["Detailed"] = "Chi tiết", ["Fills the frame"] = "Đầy khung", ["More background"] = "Thêm nền",
            ["Any"] = "Tuỳ ý", ["Warm"] = "Ấm", ["Cool"] = "Lạnh", ["Soft"] = "Nhạt", ["Mix"] = "Mix", ["Let the AI decide"] = "AI tự chọn",
            ["Rays"] = "Tia", ["Waves"] = "Dải sóng", ["Glass shards"] = "Mảnh kính", ["Rings"] = "Vòng tròn",
            ["latest"] = "mới nhất", ["fast"] = "nhanh, rẻ", ["preview"] = "bản thử",
        };

        private static readonly Dictionary<string, string> HintVi = new()
        {
            ["each image gets a different pattern"] = "Mỗi ảnh một kiểu",
            ["all game colors"] = "Mọi màu của game",
            ["reds, oranges, yellows, pinks"] = "Đỏ, cam, vàng, hồng",
            ["blues, greens, purples"] = "Xanh dương, xanh lá, tím",
            ["light pastel tones"] = "Pastel nhạt",
        };

        private static readonly Dictionary<string, string> ModeHint = new()
        {
            ["subject"] = "Phóng to chủ thể, bỏ hậu cảnh", ["scene"] = "Giữ nguyên bố cục cả ảnh",
        };

        private sealed class Settings
        {
            public string url = "http://127.0.0.1:8765", mode = "subject", model = "";
            public int count = 2;
            public Dictionary<string, string> opts = new();
        }

        private readonly PictureEditor _editor;
        private readonly AiClient _client = new();
        private Settings _cfg = new();
        private KitModal _modal;
        private VisualElement _content;
        private Screen _screen;

        private AiStatus _status = new();
        private AiSpec _spec;
        private List<AiModel> _models;
        private Task _load;

        private Task<string> _start;
        private Task<AiJob> _poll;
        private string _jobId, _mode, _model;
        private int _runCount, _failures;
        private float _started, _nextPoll;
        private bool _cancelling, _finished;
        private AiJob _job;
        private readonly List<SlotView> _slots = new();
        private Label _log, _note;
        private Button _cancel;
        private readonly Dictionary<string, float> _doneAt = new();

        private Task<byte[]>[] _images;
        private List<AiResult> _results;
        private readonly List<(byte[] png, Texture2D tex)> _pics = new();
        private readonly List<VisualElement> _cards = new();
        private readonly List<VisualElement> _checks = new();
        private Label _prompt;
        private int _sel;

        private sealed class SlotView
        {
            public VisualElement Icon, Spin, Bar, BarSeg;
            public Label Title, Status, Badge;
            public Task<byte[]> Thumb;
            public Texture2D Tex;
        }

        public AiDialog(PictureEditor editor)
        {
            _editor = editor;
            try { _cfg = JsonConvert.DeserializeObject<Settings>(PlayerPrefs.GetString(PrefsKey, "")) ?? new Settings(); }
            catch { _cfg = new Settings(); }
            _cfg.opts ??= new Dictionary<string, string>();
            _client.Url = _cfg.url;
        }

        public bool IsOpen => _modal != null && _modal.IsOpen;

        // Nhãn chữ của cửa sổ: lớp ai-t xoá lề và đệm mặc định của Unity để mép chữ thẳng với phần tử kề
        private static Label T(VisualElement parent, string text, KitText role = KitText.Body, bool wrap = false)
        {
            var label = Kit.Text(parent, text, role, wrap);
            label.AddToClassList("ai-t");
            return label;
        }

        private static Label Plain(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ai-t");
            return label;
        }

        private static string Vi(string text) => text != null && Viet.TryGetValue(text, out var vi) ? vi : text;

        private void Save()
        {
            _cfg.url = _client.Url;
            PlayerPrefs.SetString(PrefsKey, JsonConvert.SerializeObject(_cfg));
        }

        // ------------------------------------------------------------------ xem trước bằng dữ liệu mẫu (công cụ chụp giao diện)
        /// <summary>Dựng một màn của cửa sổ bằng dữ liệu mẫu, không gọi server: config, run hoặc results.</summary>
        public void Preview(string state)
        {
            _modal ??= Kit.Modal("Tạo ảnh bằng AI", KitModalSize.Xl, Cleanup);
            _modal.Title.AddToClassList("ai-title");
            _modal.Title.pickingMode = PickingMode.Ignore;
            _modal.Box.AddToClassList("ai-win");
            _load = null;
            _status = new AiStatus { Reachable = true, Ready = true };
            _spec = SampleSpec();
            _models = new List<AiModel>
            {
                new() { id = "gpt-image-2", tag = "latest" }, new() { id = "gpt-image-1.5" }, new() { id = "gpt-image-1-mini", tag = "fast" },
                new() { id = "gpt-image-2.6-sunburst", tag = "preview" }, new() { id = "gpt-image-2.6-flare", tag = "preview" },
                new() { id = "gpt-image-2.5-sunburst", tag = "preview" }, new() { id = "gpt-image-2.5-flare", tag = "preview" },
            };
            _cfg.mode = "scene";
            _cfg.model = "gpt-image-2";
            _cfg.count = 2;
            _cfg.opts["detail"] = "standard";
            _cfg.opts["framing"] = "standard";
            _cfg.opts["colors"] = "any";
            _cfg.opts["background"] = "mix";
            _modal.SetOpen(true);
            _jobId = null;
            _finished = false;
            switch (state)
            {
                case "run":
                    _mode = "scene";
                    _model = "gpt-image-2";
                    _runCount = 2;
                    _doneAt.Clear();
                    ShowRun();
                    _doneAt["1.png"] = 72f;
                    _job = new AiJob
                    {
                        results = new List<AiResult> { new() { name = "1.png", bg = "Waves" } },
                        log = new List<string> { "[1] gửi ảnh gốc và ảnh mẫu phong cách tới gpt-image-2", "[1] nhận ảnh sau 72 giây", "[2] đang chờ phản hồi từ model…" },
                    };
                    PaintSlots(94f);
                    _slots[0].Thumb = Task.FromResult(SampleImage.Make(128, 10).EncodeToPNG());
                    PaintSlots(94f);
                    break;
                case "results":
                    _mode = "scene";
                    _model = "gpt-image-2";
                    _runCount = 2;
                    _doneAt["1.png"] = 72f;
                    _doneAt["2.png"] = 94f;
                    _results = new List<AiResult>
                    {
                        new() { name = "1.png", bg = "Waves", prompt = "Redraw image 1 as a stained glass picture with thick pure black outlines. Keep the whole layout of the picture. Use about 3-4 wide wavy horizontal bands for the background..." },
                        new() { name = "2.png", bg = "Rings", prompt = "Redraw image 1 as a stained glass picture with thick pure black outlines. Keep the whole layout of the picture. Use 3 big concentric rings and arcs around the main subject..." },
                    };
                    ClearPics();
                    for (var i = 0; i < 2; i++) _pics.Add((null, SampleImage.Make(256, 10 + i * 8)));
                    BuildResults();
                    break;
                default:
                    ShowConfig();
                    break;
            }
        }

        /// <summary>Hình chữ nhật của hộp trên màn hình (pixel, gốc dưới trái) để cắt ảnh chụp; null nếu chưa dựng.</summary>
        public Rect? ScreenRect()
        {
            if (_modal?.Box == null || _modal.Box.panel == null) return null;
            var layout = _modal.Box.panel.visualTree.layout;
            if (layout.width <= 0f) return null;
            var k = UnityEngine.Screen.width / layout.width;
            var b = _modal.Box.worldBound;
            return Rect.MinMaxRect(b.xMin * k, UnityEngine.Screen.height - b.yMax * k, b.xMax * k, UnityEngine.Screen.height - b.yMin * k);
        }

        private static AiSpec SampleSpec()
        {
            AiChoice C(string id, string label, string hint = "") => new() { id = id, label = label, hint = hint };
            return new AiSpec
            {
                modes = new List<AiMode> { new() { id = "subject", label = "Subject" }, new() { id = "scene", label = "Scene" } },
                options = new List<AiOption>
                {
                    new() { id = "detail", label = "Detail", type = "choice", def = "standard", modes = new List<string> { "subject", "scene" }, choices = new List<AiChoice> { C("simple", "Simple", "~25 pieces"), C("standard", "Standard", "~40 pieces"), C("detailed", "Detailed", "~60 pieces") } },
                    new() { id = "framing", label = "Subject size", type = "choice", def = "standard", modes = new List<string> { "subject" }, choices = new List<AiChoice> { C("tight", "Fills the frame", "90-95%"), C("standard", "Standard", "80-90%"), C("loose", "More background", "60-70%") } },
                    new() { id = "colors", label = "Colors", type = "choice", def = "any", modes = new List<string> { "subject", "scene" }, choices = new List<AiChoice> { C("any", "Any"), C("warm", "Warm"), C("cool", "Cool"), C("soft", "Soft") } },
                    new() { id = "background", label = "Background", type = "choice", def = "mix", modes = new List<string> { "subject", "scene" }, choices = new List<AiChoice> { C("mix", "Mix", "each image gets a different pattern"), C("auto", "Let the AI decide"), C("rays", "Rays"), C("waves", "Waves"), C("shards", "Glass shards"), C("rings", "Rings") } },
                    new() { id = "extra", label = "Extra instructions", type = "text", def = "", modes = new List<string> { "subject", "scene" } },
                },
            };
        }

        // ------------------------------------------------------------------ mở và đóng
        public void Open()
        {
            if (!_editor.HasSource) { LevelEditorMainUI.Warn("Chưa nạp ảnh gốc"); return; }
            if (_modal == null)
            {
                _modal = Kit.Modal("Tạo ảnh bằng AI", KitModalSize.Xl, Cleanup);
                _modal.Title.AddToClassList("ai-title");
                _modal.Title.pickingMode = PickingMode.Ignore;
                _modal.Box.AddToClassList("ai-win");
            }
            _screen = Screen.Config;
            _modal.SetOpen(true);
            Reload(false);
            ShowConfig();
        }

        private void Close() => _modal?.SetOpen(false);

        public void ClosePreview() => Close();

        // Huỷ job đang chạy và bỏ ảnh chưa dùng
        private void Cleanup()
        {
            if (_jobId != null && !_finished) _ = _client.Cancel(_jobId);
            _jobId = null;
            _start = null;
            _poll = null;
            _images = null;
            _finished = false;
            ClearPics();
            ClearThumbs();
        }

        private void ClearPics()
        {
            foreach (var (_, tex) in _pics) if (tex) UnityEngine.Object.Destroy(tex);
            _pics.Clear();
            _cards.Clear();
            _checks.Clear();
        }

        private void ClearThumbs()
        {
            foreach (var s in _slots) if (s.Tex) UnityEngine.Object.Destroy(s.Tex);
            _slots.Clear();
        }

        // ------------------------------------------------------------------ trạng thái server, model, tuỳ chọn
        private void Reload(bool refresh)
        {
            _load = Task.Run(async () =>
            {
                _status = await _client.Status();
                _spec = null;
                _models = null;
                if (!_status.Reachable) return;
                try
                {
                    _spec = await _client.Spec();
                    _models = await _client.Models(refresh);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Không tải được tuỳ chọn từ server ảnh: " + e.Message);
                }
            });
        }

        private void FixSettings()
        {
            if (_spec != null)
            {
                if (_spec.modes.Count > 0 && _spec.modes.All(m => m.id != _cfg.mode)) _cfg.mode = _spec.modes[0].id;
                foreach (var o in _spec.options)
                {
                    _cfg.opts.TryGetValue(o.id, out var v);
                    var valid = v != null && (o.type != "choice" || o.choices.Any(c => c.id == v));
                    if (!valid) _cfg.opts[o.id] = o.def ?? "";
                }
            }
            if (_models != null && _models.All(m => m.id != _cfg.model)) _cfg.model = _models.Count > 0 ? _models[0].id : "";
            Save();
        }

        // ------------------------------------------------------------------ mỗi khung hình
        public void Tick()
        {
            if (!IsOpen) return;
            if (_load != null && _load.IsCompleted)
            {
                _load = null;
                FixSettings();
                if (_screen == Screen.Config) ShowConfig();
            }
            if (_screen == Screen.Run) TickRun();
            if (_screen == Screen.Run && _images != null && _images.All(t => t.IsCompleted)) ShowResults();
        }

        // ------------------------------------------------------------------ khung chung
        // Xoá nội dung cũ; trả về hàng hai cột (cột trái cố định, cột phải co giãn) trong vùng cuộn
        private (VisualElement left, VisualElement right) Frame(bool twoColumns = true)
        {
            _modal.Body.Clear();
            _modal.Foot.Clear();
            _modal.Body.AddToClassList("ai-body");
            _modal.Foot.AddToClassList("ai-foot");
            var scroll = Kit.Scroll(_modal.Body, "ai-scroll");
            _content = scroll.contentContainer;
            var cols = Kit.Row(_content, KitGap.None, "ai-cols");
            if (!twoColumns) return (null, Kit.Col(cols, KitGap.None, "ai-col2"));
            return (Kit.Col(cols, KitGap.None, "ai-col1"), Kit.Col(cols, KitGap.None, "ai-col2"));
        }

        private void Width(float px)
        {
            _modal.Box.style.width = px;
            _modal.Box.style.maxWidth = Length.Percent(96);
        }

        private static VisualElement Section(VisualElement parent, string title, string rightText = null, Action rightAction = null)
        {
            var row = Kit.Row(parent, KitGap.None, "ai-sec");
            T(row, title.ToUpperInvariant(), KitText.Dim).AddToClassList("ai-sec-title");
            Kit.Spacer(row);
            if (rightText == null) return row;
            if (rightAction == null) T(row, rightText, KitText.Dim);
            else
            {
                var link = T(row, rightText, KitText.Dim);
                link.AddToClassList("ai-link");
                link.RegisterCallback<ClickEvent>(_ => rightAction());
            }
            return row;
        }

        // Hàng nút chọn một: nhãn và dòng phụ nhỏ bên dưới; nút đang chọn có viền vàng
        private static void Choices(VisualElement parent, IList<(string label, string sub)> items, int current, Action<int> pick, string classes = null)
        {
            var row = Kit.Row(parent, KitGap.None, "ai-ch " + classes);
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var b = new Button(() => pick(index)) { focusable = false };
                b.AddToClassList("ai-b");
                b.EnableInClassList("ai-on", i == current);
                var label = Plain(items[i].label);
                label.AddToClassList("ai-b-label");
                b.Add(label);
                if (!string.IsNullOrEmpty(items[i].sub))
                {
                    var sub = Plain(items[i].sub);
                    sub.AddToClassList("ai-b-sub");
                    b.Add(sub);
                }
                row.Add(b);
            }
        }

        // Lề đầu cột (tiêu đề phần) và lề cuối hàng (nút, thẻ) bỏ về 0; USS không có :first-child và :last-child
        private static void TrimEnds(VisualElement column)
        {
            if (column == null) return;
            if (column.childCount > 0 && column[0].ClassListContains("ai-sec")) column[0].style.marginTop = 0;
            foreach (var row in column.Children())
                if (row.ClassListContains("ai-ch") || row.ClassListContains("ai-modes") || row.ClassListContains("ai-two"))
                    if (row.childCount > 0) row[row.childCount - 1].style.marginRight = 0;
        }

        private static string Short(string hint) => string.IsNullOrEmpty(hint) ? "" : HintVi.GetValueOrDefault(hint, hint.Replace(" pieces", ""));

        // ------------------------------------------------------------------ màn 1: tuỳ chọn
        private void ShowConfig()
        {
            _screen = Screen.Config;
            Width(1060);
            var (left, right) = Frame();

            Section(left, "Ảnh gốc");
            var holder = Kit.El(left, "ai-photo-box");
            var photo = new Image { image = _editor.SourceTexture, scaleMode = ScaleMode.ScaleAndCrop };
            photo.AddToClassList("ai-photo");
            holder.Add(photo);
            var tex = _editor.SourceTexture;
            var cap = Kit.Row(holder, KitGap.None, "ai-photo-cap");
            T(cap, _editor.SourceName ?? "ảnh", KitText.Caption);
            Kit.Spacer(cap);
            T(cap, $"{(tex ? tex.width : 0)} × {(tex ? tex.height : 0)}", KitText.Caption);

            Section(left, "Server ảnh", "↻ Kiểm tra lại", () => { Reload(true); ShowConfig(); });
            var box = Kit.Col(left, KitGap.Xs, "ai-srv");
            var url = Kit.Input(box, _client.Url, v => { _client.Url = v.Trim(); Save(); Reload(false); ShowConfig(); }, "http://127.0.0.1:8765", true);
            url.tooltip = "Địa chỉ server ảnh (chạy server/server.py của Studio)";
            var none = _status.Reachable && _status.Ready && _models != null && _models.Count == 0;
            var warn = !_status.Reachable || !_status.Ready || _status.Busy || none;
            var text = _load != null ? "Đang kiểm tra server…"
                : !_status.Reachable ? "Không kết nối được server"
                : !_status.Ready ? "Server chưa sẵn sàng"
                : _status.Busy ? "Server đang bận vẽ ảnh khác"
                : none ? "Kết nối được nhưng chưa có model vẽ ảnh nào dùng được"
                : "Sẵn sàng";
            var state = Kit.Row(box, KitGap.Sm, "ai-state");
            var pill = Kit.Row(state, KitGap.None, "ai-pill");
            pill.EnableInClassList("ai-pill-bad", !_status.Reachable && _load == null);
            pill.EnableInClassList("ai-pill-warn", (_status.Reachable && warn) || _load != null);
            Kit.El(pill, "ai-dot");
            T(pill, text, KitText.Body);
            if (_models != null && _models.Count > 0) T(state, _models.Count + " model", KitText.Dim).AddToClassList("ai-after-pill");
            if (!_status.Reachable && _load == null) T(box, "Chạy: uv run server/server.py (xem Studio/server/README.md)", KitText.Dim, true);
            if (!string.IsNullOrEmpty(_status.Message)) T(box, _status.Message, KitText.Dim, true);

            Section(left, "Số ảnh cần vẽ");
            Choices(left, new[] { ("1", ""), ("2", ""), ("3", ""), ("4", "") }, _cfg.count - 1, i => { _cfg.count = i + 1; Save(); ShowConfig(); });
            T(left, "Các ảnh vẽ cùng lúc trên server; chọn ảnh ưng nhất sau khi vẽ xong.", KitText.Dim, true);

            if (_spec != null)
            {
                Section(right, "Kiểu vẽ");
                var modes = _spec.modes;
                var modeRow = Kit.Row(right, KitGap.None, "ai-modes");
                foreach (var m in modes)
                {
                    var mode = m;
                    var card = Kit.Row(modeRow, KitGap.None, "ai-mode");
                    card.EnableInClassList("ai-on", mode.id == _cfg.mode);
                    var holderIcon = Kit.El(card, "ai-mode-ic");
                    holderIcon.Add(new AiPatternIcon(mode.id == "scene" ? "scene" : "person"));
                    var texts = Kit.Col(card, KitGap.None);
                    T(texts, Vi(mode.label), KitText.Heading);
                    T(texts, ModeHint.GetValueOrDefault(mode.id, ""), KitText.Dim, true);
                    card.RegisterCallback<ClickEvent>(_ => { _cfg.mode = mode.id; Save(); ShowConfig(); });
                }
                Section(right, "Model vẽ ảnh", _models != null && _models.Count > 0 ? _models.Count + " model dùng được trên server" : null);
                if (_models != null && _models.Count > 0) ModelList(right);
                else T(right, _status.Reachable ? "Server chưa có model nào dùng được: kiểm tra API key của server." : "Chưa lấy được danh sách model: hãy kết nối server trước.", KitText.Dim, true);

                var pairs = new List<AiOption>();
                foreach (var o in _spec.options.Where(o => o.modes.Contains(_cfg.mode)))
                {
                    if (o.type == "choice" && o.id != "background") pairs.Add(o);
                }
                for (var i = 0; i < pairs.Count; i += 2)
                {
                    var row = Kit.Row(right, KitGap.None, "ai-two");
                    foreach (var o in pairs.Skip(i).Take(2))
                    {
                        var column = Kit.Col(row, KitGap.None, "ai-half");
                        OptionColumn(column, o);
                        if (o.id == "colors") T(column, "Vẫn chỉ dùng màu của game.", KitText.Dim);
                    }
                    if (pairs.Count - i == 1) Kit.Col(row, KitGap.None, "ai-half");
                }
                foreach (var o in _spec.options.Where(o => o.modes.Contains(_cfg.mode) && o.id == "background")) PatternGrid(right, o);
                foreach (var o in _spec.options.Where(o => o.modes.Contains(_cfg.mode) && o.type == "text"))
                {
                    Section(right, Vi(o.label));
                    var count = T(right, _cfg.opts.GetValueOrDefault(o.id, "").Length + " / " + MaxExtra, KitText.Caption);
                    count.AddToClassList("ai-count");
                    var field = Kit.Input(right, _cfg.opts.GetValueOrDefault(o.id, ""), v =>
                    {
                        _cfg.opts[o.id] = v.Length > MaxExtra ? v[..MaxExtra] : v;
                        count.text = _cfg.opts[o.id].Length + " / " + MaxExtra;
                        Save();
                    });
                    field.multiline = true;
                    field.AddToClassList("ai-extra");
                    count.PlaceInFront(field);
                }
            }
            else T(right, "Các tuỳ chọn lấy từ server: hãy kết nối server trước.", KitText.Dim, true);

            var reason = _load != null ? "Đang tải thông tin từ server…"
                : !_status.Reachable ? "Chưa kết nối server"
                : !_status.Ready ? "Server chưa sẵn sàng"
                : _status.Busy ? "Server đang bận với việc khác"
                : _spec == null || _models == null ? "Chưa tải được tuỳ chọn từ server"
                : _models.Count == 0 ? "Server chưa có model nào dùng được"
                : string.IsNullOrEmpty(_cfg.model) ? "Chưa chọn model"
                : null;
            if (reason == null)
            {
                var chips = Kit.Row(_modal.Foot, KitGap.None, "ai-chips");
                var modeLabel = _spec?.modes.FirstOrDefault(m => m.id == _cfg.mode)?.label;
                foreach (var chip in new[] { _cfg.count + " ảnh", Vi(modeLabel), _cfg.model }) T(chips, chip, KitText.Caption).AddToClassList("ai-chip");
            }
            var hint = T(_modal.Foot, reason ?? "Lời nhắc do server dựng; ảnh mẫu phong cách (style_reference.png) được gửi kèm ảnh của bạn.", KitText.Dim, true);
            hint.EnableInClassList("ai-why", reason != null);
            hint.style.flexGrow = 1;
            hint.style.flexShrink = 1;
            Kit.Button(_modal.Foot, "Huỷ", Close, KitTone.Neutral, KitSize.Md);
            TrimEnds(left);
            TrimEnds(right);
            var go = Kit.Button(_modal.Foot, $"✦ Vẽ {_cfg.count} ảnh" + (reason == null ? " bằng " + _cfg.model : ""), Begin, KitTone.Neutral, KitSize.Md);
            go.AddToClassList("ai-go");
            go.SetEnabled(reason == null);
        }

        private void ModelList(VisualElement parent)
        {
            var grid = Kit.Row(parent, KitGap.None, "ai-grid");
            foreach (var m in _models)
            {
                var id = m.id;
                var row = Kit.Row(grid, KitGap.None, "ai-m");
                row.EnableInClassList("ai-m-on", id == _cfg.model);
                Kit.El(row, "ai-radio").EnableInClassList("ai-radio-on", id == _cfg.model);
                T(row, id, KitText.Body).AddToClassList("ai-m-name");
                Kit.Spacer(row);
                if (!string.IsNullOrEmpty(m.tag))
                {
                    var tag = T(row, Vi(m.tag), KitText.Caption);
                    tag.AddToClassList("ai-tag");
                    tag.AddToClassList("ai-tag-" + m.tag);
                }
                row.RegisterCallback<ClickEvent>(_ => { _cfg.model = id; Save(); ShowConfig(); });
            }
        }

        private void OptionColumn(VisualElement parent, AiOption o)
        {
            Section(parent, Vi(o.label));
            var choices = o.choices;
            var current = _cfg.opts.GetValueOrDefault(o.id, o.def);
            Choices(parent, choices.Select(c => (Vi(c.label), o.id == "colors" ? "" : Short(c.hint))).ToList(), Mathf.Max(0, choices.FindIndex(c => c.id == current)), i => { _cfg.opts[o.id] = choices[i].id; Save(); ShowConfig(); });
        }

        private void PatternGrid(VisualElement parent, AiOption o)
        {
            Section(parent, Vi(o.label));
            var grid = Kit.Row(parent, KitGap.None, "ai-pat");
            var current = _cfg.opts.GetValueOrDefault(o.id, o.def);
            foreach (var c in o.choices)
            {
                var id = c.id;
                var b = new Button(() => { _cfg.opts[o.id] = id; Save(); ShowConfig(); }) { focusable = false };
                b.AddToClassList("ai-b");
                b.AddToClassList("ai-pat-cell");
                b.EnableInClassList("ai-on", id == current);
                if (!string.IsNullOrEmpty(c.hint)) b.tooltip = HintVi.GetValueOrDefault(c.hint, c.hint);
                b.Add(new AiPatternIcon(id));
                var label = Plain(Vi(c.label));
                label.AddToClassList("ai-b-label");
                b.Add(label);
                grid.Add(b);
            }
        }

        // ------------------------------------------------------------------ màn 2: đang vẽ
        private static byte[] Png(Texture2D t)
        {
            var longSide = Mathf.Max(t.width, t.height);
            if (longSide <= MaxPixels) return t.EncodeToPNG();
            var k = (float)MaxPixels / longSide;
            var rt = RenderTexture.GetTemporary(Mathf.Max(1, Mathf.RoundToInt(t.width * k)), Mathf.Max(1, Mathf.RoundToInt(t.height * k)));
            Graphics.Blit(t, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            small.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var png = small.EncodeToPNG();
            UnityEngine.Object.Destroy(small);
            return png;
        }

        private void Begin()
        {
            if (_editor.SourceTexture == null) return;
            var png = Png(_editor.SourceTexture);
            _mode = _cfg.mode;
            _model = _cfg.model;
            _runCount = _cfg.count;
            var options = _cfg.opts.Where(p => _spec != null && _spec.options.Any(o => o.id == p.Key)).ToDictionary(p => p.Key, p => p.Value);
            var mode = _mode;
            var model = _model;
            var count = _runCount;
            _start = Task.Run(() => _client.Start(png, mode, model, count, options));
            _jobId = null;
            _poll = null;
            _images = null;
            _job = null;
            _failures = 0;
            _cancelling = _finished = false;
            _doneAt.Clear();
            ShowRun();
        }

        private void ShowRun()
        {
            _screen = Screen.Run;
            Width(780);
            ClearThumbs();
            var (_, body) = Frame(false);
            body.AddToClassList("ai-run");
            var title = Section(body, "Đang vẽ ảnh");
            var info = Kit.Row(title, KitGap.None, "ai-chips");
            T(info, _model, KitText.Caption).AddToClassList("ai-chip");
            T(info, _mode == "scene" ? "Cảnh" : "Chủ thể", KitText.Caption).AddToClassList("ai-chip");
            var grid = Kit.Row(body, KitGap.None, "ai-slots");
            for (var k = 1; k <= _runCount; k++)
            {
                var slot = Kit.Row(grid, KitGap.None, "ai-slot");
                var view = new SlotView { Icon = Kit.El(slot, "ai-ic") };
                view.Spin = Kit.El(view.Icon, "ai-spin");
                var texts = Kit.Col(slot, KitGap.None, "ai-slot-text");
                var titleRow = Kit.Row(texts, KitGap.Sm, "ai-slot-head");
                view.Title = T(titleRow, "Ảnh " + k, KitText.Body);
                view.Title.AddToClassList("ai-slot-title");
                view.Badge = T(titleRow, "", KitText.Caption);
                view.Badge.style.display = DisplayStyle.None;
                view.Status = T(texts, "Đang gửi ảnh…", KitText.Dim, true);
                view.Bar = Kit.El(texts, "ai-bar");
                view.BarSeg = Kit.El(view.Bar, "ai-bar-seg");
                _slots.Add(view);
            }
            _log = T(body, "", KitText.Dim, true);
            _log.AddToClassList("ai-log");
            var hint = T(body, "Giữ cửa sổ này mở: server tự huỷ job nếu 60 giây không ai hỏi thăm.", KitText.Dim, true);
            hint.AddToClassList("ai-center");
            _note = T(_modal.Foot, "", KitText.Dim, true);
            Kit.Spacer(_modal.Foot);
            _cancel = Kit.Button(_modal.Foot, "Huỷ vẽ", Cancel, KitTone.Neutral, KitSize.Md);
        }

        private void Cancel()
        {
            _cancelling = true;
            _cancel.SetEnabled(false);
            Kit.SetText(_cancel, "Đang dừng…");
            if (_jobId != null) _ = _client.Cancel(_jobId);
        }

        private static string Mmss(float seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";

        private void Fail(string message)
        {
            _finished = true;
            _note.text = message;
            _note.AddToClassList("ai-why");
            Kit.SetText(_cancel, "Đóng");
            _cancel.SetEnabled(true);
            _cancel.clicked -= Cancel;
            _cancel.clicked += Close;
        }

        private void TickRun()
        {
            Spin();
            if (_finished || _images != null) return;
            if (_start != null && _start.IsCompleted)
            {
                var t = _start;
                _start = null;
                if (t.IsFaulted) { Fail(t.Exception?.GetBaseException().Message ?? "Không gửi được ảnh"); return; }
                _jobId = t.Result;
                _started = Time.realtimeSinceStartup;
                _nextPoll = 0;
            }
            if (_jobId == null) return;
            var now = Time.realtimeSinceStartup;
            if (_poll == null && now >= _nextPoll) _poll = _client.Job(_jobId);
            if (_poll != null && _poll.IsCompleted)
            {
                var p = _poll;
                _poll = null;
                _nextPoll = now + PollSeconds;
                if (p.IsFaulted)
                {
                    if (++_failures >= 5) Fail("Mất kết nối server. Job có thể bị huỷ sau 60 giây.");
                }
                else
                {
                    _failures = 0;
                    _job = p.Result;
                    if (_job.status != "running") { PaintSlots(now - _started); Finish(_job); return; }
                }
            }
            PaintSlots(now - _started);
        }

        // Vòng xoay của ảnh đang vẽ
        private void Spin()
        {
            var angle = Time.realtimeSinceStartup * 360f % 360f;
            var t = Time.realtimeSinceStartup;
            foreach (var s in _slots)
            {
                if (s.Spin != null && s.Spin.resolvedStyle.display != DisplayStyle.None) s.Spin.style.rotate = new Rotate(angle);
                if (s.BarSeg != null && s.Bar.resolvedStyle.display != DisplayStyle.None) s.BarSeg.style.left = Length.Percent(Mathf.PingPong(t * 55f, 70f));
            }
        }

        private void PaintSlots(float elapsed)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var view = _slots[i];
                var name = (i + 1) + ".png";
                var res = _job?.results.FirstOrDefault(r => r.name == name);
                var err = _job?.errors.FirstOrDefault(e => e.StartsWith("Ảnh " + (i + 1) + ":"));
                if (res != null && !_doneAt.ContainsKey(name))
                {
                    _doneAt[name] = elapsed;
                    var id = _jobId;
                    view.Thumb = Task.Run(() => _client.Image(id, name));
                }
                view.Title.text = "Ảnh " + (i + 1);
                view.Badge.style.display = res != null || err != null ? DisplayStyle.Flex : DisplayStyle.None;
                view.Badge.text = res != null ? "xong" : "lỗi";
                view.Badge.EnableInClassList("ai-ok", res != null);
                view.Badge.EnableInClassList("ai-bad", res == null);
                if (res != null || err != null) view.Bar.style.display = DisplayStyle.None;
                view.Status.text = res != null ? Mmss(_doneAt[name]) + (string.IsNullOrEmpty(res.bg) ? "" : " · nền: " + Vi(res.bg))
                    : err != null ? err.Substring(err.IndexOf(':') + 1).Trim()
                    : _cancelling ? "Đang dừng…" : "Đang vẽ · " + Mmss(elapsed);
                if (err != null && res == null) { view.Spin.style.display = DisplayStyle.None; view.Icon.AddToClassList("ai-ic-err"); }
                if (view.Thumb != null && view.Thumb.IsCompleted && view.Tex == null)
                {
                    view.Spin.style.display = DisplayStyle.None;
                    if (view.Thumb.IsFaulted) { view.Icon.AddToClassList("ai-ic-ok"); view.Tex = new Texture2D(1, 1); }
                    else
                    {
                        view.Tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        view.Tex.LoadImage(view.Thumb.Result);
                        view.Icon.style.backgroundImage = new StyleBackground(view.Tex);
                        view.Icon.AddToClassList("ai-ic-ok");
                    }
                }
            }
            if (_log != null && _job != null) _log.text = string.Join("\n", _job.log.TakeLast(6));
        }

        private void Finish(AiJob job)
        {
            if (job.status == "error" || job.results.Count == 0)
            {
                Reload(false);
                Fail(job.error ?? (job.status == "cancelled" ? "Đã dừng." : "Không có ảnh nào được vẽ."));
                return;
            }
            _finished = true;
            _results = job.results.OrderBy(r => int.TryParse(r.name.Split('.')[0], out var n) ? n : 0).ToList();
            var id = _jobId;
            _images = _results.Select(r => Task.Run(() => _client.Image(id, r.name))).ToArray();
            _note.text = "Đang tải ảnh về…";
        }

        // ------------------------------------------------------------------ màn 3: chọn ảnh
        private void ShowResults()
        {
            ClearPics();
            ClearThumbs();
            var kept = new List<AiResult>();
            for (var i = 0; i < _images.Length; i++)
            {
                if (_images[i].IsFaulted) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(_images[i].Result);
                _pics.Add((_images[i].Result, tex));
                kept.Add(_results[i]);
            }
            if (_pics.Count == 0) { Fail("Không tải được ảnh nào từ server."); return; }
            _results = kept;
            _images = null;
            BuildResults();
        }

        private void BuildResults()
        {
            _screen = Screen.Results;
            Width(1020);
            _sel = 0;

            var (left, right) = Frame();
            left.RemoveFromClassList("ai-col1");
            left.AddToClassList("ai-list-col");
            right.RemoveFromClassList("ai-col2");
            right.AddToClassList("ai-side");
            var list = Kit.Row(left, KitGap.None, "ai-list");
            for (var i = 0; i < _pics.Count; i++)
            {
                var index = i;
                var card = Kit.Col(list, KitGap.None, "ai-card");
                var pic = new Image { image = _pics[i].tex, scaleMode = ScaleMode.ScaleAndCrop };
                pic.AddToClassList("ai-card-img");
                pic.RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.width > 1f) pic.style.height = e.newRect.width; });
                Kit.Put(card, pic);
                var r = _results[i];
                var tag = T(pic, string.IsNullOrEmpty(r.bg) ? "Ảnh " + (i + 1) : "Nền: " + Vi(r.bg), KitText.Caption);
                tag.AddToClassList("ai-card-tag");
                var check = T(pic, "✓", KitText.Caption);
                check.AddToClassList("ai-card-chk");
                _checks.Add(check);
                var meta = Kit.Row(card, KitGap.None, "ai-meta");
                var number = int.TryParse(r.name.Split('.')[0], out var n) ? n : i + 1;
                T(meta, $"Ảnh {number} · {_model}", KitText.Dim);
                Kit.Spacer(meta);
                if (_doneAt.TryGetValue(r.name, out var seconds)) T(meta, Mmss(seconds), KitText.Dim);
                card.RegisterCallback<ClickEvent>(_ => { _sel = index; PaintSelection(); });
                _cards.Add(card);
            }
            Section(right, "Lời nhắc của ảnh đang chọn");
            var promptBox = Kit.Scroll(right, "ai-prompt");
            _prompt = T(promptBox.contentContainer, "", KitText.Dim, true);
            var info = T(right, "Ảnh được nạp như Import Image: dùng xong bấm Generate để tách mảnh.", KitText.Dim, true);
            info.AddToClassList("ai-note");
            if (_job != null && _job.errors.Count > 0) T(right, _job.errors[0], KitText.Dim, true).AddToClassList("ai-why");
            TrimEnds(left);
            TrimEnds(right);
            PaintSelection();

            var hint = T(_modal.Foot, "Ảnh chưa dùng sẽ bị bỏ khi đóng cửa sổ: bấm \"Lưu ảnh\" để giữ lại.", KitText.Dim, true);
            hint.style.flexShrink = 1;
            Kit.Spacer(_modal.Foot);
            Kit.Button(_modal.Foot, $"Vẽ lại {_runCount} ảnh", () => { _cfg.count = _runCount; ClearPics(); Reload(false); ShowConfig(); }, KitTone.Neutral, KitSize.Md);
            Kit.Button(_modal.Foot, "Lưu ảnh", SaveAll, KitTone.Neutral, KitSize.Md, "Chọn một thư mục: tất cả ảnh vừa vẽ được lưu vào đó dạng PNG");
            var use = Kit.Button(_modal.Foot, "Dùng ảnh này", Use, KitTone.Neutral, KitSize.Md);
            use.AddToClassList("ai-go");
        }

        private void PaintSelection()
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                _cards[i].EnableInClassList("ai-card-on", i == _sel);
                _checks[i].style.display = i == _sel ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_prompt != null && _sel < _results.Count) _prompt.text = _results[_sel].prompt ?? "";
        }

        private void Use()
        {
            var number = int.TryParse(_results[_sel].name.Split('.')[0], out var n) ? n : _sel + 1;
            if (_pics[_sel].png != null && _editor.LoadImageBytes(_pics[_sel].png, $"ai_{_mode}_{number}")) Close();
        }

        private void SaveAll()
        {
            var paths = StandaloneFileBrowser.OpenFolderPanel("Lưu ảnh AI vào thư mục", "", false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            for (var i = 0; i < _pics.Count; i++)
            {
                var number = int.TryParse(_results[i].name.Split('.')[0], out var n) ? n : i + 1;
                File.WriteAllBytes(Path.Combine(paths[0], $"ai_{_mode}_{stamp}_{number}.png"), _pics[i].png);
            }
            LevelEditorMainUI.Log($"Đã lưu {_pics.Count} ảnh vào {paths[0]}");
        }
    }
}
