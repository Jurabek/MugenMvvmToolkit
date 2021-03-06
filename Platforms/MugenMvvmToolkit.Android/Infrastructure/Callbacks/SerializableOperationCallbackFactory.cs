#region Copyright

// ****************************************************************************
// <copyright file="SerializableOperationCallbackFactory.cs">
// Copyright (c) 2012-2015 Vyacheslav Volkov
// </copyright>
// ****************************************************************************
// <author>Vyacheslav Volkov</author>
// <email>vvs0205@outlook.com</email>
// <project>MugenMvvmToolkit</project>
// <web>https://github.com/MugenMvvmToolkit/MugenMvvmToolkit</web>
// <license>
// See license.txt in this solution or http://opensource.org/licenses/MS-PL
// </license>
// ****************************************************************************

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using JetBrains.Annotations;
using MugenMvvmToolkit.Interfaces.Callbacks;
using MugenMvvmToolkit.Interfaces.Models;
using MugenMvvmToolkit.Interfaces.Navigation;
using MugenMvvmToolkit.Interfaces.ViewModels;
using MugenMvvmToolkit.Models;
using MugenMvvmToolkit.ViewModels;

namespace MugenMvvmToolkit.Infrastructure.Callbacks
{
    //NOTE do you want to see some magic? :)
    /// <summary>
    ///     Rerpresets the factory that allows to create serializable callback operations.
    /// </summary>
    public class SerializableOperationCallbackFactory : IOperationCallbackFactory
    {
        #region Nested types

        [DataContract(Namespace = ApplicationSettings.DataContractNamespace, IsReference = true)]
#if ANDROID
        [Serializable]
#endif
        internal sealed class FieldSnapshot
        {
            #region Fields

            private const int SerializableField = 1;
            private const int AwaiterField = 2;
            private const int NonSerializableField = 3;
            private const int ViewModelField = 4;
            private const int AnonymousClass = 5;
            private const int BuilderField = 6;

            #endregion

            #region Properties

            [DataMember(Name = "n", EmitDefaultValue = false)]
            public string Name { get; set; }

            [DataMember(Name = "t", EmitDefaultValue = false)]
            public string TypeName { get; set; }

            [DataMember(Name = "s", EmitDefaultValue = false)]
            public object State { get; set; }

            [DataMember(Name = "sn", EmitDefaultValue = false)]
            public List<FieldSnapshot> Snapshots { get; set; }

            [DataMember(Name = "f", EmitDefaultValue = false)]
            public int FieldType { get; set; }

            [DataMember(Name = "ist", EmitDefaultValue = false)]
            public bool IsType { get; set; }

            #endregion

            #region Methods

            public bool Restore(Type targetType, object target, Dictionary<Type, object> items, ICollection<IViewModel> viewModels, string awaiterResultType, IOperationResult result)
            {
                var field = targetType.GetFieldEx(Name, MemberFlags.NonPublic | MemberFlags.Public | MemberFlags.Instance);
                if (field == null)
                {
                    TraceError(null, targetType);
                    return false;
                }
                switch (FieldType)
                {
                    case BuilderField:
                        var type = Type.GetType(TypeName, true);
                        var createMethod = type.GetMethodEx("Create", MemberFlags.NonPublic | MemberFlags.Public | MemberFlags.Static);
                        if (createMethod == null)
                        {
                            TraceError(field, targetType);
                            return false;
                        }
                        var startMethod = type.GetMethodEx("Start", MemberFlags.NonPublic | MemberFlags.Public | MemberFlags.Instance);
                        if (startMethod == null || !startMethod.IsGenericMethodDefinition)
                        {
                            TraceError(field, targetType);
                            return false;
                        }
                        var builder = createMethod.Invoke(null, Empty.Array<object>());
                        field.SetValueEx(target, builder);
                        startMethod.MakeGenericMethod(typeof(IAsyncStateMachine))
                                   .Invoke(builder, new[] { target });
                        break;
                    case AwaiterField:
                        var awaiterType = typeof(SerializableAwaiter<>).MakeGenericType(Type.GetType(awaiterResultType, true));
                        var instance = Activator.CreateInstance(awaiterType, result);
                        field.SetValueEx(target, instance);
                        break;
                    case AnonymousClass:
                        var anonType = Type.GetType(TypeName, true);
                        object anonClass;
                        if (!items.TryGetValue(anonType, out anonClass))
                        {
                            anonClass = ServiceProvider.IocContainer.Get(anonType);
                            foreach (var snapshot in Snapshots)
                                snapshot.Restore(anonType, anonClass, items, viewModels, awaiterResultType, result);
                            items[anonType] = anonClass;
                        }
                        field.SetValueEx(target, anonClass);
                        break;
                    case NonSerializableField:
                        object service;
                        if (State == null)
                        {
                            var serviceType = Type.GetType(TypeName, true);
                            if (!items.TryGetValue(serviceType, out service))
                            {
                                service = ServiceProvider.IocContainer.Get(serviceType);
                                items[serviceType] = service;
                            }
                        }
                        else
                        {
                            var restoreValueState = RestoreValueState;
                            service = restoreValueState == null ? State : restoreValueState(State);
                        }
                        field.SetValueEx(target, service);
                        break;
                    case SerializableField:
                        field.SetValueEx(target, IsType ? Type.GetType((string)State, false) : State);
                        break;
                    case ViewModelField:
                        var viewModel = FindViewModel(viewModels);
                        if (viewModel == null)
                        {
                            TraceError(field, targetType);
                            return false;
                        }
                        field.SetValueEx(target, viewModel);
                        break;
                }
                return true;
            }

