using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CupkekGames.Graphs.Tests
{
    /// <summary>
    /// Model proof for the <see cref="GraphLayout"/> sidecar — node canvas
    /// positions plus the sparse collapsed-node set, keyed by GUID. Covers the
    /// collapse tracking added for node folding: round-trip, idempotent
    /// toggling, position/collapse independence, and that <c>PruneExcept</c>
    /// drops deleted nodes from BOTH lists without desync.
    /// </summary>
    [Category("Graphs")]
    public class GraphLayoutTests
    {
        private GraphLayout _layout;

        [SetUp]
        public void SetUp() => _layout = ScriptableObject.CreateInstance<GraphLayout>();

        [TearDown]
        public void TearDown()
        {
            if (_layout != null) Object.DestroyImmediate(_layout);
        }

        [Test]
        public void SetCollapsed_AddsThenClears()
        {
            Assert.IsFalse(_layout.IsCollapsed("a"), "an absent guid is expanded by default");

            _layout.SetCollapsed("a", true);
            Assert.IsTrue(_layout.IsCollapsed("a"));

            _layout.SetCollapsed("a", false);
            Assert.IsFalse(_layout.IsCollapsed("a"));
        }

        [Test]
        public void SetCollapsed_IsIdempotent_NoDuplicateMembership()
        {
            _layout.SetCollapsed("a", true);
            _layout.SetCollapsed("a", true); // repeat must not double-add

            // A single clear must fully expand — would fail if a duplicate lingered.
            _layout.SetCollapsed("a", false);
            Assert.IsFalse(_layout.IsCollapsed("a"));
        }

        [Test]
        public void Position_And_Collapsed_AreIndependent()
        {
            _layout.Set("a", new Vector2(12f, 34f));
            _layout.SetCollapsed("a", true);

            Assert.IsTrue(_layout.TryGet("a", out var pos));
            Assert.AreEqual(new Vector2(12f, 34f), pos);
            Assert.IsTrue(_layout.IsCollapsed("a"));

            // Clearing collapse leaves the stored position untouched.
            _layout.SetCollapsed("a", false);
            Assert.IsTrue(_layout.TryGet("a", out var pos2));
            Assert.AreEqual(new Vector2(12f, 34f), pos2);
        }

        [Test]
        public void PruneExcept_DropsStaleEntriesFromBothLists()
        {
            _layout.Set("a", Vector2.one);
            _layout.Set("b", new Vector2(2f, 2f));
            _layout.Set("c", new Vector2(3f, 3f));
            _layout.SetCollapsed("b", true);
            _layout.SetCollapsed("c", true);

            _layout.PruneExcept(new HashSet<string> { "a" }); // b and c deleted

            Assert.IsTrue(_layout.TryGet("a", out _), "live guid's position kept");
            Assert.IsFalse(_layout.TryGet("b", out _), "stale position pruned");
            Assert.IsFalse(_layout.TryGet("c", out _), "stale position pruned");
            Assert.IsFalse(_layout.IsCollapsed("b"), "stale collapse pruned");
            Assert.IsFalse(_layout.IsCollapsed("c"), "stale collapse pruned");
        }

        [Test]
        public void PruneExcept_KeepsCollapseForLiveGuid()
        {
            _layout.Set("a", Vector2.zero);
            _layout.SetCollapsed("a", true);

            _layout.PruneExcept(new HashSet<string> { "a" });

            Assert.IsTrue(_layout.IsCollapsed("a"), "a live node's collapse survives the prune");
        }
    }
}
