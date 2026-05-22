using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Inspector for any <see cref="GraphAssetSO"/> subclass. Shows a
    /// prominent "Open in Graph Editor" button followed by Unity's default
    /// field view; the actual graph canvas lives in
    /// <see cref="GraphEditorWindow"/>, not in the Inspector pane.
    /// </summary>
    [CustomEditor(typeof(GraphAssetSO), editorForChildClasses: true)]
    public class GraphAssetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var openButton = new Button(() =>
            {
                if (target is GraphAssetSO asset)
                    GraphEditorWindow.Open(asset);
            })
            {
                text = "Open in Graph Editor",
            };
            openButton.style.height = 28f;
            openButton.style.marginTop = 4f;
            openButton.style.marginBottom = 6f;
            openButton.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            root.Add(openButton);

            // Default Unity field rendering below the button so users can
            // still inspect / edit any serialized field on their subclass.
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            return root;
        }
    }
}
