using System;
using System.Collections.Generic;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Builds the runtime debugger's "All mounted" view: one transient composite graph
    /// holding every live <see cref="GraphRuntimeRegistry"/> entry of a single concrete
    /// graph type, each source graph laid out as its own cluster with a synthetic group
    /// frame titled by its registry label. Mirrors runtimes that union mounted graphs
    /// into one id space (nav), where a per-graph view understates what is reachable.
    ///
    /// <para>
    /// The composite is an instance of the source graphs' own type, so the domain canvas
    /// (<see cref="GraphAssetSO.EditorCanvasType"/>) and its runtime state source resolve
    /// exactly as they would for a real asset. It is <see cref="HideFlags.HideAndDontSave"/>
    /// and the caller owns destroying it. Mutating node <c>Position</c> is safe (it is
    /// non-serialized and re-stamped from the layout sidecar on every bind); source
    /// <see cref="GraphGroup"/>s are cloned rather than shifted in place, so source
    /// assets are never dirtied.
    /// </para>
    /// </summary>
    public static class MergedGraphBuilder
    {
        const float ClusterGap = 140f;
        const float FramePadding = 36f;
        const float FrameTitleHeight = 30f;

        // Card footprint estimate for cluster bounds. Cards are laid out by UI Toolkit,
        // so exact sizes are unknown at build time; these match the AutoLayoutEngine's
        // assumptions closely enough for framing.
        const float NodeWidth = 200f;
        const float NodeHeight = 90f;

        /// <summary>Distinct concrete graph types among <paramref name="entries"/>, in registration order.</summary>
        public static List<Type> LiveTypes(IReadOnlyList<GraphRuntimeRegistry.Entry> entries)
        {
            var types = new List<Type>();
            for (int i = 0; i < entries.Count; i++)
            {
                var g = entries[i].Graph;
                if (g == null) continue;
                var t = g.GetType();
                if (!types.Contains(t)) types.Add(t);
            }
            return types;
        }

        /// <summary>
        /// Build the composite for every entry whose graph is exactly of
        /// <paramref name="graphType"/>. Returns null when no entry matches.
        /// </summary>
        public static GraphAssetSO Build(IReadOnlyList<GraphRuntimeRegistry.Entry> entries, Type graphType)
        {
            GraphAssetSO composite = null;
            float cursorX = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                var source = entries[i].Graph;
                if (source == null || source.GetType() != graphType) continue;

                if (composite == null)
                {
                    composite = (GraphAssetSO)ScriptableObject.CreateInstance(graphType);
                    composite.name = "All mounted";
                    composite.hideFlags = HideFlags.HideAndDontSave;
                }

                // Stamp authored positions from the source's sidecar, then shift the
                // whole cluster so graphs sit side by side instead of stacked at origin.
                GraphLayoutIO.Apply(source, source);
                Rect bounds = NodeBounds(source);
                var offset = new Vector2(
                    cursorX - bounds.xMin,
                    FrameTitleHeight + FramePadding - bounds.yMin);

                foreach (var n in source.Nodes)
                {
                    if (n == null) continue;
                    n.Position += offset;
                    composite.AddNode(n);
                }

                foreach (var c in source.Connections)
                {
                    if (c != null) composite.AddConnection(c);
                }

                foreach (var g in source.Groups)
                {
                    if (g == null) continue;
                    composite.AddGroup(new GraphGroup
                    {
                        Title = g.Title,
                        Color = g.Color,
                        Bounds = new Rect(g.Bounds.position + offset, g.Bounds.size),
                    });
                }

                string label = string.IsNullOrEmpty(entries[i].Label) ? source.name : entries[i].Label;
                composite.AddGroup(new GraphGroup
                {
                    Title = label,
                    Color = FrameTint(label),
                    Bounds = new Rect(
                        cursorX - FramePadding,
                        0f,
                        bounds.width + FramePadding * 2f,
                        bounds.height + FramePadding * 2f + FrameTitleHeight),
                });

                cursorX += bounds.width + FramePadding * 2f + ClusterGap;
            }

            return composite;
        }

        static Rect NodeBounds(GraphAssetSO graph)
        {
            bool any = false;
            float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
            foreach (var n in graph.Nodes)
            {
                if (n == null) continue;
                if (!any)
                {
                    minX = n.Position.x;
                    minY = n.Position.y;
                    maxX = n.Position.x + NodeWidth;
                    maxY = n.Position.y + NodeHeight;
                    any = true;
                    continue;
                }
                minX = Mathf.Min(minX, n.Position.x);
                minY = Mathf.Min(minY, n.Position.y);
                maxX = Mathf.Max(maxX, n.Position.x + NodeWidth);
                maxY = Mathf.Max(maxY, n.Position.y + NodeHeight);
            }
            return any ? Rect.MinMaxRect(minX, minY, maxX, maxY) : new Rect(0f, 0f, NodeWidth, NodeHeight);
        }

        // Stable per-label hue so a host's frame keeps its color across rebinds.
        static Color FrameTint(string label)
        {
            uint h = 2166136261u;
            for (int i = 0; i < label.Length; i++)
            {
                h ^= label[i];
                h *= 16777619u;
            }
            var c = Color.HSVToRGB((h % 360u) / 360f, 0.45f, 0.55f);
            c.a = 0.14f;
            return c;
        }
    }
}
