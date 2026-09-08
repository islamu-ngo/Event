namespace Event.SetupAssistant.Tests;

using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

internal sealed class SetupPresentationModelContract
{
    private const string ProductNamespace = "ISLAMU.Event.SetupAssistant.Presentation";
    private readonly Assembly _assembly = Assembly.Load("Event.SetupAssistant");

    internal Type RequireProductType(string name) =>
        _assembly.GetType($"{ProductNamespace}.{name}", throwOnError: false)
        ?? throw new InvalidOperationException(
            $"missing-approved-owner:{ProductNamespace}.{name}");

    internal SetupPresentationSessionHandle CreateSession(
        params long?[] generationAllocations)
    {
        Type sessionType = RequireProductType("SetupPresentationSession");
        Type operationType = RequireProductType("ISetupPresentationOperation");
        Type outcomeType = RequireProductType("SetupPresentationOutcome");
        Type generationType = RequireProductType("SetupOperationGeneration");
        object messenger = CreateMessenger();
        object? allocator = generationAllocations.Length == 0
            ? null
            : new ControlledGenerationAllocator(generationAllocations)
                .CreateProxy(RequireProductType("ISetupOperationGenerationAllocator"), generationType);
        object? session = allocator is null
            ? Activator.CreateInstance(sessionType, messenger)
            : Activator.CreateInstance(sessionType, messenger, allocator);
        return new SetupPresentationSessionHandle(
            this,
            session ?? throw new InvalidOperationException("presentation-session-construction-failed"),
            messenger,
            operationType,
            outcomeType,
            generationType);
    }

    internal object CreateGeneration(long value)
    {
        Type type = RequireProductType("SetupOperationGeneration");
        return Activator.CreateInstance(type, value)
            ?? throw new InvalidOperationException("presentation-generation-construction-failed");
    }

    internal long GenerationValue(object generation) =>
        Property<long>(generation, "Value");

    internal object CreateOutcome(object coreResult, ReadOnlyMemory<byte> output)
    {
        Type type = RequireProductType("SetupPresentationOutcome");
        return Activator.CreateInstance(type, coreResult, output)
            ?? throw new InvalidOperationException("presentation-outcome-construction-failed");
    }

    internal Type SettledMessageType => RequireProductType("SetupOperationSettledMessage");

    internal Type InvalidationEventArgsType =>
        RequireProductType("SetupOperationInvalidatedEventArgs");

    internal Type WorkspaceType => RequireProductType("SetupPresentationWorkspace");

    internal IEnumerable<Type> PublicPresentationTypes() => _assembly.GetExportedTypes()
        .Where(type => type.Namespace?.StartsWith(ProductNamespace, StringComparison.Ordinal) == true);

    private static object CreateMessenger()
    {
        Type messengerType = Type.GetType(
            "CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger, CommunityToolkit.Mvvm",
            throwOnError: false)
            ?? throw new InvalidOperationException(
                "missing-approved-owner:CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger");
        return Activator.CreateInstance(messengerType)
            ?? throw new InvalidOperationException("presentation-messenger-construction-failed");
    }

    internal static object Property(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"missing-approved-owner:{instance.GetType().Name}.{name}");
        return property.GetValue(property.GetMethod?.IsStatic == true ? null : instance)
            ?? throw new InvalidOperationException(
                $"presentation-property-null:{instance.GetType().Name}.{name}");
    }

    internal static T Property<T>(object instance, string name) =>
        (T)Property(instance, name);

    internal static T StaticProperty<T>(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"missing-approved-owner:{type.Name}.{name}");
        return (T)(property.GetValue(null)
            ?? throw new InvalidOperationException($"presentation-property-null:{type.Name}.{name}"));
    }

    internal static object? InvokeAllowingNull(
        object instance,
        string name,
        params object?[] arguments)
    {
        MethodInfo method = RequiredMethod(instance.GetType(), name, arguments.Length);
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    internal static object Invoke(object instance, string name, params object?[] arguments) =>
        InvokeAllowingNull(instance, name, arguments)
        ?? throw new InvalidOperationException(
            $"presentation-method-null:{instance.GetType().Name}.{name}");

    internal static async Task InvokeAsync(
        object instance,
        string name,
        params object?[] arguments)
    {
        object result = Invoke(instance, name, arguments);
        if (result is not Task task)
            throw new InvalidOperationException(
                $"presentation-method-not-task:{instance.GetType().Name}.{name}");
        await task;
    }

    internal static MethodInfo RequiredMethod(Type type, string name, int? parameterCount = null) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SingleOrDefault(method => method.Name == name
                && (parameterCount is null || method.GetParameters().Length == parameterCount))
        ?? throw new InvalidOperationException($"missing-approved-owner:{type.Name}.{name}");
}

