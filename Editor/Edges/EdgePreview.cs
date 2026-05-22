using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Ghost edge painted while the user is drag-connecting from an output
    /// port. Lives in the canvas's edge layer; tracks the cursor position
    /// (in canvas world coords) until <see cref="PortDragManipulator"/>
    /// either commits to a real <see cref="GraphConnection"/> or cancels.
    /// </summary>
    public class EdgePreview : VisualElement
    {
        const float BoundsPadding = 40f;
        const float StrokeWidth = 2f;

        static readonly Color PreviewColor = GraphTheme.Attention;

        readonly PortElement _sourcePort;
        Vector2 _endWorld;
        Vector2 _localOffset;

        public PortElement SourcePort => _sourcePort;

        public EdgePreview(PortElement sourcePort)
        {
            _sourcePort = sourcePort;
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

        void Refresh()
        {
            Vector2 start = _sourcePort.OwnerNode.GetOutputAnchor();
            Vector2 end = _endWorld;

            Vector2 min = Vector2.Min(start, end) - Vector2.one * BoundsPadding;
            Vector2 max = Vector2.Max(start, end) + Vector2.one * BoundsPadding;

            style.left = min.x;
            style.top = min.y;
            style.width = max.x - min.x;
            style.height = max.y - min.y;

            _localOffset = -min;
            MarkDirtyRepaint();
        }

        void OnPaint(MeshGenerationContext ctx)
        {
            Vector2 start = _sourcePort.OwnerNode.GetOutputAnchor() + _localOffset;
            Vector2 end = _endWorld + _localOffset;

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
