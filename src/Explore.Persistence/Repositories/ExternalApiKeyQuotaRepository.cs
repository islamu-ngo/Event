using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class ExternalApiKeyQuotaRepository : GenericRepository<ExternalApiKeyQuota, Guid>, IExternalApiKeyQuotaRepository
{
    private readonly ExploreDbContext _dbContext;

    public ExternalApiKeyQuotaRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ExternalApiKeyQuota?> GetCurrentPeriodQuota(Guid externalApiKeyId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return await _dbContext.ExternalApiKeyQuotas
            .AsNoTracking()
            .Where(q => q.ExternalApiKeyId == externalApiKeyId
                        && q.PeriodStart <= today
                        && q.PeriodEnd >= today)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ExternalApiKeyQuota> LazyProvisionPeriod(
        Guid externalApiKeyId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int creditLimit,
        int rolloverCredits,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await FindOrCreatePeriodAsync(
                externalApiKeyId,
                periodStart,
                periodEnd,
                creditLimit,
                rolloverCredits,
                cancellationToken);
        }

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            await using IAsyncDisposable provisionLease = await AcquireProvisionTransactionLeaseAsync(
                externalApiKeyId,
                periodStart,
                cancellationToken);
            return await FindOrCreatePeriodAsync(
                externalApiKeyId,
                periodStart,
                periodEnd,
                creditLimit,
                rolloverCredits,
                cancellationToken);
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using IAsyncDisposable provisionLease = await RelationalNamedLock.AcquireSessionAsync(
                _dbContext,
                $"external-api-key-quota:{externalApiKeyId:N}:{periodStart:yyyyMMdd}",
                cancellationToken);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var quota = await FindOrCreatePeriodAsync(
                externalApiKeyId,
                periodStart,
                periodEnd,
                creditLimit,
                rolloverCredits,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return quota;
        });
    }

    public async Task<bool> TryConsumeCredits(Guid quotaId, int amount, CancellationToken cancellationToken = default)
    {
        // Atomic conditional UPDATE — race-safe credit enforcement + request counting.
        var rowsAffected = await _dbContext.ExternalApiKeyQuotas
            .Where(quota => quota.Id == quotaId
                            && quota.CreditsUsed + amount <= quota.CreditLimit + quota.RolloverCredits)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(quota => quota.CreditsUsed, quota => quota.CreditsUsed + amount)
                .SetProperty(quota => quota.RequestCount, quota => quota.RequestCount + 1)
                .SetProperty(quota => quota.UpdatedAt, DateTime.UtcNow), cancellationToken);

        return rowsAffected > 0;
    }

    public async Task IncrementRequestCount(Guid quotaId, CancellationToken cancellationToken = default)
    {
        // Atomic request count increment without credit consumption — for unlimited keys.
        await _dbContext.ExternalApiKeyQuotas
            .Where(quota => quota.Id == quotaId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(quota => quota.RequestCount, quota => quota.RequestCount + 1)
                .SetProperty(quota => quota.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalApiKeyQuota>> GetQuotaHistory(Guid externalApiKeyId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalApiKeyQuotas
            .AsNoTracking()
            .Where(q => q.ExternalApiKeyId == externalApiKeyId)
            .OrderByDescending(q => q.PeriodStart)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantApiKeyUsageSummary>> GetUsageByTenant(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalApiKeyQuotas
            .AsNoTracking()
            .Include(q => q.ExternalApiKey)
            .Where(q => q.ExternalApiKey.TenantId == tenantId
                        && q.PeriodStart >= from
                        && q.PeriodEnd <= to)
            .GroupBy(q => new
            {
                q.ExternalApiKeyId,
                q.ExternalApiKey.Name,
                q.ExternalApiKey.TenantId,
                q.ExternalApiKey.ExternalApiKeyOwnerTypeId,
                q.ExternalApiKey.OwnerId,
                q.ExternalApiKey.CreditLimit
            })
            .Select(g => new TenantApiKeyUsageSummary(
                g.Key.ExternalApiKeyId,
                g.Key.Name,
                g.Key.TenantId,
                g.Key.ExternalApiKeyOwnerTypeId,
                g.Key.OwnerId,
                g.Sum(q => q.RequestCount),
                g.Sum(q => q.CreditsUsed),
                g.Key.CreditLimit ?? 0))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantApiKeyUsageSummary>> GetUsagePlatformWide(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalApiKeyQuotas
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformUsageReport)
            .AsNoTracking()
            .Include(q => q.ExternalApiKey)
            .Where(q => q.PeriodStart >= from && q.PeriodEnd <= to)
            .GroupBy(q => new
            {
                q.ExternalApiKeyId,
                q.ExternalApiKey.Name,
                q.ExternalApiKey.TenantId,
                q.ExternalApiKey.ExternalApiKeyOwnerTypeId,
                q.ExternalApiKey.OwnerId,
                q.ExternalApiKey.CreditLimit
            })
            .Select(g => new TenantApiKeyUsageSummary(
                g.Key.ExternalApiKeyId,
                g.Key.Name,
                g.Key.TenantId,
                g.Key.ExternalApiKeyOwnerTypeId,
                g.Key.OwnerId,
                g.Sum(q => q.RequestCount),
                g.Sum(q => q.CreditsUsed),
                g.Key.CreditLimit ?? 0))
            .ToListAsync(cancellationToken);
    }

    private Task<IAsyncDisposable> AcquireProvisionTransactionLeaseAsync(
        Guid externalApiKeyId,
        DateOnly periodStart,
        CancellationToken cancellationToken) =>
        RelationalNamedLock.AcquireTransactionAsync(
            _dbContext,
            $"external-api-key-quota:{externalApiKeyId:N}:{periodStart:yyyyMMdd}",
            cancellationToken);

    private async Task<ExternalApiKeyQuota> FindOrCreatePeriodAsync(
        Guid externalApiKeyId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int creditLimit,
        int rolloverCredits,
        CancellationToken cancellationToken)
    {
        var quota = await _dbContext.ExternalApiKeyQuotas.SingleOrDefaultAsync(
            current => current.ExternalApiKeyId == externalApiKeyId && current.PeriodStart == periodStart,
            cancellationToken);
        if (quota is not null)
        {
            return quota;
        }

        quota = new ExternalApiKeyQuota
        {
            Id = Guid.CreateVersion7(),
            ExternalApiKeyId = externalApiKeyId,
            ExternalApiKey = null!,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CreditLimit = creditLimit,
            RolloverCredits = rolloverCredits,
            CreatedAt = DateTime.UtcNow,
        };
        await _dbContext.ExternalApiKeyQuotas.AddAsync(quota, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return quota;
    }
}
