using System;
using System.Collections;
using UnityEngine;
using Mimas.Core.Data;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Flies a placeholder projectile along a resolved attack's curve and calls back on impact
    /// (design: #presentation). It is handed the <em>same</em> <see cref="FlightCurve"/> evaluator the preview
    /// used, so the shot the player was shown is the shot that flies. A sphere with one flat colour is the whole
    /// visual until there is art; the impact is the HUD's flyover, which fires from <c>onImpact</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProjectilePlayback : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Radius of the placeholder sphere in world units.")]
        [SerializeField] private float _radius = 0.08f;

        [Tooltip("Colour of a weapon shot: arrow, bullet.")]
        [SerializeField] private Color _weaponColor = new Color(0.949f, 0.910f, 0.784f, 1f);

        [Tooltip("Colour of a spell shot.")]
        [SerializeField] private Color _spellColor = new Color(0.549f, 0.784f, 1f, 1f);

        private Transform _projectile;
        private Material _material;
        private Coroutine _routine;

        /// <summary>True while a projectile is in the air. The session keeps the board non-interactive meanwhile.</summary>
        public bool IsPlaying { get; private set; }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void OnDisable()
        {
            Stop();
        }

        /// <summary>Flight time for one shot: straight shots snap, arcs hang, a sky shot takes as long as it takes.</summary>
        public static float DurationFor(string trajectory, int hexDistance)
        {
            int tiles = Mathf.Max(0, hexDistance);
            if (trajectory == Trajectories.Arc) return 0.35f + 0.06f * tiles;
            if (trajectory == Trajectories.Sky) return 0.6f;
            return 0.18f + 0.03f * tiles;
        }

        /// <summary>The placeholder colour for an ability category (<c>AbilityCategories</c>).</summary>
        public Color ColorFor(string category)
        {
            return category == AbilityCategories.Spell ? _spellColor : _weaponColor;
        }

        /// <summary>
        /// Flies the curve over <paramref name="duration"/> seconds, then hides the sphere and calls
        /// <paramref name="onImpact"/>. A null curve or a non-positive duration lands immediately, so the
        /// callback always runs exactly once and the session never stalls.
        /// </summary>
        public void Play(Func<float, Vector3> curve, float duration, Color color, Action onImpact)
        {
            Stop();

            if (curve == null || duration <= 0f || !isActiveAndEnabled)
            {
                if (onImpact != null) onImpact();
                return;
            }

            EnsureProjectile();
            if (_material != null) _material.SetColor(BaseColorId, color);
            _routine = StartCoroutine(FlyRoutine(curve, duration, onImpact));
        }

        /// <summary>Cancels a flight without calling back. Used when the session tears down mid-animation.</summary>
        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            IsPlaying = false;
            if (_projectile != null) _projectile.gameObject.SetActive(false);
        }

        private IEnumerator FlyRoutine(Func<float, Vector3> curve, float duration, Action onImpact)
        {
            IsPlaying = true;
            _projectile.gameObject.SetActive(true);
            _projectile.position = curve(0f);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _projectile.position = curve(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            _projectile.position = curve(1f);
            _projectile.gameObject.SetActive(false);
            IsPlaying = false;
            _routine = null;

            if (onImpact != null) onImpact();
        }

        private void EnsureProjectile()
        {
            if (_projectile != null) return;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * (_radius * 2f);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var renderer = go.GetComponent<Renderer>();
            Shader shader = Shader.Find("Mimas/AimLine");
            if (shader != null && renderer != null)
            {
                // The same unlit overlay the aim line uses: a flat, readable dot that terrain can hide.
                _material = new Material(shader) { name = "Projectile (runtime)" };
                _material.SetFloat(Shader.PropertyToID("_DashDuty"), 1f);
                // Unlike the preview line, a projectile is an object in the world: terrain may hide it.
                _material.SetFloat(Shader.PropertyToID("_ZTest"), (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                renderer.sharedMaterial = _material;
            }

            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            go.SetActive(false);
            _projectile = go.transform;
        }
    }
}
