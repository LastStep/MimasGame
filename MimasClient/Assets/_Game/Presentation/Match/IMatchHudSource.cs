using System;
using System.Collections.Generic;

namespace Mimas.Client.Presentation
{
    /// <summary>One entry in the action bar: what to draw, where it sits, and whether it can be picked right now.</summary>
    public readonly struct HudAction
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;

        /// <summary>Presentation icon key from the ability JSON, or null (the HUD then draws a glyph).</summary>
        public readonly string Icon;

        /// <summary>Section of the bar (<c>AbilityCategories</c>: movement, weapon, spell).</summary>
        public readonly string Category;

        public readonly bool Enabled;

        public HudAction(string id, string name, string description, string icon, string category, bool enabled)
        {
            Id = id;
            Name = name;
            Description = description;
            Icon = icon;
            Category = category;
            Enabled = enabled;
        }
    }

    /// <summary>One ability line in the examine panel. Hidden entries are the opponent's unrevealed abilities.</summary>
    public sealed class HudExamineEntry
    {
        public string Name;
        public string Description;
        public string Icon;
        public bool Hidden;
    }

    /// <summary>What the examine panel shows for the unit the player clicked.</summary>
    public sealed class HudExamine
    {
        public string Title;
        public string Subtitle;
        public string Description;
        public List<HudExamineEntry> Abilities = new List<HudExamineEntry>();
    }

    /// <summary>
    /// Everything the in-match HUD reads and the only things it may ask for. The HUD never sees units,
    /// rules or the network: today <c>SkeletonMatchController</c> implements this with a local fake turn
    /// cycle, later the networked match session implements it from <c>PlayerView</c> updates, and the
    /// HUD does not change. <see cref="StateChanged"/> fires for structural changes (actions, armed
    /// action, whose turn, examine target); the clock is polled every frame instead of raising per tick.
    /// </summary>
    public interface IMatchHudSource
    {
        /// <summary>Selectable actions in bar order. Empty until the match is ready.</summary>
        IReadOnlyList<HudAction> Actions { get; }

        /// <summary>Index into <see cref="Actions"/> that is armed (the board is targeting it), or -1 for none. Nothing is armed by default.</summary>
        int ActiveActionIndex { get; }

        /// <summary>True while the local player may act.</summary>
        bool IsMyTurn { get; }

        /// <summary>True when pressing End Turn would do something right now.</summary>
        bool CanEndTurn { get; }

        /// <summary>1-based turn counter shown in the HUD; counts the local player's turns only.</summary>
        int TurnNumber { get; }

        /// <summary>Seconds left on the current turn (whoever's turn it is). Never negative.</summary>
        float TurnSecondsRemaining { get; }

        /// <summary>Length of the current turn in seconds.</summary>
        float TurnSecondsTotal { get; }

        /// <summary>The rope is shown once <see cref="TurnSecondsRemaining"/> drops to this; it burns from full to empty over these seconds.</summary>
        float RopeSeconds { get; }

        /// <summary>The unit being examined, or null when the panel is closed.</summary>
        HudExamine Examine { get; }

        event Action StateChanged;

        /// <summary>Arms <see cref="Actions"/>[index]; the same index again, or -1, disarms. Disabled indices are ignored.</summary>
        void SelectAction(int index);

        /// <summary>Passes the turn early. Ignored when <see cref="CanEndTurn"/> is false.</summary>
        void EndTurn();

        /// <summary>Closes the examine panel.</summary>
        void CloseExamine();
    }
}
