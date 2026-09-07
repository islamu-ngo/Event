using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.Footer.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record DeleteFooterLinkCommand : IRequest<bool>, ISecureRequest
{
    public Guid UserId { get; init; }
    public Guid TenantId { get; init; }
    public Guid LinkId { get; init; }
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);

}
