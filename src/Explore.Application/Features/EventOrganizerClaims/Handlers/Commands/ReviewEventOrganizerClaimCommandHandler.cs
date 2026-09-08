using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.DTOs.EventOrganizerClaim.Validators;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Commands;

public sealed class ReviewEventOrganizerClaimCommandHandler(
    IEventRepository eventRepository,
    IEventOrganizerClaimRepository claimRepository,
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IGroupTenantRepository groupTenantRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<ReviewEventOrganizerClaimCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReviewEventOrganizerClaimCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ReviewEventOrganizerClaimDtoValidator();
        var validation = await validator.ValidateAsync(request.Review, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(request.ClaimId, "Organizer claim review failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (currentUserService.UserId is not { } reviewerUserId)
        {
            return Failure(request.ClaimId, "Organizer claim could not be reviewed.", ["An authenticated reviewer is required."]);
        }

        var reviewedAt = DateTime.UtcNow;
        var reasonCode = request.Review.ReasonCode.Trim();
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var claim = await claimRepository.GetForUpdateAsync(request.ClaimId, token);
            if (claim is null || claim.EventId != request.EventId || claim.TenantId != tenantContext.TenantId)
            {
                return Failure(request.ClaimId, "Organizer claim could not be reviewed.", ["Organizer claim was not found for this event."]);
            }

            var targetStatusId = GetTargetStatusId(request.Review.Decision);
            if (claim.StatusId == targetStatusId
                && string.Equals(claim.DecisionReasonCode, reasonCode, StringComparison.Ordinal))
            {
                return Success(claim.Id, "Organizer claim decision already applied.");
            }

            if (claim.ConcurrencyStamp != request.Review.ExpectedConcurrencyStamp)
            {
                return Failure(request.ClaimId, "Organizer claim could not be reviewed.", ["Organizer claim changed since it was loaded."]);
            }

            try
            {
                if (request.Review.Decision == EventOrganizerClaimReviewDecisionDto.Approve)
                {
                    if (!await ClaimantActorAccessEvaluator.IsEligibleAsync(
                            claim.ClaimantActorId,
                            tenantContext.TenantId,
                            actorRepository,
                            tenantUserRepository,
                            organizationTenantRepository,
                            groupTenantRepository,
                            token))
                    {
                        return Failure(request.ClaimId, "Organizer claim could not be reviewed.", ["The claimant actor is not eligible to organize events in the current tenant."]);
                    }

                    var @event = await eventRepository.GetById(request.EventId);
                    if (@event is null || @event.TenantId != tenantContext.TenantId)
                    {
                        return Failure(request.ClaimId, "Organizer claim could not be reviewed.", ["Event was not found in the current tenant."]);
                    }

                    claim.Approve(@event, reviewerUserId, reasonCode, reviewedAt);
                    await claimRepository.UpdateApprovalAsync(claim, @event, token);
                }
                else if (request.Review.Decision == EventOrganizerClaimReviewDecisionDto.RequestEvidence)
                {
                    claim.RequestEvidence(reviewerUserId, reasonCode, reviewedAt);
                }
                else
                {
                    claim.Reject(reviewerUserId, reasonCode, reviewedAt);
                }
            }
            catch (InvalidOperationException exception)
            {
                return Failure(request.ClaimId, "Organizer claim could not be reviewed.", [exception.Message]);
            }

            if (request.Review.Decision != EventOrganizerClaimReviewDecisionDto.Approve)
            {
                await claimRepository.Update(claim);
            }
            return Success(claim.Id, "Organizer claim decision applied.");
        }, cancellationToken);
    }

    private static int GetTargetStatusId(EventOrganizerClaimReviewDecisionDto decision) => decision switch
    {
        EventOrganizerClaimReviewDecisionDto.RequestEvidence => (int)EventOrganizerClaimStatusEnum.EvidenceRequired,
        EventOrganizerClaimReviewDecisionDto.Approve => (int)EventOrganizerClaimStatusEnum.Approved,
        EventOrganizerClaimReviewDecisionDto.Reject => (int)EventOrganizerClaimStatusEnum.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null)
    };

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation(errors, message, id);
}
