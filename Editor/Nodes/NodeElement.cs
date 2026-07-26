using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Card-style visual representation of a single <see cref="GraphNodeSO"/>
    /// on the canvas. Header strip with title + optional subtitle, an inline
    /// body of the node's serialized fields (grouped into [NodeGroup] sections),
    /// and the input/output port stubs along the card edges (multi-port: one
    /// stub per declared GraphPortDef).
    ///
    /// Position is bound to the underlying node SO — drag the element and
    /// the SO's <see cref="GraphNodeSO.Position"/> follows.
    /// </summary>
    public class NodeElement : VisualElement
    {
        const float HeaderHeight = 24f;
        const float PortRowMinHeight = 20f;
        // Corner radius so the header + body read as one rounded card.
        const float CornerRadius = 6f;
        // Width of the type-colored accent stripe down the card's left edge.
        const float AccentStripeWidth = 6f;
        // Ports sit fully INSIDE the card (not straddling the border): the
        // circle's outer rim is tangent to the card's inner edge, with a 2px
        // hair of clearance so the selection ring doesn't cover the rim — the
        // port still reads as "touching the edge" when the node is selected.
        // Input clears the left accent stripe; output hugs the right edge.
        const float PortEdgeClearance = 2f;
        const float PortInsetInput = AccentStripeWidth + PortEdgeClearance;
        const float PortInsetOutput = PortEdgeClearance;
        // 4px base spacing unit — header/body/subtitle paddings are multiples.
        const float Pad = 4f;

        // USS state classes (the shared contract — see GraphEditor.uss).
        const string ClassNode      = "cgg-graph-node";
        const string ClassHover     = "cgg-graph-node--hover";
        const string ClassSelected  = "cgg-graph-node--selected";
        const string ClassStart     = "cgg-graph-node--start";

        static readonly Color BackgroundColor     = GraphTheme.Surface;
        static readonly Color SelectedBorderColor = GraphTheme.SelectionAccent;
        static readonly Color SubtitleColor       = GraphTheme.TextSubtitle;

        // Code-only "bevel" elevation stand-in derived from the soft
        // GraphTheme.NodeBorder (which replaced the near-black Separator
        // hairline on cards): top/left a hair lighter, bottom/right a hair
        // darker, so the flat card reads with a little depth. Hover bumps
        // both toward white; selection replaces them with a solid ring.
        // TODO: a 9-slice soft-shadow sprite would upgrade this flat bevel to
        //       a true drop shadow — deferred (no new PNG/sprite assets yet).
        static readonly Color BevelLight      = Lighten(GraphTheme.NodeBorder, 0.06f);
        static readonly Color BevelDark       = Lighten(GraphTheme.NodeBorder, -0.05f);
        static readonly Color HoverBevelLight = Lighten(GraphTheme.NodeBorder, 0.15f);
        static readonly Color HoverBevelDark  = Lighten(GraphTheme.NodeBorder, 0.07f);

        // Nudge an RGB color toward white (positive) or black (negative),
        // clamped, alpha preserved. Used only to derive the bevel shades.
        static Color Lighten(Color c, float amount) => new Color(
            Mathf.Clamp01(c.r + amount),
            Mathf.Clamp01(c.g + amount),
            Mathf.Clamp01(c.b + amount),
            c.a);

        readonly GraphCanvas _canvas;
        readonly GraphNodeSO _node;

        // Protected so subclasses (e.g. StickyNoteElement) can hide or
        // replace the inherited chrome with their own visual layout.
        protected readonly VisualElement _accentBar;
        protected readonly VisualElement _header;
        protected readonly Label _caret;
        protected readonly Label _iconLabel;
        protected readonly Label _titleLabel;
        protected readonly VisualElement _badgeRow;
        protected readonly Label _subtitleLabel;
        protected readonly VisualElement _portRow;
        protected readonly VisualElement _body;

        // Worst validation severity + message for this node, pushed by the
        // canvas after each Validate() pass (see GraphCanvas.ApplyNodeValidation).
        // Null = clean. Drives the header chip + a tinted card border.
        GraphValidationIssue.SeverityLevel? _validation;
        string _validationMessage;

        // Canvas-pushed decorations — domain canvases derive these from graph
        // context the node alone can't see (e.g. nav tab-ness / channel).
        Color? _accentTint;                     // overrides the accent-bar color
        IReadOnlyList<NodeBadge> _extraBadges;   // appended after the node's own badges
        bool _searchHighlight;                   // toolbar-find match outline
        GraphNodeRuntimeState? _runtimeState;    // play-mode live state (glow + pill)

        readonly List<PortElement> _inputPorts = new List<PortElement>();
        readonly List<PortElement> _outputPorts = new List<PortElement>();

        public GraphCanvas Canvas => _canvas;
        public GraphNodeSO Node => _node;

        public IReadOnlyList<PortElement> InputPorts => _inputPorts;
        public IReadOnlyList<PortElement> OutputPorts => _outputPorts;

        public bool IsSelected { get; private set; }
        public bool IsStartNode { get; private set; }

        // Pointer-hover state — drives the brighter border + the
        // "cgg-graph-node--hover" class. Visual only; no behavior change.
        bool _isHovered;

        // Effective hover: selection dominates hover, and hover yields to a
        // live connection/reroute drag (so the hover brighten can't compete
        // with the green/red port ring while a wire is dragged toward this
        // card). Computed (not stored) because IsSelected and the canvas drag
        // state change independently of this element's pointer events.
        bool HoverActive => _isHovered && !IsSelected && !(_canvas?.IsConnectionDragActive ?? false);

        /// <summary>
        /// Toggle the selection halo. Called by <see cref="GraphSelection"/>;
        /// don't call directly — go through the selection so other state stays
        /// consistent.
        /// </summary>
        internal void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            EnableInClassList(ClassSelected, selected);
            UpdateBorder();
            // Selection dominates hover — drop the hover class instantly so a
            // selected node doesn't keep the hover brighten under its ring.
            EnableInClassList(ClassHover, HoverActive);
        }

        /// <summary>
        /// Mark this node as the graph's start node. The start cue rides on
        /// the header accent bar (green) + the green header pill + ★ title,
        /// NOT on the card border ring — so a node can be the start AND be
        /// selected at the same time without one cue stomping the other.
        /// (Previously selection overrode the start outline, hiding the
        /// selection ring on the start node; the accent bar fixes that.)
        /// Called by <see cref="GraphCanvas"/> after the element is added,
        /// based on <see cref="GraphAssetSO.StartNodeGuid"/>.
        /// </summary>
        internal void SetIsStartNode(bool isStart)
        {
            if (IsStartNode == isStart) return;
            IsStartNode = isStart;
            EnableInClassList(ClassStart, isStart);
            UpdateBorder();
            // Keep the hover class consistent with the new state (start toggle
            // doesn't change HoverActive itself, but re-evaluate for parity).
            EnableInClassList(ClassHover, HoverActive);
            RefreshDisplay();  // repaint header bg + accent bar + title prefix
        }

        public NodeElement(GraphCanvas canvas, GraphNodeSO node)
        {
            _canvas = canvas;
            _node = node;

            AddToClassList(ClassNode);
            style.position = Position.Absolute;
            style.minWidth = node != null ? node.PreferredWidth : 180f;
            style.backgroundColor = BackgroundColor;
            style.overflow = Overflow.Visible;

            UpdateBorder();

            // Type-colored accent stripe down the card's LEFT edge (fed from
            // GraphNodeSO.HeaderColor / accent tint, or StartAccent for the
            // start node) — reads as the card's type tag. Absolute-positioned,
            // full height, left corners rounded to match the card radius.
            //
            // Hidden until RefreshDisplay() turns it on. Subclasses that own
            // their layout and skip base.RefreshDisplay() (e.g.
            // StickyNoteElement) therefore never show it — same opt-out path
            // the header/subtitle/port-row already use, without this base
            // type needing to know about the subclass.
            _accentBar = new VisualElement();
            _accentBar.AddToClassList("cgg-graph-node__accent-bar");
            _accentBar.style.position = Position.Absolute;
            _accentBar.style.left = 0f;
            _accentBar.style.top = 0f;
            _accentBar.style.bottom = 0f;
            _accentBar.style.width = AccentStripeWidth;
            _accentBar.style.borderTopLeftRadius = CornerRadius;
            _accentBar.style.borderBottomLeftRadius = CornerRadius;
            _accentBar.style.display = DisplayStyle.None;
            _accentBar.pickingMode = PickingMode.Ignore;
            Add(_accentBar);

            _header = new VisualElement();
            _header.AddToClassList("cgg-graph-node__header");
            _header.style.flexDirection = FlexDirection.Row;
            _header.style.alignItems = Align.Center;
            _header.style.paddingLeft = 2f * Pad;   // 8px
            _header.style.paddingRight = 2f * Pad;  // 8px
            // ~6px vertical pad (per the 4px rhythm token) instead of a fixed
            // header height, so the strip hugs the title; accent bar adds 3px.
            _header.style.paddingTop = Pad;          // 4px
            _header.style.paddingBottom = Pad;       // 4px
            // Squared bottom corners so header + body read as one piece. Top
            // corners are squared too — the dedicated _accentBar above owns the
            // rounded top of the card. Zero the header's own borders inline so
            // the .cgg-graph-node__header USS rule's 3px top-border reservation
            // (an alternate accent-bar approach) doesn't double up with the
            // _accentBar child; the bar child is the single accent source.
            _header.style.borderTopLeftRadius = 0f;
            _header.style.borderTopRightRadius = 0f;
            _header.style.borderBottomLeftRadius = 0f;
            _header.style.borderBottomRightRadius = 0f;
            _header.style.borderTopWidth = 0f;
            _header.pickingMode = PickingMode.Ignore;
            Add(_header);

            // Collapse caret in the header's leading slot (▾ expanded / ▸
            // collapsed). Clicking it folds the card down to its header strip
            // (body + subtitle hidden; ports + badges stay so edges read). It's
            // pickable and stops propagation so the click neither starts a node
            // drag (NodeDragManipulator) nor triggers the header double-click
            // rename. Hidden by UpdateCaret() for nodes with no inline body to
            // fold (e.g. sticky notes).
            _caret = new Label("▼");
            _caret.AddToClassList("cgg-graph-node__caret");
            // Filled triangle, near-white, sized to the title — the small thin
            // ▾ at subtitle-grey was hard to see against the header tint.
            _caret.style.color = new Color(0.88f, 0.90f, 0.95f);
            _caret.style.fontSize = 8;
            _caret.style.marginRight = 6f;
            _caret.style.unityTextAlign = TextAnchor.MiddleCenter;
            _caret.style.display = DisplayStyle.None;
            _caret.pickingMode = PickingMode.Position;
            _caret.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                ToggleCollapsed();
                evt.StopPropagation();
            });
            // Shield the legacy double-click rename path too (separate event).
            _caret.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            _header.Add(_caret);

            // Optional glyph in front of the title — driven by
            // GraphNodeSO.IconGlyph (default null hides the slot). Domain
            // nodes can return a Material-icon code or a single character
            // here to type-tag their entries without an asset reference.
            _iconLabel = new Label();
            _iconLabel.AddToClassList("cgg-graph-node__icon");
            _iconLabel.style.color = Color.white;
            _iconLabel.style.fontSize = 11;
            _iconLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _iconLabel.style.marginRight = 6f;
            _iconLabel.style.display = DisplayStyle.None;
            _iconLabel.pickingMode = PickingMode.Ignore;
            _header.Add(_iconLabel);

            _titleLabel = new Label();
            _titleLabel.AddToClassList("cgg-graph-node__title");
            _titleLabel.style.color = Color.white;
            _titleLabel.style.fontSize = 11;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.pickingMode = PickingMode.Ignore;
            _header.Add(_titleLabel);

            // Spacer pushes the badge row to the header's trailing edge.
            var headerSpacer = new VisualElement
            {
                style = { flexGrow = 1f },
                pickingMode = PickingMode.Ignore,
            };
            _header.Add(headerSpacer);

            // Trailing status-chip row — fed from GraphNodeSO.DisplayBadges plus
            // a leading validation chip (RebuildBadges). Hidden until it has
            // content. The row ignores picking; individual pills are pickable so
            // their tooltips show.
            _badgeRow = new VisualElement();
            _badgeRow.AddToClassList("cgg-graph-node__badge-row");
            _badgeRow.style.flexDirection = FlexDirection.Row;
            _badgeRow.style.alignItems = Align.Center;
            _badgeRow.style.flexShrink = 0f;
            _badgeRow.style.display = DisplayStyle.None;
            _badgeRow.pickingMode = PickingMode.Ignore;
            _header.Add(_badgeRow);

            _subtitleLabel = new Label();
            _subtitleLabel.AddToClassList("cgg-graph-node__subtitle");
            _subtitleLabel.style.color = SubtitleColor;
            _subtitleLabel.style.fontSize = 9;
            _subtitleLabel.style.paddingLeft = 2f * Pad;   // 8px
            _subtitleLabel.style.paddingRight = 2f * Pad;  // 8px
            _subtitleLabel.style.paddingTop = 2f;          // tight under the header
            _subtitleLabel.style.paddingBottom = 2f;
            _subtitleLabel.pickingMode = PickingMode.Ignore;
            Add(_subtitleLabel);

            _portRow = new VisualElement();
            _portRow.AddToClassList("cgg-graph-node__port-row");
            _portRow.style.minHeight = PortRowMinHeight;
            _portRow.pickingMode = PickingMode.Ignore;
            _portRow.RegisterCallback<GeometryChangedEvent>(_ => LayOutPorts());
            Add(_portRow);

            // Body — inline auto-inspector for the node's serialized fields,
            // rendered below the port row. Cards grow vertically with their
            // content. Skip for nodes that own their layout (sticky notes).
            _body = new VisualElement();
            _body.AddToClassList("cgg-graph-node__body");
            _body.style.paddingLeft = 2f * Pad;   // 8px
            _body.style.paddingRight = 2f * Pad;  // 8px
            _body.style.paddingTop = Pad;         // 4px
            _body.style.paddingBottom = 2f * Pad; // 8px
            Add(_body);
            // Skip the on-card body when the canvas defers editing to the docked
            // inspector panel (InlineBodyEnabled == false) — keeps cards compact.
            if (_node != null && _node.ShowInlineProperties && (_canvas?.InlineBodyEnabled ?? true))
                BuildBody(_body);

            RebuildPorts();
            RefreshDisplay();
            ApplyPosition();
            UpdateCaret();          // show the caret only if there's a body to fold
            ApplyCollapsedState();  // honor sidecar-restored Collapsed on first build

            // A read-only canvas (the runtime debugger) skips the drag manipulator
            // so the bound live asset's node positions can't be moved. Click-selection
            // must still work there — the debugger's Selected pane depends on it — so
            // read-only cards get a minimal select-on-press handler instead.
            if (!(_canvas?.ReadOnly ?? false))
                this.AddManipulator(new NodeDragManipulator());
            else
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    _canvas?.Selection.SetTo(this);
                });

            // Double-click handling. Uses MouseDownEvent (not ClickEvent) to
            // match GroupElement's title double-click: the attached
            // NodeDragManipulator captures the pointer, which suppresses
            // synthesized ClickEvents.
            //   • header double-click on a renameable node → inline rename
            //   • otherwise, a sub-graph node → descend into its graph
            //   • anything else → falls through to drag/select
            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return; // double-click only

                if (_node != null && _node.CanRename && !(_canvas?.ReadOnly ?? false)
                    && IsWithinHeader(evt.localMousePosition))
                {
                    BeginInlineRename();
                    evt.StopPropagation();
                    return;
                }

                if (_node is ISubGraphNode sg && sg.SubGraph != null)
                {
                    _canvas?.RequestDescend(sg.SubGraph);
                    evt.StopPropagation();
                }
            });

            // Hover affordance: brighten the card border + add the
            // "cgg-graph-node--hover" class (USS adds the subtle raise/scale).
            // Pure visual — does not touch selection/drag behavior.
            RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        }

        void SetHovered(bool hovered)
        {
            if (_isHovered == hovered) return;
            _isHovered = hovered;
            EnableInClassList(ClassHover, HoverActive);
            UpdateBorder();
        }

        /// <summary>
        /// Populate the node's body container with editable per-node UI.
        /// Default iterates the node's <c>SerializedObject</c> and emits
        /// a <see cref="PropertyField"/> per visible field — every
        /// <c>[SerializeField]</c> renders via its registered drawer, edits
        /// route through <c>SerializedProperty</c> (Undo + dirty handled by
        /// Unity). The script reference (<c>m_Script</c>) is skipped.
        ///
        /// Override to replace the auto-inspector with a hand-built layout
        /// (e.g. a single-line label-and-field, or a stat-pulse bar).
        /// Subclasses can call <c>base.BuildBody(container)</c> to keep the
        /// default fields and append more chrome above or below.
        /// </summary>
        protected virtual void BuildBody(VisualElement container)
        {
            // Delegate to the shared builder (the docked inspector panel uses it
            // too). On any inline edit, refresh this card's header (title /
            // subtitle / badges may derive from the changed field) and flip the
            // window "*".
            NodeInspectorBody.Build(_node, container, () =>
            {
                RefreshDisplay();
                _canvas?.NotifyGraphChanged();
            });
        }

        // ---------------------------------------------------------------
        // Display + position
        // ---------------------------------------------------------------

        /// <summary>
        /// Re-read display data (title / subtitle / header color) from the
        /// bound node SO. Call after the node's fields change externally.
        /// Virtual so subclasses can replace the read (e.g. sticky notes
        /// read text from a TextField, not the header label).
        /// </summary>
        public virtual void RefreshDisplay()
        {
            if (_node == null) return;

            // Start nodes paint as a bold green pill — header strip filled
            // with StartAccent and the title prefixed with a ★ so the
            // entry point reads from across the canvas. The start cue lives
            // on the header + accent bar (NOT the card border), so a start
            // node can also show the selection ring; see UpdateBorder.
            string title = _node.DisplayTitle ?? string.Empty;
            _titleLabel.text = IsStartNode ? "★ " + title : title;
            // The header is neutral now — the left accent stripe carries the
            // type color. (A start node still reads via the green stripe + ★.)
            _header.style.backgroundColor = Color.clear;

            // Left accent stripe: node's HeaderColor / accent tint normally,
            // green StartAccent for the start node (its persistent, selection-
            // proof cue). Shown here (constructor leaves it hidden) so subclasses
            // that skip base.RefreshDisplay() keep it off.
            if (_accentBar != null)
            {
                _accentBar.style.display = DisplayStyle.Flex;
                _accentBar.style.backgroundColor = IsStartNode
                    ? GraphTheme.StartAccent
                    : (_accentTint ?? _node.HeaderColor);
            }

            // Optional icon glyph — hidden when the node returns null.
            string glyph = _node.IconGlyph;
            if (string.IsNullOrEmpty(glyph))
            {
                _iconLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _iconLabel.text = glyph;
                _iconLabel.style.display = DisplayStyle.Flex;
            }

            string subtitle = _node.DisplaySubtitle;
            // Also hidden while collapsed (card folded to its header) — keeping
            // the rule here means every RefreshDisplay caller (incl.
            // SetIsStartNode) stays consistent with the collapsed state without
            // each having to re-apply it.
            if (string.IsNullOrEmpty(subtitle) || _node.Collapsed)
            {
                _subtitleLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _subtitleLabel.style.display = DisplayStyle.Flex;
                _subtitleLabel.text = subtitle;
            }

            RebuildBadges();
        }

        // ---------------------------------------------------------------
        // Collapse (fold body to the header strip; sidecar-persisted)
        // ---------------------------------------------------------------

        // True when the node has an inline body worth folding — the SINGLE
        // source of truth shared by the caret's visibility and the collapse
        // commands, so the per-node caret and the bulk Collapse menu always
        // agree on what can fold (a bodyless node is never put into a collapsed
        // state it has no on-card affordance to leave).
        bool IsCollapsible => _node != null && _node.ShowInlineProperties
                              && _body != null && _body.childCount > 0;

        // Flip this node's collapsed state from the header caret.
        void ToggleCollapsed()
        {
            if (!IsCollapsible) return;
            SetCollapsed(!_node.Collapsed);
        }

        /// <summary>
        /// Collapse or expand this node. Hides the body, keeps the header +
        /// badges + ports (so edges stay glued). Persists like a move: flips the
        /// window "*" via <c>NotifyGraphChanged</c> so the next save writes the
        /// flag to the layout sidecar. Pass <paramref name="notify"/> false for
        /// bulk ops that fire one notify at the end. Returns true only when the
        /// state actually changed, so bulk callers can skip a no-op dirty.
        /// </summary>
        internal bool SetCollapsed(bool collapsed, bool notify = true)
        {
            if (!IsCollapsible) return false;        // nothing to fold
            if (_node.Collapsed == collapsed) return false;
            _node.Collapsed = collapsed;
            RefreshDisplay();       // re-evaluates subtitle for the new state
            ApplyCollapsedState();
            _canvas?.RefreshEdgesForNode(_node); // height changed -> ports moved
            if (notify) _canvas?.NotifyGraphChanged();
            return true;
        }

        // Apply the current Collapsed flag to the card: body hidden when
        // collapsed; caret glyph reflects the state. (The subtitle is governed
        // by RefreshDisplay, which every caller runs first.)
        void ApplyCollapsedState()
        {
            bool collapsed = _node != null && _node.Collapsed;
            if (_body != null)
                // Hidden when collapsed OR when there are no inline fields (the
                // canvas defers editing to the docked inspector panel) — so a
                // compact card doesn't reserve an empty body strip.
                _body.style.display = (collapsed || _body.childCount == 0)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            if (_caret != null)
                _caret.text = collapsed ? "▶" : "▼";
        }

        // Show the caret only when there's an inline body worth folding.
        void UpdateCaret()
        {
            if (_caret == null) return;
            _caret.style.display = IsCollapsible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---------------------------------------------------------------
        // Header status badges (DisplayBadges + validation chip)
        // ---------------------------------------------------------------

        /// <summary>
        /// Rebuild the trailing chip row: a leading validation chip (when this
        /// node carries an issue) followed by one pill per
        /// <see cref="GraphNodeSO.DisplayBadges"/> entry. Hides the row when
        /// empty so a clean node shows no chrome.
        /// </summary>
        void RebuildBadges()
        {
            if (_badgeRow == null) return;
            _badgeRow.Clear();

            // Live runtime pill first (leftmost) — the signal you're watching in play.
            if (_runtimeState.HasValue && _runtimeState.Value.Badge.HasValue)
            {
                var rb = _runtimeState.Value.Badge.Value;
                _badgeRow.Add(MakeBadgePill(rb.Text, rb.Tint, rb.Tooltip));
            }

            if (_validation.HasValue)
            {
                bool err = _validation.Value == GraphValidationIssue.SeverityLevel.Error;
                _badgeRow.Add(MakeBadgePill(
                    "!",
                    err ? GraphTheme.ValidationError : GraphTheme.ValidationWarning,
                    string.IsNullOrEmpty(_validationMessage)
                        ? (err ? "Error" : "Warning")
                        : _validationMessage));
            }

            var badges = _node?.DisplayBadges;
            if (badges != null)
            {
                for (int i = 0; i < badges.Count; i++)
                    _badgeRow.Add(MakeBadgePill(badges[i].Text, badges[i].Tint, badges[i].Tooltip));
            }

            // Canvas-pushed derived badges (e.g. nav "tab") after the node's own.
            if (_extraBadges != null)
            {
                for (int i = 0; i < _extraBadges.Count; i++)
                    _badgeRow.Add(MakeBadgePill(_extraBadges[i].Text, _extraBadges[i].Tint, _extraBadges[i].Tooltip));
            }

            _badgeRow.style.display = _badgeRow.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // One status pill. Pickable (default) so its tooltip shows on hover;
        // it doesn't stop propagation, so a drag/select started over a pill
        // still bubbles to the card's manipulators.
        static Label MakeBadgePill(string text, Color tint, string tooltip)
        {
            var pill = new Label(text ?? string.Empty)
            {
                style =
                {
                    fontSize = 8,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = Color.white,
                    backgroundColor = tint,
                    marginLeft = 3f,
                    paddingLeft = 4f,
                    paddingRight = 4f,
                    paddingTop = 1f,
                    paddingBottom = 1f,
                    borderTopLeftRadius = 3f,
                    borderTopRightRadius = 3f,
                    borderBottomLeftRadius = 3f,
                    borderBottomRightRadius = 3f,
                },
            };
            if (!string.IsNullOrEmpty(tooltip)) pill.tooltip = tooltip;
            return pill;
        }

        /// <summary>
        /// Push this node's worst validation state (or null to clear). Called
        /// by <see cref="GraphCanvas.ApplyNodeValidation"/> after each validate
        /// pass — drives the header chip + the tinted card border. Validation
        /// is single-sourced from the footer; this only displays it.
        /// </summary>
        internal void SetValidationState(GraphValidationIssue.SeverityLevel? severity, string message)
        {
            if (_validation == severity && _validationMessage == message) return;
            _validation = severity;
            _validationMessage = message;
            UpdateBorder();
            RebuildBadges();
        }

        /// <summary>
        /// Override the accent-bar color (null restores the node's
        /// <see cref="GraphNodeSO.HeaderColor"/>). A canvas decoration — domain
        /// canvases use it to tint a derived grouping (e.g. nav channel) the
        /// node can't compute alone.
        /// </summary>
        public void SetAccentTint(Color? tint)
        {
            if (_accentTint == tint) return;
            _accentTint = tint;
            if (_accentBar != null && !IsStartNode && _node != null)
                _accentBar.style.backgroundColor = tint ?? _node.HeaderColor;
        }

        /// <summary>
        /// Extra header badges appended after the node's own
        /// <see cref="GraphNodeSO.DisplayBadges"/>. A canvas decoration for
        /// derived facts (e.g. nav "tab", which depends on the parent).
        /// </summary>
        public void SetExtraBadges(IReadOnlyList<NodeBadge> badges)
        {
            _extraBadges = badges;
            RebuildBadges();
        }

        /// <summary>
        /// Toolbar-find match outline (amber). Below selection + validation in
        /// border priority, above hover/default.
        /// </summary>
        internal void SetSearchHighlight(bool on)
        {
            if (_searchHighlight == on) return;
            _searchHighlight = on;
            UpdateBorder();
        }

        /// <summary>
        /// Play-mode runtime state pushed by the canvas (a glow border + a live
        /// pill). Null clears it. In border priority it sits below selection +
        /// validation, above search; the pill always shows (leftmost).
        /// </summary>
        public void SetRuntimeState(GraphNodeRuntimeState? state)
        {
            if (!_runtimeState.HasValue && !state.HasValue) return; // stays clear — skip
            _runtimeState = state;
            UpdateBorder();
            UpdateRuntimeFill();
            RebuildBadges();
        }

        // Tint the card body with the runtime Fill (e.g. the active/visible node),
        // restoring the normal surface when there's no fill. Background is otherwise
        // untouched by selection/hover, so this is the single owner of the card's
        // runtime tint.
        void UpdateRuntimeFill()
        {
            Color? fill = _runtimeState?.Fill;
            style.backgroundColor = fill ?? BackgroundColor;
        }

        // True when a card-local point lands in the header strip (the rename
        // hit-region). Header sits at the top of the card; everything at or
        // above its bottom edge counts. NaN before first layout → treated as
        // "not in header" so the double-click falls through.
        bool IsWithinHeader(Vector2 localPoint)
        {
            float headerBottom = _header.layout.yMax;
            if (float.IsNaN(headerBottom)) return false;
            return localPoint.y <= headerBottom;
        }

        // Swap the title label for a TextField; commit on Enter/blur, cancel on
        // Escape. The field shields the canvas's key shortcuts (Delete / F /
        // Ctrl+*) while focused so typing doesn't delete the node.
        void BeginInlineRename()
        {
            if (_node == null || !_node.CanRename) return;

            int titleIndex = _header.IndexOf(_titleLabel);
            // Seed from the raw DisplayTitle (not the label text, which may
            // carry the start-node "★ " prefix).
            string current = _node.DisplayTitle ?? string.Empty;
            var field = new TextField { value = current };
            field.style.flexGrow = 1f;
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.style.fontSize = 13;

            _titleLabel.style.display = DisplayStyle.None;
            _header.Insert(titleIndex, field);
            field.Focus();
            field.SelectAll();

            bool finished = false;
            void Finish(bool commit)
            {
                if (finished) return;
                finished = true;

                string newValue = field.value;
                field.RemoveFromHierarchy();
                _titleLabel.style.display = DisplayStyle.Flex;

                if (commit)
                {
                    string trimmed = (newValue ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != current)
                    {
                        Undo.RecordObject(_node, "Rename Node");
                        _node.Rename(trimmed);
                        EditorUtility.SetDirty(_node);
                        _canvas?.NotifyGraphChanged();
                    }
                }

                RefreshDisplay();
            }

            field.RegisterCallback<BlurEvent>(_ => Finish(true));
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    Finish(true);
                else if (evt.keyCode == KeyCode.Escape)
                    Finish(false);
                // Shield the canvas's key shortcuts while editing (Delete would
                // otherwise delete the node, F would fit-view, etc.).
                evt.StopPropagation();
            });
        }

        /// <summary>
        /// Snap the element to the SO's <see cref="GraphNodeSO.Position"/>.
        /// Called after external edits (e.g. undo).
        /// </summary>
        public void ApplyPosition()
        {
            if (_node == null) return;
            style.left = _node.Position.x;
            style.top = _node.Position.y;
        }

        /// <summary>
        /// Update both the visual position and the SO's stored position.
        /// Called from <see cref="NodeDragManipulator"/> during drag.
        /// </summary>
        public void SetWorldPosition(Vector2 worldPos)
        {
            if (_node == null) return;
            _node.Position = worldPos;
            style.left = worldPos.x;
            style.top = worldPos.y;
        }

        // ---------------------------------------------------------------
        // Ports
        // ---------------------------------------------------------------

        void RebuildPorts()
        {
            foreach (var p in _inputPorts) p.RemoveFromHierarchy();
            foreach (var p in _outputPorts) p.RemoveFromHierarchy();
            _inputPorts.Clear();
            _outputPorts.Clear();

            if (_node == null) return;

            int idx = 0;
            foreach (var def in _node.InputPorts)
            {
                var port = new PortElement(this, isInput: true, def, idx++);
                port.RegisterCallback<GeometryChangedEvent>(OnPortGeometryChanged);
                _inputPorts.Add(port);
                Add(port);
            }

            idx = 0;
            foreach (var def in _node.OutputPorts)
            {
                var port = new PortElement(this, isInput: false, def, idx++);
                port.RegisterCallback<GeometryChangedEvent>(OnPortGeometryChanged);
                _outputPorts.Add(port);
                Add(port);
            }

            // Collapse the port row when the node declares no ports —
            // otherwise the always-on minHeight reserves an empty strip
            // that makes port-less nodes (e.g. nav destinations) look
            // unbalanced.
            bool hasPorts = _inputPorts.Count > 0 || _outputPorts.Count > 0;
            _portRow.style.display = hasPorts ? DisplayStyle.Flex : DisplayStyle.None;

            // Initial layout — geometry pass will fire LayOutPorts once layout settles.
            LayOutPorts();
        }

        // When a port's resolved layout settles — notably as a freshly-opened
        // graph finishes laying out, after the first fallback pass — re-route
        // the edges touching this node so they glue to the final port
        // positions. Without this, edges built before the canvas had a resolved
        // layout keep stale endpoints: the drawn curve looks fine (OnPaint
        // reads anchors live) but the cached hit-test band is offset, so old
        // lines can't be clicked/grabbed while freshly created ones can.
        void OnPortGeometryChanged(GeometryChangedEvent _)
        {
            _canvas?.RefreshEdgesForNode(_node);
        }

        void LayOutPorts()
        {
            float portRowY = _portRow.layout.y;
            float portRowH = _portRow.layout.height;
            if (float.IsNaN(portRowY) || float.IsNaN(portRowH) || portRowH <= 0f)
            {
                // Layout pre-resolution — fall back to a sensible position
                // so first paint isn't broken. GeometryChangedEvent will fix.
                portRowY = HeaderHeight + (_subtitleLabel.resolvedStyle.display == DisplayStyle.None ? 0f : 22f);
                portRowH = PortRowMinHeight;
            }
            float centerY = portRowY + portRowH * 0.5f - PortElement.PortSize * 0.5f;

            for (int i = 0; i < _inputPorts.Count; i++)
            {
                var p = _inputPorts[i];
                p.style.left = PortInsetInput;
                p.style.top = centerY + (i - (_inputPorts.Count - 1) * 0.5f) * (PortElement.PortSize + 6f);
            }
            for (int i = 0; i < _outputPorts.Count; i++)
            {
                var p = _outputPorts[i];
                p.style.right = PortInsetOutput;
                p.style.top = centerY + (i - (_outputPorts.Count - 1) * 0.5f) * (PortElement.PortSize + 6f);
            }
        }

        /// <summary>
        /// World-space anchor point where an incoming edge should terminate
        /// — the outer tip of the first input port. Multi-port targets will
        /// extend to per-port anchors in a later piece.
        /// </summary>
        public Vector2 GetInputAnchor()
        {
            return GetPortAnchorWorld(isInput: true, portIndex: 0);
        }

        /// <summary>World-space anchor where an outgoing edge starts.</summary>
        public Vector2 GetOutputAnchor()
        {
            return GetPortAnchorWorld(isInput: false, portIndex: 0);
        }

        Vector2 GetPortAnchorWorld(bool isInput, int portIndex)
        {
            var list = isInput ? _inputPorts : _outputPorts;
            float portY;
            if (portIndex >= 0 && portIndex < list.Count)
            {
                portY = list[portIndex].layout.y + PortElement.PortSize * 0.5f;
            }
            else
            {
                // Fallback — center of port row, used before ports exist.
                float portRowY = _portRow.layout.y;
                float portRowH = _portRow.layout.height;
                if (float.IsNaN(portRowY) || portRowH <= 0f)
                {
                    portRowY = HeaderHeight;
                    portRowH = PortRowMinHeight;
                }
                portY = portRowY + portRowH * 0.5f;
            }

            // Width: prefer resolved layout, fall back to the node's
            // preferred width before first layout completes (avoids edges
            // snapping to x=0 on spawn).
            float width = layout.width;
            if (float.IsNaN(width) || width <= 0f)
                width = _node != null ? _node.PreferredWidth : 180f;

            float portX = isInput
                ? PortInsetInput + PortElement.PortSize * 0.5f
                : width - PortInsetOutput - PortElement.PortSize * 0.5f;
            return _node.Position + new Vector2(portX, portY);
        }

        void UpdateBorder()
        {
            style.borderTopLeftRadius = CornerRadius;
            style.borderTopRightRadius = CornerRadius;
            style.borderBottomLeftRadius = CornerRadius;
            style.borderBottomRightRadius = CornerRadius;

            if (IsSelected)
            {
                // Selection: a 2px SelectionAccent ring on all four edges.
                // The start node keeps its cue on the accent bar/header, so
                // selection + start now coexist (was: start outline lost when
                // a start node got selected). Selection dominates the
                // validation tint, but the validation chip stays visible.
                SetBorder(2f, SelectedBorderColor, SelectedBorderColor,
                          SelectedBorderColor, SelectedBorderColor);
                return;
            }

            if (_validation.HasValue)
            {
                // Validation tint: a 2px error/warning ring so the offending
                // card is impossible to miss (pairs with the header chip).
                Color v = _validation.Value == GraphValidationIssue.SeverityLevel.Error
                    ? GraphTheme.ValidationError
                    : GraphTheme.ValidationWarning;
                SetBorder(2f, v, v, v, v);
                return;
            }

            if (_runtimeState.HasValue && _runtimeState.Value.Glow.HasValue)
            {
                // Play-mode runtime glow (below selection + validation, above search).
                Color rg = _runtimeState.Value.Glow.Value;
                SetBorder(2f, rg, rg, rg, rg);
                return;
            }

            if (_searchHighlight)
            {
                // Toolbar-find match: a 2px amber ring (below selection +
                // validation, above the resting look).
                Color s = GraphTheme.Attention;
                SetBorder(2f, s, s, s, s);
                return;
            }

            // Default / hover: a soft 1px NodeBorder with a code-only bevel
            // (top/left a hair lighter, bottom/right a hair darker) as the
            // elevation stand-in. Hover brightens the bevel.
            // TODO: a 9-slice soft-shadow sprite would replace this flat bevel
            //       with a true drop shadow — deferred (no new sprite assets).
            Color light = HoverActive ? HoverBevelLight : BevelLight;
            Color dark  = HoverActive ? HoverBevelDark  : BevelDark;
            SetBorder(1f, light, dark, light, dark);
        }

        // top, bottom, left, right — keeps UpdateBorder readable.
        void SetBorder(float width, Color top, Color bottom, Color left, Color right)
        {
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
            style.borderTopColor = top;
            style.borderBottomColor = bottom;
            style.borderLeftColor = left;
            style.borderRightColor = right;
        }
    }
}
