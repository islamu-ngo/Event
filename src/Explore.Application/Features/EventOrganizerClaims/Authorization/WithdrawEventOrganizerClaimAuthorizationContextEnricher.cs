using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;

namespace Explore.Application.Features.EventOrganizerClaims.Authorization;

public sealed class WithdrawEventOrganizerClaimAuthorizationContextEnricher(
    IEventOrganizerClaimRepository claimRepository,
    IActorRepository actorRepository,
    ITenantContext? tenantContext = null)
    : IAuthorizationContextEnricher<WithdrawEventOrganizerClaimCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(
        WithdrawEventOrganizerClaimCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ClaimId == Guid.Empty || tenantContext is null)
        {
            throw new AuthorizationException(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.WithdrawOrganizerClaim);
        }

        var claim = await claimRepository.GetDetailsAsync(request.ClaimId, trackChanges: false, cancellationToken);
        if (claim is null || claim.TenantId != tenantContext.TenantId)
        {
            throw new AuthorizationException(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.WithdrawOrganizerClaim);
        }

        var claimantActor = await actorRepository.GetActorWithDetails(claim.ClaimantActorId, cancellationToken);
        if (claimantActor is null || claimantActor.Id != claim.ClaimantActorId)
        {
            throw new AuthorizationException(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.WithdrawOrganizerClaim);
        }

        return new AuthorizationContext(
            claim.Id.ToString(),
            new EventOrganizerClaimAuthorizationFacts(
                claim.TenantId,
                claim.EventId,
                claim.Id,
                claim.ClaimantActorId,
                claimantActor.UserId,
                claimantActor.OrganizationId,
                claimantActor.GroupId,
                claim.Status?.MasterCode ?? claim.StatusId.ToString()));
    }
}
