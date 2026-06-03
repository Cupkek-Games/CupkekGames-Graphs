using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Ghost edge painted while the user is drag-connecting or
    /// drag-rerouting. One end is anchored to a fixed port; the other tracks
    /// the cursor (in canvas world coords). The anchor may be an OUTPUT port
    /// (a normal new-connection / target-reroute drag — loose end seeks an
    /// input) or an INPUT port (a source-reroute drag — loose end seeks an
    /// output). Lives in the canvas's edge layer until the drag either
    /// commits to a real <see cref="GraphConnection"/> or cancels.
    /// </summary>
    public class EdgePreview : VisualElement
    {
        const float BoundsPadding = 40f;
        const float StrokeWidth = 2f;

        static readonly Color PreviewColor = GraphTheme.Attention;

        readonly PortElement _anchorPort;
        Vector2 _endWorld;
        Vector2 _localOffset;

        public PortElement AnchorPort => _anchorPort;

        public EdgePreview(PortElement anchorPort)
        {
            _anchorPort = anchorPort;
            AddToClassList("cgg-graph-edge-preview");
            style.position = Position.Absolute;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnPaint;
        }

        /// <summary>Update the cursor end in canvas world coordinates.</summary>
        public void SetEndWorld(Vector2 worldPos)
        {
            _endWorld = worldPos;
            Refresh();
        }

        /// <summary>
        /// World-space anchor point: the input edge of the node for an input
        /// anchor, the output edge for an output anchor.
        /// </summary>
        Vector2 AnchorWorld()
        {
            return _anchorPort.IsInput
                ? _anchorPort.OwnerNode.GetInputAnchor()
                : _anchorPort.OwnerNode.GetOutputAnchor();
        }

        void Refresh()
        {
            Vector2 anchor = AnchorWorld();
            Vector2 end = _endWorld;

            Vector2 min = Vector2.Min(anchor, end) - Vector2.one * BoundsPadding;
            Vector2 max = Vector2.Max(anchor, end) + Vector2.one * BoundsPadding;

            style.left = min.x;
            style.top = min.y;
            style.width = max.x - min.x;
            style.height = max.y - min.y;

            _localOffset = -min;
            MarkDirtyRepaint();
        }

        void OnPaint(MeshGenerationContext ctx)
        {
            Vector2 anchor = AnchorWorld() + _localOffset;
            Vector2 loose = _endWorld + _localOffset;

            // Always paint output-side -> input-side so the curve bows the
            // natural way regardless of which end is anchored: for an input
            // anchor the loose (cursor) end plays the output role.
            Vector2 start = _anchorPort.IsInput ? loose : anchor; // output-ish (left)
            Vector2 end = _anchorPort.IsInput ? anchor : loose;   // input-ish (right)

            float dx = Mathf.Abs(end.x - start.x);
            float tension = Mathf.Max(dx * 0.5f, 30f);
            Vector2 c1 = start + new Vector2(tension, 0f);
            Vector2 c2 = end - new Vector2(tension, 0f);

            var painter = ctx.painter2D;
            painter.strokeColor = PreviewColor;
            painter.lineWidth = StrokeWidth;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(c1, c2, end);
            painter.Stroke();
        }
    }
}
