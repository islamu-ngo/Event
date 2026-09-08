using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.View)]
public sealed record GetEventTemplateSyncHistoryQuery(Guid EventId, int PageNumber, int PageSize)
    : IRequest<PaginatedResult<EventTemplateSyncHistoryItemDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString();
}
