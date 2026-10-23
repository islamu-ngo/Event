using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventCustomProperties.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventCustomPropertiesRequest : IQuery<ManagedEventCustomPropertiesDto>, ISecureRequest
{
    public Guid EventId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<EventCustomPropertyDefinitionListDto>.DefaultPageSize;

    string? ISecureRequest.ResourceId => EventId.ToString();
}
