using UnityEngine;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// A small status chip shown on a node header's trailing edge — the
    /// per-node "status vocabulary" that a single title / subtitle / color /
    /// icon can't express. Domain nodes return a list from
    /// <see cref="GraphNodeSO.DisplayBadges"/>; the editor renders each as a
    /// short colored pill (right-aligned in the header).
    ///
    /// <para>
    /// Keep badges <b>node-local</b> — readable from the node alone. Derived /
    /// cross-node facts (e.g. "this node's parent is a tab container") need
    /// graph context and belong on the canvas, not here.
    /// </para>
    /// </summary>
    public readonly struct NodeBadge
    {
        /// <summary>Very short pill text (e.g. "Tabs", "seed", "multi").</summary>
        public readonly string Text;

        /// <summary>Pill fill color.</summary>
        public readonly Color Tint;

        /// <summary>Optional hover tooltip explaining the badge. Null/empty = no tooltip.</summary>
        public readonly string Tooltip;

        public NodeBadge(string text, Color tint, string tooltip = null)
        {
            Text = text;
            Tint = tint;
            Tooltip = tooltip;
        }
    }
}
