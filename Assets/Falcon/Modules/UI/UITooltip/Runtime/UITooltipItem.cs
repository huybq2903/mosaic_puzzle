/*
 * Author: Bui Quang Huy
 * Email: huybq@falcongames.com
 * Company: Falcon Games
 * Date: 2025-10-15
 */

using System.Collections.Generic;
using DG.Tweening;
using Falcon.Helpers.EventBus;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Pool;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Falcon.Modules.UI.UITooltip.Runtime
{
    public class UITooltipItem : MonoBehaviour
    {
        [SerializeField] private Transform rewardPrefab, plusPrefab, grid;
        [SerializeField] private int maxItemsOneRow;
        [SerializeField] private SpriteAtlas atlas, atlasReward;
        [SerializeField] private TMP_Text txtContent;
        [SerializeField] private float maxTextWidth = 560f;
        [SerializeField] private float screenMargin = 40f;

        private List<Transform> _lsRewards, _lsPluses;
        private ObjectPool<Transform> _poolReward, _poolPlus;
        private GridLayoutGroup _gridLayoutGroup;
        private Image _imgBG;
        private LayoutGroup _layoutGroup;
        private LayoutElement _txtLayout;
        private LayoutElement _gridLayout;
        private bool _plus = true;
        private Rect _rewardBounds, _plusBounds;

        private void Awake()
        {
            _lsRewards = new List<Transform>();
            _lsPluses = new List<Transform>();
            _poolReward = new ObjectPool<Transform>(
                createFunc: () => Instantiate(rewardPrefab, grid),
                actionOnGet: trans => trans.gameObject.SetActive(true),
                actionOnRelease: trans => trans.gameObject.SetActive(false),
                actionOnDestroy: Destroy);
            _poolPlus = new ObjectPool<Transform>(
                createFunc: () => Instantiate(plusPrefab, grid),
                actionOnGet: trans => trans.gameObject.SetActive(true),
                actionOnRelease: trans => trans.gameObject.SetActive(false),
                actionOnDestroy: Destroy);

            // Grid chỉ giữ padding/spacing/cellSize để chỉnh; nó xếp theo ô nên không biết icon bị scale, tự xếp ở LayoutItems
            _gridLayoutGroup = grid.GetComponent<GridLayoutGroup>();
            _gridLayoutGroup.enabled = false;
            if (!grid.TryGetComponent(out _gridLayout)) _gridLayout = grid.gameObject.AddComponent<LayoutElement>();
            _imgBG = GetComponent<Image>();
            _layoutGroup = GetComponent<LayoutGroup>();
            if (!txtContent.TryGetComponent(out _txtLayout)) _txtLayout = txtContent.gameObject.AddComponent<LayoutElement>();
        }

        private void Active((string, int)[] data) => Active((data, true));

        // bool = có chèn dấu "+" giữa các món
        private void Active(((string, int)[] rewards, bool plus) args)
        {
            var data = args.rewards;
            _plus = args.plus;
            DeActive();
            _txtLayout.preferredWidth = -1f;
            _txtLayout.preferredHeight = -1f;
            var rewardCount = data.Length;
            if (rewardCount <= 0)
            {
                grid.gameObject.SetActive(false);
                txtContent.SetText(string.Empty);
                return;
            }

            grid.gameObject.SetActive(true);
            MeasureSlots();
            var itemsPerRow = CalculateItemsPerRow(rewardCount);

            var count = 0;
            foreach (var (nameIcon, amount) in data)
            {
                if (_plus && count % itemsPerRow > 0)
                {
                    var plus = _poolPlus.Get();
                    plus.SetAsLastSibling();
                    _lsPluses.Add(plus);
                }

                count++;
                var rewardItem = _poolReward.Get();
                var icon = rewardItem.GetComponentInChildren<Image>();
                var txtAmount = rewardItem.GetComponentInChildren<TMP_Text>();

                rewardItem.SetAsLastSibling();
                icon.sprite = atlasReward.GetSprite(nameIcon);
                txtAmount.text = GameRequest<(string, int), string>.Request("falcon.modules.format_quantity_reward", (nameIcon, amount));

                _lsRewards.Add(rewardItem);
            }

            LayoutItems(itemsPerRow);
            txtContent.SetText(string.Empty);
            transform.DOScale(1, 0.2f).From(0).SetEase(Ease.OutBack);
            Canvas.ForceUpdateCanvases();
            SetDirection();
        }

        private int CalculateItemsPerRow(int rewardCount)
        {
            var maxByConfig = maxItemsOneRow > 0 ? maxItemsOneRow : rewardCount;
            var maxCandidate = Mathf.Clamp(maxByConfig, 1, rewardCount);

            var canvas = GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            if (canvasRect == null || _gridLayoutGroup == null)
                return maxCandidate;

            var canvasRectArea = canvasRect.rect;
            var anchor = (Vector2)canvasRect.InverseTransformPoint(transform.position);

            var spaceLeft = Mathf.Max(0f, anchor.x - canvasRectArea.xMin);
            var spaceRight = Mathf.Max(0f, canvasRectArea.xMax - anchor.x);
            var spaceBottom = Mathf.Max(0f, anchor.y - canvasRectArea.yMin);
            var spaceTop = Mathf.Max(0f, canvasRectArea.yMax - anchor.y);

            var availableWidth = transform.localPosition.x > 0f ? spaceLeft : spaceRight;
            var availableHeight = transform.localPosition.y > 0f ? spaceBottom : spaceTop;
            availableWidth = Mathf.Max(0f, availableWidth);
            availableHeight = Mathf.Max(0f, availableHeight);

            var best = 1;
            for (var itemsPerRow = 1; itemsPerRow <= maxCandidate; itemsPerRow++)
            {
                if (FitsCanvas(rewardCount, itemsPerRow, availableWidth, availableHeight))
                    best = itemsPerRow;
            }

            return Mathf.Max(1, best);
        }

        private bool FitsCanvas(int rewardCount, int itemsPerRow, float availableWidth, float availableHeight)
        {
            var rows = Mathf.CeilToInt((float)rewardCount / itemsPerRow);
            var padding = _gridLayoutGroup.padding;

            var requiredWidth = padding.horizontal + RowWidth(itemsPerRow);
            var requiredHeight = padding.vertical + ContentHeight(rows);

            return requiredWidth <= availableWidth && requiredHeight <= availableHeight;
        }

        private Vector2 RewardSlot => Vector2.Max(_gridLayoutGroup.cellSize, _rewardBounds.size);

        private float RowWidth(int items)
        {
            var gaps = Mathf.Max(0, items - 1);
            var spacing = _gridLayoutGroup.spacing.x;
            return items * RewardSlot.x + (_plus ? gaps * (_plusBounds.width + 2f * spacing) : gaps * spacing);
        }

        private float ContentHeight(int rows) => rows * RewardSlot.y + Mathf.Max(0, rows - 1) * _gridLayoutGroup.spacing.y;

        // Đo trên bản đang bật: template P_Reward tắt nên TMP chưa tính được cỡ chữ "+"
        private void MeasureSlots()
        {
            var reward = _poolReward.Get();
            _rewardBounds = VisualBounds((RectTransform)reward);
            _poolReward.Release(reward);

            var plus = _poolPlus.Get();
            _plusBounds = VisualBounds((RectTransform)plus);
            _poolPlus.Release(plus);
        }

        // Hình thật trong toạ độ của cha, gồm mọi con và scale; rect 0 có TMP thì lấy cỡ chữ
        private static Rect VisualBounds(RectTransform root)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var corners = new Vector3[4];

            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                var rect = rt.rect;
                if (rect.width <= 0f && rt.TryGetComponent<TMP_Text>(out var text))
                {
                    var size = text.GetPreferredValues();
                    rect = new Rect(-size * rt.pivot, size);
                }

                if (rect.width <= 0f || rect.height <= 0f) continue;

                corners[0] = rect.min;
                corners[1] = new Vector2(rect.xMin, rect.yMax);
                corners[2] = rect.max;
                corners[3] = new Vector2(rect.xMax, rect.yMin);
                foreach (var corner in corners)
                {
                    var p = (Vector2)root.InverseTransformPoint(rt.TransformPoint(corner));
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }

            if (min.x > max.x) return new Rect(0f, 0f, 0f, 0f);
            var scale = (Vector2)root.localScale;
            return Rect.MinMaxRect(min.x * scale.x, min.y * scale.y, max.x * scale.x, max.y * scale.y);
        }

        // Xếp theo hình thật nên scale icon bao nhiêu cũng giữ đúng spacing giữa quà và dấu "+"
        private void LayoutItems(int itemsPerRow)
        {
            var spacing = _gridLayoutGroup.spacing;
            var padding = _gridLayoutGroup.padding;
            var slot = RewardSlot;
            var rows = Mathf.CeilToInt((float)_lsRewards.Count / itemsPerRow);

            var width = 0f;
            for (var r = 0; r < rows; r++) width = Mathf.Max(width, RowWidth(Mathf.Min(itemsPerRow, _lsRewards.Count - r * itemsPerRow)));
            var height = ContentHeight(rows);

            _gridLayout.minWidth = _gridLayout.preferredWidth = width + padding.horizontal;
            _gridLayout.minHeight = _gridLayout.preferredHeight = height + padding.vertical;

            // Tâm khối nội dung lệch theo padding không đều hai bên
            var center = new Vector2(padding.left - padding.right, padding.bottom - padding.top) * 0.5f;
            var plusIndex = 0;
            for (var r = 0; r < rows; r++)
            {
                var first = r * itemsPerRow;
                var count = Mathf.Min(itemsPerRow, _lsRewards.Count - first);
                var rowY = center.y + height * 0.5f - slot.y * 0.5f - r * (slot.y + spacing.y);
                var x = center.x - RowWidth(count) * 0.5f;

                for (var i = 0; i < count; i++)
                {
                    if (i > 0 && _plus && plusIndex < _lsPluses.Count)
                    {
                        x += spacing.x;
                        Place(_lsPluses[plusIndex++], _plusBounds, x + _plusBounds.width * 0.5f, rowY);
                        x += _plusBounds.width + spacing.x;
                    }
                    else if (i > 0) x += spacing.x;

                    Place(_lsRewards[first + i], _rewardBounds, x + slot.x * 0.5f, rowY);
                    x += slot.x;
                }
            }
        }

        // Neo vào tâm grid: neo góc thì grid đổi cỡ sau đó sẽ kéo lệch vị trí vừa đặt
        private static void Place(Transform item, Rect bounds, float centerX, float centerY)
        {
            var rt = (RectTransform)item;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(centerX - bounds.center.x, centerY - bounds.center.y);
        }

        private void Active(string content)
        {
            grid.gameObject.SetActive(false);
            LimitTextWidth(content);
            txtContent.SetText(content);
            transform.DOScale(1, 0.2f).From(0).SetEase(Ease.OutBack);
            Canvas.ForceUpdateCanvases();
            SetDirection();
        }

        // ContentSizeFitter lấy rộng theo nguyên dòng chữ nên phải tự giới hạn thì TMP mới xuống dòng
        private void LimitTextWidth(string content)
        {
            var limit = Mathf.Max(200f, Mathf.Min(maxTextWidth, AvailableTextWidth()));
            var width = Mathf.Ceil(Mathf.Min(txtContent.GetPreferredValues(content).x, limit));
            _txtLayout.preferredWidth = width;
            _txtLayout.preferredHeight = txtContent.GetPreferredValues(content, width, Mathf.Infinity).y;
        }

        private float AvailableTextWidth()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return maxTextWidth;

            var canvasRect = (RectTransform)canvas.transform;
            var area = canvasRect.rect;
            var anchorX = canvasRect.InverseTransformPoint(transform.position).x;
            var space = transform.localPosition.x > 0f ? anchorX - area.xMin : area.xMax - anchorX;
            return space - _layoutGroup.padding.horizontal - screenMargin;
        }

        private void SetDirection()
        {
            var nameBgSprite = transform.localPosition switch
            {
                { x: > 0, y: <= 0 } => "BG_Tooltip_UpLeft",
                { x: > 0, y: > 0 } => "BG_Tooltip_DownLeft",
                { x: <= 0, y: > 0 } => "BG_Tooltip_DownRight",
                { x: <= 0, y: <= 0 } => "BG_Tooltip_UpRight",
                _ => ""
            };
            var sizeRect = _imgBG.rectTransform.sizeDelta;
            var buttonTrigger = EventSystem.current.currentSelectedGameObject.GetComponent<RectTransform>();
            float deltaX, deltaY;

            _imgBG.sprite = atlas.GetSprite(nameBgSprite);
            switch (transform.localPosition)
            {
                case { x: > 0, y: <= 0 }:
                    deltaX = _imgBG.sprite.rect.width - _imgBG.sprite.pivot.x;
                    _imgBG.rectTransform.pivot = new Vector2(1 - deltaX / sizeRect.x, 0);
                    break;
                case { x: > 0, y: > 0 }:
                    deltaX = _imgBG.sprite.rect.width - _imgBG.sprite.pivot.x;
                    _imgBG.rectTransform.pivot = new Vector2(1 - deltaX / sizeRect.x, 1);
                    break;
                case { x: <= 0, y: > 0 }:
                    deltaX = _imgBG.sprite.pivot.x;
                    _imgBG.rectTransform.pivot = new Vector2(deltaX / sizeRect.x, 1);
                    break;
                case { x: <= 0, y: <= 0 }:
                    deltaX = _imgBG.sprite.pivot.x;
                    _imgBG.rectTransform.pivot = new Vector2(deltaX / sizeRect.x, 0);
                    break;
                default:
                    _imgBG.rectTransform.pivot = new Vector2(0, 0);
                    break;
            }
        }

        private void DeActive()
        {
            foreach (var item in _lsRewards) _poolReward.Release(item);
            foreach (var item in _lsPluses) _poolPlus.Release(item);
            _lsRewards.Clear();
            _lsPluses.Clear();
        }
    }
}



