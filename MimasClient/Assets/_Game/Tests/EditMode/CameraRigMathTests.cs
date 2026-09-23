using System.Collections.Generic;
using Mimas.Client.Presentation;
using Mimas.Core.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace Mimas.Client.Tests
{
    /// <summary>
    /// The camera's framing (spec <c>docs/specs/2026-09-23-camera.md</c> §3): Side On puts your own end of the
    /// arena on the left whichever seat you hold, Behind You looks from your spawn at the opponent's, Side On for
    /// seat 1 is the camera the Arena was authored with, and the pan limit is a disc round the arena's centre.
    /// Spawns are open field's (<c>board-3</c>): p1 at (−3, 0), p2 at (3, 0).
    /// </summary>
    public class CameraRigMathTests
    {
        private const float TileSize = 1f;
        private static readonly Vector3 P1 = HexLayout.HexToWorld(new Hex(-3, 0), TileSize);
        private static readonly Vector3 P2 = HexLayout.HexToWorld(new Hex(3, 0), TileSize);

        private static void Spawns(int seat, out Vector3 home, out Vector3 away)
        {
            home = seat == 0 ? P1 : P2;
            away = seat == 0 ? P2 : P1;
        }

        [Test]
        public void ViewYaw_SideOnForSeatOne_IsTheAuthoredArenaCamera()
        {
            float yaw = CameraRigMath.ViewYaw(ArenaView.SideOn, P1, P2);
            Vector3 position = CameraRigMath.OrbitPosition(Vector3.zero, yaw, 50f, 21f);
            Quaternion rotation = CameraRigMath.OrbitRotation(yaw, 50f);

            // vcam_Tilted as authored: (0, 16.1, −13.5), pitched 50° down, facing +Z.
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, yaw), 1e-3f);
            Assert.Less(Vector3.Distance(new Vector3(0f, 16.1f, -13.5f), position), 0.05f, "position " + position);
            Assert.Less(Quaternion.Angle(new Quaternion(0.4226183f, 0f, 0f, 0.9063078f), rotation), 0.01f);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ViewYaw_SideOn_PutsYourSpawnOnTheLeft(int seat)
        {
            Vector3 home, away;
            Spawns(seat, out home, out away);

            Vector3 right = CameraRigMath.OrbitRotation(CameraRigMath.ViewYaw(ArenaView.SideOn, home, away), 50f) * Vector3.right;

            Assert.Less(Vector3.Dot(right, home), Vector3.Dot(right, away), "seat " + (seat + 1) + "'s spawn is not left of the opponent's");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ViewYaw_BehindYou_LooksFromYourSpawnAtTheirs(int seat)
        {
            Vector3 home, away;
            Spawns(seat, out home, out away);
            float yaw = CameraRigMath.ViewYaw(ArenaView.BehindYou, home, away);

            Vector3 forward = CameraRigMath.OrbitRotation(yaw, 45f) * Vector3.forward;
            Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 camera = CameraRigMath.OrbitPosition(Vector3.zero, yaw, 45f, 21f);

            Assert.Greater(Vector3.Dot(flat, (away - home).normalized), 0.999f, "not facing the opponent's end");
            Assert.Greater(Vector3.Dot(camera, home), 0f, "the camera is not on your side of the arena");
        }

        [Test]
        public void ClampToDisc_PointInside_ComesBackUnchanged()
        {
            var inside = new Vector3(1f, 2f, -1f);

            Assert.AreEqual(inside, CameraRigMath.ClampToDisc(inside, Vector3.zero, 5f));
        }

        [Test]
        public void ClampToDisc_PointOutside_LandsOnTheRimKeepingItsHeight()
        {
            Vector3 clamped = CameraRigMath.ClampToDisc(new Vector3(30f, 2f, 40f), new Vector3(10f, 0f, 0f), 5f);

            float dx = clamped.x - 10f;
            Assert.AreEqual(5f, Mathf.Sqrt(dx * dx + clamped.z * clamped.z), 1e-4f);
            Assert.AreEqual(2f, clamped.y);
            Assert.Greater(dx, 0f);
            Assert.Greater(clamped.z, 0f);
        }

        [Test]
        public void Extent_RadiusThreeBoard_IsCentredWithThreeTilesToTheEdge()
        {
            var centres = new List<Vector3>();
            foreach (Hex hex in Hex.Spiral(new Hex(0, 0), 3)) centres.Add(HexLayout.HexToWorld(hex, TileSize));

            Vector3 centre;
            float radius;
            CameraRigMath.Extent(centres, out centre, out radius);

            Assert.AreEqual(37, centres.Count);
            Assert.Less(centre.magnitude, 1e-4f);
            Assert.AreEqual(3f * HexLayout.TileWidth(TileSize), radius, 1e-4f);
        }

        [Test]
        public void PanDirection_ForwardAndDiagonal_FollowTheYawAtUnitSpeed()
        {
            Vector3 forward = CameraRigMath.PanDirection(new Vector2(0f, 1f), 90f);
            Vector3 diagonal = CameraRigMath.PanDirection(new Vector2(1f, 1f), 0f);

            Assert.Less(Vector3.Distance(Vector3.right, forward), 1e-4f, "W at yaw 90 should pan towards +X");
            Assert.AreEqual(1f, diagonal.magnitude, 1e-4f);
        }

        [Test]
        public void Smoothing_ZeroSharpness_Snaps()
        {
            Assert.AreEqual(1f, CameraRigMath.Smoothing(0f, 0.016f));
            Assert.That(CameraRigMath.Smoothing(10f, 0.016f), Is.InRange(0.01f, 0.99f));
        }
    }
}
