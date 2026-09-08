using Explore.Application.Authorization;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Requests.Queries;

[AuthorizeResource(ResourceKinds.SupportAccessSession, AuthorizationActions.SupportAccessSessions.ViewAudit)]
public sealed record GetSupportAccessAuditEventsQuery : IRequest<PaginatedResult<SupportAccessAuditEventDto>>, ISecureRequest
{
    public Guid TargetTenantId { get; init; }
    public Guid SessionId { get; init; }
    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => SessionId == Guid.Empty ? null : SessionId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new SupportAccessSessionAuthorizationFacts(TargetTenantId, SessionId, null, null, null);
}
