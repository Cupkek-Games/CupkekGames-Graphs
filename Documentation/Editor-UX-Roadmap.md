# Graph Editor UX Roadmap — node header status + canvas QoL

Scope: `com.cupkekgames.graphs` (the central editor) plus its `com.cupkekgames.luna` Navigation consumer. Captured 2026-06-04 after a header/UX review.

**The throughline.** The graph editor gives a node a good *static* header (title / subtitle / one color / one icon) but **no status vocabulary, no inline title editing, and no find**. Single-type graphs (every nav node is a `NavNode`) expose this hardest: the generic default `DisplayTitle => GetType().Name` prints the same word on every card, and the one fact that matters — the id / derived role — has nowhere prominent to live. The central fix is a small, reusable **header-status system**; nav is its first and loudest consumer.

Items are ordered by **payoff × reuse** — Phase 1 is the spine everything else leans on. Each phase is independently shippable.

---

## Status

- **Phase 1 — DONE + committed (2026-06-04).** graphs `7f634a6`, luna `6ba4e15d`. Compile-verified green.
- **Batch 2 (4A / 2B / 2A) — IMPLEMENTED (2026-06-04), pending Unity compile-verify.** 3B (port labels) was tried and **dropped** on review — no labels wanted.
- **Batch 3 — scoped, mostly not started:**
  - **Runtime debug overlay** (generic, base package) — DESIGNED, see [Runtime-Overlay-Design.md](Runtime-Overlay-Design.md); sourcing model locked = layered (render contract + pluggable source); build pending.
  - **4C id ergonomics** — locked: node stays the id source; add sibling-prefix autocomplete + cross-catalog uniqueness check. Build pending.
  - **Catalog wiring warning** — locked (warning only): flag a NavGraph not referenced by any registered `NavDestinationCatalog`. Build pending.
  - **2C** frame-selected + align/distribute — locked. Build pending.
  - **4B nav inspector** — retire the legacy `NavDestinationSO` Destinations tab (the runtime overlay supersedes the live-debug need); keep Live State text tab.
- Remaining (3A breadcrumbs) — proposed, not started.

### Decisions locked — Phase 1 (2026-06-04)

| Feature | Decision |
|---|---|
| **1A** Badge style | **Text pills** — short colored word-pills, right-aligned in the header (`NodeBadge { Text, Tint, Tooltip }`). |
| **1B** Rename trigger | **Double-click the header** → inline `TextField`, commit on Enter/blur, cancel on Esc. Opt-in per node via `CanRename`. |
| **1C** Validation cue | **Border tint + chip** — offending card gets a 2px error/warning border AND a leading `!` chip (tooltip = the issue message). |
| **1D** Nav headline | **id as title, no subtitle** — `NavNode.DisplayTitle => _id`; rename writes `_id`; flags shown as node-local badges. |

### Decisions locked — Batch 2 (2026-06-04)

| Feature | Decision | Shape |
|---|---|---|
| **4A** Derived nav semantics | **Tab badges + channel tint** | `NavGraphCanvas` builds `NavTopology` on `GraphChanged`; pushes a derived "tab" badge (`NodeElement.SetExtraBadges`) + a per-channel accent tint (`SetAccentTint`) keyed off `Entry.ChannelId`. |
| **2B** Directed edges | **Midpoint arrowhead (small filled triangle)** | `GraphAssetSO.DirectedEdges` opt-in (NavGraph = true); `EdgeElement` fills a compact triangle at the curve midpoint along the tangent. |
| **2A** Find / jump | **Toolbar search field** | Always-visible `Find` field in `GraphToolbar`; highlights matches (amber border via `SetSearchHighlight`), jumps to first, Enter cycles, Esc clears. |
| **3B** Port labels | **Dropped** | Tried always-visible "parent"/"children" labels; removed on review (`PortElement` label rendering + `NavNode` port labels reverted). `GraphPortDef.Label` stays an unused field. |

---

## Why this, why now (the concrete symptom)

- `GraphNodeSO` display API: [`DisplayTitle`/`DisplaySubtitle`/`HeaderColor`/`IconGlyph`](../Runtime/GraphNodeSO.cs) — one text line, one subtitle, one tint, one glyph. No way to say "I'm a tab **and** start-visible **and** my id is duplicated."
- `NodeElement.RefreshDisplay()` reads exactly those four. A card can't carry status.
- `NavNode` works around the type-name default by setting title = subasset `name`, subtitle = `_id`. But **`name` is never editable from the canvas** and is born as `"NavNode"` (`CreateAndAddNode` sets `node.name = nodeType.Name`). Result: a wall of cards titled "NavNode", with the real id whispered in 10px subtitle text.
- Nav's whole authoring story — tab-ness, channel, parent, seed — is **derived by `NavTopology` at load** and therefore **invisible at author time**. You set `ChildMode = Switched` and must *imagine* the children became tabs.
- The generic green ★ "start node" affordance never fires for nav (a forest with empty `StartNodeGuid`), while nav's real entry concept (`StartVisible`, possibly several) has no cue at all. Wasted visual budget.

