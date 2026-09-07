using System.Collections.Concurrent;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

public class TenantSlugCache : ITenantSlugCache, IDisposable
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private ConcurrentDictionary<string, Guid> _slugToTenantId = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, Guid> _domainToTenantId = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _isWarm;

    public TenantSlugCache(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public Task WarmAsync(CancellationToken cancellationToken = default)
    {
        return EnsureWarmAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return LoadAsync(forceRefresh: true, cancellationToken);
    }

    public async ValueTask<Guid?> GetTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await EnsureWarmAsync(cancellationToken);

        var tenantId = TryGetValue(_slugToTenantId, slug);
        if (tenantId.HasValue)
        {
            return tenantId;
        }

        await LoadAsync(forceRefresh: true, cancellationToken);

        return TryGetValue(_slugToTenantId, slug);
    }

    public async ValueTask<Guid?> GetTenantIdByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        await EnsureWarmAsync(cancellationToken);

        var tenantId = TryGetValue(_domainToTenantId, domain);
        if (tenantId.HasValue)
        {
            return tenantId;
        }

        await LoadAsync(forceRefresh: true, cancellationToken);

        return TryGetValue(_domainToTenantId, domain);
    }

    private async Task EnsureWarmAsync(CancellationToken cancellationToken)
    {
        if (_isWarm)
        {
            return;
        }

        await LoadAsync(forceRefresh: false, cancellationToken);
    }

    private async Task LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (_isWarm && !forceRefresh)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_isWarm && !forceRefresh)
            {
                return;
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var tenantLookupSource = scope.ServiceProvider.GetRequiredService<ITenantLookupSource>();
            var tenantLookups = await tenantLookupSource.GetTenantLookupsAsync(cancellationToken);

            var slugMap = new ConcurrentDictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var domainMap = new ConcurrentDictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var tenantLookup in tenantLookups)
            {
                AddLookup(slugMap, tenantLookup.Slug, tenantLookup.TenantId);
                AddLookup(domainMap, tenantLookup.Subdomain, tenantLookup.TenantId);
                AddLookup(domainMap, tenantLookup.CustomDomain, tenantLookup.TenantId);
            }

            _slugToTenantId = slugMap;
            _domainToTenantId = domainMap;
            _isWarm = true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static Guid? TryGetValue(ConcurrentDictionary<string, Guid> lookups, string key)
    {
        var normalizedKey = NormalizeKey(key);
        if (normalizedKey == null)
        {
            return null;
        }

        return lookups.TryGetValue(normalizedKey, out var tenantId) ? tenantId : null;
    }

    private static void AddLookup(ConcurrentDictionary<string, Guid> lookups, string? key, Guid tenantId)
    {
        var normalizedKey = NormalizeKey(key);
        if (normalizedKey == null || tenantId == Guid.Empty)
        {
            return;
        }

        lookups[normalizedKey] = tenantId;
    }

    private static string? NormalizeKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? null
            : key.Trim().TrimEnd('.').ToLowerInvariant();
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}
