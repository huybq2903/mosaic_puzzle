using System.Collections.Generic;
using Shapes;
using TMPro;
using UnityEngine;

namespace Falcon.InGame.Core
{
    /// <summary>Dựng tranh mosaic: mỗi mảnh 1 mesh vertex color + vân kính, viền Polyline.</summary>
    public class PictureView : MonoBehaviour
    {
        [SerializeField, Tooltip("Chiều cao tranh (world unit).")] private float viewHeight = 8f;
        [SerializeField] private float outlineWidth = 0.06f;
        [SerializeField] private Color outlineColor = new(0.08f, 0.08f, 0.08f, 1f);
        [SerializeField] private Color copperColor = new(0.80f, 0.52f, 0.26f, 1f);
        [SerializeField] private Color shineColor = new(1f, 0.82f, 0.55f, 0.85f);
        [SerializeField, Tooltip("Độ lệch bóng đổ dưới tranh (world unit).")] private Vector2 shadowOffset = new(0.05f, -0.07f);
        [SerializeField] private Color highlightColor = new(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private Color selectionColor = new(1f, 0.2f, 0.75f, 1f);
        [SerializeField] private Color cutColor = new(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private Color frameColor = new(1f, 0.6f, 0.1f, 1f);
        [SerializeField, Tooltip("Cỡ 1 chu kỳ vân kính theo tỉ lệ chiều cao tranh; cỡ cố định, mảnh to thì vân lặp lại.")] private float glassPeriod = 0.11f;
        [SerializeField, Tooltip("Độ dày viền mảnh trong tranh có khe nét (nhân với Outline Width): khe đã là nét chì nên viền chỉ cần mỏng."), Min(0f)] private float gapOutlineScale = 0.3f;
        [SerializeField, Tooltip("Âm để UI Imui (order 0) vẽ đè lên.")] private int sortingOrder = -10;

        private const float MinLead = 0.7f, MaxLead = 4f, FlatLeadScale = 0.75f;
        private float _lead = 1f; // hệ số độ dày viền mảnh: tranh có khe nét thật thì viền mỏng, khe đã là nét chì
        private GameObject _leadBacking;
        private Mesh _leadMesh;
        private static readonly Color FlatLeadColor = new(0.07f, 0.05f, 0.04f, 1f);
        private readonly List<GameObject> _regionObjects = new();
        private readonly List<Mesh> _meshes = new();
        private readonly List<Mesh> _shadowMeshes = new();
        private readonly List<Polyline[]> _outlines = new();
        private readonly List<List<PolylinePoint>> _outerRings = new(); // điểm viền vòng ngoài của từng mảnh, để ẩn rồi hiện lại một đoạn
        private readonly List<int> _colorIds = new();
        private readonly List<bool> _painted = new();
        private Material _material, _shadowMaterial;
        private bool _glassShader;
        private Texture2D _glass, _facets;
        private bool _ownsTextures; // chỉ huỷ texture do mình tự tạo, không huỷ asset nạp từ Resources
        private bool _glassOn = true;
        private float _scale;
        private Vector2 _offset;
        private int _highlight = -1;
        private float _zs = 1f; // hệ số thu nhỏ công cụ vẽ (điểm neo, tay cầm, đường xem trước, viền focus) theo mức zoom: zoom gần thì nhỏ lại để dễ vẽ chính xác
        private const float MinToolScale = 0.12f;
        private readonly HashSet<int> _selection = new();
        private SpriteRenderer _source;
        private RectInt _sourceRect;
        private Polyline _frame;
        private SpriteRenderer _overlay;
        private Texture2D _overlayTex;
        private Vector2 _frameSize;
        private Polyline _cut;
        private Mesh _decorMesh; // hình trang trí kín (PictureProperty.decor)
        private GameObject _decorShape;
        private Disc _snap;
        private readonly List<TextMeshPro> _values = new();
        private bool _showValues = true;
        private const float ValueMaxHeight = 0.055f, ValueMinHeight = 0.03f, ValueOutline = 0.22f; // chiều cao số tối đa / tối thiểu theo chiều cao tranh
        private readonly List<Disc> _anchors = new();
        private readonly List<Polyline> _handleLines = new();
        private readonly List<Disc> _handleDots = new();
        private readonly List<Polyline> _handleCases = new();
        private readonly List<Disc> _handleDotCases = new();
        private static readonly Color PreviewColor = new(0f, 1f, 0f, 1f); // đường xem trước và thanh cong: xanh lá neon, xa mọi màu palette nhất (ΔE >= 40)
        private static readonly Color CaseColor = Color.black; // viền đen đi kèm để vẫn thấy trên mảnh xanh lá và mảnh sáng
        private Polyline _cutCase;
        private Polyline _cutBand; // dải mờ đúng bề rộng khe sẽ tạo giữa hai mảnh
        private float _bandCells;

        public int RegionCount => _meshes.Count;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam || !cam.orthographic || viewHeight <= 0f) return;
            var z = Mathf.Clamp(cam.orthographicSize / (viewHeight * 0.55f), MinToolScale, 1f); // 1 khi vừa khung hoặc zoom ra; nhỏ dần khi zoom vào
            if (Mathf.Abs(z - _zs) < 0.01f) return;
            _zs = z;
            RestyleTools();
        }

