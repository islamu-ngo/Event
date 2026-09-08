namespace Explore.Application.Contracts.Infrastructure;

using Explore.Application.Contracts.SetupLive;

public interface ISetupSecretBindingReadinessReader
{
    Task<SetupSecretBindingWriteOutcome> GetReadinessAsync(
        Guid bindingId,
        string bindingKey,
        CancellationToken cancellationToken);
}
