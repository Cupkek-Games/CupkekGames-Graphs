using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Arrange ops on the current node selection — frame-to-fit, align, and
    /// distribute. Lives in a partial so it shares the canvas's selection +
    /// view state. Wired to the <c>F</c> shortcut (frame) and the node context
    /// menu (align / distribute).
    /// </summary>
    public partial class GraphCanvas
    {
        public enum AlignEdge { Left, CenterX, Right, Top, CenterY, Bottom }

        /// <summary>
        /// Pan + zoom so the selected nodes fill the viewport (with padding).
        /// Never zooms past 1:1 — framing shows the set, not a close-up. Falls
        /// back to <see cref="ResetView"/> when nothing is selected.
        /// </summary>
        public void FrameSelection()
        {
            if (!TryGetSelectionBounds(out var min, out var max)) { ResetView(); return; }

            const float pad = 80f;
            float bw = (max.x - min.x) + pad * 2f;
            float bh = (max.y - min.y) + pad * 2f;
            float vw = layout.width, vh = layout.height;

            if (vw > 0f && vh > 0f && bw > 0f && bh > 0f)
            {
                float zoom = Mathf.Min(Mathf.Min(vw / bw, vh / bh), 1f);
                _viewZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            }
            CenterOn((min + max) * 0.5f); // applies the transform with the new zoom
        }

        bool TryGetSelectionBounds(out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var ne in Selection.Nodes)
            {
                if (ne?.Node == null) continue;
                any = true;
                Vector2 p = ne.Node.Position;
                Vector2 s = NodeSize(ne);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p + s);
            }
            return any;
        }

        static Vector2 NodeSize(NodeElement ne)
        {
            float w = ne.layout.width  > 0f && !float.IsNaN(ne.layout.width)  ? ne.layout.width  : (ne.Node != null ? ne.Node.PreferredWidth : 240f);
            float h = ne.layout.height > 0f && !float.IsNaN(ne.layout.height) ? ne.layout.height : 80f;
            return new Vector2(w, h);
        }

        /// <summary>Align the selected nodes (≥2) to a shared edge / center.</summary>
        public void AlignSelectedNodes(AlignEdge edge)
        {
            var nodes = SelectedNodesSnapshot();
            if (nodes.Count < 2) return;

            TryGetSelectionBounds(out var min, out var max);
            BeginArrange(nodes, "Align Nodes");
            foreach (var ne in nodes)
            {
                Vector2 p = ne.Node.Position;
                Vector2 s = NodeSize(ne);
                switch (edge)
                {
                    case AlignEdge.Left:    p.x = min.x; break;
                    case AlignEdge.Right:   p.x = max.x - s.x; break;
                    case AlignEdge.CenterX: p.x = (min.x + max.x) * 0.5f - s.x * 0.5f; break;
                    case AlignEdge.Top:     p.y = min.y; break;
                    case AlignEdge.Bottom:  p.y = max.y - s.y; break;
                    case AlignEdge.CenterY: p.y = (min.y + max.y) * 0.5f - s.y * 0.5f; break;
                }
                ne.SetWorldPosition(p);
                RefreshEdgesForNode(ne.Node);
            }
            EndArrange(nodes);
        }

        /// <summary>
        /// Even out spacing between selected nodes (≥3) along an axis — the node
        /// centers end up equally spaced between the first and last.
        /// </summary>
        public void DistributeSelectedNodes(bool horizontal)
        {
            var nodes = SelectedNodesSnapshot();
            if (nodes.Count < 3) return;

            nodes.Sort((a, b) => Center(a, horizontal).CompareTo(Center(b, horizontal)));

            float first = Center(nodes[0], horizontal);
            float last = Center(nodes[nodes.Count - 1], horizontal);
            float step = (last - first) / (nodes.Count - 1);

            BeginArrange(nodes, "Distribute Nodes");
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                var ne = nodes[i];
                Vector2 p = ne.Node.Position;
                Vector2 s = NodeSize(ne);
                float targetCenter = first + step * i;
                if (horizontal) p.x = targetCenter - s.x * 0.5f;
                else p.y = targetCenter - s.y * 0.5f;
                ne.SetWorldPosition(p);
                RefreshEdgesForNode(ne.Node);
            }
            EndArrange(nodes);
        }

        static float Center(NodeElement ne, bool horizontal)
        {
            Vector2 p = ne.Node.Position;
            Vector2 s = NodeSize(ne);
            return horizontal ? p.x + s.x * 0.5f : p.y + s.y * 0.5f;
        }

        List<NodeElement> SelectedNodesSnapshot()
        {
            var list = new List<NodeElement>();
            foreach (var ne in Selection.Nodes)
                if (ne?.Node != null) list.Add(ne);
            return list;
        }

        // Mirrors NodeDragManipulator: register the SOs for the undo group +
        // dirty them. (Node Position lives in the layout sidecar, so the move
        // itself isn't captured by Undo — same as drag — but the dirty flag
        // flips so Save writes the new layout.)
        void BeginArrange(List<NodeElement> nodes, string label)
        {
            var sos = new GraphNodeSO[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) sos[i] = nodes[i].Node;
            Undo.RegisterCompleteObjectUndo(sos, label);
        }

        void EndArrange(List<NodeElement> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
                EditorUtility.SetDirty(nodes[i].Node);
            RaiseGraphChanged();
        }
    }
}