---

## Phase 1 — Header status system (central) + nav consumer

The spine. Three central features in `graphs`, then nav cashes them in. Ship as one slice.

### 1A — `DisplayBadges`: a status-chip vocabulary

**Problem.** A node can express one icon + one color + two text lines. Domains need to tag *several* boolean/enum facts at a glance.

**Fix.** A tiny value type + one virtual on the node base; `NodeElement` renders a right-aligned chip row in the header.

```csharp
// Runtime/NodeBadge.cs — new
namespace CupkekGames.Graphs
{
    /// A small status chip shown on a node header's trailing edge.
    public readonly struct NodeBadge
    {
        public readonly string Text;     // very short: "Tab", "seed", "×N", "▒"
        public readonly Color  Tint;     // pill fill
        public readonly string Tooltip;  // optional hover explanation
        public NodeBadge(string text, Color tint, string tooltip = null)
        { Text = text; Tint = tint; Tooltip = tooltip; }
    }
}
```

```csharp
// GraphNodeSO.cs — add next to the other display hooks
/// Optional status chips on the header's trailing edge. Null/empty hides the row.
/// Cheap + node-local only — derived/cross-node facts belong on the canvas (Phase 4).
public virtual IReadOnlyList<NodeBadge> DisplayBadges => null;
```

`NodeElement` integration (the header is already a flex row — [`NodeElement.cs` header build](../Editor/Nodes/NodeElement.cs)):
- Add a `flexGrow:1` spacer + a `_badgeRow` container after `_titleLabel` so chips pin right.
- In `RefreshDisplay()`, clear `_badgeRow` and rebuild one pill per badge (small rounded `Label`, `fontSize 9`, `pickingMode Ignore`, `tooltip` set). Hide the row when the list is null/empty.
- **Refresh-on-edit (important):** today the body's `TrackSerializedObjectValue` callback only calls `_canvas?.NotifyGraphChanged()`. Add a `RefreshDisplay()` call there so editing a field live-updates that card's badges/title/color. (Single-line change in `BuildBody`.)

**Touch list:** `Runtime/NodeBadge.cs` (new), `Runtime/GraphNodeSO.cs`, `Editor/Nodes/NodeElement.cs`. Optional `cgg-graph-node__badge` USS class to match existing inline-style idiom.

### 1B — Inline title rename on the card

**Problem.** There is no way to set a node's display title from the canvas; the only "rename" is editing whatever serialized field the override happens to read, in the body.

**Fix.** Double-click the header title → swap `_titleLabel` for a `TextField`, commit on Enter/blur through Undo. Because some nodes compute their title (not from `name`), route the write through the node so each domain controls what gets renamed:

```csharp
// GraphNodeSO.cs
public virtual bool CanRename => false;          // opt-in
public virtual void Rename(string title) { name = title; }  // default writes the subasset name
```

- `NodeElement`: only wire the double-click handler when `_node.CanRename`. The header handler must `StopPropagation()` so it does **not** collide with the element-level double-click that descends into a sub-graph ([`NodeElement.cs` MouseDownEvent descend](../Editor/Nodes/NodeElement.cs)).
- Commit: `Undo.RecordObject(_node, "Rename Node"); _node.Rename(text); EditorUtility.SetDirty(_node); _canvas?.NotifyGraphChanged(); RefreshDisplay();`

**Touch list:** `Runtime/GraphNodeSO.cs`, `Editor/Nodes/NodeElement.cs`.

### 1C — Per-node validation chip

**Problem.** `GraphAssetSO.Validate()` issues already carry `TargetNodeGuid`, but they only surface in the bottom `ValidationFooter`. A "duplicate id" error isn't shown *on the offending card*.

