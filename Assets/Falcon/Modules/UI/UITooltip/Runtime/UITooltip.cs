/*
 * Author: Bui Quang Huy
 * Email: huybq@falcongames.com
 * Company: Falcon Games
 * Date: 2025-10-15
 */

using System;
using DG.Tweening;
using Falcon.Helpers.EventBus;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Falcon.Modules.UI.UITooltip.Runtime
{
    public class UITooltip : MonoBehaviour
    {
        [SerializeField] private GameObject element;

        private GameObject _goTrigger;
        private CanvasGroup _canvasGroup;
        private bool _active;
        private UITooltipDisableNotifier _triggerDisableNotifier;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            GameEvent<(string, int)[]>.Register("falcon.modules.ui.tooltip", Open, this);
            GameEvent<((string, int)[], bool)>.Register("falcon.modules.ui.tooltip", Open, this);
            GameEvent<string>.Register("falcon.modules.ui.tooltip", Open, this);
        }

        private void OnDisable()
        {
            GameEvent<(string, int)[]>.Unregister("falcon.modules.ui.tooltip", Open, this);
            GameEvent<((string, int)[], bool)>.Unregister("falcon.modules.ui.tooltip", Open, this);
            GameEvent<string>.Unregister("falcon.modules.ui.tooltip", Open, this);
            UnregisterTriggerDisableEvent();
        }

        private void Open(string content)
        {
            if (Open())
            {
                element.SetActive(true);
                element.transform.position = _goTrigger.transform.position;
                element.SendMessage("Active", content, SendMessageOptions.DontRequireReceiver);
            }
        }

        private void Open((string, int)[] data) => Open((data, true));

        // bool = có chèn dấu "+" giữa các món
        private void Open(((string, int)[], bool) data)
        {
            if (Open())
            {
                element.SetActive(true);
                element.transform.position = _goTrigger.transform.position;
                element.SendMessage("Active", data, SendMessageOptions.DontRequireReceiver);
            }
        }

        private bool Open()
        {
            this.DOComplete();

            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (_goTrigger == selected)
            {
                Close();
                _goTrigger = null;
                UnregisterTriggerDisableEvent();
                return false;
            }

            UnregisterTriggerDisableEvent();

            _goTrigger = selected;
            if (_goTrigger == null)
            {
                Close();
                return false;
            }

            RegisterTriggerDisableEvent(_goTrigger);

            _active = true;
            _canvasGroup.alpha = 1;
            return true;
        }

        private void RegisterTriggerDisableEvent(GameObject trigger)
        {
            if (trigger == null) return;

            _triggerDisableNotifier = trigger.GetComponent<UITooltipDisableNotifier>();
            if (_triggerDisableNotifier == null)
                _triggerDisableNotifier = trigger.AddComponent<UITooltipDisableNotifier>();

            _triggerDisableNotifier.Disabled -= OnTriggerDisabled;
            _triggerDisableNotifier.Disabled += OnTriggerDisabled;
        }

        private void UnregisterTriggerDisableEvent()
        {
            if (_triggerDisableNotifier == null) return;
            _triggerDisableNotifier.Disabled -= OnTriggerDisabled;
            _triggerDisableNotifier = null;
        }

        private void OnTriggerDisabled()
        {
            Close();
            _goTrigger = null;
            _active = false;
            UnregisterTriggerDisableEvent();
        }

        private void Close()
        {
            _canvasGroup.alpha = 0;
            element.SendMessage("DeActive", SendMessageOptions.DontRequireReceiver);
            element.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_active) return;

            if (Input.GetMouseButtonDown(0))
                Close();
            else if (Input.GetMouseButtonUp(0))
                OnEndTouch();

            foreach (var touch in Input.touches)
            {
                switch (touch.phase)
                {
                    case TouchPhase.Began: Close(); break;
                    case TouchPhase.Ended: OnEndTouch(); break;
                }
            }
        }

        private void OnEndTouch()
        {
            if (_goTrigger != null)
            {
                _goTrigger = EventSystem.current.currentSelectedGameObject;
            }

            if (_canvasGroup.alpha != 0) return;
            _active = false;
        }
    }

    public class UITooltipDisableNotifier : MonoBehaviour
    {
        public event Action Disabled;

        private void OnDisable()
        {
            Disabled?.Invoke();
        }
    }
}
