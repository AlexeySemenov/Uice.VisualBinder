using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// A GraphView <see cref="Group"/> that can be collapsed and ungrouped via its own right-click
    /// menu. Collapse/ungroup are executed by the GraphView (via the callbacks below); this type
    /// carries identity + collapsed state and exposes its member component-nodes.
    /// </summary>
    internal sealed class CollapsibleGroup : Group
    {
        public string Id { get; }
        public bool Collapsed { get; set; }

        public Action<CollapsibleGroup> CollapseToggleRequested;
        public Action<CollapsibleGroup> UngroupRequested;

        public CollapsibleGroup(string id, string groupTitle)
        {
            Id = id;
            title = groupTitle;
            this.AddManipulator(new ContextualMenuManipulator(BuildMenu));
        }

        /// <summary>The component-backed member nodes (excludes proxy nodes and nested elements).</summary>
        public IEnumerable<Node> MemberNodes =>
            containedElements.OfType<Node>().Where(n => n is IComponentNode);

        private void BuildMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction(Collapsed ? "Expand Group" : "Collapse Group",
                _ => CollapseToggleRequested?.Invoke(this));
            evt.menu.AppendAction("Ungroup", _ => UngroupRequested?.Invoke(this));
            evt.menu.AppendSeparator();
        }
    }
}
