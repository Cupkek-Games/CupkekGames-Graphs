# CupkekGames Graphs

Generic graph-editing foundation for CupkekGames packages. Ships:

- **Runtime abstractions** — `GraphAssetSO`, `GraphNodeSO`, `GraphConnection`, `GraphPortDef` — for any graph-shaped ScriptableObject asset (behaviour trees, navigation graphs, dialogue graphs, quest graphs, ability composition, ...).
- **Custom UI Toolkit canvas editor** — pan/zoom, multi-select, marquee, drag-to-connect, copy/paste, undo/redo, search-driven node creation, sticky notes, group regions, validation footer. **No `UnityEditor.Experimental.GraphView` dependency** — built directly on UI Toolkit primitives (`VisualElement`, `Painter2D`, manipulators).

The original design doc (`GRAPHS_PACKAGE_DESIGN.md` — architecture, locked decisions, implementation phasing) was removed in the 2026-06-11 luna docs purge; recover it from the luna repo's git history if needed (`git log --diff-filter=D -- Documentation/GRAPHS_PACKAGE_DESIGN.md`).

## Status

- Runtime types **and** the full UI Toolkit canvas editor are implemented and shipping — pan/zoom, drag-to-connect (incl. drag-into-empty-space node creation), copy/paste, undo/redo, search, sticky notes, groups, validation, node collapse, `[NodeGroup]` field sections, and a play-mode runtime overlay. Consumed by `com.cupkekgames.behaviourtrees` and `com.cupkekgames.luna` navigation.

## Consumers (planned)

- `com.cupkekgames.behaviourtrees` — refactor to consume Graphs in Phase 4 of the package's roll-out.
- `com.cupkekgames.luna` navigation — `NavigationGraphSO` in Phase 5.

## Dependencies

- `com.cupkekgames.data` — for `SerializedGuid`.
