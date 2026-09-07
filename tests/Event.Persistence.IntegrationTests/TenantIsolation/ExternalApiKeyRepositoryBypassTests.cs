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
public class ExternalApiKeyRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CredentialBypasses_WithAmbientTenant_ReturnOnlyExplicitApiKeyPredicates()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("api-key-a");
        var tenantB = CreateTenant("api-key-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var platformOwnerId = Guid.CreateVersion7();
        var otherPlatformOwnerId = Guid.CreateVersion7();
        var tenantAKey = CreateApiKey(
            "Tenant A Key",
            "tenant-a",
            tenantA.Id,
            ExternalApiKeyOwnerType.Tenant,
            tenantA.Id);
        var tenantBKey = CreateApiKey(
            "Tenant B Key",
            "tenant-b",
            tenantB.Id,
            ExternalApiKeyOwnerType.Tenant,
            tenantB.Id);
        var platformKey = CreateApiKey(
            "Platform Key",
            "platform",
            tenantId: null,
            ExternalApiKeyOwnerType.InstanceAdmin,
            platformOwnerId);
        var otherPlatformKey = CreateApiKey(
            "Other Platform Key",
            "other-platform",
            tenantId: null,
            ExternalApiKeyOwnerType.InstanceAdmin,
            otherPlatformOwnerId);
        seedContext.ExternalApiKeys.AddRange(tenantAKey, tenantBKey, platformKey, otherPlatformKey);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.ExternalApiKeys
            .AsNoTracking()
            .Select(apiKey => apiKey.Id)
            .ToListAsync();

        var repository = new ExternalApiKeyRepository(tenantBContext);
        var tenantAAuthenticationKey = await repository.GetByKeyIdForAuthentication(tenantAKey.KeyId);
        var usageTouched = await repository.TouchUsageMetadata(
            tenantAKey.Id,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            "203.0.113.10",
            TimeSpan.Zero);
        var platformKeyById = await repository.GetByIdIgnoringTenantFilter(platformKey.Id);
        var platformKeys = await repository.GetByOwnerIgnoringTenantFilter(
            ExternalApiKeyOwnerType.InstanceAdmin,
            platformOwnerId);
        var platformNameExists = await repository.ExistsByOwnerAndNameIgnoringTenantFilter(
            ExternalApiKeyOwnerType.InstanceAdmin,
            platformOwnerId,
            platformKey.Name);
        var tenantNameDoesNotMatchPlatformOwner = await repository.ExistsByOwnerAndNameIgnoringTenantFilter(
            ExternalApiKeyOwnerType.InstanceAdmin,
            platformOwnerId,
            tenantAKey.Name);

        await using var verifyContext = fixture.CreateDbContext();
        var reloadedKeys = await verifyContext.ExternalApiKeys
            .AsNoTracking()
            .Where(apiKey => apiKey.Id == tenantAKey.Id || apiKey.Id == tenantBKey.Id)
            .ToDictionaryAsync(apiKey => apiKey.Id);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBKey.Id]);

        await Assert.That(tenantAAuthenticationKey).IsNotNull();
        await Assert.That(tenantAAuthenticationKey!.Id).IsEqualTo(tenantAKey.Id);
        await Assert.That(tenantAAuthenticationKey.TenantId).IsEqualTo(tenantA.Id);

        await Assert.That(usageTouched).IsTrue();
        await Assert.That(reloadedKeys[tenantAKey.Id].LastUsedAt)
            .IsEqualTo(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        await Assert.That(reloadedKeys[tenantAKey.Id].LastUsedIp).IsEqualTo("203.0.113.10");
        await Assert.That(reloadedKeys[tenantBKey.Id].LastUsedAt).IsNull();

        await Assert.That(platformKeyById).IsNotNull();
        await Assert.That(platformKeyById!.Id).IsEqualTo(platformKey.Id);
        await Assert.That(platformKeyById.TenantId).IsNull();

        await Assert.That(platformKeys.Select(apiKey => apiKey.Id)).IsEquivalentTo([platformKey.Id]);
        await Assert.That(platformNameExists).IsTrue();
        await Assert.That(tenantNameDoesNotMatchPlatformOwner).IsFalse();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"External API Key {slugPrefix}",
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
        Guid ownerId)
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
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!,
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
