# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
