using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Mimas.Client.Presentation;
using Mimas.Core.Data;

namespace Mimas.Client.UI
{
    /// <summary>
    /// The in-match HUD: sectioned action bar (bottom centre), turn owner with a burning rope for the last
    /// seconds (top centre), examine panel (left), End Turn (bottom right). Reads everything from an
    /// <see cref="IMatchHudSource"/> and asks it for exactly three things: arm or disarm an action, end the
    /// turn, close examine. Mouse only for now. Tells the board input controller when the pointer is over a
    /// panel so clicks do not fall through to tiles. Layout lives in MatchHud.uxml / MatchHud.uss; this
    /// class only fills slots and toggles classes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        /// <summary>Bar order and captions per category. Unknown categories are appended with their key upper-cased.</summary>
        private static readonly string[][] Groups =
        {
            new[] { AbilityCategories.Movement, "MOVE" },
            new[] { AbilityCategories.Weapon, "WEAPONS" },
            new[] { AbilityCategories.Spell, "SPELLS" },
        };

        [Tooltip("Any component implementing IMatchHudSource (today: SkeletonMatchController).")]
        [SerializeField] private MonoBehaviour _sourceBehaviour;

        [Tooltip("Board input to shield from clicks that land on the HUD.")]
        [SerializeField] private BoardInputController _boardInput;

        [Tooltip("Sprites matched to ability 'icon' keys by sprite name. Missing keys fall back to a letter glyph.")]
        [SerializeField] private Sprite[] _icons;

        private readonly Dictionary<string, Sprite> _iconsByKey = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly List<VisualElement> _slots = new List<VisualElement>();
        private readonly List<string> _groupOrder = new List<string>();
        private readonly StringBuilder _signature = new StringBuilder();

        private UIDocument _document;
        private IMatchHudSource _source;
        private Func<Vector2, bool> _pointerBlocker;

        private VisualElement _root;
        private VisualElement _turnPanel;
        private VisualElement _rope;
        private VisualElement _ropeFill;
        private VisualElement _ropeEmber;
        private VisualElement _actionBar;
        private VisualElement _tooltip;
        private VisualElement _examine;
        private VisualElement _examineAbilities;
        private Label _turnOwner;
        private Label _tooltipTitle;
        private Label _tooltipBody;
        private Label _examineTitle;
        private Label _examineSubtitle;
        private Label _examineDescription;
        private Button _examineClose;
        private Button _endTurn;

        private bool _bound;
        private bool _ropeVisible;
        private string _barSignature;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _source = _sourceBehaviour as IMatchHudSource;
            if (_source == null)
            {
                Debug.LogError("[MatchHudView] _sourceBehaviour must implement IMatchHudSource.", this);
                enabled = false;
                return;
            }

            _iconsByKey.Clear();
            if (_icons != null)
            {
                for (int i = 0; i < _icons.Length; i++)
                {
                    Sprite sprite = _icons[i];
                    if (sprite == null) continue;
                    _iconsByKey[sprite.name] = sprite;
                }
            }

