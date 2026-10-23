namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads actor ownership for an already admitted user within the original selected tenant.
/// The caller establishes user authority; these data facts do not authenticate or authorize it.
/// </summary>
public interface IActorOwnershipReader
{
    Task<ActorOwnershipFacts?> ReadForUserAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Ownership identifiers only, without actor profile, moderation or audit data.</summary>
public sealed record ActorOwnershipFacts(Guid Id, Guid? UserId, Guid? GroupId, Guid? OrganizationId);
