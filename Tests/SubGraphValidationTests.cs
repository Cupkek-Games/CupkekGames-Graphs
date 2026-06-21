using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CupkekGames.Graphs.Tests
{
    /// <summary>
    /// Model proof for <see cref="SubGraphValidation"/>, including the
    /// working-copy identity case: a self-reference or cycle introduced in a
    /// <see cref="GraphAssetSO.CloneForEditing"/> clone must be reported live
    /// (the clone is reference-unequal to the on-disk asset its sub-graph
    /// references point at — <c>EditorIdentity</c> bridges that).
    /// </summary>
    [Category("Graphs")]
    public class SubGraphValidationTests
    {
        private sealed class TestGraph : GraphAssetSO
        {
            public override Type NodeBaseType => typeof(GraphNodeSO);
        }

        private sealed class SubGraphNode : GraphNodeSO, ISubGraphNode
        {
            [SerializeField] public GraphAssetSO Reference;
            public GraphAssetSO SubGraph => Reference;
        }

        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private TestGraph Graph(string name = "G")
        {
            var g = ScriptableObject.CreateInstance<TestGraph>();
            g.name = name;
            _created.Add(g);
            return g;
        }

        private SubGraphNode RefNode(TestGraph host, GraphAssetSO reference)
        {
            var n = ScriptableObject.CreateInstance<SubGraphNode>();
            n.Reference = reference;
            host.AddNode(n);
            _created.Add(n);
            return n;
        }

        private static List<GraphValidationIssue> Issues(GraphAssetSO g) =>
            SubGraphValidation.Validate(g).ToList();

        // ── On-disk (identity == self) ─────────────────────────────────

        [Test]
        public void Validate_UnassignedReference_IsWarning()
        {
            var g = Graph();
            RefNode(g, null);

            var issues = Issues(g);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GraphValidationIssue.SeverityLevel.Warning, issues[0].Severity);
        }

        [Test]
        public void Validate_SelfReference_IsError()
        {
            var g = Graph();
            RefNode(g, g);

            var issues = Issues(g);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GraphValidationIssue.SeverityLevel.Error, issues[0].Severity);
        }

        [Test]
        public void Validate_TransitiveCycle_IsError()
        {
            var a = Graph("A");
            var b = Graph("B");
            RefNode(a, b);
            RefNode(b, a); // B → A closes the loop

            Assert.AreEqual(1, Issues(a).Count, "A's edge to B closes a cycle (B reaches A)");
            Assert.AreEqual(1, Issues(b).Count, "and symmetrically from B");
        }

        [Test]
        public void Validate_Diamond_IsNotACycle()
        {
            var root = Graph("Root");
            var left = Graph("L");
            var right = Graph("R");
            var common = Graph("C");
            RefNode(root, left);
            RefNode(root, right);
            RefNode(left, common);
            RefNode(right, common);

            Assert.IsEmpty(Issues(root));
        }

        // ── Working copy (identity != self) ────────────────────────────

        [Test]
        public void Validate_WorkingCopy_SelfReferenceToOriginal_IsReportedLive()
        {
            var original = Graph("O");
            var working = original.CloneForEditing();
            _created.Add(working);
            foreach (var n in working.Nodes) _created.Add(n);

            // The user assigns the on-disk original as a sub-graph of itself,
            // inside the working copy — pre-EditorIdentity this slipped past
            // reference equality until save + reopen.
            RefNode((TestGraph)working, original);

            var issues = Issues(working);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GraphValidationIssue.SeverityLevel.Error, issues[0].Severity);
        }

        [Test]
        public void Validate_WorkingCopy_TransitiveCycleToOriginal_IsReportedLive()
        {
            var original = Graph("O");
            var middle = Graph("M");
            RefNode(middle, original); // M → O already on disk

            var working = original.CloneForEditing();
            _created.Add(working);
            foreach (var n in working.Nodes) _created.Add(n);

            // In the working copy, reference M: saving would create O → M → O.
            RefNode((TestGraph)working, middle);

            var issues = Issues(working);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GraphValidationIssue.SeverityLevel.Error, issues[0].Severity);
        }

        [Test]
        public void Validate_WorkingCopy_LegitimateReference_NoIssues()
        {
            var original = Graph("O");
            var child = Graph("Child");

            var working = original.CloneForEditing();
            _created.Add(working);
            foreach (var n in working.Nodes) _created.Add(n);

            RefNode((TestGraph)working, child);

            Assert.IsEmpty(Issues(working));
        }
    }
}
