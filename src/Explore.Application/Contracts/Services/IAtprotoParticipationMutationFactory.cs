using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Services;

/// <summary>Joins verified AT Protocol onboarding to its already-owned privacy and primary transaction.</summary>
public interface IAtprotoParticipationMutationFactory
{
    Task<IAtprotoParticipationMutation> AcquireAsync(ProviderAccountKey account, Guid userId,
        Guid externalLoginId, Guid tenantId, CancellationToken cancellationToken);
}

public interface IAtprotoParticipationMutation
{
    Task FenceActorAsync(Guid actorId, CancellationToken cancellationToken);
    Task RecordMembershipAsync(Guid memberId, CancellationToken cancellationToken);
    Task EnsureCurrentAsync(CancellationToken cancellationToken);
}
