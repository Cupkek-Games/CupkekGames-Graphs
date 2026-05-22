using System;
using System.Collections.Generic;
using UnityEditor;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Generic "start node" dropdown for any <see cref="GraphAssetSO"/>
    /// subclass that exposes a settable start guid via a serialized
    /// <c>SerializedGuid</c> field. Lists the graph's nodes (optionally
    /// filtered), labels them via <see cref="GraphNodeSO.DisplayTitle"/>
    /// or a custom label-extractor, and mutates the guid through the
    /// inspector's <see cref="SerializedObject"/> so Undo + SetDirty +
    /// prefab override tracking come for free.
    ///
    /// Use from a <c>CustomEditor</c>'s <c>OnInspectorGUI</c>:
    /// <code>
    /// StartNodePickerGUI.Draw(
    ///     serializedObject,
    ///     (LunaNavigationGraphSO)target,
    ///     "_startDestinationGuid",
    ///     filter: n => n is LunaNavDestinationSO,
    ///     labelOf: n => ((LunaNavDestinationSO)n).DestinationId);
    /// </code>
    /// </summary>
    public static class StartNodePickerGUI
    {
        /// <param name="serializedObject">The inspector's serializedObject — used to mutate the guid field with Undo support.</param>
        /// <param name="graph">The bound graph. Read-only here; the guid mutation goes through <paramref name="serializedObject"/>.</param>
        /// <param name="guidFieldName">Name of the serialised <c>SerializedGuid</c> field on the graph (e.g. <c>_startDestinationGuid</c>).</param>
        /// <param name="label">Label shown next to the dropdown. Defaults to "Start Node".</param>
        /// <param name="filter">Optional — return true to include a node in the dropdown. Default: all nodes included.</param>
        /// <param name="labelOf">Optional — derive the dropdown label for a node. Default: <see cref="GraphNodeSO.DisplayTitle"/>.</param>
        public static void Draw(
            SerializedObject serializedObject,
            GraphAssetSO graph,
            string guidFieldName,
            string label = "Start Node",
            Func<GraphNodeSO, bool> filter = null,
            Func<GraphNodeSO, string> labelOf = null)
        {
            if (serializedObject == null || graph == null || string.IsNullOrEmpty(guidFieldName))
                return;

            var matching = new List<GraphNodeSO>();
            foreach (var node in graph.Nodes)
            {
                if (node == null) continue;
                if (filter != null && !filter(node)) continue;
                matching.Add(node);
            }

            if (matching.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No nodes to pick from. Open the graph editor and add " +
                    "some nodes first.",
                    MessageType.Info);
                return;
            }

            var labels = new string[matching.Count + 1];
            labels[0] = "(none)";
            int currentIdx = 0;
            var currentGuid = graph.StartNodeGuid;
            bool hasCurrent = !string.IsNullOrEmpty(currentGuid.ValueStr);

            for (int i = 0; i < matching.Count; i++)
            {
                string rawLabel = labelOf != null ? labelOf(matching[i]) : matching[i].DisplayTitle;
                labels[i + 1] = string.IsNullOrEmpty(rawLabel) ? "<unnamed>" : rawLabel;
                if (hasCurrent && matching[i].Guid == currentGuid)
                    currentIdx = i + 1;
            }

            int newIdx = EditorGUILayout.Popup(label, currentIdx, labels);
            if (newIdx == currentIdx) return;

            var guidProp = serializedObject.FindProperty(guidFieldName);
            if (guidProp == null)
            {
                UnityEngine.Debug.LogError(
                    $"[StartNodePickerGUI] No serialised field named '{guidFieldName}' on '{graph.GetType().Name}'.",
                    graph);
                return;
            }

            var valueStrProp = guidProp.FindPropertyRelative("ValueStr");
            valueStrProp.stringValue = newIdx == 0
                ? string.Empty
                : matching[newIdx - 1].Guid.ValueStr;
            serializedObject.ApplyModifiedProperties();

            graph.EditorRaiseMutated();
        }
    }
}
