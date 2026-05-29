using System;
using System.Collections.Generic;
using Mace;
using Mace.Utils;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Uice.VisualBinder.Editor
{
    /// <summary>Data attached to a ViewModel output port (a bindable source property).</summary>
    internal sealed class BindingSourcePortData
    {
        public ViewModelComponent ViewModel;
        public string PropertyName;
        public Type ActualType;
    }

    /// <summary>Data attached to a binder input port (a single <see cref="BindingInfo"/> slot).</summary>
    internal sealed class BindingTargetPortData
    {
        public Component Binder;
        public string PropertyPath;
        public Type TargetType;
    }

    internal static class ObservableTypeUtility
    {
        private static readonly Type GenericVariableType = typeof(IReadOnlyObservableVariable<>);
        private static readonly Type GenericCollectionType = typeof(IReadOnlyObservableCollection<>);
        private static readonly Type GenericCommandType = typeof(IObservableCommand<>);
        private static readonly Type CommandType = typeof(IObservableCommand);
        private static readonly Type GenericEventType = typeof(IObservableEvent<>);
        private static readonly Type EventType = typeof(IObservableEvent);

        public static readonly Color VariableColor = new Color(0.40f, 0.65f, 0.95f);
        public static readonly Color CollectionColor = new Color(0.45f, 0.80f, 0.50f);
        public static readonly Color CommandColor = new Color(0.95f, 0.65f, 0.35f);
        public static readonly Color EventColor = new Color(0.80f, 0.55f, 0.90f);
        public static readonly Color UnknownColor = new Color(0.7f, 0.7f, 0.7f);

        public static Color GetColor(Type observableType)
        {
            if (observableType == null)
            {
                return UnknownColor;
            }

            observableType = NormalizeToDefinition(observableType);

            if (observableType.ImplementsOrDerives(GenericVariableType))
            {
                return VariableColor;
            }
            if (observableType.ImplementsOrDerives(GenericCollectionType))
            {
                return CollectionColor;
            }
            if (observableType.ImplementsOrDerives(GenericCommandType) || observableType.ImplementsOrDerives(CommandType))
            {
                return CommandColor;
            }
            if (observableType.ImplementsOrDerives(GenericEventType) || observableType.ImplementsOrDerives(EventType))
            {
                return EventColor;
            }

            return UnknownColor;
        }

        /// <summary>Reduces a constructed generic (e.g. Variable&lt;string&gt;) to its definition for kind checks.</summary>
        private static Type NormalizeToDefinition(Type type)
        {
            Type generic = type.GetGenericTypeTowardsRoot();
            if (generic != null)
            {
                return generic.GetGenericTypeDefinition();
            }

            return type;
        }

        public static string Describe(Type observableType, Type genericArgument)
        {
            if (observableType == null)
            {
                return "?";
            }

            observableType = NormalizeToDefinition(observableType);
            string arg = genericArgument != null ? genericArgument.GetPrettifiedName() : null;

            if (observableType.ImplementsOrDerives(GenericVariableType))
            {
                return $"Variable<{arg}>";
            }
            if (observableType.ImplementsOrDerives(GenericCollectionType))
            {
                return $"Collection<{arg}>";
            }
            if (observableType.ImplementsOrDerives(GenericCommandType))
            {
                return $"Command<{arg}>";
            }
            if (observableType.ImplementsOrDerives(GenericEventType))
            {
                return $"Event<{arg}>";
            }
            if (observableType.ImplementsOrDerives(CommandType))
            {
                return "Command";
            }
            if (observableType.ImplementsOrDerives(EventType))
            {
                return "Event";
            }

            return observableType.GetPrettifiedName();
        }

        /// <summary>Extracts the first generic argument of an observable type (e.g. string from Variable&lt;string&gt;).</summary>
        public static Type GetGenericArgument(Type targetType)
        {
            if (targetType == null)
            {
                return null;
            }

            Type generic = targetType.GetGenericTypeTowardsRoot();
            return generic != null && generic.GenericTypeArguments.Length > 0 ? generic.GenericTypeArguments[0] : null;
        }

        /// <summary>Reconstructs the actual property type from a discovered binding entry.</summary>
        public static Type ReconstructActualType(BindingEntry entry)
        {
            if (entry.ObservableType == null)
            {
                return typeof(object);
            }

            if (entry.ObservableType.IsGenericTypeDefinition && entry.GenericArgument != null)
            {
                return entry.ObservableType.MakeGenericType(entry.GenericArgument);
            }

            return entry.ObservableType;
        }
    }

    /// <summary>A graph node backed by a scene/prefab component (used for stable layout keys).</summary>
    internal interface IComponentNode
    {
        Component Component { get; }
    }

    /// <summary>A ViewModelComponent rendered as a source node with one output port per bindable property.</summary>
    internal sealed class ViewModelNode : Node, IComponentNode
    {
        public ViewModelComponent ViewModel { get; }

        public Component Component => ViewModel;

        /// <summary>Output ports keyed by property name (the view model's bindable properties).</summary>
        public Dictionary<string, Port> PortsByProperty { get; } = new Dictionary<string, Port>();

        /// <summary>
        /// Input ports keyed by BindingInfo serialized path. Non-empty for components that both expose
        /// a view model and consume one, e.g. <c>BindableViewModelComponent</c>.
        /// </summary>
        public Dictionary<string, Port> PortsByPath { get; } = new Dictionary<string, Port>();

        public ViewModelNode(ViewModelComponent viewModel)
        {
            ViewModel = viewModel;

            string typeName = viewModel.ExpectedType != null ? viewModel.ExpectedType.GetPrettifiedName() : "<no type>";
            title = $"{viewModel.gameObject.name}  ▸  {typeName}";
            tooltip = $"ViewModel · id {viewModel.Id}";
            AddToClassList("uice-viewmodel-node");

            // Input ports: this component's own BindingInfo slots (the data source feeding it).
            foreach (BindingSlot slot in BindingReflection.GetBindingSlots(viewModel))
            {
                AddSlotPort(slot);
            }

            // Output ports: the bindable properties this view model exposes.
            if (viewModel.ExpectedType != null)
            {
                foreach (BindingEntry entry in BindingUtils.GetAllBindings(viewModel.ExpectedType, viewModel))
                {
                    AddPropertyPort(entry);
                }
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        private void AddSlotPort(BindingSlot slot)
        {
            Port port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(object));
            port.portName = $"{slot.DisplayName}  :  {ObservableTypeUtility.Describe(slot.TargetType, ObservableTypeUtility.GetGenericArgument(slot.TargetType))}";
            port.portColor = ObservableTypeUtility.GetColor(slot.TargetType);
            port.userData = new BindingTargetPortData
            {
                Binder = ViewModel,
                PropertyPath = slot.PropertyPath,
                TargetType = slot.TargetType
            };

            inputContainer.Add(port);
            PortsByPath[slot.PropertyPath] = port;
        }

        private void AddPropertyPort(BindingEntry entry)
        {
            Port port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(object));
            port.portName = $"{entry.PropertyName}  :  {ObservableTypeUtility.Describe(entry.ObservableType, entry.GenericArgument)}";
            port.portColor = ObservableTypeUtility.GetColor(entry.ObservableType);
            port.userData = new BindingSourcePortData
            {
                ViewModel = ViewModel,
                PropertyName = entry.PropertyName,
                ActualType = ObservableTypeUtility.ReconstructActualType(entry)
            };

            outputContainer.Add(port);
            PortsByProperty[entry.PropertyName] = port;
        }
    }

    /// <summary>A ComponentBinder rendered as a target node with one input port per BindingInfo slot.</summary>
    internal sealed class BinderNode : Node, IComponentNode
    {
        public Component Binder { get; }

        public Component Component => Binder;

        /// <summary>Input ports keyed by the BindingInfo serialized property path.</summary>
        public Dictionary<string, Port> PortsByPath { get; } = new Dictionary<string, Port>();

        /// <summary>Kept alive so the bound PropertyFields in the node body keep updating.</summary>
        private SerializedObject serializedObject;

        public BinderNode(Component binder)
        {
            Binder = binder;

            title = $"{binder.gameObject.name}  ▸  {binder.GetType().Name}";
            tooltip = binder.GetType().FullName;
            AddToClassList("uice-binder-node");

            foreach (BindingSlot slot in BindingReflection.GetBindingSlots(binder))
            {
                AddSlotPort(slot);
            }

            BuildFieldsBody();

            RefreshExpandedState();
            RefreshPorts();
        }

        /// <summary>
        /// Renders the binder's non-binding serialized fields as editable PropertyFields inside a
        /// collapsible "Fields" foldout (collapsed by default; its state is remembered per binder).
        /// </summary>
        private void BuildFieldsBody()
        {
            serializedObject = new SerializedObject(Binder);
            HashSet<string> bindingFields = BindingReflection.GetBindingFieldNames(Binder);

            var foldout = new Foldout { text = "Fields", value = LoadFieldsExpanded() };

            int added = 0;
            SerializedProperty iterator = serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                do
                {
                    if (iterator.propertyPath == "m_Script" || bindingFields.Contains(iterator.name))
                    {
                        continue;
                    }

                    foldout.Add(new PropertyField(iterator.Copy()));
                    added++;
                }
                while (iterator.NextVisible(false));
            }

            if (added == 0)
            {
                return;
            }

            // Only the foldout's own toggle should persist state — ignore bubbled events from
            // child bool fields (e.g. Toggle PropertyFields) which also raise ChangeEvent<bool>.
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                {
                    SaveFieldsExpanded(evt.newValue);
                }
            });

            extensionContainer.Add(foldout);
            extensionContainer.Bind(serializedObject);
        }

        private string FieldsPrefKey()
        {
            string key = VisualBinderLayoutStore.GetKey(Binder);
            return string.IsNullOrEmpty(key) ? null : "UiceVisualBinder.FieldsExpanded." + key;
        }

        private bool LoadFieldsExpanded()
        {
            string key = FieldsPrefKey();
            return key != null && EditorPrefs.GetBool(key, false);
        }

        private void SaveFieldsExpanded(bool expanded)
        {
            string key = FieldsPrefKey();
            if (key != null)
            {
                EditorPrefs.SetBool(key, expanded);
            }
        }

        private void AddSlotPort(BindingSlot slot)
        {
            Port port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(object));
            port.portName = $"{slot.DisplayName}  :  {ObservableTypeUtility.Describe(slot.TargetType, ObservableTypeUtility.GetGenericArgument(slot.TargetType))}";
            port.portColor = ObservableTypeUtility.GetColor(slot.TargetType);
            port.userData = new BindingTargetPortData
            {
                Binder = Binder,
                PropertyPath = slot.PropertyPath,
                TargetType = slot.TargetType
            };

            inputContainer.Add(port);
            PortsByPath[slot.PropertyPath] = port;
        }
    }
}
