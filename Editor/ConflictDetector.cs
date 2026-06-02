using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mace;
using UnityEngine;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// Describes why a binder node conflicts and which serialized list elements hold the shared
    /// targets (field name → conflicting element indices).
    /// </summary>
    internal sealed class ConflictInfo
    {
        public string Reason;
        public readonly Dictionary<string, HashSet<int>> Elements = new Dictionary<string, HashSet<int>>();
    }

    /// <summary>
    /// Finds binders that fight over the same target. Binders such as <c>ActivateGameObjectBinder</c>
    /// / <c>EnableBehaviourBinder</c> drive a target's active/enabled state, each from its own bool;
    /// if the same target is driven by two or more binders they are independent writers to one flag
    /// (last write wins). Detection is generic: any serialized <c>List&lt;T&gt;</c> field whose element
    /// type exposes a <c>UnityEngine.Object Target</c> member is treated as "driven targets".
    /// </summary>
    internal static class ConflictDetector
    {
        private readonly struct ElementRef
        {
            public readonly string Field;
            public readonly int Index;

            public ElementRef(string field, int index)
            {
                Field = field;
                Index = index;
            }
        }

        private static readonly BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Returns the conflicting binder nodes mapped to their conflict info.</summary>
        public static Dictionary<BinderNode, ConflictInfo> Detect(IEnumerable<BinderNode> binderNodes)
        {
            // node -> (target -> the list element(s) on that node referencing the target)
            var drivenByNode = new Dictionary<BinderNode, Dictionary<Object, List<ElementRef>>>();
            var nodesByTarget = new Dictionary<Object, List<BinderNode>>();

            foreach (BinderNode node in binderNodes)
            {
                Dictionary<Object, List<ElementRef>> targets = ExtractDrivenTargets(node.Binder);
                drivenByNode[node] = targets;

                foreach (Object target in targets.Keys)
                {
                    if (!nodesByTarget.TryGetValue(target, out List<BinderNode> list))
                    {
                        list = new List<BinderNode>();
                        nodesByTarget[target] = list;
                    }
                    list.Add(node);
                }
            }

            var result = new Dictionary<BinderNode, ConflictInfo>();

            foreach (KeyValuePair<Object, List<BinderNode>> entry in nodesByTarget)
            {
                if (entry.Value.Count < 2)
                {
                    continue;
                }

                foreach (BinderNode node in entry.Value)
                {
                    if (!result.TryGetValue(node, out ConflictInfo info))
                    {
                        info = new ConflictInfo();
                        result[node] = info;
                    }

                    foreach (ElementRef element in drivenByNode[node][entry.Key])
                    {
                        if (!info.Elements.TryGetValue(element.Field, out HashSet<int> indices))
                        {
                            indices = new HashSet<int>();
                            info.Elements[element.Field] = indices;
                        }
                        indices.Add(element.Index);
                    }
                }
            }

            foreach (KeyValuePair<BinderNode, ConflictInfo> entry in result)
            {
                IEnumerable<Object> sharedTargets = drivenByNode[entry.Key].Keys
                    .Where(t => nodesByTarget.TryGetValue(t, out List<BinderNode> n) && n.Count >= 2);

                string names = string.Join(", ", sharedTargets.Select(o => o != null ? o.name : "<null>").Distinct());
                entry.Value.Reason = $"Conflict: also drives \"{names}\" — multiple binders fight over its state.";
            }

            return result;
        }

        private static Dictionary<Object, List<ElementRef>> ExtractDrivenTargets(Component binder)
        {
            var targets = new Dictionary<Object, List<ElementRef>>();
            System.Type type = binder.GetType();

            while (type != null && type != typeof(ComponentBinder) && type != typeof(MonoBehaviour))
            {
                foreach (FieldInfo field in type.GetFields(FieldFlags))
                {
                    if (!IsSerialized(field) || !IsGenericList(field.FieldType, out System.Type elementType))
                    {
                        continue;
                    }

                    FieldInfo targetField = elementType.GetField("Target");
                    if (targetField == null || !typeof(Object).IsAssignableFrom(targetField.FieldType))
                    {
                        continue;
                    }

                    if (field.GetValue(binder) is IEnumerable list)
                    {
                        int index = 0;
                        foreach (object element in list)
                        {
                            if (element != null && targetField.GetValue(element) is Object target && target)
                            {
                                if (!targets.TryGetValue(target, out List<ElementRef> refs))
                                {
                                    refs = new List<ElementRef>();
                                    targets[target] = refs;
                                }
                                refs.Add(new ElementRef(field.Name, index));
                            }
                            index++;
                        }
                    }
                }

                type = type.BaseType;
            }

            return targets;
        }

        private static bool IsGenericList(System.Type type, out System.Type elementType)
        {
            elementType = null;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            return false;
        }

        private static bool IsSerialized(FieldInfo field)
        {
            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }
    }
}
