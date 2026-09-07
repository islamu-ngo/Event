using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Queries;
using Explore.Domain;
using MediatR;
using OrganizationLegitimacyEvidence = Explore.Domain.OrganizationTenantEvidence;

namespace Explore.Application.Features.OrganizationTenantEvidence.Handlers.Queries;

public sealed class GetOrganizationTenantEvidenceRequestHandler(
    IOrganizationTenantRepository organizationTenantRepository,
    IOrganizationTenantEvidenceRepository evidenceRepository,
    ITenantContext tenantContext)
    : IRequestHandler<GetOrganizationTenantEvidenceRequest, OrganizationTenantEvidenceDto?>
{
    public async Task<OrganizationTenantEvidenceDto?> Handle(
        GetOrganizationTenantEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        var participation = await organizationTenantRepository.GetByOrganizationAndTenant(
            request.OrganizationId,
            tenantContext.TenantId,
            cancellationToken);
        if (participation is null)
        {
            return null;
        }

        var evidence = await evidenceRepository.GetDetailsAsync(
            request.EvidenceId,
            trackChanges: false,
            cancellationToken);
        return evidence?.OrganizationTenantId == participation.Id
            ? Map(evidence)
            : null;
    }

    internal static OrganizationTenantEvidenceDto Map(OrganizationLegitimacyEvidence evidence)
    {
        var document = evidence.DocumentStorageObject
            ?? throw new InvalidOperationException("Legitimacy evidence document metadata was not loaded.");
        var organizationId = evidence.OrganizationTenant?.OrganizationId
            ?? throw new InvalidOperationException("Legitimacy evidence participation was not loaded.");

        return new OrganizationTenantEvidenceDto
        {
            Id = evidence.Id,
            TenantId = evidence.TenantId,
            OrganizationTenantId = evidence.OrganizationTenantId,
            OrganizationId = organizationId,
            DocumentStorageObjectId = evidence.DocumentStorageObjectId,
            DocumentCreatedBy = document.CreatedBy,
            DocumentDisplayName = document.SafeDisplayName,
            DocumentContentType = document.ContentType,
            DocumentSizeBytes = document.Size,
            ReviewStatusId = evidence.ReviewStatusId,
            ReviewStatusCode = evidence.ReviewStatus?.MasterCode,
            ReviewStatusName = evidence.ReviewStatus?.FullName,
            ReviewNotes = evidence.ReviewNotes,
            ReviewedAt = evidence.ReviewedAt,
            CreatedAt = evidence.CreatedAt,
            ConcurrencyStamp = evidence.ConcurrencyStamp
        };
    }
}

public sealed class GetOrganizationTenantEvidenceCollectionRequestHandler(
    IOrganizationTenantRepository organizationTenantRepository,
    IOrganizationTenantEvidenceRepository evidenceRepository,
    ITenantContext tenantContext)
    : IRequestHandler<GetOrganizationTenantEvidenceCollectionRequest, IReadOnlyList<OrganizationTenantEvidenceDto>>
{
    public async Task<IReadOnlyList<OrganizationTenantEvidenceDto>> Handle(
        GetOrganizationTenantEvidenceCollectionRequest request,
        CancellationToken cancellationToken)
    {
        var participation = await organizationTenantRepository.GetByOrganizationAndTenant(
            request.OrganizationId,
            tenantContext.TenantId,
            cancellationToken);
        if (participation is null)
        {
            return [];
        }

        var evidence = await evidenceRepository.ListByParticipationAsync(participation.Id, cancellationToken);
        return evidence.Select(GetOrganizationTenantEvidenceRequestHandler.Map).ToList();
    }
}
