using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresLocationPrivacyGovernanceAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string Key = GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress;

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ReviewDepthIncludesForeignActiveRowsButNotDetachedRows(bool foreignTenant, bool aclDrift)
    {
        await using var rows = await GovernanceRows.CreateAsync(fixture);
        await using (var seed = await rows.ObserverAsync())
        {
            foreach (Guid id in new[] { rows.A.Id, rows.B.Id })
            {
                EventLocation placement = await seed.EventLocations.SingleAsync(row => row.Id == id);
                seed.EventLocationDisclosureAudits.Add(placement.ApplyGovernanceTightening(
                    true, rows.Authentication.UserId, GovernanceRows.Now.AddMinutes(1)));
            }
            await seed.SaveChangesAsync();
        }
        await using var request = rows.Authentication.Context(foreignTenant ? rows.Authentication.B : null);
        await rows.Authentication.AssertOrdinaryRoleAsync(request);

        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                fixture, PostgresLocationPrivacyReviewContract.GlobalRole);
        await using (var global = await authority.Factory.CreateDbContextAsync())
        {
            PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.EventLocations.IgnoreQueryFilters().AnyAsync());
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
        }
        if (aclDrift)
        {
            await using var migrator = fixture.CreateDbContext();
            await using var outsider = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
            string function = PostgresLocationPrivacyReviewContract.FunctionSql(migrator);
            await migrator.Database.ExecuteSqlRawAsync($"GRANT EXECUTE ON FUNCTION {function}() TO \"{outsider.Role}\"");
            try
            {
                PostgresException? entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await new EventLocationPrivacyReviewReader(outsider.Factory).CountNeedingPrivacyReviewAsync(default));
                await Assert.That(entry!.SqlState).IsEqualTo("55000");
                PostgresException? reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await PostgresLocationPrivacyReviewContract.ApplyAsync(migrator));
                await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
                await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                    $"SELECT pg_catalog.has_function_privilege('{outsider.Role}', '{function}()', 'EXECUTE') AS \"Value\"")
                    .SingleAsync()).IsTrue();
            }
            finally
            {
                await migrator.Database.ExecuteSqlRawAsync($"REVOKE EXECUTE ON FUNCTION {function}() FROM \"{outsider.Role}\"");
            }
            await PostgresLocationPrivacyReviewContract.ApplyAsync(migrator);
        }
        int depth = await new EventLocationPrivacyReviewReader(authority.Factory)
            .CountNeedingPrivacyReviewAsync(default);

        await Assert.That(depth).IsEqualTo(2);
        await Assert.That(request.TenantFilterTenantId)
            .IsEqualTo(foreignTenant ? rows.Authentication.B : (Guid?)null);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeGovernanceCannotReadInstanceOrAnotherTenant(bool foreignTarget)
    {
        await using var rows = await GovernanceRows.CreateAsync(fixture);
        await using var request = rows.Authentication.Context(rows.Authentication.A);
        await using var transaction = await request.Database.BeginTransactionAsync();
        // Exercise the native function with the original restricted login. Going
        // through an unprepared application store would stop at human admission
        // and no longer prove the PostgreSQL boundary's SQLSTATE.
        await using var command = request.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            $"SELECT * FROM {PostgresLocationPrivacyGovernanceContract.OverridesFunctionSql(request)}(@key, @tenant)";
        command.Parameters.Add(new NpgsqlParameter("key", Key));
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlTypes.NpgsqlDbType.Uuid)
        { Value = foreignTarget ? rows.Authentication.B : DBNull.Value });
        PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        });
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo((Guid?)rows.Authentication.A);
        await rows.AssertUnchangedAsync();
    }

    internal sealed class GovernanceRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        private PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority? _globalAuthority;
        private string? _globalLogin;
        internal static readonly DateTime Now = new(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Authentication { get; private set; } = null!;
        internal EventLocation A { get; private set; } = null!;
        internal EventLocation B { get; private set; } = null!;
        private EventLocation _restricted = null!;
        private EventLocation _detached = null!;
        private readonly List<string> _tables = [];
        private PolicyState[] _initialPolicies = [];
        private Guid[] _initialAudits = [];
        private TenantValue[] _initialTenantValues = [];

        internal static async Task<GovernanceRows> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var rows = new GovernanceRows(fixture);
            rows.Authentication = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
            await using var seed = await rows.ObserverAsync();
            var actor = await seed.Actors.SingleAsync(row => row.UserId == rows.Authentication.UserId);
            rows.A = await AddPlacement(rows.Authentication.A);
            rows.B = await AddPlacement(rows.Authentication.B);
            rows._restricted = await AddPlacement(rows.Authentication.Inactive);
            rows._detached = EventLocation.CreatePhysical(
                rows.B.TenantId, rows.B.EventId, rows.B.LocationId!.Value, rows.Authentication.UserId, Now);
            rows._detached.DetachFinalReference(rows.Authentication.UserId, Now);
            await new EventLocationRepository(seed).AddAsync(rows._detached, default);
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.Authentication.Inactive,
                Tenant = null!,
                SettingKey = Key,
                Value = "false",
                CreatedAt = Now
            });
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == Key);
            if (setting is null)
            {
                setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = Key,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    CreatedAt = Now
                };
                seed.SystemSettings.Add(setting);
            }
            setting.Value = "true";
            setting.IsLocked = false;
            await seed.SaveChangesAsync();
            rows._initialPolicies = await rows.PoliciesAsync(seed);
            rows._initialAudits = await seed.EventLocationDisclosureAudits.Select(row => row.Id).ToArrayAsync();
            rows._initialTenantValues = await seed.TenantSettingOverrides.Where(row => row.SettingKey == Key)
                .Select(row => new TenantValue(row.TenantId, row.Value)).ToArrayAsync();

            foreach (Type type in new[] { typeof(SystemSetting), typeof(TenantSetting), typeof(EventLocation),
                typeof(Location), typeof(LocationPii), typeof(Explore.Domain.Event),
                typeof(EventLocationDisclosureAudit), typeof(OutboxMessage), typeof(EventDiscoveryRevision),
                typeof(Tenant), typeof(LocationPrivacyCorrectionReceipt) })
            {
                var entity = seed.Model.FindEntityType(type)!;
                rows._tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var grants = admin.CreateCommand();
            string role = rows.Authentication.RuntimeOptions.Username
                ?? throw new InvalidOperationException("The fixture runtime role must be configured.");
            grants.CommandText = $"""
                GRANT SELECT ON {string.Join(", ", rows._tables)} TO "{role}";
                GRANT INSERT, UPDATE ON {rows._tables[0]}, {rows._tables[1]} TO "{role}";
                GRANT UPDATE ON {rows._tables[2]} TO "{role}";
                GRANT INSERT ON {rows._tables[6]}, {rows._tables[7]} TO "{role}";
                GRANT INSERT, UPDATE ON {rows._tables[8]} TO "{role}";
                GRANT UPDATE (id) ON {rows._tables[9]} TO "{role}";
                GRANT "{PostgresDiscoveryDisclosureContract.RuntimeRole}" TO "{role}" WITH INHERIT TRUE, SET FALSE;
                GRANT "{PostgresLocationPrivacyGovernanceContract.RuntimeRole}" TO "{role}" WITH INHERIT TRUE, SET FALSE;
                """;
            await grants.ExecuteNonQueryAsync();
            return rows;

            async Task<EventLocation> AddPlacement(Guid tenantId)
            {
                var source = new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    ActorId = actor.Id,
                    Actor = null!,
                    Title = "Governance placement",
                    EventStatus = null!,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Local,
                    EventFormat = null!,
                    CreatedAt = Now,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                var location = new Location
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    FullName = "Governance venue",
                    Country = "BE",
                    City = "Brussels",
                    CreatedAt = Now,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                location.SetManualAddress("Invariant venue street", "1000");
                seed.AddRange(source, location);
                await seed.SaveChangesAsync();
                EventLocation placement = EventLocation.CreatePhysical(
                    tenantId, source.Id, location.Id, rows.Authentication.UserId, Now);
                await new EventLocationRepository(seed).AddAsync(placement, default);
                seed.EventLocationDisclosureAudits.Add(placement.ChangeDisclosurePolicy(
                    EventLocationDisclosureFields.StreetAddress | EventLocationDisclosureFields.Postcode,
                    LocationDisclosureAudienceEnum.AnyCurrentRegistrant, null, placement.PolicyVersion,
                    rows.Authentication.UserId, EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                    Now, needsPrivacyReview: false));
                await seed.SaveChangesAsync();
                return placement;
            }
        }

        internal async Task<ExploreDbContext> ObserverAsync()
        {
            var observer = fixture.CreateDbContext();
            await observer.Database.OpenConnectionAsync();
            await using var command = observer.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT rolsuper OR rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
            return observer;
        }

        private Task<PolicyState[]> PoliciesAsync(ExploreDbContext observer) =>
            observer.EventLocations.IgnoreQueryFilters().AsNoTracking()
                .Select(row => new PolicyState(row.Id, row.PolicyVersion, row.NeedsPrivacyReview,
                    row.ConcurrencyStamp, row.LastPolicyActorUserId, row.LastPolicyChangedAtUtc,
                    row.ShowStreetAddress, row.ShowPostcode, row.ShowCoordinates,
                    row.FullDetailsAudienceId, row.RevealFullDetailsFromUtc, row.IsDeleted))
                .ToArrayAsync();

        internal async Task AssertUnchangedAsync()
        {
            await using var observer = await ObserverAsync();
            await Assert.That(await PoliciesAsync(observer)).IsEquivalentTo(_initialPolicies);
            await Assert.That(await observer.EventLocationDisclosureAudits.Select(row => row.Id).ToArrayAsync())
                .IsEquivalentTo(_initialAudits);
            await Assert.That(await observer.OutboxMessages.CountAsync(row =>
                row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType)).IsEqualTo(0);
            await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>().CountAsync()).IsEqualTo(0);
            await Assert.That((await observer.SystemSettings.SingleAsync(row => row.SettingKey == Key)).Value)
                .IsEqualTo("true");
            await Assert.That(await observer.TenantSettingOverrides.Where(row => row.SettingKey == Key)
                .Select(row => new TenantValue(row.TenantId, row.Value)).ToArrayAsync())
                .IsEquivalentTo(_initialTenantValues);
        }

        internal async Task AssertCommittedAsync(Guid[] corrected, bool instance)
        {
            await using var observer = await ObserverAsync();
            foreach (PolicyState previous in _initialPolicies)
            {
                EventLocation current = await observer.EventLocations.IgnoreQueryFilters()
                    .AsNoTracking().SingleAsync(row => row.Id == previous.Id);
                bool changed = corrected.Contains(previous.Id);
                await Assert.That(current.PolicyVersion).IsEqualTo(changed ? previous.Version + 1 : previous.Version);
                await Assert.That(current.NeedsPrivacyReview).IsEqualTo(changed || previous.Review);
                await Assert.That(current.ShowStreetAddress).IsEqualTo(previous.Street);
                await Assert.That(current.ShowPostcode).IsEqualTo(previous.Postcode);
                await Assert.That(current.ShowCoordinates).IsEqualTo(previous.Coordinates);
                await Assert.That(current.FullDetailsAudienceId).IsEqualTo(previous.Audience);
                await Assert.That(current.RevealFullDetailsFromUtc).IsEqualTo(previous.Reveal);
                await Assert.That(current.IsDeleted).IsEqualTo(previous.Deleted);
                if (!changed)
                {
                    await Assert.That(current.ConcurrencyStamp).IsEqualTo(previous.Stamp);
                    await Assert.That(current.LastPolicyActorUserId).IsEqualTo(previous.Actor);
                    await Assert.That(current.LastPolicyChangedAtUtc).IsEqualTo(previous.ChangedAt);
                }
                if (changed)
                    await Assert.That(current.LastPolicyActorUserId).IsEqualTo(Authentication.UserId);
            }
            await Assert.That(await observer.EventLocationDisclosureAudits
                .Where(row => row.Reason != EventLocationDisclosureAuditReasonEnum.GovernanceTightening)
                .Select(row => row.Id).ToArrayAsync()).IsEquivalentTo(_initialAudits);
            await Assert.That(await observer.EventLocationDisclosureAudits
                .Where(row => row.Reason == EventLocationDisclosureAuditReasonEnum.GovernanceTightening)
                .Select(row => row.EventLocationId).ToArrayAsync()).IsEquivalentTo(corrected);
            await Assert.That(await observer.OutboxMessages
                .Where(row => row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType)
                .Select(row => row.AggregateId).ToArrayAsync()).IsEquivalentTo(corrected);
            await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>()
                .Select(row => row.EventLocationId).ToArrayAsync()).IsEquivalentTo(corrected);
            await Assert.That((await observer.SystemSettings.SingleAsync(row => row.SettingKey == Key)).Value)
                .IsEqualTo(instance ? "false" : "true");
            await Assert.That(await observer.TenantSettingOverrides.Where(row => row.SettingKey == Key)
                .Select(row => new TenantValue(row.TenantId, row.Value)).ToArrayAsync())
                .IsEquivalentTo(instance
                    ? _initialTenantValues
                    : new[] { new TenantValue(Authentication.Inactive, "false"), new TenantValue(Authentication.A, "false") });
        }

        internal async Task<ExploreDbContext> CreateGlobalAsync()
        {
            _globalAuthority = await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                fixture, PostgresLocationPrivacyGovernanceContract.GlobalRole,
                PostgresDiscoveryDisclosureContract.RuntimeRole);
            var context = await _globalAuthority.Factory.CreateDbContextAsync();
            _globalLogin = await context.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"")
                .SingleAsync();
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"GRANT SELECT, INSERT, UPDATE ON {_tables[0]} TO \"{_globalLogin}\"";
            await grant.ExecuteNonQueryAsync();
            PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await context.EventLocations.IgnoreQueryFilters().AnyAsync());
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
            return context;
        }

        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync();
            await using var revoke = admin.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _tables)} FROM "{Authentication.RuntimeOptions.Username}";
                REVOKE "{PostgresDiscoveryDisclosureContract.RuntimeRole}" FROM "{Authentication.RuntimeOptions.Username}";
                REVOKE "{PostgresLocationPrivacyGovernanceContract.RuntimeRole}" FROM "{Authentication.RuntimeOptions.Username}";
                """;
            await revoke.ExecuteNonQueryAsync();
            if (_globalLogin is not null)
            {
                revoke.CommandText = $"REVOKE ALL ON {_tables[0]} FROM \"{_globalLogin}\"";
                await revoke.ExecuteNonQueryAsync();
            }
            if (_globalAuthority is not null)
                await _globalAuthority.DisposeAsync();
            await Authentication.DisposeAsync();
        }

        private sealed record PolicyState(
            Guid Id, int Version, bool Review, Guid Stamp, Guid? Actor, DateTime? ChangedAt,
            bool Street, bool Postcode, bool Coordinates, int Audience, DateTime? Reveal, bool Deleted);
        private sealed record TenantValue(Guid TenantId, string Value);
    }
}
