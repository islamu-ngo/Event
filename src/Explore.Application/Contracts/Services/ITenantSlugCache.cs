namespace Explore.Application.Contracts.Services;

public interface ITenantSlugCache
{
    Task WarmAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    ValueTask<Guid?> GetTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default);

    ValueTask<Guid?> GetTenantIdByDomainAsync(string domain, CancellationToken cancellationToken = default);
}
