using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedSessionTemplateSyncHistoryQuery(
    Guid EventId, Guid SessionId, int PageNumber, int PageSize)
    : IQuery<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString("D");
}
