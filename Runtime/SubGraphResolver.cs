using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Pure reference-graph walking over <see cref="ISubGraphNode"/> links between
    /// <see cref="GraphAssetSO"/> assets. The package never interprets node semantics —
    /// it only knows "this node implements <see cref="ISubGraphNode"/> and points at that
    /// <see cref="GraphAssetSO"/>." There is deliberately <b>no</b> Execute/Update/Tick
    /// across the boundary: a consumer descends at its own point of reaching a sub-graph
    /// node and drives the child with its own runtime.
    ///
    /// <para>
    /// Comparison is by asset reference, which is correct at runtime (real assets). The
    /// editor working-copy case (a transient clone is reference-unequal to its on-disk
    /// original) is handled separately by the validation pass when it wires into the
    /// editor — not here.
    /// </para>
    /// </summary>
    public static class SubGraphResolver
    {
        /// <summary>
        /// Depth-1 (node, child) pairs for every <see cref="ISubGraphNode"/> in
        /// <paramref name="graph"/>. <c>child</c> may be null (an unassigned reference).
        /// </summary>
        public static IEnumerable<(GraphNodeSO node, GraphAssetSO child)> DirectReferences(GraphAssetSO graph)
        {
            if (graph == null) yield break;
            foreach (var n in graph.Nodes)
            {
                if (n is ISubGraphNode sg)
                    yield return (n, sg.SubGraph);
            }
        }

        /// <summary>
        /// All graphs transitively reachable from <paramref name="root"/> (including
        /// root), each yielded once, in pre-order DFS. Cycle-safe and memoized via a
        /// shared visited set, so a reference cycle terminates and a diamond
        /// (Root→A, Root→B, both→Common) does not re-walk Common.
        /// </summary>
        public static IEnumerable<GraphAssetSO> Flatten(GraphAssetSO root)
        {
            var visited = new HashSet<GraphAssetSO>();
            return FlattenInner(root, visited);
        }

        private static IEnumerable<GraphAssetSO> FlattenInner(GraphAssetSO graph, HashSet<GraphAssetSO> visited)
        {
            if (graph == null || !visited.Add(graph)) yield break;
            yield return graph;
            foreach (var (_, child) in DirectReferences(graph))
            {
                foreach (var sub in FlattenInner(child, visited))
                    yield return sub;
            }
        }

        /// <summary>
        /// True if the reference graph rooted at <paramref name="root"/> contains a cycle
        /// (a graph transitively reaching itself, including a direct self-reference).
        /// <paramref name="cyclePath"/> is the offending chain (first repeated graph →
        /// … → itself) when true, otherwise empty.
        /// </summary>
        public static bool HasReferenceCycle(GraphAssetSO root, out IReadOnlyList<GraphAssetSO> cyclePath)
        {
            var path = new List<GraphAssetSO>();
            var onPath = new HashSet<GraphAssetSO>();
            var done = new HashSet<GraphAssetSO>();
            var result = new List<GraphAssetSO>();

            bool found = Dfs(root, path, onPath, done, result);
            cyclePath = result; // populated only on a back-edge; empty when no cycle
            return found;
        }

        private static bool Dfs(
            GraphAssetSO graph,
            List<GraphAssetSO> path,
            HashSet<GraphAssetSO> onPath,
            HashSet<GraphAssetSO> done,
            List<GraphAssetSO> result)
        {
            if (graph == null) return false;

            if (onPath.Contains(graph))
            {
                // Back-edge: capture the chain from the first occurrence of `graph`
                // to the end of the current path, then close it on `graph`.
                int idx = path.IndexOf(graph);
                for (int i = idx; i < path.Count; i++) result.Add(path[i]);
                result.Add(graph);
                return true;
            }
            if (done.Contains(graph)) return false;

            path.Add(graph);
            onPath.Add(graph);
            foreach (var (_, child) in DirectReferences(graph))
            {
                if (Dfs(child, path, onPath, done, result)) return true;
            }
            path.RemoveAt(path.Count - 1);
            onPath.Remove(graph);
            done.Add(graph);
            return false;
        }
    }
}
