using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationProviderConnectionCheckpointService(
    IRegistrationProviderRepository providerRepository,
    TimeProvider timeProvider,
    IRegistrationProviderDeferredCheckpointJournal deferredCheckpoints) : IRegistrationProviderConnectionCheckpoint
{
    public async Task RecordCredentialRefreshAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTime observedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (deferredCheckpoints.TryDefer(tenantId, connectionId,
            RegistrationProviderCheckpointEffect.CredentialRefreshed, observedAt))
            return;
        RegistrationProviderConnection connection = await GetConnectionAsync(tenantId, connectionId, cancellationToken);
        connection.RecordCredentialRefresh(observedAt);
        await providerRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAccessValidatedAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTime observedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (deferredCheckpoints.TryDefer(tenantId, connectionId,
            RegistrationProviderCheckpointEffect.AccessValidated, observedAt))
            return;
        RegistrationProviderConnection connection = await GetConnectionAsync(tenantId, connectionId, cancellationToken);
        connection.RecordAccessValidated(observedAt);
        await providerRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<RegistrationProviderConnection> GetConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken) =>
        await providerRepository.GetConnectionAsync(tenantId, connectionId, cancellationToken) ??
        throw new InvalidOperationException("Registration provider connection was not found for checkpoint update.");
}