internal sealed class SetupPresentationSessionHandle : IDisposable
{
    private readonly SetupPresentationModelContract _contract;
    private readonly Type _generationType;
    private readonly Type _operationType;
    private readonly Type _outcomeType;
    private bool _disposed;

    internal SetupPresentationSessionHandle(
        SetupPresentationModelContract contract,
        object session,
        object messenger,
        Type operationType,
        Type outcomeType,
        Type generationType)
    {
        _contract = contract;
        Session = session;
        Messenger = messenger;
        _operationType = operationType;
        _outcomeType = outcomeType;
        _generationType = generationType;
    }

    internal object Session { get; }
    internal object Messenger { get; }
    internal Guid SessionId => SetupPresentationModelContract.Property<Guid>(Session, "SessionId");
    internal bool IsTerminated => SetupPresentationModelContract.Property<bool>(Session, "IsTerminated");

    internal PresentationWorkspaceHandle CreateWorkspace(
        string workspaceId,
        int invocationCount = 1)
    {
        Type workspaceIdType = _contract.RequireProductType("SetupWorkspaceId");
        MethodInfo tryCreate = SetupPresentationModelContract.RequiredMethod(
            workspaceIdType,
            "TryCreate",
            2);
        object?[] identifierArguments = [workspaceId, null];
        bool accepted = (bool)(tryCreate.Invoke(null, identifierArguments)
            ?? throw new InvalidOperationException("presentation-workspace-id-returned-null"));
        if (!accepted || identifierArguments[1] is null)
            throw new InvalidOperationException("presentation-workspace-id-rejected");

        var operation = new ControlledPresentationOperation(invocationCount);
        object proxy = operation.CreateProxy(_operationType, _outcomeType);
        object workspace = SetupPresentationModelContract.Invoke(
            Session,
            "CreateWorkspace",
            identifierArguments[1],
            proxy);
        return new PresentationWorkspaceHandle(
            workspace,
            operation,
            _outcomeType,
            _generationType);
    }

    internal int WorkspaceIdMaxLength()
    {
        Type type = _contract.RequireProductType("SetupWorkspaceId");
        return SetupPresentationModelContract.StaticProperty<int>(type, "MaxLength");
    }

    internal bool TryCreateWorkspaceId(string value, out object? identifier)
    {
        Type type = _contract.RequireProductType("SetupWorkspaceId");
        object?[] arguments = [value, null];
        bool result = (bool)(SetupPresentationModelContract.RequiredMethod(type, "TryCreate", 2)
            .Invoke(null, arguments)
            ?? throw new InvalidOperationException("presentation-workspace-id-returned-null"));
        identifier = arguments[1];
        return result;
    }

    internal void PublishSettlement(
        string workspaceId,
        Guid operationId,
        object generation,
        string status)
    {
        Type messageType = _contract.RequireProductType("SetupOperationSettledMessage");
        Type statusType = _contract.RequireProductType("SetupOperationStatus");
        Type workspaceIdType = _contract.RequireProductType("SetupWorkspaceId");
        object?[] identifierArguments = [workspaceId, null];
        bool accepted = (bool)(SetupPresentationModelContract.RequiredMethod(
            workspaceIdType,
            "TryCreate",
            2).Invoke(null, identifierArguments)
            ?? throw new InvalidOperationException("presentation-workspace-id-returned-null"));
        if (!accepted || identifierArguments[1] is null)
            throw new InvalidOperationException("presentation-workspace-id-rejected");
        object message = Activator.CreateInstance(
            messageType,
            SessionId,
            identifierArguments[1],
            operationId,
            generation,
            Enum.Parse(statusType, status))
            ?? throw new InvalidOperationException("presentation-message-construction-failed");
        Type extensions = Type.GetType(
            "CommunityToolkit.Mvvm.Messaging.IMessengerExtensions, CommunityToolkit.Mvvm",
            throwOnError: false)
            ?? throw new InvalidOperationException(
                "missing-approved-owner:CommunityToolkit.Mvvm.Messaging.IMessengerExtensions");
        MethodInfo send = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Send"
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 1
                && method.GetParameters().Length == 2);
        send.MakeGenericMethod(messageType).Invoke(null, [Messenger, message]);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Session is IDisposable session)
            session.Dispose();
        if (Messenger is IDisposable messenger)
            messenger.Dispose();
    }
}

internal sealed class PresentationWorkspaceHandle : IDisposable
{
    private readonly Type _generationType;
    private readonly Type _outcomeType;
    private bool _disposed;

    internal PresentationWorkspaceHandle(
        object workspace,
        ControlledPresentationOperation operation,
        Type outcomeType,
        Type generationType)
    {
        Workspace = workspace;
        Operation = operation;
        _outcomeType = outcomeType;
        _generationType = generationType;
    }

