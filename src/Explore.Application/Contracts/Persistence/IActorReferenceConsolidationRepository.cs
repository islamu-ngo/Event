namespace Explore.Application.Contracts.Persistence;

public interface IActorReferenceConsolidationRepository
{
    Task<bool> MoveMutableReferencesAsync(Guid sourceActorId, Guid targetActorId, int targetActorTypeId, CancellationToken cancellationToken = default);
    Task<bool> HasCompletedConsolidationAsync(Guid atprotoIdentityId, Guid targetActorId, string evidenceReference, CancellationToken cancellationToken = default);
}