            _pointerBlocker = IsPointerOverHud;
        }

        private void OnEnable()
        {
            if (_source == null) return;
            _source.StateChanged += Refresh;
            if (_boardInput != null) _boardInput.SetPointerBlocker(_pointerBlocker);
            TryBind();
        }

        private void OnDisable()
        {
            if (_source != null) _source.StateChanged -= Refresh;
            if (_boardInput != null) _boardInput.SetPointerBlocker(null);
            Unbind();
        }

        private void Update()
        {
            if (!_bound && !TryBind()) return;
            UpdateRope();
        }

        // ---- binding --------------------------------------------------------------------------------

        private bool TryBind()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null) return false;

            _root = root.Q<VisualElement>("hud-root");
            _turnPanel = root.Q<VisualElement>("turn-panel");
            _turnOwner = root.Q<Label>("turn-owner");
            _rope = root.Q<VisualElement>("rope");
            _ropeFill = root.Q<VisualElement>("rope-fill");
            _ropeEmber = root.Q<VisualElement>("rope-ember");
            _actionBar = root.Q<VisualElement>("action-bar");
            _tooltip = root.Q<VisualElement>("tooltip");
            _tooltipTitle = root.Q<Label>("tooltip-title");
            _tooltipBody = root.Q<Label>("tooltip-body");
            _examine = root.Q<VisualElement>("examine");
            _examineTitle = root.Q<Label>("examine-title");
            _examineSubtitle = root.Q<Label>("examine-subtitle");
            _examineDescription = root.Q<Label>("examine-description");
            _examineAbilities = root.Q<VisualElement>("examine-abilities");
            _examineClose = root.Q<Button>("examine-close");
            _endTurn = root.Q<Button>("end-turn");

            if (_root == null || _turnPanel == null || _turnOwner == null || _rope == null || _ropeFill == null || _ropeEmber == null
                || _actionBar == null || _tooltip == null || _tooltipTitle == null || _tooltipBody == null
                || _examine == null || _examineTitle == null || _examineSubtitle == null || _examineDescription == null
                || _examineAbilities == null || _examineClose == null || _endTurn == null)
            {
                Debug.LogError("[MatchHudView] MatchHud.uxml is missing one of the named elements.", this);
                enabled = false;
                return false;
            }

            _endTurn.clicked += HandleEndTurnClicked;
            _examineClose.clicked += HandleExamineCloseClicked;
            _bound = true;
            _barSignature = null;
            _ropeVisible = true;      // force the first UpdateRope to apply the real state
            Refresh();
            UpdateRope();
            return true;
        }

        private void Unbind()
        {
            if (!_bound) return;
            _endTurn.clicked -= HandleEndTurnClicked;
            _examineClose.clicked -= HandleExamineCloseClicked;
            ClearSlots();
            _bound = false;
        }

        // ---- state → visuals ------------------------------------------------------------------------

        /// <summary>Full repaint of everything except the rope, which ticks every frame.</summary>
        private void Refresh()
        {
            if (!_bound) return;

            bool mine = _source.IsMyTurn;
            _turnOwner.text = mine ? "YOUR TURN  ·  " + _source.TurnNumber : "OPPONENT'S TURN";
            _turnPanel.EnableInClassList("turn-panel--theirs", !mine);
            _endTurn.SetEnabled(_source.CanEndTurn);

            RefreshActionBar();
            RefreshExamine();
        }

        private void RefreshActionBar()
        {
            IReadOnlyList<HudAction> actions = _source.Actions;

            // Rebuild the sections only when the set of actions changes; otherwise update slots in place
            // so hover state and the tooltip survive a repaint.
            _signature.Length = 0;
            for (int i = 0; i < actions.Count; i++) _signature.Append(actions[i].Category).Append(':').Append(actions[i].Id).Append('|');
            string signature = _signature.ToString();
            if (signature != _barSignature)
            {
                BuildSections(actions);
                _barSignature = signature;
            }

            int active = _source.ActiveActionIndex;
            for (int i = 0; i < actions.Count && i < _slots.Count; i++)
            {
                HudAction action = actions[i];
                VisualElement slot = _slots[i];
                slot.EnableInClassList("action-slot--active", i == active);
                slot.EnableInClassList("action-slot--disabled", !action.Enabled);
                ApplyIcon(slot.Q<VisualElement>("icon"), slot.Q<Label>("glyph"), action.Icon, action.Name);
            }
        }

        private void RefreshExamine()
        {
            HudExamine examine = _source.Examine;
            if (examine == null)
            {
                _examine.RemoveFromClassList("examine--visible");
                return;
            }

            _examineTitle.text = examine.Title ?? string.Empty;
            _examineSubtitle.text = (examine.Subtitle ?? string.Empty).ToUpperInvariant();
            _examineDescription.text = examine.Description ?? string.Empty;
            _examineDescription.style.display = string.IsNullOrEmpty(examine.Description) ? DisplayStyle.None : DisplayStyle.Flex;
            _examine.EnableInClassList("examine--theirs", examine.Subtitle != null && examine.Subtitle.StartsWith("Opp", StringComparison.Ordinal));

            _examineAbilities.Clear();
            for (int i = 0; i < examine.Abilities.Count; i++)
            {
                HudExamineEntry entry = examine.Abilities[i];

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("examine-entry");
                row.EnableInClassList("examine-entry--hidden", entry.Hidden);

                var icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
                icon.AddToClassList("examine-entry-icon");
                var glyph = new Label { name = "glyph", pickingMode = PickingMode.Ignore };
                glyph.AddToClassList("examine-entry-glyph");
                icon.Add(glyph);
                row.Add(icon);
                if (entry.Hidden) { icon.style.backgroundImage = StyleKeyword.None; glyph.text = "?"; }
                else ApplyIcon(icon, glyph, entry.Icon, entry.Name);

                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("examine-entry-text");
                var name = new Label { text = entry.Name ?? string.Empty, pickingMode = PickingMode.Ignore };
                name.AddToClassList("examine-entry-name");
                text.Add(name);
                if (!string.IsNullOrEmpty(entry.Description))
                {
                    var desc = new Label { text = entry.Description, pickingMode = PickingMode.Ignore };
                    desc.AddToClassList("examine-entry-desc");
                    text.Add(desc);
                }
                row.Add(text);

                _examineAbilities.Add(row);
            }

            _examine.AddToClassList("examine--visible");
        }

        private void UpdateRope()
        {
            float remaining = _source.TurnSecondsRemaining;
            float ropeSeconds = _source.RopeSeconds;
            if (ropeSeconds < 0.001f) ropeSeconds = 0.001f;

            bool visible = _source.TurnSecondsTotal > 0f && remaining > 0f && remaining <= ropeSeconds;
            if (visible != _ropeVisible)
            {
                _ropeVisible = visible;
                _rope.EnableInClassList("rope--visible", visible);
            }
            if (!visible) return;

            float fraction = Mathf.Clamp01(remaining / ropeSeconds);
            _ropeFill.style.width = Length.Percent(fraction * 100f);
            _ropeEmber.style.left = Length.Percent(fraction * 100f);

            // The ember flickers faster as the rope gets short.
            float pulse = 0.85f + 0.3f * Mathf.PingPong(Time.unscaledTime * (2f + (1f - fraction) * 4f), 1f);
            _ropeEmber.style.scale = new Scale(new Vector2(pulse, pulse));
        }

        // ---- action bar sections --------------------------------------------------------------------

        private void BuildSections(IReadOnlyList<HudAction> actions)
        {
            ClearSlots();
            _actionBar.Clear();

            _groupOrder.Clear();
            for (int g = 0; g < Groups.Length; g++) _groupOrder.Add(Groups[g][0]);
            for (int i = 0; i < actions.Count; i++)
            {
                string category = actions[i].Category ?? string.Empty;
                if (!_groupOrder.Contains(category)) _groupOrder.Add(category);
            }

            // Slots are created in action order so _slots[i] always matches actions[i].
            var slotsByIndex = new VisualElement[actions.Count];
            bool first = true;
            for (int g = 0; g < _groupOrder.Count; g++)
            {
                string category = _groupOrder[g];
                bool any = false;
                for (int i = 0; i < actions.Count; i++) if ((actions[i].Category ?? string.Empty) == category) { any = true; break; }
                if (!any) continue;

                if (!first)
                {
                    var divider = new VisualElement { pickingMode = PickingMode.Ignore };
                    divider.AddToClassList("action-group-divider");
                    _actionBar.Add(divider);
                }
                first = false;

                var group = new VisualElement { name = "group-" + category, pickingMode = PickingMode.Ignore };
                group.AddToClassList("action-group");

                var caption = new Label { text = CaptionFor(category), pickingMode = PickingMode.Ignore };
                caption.AddToClassList("action-group-caption");
                group.Add(caption);

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("action-group-row");
                group.Add(row);

                for (int i = 0; i < actions.Count; i++)
                {
                    if ((actions[i].Category ?? string.Empty) != category) continue;
                    VisualElement slot = CreateSlot(i);
                    row.Add(slot);
                    slotsByIndex[i] = slot;
                }

                _actionBar.Add(group);
            }

            for (int i = 0; i < slotsByIndex.Length; i++) _slots.Add(slotsByIndex[i]);
        }

        private static string CaptionFor(string category)
        {
            for (int g = 0; g < Groups.Length; g++) if (Groups[g][0] == category) return Groups[g][1];
            return string.IsNullOrEmpty(category) ? "OTHER" : category.ToUpperInvariant();
        }

        private VisualElement CreateSlot(int index)
        {
            var slot = new VisualElement { name = "slot-" + index, pickingMode = PickingMode.Position, userData = index };
            slot.AddToClassList("action-slot");

            var icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
            icon.AddToClassList("action-icon");
            slot.Add(icon);

            var glyph = new Label { name = "glyph", pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("action-glyph");
            slot.Add(glyph);

            slot.RegisterCallback<ClickEvent>(HandleSlotClicked);
            slot.RegisterCallback<PointerEnterEvent>(HandleSlotEnter);
            slot.RegisterCallback<PointerLeaveEvent>(HandleSlotLeave);
            return slot;
        }

        private void ClearSlots()
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i] != null) _slots[i].RemoveFromHierarchy();
            _slots.Clear();
            HideTooltip();
        }

        private void ApplyIcon(VisualElement icon, Label glyph, string iconKey, string fallbackName)
        {
            Sprite sprite;
            if (iconKey != null && _iconsByKey.TryGetValue(iconKey, out sprite))
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
                glyph.style.display = DisplayStyle.None;
            }
            else
            {
                icon.style.backgroundImage = StyleKeyword.None;
                glyph.style.display = DisplayStyle.Flex;
                glyph.text = string.IsNullOrEmpty(fallbackName) ? "?" : fallbackName.Substring(0, 1).ToUpperInvariant();
            }
        }

        // ---- events ---------------------------------------------------------------------------------

        private void HandleSlotClicked(ClickEvent evt)
        {
            var slot = evt.currentTarget as VisualElement;
            if (slot == null || !(slot.userData is int index)) return;
            _source.SelectAction(index);
        }

        private void HandleSlotEnter(PointerEnterEvent evt)
        {
            var slot = evt.currentTarget as VisualElement;
            if (slot == null || !(slot.userData is int index)) return;

            IReadOnlyList<HudAction> actions = _source.Actions;
            if (index < 0 || index >= actions.Count) return;

            HudAction action = actions[index];
            _tooltipTitle.text = action.Name ?? action.Id;
            _tooltipBody.text = action.Description ?? string.Empty;
            _tooltipBody.style.display = string.IsNullOrEmpty(action.Description) ? DisplayStyle.None : DisplayStyle.Flex;
            _tooltip.AddToClassList("tooltip--visible");
        }

        private void HandleSlotLeave(PointerLeaveEvent evt) => HideTooltip();

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.RemoveFromClassList("tooltip--visible");
        }

        private void HandleEndTurnClicked() => _source.EndTurn();

        private void HandleExamineCloseClicked() => _source.CloseExamine();

        /// <summary>True when a pickable HUD element sits under the given screen position (bottom-left origin).</summary>
        private bool IsPointerOverHud(Vector2 screenPosition)
        {
            if (!_bound || _root == null) return false;
            IPanel panel = _root.panel;
            if (panel == null) return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            return panel.Pick(panelPosition) != null;
        }
    }
}
