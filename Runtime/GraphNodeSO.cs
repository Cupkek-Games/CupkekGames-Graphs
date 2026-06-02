using System;
using System.Collections.Generic;
using UnityEngine;
using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Abstract base for any node inside a <see cref="GraphAssetSO"/>.
    /// Stored as a subasset of the parent graph; carries identity, canvas
    /// position, and optional display metadata. Subclasses add domain data
    /// and runtime behaviour.
    /// </summary>
    public abstract class GraphNodeSO : ScriptableObject
    {
        [HideInInspector] public SerializedGuid Guid = new SerializedGuid(System.Guid.NewGuid());

        // Canvas position is NOT serialized into the graph asset — it lives in the
        // GraphLayout sidecar (Foo.layout.asset), keyed by Guid, so layout churn doesn't
        // collide with structural edits in source control. In-memory only; the editor
        // loads it from the sidecar on bind and writes it back on save. (Trade-off:
        // node-move Undo is not captured, and pre-sidecar graphs reset to 0 — re-run
        // Auto Layout once to seed a sidecar.)
        [System.NonSerialized] public Vector2 Position;

        /// <summary>Title rendered in the node header. Defaults to the type name.</summary>
        public virtual string DisplayTitle => GetType().Name;

        /// <summary>Small subtitle line under the title. Null hides the subtitle row.</summary>
        public virtual string DisplaySubtitle => null;

        /// <summary>Tint applied to the header strip. Default is muted grey.</summary>
        public virtual Color HeaderColor => new Color(0.35f, 0.38f, 0.45f);

        /// <summary>
        /// Optional icon glyph (Material icon code or single character) shown
        /// in the header. Null hides the icon slot.
        /// </summary>
        public virtual string IconGlyph => null;

        /// <summary>Override to declare input ports. Default: one anonymous input.</summary>
        public virtual IReadOnlyList<GraphPortDef> InputPorts => GraphPortDef.SingleAnonymous;

        /// <summary>Override to declare output ports. Default: one anonymous output.</summary>
        public virtual IReadOnlyList<GraphPortDef> OutputPorts => GraphPortDef.SingleAnonymous;

        /// <summary>
        /// When true (default), <c>NodeElement</c> renders the node's
        /// serialized fields directly inside the card body via UI Toolkit's
        /// <c>InspectorElement</c>. Set to false for nodes whose visual body
        /// is owned by a custom <c>NodeElement</c> subclass (e.g. sticky
        /// notes) — the inline auto-inspector would collide with their
        /// hand-built layout.
        /// </summary>
        public virtual bool ShowInlineProperties => true;

        /// <summary>
        /// Preferred card width in canvas-world pixels. Cards grow vertically
        /// with content; width is the per-type knob for "how much room do my
        /// fields need." Defaults to 240 — narrow enough to keep the canvas
        /// scannable, wide enough for most label + field rows.
        /// </summary>
        public virtual float PreferredWidth => 240f;
    }
}
