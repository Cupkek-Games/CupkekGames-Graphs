using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Bottom status bar for the <see cref="GraphEditorWindow"/>. Shows the
    /// zoom percentage, node count, and selection count. Subscribes to the
    /// canvas's <see cref="GraphCanvas.ViewChanged"/> and
    /// <see cref="GraphSelection.Changed"/> so it stays in sync without polling.
    /// </summary>
    public class GraphStatusBar : VisualElement
    {
        // SurfaceElevated (not CanvasBackdrop) so the bar reads as chrome
        // sitting above the canvas. With the retuned palette SurfaceElevated
        // is genuinely lighter than the backdrop, so the bar is no longer
        // invisible against it (and now matches the toolbar's plane).
        static readonly Color BackgroundColor = GraphTheme.SurfaceElevated;
        static readonly Color SeparatorColor  = GraphTheme.Separator;
        static readonly Color TextColor       = GraphTheme.TextSecondary;

        readonly GraphCanvas _canvas;
        readonly Label _zoomLabel;
        readonly Label _nodeCountLabel;
        readonly Label _selectionLabel;

        public GraphStatusBar(GraphCanvas canvas)
        {
            _canvas = canvas;

            AddToClassList("cgg-graph-status-bar");
            style.flexDirection = FlexDirection.Row;
            style.height = 22f;
            style.backgroundColor = BackgroundColor;
            style.borderTopWidth = 1f;
            style.borderTopColor = SeparatorColor;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.alignItems = Align.Center;

            _zoomLabel = MakeStatLabel();
            Add(_zoomLabel);
            AddSeparator();

            _nodeCountLabel = MakeStatLabel();
            Add(_nodeCountLabel);
            AddSeparator();

            _selectionLabel = MakeStatLabel();
            Add(_selectionLabel);

            // React to canvas view + selection events. Detach unhooks so we
            // don't leak when the window is closed.
            canvas.ViewChanged += Refresh;
            canvas.Selection.Changed += Refresh;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                canvas.ViewChanged -= Refresh;
                canvas.Selection.Changed -= Refresh;
            });

            Refresh();
        }

        public void Refresh()
        {
            _zoomLabel.text       = $"Zoom {Mathf.RoundToInt(_canvas.ViewZoom * 100f)}%";
            _nodeCountLabel.text  = $"Nodes {(_canvas.Asset != null ? _canvas.Asset.Nodes.Count : 0)}";
            int sel = _canvas.Selection.Nodes.Count;
            _selectionLabel.text  = sel == 0 ? "—" : (sel == 1 ? "1 selected" : $"{sel} selected");
        }

        static Label MakeStatLabel()
        {
            return new Label
            {
                style =
                {
                    color = TextColor,
                    fontSize = 11,
                    marginRight = 0f,
                },
            };
        }

        void AddSeparator()
        {
            var sep = new VisualElement
            {
                style =
                {
                    width = 1f,
                    height = 14f,
                    marginLeft = 10f,
                    marginRight = 10f,
                    backgroundColor = GraphTheme.SeparatorLine,
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(sep);
        }
    }
}
