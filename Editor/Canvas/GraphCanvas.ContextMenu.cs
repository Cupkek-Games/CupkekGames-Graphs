using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Context-menu + node-create half of <see cref="GraphCanvas"/>.
    /// Owns the right-click menu populators (one branch for empty
    /// canvas, one for nodes), the node-search popup entry point, and
    /// the Create / Delete flows for nodes and groups. Lives in a
    /// partial so the methods keep direct access to private state and
    /// the <see cref="PopulateNodeContextMenu"/> virtual stays
    /// overridable by domain canvases.
    /// </summary>
    public partial class GraphCanvas
    {
        void OnPopulateContextMenu(ContextualMenuPopulateEvent evt)
        {
            if (Asset == null) return;

            // Remember the cursor for the popup; mousePosition is panel-space.
            Vector2 panelPos = evt.mousePosition;
            Vector2 worldPos = ScreenToWorld(this.WorldToLocal(panelPos));

            // Did the user right-click on a node element? Hit-test the
            // click position against every mounted NodeElement (more
            // reliable than walking evt.target — ContextualMenuManipulator
            // can dispatch with the canvas itself as the target when the
            // click lands on a non-picking descendant). If a node is hit,
            // surface node-scoped actions and skip the place-something-
            // here items (Add Node / Add Group only make sense on empty
            // canvas).
            var ne = FindNodeElementAtPanel(panelPos);
            if (ne != null)
            {
                PopulateNodeContextMenu(evt, ne);
                return;
            }

            // Edge hit-test runs after node — node bodies are larger and
            // sit on top of edges visually, so this ordering matches what
            // the user sees. Edge-scoped menu: edit/clear label + delete.
            var edge = FindEdgeElementAtPanel(panelPos);
            if (edge != null)
            {
                PopulateEdgeContextMenu(evt, edge, panelPos);
                return;
            }

            evt.menu.AppendAction("Add Node...   (Space)", _ => OpenNodeSearchAtPanel(panelPos));
            evt.menu.AppendAction("Add Group", _ => CreateAndAddGroup(worldPos));

            if (ClipboardSerializer.HasPayload(EditorGUIUtility.systemCopyBuffer))
                evt.menu.AppendAction("Paste   (Ctrl+V)", _ => Paste());

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Auto Layout", _ => AutoLayout());
            evt.menu.AppendAction("Fit View   (F)", _ => ResetView());
        }

        /// <summary>
        /// Append node-scoped actions to <paramref name="evt"/>'s menu —
        /// fires when the user right-clicks a <see cref="NodeElement"/>.
        /// Promotes the click-target node into the selection if it wasn't
        /// already there so the action runs on the expected set.
        /// <para>
        /// Domain canvases can <c>override</c> to insert domain-specific
        /// entries (e.g. a future "Set as Entry" item). Call <c>base</c>
        /// to keep Cut / Copy / Duplicate / Delete.
        /// </para>
        /// </summary>
        protected virtual void PopulateNodeContextMenu(ContextualMenuPopulateEvent evt, NodeElement target)
        {
            // Make the right-clicked node part of the selection if the
            // user hasn't picked it (or anything else). Multi-select
            // right-clicks operate on the whole set.
            if (!Selection.Contains(target) && Selection.IsEmpty)
                Selection.SetTo(target);

            evt.menu.AppendAction("Cut   (Ctrl+X)", _ => Cut(), Selection.IsEmpty ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("Copy   (Ctrl+C)", _ => Copy(), Selection.IsEmpty ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("Duplicate   (Ctrl+D)", _ => Duplicate(), Selection.IsEmpty ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

            if (ClipboardSerializer.HasPayload(EditorGUIUtility.systemCopyBuffer))
                evt.menu.AppendAction("Paste   (Ctrl+V)", _ => Paste());

            // Arrange — align (≥2 selected) / distribute (≥3) / frame.
            int selCount = Selection.Nodes.Count;
            if (selCount >= 2)
            {
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Align/Left",     _ => AlignSelectedNodes(AlignEdge.Left));
                evt.menu.AppendAction("Align/Center X",  _ => AlignSelectedNodes(AlignEdge.CenterX));
                evt.menu.AppendAction("Align/Right",     _ => AlignSelectedNodes(AlignEdge.Right));
                evt.menu.AppendAction("Align/Top",       _ => AlignSelectedNodes(AlignEdge.Top));
                evt.menu.AppendAction("Align/Center Y",  _ => AlignSelectedNodes(AlignEdge.CenterY));
                evt.menu.AppendAction("Align/Bottom",    _ => AlignSelectedNodes(AlignEdge.Bottom));
                if (selCount >= 3)
                {
                    evt.menu.AppendAction("Distribute/Horizontally", _ => DistributeSelectedNodes(true));
                    evt.menu.AppendAction("Distribute/Vertically",   _ => DistributeSelectedNodes(false));
                }
                evt.menu.AppendAction("Frame Selection   (F)", _ => FrameSelection());
            }

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Delete   (Del)", _ => DeleteSelection(), Selection.IsEmpty ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        }

        /// <summary>
        /// Hit-test every mounted <see cref="NodeElement"/> against a
        /// panel-space position. Used by the context-menu populator to
        /// distinguish a right-click on a node from a right-click on the
        /// empty canvas. Latest-mounted wins on overlap (matches the
        /// visual stacking order — node layer's draw order).
        /// </summary>
        NodeElement FindNodeElementAtPanel(Vector2 panelPos)
        {
            NodeElement hit = null;
            foreach (var ne in _nodeElements.Values)
            {
                if (ne == null) continue;
                Vector2 local = ne.WorldToLocal(panelPos);
                if (ne.ContainsPoint(local)) hit = ne;
            }
            return hit;
        }

        /// <summary>
        /// Hit-test every mounted <see cref="EdgeElement"/> against a
        /// panel-space position. <see cref="EdgeElement.ContainsPoint"/>
        /// samples the curve with a tolerance band so the user doesn't
        /// have to pixel-target the line. Last-mounted wins on overlap.
        /// </summary>
        EdgeElement FindEdgeElementAtPanel(Vector2 panelPos)
        {
            EdgeElement hit = null;
            foreach (var edge in _edgeElements.Values)
            {
                if (edge == null) continue;
                Vector2 local = edge.WorldToLocal(panelPos);
                if (edge.ContainsPoint(local)) hit = edge;
            }
            return hit;
        }

        /// <summary>
        /// Append edge-scoped actions to <paramref name="evt"/>'s menu —
        /// fires when the user right-clicks an <see cref="EdgeElement"/>.
        /// Includes label edit + delete. Domain canvases can override to
        /// add more (e.g. swap source/target on undirected graphs).
        /// </summary>
        protected virtual void PopulateEdgeContextMenu(ContextualMenuPopulateEvent evt, EdgeElement target, Vector2 panelPos)
        {
            // Find the connection's array index so the label popup can
            // mutate via SerializedProperty (Unity Undo handling).
            int idx = -1;
            for (int i = 0; i < Asset.Connections.Count; i++)
            {
                if (ReferenceEquals(Asset.Connections[i], target.Connection)) { idx = i; break; }
            }
            if (idx < 0) return;

            Vector2 screenPos = GUIUtility.GUIToScreenPoint(panelPos);
            bool hasLabel = !string.IsNullOrEmpty(target.Connection.Label);

            evt.menu.AppendAction(hasLabel ? "Edit Label..." : "Add Label...",
                _ => EdgeLabelPopup.Show(screenPos, Asset, idx, target.Refresh));

            if (hasLabel)
            {
                evt.menu.AppendAction("Clear Label", _ =>
                {
                    var so = new SerializedObject(Asset);
                    var connections = so.FindProperty("_connections");
                    if (idx >= 0 && idx < connections.arraySize)
                    {
                        connections.GetArrayElementAtIndex(idx)
                            .FindPropertyRelative("Label").stringValue = string.Empty;
                        so.ApplyModifiedProperties();
                        Asset.EditorRaiseMutated();
                        target.Refresh();
                    }
                });
            }

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Delete Edge", _ =>
            {
                Selection.SetTo(target);
                DeleteSelection();
            });
        }

        /// <summary>
        /// Open the node search popup at a panel-space position. Converts to
        /// screen space (where <see cref="EditorWindow.ShowAsDropDown"/> wants
        /// its rect) and to canvas world space (where the new node lands).
        /// </summary>
        public void OpenNodeSearchAtPanel(Vector2 panelPos)
        {
            if (Asset == null) return;

            Vector2 canvasLocal = this.WorldToLocal(panelPos);
            Vector2 worldPos = ScreenToWorld(canvasLocal);
            Vector2 screenPos = GUIUtility.GUIToScreenPoint(panelPos);

            NodeSearchPopup.Show(
                screenPos,
                Asset.NodeBaseType,
                allowStickyNote: true,
                onSelected: type => CreateAndAddNode(type, worldPos));
        }

        /// <summary>
        /// Spawn a new <see cref="GraphGroup"/> at the requested world
        /// position. Called from the right-click "Add Group" context menu.
        /// </summary>
        public void CreateAndAddGroup(Vector2 worldPos)
        {
            if (Asset == null) return;

            var group = new GraphGroup
            {
                Title = "Group",
                Bounds = new Rect(worldPos.x, worldPos.y, 320f, 200f),
            };

            Undo.RegisterCompleteObjectUndo(Asset, "Create Group");
            Asset.AddGroup(group);
            EditorUtility.SetDirty(Asset);

            AddGroupElement(group);
            RaiseGraphChanged();
        }

        /// <summary>
        /// Remove a group from the bound asset. Called by the group's "×"
        /// close button.
        /// </summary>
        public void DeleteGroup(GraphGroup group)
        {
            if (Asset == null || group == null) return;

            Undo.RegisterCompleteObjectUndo(Asset, "Delete Group");
            if (!Asset.RemoveGroup(group)) return;

            if (_groupElements.TryGetValue(group, out var element))
            {
                element.RemoveFromHierarchy();
                _groupElements.Remove(group);
            }

            EditorUtility.SetDirty(Asset);
            RaiseGraphChanged();
        }

        void CreateAndAddNode(Type nodeType, Vector2 worldPos)
        {
            if (Asset == null || nodeType == null) return;

            var node = (GraphNodeSO)ScriptableObject.CreateInstance(nodeType);
            node.name = nodeType.Name;
            node.Position = worldPos;
            // Asset is the editor's transient working copy. Node lives
            // alongside it as another transient SO; ApplyToOriginal will
            // promote new nodes to subassets on Save. HideInHierarchy +
            // DontSave (not HideAndDontSave) so Inspector fields stay
            // editable — HideAndDontSave bundles NotEditable.
            node.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

            Undo.RegisterCreatedObjectUndo(node, "Create Node");
            Undo.RecordObject(Asset, "Add Node");

            Asset.AddNode(node);

            EditorUtility.SetDirty(Asset);

            AddNodeElement(node);
            RaiseGraphChanged();
        }
    }
}
