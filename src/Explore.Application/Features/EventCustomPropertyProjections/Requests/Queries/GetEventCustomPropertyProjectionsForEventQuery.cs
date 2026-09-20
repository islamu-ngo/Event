using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;

[AuthorizeResource(ResourceKinds.CustomPropertyProjection, AuthorizationActions.CustomPropertyProjections.View)]
public sealed record GetEventCustomPropertyProjectionsForEventQuery : IQuery<BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public ExposureLevel? ExposureCeiling { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        EventId == Guid.Empty
        ? null
        : new CustomPropertyProjectionAuthorizationFacts(Guid.Empty, EventId, null);
}
