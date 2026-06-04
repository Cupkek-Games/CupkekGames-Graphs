#if UNITY_EDITOR
using UnityEngine;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Per-node live runtime state painted on a node card while the editor is in
    /// play mode — a glow border + an optional status pill. Produced by an
    /// <see cref="IGraphRuntimeStateSource"/>, rendered by the editor. Editor-only;
    /// no debug visuals ship in player builds.
    /// </summary>
    public readonly struct GraphNodeRuntimeState
    {
        /// <summary>Glow / border tint (null = no glow).</summary>
        public readonly Color? Glow;

        /// <summary>Optional live status pill (reuses <see cref="NodeBadge"/>). Null = none.</summary>
        public readonly NodeBadge? Badge;

        public GraphNodeRuntimeState(Color? glow, NodeBadge? badge)
        {
            Glow = glow;
            Badge = badge;
        }
    }
}
#endif
