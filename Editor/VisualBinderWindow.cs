using System;
using Mace;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// Node-graph window for authoring Mace/Uice bindings on a selected Uice View hierarchy.
    /// Non-invasive: it only reads and writes the standard serialized <see cref="BindingInfo"/> data.
    /// </summary>
    public sealed class VisualBinderWindow : EditorWindow
    {
        private VisualBinderGraphView graphView;
        private ObjectField rootField;
        private GameObject root;
        private bool followSelection = true;

        [MenuItem("Window/Uice/Visual Binder")]
        public static void Open()
        {
            var window = GetWindow<VisualBinderWindow>();
            window.titleContent = new GUIContent("Visual Binder");
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            graphView = new VisualBinderGraphView();
            graphView.style.flexGrow = 1;

            rootView.Add(BuildToolbar());
            rootView.Add(graphView);

            Selection.selectionChanged += OnSelectionChanged;

            ResolveRootFromSelection();
            Rebuild();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private VisualElement rootView => rootVisualElement;

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar();

            rootField = new ObjectField("Root")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = root
            };
            rootField.style.width = 360f;
            rootField.RegisterValueChangedCallback(evt =>
            {
                root = evt.newValue as GameObject;
                Rebuild();
            });
            toolbar.Add(rootField);

            var refreshButton = new ToolbarButton(Rebuild) { text = "Refresh" };
            toolbar.Add(refreshButton);

            var arrangeButton = new ToolbarButton(() => graphView.ArrangeNodes()) { text = "Arrange" };
            arrangeButton.tooltip = "Lay out nodes in left-to-right columns by their connections";
            toolbar.Add(arrangeButton);

            var groupButton = new ToolbarButton(() => graphView.GroupSelection()) { text = "Group Selection" };
            groupButton.tooltip = "Group the selected nodes into a collapsible box";
            toolbar.Add(groupButton);

            var conflictsButton = new ToolbarButton(FindConflicts) { text = "Find Conflicts" };
            conflictsButton.tooltip = "Highlight binders that drive the same target (e.g. two ActivateGameObjectBinders on one GameObject)";
            toolbar.Add(conflictsButton);

            var addMenu = new ToolbarMenu { text = "Add Binder" };
            foreach (Type binderType in BindingReflection.GetBinderTypes())
            {
                Type captured = binderType;
                addMenu.menu.AppendAction(captured.Name, _ => AddBinder(captured));
            }
            toolbar.Add(addMenu);

            var followToggle = new ToolbarToggle { text = "Follow selection", value = followSelection };
            followToggle.RegisterValueChangedCallback(evt => followSelection = evt.newValue);
            toolbar.Add(followToggle);

            return toolbar;
        }

        private void OnSelectionChanged()
        {
            if (!followSelection)
            {
                return;
            }

            GameObject previous = root;
            ResolveRootFromSelection();

            if (root != previous)
            {
                if (rootField != null)
                {
                    rootField.SetValueWithoutNotify(root);
                }
                Rebuild();
            }
        }

        private void ResolveRootFromSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            // Prefer the nearest Uice Widget/View ancestor so the graph spans a whole view.
            var widget = selected.GetComponentInParent<Widget>(true);
            root = widget != null ? widget.gameObject : selected;
        }

        private void AddBinder(Type binderType)
        {
            GameObject target = Selection.activeGameObject != null ? Selection.activeGameObject : root;
            if (target == null)
            {
                EditorUtility.DisplayDialog("Visual Binder", "Select a GameObject to add the binder to.", "OK");
                return;
            }

            Undo.AddComponent(target, binderType);
            Rebuild();
        }

        private void Rebuild()
        {
            graphView?.Rebuild(root);
        }

        private void FindConflicts()
        {
            int count = graphView != null ? graphView.HighlightConflicts() : 0;
            ShowNotification(new GUIContent(count == 0
                ? "No conflicts found"
                : $"{count} conflicting binder(s) highlighted"));
        }
    }
}
