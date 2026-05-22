# Graphs / BT / Nav — Cleanup Plan

Scope: `com.cupkekgames.graphs`, `com.cupkekgames.behaviourtrees`, and `com.cupkekgames.luna/Runtime/Scripts/Navigation`. Captured after a review pass on 2026-05-16.

Items are ordered by **risk × payoff** — start at the top, each step makes the next easier.

---

## Item 6 — Drop `EditorSetStartDestinationGuid`, use `SerializedProperty` directly

**Status**: ready, tiny.

**Problem.** `LunaNavigationGraphSO` exposes `public void EditorSetStartDestinationGuid(SerializedGuid)` (guarded `#if UNITY_EDITOR`) so the inspector can mutate the guid through a typed API. That mixes runtime SO surface with an editor-only concern — anyone can call it; the `Editor` prefix is a convention not an enforcement.

**Fix.** Editor code reaches the guid via `SerializedProperty` — handles Undo + SetDirty automatically:

```csharp
var prop = serializedObject.FindProperty("_startDestinationGuid");
prop.FindPropertyRelative("ValueStr").stringValue = chosen.Guid.ValueStr;
serializedObject.ApplyModifiedProperties();
```

Then **delete** `EditorSetStartDestinationGuid` from `LunaNavigationGraphSO`.

**Touch list:** `LunaNavigationGraphSO.cs`, `LunaNavigationGraphSOEditor.cs`.

---

## Item 7+8 — Replace polls with a generic mutation event

**Status**: ready, small.

**Problem.** Two pollers exist for the same reason (off-canvas mutations need to refresh the canvas):
- `GraphCanvas.PollStartGuid` — 250 ms tick, every graph window, even on graphs that never set a start.
- `ValidationFooter.PollIfChanged` — 500 ms tick, every graph window, signature-compare for any inspector edit.

Both burn cycles on healthy graphs.

**Fix.** Add a generic mutation-notification channel in `graphs/Runtime`:

```csharp
public abstract class GraphAssetSO : ScriptableObject
{
#if UNITY_EDITOR
    public static event Action<GraphAssetSO> EditorAssetMutated;
    public void EditorRaiseMutated() => EditorAssetMutated?.Invoke(this);
#endif
}
```

Inspector calls `target.EditorRaiseMutated()` after `ApplyModifiedProperties`. `GraphCanvas` and `ValidationFooter` subscribe to the static event, filter by `asset == this.Asset`, refresh. Both pollers deleted.

**Touch list:** `GraphAssetSO.cs`, `GraphCanvas.cs` (remove poll, subscribe), `ValidationFooter.cs` (remove poll, subscribe), `LunaNavigationGraphSOEditor.cs` (raise after edit), plus any future domain inspector that mutates a graph asset.

**Net diff:** roughly +20 / −60.

---

## Item 3 — Promote start-node picker to graphs editor helper

**Status**: ready, small.

**Problem.** `LunaNavigationGraphSOEditor.DrawStartPicker` reinvents work that any domain with a `StartNodeGuid` would need (dialogue graphs, quest graphs, future N).

**Fix.** Add `Graphs.Editor.StartNodePickerGUI`:

```csharp
public static class StartNodePickerGUI
{
    /// Draws a "Start Node" enum dropdown listing every node in the
    /// graph that passes `filter`, labelled by `labelOf`. Mutates the
    /// graph's _startDestinationGuid via SerializedProperty (handles
    /// Undo automatically).
    public static void Draw(
        SerializedObject serializedObject,
        GraphAssetSO graph,
        Func<GraphNodeSO, bool> filter = null,
        Func<GraphNodeSO, string> labelOf = null);
}
```

`LunaNavigationGraphSOEditor` becomes a one-liner:

```csharp
StartNodePickerGUI.Draw(
    serializedObject, target,
    filter: n => n is LunaNavDestinationSO,
    labelOf: n => ((LunaNavDestinationSO)n).DestinationId);
```

BT stays unaffected — `BehaviourTree` uses `_rootNode` (auto-created, structural), not a picker.

**Touch list:** new `Graphs.Editor.StartNodePickerGUI.cs`, simplified `LunaNavigationGraphSOEditor.cs`.

---

## Item 1+2 — **Delete the BlackboardVariable family entirely**

**Status**: ready, medium. Bigger than it looks because it touches multiple files, but no real code is lost — what's there is unused.

### What does `BlackboardVariable` do today?

It's a polymorphic-by-value authoring shape for graph-level typed defaults:

