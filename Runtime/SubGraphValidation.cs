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
    /// Comparison is by asset reference — correct at runtime and whenever this runs against
    /// a resolved / on-disk graph. The editor edits a transient working copy that is
    /// reference-unequal to the on-disk assets its references point at, so a self-reference
    /// to the original would slip past reference equality; the editor therefore runs this
    /// against the on-disk original (resolved when the descend UX wires the footer), not the
    /// working copy.
    /// </para>
    /// </summary>
    public static class SubGraphValidation
    {
        public static IEnumerable<GraphValidationIssue> Validate(GraphAssetSO graph)
        {
            if (graph == null) yield break;

            foreach (var (node, child) in SubGraphResolver.DirectReferences(graph))
            {
                if (child == null)
                {
                    yield return Issue(GraphValidationIssue.SeverityLevel.Warning,
                        $"Sub-graph node \"{node.DisplayTitle}\" has no graph assigned.", node);
                    continue;
                }

                if (ReferenceEquals(child, graph))
                {
                    yield return Issue(GraphValidationIssue.SeverityLevel.Error,
                        $"Sub-graph node \"{node.DisplayTitle}\" references its own graph (self-reference).", node);
                    continue;
                }

                // This edge closes a loop iff the child can transitively reach `graph`.
                if (Reaches(child, graph))
                {
                    yield return Issue(GraphValidationIssue.SeverityLevel.Error,
                        $"Sub-graph node \"{node.DisplayTitle}\" creates a reference cycle back to \"{graph.name}\".", node);
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
