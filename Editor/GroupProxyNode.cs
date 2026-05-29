using System;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Uice.VisualBinder.Editor
{
    /// <summary>Marker on proxy ports so they're never treated as real binding ports.</summary>
    internal sealed class ProxyPortMarker
    {
    }

    /// <summary>
    /// Stand-in node shown while a group is collapsed. It carries synthetic "proxy" ports that
    /// re-expose the collapsed cluster's external connections, with proxy edges drawn to them.
    /// </summary>
    internal sealed class GroupProxyNode : Node
    {
        public string GroupId { get; }
        public Action ExpandRequested;
        public Action UngroupRequested;

        private readonly string groupTitle;
        private int memberCount;

        public GroupProxyNode(string groupId, string groupTitle)
        {
            GroupId = groupId;
            this.groupTitle = groupTitle;

            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable);
            AddToClassList("uice-group-proxy");
            tooltip = "Collapsed group — double-click to expand";

            UpdateTitle();

            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    ExpandRequested?.Invoke();
                    evt.StopPropagation();
                }
            });

            this.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Expand Group", _ => ExpandRequested?.Invoke());
                evt.menu.AppendAction("Ungroup", _ => UngroupRequested?.Invoke());
                evt.menu.AppendSeparator();
            }));
        }

        public void SetMemberCount(int count)
        {
            memberCount = count;
            UpdateTitle();
        }

        /// <summary>Adds a proxy port (gray, non-binding) and returns it. Caller refreshes once done.</summary>
        public Port AddProxyPort(Direction direction, string label)
        {
            Port port = InstantiatePort(Orientation.Horizontal, direction, Port.Capacity.Multi, typeof(object));
            port.portName = label ?? string.Empty;
            port.portColor = new Color(0.7f, 0.7f, 0.7f);
            port.userData = new ProxyPortMarker();

            (direction == Direction.Input ? inputContainer : outputContainer).Add(port);
            return port;
        }

        public void ClearProxyPorts()
        {
            ClearContainer(inputContainer);
            ClearContainer(outputContainer);
            RefreshPorts();
            RefreshExpandedState();
        }

        public void RefreshProxy()
        {
            UpdateTitle();
            RefreshPorts();
            RefreshExpandedState();
        }

        private static void ClearContainer(VisualElement container)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                if (container.ElementAt(i) is Port port)
                {
                    foreach (Edge edge in port.connections.ToArray())
                    {
                        port.Disconnect(edge);
                    }
                }

                container.RemoveAt(i);
            }
        }

        private void UpdateTitle()
        {
            int links = inputContainer.childCount + outputContainer.childCount;
            title = $"▣ {groupTitle}  ·  {memberCount} nodes" + (links > 0 ? $"  ·  {links} links" : string.Empty);
        }
    }
}
