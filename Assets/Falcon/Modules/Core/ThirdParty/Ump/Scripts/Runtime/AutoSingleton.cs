/*
 * Author: namnx
 * Email: namnx@falcongames.com
 * Company: Falcon Games
 * Date: 2025-07-21
 */

using UnityEngine;

namespace Falcon.Modules.Core.ThirdParty.Ump.Runtime
{
    public abstract class AutoSingleton<T> : MonoBehaviour where T : Component
    {
        private static T _instance;
        private const string _ROOT_OBJECT_NAME = "[SingletonThirdParty]";

        public static bool IsNull => _instance == null;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject root = GetOrCreateRoot();

                    string childName = typeof(T).Name;
                    Transform child = root.transform.Find(childName);
                    GameObject childObj;

                    if (child == null)
                    {
                        childObj = new GameObject(childName);
                        childObj.transform.SetParent(root.transform);
                    }
                    else
                    {
                        childObj = child.gameObject;
                    }

                    _instance = childObj.GetComponent<T>();
                    if (_instance == null)
                        _instance = childObj.AddComponent<T>();
                }

                return _instance;
            }
        }

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = this as T;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private static GameObject GetOrCreateRoot()
        {
            GameObject root = GameObject.Find(_ROOT_OBJECT_NAME);
            if (root == null)
            {
                root = new GameObject(_ROOT_OBJECT_NAME);
            }

            DontDestroyOnLoad(root);


            return root;
        }
    }
}