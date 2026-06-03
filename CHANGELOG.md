# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.1] - 2026-05-29

### Fixed
- Expanding a binder node's **Fields** no longer makes the node grow enormously tall when a field is a
  list (rendered as a ListView, which reports an unbounded height inside a content-sized graph node).
  The Fields body is now a height-capped scroll view, so tall content scrolls instead of inflating
  the node.

## [0.6.0] - 2026-05-29

### Added
- **Find Conflicts** toolbar button: highlights binder nodes that drive the same target and therefore
  fight over its state — e.g. two `ActivateGameObjectBinder`s targeting one GameObject, or
  `EnableBehaviourBinder`s sharing a Behaviour. Detection is generic (any serialized list whose
  element has a `UnityEngine.Object Target`). Offending nodes get a red border + a tooltip naming the
  shared target, and are selected and framed; a notification reports the count. Inside the node, the
  Fields body auto-expands and the **specific list element** referencing the shared target is shown
  and tinted (the offending list is broken out into its individual elements), so you can jump
  straight to the exact reference. Cleared on Refresh.

## [0.5.0] - 2026-05-29

### Changed
- **Graph layout is now committable/shareable.** Node positions and groups are stored in a JSON file
  per view at `Assets/Editor/UiceVisualBinder/GraphLayouts/<hash>.json` (keyed by the root's
  GlobalObjectId) instead of per-user `EditorPrefs`. Commit the file and teammates see the same
  arrangement. Existing local layouts are auto-migrated into a file on first open. (The Fields
  foldout's open/closed state stays in EditorPrefs as local UI preference.)

## [0.4.1] - 2026-05-29

### Fixed
- The expandable "Fields" body in binder nodes now has an opaque background, so it stays readable
  when a node overlaps another instead of showing the node behind it.
- Expanding a previously-collapsed group no longer piles all its member nodes on top of each other;
  members are restored to their original positions.

## [0.4.0] - 2026-05-29

### Added
- **Manual node groups.** Select nodes and **Group Selection** (toolbar or right-click) to wrap them
  in a titled, renamable box that moves as one. Groups, titles, membership, collapsed state and
  positions persist per root.
- **Collapsible groups with proxy edges.** Collapse a group (right-click ▸ Collapse) to hide its
  members and internal wiring behind a compact proxy node; bindings that cross the group boundary are
  re-drawn as proxy edges to ports on that proxy node (including group-to-group), so external
  connections stay visible. Expand via right-click or double-click the proxy.
- Arrange now leaves grouped nodes (and group/proxy boxes) where they are and only auto-arranges the
  ungrouped nodes.

## [0.3.0] - 2026-05-29

### Added
- **Arrange** toolbar button: lays nodes out in left-to-right columns based on their input/output
  connectivity (layered/Sugiyama-style layout) with barycenter crossing reduction. Fully
  unconnected nodes are grouped into a trailing column. The view is framed to fit afterwards, and
  the arranged positions are persisted.
- Hierarchies opened with no saved layout are now auto-arranged on first open, so complex existing
  views appear organized immediately instead of needing manual tidying.

## [0.2.0] - 2026-05-29

### Added
- Binder nodes now display the binder's non-binding serialized fields (e.g. `WidgetBinder.widget`,
  `SpawnPrefabBinder`'s prefab/container config) as editable `PropertyField`s in a collapsible
  "Fields" foldout in the node body, bound to the component with full drawer support, Undo, and live
  Inspector sync. `BindingInfo` fields remain ports and are not duplicated. The foldout is collapsed
  by default and its open/closed state is remembered per binder.

## [0.1.0] - 2026-05-29

### Added
- Initial release.
- `Window ▸ Uice ▸ Visual Binder` GraphView window that visualizes a selected Uice View hierarchy:
  ViewModel components as source nodes (typed, color-coded output ports) and `ComponentBinder`s as
  target nodes.
- Drag-to-bind authoring that reads and writes the standard serialized `BindingInfo` (non-invasive;
  runtime is unchanged), with port type-compatibility validation via `Mace.Utils.BindingUtils`.
- Delete-to-unbind, undo support, and live Inspector synchronization.
- Toolbar with root selection, follow-selection, refresh, and an Add-Binder menu.
- Node-position persistence per graph root (keyed by `GlobalObjectId`).
- `BindableViewModelComponent` shown as both a source and a target (its injected view model appears
  as an input port).
