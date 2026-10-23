namespace Explore.AppHost;

/// <summary>Retains the actual AppHost execution task for its isolated test owner.</summary>
public sealed class TestingApplicationExecution
{
    private readonly TaskCompletionSource<Task> observation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task completion;
    private int observationCount;

    public TestingApplicationExecution() => completion = observation.Task.Unwrap();

    public Task Completion => Volatile.Read(ref observationCount) > 1
        ? Task.FromException(new InvalidOperationException("test_application_execution_already_observed"))
        : completion;

    public bool IsSettled => Volatile.Read(ref observationCount) == 1 && completion.IsCompleted;

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
