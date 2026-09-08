using Explore.Application.Authorization;
using Explore.Application.DTOs.EventReporting;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetModerationReportDetailRequest : IRequest<ModerationReportDetailDto?>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ReportId { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
}
