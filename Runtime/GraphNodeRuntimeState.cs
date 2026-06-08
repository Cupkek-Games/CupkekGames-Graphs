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

        /// <summary>
        /// Optional card background tint (null = the card's normal surface). Lets a
        /// source make the most-important node — e.g. the active/visible one — <i>pop</i>
        /// with a filled card instead of relying on a thin border alone.
        /// </summary>
        public readonly Color? Fill;

        /// <summary>Optional live status pill (reuses <see cref="NodeBadge"/>). Null = none.</summary>
        public readonly NodeBadge? Badge;

        public GraphNodeRuntimeState(Color? glow, NodeBadge? badge)
        {
            Glow = glow;
            Fill = null;
            Badge = badge;
        }

        public GraphNodeRuntimeState(Color? glow, Color? fill, NodeBadge? badge)
        {
            Glow = glow;
            Fill = fill;
            Badge = badge;
        }
    }
}
#endif
