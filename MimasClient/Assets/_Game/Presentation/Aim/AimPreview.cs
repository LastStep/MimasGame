using System;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The line the player aims along (design: #presentation). Four states, and the state is the whole message:
    /// <list type="bullet">
    /// <item><b>Clear</b> — a dashed green path flowing towards the target with a ring on the point it will hit.</item>
    /// <item><b>Blocked</b> — red, stopping where the shot stops, with an X on the blocker. The HUD names the reason.</item>
    /// <item><b>Out of range</b> — grey, no markers; the cursor tag says so.</item>
    /// <item><b>Not targetable</b> — grey with a dimmed ring: there is something there, it just cannot be hit.</item>
    /// </list>
    /// Everything is built in code from <see cref="LineRenderer"/>s and one unlit shader: no art asset, nothing
    /// WebGL2 dislikes, and no world-space UI Toolkit (UUM-149277). The curve comes from
    /// <see cref="FlightCurve"/>, so the preview and the projectile cannot disagree.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AimPreview : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int DashCountId = Shader.PropertyToID("_DashCount");
        private static readonly int DashDutyId = Shader.PropertyToID("_DashDuty");

        /// <summary>Resolution used to find where along the curve a blocker sits. Independent of how many points are drawn.</summary>
        private const int ProbeSamples = 64;

        private const int MaxSamples = 48;
        private const int RingSegments = 24;

        [Header("Colours")]
        [SerializeField] private Color _clearColor = new Color(0.486f, 1f, 0.604f, 1f);
        [SerializeField] private Color _blockedColor = new Color(1f, 0.353f, 0.290f, 1f);
        [SerializeField] private Color _greyColor = new Color(0.604f, 0.604f, 0.604f, 0.6f);

        [Header("Shape")]
        [SerializeField] private float _width = 0.06f;
        [SerializeField] private float _markerWidth = 0.05f;

        [Tooltip("World length of one dash-plus-gap. The shader gets the count, so dashes stay the same size on a long or a short line.")]
        [SerializeField] private float _dashLength = 0.28f;

        [SerializeField] private float _ringRadius = 0.17f;
        [SerializeField] private float _markRadius = 0.17f;

        [Tooltip("Camera the ring and the X turn to face. Empty uses Camera.main.")]
        [SerializeField] private Camera _camera;

        private readonly Vector3[] _points = new Vector3[MaxSamples];
        private readonly Vector3[] _ringPoints = new Vector3[RingSegments];

        private LineRenderer _line;
        private LineRenderer _ring;
        private LineRenderer _markA;
        private LineRenderer _markB;
        private Material _lineMaterial;
        private Material _ringMaterial;
        private Material _markMaterial;

        private Vector3 _ringCentre;
        private Vector3 _markCentre;
        private bool _ringVisible;
        private bool _markVisible;

        private void Awake()
        {
            _lineMaterial = CreateMaterial(0.55f);
            _ringMaterial = CreateMaterial(1f);
            _markMaterial = CreateMaterial(1f);

            _line = CreateLine("AimLine", _lineMaterial, _width, false);
            _ring = CreateLine("AimRing", _ringMaterial, _markerWidth, true);
            _markA = CreateLine("BlockMarkA", _markMaterial, _markerWidth, false);
            _markB = CreateLine("BlockMarkB", _markMaterial, _markerWidth, false);

            _markMaterial.SetColor(BaseColorId, _blockedColor);
            Hide();
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_ringMaterial != null) Destroy(_ringMaterial);
            if (_markMaterial != null) Destroy(_markMaterial);
        }

        private void OnDisable()
        {
            Hide();
        }

        /// <summary>The shot is legal: dashed path in the clear colour with the aim ring on the point it lands.</summary>
        public void ShowClear(Func<float, Vector3> curve, Vector3 aimPoint, int samples = 24)
        {
            if (!DrawCurve(curve, samples, 1f, _clearColor, true)) return;
            ShowRing(aimPoint, Color.white);
            HideMark();
        }

        /// <summary>
        /// Something stops the shot. The path runs red as far as it gets and an X sits on the blocker;
        /// <paramref name="blockedWorld"/> is the blocking hex, matched to the curve by horizontal distance.
        /// </summary>
        public void ShowBlocked(Func<float, Vector3> curve, Vector3 blockedWorld, int samples = 24)
        {
            if (curve == null) { Hide(); return; }

            float stop = NearestParameter(curve, blockedWorld);
            if (!DrawCurve(curve, samples, stop, _blockedColor, true)) return;

            HideRing();
            ShowMark(curve(stop));
        }

        /// <summary>Out of range: the whole path in grey, nothing marked. The cursor tag carries the words.</summary>
        public void ShowOutOfRange(Func<float, Vector3> curve, int samples = 24)
        {
            if (!DrawCurve(curve, samples, 1f, _greyColor, false)) return;
            HideRing();
            HideMark();
        }

        /// <summary>There is a body there but it cannot be hit (a wall): grey path, dimmed ring on it.</summary>
        public void ShowNotTargetable(Func<float, Vector3> curve, Vector3 aimPoint, int samples = 24)
        {
            if (!DrawCurve(curve, samples, 1f, _greyColor, false)) return;
            ShowRing(aimPoint, new Color(1f, 1f, 1f, 0.35f));
            HideMark();
        }

        /// <summary>Clears the whole preview.</summary>
        public void Hide()
        {
            if (_line != null) _line.enabled = false;
            HideRing();
            HideMark();
        }

        private void LateUpdate()
        {
            if (!_ringVisible && !_markVisible) return;

            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null) return;

            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;

            if (_ringVisible)
            {
                for (int i = 0; i < RingSegments; i++)
                {
                    float angle = i * (2f * Mathf.PI / RingSegments);
                    _ringPoints[i] = _ringCentre + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * _ringRadius;
                }
                _ring.positionCount = RingSegments;
                _ring.SetPositions(_ringPoints);
            }

            if (_markVisible)
            {
                Vector3 a = (right + up).normalized * _markRadius;
                Vector3 b = (right - up).normalized * _markRadius;
                _markA.SetPosition(0, _markCentre - a);
                _markA.SetPosition(1, _markCentre + a);
                _markB.SetPosition(0, _markCentre - b);
                _markB.SetPosition(1, _markCentre + b);
            }
        }

        /// <summary>
        /// Writes the first <paramref name="stop"/> of the curve into the line. Returns false (and hides
        /// everything) when there is nothing to draw.
        /// </summary>
        private bool DrawCurve(Func<float, Vector3> curve, int samples, float stop, Color color, bool dashed)
        {
            if (curve == null)
            {
                Hide();
                return false;
            }

            int count = Mathf.Clamp(samples, 2, MaxSamples);
            float end = Mathf.Clamp01(stop);
            float length = 0f;

            for (int i = 0; i < count; i++)
            {
                _points[i] = curve(end * i / (count - 1f));
                if (i > 0) length += Vector3.Distance(_points[i - 1], _points[i]);
            }

            _line.positionCount = count;
            _line.SetPositions(_points);
            _line.enabled = true;

            _lineMaterial.SetColor(BaseColorId, color);
            _lineMaterial.SetFloat(DashDutyId, dashed ? 0.55f : 1f);
            _lineMaterial.SetFloat(DashCountId, Mathf.Max(1f, length / Mathf.Max(0.01f, _dashLength)));
            return true;
        }

        /// <summary>
        /// Where along the curve <paramref name="world"/> sits, matched horizontally: an arc passes over the
        /// blocking hex rather than through it, so only the XZ distance is meaningful.
        /// </summary>
        private static float NearestParameter(Func<float, Vector3> curve, Vector3 world)
        {
            float best = 1f;
            float bestDistance = float.MaxValue;
            for (int i = 0; i <= ProbeSamples; i++)
            {
                float t = i / (float)ProbeSamples;
                Vector3 point = curve(t);
                float dx = point.x - world.x;
                float dz = point.z - world.z;
                float distance = dx * dx + dz * dz;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = t;
            }
            return best;
        }

        private void ShowRing(Vector3 centre, Color color)
        {
            _ringCentre = centre;
            _ringVisible = true;
            _ring.enabled = true;
            _ringMaterial.SetColor(BaseColorId, color);
            LateUpdate();
        }

        private void HideRing()
        {
            _ringVisible = false;
            if (_ring != null) _ring.enabled = false;
        }

        private void ShowMark(Vector3 centre)
        {
            _markCentre = centre;
            _markVisible = true;
            _markA.enabled = true;
            _markB.enabled = true;
            LateUpdate();
        }

        private void HideMark()
        {
            _markVisible = false;
            if (_markA != null) _markA.enabled = false;
            if (_markB != null) _markB.enabled = false;
        }

        private static Material CreateMaterial(float duty)
        {
            Shader shader = Shader.Find("Mimas/AimLine");
            if (shader == null)
            {
                Debug.LogError("[AimPreview] Shader 'Mimas/AimLine' not found; the aim preview will not draw.");
                return null;
            }
            var material = new Material(shader) { name = "AimLine (runtime)" };
            material.SetFloat(DashDutyId, duty);
            return material;
        }

        private LineRenderer CreateLine(string name, Material material, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.loop = loop;
            line.positionCount = loop ? RingSegments : 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (material != null) line.sharedMaterial = material;
            line.enabled = false;
            return line;
        }
    }
}
