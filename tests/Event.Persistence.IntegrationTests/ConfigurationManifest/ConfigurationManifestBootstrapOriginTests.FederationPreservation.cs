using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed partial class ConfigurationManifestBootstrapOriginTests
{
    [Test]
    public async Task OrdinaryBootstrapPreservesDedicatedFederationRowsAndAuditImages()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        Guid existingTenantId = Guid.CreateVersion7();
        await using (var seed = FederationManifestStorageSnapshot.OpenMigrator(fixture))
        {
            await using var human = new HumanSecuritySourceGate(seed);
            await using var humanLease = await human.AcquireAsync(token);
            await using var discovery = new EventDiscoveryEnrollmentGate(seed);
            await using var discoveryLease = await discovery.AcquireAsync(token);
            seed.Tenants.Add(new Tenant
            {
                Id = existingTenantId,
                Slug = "preserved-federation-tenant",
                FullName = "Preserved federation tenant",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await seed.SaveChangesAsync(token);
        }
        var before = await FederationManifestStorageSnapshot.SeedAsync(fixture, [existingTenantId], null, token);
        await using var host = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
        ExploreDbContext? original = null;
        object? connection = null;
        bool observed = false;
        host.BeforeLock = async (_, actual, _) =>
        {
            if (original is null)
            {
                original = actual;
                connection = actual.Database.GetDbConnection();
            }
            await Assert.That(actual).IsSameReferenceAs(original);
            await Assert.That(actual.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(actual.Database.CurrentTransaction).IsNull();
        };
        host.AfterObservation = async (actual, _, cancellationToken) =>
        {
            observed = true;
            await Assert.That(actual).IsSameReferenceAs(original);
            await Assert.That(actual.Database.GetDbConnection()).IsSameReferenceAs(connection);
            var transaction = actual.Database.CurrentTransaction;
            await Assert.That(transaction).IsNotNull();
            await Assert.That(transaction!.GetDbTransaction().IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            await Assert.That(actual.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(actual.Database.GetDbConnection()).IsSameReferenceAs(connection);
        };
        var response = await host.ExecuteAsync(new ApplyConfigurationManifestCommand(
            ConfigurationManifestApplicationTestSupport.Source("ordinary-federation-preservation")), token);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(observed).IsTrue();
        await using var verification = FederationManifestStorageSnapshot.OpenMigrator(fixture);
        await before.AssertUnchangedAsync(verification, token);
        var created = await verification.Tenants.AsNoTracking().SingleAsync(
            row => row.Slug == "ordinary-federation-preservation", token);
        await Assert.That(await verification.TenantSettingOverrides.AsNoTracking().Where(
                row => row.TenantId == created.Id
                    && row.SettingKey == PublicExperienceSettingDefinitions.EventCatalogLabel.Key)
            .Select(row => row.Value).SingleAsync(token)).IsEqualTo("\"Community Events\"");
        var operation = await verification.ConfigurationManifestOperations.AsNoTracking()
            .SingleAsync(row => row.Id == response.Id, token);
        await Assert.That(operation.Status).IsEqualTo(ConfigurationManifestOperationStatus.Applied);
        await Assert.That(operation.EffectOutboxId).IsNotNull();
        await Assert.That(await verification.OutboxMessages.AnyAsync(
            row => row.Id == operation.EffectOutboxId && row.AggregateId == operation.Id, token)).IsTrue();
    }
}

internal sealed record FederationManifestStorageSnapshot(
    FederationManifestStorageRow[] Instance, FederationManifestStorageRow[] Overrides)
{
    internal static readonly string[] Keys =
    [
        GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode
    ];

    internal static ExploreDbContext OpenMigrator(PostgreSqlContainerFixture fixture)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.EnableTenantFilterBypass("Independent Migrator observation of dedicated federation storage during ordinary manifest workflows.");
        context.BindTenantLease();
        return context;
    }

    internal static async Task<FederationManifestStorageSnapshot> SeedAsync(PostgreSqlContainerFixture fixture,
        Guid[] tenants, Guid? actor, CancellationToken token)
    {
        await using var context = OpenMigrator(fixture);
        await using var human = new HumanSecuritySourceGate(context);
        await using var humanLease = await human.AcquireAsync(token);
        await using var discovery = new EventDiscoveryEnrollmentGate(context);
        await using var discoveryLease = await discovery.AcquireAsync(token);
        DateTime createdAt = new(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        foreach (string key in Keys)
        {
            var root = await context.Set<AtprotoFederationInstanceSetting>().SingleAsync(
                row => row.SettingKey == key, token);
            root.SetValue(key == GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode ? "\"full\"" : "true",
                createdAt.AddDays(1), actor);
            root.SetLock(false, createdAt.AddDays(2), actor);
            foreach (Guid tenant in tenants)
            {
                var row = AtprotoFederationTenantSetting.Create(tenant, key,
                    key == GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode ? "\"downtime_only\"" : "false",
                    true, createdAt, actor);
                row.SetLock(false, createdAt.AddDays(3), actor);
                context.Add(row);
            }
        }
        await context.SaveChangesAsync(token);
        return await ReadAsync(context, token);
    }

    private static async Task<FederationManifestStorageSnapshot> ReadAsync(ExploreDbContext context, CancellationToken token)
    {
        var instance = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new FederationManifestStorageRow(row.Id, null, row.SettingKey, row.Value, row.IsLocked,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync(token);
        var overrides = await context.Set<AtprotoFederationTenantSetting>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceConfigurationManifestExport).AsNoTracking()
            .OrderBy(row => row.TenantId).ThenBy(row => row.SettingKey)
            .Select(row => new FederationManifestStorageRow(row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync(token);
        return new(instance, overrides);
    }

    internal async Task AssertUnchangedAsync(ExploreDbContext context, CancellationToken token)
    {
        var actual = await ReadAsync(context, token);
        await Assert.That(actual.Instance).IsEquivalentTo(Instance);
        await Assert.That(actual.Overrides).IsEquivalentTo(Overrides);
    }

    internal static async Task AssertPublicExclusionAsync(ReadOnlyMemory<byte> utf8Json)
    {
        using var exported = JsonDocument.Parse(utf8Json);
        JsonElement spec = exported.RootElement.GetProperty("spec");
        JsonElement instance = spec.GetProperty("instance").GetProperty("settings");
        foreach (string key in Keys)
        {
            await Assert.That(instance.TryGetProperty(key, out _)).IsFalse();
            foreach (JsonElement tenant in spec.GetProperty("tenants").EnumerateArray())
                await Assert.That(tenant.GetProperty("spec").GetProperty("settings").TryGetProperty(key, out _)).IsFalse();
        }
    }
}

internal sealed record FederationManifestStorageRow(Guid Id, Guid? TenantId, string Key, string Value, bool IsLocked,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
