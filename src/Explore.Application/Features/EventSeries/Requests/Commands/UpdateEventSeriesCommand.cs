using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Requests.Commands;

[AuthorizeResource(ResourceKinds.Actor, AuthorizationActions.Update)]
public sealed record UpdateEventSeriesCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventSeriesId { get; init; }
    public Guid ActorId { get; init; }
    public Guid TenantId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventSeriesDto EventSeriesDto { get; init; }

    string? ISecureRequest.ResourceId => ActorId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new ActorAuthorizationFacts(TenantId, ActorId);
}
