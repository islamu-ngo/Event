using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class AtprotoFederationSettingsPersistenceTests
{
    private const string EventsEnabled = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
    private const string BackfillEnabled = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled;
    private const string BackfillMode = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode;
    private static readonly DateTime CreatedAt = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task DedicatedRowsRoundTripAllThreeKeysAndPreserveAcceptedModeCasing()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext(fixture.FirstTenantId);
        foreach (var (key, value) in new[] { (EventsEnabled, "true"), (BackfillEnabled, "false"), (BackfillMode, "\"FuLl\"") })
        {
            context.Add(AtprotoFederationInstanceSetting.Create(key, value, true, CreatedAt, null));
            context.Add(AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, key, value, false, CreatedAt, null));
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var instanceRows = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking().ToArrayAsync();
        var tenantRows = await context.Set<AtprotoFederationTenantSetting>().AsNoTracking().ToArrayAsync();
        await Assert.That(instanceRows.Length).IsEqualTo(3);
        await Assert.That(tenantRows.Length).IsEqualTo(3);
        await Assert.That(instanceRows.Single(row => row.SettingKey == EventsEnabled).Value).IsEqualTo("true");
        await Assert.That(tenantRows.Single(row => row.SettingKey == BackfillEnabled).Value).IsEqualTo("false");
        await Assert.That(instanceRows.Single(row => row.SettingKey == BackfillMode).Value).IsEqualTo("\"FuLl\"");
        await Assert.That(tenantRows.Single(row => row.SettingKey == BackfillMode).Value).IsEqualTo("\"FuLl\"");
        await Assert.That(instanceRows.All(row => row.IsLocked)).IsTrue();
        await Assert.That(tenantRows.All(row => !row.IsLocked)).IsTrue();
    }

    [Test]
    public async Task InstanceKeyAndSelectedTenantKeyEachHaveOneAuthoritativeRow()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext(fixture.FirstTenantId);
        context.Add(AtprotoFederationInstanceSetting.Create(EventsEnabled, "false", true, CreatedAt, null));
        context.Add(AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, EventsEnabled, "false", false, CreatedAt, null));
        await context.SaveChangesAsync();

        var duplicateInstance = AtprotoFederationInstanceSetting.Create(EventsEnabled, "true", false, CreatedAt, null);
        context.Add(duplicateInstance);
        await RequireSqliteConstraintAsync(context, 2067);
        context.Entry(duplicateInstance).State = EntityState.Detached;

        var duplicateTenant = AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, EventsEnabled, "true", true, CreatedAt, null);
        context.Add(duplicateTenant);
        await RequireSqliteConstraintAsync(context, 2067);
    }

    [Test]
    public async Task SameKeyBelongsToIndependentTenantsAndAbsentTenantAuthoritySeesNoOverride()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using (var first = fixture.CreateContext(fixture.FirstTenantId))
        {
            first.Add(AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, EventsEnabled, "true", false, CreatedAt, null));
            await first.SaveChangesAsync();
        }
        await using (var second = fixture.CreateContext(fixture.SecondTenantId))
        {
            second.Add(AtprotoFederationTenantSetting.Create(fixture.SecondTenantId, EventsEnabled, "false", false, CreatedAt, null));
            await second.SaveChangesAsync();
        }

        await using var firstRead = fixture.CreateContext(fixture.FirstTenantId);
        await using var secondRead = fixture.CreateContext(fixture.SecondTenantId);
        await using var noTenantRead = fixture.CreateContext(null);
        var firstRow = await firstRead.Set<AtprotoFederationTenantSetting>().AsNoTracking().SingleAsync();
        var secondRow = await secondRead.Set<AtprotoFederationTenantSetting>().AsNoTracking().SingleAsync();
        await Assert.That(firstRow.TenantId).IsEqualTo(fixture.FirstTenantId);
        await Assert.That(firstRow.Value).IsEqualTo("true");
        await Assert.That(secondRow.TenantId).IsEqualTo(fixture.SecondTenantId);
        await Assert.That(secondRow.Value).IsEqualTo("false");
        await Assert.That(await noTenantRead.Set<AtprotoFederationTenantSetting>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task StorageRejectsMalformedValuesEvenWhenTrackedStateBypassesTheDomainFactory()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext(fixture.FirstTenantId);
        foreach (var (key, invalidValue) in new[]
        {
            (EventsEnabled, "\"true\""), (EventsEnabled, "true "), (BackfillEnabled, "TRUE"),
            (BackfillEnabled, "null"), (BackfillEnabled, "{\"secretRef\":\"federation/policy\"}"),
            (BackfillMode, "true"), (BackfillMode, "\"incremental\""), (BackfillMode, "\"full\" "),
            (BackfillMode, "\"\\u0066ull\""), (BackfillMode, "\"full\" true")
        })
        {
            string validValue = key == BackfillMode ? "\"full\"" : "false";
            var instance = AtprotoFederationInstanceSetting.Create(key, validValue, true, CreatedAt, null);
            context.Add(instance);
            context.Entry(instance).Property(row => row.Value).CurrentValue = invalidValue;
            await RequireSqliteConstraintAsync(context, 275, "ck_atproto_federation_instance_settings_value");
            context.Entry(instance).State = EntityState.Detached;

            var tenant = AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, key, validValue, false, CreatedAt, null);
            context.Add(tenant);
            context.Entry(tenant).Property(row => row.Value).CurrentValue = invalidValue;
            await RequireSqliteConstraintAsync(context, 275, "ck_atproto_federation_tenant_settings_value");
            context.Entry(tenant).State = EntityState.Detached;
        }
    }

    [Test]
    public async Task StorageRejectsUnknownUnmovedAndPaddedKeysEvenWhenTrackedStateIsForged()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext(fixture.FirstTenantId);
        foreach (var invalidKey in new[]
        {
            "federation.unknown", EventsEnabled + " ", "Federation.atproto_events_enabled",
            GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
            GovernanceSettingKeys.Federation.AtprotoPublishMyEvents
        })
        {
            var instance = AtprotoFederationInstanceSetting.Create(EventsEnabled, "false", true, CreatedAt, null);
            context.Add(instance);
            context.Entry(instance).Property(row => row.SettingKey).CurrentValue = invalidKey;
            await RequireSqliteConstraintAsync(context, 275);
            context.Entry(instance).State = EntityState.Detached;

            var tenant = AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, EventsEnabled, "false", false, CreatedAt, null);
            context.Add(tenant);
            context.Entry(tenant).Property(row => row.SettingKey).CurrentValue = invalidKey;
            await RequireSqliteConstraintAsync(context, 275);
            context.Entry(tenant).State = EntityState.Detached;
        }
    }

    [Test]
    public async Task GenericSettingsTablesCannotReintroduceAnyMovedKey()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext(fixture.FirstTenantId);
        foreach (var key in new[] { EventsEnabled, BackfillEnabled, BackfillMode })
        {
            var instance = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = key == BackfillMode ? "\"full\"" : "true",
                SettingValueTypeId = (int)(key == BackfillMode ? SettingValueType.String : SettingValueType.Boolean),
                CreatedAt = CreatedAt
            };
            context.Add(instance);
            await RequireSqliteConstraintAsync(context, 275);
            context.Entry(instance).State = EntityState.Detached;

            var tenant = new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.FirstTenantId,
                Tenant = null!,
                SettingKey = key,
                Value = key == BackfillMode ? "\"full\"" : "true",
                CreatedAt = CreatedAt
            };
            context.Add(tenant);
            await RequireSqliteConstraintAsync(context, 275);
            context.Entry(tenant).State = EntityState.Detached;
        }
    }

    [Test]
    public async Task RemovingTheTenantOverrideLeavesTheInstanceRootValueIntact()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var context = fixture.CreateContext(fixture.FirstTenantId);
        var root = AtprotoFederationInstanceSetting.Create(EventsEnabled, "false", true, CreatedAt, null);
        var tenant = AtprotoFederationTenantSetting.Create(fixture.FirstTenantId, EventsEnabled, "true", false, CreatedAt, null);
        context.AddRange(root, tenant);
        await context.SaveChangesAsync();
        context.Remove(tenant);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Assert.That(await context.Set<AtprotoFederationTenantSetting>().CountAsync()).IsEqualTo(0);
        var persistedRoot = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking().SingleAsync();
        await Assert.That(persistedRoot.Value).IsEqualTo("false");
        await Assert.That(persistedRoot.IsLocked).IsTrue();
    }

    private static async Task RequireSqliteConstraintAsync(ExploreDbContext context, int extendedCode, string? constraint = null)
    {
        var failure = await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
        var sqlite = failure!.InnerException as SqliteException;
        await Assert.That(sqlite).IsNotNull();
        await Assert.That(sqlite!.SqliteErrorCode).IsEqualTo(19);
        await Assert.That(sqlite.SqliteExtendedErrorCode).IsEqualTo(extendedCode);
        if (constraint is not null)
            await Assert.That(sqlite.Message).Contains(constraint);
    }

    private sealed class DatabaseFixture(SqliteConnection connection) : IAsyncDisposable
    {
        internal Guid FirstTenantId { get; } = Guid.CreateVersion7();
        internal Guid SecondTenantId { get; } = Guid.CreateVersion7();

        internal ExploreDbContext CreateContext(Guid? tenantId)
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            var context = new ExploreDbContext(options)
            {
                TenantContext = tenantId is { } id ? new SelectedTenant(id) : null
            };
            context.BindTenantLease();
            return context;
        }

        internal static async Task<DatabaseFixture> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var fixture = new DatabaseFixture(connection);
            try
            {
                await using var context = fixture.CreateContext(fixture.FirstTenantId);
                await context.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
                var status = new TenantStatus
                {
                    Id = (int)TenantStatusEnum.Active,
                    MasterCode = "ACTIVE",
                    FullName = "Active",
                    IsActiveState = true
                };
                context.Add(status);
                context.AddRange(
                    new SettingValueTypeLookup { Id = (int)SettingValueType.String, MasterCode = "STRING", FullName = "String" },
                    new SettingValueTypeLookup { Id = (int)SettingValueType.Boolean, MasterCode = "BOOLEAN", FullName = "Boolean" });
                foreach (var tenantId in new[] { fixture.FirstTenantId, fixture.SecondTenantId })
                    context.Add(new Tenant
                    {
                        Id = tenantId,
                        FullName = "Federation policy fixture",
                        Slug = $"federation-{tenantId:N}",
                        TenantStatusId = status.Id,
                        TenantStatus = status,
                        CreatedAt = CreatedAt
                    });
                await context.SaveChangesAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
