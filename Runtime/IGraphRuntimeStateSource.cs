#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Domain-supplied source of per-node live runtime state, sampled by the editor
    /// while in play mode. <b>Push-driven</b>: raise <see cref="Changed"/> when the
    /// live state changes and the canvas re-applies — there is no editor poll. The
    /// canvas owns the source's lifetime and <see cref="IDisposable.Dispose"/>s it on
    /// exit-play / asset switch. Editor-only.
    /// </summary>
    public interface IGraphRuntimeStateSource : IDisposable
    {
        /// <summary>Is there a running instance to read? When false, the overlay clears.</summary>
        bool IsLive { get; }

        /// <summary>
        /// Resolve <paramref name="node"/>'s current state. Return false for "no
        /// state" (the card's overlay is cleared).
        /// </summary>
        bool TryGetState(GraphNodeSO node, out GraphNodeRuntimeState state);

        /// <summary>Raised when the live state changes → the canvas re-applies. No timer.</summary>
        event Action Changed;
    }

    /// <summary>
    /// Additive capability for multi-instance domains (e.g. many agents running the
    /// same behaviour-tree). A source that <i>also</i> implements this lets the editor
    /// show an instance picker; single-instance sources (nav) don't implement it, so
    /// the base render path is unchanged. (The picker UI itself lands when the first
    /// multi-instance source does.)
    /// </summary>
    public interface IGraphRuntimeInstanceSelector
    {
        IReadOnlyList<string> InstanceLabels { get; }
        int SelectedInstance { get; set; }
    }

    /// <summary>
    /// Optional capability for a source whose state changes <b>continuously</b>
    /// (e.g. a behaviour-tree ticking every frame) rather than on discrete events.
    /// The canvas ticks <see cref="Poll"/> via <c>EditorApplication.update</c> while
    /// the source is active; the source samples its state there and raises
    /// <see cref="IGraphRuntimeStateSource.Changed"/> only when it actually changed
    /// (coalesce — don't fire every frame). This lets a continuous source live in the
    /// runtime assembly with no <c>EditorApplication</c> dependency of its own, just
    /// like a discrete (push-driven) source.
    /// </summary>
    public interface IGraphRuntimePollable
    {
        void Poll();
    }

    /// <summary>
    /// Additive capability: a source that also tints <b>edges</b> in play mode — e.g. to
    /// light the active flow path through the graph, not just the nodes. The canvas asks
    /// per connection, passing that edge's endpoint nodes (source → target); return true
    /// plus a colour to override the edge's stroke, false to leave it at the default look.
    /// Optional — sources that don't implement it leave edges untinted, so the base render
    /// path is unchanged.
    /// </summary>
    public interface IGraphRuntimeEdgeColorSource
    {
        bool TryGetEdgeColor(GraphNodeSO from, GraphNodeSO to, out UnityEngine.Color color);
    }
}
#endif
