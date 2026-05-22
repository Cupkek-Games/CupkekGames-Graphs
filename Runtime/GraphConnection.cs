using System;
using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// A directed edge between two <see cref="GraphNodeSO"/> instances via
    /// named ports. Connections are stored on the parent
    /// <see cref="GraphAssetSO"/>, not on the source node — this mirrors how
    /// Shader Graph / Animator persist connections and avoids per-port field
    /// proliferation on the node SO.
    /// </summary>
    [Serializable]
    public class GraphConnection
    {
        public SerializedGuid Guid = new SerializedGuid(System.Guid.NewGuid());
        public SerializedGuid SourceNodeGuid;
        public SerializedGuid TargetNodeGuid;

        /// <summary>Null for anonymous default port.</summary>
        public string SourcePortId;
        public string TargetPortId;

        /// <summary>Optional edge label rendered mid-curve (e.g. "OnConfirm").</summary>
        public string Label;

        /// <summary>
        /// Used to order outbound connections from a Multi-capacity port
        /// (e.g. composite children must execute left-to-right).
        /// </summary>
        public int OrderIndex;
    }
}
