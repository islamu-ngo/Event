using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

public sealed record GetEventSessionListRequest : IRequest<PaginatedResult<EventSessionListDto>>
{
    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    // ===== Custom property projection filters (Layer 3 — tenant-gated) =====

    private IReadOnlyList<CustomPropertyFilterCriterion>? _customPropertyFilters;

    public IReadOnlyList<CustomPropertyFilterCriterion>? CustomPropertyFilters
    {
        get => _customPropertyFilters;
        init => _customPropertyFilters = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    public string? CustomPropertySearchTerm { get; init; }
}
