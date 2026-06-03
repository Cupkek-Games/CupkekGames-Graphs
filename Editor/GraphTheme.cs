using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Central color palette for every editor surface. Single place to
    /// retune the look of nodes / edges / panels / validation states.
    /// Replaces the ~50 inline <c>new Color(...)</c> literals previously
    /// scattered across the editor.
    ///
    /// <para>
    /// Naming: semantic, not physical. <see cref="SelectionAccent"/> is
    /// "the color we use for selection indicators" — currently a soft
    /// blue, but a single edit here recolors marquee, edge-selected,
    /// node-selected outline, group-selected outline, etc., in one place.
    /// </para>
    /// </summary>
    public static class GraphTheme
    {
        // ─── Backdrops ─────────────────────────────────────────────────
        // "Lighten with elevation": the canvas is the darkest plane;
        // chrome (SurfaceElevated) sits above it; node bodies (Surface)
        // are lighter still. Grid lines are now opaque near-black tints
        // (was white-with-alpha) so they read as faint texture on the
        // dark backdrop rather than washing it out.
        public static readonly Color CanvasBackdrop = new Color(0.12f, 0.12f, 0.14f);
        public static readonly Color GridLineMinor  = new Color(0.075f, 0.075f, 0.085f);
        public static readonly Color GridLineMajor  = new Color(0.11f, 0.11f, 0.13f);

        // ─── Surfaces (panels / nodes / chrome) ────────────────────────
        // Ordering by elevation (darkest → lightest): CanvasBackdrop <
        // SurfaceElevated (toolbar / status-bar chrome) < Surface (node
        // body). SurfaceElevated used to be *darker* than Surface — fixed.
        public static readonly Color Surface          = new Color(0.22f, 0.23f, 0.26f);
        public static readonly Color SurfaceElevated  = new Color(0.17f, 0.17f, 0.20f);
        public static readonly Color SurfaceTinted    = new Color(0.17f, 0.17f, 0.20f, 0.96f);
        public static readonly Color Separator        = new Color(0.08f, 0.08f, 0.10f);
        public static readonly Color SeparatorLine    = new Color(0.30f, 0.30f, 0.34f);

        /// <summary>
        /// Soft card border for node cards — lighter than the node body so
        /// the card edge reads as a gentle bevel rather than the near-black
        /// <see cref="Separator"/> hairline it replaces on node chrome.
        /// </summary>
        public static readonly Color NodeBorder = new Color(0.34f, 0.35f, 0.40f);

        // ─── Text ──────────────────────────────────────────────────────
        public static readonly Color TextPrimary   = new Color(0.90f, 0.90f, 0.92f);
        public static readonly Color TextSecondary = new Color(0.85f, 0.86f, 0.88f);
        public static readonly Color TextSubtitle  = new Color(0.75f, 0.78f, 0.82f);
        public static readonly Color TextHint      = new Color(0.60f, 0.62f, 0.66f);

        // ─── Selection (used by marquee, node outline, edge, group) ────
        public static readonly Color SelectionAccent       = new Color(0.55f, 0.85f, 1f);
        public static readonly Color SelectionAccentSoft   = new Color(0.55f, 0.85f, 1f, 0.15f);
        public static readonly Color SelectionAccentMid    = new Color(0.55f, 0.85f, 1f, 0.85f);
        public static readonly Color SelectionAccentStrong = new Color(0.55f, 0.85f, 1f, 0.95f);

        // ─── Semantic accents ─────────────────────────────────────────
        /// <summary>Outline + future pill fill for start-of-graph nodes (green).</summary>
        public static readonly Color StartAccent     = new Color(0.45f, 0.85f, 0.50f);
        /// <summary>Drag-preview edges + minimap viewport ring (warm amber).</summary>
        public static readonly Color Attention       = new Color(1f, 0.85f, 0.40f, 0.95f);

        // ─── Ports ─────────────────────────────────────────────────────
        public static readonly Color PortInput  = new Color(0.55f, 0.70f, 0.95f);
        public static readonly Color PortOutput = new Color(0.60f, 0.90f, 0.65f);
        /// <summary>
        /// Idle-hover ring for ports — a neutral cool near-white, deliberately
        /// NOT green (StartAccent/candrop) or red (incompatible) so idle hover
        /// is never confused with an armed drop target. USS mirror: rgb(217,224,242).
        /// </summary>
        public static readonly Color PortHoverRing = new Color(0.85f, 0.88f, 0.95f);

        // ─── Edges ─────────────────────────────────────────────────────
        public static readonly Color EdgeDefault  = new Color(0.78f, 0.80f, 0.85f, 0.85f);
        public static readonly Color EdgeSelected = new Color(0.55f, 0.85f, 1f, 1f);
        /// <summary>Idle-hover tint for a resting wire (lighten toward white, opaque).</summary>
        public static readonly Color EdgeHover    = new Color(0.92f, 0.94f, 0.98f, 1f);
        /// <summary>Fill for the endpoint "grab handle" dots shown on edge hover.</summary>
        public static readonly Color EdgeEndpointHandle = new Color(0.92f, 0.94f, 0.98f, 1f);

        // ─── Group regions ────────────────────────────────────────────
        public static readonly Color GroupBorder            = new Color(0f, 0f, 0f, 0.30f);
        public static readonly Color GroupTitleBg           = new Color(0f, 0f, 0f, 0.20f);
        public static readonly Color GroupTitleBgHover      = new Color(0f, 0f, 0f, 0.28f);
        public static readonly Color GroupResizeHandle      = new Color(1f, 1f, 1f, 0.35f);
        public static readonly Color GroupResizeHandleHover = new Color(1f, 1f, 1f, 0.55f);

        // ─── Sticky notes ─────────────────────────────────────────────
        public static readonly Color StickyDragHandle = new Color(0f, 0f, 0f, 0.10f);

        // ─── Minimap ──────────────────────────────────────────────────
        public static readonly Color MinimapBackground = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color MinimapBorder     = new Color(1f, 1f, 1f, 0.20f);

        // ─── Toolbar toggles ──────────────────────────────────────────
        public static readonly Color ToggleChecked   = new Color(0.35f, 0.62f, 1f);
        public static readonly Color ToggleUnchecked = new Color(0.22f, 0.23f, 0.27f);
        public static readonly Color ToggleBorder    = new Color(0.45f, 0.45f, 0.50f);

        // ─── Interaction overlays ─────────────────────────────────────
        /// <summary>Shared additive-white hover overlay for chrome rows / toggles.</summary>
        public static readonly Color HoverOverlay = new Color(1f, 1f, 1f, 0.06f);

        // ─── Validation footer ────────────────────────────────────────
        public static readonly Color ValidationBackdrop = new Color(0.18f, 0.16f, 0.13f);
        public static readonly Color ValidationHeader   = new Color(1f, 0.78f, 0.30f);
        public static readonly Color ValidationError    = new Color(1f, 0.42f, 0.42f);
        public static readonly Color ValidationWarning  = new Color(1f, 0.83f, 0.40f);
        public static readonly Color ValidationInfo     = new Color(0.65f, 0.85f, 1f);
        public static readonly Color ValidationRowHover = HoverOverlay;
    }
}
