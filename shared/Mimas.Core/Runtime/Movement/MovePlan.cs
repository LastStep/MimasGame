using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// How a unit physically travels: the client picks an animation from this, and rules that care whether
    /// intermediate tiles were touched read <see cref="MovePlan.EnteredTiles"/> instead.
    /// </summary>
    public enum TraversalKind
    {
        /// <summary>Walks every tile of the path.</summary>
        Ground = 0,

        /// <summary>Arcs from origin to destination over whatever lies between.</summary>
        Leap = 1,

        /// <summary>Vanishes and reappears; nothing between is crossed.</summary>
        Blink = 2,
    }

    /// <summary>
    /// A fully resolved, legal move: the rules have already agreed to it. <see cref="Path"/> is the route the
    /// presentation animates (inclusive of both ends). <see cref="EnteredTiles"/> is the subset of tiles the
    /// unit actually sets foot on, in order, excluding the origin: tile effects (fire, traps, pickups) fire for
    /// exactly these. A walk enters every tile of its path; a jump or teleport enters only the destination.
    /// </summary>
    public sealed class MovePlan
    {
        public Hex Origin { get; }
        public Hex Destination { get; }
        public IReadOnlyList<Hex> Path { get; }
        public IReadOnlyList<Hex> EnteredTiles { get; }
        public TraversalKind Traversal { get; }

        /// <summary>Range consumed: movement points for a walk, hex distance for a leap or blink.</summary>
        public int Cost { get; }

        public MovePlan(Hex origin, Hex destination, IReadOnlyList<Hex> path, IReadOnlyList<Hex> enteredTiles, TraversalKind traversal, int cost)
        {
            if (path == null || path.Count < 2) throw new ArgumentException("A move plan needs at least origin and destination.", nameof(path));
            if (path[0] != origin || path[path.Count - 1] != destination) throw new ArgumentException("Path must run from origin to destination.", nameof(path));
            if (enteredTiles == null || enteredTiles.Count == 0 || enteredTiles[enteredTiles.Count - 1] != destination)
                throw new ArgumentException("EnteredTiles must end at the destination.", nameof(enteredTiles));
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
            Origin = origin;
            Destination = destination;
            Path = path;
            EnteredTiles = enteredTiles;
            Traversal = traversal;
            Cost = cost;
        }

        /// <summary>A direct hop (leap or blink): path is origin then destination, only the destination is entered.</summary>
        public static MovePlan Direct(Hex origin, Hex destination, TraversalKind traversal, int cost)
        {
            return new MovePlan(origin, destination, new[] { origin, destination }, new[] { destination }, traversal, cost);
        }

        public override string ToString() => $"{Traversal} {Origin}->{Destination} cost {Cost} ({Path.Count - 1} steps)";
    }

    /// <summary>Why a requested move was refused. <see cref="None"/> means it was accepted.</summary>
    public enum MoveRejectReason
    {
        None = 0,
        UnknownMovement,
        SameTile,
        OffMap,
        OutOfRange,
        NotEnterable,
        Occupied,
        TooHigh,
        PathBlocked,
        NoLineOfSight,
        Unreachable,
    }

    /// <summary>
    /// Outcome of validating one destination. Carries the reason so UI can explain and a bot can learn from
    /// a refusal; the old prototype's silent no-ops made both impossible.
    /// </summary>
    public readonly struct MoveResult
    {
        public readonly bool Ok;
        public readonly MoveRejectReason Reason;
        public readonly MovePlan Plan;

        private MoveResult(bool ok, MoveRejectReason reason, MovePlan plan)
        {
            Ok = ok;
            Reason = reason;
            Plan = plan;
        }

        public static MoveResult Accept(MovePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return new MoveResult(true, MoveRejectReason.None, plan);
        }

        public static MoveResult Reject(MoveRejectReason reason)
        {
            if (reason == MoveRejectReason.None) throw new ArgumentException("Reject needs a reason.", nameof(reason));
            return new MoveResult(false, reason, null);
        }

        public override string ToString() => Ok ? "Ok " + Plan : "Rejected: " + Reason;
    }

    /// <summary>
    /// Every legal move for one unit with one movement ability, in a deterministic order (one plan per
    /// destination). The same object feeds the UI highlight, server-side validation and bot move generation.
    /// </summary>
    public sealed class MovementOptions
    {
        public static readonly MovementOptions Empty = new MovementOptions(new List<MovePlan>(0));

        private readonly List<MovePlan> _plans;
        private readonly Dictionary<Hex, MovePlan> _byDestination;

        public IReadOnlyList<MovePlan> Plans => _plans;

        public int Count => _plans.Count;

        public MovementOptions(List<MovePlan> plans)
        {
            _plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _byDestination = new Dictionary<Hex, MovePlan>(plans.Count);
            for (int i = 0; i < plans.Count; i++)
            {
                if (_byDestination.ContainsKey(plans[i].Destination))
                    throw new ArgumentException($"Two plans share destination {plans[i].Destination}.", nameof(plans));
                _byDestination[plans[i].Destination] = plans[i];
            }
        }

        public bool Contains(Hex destination) => _byDestination.ContainsKey(destination);

        public bool TryGet(Hex destination, out MovePlan plan) => _byDestination.TryGetValue(destination, out plan);
    }
}
