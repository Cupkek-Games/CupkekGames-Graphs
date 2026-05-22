using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Element-tracking half of <see cref="GraphCanvas"/>. Owns the
    /// rebuild path (clear-and-re-mount everything from the bound asset)
    /// plus the small Add / Find helpers. Lives in a partial so the
    /// methods keep direct access to the canvas's private dictionaries
    /// and the layer fields.
    /// </summary>
    public partial class GraphCanvas
    {
        void RebuildNodeElements()
        {
            // Selection references the old elements; clear before they vanish.
            Selection.Clear();

            foreach (var elem in _nodeElements.Values)
                elem.RemoveFromHierarchy();
            _nodeElements.Clear();

            foreach (var edge in _edgeElements.Values)
                edge.RemoveFromHierarchy();
            _edgeElements.Clear();

            foreach (var group in _groupElements.Values)
                group.RemoveFromHierarchy();
            _groupElements.Clear();

            if (Asset == null) return;

            foreach (var group in Asset.Groups)
                AddGroupElement(group);

            foreach (var node in Asset.Nodes)
                AddNodeElement(node);

            foreach (var conn in Asset.Connections)
                AddEdgeElement(conn);
        }

        void AddNodeElement(GraphNodeSO node)
        {
            if (node == null || _nodeElements.ContainsKey(node)) return;
            var element = CreateNodeElement(node);
            _nodeLayer.Add(element);
            _nodeElements[node] = element;

            // Domain graphs (nav, BT, dialogue) expose a start-node guid; paint
            // a coloured outline on the matching element so it's obvious which
            // node is the entry point.
            if (Asset != null && !string.IsNullOrEmpty(Asset.StartNodeGuid.ValueStr) && node.Guid == Asset.StartNodeGuid)
                element.SetIsStartNode(true);
        }

        /// <summary>
        /// Repaint the start-node outline. Call after the bound asset's
        /// <see cref="GraphAssetSO.StartNodeGuid"/> changes (e.g. via a domain
        /// inspector). Walks the live node elements; cheap for normal graphs.
        /// </summary>
        public void RefreshStartNodeOutline()
        {
            if (Asset == null) return;
            var startGuid = Asset.StartNodeGuid;
            bool hasStart = !string.IsNullOrEmpty(startGuid.ValueStr);
            foreach (var kvp in _nodeElements)
                kvp.Value.SetIsStartNode(hasStart && kvp.Key.Guid == startGuid);
        }

        /// <summary>
        /// Factory hook — domain canvases (e.g. a future BehaviourTreeCanvas)
        /// override to return their own NodeElement subclass for richer
        /// visuals. The default returns a <see cref="StickyNoteElement"/> for
        /// sticky notes and a plain <see cref="NodeElement"/> for everything
        /// else.
        /// </summary>
        protected virtual NodeElement CreateNodeElement(GraphNodeSO node)
        {
            if (node is StickyNoteNodeSO sticky)
                return new StickyNoteElement(this, sticky);
            return new NodeElement(this, node);
        }

        void AddGroupElement(GraphGroup group)
        {
            if (group == null || _groupElements.ContainsKey(group)) return;
            var element = new GroupElement(this, group);
            _groupLayer.Add(element);
            _groupElements[group] = element;
        }

        void AddEdgeElement(GraphConnection conn)
        {
            if (conn == null || _edgeElements.ContainsKey(conn)) return;
            if (Asset == null) return;

            var source = FindNodeByGuid(conn.SourceNodeGuid);
            var target = FindNodeByGuid(conn.TargetNodeGuid);
            if (source == null || target == null) return;

            NodeElement sourceElem = _nodeElements.TryGetValue(source, out var se) ? se : null;
            NodeElement targetElem = _nodeElements.TryGetValue(target, out var te) ? te : null;
            if (sourceElem == null || targetElem == null) return;

            var edge = new EdgeElement(conn, sourceElem, targetElem);
            _edgeLayer.Add(edge);
            _edgeElements[conn] = edge;
            // First Refresh runs once the node layouts settle and ports
            // know where they are; schedule on next frame to avoid using
            // NaN layout values during initial mount.
            schedule.Execute(() => edge.Refresh()).StartingIn(0);
        }

        GraphNodeSO FindNodeByGuid(SerializedGuid guid)
        {
            foreach (var n in Asset.Nodes)
                if (n != null && n.Guid == guid)
                    return n;
            return null;
        }

        /// <summary>
        /// Re-route every edge that starts or ends at <paramref name="node"/>.
        /// Called by <see cref="NodeDragManipulator"/> during drag.
        /// </summary>
        public void RefreshEdgesForNode(GraphNodeSO node)
        {
            if (node == null) return;
            foreach (var edge in _edgeElements.Values)
            {
                var c = edge.Connection;
                if (c.SourceNodeGuid == node.Guid || c.TargetNodeGuid == node.Guid)
                    edge.Refresh();
            }
        }
    }
}
