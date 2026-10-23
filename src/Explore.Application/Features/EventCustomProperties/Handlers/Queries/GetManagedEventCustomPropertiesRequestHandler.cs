using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public sealed class GetManagedEventCustomPropertiesRequestHandler(IPrivateEventManagementReader reader)
    : IQueryHandler<GetManagedEventCustomPropertiesRequest, ManagedEventCustomPropertiesDto>
{
    public async Task<ManagedEventCustomPropertiesDto> QueryAsync(
        GetManagedEventCustomPropertiesRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventCustomPropertyDefinitionListDto>
            .NormalizeParameters(request.PageNumber, request.PageSize);
        var graph = await reader.ReadAsync(
            new(PrivateEventManagementReadKind.CustomProperties, request.EventId), cancellationToken);
        var definitions = graph.Definitions.OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.DisplayName)
            .Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize)
            .Select(CustomPropertyMapper.ToListItem).ToList();
        var page = PaginatedResult<EventCustomPropertyDefinitionListDto>.Create(
            definitions, graph.Definitions.Count, pageNumber, pageSize);
        var values = graph.Values.OrderBy(value => value.Ordinal).Select(CustomPropertyMapper.ToValue).ToList();
        return new(page, values);
    }
}
