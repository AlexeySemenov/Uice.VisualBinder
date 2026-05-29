using System.Collections.Generic;
using System.Linq;
using Mace;
using Mace.Utils;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// The node canvas. Builds source (ViewModel) and target (binder) nodes for a hierarchy and
    /// turns edge create/delete into edits of the underlying serialized <see cref="BindingInfo"/>.
    /// </summary>
    internal sealed class VisualBinderGraphView : GraphView
    {
        private const float ColumnGap = 520f;
        private const float RowGap = 32f;
        private const float NodeWidth = 320f;

        private readonly Dictionary<ViewModelComponent, ViewModelNode> viewModelNodes =
            new Dictionary<ViewModelComponent, ViewModelNode>();

        /// <summary>Set while we mutate the graph programmatically, so we don't write back during a rebuild.</summary>
        private bool suppressChanges;

        /// <summary>Persisted node positions for the current root (null if the root has no stable id).</summary>
        private VisualBinderLayoutStore layout;

        /// <summary>True between a rebuild with no saved layout and the first resolved-geometry frame.</summary>
        private bool autoArrangePending;

        public VisualBinderGraphView()
        {
            style.flexGrow = 1;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = OnGraphViewChanged;
        }

        /// <summary>Rebuilds the whole graph for the given hierarchy root.</summary>
        public void Rebuild(GameObject root)
        {
            suppressChanges = true;
            try
            {
                DeleteElements(graphElements.ToList());
                viewModelNodes.Clear();

                if (root == null)
                {
                    layout = null;
                    return;
                }

                layout = VisualBinderLayoutStore.ForRoot(root);

                BuildViewModelNodes(root);
                BuildBinderNodes(root);
                ConnectAllBindings();

                // No saved positions yet → arrange once geometry resolves (first open of a hierarchy).
                autoArrangePending = layout == null || layout.IsEmpty;
            }
            finally
            {
                suppressChanges = false;
            }

            if (autoArrangePending)
            {
                ScheduleAutoArrange(0);
            }
        }

        /// <summary>
        /// Polls until nodes have a resolved size (UI Toolkit lays them out asynchronously), then
        /// runs the auto-arrange once. Bounded so it can't loop forever.
        /// </summary>
        private void ScheduleAutoArrange(int attempt)
        {
            const int maxAttempts = 30;

            schedule.Execute(() =>
            {
                if (!autoArrangePending)
                {
                    return;
                }

                if (nodes.ToList().Any(n => n.layout.height > 1f))
                {
                    autoArrangePending = false;
                    ArrangeNodes();
                }
                else if (attempt < maxAttempts)
                {
                    ScheduleAutoArrange(attempt + 1);
                }
                else
                {
                    autoArrangePending = false; // give up; manual Arrange button still works
                }
            }).StartingIn(16);
        }

        /// <summary>Lays nodes out in connectivity-based columns (left→right) and persists the result.</summary>
        public void ArrangeNodes()
        {
            List<Node> nodeList = nodes.ToList();
            if (nodeList.Count == 0)
            {
                return;
            }

            Dictionary<Node, Rect> positions = GraphAutoLayout.Compute(nodeList, edges.ToList(), SizeOf);

            bool dirty = false;
            foreach (KeyValuePair<Node, Rect> entry in positions)
            {
                entry.Key.SetPosition(entry.Value);

                if (entry.Key is IComponentNode componentNode && layout != null)
                {
                    string key = VisualBinderLayoutStore.GetKey(componentNode.Component);
                    if (!string.IsNullOrEmpty(key))
                    {
                        layout.Set(key, entry.Value);
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                layout.Save();
            }

            schedule.Execute(() => FrameAll());
        }

        /// <summary>Resolved node size, falling back to estimates before the first layout pass.</summary>
        private static Vector2 SizeOf(Node node)
        {
            float width = node.layout.width > 1f ? node.layout.width : NodeWidth;
            float height = node.layout.height > 1f ? node.layout.height : EstimateHeight(node);
            return new Vector2(width, height);
        }

        private void BuildViewModelNodes(GameObject root)
        {
            var seen = new HashSet<ViewModelComponent>();
            var sources = root.GetComponentsInChildren<ViewModelComponent>(true)
                .Concat(root.GetComponentsInParent<ViewModelComponent>(true));

            float y = 0f;
            foreach (ViewModelComponent vm in sources)
            {
                if (vm == null || !seen.Add(vm))
                {
                    continue;
                }

                var node = new ViewModelNode(vm);
                PlaceNode(node, new Rect(0f, y, NodeWidth, 0f));
                AddElement(node);
                viewModelNodes[vm] = node;

                y += EstimateHeight(node) + RowGap;
            }
        }

        private void BuildBinderNodes(GameObject root)
        {
            ComponentBinder[] binders = root.GetComponentsInChildren<ComponentBinder>(true);

            float y = 0f;
            foreach (ComponentBinder binder in binders)
            {
                if (binder == null)
                {
                    continue;
                }

                var node = new BinderNode(binder);
                PlaceNode(node, new Rect(ColumnGap, y, NodeWidth, 0f));
                AddElement(node);

                y += EstimateHeight(node) + RowGap;
            }
        }

        /// <summary>Positions a node at its saved location if one exists, otherwise the auto-layout fallback.</summary>
        private void PlaceNode(Node node, Rect fallback)
        {
            if (node is IComponentNode componentNode && layout != null)
            {
                string key = VisualBinderLayoutStore.GetKey(componentNode.Component);
                if (!string.IsNullOrEmpty(key) && layout.TryGet(key, out Rect saved))
                {
                    node.SetPosition(saved);
                    return;
                }
            }

            node.SetPosition(fallback);
        }

        /// <summary>
        /// After all nodes exist, draws an edge for every input port that already has a binding.
        /// Covers both binder nodes and view-model nodes that consume a view model
        /// (e.g. BindableViewModelComponent).
        /// </summary>
        private void ConnectAllBindings()
        {
            foreach (Node node in nodes.ToList())
            {
                switch (node)
                {
                    case BinderNode binderNode:
                        ConnectInputs(binderNode.Binder, binderNode.PortsByPath);
                        break;
                    case ViewModelNode viewModelNode:
                        ConnectInputs(viewModelNode.ViewModel, viewModelNode.PortsByPath);
                        break;
                }
            }
        }

        private void ConnectInputs(Component component, IReadOnlyDictionary<string, Port> inputPorts)
        {
            foreach (KeyValuePair<string, Port> entry in inputPorts)
            {
                if (!BindingReflection.TryGetBinding(component, entry.Key, out ViewModelComponent vm, out string propertyName))
                {
                    continue;
                }

                if (!viewModelNodes.TryGetValue(vm, out ViewModelNode vmNode))
                {
                    continue;
                }

                if (!vmNode.PortsByProperty.TryGetValue(propertyName, out Port outputPort))
                {
                    continue;
                }

                Edge edge = outputPort.ConnectTo(entry.Value);
                AddElement(edge);
            }
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();

            ports.ForEach(candidate =>
            {
                if (candidate == startPort || candidate.node == startPort.node || candidate.direction == startPort.direction)
                {
                    return;
                }

                Port output = startPort.direction == Direction.Output ? startPort : candidate;
                Port input = startPort.direction == Direction.Input ? startPort : candidate;

                if (output.userData is BindingSourcePortData source &&
                    input.userData is BindingTargetPortData target &&
                    target.TargetType != null &&
                    BindingUtils.CanBeBound(source.ActualType, target.TargetType))
                {
                    compatible.Add(candidate);
                }
            });

            return compatible;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (suppressChanges)
            {
                return change;
            }

            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is Edge edge && edge.input?.userData is BindingTargetPortData target)
                    {
                        BindingReflection.ClearBinding(target.Binder, target.PropertyPath);
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    ApplyNewEdge(edge);
                }
            }

            if (change.movedElements != null)
            {
                PersistPositions(change.movedElements);
            }

            return change;
        }

        private void PersistPositions(IEnumerable<GraphElement> movedElements)
        {
            if (layout == null)
            {
                return;
            }

            bool dirty = false;
            foreach (GraphElement element in movedElements)
            {
                if (element is IComponentNode componentNode && element is Node node)
                {
                    string key = VisualBinderLayoutStore.GetKey(componentNode.Component);
                    if (!string.IsNullOrEmpty(key))
                    {
                        layout.Set(key, node.GetPosition());
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                layout.Save();
            }
        }

        private void ApplyNewEdge(Edge edge)
        {
            if (edge.output?.userData is not BindingSourcePortData source ||
                edge.input?.userData is not BindingTargetPortData target)
            {
                return;
            }

            // A BindingInfo slot holds a single binding: drop any edge already on this input.
            foreach (Edge existing in edge.input.connections.Where(e => e != edge).ToList())
            {
                existing.output?.Disconnect(existing);
                edge.input.Disconnect(existing);
                RemoveElement(existing);
            }

            BindingReflection.SetBinding(target.Binder, target.PropertyPath, source.ViewModel, source.PropertyName);
        }

        private static float EstimateHeight(Node node)
        {
            int ports = node.inputContainer.childCount + node.outputContainer.childCount;
            return 60f + ports * 24f;
        }
    }
}
