using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.OrganizationTenantEvidence.Validators;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using OrganizationLegitimacyEvidence = Explore.Domain.OrganizationTenantEvidence;

namespace Explore.Application.Features.OrganizationTenantEvidence.Handlers.Commands;

public sealed class ReviewOrganizationTenantEvidenceCommandHandler(
    IOrganizationTenantEvidenceRepository evidenceRepository,
    IAdminContext adminContext,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ReviewOrganizationTenantEvidenceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReviewOrganizationTenantEvidenceCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ReviewOrganizationTenantEvidenceDtoValidator();
        var validation = await validator.ValidateAsync(request.Review, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(request.EvidenceId, "Legitimacy evidence review failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (currentUserService.UserId is not { } reviewerUserId
            || !await adminContext.IsTenantAdminAsync(tenantContext.TenantId, cancellationToken))
        {
            return Failure(request.EvidenceId, "Legitimacy evidence could not be reviewed.", ["Tenant administrator authority is required."]);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var evidence = await evidenceRepository.GetDetailsAsync(request.EvidenceId, trackChanges: true, token);
            if (evidence is null
                || evidence.TenantId != tenantContext.TenantId
                || evidence.OrganizationTenant?.OrganizationId != request.OrganizationId)
            {
                return Failure(request.EvidenceId, "Legitimacy evidence could not be reviewed.", ["Legitimacy evidence was not found for this Organization."]);
            }

            var targetStatusId = request.Review.Decision == OrganizationTenantEvidenceReviewDecisionDto.Approve
                ? (int)ApprovalStatusEnum.Approved
                : (int)ApprovalStatusEnum.Rejected;
            var normalizedNotes = string.IsNullOrWhiteSpace(request.Review.Notes)
                ? null
                : request.Review.Notes.Trim();
            if (evidence.ReviewStatusId == targetStatusId
                && string.Equals(evidence.ReviewNotes, normalizedNotes, StringComparison.Ordinal))
            {
                return Success(evidence.Id, "Legitimacy evidence decision is already applied.");
            }

            if (evidence.ConcurrencyStamp != request.Review.ExpectedConcurrencyStamp)
            {
                return Failure(request.EvidenceId, "Legitimacy evidence could not be reviewed.", ["Legitimacy evidence changed since it was loaded."]);
            }

            if (!IsStillEligible(evidence))
            {
                return Failure(request.EvidenceId, "Legitimacy evidence could not be reviewed.", ["The retained document is no longer active and eligible for review."]);
            }

            try
            {
                evidence.Review(
                    request.Review.Decision == OrganizationTenantEvidenceReviewDecisionDto.Approve,
                    reviewerUserId,
                    normalizedNotes,
                    DateTime.UtcNow);
            }
            catch (InvalidOperationException exception)
            {
                return Failure(request.EvidenceId, "Legitimacy evidence could not be reviewed.", [exception.Message]);
            }

            await evidenceRepository.Update(evidence);
            return Success(evidence.Id, "Legitimacy evidence decision applied.");
        }, cancellationToken);
    }

    private static bool IsStillEligible(OrganizationLegitimacyEvidence evidence)
    {
        var document = evidence.DocumentStorageObject;
        return document is not null
            && !document.IsDeleted
            && document.TenantId == evidence.TenantId
            && document.FileTypeId == (int)FileTypeEnum.Document
            && document.LifecycleState == StorageObjectLifecycleStates.Active
            && document.Visibility == StorageObjectVisibilities.PrivateOwner
            && document.Purpose == StorageObjectPurposes.Document
            && document.OwningResourceKind == StorageOwningResourceKinds.OrganizationTenant
            && document.OwningResourceId == evidence.OrganizationTenantId;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation(errors, message, id);
}
