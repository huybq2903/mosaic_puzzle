using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using Newtonsoft.Json;
using SFB;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Handler tranh của level editor Color Sort: mở ảnh, tách mảnh (nền), hiển thị bằng PictureView, chọn nhiều mảnh, đổi màu và số cát, gộp, xoá.</summary>
    public partial class PictureEditor : APropertyDataHandler<PictureProperty>, IEditorManager
    {
        private const int MinFrame = 8, MaxFrame = 400;
        private const float ZoomStep = 1.1f, DragPixels = 6f, PickRadius = 6f;

        private PictureView _view;
        private Camera _camera;
        private Task<GeneratedPicture> _job;
        private Task _work;
        private Action _finish;
        private Stopwatch _watch;
        private SourceImage _source;
        private Texture2D _texture;
        private readonly GenOptions _options = new();
        private readonly List<int> _selection = new();
        private RegionMap _map;
        private List<SmallPiece> _small;
        private float _baseSize;
        private bool _panning, _pressing, _banding, _pressAdd;
        private Vector2 _pressScreen, _pressPicture;

        public event Action Changed;

        public EditHistory History { get; set; }

        public PictureProperty Picture => _propertyData;

        public string SourceName { get; private set; }

        public bool HasSource => _source != null;

        public bool HasPicture => _propertyData != null && _propertyData.regions.Count > 0;

        public bool IsBusy => _job != null || _work != null;

        public string BusyLabel { get; private set; }

        public Texture2D SourceTexture => _texture;

        public int FrameW => _options.FrameW;

        public int FrameH => _options.FrameH;

        public bool ShowColor { get; private set; } = true;

        public bool ShowGlass { get; private set; } = true;

        public bool ShowValues { get; private set; } = true;

        public bool HiddenMode { get; set; }

        /// <summary>Che màu mảnh ẩn (chỉ đổi cách xem, không đổi dữ liệu).</summary>
        public bool Cover { get; private set; } = true;

        // ------------------------------------------------------------------ vòng đời
        public void Initialized()
        {
            _view = new GameObject("[Picture]").AddComponent<PictureView>();
            _view.SetShowValues(true);
            _camera = FindFirstObjectByType<Camera>();
            var saveLoad = LevelEditorManager.Get<LevelEditorSaveLoad>();
            saveLoad?.RegisterSaveCondition(new SaveCondition("Chưa có tranh: Import Image rồi Generate", () => _propertyData == null || _propertyData.regions.Count == 0));
            Rebuild();
        }

        protected override void OnLoadLevel(OnLoadLevel data)
        {
            base.OnLoadLevel(data);
            _source = null;
            SourceName = null;
            ReleaseTexture();
            _selection.Clear();
            SelectionVersion++;
            History?.Clear();
            Rebuild();
            Changed?.Invoke();
        }

        private void Rebuild()
        {
            _map = null;
            _small = null;
            if (!_view) return;
            _view.Build(_propertyData);
            _view.SetShowValues(ShowValues);
            _view.SetGlass(ShowGlass);
            ApplyPaint();
            _view.SetSelection(_selection.Count > 0 ? _selection.ToArray() : null);
        }

        // ------------------------------------------------------------------ hoàn tác
        public string Capture() => JsonConvert.SerializeObject(_propertyData);

        public void Restore(string json)
        {
            _propertyData = JsonConvert.DeserializeObject<PictureProperty>(json) ?? new PictureProperty();
            _selection.RemoveAll(i => !Valid(i));
            SavePropertyData();
            Rebuild();
            SelectionChanged();
        }

        // Sửa dữ liệu: lưu bước hoàn tác, đẩy xuống level, dựng lại hình
        private void Commit(Action change)
        {
            History?.Record();
            change();
            SavePropertyData();
            Rebuild();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ xem
        public void SetCover(bool on) { Cover = on; ApplyPaint(); Changed?.Invoke(); }

        // Mảnh ẩn đang che thì xám như khi tắt màu
        private void ApplyPaint()
        {
            if (!_view || _propertyData == null) return;
            for (var i = 0; i < _propertyData.regions.Count; i++)
            {
                var r = _propertyData.regions[i];
                var covered = Cover && r.hidden;
                _view.SetPainted(i, ShowColor && !covered);
                _view.SetValueText(i, covered ? "?" : r.value.ToString(), covered ? ColorPalette.Get(r.colorId) : null);
            }
        }

        public void SetShowColor(bool on) { ShowColor = on; ApplyPaint(); }

        public void SetShowGlass(bool on) { ShowGlass = on; _view?.SetGlass(on); }

        public void SetShowValues(bool on) { ShowValues = on; _view?.SetShowValues(on); }

        // ------------------------------------------------------------------ vòng lặp
        private void Update()
        {
            if (_job != null && _job.IsCompleted) FinishJob();
            if (_work != null && _work.IsCompleted)
            {
                var finish = _finish;
                _work = null;
                _finish = null;
                finish?.Invoke();
                Changed?.Invoke();
            }
            HandleView();
            if (!IsBusy && _propertyData != null) HandlePick();
        }

        // Cuộn chuột phóng to, thu nhỏ quanh con trỏ; giữ chuột giữa kéo để dịch chuyển (như Studio)
        private void HandleView()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!_camera) _camera = FindFirstObjectByType<Camera>();
            if (!_camera || !_camera.orthographic) return;
            if (_baseSize <= 0f) _baseSize = _camera.orthographicSize;

            var over = EditorUiHost.IsPointerOverUi;
            var wheel = mouse.scroll.ReadValue().y;
            if (!over && Mathf.Abs(wheel) > 0.01f)
            {
                var screen = mouse.position.ReadValue();
                var before = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
                var size = _camera.orthographicSize * (wheel > 0f ? 1f / ZoomStep : ZoomStep);
                _camera.orthographicSize = Mathf.Clamp(size, _baseSize * 0.1f, _baseSize * 5f);
                var after = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
                _camera.transform.position += before - after;
            }

            if (mouse.middleButton.wasPressedThisFrame && !over) _panning = true;
            if (!mouse.middleButton.isPressed) _panning = false;
            if (!_panning) return;
            var move = mouse.delta.ReadValue() * (2f * _camera.orthographicSize / Screen.height);
            _camera.transform.position -= new Vector3(move.x, move.y, 0f);
        }
    }

}
