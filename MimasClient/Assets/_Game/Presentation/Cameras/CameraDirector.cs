using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Owns which camera view is live. Views are dragged in as MonoBehaviours and validated as
    /// <see cref="ICameraView"/> in Awake — Unity cannot serialize interface lists, so the runtime check
    /// plus a warning is the price of Inspector drag-and-drop. Switching is a priority swap
    /// (active / inactive), never enable/disable, so Cinemachine can blend between poses.
    /// Tab cycles views during development; production code should call <see cref="ActivateIndex"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraDirector : MonoBehaviour
    {
        [Tooltip("Camera view components in cycle order. Each must implement ICameraView.")]
        [SerializeField] private List<MonoBehaviour> _views = new List<MonoBehaviour>();

        [SerializeField] private int _activePriority = 10;
        [SerializeField] private int _inactivePriority = 0;

        [Tooltip("Index activated on Start.")]
        [SerializeField] private int _defaultIndex = 0;

        [Tooltip("Development convenience: cycle views with the Tab key.")]
        [SerializeField] private bool _cycleOnTab = true;

        private readonly List<ICameraView> _resolved = new List<ICameraView>();

        /// <summary>Number of usable views after validation.</summary>
        public int ViewCount => _resolved.Count;

        /// <summary>Index of the live view, or -1 before the first activation.</summary>
        public int ActiveIndex { get; private set; } = -1;

        private void Awake()
        {
            _resolved.Clear();
            for (int i = 0; i < _views.Count; i++)
            {
                MonoBehaviour behaviour = _views[i];
                if (behaviour == null)
                {
                    Debug.LogWarning("[CameraDirector] _views[" + i + "] is empty; dropped.", this);
                    continue;
                }

                ICameraView view = behaviour as ICameraView;
                if (view == null)
                {
                    Debug.LogWarning("[CameraDirector] '" + behaviour.GetType().Name + "' does not implement ICameraView; dropped.", this);
                    continue;
                }

                _resolved.Add(view);
            }

            if (_resolved.Count == 0)
            {
                Debug.LogWarning("[CameraDirector] No valid camera views registered.", this);
            }
        }

        private void Start()
        {
            if (_resolved.Count > 0) ActivateIndex(_defaultIndex);
        }

        private void Update()
        {
            if (!_cycleOnTab || _resolved.Count < 2) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.tabKey.wasPressedThisFrame) CycleNext();
        }

        /// <summary>Makes the view at <paramref name="index"/> live; the index wraps.</summary>
        public void ActivateIndex(int index)
        {
            if (_resolved.Count == 0) return;

            int wrapped = ((index % _resolved.Count) + _resolved.Count) % _resolved.Count;
            for (int i = 0; i < _resolved.Count; i++)
            {
                if (i == wrapped) _resolved[i].Activate(_activePriority);
                else _resolved[i].Deactivate(_inactivePriority);
            }

            ActiveIndex = wrapped;
        }

        /// <summary>Activates the next view in the list, wrapping at the end.</summary>
        public void CycleNext() => ActivateIndex(ActiveIndex + 1);

        /// <summary>Fans a tracking target out to every view. Fixed-pose views ignore it by design.</summary>
        public void SetTarget(Transform target)
        {
            for (int i = 0; i < _resolved.Count; i++) _resolved[i].SetTarget(target);
        }

        /// <summary>The live view, or null before the first activation.</summary>
        public ICameraView ActiveView => ActiveIndex >= 0 && ActiveIndex < _resolved.Count ? _resolved[ActiveIndex] : null;
    }
}
