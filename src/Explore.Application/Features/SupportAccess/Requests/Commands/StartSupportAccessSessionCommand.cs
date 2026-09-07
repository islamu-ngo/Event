using Explore.Application.Authorization;
using Explore.Application.DTOs.SupportAccess;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Requests.Commands;

[AuthorizeResource(ResourceKinds.SupportAccessSession, AuthorizationActions.SupportAccessSessions.Start)]
public sealed record StartSupportAccessSessionCommand : IRequest<SupportAccessSessionCommandResponseDto>, ISecureRequest
{
    public Guid TargetTenantId { get; init; }
    public Guid? TargetTenantUserId { get; init; }
    public SupportAccessModeEnum Mode { get; init; } = SupportAccessModeEnum.ReadOnly;
    public int DurationMinutes { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string ReasonText { get; init; } = string.Empty;
    public string? TicketReference { get; init; }

    string? ISecureRequest.ResourceId => TargetTenantId == Guid.Empty ? null : TargetTenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new SupportAccessSessionAuthorizationFacts(TargetTenantId, null, null, null, null);
}
