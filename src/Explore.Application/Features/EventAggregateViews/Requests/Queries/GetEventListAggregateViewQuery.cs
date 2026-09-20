using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAggregateView;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Features.EventAggregateViews.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.View)]
public sealed record GetEventListAggregateViewQuery(
    AggregateViewFilterDto Filter,
    ExposureLevel ExposureCeiling,
    int Page,
    int PageSize) : IQuery<BaseCommandResponse<PaginatedResult<EventListAggregateViewDto>>>, ISecureRequest;
