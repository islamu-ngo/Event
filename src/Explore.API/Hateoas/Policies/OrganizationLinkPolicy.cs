namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Organization;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

/// <summary>
/// Link policy for OrganizationDto (detail view).
/// Determines which links should be included based on resource state and user authorization.
/// </summary>
public sealed class OrganizationDetailLinkPolicy : ILinkPolicy<OrganizationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(OrganizationDto dto, ClaimsPrincipal? user)
    {
        // Self link - always included
        yield return LinkDefinition.Self(
            RouteNames.GetOrganizationById,
            new { id = dto.Id });

        // Collection link
        yield return LinkDefinition.Collection(RouteNames.GetOrganizations);

        yield return LinkDefinition.Related(
            LinkRelations.Members,
            RouteNames.GetOrganizationMembersByOrganization,
            new { organizationId = dto.Id });

        yield return new LinkDefinition(
            LinkRelations.LegitimacyEvidence,
            RouteNames.GetOrganizationTenantEvidenceCollection,
            new { organizationId = dto.Id },
            HttpMethods.Get,
            "Organization legitimacy evidence",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Organizations.ViewEvidence,
                ResourceDescriptors.Organization,
                dto);

        if (dto.ApprovalStatusId == (int)ApprovalStatusEnum.Pending)
        {
            yield return new LinkDefinition(
                LinkRelations.PrepareEvidenceUpload,
                RouteNames.CreateOrganizationTenantEvidenceUploadSession,
                new { organizationId = dto.Id },
                HttpMethods.Post,
                "Prepare legitimacy evidence upload",
                RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.Organizations.SubmitEvidence,
                    ResourceDescriptors.Organization,
                    dto);

            yield return new LinkDefinition(
                LinkRelations.SubmitEvidence,
                RouteNames.SubmitOrganizationTenantEvidence,
                new { organizationId = dto.Id },
                HttpMethods.Post,
                "Submit legitimacy evidence",
                RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.Organizations.SubmitEvidence,
                    ResourceDescriptors.Organization,
                    dto);
        }

        // Actor link (if organization has an actor)
        if (dto.ActorId.HasValue)
        {
            yield return LinkDefinition.Related(
                LinkRelations.Actor,
                RouteNames.GetActorById,
                new { id = dto.ActorId.Value });
        }

        // Edit link - requires authentication
        // In a real implementation, you'd check if user is a member of this organization
        yield return LinkDefinition.Edit(
            RouteNames.UpdateOrganization,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Organization, dto);

        // Delete link - requires admin role
        yield return LinkDefinition.Delete(
            RouteNames.DeleteOrganization,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.Organization, dto);
    }
}

/// <summary>
/// Link policy for OrganizationListDto (collection items).
/// </summary>
public sealed class OrganizationCollectionLinkPolicy : ICollectionLinkPolicy<OrganizationListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(OrganizationListDto dto, ClaimsPrincipal? user)
    {
        // Self link for the item
        yield return LinkDefinition.Self(
            RouteNames.GetOrganizationById,
            new { id = dto.Id });

        // Members link
        yield return LinkDefinition.Related(
            LinkRelations.Members,
            RouteNames.GetOrganizationMembersByOrganization,
            new { organizationId = dto.Id });

        // Edit link - requires authorization
        yield return LinkDefinition.Edit(
            RouteNames.UpdateOrganization,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.OrganizationList, dto);

        // Delete link - requires authorization
        yield return LinkDefinition.Delete(
            RouteNames.DeleteOrganization,
            new { id = dto.Id })
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.OrganizationList, dto);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return LinkDefinition.Create(RouteNames.CreateOrganization)
            .RequirePermission(AuthorizationActions.Create, typeof(OrganizationDto), "organization");
    }
}
