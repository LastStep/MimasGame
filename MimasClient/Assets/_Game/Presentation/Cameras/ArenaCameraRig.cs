using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>How one <see cref="ArenaView"/> frames the arena: the tilt and the distance it starts at.</summary>
    [Serializable]
    public struct ViewFraming
    {
        [Tooltip("Degrees below the horizon. 90 would look straight down.")]
        [Range(10f, 89f)] public float Pitch;

        [Tooltip("Distance from the point the camera looks at, in world units. Kept inside the zoom range.")]
        [Min(1f)] public float Distance;

        public ViewFraming(float pitch, float distance)
        {
            Pitch = pitch;
            Distance = distance;
        }
    }

    /// <summary>
    /// The player's camera (design <c>#camera</c>, spec <c>docs/specs/2026-09-23-camera.md</c>): an orbit round a
    /// point on the board. Q/E turn it while held, W A S D slide the point the way the camera faces and stop it a
    /// set distance past the arena's edge, the wheel zooms, Space glides home to the default view and V to the other.
    /// It poses the transform of the vcam it sits on — that vcam has no body or aim, so its transform is the shot
    /// (ADR-038) — and only while that view is live, so the top-down camera is left alone. Every value is read each
    /// frame, so tuning in Play Mode is live. Nothing here touches the rules.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaCameraRig : MonoBehaviour
    {
        [Header("Views")]
        [Tooltip("The view a match opens on, and the one Space returns to. V flips to the other.")]
        [SerializeField] private ArenaView _defaultView = ArenaView.SideOn;

        [Tooltip("Your end of the arena on the left, the opponent's on the right.")]
        [SerializeField] private ViewFraming _sideOn = new ViewFraming(50f, 21f);

        [Tooltip("Past your end of the arena, looking across at the opponent's.")]
        [SerializeField] private ViewFraming _behindYou = new ViewFraming(55f, 25f);

        [Header("Rotate (Q / E)")]
        [Tooltip("Degrees per second while Q or E is held.")]
        [Min(0f)] [SerializeField] private float _rotateSpeed = 90f;

        [Tooltip("Swaps which way Q and E turn. Off: E turns the camera clockwise seen from above.")]
        [SerializeField] private bool _invertRotation;

        [Header("Pan (W A S D)")]
        [Tooltip("World units per second at the view's own distance. Zoomed out it pans faster, zoomed in slower.")]
        [Min(0f)] [SerializeField] private float _panSpeed = 10f;

        [Tooltip("How far past the outermost tiles the point the camera looks at may go, in tiles. 0 stops on the outermost tiles; negative keeps it further in.")]
        [Range(-3f, 6f)] [SerializeField] private float _panLimitTiles = 0f;

        [Header("Zoom (mouse wheel)")]
        [Tooltip("World units per wheel notch.")]
        [Min(0f)] [SerializeField] private float _zoomSpeed = 2f;

        [Tooltip("Nearest the camera may get to the point it looks at.")]
        [Min(1f)] [SerializeField] private float _minDistance = 10f;

        [Tooltip("Furthest the camera may get from the point it looks at.")]
        [Min(1f)] [SerializeField] private float _maxDistance = 32f;

        [Header("Feel")]
        [Tooltip("How quickly the camera catches up with where it was sent. Higher is snappier; 0 is instant.")]
        [Min(0f)] [SerializeField] private float _smoothing = 10f;

        [Header("Wiring")]
        [Tooltip("The rig steers only while its own view is the live one here. Empty: always.")]
        [SerializeField] private CameraDirector _director;

        [Tooltip("Wheel zoom is ignored while the HUD has the pointer (the examine plate scrolls). Empty: never ignored.")]
        [SerializeField] private BoardInputController _input;

        private readonly List<Vector3> _tileScratch = new List<Vector3>();
        private CinemachineCameraView _view;

        private bool _framed;
        private Vector3 _centre;
        private float _arenaRadius;
        private float _tileSpacing = 1f;
        private Vector3 _home;
        private Vector3 _away;
        private ArenaView _currentView;

        private Vector3 _pivot;
        private float _yaw;
        private float _pitch;
        private float _distance;

        private Vector3 _pivotTarget;
        private float _yawTarget;
        private float _pitchTarget;
        private float _distanceTarget;

        /// <summary>True once <see cref="Frame"/> has placed the camera on a board.</summary>
        public bool IsFramed => _framed;

        /// <summary>The view Space returns to.</summary>
        public ArenaView DefaultView => _defaultView;

        /// <summary>The view last sent to by Space, V or <see cref="Frame"/>. Turning and panning do not change it.</summary>
        public ArenaView CurrentView => _currentView;

        /// <summary>The point the camera is looking at now.</summary>
        public Vector3 Pivot => _pivot;

        /// <summary>The camera's turn round the pivot now, in degrees.</summary>
        public float Yaw => _yaw;

        /// <summary>The camera's tilt below the horizon now, in degrees.</summary>
        public float Pitch => _pitch;

        /// <summary>The camera's distance from the pivot now.</summary>
        public float Distance => _distance;

        /// <summary>The arena's centre on the board, where Space and V send the pivot.</summary>
        public Vector3 ArenaCentre => _centre;

        /// <summary>How far from the arena's centre the pivot may go, with the current tuning.</summary>
        public float PanLimitRadius => Mathf.Max(0f, _arenaRadius + _panLimitTiles * _tileSpacing);

        private void Awake()
        {
            _view = GetComponent<CinemachineCameraView>();
        }

        /// <summary>
        /// Frames the camera on <paramref name="board"/> for the player in <paramref name="localSeat"/> and cuts
        /// (no glide) to the default view. Called once per scene load, when the board has been built.
        /// </summary>
        public void Frame(BoardView board, int localSeat)
        {
            if (board == null || !board.IsBuilt || board.MapData == null)
            {
                Debug.LogWarning("[ArenaCameraRig] Frame needs a built board; the camera stays where it was authored.", this);
                return;
            }

            MapData map = board.MapData;
            _tileScratch.Clear();
            foreach (MapHex hex in map.Hexes) _tileScratch.Add(board.HexToWorld(hex.Position));
            CameraRigMath.Extent(_tileScratch, out _centre, out _arenaRadius);
            _tileScratch.Clear();
            _tileSpacing = board.Spacing;

            Hex homeHex = localSeat == 0 ? map.SpawnP1 : map.SpawnP2;
            Hex awayHex = localSeat == 0 ? map.SpawnP2 : map.SpawnP1;
            _home = board.HexToWorld(homeHex);
            _away = board.HexToWorld(awayHex);

            _framed = true;
            _yaw = CameraRigMath.ViewYaw(_defaultView, _home, _away);
            GoTo(_defaultView);
            Cut();
        }

        /// <summary>Glides to <paramref name="view"/>: centred on the arena, at that view's turn, tilt and zoom.</summary>
        public void GoTo(ArenaView view)
        {
            if (!_framed) return;

            ViewFraming framing = FramingOf(view);
            _currentView = view;
            _pivotTarget = _centre;
            // The short way round: the target is the view's yaw nearest to wherever the camera has turned to.
            _yawTarget = _yaw + Mathf.DeltaAngle(_yaw, CameraRigMath.ViewYaw(view, _home, _away));
            _pitchTarget = framing.Pitch;
            _distanceTarget = ClampDistance(framing.Distance);
        }

        /// <summary>Jumps to the targets at once and poses the camera there.</summary>
        public void Cut()
        {
            if (!_framed) return;

            _pivot = _pivotTarget;
            _yaw = _yawTarget;
            _pitch = _pitchTarget;
            _distance = _distanceTarget;
            Pose();
        }

        private void Update()
        {
            if (!_framed) return;

            // Unscaled, so the camera still answers while time is stopped (the examine preview does that).
            float deltaTime = Time.unscaledDeltaTime;
            if (IsLive()) ReadInput(deltaTime);

            float step = CameraRigMath.Smoothing(_smoothing, deltaTime);
            _pivot = Vector3.Lerp(_pivot, _pivotTarget, step);
            _yaw = Mathf.Lerp(_yaw, _yawTarget, step);
            _pitch = Mathf.Lerp(_pitch, _pitchTarget, step);
            _distance = Mathf.Lerp(_distance, _distanceTarget, step);
            KeepYawSmall();
            Pose();
        }

        private void ReadInput(float deltaTime)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) GoTo(_defaultView);
                if (keyboard.vKey.wasPressedThisFrame) GoTo(_currentView == ArenaView.SideOn ? ArenaView.BehindYou : ArenaView.SideOn);

                float turn = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                if (_invertRotation) turn = -turn;
                _yawTarget += turn * _rotateSpeed * deltaTime;

                var pan = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                if (pan != Vector2.zero)
                {
                    float zoomScale = _distanceTarget / Mathf.Max(1f, FramingOf(_currentView).Distance);
                    // The yaw the player is looking along now, not the one it is gliding to, so W is always "into the screen".
                    _pivotTarget += CameraRigMath.PanDirection(pan, _yaw) * (_panSpeed * zoomScale * deltaTime);
                }
            }

            // Re-clamped every frame, so a tighter limit tuned in Play Mode pulls the camera back in.
            _pivotTarget = CameraRigMath.ClampToDisc(_pivotTarget, _centre, PanLimitRadius);
            _distanceTarget = ClampDistance(_distanceTarget);

            Mouse mouse = Mouse.current;
            if (mouse == null || (_input != null && _input.PointerOverOverlay)) return;

            // About ±1 per notch on Input System 1.19; clamped so an unnormalised browser value cannot jump the range.
            float notches = Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f);
            if (notches != 0f) _distanceTarget = ClampDistance(_distanceTarget - notches * _zoomSpeed);
        }

        private bool IsLive()
        {
            if (_director == null || _view == null) return true;
            return ReferenceEquals(_director.ActiveView, _view);
        }

        private ViewFraming FramingOf(ArenaView view) => view == ArenaView.BehindYou ? _behindYou : _sideOn;

        private float ClampDistance(float distance)
        {
            float nearest = Mathf.Min(_minDistance, _maxDistance);
            float furthest = Mathf.Max(_minDistance, _maxDistance);
            return Mathf.Clamp(distance, nearest, furthest);
        }

        /// <summary>Free turning adds up forever; shift both yaws by whole turns so the floats stay small.</summary>
        private void KeepYawSmall()
        {
            if (_yaw > -360f && _yaw < 360f) return;
            float whole = Mathf.Floor(_yaw / 360f) * 360f;
            _yaw -= whole;
            _yawTarget -= whole;
        }

        private void Pose()
        {
            transform.SetPositionAndRotation(
                CameraRigMath.OrbitPosition(_pivot, _yaw, _pitch, _distance),
                CameraRigMath.OrbitRotation(_yaw, _pitch));
        }

        private void OnDrawGizmosSelected()
        {
            if (!_framed) return;

            const int Segments = 64;
            float radius = PanLimitRadius;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Vector3 previous = _centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 next = _centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
            Gizmos.DrawWireSphere(_pivot, 0.25f);
        }
    }
}
