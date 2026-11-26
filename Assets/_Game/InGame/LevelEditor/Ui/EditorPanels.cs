using System.Collections.Generic;
using System.Linq;
using Falcon.InGame.Core;
using Falcon.InGame.LevelEditor.Gen;
using Falcon.Shared.BaseLevelEditor;
using Falcon.Shared.EditorUi;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Falcon.InGame.LevelEditor
{
    /// <summary>Khung editor: panel trái 3 tab (Tạo tranh, Hàng chờ, Mechanic), panel mechanic nổi và inspector phải cho mảnh hoặc hộp, theo mockup Mosaic Studio.</summary>
    public sealed partial class EditorPanels : IEditorScreen
    {
        private const int TabQueue = 1, TabMechanic = 2, Hidden = 0, Pair = 1, Frozen = 2;
        private static readonly Color HiddenGrey = new(0.5f, 0.5f, 0.5f);
        private static readonly Color[] PairColors =
        {
            new(0.133f, 0.827f, 0.933f), new(0.961f, 0.620f, 0.043f), new(0.639f, 0.902f, 0.208f),
            new(0.957f, 0.447f, 0.714f), new(0.506f, 0.549f, 0.973f), new(0.984f, 0.443f, 0.522f),
        };

        private readonly PictureEditor _editor;
        private readonly BoxQueueEditor _boxes;
        private readonly Dictionary<int, int> _addValue = new();
        private readonly Button[] _pieceSwatches = new Button[ColorPalette.Count];
        private readonly Button[] _boxSwatches = new Button[ColorPalette.Count];

        private VisualElement _tip, _info, _infoColors, _infoSmall, _band, _left, _right, _pieceBox, _boxBox, _balance, _columns, _mechColumns, _mech, _mechBody, _frameRow, _boxFrozen, _boxIdle;
        private Label _boxDiffer, _differ, _infoFrame, _infoCount, _infoSand, _infoManual, _source, _head, _headSub, _empty, _boxHead, _boxSub;
        private Button _splitBtn, _mergeBtn, _deleteBtn, _import, _clear, _generate, _pieceHide, _boxHide, _freezeNow;
        private Button[] _mechTabs;
        private readonly Button[] _patterns = new Button[Subdivide.Labels.Length];
        private Image _thumb;
        private KitHead _frameHead;
        private Toggle _showColor, _showGlass, _showValues;
        private KitStepper _splitCount, _value, _boxValue, _frameW, _frameH, _slots, _boxFreeze;
        private Button _cutBtn, _genQueue, _boxDelete, _paste, _aiBtn;
        private readonly AiDialog _ai;
        private readonly BusyPopup _busy = new();
        private Label _penHint;
        private bool _dirty = true, _ice, _tipOpen;
        private int _shownVersion = -1, _tab, _mechanic;

        private readonly EditHistory _history;

        public EditorPanels(PictureEditor editor, BoxQueueEditor boxes, EditHistory history)
        {
            _history = history;
            Current = this;
            _editor = editor;
            _boxes = boxes;
            _ai = new AiDialog(editor);
            _editor.Changed += () => _dirty = true;
            _boxes.Changed += () => _dirty = true;
        }

        private VisualElement _root;
        private KitTabs _tabs;

        public VisualElement Root => _root;

        /// <summary>Bảng đang chạy (cho công cụ rà layout trong Editor).</summary>
        public static EditorPanels Current { get; private set; }

        public void Build(VisualElement root)
        {
            _root = root;
            BuildLeft(root);
            BuildMechanic(root);
            BuildTip(root);
            _band = Kit.El(root, "ps-band");
            _band.pickingMode = PickingMode.Ignore;
            Kit.SetShown(_band, false);
            BuildRight(root);
        }
    }

}
