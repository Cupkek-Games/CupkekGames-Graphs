using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Drag a <see cref="NodeElement"/> with the left mouse button. Also
    /// owns the click-to-select semantics on the node — PointerDown updates
    /// the canvas selection (plain click replaces; shift adds; ctrl toggles),
    /// then drags every currently-selected node together. The whole drag is
    /// one Undo group.
    ///
    /// Alt + left is reserved for canvas pan and explicitly ignored here so
    /// it bubbles up.
    /// </summary>
    public class NodeDragManipulator : Manipulator
    {
        NodeElement _node;
        bool _dragging;
        int _pointerId = -1;
        readonly List<NodeElement> _dragNodes = new List<NodeElement>();
        readonly List<Vector2> _dragStartPositions = new List<Vector2>();
        Vector2 _accumulatedWorldDelta;

        protected override void RegisterCallbacksOnTarget()
        {
            _node = target as NodeElement
                ?? throw new InvalidOperationException(
                    $"{nameof(NodeDragManipulator)} must be attached to a {nameof(NodeElement)}.");

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
            // Only plain left-click starts a drag. Alt+left is canvas pan.
            if (evt.button != (int)MouseButton.LeftMouse || evt.altKey)
                return;
            if (_dragging || _node.Node == null)
                return;

            var canvas = _node.Canvas;
            if (canvas == null) return;

            // ---- Selection update ----
            if (evt.shiftKey)
            {
                canvas.Selection.Add(_node);
            }
            else if (evt.ctrlKey || evt.commandKey)
            {
                canvas.Selection.Toggle(_node);
            }
            else if (!canvas.Selection.Contains(_node))
            {
                canvas.Selection.SetTo(_node);
            }

            canvas.Focus(); // ensure keyboard delete works after the drag

            // Don't drag if the clicked node ended up unselected
            // (only happens with ctrl+click on an already-selected node).
            if (!canvas.Selection.Contains(_node))
                return;

            // ---- Begin drag ----
            _dragging = true;
            _pointerId = evt.pointerId;
            target.CapturePointer(evt.pointerId);

            _dragNodes.Clear();
            _dragStartPositions.Clear();
            _accumulatedWorldDelta = Vector2.zero;

            foreach (var ne in canvas.Selection.Nodes)
            {
                if (ne.Node == null) continue;
                _dragNodes.Add(ne);
                _dragStartPositions.Add(ne.Node.Position);
            }

            var sos = new GraphNodeSO[_dragNodes.Count];
            for (int i = 0; i < sos.Length; i++) sos[i] = _dragNodes[i].Node;
            Undo.RegisterCompleteObjectUndo(sos, _dragNodes.Count > 1 ? "Move Nodes" : "Move Node");

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId)
                return;

            var canvas = _node.Canvas;
            if (canvas == null) return;

            // deltaPosition is in panel-space pixels — divide by zoom to get
            // the equivalent movement in canvas world space. Accumulate from
            // the drag-start positions so snap-to-grid doesn't drift: each
            // frame derives the new position from (startPos + totalDelta),
            // then optionally rounds to a grid cell.
            _accumulatedWorldDelta += (Vector2)evt.deltaPosition / canvas.ViewZoom;

            bool snap = canvas.SnapToGrid;
            for (int i = 0; i < _dragNodes.Count; i++)
            {
                var elem = _dragNodes[i];
                if (elem.Node == null) continue;

                Vector2 newPos = _dragStartPositions[i] + _accumulatedWorldDelta;
                if (snap) newPos = SnapToGridSize(newPos, GridBackground.MinorSpacing);

                elem.SetWorldPosition(newPos);
                canvas.RefreshEdgesForNode(elem.Node);
            }

            evt.StopPropagation();
        }

        static Vector2 SnapToGridSize(Vector2 pos, float spacing)
        {
            return new Vector2(
                Mathf.Round(pos.x / spacing) * spacing,
                Mathf.Round(pos.y / spacing) * spacing);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId)
                return;

            EndDrag();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragging && evt.pointerId == _pointerId)
                EndDrag();
        }

        void EndDrag()
        {
            if (target.HasPointerCapture(_pointerId))
                target.ReleasePointer(_pointerId);

            bool moved = _dragNodes.Count > 0 && _accumulatedWorldDelta.sqrMagnitude > 0.001f;

            foreach (var elem in _dragNodes)
                if (elem.Node != null)
                    EditorUtility.SetDirty(elem.Node);

            // Mark the bound asset dirty too (the window listens to
            // GraphChanged to flip its "*"). Only when the drag actually
            // moved something — a plain click is a selection, not an edit.
            if (moved && _node.Canvas != null)
                _node.Canvas.NotifyGraphChanged();

            _dragNodes.Clear();
            _dragging = false;
            _pointerId = -1;
        }
    }
}
