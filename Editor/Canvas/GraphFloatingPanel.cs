using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Base class for small floating UI surfaces docked to one corner of
    /// the canvas (start-destination picker, BT runtime inspector, etc.).
    /// Panels live in a fixed overlay layer outside the pan/zoom transform,
    /// so they stay put as the user navigates the graph.
    ///
    /// <para>
    /// Domain canvases register panels by overriding
    /// <see cref="GraphCanvas.PopulateFloatingPanels"/>. Multiple panels
    /// at the same corner stack vertically with a small gap.
    /// </para>
    /// </summary>
    public abstract class GraphFloatingPanel : VisualElement
    {
        public enum Anchor { TopLeft, TopRight, BottomLeft, BottomRight }

        static readonly Color BackgroundColor = GraphTheme.SurfaceTinted;
        static readonly Color BorderColor     = GraphTheme.Separator;
        static readonly Color TitleColor      = GraphTheme.TextSecondary;

        public Anchor PanelAnchor { get; }

        protected GraphFloatingPanel(string title, Anchor anchor)
        {
            PanelAnchor = anchor;

            AddToClassList("cgg-graph-floating-panel");
            style.position = Position.Absolute;
            style.backgroundColor = BackgroundColor;
            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            style.borderTopColor = BorderColor;
            style.borderBottomColor = BorderColor;
            style.borderLeftColor = BorderColor;
            style.borderRightColor = BorderColor;
            style.borderTopLeftRadius = 4f;
            style.borderTopRightRadius = 4f;
            style.borderBottomLeftRadius = 4f;
            style.borderBottomRightRadius = 4f;
            style.paddingTop = 4f;
            style.paddingBottom = 6f;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;

            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title)
                {
                    style =
                    {
                        color = TitleColor,
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginBottom = 4f,
                    },
                    pickingMode = PickingMode.Ignore,
                };
                Add(titleLabel);
            }
        }
    }
}
