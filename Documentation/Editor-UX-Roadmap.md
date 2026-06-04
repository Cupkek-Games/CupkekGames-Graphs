# Graph Editor UX Roadmap — node header status + canvas QoL

Scope: `com.cupkekgames.graphs` (the central editor) plus its `com.cupkekgames.luna` Navigation consumer. Captured 2026-06-04 after a header/UX review.

**The throughline.** The graph editor gives a node a good *static* header (title / subtitle / one color / one icon) but **no status vocabulary, no inline title editing, and no find**. Single-type graphs (every nav node is a `NavNode`) expose this hardest: the generic default `DisplayTitle => GetType().Name` prints the same word on every card, and the one fact that matters — the id / derived role — has nowhere prominent to live. The central fix is a small, reusable **header-status system**; nav is its first and loudest consumer.

Items are ordered by **payoff × reuse** — Phase 1 is the spine everything else leans on. Each phase is independently shippable.

---

## Status — COMPLETE (2026-06-04 → 06-05)

Everything below shipped, compiled green, and (where noted) has test coverage. The base graph editor is now legible at author time *and* runtime, and was audited twice for genericness (a 7-agent sweep + the BT-migration pass).

### Shipped

| Area | What | Commits |
|---|---|---|
| **Phase 1** header-status | badge chips (`NodeBadge`/`DisplayBadges`), inline rename (`CanRename`/`Rename`), per-node validation chip + border, nav `id`-as-title | graphs `7f634a6` · luna `6ba4e15d` |
| **Batch 2** | directed-edge arrowhead (small filled triangle, `DirectedEdges` opt-in), toolbar Find, derived tab-badge + per-channel tint | graphs `74c4076` · luna `d94d3300` |
| **`GraphTopology` de-dup** | promoted `Reaches`/`WouldCreateCycle` + `Adjacency` (Roots/ChildrenOf/ParentOf/PreOrder); `EnforceAcyclic` opt-in; collapsed the nav + BT cycle checks, `AutoLayoutEngine`, BT runtime child-resolution | graphs `95479fe` + tests `85f9c0e` · bt `9ecb394` · luna `fab7a51f` |
| **Zero polling** | Live State tab → event-driven; nav polls nowhere (runtime or editor) | luna `4d49af16` |
| **Legacy removal** | deleted `NavDestinationSO`/`UILayerSO` + editors + 22 sample assets; inspector is Live-State-only | luna `ccac052d` |
| **Multi-host topology merge** | union of per-host topologies → a destination in one graph (e.g. a global Settings on a persistent host) reachable from every scene; **no global-destination type** | luna `99c3763d` + tests `b4e49020` |
| **Arrange tools (2C)** | frame-selected (`F`), align, distribute | graphs `879d77c` |
| **Nav health check (4C / wiring warning)** | cross-graph dup-id detection (catalog bake + `Luna Nav Health Check` menu: dup ids, graphs-not-in-catalog, catalogs-not-registered) | luna `f6877d02` |
| **Id autocomplete (4C)** | `[NavId]` + drawer — sibling-prefix suggestions across all NavGraphs | luna `3062fad3` |
| **Runtime debug overlay** | generic base (`GraphNodeRuntimeState`, `IGraphRuntimeStateSource`, `CreateRuntimeStateSource`, glow + pill render, event-driven) + nav source (live destinations glow) | graphs `cabd9cf` · luna `3039b591` |
| **BT overlay + generalize** | `IGraphRuntimePollable` (continuous sources) + instance-picker UI; BT source (running/ok/fail); deleted the ad-hoc per-node-polling `BTNodeElement` | graphs `bf3d0af` · bt `3089dc8` |

Design docs: [Runtime-Overlay-Design.md](Runtime-Overlay-Design.md) · [NavGraph-MultiHost-Topology.md](../../com.cupkekgames.luna/Documentation/NavGraph-MultiHost-Topology.md).

### Skipped / deferred / declined (and why)

| Item | Verdict | Why |
|---|---|---|
| **3B port labels** | **dropped** (tried) | Always-visible "parent"/"children" labels added then removed on review — unwanted clutter. `GraphPortDef.Label` stays an unused field. |
| **Nav-forest dedups** (`NavTopology` Pass 2, `LunaLayerHost.OrderedForSpawn`) | **declined** | Close reading: Pass 2 needs multi-parent *validation* + the raw `OrderIndex` int (`Adjacency` exposes neither); `OrderedForSpawn` is coupled to `NavTopology.Entry` and **boot-critical** with ~zero code savings. `GraphTopology.PreOrder`/`ParentOf` exist if a future *tested* refactor wants them. |
| **3A sub-graph breadcrumbs** | **deferred** | Pure editor code, but only meaningful once sub-graphs are actually authored/used. Build when nesting is real. |
| **Separate "global destination" type / definition asset** | **rejected (design)** | "Global" = a normal NavGraph on a persistent (`_persistAcrossScenes`) host; opening = `Push(id)`, not containment — Settings is a global *root*, never duplicated. No special type. |
| **Sub-graph composition** (cross-graph references) | **separate non-goal** | The shared-global case is solved by the topology *merge* + one persistent host. Sub-graph references are for *composing reusable sub-flows* (distinct instances) — a different feature, unbuilt. |
| **Cross-graph containment edges** | **unsupported (design)** | Global destinations are roots; per-graph `IsTab`/`ChannelId` stay valid under the union. Not needed. |
| **Runtime overlay polling** | **rejected** | Event-driven (push) instead, with opt-in `IGraphRuntimePollable` only for continuous sources (BT). No idle work. |
| **"Decorate-on-structure-change" base hook** | **candidate, not built** | `NavGraphCanvas` subscribes `GraphChanged` → `DecorateFromTopology`. Could be a base hook — but each domain subscribing the event is fine; build only when a 2nd domain wants structural decoration. |
| **Generalize `ChannelTint` / `IsTab`-`ChannelId` derivation / `LunaLayerHost` spawn** | **kept domain-local (audited)** | The genericness audit confirmed these are correctly nav-specific — do NOT generalize. |

### Verification still owed (user-side, no code)
Play-mode visual checks: nav overlay (destinations glow on `Push`/`Pop`) and BT overlay (running/ok/fail + the instance dropdown with >1 agent).

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

> **The sections below are the original design detail — all shipped (see the Status table).** Implementation reorganized some numbering: **4A** (derived nav semantics) landed in **Batch 2**; **4B** became "retire the legacy Destinations tab + keep an event-driven Live State" (superseded by the runtime overlay + the new-model `Luna Nav Health Check`); **3B** was dropped. Kept here as the design record + rationale.

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

## Original sequence (historical)

This was the planned order — Phase 1 (header-status spine) → Phase 2 (canvas polish) → Phases 3/4 after the nav migration settled. It held up: Phase 1 shipped as one slice, Batch 2 followed, the `GraphTopology` de-dup + topology merge + runtime overlay came out of the audit/architecture work, and the legacy-asset removal unblocked the nav-inspector simplification. See the **Status** table at the top for what actually landed and **Skipped / deferred / declined** for what didn't.