            public static FieldSnapshot Create(FieldInfo field, object target)
            {
                var isStateMachine = target is IAsyncStateMachine;
                if (isStateMachine)
                {
                    if (field.Name == "$Builder" || field.Name == "<>t__builder")
                        return new FieldSnapshot
                        {
                            FieldType = BuilderField,
                            Name = field.Name,
                            TypeName = field.FieldType.AssemblyQualifiedName
                        };
                }

                var value = field.GetValueEx<object>(target);
                if (value == null || value is IAsyncStateMachine)
                    return null;

                if (isStateMachine && field.Name.Contains("$awaiter"))
                {
                    if (value is IAsyncOperationAwaiter)
                        return new FieldSnapshot { Name = field.Name, FieldType = AwaiterField };
                    return null;
                }

                var viewModel = value as IViewModel;
                if (viewModel != null)
                {
                    return new FieldSnapshot
                    {
                        Name = field.Name,
                        FieldType = ViewModelField,
                        TypeName = value.GetType().AssemblyQualifiedName,
                        State = viewModel.GetViewModelId()
                    };
                }
                //field is type.
                if (typeof(Type).IsAssignableFrom(field.FieldType))
                    return new FieldSnapshot
                    {
                        State = ((Type)value).AssemblyQualifiedName,
                        FieldType = SerializableField,
                        Name = field.Name,
                        IsType = true
                    };

                if (field.FieldType.IsSerializable())
                    return new FieldSnapshot
                    {
                        State = value,
                        FieldType = SerializableField,
                        Name = field.Name
                    };
                //Anonymous class
                if (field.FieldType.IsAnonymousClass())
                {
                    var type = value.GetType();
                    var snapshots = new List<FieldSnapshot>();
                    foreach (var anonymousField in type.GetFieldsEx(MemberFlags.Instance | MemberFlags.NonPublic | MemberFlags.Public))
                    {
                        var snapshot = Create(anonymousField, value);
                        if (snapshot != null)
                            snapshots.Add(snapshot);
                    }
                    return new FieldSnapshot
                    {
                        FieldType = AnonymousClass,
                        Name = field.Name,
                        Snapshots = snapshots,
                        TypeName = type.AssemblyQualifiedName
                    };
                }
                var saveValueState = SaveValueState;
                if (saveValueState != null)
                {
                    var valueState = saveValueState(value);
                    if (valueState != null)
                        return new FieldSnapshot
                        {
                            Name = field.Name,
                            State = valueState,
                            FieldType = NonSerializableField
                        };
                }

                return new FieldSnapshot
                {
                    Name = field.Name,
                    TypeName = value.GetType().AssemblyQualifiedName,
                    FieldType = NonSerializableField
                };
            }

