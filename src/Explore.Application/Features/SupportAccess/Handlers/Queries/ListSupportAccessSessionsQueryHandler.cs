using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Handlers.Queries;

public sealed class ListSupportAccessSessionsQueryHandler(
    ISupportAccessSessionRepository sessionRepository)
    : IRequestHandler<ListSupportAccessSessionsQuery, PaginatedResult<SupportAccessSessionDto>>
{
    public async Task<PaginatedResult<SupportAccessSessionDto>> Handle(
        ListSupportAccessSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, PaginatedResult<SupportAccessSessionDto>.MaxPageSize);
        var sessions = await sessionRepository.ListForTargetTenantAsync(
            request.TargetTenantId,
            limit,
            cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        var items = sessions
            .Select(session => SupportAccessMapper.ToDto(session, nowUtc))
            .ToList();

        return PaginatedResult<SupportAccessSessionDto>.Create(items, items.Count, 1, limit);
    }
}
