using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.View)]
public sealed record GetEventSessionTemplateSyncHistoryQuery(Guid EventSessionId, int PageNumber, int PageSize)
    : IRequest<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