**Fix.** The canvas already re-validates on `GraphChanged` + `GraphAssetSO.EditorAssetMutated` (the footer's two signals). On the same pass, build a `Dictionary<guidStr, SeverityLevel>` of the worst severity per node and push it to each `NodeElement.SetValidationState(severity)`, which:
- tints the card border red/amber (reuse `GraphTheme.ValidationError/Warning`), and
- shows a ⛔/⚠ badge (a built-in chip, drawn alongside the `DisplayBadges` row).

Keeps validation single-sourced (no per-node `Validate()`), just re-broadcasts the footer's result.

**Touch list:** `Editor/Misc/ValidationFooter.cs` or `Editor/Canvas/GraphCanvas.cs` (whichever owns the validate pass) to fan severities out; `Editor/Nodes/NodeElement.cs` for `SetValidationState`.

### 1D — Nav consumer: title = id, node-local badges

With 1A–1C in place, nav stops fighting the header:

```csharp
// NavNode.cs
public override string DisplayTitle  => string.IsNullOrEmpty(_id) ? "(no id)" : _id;
public override string DisplaySubtitle => /* optional human label, or null */;
public override bool   CanRename       => true;
public override void   Rename(string t) { /* write _id via SerializedProperty for Undo */ }

public override IReadOnlyList<NodeBadge> DisplayBadges
{
    // node-local facts only (no graph context needed):
    //   ChildMode.Switched     -> "⊞ Tabs"
    //   StartVisible           -> "seed"
    //   Backdrop != None       -> "▒"
    //   IsMultiInstance        -> "×N"
    //   Occlusion == Replace   -> "replace"  (Overlay is the quiet default)
    => /* build the list from the above */;
}
```

> **Honest scope line.** "This node *is* a tab" (its parent is `Switched`), its channel, and its parent path are **derived** — the node alone can't know them. Those are **canvas decorations** that need `NavTopology` in the editor; they land in **Phase 4 (N1)**. Phase 1 ships the node-local badges, which already kill the "every card says NavNode" problem and surface tabs/seed/backdrop at a glance.

**Touch list:** `Runtime/Scripts/Navigation/NavNode.cs`.

---

## Phase 2 — Canvas navigation (central polish, no nav coupling)

### 2A — In-graph find / jump (`Ctrl+F`)
`NodeSearchPopup` is **create-only**; the only jump-to-existing path is clicking a validation row. Add a find popup (title/id substring) that reuses the existing `GraphCanvas.FocusOnNode`. High value on big graphs.

### 2B — Directed-edge arrowheads
Edges paint a bare Bezier ([`EdgeElement` paint](../Editor/Edges/EdgeElement.cs)) — no direction marker. For containment ("who contains whom") direction *is* the meaning. Draw a midpoint arrowhead; gate on a `virtual bool DirectedEdges => false` on `GraphAssetSO` so undirected domains opt out. Nav sets it true.

### 2C — Frame-selected + align/distribute
`F` fits the whole view; add "frame selection" (e.g. `F` on a non-empty selection) and align-left / distribute-horizontally context actions. Standard graph hygiene.

---

## Phase 3 — Structure & nesting (central)

### 3A — Sub-graph breadcrumbs
Descend/ascend works but the toolbar shows only the current asset name + a `←` ([`GraphToolbar`](../Editor/Misc/GraphToolbar.cs)). A `Root ▸ Shop ▸ Checkout` trail matters the moment nesting is real (the sub-graph primitive is already in `ISubGraphNode`).

### 3B — Labeled ports
NavNode's "parent-in / children-out" ports are anonymous dots — the "drag here to add a child" affordance isn't discoverable. Surface `GraphPortDef` name/tooltip on the `PortElement`.

---

## Phase 4 — Nav inspector & derived semantics (luna consumer)

### 4A — Render derived semantics on the canvas (N1)
The biggest *nav* win, built on Phase 1's badge/decoration plumbing: badge a `Switched` node's children as tabs, bracket/tint a channel set, mark seed roots. Needs `NavTopology` available to `NavGraphCanvas` at author time.

### 4B — New-model nav inspector (N3)
`LunaNavInspectorWindow`'s **Destinations** tab still scans the legacy `NavDestinationSO`/`UILayerSO` (slated for deletion per the migration guide §7). Replace it with a `NavGraphSO` topology view (derived channels/tabs, catalog bake status, which host mounts which graph). Keep the **Live State** tab — it reads the new stack.

### 4C — Id ergonomics (N4)
Free-string ids with only duplicate detection. Add right-click "Copy id" and dotted-path autocomplete from sibling ids to cut typos the catalog can't catch until runtime.

---

## Suggested sequence

1. **Phase 1** (1A → 1B → 1C → 1D) as one PR — the reusable header-status system + nav payoff.
2. **Phase 2** as independent polish whenever.
3. **Phase 3 / Phase 4** after the nav migration assets settle (Phase 4 depends on the new model being the only one).

Phase 1 is the only phase with cross-package coupling, and it's one-directional (graphs exposes, nav consumes) — no nav change is required for the graphs API to land.
