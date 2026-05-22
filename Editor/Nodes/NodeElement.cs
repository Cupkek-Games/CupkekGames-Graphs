using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Card-style visual representation of a single <see cref="GraphNodeSO"/>
    /// on the canvas. Header strip with title + optional subtitle, plus a
    /// body strip containing the input and output port stubs (one of each
    /// for now — multi-port support lands in a later piece).
    ///
    /// Position is bound to the underlying node SO — drag the element and
    /// the SO's <see cref="GraphNodeSO.Position"/> follows.
    /// </summary>
    public class NodeElement : VisualElement
    {
        const float HeaderHeight = 28f;
        const float PortRowMinHeight = 28f;
        const float CornerRadius = 4f;

        static readonly Color BackgroundColor     = GraphTheme.Surface;
        static readonly Color BorderColor         = GraphTheme.Separator;
        static readonly Color SelectedBorderColor = GraphTheme.SelectionAccent;
        static readonly Color StartBorderColor    = GraphTheme.StartAccent;
        static readonly Color SubtitleColor       = GraphTheme.TextSubtitle;

        readonly GraphCanvas _canvas;
        readonly GraphNodeSO _node;

        // Protected so subclasses (e.g. StickyNoteElement) can hide or
        // replace the inherited chrome with their own visual layout.
        protected readonly VisualElement _header;
        protected readonly Label _iconLabel;
        protected readonly Label _titleLabel;
        protected readonly Label _subtitleLabel;
        protected readonly VisualElement _portRow;
        protected readonly VisualElement _body;

        readonly List<PortElement> _inputPorts = new List<PortElement>();
        readonly List<PortElement> _outputPorts = new List<PortElement>();

        public GraphCanvas Canvas => _canvas;
        public GraphNodeSO Node => _node;

        public IReadOnlyList<PortElement> InputPorts => _inputPorts;
        public IReadOnlyList<PortElement> OutputPorts => _outputPorts;

        public bool IsSelected { get; private set; }
        public bool IsStartNode { get; private set; }

        /// <summary>
        /// Toggle the selection halo. Called by <see cref="GraphSelection"/>;
        /// don't call directly — go through the selection so other state stays
        /// consistent.
        /// </summary>
        internal void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            UpdateBorder();
        }

        /// <summary>
        /// Mark this node as the graph's start node — paints a coloured
        /// outline AND swaps the header strip to <see cref="GraphTheme.StartAccent"/>
        /// (green pill look) so the start is unmistakable. Called by
        /// <see cref="GraphCanvas"/> after the element is added, based
        /// on <see cref="GraphAssetSO.StartNodeGuid"/>.
        /// </summary>
        internal void SetIsStartNode(bool isStart)
        {
            if (IsStartNode == isStart) return;
            IsStartNode = isStart;
            UpdateBorder();
            RefreshDisplay();  // repaint header bg + title prefix
        }

        public NodeElement(GraphCanvas canvas, GraphNodeSO node)
        {
            _canvas = canvas;
            _node = node;

            AddToClassList("cgg-graph-node");
            style.position = Position.Absolute;
            style.minWidth = node != null ? node.PreferredWidth : 240f;
            style.backgroundColor = BackgroundColor;
            style.overflow = Overflow.Visible;

            UpdateBorder();

            _header = new VisualElement();
            _header.AddToClassList("cgg-graph-node__header");
            _header.style.flexDirection = FlexDirection.Row;
            _header.style.alignItems = Align.Center;
            _header.style.paddingLeft = 8f;
            _header.style.paddingRight = 8f;
            _header.style.height = HeaderHeight;
            _header.pickingMode = PickingMode.Ignore;
            Add(_header);

            // Optional glyph in front of the title — driven by
            // GraphNodeSO.IconGlyph (default null hides the slot). Domain
            // nodes can return a Material-icon code or a single character
            // here to type-tag their entries without an asset reference.
            _iconLabel = new Label();
            _iconLabel.AddToClassList("cgg-graph-node__icon");
            _iconLabel.style.color = Color.white;
            _iconLabel.style.fontSize = 13;
            _iconLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _iconLabel.style.marginRight = 6f;
            _iconLabel.style.display = DisplayStyle.None;
            _iconLabel.pickingMode = PickingMode.Ignore;
            _header.Add(_iconLabel);

            _titleLabel = new Label();
            _titleLabel.AddToClassList("cgg-graph-node__title");
            _titleLabel.style.color = Color.white;
            _titleLabel.style.fontSize = 13;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.pickingMode = PickingMode.Ignore;
            _header.Add(_titleLabel);

            _subtitleLabel = new Label();
            _subtitleLabel.AddToClassList("cgg-graph-node__subtitle");
            _subtitleLabel.style.color = SubtitleColor;
            _subtitleLabel.style.fontSize = 10;
            _subtitleLabel.style.paddingLeft = 8f;
            _subtitleLabel.style.paddingRight = 8f;
            _subtitleLabel.style.paddingTop = 4f;
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
            _body.style.paddingLeft = 8f;
            _body.style.paddingRight = 8f;
            _body.style.paddingTop = 2f;
            _body.style.paddingBottom = 6f;
            Add(_body);
            if (_node != null && _node.ShowInlineProperties)
                BuildBody(_body);

            RebuildPorts();
            RefreshDisplay();
            ApplyPosition();

            this.AddManipulator(new NodeDragManipulator());
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
            var so = new SerializedObject(_node);
            var iter = so.GetIterator();
            iter.NextVisible(enterChildren: true); // skip m_Script
            while (iter.NextVisible(enterChildren: false))
            {
                var field = new PropertyField(iter.Copy());
                field.Bind(so);
                container.Add(field);
            }

            // Inline edits dirty the bound asset — notify the canvas so the
            // window's "*" flips when a field changes.
            container.TrackSerializedObjectValue(so, _ => _canvas?.NotifyGraphChanged());
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
            // entry point reads from across the canvas. Selection halo
            // still wins over the green outline; see UpdateBorder.
            string title = _node.DisplayTitle ?? string.Empty;
            _titleLabel.text = IsStartNode ? "★ " + title : title;
            _header.style.backgroundColor = IsStartNode
                ? GraphTheme.StartAccent
                : _node.HeaderColor;

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
            if (string.IsNullOrEmpty(subtitle))
            {
                _subtitleLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _subtitleLabel.style.display = DisplayStyle.Flex;
                _subtitleLabel.text = subtitle;
            }
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
                _inputPorts.Add(port);
                Add(port);
            }

            idx = 0;
            foreach (var def in _node.OutputPorts)
            {
                var port = new PortElement(this, isInput: false, def, idx++);
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
                p.style.left = -PortElement.PortSize * 0.5f;
                p.style.top = centerY + (i - (_inputPorts.Count - 1) * 0.5f) * (PortElement.PortSize + 6f);
            }
            for (int i = 0; i < _outputPorts.Count; i++)
            {
                var p = _outputPorts[i];
                p.style.right = -PortElement.PortSize * 0.5f;
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
                width = _node != null ? _node.PreferredWidth : 240f;

            float portX = isInput ? -PortElement.PortSize * 0.5f : width + PortElement.PortSize * 0.5f;
            return _node.Position + new Vector2(portX, portY);
        }

        void UpdateBorder()
        {
            // Selection takes priority over the start-node outline so the
            // user always knows what's currently selected.
            Color color = IsSelected
                ? SelectedBorderColor
                : (IsStartNode ? StartBorderColor : BorderColor);
            float width = IsSelected || IsStartNode ? 2f : 1f;

            style.borderTopLeftRadius = CornerRadius;
            style.borderTopRightRadius = CornerRadius;
            style.borderBottomLeftRadius = CornerRadius;
            style.borderBottomRightRadius = CornerRadius;
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
