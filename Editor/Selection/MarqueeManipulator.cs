using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Drag-from-empty-canvas selection rectangle. Plain left-click clears
    /// the current selection at the start of the drag (or on a non-drag
    /// click); shift-left preserves the existing selection and adds to it.
    /// Both filter to <c>evt.target == canvas</c> so a click that bubbled
    /// up from a node / port doesn't accidentally start a marquee.
    /// </summary>
    public class MarqueeManipulator : Manipulator
    {
        static readonly Color FillColor   = GraphTheme.SelectionAccentSoft;
        static readonly Color BorderColor = GraphTheme.SelectionAccentMid;

        GraphCanvas _canvas;
        bool _active;
        int _pointerId = -1;
        bool _shiftHeld;
        Vector2 _startCanvasLocal;
        VisualElement _rect;

        protected override void RegisterCallbacksOnTarget()
        {
            _canvas = target as GraphCanvas
                ?? throw new InvalidOperationException(
                    $"{nameof(MarqueeManipulator)} must be attached to a {nameof(GraphCanvas)}.");

            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            // Only direct hits on the canvas; bubbled events from nodes /
            // ports are not us.
            if (evt.target != _canvas) return;
            if (evt.button != (int)MouseButton.LeftMouse) return;
            // Alt+left is reserved for pan (handled by PanZoomManipulator).
            if (evt.altKey) return;

            _shiftHeld = evt.shiftKey;
            if (!_shiftHeld)
                _canvas.Selection.Clear();

            _canvas.Focus();

            _active = true;
            _pointerId = evt.pointerId;
            _startCanvasLocal = (Vector2)evt.localPosition;
            target.CapturePointer(evt.pointerId);

            SpawnRect();
            UpdateRect((Vector2)evt.localPosition);

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_active || evt.pointerId != _pointerId) return;
            UpdateRect((Vector2)evt.localPosition);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_active || evt.pointerId != _pointerId) return;
            SelectNodesInMarquee((Vector2)evt.localPosition);
            EndMarquee();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_active && evt.pointerId == _pointerId)
                EndMarquee();
        }

        // ---------------------------------------------------------------
        // Rect lifecycle
        // ---------------------------------------------------------------

        void SpawnRect()
        {
            _rect = new VisualElement();
            _rect.AddToClassList("cgg-graph-marquee");
            _rect.style.position = Position.Absolute;
            _rect.style.backgroundColor = FillColor;
            _rect.style.borderTopWidth = 1f;
            _rect.style.borderBottomWidth = 1f;
            _rect.style.borderLeftWidth = 1f;
            _rect.style.borderRightWidth = 1f;
            _rect.style.borderTopColor = BorderColor;
            _rect.style.borderBottomColor = BorderColor;
            _rect.style.borderLeftColor = BorderColor;
            _rect.style.borderRightColor = BorderColor;
            _rect.pickingMode = PickingMode.Ignore;

            // Attach as direct child of canvas so it lives in screen-space
            // (NOT inside the content layer's transformed coordinate system).
            _canvas.Add(_rect);
        }

        void UpdateRect(Vector2 endCanvasLocal)
        {
            if (_rect == null) return;
            Vector2 min = Vector2.Min(_startCanvasLocal, endCanvasLocal);
            Vector2 max = Vector2.Max(_startCanvasLocal, endCanvasLocal);
            _rect.style.left = min.x;
            _rect.style.top = min.y;
            _rect.style.width = max.x - min.x;
            _rect.style.height = max.y - min.y;
        }

        void EndMarquee()
        {
            if (target.HasPointerCapture(_pointerId))
                target.ReleasePointer(_pointerId);
            if (_rect != null)
            {
                _rect.RemoveFromHierarchy();
                _rect = null;
            }
            _active = false;
            _pointerId = -1;
        }

        // ---------------------------------------------------------------
        // Hit-test
        // ---------------------------------------------------------------

        void SelectNodesInMarquee(Vector2 endCanvasLocal)
        {
            Vector2 min = Vector2.Min(_startCanvasLocal, endCanvasLocal);
            Vector2 max = Vector2.Max(_startCanvasLocal, endCanvasLocal);
            Rect marqueeCanvas = new Rect(min, max - min);

            float zoom = _canvas.ViewZoom;
            Vector2 offset = _canvas.ViewOffset;

            foreach (var ne in _canvas.NodeElements)
            {
                if (ne == null || ne.Node == null) continue;

                // Node world rect → canvas-local rect.
                Vector2 worldTL = ne.Node.Position;
                float w = ne.layout.width  > 0f ? ne.layout.width  : 180f;
                float h = ne.layout.height > 0f ? ne.layout.height : 60f;

                Vector2 canvasTL = worldTL * zoom + offset;
                Rect nodeCanvas = new Rect(canvasTL, new Vector2(w, h) * zoom);

                if (marqueeCanvas.Overlaps(nodeCanvas))
                    _canvas.Selection.Add(ne);
            }
        }
    }
}
