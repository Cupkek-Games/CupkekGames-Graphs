using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Visual variant of <see cref="NodeElement"/> for <see cref="StickyNoteNodeSO"/>.
    /// Hides the inherited header / subtitle / port-row and renders a
    /// coloured card with an inline multi-line <see cref="TextField"/> bound
    /// to the note's <see cref="StickyNoteNodeSO.Text"/> via
    /// <see cref="BindingExtensions.BindProperty(IBindable, SerializedProperty)"/>
    /// — every keystroke persists to the asset and rides Unity's built-in
    /// undo.
    /// </summary>
    public class StickyNoteElement : NodeElement
    {
        const float MinNoteWidth = 220f;
        const float MinNoteHeight = 110f;
        const float DragHandleHeight = 12f;

        static readonly Color DragHandleColor = GraphTheme.StickyDragHandle;

        readonly StickyNoteNodeSO _sticky;
        readonly TextField _textField;

        public StickyNoteElement(GraphCanvas canvas, StickyNoteNodeSO sticky)
            : base(canvas, sticky)
        {
            _sticky = sticky;

            // Stash the inherited chrome — sticky notes have their own shape.
            _header.style.display = DisplayStyle.None;
            _subtitleLabel.style.display = DisplayStyle.None;
            _portRow.style.display = DisplayStyle.None;

            AddToClassList("cgg-graph-sticky-note");
            style.minWidth = MinNoteWidth;
            style.minHeight = MinNoteHeight;
            style.backgroundColor = sticky.BackgroundColor;

            // Small drag-handle strip at the top. Picking off here so the
            // base NodeDragManipulator (attached to the NodeElement root)
            // handles the drag — but it gives the user a visible grab area
            // separate from the editable text body.
            var dragHandle = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    height = DragHandleHeight,
                    backgroundColor = DragHandleColor,
                    borderTopLeftRadius = 4f,
                    borderTopRightRadius = 4f,
                },
            };
            Add(dragHandle);

            _textField = new TextField
            {
                multiline = true,
            };
            _textField.style.flexGrow = 1f;
            _textField.style.minHeight = MinNoteHeight - DragHandleHeight - 12f;
            _textField.style.marginTop = 4f;
            _textField.style.marginLeft = 6f;
            _textField.style.marginRight = 6f;
            _textField.style.marginBottom = 6f;
            _textField.style.whiteSpace = WhiteSpace.Normal;

            // BindProperty wires the field directly to the SO's serialized
            // Text field — edits persist, undo works, no manual SetDirty.
            var serialized = new SerializedObject(sticky);
            var textProp = serialized.FindProperty(nameof(StickyNoteNodeSO.Text));
            if (textProp != null)
                _textField.BindProperty(textProp);
            else
                _textField.SetValueWithoutNotify(sticky.Text);

            Add(_textField);
        }

        public override void RefreshDisplay()
        {
            // Skip the base header/subtitle update — sticky notes don't show
            // them. The text field is bound to the SO so it self-syncs.
            if (_sticky != null)
                style.backgroundColor = _sticky.BackgroundColor;
        }
    }
}