        // Áp lại cỡ cho các công cụ đã dựng khi mức zoom đổi
        private void RestyleTools()
        {
            for (var i = 0; i < _anchors.Count; i++) { _anchors[i].Radius = outlineWidth * 1.1f * _zs; _anchors[i].Thickness = outlineWidth * 0.5f * _zs; }
            for (var i = 0; i < _handleLines.Count; i++)
            {
                _handleLines[i].Thickness = outlineWidth * 0.3f * _zs;
                _handleCases[i].Thickness = outlineWidth * 0.9f * _zs;
                _handleDots[i].Radius = outlineWidth * 0.8f * _zs;
                _handleDotCases[i].Radius = outlineWidth * 1.15f * _zs;
            }
            if (_snap) _snap.Radius = outlineWidth * 0.9f * _zs;
            if (_cut) _cut.Thickness = outlineWidth * 1.5f * _zs;
            if (_cutCase) _cutCase.Thickness = outlineWidth * 2.6f * _zs;
            if (_highlight >= 0 && _highlight < _outlines.Count) foreach (var o in _outlines[_highlight]) Style(o, true, selected: _selection.Contains(_highlight));
            foreach (var i in _selection) if (i < _outlines.Count) foreach (var o in _outlines[i]) Style(o, true, selected: true);
        }

        public Bounds FrameBounds => _source && _source.gameObject.activeSelf ? new Bounds(transform.position, new Vector3(_frameSize.x, _frameSize.y, 0.1f)) : default;

        public Bounds WorldBounds => new(transform.position, new Vector3(_offset.x * 2f * _scale, _offset.y * 2f * _scale, 0.1f));

#if UNITY_EDITOR
        private PictureProperty _built;

        private void OnValidate()
        {
            if (_built != null) UnityEditor.EditorApplication.delayCall += () => { if (this && _built != null) Build(_built); }; // sửa độ dày trên Inspector: dựng lại ngay để thấy
        }
#endif