| Type | Purpose |
|---|---|
| `BlackboardVariable` (abstract) | Base class with `Name` field + abstract `Type` / `GetDefaultValue()`. |
| `IntBlackboardVariable`, `FloatBlackboardVariable`, `BoolBlackboardVariable`, `StringBlackboardVariable`, `Vector2BlackboardVariable`, `Vector3BlackboardVariable`, `ObjectReferenceBlackboardVariable` | One concrete subclass per supported value type. |
| `IBlackboardOwner` | Interface: "this graph asset has a list of BlackboardVariables". |
| `GraphAssetSO._blackboardVariables` | `[SerializeReference] List<BlackboardVariable>` field on every graph asset. |
| `BlackboardVariableReference<T>` | Generic struct holding a string name; resolves against a `GraphFrame` at runtime. The "typed reference into the blackboard" abstraction. |
| `BlackboardSeeder.Apply(owner, blackboard)` | Static helper: copy each declared variable's default into a runtime `GraphBlackboard`. |
| `BlackboardVariableDrawer` (editor) | `PolymorphicReferenceDrawer<BlackboardVariable>` — Inspector dropdown picker. |
| `BlackboardVariableReferenceDrawer` (editor) | Dropdown of declared variables when authoring a `BlackboardVariableReference<T>` field. |
| `BlackboardDrawerPanel` (editor) | Left-side drawer on the graph canvas that lists / edits `_blackboardVariables`. |

### Actual usage — verified via grep across all packages

| Symbol | Real consumers |
|---|---|
| `BlackboardVariable` concretes (Int/Float/Bool/etc.) | **None.** No BT node, no editor code outside the drawers, no scene/asset authors any. |
| `BlackboardVariableReference<T>` | **None.** Defined and drawn, never referenced as a field type. |
| `BlackboardSeeder.Apply` | **One** call site: `BehaviourTreeRunner` ctor. With empty `BlackboardVariables`, it's a no-op. |
| `IBlackboardOwner` | Implemented by `GraphAssetSO`. Only consumer is `BlackboardSeeder.Apply`. |
| `_blackboardVariables` field | **Empty** on every graph asset that exists. `BlackboardDrawerPanel` lets the user author entries, but nothing reads them at runtime. |

### Why this is dead infrastructure

Originally pitched as a typed authoring layer for BT (and any future graph), but **no BT node ever consumed it**. BT nodes that wanted typed runtime values either hardcoded them or read from agent components. The drawer + reference machinery shipped without any consumer to validate the shape.

### Why we shouldn't just "move it to BT"

Even if we moved the files, BT still has no consumer. The data package's idiom (`CatalogKey + [CatalogKeyConstraint]` for authored typed values on a node's own fields) is the better path for any future "BT node wants an authored typed value" need — typed-by-field via attribute, not polymorphic-by-value. Captured in MEMORY.md as `[static-vs-content + DataSO axes]`.

### The right fix: delete everything in the family

**Delete from `com.cupkekgames.graphs/Runtime`:**
- `BlackboardVariable.cs`
- `BlackboardVariableReference.cs`
- `BlackboardSeeder.cs`
- `IBlackboardOwner.cs`

**Delete from `com.cupkekgames.graphs/Editor/Misc`:**
- `BlackboardVariableDrawer.cs`
- `BlackboardVariableReferenceDrawer.cs`
- `BlackboardDrawerPanel.cs`

**Edit `GraphAssetSO.cs`:**
- Drop the `[SerializeReference] List<BlackboardVariable> _blackboardVariables` field.
- Drop `IBlackboardOwner` from the inheritance list.
- Drop the `public IReadOnlyList<BlackboardVariable> BlackboardVariables` accessor.

**Edit `BehaviourTreeRunner.cs`:**
- Drop the `BlackboardSeeder.Apply(_originalTree, Blackboard)` call. The runner's `Blackboard` stays — that's still the runtime key/value store nodes use. Just no longer auto-seeded from authored variables (because nothing authors any).

**Edit `GraphEditorWindow.cs`:**
- Wherever it mounts `BlackboardDrawerPanel`, drop the mount.

**Edit `Documentation/GRAPHS_PACKAGE_DESIGN.md`:**
- Drop any references to the blackboard variable schema as a foundation feature. Mention `GraphBlackboard` / `GraphFrame` as the runtime types; remove the BlackboardVariable shape entirely.

### What stays (and why)

| Type | Why it stays |
|---|---|
| `GraphBlackboard` | Generic runtime key/value store. Used by nav (per-push args) and BT (execution-wide globals). Has real consumers. |
| `GraphFrame` | Hierarchical scopes for BT parallel branches. Used by BT runner. Has a real consumer. |

### Nav-side ask: "views can use global per nav graph variables if they want?"

