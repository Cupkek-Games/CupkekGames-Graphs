# Graph Runtime Debug Overlay — design

A generic, opt-in way to light up a graph's nodes with **live runtime state** while the editor window is open in play mode — so any graph (nav, behaviour-trees, state-machines, dialogue) gets debug visuals with a tiny per-domain adapter. Locked design 2026-06-04 (sourcing model: **layered — stable render contract + pluggable source**).

## Principle: separate *rendering* from *sourcing*

The mistake to avoid: assuming one runtime instance per asset. That's true for **nav** (a single `LunaUIManager.Navigation`), but false for the graphs that benefit most — one behaviour-tree asset is run by many agents at once, each with its own "current node." So:

- **How a live node looks** (glow + badge) is generic and stable → lives in the base package.
- **Where the state comes from** (singleton poll vs. per-instance vs. event stream) is domain-variable and is where instance-multiplicity lives → a pluggable source.

Everything here is `#if UNITY_EDITOR` (interface, struct, hook, sources) so no debug code ships in player builds — same pattern as `GraphAssetSO.EditorAssetMutated`.

---

## 1. Render contract (base, stable)

```csharp
// graphs runtime, #if UNITY_EDITOR — the per-node live state the canvas renders.
public readonly struct GraphNodeRuntimeState
{
    public readonly Color? Glow;       // halo/border tint (null = no glow)
    public readonly NodeBadge? Badge;  // optional live pill (reuses NodeBadge)
    public GraphNodeRuntimeState(Color? glow, NodeBadge? badge) { Glow = glow; Badge = badge; }
}
```

- `NodeElement.SetRuntimeState(GraphNodeRuntimeState?)` — a new decoration that drives:
  - a **glow border tier** in `UpdateBorder` (priority: selection › validation › **runtime** › search › default — runtime sits high because it's a live, transient signal you're actively watching), and
  - a **runtime badge** in `RebuildBadges` (prepended, ahead of the node's own + extra badges).
- `GraphCanvas.ApplyRuntimeStates(map)` / `ClearRuntimeStates()` — fan a `node→state` map onto the live elements (same shape as `ApplyNodeValidation`).

This layer knows nothing about where state comes from.

## 2. Source (pluggable, domain-supplied)

```csharp
// graphs runtime, #if UNITY_EDITOR
public interface IGraphRuntimeStateSource
{
    bool IsLive { get; }  // is there a running instance to read? false → overlay off + cleared
    bool TryGetState(GraphNodeSO node, out GraphNodeRuntimeState state);
    event Action Changed; // raised when live state changes → canvas re-applies. NO base poll.
}

// Additive capability — implemented LATER by multi-instance sources (BT/SM).
// If a source also implements this, the canvas shows an instance picker; nav
// (singleton) does NOT implement it, so nothing extra renders. This is how the
// minimal hook "shapes for instance-selection later" without base churn now.
public interface IGraphRuntimeInstanceSelector
{
    IReadOnlyList<string> InstanceLabels { get; }
    int SelectedInstance { get; set; }   // canvas writes on picker change
}
```

- `GraphAssetSO.CreateRuntimeStateSource()` → `IGraphRuntimeStateSource` (default `null`; `#if UNITY_EDITOR`). The domain asset builds its source.

## 3. Update model — event-driven (no base poll)

Why not poll: the overlay is editor-only + play-mode-only, but a 5×/sec sample still does idle work (re-`CaptureSnapshot` + diff every node) when nothing changed. Nav state only changes on discrete `Push`/`Pop`/`SwitchChannel` ops we own, so the source **pushes**:

- On entering play, `GraphCanvas` asks `Asset.CreateRuntimeStateSource()`, subscribes `source.Changed`, and applies once. On exiting play (`EditorApplication.playModeStateChanged`), it unsubscribes + `ClearRuntimeStates`.
- On each `Changed` (and the initial apply): `if (source.IsLive)` build the `node→state` map via `TryGetState` → `ApplyRuntimeStates`; else `ClearRuntimeStates`. **No timer.** Idle = zero work.
- If `source is IGraphRuntimeInstanceSelector sel`, draw a small corner dropdown (`GraphFloatingPanel`) bound to `sel`; changing it raises `Changed`.
- A domain whose state has no natural change signal MAY self-tick inside its own source and raise `Changed` — that's the source's choice, never the base's. (Our domains — nav, BT, SM — all own their runtime and can emit events.)

## 4. Nav's first source (singleton-pull)

```csharp
// luna, #if UNITY_EDITOR — implements ONLY IGraphRuntimeStateSource (nav is a singleton).
// NavGraphSO.CreateRuntimeStateSource() returns this.
//   IsLive            = LunaUIManager.Instance?.Navigation != null
//   Changed           = forwards the EXISTING LunaNavigationStack.DestinationChanged
//                       + ChannelChanged events (raised on every mutation already)
//   TryGetState(node) = match NavNode.Id against CaptureSnapshot():
//       on top of a stack   -> Glow = green,  Badge = "live"
//       on a stack, occluded -> Glow = dim,    Badge = "stacked"
//       not on any stack     -> no state (false)
```

- **No new runtime code needed:** the stack *already* raises `DestinationChanged` + `ChannelChanged` (and `OnPushed`/`OnPopped`) synchronously on every mutation ([LunaNavigation.cs:581-606](../../com.cupkekgames.luna/Runtime/Scripts/Navigation/LunaNavigation.cs)). The source just subscribes to those. (Audit-confirmed: my earlier "add a `LunaNavigationStack.Changed` event" was unnecessary.) The existing **Live State tab can drop its 5×/sec poll** the same way.
- The boot-time `StartVisible` seeding goes through `Push`, so the overlay populates on boot with no special-casing.
- Matching is by **id**, so the editor's working clone vs. the real runtime asset is a non-issue (the snapshot is keyed by id — the same data the Live State tab reads).

## 5. Future sources (no base change)

- **Behaviour-trees / state-machines:** a source implementing **both** interfaces — `IGraphRuntimeInstanceSelector` lists live agents, `TryGetState` returns the selected agent's running/succeeded/failed node. The base poll + render + picker already handle it.
- **Push variant (optional):** a runner could report `(graph, node, state)` to an editor static that backs an `IGraphRuntimeStateSource`; same contract, different plumbing. Not needed for nav.

---

## Phasing

1. **Base render contract** — `GraphNodeRuntimeState`, `NodeElement.SetRuntimeState`, `GraphCanvas.ApplyRuntimeStates/Clear`, the `UpdateBorder` glow tier + badge slot.
2. **Base poll loop** — play-mode tick + `CreateRuntimeStateSource` hook + optional instance-picker panel.
3. **Nav source** — `NavRuntimeStateSource` + `NavGraphSO.CreateRuntimeStateSource()`; visual mapping above.
4. (later) BT/SM instance-selecting sources — no base change.

## Touch list

- graphs: `Runtime/GraphNodeRuntimeState.cs` (new), `Runtime/IGraphRuntimeStateSource.cs` (new, + selector iface), `Runtime/GraphAssetSO.cs` (hook), `Editor/Nodes/NodeElement.cs` (`SetRuntimeState` + border tier + badge), `Editor/Canvas/GraphCanvas.cs` (play-mode subscribe/apply/clear + picker — **no timer**).
- luna: `Runtime/Scripts/Navigation/NavRuntimeStateSource.cs` (new, subscribes existing `DestinationChanged`/`ChannelChanged` — no runtime changes), `NavGraphSO.cs` (hook override), optionally `LunaNavInspectorWindow.cs` (Live State tab → subscribe to the same events instead of the `.Every(200)` poll).
