using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Docked right-side property panel for <see cref="GraphEditorWindow"/>.
    /// Renders the currently-selected node's fields (grouped <c>[NodeGroup]</c>
    /// sections, via <see cref="NodeInspectorBody"/>) so the canvas cards can
    /// stay compact — selection drives editing here instead of on the card.
    /// Driven by <see cref="GraphSelection.Changed"/>; an edit refreshes the
    /// node's card + the panel title and flips the window's dirty flag.
    /// </summary>
    internal sealed class GraphInspectorPanel : VisualElement
    {
        const float PanelWidth = 300f;

        readonly GraphCanvas _canvas;
        readonly Label _title;
        readonly ScrollView _scroll;

        public GraphInspectorPanel(GraphCanvas canvas)
        {
            _canvas = canvas;

            AddToClassList("cgg-graph-inspector-panel");
            // Initial width; the host wraps this in a TwoPaneSplitView whose draggable
            // divider then owns the live width (clamped to minWidth below).
            style.width = PanelWidth;
            style.minWidth = 160f;
            style.flexShrink = 0f;
            style.backgroundColor = GraphTheme.SurfaceTinted;
            style.borderLeftWidth = 1f;
            style.borderLeftColor = GraphTheme.Separator;

            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 8f, paddingRight = 8f, paddingTop = 6f, paddingBottom = 6f,
                    borderBottomWidth = 1f,
                    borderBottomColor = GraphTheme.Separator,
                },
            };
            _title = new Label("Inspector")
            {
                style = { color = GraphTheme.TextSecondary, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold },
            };
            header.Add(_title);
            Add(header);

            _scroll = new ScrollView { style = { flexGrow = 1f } };
            _scroll.contentContainer.style.paddingLeft = 8f;
            _scroll.contentContainer.style.paddingRight = 8f;
            _scroll.contentContainer.style.paddingTop = 6f;
            _scroll.contentContainer.style.paddingBottom = 8f;
            Add(_scroll);

            _canvas.Selection.Changed += ScheduleRebuild;
            RegisterCallback<DetachFromPanelEvent>(_ => _canvas.Selection.Changed -= ScheduleRebuild);

            Rebuild();
        }

        // Coalesce + defer rebuilds to the next frame. A selection change made
        // DURING a pointer interaction (marquee end, node drag-start) must not run
        // a heavy synchronous PropertyField rebuild inside the event handler —
        // that can fault the marquee end or disrupt the drag's pointer capture.
        // The next-frame rebuild also collapses a marquee's many per-node
        // selection events into a single rebuild.
        bool _rebuildScheduled;

        void ScheduleRebuild()
        {
            if (_rebuildScheduled) return;
            _rebuildScheduled = true;
            schedule.Execute(() => { _rebuildScheduled = false; Rebuild(); });
        }

        void Rebuild()
        {
            _scroll.Clear();
            var nodes = _canvas.Selection.Nodes;

            if (nodes.Count == 0)
            {
                _title.text = "Inspector";
                _scroll.Add(Hint("Select a node to edit its properties."));
                return;
            }
            if (nodes.Count > 1)
            {
                _title.text = $"{nodes.Count} nodes selected";
                _scroll.Add(Hint("Select a single node to edit. Multi-edit isn't supported yet."));
                return;
            }

            NodeElement selected = null;
            foreach (var ne in nodes) { selected = ne; break; }
            var node = selected?.Node;
            if (node == null) return;

            _title.text = TitleFor(node);

            NodeInspectorBody.Build(node, _scroll.contentContainer, () =>
            {
                // An edit may change the node's title / badges — refresh the card
                // + the panel title, and flip the window's dirty flag.
                if (_canvas.TryGetNodeElement(node, out var ne)) ne?.RefreshDisplay();
                _title.text = TitleFor(node);
                _canvas.NotifyGraphChanged();
            });
        }

        static string TitleFor(GraphNodeSO node)
        {
            string t = node.DisplayTitle;
            return string.IsNullOrEmpty(t) ? node.GetType().Name : t;
        }

        static Label Hint(string text) => new Label(text)
        {
            style =
            {
                color = GraphTheme.TextSubtitle,
                fontSize = 10,
                whiteSpace = WhiteSpace.Normal,
                marginTop = 4f,
            },
        };
    }
}