            private IViewModel FindViewModel(ICollection<IViewModel> viewModels)
            {
                Guid id = Guid.Empty;
                var vmType = Type.GetType(TypeName, true);
                if (State is Guid)
                    id = (Guid)State;

                var vm = ViewModelProvider.TryGetViewModelById(id);
                if (vm != null)
                    return vm;
                foreach (var viewModel in viewModels)
                {
                    if (viewModel.GetViewModelId() == id)
                        return viewModel;
                    if (viewModel.GetType() == vmType)
                        vm = viewModel;
                }
                return vm;
            }

            private void TraceError(FieldInfo field, Type stateMachineType)
            {
                string fieldSt = field == null ? Name : field.ToString();
                Tracer.Error("The field {0} cannot be restored on type {1}", fieldSt, stateMachineType);
            }

            #endregion
        }

        /// <summary>
        /// Rerpresents the serializable callback that allows to restore async callback.
        /// </summary>
        [DataContract(Namespace = ApplicationSettings.DataContractNamespace, IsReference = true)]
#if ANDROID
        [Serializable]
#endif
        internal sealed class AwaiterSerializableCallback : ISerializableCallback
        {
            #region Constructors

            public AwaiterSerializableCallback(Action continuation, IAsyncStateMachine stateMachine, string awaiterResultType)
            {
                AwaiterResultType = awaiterResultType;
                Initialize(continuation, stateMachine);
            }

            #endregion

            #region Properties

            [DataMember(EmitDefaultValue = false)]
            public string AwaiterResultType { get; set; }

            [DataMember(EmitDefaultValue = false)]
            public string StateMachineType { get; set; }

            [DataMember(EmitDefaultValue = false)]
            public List<FieldSnapshot> FieldSnapshots { get; set; }

            #endregion

            #region Methods

            private void Initialize(Action continuation, IAsyncStateMachine stateMachine)
            {
                const MemberFlags flags = MemberFlags.NonPublic | MemberFlags.Public | MemberFlags.Instance;
                if (stateMachine == null)
                {
                    stateMachine = continuation.Target as IAsyncStateMachine;
                    if (stateMachine == null)
                    {
                        var fieldInfo = continuation.Target
                            .GetType()
                            .GetFieldEx("m_continuation", flags);
                        if (fieldInfo != null)
                            continuation = fieldInfo.GetValueEx<Action>(continuation.Target);

                        fieldInfo = continuation.Target
                            .GetType()
                            .GetFieldEx("m_stateMachine", flags);
                        if (fieldInfo == null)
                        {
                            TraceError(continuation.Target);
                            return;
                        }
                        stateMachine = fieldInfo.GetValueEx<IAsyncStateMachine>(continuation.Target);
                        if (stateMachine == null)
                        {
                            TraceError(continuation.Target);
                            return;
                        }
                    }
                }
                var type = stateMachine.GetType();
                StateMachineType = type.AssemblyQualifiedName;
                FieldSnapshots = new List<FieldSnapshot>();

                foreach (var field in type.GetFieldsEx(flags))
                {
                    var snapshot = FieldSnapshot.Create(field, stateMachine);
                    if (snapshot != null)
                        FieldSnapshots.Add(snapshot);
                }
            }

            private static void TraceError(object target)
            {
                Tracer.Error("The serializable awaiter cannot get IAsyncStateMachine from target {0}",
                    target);
            }

            #endregion

            #region Implementation of ISerializableCallback

            public object Invoke(IOperationResult result)
            {
                if (StateMachineType == null || FieldSnapshots == null)
                {
                    Tracer.Error("The await callback cannot be executed.");
                    return null;
                }
                var type = Type.GetType(StateMachineType, true);
                IAsyncStateMachine stateMachine;
#if NETFX_CORE || WINDOWSCOMMON
                if (type.GetTypeInfo().IsValueType)
#else
                if (type.IsValueType)
#endif

                    stateMachine = (IAsyncStateMachine)GetDefault(type);
                else
                {
                    var constructor = type.GetConstructor(Empty.Array<Type>());
                    if (constructor == null)
                    {
                        Tracer.Error("The await callback cannot be executed.");
                        return null;
                    }
                    stateMachine = (IAsyncStateMachine)constructor.InvokeEx();
                }

