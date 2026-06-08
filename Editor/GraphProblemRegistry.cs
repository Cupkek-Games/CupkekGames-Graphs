using System;
using System.Collections.Generic;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// One wiring problem surfaced in the Graph Runtime Debugger's Problems panel.
    /// Generic: the graphs package never interprets the message — a consumer decides
    /// what counts as a problem and registers a provider.
    /// </summary>
    public readonly struct GraphProblem
    {
        public readonly GraphValidationIssue.SeverityLevel Severity;
        public readonly string Message;
        /// <summary>Optional graph the problem concerns — lets the panel offer a ping/jump.</summary>
        public readonly GraphAssetSO Graph;

        public GraphProblem(GraphValidationIssue.SeverityLevel severity, string message, GraphAssetSO graph = null)
        {
            Severity = severity;
            Message = message;
            Graph = graph;
        }
    }

    /// <summary>
    /// Generic seam for surfacing cross-cutting graph wiring problems. A consumer
    /// registers a provider (e.g. nav's catalog / registry checks); the Graph Runtime
    /// Debugger aggregates every provider's output — both at edit time and in play.
    /// The graphs package owns only the seam and the aggregator; it never knows what a
    /// problem <i>means</i>. Providers run on demand (the window pulls), so they can be
    /// as cheap or thorough as the consumer likes.
    /// </summary>
    public static class GraphProblemRegistry
    {
        static readonly List<Func<IEnumerable<GraphProblem>>> _providers = new();

        /// <summary>Raised when the provider set changes so an open debugger can refresh.</summary>
        public static event Action Changed;

        public static void RegisterProvider(Func<IEnumerable<GraphProblem>> provider)
        {
            if (provider == null || _providers.Contains(provider)) return;
            _providers.Add(provider);
            Changed?.Invoke();
        }

        public static void UnregisterProvider(Func<IEnumerable<GraphProblem>> provider)
        {
            if (provider != null && _providers.Remove(provider)) Changed?.Invoke();
        }

        /// <summary>
        /// Run every provider and gather all problems. A faulting provider is logged and
        /// skipped so one bad provider can't blank the panel.
        /// </summary>
        public static List<GraphProblem> Collect()
        {
            var all = new List<GraphProblem>();
            for (int i = 0; i < _providers.Count; i++)
            {
                try
                {
                    var result = _providers[i]?.Invoke();
                    if (result != null) all.AddRange(result);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            return all;
        }
    }
}
