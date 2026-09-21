#nullable disable

using System.Linq;
using Mimas.Core.Combat;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// Two refusals a player actually meets on <b>arena-4</b>, written down rather than changed. Rohan's
    /// playtest of 21 Sep 2026 reported shots refused "where the picture looks open" (T-0006); these are
    /// the two shapes that produces, and in both of them the rule is right and the picture is wrong. The
    /// fix is presentation — the blocking tile is tinted and a pillar is drawn the size of the hex it
    /// blocks — so these tests exist to prove nothing in <c>Mimas.Core</c> moved while that happened.
    /// <para>
    /// Design: <c>#line-of-sight</c> rule 5 (a grazing line is blocked if <em>either</em> flanking hex
    /// blocks — supercover, so a shot is never legal one way and illegal the other) and rule 3 (bodies
    /// block, with the attacker's and the target's own bodies ignored). Map: four pillars at (±2,∓1) and
    /// (±2,±2), body 6; a hero's aim point is 4 above its tile, so a pillar's top at 6 clears every ray
    /// between two ground tiles.
    /// </para>
    /// </summary>
    public class SightArena4Tests
    {
        private static TargetCheck Check(MatchState state, Unit attacker, AttackDef attack, Hex target)
            => AttackTargeting.Check(state.Map, state.Bodies, state.Catalog.Rules.Heights, state.Trajectories, attacker, attack, target);

        /// <summary>arena-4 with both heroes carrying the gun kit, started, so a seat can be moved anywhere.</summary>
        private static MatchState Arena()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = new MatchState(catalog, new MatchSetup("arena-4", ContentFixtures.GunKit, ContentFixtures.GunKit), 1);
            state.Start();
            return state;
        }

        [Fact]
        public void IsClear_Arena4_GrazingPillar_NamesFlankingHex()
        {
            MatchState state = Arena();
            AttackDef gun = state.Catalog.GetAttack("quick-shot");   // direct, needs sight, range 7
            Unit shooter = state.Units.Get(0);
            Unit enemy = state.Units.Get(1);

            // Two hexes apart, both flat ground, well inside range, and the hex the game draws between
            // them — (-1,-2) — is empty. The pillar sits beside that line, at (-2,-2).
            var from = new Hex(-1, -3);
            var to = new Hex(-2, -1);
            var pillar = new Hex(-2, -2);
            shooter.MoveTo(from);
            enemy.MoveTo(to);

            Assert.Equal("pillar", ((Prop)state.Bodies.All.Single(b => b.Position == pillar)).Def.Id);
            Assert.DoesNotContain(pillar, Hex.Line(from, to));
            Assert.Equal(2, Hex.Distance(from, to));

            TargetCheck check = Check(state, shooter, gun, to);

            // Rule 5: the ray runs along the edge those two hexes share, and either of them blocking is
            // enough. So the refusal names a pillar the player can see is *not* in the way — which is
            // exactly what "refused where the picture looks open" felt like from the other side.
            Assert.Equal(TargetRejectReason.NoLineOfSight, check.Reason);
            Assert.True(check.HasBlockedAt);
            Assert.Equal(pillar, check.BlockedAt);

            // And it is symmetric, as rule 1 promises: neither of them can shoot the other.
            shooter.MoveTo(to);
            enemy.MoveTo(from);
            Assert.Equal(pillar, Check(state, shooter, gun, from).BlockedAt);
        }

        [Fact]
        public void IsClear_Arena4_HeroBodyBlocksShotAtPillar_NamesHeroHex()
        {
            MatchState state = Arena();
            AttackDef gun = state.Catalog.GetAttack("quick-shot");
            Unit shooter = state.Units.Get(0);
            Unit enemy = state.Units.Get(1);

            // Straight down one column: shooter, the enemy hero, then the pillar the shooter is aiming at.
            var from = new Hex(2, -4);
            var between = new Hex(2, -2);
            var pillar = new Hex(2, -1);
            shooter.MoveTo(from);
            enemy.MoveTo(new Hex(4, 0));

            // The pillar is a legal target with nobody in the way: it has hit points, so it can be shot.
            Assert.True(Check(state, shooter, gun, pillar).Ok);

            enemy.MoveTo(between);
            TargetCheck check = Check(state, shooter, gun, pillar);

            // Rule 3: a living body's height is part of its column, and only the attacker's and the
            // target's own bodies are ignored. The blocker here is the opponent, and the hex named is
            // the one they are standing on.
            Assert.Equal(TargetRejectReason.NoLineOfSight, check.Reason);
            Assert.True(check.HasBlockedAt);
            Assert.Equal(between, check.BlockedAt);

            // Sight and the flight are separate fields over the same geometry: the same shot with sight
            // switched off is refused by the trajectory instead, and names the same hex. Whichever
            // refusal a client receives, the tile it should tint is the same one.
            TargetCheck blind = Check(state, shooter, gun.WithTrajectory(Trajectories.Direct, 0, false), pillar);
            Assert.Equal(TargetRejectReason.TrajectoryBlocked, blind.Reason);
            Assert.Equal(between, blind.BlockedAt);
        }
    }
}
