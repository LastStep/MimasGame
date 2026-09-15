using System;
using System.Collections.Generic;
using Mimas.Core.Match;

namespace Mimas.Core.Bots
{
    /// <summary>Something that picks a command for a player. Runs on the server (or a local session); never on a remote client.</summary>
    public interface IBot
    {
        /// <summary>The next command for <paramref name="player"/>, or null when it is not their turn or the match is over.</summary>
        Command Choose(MatchState state, int player);
    }

    /// <summary>
    /// Picks a uniformly random legal action and only ends the turn when nothing else is possible. Its own
    /// <see cref="Rng"/> keeps the match RNG untouched, so a bot game and a human game with the same seed
    /// and commands stay identical.
    /// </summary>
    public sealed class RandomBot : IBot
    {
        private readonly Rng _rng;
        private readonly List<Command> _legal = new List<Command>();
        private readonly List<Command> _actions = new List<Command>();

        public RandomBot(uint seed)
        {
            _rng = new Rng(seed);
        }

        public Command Choose(MatchState state, int player)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.EnumerateLegal(player, _legal);
            if (_legal.Count == 0) return null;

            _actions.Clear();
            for (int i = 0; i < _legal.Count; i++)
                if (!(_legal[i] is EndTurnCommand)) _actions.Add(_legal[i]);

            if (_actions.Count == 0) return _legal[_legal.Count - 1];
            return _actions[_rng.Range(0, _actions.Count)];
        }
    }
}
