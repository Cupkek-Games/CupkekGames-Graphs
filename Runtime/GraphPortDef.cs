using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Declarative description of an input or output port on a node type.
    /// Returned from <see cref="GraphNodeSO.InputPorts"/> /
    /// <see cref="GraphNodeSO.OutputPorts"/>; consumed by the editor to
    /// render port stubs and validate connections.
    /// </summary>
    [Serializable]
    public partial class GraphPortDef
    {
        /// <summary>Null marks an anonymous default port.</summary>
        public string Id;

        /// <summary>Optional display label rendered next to the port stub.</summary>
        public string Label;

        /// <summary>
        /// Single = port accepts one connection (decorator child, default in/out).
        /// Multi  = port accepts many connections (composite children).
        /// </summary>
        public PortCapacity Capacity = PortCapacity.Single;

        [NoAutoStaticsCleanup]
        public static readonly IReadOnlyList<GraphPortDef> None = Array.Empty<GraphPortDef>();

        [NoAutoStaticsCleanup]
        public static readonly IReadOnlyList<GraphPortDef> SingleAnonymous = new[]
        {
            new GraphPortDef { Capacity = PortCapacity.Single }
        };
    }
}
