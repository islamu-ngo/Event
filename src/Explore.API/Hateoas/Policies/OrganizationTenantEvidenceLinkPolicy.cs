using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class OrganizationTenantEvidenceDetailLinkPolicy
    : ILinkPolicy<OrganizationTenantEvidenceDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        OrganizationTenantEvidenceDto dto,
        ClaimsPrincipal? user)
    {
        yield return Self(dto);
        yield return Document(dto);
        if (dto.ReviewStatusId == (int)ApprovalStatusEnum.Pending)
        {
            yield return Review(dto);
        }
    }

    internal static LinkDefinition Self(OrganizationTenantEvidenceDto dto) =>
        new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetOrganizationTenantEvidence,
            new { organizationId = dto.OrganizationId, evidenceId = dto.Id },
            HttpMethods.Get,
            "Organization legitimacy evidence",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Organizations.ViewEvidence,
                ResourceDescriptors.OrganizationTenantEvidence,
                dto);

    internal static LinkDefinition Document(OrganizationTenantEvidenceDto dto) =>
        new LinkDefinition(
            LinkRelations.Document,
            RouteNames.GetStorageObjectContent,
            new { id = dto.DocumentStorageObjectId },
            HttpMethods.Get,
            "Evidence document",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.StorageObjects.Download,
                ResourceDescriptors.OrganizationTenantEvidenceDocument,
                dto);

    internal static LinkDefinition Review(OrganizationTenantEvidenceDto dto) =>
        new LinkDefinition(
            LinkRelations.ReviewEvidence,
            RouteNames.ReviewOrganizationTenantEvidence,
            new { organizationId = dto.OrganizationId, evidenceId = dto.Id },
            HttpMethods.Post,
            "Review legitimacy evidence",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Organizations.ReviewEvidence,
                ResourceDescriptors.OrganizationTenantEvidence,
                dto);
}

public sealed class OrganizationTenantEvidenceCollectionLinkPolicy
    : ICollectionLinkPolicy<OrganizationTenantEvidenceDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        OrganizationTenantEvidenceDto dto,
        ClaimsPrincipal? user)
    {
        yield return OrganizationTenantEvidenceDetailLinkPolicy.Self(dto);
        yield return OrganizationTenantEvidenceDetailLinkPolicy.Document(dto);
        if (dto.ReviewStatusId == (int)ApprovalStatusEnum.Pending)
        {
            yield return OrganizationTenantEvidenceDetailLinkPolicy.Review(dto);
        }
    }
}
