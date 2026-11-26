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
    public partial class PictureEditor
    {
        // ------------------------------------------------------------------ mở ảnh, khung, tạo tranh
        /// <summary>Xoá ảnh nguồn và tranh đang mở.</summary>
        public void Clear()
        {
            if (IsBusy) return;
            _source = null;
            SourceName = null;
            ReleaseTexture();
            _selection.Clear();
            SelectionVersion++;
            Commit(() => _propertyData = new PictureProperty());
        }

        private void ReleaseTexture()
        {
            if (_texture) Destroy(_texture);
            _texture = null;
            _view?.SetSource(null, false, 0f, default, default, 1f);
        }

        public void OpenImage()
        {
            if (IsBusy || HasPicture) return;
            var paths = StandaloneFileBrowser.OpenFilePanel("Chọn ảnh", "", new[] { new ExtensionFilter("Ảnh", "png", "jpg", "jpeg") }, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            LoadImageBytes(File.ReadAllBytes(paths[0]), Path.GetFileNameWithoutExtension(paths[0]));
        }

        /// <summary>Nạp ảnh từ clipboard (ảnh chụp, ảnh copy, hoặc file ảnh vừa copy trong Explorer).</summary>
        public void PasteImage()
        {
            if (IsBusy || HasPicture) return;
            if (!ClipboardImage.TryRead(out var data, out var name)) { LevelEditorMainUI.Warn("Clipboard không có ảnh"); return; }
            LoadImageBytes(data, name);
        }

        /// <summary>Nạp ảnh nguồn từ dữ liệu PNG hoặc JPG (dùng cho Import, Paste và ảnh AI); false nếu không đọc được.</summary>
        public bool LoadImageBytes(byte[] data, string name)
        {
            if (IsBusy || HasPicture) return false;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data))
            {
                Destroy(tex);
                LevelEditorMainUI.Warn("Không đọc được ảnh này");
                return false;
            }
            ReleaseTexture();
            _texture = tex;
            _source = PictureConvert.ToSource(tex);
            SourceName = name;
            FitFrame();
            LevelEditorMainUI.Log($"Đã nạp {SourceName} ({tex.width}x{tex.height}): chỉnh khung rồi bấm Generate");
            return true;
        }

        /// <summary>Nạp ảnh mẫu sinh bằng code (thay cho hộp chọn file), dùng để thử và rà giao diện.</summary>
        public void LoadSample()
        {
            if (IsBusy || HasPicture) return;
            ReleaseTexture();
            _texture = SampleImage.Make();
            _source = PictureConvert.ToSource(_texture);
            SourceName = "sample";
            FitFrame();
        }

        public void SetFrame(int w, int h)
        {
            _options.FrameW = Mathf.Clamp(w, MinFrame, MaxFrame);
            _options.FrameH = Mathf.Clamp(h, MinFrame, MaxFrame);
            ShowPreview();
            Changed?.Invoke();
        }

        /// <summary>Đặt chiều cao khung theo tỉ lệ ảnh (phần có màu).</summary>
        public void FitFrame()
        {
            if (_source == null) return;
            var crop = PictureGenerator.OpaqueBounds(_source);
            if (crop.W <= 0 || crop.H <= 0) crop = new IntRect { X = 0, Y = 0, W = _source.Width, H = _source.Height };
            SetFrame(_options.FrameW, Mathf.RoundToInt(_options.FrameW * (float)crop.H / crop.W));
        }

        // Ảnh nguồn nửa trong suốt trong khung, cắt phủ kín như lúc tạo tranh
        private void ShowPreview()
        {
            if (!_view || !_texture || _source == null) return;
            var crop = PictureGenerator.OpaqueBounds(_source);
            if (crop.W <= 0 || crop.H <= 0) crop = new IntRect { X = 0, Y = 0, W = _source.Width, H = _source.Height };
            var pl = PictureGenerator.Place(crop.W, crop.H, _options.FrameW, _options.FrameH, true);
            var rect = new RectInt(crop.X + Mathf.RoundToInt((float)(pl.UX * crop.W)), crop.Y + Mathf.RoundToInt((float)(pl.UY * crop.H)),
                Mathf.Max(1, Mathf.RoundToInt((float)(pl.UW * crop.W))), Mathf.Max(1, Mathf.RoundToInt((float)(pl.UH * crop.H))));
            _view.SetSource(_texture, true, 0.85f, rect, new Rect(0, 0, 1, 1), _options.FrameW / (float)_options.FrameH);
        }

        /// <summary>Tách mảnh lại từ ảnh đã mở.</summary>
        public void Generate()
        {
            if (IsBusy || _source == null) return;
            var src = _source;
            var opts = _options;
            _watch = Stopwatch.StartNew();
            BusyLabel = "Đang tạo tranh, đợi vài giây…";
            _job = Task.Run(() => PictureGenerator.Generate(src, opts));
            LevelEditorMainUI.Log("Đang tạo tranh…");
            Changed?.Invoke();
        }

        private void FinishJob()
        {
            var job = _job;
            _job = null;
            _watch.Stop();
            if (job.IsFaulted)
            {
                Debug.LogException(job.Exception);
                LevelEditorMainUI.Warn("Tạo tranh lỗi: " + job.Exception?.GetBaseException().Message);
            }
            else if (job.Result == null) LevelEditorMainUI.Warn("Ảnh này không phải kiểu kính màu viền đen, chưa hỗ trợ");
            else
            {
                _view.SetSource(null, false, 0f, default, default, 1f);
                _selection.Clear();
                SelectionVersion++;
                Commit(() => _propertyData = PictureConvert.ToProperty(job.Result, _options, SourceName));
                LevelEditorMainUI.Log($"Đã tạo {_propertyData.regions.Count} mảnh trong {_watch.ElapsedMilliseconds / 1000f:0.0}s");
            }
            Changed?.Invoke();
        }

        // Chạy việc nặng ở luồng nền, xong thì áp kết quả ở luồng chính
        private void RunBusy<T>(string label, Func<T> work, Action<T> done)
        {
            if (IsBusy) return;
            BusyLabel = label;
            LevelEditorMainUI.Log(label);
            var task = Task.Run(work);
            _work = task;
            _finish = () =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    LevelEditorMainUI.Warn("Lỗi khi xử lý: " + task.Exception?.GetBaseException().Message);
                }
                else done(task.Result);
            };
            Changed?.Invoke();
        }
    }
}
