using System;
using System.Collections.Generic;
using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Read-only topology helpers over a <see cref="GraphAssetSO"/>'s
    /// <b>node/edge</b> graph — forward reachability / cycle checks and a forest
    /// adjacency view (roots, ordered children, single parent, pre-order walk).
    ///
    /// <para>
    /// This is the intra-graph connection-graph counterpart to
    /// <see cref="SubGraphResolver"/> (which covers the asset-<i>reference</i>
    /// graph). Edge direction is <c>source → target</c>; sibling order is
    /// <see cref="GraphConnection.OrderIndex"/> — the same contracts the editor
    /// canvases and the domain runtimes already rely on. Promoted here so nav,
    /// behaviour-trees, and the editor's own <c>AutoLayoutEngine</c> stop
    /// re-implementing the same walks.
    /// </para>
    /// </summary>
    public static class GraphTopology
    {
        // ── Reachability / cycles ────────────────────────────────────

        /// <summary>
        /// True if <paramref name="to"/> is reachable from <paramref name="from"/>
        /// by walking outbound (<c>source → target</c>) edges. Self (<c>from == to</c>)
        /// counts as reachable.
        /// </summary>
        public static bool Reaches(GraphAssetSO asset, SerializedGuid from, SerializedGuid to)
        {
            if (asset == null) return false;
            if (from == to) return true;

            var visited = new HashSet<string>();
            var stack = new Stack<SerializedGuid>();
            stack.Push(from);

            var conns = asset.Connections;
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == to) return true;
                if (!visited.Add(cur.ValueStr)) continue;

                for (int i = 0; i < conns.Count; i++)
                    if (conns[i].SourceNodeGuid == cur)
                        stack.Push(conns[i].TargetNodeGuid);
            }
            return false;
        }

        /// <summary>
        /// True if adding a <paramref name="source"/> → <paramref name="target"/>
        /// edge would close a cycle — i.e. <paramref name="target"/> can already
        /// reach <paramref name="source"/>. Use from a domain canvas's
        /// <c>CanConnect</c> (or the base <c>EnforceAcyclic</c> opt-in) to keep a
        /// graph acyclic.
        /// </summary>
        public static bool WouldCreateCycle(GraphAssetSO asset, SerializedGuid source, SerializedGuid target)
            => Reaches(asset, target, source);

        // ── One-shot child resolution ────────────────────────────────

        /// <summary>
        /// Outbound children of <paramref name="node"/> in <paramref name="asset"/>,
        /// ordered by <see cref="GraphConnection.OrderIndex"/>. One-shot (no
        /// prebuilt index) — for runtime callers resolving a single node's
        /// children on demand (e.g. a behaviour-tree composite). Build an
        /// <see cref="Adjacency"/> instead when walking the whole forest.
        /// </summary>
        public static IReadOnlyList<GraphNodeSO> ChildrenOf(GraphAssetSO asset, GraphNodeSO node)
        {
            if (asset == null || node == null) return Array.Empty<GraphNodeSO>();

            List<(int order, GraphNodeSO node)> matches = null;
            var conns = asset.Connections;
            for (int i = 0; i < conns.Count; i++)
            {
                var c = conns[i];
                if (c.SourceNodeGuid != node.Guid) continue;
                var target = FindNode(asset, c.TargetNodeGuid);
                if (target == null) continue;
                (matches ??= new List<(int, GraphNodeSO)>()).Add((c.OrderIndex, target));
            }

            if (matches == null) return Array.Empty<GraphNodeSO>();
            matches.Sort((a, b) => a.order.CompareTo(b.order));

            var result = new GraphNodeSO[matches.Count];
            for (int i = 0; i < matches.Count; i++) result[i] = matches[i].node;
            return result;
        }

        private static GraphNodeSO FindNode(GraphAssetSO asset, SerializedGuid guid)
        {
            var nodes = asset.Nodes;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && nodes[i].Guid == guid) return nodes[i];
            return null;
        }

        // ── Forest adjacency view ────────────────────────────────────

        /// <summary>
        /// Build a read-only forest view of the asset's connections. Built once
        /// — cache it if you query repeatedly.
        /// </summary>
        public static Adjacency BuildAdjacency(GraphAssetSO asset) => new Adjacency(asset);

        /// <summary>
        /// Forest view over a graph's connections: each node's ordered children
        /// (outbound targets sorted by <see cref="GraphConnection.OrderIndex"/>),
        /// its single parent (first inbound source), the roots (no inbound), and
        /// a pre-order walk (parents before children, visited-guarded).
        /// </summary>
        public sealed class Adjacency
        {
            private readonly Dictionary<string, GraphNodeSO> _byGuid = new();
            private readonly Dictionary<string, List<GraphNodeSO>> _children = new();
            private readonly Dictionary<string, GraphNodeSO> _parent = new();
            private readonly List<GraphNodeSO> _roots = new();

            internal Adjacency(GraphAssetSO asset)
            {
                if (asset == null) return;

                var nodes = asset.Nodes;
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i] != null) _byGuid[nodes[i].Guid.ValueStr] = nodes[i];

                // Pre-sort edges by OrderIndex so each parent's children bucket
                // in sibling order (matches composite left-to-right semantics).
                var conns = new List<GraphConnection>(asset.Connections);
                conns.Sort((a, b) => a.OrderIndex.CompareTo(b.OrderIndex));

                var hasInbound = new HashSet<string>();
                foreach (var c in conns)
                {
                    if (!_byGuid.TryGetValue(c.SourceNodeGuid.ValueStr, out var src)) continue;
                    if (!_byGuid.TryGetValue(c.TargetNodeGuid.ValueStr, out var tgt)) continue;

                    if (!_children.TryGetValue(src.Guid.ValueStr, out var list))
                        _children[src.Guid.ValueStr] = list = new List<GraphNodeSO>();
                    list.Add(tgt);

                    // First inbound edge wins as the (singular) parent.
                    if (!_parent.ContainsKey(tgt.Guid.ValueStr))
                        _parent[tgt.Guid.ValueStr] = src;
                    hasInbound.Add(tgt.Guid.ValueStr);
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    if (n != null && !hasInbound.Contains(n.Guid.ValueStr))
                        _roots.Add(n);
                }
            }

            /// <summary>Nodes with no inbound edge (forest roots), in asset order.</summary>
            public IReadOnlyList<GraphNodeSO> Roots => _roots;

            /// <summary>Outbound children of <paramref name="node"/>, ordered by OrderIndex. Empty when none.</summary>
            public IReadOnlyList<GraphNodeSO> ChildrenOf(GraphNodeSO node)
            {
                if (node != null && _children.TryGetValue(node.Guid.ValueStr, out var list)) return list;
                return Array.Empty<GraphNodeSO>();
            }

            /// <summary>The (first) inbound parent of <paramref name="node"/>, or null if it's a root.</summary>
            public GraphNodeSO ParentOf(GraphNodeSO node)
            {
                if (node != null && _parent.TryGetValue(node.Guid.ValueStr, out var p)) return p;
                return null;
            }

            /// <summary>
            /// Pre-order walk of the containment forest: each root then its
            /// descendants (children in OrderIndex order), parents always before
            /// children, every node visited at most once (cycle/diamond-guarded).
            /// Nodes not reachable from any root (cycle islands) are not emitted,
            /// matching the spawn/layout consumers.
            /// </summary>
            public IEnumerable<GraphNodeSO> PreOrder()
            {
                var visited = new HashSet<string>();
                var result = new List<GraphNodeSO>();
                foreach (var r in _roots) Walk(r, visited, result);
                return result;
            }

            private void Walk(GraphNodeSO node, HashSet<string> visited, List<GraphNodeSO> result)
            {
                if (node == null || !visited.Add(node.Guid.ValueStr)) return;
                result.Add(node);
                var children = ChildrenOf(node);
                for (int i = 0; i < children.Count; i++) Walk(children[i], visited, result);
            }
        }
    }
}
