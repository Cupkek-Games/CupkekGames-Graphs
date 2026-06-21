using System.Collections.Generic;
using System.Text;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Structural validation + repair for <see cref="GraphConnection"/>s whose
    /// endpoints went stale: a node deleted outside <see cref="GraphAssetSO.RemoveNode"/>,
    /// or a node type whose <see cref="GraphNodeSO.InputPorts"/> /
    /// <see cref="GraphNodeSO.OutputPorts"/> changed between versions (port renamed,
    /// removed, or anonymous → named), leaving connections that reference port ids
    /// which no longer exist — those edges silently never match at traversal time.
    ///
    /// <para>
    /// <see cref="GraphAssetSO.Validate"/> includes <see cref="Validate"/> by default.
    /// Domain graphs that override Validate replace that default — include it the same
    /// way they include <see cref="SubGraphValidation"/>:
    /// <c>foreach (var i in GraphConnectionValidation.Validate(this)) yield return i;</c>
    /// </para>
    /// </summary>
    public static class GraphConnectionValidation
    {
        /// <summary>
        /// Yield an Error issue for every connection whose source/target node is
        /// missing from the graph, or whose port id no longer exists on the
        /// node's current port definitions.
        /// </summary>
        public static IEnumerable<GraphValidationIssue> Validate(GraphAssetSO graph)
        {
            if (graph == null) yield break;

            Dictionary<string, GraphNodeSO> byGuid = BuildNodeMap(graph);

            foreach (GraphConnection c in graph.Connections)
            {
                if (c == null) continue;

                bool hasSource = byGuid.TryGetValue(c.SourceNodeGuid.ValueStr, out GraphNodeSO source);
                bool hasTarget = byGuid.TryGetValue(c.TargetNodeGuid.ValueStr, out GraphNodeSO target);

                if (!hasSource || !hasTarget)
                {
                    yield return new GraphValidationIssue
                    {
                        Severity = GraphValidationIssue.SeverityLevel.Error,
                        Message = $"Connection references a deleted {(!hasSource ? "source" : "target")} node — Repair Connections removes it.",
                        TargetConnectionGuid = c.Guid,
                    };
                    continue;
                }

                if (!HasPort(source.OutputPorts, c.SourcePortId))
                {
                    yield return new GraphValidationIssue
                    {
                        Severity = GraphValidationIssue.SeverityLevel.Error,
                        Message = PortIssueMessage(source, c.SourcePortId, isInput: false),
                        TargetConnectionGuid = c.Guid,
                    };
                }

                if (!HasPort(target.InputPorts, c.TargetPortId))
                {
                    yield return new GraphValidationIssue
                    {
                        Severity = GraphValidationIssue.SeverityLevel.Error,
                        Message = PortIssueMessage(target, c.TargetPortId, isInput: true),
                        TargetConnectionGuid = c.Guid,
                    };
                }
            }
        }

        /// <summary>Outcome of a <see cref="Repair"/> pass, for status-bar / log reporting.</summary>
        public class RepairReport
        {
            /// <summary>Connections removed because an endpoint node no longer exists.</summary>
            public int RemovedConnections;

            /// <summary>Port ids remapped to a node's single remaining port on that side.</summary>
            public int RemappedPortIds;

            /// <summary>
            /// Connections whose stale port id could not be remapped automatically
            /// (the node has zero or several ports on that side — the right target
            /// is ambiguous). Left in place; still reported by <see cref="Validate"/>.
            /// </summary>
            public List<GraphConnection> Unrepairable = new List<GraphConnection>();

            public bool ChangedAnything => RemovedConnections > 0 || RemappedPortIds > 0;
        }

        /// <summary>
        /// Repair what is unambiguous: remove connections whose endpoint node is
        /// gone, and remap a stale port id when the node has exactly one port on
        /// that side (covers the common rename / anonymous→named migrations).
        /// Ambiguous cases are reported, not guessed. Mutates <paramref name="graph"/> —
        /// run on the editor working copy (save/discard then applies as usual);
        /// callers outside that flow own their own undo/dirty handling.
        /// </summary>
        public static RepairReport Repair(GraphAssetSO graph)
        {
            RepairReport report = new RepairReport();
            if (graph == null) return report;

            Dictionary<string, GraphNodeSO> byGuid = BuildNodeMap(graph);

            for (int i = graph.Connections.Count - 1; i >= 0; i--)
            {
                GraphConnection c = graph.Connections[i];
                if (c == null) continue;

                bool hasSource = byGuid.TryGetValue(c.SourceNodeGuid.ValueStr, out GraphNodeSO source);
                bool hasTarget = byGuid.TryGetValue(c.TargetNodeGuid.ValueStr, out GraphNodeSO target);

                if (!hasSource || !hasTarget)
                {
                    graph.RemoveConnection(c);
                    report.RemovedConnections++;
                    continue;
                }

                bool unrepairable = false;
                unrepairable |= !TryRepairPort(source.OutputPorts, c.SourcePortId,
                    repairedId => c.SourcePortId = repairedId, report);
                unrepairable |= !TryRepairPort(target.InputPorts, c.TargetPortId,
                    repairedId => c.TargetPortId = repairedId, report);

                if (unrepairable)
                {
                    report.Unrepairable.Add(c);
                }
            }

            return report;
        }

        /// <summary>
        /// Returns true when the port id is valid (already or after remap);
        /// false when it is stale and ambiguous to remap.
        /// </summary>
        private static bool TryRepairPort(IReadOnlyList<GraphPortDef> ports, string portId,
            System.Action<string> applyRepairedId, RepairReport report)
        {
            if (HasPort(ports, portId)) return true;

            if (ports != null && ports.Count == 1)
            {
                applyRepairedId(Normalize(ports[0].Id));
                report.RemappedPortIds++;
                return true;
            }

            return false;
        }

        private static Dictionary<string, GraphNodeSO> BuildNodeMap(GraphAssetSO graph)
        {
            Dictionary<string, GraphNodeSO> byGuid =
                new Dictionary<string, GraphNodeSO>(System.StringComparer.Ordinal);
            foreach (GraphNodeSO node in graph.Nodes)
            {
                if (node == null) continue;
                byGuid[node.Guid.ValueStr] = node;
            }
            return byGuid;
        }

        private static bool HasPort(IReadOnlyList<GraphPortDef> ports, string portId)
        {
            if (ports == null) return false;
            string wanted = Normalize(portId);
            for (int i = 0; i < ports.Count; i++)
            {
                if (Normalize(ports[i].Id) == wanted) return true;
            }
            return false;
        }

        /// <summary>Null and empty both mean the anonymous default port.</summary>
        private static string Normalize(string portId) =>
            string.IsNullOrEmpty(portId) ? null : portId;

        private static string PortIssueMessage(GraphNodeSO node, string portId, bool isInput)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(isInput ? "Input" : "Output")
              .Append(" port '").Append(string.IsNullOrEmpty(portId) ? "(anonymous)" : portId)
              .Append("' no longer exists on \"").Append(node.DisplayTitle)
              .Append("\" (current: ");

            IReadOnlyList<GraphPortDef> ports = isInput ? node.InputPorts : node.OutputPorts;
            if (ports == null || ports.Count == 0)
            {
                sb.Append("none");
            }
            else
            {
                for (int i = 0; i < ports.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(string.IsNullOrEmpty(ports[i].Id) ? "(anonymous)" : ports[i].Id);
                }
            }

            sb.Append("). Repair Connections remaps single-port nodes automatically.");
            return sb.ToString();
        }
    }
}
