using System.Collections.Generic;
using CupkekGames.Data.Primitives;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// One-shot tree layout for the bound graph. Identifies "roots" (nodes
    /// with no inbound connection), recursively places each subtree using a
    /// Reingold-Tilford-style horizontal arrangement (children laid out side
    /// by side beneath their parent), then places independent roots left to
    /// right with separation. Cyclic / orphan nodes are left where they
    /// were — auto-layout is opt-in for tree-shaped graphs.
    ///
    /// Wrapped in a single Undo group so the user can Ctrl+Z back to their
    /// previous layout if the result isn't what they wanted.
    /// </summary>
    public static class AutoLayoutEngine
    {
        public const float NodeWidth = 200f;
        public const float NodeHeight = 80f;
        public const float HorizontalGap = 40f;
        public const float VerticalGap = 60f;
        public const float ComponentGap = HorizontalGap * 2f;

        /// <summary>
        /// Lay out <paramref name="asset"/>'s tree(s). Nodes that aren't
        /// reachable from any root (cycles or isolated nodes) stay where
        /// they were.
        /// </summary>
        public static void LayoutTree(GraphAssetSO asset)
        {
            if (asset == null || asset.Nodes.Count == 0) return;

            // Inbound count per node — roots are the ones with zero.
            var inboundCount = new Dictionary<string, int>();
            foreach (var c in asset.Connections)
            {
                inboundCount.TryGetValue(c.TargetNodeGuid.ValueStr, out var n);
                inboundCount[c.TargetNodeGuid.ValueStr] = n + 1;
            }

            var roots = new List<GraphNodeSO>();
            foreach (var n in asset.Nodes)
            {
                if (n == null) continue;
                if (!inboundCount.ContainsKey(n.Guid.ValueStr))
                    roots.Add(n);
            }

            if (roots.Count == 0) return;

            var visited = new HashSet<string>();
            var trees = new List<Node>();
            foreach (var r in roots)
            {
                var tree = BuildTree(asset, r, visited);
                if (tree != null) trees.Add(tree);
            }

            foreach (var t in trees) ComputeWidth(t);

            // Lay each independent tree out side-by-side at y=0.
            float cursorX = 0f;
            foreach (var t in trees)
            {
                Place(t, cursorX + t.Width * 0.5f, 0f);
                cursorX += t.Width + ComponentGap;
            }

            // Single Undo group for the entire layout pass.
            var nodes = CollectAllNodes(trees);
            if (nodes.Count == 0) return;

            Undo.RegisterCompleteObjectUndo(nodes.ToArray(), "Auto Layout");

            foreach (var t in trees) ApplyRecursive(t);
        }

        // ---------------------------------------------------------------

        class Node
        {
            public GraphNodeSO Source;
            public List<Node> Children = new();
            public float Width;
            public Vector2 Position;
        }

        static Node BuildTree(GraphAssetSO asset, GraphNodeSO source, HashSet<string> visited)
        {
            if (source == null || !visited.Add(source.Guid.ValueStr)) return null;

            var node = new Node { Source = source };

            // Children ordered by GraphConnection.OrderIndex — matches the
            // composite-children semantic the runtime uses.
            var ordered = new List<GraphConnection>();
            foreach (var c in asset.Connections)
                if (c.SourceNodeGuid == source.Guid)
                    ordered.Add(c);
            ordered.Sort((a, b) => a.OrderIndex.CompareTo(b.OrderIndex));

            foreach (var conn in ordered)
            {
                var target = FindNode(asset, conn.TargetNodeGuid);
                if (target == null) continue;

                var child = BuildTree(asset, target, visited);
                if (child != null) node.Children.Add(child);
            }
            return node;
        }

        static GraphNodeSO FindNode(GraphAssetSO asset, SerializedGuid guid)
        {
            foreach (var n in asset.Nodes)
                if (n != null && n.Guid == guid) return n;
            return null;
        }

        static float ComputeWidth(Node n)
        {
            if (n.Children.Count == 0)
            {
                n.Width = NodeWidth;
                return n.Width;
            }

            float total = 0f;
            foreach (var c in n.Children) total += ComputeWidth(c);
            total += (n.Children.Count - 1) * HorizontalGap;

            n.Width = Mathf.Max(NodeWidth, total);
            return n.Width;
        }

        static void Place(Node n, float centerX, float topY)
        {
            n.Position = new Vector2(centerX - NodeWidth * 0.5f, topY);
            if (n.Children.Count == 0) return;

            float totalChildWidth = 0f;
            foreach (var c in n.Children) totalChildWidth += c.Width;
            totalChildWidth += (n.Children.Count - 1) * HorizontalGap;

            float childY = topY + NodeHeight + VerticalGap;
            float leftX = centerX - totalChildWidth * 0.5f;
            float cursorX = leftX;

            foreach (var c in n.Children)
            {
                Place(c, cursorX + c.Width * 0.5f, childY);
                cursorX += c.Width + HorizontalGap;
            }
        }

        static List<UnityEngine.Object> CollectAllNodes(List<Node> trees)
        {
            var list = new List<UnityEngine.Object>();
            foreach (var t in trees) CollectRecursive(t, list);
            return list;
        }

        static void CollectRecursive(Node n, List<UnityEngine.Object> list)
        {
            if (n.Source != null) list.Add(n.Source);
            foreach (var c in n.Children) CollectRecursive(c, list);
        }

        static void ApplyRecursive(Node n)
        {
            if (n.Source != null)
            {
                n.Source.Position = n.Position;
                EditorUtility.SetDirty(n.Source);
            }
            foreach (var c in n.Children) ApplyRecursive(c);
        }
    }
}
