// ABOUTME: File-backed SQLite regression for portable external API key quota mutations.
// ABOUTME: Proves concurrent provisioners create one period row and credit use remains bounded.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("ExternalApiKeyQuotaSqlite")]
public sealed class ExternalApiKeyQuotaRepositorySqliteTests
{
    [Test]
    public async Task ConcurrentProvisionAndCreditConsumption_CreateOneQuotaAndRespectCreditLimit()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"external-api-key-quota-{Guid.CreateVersion7():N}.db");
        var periodStart = new DateOnly(2026, 8, 1);

        try
        {
            (Guid externalApiKeyId, Guid tenantId) = await CreateDatabaseAndKeyAsync(databasePath);
            ExternalApiKeyQuota[] provisioned = await ProvisionConcurrentlyAsync(
                databasePath,
                externalApiKeyId,
                tenantId,
                periodStart);
            bool[] consumption = await ConsumeConcurrentlyAsync(databasePath, tenantId, provisioned[0].Id);

            await Assert.That(provisioned.Select(quota => quota.Id).Distinct()).HasSingleItem();
            await Assert.That(consumption.Count(consumed => consumed)).IsEqualTo(3);

            await using ExploreDbContext assertContext = CreateContext(databasePath, tenantId);
            ExternalApiKeyQuota[] quotas = await assertContext.ExternalApiKeyQuotas
                .AsNoTracking()
                .Where(quota => quota.ExternalApiKeyId == externalApiKeyId && quota.PeriodStart == periodStart)
                .ToArrayAsync();

            await Assert.That(quotas).HasSingleItem();
            await Assert.That(quotas[0].CreditsUsed).IsEqualTo(3);
            await Assert.That(quotas[0].RequestCount).IsEqualTo(3);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static async Task<(Guid ExternalApiKeyId, Guid TenantId)> CreateDatabaseAndKeyAsync(string databasePath)
    {
        await using ExploreDbContext context = CreateContext(databasePath);
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);

        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "SQLite quota tenant",
            Slug = $"sqlite-quota-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        var apiKey = new ExternalApiKey
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Name = "SQLite quota key",
            KeyId = $"sqlite-quota-{Guid.CreateVersion7():N}",
            SecretHash = "sqlite-quota-hash",
            Scopes = "events:read",
            OwnerType = ExternalApiKeyOwnerType.Tenant,
            OwnerId = Guid.CreateVersion7(),
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.Monthly,
            ExternalApiKeyCreditPeriod = null!,
            CreditLimit = 3,
            CreatedAt = DateTime.UtcNow,
        };
        context.ExternalApiKeys.Add(apiKey);
        await context.SaveChangesAsync();
        return (apiKey.Id, tenant.Id);
    }

    private static async Task<ExternalApiKeyQuota[]> ProvisionConcurrentlyAsync(
        string databasePath,
        Guid externalApiKeyId,
        Guid tenantId,
        DateOnly periodStart)
    {
        ExploreDbContext[] contexts = Enumerable.Range(0, 4).Select(_ => CreateContext(databasePath, tenantId)).ToArray();
        try
        {
            foreach (ExploreDbContext context in contexts)
            {
                await context.Database.OpenConnectionAsync();
                await Assert.That(await context.ExternalApiKeys.AnyAsync(key => key.Id == externalApiKeyId)).IsTrue();
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ExternalApiKeyQuota>[] attempts = contexts.Select(async context =>
            {
                await start.Task;
                return await new ExternalApiKeyQuotaRepository(context).LazyProvisionPeriod(
                    externalApiKeyId,
                    periodStart,
                    periodStart.AddMonths(1).AddDays(-1),
                    creditLimit: 3,
                    rolloverCredits: 0,
                    deadline.Token);
            }).ToArray();

            start.SetResult();
            return await Task.WhenAll(attempts);
        }
        finally
        {
            foreach (ExploreDbContext context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    private static async Task<bool[]> ConsumeConcurrentlyAsync(string databasePath, Guid tenantId, Guid quotaId)
    {
        ExploreDbContext[] contexts = Enumerable.Range(0, 8).Select(_ => CreateContext(databasePath, tenantId)).ToArray();
        try
        {
            foreach (ExploreDbContext context in contexts)
            {
                await context.Database.OpenConnectionAsync();
                await Assert.That(await context.Set<ExternalApiKeyQuota>().AnyAsync(quota => quota.Id == quotaId)).IsTrue();
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool>[] attempts = contexts.Select(async context =>
            {
                await start.Task;
                return await new ExternalApiKeyQuotaRepository(context).TryConsumeCredits(quotaId, 1, deadline.Token);
            }).ToArray();

            start.SetResult();
            return await Task.WhenAll(attempts);
        }
        finally
        {
            foreach (ExploreDbContext context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    private static ExploreDbContext CreateContext(string databasePath, Guid? tenantId = null)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();

        var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
        context.TenantContext = tenantId is null ? null : new TestTenantContext(tenantId.Value);
        return context;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
