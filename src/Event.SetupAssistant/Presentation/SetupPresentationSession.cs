namespace ISLAMU.Event.SetupAssistant.Presentation;

using CommunityToolkit.Mvvm.Messaging;

public sealed class SetupPresentationSession : IDisposable
{
    private readonly ISetupOperationGenerationAllocator _allocator;
    private readonly object _gate = new();
    private readonly IMessenger _messenger;
    private readonly List<SetupPresentationWorkspace> _workspaces = [];
    private SetupOperationGeneration _lastGeneration;
    private bool _disposed;
    private bool _isTerminated;

    public SetupPresentationSession(IMessenger messenger)
        : this(messenger, new SetupOperationGenerationAllocator())
    {
    }

    public SetupPresentationSession(
        IMessenger messenger,
        ISetupOperationGenerationAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        ArgumentNullException.ThrowIfNull(allocator);
        _messenger = messenger;
        _allocator = allocator;
        SessionId = Guid.CreateVersion7();
    }

    public bool IsTerminated
    {
        get
        {
            lock (_gate)
                return _isTerminated;
        }
    }

    public Guid SessionId { get; }

    public SetupPresentationWorkspace CreateWorkspace(
        SetupWorkspaceId workspaceId,
        ISetupPresentationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isTerminated)
                throw new InvalidOperationException("presentation-session-terminated");

            var workspace = new SetupPresentationWorkspace(
                this,
                _messenger,
                workspaceId,
                operation);
            _workspaces.Add(workspace);
            return workspace;
        }
    }

    internal bool TryAllocate(out SetupOperationGeneration generation)
    {
        SetupPresentationWorkspace[] terminate = [];
        lock (_gate)
        {
            if (_disposed || _isTerminated)
            {
                generation = default;
                return false;
            }

            if (_allocator.TryAllocate(out generation)
                && generation.Value > _lastGeneration.Value)
            {
                _lastGeneration = generation;
                return true;
            }

            generation = default;
            _isTerminated = true;
            terminate = [.. _workspaces];
        }

        foreach (SetupPresentationWorkspace workspace in terminate)
            workspace.TerminateFromSession();
        return false;
    }

    internal void Release(SetupPresentationWorkspace workspace)
    {
        lock (_gate)
            _workspaces.Remove(workspace);
    }

    public void Dispose()
    {
        SetupPresentationWorkspace[] terminate;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _isTerminated = true;
            terminate = [.. _workspaces];
            _workspaces.Clear();
        }

        foreach (SetupPresentationWorkspace workspace in terminate)
            workspace.TerminateFromSession();
    }
}
