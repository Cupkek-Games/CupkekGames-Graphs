using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Drag-to-connect half of <see cref="GraphCanvas"/>. Owns the live
    /// preview edge during a port drag, the validation hook for
    /// connection-shape rules, and the commit path that materialises a
    /// real <see cref="GraphConnection"/>. Subclasses override
    /// <see cref="CanConnect"/> to enforce domain rules (BT no-cycles,
    /// dialogue port-type compat, etc.).
    /// </summary>
    public partial class GraphCanvas
    {
        EdgePreview _activePreview;

        /// <summary>
        /// Convert a panel-space position (typically <c>evt.position</c>) to
        /// canvas world coordinates. Same coordinate space nodes use for
        /// their <see cref="GraphNodeSO.Position"/>.
        /// </summary>
        public Vector2 PanelToCanvasWorld(Vector2 panelPos)
        {
            return _content.WorldToLocal(panelPos);
        }

        /// <summary>Called by <see cref="PortDragManipulator"/> on PointerDown.</summary>
        public EdgePreview BeginConnectionPreview(PortElement sourcePort)
        {
            if (sourcePort == null) return null;
            if (_activePreview != null) CancelConnectionPreview();

            _activePreview = new EdgePreview(sourcePort);
            _edgeLayer.Add(_activePreview);
            return _activePreview;
        }

        /// <summary>
        /// Called by <see cref="PortDragManipulator"/> on PointerUp — looks
        /// for a valid drop target under <paramref name="panelPos"/> and, if
        /// one is found, creates the connection. Always clears the preview.
        /// </summary>
        public void CompleteConnectionPreview(PortElement sourcePort, Vector2 panelPos)
        {
            try
            {
                if (sourcePort == null || _activePreview == null) return;

                PortElement target = FindInputPortAtPanelPosition(panelPos);
                if (target == null) return;
                if (target.OwnerNode == sourcePort.OwnerNode) return; // no self-edges
                if (!CanConnect(sourcePort, target)) return;

                CreateConnection(sourcePort, target);
            }
            finally
            {
                CancelConnectionPreview();
            }
        }

        public void CancelConnectionPreview()
        {
            if (_activePreview == null) return;
            _activePreview.RemoveFromHierarchy();
            _activePreview = null;
        }

        /// <summary>
        /// Connection-shape validation. Subclasses of <see cref="GraphCanvas"/>
        /// override to enforce graph-specific rules (no cycles in BT, no
        /// duplicate edges, port-type compatibility, ...). Default: allow if
        /// the target's port is multi-capacity, or single-capacity with no
        /// existing inbound edge.
        /// </summary>
        protected virtual bool CanConnect(PortElement source, PortElement target)
        {
            if (Asset == null) return false;

            if (target.PortDef != null && target.PortDef.Capacity == PortCapacity.Single)
            {
                foreach (var c in Asset.Connections)
                {
                    if (c.TargetNodeGuid == target.OwnerNode.Node.Guid
                        && c.TargetPortId == target.PortId)
                        return false; // already occupied
                }
            }

            return true;
        }

        void CreateConnection(PortElement source, PortElement target)
        {
            if (Asset == null) return;

            int orderIndex = 0;
            if (source.PortDef != null && source.PortDef.Capacity == PortCapacity.Multi)
            {
                foreach (var c in Asset.Connections)
                {
                    if (c.SourceNodeGuid == source.OwnerNode.Node.Guid
                        && c.SourcePortId == source.PortId)
                        orderIndex = Mathf.Max(orderIndex, c.OrderIndex + 1);
                }
            }

            var conn = new GraphConnection
            {
                SourceNodeGuid = source.OwnerNode.Node.Guid,
                TargetNodeGuid = target.OwnerNode.Node.Guid,
                SourcePortId = source.PortId,
                TargetPortId = target.PortId,
                OrderIndex = orderIndex,
            };

            Undo.RegisterCompleteObjectUndo(Asset, "Connect Nodes");
            Asset.AddConnection(conn);
            EditorUtility.SetDirty(Asset);

            AddEdgeElement(conn);
            RaiseGraphChanged();
        }

        PortElement FindInputPortAtPanelPosition(Vector2 panelPos)
        {
            foreach (var ne in _nodeElements.Values)
            {
                if (ne == null) continue;
                foreach (var port in ne.InputPorts)
                {
                    if (port == null) continue;
                    Vector2 portLocal = port.WorldToLocal(panelPos);
                    if (port.ContainsPoint(portLocal))
                        return port;
                }
            }
            return null;
        }
    }
}
