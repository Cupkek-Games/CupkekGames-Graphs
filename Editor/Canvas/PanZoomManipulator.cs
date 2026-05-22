using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Wires pan + zoom input on a <see cref="GraphCanvas"/>.
    ///
    /// Pan: middle-mouse drag, or alt + left-mouse drag.
    /// Zoom: scroll wheel (zooms toward the cursor pivot).
    ///
    /// All other input passes through, so node-level manipulators can claim
    /// left-mouse drag for selection and drag-to-connect.
    /// </summary>
    public class PanZoomManipulator : Manipulator
    {
        const float ZoomStep = 0.05f;

        GraphCanvas _canvas;
        bool _panning;
        int _panPointerId = -1;
        Vector2 _lastPos;

        protected override void RegisterCallbacksOnTarget()
        {
            _canvas = target as GraphCanvas
                ?? throw new InvalidOperationException(
                    $"{nameof(PanZoomManipulator)} must be attached to a {nameof(GraphCanvas)}.");

            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.RegisterCallback<WheelEvent>(OnWheel);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.UnregisterCallback<WheelEvent>(OnWheel);
        }

        bool IsPanTrigger(PointerDownEvent evt)
        {
            // Middle mouse button always pans.
            if (evt.button == (int)MouseButton.MiddleMouse)
                return true;
            // Alt + left mouse also pans (a common alternative for users on
            // trackpads or laptops without a middle button).
            if (evt.button == (int)MouseButton.LeftMouse && evt.altKey)
                return true;
            return false;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (_panning || !IsPanTrigger(evt))
                return;

            _panning = true;
            _panPointerId = evt.pointerId;
            _lastPos = evt.position;
            target.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_panning || evt.pointerId != _panPointerId)
                return;

            Vector2 cur = evt.position;
            Vector2 delta = cur - _lastPos;
            _lastPos = cur;
            _canvas.Pan(delta);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_panning || evt.pointerId != _panPointerId)
                return;

            EndPan();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // Pointer capture can be revoked externally (e.g. window focus
            // loss). Treat that as a clean pan end so we don't get stuck.
            if (_panning && evt.pointerId == _panPointerId)
                EndPan();
        }

        void EndPan()
        {
            if (target.HasPointerCapture(_panPointerId))
                target.ReleasePointer(_panPointerId);
            _panning = false;
            _panPointerId = -1;
        }

        void OnWheel(WheelEvent evt)
        {
            // delta.y > 0 = scrolling down (toward user) = zoom out.
            float delta = -evt.delta.y * ZoomStep;
            if (Mathf.Approximately(delta, 0f))
                return;

            _canvas.Zoom(delta, evt.localMousePosition);
            evt.StopPropagation();
        }
    }
}
