using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// One-shot <b>group-aware</b> left-to-right tree layout for the bound graph.
    /// Roots (no inbound edge) and their subtrees are placed Reingold-Tilford style —
    /// children stacked top-to-bottom to the RIGHT of their parent — then independent
    /// trees stack top-to-bottom.
    ///
    /// <para>
    /// <b>Groups are honoured as clusters.</b> A node belongs (spatially) to the
    /// smallest <see cref="GraphGroup"/> whose box contains it. Each group's members are
    /// laid out <i>among themselves</i> first; the group then acts as a single sized
    /// <b>block</b> in the outer layout (its box is shrink-wrapped around the members).
    /// Edges fully inside a group drive only the inner layout; edges crossing a group
    /// boundary attach to the group block in the outer layout. This is a clustered
    /// (quotient-graph) tidy-tree — cleanest for unconnected clusters (e.g. a box of
    /// modals) or self-contained subtrees; a group that splits a connected subtree still
    /// lays out without error, just less tidily (cycle/multi-parent in the quotient is
    /// guarded — extras keep their place).
    /// </para>
    ///
    /// <para>
    /// Purely editor-side: writes node <c>Position</c> (sidecar) + group <c>Bounds</c>
    /// (asset, runtime-ignored). Membership is derived, never stored. Wrapped in one Undo
    /// group.
    /// </para>
    /// </summary>
    public static class AutoLayoutEngine
    {
        // Fallback card width when a node is null; real nodes use PreferredWidth.
        public const float NodeWidth = 200f;
        // Assumed card vertical footprint (~compact card height) so vertical spacing
        // isn't inflated by a phantom gap. Public: GraphCanvas reuses it so its spatial
        // membership test agrees with this engine's.
        public const float NodeHeight = 56f;
        public const float SiblingGap = 24f;                 // vertical, between stacked siblings
        public const float LevelGap = 80f;                   // horizontal, between depth levels
        public const float ComponentGap = SiblingGap * 2f;   // between independent trees / blocks

        // Insets that shrink-wrap a group box around its members.
        const float GroupPaddingX = 14f;
        const float GroupPaddingY = 12f;
        const float GroupTitleBar = 22f;   // matches GroupElement's title bar height

        /// <summary>
        /// Lay out <paramref name="asset"/>'s tree(s), clustering nodes by the groups
        /// that spatially contain them. Cyclic / unreachable items still get placed.
        /// </summary>
        public static void LayoutTree(GraphAssetSO asset)
        {
            if (asset == null || asset.Nodes.Count == 0) return;

            var byGuid = new Dictionary<string, GraphNodeSO>();
            foreach (var n in asset.Nodes)
                if (n != null) byGuid[n.Guid.ValueStr] = n;
            GraphNodeSO ByGuid(string guidStr) => byGuid.TryGetValue(guidStr, out var n) ? n : null;

            // ── Membership: each node → smallest group whose box contains its centre ──
            var groups = asset.Groups;
            var nodeGroup = new Dictionary<GraphNodeSO, GraphGroup>();
            if (groups != null && groups.Count > 0)
            {
                foreach (var n in asset.Nodes)
                {
                    if (n == null) continue;
                    Vector2 center = n.Position + new Vector2(Width(n), NodeHeight) * 0.5f;
                    GraphGroup best = null;
                    float bestArea = float.MaxValue;
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var g = groups[i];
                        if (g == null || !g.Bounds.Contains(center)) continue;
                        float area = g.Bounds.width * g.Bounds.height;
                        if (area < bestArea) { bestArea = area; best = g; }
                    }
                    if (best != null) nodeGroup[n] = best;
                }
            }

            // ── Partition into per-group members + free nodes (asset order) ──
            var groupMembers = new Dictionary<GraphGroup, List<GraphNodeSO>>();
            var freeNodes = new List<GraphNodeSO>();
            foreach (var n in asset.Nodes)
            {
                if (n == null) continue;
                if (nodeGroup.TryGetValue(n, out var g))
                {
                    if (!groupMembers.TryGetValue(g, out var list)) { list = new List<GraphNodeSO>(); groupMembers[g] = list; }
                    list.Add(n);
                }
                else freeNodes.Add(n);
            }

            // ── Inner layout per group → member relative positions + block size ──
            var groupSize = new Dictionary<GraphGroup, Vector2>();
            var memberRel = new Dictionary<GraphNodeSO, Vector2>();
            foreach (var kv in groupMembers)
            {
                var members = kv.Value;
                var memberItem = new Dictionary<GraphNodeSO, LayoutItem>();
                var memberItems = new List<LayoutItem>(members.Count);
                foreach (var m in members)
                {
                    var it = new LayoutItem { Node = m, Size = new Vector2(Width(m), NodeHeight) };
                    memberItem[m] = it;
                    memberItems.Add(it);
                }

                var memberSet = new HashSet<GraphNodeSO>(members);
                var childTmp = new Dictionary<LayoutItem, List<(int order, LayoutItem child)>>();
                foreach (var c in asset.Connections)
                {
                    var s = ByGuid(c.SourceNodeGuid.ValueStr);
                    var t = ByGuid(c.TargetNodeGuid.ValueStr);
                    if (s == null || t == null || !memberSet.Contains(s) || !memberSet.Contains(t)) continue;
                    var si = memberItem[s];
                    var ti = memberItem[t];
                    if (si == ti) continue;
                    Add(childTmp, si, c.OrderIndex, ti);
                }

                var roots = BuildForest(memberItems, ToOrderedMap(childTmp));
                LayoutForest(roots);

                Bbox(memberItems, out float minX, out float minY, out float maxX, out float maxY);
                Vector2 shift = new Vector2(GroupPaddingX - minX, (GroupTitleBar + GroupPaddingY) - minY);
                foreach (var m in members) memberRel[m] = memberItem[m].Pos + shift;
                groupSize[kv.Key] = new Vector2(
                    (maxX - minX) + GroupPaddingX * 2f,
                    (maxY - minY) + GroupTitleBar + GroupPaddingY * 2f);
            }

            // ── Outer items: free nodes + one block per non-empty group ──
            var itemOfNode = new Dictionary<GraphNodeSO, LayoutItem>();
            var outerItems = new List<LayoutItem>();
            foreach (var n in freeNodes)
            {
                var it = new LayoutItem { Node = n, Size = new Vector2(Width(n), NodeHeight) };
                itemOfNode[n] = it;
                outerItems.Add(it);
            }
            foreach (var kv in groupMembers)
            {
                if (kv.Value.Count == 0) continue;
                var it = new LayoutItem { Group = kv.Key, Size = groupSize[kv.Key] };
                outerItems.Add(it);
                foreach (var m in kv.Value) itemOfNode[m] = it; // members resolve to their block
            }
            if (outerItems.Count == 0) return;

            // ── Outer adjacency (edges remapped to items; group-internal edges dropped) ──
            var outerTmp = new Dictionary<LayoutItem, List<(int order, LayoutItem child)>>();
            foreach (var c in asset.Connections)
            {
                var s = ByGuid(c.SourceNodeGuid.ValueStr);
                var t = ByGuid(c.TargetNodeGuid.ValueStr);
                if (s == null || t == null) continue;
                if (!itemOfNode.TryGetValue(s, out var si) || !itemOfNode.TryGetValue(t, out var ti)) continue;
                if (si == ti) continue;
                Add(outerTmp, si, c.OrderIndex, ti);
            }

            var outerRoots = BuildForest(outerItems, ToOrderedMap(outerTmp));
            LayoutForest(outerRoots);

            // ── Resolve final positions (compute, then apply under one Undo) ──
            var finalNodePos = new Dictionary<GraphNodeSO, Vector2>();
            var finalGroupBounds = new Dictionary<GraphGroup, Rect>();
            foreach (var it in outerItems)
            {
                if (it.Node != null)
                {
                    finalNodePos[it.Node] = it.Pos;
                }
                else if (it.Group != null)
                {
                    finalGroupBounds[it.Group] = new Rect(it.Pos.x, it.Pos.y, it.Size.x, it.Size.y);
                    foreach (var m in groupMembers[it.Group])
                        finalNodePos[m] = it.Pos + memberRel[m];
                }
            }

            var undoObjects = new List<UnityEngine.Object>();
            foreach (var n in finalNodePos.Keys) if (n != null) undoObjects.Add(n);
            undoObjects.Add(asset); // group bounds are serialized on the asset
            Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), "Auto Layout");

            foreach (var kv in finalNodePos)
            {
                kv.Key.Position = kv.Value;
                EditorUtility.SetDirty(kv.Key);
            }
            foreach (var kv in finalGroupBounds) kv.Key.Bounds = kv.Value;
            EditorUtility.SetDirty(asset);
        }

        // ---------------------------------------------------------------
        // Tidy-tree over generic items (a node, a group member, or a group block)
        // ---------------------------------------------------------------

        class LayoutItem
        {
            public GraphNodeSO Node;   // a free node or a group member
            public GraphGroup Group;   // a group block (outer layout only)
            public Vector2 Size;
            public readonly List<LayoutItem> Children = new List<LayoutItem>();
            public float Breadth;      // vertical extent of this subtree
            public Vector2 Pos;        // top-left, set by Place
        }

        static float Width(GraphNodeSO n) => n != null ? n.PreferredWidth : NodeWidth;

        static void Add(Dictionary<LayoutItem, List<(int, LayoutItem)>> map, LayoutItem parent, int order, LayoutItem child)
        {
            if (!map.TryGetValue(parent, out var list)) { list = new List<(int, LayoutItem)>(); map[parent] = list; }
            list.Add((order, child));
        }

        static Dictionary<LayoutItem, List<LayoutItem>> ToOrderedMap(Dictionary<LayoutItem, List<(int order, LayoutItem child)>> tmp)
        {
            var map = new Dictionary<LayoutItem, List<LayoutItem>>();
            foreach (var kv in tmp)
            {
                kv.Value.Sort((a, b) => a.order.CompareTo(b.order));
                var list = new List<LayoutItem>(kv.Value.Count);
                foreach (var (_, child) in kv.Value) list.Add(child);
                map[kv.Key] = list;
            }
            return map;
        }

        // Build a forest: real roots (no inbound) first, then any item still unvisited
        // (a cycle with no root) as its own root — so nothing is lost. Children are
        // assigned from the ordered map with a visited guard (first parent wins on a
        // multi-parent item — the quotient can introduce those).
        static List<LayoutItem> BuildForest(List<LayoutItem> items, Dictionary<LayoutItem, List<LayoutItem>> childrenMap)
        {
            var hasParent = new HashSet<LayoutItem>();
            foreach (var kv in childrenMap)
                foreach (var c in kv.Value)
                    hasParent.Add(c);

            var visited = new HashSet<LayoutItem>();
            var roots = new List<LayoutItem>();
            foreach (var it in items)
                if (!hasParent.Contains(it))
                {
                    var built = BuildSubtree(it, childrenMap, visited);
                    if (built != null) roots.Add(built);
                }
            foreach (var it in items)
                if (!visited.Contains(it))
                {
                    var built = BuildSubtree(it, childrenMap, visited);
                    if (built != null) roots.Add(built);
                }
            return roots;
        }

        static LayoutItem BuildSubtree(LayoutItem item, Dictionary<LayoutItem, List<LayoutItem>> childrenMap, HashSet<LayoutItem> visited)
        {
            if (!visited.Add(item)) return null;
            item.Children.Clear();
            if (childrenMap.TryGetValue(item, out var kids))
                foreach (var c in kids)
                {
                    var built = BuildSubtree(c, childrenMap, visited);
                    if (built != null) item.Children.Add(built);
                }
            return item;
        }

        static void LayoutForest(List<LayoutItem> roots)
        {
            foreach (var r in roots) ComputeBreadth(r);
            float cursorY = 0f;
            foreach (var r in roots)
            {
                Place(r, cursorY + r.Breadth * 0.5f, 0f);
                cursorY += r.Breadth + ComponentGap;
            }
        }

        static float ComputeBreadth(LayoutItem n)
        {
            if (n.Children.Count == 0) { n.Breadth = n.Size.y; return n.Breadth; }
            float total = 0f;
            foreach (var c in n.Children) total += ComputeBreadth(c);
            total += (n.Children.Count - 1) * SiblingGap;
            n.Breadth = Mathf.Max(n.Size.y, total);
            return n.Breadth;
        }

        // Parent at (leftX, centerY); children stacked vertically, centered on centerY,
        // one level to the RIGHT (advance by THIS item's width — a group block is wide).
        static void Place(LayoutItem n, float centerY, float leftX)
        {
            n.Pos = new Vector2(leftX, centerY - n.Size.y * 0.5f);
            if (n.Children.Count == 0) return;

            float totalChild = 0f;
            foreach (var c in n.Children) totalChild += c.Breadth;
            totalChild += (n.Children.Count - 1) * SiblingGap;

            float childX = leftX + n.Size.x + LevelGap;
            float cursorY = centerY - totalChild * 0.5f;
            foreach (var c in n.Children)
            {
                Place(c, cursorY + c.Breadth * 0.5f, childX);
                cursorY += c.Breadth + SiblingGap;
            }
        }

        static void Bbox(List<LayoutItem> items, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            foreach (var it in items)
            {
                minX = Mathf.Min(minX, it.Pos.x);
                minY = Mathf.Min(minY, it.Pos.y);
                maxX = Mathf.Max(maxX, it.Pos.x + it.Size.x);
                maxY = Mathf.Max(maxY, it.Pos.y + it.Size.y);
            }
        }
    }
}
