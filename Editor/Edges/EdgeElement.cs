using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Paints one directed connection as a Bezier curve in canvas-world
    /// space. Resized to the curve's bounding rect on every refresh so the
    /// element stays small and the paint area is bounded. Selectable — left
    /// click hits the curve within ~6px tolerance via a per-sample
    /// signed-distance check.
    /// </summary>
    public class EdgeElement : VisualElement
    {
        const float BoundsPadding = 40f;
        const float StrokeWidth = 2f;
        const float SelectedStrokeWidth = 3f;
        const int HitTestSamples = 24;
        const float HitTestTolerance = 6f;

        static readonly Color EdgeColor     = GraphTheme.EdgeDefault;
        static readonly Color SelectedColor = GraphTheme.EdgeSelected;

        public GraphConnection Connection { get; }
        public bool IsSelected { get; private set; }

        NodeElement _sourceNode;
        NodeElement _targetNode;
        Vector2 _localOffset;
        readonly Label _labelChip;

        public NodeElement SourceNode
        {
            get => _sourceNode;
            set { _sourceNode = value; Refresh(); }
        }

        public NodeElement TargetNode
        {
            get => _targetNode;
            set { _targetNode = value; Refresh(); }
        }

        public EdgeElement(GraphConnection connection, NodeElement sourceNode, NodeElement targetNode)
        {
            Connection = connection;
            _sourceNode = sourceNode;
            _targetNode = targetNode;

            AddToClassList("cgg-graph-edge");
            style.position = Position.Absolute;
            pickingMode = PickingMode.Position;

            // Small pill-shaped chip that displays Connection.Label at the
            // midpoint of the curve. PickingMode.Ignore so it doesn't
            // swallow clicks meant for the edge itself.
            _labelChip = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    backgroundColor = GraphTheme.SurfaceTinted,
                    color = GraphTheme.TextSecondary,
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopColor = GraphTheme.Separator,
                    borderBottomColor = GraphTheme.Separator,
                    borderLeftColor = GraphTheme.Separator,
                    borderRightColor = GraphTheme.Separator,
                    borderTopLeftRadius = 8f,
                    borderTopRightRadius = 8f,
                    borderBottomLeftRadius = 8f,
                    borderBottomRightRadius = 8f,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                    paddingTop = 1f,
                    paddingBottom = 1f,
                    fontSize = 10,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    display = DisplayStyle.None,
                },
            };
            Add(_labelChip);

            generateVisualContent += OnPaint;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
        }

        // ---------------------------------------------------------------
        // Selection
        // ---------------------------------------------------------------

        internal void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            MarkDirtyRepaint();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse) return;
            if (evt.altKey) return; // alt+click is canvas pan

            var canvas = _sourceNode?.Canvas ?? _targetNode?.Canvas;
            if (canvas == null) return;

            if (evt.shiftKey) canvas.Selection.Add(this);
            else if (evt.ctrlKey || evt.commandKey) canvas.Selection.Toggle(this);
            else canvas.Selection.SetTo(this);

            canvas.Focus();
            evt.StopPropagation();
        }

        // ---------------------------------------------------------------
        // Hit test — sample the Bezier in N segments, return true if
        // localPoint is within HitTestTolerance of any segment.
        // ---------------------------------------------------------------

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (_sourceNode == null || _targetNode == null) return false;

            Vector2 start = _sourceNode.GetOutputAnchor() + _localOffset;
            Vector2 end = _targetNode.GetInputAnchor() + _localOffset;
            ComputeControlPoints(start, end, out var c1, out var c2);

            Vector2 prev = start;
            for (int i = 1; i <= HitTestSamples; i++)
            {
                float t = (float)i / HitTestSamples;
                Vector2 cur = SampleBezier(start, c1, c2, end, t);
                if (DistanceToSegment(localPoint, prev, cur) <= HitTestTolerance)
                    return true;
                prev = cur;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Bounds + paint
        // ---------------------------------------------------------------

        public void Refresh()
        {
            if (_sourceNode == null || _targetNode == null) return;

            Vector2 start = _sourceNode.GetOutputAnchor();
            Vector2 end = _targetNode.GetInputAnchor();

            Vector2 min = Vector2.Min(start, end) - Vector2.one * BoundsPadding;
            Vector2 max = Vector2.Max(start, end) + Vector2.one * BoundsPadding;

            style.left = min.x;
            style.top = min.y;
            style.width = max.x - min.x;
            style.height = max.y - min.y;

            _localOffset = -min;
            RefreshLabel(start, end);
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Show / hide the label chip and park it at the curve's midpoint.
        /// Centered horizontally + vertically on the midpoint via negative
        /// translate (we don't know its measured size before layout, but
        /// translate -50% / -50% via Position styles isn't supported on
        /// VisualElement; using GeometryChangedEvent for measured re-center).
        /// </summary>
        void RefreshLabel(Vector2 start, Vector2 end)
        {
            var text = Connection.Label;
            if (string.IsNullOrEmpty(text))
            {
                _labelChip.style.display = DisplayStyle.None;
                return;
            }

            _labelChip.text = text;
            _labelChip.style.display = DisplayStyle.Flex;

            // Approximate midpoint of the curve in canvas world space —
            // for a symmetric cubic the bezier midpoint sits on the line
            // between start and end, biased horizontally. Close enough.
            Vector2 mid = (start + end) * 0.5f + _localOffset;
            _labelChip.style.left = mid.x;
            _labelChip.style.top = mid.y;

            // Re-center on measured size — fires after the label lays out.
            _labelChip.UnregisterCallback<GeometryChangedEvent>(OnLabelGeometry);
            _labelChip.RegisterCallback<GeometryChangedEvent>(OnLabelGeometry);
        }

        void OnLabelGeometry(GeometryChangedEvent _)
        {
            var w = _labelChip.resolvedStyle.width;
            var h = _labelChip.resolvedStyle.height;
            if (w > 0f && h > 0f)
            {
                _labelChip.style.translate = new Translate(-w * 0.5f, -h * 0.5f);
            }
        }

        void OnPaint(MeshGenerationContext ctx)
        {
            if (_sourceNode == null || _targetNode == null) return;

            Vector2 start = _sourceNode.GetOutputAnchor() + _localOffset;
            Vector2 end = _targetNode.GetInputAnchor() + _localOffset;
            ComputeControlPoints(start, end, out var c1, out var c2);

            var painter = ctx.painter2D;
            painter.strokeColor = IsSelected ? SelectedColor : EdgeColor;
            painter.lineWidth = IsSelected ? SelectedStrokeWidth : StrokeWidth;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(c1, c2, end);
            painter.Stroke();
        }

        static void ComputeControlPoints(Vector2 start, Vector2 end, out Vector2 c1, out Vector2 c2)
        {
            float dx = Mathf.Abs(end.x - start.x);
            float tension = Mathf.Max(dx * 0.5f, 30f);
            c1 = start + new Vector2(tension, 0f);
            c2 = end - new Vector2(tension, 0f);
        }

        static Vector2 SampleBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            return uu * u * p0
                + 3f * uu * t * p1
                + 3f * u * tt * p2
                + tt * t * p3;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lenSq = Vector2.Dot(ab, ab);
            if (lenSq < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
