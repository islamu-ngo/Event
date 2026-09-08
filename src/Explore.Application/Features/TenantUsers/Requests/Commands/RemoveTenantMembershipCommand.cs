using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.TenantUsers.Requests.Commands;

[AuthorizeResource(ResourceKinds.User, AuthorizationActions.Update)]
public sealed record RemoveTenantMembershipCommand(Guid TenantId, Guid UserId) : IRequest<bool>, ISecureRequest
{
    string? ISecureRequest.ResourceId => UserId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new UserAuthorizationFacts(TenantId, null, null);
}
