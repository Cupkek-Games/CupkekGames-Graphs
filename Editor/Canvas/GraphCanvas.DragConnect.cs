using System;
using CupkekGames.Data.Primitives;
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
        /// True while a connection / reroute drag is live (a preview edge is
        /// being dragged). Elements gate their idle hover on this so nothing
        /// lights up under the cursor while the user is actively wiring.
        /// </summary>
        public bool IsConnectionDragActive => _activePreview != null;

        /// <summary>
        /// Input port currently showing the green "compatible drop target"
        /// affordance during a drag. Tracked so each pointer move can clear
        /// the previous candidate before lighting the new one (and so the
        /// drag end has a single place to clear it).
        /// </summary>
        PortElement _dropCandidate;

        /// <summary>
        /// Magnetic-snap hit radius (panel-space px). A drag whose pointer
        /// is within this distance of a compatible input port's center snaps
        /// the preview edge to that port — wider than the port's own ~12px
        /// footprint so the connection "wants" to land.
        /// </summary>
        const float SnapRadius = 28f;

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
        /// Called on PointerUp — looks for a valid drop target under
        /// <paramref name="panelPos"/> and, if one is found, creates the
        /// connection. <paramref name="anchorPort"/> is the fixed end of the
        /// drag (an output for a new-connection / target-reroute drag, an
        /// input for a source-reroute drag); the drop seeks a compatible
        /// port of the OPPOSITE kind. Always clears the preview.
        /// </summary>
        public void CompleteConnectionPreview(PortElement anchorPort, Vector2 panelPos)
        {
            try
            {
                if (anchorPort == null || _activePreview == null) return;

                // Use the SAME nearest-compatible search the move-time affordance
                // and magnetic snap use, so a drop lands wherever the green
                // "armed" target showed — anywhere within SnapRadius, not just
                // the port's ~12px footprint (otherwise the 12–28px band would
                // show an armed, snapped preview that releases into nothing).
                // FindNearestCompatiblePort already excludes the anchor's own
                // node and applies CanConnect, so a non-null result is valid.
                PortElement candidate = FindNearestCompatiblePort(anchorPort, panelPos, out _);
                if (candidate != null)
                {
                    // Always materialise output -> input regardless of which
                    // end the drag was anchored to.
                    PortElement source = anchorPort.IsInput ? candidate : anchorPort;
                    PortElement target = anchorPort.IsInput ? anchorPort : candidate;
                    CreateConnection(source, target);
                }
                else if (FindNodeElementAtPanel(panelPos) == null)
                {
                    // Released on EMPTY canvas (no compatible port within the
                    // snap radius, not over a node) — the universal graph
                    // gesture: open the node-search popup here and wire whatever
                    // the user creates to the dragged port.
                    OpenCreateConnectedNodePopup(anchorPort, panelPos);
                }
            }
            finally
            {
                CancelConnectionPreview();
            }
        }

        /// <summary>
        /// Drag-to-create: a wire dropped on empty canvas opens the node-search
        /// popup at the cursor; choosing a type spawns that node at the drop
        /// point and connects it to <paramref name="anchorPort"/> (output anchor
        /// → the new node's input; input anchor → its output). The ubiquitous
        /// "pull a wire into space to make a connected node" gesture.
        /// </summary>
        void OpenCreateConnectedNodePopup(PortElement anchorPort, Vector2 panelPos)
        {
            if (Asset == null || anchorPort?.OwnerNode?.Node == null) return;

            // Capture the drop point (canvas world) + the popup anchor (screen)
            // now — the popup resolves after the preview is torn down. The
            // anchor PortElement stays mounted on its node, so keep the ref.
            Vector2 dropWorld = PanelToCanvasWorld(panelPos);
            Vector2 screenPos = GUIUtility.GUIToScreenPoint(panelPos);
            PortElement anchor = anchorPort;

            NodeSearchPopup.Show(
                screenPos,
                Asset.NodeBaseType,
                allowStickyNote: false, // a sticky note has no ports to wire
                onSelected: type => CreateConnectedNode(type, anchor, dropWorld));
        }

        /// <summary>
        /// Spawn <paramref name="nodeType"/> at <paramref name="dropWorld"/> and
        /// wire it to <paramref name="anchor"/>: an output anchor connects to the
        /// new node's first input (downstream); an input anchor connects to the
        /// new node's first output (upstream). The node is still created if it
        /// has no matching port — just left unconnected.
        /// </summary>
        void CreateConnectedNode(Type nodeType, PortElement anchor, Vector2 dropWorld)
        {
            if (Asset == null || nodeType == null || anchor?.OwnerNode?.Node == null) return;

            // Spawn at the drop point, nudged up so the cursor sits near the
            // node's port row rather than its top edge.
            var node = CreateAndAddNode(nodeType, new Vector2(dropWorld.x, dropWorld.y - 18f));
            if (node == null) return;
            if (!_nodeElements.TryGetValue(node, out var ne) || ne == null) return;

            // For an input anchor the new node is the SOURCE (its output wires
            // back to the anchor), so put its RIGHT edge near the cursor.
            if (anchor.IsInput)
            {
                node.Position = new Vector2(node.Position.x - node.PreferredWidth, node.Position.y);
                ne.ApplyPosition();
            }

            // The drag seeks the OPPOSITE kind of port to the anchor.
            PortElement newPort = anchor.IsInput
                ? (ne.OutputPorts.Count > 0 ? ne.OutputPorts[0] : null)
                : (ne.InputPorts.Count > 0 ? ne.InputPorts[0] : null);
            if (newPort == null) return; // created, nothing to wire to

            PortElement source = anchor.IsInput ? newPort : anchor;
            PortElement target = anchor.IsInput ? anchor : newPort;
            if (CanConnect(source, target))
                CreateConnection(source, target);
        }

        public void CancelConnectionPreview()
        {
            if (_activePreview == null) return;
            _activePreview.RemoveFromHierarchy();
            _activePreview = null;
        }

        /// <summary>
        /// Called by <see cref="PortDragManipulator"/> on every PointerMove
        /// during a drag. Finds the nearest input port within
        /// <see cref="SnapRadius"/> of <paramref name="panelPos"/> and, using
        /// the SAME gates as the drop (target != null, not the source's own
        /// node, <see cref="CanConnect"/>), lights it as a compatible drop
        /// target. A non-null-but-incompatible port directly under the
        /// pointer instead shows the red "incompatible" affordance. The
        /// candidate is diffed against the previous one so only the changed
        /// ports repaint, and — when a compatible candidate exists — the
        /// preview edge's end is magnetically routed to that port's center.
        /// </summary>
        /// <returns>
        /// The world-space (content-layer) point the preview edge should
        /// terminate at: the snapped port center when a compatible candidate
        /// is found, otherwise <paramref name="fallbackWorld"/> (the raw
        /// cursor position).
        /// </returns>
        public Vector2 UpdateDropCandidate(PortElement anchor, Vector2 panelPos, Vector2 fallbackWorld)
        {
            if (anchor == null || _activePreview == null)
            {
                ClearDropCandidate();
                return fallbackWorld;
            }

            // Decide which port (if any) gets an affordance this move, and
            // which flavor. The drag seeks the OPPOSITE kind of port to the
            // anchor (output anchor -> inputs; input anchor -> outputs):
            //  • nearest COMPATIBLE port within the magnetic radius -> green
            //    candrop + snap the edge to it. Same gates as the drop
            //    (candidate != null, not the anchor's own node, CanConnect)
            //    so move-time and drop-time always agree.
            //  • else, an opposite-kind port directly under the pointer that
            //    fails those gates -> red incompatible hint, no snap.
            PortElement compatible = FindNearestCompatiblePort(anchor, panelPos, out _);

            PortElement affordedPort;
            PortElement.DropState affordedState;
            bool snap;

            if (compatible != null)
            {
                affordedPort = compatible;
                affordedState = PortElement.DropState.CanDrop;
                snap = true;
            }
            else
            {
                PortElement under = FindPortAtPanelPosition(panelPos, !anchor.IsInput);
                if (under != null && under.OwnerNode != anchor.OwnerNode)
                {
                    affordedPort = under;
                    affordedState = PortElement.DropState.Incompatible;
                }
                else
                {
                    affordedPort = null;
                    affordedState = PortElement.DropState.None;
                }
                snap = false;
            }

            // Diff against the previously afforded port: clear the stale one,
            // then light the new one. _dropCandidate always holds whichever
            // port currently carries a non-None affordance (candrop OR
            // incompatible), so EndDrag's single ClearDropCandidate() is enough.
            if (_dropCandidate != affordedPort)
            {
                _dropCandidate?.SetDropState(PortElement.DropState.None);
                _dropCandidate = affordedPort;
            }
            affordedPort?.SetDropState(affordedState);

            // Magnetic snap: route the preview endpoint to the port center.
            return snap ? PortCenterWorld(compatible) : fallbackWorld;
        }

        /// <summary>
        /// Clear any active drop-target affordance. Single clear site for
        /// drop / cancel / capture-loss — called from
        /// <see cref="PortDragManipulator.EndDrag"/>.
        /// </summary>
        public void ClearDropCandidate()
        {
            if (_dropCandidate == null) return;
            _dropCandidate.SetDropState(PortElement.DropState.None);
            _dropCandidate = null;
        }

        /// <summary>
        /// Compare two port ids treating null and empty as the same id.
        /// Anonymous ports carry a null id (see <see cref="GraphPortDef"/>), but
        /// Unity serializes a null string as "", so a connection that has been
        /// saved and reloaded comes back with PortId "" while a freshly created
        /// one — and the live port's <see cref="PortElement.PortId"/> — is still
        /// null. Matching with a plain <c>==</c> therefore misses on every
        /// loaded edge (detach / reroute / occupancy) but hits on new ones. This
        /// normalises both sides so old and new edges behave identically.
        /// </summary>
        static bool SamePortId(string a, string b)
            => string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : a == b;

        /// <summary>
        /// When true, the base <see cref="CanConnect"/> blocks any edge that
        /// would close a cycle (via <see cref="GraphTopology.WouldCreateCycle"/>),
        /// keeping the graph acyclic. Default false. Forest/tree domains (nav
        /// containment, behaviour-trees) override to true instead of hand-rolling
        /// their own reachability walk.
        /// </summary>
        protected virtual bool EnforceAcyclic => false;

        /// <summary>
        /// Connection-shape validation. Subclasses of <see cref="GraphCanvas"/>
        /// override to enforce graph-specific rules (port-type compatibility,
        /// domain sink rules, ...) — but acyclicity is built in via
        /// <see cref="EnforceAcyclic"/>. Default: allow if the target's port is
        /// multi-capacity, or single-capacity with no existing inbound edge.
        /// </summary>
        protected virtual bool CanConnect(PortElement source, PortElement target)
        {
            if (Asset == null) return false;

            if (target.PortDef != null && target.PortDef.Capacity == PortCapacity.Single)
            {
                foreach (var c in Asset.Connections)
                {
                    if (c.TargetNodeGuid == target.OwnerNode.Node.Guid
                        && SamePortId(c.TargetPortId, target.PortId))
                        return false; // already occupied
                }
            }

            // Acyclic opt-in: block an edge whose target can already reach its
            // source (the new edge would close a loop). Replaces the per-domain
            // WouldCreateCycle copies in NavGraphCanvas / BehaviourTreeCanvas.
            if (EnforceAcyclic
                && source?.OwnerNode?.Node != null && target?.OwnerNode?.Node != null
                && GraphTopology.WouldCreateCycle(Asset, source.OwnerNode.Node.Guid, target.OwnerNode.Node.Guid))
                return false;

            return true;
        }

        /// <summary>
        /// Direction-agnostic <see cref="CanConnect"/>. Orders
        /// (<paramref name="anchor"/>, <paramref name="candidate"/>) into
        /// (output source, input target) before delegating, so a reroute drag
        /// anchored at either end validates against the same rule.
        /// </summary>
        bool CanConnectOrdered(PortElement anchor, PortElement candidate)
        {
            PortElement source = anchor.IsInput ? candidate : anchor;
            PortElement target = anchor.IsInput ? anchor : candidate;
            return CanConnect(source, target);
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
                        && SamePortId(c.SourcePortId, source.PortId))
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

        /// <summary>
        /// Drag-to-detach support. Removes the connection currently landing on
        /// <paramref name="inputPort"/> (a single-capacity input has at most
        /// one; for multi-capacity the first match is taken) and returns the
        /// source OUTPUT <see cref="PortElement"/> it came from — so a drag
        /// started on a connected input port can continue from that source:
        /// re-dropping on another input reconnects, releasing in empty space
        /// leaves the wire removed. Returns null when nothing is connected, so
        /// an unconnected input stays a pure drop target.
        /// </summary>
        public PortElement DetachConnectionAt(PortElement inputPort)
        {
            if (Asset == null || inputPort == null || inputPort.OwnerNode == null) return null;

            var targetGuid = inputPort.OwnerNode.Node.Guid;
            var targetPortId = inputPort.PortId;

            GraphConnection match = null;
            foreach (var c in Asset.Connections)
            {
                if (c.TargetNodeGuid == targetGuid && SamePortId(c.TargetPortId, targetPortId))
                {
                    match = c;
                    break;
                }
            }
            if (match == null) return null;

            // Resolve the source output port BEFORE removing the connection.
            PortElement sourcePort = FindOutputPort(match.SourceNodeGuid, match.SourcePortId);
            DetachConnection(match, removeEdgeElement: true);
            return sourcePort;
        }

        /// <summary>
        /// True if <paramref name="inputPort"/> currently has any inbound
        /// connection. Backs the port-hover differentiation — a connected input
        /// is a detach/reroute handle, an unconnected input is a passive drop
        /// target. Mirrors the match logic in <see cref="DetachConnectionAt"/>.
        /// </summary>
        public bool IsInputConnected(PortElement inputPort)
        {
            if (Asset == null || inputPort == null || inputPort.OwnerNode == null) return false;

            var targetGuid = inputPort.OwnerNode.Node.Guid;
            var targetPortId = inputPort.PortId;
            foreach (var c in Asset.Connections)
            {
                if (c.TargetNodeGuid == targetGuid && SamePortId(c.TargetPortId, targetPortId))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Detach an existing connection for a wire-drag reroute, returning the
        /// port that stays ANCHORED while the loose end is dragged. Grabbing
        /// the connection's target/input half keeps the source OUTPUT port (so
        /// the loose end re-targets a new input); grabbing the source/output
        /// half keeps the target INPUT port (so the loose end re-sources a new
        /// output). Returns null if the kept port isn't currently mounted —
        /// the connection is then left intact.
        ///
        /// The connection's <see cref="EdgeElement"/> is intentionally NOT
        /// removed from the hierarchy: the dragging edge owns the pointer
        /// capture, so it must stay in the panel for the duration. The caller
        /// hides it during the drag and disposes it on release.
        /// </summary>
        public PortElement DetachConnectionForReroute(GraphConnection conn, bool grabbedInputHalf)
        {
            if (Asset == null || conn == null) return null;

            PortElement anchor = grabbedInputHalf
                ? FindOutputPort(conn.SourceNodeGuid, conn.SourcePortId)
                : FindInputPort(conn.TargetNodeGuid, conn.TargetPortId);
            if (anchor == null) return null;

            DetachConnection(conn, removeEdgeElement: false);
            return anchor;
        }

        /// <summary>
        /// Shared removal core for the detach paths (undo + edge cleanup).
        /// <paramref name="removeEdgeElement"/> is false for an edge-initiated
        /// reroute, where the dragging edge keeps itself alive for its pointer
        /// capture and removes itself when the drag ends.
        /// </summary>
        void DetachConnection(GraphConnection conn, bool removeEdgeElement)
        {
            Undo.RegisterCompleteObjectUndo(Asset, "Detach Connection");
            Asset.RemoveConnection(conn);
            EditorUtility.SetDirty(Asset);

            if (_edgeElements.TryGetValue(conn, out var edge))
            {
                if (removeEdgeElement) edge.RemoveFromHierarchy();
                _edgeElements.Remove(conn);
            }

            RaiseGraphChanged();
        }

        /// <summary>
        /// Live output <see cref="PortElement"/> for
        /// (<paramref name="nodeGuid"/>, <paramref name="portId"/>), or null if
        /// the node / port is not currently mounted.
        /// </summary>
        PortElement FindOutputPort(SerializedGuid nodeGuid, string portId)
        {
            var node = FindNodeByGuid(nodeGuid);
            if (node == null) return null;
            if (!_nodeElements.TryGetValue(node, out var ne) || ne == null) return null;
            foreach (var port in ne.OutputPorts)
            {
                if (port != null && SamePortId(port.PortId, portId))
                    return port;
            }
            return null;
        }

        /// <summary>
        /// Live input <see cref="PortElement"/> for
        /// (<paramref name="nodeGuid"/>, <paramref name="portId"/>), or null if
        /// the node / port is not currently mounted.
        /// </summary>
        PortElement FindInputPort(SerializedGuid nodeGuid, string portId)
        {
            var node = FindNodeByGuid(nodeGuid);
            if (node == null) return null;
            if (!_nodeElements.TryGetValue(node, out var ne) || ne == null) return null;
            foreach (var port in ne.InputPorts)
            {
                if (port != null && SamePortId(port.PortId, portId))
                    return port;
            }
            return null;
        }

        /// <summary>
        /// Port of the requested kind (<paramref name="wantInput"/>) directly
        /// under <paramref name="panelPos"/>, or null. Used for the red
        /// "incompatible" hint when the cursor is over a port that fails the
        /// connect gates.
        /// </summary>
        PortElement FindPortAtPanelPosition(Vector2 panelPos, bool wantInput)
        {
            foreach (var ne in _nodeElements.Values)
            {
                if (ne == null) continue;
                var ports = wantInput ? ne.InputPorts : ne.OutputPorts;
                foreach (var port in ports)
                {
                    if (port == null) continue;
                    Vector2 portLocal = port.WorldToLocal(panelPos);
                    if (port.ContainsPoint(portLocal))
                        return port;
                }
            }
            return null;
        }

        /// <summary>
        /// Nearest opposite-kind port (input when <paramref name="anchor"/> is
        /// an output, output when it's an input) whose center lies within
        /// <see cref="SnapRadius"/> of <paramref name="panelPos"/> that the
        /// drag can legally connect to — same gates as the drop (not the
        /// anchor's own node, <see cref="CanConnectOrdered"/> passes). Ranks by
        /// distance so a dense cluster snaps to the closest target. Returns
        /// null if none qualify. <paramref name="distance"/> is the panel-space
        /// distance to the winner (NaN when none found).
        /// </summary>
        PortElement FindNearestCompatiblePort(PortElement anchor, Vector2 panelPos, out float distance)
        {
            bool wantInput = !anchor.IsInput;
            PortElement best = null;
            float bestSqr = SnapRadius * SnapRadius;
            distance = float.NaN;

            foreach (var ne in _nodeElements.Values)
            {
                if (ne == null) continue;
                if (ne == anchor.OwnerNode) continue; // no self-edges
                var ports = wantInput ? ne.InputPorts : ne.OutputPorts;
                foreach (var port in ports)
                {
                    if (port == null) continue;
                    if (!CanConnectOrdered(anchor, port)) continue;

                    Vector2 center = port.worldBound.center;
                    float sqr = (center - panelPos).sqrMagnitude;
                    if (sqr <= bestSqr)
                    {
                        bestSqr = sqr;
                        best = port;
                    }
                }
            }

            if (best != null) distance = Mathf.Sqrt(bestSqr);
            return best;
        }

        /// <summary>
        /// Center of <paramref name="port"/> in content-layer world coords —
        /// the space the preview edge end is expressed in (see
        /// <see cref="PanelToCanvasWorld"/> / <see cref="EdgePreview.SetEndWorld"/>).
        /// Routes the magnetic-snap endpoint straight to the port so the
        /// preview "clicks" onto the target.
        /// </summary>
        Vector2 PortCenterWorld(PortElement port)
        {
            return PanelToCanvasWorld(port.worldBound.center);
        }
    }
}