        public void Build(PictureProperty p)
        {
#if UNITY_EDITOR
            _built = p;
#endif
            Clear();
            if (p == null || p.regions.Count == 0) return;
            EnsureMaterial();

            _scale = viewHeight / p.height;
            _offset = new Vector2(p.width, p.height) * 0.5f;
            _lead = p.gen != null && p.gen.inkGaps ? gapOutlineScale : 1f;
            if (p.gen != null && p.gen.inkGaps) BuildLeadBacking(p);
            var tile = viewHeight * glassPeriod;
            var unit = Mathf.Max(1, p.unit);
            for (var i = 0; i < p.regions.Count; i++)
            {
                var r = p.regions[i];
                var pts = new List<Vector2>(r.points.Length / 2);
                for (var k = 0; k < r.points.Length; k += 2)
                    pts.Add((new Vector2(r.points[k], r.points[k + 1]) / unit - _offset) * _scale);

                var holeLists = new List<IReadOnlyList<Vector2>>();
                if (r.holes != null)
                    foreach (var hole in r.holes)
                    {
                        var hp = new List<Vector2>(hole.Length / 2);
                        for (var k = 0; k < hole.Length; k += 2) hp.Add((new Vector2(hole[k], hole[k + 1]) / unit - _offset) * _scale);
                        holeLists.Add(hp);
                    }

                var go = new GameObject($"Region_{i}");
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = go.name };
                var tris = PolygonTriangulator.Triangulate(pts, holeLists, out var verts);
                mesh.SetVertices(verts.ConvertAll(v => (Vector3)v));
                var rng = new System.Random(i * 7919 + 13); // mỗi mảnh cắt từ 1 vị trí và hướng khác nhau trên tấm kính, như kính thật
                var ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                var off = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                mesh.SetUVs(0, verts.ConvertAll(v => { var u = v / tile; return new Vector2(u.x * ca - u.y * sa, u.x * sa + u.y * ca) + off; }));
                var contrast = 0.75f + 0.70f * (float)rng.NextDouble(); // có mảnh gần phẳng, có mảnh loang đậm
                // kiểu kính của mảnh: sáng → xà cừ nhiều mặt, tối → kính vò có bóng, còn lại ngẫu nhiên giữa vệt và mặt cắt
                Color pc = ColorPalette.Get(r.colorId);
                var lumPiece = pc.grayscale;
                var facet = 0.85f + 0.15f * (float)rng.NextDouble();
                mesh.SetUVs(1, verts.ConvertAll(_ => new Vector2(contrast, facet)));
                mesh.SetTriangles(tris, 0);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.sortingOrder = sortingOrder;

                var shadowMesh = new Mesh { name = go.name + "_Shadow", vertices = mesh.vertices, triangles = tris };
                var shadowCols = new Color[verts.Count];
                for (var k = 0; k < shadowCols.Length; k++) shadowCols[k] = new Color(0f, 0f, 0f, 0.38f);
                shadowMesh.colors = shadowCols;
                var shadow = new GameObject("Shadow");
                shadow.transform.SetParent(go.transform, false);
                shadow.transform.localPosition = new Vector3(shadowOffset.x, shadowOffset.y, 0.02f);
                shadow.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
                var smr = shadow.AddComponent<MeshRenderer>();
                smr.sharedMaterial = _shadowMaterial;
                smr.sortingOrder = sortingOrder - 1;
                _shadowMeshes.Add(shadowMesh);

                var lead = outlineWidth * _lead;
                var rings = new Polyline[1 + holeLists.Count];
                List<PolylinePoint> outerRing = null;
                for (var k = 0; k < rings.Length; k++)
                {
                    var ringPts = Ring(k == 0 ? pts : holeLists[k - 1], k == 0 ? r.widths : r.holeWidths != null && k - 1 < r.holeWidths.Count ? r.holeWidths[k - 1] : null, unit);
                    var outline = new GameObject("Outline").AddComponent<Polyline>();
                    outline.transform.SetParent(go.transform, false);
                    outline.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                    outline.Closed = true;
                    outline.Joins = PolylineJoins.Round;
                    Style(outline, false);
                    outline.SetPoints(ringPts);
                    rings[k] = outline;
                    if (k == 0) outerRing = ringPts;

                    var glow = new GameObject("EdgeShade").AddComponent<Polyline>(); // tối dần vào sát viền chì, như kính dày có khối
                    glow.transform.SetParent(outline.transform, false);
                    glow.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                    glow.Closed = true;
                    glow.Joins = PolylineJoins.Round;
                    glow.Thickness = lead * 1.6f;
                    glow.Color = new Color(0f, 0f, 0f, 0.035f);
                    glow.SortingOrder = sortingOrder;
                    glow.SetPoints(ringPts);

                    var copper = new GameObject("Copper").AddComponent<Polyline>(); // sống đồng sáng giữa hai mép nâu tối: thanh chì có khối
                    copper.transform.SetParent(outline.transform, false);
                    copper.transform.localPosition = new Vector3(0f, 0f, -0.002f);
                    copper.Closed = true;
                    copper.Joins = PolylineJoins.Round;
                    copper.Thickness = lead * 0.52f;
                    copper.Color = copperColor;
                    copper.SortingOrder = sortingOrder + 1;
                    copper.SetPoints(ringPts);

                    var shine = new GameObject("Shine").AddComponent<Polyline>();
                    shine.transform.SetParent(outline.transform, false);
                    shine.transform.localPosition = new Vector3(-lead * 0.13f, lead * 0.13f, -0.004f);
                    shine.Closed = true;
                    shine.Joins = PolylineJoins.Round;
                    shine.Thickness = lead * 0.12f;
                    shine.Color = shineColor;
                    shine.SortingOrder = sortingOrder + 2;
                    shine.SetPoints(ringPts);

                    var shade = new GameObject("Shade").AddComponent<Polyline>(); // mép tối phía đối diện sáng: thanh chì có khối
                    shade.transform.SetParent(outline.transform, false);
                    shade.transform.localPosition = new Vector3(lead * 0.15f, -lead * 0.15f, -0.003f);
                    shade.Closed = true;
                    shade.Joins = PolylineJoins.Round;
                    shade.Thickness = lead * 0.14f;
                    shade.Color = new Color(0.22f, 0.10f, 0.04f, 0.9f);
                    shade.SortingOrder = sortingOrder + 2;
                    shade.SetPoints(ringPts);
                }

                AddValueLabel(go, pts, holeLists, r.value);
                _regionObjects.Add(go);
                _meshes.Add(mesh);
                _outlines.Add(rings);
                _outerRings.Add(outerRing);
                _colorIds.Add(r.colorId);
                _painted.Add(false);
                ApplyColor(i);
            }
            BuildDecor(p, unit);
        }