    internal object Workspace { get; }
    internal ControlledPresentationOperation Operation { get; }
    internal object Generation => SetupPresentationModelContract.Property(Workspace, "Generation");
    internal long GenerationValue => SetupPresentationModelContract.Property<long>(Generation, "Value");
    internal bool IsBusy => SetupPresentationModelContract.Property<bool>(Workspace, "IsBusy");
    internal bool IsTerminated => SetupPresentationModelContract.Property<bool>(Workspace, "IsTerminated");
    internal object? Result => Workspace.GetType().GetProperty("Result")?.GetValue(Workspace);
    internal ReadOnlyMemory<byte> Output =>
        SetupPresentationModelContract.Property<ReadOnlyMemory<byte>>(Workspace, "Output");
    internal int ReceivedCompletionCount =>
        SetupPresentationModelContract.Property<int>(Workspace, "ReceivedCompletionCount");
    internal string AccessibilityStatus =>
        SetupPresentationModelContract.Property(Workspace, "AccessibilityStatus").ToString()!;
    internal int MaxPublicInputLength =>
        SetupPresentationModelContract.Property<int>(Workspace, "MaxPublicInputLength");
    internal string PublicInput =>
        SetupPresentationModelContract.Property<string>(Workspace, "PublicInput");
    internal ICommand ExecuteCommand =>
        SetupPresentationModelContract.Property<ICommand>(Workspace, "ExecuteCommand");
    internal ICommand CancelCommand =>
        SetupPresentationModelContract.Property<ICommand>(Workspace, "CancelCommand");

    internal void Activate() =>
        SetupPresentationModelContract.InvokeAllowingNull(Workspace, "Activate");
    internal void Deactivate() =>
        SetupPresentationModelContract.InvokeAllowingNull(Workspace, "Deactivate");
    internal void Cancel() =>
        SetupPresentationModelContract.InvokeAllowingNull(Workspace, "Cancel");
    internal bool SetPublicInput(string value) =>
        (bool)SetupPresentationModelContract.Invoke(Workspace, "SetPublicInput", value);
    internal Task ExecuteAsync(Guid operationId) =>
        SetupPresentationModelContract.InvokeAsync(Workspace, "ExecuteAsync", operationId);

    internal object CreateOutcome(object coreResult, ReadOnlyMemory<byte> output) =>
        Activator.CreateInstance(_outcomeType, coreResult, output)
        ?? throw new InvalidOperationException("presentation-outcome-construction-failed");

    internal void Complete(int invocation, object coreResult, ReadOnlyMemory<byte> output) =>
        Operation.Invocations[invocation].Complete(_outcomeType, coreResult, output);

    internal IDisposable SubscribeInvalidated(Action<EventArgs> observer) =>
        Subscribe("Invalidated", observer);

    internal IDisposable SubscribeDiscarded(Action<EventArgs> observer) =>
        Subscribe("CompletionDiscarded", observer);

    internal IDisposable SubscribePropertyChanged(PropertyChangedEventHandler observer)
    {
        if (Workspace is not INotifyPropertyChanged source)
            throw new InvalidOperationException(
                "missing-approved-behavior:SetupPresentationWorkspace.INotifyPropertyChanged");
        source.PropertyChanged += observer;
        return new CallbackSubscription(() => source.PropertyChanged -= observer);
    }

    private CallbackSubscription Subscribe(string eventName, Action<EventArgs> observer)
    {
        EventInfo eventInfo = Workspace.GetType().GetEvent(eventName)
            ?? throw new InvalidOperationException(
                $"missing-approved-owner:SetupPresentationWorkspace.{eventName}");
        Type eventArguments = eventInfo.EventHandlerType?.GetGenericArguments().SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"presentation-event-contract-mismatch:SetupPresentationWorkspace.{eventName}");
        var bridge = new ReflectedEventObserver(observer);
        MethodInfo handlerMethod = typeof(ReflectedEventObserver)
            .GetMethod(nameof(ReflectedEventObserver.Handle))!
            .MakeGenericMethod(eventArguments);
        Delegate handler = Delegate.CreateDelegate(
            eventInfo.EventHandlerType!,
            bridge,
            handlerMethod);
        eventInfo.AddEventHandler(Workspace, handler);
        return new CallbackSubscription(() => eventInfo.RemoveEventHandler(Workspace, handler));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Workspace is IDisposable disposable)
            disposable.Dispose();
    }
}

internal sealed class ControlledGenerationAllocator
{
    private readonly Queue<long?> _allocations;

    internal ControlledGenerationAllocator(IEnumerable<long?> allocations) =>
        _allocations = new Queue<long?>(allocations);

