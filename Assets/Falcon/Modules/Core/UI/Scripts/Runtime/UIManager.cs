/*
 * Author: dongvv
 * Email: dongvv@falcongames.com
 * Company: Falcon Games
 * Date: 2025-06-03
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sirenix.OdinInspector;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Falcon.Helpers.Singleton;

namespace Falcon.Modules.Core.UI.Runtime
{
    [DefaultExecutionOrder(-999)]
    public class UIManager : Singleton<UIManager>
    {
        public Action onUIPopupBeforeOpen;
        public Action onUIPopupBeforeClose;
        public Action onUIPopupChanged;

        [Header("Menus")]
        public List<UIMenu> menus = new List<UIMenu>();
        [HideInInspector] public UIMenu currentActiveMenu;

        [Header("Popups")]
        public List<UIPopup> popups = new List<UIPopup>();
        [HideInInspector] public UIPopup currentActivePopup;

        [Header("Store - UIPopup")]
        public RectTransform rootStorePopup;

        [ReadOnly] public List<UIPopup> stackPopups = new List<UIPopup>();

        protected override void Awake()
        {
            base.Awake();
            InitializeDefaultMenu();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            menus = null;
            popups = null;
        }

        private void InitializeDefaultMenu()
        {
            if (currentActiveMenu != null || menus.Count == 0) return;

            foreach (var menu in menus)
            {
                if (menu == null) continue;
                currentActiveMenu = menu;
                menu.gameObject.SetActive(true);
                menu.ChangeVisibility(true);
                break;
            }
        }

        #region Menu Functions

        public void OpenMenu_Stack(string menuName)
        {
            var currentMenu = currentActiveMenu;
            if (currentMenu != null && currentMenu.name == menuName) return;

            OpenMenu(menuName);
            if (currentActiveMenu != null)
                currentActiveMenu.previousMenu = currentMenu;
        }

        public void OpenMenu(string menuName)
        {
            var lastMenu = currentActiveMenu;
            foreach (var menu in menus)
            {
                if (menu == null) continue;

                if (menu.name == menuName)
                {
                    menu.gameObject.SetActive(true);
                    menu.ChangeVisibility(true);
                    currentActiveMenu = menu;
                    break;
                }
            }

            if (lastMenu != null && lastMenu != currentActiveMenu)
                lastMenu.ChangeVisibility(false);
        }

        public void OpenMenu(UIMenu menu)
        {
            var lastMenu = currentActiveMenu;

            if (menu != null && menus.Contains(menu))
            {
                menu.gameObject.SetActive(true);
                menu.ChangeVisibility(true);
                currentActiveMenu = menu;
            }

            if (lastMenu != null && lastMenu != currentActiveMenu)
                lastMenu.ChangeVisibility(false);
        }

        public void NextMenu()
        {
            if (currentActiveMenu?.nextMenu != null)
                OpenMenu(currentActiveMenu.nextMenu);
        }

        #endregion

        #region Popup Functions

        private HashSet<string> _popupFromAddressables = new HashSet<string>();

        // Anti-spam: popups currently loading (Addressables or scene)
        private HashSet<string> _popupLoading = new HashSet<string>();

        // Open callbacks of every OpenPopup call made while the popup was loading, invoked once it opens
        private Dictionary<string, Action<Transform>> _popupLoadingCallbacks = new Dictionary<string, Action<Transform>>();

        // Popups closed while still loading: kept hidden when the load finishes instead of opening
        private HashSet<string> _popupCloseRequests = new HashSet<string>();

        public void OpenPopup(string popupName, Action<Transform> callback = null)
        {
            //Load UIPopup - Default
            foreach (var popup in popups)
            {
                if (popup != null && popup.name == popupName)
                {
                    OpenPopup(popup, callback);
                    return;
                }
            }

            // Anti-spam: already loading, so do not load twice. Keep the callback for when it opens,
            // and an open request made after a close request wins.
            if (_popupLoading.Contains(popupName))
            {
                _popupCloseRequests.Remove(popupName);
                if (callback != null)
                {
                    _popupLoadingCallbacks.TryGetValue(popupName, out var queued);
                    _popupLoadingCallbacks[popupName] = queued + callback;
                }

                Debug.Log($"Popup '{popupName}' is already loading, its callback runs when it opens.");
                return;
            }

            _popupLoading.Add(popupName);
            _popupLoadingCallbacks[popupName] = callback;
            StartCoroutine(IELoad());
            IEnumerator IELoad()
            {
                Transform instance = null;

                if (!_popupFromAddressables.Contains(popupName))
                {
                    var checkHandle = Addressables.LoadResourceLocationsAsync(popupName);
                    yield return checkHandle;

                    if (checkHandle.Status == AsyncOperationStatus.Succeeded && checkHandle.Result.Count > 0)
                        _popupFromAddressables.Add(popupName);
                }

                if (_popupFromAddressables.Contains(popupName))
                {
                    yield return IELoadPopupFromAddressable(popupName, t => instance = t);
                }
                else
                {
                    Debug.LogWarning($"Popup {popupName} not found in Addressables. Trying load popup scene");
                    yield return StartCoroutine(IELoadPopupFromScene(popupName, t => instance = t));
                }

                OnPopupLoaded(popupName, instance);
            }
        }

        /// <summary>
        /// Ends the load of a popup: opens it with every queued callback, or keeps it hidden if it was closed meanwhile.
        /// </summary>
        /// <param name="popupName">Name of the loaded popup.</param>
        /// <param name="instance">The loaded popup, or null when the load failed.</param>
        private void OnPopupLoaded(string popupName, Transform instance)
        {
            _popupLoading.Remove(popupName);
            _popupLoadingCallbacks.TryGetValue(popupName, out var callback);
            _popupLoadingCallbacks.Remove(popupName);
            var isCloseRequested = _popupCloseRequests.Remove(popupName);

            if (instance == null)
            {
                if (callback != null) Debug.LogWarning($"Popup '{popupName}' failed to load, its open callbacks are dropped.");
                return;
            }

            if (isCloseRequested)
            {
                // Register it so the next OpenPopup finds it, but do not show it
                var uiPopup = AddToPopupList(instance);
                MoveToRootStore(uiPopup);
                uiPopup.gameObject.SetActive(false);

                Debug.Log($"Popup '{popupName}' was closed while loading, it stays hidden.");
                return;
            }

            OpenPopup(instance, callback);
        }

        private IEnumerator IELoadPopupFromAddressable(string popupName, Action<Transform> onLoaded)
        {
            var handlerPopup = Addressables.LoadAssetAsync<GameObject>(popupName);
            yield return handlerPopup;

            if (handlerPopup.Status == AsyncOperationStatus.Succeeded && handlerPopup.Result != null)
            {
                var instance = Instantiate(handlerPopup.Result);

                if (instance.TryGetComponent<UIPopupAddressableHandler>(out var popupAddressableHandler))
                    popupAddressableHandler.Init(handlerPopup);
                else
                    instance.AddComponent<UIPopupAddressableHandler>().Init(handlerPopup);

                // Change Name
                instance.name = popupName;

                yield return new WaitForEndOfFrame();
                onLoaded?.Invoke(instance.transform);
            }
            else
            {
                Debug.LogWarning($"Addressables load failed for {popupName}: {handlerPopup.OperationException?.Message}");
            }
        }

        IEnumerator IELoadPopupFromScene(string popupName, Action<Transform> onLoaded)
        {
            var asyncLoad = SceneManager.LoadSceneAsync(popupName, LoadSceneMode.Additive);
            if (asyncLoad == null) yield break;

            while (!asyncLoad.isDone) yield return new WaitForEndOfFrame();
            var scenePopup = SceneManager.GetSceneByName(popupName);
            var rootObjects = scenePopup.GetRootGameObjects();
            if (rootObjects.Length == 0)
            {
                Debug.LogWarning($"Popup scene {popupName} has no root object.");
                SceneManager.UnloadSceneAsync(popupName);
                yield break;
            }

            var instance = rootObjects[0];

            // Change Name
            instance.name = popupName;

            yield return new WaitForEndOfFrame();

            // Move it out of the popup scene before the scene unloads, whether it opens or stays hidden
            MoveToRootStore(AddToPopupList(instance.transform));
            onLoaded?.Invoke(instance.transform);

            // Unload scene after load popup
            SceneManager.UnloadSceneAsync(popupName);
        }

        public void OpenPopup(Transform popup, Action<Transform> callback = null)
        {
            var uiPopup = AddToPopupList(popup);
            OpenPopup(uiPopup, p => callback?.Invoke(p.transform));
        }

        /// <summary>
        /// Adds the popup to the popup list once (adds a UIPopup component if it has none).
        /// </summary>
        private UIPopup AddToPopupList(Transform popup)
        {
            if (!popup.TryGetComponent<UIPopup>(out var uiPopup)) uiPopup = popup.gameObject.AddComponent<UIPopup>();

            //Add UIPopup To Default
            if (!popups.Contains(uiPopup)) popups.Add(uiPopup);
            return uiPopup;
        }

        public void OpenPopup(UIPopup popup, Action<Transform> callback = null)
        {
            if (popup == null) return;

            // Already open: only bring it to the top. Replaying the show animation and the simulated
            // Awake/Enable/Start would look like the popup opens again.
            if (popup.visible && popup.gameObject.activeSelf && stackPopups.Contains(popup))
            {
                currentActivePopup = popup;
                RegisterPopupStack(popup);
                StateActivatePopupStack(true);

                callback?.Invoke(popup.transform);
                onUIPopupChanged?.Invoke();
                return;
            }

            onUIPopupBeforeOpen?.Invoke();
            
            // Cache
            currentActivePopup = popup;

            RegisterPopupStack(popup);
            StateActivatePopupStack(true);
   
            popup.ChangeVisibility(true);

            // Broadcast Simulation Awake
            var hasStarted = !popup.HasStarted;
            if (hasStarted)
            {
                popup.gameObject.BroadcastMessage("OnUIPopupAwake", SendMessageOptions.DontRequireReceiver);
            }

            // Invoke callback after popup is opened
            callback?.Invoke(popup.transform);

            // Broadcast Simulation Enable
            popup.gameObject.BroadcastMessage("OnUIPopupEnable", SendMessageOptions.DontRequireReceiver);
            
            // Broadcast Simulation Start
            if (hasStarted)
            {
                popup.MarkStarted();
                popup.gameObject.BroadcastMessage("OnUIPopupStart", SendMessageOptions.DontRequireReceiver);
            }

            onUIPopupChanged?.Invoke();
        }

        // public void ClosePopup()
        // {
        //     if (currentActivePopup != null)
        //     {
        //         currentActivePopup.ChangeVisibility(false);
        //         stackPopups.Remove(currentActivePopup);
        //     }

        //     currentActivePopup = GetTopMostPopupStack();
        //     StateActivatePopupStack(false);
        // }

        public void ClosePopup(string popupName)
        {
            foreach (var popup in popups)
            {
                if (popup != null && popup.name == popupName)
                {
                    ClosePopup(popup);
                    return;
                }
            }

            // Still loading: keep it hidden when the load finishes instead of letting it open after this close
            if (_popupLoading.Contains(popupName))
            {
                _popupCloseRequests.Add(popupName);
                return;
            }

            Debug.LogWarning($"Popup '{popupName}' not found in list.");
        }

        public void ClosePopup(UIPopup popup)
        {
            if (popup == null) return;

            onUIPopupBeforeClose?.Invoke();

            popup.ChangeVisibility(false);
            stackPopups.Remove(popup);
  
            currentActivePopup = GetTopMostPopupStack();
            StateActivatePopupStack(false);

            onUIPopupChanged?.Invoke();
        }

        public void CloseAllPopup()
        {
            for (int i = stackPopups.Count - 1; i >= 0; i--)
            {
                // Popup destroyed while still in the stack: just drop it
                if (stackPopups[i] == null) stackPopups.RemoveAt(i);
                else ClosePopup(stackPopups[i]);
            }
        }

        public void ClearAllStack()
        {
            stackPopups.Clear();
            onUIPopupChanged?.Invoke();
        }

        private void RegisterPopupStack(UIPopup popup)
        {
            stackPopups.Remove(popup);
            stackPopups.Add(popup);
        }

        public UIPopup GetTopMostPopupStack()
        {
            for (int i = stackPopups.Count - 1; i >= 0; i--)
            {
                if (stackPopups[i] != null && stackPopups[i].visible)
                    return stackPopups[i];
            }

            return null;
        }

        private void StateActivatePopupStack(bool isOpenPopup)
        {
            if (currentActivePopup == null) return;

            MoveToRootStore(currentActivePopup);

            // If open popup, always on top of hierarchy
            if (isOpenPopup)
                currentActivePopup.transform.SetAsLastSibling();
        }

        private void MoveToRootStore(UIPopup popup)
        {
            // If popup in root, force move to root store
            if (popup.transform.parent == rootStorePopup) return;

            popup.transform.SetParent(rootStorePopup);

            // Render full stretch rect transform
            var rect = popup.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
        }

        public List<UIPopup> GetAllPopupStack() => stackPopups;

        public int GetNumPopupStack() => stackPopups.Count;

        public bool IsFreeUIPopupFullScreen()
        {
            foreach (var item in stackPopups)
                if (item != null && item.isFullScreen) return false;
            return true;
        }

        public bool IsFreeUIPopupHalfScreen()
        {
            foreach (var item in stackPopups)
                if (item != null && item.isHalfScreen) return false;
            return true;
        }

        #endregion

        #region Utility

        public void OpenUI(string nameUI)
        {
            foreach (var menu in menus)
            {
                if (menu.name == nameUI)
                {
                    OpenMenu(nameUI);
                    return;
                }
            }

            foreach (var popup in popups)
            {
                if (popup.name == nameUI)
                {
                    OpenPopup(nameUI);
                    return;
                }
            }

            Debug.LogError($"Can't find UI named: {nameUI}");
        }

        public void Test_OpenPopup(UIPopup popupName)
        {
            OpenPopup(popupName);
        }
        #endregion
    }
}