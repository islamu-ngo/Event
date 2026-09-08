using Explore.Application.Authorization;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetModerationReportQueueRequest : IRequest<PaginatedResult<ModerationReportQueueItemDto>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    private IReadOnlyCollection<EventReportStatus> _statuses = Array.AsReadOnly(Array.Empty<EventReportStatus>());

    public IReadOnlyCollection<EventReportStatus> Statuses
    {
        get => _statuses;
        init => _statuses = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    private IReadOnlyCollection<EventReportCaseStatus> _caseStatuses = Array.AsReadOnly(Array.Empty<EventReportCaseStatus>());

    public IReadOnlyCollection<EventReportCaseStatus> CaseStatuses
    {
        get => _caseStatuses;
        init => _caseStatuses = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public EventReportPriority? Priority { get; init; }
    public string? QueueCode { get; init; }
    public Guid? AssignedModeratorUserId { get; init; }
    public bool UnassignedOnly { get; init; }
    public bool OpenOnly { get; init; } = true;
    public string? ReasonCode { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
}