    internal object CreateProxy(Type allocatorType, Type generationType)
    {
        object proxy = DispatchProxy.Create(allocatorType, typeof(ControlledGenerationAllocatorProxy));
        ((ControlledGenerationAllocatorProxy)proxy).Configure(_allocations, generationType);
        return proxy;
    }
}

internal class ControlledGenerationAllocatorProxy : DispatchProxy
{
    private Queue<long?>? _allocations;
    private Type? _generationType;

    internal void Configure(Queue<long?> allocations, Type generationType)
    {
        _allocations = allocations;
        _generationType = generationType;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "TryAllocate" || args is null
            || _allocations is null || _generationType is null)
            throw new InvalidOperationException("presentation-generation-allocator-contract-mismatch");
        long? allocation = _allocations.Count == 0 ? null : _allocations.Dequeue();
        int outputIndex = Array.FindIndex(
            targetMethod.GetParameters(),
            parameter => parameter.ParameterType.IsByRef);
        if (outputIndex < 0)
            throw new InvalidOperationException("presentation-generation-allocator-output-missing");
        args[outputIndex] = allocation is null
            ? Activator.CreateInstance(_generationType)
            : Activator.CreateInstance(_generationType, allocation.Value);
        return allocation is not null;
    }
}

internal sealed class ControlledPresentationOperation
{
    private readonly ControlledPresentationInvocation[] _invocations;
    private int _nextInvocation;

    internal ControlledPresentationOperation(int invocationCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(invocationCount, 1);
        _invocations = Enumerable.Range(0, invocationCount)
            .Select(_ => new ControlledPresentationInvocation())
            .ToArray();
    }

    internal IReadOnlyList<ControlledPresentationInvocation> Invocations => _invocations;

    internal object CreateProxy(Type operationType, Type outcomeType)
    {
        object proxy = DispatchProxy.Create(operationType, typeof(ControlledPresentationOperationProxy));
        ((ControlledPresentationOperationProxy)proxy).Configure(this, outcomeType);
        return proxy;
    }

    internal object Execute(Type outcomeType, CancellationToken cancellationToken)
    {
        int index = Interlocked.Increment(ref _nextInvocation) - 1;
        if ((uint)index >= (uint)_invocations.Length)
            throw new InvalidOperationException("presentation-operation-invocation-exhausted");
        return _invocations[index].Start(outcomeType, cancellationToken);
    }
}

internal class ControlledPresentationOperationProxy : DispatchProxy
{
    private ControlledPresentationOperation? _operation;
    private Type? _outcomeType;

    internal void Configure(ControlledPresentationOperation operation, Type outcomeType)
    {
        _operation = operation;
        _outcomeType = outcomeType;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "ExecuteAsync"
            || args?.LastOrDefault() is not CancellationToken cancellationToken
            || _operation is null
            || _outcomeType is null)
            throw new InvalidOperationException("presentation-operation-contract-mismatch");
        return _operation.Execute(_outcomeType, cancellationToken);
    }
}

internal sealed class ControlledPresentationInvocation
{
    private object? _completion;
    private MethodInfo? _trySetResult;

    internal TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal object Start(Type outcomeType, CancellationToken cancellationToken)
    {
        Type completionType = typeof(TaskCompletionSource<>).MakeGenericType(outcomeType);
        _completion = Activator.CreateInstance(
            completionType,
            TaskCreationOptions.RunContinuationsAsynchronously)
            ?? throw new InvalidOperationException("presentation-completion-construction-failed");
        _trySetResult = completionType.GetMethod("TrySetResult")
            ?? throw new InvalidOperationException("presentation-completion-contract-mismatch");
        cancellationToken.Register(static state =>
            ((TaskCompletionSource)state!).TrySetResult(), Cancelled);
        Started.TrySetResult();
        return completionType.GetProperty("Task")!.GetValue(_completion)!;
    }

    internal void Complete(Type outcomeType, object coreResult, ReadOnlyMemory<byte> output)
    {
        object outcome = Activator.CreateInstance(outcomeType, coreResult, output)
            ?? throw new InvalidOperationException("presentation-outcome-construction-failed");
        if (_completion is null || _trySetResult is null)
            throw new InvalidOperationException("presentation-operation-not-started");
        _trySetResult.Invoke(_completion, [outcome]);
    }
}

internal sealed class CallbackSubscription(Action unsubscribe) : IDisposable
{
    private Action? _unsubscribe = unsubscribe;
    public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
}

internal sealed class ReflectedEventObserver(Action<EventArgs> observer)
{
    public void Handle<TEventArgs>(object? sender, TEventArgs args)
        where TEventArgs : EventArgs =>
        observer(args);
}
