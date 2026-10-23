using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.SupportAccess.Handlers.Queries;

public sealed class ListSupportAccessSessionsQueryHandler(
    ISupportAccessSessionRepository sessionRepository,
    IHumanSupportAdministrationPreparation preparation,
    ISerializableAttemptOwner owner,
    TimeProvider clock)
    : IQueryHandler<ListSupportAccessSessionsQuery, PaginatedResult<SupportAccessSessionDto>>
{
    public async Task<PaginatedResult<SupportAccessSessionDto>> QueryAsync(
        ListSupportAccessSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        return await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            return await QueryBodyAsync(request, prepared, token);
        }, cancellationToken);
    }

    private async Task<PaginatedResult<SupportAccessSessionDto>> QueryBodyAsync(
        ListSupportAccessSessionsQuery request, IPreparedHumanSupportAdministration prepared, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, PaginatedResult<SupportAccessSessionDto>.MaxPageSize);
        var sessions = prepared.NativeData is { } data
            ? await data.ListSessionsAsync(cancellationToken) : await sessionRepository.ListForTargetTenantAsync(
            request.TargetTenantId,
            limit,
            cancellationToken);
        var nowUtc = clock.GetUtcNow();
        var items = sessions
            .Select(session => SupportAccessMapper.ToDto(session, nowUtc))
            .ToList();

        return PaginatedResult<SupportAccessSessionDto>.Create(items, items.Count, 1, limit);
    }
}
