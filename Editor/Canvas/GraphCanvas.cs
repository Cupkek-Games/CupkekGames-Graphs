using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Root canvas VisualElement for the graph editor. Owns a transformable
    /// content layer (where nodes/edges live) and a fixed background layer
    /// (where the grid paints). Pan and zoom mutate the content layer's
    /// transform; the background reads view state and repaints accordingly.
    ///
    /// Call <see cref="BindToAsset"/> once with the graph being edited to
    /// populate the canvas and wire the right-click "Create Node" menu.
    /// </summary>
    public partial class GraphCanvas : VisualElement
    {
        public const float MinZoom = 0.1f;
        public const float MaxZoom = 3f;

        readonly GridBackground _background;
        readonly VisualElement _content;
        readonly VisualElement _groupLayer;
        readonly VisualElement _edgeLayer;
        readonly VisualElement _nodeLayer;
        readonly MinimapElement _minimap;
        readonly VisualElement _overlayLayer;
        readonly List<GraphFloatingPanel> _floatingPanels = new List<GraphFloatingPanel>();
        readonly Dictionary<GraphNodeSO, NodeElement> _nodeElements = new Dictionary<GraphNodeSO, NodeElement>();
        readonly Dictionary<GraphConnection, EdgeElement> _edgeElements = new Dictionary<GraphConnection, EdgeElement>();
        readonly Dictionary<GraphGroup, GroupElement> _groupElements = new Dictionary<GraphGroup, GroupElement>();

        Vector2 _viewOffset = Vector2.zero;
        float _viewZoom = 1f;

        /// <summary>Most recent pointer panel-space position; used as the spawn
        /// point for keyboard-triggered node search (Space).</summary>
        Vector2 _lastPointerPanelPos;

        ContextualMenuManipulator _menuManipulator;

        public GraphAssetSO Asset { get; private set; }

        /// <summary>Selection state — single instance, lives for the canvas's lifetime.</summary>
        public GraphSelection Selection { get; } = new GraphSelection();

        /// <summary>
        /// When true, node-drag rounds positions to the nearest
        /// <see cref="GridBackground.MinorSpacing"/> cell. Toolbar exposes a
        /// toggle; off by default.
        /// </summary>
        public bool SnapToGrid { get; set; }

        /// <summary>Show / hide the grid lines without touching the dark backdrop.</summary>
        public bool GridVisible
        {
            get => _background.Visible;
            set => _background.Visible = value;
        }

        /// <summary>Show / hide the floating minimap in the top-right corner.</summary>
        public bool MinimapVisible
        {
            get => _minimap.style.display != DisplayStyle.None;
            set => _minimap.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Fires after every pan / zoom / reset — toolbars and status bars subscribe.</summary>
        public event System.Action ViewChanged;

        /// <summary>
        /// Fires after the bound graph asset is mutated through this canvas
        /// (node / connection / paste / undo). Validation footers and status
        /// indicators subscribe to refresh on demand instead of polling.
        /// </summary>
        public event System.Action GraphChanged;

        void RaiseGraphChanged() => GraphChanged?.Invoke();

        /// <summary>
        /// Public alias for raising <see cref="GraphChanged"/> — used by
        /// manipulators outside the canvas class (e.g. NodeDragManipulator)
        /// to signal that the bound asset's state changed and the window
        /// should mark itself dirty.
        /// </summary>
        public void NotifyGraphChanged() => RaiseGraphChanged();

        /// <summary>
        /// Fires when something on the canvas asks to descend into a sub-graph
        /// (e.g. double-clicking an <see cref="ISubGraphNode"/>). The canvas does
        /// NOT switch assets itself — the host <see cref="GraphEditorWindow"/>
        /// subscribes and pushes the child onto its nav stack.
        /// </summary>
        public event System.Action<GraphAssetSO> DescendRequested;

        /// <summary>Raise <see cref="DescendRequested"/> for <paramref name="child"/> (ignored if null).</summary>
        public void RequestDescend(GraphAssetSO child)
        {
            if (child != null) DescendRequested?.Invoke(child);
        }

        /// <summary>Every node element currently mounted on the canvas, in no particular order.</summary>
        public IEnumerable<NodeElement> NodeElements => _nodeElements.Values;

        /// <summary>The layer where nodes, edges, and groups should be parented.</summary>
        public VisualElement ContentLayer => _content;

        /// <summary>Translation applied to the content layer (in canvas-local pixels).</summary>
        public Vector2 ViewOffset
        {
            get => _viewOffset;
            set { _viewOffset = value; ApplyTransform(); }
        }

        /// <summary>Uniform scale applied to the content layer.</summary>
        public float ViewZoom
        {
            get => _viewZoom;
            set { _viewZoom = Mathf.Clamp(value, MinZoom, MaxZoom); ApplyTransform(); }
        }

        /// <summary>Package-absolute path to the shared editor stylesheet.</summary>
        const string StyleSheetPath = "Packages/com.cupkekgames.graphs/Editor/GraphEditor.uss";

        public GraphCanvas()
        {
            AddToClassList("cgg-graph-canvas");

            // Attach the shared visual foundation. Class names + state
            // modifiers in GraphEditor.uss (cgg-graph-node, --selected /
            // --hover / --start, cgg-graph-port, --candrop / --incompatible)
            // are toggled in C# by later interaction streams. Loaded by its
            // package-absolute path so it resolves regardless of how the
            // package is consumed (registry / file: override).
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (sheet != null)
                styleSheets.Add(sheet);

            style.overflow = Overflow.Hidden;
            style.flexGrow = 1;
            focusable = true;

            _background = new GridBackground();
            _background.style.position = Position.Absolute;
            _background.style.left = 0;
            _background.style.top = 0;
            _background.style.right = 0;
            _background.style.bottom = 0;
            _background.pickingMode = PickingMode.Ignore;
            Add(_background);

            _content = new VisualElement();
            _content.AddToClassList("cgg-graph-canvas__content");
            _content.style.position = Position.Absolute;
            _content.style.left = 0;
            _content.style.top = 0;
            _content.usageHints = UsageHints.DynamicTransform;
            // The content layer must not intercept pointer events when empty;
            // pan/zoom relies on the canvas itself receiving them.
            _content.pickingMode = PickingMode.Ignore;
            Add(_content);

            // Sub-layers inside _content. Z-order: groups (behind) → edges →
            // nodes (on top). Each layer ignores picking itself so empty
            // areas fall through to the canvas's manipulators.
            _groupLayer = new VisualElement();
            _groupLayer.AddToClassList("cgg-graph-canvas__group-layer");
            _groupLayer.pickingMode = PickingMode.Ignore;
            _content.Add(_groupLayer);

            _edgeLayer = new VisualElement();
            _edgeLayer.AddToClassList("cgg-graph-canvas__edge-layer");
            _edgeLayer.pickingMode = PickingMode.Ignore;
            _content.Add(_edgeLayer);

            _nodeLayer = new VisualElement();
            _nodeLayer.AddToClassList("cgg-graph-canvas__node-layer");
            _nodeLayer.pickingMode = PickingMode.Ignore;
            _content.Add(_nodeLayer);

            // Floating overlay — added as a direct child so it lives in
            // screen-space and isn't pan/zoom'd by _content's transform.
            _minimap = new MinimapElement(this);
            Add(_minimap);

            // Sibling overlay layer for domain-supplied floating panels
            // (start picker, runtime inspector, etc.). Picking ignored so
            // the layer doesn't swallow pan/zoom; each panel has its own
            // picking mode for its interactive bits.
            _overlayLayer = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0, top = 0, right = 0, bottom = 0,
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(_overlayLayer);

            this.AddManipulator(new PanZoomManipulator());
            // MarqueeManipulator owns left-click on the canvas: drag = paint
            // selection rect, plain click = clear selection (degenerate 0x0
            // rect collapses to a clear). Shift preserves existing selection.
            this.AddManipulator(new MarqueeManipulator());

            ApplyTransform();

            RegisterCallback<DetachFromPanelEvent>(_ => Unbind());
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<PointerMoveEvent>(evt => _lastPointerPanelPos = evt.position);

            // Off-canvas mutations (e.g. a domain inspector picking a new
            // start destination) raise GraphAssetSO.EditorAssetMutated.
            // Refresh visuals when our bound asset is the one that changed.
            GraphAssetSO.EditorAssetMutated += OnAssetMutatedExternally;
            RegisterCallback<DetachFromPanelEvent>(_ => GraphAssetSO.EditorAssetMutated -= OnAssetMutatedExternally);
        }

        void OnAssetMutatedExternally(GraphAssetSO asset)
        {
            if (asset != Asset) return;
            RefreshStartNodeOutline();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (DeleteSelection())
                    evt.StopPropagation();
                return;
            }

            // Unmodified-key shortcuts.
            if (!evt.ctrlKey && !evt.commandKey && !evt.altKey && !evt.shiftKey)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Space:
                        OpenNodeSearchAtPanel(_lastPointerPanelPos);
                        evt.StopPropagation();
                        return;
                    case KeyCode.F:
                        ResetView();
                        evt.StopPropagation();
                        return;
                }
            }

            // Clipboard shortcuts — Cmd on macOS, Ctrl elsewhere.
            bool cmd = evt.ctrlKey || evt.commandKey;
            if (!cmd) return;

            switch (evt.keyCode)
            {
                case KeyCode.C:
                    Copy();
                    evt.StopPropagation();
                    break;
                case KeyCode.V:
                    Paste();
                    evt.StopPropagation();
                    break;
                case KeyCode.X:
                    Cut();
                    evt.StopPropagation();
                    break;
                case KeyCode.D:
                    Duplicate();
                    evt.StopPropagation();
                    break;
            }
        }

        void ApplyTransform()
        {
            // Unity 6 deprecated VisualElement.transform.position/scale in
            // favour of style.translate / style.scale (which compose with the
            // rest of the style system and survive USS animations).
            _content.style.translate = new Translate(_viewOffset.x, _viewOffset.y);
            _content.style.scale = new Scale(new Vector3(_viewZoom, _viewZoom, 1f));
            _background.SetView(_viewOffset, _viewZoom);
            ViewChanged?.Invoke();
        }

        /// <summary>
        /// Convert a canvas-local screen position (e.g. an event's
        /// <c>localMousePosition</c>) to world coordinates inside
        /// <see cref="ContentLayer"/>.
        /// </summary>
        public Vector2 ScreenToWorld(Vector2 screenPos)
        {
            return (screenPos - _viewOffset) / _viewZoom;
        }

        /// <summary>Inverse of <see cref="ScreenToWorld"/>.</summary>
        public Vector2 WorldToScreen(Vector2 worldPos)
        {
            return worldPos * _viewZoom + _viewOffset;
        }

        /// <summary>Translate the view by <paramref name="delta"/> screen-space pixels.</summary>
        public void Pan(Vector2 delta)
        {
            _viewOffset += delta;
            ApplyTransform();
        }

        /// <summary>
        /// Zoom toward / away from <paramref name="pivotScreen"/>, keeping
        /// the world point under the pivot anchored.
        /// </summary>
        public void Zoom(float zoomDelta, Vector2 pivotScreen)
        {
            float oldZoom = _viewZoom;
            float newZoom = Mathf.Clamp(_viewZoom + zoomDelta, MinZoom, MaxZoom);
            if (Mathf.Approximately(oldZoom, newZoom))
                return;

            Vector2 worldPivot = (pivotScreen - _viewOffset) / oldZoom;
            _viewOffset = pivotScreen - worldPivot * newZoom;
            _viewZoom = newZoom;
            ApplyTransform();
        }

        /// <summary>Restore the identity view (no pan, no zoom).</summary>
        public void ResetView()
        {
            _viewOffset = Vector2.zero;
            _viewZoom = 1f;
            ApplyTransform();
        }

        /// <summary>
        /// Run a one-shot tree layout over the bound asset. See
        /// <see cref="AutoLayoutEngine"/> for the algorithm. The whole
        /// pass is wrapped in a single Undo group so Ctrl+Z restores the
        /// prior layout.
        /// </summary>
        public void AutoLayout()
        {
            if (Asset == null) return;
            AutoLayoutEngine.LayoutTree(Asset);

            // Snap visuals to the new positions + re-route edges.
            foreach (var ne in _nodeElements.Values)
                ne?.ApplyPosition();
            foreach (var ed in _edgeElements.Values)
                ed?.Refresh();

            EditorUtility.SetDirty(Asset);
            RaiseGraphChanged();
        }

        /// <summary>
        /// Pan (without changing zoom) so <paramref name="ne"/>'s rough centre
        /// lands at the canvas viewport centre. Used by the validation footer
        /// to "jump to" an offending node when the user clicks an issue row.
        /// </summary>
        public void FocusOnNode(NodeElement ne)
        {
            if (ne?.Node == null) return;

            float w = ne.layout.width  > 0f ? ne.layout.width  : 180f;
            float h = ne.layout.height > 0f ? ne.layout.height : 80f;
            Vector2 nodeCenterWorld = ne.Node.Position + new Vector2(w, h) * 0.5f;
            CenterOn(nodeCenterWorld);
        }

        /// <summary>Pan so <paramref name="worldPos"/> sits at the viewport centre.</summary>
        public void CenterOn(Vector2 worldPos)
        {
            Vector2 canvasCenter = new Vector2(layout.width, layout.height) * 0.5f;
            // canvasCenter = worldPos * zoom + offset  ⇒  offset = canvasCenter − worldPos * zoom
            _viewOffset = canvasCenter - worldPos * _viewZoom;
            ApplyTransform();
        }

        // ---------------------------------------------------------------
        // Asset binding
        // ---------------------------------------------------------------

        /// <summary>
        /// Bind the canvas to <paramref name="asset"/>: render existing
        /// nodes, wire the right-click create menu. Pass null to unbind.
        /// Safe to call multiple times — the prior binding is cleared first.
        /// </summary>
        public virtual void BindToAsset(GraphAssetSO asset)
        {
            Unbind();
            Asset = asset;
            if (asset == null) return;

            RebuildNodeElements();
            RebuildFloatingPanels();

            _menuManipulator = new ContextualMenuManipulator(OnPopulateContextMenu);
            this.AddManipulator(_menuManipulator);

            Undo.undoRedoPerformed += OnUndoRedo;

            RaiseGraphChanged();
        }

        /// <summary>
        /// Hook for domain canvases to register floating overlay panels
        /// (e.g. nav's start-destination picker). Called once per
        /// <see cref="BindToAsset"/>; the canvas owns positioning + the
        /// overlay layer. Add panels to <paramref name="panels"/>; the
        /// canvas mounts them to their requested corner.
        /// </summary>
        protected virtual void PopulateFloatingPanels(List<GraphFloatingPanel> panels) { }

        void RebuildFloatingPanels()
        {
            foreach (var p in _floatingPanels) p.RemoveFromHierarchy();
            _floatingPanels.Clear();

            PopulateFloatingPanels(_floatingPanels);

            // Stack panels at each corner. Per-corner offset accumulates
            // so multiple panels at the same anchor don't overlap.
            const float margin = 8f;
            const float gap = 6f;
            var corners = new Dictionary<GraphFloatingPanel.Anchor, float>
            {
                { GraphFloatingPanel.Anchor.TopLeft, margin },
                { GraphFloatingPanel.Anchor.TopRight, margin },
                { GraphFloatingPanel.Anchor.BottomLeft, margin },
                { GraphFloatingPanel.Anchor.BottomRight, margin },
            };

            foreach (var panel in _floatingPanels)
            {
                _overlayLayer.Add(panel);
                float offset = corners[panel.PanelAnchor];
                ApplyAnchor(panel, offset);
                // Defer height read until layout has resolved so the next
                // panel at the same corner stacks below this one.
                var captured = panel;
                var anchor = panel.PanelAnchor;
                panel.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    float h = captured.resolvedStyle.height;
                    if (h > 0f) corners[anchor] = offset + h + gap;
                });
            }
        }

        static void ApplyAnchor(GraphFloatingPanel panel, float offset)
        {
            const float side = 8f;
            switch (panel.PanelAnchor)
            {
                case GraphFloatingPanel.Anchor.TopLeft:
                    panel.style.left = side; panel.style.top = offset;
                    panel.style.right = StyleKeyword.Auto; panel.style.bottom = StyleKeyword.Auto;
                    break;
                case GraphFloatingPanel.Anchor.TopRight:
                    panel.style.right = side; panel.style.top = offset;
                    panel.style.left = StyleKeyword.Auto; panel.style.bottom = StyleKeyword.Auto;
                    break;
                case GraphFloatingPanel.Anchor.BottomLeft:
                    panel.style.left = side; panel.style.bottom = offset;
                    panel.style.right = StyleKeyword.Auto; panel.style.top = StyleKeyword.Auto;
                    break;
                case GraphFloatingPanel.Anchor.BottomRight:
                    panel.style.right = side; panel.style.bottom = offset;
                    panel.style.left = StyleKeyword.Auto; panel.style.top = StyleKeyword.Auto;
                    break;
            }
        }

        void Unbind()
        {
            if (Asset == null && _nodeElements.Count == 0 && _menuManipulator == null)
                return;

            Undo.undoRedoPerformed -= OnUndoRedo;

            Selection.Clear();

            foreach (var elem in _nodeElements.Values)
                elem.RemoveFromHierarchy();
            _nodeElements.Clear();

            foreach (var edge in _edgeElements.Values)
                edge.RemoveFromHierarchy();
            _edgeElements.Clear();

            foreach (var group in _groupElements.Values)
                group.RemoveFromHierarchy();
            _groupElements.Clear();

            foreach (var panel in _floatingPanels)
                panel.RemoveFromHierarchy();
            _floatingPanels.Clear();

            if (_menuManipulator != null)
            {
                this.RemoveManipulator(_menuManipulator);
                _menuManipulator = null;
            }
            Asset = null;
        }

        void OnUndoRedo()
        {
            if (Asset == null) return;
            RebuildNodeElements();
            RaiseGraphChanged();
        }

        // Element rebuild / add / find ops live in GraphCanvas.Elements.cs.

        // Drag-to-connect + drag-to-detach/reroute ops (BeginPreview /
        // CompletePreview / CancelPreview / CanConnect / CreateConnection /
        // DetachConnectionAt / DetachConnectionForReroute) live in
        // GraphCanvas.DragConnect.cs.

        // ---------------------------------------------------------------
        // Selection helpers
        // ---------------------------------------------------------------

        public bool TryGetNodeElement(GraphNodeSO node, out NodeElement element)
        {
            return _nodeElements.TryGetValue(node, out element);
        }

        // Clipboard ops (Cut / Copy / Paste / Duplicate / DeleteSelection)
        // live in GraphCanvas.Clipboard.cs.

        // Context-menu populators + node-create flows live in
        // GraphCanvas.ContextMenu.cs.
    }
}
