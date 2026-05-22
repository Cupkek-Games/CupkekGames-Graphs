# CupkekGames Graphs

Generic graph-editing foundation for CupkekGames packages. Ships:

- **Runtime abstractions** — `GraphAssetSO`, `GraphNodeSO`, `GraphConnection`, `GraphPortDef` — for any graph-shaped ScriptableObject asset (behaviour trees, navigation graphs, dialogue graphs, quest graphs, ability composition, ...).
- **Custom UI Toolkit canvas editor** — pan/zoom, multi-select, marquee, drag-to-connect, copy/paste, undo/redo, search-driven node creation, sticky notes, group regions, validation footer. **No `UnityEditor.Experimental.GraphView` dependency** — built directly on UI Toolkit primitives (`VisualElement`, `Painter2D`, manipulators).

See `Documentation/GRAPHS_PACKAGE_DESIGN.md` (lives in `com.cupkekgames.luna` during design phase; moves here when the package fully lands) for the architecture, the locked design decisions, and the implementation phasing.

## Status

- **v0.1.0** — Phase 1: runtime types + package skeleton. Editor canvas not yet implemented.

## Consumers (planned)

- `com.cupkekgames.behaviourtrees` — refactor to consume Graphs in Phase 4 of the package's roll-out.
- `com.cupkekgames.luna` navigation — `NavigationGraphSO` in Phase 5.

## Dependencies

- `com.cupkekgames.data` — for `SerializedGuid`.
