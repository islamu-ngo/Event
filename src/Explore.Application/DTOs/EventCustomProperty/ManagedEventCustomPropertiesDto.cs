using Explore.Application.Responses;

namespace Explore.Application.DTOs.EventCustomProperty;

public sealed record ManagedEventCustomPropertiesDto(
    PaginatedResult<EventCustomPropertyDefinitionListDto> Definitions,
    IReadOnlyList<EventCustomPropertyValueDto> Values);
