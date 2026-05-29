using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mace;
using Mace.Editor;
using UnityEditor;
using UnityEngine;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// One bindable slot exposed by a <see cref="ComponentBinder"/>: a single serialized
    /// <see cref="BindingInfo"/> (standalone or an element of a <see cref="BindingInfoList"/>).
    /// </summary>
    internal readonly struct BindingSlot
    {
        /// <summary>SerializedProperty path to the <see cref="BindingInfo"/> on the binder.</summary>
        public readonly string PropertyPath;

        /// <summary>Human-readable label shown on the node port.</summary>
        public readonly string DisplayName;

        /// <summary>The observable type the slot expects (e.g. IReadOnlyObservableVariable&lt;object&gt;).</summary>
        public readonly Type TargetType;

        public BindingSlot(string propertyPath, string displayName, Type targetType)
        {
            PropertyPath = propertyPath;
            DisplayName = displayName;
            TargetType = targetType;
        }
    }

    /// <summary>
    /// Reflection + serialized-write helpers that let the graph read and edit the exact same
    /// <see cref="BindingInfo"/> data the Mace inspector drawers use, so runtime behaviour is
    /// unchanged.
    /// </summary>
    internal static class BindingReflection
    {
        private const string ViewModelContainerRelative = "viewModelContainer";
        private const string PathRelative = "path";
        private const string ListRelative = "bindingInfoList";

        private static readonly BindingFlags FieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static List<Type> cachedBinderTypes;

        /// <summary>All concrete, instantiable <see cref="ComponentBinder"/> subclasses (for the "Add Binder" menu).</summary>
        public static IReadOnlyList<Type> GetBinderTypes()
        {
            if (cachedBinderTypes != null)
            {
                return cachedBinderTypes;
            }

            cachedBinderTypes = TypeCache.GetTypesDerivedFrom<ComponentBinder>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                .OrderBy(t => t.Name)
                .ToList();

            return cachedBinderTypes;
        }

        /// <summary>
        /// Enumerates the bindable slots of a binder by walking its serialized
        /// <see cref="BindingInfo"/> / <see cref="BindingInfoList"/> fields up the type hierarchy.
        /// </summary>
        public static IEnumerable<BindingSlot> GetBindingSlots(Component binder)
        {
            Type type = binder.GetType();

            while (type != null && type != typeof(ComponentBinder) && type != typeof(MonoBehaviour))
            {
                foreach (FieldInfo field in type.GetFields(FieldFlags))
                {
                    if (!IsSerialized(field))
                    {
                        continue;
                    }

                    if (typeof(BindingInfo).IsAssignableFrom(field.FieldType))
                    {
                        var info = field.GetValue(binder) as BindingInfo;
                        yield return new BindingSlot(field.Name, Prettify(field.Name), info?.Type);
                    }
                    else if (typeof(BindingInfoList).IsAssignableFrom(field.FieldType))
                    {
                        var list = field.GetValue(binder) as BindingInfoList;
                        if (list == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < list.BindingInfos.Count; i++)
                        {
                            string path = $"{field.Name}.{ListRelative}.Array.data[{i}]";
                            yield return new BindingSlot(path, $"{Prettify(field.Name)} [{i}]", list.BindingInfos[i]?.Type);
                        }
                    }
                }

                type = type.BaseType;
            }
        }

        /// <summary>
        /// Top-level serialized field names of a binder whose type is <see cref="BindingInfo"/> or
        /// <see cref="BindingInfoList"/>. These are rendered as ports, so node bodies skip them.
        /// </summary>
        public static HashSet<string> GetBindingFieldNames(Component binder)
        {
            var names = new HashSet<string>();
            Type type = binder.GetType();

            while (type != null && type != typeof(ComponentBinder) && type != typeof(MonoBehaviour))
            {
                foreach (FieldInfo field in type.GetFields(FieldFlags))
                {
                    if (!IsSerialized(field))
                    {
                        continue;
                    }

                    if (typeof(BindingInfo).IsAssignableFrom(field.FieldType) ||
                        typeof(BindingInfoList).IsAssignableFrom(field.FieldType))
                    {
                        names.Add(field.Name);
                    }
                }

                type = type.BaseType;
            }

            return names;
        }

        /// <summary>Reads the currently-bound (viewModel, propertyName) for a slot, if any.</summary>
        public static bool TryGetBinding(Component binder, string propertyPath, out ViewModelComponent viewModel, out string propertyName)
        {
            viewModel = null;
            propertyName = null;

            var serializedObject = new SerializedObject(binder);
            SerializedProperty bindingInfo = serializedObject.FindProperty(propertyPath);
            if (bindingInfo == null)
            {
                return false;
            }

            viewModel = bindingInfo.FindPropertyRelative(ViewModelContainerRelative).objectReferenceValue as ViewModelComponent;
            propertyName = bindingInfo.FindPropertyRelative(PathRelative).stringValue;
            return viewModel != null && !string.IsNullOrEmpty(propertyName);
        }

        /// <summary>
        /// Writes a binding into a slot, mirroring <c>BindingInfoDrawer.SetBinding</c>.
        /// Pass <paramref name="viewModel"/> = null / empty name to clear it.
        /// </summary>
        public static void SetBinding(Component binder, string propertyPath, ViewModelComponent viewModel, string propertyName)
        {
            var serializedObject = new SerializedObject(binder);
            SerializedProperty bindingInfo = serializedObject.FindProperty(propertyPath);
            if (bindingInfo == null)
            {
                return;
            }

            bindingInfo.FindPropertyRelative(ViewModelContainerRelative).objectReferenceValue = viewModel;
            bindingInfo.FindPropertyRelative(PathRelative).stringValue = propertyName ?? string.Empty;
            serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(binder);
            BindingInfoTracker.RefreshBindingInfoDrawers();
        }

        public static void ClearBinding(Component binder, string propertyPath)
        {
            SetBinding(binder, propertyPath, null, string.Empty);
        }

        private static bool IsSerialized(FieldInfo field)
        {
            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }

        private static string Prettify(string fieldName)
        {
            return ObjectNames.NicifyVariableName(fieldName);
        }
    }
}
