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

        /// <summary>One line of rules shorthand above the description ("Lobbed · no sight needed · range 1-5"), or null.</summary>
        public readonly string Detail;

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

        /// <summary>
        /// A boon changed the cost, range or damage this button shows (ADR-034). The numbers here are already
        /// the changed ones; the mark says so, and it is only ever set for the local unit, whose boons it knows.
        /// The violet edge and diamond (language §5 <c>state.changed</c>).
        /// </summary>
        public readonly bool Modified;

        /// <summary>A Sigil granted it (<c>Unit.BoonOfAbility</c>): the violet edge and triangle (<c>state.added</c>).</summary>
        public readonly bool Added;

        /// <summary>The opponent has not seen it yet (<c>KnownEntry.SeenByOpponent</c> false, ADR-039): the closed eye.</summary>
        public readonly bool UnseenByThem;

        /// <summary>
        /// The examine builder's tile for the same ability, so the bar's hover panel is the plate's panel and an
        /// action cannot say one thing on the bar and another on the plate (ADR-040). Null only when the view
        /// does not list it.
        /// </summary>
        public readonly HudTile Hover;

        public HudAction(string id, string name, string description, string detail, string icon, string category, int cost, bool affordable, bool enabled,
            bool modified = false, bool added = false, bool unseenByThem = false, HudTile hover = null)
        {
            Id = id;
            Name = name;
            Description = description;
            Detail = detail;
            Icon = icon;
            Category = category;
            Cost = cost;
            Affordable = affordable;
            Enabled = enabled;
            Modified = modified;
            Added = added;
            UnseenByThem = unseenByThem;
            Hover = hover;
        }
    }

    /// <summary>Where the series stands for the turn track (docs/ui/hud.md §3.1).</summary>
    public enum HudPhase
    {
        /// <summary>A round is being played, starting or has just ended: the track shows its number.</summary>
        Round,

        /// <summary>The draft is open: the track reads DRAFT and nothing on it is lit.</summary>
        Draft,

        /// <summary>The series result (or the match lost) is showing: the track reads SERIES.</summary>
        Over,
    }

    /// <summary>The four moments the band shows (docs/ui/between-rounds.md §3).</summary>
    public enum HudMomentKind
    {
        RoundStart,
        RoundResult,
        SeriesResult,
        MatchLost,
    }

    /// <summary>
    /// One round moment as data (between-rounds.md §3, ADR-040): the band colours it by <see cref="IWon"/> and
    /// <see cref="IMoveFirst"/>, never by its text. Built by <see cref="HudModel"/>.
    /// </summary>
    public sealed class HudMoment
    {
        public HudMomentKind Kind;

        /// <summary>RoundStart and RoundResult: the round.</summary>
        public int Round;

        /// <summary>RoundStart: the map's display name.</summary>
        public string MapName;

        /// <summary>RoundStart: whether the local seat takes the first turn.</summary>
        public bool IMoveFirst;

        /// <summary>RoundResult and SeriesResult: whether the local seat took it.</summary>
        public bool IWon;

        /// <summary>RoundResult: the name of whoever took the round.</summary>
        public string WinnerName;

        /// <summary>The score after it, the local seat's first.</summary>
        public int ScoreMine, ScoreTheirs;

        /// <summary>How it ended ("by elimination", "opponent resigned"); MatchLost: why there is no result.</summary>
        public string Reason;

        /// <summary>SeriesResult (a beat after it lands) and MatchLost: the way out is showing.</summary>
        public bool ShowBack;

        /// <summary>"Back to room" online (the room outlives the match, ADR-032), "Back to lobby" in practice.</summary>
        public string BackLabel;
    }

    /// <summary>One of the enemy's boons on their tag: its kind once revealed, null while it is a "?" (docs/ui/hud.md §3.5).</summary>
    public sealed class HudBoonMark
    {
        /// <summary>"blessing" / "enchant" / "sigil", or null when unrevealed.</summary>
        public string Kind;
    }

    /// <summary>
    /// What the examine plate shows for the unit the player clicked (docs/ui/examine.md §3, spec E §7.2):
    /// the inventory with every flag already resolved, so the view formats numbers and picks colours and
    /// never touches a Core type. Every string is composed by <c>ExamineModelBuilder</c>, which has the
    /// catalogue. Nothing in it is a number the rules would not let the viewer see (#hidden-info).
    /// </summary>
    public sealed class HudExamine
    {
        public int UnitId;

        /// <summary>The seat's name ("You", "Random Bot" in practice; the room's names online), or the prop's name.</summary>
        public string Name;

        /// <summary>"Greek · your hero" / "Norse · the enemy" / "Unknown lineage" / "Terrain".</summary>
        public string LineageLine;

        /// <summary>The lineage's display name, or null when unknown.</summary>
        public string LineageName;

        /// <summary>Emblem glyph key ("laurel", "hammer", "lotus"), or null when unknown.</summary>
        public string LineageIcon;

        /// <summary>The painting's hues (<c>#rrggbb</c>), or null when the lineage is unknown: the view falls back to grey.</summary>
        public string HueDark, HueLight;

        public bool IsMine;

        /// <summary>A prop: the reduced plate (name, height, health; no stats, boons or equipment).</summary>
        public bool IsProp;

        /// <summary>A prop's one line of description; null for a hero.</summary>
        public string Description;

        public int Hp, MaxHp, Ap, ApPerTurn, Height;

        /// <summary>Six, in inventory order: hp, ap, power.weapon, power.spell, defense.weapon, defense.spell.</summary>
        public List<HudStat> Stats = new List<HudStat>();

        /// <summary>Oldest to latest (E8): yours in grant order; theirs in the order you learned them, unknown last.</summary>
        public List<HudBoon> Boons = new List<HudBoon>();

        /// <summary>Four, slot order.</summary>
        public List<HudItem> Items = new List<HudItem>();

        public HudStat FindStat(string id)
        {
            for (int i = 0; i < Stats.Count; i++) if (Stats[i].Id == id) return Stats[i];
            return null;
        }

        public HudItem FindItem(string slot)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Slot == slot) return Items[i];
            return null;
        }
    }

    /// <summary>One cell of the stats grid: base (rules base + items, public) then net (what known boons changed).</summary>
    public sealed class HudStat
    {
        /// <summary><c>hp</c>, <c>ap</c>, <c>power.weapon</c>, <c>power.spell</c>, <c>defense.weapon</c>, <c>defense.spell</c>.</summary>
        public string Id;

        /// <summary>The UXML name suffix: hp, ap, strength, magic, armour-weapon, armour-spell.</summary>
        public string Element;

        /// <summary>"Health", "Actions", "Strength", "Magic", "Armour · weapon", "Armour · spell".</summary>
        public string Label;

        /// <summary>Glyph key.</summary>
        public string Icon;

        public int Base;
        public int Net;

        /// <summary>Theirs, a lane stat, and a boon of theirs is still unrevealed: a grey "?" instead of the net (E4).</summary>
        public bool Hidden;

        /// <summary>The hover panel's rows: base, each item, each known boon, and a "?" row when hidden.</summary>
        public List<HudStatLine> Lines = new List<HudStatLine>();
    }

    public sealed class HudStatLine
    {
        public string Label;
        public int Amount;

        /// <summary>The "unrevealed boons · ?" row.</summary>
        public bool Unknown;
    }

    /// <summary>One boon row. An unrevealed one carries only <see cref="Revealed"/> = false and the reveal sentence.</summary>
    public sealed class HudBoon
    {
        public string Id;
        public string Name;

        /// <summary>"Blessing" / "Enchant" / "Sigil", or null when unrevealed.</summary>
        public string Kind;
        public string God;
        public string LineageName;

        /// <summary>The item it sits on ("Longbow"), or null for a Blessing.</summary>
        public string OnItemName;
        public string Description;

        /// <summary>"starting Blessing" / "drafted".</summary>
        public string Badge;

        /// <summary>The hover panel's type line: "Blessing · Greek · on you", "drafted · not yet revealed".</summary>
        public string TypeLine;

        /// <summary>"Athena answers those who pray to the Greek gods."</summary>
        public string Flavour;

        public bool Revealed;

        /// <summary>The Blessing the lineage starts you with: its circle is filled.</summary>
        public bool Starting;
    }

    /// <summary>One item group: square, name, its stat line, and its actions as tiles.</summary>
    public sealed class HudItem
    {
        public string Id;
        public string Name;

        /// <summary>weapon / crown / boots / armour.</summary>
        public string Slot;
        public string Kind;

        /// <summary>The hover panel's type line: "weapon · bow".</summary>
        public string Quick;
        public string Description;

        /// <summary>"+2 Strength", in the up colour on the plate; null when the item adds nothing.</summary>
        public string StatLine;

        /// <summary>The armour's grey line in place of tiles ("turns 2 of every blow"), else null.</summary>
        public string Note;

        /// <summary>Theirs only: "You have seen 1 of its 2 abilities."; null for yours.</summary>
        public string SeenLine;

        public List<HudTile> Tiles = new List<HudTile>();

        /// <summary>The known boons on this item, for the hover panel's Changes. Never drawn on the plate (E7).</summary>
        public List<string> BoonNames = new List<string>();
    }

    /// <summary>One action tile: icon and name on the plate; everything else waits in the hover panel.</summary>
    public sealed class HudTile
    {
        public string Id;
        public string Name;

        /// <summary>The ability's icon key, or null. The tile shows <see cref="Letter"/> until action art exists.</summary>
        public string Icon;
        public string Letter;

        /// <summary>"weapon attack · Longbow · action", "movement · innate", or "ability · Flintlock" when unseen.</summary>
        public string TypeLine;
        public string Description;

        public bool IsMovement;

        /// <summary>False: the dashed "?" tile, "unseen".</summary>
        public bool Revealed;

        /// <summary>An Enchant changed one of its numbers (violet edge, diamond).</summary>
        public bool Changed;

        /// <summary>A Sigil granted it (violet edge, triangle).</summary>
        public bool Added;

        /// <summary>Your own, revealed to you and not yet seen by the opponent (ADR-039): the closed eye on the bar, one line in the panel.</summary>
        public bool UnseenByThem;

        /// <summary>Null for an unseen tile.</summary>
        public HudTileNumbers Numbers;

        /// <summary>"reach 5 → 6 · Apollo's Bowstring", "granted by Nike's Jab".</summary>
        public List<string> Changes = new List<string>();

        /// <summary>"lobbed, clears cover", "no sight needed", "one target"; for movement its mode's own words.</summary>
        public List<string> Conditions = new List<string>();

        /// <summary>"3 base + 3 Strength − their armour" for an attack; null for movement.</summary>
        public string DamageLine;
    }

    public sealed class HudTileNumbers
    {
        public int Cost;
        public int? Damage, Apex;
        public int? RangeMin, RangeMax;
        public string Element;
        public bool CostChanged, RangeChanged, DamageChanged;
    }

    /// <summary>One of the three cards a draft offers (design: #draft; decided 22 Sep 2026, P1).</summary>
    public sealed class HudDraftCard
    {
        public string Id;
        public string Name;

        /// <summary>"Blessing" / "Enchant" / "Sigil" — what kind of boon this is, and the colour of the card.</summary>
        public string Kind;

        /// <summary>The god: the name up to its first apostrophe ("Hera" from "Hera's Resolve"), or the whole name.</summary>
        public string God;

        /// <summary>The lineage's name, for the line under the god.</summary>
        public string Lineage;

        /// <summary>The boon's own description, in its own words.</summary>
        public string Effect;

        /// <summary>Where it lands: "On you" for a Blessing, "On your Longbow" for anything with a slot.</summary>
        public string Attach;

        public string Icon;

        /// <summary>The lineage's painting hues (<c>#rrggbb</c>) for the card's wash; null when the lineage is unknown.</summary>
        public string HueDark, HueLight;

        /// <summary>The slot it lands in (<c>requires.slot</c>), or null for a Blessing: the glyph at the card's foot.</summary>
        public string Slot;
    }

    /// <summary>
    /// The draft, drawn over the dimmed board between rounds. Null whenever no draft is open; the HUD shows
    /// nothing and the board is undimmed then.
    /// </summary>
    public sealed class HudDraft
    {
        /// <summary>"ROUND 2 TO GUEST-2869 · 1 – 1" — the round that just ended, who took it, the score yours first.</summary>
        public string Headline;

        /// <summary>"Choose one boon · Round 2 on Board".</summary>
        public string NextRoundLine;

        public List<HudDraftCard> Cards = new List<HudDraftCard>();

        /// <summary>Index of the highlighted card, or -1.</summary>
        public int Selected = -1;

        /// <summary>This seat has picked; the cards lock and the status says who we are waiting for.</summary>
        public bool Picked;

        public bool OpponentPicked;

        /// <summary>"Guest-4471 has picked" / "Waiting for Guest-4471…".</summary>
        public string Status;

        public float SecondsRemaining;
        public float SecondsTotal;
    }

    /// <summary>
    /// One unit's floating overlay: hit points, the enemy's boon marks and lineage, anchored to a
    /// transform in the world. The session mutates the numbers as events play; the HUD repositions the
    /// element every frame from <see cref="Anchor"/>.
    /// </summary>
    public sealed class HudUnit
    {
        public int Id;
        public bool IsMine;

        /// <summary>True for a destructible prop: a shorter, neutral bar and no action points. Walls get no tag at all.</summary>
        public bool IsProp;

        public bool IsAlive = true;
        public int Hp;
        public int MaxHp;
        public int Ap;
        public int ApPerTurn;

        /// <summary>Drawn at full opacity (hovered, targeted, or something is armed); otherwise faded.</summary>
        public bool Emphasised;

        /// <summary>The examine plate is open for this unit: its tag wears the owner's ring (ex.ring).</summary>
        public bool Examined;

        /// <summary>Hit points the armed attack would remove if it landed on this unit, or 0 (ghosted on the bar).</summary>
        public int GhostDamage;

        /// <summary>The lineage's name once it has been revealed, drawn on the nameplate; null until then.</summary>
        public string LineageTag;

        public Transform Anchor;
        public Vector3 AnchorOffset;

        /// <summary>The enemy's only: one mark per boon they hold, in grant order — its kind once revealed, a "?" until then.</summary>
        public List<HudBoonMark> BoonMarks = new List<HudBoonMark>();
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

        /// <summary>The armed ability's icon key; the panel shows its letter until action art exists.</summary>
        public string AbilityIcon;

        /// <summary>Who or what it is aimed at: the seat's name, or the prop's.</summary>
        public string TargetName;

        /// <summary>"lobbed", "straight", "from the sky": the type line's second half.</summary>
        public string TrajectoryWord;

        public int Total;
        public bool IsExact;

        /// <summary>
        /// Why this shot would be refused ("No line of sight", "Trajectory blocked", "Out of range",
        /// "Cannot be hit"), or null when it is legal. The damage below it is then what the shot <em>would</em>
        /// do, and the target's bar shows no ghost.
        /// </summary>
        public string BlockedReason;

        /// <summary>True when the target is a prop rather than a hero; <see cref="TargetUnitId"/> is then its body id.</summary>
        public bool TargetIsProp;

        public List<HudPreviewLine> Lines = new List<HudPreviewLine>();
    }

    /// <summary>A short-lived floating text over a unit: the damage that landed and, when it differed from the preview, why.</summary>
    public sealed class HudFlyover
    {
        public int UnitId;
        public Vector3 WorldPosition;
        public string Headline;
        public string Detail;

        /// <summary>A reveal's glyph (the boon's kind, or the lineage's emblem), drawn in <c>them</c>; null for damage, whose number is amber.</summary>
        public string Glyph;
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

        /// <summary>Damage preview for the hovered target of the armed attack, or null.</summary>
        HudPreview Preview { get; }

        /// <summary>
        /// A short label pinned next to the cursor ("Out of range", "Cannot be hit"), or null. It answers the
        /// question the player is asking with the cursor, where they are looking, rather than in a panel.
        /// </summary>
        string CursorTag { get; }

        /// <summary>Cursor position in screen pixels, for placing <see cref="CursorTag"/>.</summary>
        Vector2 CursorScreenPosition { get; }

        /// <summary>The open draft, or null when there is none.</summary>
        HudDraft Draft { get; }

        /// <summary>
        /// The round moment in the band (round card, round result, series result, match lost), or null when none
        /// is showing (docs/ui/between-rounds.md §3). Its way out reads <c>ShowBack</c> and <c>BackLabel</c>.
        /// </summary>
        HudMoment Moment { get; }

        /// <summary>Your own name for your end of the track: the seat's name online, "You" in practice.</summary>
        string MyName { get; }

        /// <summary>Who is on the other side, for their end of the track. Null before the match is ready.</summary>
        string OpponentName { get; }

        /// <summary>Rounds each side has won this series, yours and theirs (the pips, P2).</summary>
        int ScoreMine { get; }
        int ScoreTheirs { get; }

        /// <summary>Rounds to take the series: the pips per side (2 at best of 3).</summary>
        int RoundsToWin { get; }

        /// <summary>The round being played or just finished; 0 before the first.</summary>
        int RoundNumber { get; }

        /// <summary>Round, draft or over: what the middle of the track says.</summary>
        HudPhase Phase { get; }

        /// <summary>Your boons, oldest to latest, as the examine plate builds them: the circles down the right edge.</summary>
        IReadOnlyList<HudBoon> MyBoons { get; }

        /// <summary>
        /// A line about the opponent's connection, or null when there is nothing to say
        /// ("Opponent disconnected · 47 s", "Reconnecting…"). The HUD hides the row when it is null.
        /// </summary>
        string OpponentStatus { get; }

        /// <summary>False when resigning would do nothing: local practice, or a match that is over.</summary>
        bool CanResign { get; }

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

        /// <summary>Highlights one of the draft's cards; -1 clears. Ignored when no draft is open or this seat has picked.</summary>
        void SelectDraftCard(int index);

        /// <summary>Keeps the selected card. Ignored when nothing is selected, no draft is open, or this seat has picked.</summary>
        void ConfirmDraft();

        /// <summary>Concedes the match. The HUD asks twice before calling this; ignored when <see cref="CanResign"/> is false.</summary>
        void Resign();

        /// <summary>Leaves the finished match: the lobby online, a fresh Arena in practice.</summary>
        void BackToLobby();
    }
}