        // Hình trang trí kín: vòng cùng chiều với vòng lớn nhất là thân, vòng ngược chiều là lỗ trong thân nhỏ nhất chứa nó
        private void BuildDecor(PictureProperty p, int unit)
        {
            if (p.decor == null || p.decor.Count == 0) return;
            var rings = new List<List<Vector2>>();
            foreach (var d in p.decor)
            {
                if (d == null || d.Length < 6) continue;
                var pts = new List<Vector2>(d.Length / 2);
                for (var k = 0; k < d.Length; k += 2) pts.Add((new Vector2(d[k], d[k + 1]) / unit - _offset) * _scale);
                rings.Add(pts);
            }
            var areas = rings.ConvertAll(PolygonTriangulator.SignedArea);
            var best = 0;
            for (var i = 1; i < rings.Count; i++) if (Mathf.Abs(areas[i]) > Mathf.Abs(areas[best])) best = i;
            if (rings.Count == 0) return;
            var sign = Mathf.Sign(areas[best]);
            var holes = new Dictionary<int, List<IReadOnlyList<Vector2>>>();
            for (var i = 0; i < rings.Count; i++)
            {
                if (Mathf.Sign(areas[i]) == sign) continue;
                var owner = -1;
                for (var o = 0; o < rings.Count; o++)
                    if (Mathf.Sign(areas[o]) == sign && (owner < 0 || Mathf.Abs(areas[o]) < Mathf.Abs(areas[owner])) && Contains(rings[o], rings[i][0])) owner = o;
                if (owner < 0) continue;
                if (!holes.TryGetValue(owner, out var list)) holes[owner] = list = new List<IReadOnlyList<Vector2>>();
                list.Add(rings[i]);
            }
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i < rings.Count; i++)
            {
                if (Mathf.Sign(areas[i]) != sign) continue;
                holes.TryGetValue(i, out var hs);
                var t = PolygonTriangulator.Triangulate(rings[i], hs, out var vs);
                var at = verts.Count;
                foreach (var v in vs) verts.Add(v);
                foreach (var idx in t) tris.Add(at + idx);
            }
            if (tris.Count == 0) return;
            _decorMesh = new Mesh { name = "DecorShapes" };
            _decorMesh.SetVertices(verts);
            _decorMesh.SetColors(verts.ConvertAll(_ => FlatLeadColor));
            _decorMesh.SetTriangles(tris, 0);
            _decorShape = new GameObject("DecorShapes");
            _decorShape.transform.SetParent(transform, false);
            _decorShape.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            _decorShape.AddComponent<MeshFilter>().sharedMesh = _decorMesh;
            var mr = _decorShape.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _shadowMaterial;
            mr.sortingOrder = sortingOrder + 1;
        }

        private static bool Contains(List<Vector2> ring, Vector2 q)
        {
            var inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                if ((ring[i].y > q.y) != (ring[j].y > q.y) && q.x < (ring[j].x - ring[i].x) * (q.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x) inside = !inside;
            return inside;
        }

        // Tấm nền chì phủ cả khung tranh, nằm sau mọi mảnh: chỗ khe giữa các mảnh lộ ra thành nét chì
        private void BuildLeadBacking(PictureProperty p)
        {
            var half = _offset * _scale;
            _leadMesh = new Mesh { name = "LeadBacking" };
            _leadMesh.SetVertices(new[] { new Vector3(-half.x, -half.y), new Vector3(-half.x, half.y), new Vector3(half.x, half.y), new Vector3(half.x, -half.y) });
            _leadMesh.SetColors(new[] { FlatLeadColor, FlatLeadColor, FlatLeadColor, FlatLeadColor });
            _leadMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _leadBacking = new GameObject("LeadBacking");
            _leadBacking.transform.SetParent(transform, false);
            _leadBacking.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            _leadBacking.AddComponent<MeshFilter>().sharedMesh = _leadMesh;
            var mr = _leadBacking.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _shadowMaterial;
            mr.sortingOrder = sortingOrder - 2;
        }

        private readonly Dictionary<int, (Vector2 pos, float radius)> _labelCache = new(); // vị trí số theo hình mảnh: dựng lại tranh không tính lại mảnh không đổi

        // Số giá trị cát ở tâm đường tròn nội tiếp lớn nhất của mảnh; cỡ chữ tự co cho vừa
        private void AddValueLabel(GameObject go, List<Vector2> pts, List<IReadOnlyList<Vector2>> holes, int value)
        {
            var key = new System.HashCode();
            foreach (var v in pts) key.Add(v);
            if (holes != null) foreach (var h in holes) { key.Add(h.Count); foreach (var v in h) key.Add(v); }
            var hash = key.ToHashCode();
            if (!_labelCache.TryGetValue(hash, out var hit))
            {
                if (_labelCache.Count > 4096) _labelCache.Clear();
                hit = (PolyLabel.Find(pts, holes, out var r), r);
                _labelCache[hash] = hit;
            }
            var (c, radius) = hit;
            var t = new GameObject("Value").AddComponent<TextMeshPro>();
            t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(c.x, c.y, -0.02f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontSize = 10f;
            t.rectTransform.sizeDelta = new Vector2(100f, 100f);
            t.text = value.ToString();
            t.ForceMeshUpdate();
            var box = new Vector2(radius * 1.85f, radius * 1.2f); // hình chữ nhật nội tiếp trong đường tròn nội tiếp của mảnh
            var k = Mathf.Min(box.x / Mathf.Max(0.0001f, t.preferredWidth), box.y / Mathf.Max(0.0001f, t.preferredHeight), viewHeight * ValueMaxHeight / Mathf.Max(0.0001f, t.preferredHeight)); // mảnh to không có số quá lớn
            k = Mathf.Max(k, viewHeight * ValueMinHeight / Mathf.Max(0.0001f, t.preferredHeight)); // mảnh nhỏ: số vẫn đủ lớn để đọc, được tràn ra viền
            t.fontSize = 10f * k;
            t.rectTransform.sizeDelta = box;
            t.sortingOrder = sortingOrder + 3;
            t.gameObject.SetActive(_showValues);
            _values.Add(t);
        }

        // Đổi chữ trên mảnh (ví dụ "?" cho mảnh ẩn màu); ink != null thì chữ lấy màu đó, viền chữ ngược sáng tối
        public void SetValueText(int id, string text, Color? ink = null)
        {
            if (id < 0 || id >= _values.Count || !_values[id]) return;
            _values[id].text = text;
            if (!ink.HasValue) return;
            var c = ink.Value;
            _values[id].color = c;
            _values[id].outlineColor = c.grayscale > 0.6f ? new Color32(15, 23, 41, 255) : new Color32(250, 251, 252, 255);
        }

        public void SetShowValues(bool on)
        {
            _showValues = on;
            foreach (var t in _values) if (t) t.gameObject.SetActive(on);
        }

        // Các điểm neo đang đặt (world): điểm đang chọn là chấm đặc, còn lại là vòng; null = ẩn hết
        public void SetAnchorMarkers(IReadOnlyList<Vector3> world, int selected = -1)
        {
            var n = world?.Count ?? 0;
            while (_anchors.Count < n)
            {
                var go = new GameObject("Anchor");
                go.transform.SetParent(transform, false);
                var d = go.AddComponent<Disc>();
                d.Color = Color.white;
                d.SortingOrder = sortingOrder + 6;
                _anchors.Add(d);
            }
            for (var i = 0; i < _anchors.Count; i++)
            {
                _anchors[i].gameObject.SetActive(i < n);
                if (i >= n) continue;
                _anchors[i].Type = i == selected ? DiscType.Disc : DiscType.Ring;
                _anchors[i].Radius = outlineWidth * 1.1f * _zs;
                _anchors[i].Thickness = outlineWidth * 0.5f * _zs;
                _anchors[i].transform.position = new Vector3(world[i].x, world[i].y, transform.position.z - 0.025f);
            }
        }

        // Tay cầm của điểm đang chọn: cặp (điểm neo, đầu tay cầm) nối tiếp nhau; null = ẩn
        public void SetHandles(IReadOnlyList<Vector3> pairs)
        {
            var n = (pairs?.Count ?? 0) / 2;
            while (_handleLines.Count < n)
            {
                var go = new GameObject("Handle");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Polyline>();
                l.Closed = false;
                l.Color = PreviewColor;
                l.Thickness = outlineWidth * 0.3f * _zs;
                l.SortingOrder = sortingOrder + 5;
                _handleLines.Add(l);
                var lc = new GameObject("HandleCase").AddComponent<Polyline>(); // viền đen dưới thanh cong: thấy rõ trên mọi màu mảnh
                lc.transform.SetParent(go.transform, false);
                lc.transform.localPosition = new Vector3(0f, 0f, 0.001f);
                lc.Closed = false;
                lc.Color = CaseColor;
                lc.Thickness = outlineWidth * 0.9f * _zs;
                lc.SortingOrder = sortingOrder + 4;
                _handleCases.Add(lc);
                var dot = new GameObject("HandleDot").AddComponent<Disc>();
                dot.transform.SetParent(go.transform, false);
                dot.Color = PreviewColor;
                dot.SortingOrder = sortingOrder + 6;
                _handleDots.Add(dot);
                var dc = new GameObject("HandleDotCase").AddComponent<Disc>();
                dc.transform.SetParent(go.transform, false);
                dc.Color = CaseColor;
                dc.SortingOrder = sortingOrder + 5;
                _handleDotCases.Add(dc);
            }
            for (var i = 0; i < _handleLines.Count; i++)
            {
                _handleLines[i].gameObject.SetActive(i < n);
                if (i >= n) continue;
                Vector2 a = transform.InverseTransformPoint(pairs[i * 2]), b = transform.InverseTransformPoint(pairs[i * 2 + 1]);
                var seg = new List<Vector2> { a, b };
                _handleLines[i].SetPoints(seg);
                _handleCases[i].SetPoints(seg);
                _handleDots[i].Radius = outlineWidth * 0.8f * _zs;
                _handleDots[i].transform.localPosition = new Vector3(b.x, b.y, -0.03f);
                _handleDotCases[i].Radius = outlineWidth * 1.15f * _zs;
                _handleDotCases[i].transform.localPosition = new Vector3(b.x, b.y, -0.029f);
            }
        }

        // Chấm hút (điểm đặt nét sẽ rơi vào); show = false thì ẩn
        public void SetSnapMarker(bool show, Vector3 world = default)
        {
            if (!_snap)
            {
                if (!show) return;
                var go = new GameObject("SnapMarker");
                go.transform.SetParent(transform, false);
                _snap = go.AddComponent<Disc>();
                _snap.Color = PreviewColor;
                _snap.SortingOrder = sortingOrder + 5;
            }
            _snap.gameObject.SetActive(show);
            if (!show) return;
            _snap.Radius = outlineWidth * 0.9f * _zs;
            _snap.transform.position = new Vector3(world.x, world.y, transform.position.z - 0.02f);
        }

        public Vector2 WorldToPicture(Vector3 world) => (Vector2)transform.InverseTransformPoint(world) / _scale + _offset;

        public Vector3 PictureToWorld(Vector2 pic) => transform.TransformPoint((pic - _offset) * _scale);

        public void SetPainted(int id, bool painted)
        {
            if (id < 0 || id >= _meshes.Count) return;
            _painted[id] = painted;
            ApplyColor(id);
        }

        public void SetAllPainted(bool painted)
        {
            for (var i = 0; i < _meshes.Count; i++) SetPainted(i, painted);
        }

        public void SetGlass(bool on)
        {
            _glassOn = on;
            if (!_material) return;
            _material.mainTexture = on ? _glass : Texture2D.whiteTexture;
            if (_glassShader) _material.SetTexture("_Facets", _facets);
            if (_glassShader) _material.SetFloat("_Flat", on ? 0f : 1f);
            ApplyLeadStyle();
        }

        // Tắt kính = nét đen đậm đều (dễ đọc hình thể); bật kính = thanh chì đồng có khối
        private void ApplyLeadStyle()
        {
            foreach (var rings in _outlines)
                foreach (var o in rings)
                {
                    if (!o) continue;
                    foreach (Transform c in o.transform) c.gameObject.SetActive(false);
                    Style(o, false);
                }
        }

        public void SetHighlight(int id)
        {
            if (id >= _outlines.Count) id = -1;
            if (id == _highlight) return;
            if (_highlight >= 0) foreach (var o in _outlines[_highlight]) Style(o, _selection.Contains(_highlight), selected: _selection.Contains(_highlight));
            _highlight = id;
            if (_highlight >= 0) foreach (var o in _outlines[_highlight]) Style(o, true, selected: _selection.Contains(_highlight));
        }

        // Các mảnh đang chọn (nhiều mảnh) viền sáng như khi rê chuột
        public void SetSelection(IReadOnlyCollection<int> ids)
        {
            foreach (var i in _selection) if (i < _outlines.Count) foreach (var o in _outlines[i]) Style(o, i == _highlight);
            _selection.Clear();
            if (ids != null) foreach (var i in ids) if (i >= 0 && i < _outlines.Count) _selection.Add(i);
            foreach (var i in _selection) foreach (var o in _outlines[i]) Style(o, true, selected: true);
        }

        // Ẩn đoạn viền vòng ngoài từ đỉnh lo tới hi (theo chiều tăng chỉ số) của mảnh: chỉ vẽ phần còn lại của vòng
        public void HideOutlineRun(int region, int lo, int hi)
        {
            if (region < 0 || region >= _outerRings.Count || _outerRings[region] == null) return;
            var ring = _outerRings[region];
            var n = ring.Count;
            if (lo < 0 || hi < 0 || lo >= n || hi >= n) return;
            var rest = new List<PolylinePoint> { ring[hi] };
            for (var i = hi; i != lo;)
            {
                i = (i + 1) % n;
                rest.Add(ring[i]);
            }
            SetOuterRing(region, rest, false);
        }

        // Vẽ lại đủ vòng viền ngoài của mảnh
        public void ShowOutline(int region)
        {
            if (region < 0 || region >= _outerRings.Count || _outerRings[region] == null) return;
            SetOuterRing(region, _outerRings[region], true);
        }

        private void SetOuterRing(int region, List<PolylinePoint> pts, bool closed)
        {
            var outline = _outlines[region][0];
            if (!outline) return;
            foreach (var pl in outline.GetComponentsInChildren<Polyline>(true))
            {
                pl.Closed = closed;
                pl.SetPoints(pts);
            }
        }

        // srcRect: phần ảnh (pixel) được dùng; place: vị trí ảnh trong khung (0..1, y = 0 dưới); frameAspect = W/H của khung
        public void SetSource(Texture2D tex, bool visible, float alpha, RectInt srcRect, Rect place, float frameAspect)
        {
            if (!tex || !visible)
            {
                if (_source) _source.gameObject.SetActive(false);
                if (_frame) _frame.gameObject.SetActive(false);
                return;
            }
            if (!_source)
            {
                var go = new GameObject("Source");
                go.transform.SetParent(transform, false);
                _source = go.AddComponent<SpriteRenderer>();
                _source.sortingOrder = sortingOrder + 3;
                _frame = new GameObject("Frame").AddComponent<Polyline>();
                _frame.transform.SetParent(transform, false);
                _frame.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                _frame.Closed = true;
                _frame.Thickness = outlineWidth * 1.5f;
                _frame.Color = frameColor;
                _frame.SortingOrder = sortingOrder + 5;
            }
            if (!_source.sprite || _source.sprite.texture != tex || _sourceRect != srcRect)
            {
                _sourceRect = srcRect;
                if (_source.sprite) Destroy(_source.sprite);
                _source.sprite = Sprite.Create(tex, new Rect(srcRect.x, srcRect.y, srcRect.width, srcRect.height), new Vector2(0.5f, 0.5f), srcRect.height);
            }
            var fw = viewHeight * frameAspect;
            var size = new Vector2(place.width * fw, place.height * viewHeight); // sprite 1 đơn vị/pixel-cao → co theo scale
            _source.transform.localPosition = new Vector3((place.center.x - 0.5f) * fw, (place.center.y - 0.5f) * viewHeight, -0.02f);
            _source.transform.localScale = new Vector3(size.x / srcRect.width * srcRect.height, size.y, 1f);
            _frameSize = new Vector2(fw, viewHeight);
            var e = _frameSize * 0.5f;
            _frame.SetPoints(new List<Vector2> { new(-e.x, -e.y), new(e.x, -e.y), new(e.x, e.y), new(-e.x, e.y) });
            _source.gameObject.SetActive(true);
            _frame.gameObject.SetActive(true);
            _source.color = new Color(1f, 1f, 1f, alpha);
        }

        // Đường cắt đang kéo (toạ độ world); show = false hoặc < 2 điểm thì ẩn
        public void SetCutPreview(bool show, IReadOnlyList<Vector3> worldPoints = null)
        {
            if (!_cut)
            {
                if (!show) return;
                var go = new GameObject("CutPreview");
                go.transform.SetParent(transform, false);
                _cut = go.AddComponent<Polyline>();
                _cut.Closed = false; // mặc định của Polyline là khép kín: sẽ vẽ thêm đoạn thẳng từ điểm cuối về điểm đầu
                _cut.Thickness = outlineWidth * 1.5f * _zs;
                _cut.Color = PreviewColor;
                _cut.Joins = PolylineJoins.Round;
                _cut.SortingOrder = sortingOrder + 5;
                var back = new GameObject("CutCase"); // viền đen dưới đường xem trước: thấy rõ trên mọi màu mảnh
                back.transform.SetParent(go.transform, false);
                back.transform.localPosition = new Vector3(0f, 0f, 0.001f);
                _cutCase = back.AddComponent<Polyline>();
                _cutCase.Closed = false;
                _cutCase.Thickness = outlineWidth * 2.6f * _zs;
                _cutCase.Color = CaseColor;
                _cutCase.Joins = PolylineJoins.Round;
                _cutCase.SortingOrder = sortingOrder + 4;
                var band = new GameObject("CutBand");
                band.transform.SetParent(go.transform, false);
                band.transform.localPosition = new Vector3(0f, 0f, 0.002f);
                _cutBand = band.AddComponent<Polyline>();
                _cutBand.Closed = false;
                _cutBand.Color = new Color(0.04f, 0.04f, 0.04f, 0.55f); // tối như rãnh thật
                _cutBand.Joins = PolylineJoins.Round;
                _cutBand.SortingOrder = sortingOrder + 3;
            }
            _cut.gameObject.SetActive(show && worldPoints != null && worldPoints.Count >= 2);
            if (!_cut.gameObject.activeSelf) return;
            var pts = new List<Vector2>(worldPoints.Count);
            foreach (var w in worldPoints) pts.Add(transform.InverseTransformPoint(w));
            _cut.SetPoints(pts);
            _cutCase.SetPoints(pts);
            _cutBand.SetPoints(pts);
            ApplyBand();
        }

        // Bề rộng khe xem trước theo ô lưới; 0 = ẩn
        public void SetCutBand(float cells)
        {
            _bandCells = cells;
            ApplyBand();
        }

        private void ApplyBand()
        {
            if (!_cutBand) return;
            _cutBand.gameObject.SetActive(_bandCells > 0f);
            if (_bandCells > 0f) _cutBand.Thickness = _bandCells * _scale;
        }

        public void Clear()
        {
            foreach (var go in _regionObjects) Destroy(go);
            if (_leadBacking) Destroy(_leadBacking);
            if (_leadMesh) Destroy(_leadMesh);
            _values.Clear();
            if (_decorShape) Destroy(_decorShape);
            if (_decorMesh) Destroy(_decorMesh);
            foreach (var m in _meshes) Destroy(m);
            foreach (var m in _shadowMeshes) Destroy(m);
            _shadowMeshes.Clear();
            _regionObjects.Clear();
            _meshes.Clear();
            _outlines.Clear();
            _outerRings.Clear();
            _colorIds.Clear();
            _painted.Clear();
            if (_overlay) _overlay.gameObject.SetActive(false);
            _highlight = -1;
            _selection.Clear();
        }

        private void EnsureMaterial()
        {
            if (_material) return;
            // Nạp texture đã bake sẵn (Resources/PictureGlass); chưa bake thì tính bằng code, chậm vài giây ở lần đầu
            _glass = Resources.Load<Texture2D>("PictureGlass/glass");
            _facets = Resources.Load<Texture2D>("PictureGlass/facets");
            _ownsTextures = !_glass || !_facets;
            if (_ownsTextures)
            {
                _glass = GlassTexture.Create();
                _facets = GlassTexture.CreateFacets();
            }
            var shader = Shader.Find("Falcon/GlassPiece");
            _glassShader = shader != null;
            _material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            _shadowMaterial = new Material(Shader.Find("Sprites/Default"));
            SetGlass(_glassOn);
        }

        // Độ dày viền theo từng đỉnh (nhân với outlineWidth); không đo được hoặc lệch số đỉnh thì dày mặc định
        private List<PolylinePoint> Ring(IReadOnlyList<Vector2> pts, int[] widths, int unit)
        {
            var res = new List<PolylinePoint>(pts.Count);
            var ok = widths != null && widths.Length == pts.Count;
            for (var i = 0; i < pts.Count; i++)
            {
                var m = ok && widths[i] > 0 ? Mathf.Clamp(widths[i] / (float)unit * _scale / outlineWidth, MinLead, MaxLead) : 1f;
                res.Add(new PolylinePoint(pts[i], Color.white, m));
            }
            return res;
        }

        private void Style(Polyline l, bool hot, bool selected = false)
        {
            l.Color = selected ? selectionColor : hot ? highlightColor : _glassOn ? outlineColor : FlatLeadColor;
            l.Thickness = hot ? outlineWidth * 0.75f * _zs : (_glassOn ? outlineWidth : outlineWidth * FlatLeadScale) * _lead;
            l.SortingOrder = sortingOrder + (hot ? 2 : 1);
        }

        // Lớp phủ đỏ (w×h, y = 0 ở dưới) đè đúng lên tranh; null hoặc tắt = ẩn
        public void SetOverlay(Color32[] pixels, int w, int h, bool visible)
        {
            if (pixels == null || !visible)
            {
                if (_overlay) _overlay.gameObject.SetActive(false);
                return;
            }
            if (!_overlay)
            {
                var go = new GameObject("CheckOverlay");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.03f);
                _overlay = go.AddComponent<SpriteRenderer>();
                _overlay.sortingOrder = sortingOrder + 4;
            }
            if (_overlayTex) Destroy(_overlayTex);
            if (_overlay.sprite) Destroy(_overlay.sprite);
            _overlayTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _overlayTex.SetPixels32(pixels);
            _overlayTex.Apply();
            _overlay.sprite = Sprite.Create(_overlayTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h / viewHeight);
            _overlay.gameObject.SetActive(true);
        }

        private void ApplyColor(int id)
        {
            Color c = ColorPalette.Get(_colorIds[id]);
            if (!_painted[id])
            {
                var g = c.grayscale * 0.35f + 0.35f; // xám nhưng vẫn phân biệt nhẹ giữa các mảnh
                c = new Color(g, g, g, 1f);
            }
            if (id < _values.Count && _values[id])
            {
                var darkText = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f > 0.6f;
                _values[id].color = darkText ? new Color(0.06f, 0.09f, 0.16f) : new Color(0.97f, 0.98f, 0.99f);
                _values[id].outlineColor = darkText ? new Color32(250, 251, 252, 255) : new Color32(15, 23, 41, 255); // viền ngược màu chữ: số tràn ra khe chì hay mảnh bên cạnh vẫn đọc được
                _values[id].outlineWidth = ValueOutline;
            }
            var cols = new Color[_meshes[id].vertexCount];
            for (var i = 0; i < cols.Length; i++) cols[i] = c;
            _meshes[id].colors = cols;
        }

        private void OnDestroy()
        {
            Clear();
            if (_material) Destroy(_material);
            if (_shadowMaterial) Destroy(_shadowMaterial);
            if (_ownsTextures)
            {
                if (_glass) Destroy(_glass);
                if (_facets) Destroy(_facets);
            }
            if (_source && _source.sprite) Destroy(_source.sprite);
            if (_overlay && _overlay.sprite) Destroy(_overlay.sprite);
            if (_overlayTex) Destroy(_overlayTex);
        }
    }
}
