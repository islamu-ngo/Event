namespace Explore.Application.Contracts.Persistence;

public interface IActorReferenceConsolidationRepository
{
    Task<bool> MoveMutableReferencesAsync(Guid sourceActorId, Guid canonicalActorId, int canonicalActorTypeId, CancellationToken cancellationToken = default);
    Task<bool> HasCompletedConsolidationAsync(Guid atprotoIdentityId, Guid canonicalActorId, string evidenceReference, CancellationToken cancellationToken = default);
}
