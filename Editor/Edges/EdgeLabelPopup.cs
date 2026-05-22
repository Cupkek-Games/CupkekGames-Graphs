using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Small dropdown that lets the user edit a connection's
    /// <see cref="GraphConnection.Label"/>. Spawned by the canvas's
    /// edge right-click menu. Commits on Enter / focus loss via
    /// SerializedProperty path (Undo + dirty handled by Unity);
    /// cancels on Esc.
    /// </summary>
    public class EdgeLabelPopup : EditorWindow
    {
        const float PopupWidth = 260f;
        const float PopupHeight = 40f;

        GraphAssetSO _asset;
        int _connectionIndex;
        Action _onCommitted;
        TextField _field;

        /// <summary>
        /// Show the popup at <paramref name="screenPos"/> for the given
        /// connection index on <paramref name="asset"/>. The on-committed
        /// callback fires after the edit lands so the caller can refresh
        /// dependent visuals (the edge's label chip).
        /// </summary>
        public static void Show(Vector2 screenPos, GraphAssetSO asset, int connectionIndex, Action onCommitted)
        {
            var popup = CreateInstance<EdgeLabelPopup>();
            popup._asset = asset;
            popup._connectionIndex = connectionIndex;
            popup._onCommitted = onCommitted;

            var rect = new Rect(screenPos.x, screenPos.y, 1f, 1f);
            popup.ShowAsDropDown(rect, new Vector2(PopupWidth, PopupHeight));
        }

        void OnEnable()
        {
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.style.flexDirection = FlexDirection.Row;
            rootVisualElement.style.alignItems = Align.Center;
            rootVisualElement.style.paddingLeft = 6f;
            rootVisualElement.style.paddingRight = 6f;

            _field = new TextField
            {
                isDelayed = false,
                style = { flexGrow = 1f },
            };
            _field.RegisterCallback<KeyDownEvent>(OnKeyDown);
            rootVisualElement.Add(_field);
        }

        void OnFocus()
        {
            if (_field == null || _asset == null) return;

            // Read the current label from the asset and seed the field.
            if (_connectionIndex >= 0 && _connectionIndex < _asset.Connections.Count)
                _field.value = _asset.Connections[_connectionIndex].Label ?? string.Empty;

            _field.Focus();
            // Select the entire current text so typing replaces it.
            _field.SelectAll();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Commit();
                    evt.StopPropagation();
                    break;
                case KeyCode.Escape:
                    Close();
                    evt.StopPropagation();
                    break;
            }
        }

        void Commit()
        {
            if (_asset == null)
            {
                Close();
                return;
            }

            // Mutate via SerializedProperty so Unity wires Undo +
            // dirty-tracking automatically. The working-copy editing
            // flow (see GraphAssetSO.ApplyToOriginal) commits on Save.
            var so = new SerializedObject(_asset);
            var connections = so.FindProperty("_connections");
            if (connections != null && _connectionIndex >= 0 && _connectionIndex < connections.arraySize)
            {
                var element = connections.GetArrayElementAtIndex(_connectionIndex);
                var labelProp = element.FindPropertyRelative("Label");
                if (labelProp != null)
                {
                    labelProp.stringValue = _field.value ?? string.Empty;
                    so.ApplyModifiedProperties();
                    _asset.EditorRaiseMutated();
                    _onCommitted?.Invoke();
                }
            }

            Close();
        }
    }
}
