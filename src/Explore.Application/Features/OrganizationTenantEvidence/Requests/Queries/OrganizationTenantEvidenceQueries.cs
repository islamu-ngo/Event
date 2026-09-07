using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using MediatR;

namespace Explore.Application.Features.OrganizationTenantEvidence.Requests.Queries;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Organizations.ViewEvidence)]
public sealed record GetOrganizationTenantEvidenceRequest(Guid OrganizationId, Guid EvidenceId)
    : IRequest<OrganizationTenantEvidenceDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => OrganizationId == Guid.Empty ? null : OrganizationId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationAuthorizationFacts(Guid.Empty, OrganizationId);
}

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Organizations.ViewEvidence)]
public sealed record GetOrganizationTenantEvidenceCollectionRequest(Guid OrganizationId)
    : IRequest<IReadOnlyList<OrganizationTenantEvidenceDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => OrganizationId == Guid.Empty ? null : OrganizationId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationAuthorizationFacts(Guid.Empty, OrganizationId);
}
