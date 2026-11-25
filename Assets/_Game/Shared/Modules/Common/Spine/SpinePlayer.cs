using System.Threading;
using Cysharp.Threading.Tasks;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Falcon.Shared.Common
{
    public class SpinePlayer : MonoBehaviour
    {
        [System.Serializable]
        public struct Clip
        {
            [SpineAnimation] public string name;
            public bool loop;
        }

        [SerializeField] private Clip[] clips;

        private ISkeletonComponent skeletonComponent; // SkeletonAnimation (2D) hoặc SkeletonGraphic (UI)

        private ISkeletonComponent Comp => skeletonComponent ??= GetComponent<ISkeletonComponent>();

        /// <summary>AnimationState, tự Initialize nếu chưa valid</summary>
        public Spine.AnimationState State
        {
            get
            {
                switch (Comp)
                {
                    case null: return null;
                    case SkeletonRenderer r when !r.valid: r.Initialize(false); break;
                    case SkeletonGraphic g when !g.IsValid: g.Initialize(false); break;
                }
                return (Comp as IAnimationStateComponent)?.AnimationState;
            }
        }

        public Skeleton Skeleton => State == null ? null : Comp.Skeleton;

        public float TimeScale
        {
            get => State?.TimeScale ?? 1f;
            set { if (State != null) State.TimeScale = value; }
        }

        public void PlayOne(string animationName) => PlayFresh(animationName, false);
        public void PlayLoop(string animationName) => PlayFresh(animationName, true);

        /// <summary>Play clip theo index trong danh sách (gọi được từ UnityEvent)</summary>
        public void PlayIndex(int index)
        {
            if (clips == null || index < 0 || index >= clips.Length)
            {
                Debug.LogError($"{name}: clip index {index} ngoài danh sách ({clips?.Length ?? 0})", this);
                return;
            }
            PlayFresh(clips[index].name, clips[index].loop);
        }

        /// <summary>Play từ setup pose, không dính pose của anim trước</summary>
        public TrackEntry PlayFresh(string animationName, bool loop = true, int track = 0)
        {
            var state = State;
            if (state == null) return null;
            state.ClearTracks();
            Skeleton.SetToSetupPose();
            var entry = state.SetAnimation(track, animationName, loop);
            switch (Comp)
            {
                case SkeletonGraphic g: g.Update(0); g.LateUpdate(); break;
                case SkeletonAnimation a: a.Update(0); a.LateUpdate(); break;
                default: state.Apply(Skeleton); break;
            }
            StartEditorTick();
            return entry;
        }

        private void StartEditorTick()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

#if UNITY_EDITOR
        private double lastEditorTime;

        private void EditorTick()
        {
            if (this == null || Application.isPlaying)
            {
                UnityEditor.EditorApplication.update -= EditorTick;
                return;
            }

            var now = UnityEditor.EditorApplication.timeSinceStartup;
            var dt = (float)(now - lastEditorTime);
            lastEditorTime = now;
            switch (Comp)
            {
                case SkeletonGraphic g: g.Update(dt); g.LateUpdate(); break;
                case SkeletonAnimation a: a.Update(dt); a.LateUpdate(); break;
            }
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        /// <summary>Nối animation sau animation hiện tại</summary>
        public TrackEntry Queue(string animationName, bool loop = false, float delay = 0f, int track = 0)
            => State?.AddAnimation(track, animationName, loop, delay);

        /// <summary>Play và chờ animation chạy xong 1 vòng</summary>
        public UniTask PlayAsync(string animationName, CancellationToken cancellationToken = default)
            => PlayFresh(animationName, false).WaitForCompleteAsync(true, cancellationToken);

        /// <summary>Chờ event trong animation hiện tại</summary>
        public UniTask WaitForEventAsync(string eventName, CancellationToken cancellationToken = default)
            => State.WaitForEventAsync(eventName, cancellationToken);

        public void Stop(int track = 0) => State?.SetEmptyAnimation(track, 0f);

        public void SetSkin(string skinName)
        {
            var skeleton = Skeleton;
            if (skeleton == null) return;
            skeleton.SetSkin(skinName);
            skeleton.SetSlotsToSetupPose();
            State?.Apply(skeleton);
        }
    }
}
