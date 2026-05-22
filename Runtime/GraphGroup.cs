using System;
using System.Collections.Generic;
using UnityEngine;
using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// A coloured rounded rectangle drawn behind a cluster of nodes for
    /// visual organisation. Not a node — has no ports, is not part of the
    /// connection graph. Stored on the parent <see cref="GraphAssetSO"/>
    /// alongside nodes and connections.
    /// </summary>
    [Serializable]
    public class GraphGroup
    {
        public SerializedGuid Guid = new SerializedGuid(System.Guid.NewGuid());
        public string Title = "Group";
        public Color Color = new Color(0.30f, 0.45f, 0.65f, 0.25f);
        public Rect Bounds = new Rect(0, 0, 300, 200);

        /// <summary>
        /// GUIDs of nodes the editor considers "contained" by this group.
        /// When the group is dragged, contained nodes follow.
        /// </summary>
        public List<SerializedGuid> ContainedNodeGuids = new List<SerializedGuid>();
    }
}
