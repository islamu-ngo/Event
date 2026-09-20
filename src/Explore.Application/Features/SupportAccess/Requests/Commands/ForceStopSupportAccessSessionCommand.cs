using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.SupportAccess;

namespace Explore.Application.Features.SupportAccess.Requests.Commands;

[AuthorizeResource(ResourceKinds.SupportAccessSession, AuthorizationActions.SupportAccessSessions.ForceStop)]
public sealed record ForceStopSupportAccessSessionCommand : ICommand<SupportAccessSessionCommandResponseDto>, ISecureRequest
{
    public Guid SessionId { get; init; }
    public string? EndReasonText { get; init; }

    string? ISecureRequest.ResourceId => SessionId == Guid.Empty ? null : SessionId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new SupportAccessSessionAuthorizationFacts(Guid.Empty, SessionId, null, null, null);
}
