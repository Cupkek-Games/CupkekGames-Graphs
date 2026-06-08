using System;
using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Editor-only registry of graph <i>assets</i> that currently have live runtime
    /// state — a mounted nav graph, a behaviour-tree with one-or-more running agents,
    /// any future consumer. The Graph Runtime Debugger window enumerates this to show
    /// "all live graphs at once" without knowing what any specific consumer is.
    ///
    /// <para>
    /// Register the <b>editable asset</b> (the one with a layout sidecar + an
    /// <see cref="GraphAssetSO.CreateRuntimeStateSource"/>), not a per-instance clone:
    /// N agents running one tree register that tree once and disambiguate via the
    /// source's <see cref="IGraphRuntimeInstanceSelector"/>. Registration is
    /// reference-counted, so two hosts mounting the same graph keep it listed until the
    /// last leaves.
    /// </para>
    ///
    /// <para>
    /// The storage + events are <c>#if UNITY_EDITOR</c>, and <see cref="Register"/> /
    /// <see cref="Unregister"/> are no-ops in player builds — so runtime consumers
    /// (e.g. a <c>NavHost</c> MonoBehaviour) can call them unconditionally with zero
    /// shipped cost and no <c>#if</c> at the call site.
    /// </para>
    /// </summary>
    public static class GraphRuntimeRegistry
    {
        /// <summary>A live graph: the asset + a human label for the picker.</summary>
        public readonly struct Entry
        {
            public readonly GraphAssetSO Graph;
            public readonly string Label;
            public Entry(GraphAssetSO graph, string label) { Graph = graph; Label = label; }
        }

#if UNITY_EDITOR
        sealed class LiveGraph { public GraphAssetSO Graph; public string Label; public int Count; }

        static readonly List<LiveGraph> _live = new();

        /// <summary>Raised whenever the live set changes (add / remove / relabel).</summary>
        public static event Action Changed;

        /// <summary>Every graph asset with live runtime state, in registration order.</summary>
        public static IReadOnlyList<Entry> Live
        {
            get
            {
                var list = new List<Entry>(_live.Count);
                for (int i = 0; i < _live.Count; i++)
                    list.Add(new Entry(_live[i].Graph, _live[i].Label));
                return list;
            }
        }

        // Static state survives "Enter Play Mode Without Domain Reload"; a leftover
        // entry from a prior run would linger. Clear at every play-enter — consumers
        // re-register in their Awake. Mirrors the other CupkekGames static resets.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _live.Clear();
            Changed?.Invoke();
        }
#endif

        /// <summary>
        /// Mark <paramref name="graph"/> as live (it has a running instance).
        /// Reference-counted; re-registering an already-live graph bumps the count and
        /// refreshes its label. No-op in player builds.
        /// </summary>
        public static void Register(GraphAssetSO graph, string label)
        {
#if UNITY_EDITOR
            if (graph == null) return;
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Graph == graph)
                {
                    _live[i].Count++;
                    _live[i].Label = label;
                    Changed?.Invoke();
                    return;
                }
            }
            _live.Add(new LiveGraph { Graph = graph, Label = label, Count = 1 });
            Changed?.Invoke();
#endif
        }

        /// <summary>
        /// Drop one live registration of <paramref name="graph"/>; the entry is removed
        /// once the last holder unregisters. No-op in player builds.
        /// </summary>
        public static void Unregister(GraphAssetSO graph)
        {
#if UNITY_EDITOR
            if (graph == null) return;
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Graph == graph)
                {
                    if (--_live[i].Count <= 0) _live.RemoveAt(i);
                    Changed?.Invoke();
                    return;
                }
            }
#endif
        }
    }
}
