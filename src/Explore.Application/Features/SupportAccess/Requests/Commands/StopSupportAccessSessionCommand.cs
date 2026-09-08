using Explore.Application.Authorization;
using Explore.Application.DTOs.SupportAccess;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Requests.Commands;

[AuthorizeResource(ResourceKinds.SupportAccessSession, AuthorizationActions.SupportAccessSessions.Stop)]
public sealed record StopSupportAccessSessionCommand : IRequest<SupportAccessSessionCommandResponseDto>, ISecureRequest
{
    public Guid SessionId { get; init; }
    public string? EndReasonText { get; init; }

    string? ISecureRequest.ResourceId => SessionId == Guid.Empty ? null : SessionId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new SupportAccessSessionAuthorizationFacts(Guid.Empty, SessionId, null, null, null);
}
