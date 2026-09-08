using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationAnalytics;
using MediatR;

namespace Explore.Application.Features.RegistrationAnalytics;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations)]
public sealed record GetRegistrationAnswerAnalyticsQuery(
    Guid EventId,
    Guid FormId,
    Guid FormVersionId) : IRequest<RegistrationAnswerAnalyticsDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
