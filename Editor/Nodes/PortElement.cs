using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Small interactive stub representing one input or output port on a node,
    /// anchored to the node's edge (half-outside the card).
    /// <see cref="PortDragManipulator"/> makes every port a drag handle —
    /// drag from either side to wire a connection (or, on a connected input,
    /// to detach/reroute it) — and the canvas lights ports as drop targets
    /// while a drag passes over them (see <see cref="SetDropState"/>).
    /// </summary>
    public class PortElement : VisualElement
    {
        public const float PortSize = 12f;

        static readonly Color InputColor  = GraphTheme.PortInput;
        static readonly Color OutputColor = GraphTheme.PortOutput;
        // Softened from GraphTheme.Separator (near-black hairline) to the
        // body-lighter NodeBorder so the stub reads as a gentle bevel, in
        // step with the node card. See GraphTheme.NodeBorder.
        static readonly Color BorderColor = GraphTheme.NodeBorder;

        // Drop-target affordance colors (mirror GraphEditor.uss
        // --candrop / --incompatible; both class + inline are set in
        // SetDropState because inline border styles beat USS specificity).
        static readonly Color CanDropRing      = GraphTheme.StartAccent;     // green
        static readonly Color IncompatibleRing = GraphTheme.ValidationError; // red
        // Idle-hover ring — neutral cool near-white, deliberately distinct from
        // the green candrop / red incompatible drag affordances.
        static readonly Color HoverRing        = GraphTheme.PortHoverRing;

        /// <summary>Live drop-target affordance for a drag passing over this port.</summary>
        public enum DropState
        {
            /// <summary>Not a candidate — default look.</summary>
            None,
            /// <summary>Compatible target: pop + green ring + brighter fill.</summary>
            CanDrop,
            /// <summary>Incompatible target: red ring, no scale.</summary>
            Incompatible,
        }

        public NodeElement OwnerNode { get; }
        public bool IsInput { get; }
        public GraphPortDef PortDef { get; }
        public int PortIndex { get; }

        public string PortId => PortDef?.Id;

        // Cached default look, captured at construction, restored on
        // DropState.None so the affordance is fully reversible.
        readonly Color _defaultFill;
        readonly float _defaultBorderWidth;
        readonly Color _defaultBorderColor;

        DropState _dropState = DropState.None;
        public DropState CurrentDropState => _dropState;

        // Idle-hover state. _dropState is the AUTHORITY for the port's transient
        // visuals; idle hover only paints when _dropState == None and no
        // connection drag is live, so it never stomps the drag affordance.
        bool _isHovered;

        // Hover pop. Every port is a drag handle — output = new connection,
        // input = new connection in reverse (or detach when already wired) — so
        // they all get the same lift. Kept below the candrop pop (scale 1.4 /
        // fill 0.45) so idle hover and an armed drop target stay distinct.
        const float HoverScale     = 1.18f;
        const float HoverFillLerp  = 0.22f;
        const float HoverRingWidth = 2f;

        public PortElement(NodeElement node, bool isInput, GraphPortDef def, int index)
        {
            OwnerNode = node;
            IsInput = isInput;
            PortDef = def;
            PortIndex = index;

            AddToClassList("cgg-graph-port");
            AddToClassList(isInput ? "cgg-graph-port--input" : "cgg-graph-port--output");

            // Pickable: PortDragManipulator starts a connection drag from either
            // side, and the canvas hit-tests ports as drop targets on release.
            pickingMode = PickingMode.Position;

            style.position = Position.Absolute;
            style.width = PortSize;
            style.height = PortSize;
            style.backgroundColor = isInput ? InputColor : OutputColor;

            float r = PortSize * 0.5f;
            style.borderTopLeftRadius = r;
            style.borderTopRightRadius = r;
            style.borderBottomLeftRadius = r;
            style.borderBottomRightRadius = r;

            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            style.borderTopColor = BorderColor;
            style.borderBottomColor = BorderColor;
            style.borderLeftColor = BorderColor;
            style.borderRightColor = BorderColor;

            // Snapshot the resting look so SetDropState can restore it
            // exactly when the drag leaves (None).
            _defaultFill = isInput ? InputColor : OutputColor;
            _defaultBorderWidth = 1f;
            _defaultBorderColor = BorderColor;

            this.AddManipulator(new PortDragManipulator());

            // Idle hover: highlight the port when the cursor is over it without
            // dragging, signalling it's an interactive drag handle. pickingMode
            // is Position (above) so these fire. Yields to an active drag via
            // _dropState / IsConnectionDragActive.
            RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        }

        void SetHovered(bool hovered)
        {
            if (_isHovered == hovered) return;
            _isHovered = hovered;
            RefreshHoverVisual();
        }

        /// <summary>
        /// Apply the idle-hover look — the SINGLE inline path for the
        /// <see cref="DropState.None"/> state. Hover paints only when no drop
        /// affordance is active AND no connection drag is live, so the drag
        /// affordance always wins; otherwise it restores the cached resting
        /// look exactly (never re-reads current style).
        /// </summary>
        void RefreshHoverVisual()
        {
            bool dragLive = OwnerNode?.Canvas?.IsConnectionDragActive ?? false;
            bool showHover = _isHovered && _dropState == DropState.None && !dragLive;

            EnableInClassList("cgg-graph-port--hover", showHover);

            // While a drop affordance is live, SetDropState owns the inline
            // visuals — don't touch them here.
            if (_dropState != DropState.None) return;

            if (showHover)
            {
                SetBorder(HoverRingWidth, HoverRing);
                style.backgroundColor = Color.Lerp(_defaultFill, Color.white, HoverFillLerp);
                style.scale = new Scale(new Vector3(HoverScale, HoverScale, 1f));
            }
            else
            {
                SetBorder(_defaultBorderWidth, _defaultBorderColor);
                style.backgroundColor = _defaultFill;
                style.scale = new Scale(Vector3.one);
            }
        }

        /// <summary>
        /// Toggle the drag drop-target affordance on this port. Driven by
        /// <see cref="GraphCanvas.UpdateDropCandidate"/> while a connection
        /// drag passes nearby:
        /// <list type="bullet">
        ///   <item><c>CanDrop</c> — compatible input: pop (scale 1.4) + green
        ///   ring + brighter fill.</item>
        ///   <item><c>Incompatible</c> — incompatible input: red ring, no scale.</item>
        ///   <item><c>None</c> — restore the resting look.</item>
        /// </list>
        /// Both the shared USS state class (transitions live in
        /// GraphEditor.uss) and the equivalent inline styles are set: inline
        /// border styles win over USS specificity, so the inline writes
        /// guarantee the affordance shows regardless of how the base look is
        /// authored.
        ///
        /// <para><c>_dropState</c> is the authority for the port's transient
        /// visuals; idle hover (<see cref="SetHovered"/> /
        /// <see cref="RefreshHoverVisual"/>) only paints when
        /// <c>_dropState == None</c> and otherwise just tracks the hover flag.
        /// The None branch routes through <see cref="RefreshHoverVisual"/>, so a
        /// drag that ends while the pointer is still over the port lands back on
        /// the hover look (not cold resting) — one restore path, no flicker.</para>
        /// </summary>
        public void SetDropState(DropState state)
        {
            if (_dropState == state) return;
            _dropState = state;

            EnableInClassList("cgg-graph-port--candrop", state == DropState.CanDrop);
            EnableInClassList("cgg-graph-port--incompatible", state == DropState.Incompatible);
            // A live drop affordance owns the visuals — drop the idle-hover class
            // so its USS rule can't compete (inline already wins, but keep it tidy).
            if (state != DropState.None)
                EnableInClassList("cgg-graph-port--hover", false);

            switch (state)
            {
                case DropState.CanDrop:
                    SetBorder(2f, CanDropRing);
                    // Brighter fill — lift the input color toward white so
                    // the target reads as "armed".
                    style.backgroundColor = Color.Lerp(_defaultFill, Color.white, 0.45f);
                    style.scale = new Scale(new Vector3(1.4f, 1.4f, 1f));
                    break;

                case DropState.Incompatible:
                    SetBorder(2f, IncompatibleRing);
                    style.backgroundColor = _defaultFill;
                    style.scale = new Scale(Vector3.one); // no pop
                    break;

                default: // None — restore resting, or idle-hover if still hovered.
                    RefreshHoverVisual();
                    break;
            }
        }

        void SetBorder(float width, Color color)
        {
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
            style.borderTopColor = color;
            style.borderBottomColor = color;
            style.borderLeftColor = color;
            style.borderRightColor = color;
        }
    }
}
