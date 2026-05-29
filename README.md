# Uice Visual Binder

A UI Toolkit **GraphView** editor for authoring and visualizing [Mace](https://github.com/Aleshmandr/Mace)
/ [Uice](https://github.com/Aleshmandr/Uice) data bindings as a node graph — instead of hunting
through dozens of binder components scattered across a View hierarchy.

It is **non-invasive**: the graph reads and writes the exact same serialized `BindingInfo` data the
normal Mace inspector uses. Nothing changes at runtime, and every existing binder module keeps
working. The graph is just a clearer way to see and edit "what is bound to what".

## Installation

This package depends on Mace and Uice. Because Unity Package Manager does not auto-resolve
dependencies that are themselves git packages, add the three packages **in this order** via
*Package Manager ▸ + ▸ Add package from git URL…*:

1. `https://github.com/Aleshmandr/Mace.git`
2. `https://github.com/Aleshmandr/Uice.git`
3. `https://github.com/AlexeySemenov/Uice.VisualBinder.git`

Requires Unity 2022.3 or newer.

## Opening the window

`Window ▸ Uice ▸ Visual Binder`

- Select a GameObject in a Uice View hierarchy (the window snaps to the nearest `Widget`/`View`
  ancestor as the graph **root**), or drag a root into the **Root** field in the toolbar.
- **Follow selection** keeps the graph in sync with the Hierarchy selection.
- **Refresh** rebuilds the graph (e.g. after editing scripts or adding components).

## Reading the graph

- **Left column — ViewModel nodes.** One per `ViewModelComponent` in (or above) the root. Each
  bindable property of the view model's `ExpectedType` is an **output port**, colored by kind:
  - blue = `Variable<T>`, green = `Collection<T>`, orange = `Command` / `Command<T>`,
    purple = `Event` / `Event<T>`.
  - A component that also *consumes* a view model (e.g. `BindableViewModelComponent`, which injects
    a nested view model from a parent) additionally shows an **input port** on its left — its data
    source — so nested-view-model wiring is visible and editable like any other binding.
- **Right column — Binder nodes.** One per `ComponentBinder` (e.g. `TextMeshProUGUIBinder`,
  `ButtonBinder`, Uice's `WidgetBinder`). Each `BindingInfo` slot is an **input port**.
- **Edges** are bindings. They are derived from the binders' existing serialized data.

## Editing bindings

- **Drag** from a view-model output port to a binder input port to create a binding. Only
  type-compatible ports connect (validated with `Mace.Utils.BindingUtils.CanBeBound`).
- A binder slot holds a single binding; connecting a new edge replaces the old one.
- **Select an edge + Delete** to clear that binding.
- All edits go through `SerializedObject`, so they are undoable and immediately reflected in the
  Inspector.
- **Add Binder** (toolbar) adds a `ComponentBinder` to the currently selected GameObject.

Node positions you arrange are remembered per graph root (keyed by each component's
`GlobalObjectId`) and survive refresh, domain reloads and editor restarts. Nodes without a saved
position fall back to the default two-column auto-layout.

## Requirements

- Unity 2022.3+ (uses `UnityEditor.Experimental.GraphView`).
- `com.aleshmandr.uice` and `com.aleshmandr.mace` present in the project.

## Verifying with the sample

A sample lives under `Assets/VisualBinderSample/` (`SampleViewModel`, `SampleView`). To try it:

1. Create a Canvas, add a child GameObject, add the **Sample View** component (it adds the required
   `ViewModelComponent` and sets its type to `SampleViewModel`).
2. Add a TextMeshPro - Text under it and `Add Binder ▸ TextMeshProUGUIBinder` (or use the component's
   context menu). Add a Button with `ButtonBinder`.
3. Open the Visual Binder with that View root selected. Drag `Title` → the text binder's `text`
   port, and `Submit` → the button's `On Click` command port.
4. Confirm the binders' `BindingInfo` fields in the Inspector now point at the view model. Enter Play
   mode with a `SampleViewModel` assigned to confirm the binding drives the UI.
