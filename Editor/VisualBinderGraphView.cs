using System;
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

        /// <summary>Persisted manual groups for the current root (null if the root has no stable id).</summary>
        private VisualBinderGroupStore groupStore;

        private readonly Dictionary<string, CollapsibleGroup> groupsById = new Dictionary<string, CollapsibleGroup>();
        private readonly Dictionary<string, GroupProxyNode> proxyNodesById = new Dictionary<string, GroupProxyNode>();
        private readonly HashSet<Edge> proxyEdges = new HashSet<Edge>();

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
            elementsAddedToGroup = OnElementsAddedToGroup;
            elementsRemovedFromGroup = OnElementsRemovedFromGroup;
            groupTitleChanged = OnGroupTitleChanged;
        }

        /// <summary>Rebuilds the whole graph for the given hierarchy root.</summary>
        public void Rebuild(GameObject root)
        {
            suppressChanges = true;
            try
            {
                DeleteElements(graphElements.ToList());
                viewModelNodes.Clear();
                groupsById.Clear();
                proxyNodesById.Clear();
                proxyEdges.Clear();

                if (root == null)
                {
                    layout = null;
                    groupStore = null;
                    return;
                }

                layout = VisualBinderLayoutStore.ForRoot(root);
                groupStore = VisualBinderGroupStore.ForRoot(root);

                BuildViewModelNodes(root);
                BuildBinderNodes(root);
                ConnectAllBindings();
                RecreateGroups();

                // No saved positions yet → arrange once geometry resolves (first open of a hierarchy).
                autoArrangePending = layout == null || layout.IsEmpty;
            }
            finally
            {
                suppressChanges = false;
            }

            RefreshCollapsedView();

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
            // Grouped nodes (and group/proxy boxes) keep their manual placement; arrange the rest.
            var grouped = new HashSet<Node>();
            foreach (CollapsibleGroup group in groupsById.Values)
            {
                foreach (Node member in group.MemberNodes)
                {
                    grouped.Add(member);
                }
            }

            List<Node> nodeList = nodes.ToList().Where(n => n is IComponentNode && !grouped.Contains(n)).ToList();
            if (nodeList.Count == 0)
            {
                return;
            }

            var nodeSet = new HashSet<Node>(nodeList);
            List<Edge> edgeList = edges.ToList()
                .Where(e => !proxyEdges.Contains(e)
                            && e.output?.node is Node outNode && nodeSet.Contains(outNode)
                            && e.input?.node is Node inNode && nodeSet.Contains(inNode))
                .ToList();

            Dictionary<Node, Rect> positions = GraphAutoLayout.Compute(nodeList, edgeList, SizeOf);

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

        // ---- Manual groups -------------------------------------------------------------------

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            // Group/Expand/Ungroup live on the group & proxy elements themselves (see their
            // ContextualMenuManipulator). Here we only offer to group the current selection.
            if (selection.OfType<Node>().Any(n => n is IComponentNode))
            {
                evt.menu.AppendAction("Group Selected Nodes", _ => GroupSelection());
                evt.menu.AppendSeparator();
            }

            base.BuildContextualMenu(evt);
        }

        private void WireGroup(CollapsibleGroup group)
        {
            group.CollapseToggleRequested = g => ToggleGroupCollapsed(g.Id);
            group.UngroupRequested = g => Ungroup(g.Id);
        }

        /// <summary>Removes the group box but keeps its member nodes (and their bindings).</summary>
        private void Ungroup(string groupId)
        {
            if (!groupsById.TryGetValue(groupId, out CollapsibleGroup group))
            {
                return;
            }

            foreach (Node member in group.MemberNodes)
            {
                member.style.display = DisplayStyle.Flex; // ensure visible if it was collapsed
            }

            RemoveProxyNode(groupId);

            suppressChanges = true;
            try
            {
                RemoveElement(group);
            }
            finally
            {
                suppressChanges = false;
            }

            groupsById.Remove(groupId);
            groupStore?.Remove(groupId);
            groupStore?.Save();

            RefreshCollapsedView();
        }

        /// <summary>Creates a group from the currently selected component nodes.</summary>
        public void GroupSelection()
        {
            List<Node> selected = selection.OfType<Node>().Where(n => n is IComponentNode).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var group = new CollapsibleGroup(Guid.NewGuid().ToString("N"), "Group");
            WireGroup(group);

            suppressChanges = true;
            try
            {
                AddElement(group);
                foreach (Node node in selected)
                {
                    group.AddElement(node);
                }
            }
            finally
            {
                suppressChanges = false;
            }

            groupsById[group.Id] = group;
            SaveGroup(group);

            ClearSelection();
            AddToSelection(group);
        }

        private void RecreateGroups()
        {
            if (groupStore == null)
            {
                return;
            }

            Dictionary<string, Node> byKey = NodesByKey();
            foreach (GroupData data in groupStore.Groups)
            {
                var group = new CollapsibleGroup(data.id, data.title) { Collapsed = data.collapsed };
                WireGroup(group);
                AddElement(group);
                groupsById[data.id] = group;

                foreach (string key in data.memberKeys)
                {
                    if (byKey.TryGetValue(key, out Node node))
                    {
                        group.AddElement(node);
                    }
                }
            }
        }

        private void ToggleGroupCollapsed(string groupId)
        {
            if (!groupsById.TryGetValue(groupId, out CollapsibleGroup group))
            {
                return;
            }

            if (!group.Collapsed)
            {
                Rect groupPos = group.GetPosition();
                group.Collapsed = true;
                RefreshCollapsedView();
                if (proxyNodesById.TryGetValue(groupId, out GroupProxyNode proxy))
                {
                    proxy.SetPosition(groupPos);
                }
            }
            else
            {
                Rect proxyPos = proxyNodesById.TryGetValue(groupId, out GroupProxyNode proxy)
                    ? proxy.GetPosition()
                    : group.GetPosition();
                group.Collapsed = false;
                RefreshCollapsedView();
                group.SetPosition(proxyPos);
            }

            SaveGroup(group);
        }

        /// <summary>
        /// Reconciles collapsed state with the visuals: hides members of collapsed groups, shows a
        /// proxy node per collapsed group, and rebuilds proxy edges for any binding that crosses a
        /// collapsed boundary (resolving each endpoint to itself or its collapsed group's proxy).
        /// </summary>
        private void RefreshCollapsedView()
        {
            foreach (Edge proxyEdge in proxyEdges)
            {
                proxyEdge.output?.Disconnect(proxyEdge);
                proxyEdge.input?.Disconnect(proxyEdge);
                RemoveElement(proxyEdge);
            }
            proxyEdges.Clear();

            var hiddenProxy = new Dictionary<Node, GroupProxyNode>();
            foreach (CollapsibleGroup group in groupsById.Values)
            {
                List<Node> members = group.MemberNodes.ToList();
                if (group.Collapsed)
                {
                    GroupProxyNode proxy = EnsureProxyNode(group);
                    proxy.ClearProxyPorts();
                    proxy.style.display = DisplayStyle.Flex;
                    proxy.SetMemberCount(members.Count);
                    group.style.display = DisplayStyle.None;

                    foreach (Node member in members)
                    {
                        member.style.display = DisplayStyle.None;
                        hiddenProxy[member] = proxy;
                    }
                }
                else
                {
                    group.style.display = DisplayStyle.Flex;
                    foreach (Node member in members)
                    {
                        member.style.display = DisplayStyle.Flex;
                    }
                    RemoveProxyNode(group.Id);
                }
            }

            foreach (Edge edge in edges.ToList())
            {
                Node outNode = edge.output?.node;
                Node inNode = edge.input?.node;
                if (outNode == null || inNode == null)
                {
                    continue;
                }

                hiddenProxy.TryGetValue(outNode, out GroupProxyNode outProxy);
                hiddenProxy.TryGetValue(inNode, out GroupProxyNode inProxy);

                if (outProxy == null && inProxy == null)
                {
                    edge.style.display = DisplayStyle.Flex;
                    continue;
                }

                edge.style.display = DisplayStyle.None;
                if (outProxy != null && outProxy == inProxy)
                {
                    continue; // wholly inside one collapsed group
                }

                Port outPort = outProxy != null ? outProxy.AddProxyPort(Direction.Output, edge.output.portName) : edge.output;
                Port inPort = inProxy != null ? inProxy.AddProxyPort(Direction.Input, edge.input.portName) : edge.input;

                Edge proxyEdge = outPort.ConnectTo(inPort);
                proxyEdge.capabilities &= ~(Capabilities.Deletable | Capabilities.Selectable);
                AddElement(proxyEdge);
                proxyEdges.Add(proxyEdge);
            }

            foreach (GroupProxyNode proxy in proxyNodesById.Values)
            {
                proxy.RefreshProxy();
            }
        }

        private GroupProxyNode EnsureProxyNode(CollapsibleGroup group)
        {
            if (proxyNodesById.TryGetValue(group.Id, out GroupProxyNode existing))
            {
                return existing;
            }

            var proxy = new GroupProxyNode(group.Id, group.title)
            {
                ExpandRequested = () => ToggleGroupCollapsed(group.Id),
                UngroupRequested = () => Ungroup(group.Id)
            };
            AddElement(proxy);
            proxyNodesById[group.Id] = proxy;

            Rect stored = StoredProxyRect(group.Id);
            proxy.SetPosition(stored.width > 1f || stored.x != 0f || stored.y != 0f ? stored : group.GetPosition());
            return proxy;
        }

        private void RemoveProxyNode(string groupId)
        {
            if (proxyNodesById.TryGetValue(groupId, out GroupProxyNode proxy))
            {
                proxy.ClearProxyPorts();
                RemoveElement(proxy);
                proxyNodesById.Remove(groupId);
            }
        }

        private Rect StoredProxyRect(string groupId)
        {
            if (groupStore != null)
            {
                foreach (GroupData data in groupStore.Groups)
                {
                    if (data.id == groupId)
                    {
                        return data.proxyRect;
                    }
                }
            }

            return default;
        }

        private void SaveGroup(CollapsibleGroup group)
        {
            if (groupStore == null)
            {
                return;
            }

            var data = new GroupData
            {
                id = group.Id,
                title = group.title,
                collapsed = group.Collapsed,
                proxyRect = proxyNodesById.TryGetValue(group.Id, out GroupProxyNode proxy) ? proxy.GetPosition() : StoredProxyRect(group.Id),
                memberKeys = group.MemberNodes
                    .Select(n => VisualBinderLayoutStore.GetKey(((IComponentNode)n).Component))
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList()
            };

            groupStore.Upsert(data);
            groupStore.Save();
        }

        private Dictionary<string, Node> NodesByKey()
        {
            var map = new Dictionary<string, Node>();
            foreach (Node node in nodes.ToList())
            {
                if (node is IComponentNode componentNode)
                {
                    string key = VisualBinderLayoutStore.GetKey(componentNode.Component);
                    if (!string.IsNullOrEmpty(key))
                    {
                        map[key] = node;
                    }
                }
            }

            return map;
        }

        private void OnElementsAddedToGroup(Group group, IEnumerable<GraphElement> elements)
        {
            if (!suppressChanges && group is CollapsibleGroup cg && groupsById.ContainsKey(cg.Id))
            {
                SaveGroup(cg);
            }
        }

        private void OnElementsRemovedFromGroup(Group group, IEnumerable<GraphElement> elements)
        {
            if (!suppressChanges && group is CollapsibleGroup cg && groupsById.ContainsKey(cg.Id))
            {
                SaveGroup(cg);
            }
        }

        private void OnGroupTitleChanged(Group group, string newTitle)
        {
            if (!suppressChanges && group is CollapsibleGroup cg && groupsById.ContainsKey(cg.Id))
            {
                SaveGroup(cg);
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
                    switch (element)
                    {
                        case Edge edge:
                            if (proxyEdges.Contains(edge))
                            {
                                break; // synthetic edge — not a real binding
                            }
                            if (edge.input?.userData is BindingTargetPortData target)
                            {
                                BindingReflection.ClearBinding(target.Binder, target.PropertyPath);
                            }
                            break;

                        case CollapsibleGroup group:
                            RemoveProxyNode(group.Id);
                            groupsById.Remove(group.Id);
                            groupStore?.Remove(group.Id);
                            groupStore?.Save();
                            break;
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
            bool layoutDirty = false;

            foreach (GraphElement element in movedElements)
            {
                switch (element)
                {
                    case GroupProxyNode proxy:
                        if (groupsById.TryGetValue(proxy.GroupId, out CollapsibleGroup proxyGroup))
                        {
                            SaveGroup(proxyGroup);
                        }
                        break;

                    case CollapsibleGroup group:
                        SaveGroup(group);
                        if (layout != null)
                        {
                            foreach (Node member in group.MemberNodes)
                            {
                                layoutDirty |= PersistNodePosition(member);
                            }
                        }
                        break;

                    case Node node:
                        layoutDirty |= PersistNodePosition(node);
                        break;
                }
            }

            if (layoutDirty)
            {
                layout?.Save();
            }
        }

        private bool PersistNodePosition(Node node)
        {
            if (layout == null || node is not IComponentNode componentNode)
            {
                return false;
            }

            string key = VisualBinderLayoutStore.GetKey(componentNode.Component);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            layout.Set(key, node.GetPosition());
            return true;
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
