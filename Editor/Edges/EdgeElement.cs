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
        const float HoverStrokeWidth = 3f;
        const int HitTestSamples = 24;
        const float HitTestTolerance = 6f;

        static readonly Color EdgeColor     = GraphTheme.EdgeDefault;
        static readonly Color SelectedColor = GraphTheme.EdgeSelected;
        // Hover tint — EdgeDefault lifted toward white + fully opaque so the
        // curve under the cursor reads as "live" without the selection blue.
        // (Edges paint via Painter2D, not USS, so the hover state lives here
        //  rather than in a .uss :hover rule.)
        static readonly Color HoverColor    = GraphTheme.EdgeHover;

        public GraphConnection Connection { get; }
        public bool IsSelected { get; private set; }
        bool _isHover;

        NodeElement _sourceNode;
        NodeElement _targetNode;
        Vector2 _localOffset;
        readonly Label _labelChip;

        /// <summary>Canvas this edge belongs to (resolved via its endpoints).</summary>
        public GraphCanvas Canvas => _sourceNode?.Canvas ?? _targetNode?.Canvas;

        // --- Wire-drag reroute: grab the curve and pull one end off. Which end
        // detaches depends on which half was grabbed (see OnPointerDown /
        // IsNearerTargetEnd). A plain click still just selects — the reroute
        // only begins once the pointer moves past RerouteThreshold. ---
        const float RerouteThreshold = 4f;
        bool _dragArmed;
        bool _dragging;
        int _dragPointerId = -1;
        Vector2 _downPanel;
        bool _grabbedInputHalf;
        PortElement _anchorPort;
        EdgePreview _dragPreview;

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
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        // ---------------------------------------------------------------
        // Hover — thicken (2 -> 3px) + lighten while the cursor is over the
        // curve. PickingMode.Position + the ContainsPoint hit-test keep
        // these events scoped to the actual curve, not the padded bounds.
        // ---------------------------------------------------------------

        void OnPointerEnter(PointerEnterEvent _)
        {
            // Hover yields to a live connection/reroute drag — nothing under the
            // cursor should light up mid-drag.
            if (Canvas?.IsConnectionDragActive == true) return;
            if (_isHover) return;
            _isHover = true;
            MarkDirtyRepaint();
        }

        void OnPointerLeave(PointerLeaveEvent _)
        {
            if (!_isHover) return;
            _isHover = false;
            MarkDirtyRepaint();
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

            var canvas = Canvas;
            if (canvas == null) return;

            if (evt.shiftKey) canvas.Selection.Add(this);
            else if (evt.ctrlKey || evt.commandKey) canvas.Selection.Toggle(this);
            else canvas.Selection.SetTo(this);

            canvas.Focus();

            // Arm a wire-drag reroute on a plain click (no multi-select
            // modifiers). The connection is only detached once the pointer
            // moves past the threshold (OnPointerMove), so a simple click
            // still just selects. Capture now so we receive the move/up.
            if (!evt.shiftKey && !evt.ctrlKey && !evt.commandKey)
            {
                _dragArmed = true;
                _dragging = false;
                _dragPointerId = evt.pointerId;
                _downPanel = evt.position;
                _grabbedInputHalf = IsNearerTargetEnd(evt.localPosition);
                this.CapturePointer(evt.pointerId);
            }

            evt.StopPropagation();
        }

        // ---------------------------------------------------------------
        // Wire-drag reroute
        // ---------------------------------------------------------------

        /// <summary>
        /// True if <paramref name="localPoint"/> (edge-local coords) is closer
        /// to the target/input end of the curve than the source/output end —
        /// i.e. the user grabbed the input half. Uses the same start/end the
        /// hit-test does.
        /// </summary>
        bool IsNearerTargetEnd(Vector2 localPoint)
        {
            if (_sourceNode == null || _targetNode == null) return false;
            Vector2 startLocal = _sourceNode.GetOutputAnchor() + _localOffset;
            Vector2 endLocal = _targetNode.GetInputAnchor() + _localOffset;
            return (localPoint - endLocal).sqrMagnitude < (localPoint - startLocal).sqrMagnitude;
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragArmed || evt.pointerId != _dragPointerId) return;

            if (!_dragging)
            {
                if (Vector2.Distance(evt.position, _downPanel) < RerouteThreshold) return;
                if (!BeginReroute()) { EndReroute(disposeSelf: false); return; }
            }

            UpdateReroutePreview(evt.position);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragArmed || evt.pointerId != _dragPointerId) return;

            if (_dragging)
            {
                // Drop: reconnect on a compatible port, or remove if released
                // in empty space. Either way this detached edge is disposed.
                Canvas?.CompleteConnectionPreview(_anchorPort, evt.position);
                EndReroute(disposeSelf: true);
                evt.StopPropagation();
            }
            else
            {
                // Never crossed the threshold — was just a click; keep the
                // connection and release the capture.
                EndReroute(disposeSelf: false);
            }
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_dragArmed || evt.pointerId != _dragPointerId) return;

            if (_dragging)
            {
                // Capture yanked mid-reroute: drop the preview. The connection
                // is already detached, so this dangling edge is disposed.
                Canvas?.CancelConnectionPreview();
                Canvas?.ClearDropCandidate();
                EndReroute(disposeSelf: true);
            }
            else
            {
                EndReroute(disposeSelf: false);
            }
        }

        /// <summary>
        /// Detach the connection and start the live preview from the kept end.
        /// Returns false (leaving the connection intact) when the anchor port
        /// isn't resolvable.
        /// </summary>
        bool BeginReroute()
        {
            var canvas = Canvas;
            if (canvas == null) return false;

            _anchorPort = canvas.DetachConnectionForReroute(Connection, _grabbedInputHalf);
            if (_anchorPort == null) return false;

            _dragging = true;
            // Hide the now-detached curve (kept in the hierarchy only to retain
            // the pointer capture); the EdgePreview stands in for it.
            style.opacity = 0f;
            _isHover = false; // drop any stale hover so it can't linger on the grabbed wire
            _dragPreview = canvas.BeginConnectionPreview(_anchorPort);
            return true;
        }

        void UpdateReroutePreview(Vector2 panelPos)
        {
            var canvas = Canvas;
            if (canvas == null || _dragPreview == null) return;

            Vector2 worldPos = canvas.PanelToCanvasWorld(panelPos);
            worldPos = canvas.UpdateDropCandidate(_anchorPort, panelPos, worldPos);
            _dragPreview.SetEndWorld(worldPos);
        }

        void EndReroute(bool disposeSelf)
        {
            // Reset state BEFORE releasing capture so the synchronous
            // PointerCaptureOut that release fires early-returns.
            _dragArmed = false;
            _dragging = false;

            // Drop any lingering drop-target affordance (green/red ring) on the
            // last hovered port — no-op if none is lit.
            Canvas?.ClearDropCandidate();

            if (this.HasPointerCapture(_dragPointerId))
                this.ReleasePointer(_dragPointerId);
            _dragPointerId = -1;
            _anchorPort = null;
            _dragPreview = null;

            if (disposeSelf)
            {
                Canvas?.Selection.Remove(this);
                RemoveFromHierarchy();
            }
            else
            {
                style.opacity = 1f; // restore (click path / safety)
            }
        }

        // ---------------------------------------------------------------
        // Hit test — sample the Bezier in N segments, return true if
        // localPoint is within HitTestTolerance of any segment.
        // ---------------------------------------------------------------

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (_sourceNode == null || _targetNode == null) return false;

            // Scale the grab band by inverse zoom so it keeps a stable SCREEN
            // size at low zoom (the band lives in canvas-world space).
            float tol = HitTestTolerance / Mathf.Max(Canvas?.ViewZoom ?? 1f, 0.01f);

            Vector2 start = _sourceNode.GetOutputAnchor() + _localOffset;
            Vector2 end = _targetNode.GetInputAnchor() + _localOffset;
            ComputeControlPoints(start, end, out var c1, out var c2);

            Vector2 prev = start;
            for (int i = 1; i <= HitTestSamples; i++)
            {
                float t = (float)i / HitTestSamples;
                Vector2 cur = SampleBezier(start, c1, c2, end, t);
                if (DistanceToSegment(localPoint, prev, cur) <= tol)
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

            // Hover yields to a live connection/reroute drag so the grabbed
            // wire doesn't paint a stale hover state.
            bool hover = _isHover && !(Canvas?.IsConnectionDragActive == true);

            // Selection wins over hover; hover wins over the resting look.
            Color color;
            float width;
            if (IsSelected)      { color = SelectedColor; width = SelectedStrokeWidth; }
            else if (hover)      { color = HoverColor;    width = HoverStrokeWidth; }
            else                 { color = EdgeColor;     width = StrokeWidth; }

            var painter = ctx.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(c1, c2, end);
            painter.Stroke();

            // Direction arrowhead at the curve midpoint (source -> target) for
            // graphs whose edges carry direction (e.g. nav containment).
            if (Canvas?.Asset != null && Canvas.Asset.DirectedEdges)
                DrawDirectionArrowhead(painter, start, c1, c2, end, color);

            // Endpoint grab handles — small filled dots at both ends make the
            // reroute model legible on hover. Never during a drag (hover is
            // already false then) and not while selected (selection reads as a
            // distinct state).
            if (hover && !IsSelected)
            {
                var p = ctx.painter2D;
                p.fillColor = GraphTheme.EdgeEndpointHandle;
                p.BeginPath();
                p.Arc(start, 3.5f, 0f, 360f);
                p.Fill();
                p.BeginPath();
                p.Arc(end, 3.5f, 0f, 360f);
                p.Fill();
            }
        }

        // Fill a small triangle at the curve midpoint, pointing along the
        // tangent (source -> target). Sized in canvas-world px so it scales with
        // zoom like the wire itself.
        static void DrawDirectionArrowhead(Painter2D p, Vector2 start, Vector2 c1, Vector2 c2, Vector2 end, Color color)
        {
            const float Front = 3.5f;   // tip ahead of the midpoint
            const float Back = 2.5f;    // base behind the midpoint
            const float HalfW = 3.25f;  // half the base width

            Vector2 mid = SampleBezier(start, c1, c2, end, 0.5f);
            // Cubic derivative at t=0.5 (u=0.5): 0.75(c1-start) + 1.5(c2-c1) + 0.75(end-c2).
            Vector2 tangent = 0.75f * (c1 - start) + 1.5f * (c2 - c1) + 0.75f * (end - c2);
            if (tangent.sqrMagnitude < 1e-6f) return;
            Vector2 dir = tangent.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            Vector2 tip = mid + dir * Front;
            Vector2 baseC = mid - dir * Back;
            Vector2 baseL = baseC + perp * HalfW;
            Vector2 baseR = baseC - perp * HalfW;

            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(baseL);
            p.LineTo(baseR);
            p.ClosePath();
            p.Fill();
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
