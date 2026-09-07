using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationTenantEvidence.Validators;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.OrganizationTenantEvidence.Handlers.Commands;

public sealed class CreateOrganizationTenantEvidenceUploadSessionCommandHandler(
    IOrganizationTenantRepository organizationTenantRepository,
    IAdminContext adminContext,
    ITenantContext tenantContext,
    ISender sender)
    : IRequestHandler<CreateOrganizationTenantEvidenceUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>
{
    public async Task<BaseCommandResponse<StorageUploadSessionDto>> Handle(
        CreateOrganizationTenantEvidenceUploadSessionCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new CreateOrganizationTenantEvidenceUploadSessionDtoValidator();
        var validation = await validator.ValidateAsync(request.Upload, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure("Evidence upload session failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!await adminContext.IsOrganizationAdminAsync(request.OrganizationId, cancellationToken))
        {
            return Failure("Evidence upload session could not be created.", ["Organization administrator authority is required."]);
        }

        var participation = await organizationTenantRepository.GetByOrganizationAndTenant(
            request.OrganizationId,
            tenantContext.TenantId,
            cancellationToken);
        if (participation is null
            || participation.ApprovalStatusId != (int)ApprovalStatusEnum.Pending
            || participation.IsSuspended)
        {
            return Failure(
                "Evidence upload session could not be created.",
                ["A pending, active Organization participation was not found in the current tenant."]);
        }

        var fileName = request.Upload.FileName.Trim();
        return await sender.Send(
            new CreateStorageUploadSessionCommand
            {
                TenantId = participation.TenantId,
                UploadSessionDto = new CreateStorageUploadSessionDto
                {
                    ExpectedSizeBytes = request.Upload.ExpectedSizeBytes,
                    ContentType = "application/pdf",
                    OriginalFileName = fileName,
                    SafeDisplayName = fileName,
                    Extension = "pdf",
                    Purpose = StorageObjectPurposes.Document,
                    Visibility = StorageObjectVisibilities.PrivateOwner,
                    OwningResourceKind = StorageOwningResourceKinds.OrganizationTenant,
                    OwningResourceId = participation.Id,
                    IdempotencyKey = $"organization-evidence:{Guid.CreateVersion7():N}"
                }
            },
            cancellationToken);
    }

    private static BaseCommandResponse<StorageUploadSessionDto> Failure(
        string message,
        IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<StorageUploadSessionDto>(errors, message);
}
