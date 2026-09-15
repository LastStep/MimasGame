using UnityEngine;
using Unity.Cinemachine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Wraps any pre-configured <see cref="CinemachineCamera"/> as an <see cref="ICameraView"/>. The vcam
    /// keeps whatever body/aim pipeline it was authored with; this class only owns priority and targeting.
    /// Cinemachine 3 traps handled here: <c>Priority</c> is a struct, so it is written whole with
    /// <c>Enabled = true</c> (Prioritize() writes only the value); <c>Target</c> is a struct, so retargeting
    /// is read-mutate-write; and <c>PreviousStateIsValid</c> is cleared so the camera does not sweep in
    /// from the old target's pose.
    /// Set <c>_fixedPose</c> for arena-overview cameras that must stay at their authored transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineCameraView : MonoBehaviour, ICameraView
    {
        [Tooltip("The virtual camera to drive. Leave empty to use one on this GameObject.")]
        [SerializeField] private CinemachineCamera _vcam;

        [Tooltip("When true this view ignores SetTarget and stays at its authored transform.")]
        [SerializeField] private bool _fixedPose = true;

        private bool _loggedMissingVcam;

        /// <summary>The GameObject hosting this view.</summary>
        public GameObject ViewGameObject => gameObject;

        /// <summary>Current numeric priority of the underlying vcam, or 0 when it is missing.</summary>
        public int CurrentPriority
        {
            get
            {
                CinemachineCamera vcam = ResolveVcam();
                return vcam == null ? 0 : vcam.Priority.Value;
            }
        }

        /// <summary>True when this view ignores retargeting.</summary>
        public bool IsFixedPose => _fixedPose;

        private void Awake()
        {
            ResolveVcam();
        }

        /// <summary>Sets the vcam's tracking target. No-op for fixed-pose views.</summary>
        public void SetTarget(Transform target)
        {
            if (_fixedPose) return;

            CinemachineCamera vcam = ResolveVcam();
            if (vcam == null) return;

            CameraTarget cameraTarget = vcam.Target;
            cameraTarget.TrackingTarget = target;
            vcam.Target = cameraTarget;
            vcam.PreviousStateIsValid = false;
        }

        /// <summary>Raises the vcam's priority so the brain makes it live.</summary>
        public void Activate(int priority) => WritePriority(priority);

        /// <summary>Drops the vcam's priority back to the inactive value.</summary>
        public void Deactivate(int inactivePriority) => WritePriority(inactivePriority);

        private void WritePriority(int value)
        {
            CinemachineCamera vcam = ResolveVcam();
            if (vcam == null) return;

            // Enabled must be written too: a PrioritySettings with Enabled == false reports 0
            // regardless of the value that was stored.
            vcam.Priority = new PrioritySettings { Enabled = true, Value = value };
        }

        private CinemachineCamera ResolveVcam()
        {
            if (_vcam == null)
            {
                _vcam = GetComponent<CinemachineCamera>();
                if (_vcam == null && !_loggedMissingVcam)
                {
                    _loggedMissingVcam = true;
                    Debug.LogError("[CinemachineCameraView] No CinemachineCamera assigned or found on this GameObject.", this);
                }
            }
            return _vcam;
        }
    }
}
