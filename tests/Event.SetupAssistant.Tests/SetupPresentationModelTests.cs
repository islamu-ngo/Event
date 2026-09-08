namespace Event.SetupAssistant.Tests;

using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using ISLAMU.Event.Setup.Core;

public sealed class SetupPresentationModelTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task SessionsOwnDistinctMessengersAndNeverCrossTalk()
    {
        var contract = new SetupPresentationModelContract();
        using SetupPresentationSessionHandle first = contract.CreateSession();
        using SetupPresentationSessionHandle second = contract.CreateSession();
        using PresentationWorkspaceHandle firstRecipient = first.CreateWorkspace("first-recipient");
        using PresentationWorkspaceHandle secondRecipient = second.CreateWorkspace("second-recipient");
        using PresentationWorkspaceHandle firstSender = first.CreateWorkspace("first-sender");
        firstRecipient.Activate();
        secondRecipient.Activate();

        Task execution = firstSender.ExecuteAsync(Guid.CreateVersion7());
        await firstSender.Operation.Invocations[0].Started.Task.WaitAsync(SignalTimeout);
        firstSender.Complete(0, CoreResult(), ReadOnlyMemory<byte>.Empty);
        await execution.WaitAsync(SignalTimeout);

        await Assert.That(ReferenceEquals(first.Messenger, second.Messenger)).IsFalse();
        await Assert.That(first.SessionId == second.SessionId).IsFalse();
        await Assert.That(firstRecipient.ReceivedCompletionCount).IsEqualTo(1);
        await Assert.That(secondRecipient.ReceivedCompletionCount).IsEqualTo(0);
    }

    [Test]
    public async Task RecipientsObserveOnlyActiveLifetimeAndDeduplicateTypedDelivery()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.SettledMessageType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle recipient = session.CreateWorkspace("recipient");
        object firstGeneration = contract.CreateGeneration(1);
        Guid operationId = Guid.CreateVersion7();

        session.PublishSettlement("sender", operationId, firstGeneration, "Succeeded");
        await Assert.That(recipient.ReceivedCompletionCount).IsEqualTo(0);

        recipient.Activate();
        session.PublishSettlement("sender", operationId, firstGeneration, "Succeeded");
        session.PublishSettlement("sender", operationId, firstGeneration, "Succeeded");
        await Assert.That(recipient.ReceivedCompletionCount).IsEqualTo(1);

        recipient.Deactivate();
        session.PublishSettlement(
            "sender",
            Guid.CreateVersion7(),
            contract.CreateGeneration(2),
            "Succeeded");
        await Assert.That(recipient.ReceivedCompletionCount).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationWinsOnlyAfterValueFreeInvalidationAndLateCompletionIsDiscarded()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.InvalidationEventArgsType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("cancel-wins");
        Task execution = workspace.ExecuteAsync(Guid.CreateVersion7());
        ControlledPresentationInvocation invocation = workspace.Operation.Invocations[0];
        await invocation.Started.Task.WaitAsync(SignalTimeout);
        var invalidated = new TaskCompletionSource<EventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = workspace.SubscribeInvalidated(args =>
        {
            if (invocation.Cancelled.Task.IsCompleted)
                invalidated.TrySetException(new InvalidOperationException(
                    "invalidation-after-cancellation"));
            else
                invalidated.TrySetResult(args);
        });

        workspace.Cancel();
        EventArgs signal = await invalidated.Task.WaitAsync(SignalTimeout);
        await invocation.Cancelled.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, CoreResult(), new byte[] { 3 });
        await execution.WaitAsync(SignalTimeout);

        await AssertValueFreeSignal(signal);
        await Assert.That(workspace.IsBusy).IsFalse();
        await Assert.That(workspace.Result).IsNull();
        await Assert.That(workspace.Output).IsEmpty();
    }

    [Test]
    public async Task SettlementWinsBeforeLaterCancellationAndRetainsExactOutcome()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.WorkspaceType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("settlement-wins");
        object result = CoreResult();
        byte[] buffer = [5, 8, 13, 21];
        ReadOnlyMemory<byte> region = new(buffer, 1, 2);
        Task execution = workspace.ExecuteAsync(Guid.CreateVersion7());
        ControlledPresentationInvocation invocation = workspace.Operation.Invocations[0];
        await invocation.Started.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, result, region);
        await execution.WaitAsync(SignalTimeout);
        var invalidated = new TaskCompletionSource<EventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = workspace.SubscribeInvalidated(
            args => invalidated.TrySetResult(args));

        workspace.Cancel();
        await invalidated.Task.WaitAsync(SignalTimeout);

        await Assert.That(invocation.Cancelled.Task.IsCompleted).IsFalse();
        await Assert.That(ReferenceEquals(workspace.Result, result)).IsTrue();
        await AssertExactMemoryIdentity(workspace.Output, region);
    }

    [Test]
    public async Task ReplacementInvalidatesBeforeCancellingRetainedOperation()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.InvalidationEventArgsType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("replacement", 2);
        Task stale = workspace.ExecuteAsync(Guid.CreateVersion7());
        ControlledPresentationInvocation first = workspace.Operation.Invocations[0];
        await first.Started.Task.WaitAsync(SignalTimeout);
        var invalidated = OrderedInvalidationSignal(workspace, first);

        Task current = workspace.ExecuteAsync(Guid.CreateVersion7());
        await invalidated.Signal.Task.WaitAsync(SignalTimeout);
        await first.Cancelled.Task.WaitAsync(SignalTimeout);
        await workspace.Operation.Invocations[1].Started.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, CoreResult(), new byte[] { 1 });
        await stale.WaitAsync(SignalTimeout);
        workspace.Complete(1, CoreResult(), new byte[] { 2 });
        await current.WaitAsync(SignalTimeout);

        await Assert.That(workspace.GenerationValue).IsEqualTo(2);
        invalidated.Subscription.Dispose();
    }

    [Test]
    public async Task DeactivationInvalidatesBeforeCancellationAndUnregistersRecipient()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.InvalidationEventArgsType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("deactivation");
        workspace.Activate();
        Task execution = workspace.ExecuteAsync(Guid.CreateVersion7());
        ControlledPresentationInvocation invocation = workspace.Operation.Invocations[0];
        await invocation.Started.Task.WaitAsync(SignalTimeout);
        var invalidated = OrderedInvalidationSignal(workspace, invocation);

        workspace.Deactivate();
        await invalidated.Signal.Task.WaitAsync(SignalTimeout);
        await invocation.Cancelled.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, CoreResult(), ReadOnlyMemory<byte>.Empty);
        await execution.WaitAsync(SignalTimeout);
        session.PublishSettlement(
            "sender",
            Guid.CreateVersion7(),
            contract.CreateGeneration(9),
            "Succeeded");

        await Assert.That(workspace.ReceivedCompletionCount).IsEqualTo(0);
        invalidated.Subscription.Dispose();
    }

    [Test]
    public async Task DisposalInvalidatesBeforeCancellationAndIsIdempotent()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.InvalidationEventArgsType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("disposal");
        workspace.Activate();
        Task execution = workspace.ExecuteAsync(Guid.CreateVersion7());
        ControlledPresentationInvocation invocation = workspace.Operation.Invocations[0];
        await invocation.Started.Task.WaitAsync(SignalTimeout);
        var invalidated = OrderedInvalidationSignal(workspace, invocation);

        workspace.Dispose();
        await invalidated.Signal.Task.WaitAsync(SignalTimeout);
        await invocation.Cancelled.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, CoreResult(), new byte[] { 5 });
        await execution.WaitAsync(SignalTimeout);
        workspace.Dispose();

        await Assert.That(workspace.Result).IsNull();
        await Assert.That(workspace.ReceivedCompletionCount).IsEqualTo(0);
        invalidated.Subscription.Dispose();
    }

    [Test]
    public async Task DuplicateSettlementsRaceAtBarrierAndCommitPublishRefreshDiscardExactlyOnce()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.WorkspaceType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("settlement-race", 2);
        using PresentationWorkspaceHandle recipient = session.CreateWorkspace("observer");
        recipient.Activate();
        Guid operationId = Guid.CreateVersion7();
        Task staleExecution = workspace.ExecuteAsync(operationId);
        await workspace.Operation.Invocations[0].Started.Task.WaitAsync(SignalTimeout);
        Task currentExecution = workspace.ExecuteAsync(operationId);
        await workspace.Operation.Invocations[0].Cancelled.Task.WaitAsync(SignalTimeout);
        await workspace.Operation.Invocations[1].Started.Task.WaitAsync(SignalTimeout);
        object staleResult = CoreResult();
        object currentResult = CoreResult();
        int resultRefreshes = 0;
        int commandRefreshes = 0;
        int discards = 0;
        var discarded = new TaskCompletionSource<EventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable propertySubscription = workspace.SubscribePropertyChanged((_, args) =>
        {
            if (args.PropertyName == "Result")
                Interlocked.Increment(ref resultRefreshes);
        });
        EventHandler commandHandler = (_, _) => Interlocked.Increment(ref commandRefreshes);
        workspace.ExecuteCommand.CanExecuteChanged += commandHandler;
        using IDisposable discardSubscription = workspace.SubscribeDiscarded(args =>
        {
            Interlocked.Increment(ref discards);
            discarded.TrySetResult(args);
        });
        using var barrier = new Barrier(2);

        Task[] contenders =
        [
            Task.Run(() =>
            {
                if (!barrier.SignalAndWait(SignalTimeout))
                    throw new TimeoutException("settlement-barrier-timeout");
                workspace.Complete(0, staleResult, new byte[] { 3 });
            }),
            Task.Run(() =>
            {
                if (!barrier.SignalAndWait(SignalTimeout))
                    throw new TimeoutException("settlement-barrier-timeout");
                workspace.Complete(1, currentResult, new byte[] { 7 });
            })
        ];
        await Task.WhenAll(contenders).WaitAsync(SignalTimeout);
        await Task.WhenAll(staleExecution, currentExecution).WaitAsync(SignalTimeout);
        EventArgs discard = await discarded.Task.WaitAsync(SignalTimeout);

        await Assert.That(ReferenceEquals(workspace.Result, currentResult)).IsTrue();
        await Assert.That(recipient.ReceivedCompletionCount).IsEqualTo(1);
        await Assert.That(resultRefreshes).IsEqualTo(1);
        await Assert.That(commandRefreshes).IsEqualTo(1);
        await Assert.That(discards).IsEqualTo(1);
        await AssertValueFreeSignal(discard);
        workspace.ExecuteCommand.CanExecuteChanged -= commandHandler;
    }

    [Test]
    public async Task BoundedAllocatorReachesFinalGenerationThenTerminatesRetainedCompletion()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.RequireProductType("ISetupOperationGenerationAllocator");
        using SetupPresentationSessionHandle session = contract.CreateSession(
            long.MaxValue - 1,
            long.MaxValue,
            null);
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("exhaustion", 3);
        Task penultimate = workspace.ExecuteAsync(Guid.CreateVersion7());
        await workspace.Operation.Invocations[0].Started.Task.WaitAsync(SignalTimeout);
        Task retainedFinal = workspace.ExecuteAsync(Guid.CreateVersion7());
        await workspace.Operation.Invocations[0].Cancelled.Task.WaitAsync(SignalTimeout);
        await workspace.Operation.Invocations[1].Started.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, CoreResult(), new byte[] { 1 });
        await penultimate.WaitAsync(SignalTimeout);
        Guid sessionId = session.SessionId;

        Task terminated = workspace.ExecuteAsync(Guid.CreateVersion7());
        await terminated.WaitAsync(SignalTimeout);
        await workspace.Operation.Invocations[1].Cancelled.Task.WaitAsync(SignalTimeout);
        workspace.Complete(1, CoreResult(), new byte[] { 9 });
        await retainedFinal.WaitAsync(SignalTimeout);

        await Assert.That(workspace.GenerationValue).IsEqualTo(long.MaxValue);
        await Assert.That(workspace.IsTerminated).IsTrue();
        await Assert.That(session.IsTerminated).IsTrue();
        await Assert.That(session.SessionId).IsEqualTo(sessionId);
        await Assert.That(workspace.Operation.Invocations[2].Started.Task.IsCompleted).IsFalse();
        await Assert.That(workspace.Result).IsNull();
        await Assert.That(workspace.Output).IsEmpty();
    }

    [Test]
    public async Task AllocatorDuplicateOrDecreaseTerminatesInsteadOfWrappingOrReseeding()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.RequireProductType("ISetupOperationGenerationAllocator");
        await AssertInvalidAllocationTerminates(contract, 41);
        await AssertInvalidAllocationTerminates(contract, 40);
    }

    [Test]
    public async Task CompletionMessagesAreImmutableTypedAndValueFree()
    {
        var contract = new SetupPresentationModelContract();
        Type message = contract.SettledMessageType;
        Type generation = contract.RequireProductType("SetupOperationGeneration");
        Type workspaceId = contract.RequireProductType("SetupWorkspaceId");
        PropertyInfo[] properties = message.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(message.IsSealed).IsTrue();
        await Assert.That(properties.All(property => property.SetMethod?.IsPublic != true)).IsTrue();
        await Assert.That(message.GetFields(BindingFlags.Public | BindingFlags.Instance)).IsEmpty();
        await Assert.That(properties.Select(property => property.Name).Order(StringComparer.Ordinal))
            .IsEquivalentTo(["Generation", "OperationId", "SessionId", "Status", "WorkspaceId"]);
        await Assert.That(properties.Single(property => property.Name == "Generation").PropertyType)
            .IsEqualTo(generation);
        await Assert.That(properties.Single(property => property.Name == "WorkspaceId").PropertyType)
            .IsEqualTo(workspaceId);
        await Assert.That(properties.Any(property =>
            ForbiddenSensitiveName(property.Name) || MutableSecretCarrier(property.PropertyType))).IsFalse();
    }

    [Test]
    public async Task ToolkitGeneratedPropertyRaisesBehavioralNotification()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.WorkspaceType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("notification");
        var changed = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = workspace.SubscribePropertyChanged((_, args) =>
            changed.TrySetResult(args.PropertyName));

        bool accepted = workspace.SetPublicInput("public-title");
        string? property = await changed.Task.WaitAsync(SignalTimeout);

        await Assert.That(accepted).IsTrue();
        await Assert.That(property).IsEqualTo("PublicInput");
        await Assert.That(workspace.PublicInput).IsEqualTo("public-title");
    }

    [Test]
    public async Task AsyncCommandsRefreshEligibilityExecuteAndCancelThroughPublicBehavior()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.WorkspaceType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("commands");
        Guid operationId = Guid.CreateVersion7();
        object execute = workspace.ExecuteCommand;

        await Assert.That(execute.GetType().GetInterfaces().Any(type =>
            type.FullName == "CommunityToolkit.Mvvm.Input.IAsyncRelayCommand")).IsTrue();
        await Assert.That(workspace.ExecuteCommand.CanExecute(operationId)).IsTrue();
        await Assert.That(workspace.CancelCommand.CanExecute(null)).IsFalse();

        workspace.ExecuteCommand.Execute(operationId);
        await workspace.Operation.Invocations[0].Started.Task.WaitAsync(SignalTimeout);
        await Assert.That(workspace.IsBusy).IsTrue();
        await Assert.That(workspace.ExecuteCommand.CanExecute(operationId)).IsFalse();
        await Assert.That(workspace.CancelCommand.CanExecute(null)).IsTrue();

        workspace.CancelCommand.Execute(null);
        await workspace.Operation.Invocations[0].Cancelled.Task.WaitAsync(SignalTimeout);
        Task executionTask = (Task)SetupPresentationModelContract.Property(execute, "ExecutionTask");
        await executionTask.WaitAsync(SignalTimeout);
        await Assert.That(workspace.IsBusy).IsFalse();
        await Assert.That(workspace.ExecuteCommand.CanExecute(operationId)).IsTrue();
        await Assert.That(workspace.CancelCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task CoreResultAndReadOnlyMemoryProjectWithExactIdentityAndBackingRegion()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.RequireProductType("SetupPresentationOutcome");
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("core-projection");
        object coreResult = CoreResult();
        byte[] backing = [2, 3, 5, 7, 11];
        ReadOnlyMemory<byte> bytes = new(backing, 1, 3);

        Task execution = workspace.ExecuteAsync(Guid.CreateVersion7());
        await workspace.Operation.Invocations[0].Started.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, coreResult, bytes);
        await execution.WaitAsync(SignalTimeout);

        await Assert.That(ReferenceEquals(workspace.Result, coreResult)).IsTrue();
        await AssertExactMemoryIdentity(workspace.Output, bytes);
    }

    [Test]
    public async Task GenerationTypeFlowsEndToEndWithoutPrimitiveIdentity()
    {
        var contract = new SetupPresentationModelContract();
        Type generation = contract.RequireProductType("SetupOperationGeneration");
        Type workspace = contract.WorkspaceType;
        Type message = contract.SettledMessageType;
        Type allocator = contract.RequireProductType("ISetupOperationGenerationAllocator");

        await Assert.That(workspace.GetProperty("Generation")?.PropertyType).IsEqualTo(generation);
        await Assert.That(message.GetProperty("Generation")?.PropertyType).IsEqualTo(generation);
        MethodInfo allocate = allocator.GetMethod("TryAllocate")
            ?? throw new InvalidOperationException(
                "missing-approved-owner:ISetupOperationGenerationAllocator.TryAllocate");
        await Assert.That(allocate.GetParameters().Any(parameter =>
            parameter.ParameterType == generation
            || parameter.ParameterType == generation.MakeByRefType())).IsTrue();
        await Assert.That(workspace.GetProperties().Any(property =>
            property.Name == "Generation" && property.PropertyType == typeof(long))).IsFalse();
    }

    [Test]
    public async Task WorkspaceIdentityIsNamedBoundedAndValidated()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.RequireProductType("SetupWorkspaceId");
        using SetupPresentationSessionHandle session = contract.CreateSession();
        int maximum = session.WorkspaceIdMaxLength();

        await Assert.That(maximum).IsGreaterThan(0);
        await Assert.That(maximum).IsLessThanOrEqualTo(256);
        await Assert.That(session.TryCreateWorkspaceId("bounded-workspace", out object? accepted)).IsTrue();
        await Assert.That(accepted).IsNotNull();
        await Assert.That(session.TryCreateWorkspaceId(string.Empty, out _)).IsFalse();
        await Assert.That(session.TryCreateWorkspaceId("   ", out _)).IsFalse();
        await Assert.That(session.TryCreateWorkspaceId(new string('w', maximum + 1), out _)).IsFalse();
    }

    [Test]
    public async Task DynamicCanaryAppearsOnlyAsExplicitlyAcceptedPublicEditValue()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.WorkspaceType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("canary-surface");
        string canary = $"{Guid.CreateVersion7():N}-{Guid.NewGuid():N}";
        await Assert.That(workspace.SetPublicInput(canary)).IsTrue();
        await Assert.That(workspace.PublicInput).IsEqualTo(canary);

        var observed = new List<string>();
        foreach (object owner in new[] { session.Session, workspace.Workspace })
        {
            observed.Add(owner.ToString() ?? string.Empty);
            foreach (PropertyInfo property in owner.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (owner == workspace.Workspace && property.Name == "PublicInput")
                    continue;
                if (property.GetIndexParameters().Length != 0 || property.GetMethod is null)
                    continue;
                try
                {
                    object? value = property.GetValue(owner);
                    observed.Add(value?.ToString() ?? string.Empty);
                }
                catch (TargetInvocationException exception)
                {
                    observed.Add(exception.InnerException?.Message ?? exception.Message);
                }
            }
        }

        string oversized = string.Concat(
            Enumerable.Repeat(canary, workspace.MaxPublicInputLength + 1));
        Exception? error = null;
        try
        {
            _ = workspace.SetPublicInput(oversized);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        observed.Add(error?.Message ?? string.Empty);
        observed.Add(error?.ToString() ?? string.Empty);

        await Assert.That(observed.Any(value => value.Contains(canary, StringComparison.Ordinal))).IsFalse();
        await Assert.That(contract.PublicPresentationTypes().SelectMany(type =>
            type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)).Any(member => ForbiddenSensitiveName(member.Name))).IsFalse();
    }

    [Test]
    public async Task SharedPresentationReportsOnlyTargetAgnosticAccessibilityStatus()
    {
        var contract = new SetupPresentationModelContract();
        _ = contract.WorkspaceType;
        using SetupPresentationSessionHandle session = contract.CreateSession();
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace("accessibility");

        await Assert.That(workspace.AccessibilityStatus).IsEqualTo("NotEvaluated");
    }

    private static async Task AssertInvalidAllocationTerminates(
        SetupPresentationModelContract contract,
        long invalid)
    {
        using SetupPresentationSessionHandle session = contract.CreateSession(41, invalid);
        using PresentationWorkspaceHandle workspace = session.CreateWorkspace($"invalid-{invalid}", 2);
        Task first = workspace.ExecuteAsync(Guid.CreateVersion7());
        await workspace.Operation.Invocations[0].Started.Task.WaitAsync(SignalTimeout);
        workspace.Complete(0, CoreResult(), ReadOnlyMemory<byte>.Empty);
        await first.WaitAsync(SignalTimeout);

        await workspace.ExecuteAsync(Guid.CreateVersion7()).WaitAsync(SignalTimeout);

        await Assert.That(workspace.GenerationValue).IsEqualTo(41);
        await Assert.That(workspace.IsTerminated).IsTrue();
        await Assert.That(workspace.Operation.Invocations[1].Started.Task.IsCompleted).IsFalse();
    }

    private static (TaskCompletionSource<EventArgs> Signal, IDisposable Subscription)
        OrderedInvalidationSignal(
            PresentationWorkspaceHandle workspace,
            ControlledPresentationInvocation invocation)
    {
        var signal = new TaskCompletionSource<EventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = workspace.SubscribeInvalidated(args =>
        {
            if (invocation.Cancelled.Task.IsCompleted)
                signal.TrySetException(new InvalidOperationException(
                    "invalidation-after-cancellation"));
            else
                signal.TrySetResult(args);
        });
        return (signal, subscription);
    }

    private static async Task AssertValueFreeSignal(EventArgs signal)
    {
        await Assert.That(signal.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.PropertyType == typeof(string)
                || property.PropertyType == typeof(byte[])
                || property.PropertyType == typeof(ReadOnlyMemory<byte>))).IsFalse();
        await Assert.That(signal.ToString()).DoesNotContain("=");
    }

    private static async Task AssertExactMemoryIdentity(
        ReadOnlyMemory<byte> actual,
        ReadOnlyMemory<byte> expected)
    {
        bool actualArray = MemoryMarshal.TryGetArray(actual, out ArraySegment<byte> actualSegment);
        bool expectedArray = MemoryMarshal.TryGetArray(expected, out ArraySegment<byte> expectedSegment);
        await Assert.That(actualArray).IsTrue();
        await Assert.That(expectedArray).IsTrue();
        await Assert.That(ReferenceEquals(actualSegment.Array, expectedSegment.Array)).IsTrue();
        await Assert.That(actualSegment.Offset).IsEqualTo(expectedSegment.Offset);
        await Assert.That(actualSegment.Count).IsEqualTo(expectedSegment.Count);
    }

    private static SetupReadinessResult CoreResult() => SetupReadiness.Evaluate(
        new SetupReadinessInput([], [], []));

    private static bool ForbiddenSensitiveName(string name) =>
        name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase);

    private static bool MutableSecretCarrier(Type type)
    {
        if (type == typeof(char[]) || type == typeof(byte[])
            || type == typeof(Memory<char>) || type == typeof(Memory<byte>))
            return true;
        if (!type.IsGenericType)
            return false;
        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>)
            || definition == typeof(IList<>)
            || definition == typeof(ICollection<>);
    }
}
