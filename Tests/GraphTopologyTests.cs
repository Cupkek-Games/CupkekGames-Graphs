using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CupkekGames.Graphs.Tests
{
    /// <summary>
    /// Model proof for <see cref="GraphTopology"/> — the shared node/edge-graph
    /// helper (reachability / cycle checks + the forest adjacency view) that nav,
    /// behaviour-trees, and AutoLayoutEngine now share. Builds in-memory graphs from
    /// a minimal concrete <see cref="GraphAssetSO"/> (no domain).
    /// </summary>
    public class GraphTopologyTests
    {
        private sealed class TestGraph : GraphAssetSO
        {
            public override Type NodeBaseType => typeof(TestNode);
        }

        private sealed class TestNode : GraphNodeSO { }

        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private TestGraph Graph()
        {
            var g = ScriptableObject.CreateInstance<TestGraph>();
            _created.Add(g);
            return g;
        }

        private TestNode Node(TestGraph g)
        {
            var n = ScriptableObject.CreateInstance<TestNode>();
            g.AddNode(n);
            _created.Add(n);
            return n;
        }

        private static void Connect(TestGraph g, GraphNodeSO parent, GraphNodeSO child, int order = 0)
            => g.AddConnection(new GraphConnection
            {
                SourceNodeGuid = parent.Guid,
                TargetNodeGuid = child.Guid,
                OrderIndex = order,
            });

        // ── Reachability / cycles ──────────────────────────────────────

        [Test]
        public void Reaches_FollowsOutboundEdges()
        {
            var g = Graph();
            var a = Node(g); var b = Node(g); var c = Node(g);
            Connect(g, a, b);
            Connect(g, b, c);

            Assert.IsTrue(GraphTopology.Reaches(g, a.Guid, c.Guid), "a → b → c");
            Assert.IsFalse(GraphTopology.Reaches(g, c.Guid, a.Guid), "not backwards");
            Assert.IsTrue(GraphTopology.Reaches(g, a.Guid, a.Guid), "self is reachable");
        }

        [Test]
        public void WouldCreateCycle_WhenTargetAlreadyReachesSource()
        {
            var g = Graph();
            var a = Node(g); var b = Node(g);
            Connect(g, a, b); // a → b

            // Adding b → a would close a loop (a already reaches b).
            Assert.IsTrue(GraphTopology.WouldCreateCycle(g, b.Guid, a.Guid));
            // Adding a → b again is a duplicate edge, not a cycle.
            Assert.IsFalse(GraphTopology.WouldCreateCycle(g, a.Guid, b.Guid));
        }

        // ── Forest adjacency view ──────────────────────────────────────

        [Test]
        public void Roots_AreNodesWithNoInbound()
        {
            var g = Graph();
            var root = Node(g); var child = Node(g); var orphan = Node(g);
            Connect(g, root, child);

            var adj = GraphTopology.BuildAdjacency(g);

            CollectionAssert.AreEquivalent(new[] { root, orphan }, adj.Roots); // child has inbound
        }

        [Test]
        public void ChildrenOf_AreOrderedByOrderIndex_AndParentIsResolved()
        {
            var g = Graph();
            var parent = Node(g);
            var c1 = Node(g); var c2 = Node(g); var c3 = Node(g);
            Connect(g, parent, c3, order: 2);
            Connect(g, parent, c1, order: 0);
            Connect(g, parent, c2, order: 1);

            var adj = GraphTopology.BuildAdjacency(g);

            CollectionAssert.AreEqual(new[] { c1, c2, c3 }, adj.ChildrenOf(parent)); // sorted by order
            Assert.AreEqual(parent, adj.ParentOf(c1));
            Assert.IsNull(adj.ParentOf(parent)); // a root has no parent
            CollectionAssert.IsEmpty(adj.ChildrenOf(c1));
        }

        [Test]
        public void PreOrder_VisitsParentsBeforeChildren()
        {
            var g = Graph();
            var r = Node(g);
            var a = Node(g); var b = Node(g);
            Connect(g, r, a, order: 0);
            Connect(g, r, b, order: 1);
            var a1 = Node(g);
            Connect(g, a, a1);

            var order = GraphTopology.BuildAdjacency(g).PreOrder().ToList();

            Assert.AreEqual(4, order.Count);                          // r, a, b, a1 (all root-reachable)
            Assert.Less(order.IndexOf(r), order.IndexOf(a));
            Assert.Less(order.IndexOf(r), order.IndexOf(b));
            Assert.Less(order.IndexOf(a), order.IndexOf(a1));         // parent before its child
        }
    }
}
