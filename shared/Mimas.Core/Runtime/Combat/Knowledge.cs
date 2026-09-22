using System;

namespace Mimas.Core.Combat
{
    /// <summary>What a given player has learned about the opponent's hidden things. <c>MatchState</c> implements it.</summary>
    public interface IRevealedKnowledge
    {
        /// <summary>True when <paramref name="viewer"/> has seen <paramref name="id"/> (an ability or modifier) on unit <paramref name="unitId"/>.</summary>
        bool Knows(int viewer, int unitId, string id);

        /// <summary>True when <paramref name="viewer"/> has been shown the boon on unit <paramref name="unitId"/>. Boon ids are kept apart from ability and modifier ids: shipped content shares names between a boon and its modifier on purpose.</summary>
        bool KnowsBoon(int viewer, int unitId, string boonId);
    }

    /// <summary>
    /// The lens a calculation runs through. <see cref="Full"/> is the server's truth; <see cref="For"/> limits
    /// the result to what one player can know, which is how a preview and the actual resolution share one
    /// code path without the preview ever touching a hidden value.
    /// </summary>
    public readonly struct Knowledge
    {
        public static readonly Knowledge Full = new Knowledge(-1, null);

        /// <summary>The player whose knowledge applies, or -1 for full knowledge.</summary>
        public readonly int Viewer;

        private readonly IRevealedKnowledge _revealed;

        private Knowledge(int viewer, IRevealedKnowledge revealed)
        {
            Viewer = viewer;
            _revealed = revealed;
        }

        public bool IsFull => Viewer < 0;

        public static Knowledge For(int viewer, IRevealedKnowledge revealed)
        {
            if (viewer < 0) throw new ArgumentOutOfRangeException(nameof(viewer));
            if (revealed == null) throw new ArgumentNullException(nameof(revealed));
            return new Knowledge(viewer, revealed);
        }

        /// <summary>Whether the viewer can see a hidden thing (an ability or a modifier) carried by a unit owned by <paramref name="ownerPlayer"/>.</summary>
        public bool CanSee(int ownerPlayer, int unitId, string id)
        {
            if (IsFull) return true;
            if (ownerPlayer == Viewer) return true;
            return _revealed.Knows(Viewer, unitId, id);
        }

        /// <summary>Whether the viewer has been shown a boon carried by a unit owned by <paramref name="ownerPlayer"/>.</summary>
        public bool CanSeeBoon(int ownerPlayer, int unitId, string boonId)
        {
            if (IsFull) return true;
            if (ownerPlayer == Viewer) return true;
            return _revealed.KnowsBoon(Viewer, unitId, boonId);
        }
    }
}
