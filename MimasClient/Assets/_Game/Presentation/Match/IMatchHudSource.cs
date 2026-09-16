using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>One entry in the action bar: what to draw, where it sits, what it costs and whether it can be picked right now.</summary>
    public readonly struct HudAction
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;

        /// <summary>Presentation icon key from the ability JSON, or null (the HUD then draws a glyph).</summary>
        public readonly string Icon;

        /// <summary>Section of the bar (<c>AbilityCategories</c>: movement, weapon, spell).</summary>
        public readonly string Category;

        /// <summary>Action points one use spends.</summary>
        public readonly int Cost;

        /// <summary>False when the acting unit cannot pay <see cref="Cost"/> right now (drawn desaturated with a red cost).</summary>
        public readonly bool Affordable;

        /// <summary>True when clicking it arms it: the player's turn, nothing playing, and affordable.</summary>
        public readonly bool Enabled;

        public HudAction(string id, string name, string description, string icon, string category, int cost, bool affordable, bool enabled)
        {
            Id = id;
            Name = name;
            Description = description;
            Icon = icon;
            Category = category;
            Cost = cost;
            Affordable = affordable;
            Enabled = enabled;
        }
    }

    /// <summary>One ability or passive line in the examine panel. Hidden entries are the opponent's unrevealed ones.</summary>
    public sealed class HudExamineEntry
    {
        public string Name;
        public string Description;
        public string Icon;
        public bool Hidden;

        /// <summary>Caption the entry sits under (the granting item's name, or "Innate"), or null for no grouping.</summary>
        public string Group;
    }

    /// <summary>What the examine panel shows for the unit the player clicked.</summary>
    public sealed class HudExamine
    {
        public string Title;
        public string Subtitle;
        public string Description;
        public int Hp;
        public int MaxHp;
        public int Ap;
        public int ApPerTurn;
        public List<HudExamineEntry> Items = new List<HudExamineEntry>();
        public List<HudExamineEntry> Abilities = new List<HudExamineEntry>();
        public List<HudExamineEntry> Modifiers = new List<HudExamineEntry>();
    }

    /// <summary>A revealed passive drawn as a small icon under a unit's bar.</summary>
    public sealed class HudMarker
    {
        public string Id;
        public string Name;
        public string Icon;
    }

    /// <summary>
    /// One unit's floating overlay: hit points, action points and revealed passives, anchored to a
    /// transform in the world. The session mutates the numbers as events play; the HUD repositions the
    /// element every frame from <see cref="Anchor"/>.
    /// </summary>
    public sealed class HudUnit
    {
        public int Id;
        public bool IsMine;
        public bool IsAlive = true;
        public int Hp;
        public int MaxHp;
        public int Ap;
        public int ApPerTurn;

        /// <summary>Drawn at full opacity (hovered, targeted, or something is armed); otherwise faded.</summary>
        public bool Emphasised;

        /// <summary>Hit points the armed attack would remove if it landed on this unit, or 0 (ghosted on the bar).</summary>
        public int GhostDamage;

        public Transform Anchor;
        public Vector3 AnchorOffset;
        public List<HudMarker> Markers = new List<HudMarker>();
    }

    /// <summary>One row of the attack preview tooltip.</summary>
    public sealed class HudPreviewLine
    {
        public string Label;
        public int Amount;

        /// <summary>A "?" row standing in for passives the player cannot see.</summary>
        public bool Unknown;
    }

    /// <summary>The damage the local player can predict for the hovered target.</summary>
    public sealed class HudPreview
    {
        public int TargetUnitId;
        public string AbilityName;
        public int Total;
        public bool IsExact;
        public List<HudPreviewLine> Lines = new List<HudPreviewLine>();
    }

    /// <summary>A short-lived floating text over a unit: the damage that landed and, when it differed from the preview, why.</summary>
    public sealed class HudFlyover
    {
        public int UnitId;
        public Vector3 WorldPosition;
        public string Headline;
        public string Detail;
    }

    /// <summary>
    /// Everything the in-match HUD reads and the only things it may ask for. The HUD never sees units,
    /// rules or the network: <c>LocalMatchSession</c> implements this from a local <c>MatchState</c>
    /// through its <c>PlayerView</c> projection; later the networked session implements it from server
    /// updates and the HUD does not change. <see cref="StateChanged"/> fires for structural changes
    /// (actions, armed action, whose turn, examine target, preview); the clock, unit anchors and flyovers
    /// are polled or pushed every frame.
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

        /// <summary>Action points the local acting unit has left this turn.</summary>
        int ApCurrent { get; }

        /// <summary>Action points the local acting unit gets every turn (number of dots).</summary>
        int ApPerTurn { get; }

        /// <summary>Seconds left on the current turn (whoever's turn it is). Never negative.</summary>
        float TurnSecondsRemaining { get; }

        /// <summary>Length of the current turn in seconds.</summary>
        float TurnSecondsTotal { get; }

        /// <summary>The rope is shown once <see cref="TurnSecondsRemaining"/> drops to this; it burns from full to empty over these seconds.</summary>
        float RopeSeconds { get; }

        /// <summary>The unit being examined, or null when the panel is closed.</summary>
        HudExamine Examine { get; }

        /// <summary>Every unit's overlay, living or dead. Same objects across frames; numbers change in place.</summary>
        IReadOnlyList<HudUnit> Units { get; }

        /// <summary>Damage preview for the hovered legal target of the armed attack, or null.</summary>
        HudPreview Preview { get; }

        /// <summary>Centre-screen text once the match is over ("VICTORY" / "DEFEAT"), else null.</summary>
        string Banner { get; }

        /// <summary>The camera the board is rendered with, for anchoring overlays.</summary>
        Camera WorldCamera { get; }

        event Action StateChanged;

        /// <summary>Raised when damage lands, once per hit.</summary>
        event Action<HudFlyover> Flyover;

        /// <summary>Arms <see cref="Actions"/>[index]; the same index again, or -1, disarms. Disabled indices are ignored.</summary>
        void SelectAction(int index);

        /// <summary>Passes the turn early. Ignored when <see cref="CanEndTurn"/> is false.</summary>
        void EndTurn();

        /// <summary>Closes the examine panel.</summary>
        void CloseExamine();
    }
}
