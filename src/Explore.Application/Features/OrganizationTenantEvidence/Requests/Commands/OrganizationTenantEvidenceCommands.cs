using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Organizations.SubmitEvidence)]
public sealed record CreateOrganizationTenantEvidenceUploadSessionCommand
    : IRequest<BaseCommandResponse<StorageUploadSessionDto>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }
    public required CreateOrganizationTenantEvidenceUploadSessionDto Upload { get; init; }
    string? ISecureRequest.ResourceId => OrganizationId == Guid.Empty ? null : OrganizationId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationAuthorizationFacts(Guid.Empty, OrganizationId);
}

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Organizations.SubmitEvidence)]
public sealed record SubmitOrganizationTenantEvidenceCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }
    public required SubmitOrganizationTenantEvidenceDto Evidence { get; init; }
    string? ISecureRequest.ResourceId => OrganizationId == Guid.Empty ? null : OrganizationId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationAuthorizationFacts(Guid.Empty, OrganizationId);
}

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Organizations.ReviewEvidence)]
public sealed record ReviewOrganizationTenantEvidenceCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }
    public Guid EvidenceId { get; init; }
    public required ReviewOrganizationTenantEvidenceDto Review { get; init; }
    string? ISecureRequest.ResourceId => OrganizationId == Guid.Empty ? null : OrganizationId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new OrganizationAuthorizationFacts(Guid.Empty, OrganizationId);
}
