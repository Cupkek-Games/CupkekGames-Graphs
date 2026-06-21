using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CupkekGames.Graphs.Tests
{
    /// <summary>
    /// Model proof for <see cref="GraphConnectionValidation"/> — detection and
    /// repair of connections whose endpoint node was deleted or whose port id
    /// went stale after a node type changed its port definitions.
    /// </summary>
    [Category("Graphs")]
    public class GraphConnectionValidationTests
    {
        private sealed class TestGraph : GraphAssetSO
        {
            public override Type NodeBaseType => typeof(GraphNodeSO);
        }

        /// <summary>Default ports: one anonymous input, one anonymous output.</summary>
        private sealed class AnonymousNode : GraphNodeSO { }

        /// <summary>Named single ports — the "rename happened" target shape.</summary>
        private sealed class NamedSinglePortNode : GraphNodeSO
        {
            public override IReadOnlyList<GraphPortDef> InputPorts =>
                new[] { new GraphPortDef { Id = "in" } };
            public override IReadOnlyList<GraphPortDef> OutputPorts =>
                new[] { new GraphPortDef { Id = "out" } };
        }

        /// <summary>Two named outputs — ambiguous remap target.</summary>
        private sealed class TwoOutputNode : GraphNodeSO
        {
            public override IReadOnlyList<GraphPortDef> OutputPorts => new[]
            {
                new GraphPortDef { Id = "a" },
                new GraphPortDef { Id = "b" },
            };
        }

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

        private T Node<T>(TestGraph g) where T : GraphNodeSO
        {
            var n = ScriptableObject.CreateInstance<T>();
            g.AddNode(n);
            _created.Add(n);
            return n;
        }

        private static GraphConnection Connect(TestGraph g, GraphNodeSO source, GraphNodeSO target,
            string sourcePortId = null, string targetPortId = null)
        {
            var c = new GraphConnection
            {
                SourceNodeGuid = source.Guid,
                TargetNodeGuid = target.Guid,
                SourcePortId = sourcePortId,
                TargetPortId = targetPortId,
            };
            g.AddConnection(c);
            return c;
        }

        // ── Validate ───────────────────────────────────────────────────

        [Test]
        public void Validate_CleanGraph_NoIssues()
        {
            var g = Graph();
            var a = Node<AnonymousNode>(g);
            var b = Node<AnonymousNode>(g);
            Connect(g, a, b);

            Assert.IsEmpty(GraphConnectionValidation.Validate(g).ToList());
        }

        [Test]
        public void Validate_EmptyAndNullPortIdBothMatchAnonymous()
        {
            var g = Graph();
            var a = Node<AnonymousNode>(g);
            var b = Node<AnonymousNode>(g);
            Connect(g, a, b, sourcePortId: "", targetPortId: null);

            Assert.IsEmpty(GraphConnectionValidation.Validate(g).ToList(),
                "empty string and null both mean the anonymous default port");
        }

        [Test]
        public void Validate_MissingEndpointNode_IsError()
        {
            var g = Graph();
            var a = Node<AnonymousNode>(g);
            var b = Node<AnonymousNode>(g);
            var c = Connect(g, a, b);

            // RemoveNode sweeps the connection; re-adding it recreates the
            // dangling state a node deleted outside the API would leave.
            g.RemoveNode(b);
            g.AddConnection(c);

            var issues = GraphConnectionValidation.Validate(g).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GraphValidationIssue.SeverityLevel.Error, issues[0].Severity);
            Assert.AreEqual(c.Guid, issues[0].TargetConnectionGuid);
        }

        [Test]
        public void Validate_StalePortId_IsErrorPerSide()
        {
            var g = Graph();
            var a = Node<NamedSinglePortNode>(g);
            var b = Node<NamedSinglePortNode>(g);
            // Authored against old port names that no longer exist.
            var c = Connect(g, a, b, sourcePortId: "oldOut", targetPortId: "oldIn");

            var issues = GraphConnectionValidation.Validate(g).ToList();
            Assert.AreEqual(2, issues.Count, "one issue per stale side");
            Assert.IsTrue(issues.All(i => i.TargetConnectionGuid == c.Guid));
            Assert.IsTrue(issues.All(i => i.Severity == GraphValidationIssue.SeverityLevel.Error));
        }

        // ── Repair ─────────────────────────────────────────────────────

        [Test]
        public void Repair_RemovesConnectionsToMissingNodes()
        {
            var g = Graph();
            var a = Node<AnonymousNode>(g);
            var b = Node<AnonymousNode>(g);
            var c = Connect(g, a, b);
            g.RemoveNode(b);
            g.AddConnection(c); // dangling again

            var report = GraphConnectionValidation.Repair(g);

            Assert.AreEqual(1, report.RemovedConnections);
            Assert.IsEmpty(g.Connections);
            Assert.IsEmpty(GraphConnectionValidation.Validate(g).ToList());
        }

        [Test]
        public void Repair_RemapsStalePortId_WhenSinglePortOnThatSide()
        {
            var g = Graph();
            var a = Node<NamedSinglePortNode>(g);
            var b = Node<NamedSinglePortNode>(g);
            var c = Connect(g, a, b, sourcePortId: "oldOut", targetPortId: "oldIn");

            var report = GraphConnectionValidation.Repair(g);

            Assert.AreEqual(2, report.RemappedPortIds);
            Assert.AreEqual("out", c.SourcePortId);
            Assert.AreEqual("in", c.TargetPortId);
            Assert.IsEmpty(report.Unrepairable);
            Assert.IsEmpty(GraphConnectionValidation.Validate(g).ToList());
        }

        [Test]
        public void Repair_LeavesAmbiguousPortAlone_AndReportsIt()
        {
            var g = Graph();
            var a = Node<TwoOutputNode>(g);
            var b = Node<AnonymousNode>(g);
            var c = Connect(g, a, b, sourcePortId: "oldOut");

            var report = GraphConnectionValidation.Repair(g);

            Assert.AreEqual(0, report.RemappedPortIds);
            Assert.AreEqual("oldOut", c.SourcePortId, "ambiguous remap must not guess");
            CollectionAssert.Contains(report.Unrepairable, c);
            Assert.IsNotEmpty(GraphConnectionValidation.Validate(g).ToList(),
                "still flagged after repair so the footer keeps reporting it");
        }

        [Test]
        public void Repair_CleanGraph_ReportsNothingChanged()
        {
            var g = Graph();
            var a = Node<AnonymousNode>(g);
            var b = Node<AnonymousNode>(g);
            Connect(g, a, b);

            var report = GraphConnectionValidation.Repair(g);

            Assert.IsFalse(report.ChangedAnything);
            Assert.AreEqual(1, g.Connections.Count);
        }
    }
}
