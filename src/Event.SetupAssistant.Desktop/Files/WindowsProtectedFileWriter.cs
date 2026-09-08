namespace ISLAMU.Event.SetupAssistant.Desktop.Files;

public sealed class WindowsProtectedFileWriter : IProtectedFileWriter
{
    public bool IsAvailable => false;

    public Task<ProtectedWritePreparation> PrepareAsync(
        ProtectedWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            ProtectedWritePreparation.FromResult(ProtectedWriteResult.Unsupported()));
    }
}
