using Microsoft.Extensions.Hosting;

namespace Explore.AppHost;

/// <summary>Coordinates isolated test startup with the observed AppHost lifetime.</summary>
public sealed class TestingApplicationExecution : IHostedLifecycleService, IDisposable
{
    private readonly TaskCompletionSource<Task> observation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource startPermission = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource entryPointReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposalPermission = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource hostLifetime = new();
    private readonly Task completion;
    private int observationCount;

    public TestingApplicationExecution() => completion = observation.Task.Unwrap();

    public Task Completion => Volatile.Read(ref observationCount) > 1
        ? Task.FromException(new InvalidOperationException("test_application_execution_already_observed"))
        : completion;

    public bool IsSettled => Volatile.Read(ref observationCount) == 1 && completion.IsCompleted;

    public bool IsDisposalSettled => disposal.Task.IsCompleted;

    public CancellationToken HostCancellationToken => hostLifetime.Token;

    public Task StartingAsync(CancellationToken cancellationToken) => startPermission.Task.WaitAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WaitForEntryPointAsync(CancellationToken cancellationToken) => entryPointReady.Task.WaitAsync(cancellationToken);

    public void ConfirmEntryPoint() => entryPointReady.TrySetResult();

    public void FailEntryPoint(Exception failure) => entryPointReady.TrySetException(failure);

    public bool PermitStart() => startPermission.TrySetResult();

    public void CancelUnstarted() => startPermission.TrySetCanceled(CancellationToken.None);

    public void CancelHost() => hostLifetime.Cancel();

    public void PermitDisposal() => disposalPermission.TrySetResult();

    public Task WaitForDisposalPermissionAsync() => disposalPermission.Task;

    public void CompleteDisposal() => disposal.TrySetResult();

    public void FailDisposal(Exception failure) => disposal.TrySetException(failure);

    public Task WaitForDisposalAsync(TimeSpan timeout) => disposal.Task.WaitAsync(timeout, CancellationToken.None);

    public void Dispose() => hostLifetime.Dispose();

    public async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        Task observed = Completion;
        try { await observed.WaitAsync(timeout, CancellationToken.None); }
        catch (OperationCanceledException) when (observed.IsCanceled)
        {
            // A canceled execution has settled without a cleanup fault.
        }
    }

    public void Observe(Task execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (Interlocked.Increment(ref observationCount) != 1)
            throw new InvalidOperationException("test_application_execution_already_observed");
        observation.SetResult(execution);
    }
}