                var viewModels = CollectViewModels(result);
                var items = new Dictionary<Type, object>();
                if (result.Source != null)
                    items[result.Source.GetType()] = result.Source;
                //we need to sort fields, to restore builder as last operation.
                FieldSnapshots.Sort((x1, x2) => x1.FieldType.CompareTo(x2.FieldType));
                for (int index = 0; index < FieldSnapshots.Count; index++)
                {
                    if (!FieldSnapshots[index].Restore(type, stateMachine, items, viewModels, AwaiterResultType, result))
                    {
                        Tracer.Error("The await callback cannot be executed.");
                        break;
                    }
                }
                return null;
            }

            #endregion
        }

        private sealed class AwaiterContinuation : IActionContinuation
        {
            #region Fields

            private readonly Action _continuation;
            private readonly IHasStateMachine _hasStateMachine;
            private readonly Type _resultType;
            private ISerializableCallback _serializableCallback;

            #endregion

            #region Constructors

            public AwaiterContinuation(Action continuation, IHasStateMachine hasStateMachine, Type resultType)
            {
                Should.NotBeNull(continuation, "continuation");
                _continuation = continuation;
                _hasStateMachine = hasStateMachine;
                _resultType = resultType;
            }

            #endregion

            #region Implementation of IContinuation

            public ISerializableCallback ToSerializableCallback()
            {
                if (_serializableCallback == null)
                    _serializableCallback = new AwaiterSerializableCallback(_continuation, _hasStateMachine.StateMachine,
                        _resultType.AssemblyQualifiedName);
                return _serializableCallback;
            }

            public void Invoke(IOperationResult result)
            {
                _continuation();
            }

            #endregion
        }

        private interface IHasStateMachine
        {
            IAsyncStateMachine StateMachine { get; }
        }

        private sealed class SerializableAwaiter<TResult> : IAsyncOperationAwaiter, IAsyncOperationAwaiter<TResult>, IAsyncStateMachineAware, IHasStateMachine
        {
            #region Fields

            private readonly IOperationResult _result;
            private readonly IAsyncOperation _operation;
            private IAsyncStateMachine _stateMachine;

            #endregion

            #region Constructors

            public SerializableAwaiter(IAsyncOperation operation)
            {
                _operation = operation;
            }

            [UsedImplicitly]
            public SerializableAwaiter(IOperationResult result)
            {
                _result = result;
            }

            #endregion

            #region Implementation of IAsyncOperationAwaiter

            public void OnCompleted(Action continuation)
            {
                _operation.ContinueWith(new AwaiterContinuation(continuation, this, typeof(TResult)));
            }

            public bool IsCompleted
            {
                get { return _result != null || _operation.IsCompleted; }
            }

            TResult IAsyncOperationAwaiter<TResult>.GetResult()
            {
                IOperationResult result = _result ?? _operation.Result;
                return (TResult)result.Result;
            }

            void IAsyncOperationAwaiter.GetResult()
            {
                IOperationResult result = _result ?? _operation.Result;
                var o = result.Result;
            }

            void IAsyncStateMachineAware.SetStateMachine(IAsyncStateMachine stateMachine)
            {
                _stateMachine = stateMachine;
            }

            #endregion

            #region Implementation of IHasStateMachine

            public IAsyncStateMachine StateMachine
            {
                get { return _stateMachine; }
            }

            #endregion
        }

        /// <summary>
        /// Rerpresents the serializable callback that allows to restore delegate callback.
        /// </summary>
        [DataContract(Namespace = ApplicationSettings.DataContractNamespace, IsReference = true)]
#if ANDROID
        [Serializable]
#endif
        internal sealed class DelegateSerializableCallback : ISerializableCallback
        {
            #region Fields

            [DataMember(EmitDefaultValue = false)]
            internal string TargetType;

            [DataMember(EmitDefaultValue = false)]
            internal bool FirstParameterSource;

            [DataMember(EmitDefaultValue = false)]
            internal bool IsStatic;

            [DataMember(EmitDefaultValue = false)]
            internal object Target;

