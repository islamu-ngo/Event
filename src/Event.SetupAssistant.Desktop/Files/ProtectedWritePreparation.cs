namespace ISLAMU.Event.SetupAssistant.Desktop.Files;

public sealed class ProtectedWritePreparation : IDisposable
{
    private readonly Func<CancellationToken, Task<ProtectedWriteResult>> _commit;
    private readonly Action _cleanup;
    private int _completed;

    internal ProtectedWritePreparation(
        Func<CancellationToken, Task<ProtectedWriteResult>> commit,
        Action cleanup)
    {
        _commit = commit;
        _cleanup = cleanup;
    }

    internal static ProtectedWritePreparation FromResult(ProtectedWriteResult result) =>
        new(_ => Task.FromResult(result), static () => { });

    public async Task<ProtectedWriteResult> CommitAsync(
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return ProtectedWriteResult.Rejected(ProtectedWriteFailureCode.InvalidRequest);

        try
        {
            return await _commit(cancellationToken);
        }
        finally
        {
            _cleanup();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _cleanup();
    }
}
