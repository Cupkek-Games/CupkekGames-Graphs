using UnityEditor;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Shared read of the per-graph <b>layout sidecar</b> (<c>Foo.layout.asset</c>, a
    /// <see cref="GraphLayout"/> keyed by node guid). Node <c>Position</c> + <c>Collapsed</c>
    /// are <c>[NonSerialized]</c> on the nodes, so they live in this sidecar next to the
    /// graph asset rather than in it. Factored out of the editor window so the runtime
    /// debugger can position a live graph identically (load-only; the debugger never writes).
    /// </summary>
    public static class GraphLayoutIO
    {
        /// <summary>Sidecar path for <paramref name="asset"/>, or null when it has no asset path.</summary>
        public static string PathFor(GraphAssetSO asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : System.IO.Path.ChangeExtension(path, "layout.asset");
        }

        /// <summary>
        /// Copy positions + collapsed flags from <paramref name="layoutOwner"/>'s sidecar
        /// onto <paramref name="graph"/>'s nodes (matched by guid). The editor passes the
        /// working clone as <paramref name="graph"/> and the on-disk asset as
        /// <paramref name="layoutOwner"/>; the debugger passes the same live asset for both.
        /// Missing entries leave a node at its current position.
        /// </summary>
        public static void Apply(GraphAssetSO graph, GraphAssetSO layoutOwner)
        {
            if (graph == null) return;
            string path = PathFor(layoutOwner);
            if (string.IsNullOrEmpty(path)) return;
            var layout = AssetDatabase.LoadAssetAtPath<GraphLayout>(path);
            if (layout == null) return;

            foreach (var n in graph.Nodes)
            {
                if (n == null) continue;
                if (layout.TryGet(n.Guid.ValueStr, out var pos)) n.Position = pos;
                n.Collapsed = layout.IsCollapsed(n.Guid.ValueStr);
            }
        }
    }
}
