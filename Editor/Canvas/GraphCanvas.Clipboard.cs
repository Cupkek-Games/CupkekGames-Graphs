using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Clipboard half of <see cref="GraphCanvas"/> — Cut / Copy / Paste /
    /// Duplicate / Delete. Lives as a partial so all four ops keep direct
    /// access to the canvas's private element dictionaries and the
    /// <see cref="RaiseGraphChanged"/> notifier.
    /// </summary>
    public partial class GraphCanvas
    {
        /// <summary>Pixels to offset pasted nodes by so they don't sit exactly on top of the originals.</summary>
        public const float PasteOffsetPx = 24f;

        /// <summary>Serialise the selection to the system clipboard. No-op if empty.</summary>
        public bool Copy()
        {
            if (Asset == null || Selection.IsEmpty) return false;
            EditorGUIUtility.systemCopyBuffer = ClipboardSerializer.Serialize(Selection.Nodes, Asset.Connections);
            return true;
        }

        /// <summary>Cut = Copy + DeleteSelection.</summary>
        public bool Cut()
        {
            if (!Copy()) return false;
            return DeleteSelection();
        }

        /// <summary>
        /// Materialise whatever's on the system clipboard. Pasted nodes get
        /// fresh GUIDs and are offset from the originals; interior
        /// connections are reproduced. The new nodes become the selection.
        /// </summary>
        public bool Paste()
        {
            if (Asset == null) return false;
            string payload = EditorGUIUtility.systemCopyBuffer;
            if (!ClipboardSerializer.HasPayload(payload)) return false;

            var pasteOffset = new Vector2(PasteOffsetPx, PasteOffsetPx);
            var newNodes = ClipboardSerializer.Paste(payload, Asset, pasteOffset);
            if (newNodes.Count == 0) return false;

            RebuildNodeElements();

            // Select the freshly pasted nodes so the user can move them
            // immediately without re-selecting.
            foreach (var node in newNodes)
                if (_nodeElements.TryGetValue(node, out var ne))
                    Selection.Add(ne);

            RaiseGraphChanged();
            return true;
        }

        /// <summary>
        /// In-place duplicate — same semantics as Copy+Paste but does not
        /// touch the system clipboard, so duplicating doesn't clobber the
        /// user's existing copy buffer.
        /// </summary>
        public bool Duplicate()
        {
            if (Asset == null || Selection.IsEmpty) return false;

            string payload = ClipboardSerializer.Serialize(Selection.Nodes, Asset.Connections);
            var pasteOffset = new Vector2(PasteOffsetPx, PasteOffsetPx);
            var newNodes = ClipboardSerializer.Paste(payload, Asset, pasteOffset);
            if (newNodes.Count == 0) return false;

            RebuildNodeElements();

            foreach (var node in newNodes)
                if (_nodeElements.TryGetValue(node, out var ne))
                    Selection.Add(ne);

            RaiseGraphChanged();
            return true;
        }

        /// <summary>
        /// Remove every currently-selected node, edge, and group. Connections
        /// attached to a removed node are also dropped. Wrapped in a single
        /// Undo group so one Ctrl+Z restores the lot.
        /// </summary>
        public bool DeleteSelection()
        {
            if (Asset == null || Selection.IsEmpty) return false;

            var nodesToDelete = new List<GraphNodeSO>();
            foreach (var ne in Selection.Nodes)
                if (ne.Node != null) nodesToDelete.Add(ne.Node);

            var edgesToDelete = new List<GraphConnection>();
            foreach (var ed in Selection.Edges)
                if (ed.Connection != null) edgesToDelete.Add(ed.Connection);

            var groupsToDelete = new List<GraphGroup>();
            foreach (var g in Selection.Groups)
                if (g.Group != null) groupsToDelete.Add(g.Group);

            if (nodesToDelete.Count == 0 && edgesToDelete.Count == 0 && groupsToDelete.Count == 0)
                return false;

            Undo.RegisterCompleteObjectUndo(Asset, "Delete Selection");

            foreach (var node in nodesToDelete)
            {
                Asset.RemoveNode(node);
                Undo.DestroyObjectImmediate(node);
            }
            foreach (var conn in edgesToDelete)
                Asset.RemoveConnection(conn);
            foreach (var grp in groupsToDelete)
                Asset.RemoveGroup(grp);

            Selection.Clear();
            EditorUtility.SetDirty(Asset);

            RebuildNodeElements();
            RaiseGraphChanged();
            return true;
        }
    }
}
