using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Coloured rounded rectangle drawn behind a cluster of nodes for visual
    /// organisation. Drag the title bar to move; drag the bottom-right
    /// handle to resize; click the title to rename; "×" deletes. Groups live
    /// in the canvas's group layer so they paint behind edges and nodes.
    /// </summary>
    public class GroupElement : VisualElement
    {
        const float TitleBarHeight = 22f;
        const float ResizeHandleSize = 14f;
        const float CornerRadius = 6f;
        const float MinWidth = 160f;
        const float MinHeight = 100f;

        static readonly Color BorderColor         = GraphTheme.GroupBorder;
        static readonly Color SelectedBorderColor = GraphTheme.SelectionAccentStrong;
        static readonly Color TitleColor          = GraphTheme.GroupTitleBg;
        static readonly Color ResizeHandleColor   = GraphTheme.GroupResizeHandle;

        readonly GraphCanvas _canvas;
        readonly GraphGroup _group;

        readonly VisualElement _titleBar;
        readonly Label _titleLabel;
        readonly TextField _titleField;
        readonly VisualElement _resizeHandle;

        bool _dragging;
        bool _resizing;
        int _pointerId = -1;

        // Nodes captured at drag-start that move with the box (spatial members).
        List<NodeElement> _dragMembers;

        public GraphGroup Group => _group;
        public bool IsSelected { get; private set; }

        internal void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            UpdateBorderColors();
        }

        void UpdateBorderColors()
        {
            var c = IsSelected ? SelectedBorderColor : BorderColor;
            style.borderTopColor = c;
            style.borderBottomColor = c;
            style.borderLeftColor = c;
            style.borderRightColor = c;
            float w = IsSelected ? 2f : 1f;
            style.borderTopWidth = w;
            style.borderBottomWidth = w;
            style.borderLeftWidth = w;
            style.borderRightWidth = w;
        }

        public GroupElement(GraphCanvas canvas, GraphGroup group)
        {
            _canvas = canvas;
            _group = group;

            AddToClassList("cgg-graph-group");
            style.position = Position.Absolute;
            style.backgroundColor = group.Color;
            style.borderTopLeftRadius = CornerRadius;
            style.borderTopRightRadius = CornerRadius;
            style.borderBottomLeftRadius = CornerRadius;
            style.borderBottomRightRadius = CornerRadius;
            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            style.borderTopColor = BorderColor;
            style.borderBottomColor = BorderColor;
            style.borderLeftColor = BorderColor;
            style.borderRightColor = BorderColor;

            // -- Title bar --
            _titleBar = new VisualElement
            {
                style =
                {
                    height = TitleBarHeight,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 8f,
                    paddingRight = 4f,
                    backgroundColor = TitleColor,
                    borderTopLeftRadius = CornerRadius,
                    borderTopRightRadius = CornerRadius,
                },
            };
            Add(_titleBar);

            _titleLabel = new Label(group.Title)
            {
                style =
                {
                    color = Color.white,
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow = 1f,
                },
            };
            _titleBar.Add(_titleLabel);

            _titleField = new TextField { value = group.Title };
            _titleField.style.display = DisplayStyle.None;
            _titleField.style.flexGrow = 1f;
            _titleField.RegisterCallback<FocusOutEvent>(_ => CommitTitleEdit());
            _titleField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    CommitTitleEdit();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    CancelTitleEdit();
                    evt.StopPropagation();
                }
            });
            _titleBar.Add(_titleField);

            var closeButton = new Button(DeleteGroup) { text = "×" };
            closeButton.style.width = 18f;
            closeButton.style.height = 18f;
            closeButton.style.paddingLeft = 0f;
            closeButton.style.paddingRight = 0f;
            closeButton.style.paddingTop = 0f;
            closeButton.style.paddingBottom = 0f;
            closeButton.style.marginLeft = 4f;
            closeButton.style.marginRight = 0f;
            _titleBar.Add(closeButton);

            // Drag on title bar — pointer events on _titleBar move the whole group.
            _titleBar.RegisterCallback<PointerDownEvent>(OnDragPointerDown);
            _titleBar.RegisterCallback<PointerMoveEvent>(OnDragPointerMove);
            _titleBar.RegisterCallback<PointerUpEvent>(OnDragPointerUp);
            _titleBar.RegisterCallback<PointerCaptureOutEvent>(OnDragPointerCaptureOut);

            // Idle-hover feedback — brighten the title bar unless a drag/resize is live.
            _titleBar.RegisterCallback<PointerEnterEvent>(OnTitleBarPointerEnter);
            _titleBar.RegisterCallback<PointerLeaveEvent>(OnTitleBarPointerLeave);

            // -- Resize handle (bottom-right corner) --
            _resizeHandle = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    right = 2f,
                    bottom = 2f,
                    width = ResizeHandleSize,
                    height = ResizeHandleSize,
                    backgroundColor = ResizeHandleColor,
                    borderBottomRightRadius = CornerRadius - 2f,
                },
            };
            _resizeHandle.RegisterCallback<PointerDownEvent>(OnResizePointerDown);
            _resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizePointerMove);
            _resizeHandle.RegisterCallback<PointerUpEvent>(OnResizePointerUp);
            _resizeHandle.RegisterCallback<PointerCaptureOutEvent>(OnResizePointerCaptureOut);

            // Idle-hover feedback — brighten + grow the grip unless a resize is live.
            _resizeHandle.RegisterCallback<PointerEnterEvent>(OnResizeHandlePointerEnter);
            _resizeHandle.RegisterCallback<PointerLeaveEvent>(OnResizeHandlePointerLeave);
            Add(_resizeHandle);

            ApplyBounds();
        }

        // ---------------------------------------------------------------
        // Bounds
        // ---------------------------------------------------------------

        public void ApplyBounds()
        {
            if (_group == null) return;
            style.left = _group.Bounds.x;
            style.top = _group.Bounds.y;
            style.width = _group.Bounds.width;
            style.height = _group.Bounds.height;
        }

        // ---------------------------------------------------------------
        // Title editing
        // ---------------------------------------------------------------

        void StartTitleEdit()
        {
            _titleField.SetValueWithoutNotify(_group.Title);
            _titleField.style.display = DisplayStyle.Flex;
            _titleLabel.style.display = DisplayStyle.None;
            _titleField.Focus();
            _titleField.SelectAll(); // so the default "Group" can be typed straight over
        }

        /// <summary>
        /// Enter title-edit mode programmatically — used to drop a freshly created
        /// group straight into "name it" so the user can type the title immediately.
        /// </summary>
        public void BeginRename() => StartTitleEdit();

        void CommitTitleEdit()
        {
            if (_canvas.Asset != null)
                Undo.RegisterCompleteObjectUndo(_canvas.Asset, "Rename Group");

            _group.Title = _titleField.value;
            _titleLabel.text = _group.Title;
            _titleField.style.display = DisplayStyle.None;
            _titleLabel.style.display = DisplayStyle.Flex;

            if (_canvas.Asset != null) EditorUtility.SetDirty(_canvas.Asset);
        }

        void CancelTitleEdit()
        {
            _titleField.style.display = DisplayStyle.None;
            _titleLabel.style.display = DisplayStyle.Flex;
        }

        // ---------------------------------------------------------------
        // Drag (move whole group)
        // ---------------------------------------------------------------

        void OnDragPointerDown(PointerDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse || evt.altKey) return;
            if (_dragging || _resizing) return;
            if (_canvas.ReadOnly) return; // debugger canvas: frames are display-only

            // Double-click the title bar → rename. Detected by timing (see field note)
            // and handled BEFORE arming the drag, so the drag's pointer-capture can't
            // steal focus from the title field.
            // Double-click the title bar → rename. DEFERRED a frame (schedule.Execute):
            // running it inline would let this click's pointer-up immediately blur the
            // field (FocusOut → CommitTitleEdit), hiding it before you could type — the
            // same reason the auto-rename-on-create defers too.
            if (evt.clickCount == 2)
            {
                evt.StopPropagation();
                schedule.Execute(StartTitleEdit);
                return;
            }

            // Selection update first — same modifier rules as nodes.
            if (evt.shiftKey) _canvas.Selection.Add(this);
            else if (evt.ctrlKey || evt.commandKey) _canvas.Selection.Toggle(this);
            else if (!_canvas.Selection.Contains(this)) _canvas.Selection.SetTo(this);
            _canvas.Focus();

            _dragging = true;
            _pointerId = evt.pointerId;
            _titleBar.CapturePointer(evt.pointerId);

            // Snapshot the nodes inside the box now so they move WITH the group —
            // membership is spatial, so "what's in the box comes along".
            _dragMembers = new List<NodeElement>(_canvas.NodesInGroup(_group));

            if (_canvas.Asset != null)
                Undo.RegisterCompleteObjectUndo(_canvas.Asset, "Move Group");

            evt.StopPropagation();
        }

        void OnDragPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId) return;

            float zoom = _canvas.ViewZoom;
            Vector2 worldDelta = (Vector2)evt.deltaPosition / zoom;
            var b = _group.Bounds;
            b.position += worldDelta;
            _group.Bounds = b;
            ApplyBounds();

            // Carry the captured members along by the same delta.
            if (_dragMembers != null)
            {
                foreach (var ne in _dragMembers)
                {
                    if (ne?.Node == null) continue;
                    ne.SetWorldPosition(ne.Node.Position + worldDelta);
                    _canvas.RefreshEdgesForNode(ne.Node);
                }
            }

            evt.StopPropagation();
        }

        void OnDragPointerUp(PointerUpEvent evt)
        {
            if (!_dragging || evt.pointerId != _pointerId) return;
            EndDrag();
            evt.StopPropagation();
        }

        void OnDragPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragging && evt.pointerId == _pointerId) EndDrag();
        }

        void EndDrag()
        {
            if (_titleBar.HasPointerCapture(_pointerId))
                _titleBar.ReleasePointer(_pointerId);
            _dragging = false;
            _pointerId = -1;
            _dragMembers = null;
            if (_canvas.Asset != null) EditorUtility.SetDirty(_canvas.Asset);
            // Members' positions changed (sidecar) + the box moved — flip the window
            // dirty flag so the next save persists both.
            _canvas.NotifyGraphChanged();
        }

        void OnTitleBarPointerEnter(PointerEnterEvent evt)
        {
            // Yield to an active drag/resize so hover doesn't fight it.
            if (_dragging || _resizing) return;
            _titleBar.style.backgroundColor = GraphTheme.GroupTitleBgHover;
        }

        void OnTitleBarPointerLeave(PointerLeaveEvent evt)
        {
            _titleBar.style.backgroundColor = TitleColor;
        }

        // ---------------------------------------------------------------
        // Resize (drag bottom-right handle)
        // ---------------------------------------------------------------

        void OnResizePointerDown(PointerDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse || evt.altKey) return;
            if (_dragging || _resizing) return;
            if (_canvas.ReadOnly) return; // debugger canvas: frames are display-only

            _resizing = true;
            _pointerId = evt.pointerId;
            _resizeHandle.CapturePointer(evt.pointerId);

            if (_canvas.Asset != null)
                Undo.RegisterCompleteObjectUndo(_canvas.Asset, "Resize Group");

            evt.StopPropagation();
        }

        void OnResizePointerMove(PointerMoveEvent evt)
        {
            if (!_resizing || evt.pointerId != _pointerId) return;

            float zoom = _canvas.ViewZoom;
            Vector2 worldDelta = (Vector2)evt.deltaPosition / zoom;

            var b = _group.Bounds;
            b.width = Mathf.Max(MinWidth, b.width + worldDelta.x);
            b.height = Mathf.Max(MinHeight, b.height + worldDelta.y);
            _group.Bounds = b;
            ApplyBounds();

            evt.StopPropagation();
        }

        void OnResizePointerUp(PointerUpEvent evt)
        {
            if (!_resizing || evt.pointerId != _pointerId) return;
            EndResize();
            evt.StopPropagation();
        }

        void OnResizePointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_resizing && evt.pointerId == _pointerId) EndResize();
        }

        void EndResize()
        {
            if (_resizeHandle.HasPointerCapture(_pointerId))
                _resizeHandle.ReleasePointer(_pointerId);
            _resizing = false;
            _pointerId = -1;
            if (_canvas.Asset != null) EditorUtility.SetDirty(_canvas.Asset);
        }

        void OnResizeHandlePointerEnter(PointerEnterEvent evt)
        {
            // Don't fight an in-progress resize.
            if (_resizing) return;
            _resizeHandle.style.backgroundColor = GraphTheme.GroupResizeHandleHover;
            _resizeHandle.style.scale = new Scale(Vector3.one * 1.15f);
        }

        void OnResizeHandlePointerLeave(PointerLeaveEvent evt)
        {
            _resizeHandle.style.backgroundColor = ResizeHandleColor;
            _resizeHandle.style.scale = new Scale(Vector3.one);
        }

        // ---------------------------------------------------------------
        // Delete
        // ---------------------------------------------------------------

        void DeleteGroup()
        {
            _canvas.DeleteGroup(_group);
        }
    }
}
