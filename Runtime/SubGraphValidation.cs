using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Reusable validation pass over a graph's <see cref="ISubGraphNode"/> references,
    /// yielding <see cref="GraphValidationIssue"/>s for: an unassigned reference, a
    /// self-reference, and a transitive reference cycle (each attributed to the local
    /// node so footer click-to-jump lands on the offender).
    ///
    /// <para>
    /// Wiring: a domain graph that opts into sub-graphs appends
    /// <c>foreach (var i in SubGraphValidation.Validate(this)) yield return i;</c> to its
    /// <see cref="GraphAssetSO.Validate"/> override.
    /// </para>
    ///
    /// <para>
    /// Comparison is by asset reference, made working-copy-aware through
    /// <c>GraphAssetSO.EditorIdentity</c>: the editor edits a transient clone that is
    /// reference-unequal to the on-disk assets its references point at, so checks compare
    /// against the clone's on-disk identity instead of the clone itself. A cycle or
    /// self-reference introduced in the working copy is therefore reported live in the
    /// footer, before save. At runtime (and for real assets) the identity is the graph
    /// itself and this reduces to plain reference equality.
    /// </para>
    /// </summary>
    public static class SubGraphValidation
    {
        public static IEnumerable<GraphValidationIssue> Validate(GraphAssetSO graph)
        {
            if (graph == null) yield break;

            // The asset this graph IS, for identity purposes. Sub-graph
            // references always point at on-disk assets, never at a working
            // copy, so comparing against the identity covers both cases.
            GraphAssetSO self = graph;
#if UNITY_EDITOR
            self = graph.EditorIdentity;
#endif

            foreach (var (node, child) in SubGraphResolver.DirectReferences(graph))
            {
                if (child == null)
                {
                    yield return Issue(GraphValidationIssue.SeverityLevel.Warning,
                        $"Sub-graph node \"{node.DisplayTitle}\" has no graph assigned.", node);
                    continue;
                }

                if (ReferenceEquals(child, graph) || ReferenceEquals(child, self))
                {
                    yield return Issue(GraphValidationIssue.SeverityLevel.Error,
                        $"Sub-graph node \"{node.DisplayTitle}\" references its own graph (self-reference).", node);
                    continue;
                }

                // This edge closes a loop iff the child can transitively reach
                // this graph (via its on-disk identity).
                if (Reaches(child, self))
                {
                    yield return Issue(GraphValidationIssue.SeverityLevel.Error,
                        $"Sub-graph node \"{node.DisplayTitle}\" creates a reference cycle back to \"{self.name}\".", node);
                }
            }
        }

        private static bool Reaches(GraphAssetSO from, GraphAssetSO target)
        {
            foreach (var g in SubGraphResolver.Flatten(from))
                if (ReferenceEquals(g, target)) return true;
            return false;
        }

        private static GraphValidationIssue Issue(GraphValidationIssue.SeverityLevel severity, string message, GraphNodeSO node)
        {
            return new GraphValidationIssue
            {
                Severity = severity,
                Message = message,
                TargetNodeGuid = node.Guid,
            };
        }
    }
}
