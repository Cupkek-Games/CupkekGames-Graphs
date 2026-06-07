using System;
using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Pure ordering of a node's serialized fields into <c>[NodeGroup]</c>
    /// sections. The Editor's <c>NodeElement.BuildBody</c> reads each field's
    /// group / order metadata (off the <c>SerializedObject</c> + reflection) and
    /// feeds it here; the resolver decides the section + field order and returns
    /// it, then the Editor renders the result.
    ///
    /// <para>
    /// Deliberately Editor-free (no <c>SerializedProperty</c> / UI Toolkit
    /// types) so the ordering rule — the one thing with real logic — is
    /// unit-testable in isolation.
    /// </para>
    /// </summary>
    public static class NodeFieldLayout
    {
        /// <summary>One serialized field's grouping metadata, in declaration order.</summary>
        public readonly struct Field
        {
            /// <summary>Serialized property / field name (the lookup key the Editor renders by).</summary>
            public readonly string Name;
            /// <summary>Section title; null or empty means ungrouped (the untitled leading block).</summary>
            public readonly string Group;
            /// <summary>Cross-section sort key (from <c>[NodeGroup].Order</c>).</summary>
            public readonly int GroupOrder;
            /// <summary>Within-section sort key (from <c>[NodeFieldOrder]</c>, else declaration index).</summary>
            public readonly int FieldOrder;
            /// <summary>Declaration index — the stable tie-breaker for both sorts.</summary>
            public readonly int SeenIndex;

            public Field(string name, string group, int groupOrder, int fieldOrder, int seenIndex)
            {
                Name = name;
                Group = group;
                GroupOrder = groupOrder;
                FieldOrder = fieldOrder;
                SeenIndex = seenIndex;
            }
        }

        /// <summary>An ordered body section: a title (null = ungrouped/flat) + its ordered field names.</summary>
        public sealed class Section
        {
            /// <summary>Section header, or null for the untitled ungrouped block rendered flat.</summary>
            public readonly string Title;
            /// <summary>Field names in render order.</summary>
            public readonly List<string> FieldNames;

            public Section(string title, List<string> fieldNames)
            {
                Title = title;
                FieldNames = fieldNames;
            }
        }

        /// <summary>
        /// Resolve <paramref name="fields"/> (in declaration order) into ordered
        /// sections: the ungrouped fields first as a single untitled section (in
        /// declaration order, or <c>[NodeFieldOrder]</c> when set), then one
        /// titled section per distinct group sorted by (<c>GroupOrder</c>,
        /// first-seen), each group's fields sorted by (<c>FieldOrder</c>,
        /// <c>SeenIndex</c>). With no groups at all the result is a single
        /// untitled section holding every field — i.e. the plain flat list.
        /// The ordering is fully deterministic (SeenIndex breaks every tie), so
        /// it does not depend on sort stability.
        /// </summary>
        public static List<Section> Resolve(IReadOnlyList<Field> fields)
        {
            var sections = new List<Section>();
            if (fields == null || fields.Count == 0) return sections;

            Comparison<Field> byField = (a, b) =>
            {
                int c = a.FieldOrder.CompareTo(b.FieldOrder);
                return c != 0 ? c : a.SeenIndex.CompareTo(b.SeenIndex);
            };

            var ungrouped = new List<Field>();
            var titles = new List<string>();                       // first-seen order
            var buckets = new Dictionary<string, List<Field>>();
            var groupOrder = new Dictionary<string, int>();
            var groupFirstSeen = new Dictionary<string, int>();

            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f.Group)) { ungrouped.Add(f); continue; }
                if (!buckets.TryGetValue(f.Group, out var list))
                {
                    list = new List<Field>();
                    buckets[f.Group] = list;
                    titles.Add(f.Group);
                    groupOrder[f.Group] = f.GroupOrder;
                    groupFirstSeen[f.Group] = f.SeenIndex;
                }
                list.Add(f);
            }

            if (ungrouped.Count > 0)
            {
                ungrouped.Sort(byField);
                sections.Add(new Section(null, Names(ungrouped)));
            }

            titles.Sort((x, y) =>
            {
                int c = groupOrder[x].CompareTo(groupOrder[y]);
                return c != 0 ? c : groupFirstSeen[x].CompareTo(groupFirstSeen[y]);
            });

            foreach (var title in titles)
            {
                var bucket = buckets[title];
                bucket.Sort(byField);
                sections.Add(new Section(title, Names(bucket)));
            }

            return sections;
        }

        static List<string> Names(List<Field> fields)
        {
            var names = new List<string>(fields.Count);
            for (int i = 0; i < fields.Count; i++) names.Add(fields[i].Name);
            return names;
        }
    }
}