            [DataMember(EmitDefaultValue = false)]
            internal string MethodName;

            [DataMember(EmitDefaultValue = false)]
            internal List<FieldSnapshot> Snapshots;

            #endregion

            #region Constructors

            public DelegateSerializableCallback(string targetType, string methodName, bool firstParameterSource, bool isStatic, object target, List<FieldSnapshot> snapshots)
            {
                Should.NotBeNull(targetType, "targetType");
                Should.NotBeNull(methodName, "methodName");
                Snapshots = snapshots;
                TargetType = targetType;
                MethodName = methodName;
                FirstParameterSource = firstParameterSource;
                IsStatic = isStatic;
                Target = target;
            }

            #endregion

            #region Implementation of ISerializableCallback

            /// <summary>
            ///     Invokes the callback using the specified operation result.
            /// </summary>
            public object Invoke(IOperationResult result)
            {
                var invokeInternal = InvokeInternal(result);
                Tracer.Info("The restored callback was invoked, target type '{0}', method '{1}'", TargetType, MethodName);
                return invokeInternal;
            }

            #endregion

            #region Methods

            private object InvokeInternal(IOperationResult result)
            {
                var type = Type.GetType(TargetType, true);
                var flags = MemberFlags.Public | MemberFlags.NonPublic |
                                                    (IsStatic ? MemberFlags.Static : MemberFlags.Instance);
                var method = type.GetMethodsEx(flags).First(FilterMethod);
                var viewModels = CollectViewModels(result);
                var items = new Dictionary<Type, object>();
                if (result.Source != null)
                    items[result.Source.GetType()] = result.Source;
                object[] args;
                if (FirstParameterSource)
                {
                    var parameter = method.GetParameters()[0];
                    object firstParam;
                    if (!items.TryGetValue(parameter.ParameterType, out firstParam))
                    {
                        var viewModel = viewModels.FirstOrDefault(model => model.GetType() == parameter.ParameterType);
                        firstParam = viewModel ?? result.Source;
                    }
                    args = new[] { firstParam, result };
                }
                else
                    args = new object[] { result };
                if (IsStatic)
                    return method.InvokeEx(null, args);

                object target = Target;
                if (target == null)
                {
                    if (!items.TryGetValue(type, out target))
                    {
                        target = ServiceProvider.IocContainer.Get(type);
                        items[type] = target;
                    }
                }
                if (Snapshots != null)
                {
                    foreach (var fieldSnapshot in Snapshots)
                        fieldSnapshot.Restore(type, target, items, viewModels, null, result);
                }
                return method.InvokeEx(target, args);
            }

            private bool FilterMethod(MethodInfo method)
            {
                if (method.Name != MethodName)
                    return false;
                var parameters = method.GetParameters();
                if (FirstParameterSource)
                {
                    if (parameters.Length == 2 && typeof(IOperationResult).IsAssignableFrom(parameters[1].ParameterType))
                        return true;
                }
                else
                {
                    if (parameters.Length == 1 && typeof(IOperationResult).IsAssignableFrom(parameters[0].ParameterType))
                        return true;
                }
                return false;
            }

            #endregion
        }

        #endregion

        #region Fields

        private static readonly MethodInfo GetDefaultGenericMethod;

        #endregion

        #region Constructors

