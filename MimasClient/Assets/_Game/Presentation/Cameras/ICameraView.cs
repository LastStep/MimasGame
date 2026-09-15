using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Contract for one camera view in a CinemachineBrain-managed scene. A view bundles a virtual camera
    /// and its body/aim pipeline behind an activate / deactivate / retarget surface, so
    /// <see cref="CameraDirector"/> can switch views without knowing any Cinemachine type.
    /// Priorities are explicit integers rather than <c>Prioritize()</c> so the live value stays
    /// readable in the Inspector while debugging a blend.
    /// </summary>
    public interface ICameraView
    {
        /// <summary>The GameObject hosting the virtual camera. For debug visualizers and logging.</summary>
        GameObject ViewGameObject { get; }

        /// <summary>
        /// Points the view at a target. Implementations set the tracking target and reset
        /// <c>PreviousStateIsValid</c> so the camera does not lerp in from its previous pose.
        /// Fixed-pose views may legitimately ignore this.
        /// </summary>
        void SetTarget(Transform target);

        /// <summary>Raises this view's priority so the brain makes it live.</summary>
        void Activate(int priority);

        /// <summary>Drops this view's priority back to the inactive value.</summary>
        void Deactivate(int inactivePriority);

        /// <summary>Current numeric priority of the underlying virtual camera.</summary>
        int CurrentPriority { get; }
    }
}
