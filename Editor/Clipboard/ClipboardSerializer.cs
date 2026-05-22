using System;
using System.Collections.Generic;
using CupkekGames.Data.Primitives;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Serialise a selection of nodes + their interior connections to a
    /// JSON envelope that lives in <see cref="EditorGUIUtility.systemCopyBuffer"/>.
    /// Paste deserialises, mints fresh GUIDs, offsets positions, and
    /// materialises the new subassets on the target graph.
    /// </summary>
    public static class ClipboardSerializer
    {
        const string Prefix = "cgg-graphs/v1:";

        [Serializable]
        public class Envelope
        {
            public int version = 1;
            public List<NodeEntry> nodes = new List<NodeEntry>();
            public List<ConnectionEntry> connections = new List<ConnectionEntry>();
        }

        [Serializable]
        public class NodeEntry
        {
            public string typeName;        // assembly-qualified
            public string guid;            // original guid (used to wire connections)
            public float  positionX;
            public float  positionY;
            public string data;            // JsonUtility.ToJson(node)
        }

        [Serializable]
        public class ConnectionEntry
        {
            public string sourceGuid;
            public string targetGuid;
            public string sourcePortId;
            public string targetPortId;
            public string label;
            public int    orderIndex;
        }

        // ---------------------------------------------------------------
        // Serialize
        // ---------------------------------------------------------------

        /// <summary>
        /// Build the clipboard envelope for <paramref name="nodes"/>; includes
        /// every connection in <paramref name="allConnections"/> whose endpoints
        /// are both in <paramref name="nodes"/> (so pasting reproduces the
        /// internal connectivity but doesn't dangle external connections).
        /// </summary>
        public static string Serialize(IEnumerable<NodeElement> nodes, IEnumerable<GraphConnection> allConnections)
        {
            var envelope = new Envelope();
            var includedGuids = new HashSet<Guid>();

            foreach (var ne in nodes)
            {
                var node = ne?.Node;
                if (node == null) continue;

                envelope.nodes.Add(new NodeEntry
                {
                    typeName = node.GetType().AssemblyQualifiedName,
                    guid = node.Guid.ValueStr,
                    positionX = node.Position.x,
                    positionY = node.Position.y,
                    data = JsonUtility.ToJson(node),
                });
                includedGuids.Add(node.Guid.Value());
            }

            foreach (var c in allConnections)
            {
                if (includedGuids.Contains(c.SourceNodeGuid.Value())
                    && includedGuids.Contains(c.TargetNodeGuid.Value()))
                {
                    envelope.connections.Add(new ConnectionEntry
                    {
                        sourceGuid = c.SourceNodeGuid.ValueStr,
                        targetGuid = c.TargetNodeGuid.ValueStr,
                        sourcePortId = c.SourcePortId,
                        targetPortId = c.TargetPortId,
                        label = c.Label,
                        orderIndex = c.OrderIndex,
                    });
                }
            }

            return Prefix + JsonUtility.ToJson(envelope);
        }

        public static bool TryDeserialize(string text, out Envelope envelope)
        {
            envelope = null;
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix)) return false;

            try
            {
                envelope = JsonUtility.FromJson<Envelope>(text.Substring(Prefix.Length));
                return envelope != null;
            }
            catch
            {
                envelope = null;
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Paste
        // ---------------------------------------------------------------

        /// <summary>
        /// Materialise the clipboard envelope onto <paramref name="asset"/>.
        /// Returns the freshly-created node SOs (in input order), or an empty
        /// list if the clipboard didn't contain a valid envelope.
        /// </summary>
        public static List<GraphNodeSO> Paste(string clipboardText, GraphAssetSO asset, Vector2 pasteOffset)
        {
            var result = new List<GraphNodeSO>();
            if (asset == null) return result;
            if (!TryDeserialize(clipboardText, out var envelope)) return result;
            if (envelope.nodes.Count == 0) return result;

            Undo.RegisterCompleteObjectUndo(asset, "Paste Nodes");

            var oldToNewGuid = new Dictionary<string, SerializedGuid>();

            foreach (var entry in envelope.nodes)
            {
                Type type = Type.GetType(entry.typeName);
                if (type == null || !typeof(GraphNodeSO).IsAssignableFrom(type)) continue;

                var node = (GraphNodeSO)ScriptableObject.CreateInstance(type);
                node.name = type.Name;
                // asset is the editor's transient working copy; new node
                // stays transient until Save promotes it to a subasset.
                // Skip the composite HideAndDontSave flag — it bundles
                // NotEditable, which would grey out the Inspector fields
                // when the pasted node is selected.
                node.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

                // FromJsonOverwrite will repopulate the node's fields,
                // *including* its serialized Guid. We then forcibly mint a
                // fresh one so pasted nodes don't collide with the originals.
                JsonUtility.FromJsonOverwrite(entry.data, node);
                node.Guid = SerializedGuid.NewGUID();
                node.Position = new Vector2(entry.positionX, entry.positionY) + pasteOffset;

                Undo.RegisterCreatedObjectUndo(node, "Paste Node");
                asset.AddNode(node);

                oldToNewGuid[entry.guid] = node.Guid;
                result.Add(node);
            }

            foreach (var entry in envelope.connections)
            {
                if (!oldToNewGuid.TryGetValue(entry.sourceGuid, out var newSrc)) continue;
                if (!oldToNewGuid.TryGetValue(entry.targetGuid, out var newDst)) continue;

                asset.AddConnection(new GraphConnection
                {
                    SourceNodeGuid = newSrc,
                    TargetNodeGuid = newDst,
                    SourcePortId = entry.sourcePortId,
                    TargetPortId = entry.targetPortId,
                    Label = entry.label,
                    OrderIndex = entry.orderIndex,
                });
            }

            EditorUtility.SetDirty(asset);
            return result;
        }

        /// <summary>True if the system clipboard currently holds a graphs envelope.</summary>
        public static bool HasPayload(string clipboardText)
        {
            return !string.IsNullOrEmpty(clipboardText) && clipboardText.StartsWith(Prefix);
        }
    }
}
