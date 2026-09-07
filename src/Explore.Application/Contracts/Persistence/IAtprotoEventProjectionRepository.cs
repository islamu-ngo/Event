using Explore.Domain.Federation;

namespace Explore.Application.Contracts.Persistence;

public enum AtprotoEventTemporalFilter
{
    CurrentOrUpcoming = 1,
    Upcoming = 2,
    Ongoing = 3,
    Past = 4,
    All = 5
}

public enum AtprotoEventDiscoverySort
{
    Date = 1,
    Title = 2,
    Views = 3,
    CreatedAt = 4
}

public sealed record AtprotoEventProjectionQuery(
    int Take,
    string? SearchTerm,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    IReadOnlyCollection<string>? Modes,
    AtprotoEventTemporalFilter TemporalFilter,
    AtprotoEventDiscoverySort Sort,
    bool SortDescending,
    DateTimeOffset Now);

public interface IAtprotoEventProjectionRepository
{
    Task<(IReadOnlyList<AtprotoEventProjection> Items, int TotalCount)> GetPublicWindowAsync(
        AtprotoEventProjectionQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AtprotoEventProjection>> GetVisibleByRecordIdsAsync(
        IReadOnlyCollection<Guid> atprotoRecordIds,
        CancellationToken cancellationToken);

    Task<AtprotoEventProjection?> GetVisibleByRecordIdAsync(
        Guid atprotoRecordId,
        CancellationToken cancellationToken);
}
