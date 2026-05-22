using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Floating overview painted in the top-right corner of the canvas.
    /// Shows every node as a small rect plus a stroked rectangle marking
    /// the current viewport — click anywhere inside to pan the canvas
    /// onto that world position. Refreshes on
    /// <see cref="GraphCanvas.GraphChanged"/> and
    /// <see cref="GraphCanvas.ViewChanged"/> so it never goes stale.
    /// </summary>
    public class MinimapElement : VisualElement
    {
        const float MinimapWidth = 180f;
        const float MinimapHeight = 110f;
        const float InnerPadding = 6f;
        const float NodeApproxWidth = 180f;
        const float NodeApproxHeight = 80f;
        const float BoundsExpand = 80f;
        const float NodeMinPixelSize = 3f;

        static readonly Color BackgroundColor = GraphTheme.MinimapBackground;
        static readonly Color BorderColor     = GraphTheme.MinimapBorder;
        static readonly Color NodeColor       = GraphTheme.SelectionAccentMid;
        static readonly Color ViewportColor   = GraphTheme.Attention;

        readonly GraphCanvas _canvas;

        public MinimapElement(GraphCanvas canvas)
        {
            _canvas = canvas;

            AddToClassList("cgg-graph-minimap");
            style.position = Position.Absolute;
            style.top = 8f;
            style.right = 8f;
            style.width = MinimapWidth;
            style.height = MinimapHeight;
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
            pickingMode = PickingMode.Position;

            generateVisualContent += OnPaint;
            canvas.GraphChanged += MarkDirtyRepaint;
            canvas.ViewChanged += MarkDirtyRepaint;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                canvas.GraphChanged -= MarkDirtyRepaint;
                canvas.ViewChanged -= MarkDirtyRepaint;
            });

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        // ---------------------------------------------------------------
        // Paint
        // ---------------------------------------------------------------

        void OnPaint(MeshGenerationContext ctx)
        {
            if (_canvas.Asset == null) return;

            Rect bounds = ComputeBounds();
            if (bounds.width <= 0f || bounds.height <= 0f) return;

            ComputeProjection(bounds, out Vector2 contentOrigin, out float scale);
            if (scale <= 0f) return;

            var painter = ctx.painter2D;

            // Nodes
            painter.fillColor = NodeColor;
            foreach (var node in _canvas.Asset.Nodes)
            {
                if (node == null) continue;
                Vector2 tl = WorldToMinimap(node.Position, bounds.position, contentOrigin, scale);
                Vector2 size = new Vector2(NodeApproxWidth, NodeApproxHeight) * scale;
                if (size.x < NodeMinPixelSize) size.x = NodeMinPixelSize;
                if (size.y < NodeMinPixelSize) size.y = NodeMinPixelSize;
                PaintRect(painter, tl, size, fill: true);
            }

            // Viewport rect — world-space top-left = -ViewOffset / zoom; size = canvas.layout / zoom.
            Vector2 viewTL = -_canvas.ViewOffset / _canvas.ViewZoom;
            Vector2 viewSize = new Vector2(_canvas.layout.width, _canvas.layout.height) / _canvas.ViewZoom;
            Vector2 viewMapTL = WorldToMinimap(viewTL, bounds.position, contentOrigin, scale);
            Vector2 viewMapSize = viewSize * scale;

            painter.strokeColor = ViewportColor;
            painter.lineWidth = 1.5f;
            PaintRect(painter, viewMapTL, viewMapSize, fill: false);
        }

        static void PaintRect(Painter2D painter, Vector2 tl, Vector2 size, bool fill)
        {
            painter.BeginPath();
            painter.MoveTo(tl);
            painter.LineTo(new Vector2(tl.x + size.x, tl.y));
            painter.LineTo(new Vector2(tl.x + size.x, tl.y + size.y));
            painter.LineTo(new Vector2(tl.x, tl.y + size.y));
            painter.ClosePath();
            if (fill) painter.Fill();
            else painter.Stroke();
        }

        // ---------------------------------------------------------------
        // Projection
        // ---------------------------------------------------------------

        /// <summary>
        /// Rectangle in world space that the minimap should display — the
        /// bounding box of all nodes plus the current viewport, expanded
        /// slightly for breathing room.
        /// </summary>
        Rect ComputeBounds()
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;

            if (_canvas.Asset != null)
            {
                foreach (var node in _canvas.Asset.Nodes)
                {
                    if (node == null) continue;
                    Vector2 tl = node.Position;
                    Vector2 br = tl + new Vector2(NodeApproxWidth, NodeApproxHeight);
                    min = Vector2.Min(min, tl);
                    max = Vector2.Max(max, br);
                    any = true;
                }
            }

            // Always include the current viewport so the user can see where
            // they are even when the graph is empty.
            Vector2 viewTL = -_canvas.ViewOffset / _canvas.ViewZoom;
            Vector2 viewBR = viewTL + new Vector2(_canvas.layout.width, _canvas.layout.height) / _canvas.ViewZoom;
            min = any ? Vector2.Min(min, viewTL) : viewTL;
            max = any ? Vector2.Max(max, viewBR) : viewBR;

            min -= Vector2.one * BoundsExpand;
            max += Vector2.one * BoundsExpand;
            return new Rect(min, max - min);
        }

        void ComputeProjection(Rect bounds, out Vector2 contentOrigin, out float scale)
        {
            Rect inner = new Rect(
                InnerPadding, InnerPadding,
                layout.width - 2f * InnerPadding,
                layout.height - 2f * InnerPadding);

            float sx = inner.width / bounds.width;
            float sy = inner.height / bounds.height;
            scale = Mathf.Min(sx, sy);

            Vector2 contentSize = bounds.size * scale;
            contentOrigin = inner.position + (inner.size - contentSize) * 0.5f;
        }

        static Vector2 WorldToMinimap(Vector2 world, Vector2 boundsOrigin, Vector2 contentOrigin, float scale)
        {
            return contentOrigin + (world - boundsOrigin) * scale;
        }

        Vector2 MinimapToWorld(Vector2 localPos)
        {
            Rect bounds = ComputeBounds();
            if (bounds.width <= 0f || bounds.height <= 0f) return Vector2.zero;

            ComputeProjection(bounds, out Vector2 contentOrigin, out float scale);
            if (scale <= 0f) return Vector2.zero;

            return bounds.position + (localPos - contentOrigin) / scale;
        }

        // ---------------------------------------------------------------
        // Pointer — click or drag inside the minimap pans the canvas
        // ---------------------------------------------------------------

        bool _panning;
        int _pointerId = -1;

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse) return;
            _panning = true;
            _pointerId = evt.pointerId;
            this.CapturePointer(evt.pointerId);
            ApplyPan((Vector2)evt.localPosition);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_panning || evt.pointerId != _pointerId) return;
            ApplyPan((Vector2)evt.localPosition);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_panning || evt.pointerId != _pointerId) return;
            EndPan();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_panning && evt.pointerId == _pointerId) EndPan();
        }

        void EndPan()
        {
            if (this.HasPointerCapture(_pointerId))
                this.ReleasePointer(_pointerId);
            _panning = false;
            _pointerId = -1;
        }

        void ApplyPan(Vector2 localPos)
        {
            Vector2 worldPos = MinimapToWorld(localPos);
            _canvas.CenterOn(worldPos);
        }
    }
}
