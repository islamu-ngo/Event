using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class ExternalApiKeyQuotaRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetUsagePlatformWide_WithAmbientTenant_ReturnsOnlyRequestedPeriodUsageAcrossKeys()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("quota-a");
        var tenantB = CreateTenant("quota-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var tenantAKey = CreateApiKey(
            "Tenant A Quota Key",
            "tenant-a-quota",
            tenantA.Id,
            ExternalApiKeyOwnerType.Tenant,
            tenantA.Id,
            creditLimit: 100);
        var tenantBKey = CreateApiKey(
            "Tenant B Quota Key",
            "tenant-b-quota",
            tenantB.Id,
            ExternalApiKeyOwnerType.Tenant,
            tenantB.Id,
            creditLimit: 200);
        var platformKey = CreateApiKey(
            "Platform Quota Key",
            "platform-quota",
            tenantId: null,
            ExternalApiKeyOwnerType.InstanceAdmin,
            Guid.CreateVersion7(),
            creditLimit: 500);
        seedContext.ExternalApiKeys.AddRange(tenantAKey, tenantBKey, platformKey);
        await seedContext.SaveChangesAsync();

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        var tenantAQuota = CreateQuota(tenantAKey, from, to, requestCount: 12, creditsUsed: 30);
        var tenantBQuota = CreateQuota(tenantBKey, from, to, requestCount: 20, creditsUsed: 40);
        var platformQuota = CreateQuota(platformKey, from, to, requestCount: 50, creditsUsed: 75);
        var tenantAOutOfRangeQuota = CreateQuota(
            tenantAKey,
            new DateOnly(2025, 12, 1),
            new DateOnly(2025, 12, 31),
            requestCount: 99,
            creditsUsed: 99);
        seedContext.ExternalApiKeyQuotas.AddRange(
            tenantAQuota,
            tenantBQuota,
            platformQuota,
            tenantAOutOfRangeQuota);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.ExternalApiKeyQuotas
            .Include(quota => quota.ExternalApiKey)
            .AsNoTracking()
            .Select(quota => quota.ExternalApiKeyId)
            .ToListAsync();

        var repository = new ExternalApiKeyQuotaRepository(tenantBContext);
        var tenantAUsageFromTenantBContext = await repository.GetUsageByTenant(
            tenantA.Id,
            from,
            to,
            CancellationToken.None);
        var platformUsage = await repository.GetUsagePlatformWide(from, to, CancellationToken.None);
        var usageByKey = platformUsage.ToDictionary(usage => usage.ApiKeyId);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBKey.Id]);
        await Assert.That(tenantAUsageFromTenantBContext).IsEmpty();
        await Assert.That(usageByKey.Keys).IsEquivalentTo([tenantAKey.Id, tenantBKey.Id, platformKey.Id]);

        await Assert.That(usageByKey[tenantAKey.Id].TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(usageByKey[tenantAKey.Id].TotalRequestCount).IsEqualTo(tenantAQuota.RequestCount);
        await Assert.That(usageByKey[tenantAKey.Id].TotalCreditsUsed).IsEqualTo(tenantAQuota.CreditsUsed);
        await Assert.That(usageByKey[tenantAKey.Id].CreditLimit).IsEqualTo(tenantAQuota.CreditLimit);

        await Assert.That(usageByKey[tenantBKey.Id].TenantId).IsEqualTo(tenantB.Id);
        await Assert.That(usageByKey[platformKey.Id].TenantId).IsNull();
        await Assert.That(platformUsage.Select(usage => usage.TotalRequestCount)).DoesNotContain(99);
        await Assert.That(platformUsage.Select(usage => usage.TotalCreditsUsed)).DoesNotContain(99);
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"External API Key Quota {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static ExternalApiKey CreateApiKey(
        string name,
        string keyIdPrefix,
        Guid? tenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        int creditLimit)
    {
        return new ExternalApiKey
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null,
            Name = name,
            KeyId = $"{keyIdPrefix}-{Guid.NewGuid():N}",
            SecretHash = $"hash-{keyIdPrefix}",
            Scopes = "events:read",
            OwnerType = ownerType,
            OwnerId = ownerId,
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.Monthly,
            ExternalApiKeyCreditPeriod = null!,
            CreditLimit = creditLimit,
        };
    }

    private static ExternalApiKeyQuota CreateQuota(
        ExternalApiKey apiKey,
        DateOnly periodStart,
        DateOnly periodEnd,
        long requestCount,
        int creditsUsed)
    {
        return new ExternalApiKeyQuota
        {
            Id = Guid.CreateVersion7(),
            ExternalApiKeyId = apiKey.Id,
            ExternalApiKey = apiKey,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CreditLimit = apiKey.CreditLimit ?? 0,
            CreditsUsed = creditsUsed,
            RolloverCredits = 0,
            RequestCount = requestCount,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
