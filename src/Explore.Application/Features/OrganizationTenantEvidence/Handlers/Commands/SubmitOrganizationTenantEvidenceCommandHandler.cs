using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationTenantEvidence.Validators;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using OrganizationLegitimacyEvidence = Explore.Domain.OrganizationTenantEvidence;

namespace Explore.Application.Features.OrganizationTenantEvidence.Handlers.Commands;

public sealed class SubmitOrganizationTenantEvidenceCommandHandler(
    IOrganizationTenantRepository organizationTenantRepository,
    IOrganizationTenantEvidenceRepository evidenceRepository,
    IStorageObjectRepository storageObjectRepository,
    IAdminContext adminContext,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SubmitOrganizationTenantEvidenceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SubmitOrganizationTenantEvidenceCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new SubmitOrganizationTenantEvidenceDtoValidator();
        var validation = await validator.ValidateAsync(request.Evidence, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure("Legitimacy evidence failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!await adminContext.IsOrganizationAdminAsync(request.OrganizationId, cancellationToken))
        {
            return Failure("Legitimacy evidence could not be submitted.", ["Organization administrator authority is required."]);
        }

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var participation = await organizationTenantRepository.GetByOrganizationAndTenant(
                request.OrganizationId,
                tenantContext.TenantId,
                token);
            if (participation is null
                || participation.ApprovalStatusId != (int)ApprovalStatusEnum.Pending
                || participation.IsSuspended)
            {
                return Failure(
                    "Legitimacy evidence could not be submitted.",
                    ["A pending, active Organization participation was not found in the current tenant."]);
            }

            var document = await storageObjectRepository.GetEvidenceDocumentAsync(
                request.Evidence.DocumentStorageObjectId,
                token);
            if (!IsEligibleDocument(document, participation))
            {
                return Failure(
                    "Legitimacy evidence could not be submitted.",
                    ["The document must be active, private, tenant-local, and owned by this Organization participation."]);
            }

            var existing = await evidenceRepository.GetByDocumentAsync(
                participation.Id,
                document!.Id,
                token);
            if (existing is not null)
            {
                return Success(existing.Id, "Legitimacy evidence is already attached.");
            }

            var evidence = OrganizationLegitimacyEvidence.CreatePending(participation, document);
            evidence = await evidenceRepository.Create(evidence);
            return Success(evidence.Id, "Legitimacy evidence submitted.");
        }, cancellationToken);
    }

    private static bool IsEligibleDocument(StorageObject? document, OrganizationTenant participation)
    {
        return document is not null
            && !document.IsDeleted
            && document.TenantId == participation.TenantId
            && document.FileTypeId == (int)FileTypeEnum.Document
            && document.LifecycleState == StorageObjectLifecycleStates.Active
            && document.Visibility == StorageObjectVisibilities.PrivateOwner
            && document.Purpose == StorageObjectPurposes.Document
            && document.OwningResourceKind == StorageOwningResourceKinds.OrganizationTenant
            && document.OwningResourceId == participation.Id
            && !string.IsNullOrWhiteSpace(document.ObjectKey);
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
