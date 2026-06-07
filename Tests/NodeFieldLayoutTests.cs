using System.Collections.Generic;
using NUnit.Framework;

namespace CupkekGames.Graphs.Tests
{
    /// <summary>
    /// Model proof for <see cref="NodeFieldLayout.Resolve"/> — the pure ordering
    /// behind the node card's <c>[NodeGroup]</c> field sections. No Unity Editor
    /// types involved: feed field metadata, assert the resolved section + field
    /// order (ungrouped-first, group order, within-group order, ties).
    /// </summary>
    [Category("Graphs")]
    public class NodeFieldLayoutTests
    {
        // name, group, groupOrder, declaration index (seen); fieldOrder defaults
        // to seen, mirroring NodeElement.BuildBody when no [NodeFieldOrder] is set.
        private static NodeFieldLayout.Field F(string name, string group, int groupOrder, int seen, int fieldOrder = -1)
            => new NodeFieldLayout.Field(name, group, groupOrder, fieldOrder < 0 ? seen : fieldOrder, seen);

        private static List<string> Flatten(IReadOnlyList<NodeFieldLayout.Section> sections)
        {
            var all = new List<string>();
            foreach (var s in sections) all.AddRange(s.FieldNames);
            return all;
        }

        private static List<string> SectionTitles(IReadOnlyList<NodeFieldLayout.Section> sections)
        {
            var titles = new List<string>();
            foreach (var s in sections) titles.Add(s.Title);
            return titles;
        }

        [Test]
        public void NoGroups_OneUntitledSection_InDeclarationOrder()
        {
            var sections = NodeFieldLayout.Resolve(new[]
            {
                F("a", null, 0, 0),
                F("b", null, 0, 1),
                F("c", null, 0, 2),
            });

            Assert.AreEqual(1, sections.Count);
            Assert.IsNull(sections[0].Title, "ungrouped => one untitled (flat) section");
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, sections[0].FieldNames);
        }

        [Test]
        public void Empty_Or_Null_ReturnsNoSections()
        {
            Assert.AreEqual(0, NodeFieldLayout.Resolve(new NodeFieldLayout.Field[0]).Count);
            Assert.AreEqual(0, NodeFieldLayout.Resolve(null).Count);
        }

        [Test]
        public void Groups_SortedByGroupOrder_RegardlessOfDeclarationOrder()
        {
            // B (order 2) is declared before A (order 1); A must still come first.
            var sections = NodeFieldLayout.Resolve(new[]
            {
                F("b1", "B", 2, 0),
                F("a1", "A", 1, 1),
            });

            Assert.AreEqual(2, sections.Count);
            Assert.AreEqual("A", sections[0].Title);
            Assert.AreEqual("B", sections[1].Title);
        }

        [Test]
        public void Groups_SameOrder_TieBrokenByFirstSeen()
        {
            var sections = NodeFieldLayout.Resolve(new[]
            {
                F("y1", "Y", 5, 0), // seen first
                F("x1", "X", 5, 1),
            });

            Assert.AreEqual("Y", sections[0].Title, "equal order => first-declared group wins");
            Assert.AreEqual("X", sections[1].Title);
        }

        [Test]
        public void UngroupedFields_RenderBeforeTitledGroups()
        {
            var sections = NodeFieldLayout.Resolve(new[]
            {
                F("grouped", "G", 0, 0),
                F("loose", null, 0, 1),
            });

            Assert.AreEqual(2, sections.Count);
            Assert.IsNull(sections[0].Title);
            CollectionAssert.AreEqual(new[] { "loose" }, sections[0].FieldNames);
            Assert.AreEqual("G", sections[1].Title);
        }

        [Test]
        public void WithinGroup_SortedByFieldOrder_ThenSeen()
        {
            // Same group; explicit fieldOrder overrides declaration order.
            var sections = NodeFieldLayout.Resolve(new[]
            {
                F("third",  "G", 0, 0, fieldOrder: 30),
                F("first",  "G", 0, 1, fieldOrder: 10),
                F("second", "G", 0, 2, fieldOrder: 20),
            });

            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, sections[0].FieldNames);
        }

        [Test]
        public void NavNodeShape_PrefabIsSecondOverall()
        {
            // Mirrors NavNode: Identity(order 0)=_id; Behavior(2)/Containment(3)/
            // Backdrop(4) are DECLARED before Spawn(1)=_prefab,... . Because Spawn
            // is group order 1 it renders 2nd, so _prefab (its first field) lands
            // at overall slot #2 — the whole point of the grouping change.
            var sections = NodeFieldLayout.Resolve(new[]
            {
                F("_id",                        "Identity",    0, 0),
                F("_occlusion",                 "Behavior",    2, 1),
                F("_dismissMode",               "Behavior",    2, 2),
                F("_resetStateOnReopen",        "Behavior",    2, 3),
                F("_destroyOnPop",              "Behavior",    2, 4),
                F("_disableOtherViewsOnFadeIn", "Behavior",    2, 5),
                F("_childMode",                 "Containment", 3, 6),
                F("_backdrop",                  "Backdrop",    4, 7),
                F("_customBackdropAsset",       "Backdrop",    4, 8),
                F("_prefab",                    "Spawn",       1, 9),
                F("_startVisible",              "Spawn",       1, 10),
                F("_isMultiInstance",           "Spawn",       1, 11),
            });

            CollectionAssert.AreEqual(
                new[] { "Identity", "Spawn", "Behavior", "Containment", "Backdrop" },
                SectionTitles(sections));

            var flat = Flatten(sections);
            Assert.AreEqual("_id", flat[0], "id renders first");
            Assert.AreEqual("_prefab", flat[1], "prefab renders second");

            // Spawn keeps declaration order internally (prefab, then the rest).
            CollectionAssert.AreEqual(
                new[] { "_prefab", "_startVisible", "_isMultiInstance" },
                sections[1].FieldNames);
        }
    }
}
