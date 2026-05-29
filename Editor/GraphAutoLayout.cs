using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// Layered (Sugiyama-style) left-to-right auto-layout. Columns are assigned by data-flow
    /// connectivity (provider output ports → consumer input ports): a node sits to the right of
    /// everything that feeds it. Fully unconnected nodes go in a trailing column. Within each column,
    /// a few barycenter sweeps reduce edge crossings.
    /// </summary>
    internal static class GraphAutoLayout
    {
        private const float ColumnGap = 90f;
        private const float RowGap = 28f;
        private const int BarycenterSweeps = 4;

        /// <summary>
        /// Computes target positions for every node. <paramref name="sizeOf"/> supplies each node's
        /// resolved (width, height). Returns a node → top-left-position rect map (size copied through).
        /// </summary>
        public static Dictionary<Node, Rect> Compute(
            IReadOnlyList<Node> nodes,
            IReadOnlyList<Edge> edges,
            Func<Node, Vector2> sizeOf)
        {
            var result = new Dictionary<Node, Rect>();
            if (nodes == null || nodes.Count == 0)
            {
                return result;
            }

            // Adjacency from edges (provider → consumer), de-duplicated and self-loops ignored.
            var successors = new Dictionary<Node, HashSet<Node>>();
            var predecessors = new Dictionary<Node, HashSet<Node>>();
            foreach (Node node in nodes)
            {
                successors[node] = new HashSet<Node>();
                predecessors[node] = new HashSet<Node>();
            }

            if (edges != null)
            {
                foreach (Edge edge in edges)
                {
                    Node from = edge.output?.node;
                    Node to = edge.input?.node;
                    if (from == null || to == null || from == to || !successors.ContainsKey(from) || !predecessors.ContainsKey(to))
                    {
                        continue;
                    }

                    successors[from].Add(to);
                    predecessors[to].Add(from);
                }
            }

            // Layer assignment: connected nodes via longest path; orphans in a trailing column.
            var layerOf = new Dictionary<Node, int>();
            var resolving = new HashSet<Node>();
            int maxConnectedLayer = -1;

            foreach (Node node in nodes)
            {
                bool connected = predecessors[node].Count > 0 || successors[node].Count > 0;
                if (connected)
                {
                    int layer = LongestPathLayer(node, predecessors, layerOf, resolving);
                    maxConnectedLayer = Mathf.Max(maxConnectedLayer, layer);
                }
            }

            int orphanLayer = maxConnectedLayer + 1; // 0 if there were no connected nodes
            foreach (Node node in nodes)
            {
                if (!layerOf.ContainsKey(node))
                {
                    layerOf[node] = orphanLayer;
                }
            }

            // Group into columns, ordered initially by current vertical position for stability.
            int columnCount = layerOf.Values.Max() + 1;
            var columns = new List<List<Node>>(columnCount);
            for (int i = 0; i < columnCount; i++)
            {
                columns.Add(new List<Node>());
            }

            foreach (Node node in nodes)
            {
                columns[layerOf[node]].Add(node);
            }

            foreach (List<Node> column in columns)
            {
                column.Sort((a, b) => a.GetPosition().y.CompareTo(b.GetPosition().y));
            }

            ReduceCrossings(columns, predecessors, successors);

            // Positioning: column x by cumulative max width; rows stacked, columns vertically centered.
            var heights = new Dictionary<Node, float>();
            var widths = new Dictionary<Node, float>();
            foreach (Node node in nodes)
            {
                Vector2 size = sizeOf(node);
                widths[node] = size.x;
                heights[node] = size.y;
            }

            var columnTotalHeight = new float[columnCount];
            float tallestColumn = 0f;
            for (int c = 0; c < columnCount; c++)
            {
                float h = 0f;
                foreach (Node node in columns[c])
                {
                    h += heights[node] + RowGap;
                }
                h = Mathf.Max(0f, h - RowGap);
                columnTotalHeight[c] = h;
                tallestColumn = Mathf.Max(tallestColumn, h);
            }

            float x = 0f;
            for (int c = 0; c < columnCount; c++)
            {
                float columnWidth = columns[c].Count > 0 ? columns[c].Max(n => widths[n]) : 0f;
                float y = (tallestColumn - columnTotalHeight[c]) * 0.5f; // center this column vertically

                foreach (Node node in columns[c])
                {
                    result[node] = new Rect(x, y, widths[node], heights[node]);
                    y += heights[node] + RowGap;
                }

                x += columnWidth + ColumnGap;
            }

            return result;
        }

        private static int LongestPathLayer(
            Node node,
            Dictionary<Node, HashSet<Node>> predecessors,
            Dictionary<Node, int> layerOf,
            HashSet<Node> resolving)
        {
            if (layerOf.TryGetValue(node, out int cached))
            {
                return cached;
            }

            if (!resolving.Add(node))
            {
                return 0; // cycle guard
            }

            int layer = 0;
            foreach (Node pred in predecessors[node])
            {
                layer = Mathf.Max(layer, LongestPathLayer(pred, predecessors, layerOf, resolving) + 1);
            }

            resolving.Remove(node);
            layerOf[node] = layer;
            return layer;
        }

        private static void ReduceCrossings(
            List<List<Node>> columns,
            Dictionary<Node, HashSet<Node>> predecessors,
            Dictionary<Node, HashSet<Node>> successors)
        {
            for (int sweep = 0; sweep < BarycenterSweeps; sweep++)
            {
                bool forward = sweep % 2 == 0;
                if (forward)
                {
                    for (int c = 1; c < columns.Count; c++)
                    {
                        OrderByBarycenter(columns[c], columns[c - 1], predecessors);
                    }
                }
                else
                {
                    for (int c = columns.Count - 2; c >= 0; c--)
                    {
                        OrderByBarycenter(columns[c], columns[c + 1], successors);
                    }
                }
            }
        }

        private static void OrderByBarycenter(
            List<Node> column,
            List<Node> reference,
            Dictionary<Node, HashSet<Node>> neighbours)
        {
            var referenceIndex = new Dictionary<Node, int>();
            for (int i = 0; i < reference.Count; i++)
            {
                referenceIndex[reference[i]] = i;
            }

            var keys = new Dictionary<Node, float>();
            for (int i = 0; i < column.Count; i++)
            {
                Node node = column[i];
                float sum = 0f;
                int count = 0;
                foreach (Node neighbour in neighbours[node])
                {
                    if (referenceIndex.TryGetValue(neighbour, out int idx))
                    {
                        sum += idx;
                        count++;
                    }
                }

                // Nodes with no neighbour in the reference column keep their current order.
                keys[node] = count > 0 ? sum / count : i;
            }

            column.Sort((a, b) => keys[a].CompareTo(keys[b]));
        }
    }
}
