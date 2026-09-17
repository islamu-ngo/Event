using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.View)]
public sealed record GetEventTemplateSyncHistoryQuery(Guid EventId, int PageNumber, int PageSize)
    : IQuery<PaginatedResult<EventTemplateSyncHistoryItemDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString();
}
