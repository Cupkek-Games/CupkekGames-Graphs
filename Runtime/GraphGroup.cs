using System;
using UnityEngine;
using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// A coloured rounded rectangle drawn behind a cluster of nodes for
    /// visual organisation. Not a node — has no ports, is not part of the
    /// connection graph. Stored on the parent <see cref="GraphAssetSO"/>
    /// alongside nodes and connections.
    ///
    /// <para>
    /// Membership is <b>spatial</b> — the editor treats a node as "in" the group
    /// whose box contains it (see <c>GraphCanvas.NodesInGroup</c>), so there is no
    /// stored member list to keep in sync, and groups never reach the runtime
    /// (nothing outside the editor reads <see cref="GraphAssetSO.Groups"/>).
    /// </para>
    /// </summary>
    [Serializable]
    public class GraphGroup
    {
        public SerializedGuid Guid = new SerializedGuid(System.Guid.NewGuid());
        public string Title = "Group";
        public Color Color = new Color(0.30f, 0.45f, 0.65f, 0.25f);
        public Rect Bounds = new Rect(0, 0, 300, 200);
    }
}