Yes — two paths, neither requires the BlackboardVariable family:

**Path A (recommended, ship as needed)** — `LunaNavigationManager` already has `CurrentArgs` (per-push). Add a sibling `public GraphBlackboard Globals { get; } = new();` populated however the consumer wants (set in code on Start, or via a small authored-seed list on the graph asset). Screens read `nav.Globals["theme"]` etc.

**Path B (heavier, defer)** — If designers need to author typed defaults, add a per-screen config SO pattern (already documented as the recommendation) — typed fields on a plain `ScriptableObject`, passed as the per-push arg under a known key.

No global state today. Build path A only when a real consumer asks.

### Migration risk

Low — there's no caller to break. `BlackboardSeeder.Apply` is the only runtime touch and its current effect is "no-op over an empty list". The editor panel + drawers are pure UI affordances for a thing that has no consumer.

### Net diff

Estimated **−800 / +0** across the seven file deletions + the small edits in `GraphAssetSO` / `BehaviourTreeRunner` / `GraphEditorWindow`. Big win for "is anything here actually used" comprehension.

---

## Item 4 — `LunaNavigationManager` redesign

**Status**: planned only, defer execution.

**Problem.** 381 lines doing four jobs: singleton + index/instantiation + stack ops + Unity-side fade glue.

**Proposed split:**

| File | Responsibility | LoC est. |
|---|---|---|
| `LunaNavigationManager.cs` | MonoBehaviour singleton, lifecycle, public API (Push/Pop/etc.), holds the stack + index + bridges. | ~180 |
| `NavStackEntry.cs` | The internal record (DestinationId, Instance, Args, EscapeKey, ParentKeptVisible). | ~30 |
| `NavInstanceCache.cs` | Pure helper. `Build(graph) → Dictionary<string, LunaNavDestinationSO>`. `GetOrInstantiate(id) → GameObject`. Owns `_byId` + `_instances` + the eager/lazy instantiation logic. | ~80 |
| `NavInstanceBridge.cs` | Pure helper. `ActivateAndFadeIn(entry)`, `FadeOutAndDeactivate(entry, onDone)`, `IsOverlay(instance)`. Holds nothing — just Unity glue. | ~70 |

Manager keeps Push/Pop/etc., delegates instantiate-checks to `NavInstanceCache`, delegates fade work to `NavInstanceBridge`. Better testability (bridge can be exercised without spinning up a full manager), better separation of "policy" vs "Unity mechanics".

**Risk.** Inter-class wiring where there's currently a single self-contained file. Worth it only if the manager grows further or a third Unity-mechanics concern lands. **Defer.**

---

## Item 5 — Tabs vs nav, why are they different?

**Status**: documented, no action.

**Genuinely different problems.**
- Nav = linear back-stack of distinct screens. User returns via back/pop.
- Tabs = parallel sections of one screen. User toggles freely. No "back" semantics.

**Implementation-detail differences (less justified).**
- Nav uses prefab + UIDocument + SetActive toggle. Persistent instance.
- Tabs use VTA + `CloneTree` + per-switch lifecycle hooks. Three `TabHostMode` variants (CloneOnSwitch / KeepMounted / PreMountAll).

The tab/VTA path predates the nav redesign. A unified prefab-based world is possible — tabs could become "ReplaceTop(tabId) with no back-stack". But you'd lose the cheap-clone tab body model that fits high-frequency switching.

**Recommendation: keep separate.** Mental models genuinely differ. Sharing infrastructure mostly invents abstractions that fit neither well.

---

## Suggested execution order

1. **Item 6** — Drop `EditorSetStartDestinationGuid`, switch the picker to `SerializedProperty`. **Touch:** 2 files. **Time:** 5 min.
2. **Item 7+8** — Add `GraphAssetSO.EditorAssetMutated` event, delete both pollers, raise from inspector. **Touch:** 4 files. **Time:** 15 min.
3. **Item 3** — Promote start-node picker to `Graphs.Editor.StartNodePickerGUI`. **Touch:** 2 files. **Time:** 15 min.
4. **Item 1+2** — Delete the BlackboardVariable family + reaches. **Touch:** 7 deletes + 3 edits. **Time:** 30 min.
5. *Optionally — Item 4 manager split if pain accumulates. Defer.*
6. *Item 5 — no action needed.*

After 1–4, the graphs / BT / nav surface drops by ~1000 lines and gains one clean event-based refresh mechanism. The runtime API becomes "GraphBlackboard for cross-cutting state, GraphFrame for scopes; that's it" — no polymorphic-value layer, no dead authoring infrastructure.
