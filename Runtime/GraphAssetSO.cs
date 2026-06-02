using System;
using System.Collections.Generic;
using UnityEngine;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Abstract base for any graph-shaped ScriptableObject. Owns the list of
    /// nodes (subassets), connections (serialized inline), and groups.
    /// Subclasses constrain the node type via <see cref="NodeBaseType"/>
    /// and add domain-specific behaviour (runtime traversal, validation, ...).
    /// </summary>
    public abstract class GraphAssetSO : ScriptableObject
    {
        [SerializeField] internal List<GraphNodeSO> _nodes = new List<GraphNodeSO>();
        [SerializeField] internal List<GraphConnection> _connections = new List<GraphConnection>();
        [SerializeField] internal List<GraphGroup> _groups = new List<GraphGroup>();

        public IReadOnlyList<GraphNodeSO> Nodes => _nodes;
        public IReadOnlyList<GraphConnection> Connections => _connections;
        public IReadOnlyList<GraphGroup> Groups => _groups;

        /// <summary>
        /// Constrains which node types this graph accepts. The editor's
        /// node-creation search walks the loaded assemblies for concrete
        /// subclasses of <see cref="NodeBaseType"/>.
        /// </summary>
        public abstract Type NodeBaseType { get; }

        /// <summary>
        /// Optionally override to host this graph in a custom
        /// <c>GraphCanvas</c> subclass (e.g. <c>BehaviourTreeCanvas</c>
        /// that enforces tree-shape rules in <c>CanConnect</c>, or a
        /// domain canvas that overrides <c>CreateNodeElement</c> for
        /// richer visuals). The returned type must derive from
        /// <c>CupkekGames.Graphs.Editor.GraphCanvas</c> and have a
        /// parameterless public constructor. Default (null) → the
        /// generic <c>GraphCanvas</c>.
        /// </summary>
        public virtual Type EditorCanvasType => null;

        /// <summary>
        /// Optional: guid of the "start" node in this graph. Default empty
        /// (no start) — non-execution-shaped graphs leave it alone. Domain
        /// subclasses override to expose their own serialised start guid
        /// (e.g. nav graph's start destination, BT's root, dialogue's
        /// opening node). The editor canvas paints the matching node with
        /// a coloured outline when this returns non-empty.
        /// </summary>
        public virtual Data.Primitives.SerializedGuid StartNodeGuid => default;

        /// <summary>
        /// Hook for subclasses to react when a node is added to the graph
        /// (e.g. wire runtime state). Called by the editor after the subasset
        /// has been registered.
        /// </summary>
        public virtual void OnNodeAdded(GraphNodeSO node) { }

        /// <summary>Called by the editor before a node is removed.</summary>
        public virtual void OnNodeRemoved(GraphNodeSO node) { }

        public virtual void OnConnectionAdded(GraphConnection connection) { }
        public virtual void OnConnectionRemoved(GraphConnection connection) { }

#if UNITY_EDITOR
        /// <summary>
        /// Fires when a domain inspector mutates a graph asset's fields
        /// off-canvas (e.g. picking a start destination via a dropdown).
        /// Editor surfaces that need to reflect such changes — the
        /// canvas's start-node outline, the validation footer — subscribe
        /// to this and filter by <c>asset == this.Asset</c>. Replaces
        /// per-window polling.
        ///
        /// Inspector code raises via <see cref="EditorRaiseMutated"/>
        /// after <c>serializedObject.ApplyModifiedProperties()</c>.
        /// Stripped from runtime builds.
        /// </summary>
        public static event Action<GraphAssetSO> EditorAssetMutated;

        /// <summary>Editor-only — raise <see cref="EditorAssetMutated"/> for this asset.</summary>
        public void EditorRaiseMutated() => EditorAssetMutated?.Invoke(this);

        // ---------------------------------------------------------------
        // Working-copy editing (Shader-Graph-style deferred save)
        //
        // The editor window edits a transient clone of the on-disk asset
        // so Unity's auto-flush triggers (focus loss, play mode, etc.)
        // never touch the real file. The user explicitly saves to commit
        // the working copy back to disk, or discards to throw it away.
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a transient clone of this asset for editor use. Nodes
        /// are individually re-instantiated with preserved <c>Guid</c>s
        /// so <see cref="GraphConnection"/> edges still resolve. Domain
        /// graphs fix internal node-referencing fields (e.g. BT's
        /// <c>_rootNode</c>) by overriding <see cref="OnClonedFromOriginal"/>.
        ///
        /// The returned clone has <see cref="HideFlags.HideAndDontSave"/>
        /// so Unity won't try to serialize it as part of any scene or
        /// auto-flush it. Caller is responsible for destroying it when
        /// done editing.
        /// </summary>
        public virtual GraphAssetSO CloneForEditing()
        {
            var clone = (GraphAssetSO)Instantiate(this);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.name = name + " (working)";

            // Object.Instantiate gives us a new List<GraphNodeSO> but the
            // entries still reference the originals — clone each node so
            // mutations don't leak back to the on-disk asset.
            //
            // Node clones use HideInHierarchy + DontSave (not the
            // composite HideAndDontSave) so the Inspector still treats
            // them as editable. HideAndDontSave bundles NotEditable,
            // which would grey out every field when the user selects a
            // node on the canvas.
            clone._nodes = new List<GraphNodeSO>();
            foreach (var n in _nodes)
            {
                if (n == null) continue;
                var nodeClone = Instantiate(n);
                nodeClone.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                nodeClone.name = n.name;
                nodeClone.Guid = n.Guid;
                clone._nodes.Add(nodeClone);
            }

            // GraphConnection + GraphGroup are [Serializable] classes with
            // no Object references; Instantiate above already deep-copied
            // _connections + _groups by value through serialization.

            // An ISubGraphNode's reference points at ANOTHER GraphAssetSO asset
            // (not a subasset of this one), so re-Instantiating the node above
            // copies that reference BY VALUE — the working copy shares the same
            // on-disk sub-graph and never deep-clones it. Correct by construction:
            // editing a sub-graph descends into the referenced asset's own working
            // copy, it is not mutated through this clone.

            clone.OnClonedFromOriginal(this);
            return clone;
        }

        /// <summary>
        /// Hook for domain graphs to fix up internal cross-references that
        /// point at node SOs (e.g. BT's <c>_rootNode</c>) after
        /// <see cref="CloneForEditing"/> has rebuilt <c>_nodes</c>. The
        /// references on <c>this</c> still point at the original's nodes;
        /// override to remap to the clone's nodes (typically by GUID
        /// lookup). Default no-op.
        /// </summary>
        protected virtual void OnClonedFromOriginal(GraphAssetSO original) { }

        /// <summary>
        /// Commit this working copy's state back to <paramref name="target"/>
        /// (the on-disk asset). Diffs nodes by <c>Guid</c>: matched nodes
        /// have their serialized state copied, new nodes are added as
        /// subassets, removed nodes are destroyed. Connections + groups
        /// are inline-serialized so they're replaced wholesale. Domain
        /// graphs fix domain-specific fields by overriding
        /// <see cref="OnApplyToOriginal"/>.
        ///
        /// After this returns, the caller should call
        /// <c>AssetDatabase.SaveAssets()</c> to flush <paramref name="target"/>
        /// to disk.
        /// </summary>
        public virtual void ApplyToOriginal(GraphAssetSO target)
        {
            if (target == null) return;

            // Diff nodes by Guid. Matched → CopySerialized so existing
            // subasset references stay valid. New → clone into a new
            // subasset. Removed → DestroyImmediate (subasset cleanup).
            var byGuid = new Dictionary<string, GraphNodeSO>();
            foreach (var n in target._nodes)
                if (n != null) byGuid[n.Guid.ValueStr] = n;

            var newList = new List<GraphNodeSO>();
            var seen = new HashSet<string>();
            foreach (var workingNode in _nodes)
            {
                if (workingNode == null) continue;
                seen.Add(workingNode.Guid.ValueStr);

                if (byGuid.TryGetValue(workingNode.Guid.ValueStr, out var realNode))
                {
                    UnityEditor.EditorUtility.CopySerialized(workingNode, realNode);
                    newList.Add(realNode);
                }
                else
                {
                    var newReal = Instantiate(workingNode);
                    newReal.name = workingNode.name;
                    newReal.hideFlags = HideFlags.HideInHierarchy;
                    newReal.Guid = workingNode.Guid;
                    UnityEditor.AssetDatabase.AddObjectToAsset(newReal, target);
                    newList.Add(newReal);
                }
            }

            // Destroy nodes present in target but missing from working copy.
            foreach (var realNode in target._nodes)
            {
                if (realNode == null || seen.Contains(realNode.Guid.ValueStr)) continue;
                UnityEditor.Undo.DestroyObjectImmediate(realNode);
            }

            target._nodes = newList;
            target._connections = new List<GraphConnection>(_connections);
            target._groups = new List<GraphGroup>(_groups);

            target.OnApplyToOriginal(this);

            UnityEditor.EditorUtility.SetDirty(target);
        }

        /// <summary>
        /// Hook for domain graphs to commit domain-specific fields (e.g.
        /// nav's start guid, BT's root pointer) from a working copy back
        /// to the on-disk asset. The base <see cref="ApplyToOriginal"/>
        /// already handled <c>_nodes</c> / <c>_connections</c> /
        /// <c>_groups</c> by the time this fires. <paramref name="workingCopy"/>
        /// is the source; <c>this</c> is the target.
        /// </summary>
        protected virtual void OnApplyToOriginal(GraphAssetSO workingCopy) { }
#endif

        /// <summary>
        /// Surface validation issues in the editor's footer panel. Default
        /// returns no issues; subclasses override to enforce shape rules.
        /// </summary>
        public virtual IEnumerable<GraphValidationIssue> Validate()
        {
            return Array.Empty<GraphValidationIssue>();
        }

        /// <summary>
        /// Add a node to the graph. Editor code wraps this in an Undo group
        /// and handles subasset registration; consumers calling this at
        /// runtime are responsible for their own persistence.
        /// </summary>
        public void AddNode(GraphNodeSO node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (_nodes.Contains(node)) return;
            _nodes.Add(node);
            OnNodeAdded(node);
        }

        /// <summary>
        /// Remove a node and any connections touching it. Returns true if
        /// the node was present. Subasset destruction is the caller's
        /// responsibility (so editor can wrap in Undo).
        /// </summary>
        public bool RemoveNode(GraphNodeSO node)
        {
            if (node == null || !_nodes.Remove(node)) return false;
            OnNodeRemoved(node);

            for (int i = _connections.Count - 1; i >= 0; i--)
            {
                var c = _connections[i];
                if (c.SourceNodeGuid == node.Guid || c.TargetNodeGuid == node.Guid)
                {
                    _connections.RemoveAt(i);
                    OnConnectionRemoved(c);
                }
            }
            return true;
        }

        public void AddConnection(GraphConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            _connections.Add(connection);
            OnConnectionAdded(connection);
        }

        public bool RemoveConnection(GraphConnection connection)
        {
            if (connection == null || !_connections.Remove(connection)) return false;
            OnConnectionRemoved(connection);
            return true;
        }

        public void AddGroup(GraphGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            _groups.Add(group);
        }

        public bool RemoveGroup(GraphGroup group)
        {
            return group != null && _groups.Remove(group);
        }
    }
}
