using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Commands;

public sealed class WithdrawEventOrganizerClaimCommandHandler(
    IEventOrganizerClaimRepository claimRepository,
    IActorRepository actorRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<WithdrawEventOrganizerClaimCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        WithdrawEventOrganizerClaimCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not { } userId)
        {
            return Failure(request.ClaimId, "Organizer claim could not be withdrawn.", "An authenticated user is required.");
        }

        var claim = await claimRepository.GetForUpdateAsync(request.ClaimId, cancellationToken);
        if (claim is null || claim.EventId != request.EventId || claim.TenantId != tenantContext.TenantId)
        {
            return Failure(request.ClaimId, "Organizer claim could not be withdrawn.", "Organizer claim was not found for this event.");
        }

        if (!await ClaimantActorAccessEvaluator.CanControlOwnershipAsync(
                claim.ClaimantActorId,
                userId,
                actorRepository,
                organizationMemberRepository,
                groupMemberRepository,
                cancellationToken))
        {
            return Failure(request.ClaimId, "Organizer claim could not be withdrawn.", "The claimant actor is not controlled by the current user.");
        }

        if (claim.StatusId == (int)EventOrganizerClaimStatusEnum.Withdrawn)
        {
            return Success(claim.Id, "Organizer claim already withdrawn.");
        }

        if (request.ExpectedConcurrencyStamp == Guid.Empty || claim.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            return Failure(request.ClaimId, "Organizer claim could not be withdrawn.", "Organizer claim changed since it was loaded.");
        }

        try
        {
            claim.Withdraw(DateTime.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(request.ClaimId, "Organizer claim could not be withdrawn.", exception.Message);
        }

        await claimRepository.Update(claim);
        return Success(claim.Id, "Organizer claim withdrawn.");
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, string error) =>
        BaseCommandResponse.Validation<Guid>([error], message, id);
}
