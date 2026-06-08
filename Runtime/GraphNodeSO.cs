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

        /// <summary>
        /// Editor-only: whether this node is collapsed on the canvas (body +
        /// subtitle hidden, header + badges + ports kept so edges stay glued).
        /// Like <see cref="Position"/>, it is NOT serialized into the graph
        /// asset — it lives in the <c>GraphLayout</c> sidecar keyed by Guid, so
        /// display churn stays out of the structural asset.
        /// </summary>
        [System.NonSerialized] public bool Collapsed;

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

        /// <summary>
        /// Optional status chips rendered on the header's trailing edge — the
        /// per-node status vocabulary a single title / subtitle / color / icon
        /// can't express. Null or empty hides the chip row. Keep these
        /// <b>node-local</b> (readable from this node alone); derived /
        /// cross-node facts belong on the canvas, not here.
        /// </summary>
        public virtual IReadOnlyList<NodeBadge> DisplayBadges => null;

        /// <summary>
        /// When true, the editor lets the user rename this node inline by
        /// double-clicking its header title. Default false (opt-in) — a node
        /// whose <see cref="DisplayTitle"/> is computed from something other
        /// than the subasset name should also override <see cref="Rename"/>.
        /// </summary>
        public virtual bool CanRename => false;

        /// <summary>
        /// Apply an inline-rename commit. The editor wraps the call in an
        /// <c>Undo.RecordObject</c> on this node + <c>SetDirty</c>, so an
        /// override may write a serialized field directly. Default writes the
        /// subasset <c>name</c> (which the default <see cref="DisplayTitle"/>
        /// reflects).
        /// </summary>
        public virtual void Rename(string title) => name = title;

        /// <summary>Override to declare input ports. Default: one anonymous input.</summary>
        public virtual IReadOnlyList<GraphPortDef> InputPorts => GraphPortDef.SingleAnonymous;

        /// <summary>Override to declare output ports. Default: one anonymous output.</summary>
        public virtual IReadOnlyList<GraphPortDef> OutputPorts => GraphPortDef.SingleAnonymous;

        /// <summary>
        /// When true (default), <c>NodeElement</c> renders the node's
        /// serialized fields directly inside the card body as
        /// <c>PropertyField</c>s — flat, or grouped into collapsible
        /// <c>Foldout</c> sections when the fields carry <c>[NodeGroup]</c>.
        /// Set to false for nodes whose visual body is owned by a custom
        /// <c>NodeElement</c> subclass (e.g. sticky notes) — the inline
        /// auto-inspector would collide with their hand-built layout.
        /// </summary>
        public virtual bool ShowInlineProperties => true;

        /// <summary>
        /// Preferred card width in canvas-world pixels. With editing moved to
        /// the docked inspector panel the on-canvas card is a compact chip
        /// (title + badges + ports), so the default is a slim 180 — wide enough
        /// for a title and the trailing badge row, narrow enough that the canvas
        /// stays a scannable map. Types that still render an inline body can
        /// override wider.
        /// </summary>
        public virtual float PreferredWidth => 180f;
    }
}
