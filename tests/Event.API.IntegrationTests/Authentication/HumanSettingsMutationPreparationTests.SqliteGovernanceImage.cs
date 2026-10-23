using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    /// <summary>
    /// Verifies provider encoding and row identity at the typed data-image boundary.
    /// The factory-controlled original is input, not a human admission proof; the
    /// authenticated governance controls exercise the real issuer and completion path.
    /// </summary>
    [Test]
    public async Task SqliteGovernanceImageRetainsAndUpdatesAnExistingDefaultTenantCapability()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"typed-governance-{Guid.CreateVersion7():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(token);
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance)
            .Options;
        var created = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        Guid actorId = Guid.CreateVersion7();
        var status = new TenantStatus
        {
            Id = (int)TenantStatusEnum.Active,
            MasterCode = "ACTIVE",
            FullName = "Active",
            IsActiveState = true
        };
        Tenant Tenant(Guid id, string slug) => new()
        {
            Id = id,
            FullName = slug,
            Slug = slug,
            TenantStatusId = status.Id,
            TenantStatus = status,
            CreatedAt = created
        };
        var defaultTenant = Tenant(PlatformDefaults.DefaultTenantId, "governance-default");
        var foreignTenant = Tenant(Guid.CreateVersion7(), "governance-foreign");
        var module = new ModuleDefinition
        {
            Id = Guid.CreateVersion7(),
            ModuleKey = "Mod_Tech",
            Name = "Technology",
            IsActive = true,
            CreatedAt = created
        };
        TenantCapability Capability(Tenant tenant, string configuration) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ModuleId = module.Id,
            Module = module,
            IsEnabled = false,
            ConfigurationJson = configuration,
            EnabledAt = created,
            CreatedAt = created
        };
        var originalCapability = Capability(defaultTenant, "{\"retained\":true}");
        var foreignCapability = Capability(foreignTenant, "{\"foreign\":true}");
        await using (var seed = new ExploreDbContext(options))
        {
            seed.BindTenantLease();
            await seed.Database.EnsureCreatedAsync(token);
            await SqliteDatabaseInitializer.InitializeAsync(seed, token);
            seed.AddRange(originalCapability, foreignCapability);
            await seed.SaveChangesAsync(token);
        }

        var original = new TypedSettingsOriginal(new UpdateModuleSettingsCommand
        {
            UserId = actorId,
            Patch = new() { EnableTechModule = OptionalUpdate<bool>.Set(true) }
        });
        await using (var owner = new ExploreDbContext(options))
        {
            owner.BindTenantLease();
            await owner.Database.OpenConnectionAsync(token);
            await using var transaction = await owner.Database.BeginTransactionAsync(token);
            var image = JsonNode.Parse(await MixedSettingsDataImage.ReadTypedSqliteAsync(owner, original, token))!.AsObject();
            var capabilities = image[nameof(TenantCapability)]!.AsArray();
            await Assert.That(capabilities.Count).IsEqualTo(1);
            var before = capabilities[0]!.AsObject();
            await Assert.That(Guid.Parse(before[nameof(TenantCapability.Id)]!.GetValue<string>()))
                .IsEqualTo(originalCapability.Id);
            await Assert.That(Guid.Parse(before[nameof(TenantCapability.TenantId)]!.GetValue<string>()))
                .IsEqualTo(defaultTenant.Id);
            var after = before.DeepClone().AsObject();
            after[nameof(TenantCapability.IsEnabled)] = true;
            await TypedSettingsSqliteWriter.WriteAsync(owner, transaction.GetDbTransaction(),
                typeof(TenantCapability), originalCapability.Id, before, after, token);
            await transaction.CommitAsync(token);
        }

        await using var defaultObserver = new ExploreDbContext(options)
        {
            TenantContext = new SqliteGovernanceTenant(defaultTenant.Id)
        };
        defaultObserver.BindTenantLease();
        var defaultRows = await defaultObserver.Set<TenantCapability>().AsNoTracking()
            .Where(row => row.ModuleId == module.Id).ToArrayAsync(token);
        await Assert.That(defaultRows.Length).IsEqualTo(1);
        var updated = defaultRows.Single();
        await Assert.That(updated.TenantId).IsEqualTo(defaultTenant.Id);
        await Assert.That(updated.Id).IsEqualTo(originalCapability.Id);
        await Assert.That(updated.IsEnabled).IsTrue();
        await Assert.That(updated.ConfigurationJson).IsEqualTo("{\"retained\":true}");
        await using var foreignObserver = new ExploreDbContext(options)
        {
            TenantContext = new SqliteGovernanceTenant(foreignTenant.Id)
        };
        foreignObserver.BindTenantLease();
        var foreignRows = await foreignObserver.Set<TenantCapability>().AsNoTracking()
            .Where(row => row.ModuleId == module.Id).ToArrayAsync(token);
        await Assert.That(foreignRows.Length).IsEqualTo(1);
        var untouched = foreignRows.Single();
        await Assert.That(untouched.TenantId).IsEqualTo(foreignTenant.Id);
        await Assert.That(untouched.Id).IsEqualTo(foreignCapability.Id);
        await Assert.That(untouched.IsEnabled).IsFalse();
        await Assert.That(untouched.ConfigurationJson).IsEqualTo("{\"foreign\":true}");
    }

    private sealed record SqliteGovernanceTenant(Guid TenantId) : ITenantContext;
}