        static SerializableOperationCallbackFactory()
        {
            GetDefaultGenericMethod = typeof(SerializableOperationCallbackFactory)
                .GetMethodEx("GetDefaultGeneric", MemberFlags.Public | MemberFlags.NonPublic | MemberFlags.Static);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the delegate that allows to convert non serializable value to serializable.
        /// </summary>
        [CanBeNull]
        public static Func<object, object> SaveValueState { get; set; }

        /// <summary>
        /// Gets or sets the delegate that allows to convert from serializable value to original.
        /// </summary>
        [CanBeNull]
        public static Func<object, object> RestoreValueState { get; set; }

        #endregion

        #region Implementation of IOperationCallbackFactory

        /// <summary>
        ///     Creates an instance of <see cref="IAsyncOperationAwaiter" />.
        /// </summary>
        public IAsyncOperationAwaiter CreateAwaiter(IAsyncOperation operation, IDataContext context)
        {
            Should.NotBeNull(operation, "operation");
            return new SerializableAwaiter<object>(operation);
        }

        /// <summary>
        ///     Creates an instance of <see cref="IAsyncOperationAwaiter{TResult}" />.
        /// </summary>
        public IAsyncOperationAwaiter<TResult> CreateAwaiter<TResult>(IAsyncOperation<TResult> operation,
            IDataContext context)
        {
            Should.NotBeNull(operation, "operation");
            return new SerializableAwaiter<TResult>(operation);
        }

        /// <summary>
        ///     Tries to convert a delegate to an instance of <see cref="ISerializableCallback" />.
        /// </summary>
        public ISerializableCallback CreateSerializableCallback(Delegate @delegate)
        {
            Should.NotBeNull(@delegate, "delegate");
            var method = @delegate.GetMethodInfo();
            bool firstParameterSource;
            if (!CheckMethodParameters(method, out firstParameterSource))
            {
                Tracer.Warn("The method '{0}' cannot be serialized, invalid parameters.", method);
                return null;
            }
            if (method.IsStatic)
                return new DelegateSerializableCallback(method.DeclaringType.AssemblyQualifiedName, method.Name,
                    firstParameterSource, true, null, null);

            var target = @delegate.Target;
            var targetType = target.GetType();
            if (targetType.IsAnonymousClass())
            {
                var snapshots = new List<FieldSnapshot>();
                foreach (var anonymousField in targetType.GetFieldsEx(MemberFlags.Instance | MemberFlags.NonPublic | MemberFlags.Public))
                {
                    var snapshot = FieldSnapshot.Create(anonymousField, target);
                    if (snapshot != null)
                        snapshots.Add(snapshot);
                }
                return new DelegateSerializableCallback(targetType.AssemblyQualifiedName, method.Name, firstParameterSource,
                    false, null, snapshots);
            }
            return new DelegateSerializableCallback(targetType.AssemblyQualifiedName, method.Name, firstParameterSource,
                false, targetType.IsSerializable() ? target : null, null);
        }

        #endregion

        #region Methods

        private static object GetDefault(Type t)
        {
            return GetDefaultGenericMethod.MakeGenericMethod(t).InvokeEx(null, null);
        }

        private static ICollection<IViewModel> CollectViewModels(IOperationResult result)
        {
#if (WINDOWS_PHONE && V71) || TEST
            var viewModels = new List<IViewModel>();
#else
            var viewModels = new HashSet<IViewModel>();
#endif
            var context = result.OperationContext as INavigationContext;
            if (context != null)
            {
                CollectViewModels(viewModels, context.ViewModelTo);
                CollectViewModels(viewModels, context.ViewModelFrom);
            }
            var viewModel = result.Source as IViewModel;
            if (viewModel != null)
                CollectViewModels(viewModels, viewModel);
            return viewModels;
        }

        private static void CollectViewModels(ICollection<IViewModel> viewModels, IViewModel viewModel)
        {
            while (true)
            {
                if (viewModel == null || viewModels.Contains(viewModel))
                    return;
                var parentViewModel = viewModel.GetParentViewModel();
                if (parentViewModel != null)
                    CollectViewModels(viewModels, parentViewModel);

                viewModels.Add(viewModel);
                var wrapperViewModel = viewModel as IWrapperViewModel;
                if (wrapperViewModel == null)
                    break;
                viewModel = wrapperViewModel.ViewModel;
            }
        }

        private static bool CheckMethodParameters(MethodInfo method, out bool firstParameterSource)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                firstParameterSource = false;
                return true;
            }
            if (parameters.Length == 1 && typeof(IOperationResult).IsAssignableFrom(parameters[0].ParameterType))
            {
                firstParameterSource = false;
                return true;
            }
            if (parameters.Length == 2 && typeof(IOperationResult).IsAssignableFrom(parameters[1].ParameterType))
            {
                firstParameterSource = true;
                return true;
            }
            firstParameterSource = false;
            return false;
        }

        internal static T GetDefaultGeneric<T>()
        {
            return default(T);
        }

        #endregion
    }
}