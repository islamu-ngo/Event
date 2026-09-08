namespace Explore.Application.Contracts.Secrets;

using Explore.Application.Contracts.SetupLive;

public interface ISetupSecretBindingWriter
{
    Task<SetupSecretBindingWriteOutcome> WriteAsync(
        SetupSecretBindingWriteRequest request,
        CancellationToken cancellationToken);
}
