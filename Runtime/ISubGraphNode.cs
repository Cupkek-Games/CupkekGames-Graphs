namespace CupkekGames.Graphs
{
    /// <summary>
    /// Implemented by any node that embeds another <see cref="GraphAssetSO"/> as a
    /// reusable sub-graph. This interface is the ONLY thing common across BT subtrees,
    /// dialogue sub-conversations, and nav sub-flows.
    ///
    /// <para>
    /// Editor descend and the generic traversal/validation helpers key off THIS
    /// interface, never a concrete type, so the package stays domain-agnostic: each
    /// domain node's base type is its own <see cref="GraphAssetSO.NodeBaseType"/>
    /// (BTNode / NavNode / DialogueNode), and each MUST declare its own
    /// <see cref="GraphNodeSO.InputPorts"/> / <see cref="GraphNodeSO.OutputPorts"/>
    /// explicitly — the inherited 1-in/1-out default is wrong for a containment-only
    /// consumer like nav (which declares zero output ports).
    /// </para>
    ///
    /// <para>
    /// No concrete <c>SubGraphNodeSO</c> ships: a node usable as-is would have to inherit
    /// the 1-out default, re-introducing the phantom-port hazard. Each domain defines its
    /// own thin subclass whose base matches its <c>NodeBaseType</c>.
    /// </para>
    /// </summary>
    public interface ISubGraphNode
    {
        /// <summary>The referenced sub-graph asset, or null if unassigned.</summary>
        GraphAssetSO SubGraph { get; }
    }
}
