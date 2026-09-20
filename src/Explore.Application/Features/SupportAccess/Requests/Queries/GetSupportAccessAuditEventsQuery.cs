using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Responses;

namespace Explore.Application.Features.SupportAccess.Requests.Queries;

[AuthorizeResource(ResourceKinds.SupportAccessSession, AuthorizationActions.SupportAccessSessions.ViewAudit)]
public sealed record GetSupportAccessAuditEventsQuery : IQuery<PaginatedResult<SupportAccessAuditEventDto>>, ISecureRequest
{
    public Guid TargetTenantId { get; init; }
    public Guid SessionId { get; init; }
    public int Limit { get; init; } = 100;

    string? ISecureRequest.ResourceId => SessionId == Guid.Empty ? null : SessionId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new SupportAccessSessionAuthorizationFacts(TargetTenantId, SessionId, null, null, null);
}
