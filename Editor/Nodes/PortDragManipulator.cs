using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Drag from a port to wire a connection — symmetric in both directions:
    /// from an OUTPUT port the drag seeks an input, from an (unconnected)
    /// INPUT port it seeks an output. Either way a real
    /// <see cref="GraphConnection"/> is materialised (always output→input) only
    /// if a valid opposite-kind drop target is found. The visible feedback is
    /// an <see cref="EdgePreview"/> spawned on PointerDown and removed on
    /// PointerUp.
    ///
    /// A CONNECTED input port instead drag-to-detaches: grabbing it pulls the
    /// existing wire off and continues the drag from its source output port,
    /// so it can be re-dropped on another input (reroute) or released in empty
    /// space (remove).
    /// </summary>
    public class PortDragManipulator : Manipulator
    {
        /// <summary>The port this manipulator is attached to (the drag origin element).</summary>
        PortElement _port;

        /// <summary>
        /// The OUTPUT port the live connection is being drawn from. Equal to
        /// <see cref="_port"/> for a normal output-port drag; for a
        /// detach-drag off a connected input it is the detached wire's source
        /// output port. All preview / candidate / drop logic uses this.
        /// </summary>
        PortElement _dragSource;

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
            if (_dragging || _port.OwnerNode == null) return;

            var canvas = _port.OwnerNode.Canvas;
            if (canvas == null) return;

            PortElement dragSource;
            if (_port.IsInput)
            {
                // Input port drag:
                //  • connected  → drag-to-detach: pull the existing wire off and
                //    continue from its source OUTPUT port (reroute, or release in
                //    empty space to remove).
                //  • unconnected → start a NEW connection anchored at THIS input,
                //    seeking an output — the reverse-direction drag the canvas
                //    already supports. Makes the left-side point a drag handle
                //    too, symmetric with output ports, instead of a port whose
                //    click falls through to select the node.
                dragSource = canvas.DetachConnectionAt(_port) ?? _port;
            }
            else
            {
                dragSource = _port;
            }

            _dragSource = dragSource;
            _dragging = true;
            _pointerId = evt.pointerId;
            target.CapturePointer(evt.pointerId);

            _preview = canvas.BeginConnectionPreview(_dragSource);
            UpdatePreviewEnd(evt.position);

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId) return;
            UpdatePreviewEnd(evt.position);
            evt.StopPropagation();
        }

        // Drop-affordance + magnetic-snap update is folded into
        // UpdatePreviewEnd so the preview end and the candidate are always
        // computed from the same pointer sample.

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId) return;

            var canvas = _port.OwnerNode.Canvas;
            canvas?.CompleteConnectionPreview(_dragSource, evt.position);

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

            // Drop-target affordance + magnetic snap. The canvas lights the
            // nearest compatible OPPOSITE-kind port (green) — or a hovered
            // incompatible one (red) — and, when snapped, returns that port's
            // center so the preview edge clicks onto the target instead of
            // trailing the raw cursor. Uses the drag's ANCHOR port (the output
            // for an output/detach drag, the input for a new input-anchored
            // drag) so the candidate gates match the drop.
            worldPos = canvas.UpdateDropCandidate(_dragSource, panelPos, worldPos);

            _preview.SetEndWorld(worldPos);
        }

        void EndDrag()
        {
            // Single clear site for drop / cancel / capture-loss — drops the
            // drop-target affordance no matter how the drag terminated.
            _port.OwnerNode.Canvas?.ClearDropCandidate();

            if (target.HasPointerCapture(_pointerId))
                target.ReleasePointer(_pointerId);
            _dragging = false;
            _pointerId = -1;
            _preview = null;
            _dragSource = null;
        }
    }
}
