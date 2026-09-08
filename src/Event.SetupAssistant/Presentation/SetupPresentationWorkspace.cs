namespace ISLAMU.Event.SetupAssistant.Presentation;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

public sealed partial class SetupPresentationWorkspace :
    ObservableRecipient,
    IRecipient<SetupOperationSettledMessage>,
    IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<SettlementIdentity> _received = [];
    private readonly ISetupPresentationOperation _operation;
    private readonly SetupPresentationSession _session;
    private readonly IMessenger _sessionMessenger;
    private CancellationTokenSource? _activeCancellation;
    private SetupOperationGeneration _activeGeneration;
    private Guid _activeOperationId;
    private bool _disposed;
    private bool _hasActiveOperation;

    [ObservableProperty]
    private SetupAccessibilityStatus _accessibilityStatus = SetupAccessibilityStatus.NotEvaluated;

    [ObservableProperty]
    private SetupOperationGeneration _generation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isTerminated;

    [ObservableProperty]
    private ReadOnlyMemory<byte> _output;

    [ObservableProperty]
    private string _publicInput = string.Empty;

    [ObservableProperty]
    private int _receivedCompletionCount;

    [ObservableProperty]
    private object? _result;

    internal SetupPresentationWorkspace(
        SetupPresentationSession session,
        IMessenger messenger,
        SetupWorkspaceId workspaceId,
        ISetupPresentationOperation operation)
        : base(messenger)
    {
        _session = session;
        _sessionMessenger = messenger;
        WorkspaceId = workspaceId;
        _operation = operation;
    }

    public event EventHandler<SetupCompletionDiscardedEventArgs>? CompletionDiscarded;

    public event EventHandler<SetupOperationInvalidatedEventArgs>? Invalidated;

    public int MaxPublicInputLength => 4096;

    public SetupWorkspaceId WorkspaceId { get; }

    public void Activate()
    {
        lock (_gate)
        {
            if (_disposed || IsTerminated)
                return;
            IsActive = true;
        }
    }

    public void Deactivate()
    {
        Invalidate(SetupOperationInvalidationReason.Deactivated, preserveResult: false);
        lock (_gate)
            IsActive = false;
    }

    public bool SetPublicInput(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaxPublicInputLength)
            return false;

        lock (_gate)
        {
            if (_disposed || IsTerminated)
                return false;
            PublicInput = value;
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteOperation))]
    public async Task ExecuteAsync(Guid operationId)
    {
        if (operationId == Guid.Empty || !_session.TryAllocate(out SetupOperationGeneration next))
            return;

        CancellationTokenSource? previous;
        SetupOperationGeneration previousGeneration;
        CancellationTokenSource current;
        lock (_gate)
        {
            if (_disposed || IsTerminated)
                return;

            previous = _activeCancellation;
            previousGeneration = _activeGeneration;
            current = new CancellationTokenSource();
            _activeCancellation = current;
            _activeGeneration = next;
            _activeOperationId = operationId;
            _hasActiveOperation = true;
            Generation = next;
            IsBusy = true;
        }

        if (previous is not null)
        {
            RaiseInvalidated(previousGeneration, SetupOperationInvalidationReason.Replaced);
            previous.Cancel();
            previous.Dispose();
        }

        Task<SetupPresentationOutcome> pending;
        try
        {
            pending = _operation.ExecuteAsync(current.Token);
        }
        catch
        {
            InvalidateCurrent(operationId, next, SetupOperationInvalidationReason.Cancelled);
            throw;
        }

        try
        {
            SetupPresentationOutcome outcome = await pending.WaitAsync(current.Token);
            Commit(operationId, next, outcome);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            _ = ObserveDiscardedAsync(pending, operationId, next);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel() =>
        Invalidate(SetupOperationInvalidationReason.Cancelled, preserveResult: true);

    public void Receive(SetupOperationSettledMessage message)
    {
        if (message.SessionId != _session.SessionId || message.WorkspaceId == WorkspaceId)
            return;

        lock (_gate)
        {
            if (_disposed || IsTerminated)
                return;
            var identity = new SettlementIdentity(message.OperationId, message.Generation);
            if (_received.Add(identity))
                ReceivedCompletionCount++;
        }
    }

    internal void TerminateFromSession()
    {
        CancellationTokenSource? cancellation;
        SetupOperationGeneration invalidatedGeneration;
        bool hadActive;
        lock (_gate)
        {
            if (IsTerminated)
                return;

            IsTerminated = true;
            hadActive = _hasActiveOperation;
            invalidatedGeneration = _activeGeneration;
            cancellation = _activeCancellation;
            _hasActiveOperation = false;
            _activeCancellation = null;
            IsBusy = false;
        }

        if (hadActive)
            RaiseInvalidated(invalidatedGeneration, SetupOperationInvalidationReason.SessionTerminated);
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsActive = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Invalidate(SetupOperationInvalidationReason.Disposed, preserveResult: false);
        IsActive = false;
        _session.Release(this);
    }

    public override string ToString() => nameof(SetupPresentationWorkspace);

    private bool CanExecuteOperation(Guid operationId) =>
        operationId != Guid.Empty && !_disposed && !IsTerminated && !IsBusy;

    private bool CanCancel() => !_disposed && !IsTerminated && IsBusy;

    private void Commit(
        Guid operationId,
        SetupOperationGeneration generation,
        SetupPresentationOutcome outcome)
    {
        bool accepted;
        lock (_gate)
        {
            accepted = !_disposed
                && !IsTerminated
                && _hasActiveOperation
                && _activeOperationId == operationId
                && _activeGeneration == generation
                && _activeCancellation is { IsCancellationRequested: false };
            if (accepted)
            {
                _hasActiveOperation = false;
                _activeCancellation?.Dispose();
                _activeCancellation = null;
                Result = outcome.CoreResult;
                Output = outcome.Output;
                IsBusy = false;
            }
        }

        if (!accepted)
        {
            RaiseDiscarded(operationId, generation);
            return;
        }

        _sessionMessenger.Send(new SetupOperationSettledMessage(
            _session.SessionId,
            WorkspaceId,
            operationId,
            generation,
            SetupOperationStatus.Succeeded));
    }

    private void Invalidate(
        SetupOperationInvalidationReason reason,
        bool preserveResult)
    {
        CancellationTokenSource? cancellation;
        SetupOperationGeneration invalidatedGeneration;
        bool hadActive;
        lock (_gate)
        {
            hadActive = _hasActiveOperation;
            invalidatedGeneration = _activeGeneration;
            cancellation = _activeCancellation;
            _hasActiveOperation = false;
            _activeCancellation = null;
            IsBusy = false;
            if (!preserveResult)
            {
                Result = null;
                Output = ReadOnlyMemory<byte>.Empty;
            }
        }

        RaiseInvalidated(invalidatedGeneration, reason);
        if (hadActive)
            cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void InvalidateCurrent(
        Guid operationId,
        SetupOperationGeneration generation,
        SetupOperationInvalidationReason reason)
    {
        lock (_gate)
        {
            if (!_hasActiveOperation
                || _activeOperationId != operationId
                || _activeGeneration != generation)
                return;
        }

        Invalidate(reason, preserveResult: false);
    }

    private async Task ObserveDiscardedAsync(
        Task<SetupPresentationOutcome> pending,
        Guid operationId,
        SetupOperationGeneration generation)
    {
        try
        {
            _ = await pending;
        }
        catch
        {
        }

        RaiseDiscarded(operationId, generation);
    }

    private void RaiseInvalidated(
        SetupOperationGeneration generation,
        SetupOperationInvalidationReason reason) =>
        Invalidated?.Invoke(this, new SetupOperationInvalidatedEventArgs(generation, reason));

    private void RaiseDiscarded(
        Guid operationId,
        SetupOperationGeneration generation) =>
        CompletionDiscarded?.Invoke(
            this,
            new SetupCompletionDiscardedEventArgs(operationId, generation));

    private readonly record struct SettlementIdentity(
        Guid OperationId,
        SetupOperationGeneration Generation);
}
