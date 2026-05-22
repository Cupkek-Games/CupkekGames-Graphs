using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Drag from an output port and release over an input port to create a
    /// new <see cref="GraphConnection"/>. The visible drag feedback is an
    /// <see cref="EdgePreview"/> spawned on PointerDown and removed on
    /// PointerUp; the actual connection is materialised by the canvas only
    /// if the drop target is a valid input port.
    ///
    /// Symmetric drag from input port → output port is deliberately not
    /// supported for v1 — keeps the directional model unambiguous. Add later
    /// if user demand emerges.
    /// </summary>
    public class PortDragManipulator : Manipulator
    {
        PortElement _port;
        bool _dragging;
        int _pointerId = -1;
        EdgePreview _preview;

        protected override void RegisterCallbacksOnTarget()
        {
            _port = target as PortElement
                ?? throw new InvalidOperationException(
                    $"{nameof(PortDragManipulator)} must be on a {nameof(PortElement)}.");

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
            if (evt.button != (int)MouseButton.LeftMouse || evt.altKey) return;
            if (_port.IsInput) return; // input ports are drop targets only
            if (_dragging || _port.OwnerNode == null) return;

            var canvas = _port.OwnerNode.Canvas;
            if (canvas == null) return;

            _dragging = true;
            _pointerId = evt.pointerId;
            target.CapturePointer(evt.pointerId);

            _preview = canvas.BeginConnectionPreview(_port);
            UpdatePreviewEnd(evt.position);

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId) return;
            UpdatePreviewEnd(evt.position);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId) return;

            var canvas = _port.OwnerNode.Canvas;
            canvas?.CompleteConnectionPreview(_port, evt.position);

            EndDrag();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragging && evt.pointerId == _pointerId)
            {
                _port.OwnerNode.Canvas?.CancelConnectionPreview();
                EndDrag();
            }
        }

        void UpdatePreviewEnd(Vector2 panelPos)
        {
            if (_preview == null) return;
            var canvas = _port.OwnerNode.Canvas;
            if (canvas == null) return;

            Vector2 worldPos = canvas.PanelToCanvasWorld(panelPos);
            _preview.SetEndWorld(worldPos);
        }

        void EndDrag()
        {
            if (target.HasPointerCapture(_pointerId))
                target.ReleasePointer(_pointerId);
            _dragging = false;
            _pointerId = -1;
            _preview = null;
        }
    }
}
