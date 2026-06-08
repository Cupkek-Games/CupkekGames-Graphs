using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Builds the grouped serialized-field editor for a <see cref="GraphNodeSO"/>:
    /// one <see cref="PropertyField"/> per visible field, partitioned into the
    /// ordered, collapsible <c>[NodeGroup]</c> sections decided by
    /// <see cref="NodeFieldLayout"/>. Shared by the on-card body
    /// (<c>NodeElement.BuildBody</c>) and the docked
    /// <see cref="GraphInspectorPanel"/> so both render fields identically.
    /// </summary>
    internal static class NodeInspectorBody
    {
        /// <summary>
        /// Render <paramref name="node"/>'s serialized fields into
        /// <paramref name="container"/>. Edits route through
        /// <c>SerializedProperty</c> (Undo + dirty handled by Unity);
        /// <paramref name="onChanged"/> fires after any edit so the caller can
        /// refresh derived chrome (the card header / panel title) and flip the
        /// window's dirty flag. The script reference (<c>m_Script</c>) is skipped.
        /// No <c>[NodeGroup]</c> anywhere → a single flat field list (back-compat).
        /// </summary>
        public static void Build(GraphNodeSO node, VisualElement container, Action onChanged)
        {
            if (node == null || container == null) return;
            var so = new SerializedObject(node);

            // Render into a FRESH root so the SerializedObject value-tracker is
            // scoped to this build. A reused container (the inspector panel's
            // scroll, cleared + rebuilt per selection) would otherwise hit
            // "an element can track only one serializedObject at a time": Clear()
            // removes the children but not the container's own tracker. The root
            // carries the tracker and is dropped when the container is cleared.
            var root = new VisualElement();
            container.Add(root);

            // Walk the visible serialized fields in Unity's canonical order
            // (= serialization / declaration order). For each, read the optional
            // [NodeGroup] / [NodeFieldOrder] off its FieldInfo and keep the live
            // SerializedProperty by name; the pure NodeFieldLayout resolver
            // (Editor-free, unit-tested) decides section + field order.
            var fields = new List<NodeFieldLayout.Field>();
            var byName = new Dictionary<string, SerializedProperty>();
            var iter = so.GetIterator();
            iter.NextVisible(enterChildren: true); // skip m_Script
            int seen = 0;
            while (iter.NextVisible(enterChildren: false))
            {
                string group = null;
                int groupOrder = 0;
                int fieldOrder = seen;
                var fi = FindFieldInfo(node.GetType(), iter.name);
                if (fi != null)
                {
                    var g = fi.GetCustomAttribute<NodeGroupAttribute>();
                    if (g != null) { group = g.Title; groupOrder = g.Order; }
                    var o = fi.GetCustomAttribute<NodeFieldOrderAttribute>();
                    if (o != null) fieldOrder = o.Order;
                }
                fields.Add(new NodeFieldLayout.Field(iter.name, group, groupOrder, fieldOrder, seen));
                byName[iter.name] = iter.Copy();
                seen++;
            }

            foreach (var section in NodeFieldLayout.Resolve(fields))
            {
                // Untitled section (ungrouped fields) renders flat; a titled
                // section renders as a collapsible Foldout.
                VisualElement target = root;
                if (section.Title != null)
                {
                    var foldout = new Foldout { text = section.Title, value = true };
                    foldout.AddToClassList("cgg-graph-node__field-group");
                    foldout.style.marginTop = 2f;
                    var labelEl = foldout.Q<Label>(); // header label (queried before fields)
                    if (labelEl != null) labelEl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    root.Add(foldout);
                    target = foldout;
                }

                foreach (var name in section.FieldNames)
                {
                    if (!byName.TryGetValue(name, out var prop)) continue;
                    var field = new PropertyField(prop);
                    field.Bind(so);
                    target.Add(field);
                }
            }

            root.TrackSerializedObjectValue(so, _ => onChanged?.Invoke());
        }

        // Resolve the FieldInfo for a serialized property name, walking the node
        // type's inheritance chain (private [SerializeField] fields aren't found
        // by a single GetField without DeclaredOnly per level).
        static FieldInfo FindFieldInfo(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null && t != typeof(UnityEngine.Object); t = t.BaseType)
            {
                var fi = t.GetField(name, flags);
                if (fi != null) return fi;
            }
            return null;
        }
    }
}
