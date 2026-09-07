using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;

public sealed class GetClaimantOrganizerClaimsRequestHandler(
    IEventOrganizerClaimRepository claimRepository,
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IGroupTenantRepository groupTenantRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IMapper mapper)
    : IRequestHandler<GetClaimantOrganizerClaimsRequest, IReadOnlyList<EventOrganizerClaimDto>>
{
    public async Task<IReadOnlyList<EventOrganizerClaimDto>> Handle(
        GetClaimantOrganizerClaimsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not { } userId
            || !await ClaimantActorAccessEvaluator.CanControlAsync(
                request.ClaimantActorId,
                userId,
                tenantContext.TenantId,
                actorRepository,
                tenantUserRepository,
                organizationTenantRepository,
                groupTenantRepository,
                organizationMemberRepository,
                groupMemberRepository,
                cancellationToken))
        {
            throw new AuthorizationException("Organizer claims are available only to users controlling the claimant actor.");
        }

        var claims = await claimRepository.ListByClaimantAsync(request.ClaimantActorId, cancellationToken);
        return mapper.Map<List<EventOrganizerClaimDto>>(claims);
    }
}
