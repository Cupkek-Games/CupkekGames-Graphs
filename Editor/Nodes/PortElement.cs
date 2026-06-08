using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Small interactive socket for one input or output port, sitting just
    /// inside the node's edge. Drawn as a <b>ring</b> — a colored outline with a
    /// dark center — that gains a filled colored <b>inner dot</b> when a
    /// connection lands on it (<see cref="SetConnected"/>).
    /// <see cref="PortDragManipulator"/> makes every port a drag handle, and the
    /// canvas lights ports as drop targets while a drag passes over them
    /// (<see cref="SetDropState"/>).
    /// </summary>
    public class PortElement : VisualElement
    {
        public const float PortSize = 12f;
        // Even, and an even fraction of the inner socket (PortSize - 2*RingWidth
        // = 8) so the flex-centered margins are a whole 2px on each side. An odd
        // dot needs 1.5px margins, which Yoga's per-element pixel snapping rounds
        // inconsistently against the parent — the dot then reads off-center.
        const float InnerDotSize = 4f;

        static readonly Color InputColor  = GraphTheme.PortInput;
        static readonly Color OutputColor = GraphTheme.PortOutput;
        // Dark socket center (darker than the card so it reads as a punched hole).
        static readonly Color CenterColor = Color.Lerp(GraphTheme.Surface, Color.black, 0.4f);

        // Drop-target affordance ring colors (mirror GraphEditor.uss
        // --candrop / --incompatible).
        static readonly Color CanDropRing      = GraphTheme.StartAccent;     // green
        static readonly Color IncompatibleRing = GraphTheme.ValidationError; // red

        /// <summary>Live drop-target affordance for a drag passing over this port.</summary>
        public enum DropState { None, CanDrop, Incompatible }

        public NodeElement OwnerNode { get; }
        public bool IsInput { get; }
        public GraphPortDef PortDef { get; }
        public int PortIndex { get; }

        public string PortId => PortDef?.Id;

        // The port's own outline color (input vs output) — the resting ring.
        readonly Color _ringColor;
        // Filled center dot, shown only while a connection lands on this port.
        readonly VisualElement _innerDot;

        DropState _dropState = DropState.None;
        public DropState CurrentDropState => _dropState;

        bool _isHovered;
        bool _isConnected;

        const float HoverScale = 1.18f;
        const float RingWidth  = 2f;

        public PortElement(NodeElement node, bool isInput, GraphPortDef def, int index)
        {
            OwnerNode = node;
            IsInput = isInput;
            PortDef = def;
            PortIndex = index;
            _ringColor = isInput ? InputColor : OutputColor;

            AddToClassList("cgg-graph-port");
            AddToClassList(isInput ? "cgg-graph-port--input" : "cgg-graph-port--output");

            // Pickable: PortDragManipulator starts a connection drag; the canvas
            // hit-tests ports as drop targets on release.
            pickingMode = PickingMode.Position;

            style.position = Position.Absolute;
            style.width = PortSize;
            style.height = PortSize;
            style.alignItems = Align.Center;       // center the inner dot
            style.justifyContent = Justify.Center;
            style.backgroundColor = CenterColor;    // dark center
            SetRadius(this, PortSize * 0.5f);
            SetBorder(RingWidth, _ringColor);       // colored ring

            // Inner dot — hidden until SetConnected(true).
            _innerDot = new VisualElement { pickingMode = PickingMode.Ignore };
            _innerDot.style.width = InnerDotSize;
            _innerDot.style.height = InnerDotSize;
            _innerDot.style.backgroundColor = _ringColor;
            _innerDot.style.display = DisplayStyle.None;
            SetRadius(_innerDot, InnerDotSize * 0.5f);
            Add(_innerDot);

            // A read-only canvas (the runtime debugger) skips the drag manipulator so
            // no connections can be created/rerouted on the bound live asset.
            if (!(OwnerNode?.Canvas?.ReadOnly ?? false))
                this.AddManipulator(new PortDragManipulator());

            RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        }

        /// <summary>
        /// Show/hide the filled inner dot — true when a connection currently
        /// lands on this port. Pushed by <see cref="GraphCanvas.RefreshPortStates"/>
        /// after the connection set changes.
        /// </summary>
        public void SetConnected(bool connected)
        {
            if (_isConnected == connected) return;
            _isConnected = connected;
            _innerDot.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void SetHovered(bool hovered)
        {
            if (_isHovered == hovered) return;
            _isHovered = hovered;
            RefreshRestingVisual();
        }

        // Resting / hover look. A live drop affordance (SetDropState) owns the
        // visuals while active, so this only paints when _dropState == None.
        void RefreshRestingVisual()
        {
            if (_dropState != DropState.None) return;

            bool dragLive = OwnerNode?.Canvas?.IsConnectionDragActive ?? false;
            bool showHover = _isHovered && !dragLive;
            EnableInClassList("cgg-graph-port--hover", showHover);

            if (showHover)
            {
                // Brighten the ring + a slight pop; the center stays dark.
                SetBorder(RingWidth, Color.Lerp(_ringColor, Color.white, 0.45f));
                style.scale = new Scale(new Vector3(HoverScale, HoverScale, 1f));
            }
            else
            {
                SetBorder(RingWidth, _ringColor);
                style.backgroundColor = CenterColor;
                style.scale = new Scale(Vector3.one);
            }
        }

        /// <summary>
        /// Toggle the drag drop-target affordance: green "can drop" ring + pop,
        /// red "incompatible" ring, or None to restore the resting look.
        /// </summary>
        public void SetDropState(DropState state)
        {
            if (_dropState == state) return;
            _dropState = state;

            EnableInClassList("cgg-graph-port--candrop", state == DropState.CanDrop);
            EnableInClassList("cgg-graph-port--incompatible", state == DropState.Incompatible);
            if (state != DropState.None)
                EnableInClassList("cgg-graph-port--hover", false);

            switch (state)
            {
                case DropState.CanDrop:
                    SetBorder(2.5f, CanDropRing);
                    style.backgroundColor = Color.Lerp(CenterColor, CanDropRing, 0.35f);
                    style.scale = new Scale(new Vector3(1.4f, 1.4f, 1f));
                    break;

                case DropState.Incompatible:
                    SetBorder(2.5f, IncompatibleRing);
                    style.backgroundColor = CenterColor;
                    style.scale = new Scale(Vector3.one);
                    break;

                default: // None — back to resting (or hover if still hovered).
                    style.backgroundColor = CenterColor;
                    RefreshRestingVisual();
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

        static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }
    }
}
