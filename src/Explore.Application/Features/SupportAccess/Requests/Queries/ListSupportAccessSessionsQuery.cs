using Explore.Application.Authorization;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Requests.Queries;

[AuthorizeResource(ResourceKinds.SupportAccessSession, AuthorizationActions.SupportAccessSessions.List)]
public sealed record ListSupportAccessSessionsQuery : IRequest<PaginatedResult<SupportAccessSessionDto>>, ISecureRequest
{
    public Guid TargetTenantId { get; init; }
    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => TargetTenantId == Guid.Empty ? null : TargetTenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new SupportAccessSessionAuthorizationFacts(TargetTenantId, null, null, null, null);
}
