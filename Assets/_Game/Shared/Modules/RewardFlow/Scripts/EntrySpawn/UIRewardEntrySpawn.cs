/*
 * Author: Bui Quang Huy
 * Email: huybq@falcongames.com
 * Company: Falcon Games
 * Date: 2026-02-06
 */

using Cysharp.Threading.Tasks;
using DG.Tweening;
using Falcon.Helpers.EventBus;
using Falcon.Shared.Common;
using Falcon.Shared.PoolManager;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Falcon.Shared.RewardFlow
{
    public class UIRewardEntrySpawn : RewardEntry<RewardSpawnData>
    {
        public UIRewardItem itemPrf;
        public RectTransform viewport;

        private FObjectPool<UIRewardItem> _poolItem;
        private FConfigRuntimeSO _config;
        private StaggeredDiamondLayout _layout;
        private readonly Dictionary<string, UIResource> _resources = new();

        private void Awake()
        {
            _config = Resources.Load<FConfigRuntimeSO>("ConfigRuntimeEntrySpawn");
            _poolItem = new FObjectPool<UIRewardItem>(itemPrf, viewport);
            _layout = viewport.GetComponent<StaggeredDiamondLayout>();
        }

        public override void Init()
        {
            base.Init();
            var parent = GameRequest<Transform>.Request($"falcon.reward.entry_parent_{Data.where}");
            if (parent)
            {
                transform.SetParent(parent);
            }

            _resources.Clear();

            foreach (var dataReward in Data.rewards)
            {
                var resource = UIResourceManager.GetResource(dataReward.name, Data.where);
                if (resource)
                {
                    _resources[dataReward.name] = resource;
                }
            }
        }

        public override async void Open()
        {
            var delay = _config.GetFloat("delay", 0.1f);
            var durationBetween = _config.GetFloat("durationBetween", 0.1f);
            var durationTweenScale = _config.GetFloat("durationTweenScale", 0.3f);
            var durationMoveMin = _config.GetFloat("durationMove", 0.6f);
            var durationMoveMax = _config.GetFloat("durationMoveMax", 0.9f);
            var moveSpeed = _config.GetFloat("moveSpeed", 2000f);
            var valueScale = _config.GetFloat("valueScale", 1f);
            var delayAfterScale = _config.GetFloat("delayAfterScale", 0.6f);
            var spacing = _config.GetFloat("spacing", 200);
            var curveMove = _config.GetCurve("curveMove");

            var tcs = this.GetCancellationTokenOnDestroy();
            var rewards = Data.rewards;

            _layout.enabled = true;
            _layout.horizontalSpacing = spacing;
            _layout.verticalSpacing = spacing;

            for (var i = 0; i < rewards.Length; i++)
            {
                var item = _poolItem.Get();
                item.SetItemData(rewards[i].name, rewards[i].amount);
#if UNITY_EDITOR
                item.name = $"{itemPrf.name}_{i}";
#endif
                item.transform.SetAsLastSibling();
                item.transform.localScale = Vector3.zero;
            }

            await UniTask.WaitForSeconds(delay, cancellationToken: tcs);

            _layout.enabled = false;

            for (var i = 0; i < rewards.Length; i++)
            {
                var nameReward = rewards[i].name;
                var amount = rewards[i].amount;
                var item = _poolItem.SpawnedObjects.ElementAt(i);
                var seq = DOTween.Sequence()
                    .SetLink(gameObject) // đổi scene: item chết theo entry, DOTween tự kill thay vì tween lên transform đã destroy
                    .Append(item.transform.DOScale(Vector3.one * valueScale, durationTweenScale).From(Vector3.zero).SetEase(Ease.OutBack))
                    .AppendInterval(delayAfterScale);

                if (_resources.TryGetValue(nameReward, out var resource))
                {
                    // DOLocalMove đặt theo hệ toạ độ của cha item (Viewport), không phải gốc entry
                    var localPos = item.transform.parent.InverseTransformPoint(resource.icon.transform.position);
                    var durationMove = Mathf.Clamp(Vector2.Distance(item.transform.localPosition, localPos) / moveSpeed, durationMoveMin, durationMoveMax);
                    var move = item.transform.DOLocalMove(localPos, durationMove);
                    seq = seq.Append(curveMove != null ? move.SetEase(curveMove) : move.SetEase(Ease.InBack))
                        .Join(item.transform.DOScale(Vector3.one * ArriveScale(item, resource), durationMove).SetEase(Ease.OutQuad))
                        .AppendCallback(() =>
                        {
                            if (!this || !item) return;

                            // Counter ở scene khác, unload giữa chừng thì vẫn phải trả item về pool
                            if (resource && resource.icon)
                            {
                                resource.icon.transform.DOComplete();
                                resource.icon.transform.DOPunchScale(new Vector3(0.1f, -0.1f, 0), 0.5f, 1).SetTarget(resource.icon.transform).SetLink(resource.gameObject);
                            }
                            _poolItem.Release(item);
                            GameEvent.Emit($"falcon.ui.{nameReward}_update");
                            GameEvent<(string, int)>.Emit("falcon.ui.reward_count_up", (nameReward, amount));
                        });
                }
                else
                {
                    seq = seq.Append(item.transform.DOScale(Vector3.zero, durationTweenScale).SetEase(Ease.InBack))
                        .AppendCallback(() =>
                        {
                            if (!this || !item) return;

                            _poolItem.Release(item);
                            GameEvent.Emit($"falcon.ui.{nameReward}_update");
                            GameEvent<(string, int)>.Emit("falcon.ui.reward_count_up", (nameReward, amount));
                        });
                }
                await UniTask.WaitForSeconds(durationBetween, cancellationToken: tcs);
            }

            await UniTask.WaitUntil(IsAllItemReleased, cancellationToken: tcs);
            OnDispose?.Invoke();
            OnNext?.Invoke();
        }

        private bool IsAllItemReleased()
        {
            return _poolItem.CountActive == 0;
        }

        // So kích thước world vì entry và đích có thể nằm ở hai canvas scale khác nhau.
        // Không đọc lossyScale của item: lúc dựng sequence nó đang ở scale 0, nên nhân tay localScale từ Icon lên tới item.
        private static float ArriveScale(UIRewardItem item, UIResource resource)
        {
            var from = item.icon.rectTransform;
            var to = resource.icon.rectTransform;

            var scale = item.transform.parent ? (Vector2)item.transform.parent.lossyScale : Vector2.one;
            for (var t = from.transform; t && t != item.transform; t = t.parent) scale = Vector2.Scale(scale, t.localScale);

            var fromSize = DrawnSize(item.icon, Vector2.Scale(from.rect.size, scale));
            var toSize = DrawnSize(resource.icon, Vector2.Scale(to.rect.size, to.lossyScale));
            if (fromSize.x <= 0f || fromSize.y <= 0f) return 1f;

            return Mathf.Min(toSize.x / fromSize.x, toSize.y / fromSize.y);
        }

        // preserveAspect thì sprite chỉ chiếm một phần rect, so theo phần thực vẽ.
        private static Vector2 DrawnSize(Image image, Vector2 size)
        {
            if (!image.preserveAspect || !image.sprite || size.y <= 0f) return size;

            var spriteSize = image.sprite.rect.size;
            if (spriteSize.y <= 0f) return size;

            var aspect = spriteSize.x / spriteSize.y;
            return size.x / size.y > aspect ? new Vector2(size.y * aspect, size.y) : new Vector2(size.x, size.x / aspect);
        }
    }

    public class RewardSpawnData : IRewardEntryData
    {
        public string where;
        public (string name, int amount)[] rewards;
    }
}