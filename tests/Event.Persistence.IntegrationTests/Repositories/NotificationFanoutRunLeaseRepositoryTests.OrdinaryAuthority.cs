using Explore.Persistence.Database.ProviderPrimitives;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.NotificationFanout;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class NotificationFanoutRunLeaseRepositoryTests
{
    [Test]
    public async Task NativeNotificationFanout_GlobalClaimRoundPreservesCrossTenantCapacity()
    {
        await fixture.ResetAsync();
        FanoutScenario first = await SeedOccurrenceAsync("ordinary-fanout-capacity-a");
        FanoutScenario second = await SeedOccurrenceAsync("ordinary-fanout-capacity-b");
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        await using var globalA = await authority.Global.CreateDbContextAsync();
        await using var globalB = await authority.Global.CreateDbContextAsync();
        await authority.AssertOrdinaryAsync(globalA);
        await authority.AssertOrdinaryAsync(globalB);
        DateTime now = DateTime.UtcNow;
        NotificationFanoutClaimRoundRequest request = NativeClaimRequest(now, maxActiveClaims: 1);

        NotificationFanoutClaimRoundResult[] rounds = await Task.WhenAll(
            new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime)
                .ClaimDueRoundAsync(request, CancellationToken.None),
            new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime).ClaimDueRoundAsync(
                request with { LeaseOwner = "ordinary-fanout-replica" }, CancellationToken.None));
        NotificationFanoutClaim[] claims = rounds.SelectMany(round => round.Claims).ToArray();

        await Assert.That(claims.Length).IsEqualTo(1)
            .Because("global capacity must remain atomic across independent ordinary Global leases");
        await Assert.That(claims[0].TenantId == first.TenantId || claims[0].TenantId == second.TenantId).IsTrue();
        await Assert.That(claims[0].Fence).IsEqualTo(1);
        await Assert.That(claims[0].Generation).IsEqualTo(1);
        await Assert.That(claims[0].LeaseToken).IsNotEqualTo(Guid.Empty);
        await using var persisted = fixture.CreateDbContext();
        await Assert.That(await persisted.NotificationFanoutRuns
            .CountAsync(run => run.Status == "processing")).IsEqualTo(1);
    }

    [Test]
    public async Task NativeNotificationFanout_GlobalHealthReportsAggregateBacklogWithoutPrivateReads()
    {
        await fixture.ResetAsync();
        DateTime occurredAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedOccurrenceAsync("ordinary-fanout-health-a", occurredAt: occurredAt);
        await SeedOccurrenceAsync("ordinary-fanout-health-b", occurredAt: occurredAt);
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        await using var global = await authority.Global.CreateDbContextAsync();
        await authority.AssertOrdinaryAsync(global);
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<BusinessMetrics>();
        services.AddSingleton<INotificationFanoutAdmission>(
            new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime));
        await using var provider = services.BuildServiceProvider();
        var health = new NotificationFanoutHealthCheck(
            Options.Create(new NotificationFanoutProcessorSettings()),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            provider.GetRequiredService<BusinessMetrics>());

        HealthCheckResult result = await health.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["dueOccurrenceCount"]).IsEqualTo(2);
        await Assert.That(result.Data["activeClaimCount"]).IsEqualTo(0);
        await Assert.That(result.Data.Keys).DoesNotContain("tenantId");
        await Assert.That(result.Data.Keys).DoesNotContain("recipientId");
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.NotificationFanoutOccurrences.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        var deniedCursor = await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.NotificationFanoutRuns.IgnoreQueryFilters().Select(run => run.CursorUserId).ToListAsync());
        await Assert.That(deniedCursor!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task NativeNotificationFanout_ExactRuntimeExecutionRejectsForeignAndStaleClaims()
    {
        await fixture.ResetAsync();
        FanoutScenario first = await SeedOccurrenceAsync("ordinary-fanout-execution-a");
        FanoutScenario second = await SeedOccurrenceAsync("ordinary-fanout-execution-b");
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        DateTime now = DateTime.UtcNow;
        NotificationFanoutClaimRoundResult round = await new NotificationFanoutAdmission(
                authority.AdmissionFactory, authority.Runtime)
            .ClaimDueRoundAsync(NativeClaimRequest(now, maxActiveClaims: 2), CancellationToken.None);
        await Assert.That(round.Claims.Count).IsEqualTo(2);
        NotificationFanoutClaim claim = round.Claims.Single(item => item.TenantId == first.TenantId);

        await using (var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId))
        {
            await authority.AssertOrdinaryAsync(runtime);
            var repository = new NotificationFanoutRunRepository(runtime);
            await Assert.That(await runtime.NotificationFanoutRuns.IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(1);
            await Assert.That(await repository.TryRenewClaimAsync(
                claim with { TenantId = second.TenantId }, now, now.AddMinutes(5),
                CancellationToken.None)).IsFalse();
            await Assert.That(await repository.TryCompleteAsync(
                claim with { LeaseToken = Guid.CreateVersion7() }, now, CancellationToken.None)).IsFalse();
            await Assert.That(await repository.TryRenewClaimAsync(
                claim, now, now.AddMinutes(5), CancellationToken.None)).IsTrue();
            await Assert.That(await repository.TryCompleteAsync(claim, now, CancellationToken.None)).IsTrue();
        }

        await using (var other = await authority.Runtime.CreateDbContextAsync(second.TenantId))
        {
            await Assert.That(await other.NotificationFanoutRuns.IgnoreQueryFilters()
                .Select(run => run.TenantId).ToListAsync()).IsEquivalentTo([second.TenantId]);
        }
        await using var returned = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        await Assert.That(await returned.NotificationFanoutRuns.IgnoreQueryFilters()
            .Select(run => run.TenantId).ToListAsync()).IsEquivalentTo([first.TenantId]);
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.NotificationFanoutRuns
            .Where(run => run.TenantId == first.TenantId).Select(run => run.Status).SingleAsync())
            .IsEqualTo("completed");
        await Assert.That(await verification.NotificationFanoutRuns
            .Where(run => run.TenantId == second.TenantId).Select(run => run.Status).SingleAsync())
            .IsEqualTo("processing");
    }

    [Test]
    public async Task NativeNotificationFanout_AdmissionPreservesPriorityAndOneCandidatePerTenant()
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        FanoutScenario tenantA = await SeedOccurrenceAsync("ordinary-fanout-fair-a",
            priority: 10, occurredAt: now.AddMinutes(-5));
        Guid higherPriority = await SeedAdditionalOccurrenceAsync(tenantA, "higher-priority",
            priority: 70, occurredAt: now.AddMinutes(-1));
        FanoutScenario tenantB = await SeedOccurrenceAsync("ordinary-fanout-fair-b",
            priority: 50, occurredAt: now.AddMinutes(-3));
        FanoutScenario tenantC = await SeedOccurrenceAsync("ordinary-fanout-fair-c",
            priority: 50, occurredAt: now.AddMinutes(-4));
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        var admission = new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime);

        NotificationFanoutClaimRoundResult round = await admission.ClaimDueRoundAsync(
            NativeClaimRequest(DateTime.UtcNow, 3), CancellationToken.None);

        await Assert.That(round.CandidateCount).IsEqualTo(3);
        await Assert.That(round.Claims.Select(claim => claim.OccurrenceId).ToArray())
            .IsEquivalentTo([higherPriority, tenantC.OccurrenceId, tenantB.OccurrenceId]);
        await Assert.That(round.Claims[0].OccurrenceId).IsEqualTo(higherPriority);
        await Assert.That(round.Claims[1].OccurrenceId).IsEqualTo(tenantC.OccurrenceId);
        await Assert.That(round.Claims[2].OccurrenceId).IsEqualTo(tenantB.OccurrenceId);
        await Assert.That(round.Claims.Count(claim => claim.TenantId == tenantA.TenantId)).IsEqualTo(1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1001)]
    public async Task NativeNotificationFanout_AdmissionRejectsUnboundedSqlRequests(int take)
    {
        await fixture.ResetAsync();
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        await using var global = await authority.Global.CreateDbContextAsync();
        string query = $"SELECT * FROM {PostgresNotificationFanoutAdmissionContract.ClaimFunctionSql(global)}" +
            "({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})";

        var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.Database.ExecuteSqlRawAsync(query, "bounded-fanout", DateTime.UtcNow,
                TimeSpan.FromMinutes(1), take, 8, 1, BacklogHighWatermark, BacklogLowWatermark));

        await Assert.That(rejected!.SqlState).IsEqualTo("22023");
        await using var unchanged = fixture.CreateDbContext();
        await Assert.That(await unchanged.NotificationFanoutProcessorStates.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task NativeNotificationFanout_GlobalWithoutCapabilityCannotClaim()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("ordinary-fanout-no-capability");
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture, capability: false);
        var admission = new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime);

        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await admission.ClaimDueRoundAsync(NativeClaimRequest(DateTime.UtcNow, 1), CancellationToken.None));

        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await using var unchanged = fixture.CreateDbContext();
        await Assert.That(await unchanged.NotificationFanoutRuns
            .Where(row => row.TenantId == scenario.TenantId).Select(row => row.Status).SingleAsync())
            .IsEqualTo("pending");
    }

    [Test]
    public async Task NativeNotificationFanout_FutureTimeCannotWidenEligibilityAndRuntimeCannotObserveGlobally()
    {
        await fixture.ResetAsync();
        FanoutScenario future = await SeedOccurrenceAsync(
            "ordinary-fanout-future", notBefore: DateTime.UtcNow.AddHours(1));
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        var admission = new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime);
        DateTime forgedFuture = DateTime.UtcNow.AddDays(1);

        NotificationFanoutClaimRoundResult round = await admission.ClaimDueRoundAsync(
            NativeClaimRequest(forgedFuture, 1), CancellationToken.None);
        NotificationFanoutProcessorSnapshot snapshot = await admission.GetProcessorSnapshotAsync(
            forgedFuture, CancellationToken.None);

        await Assert.That(round.Claims).IsEmpty();
        await Assert.That(snapshot.DueOccurrenceCount).IsEqualTo(0);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(future.TenantId);
        string query = $"SELECT * FROM {PostgresNotificationFanoutAdmissionContract.SnapshotFunctionSql(runtime)}({{0}})";
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync(query, DateTime.UtcNow));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        string claimQuery = $"SELECT * FROM {PostgresNotificationFanoutAdmissionContract.ClaimFunctionSql(runtime)}" +
            "({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})";
        var deniedClaim = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync(claimQuery, "ordinary-runtime", DateTime.UtcNow,
                TimeSpan.FromMinutes(1), 1, 1, 1, BacklogHighWatermark, BacklogLowWatermark));
        await Assert.That(deniedClaim!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task NativeNotificationFanout_ExpiredTakeoverRetainsCursorAndReminderHysteresis()
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        FanoutScenario core = await SeedOccurrenceAsync("ordinary-fanout-core", occurredAt: now.AddMinutes(-1));
        FanoutScenario reminder = await SeedOccurrenceAsync("ordinary-fanout-reminder",
            occurredAt: now.AddMinutes(-2), deliveryPolicy: NotificationDeliveryPolicyEnum.ReminderOptional);
        await using var authority = await FanoutOrdinaryAuthority.CreateAsync(fixture);
        var admission = new NotificationFanoutAdmission(authority.AdmissionFactory, authority.Runtime);
        var request = NativeClaimRequest(now, 2) with
        {
            OptionalReminderBacklogHighWatermark = 1,
            OptionalReminderBacklogLowWatermark = 0
        };
        NotificationFanoutClaimRoundResult firstRound = await admission.ClaimDueRoundAsync(
            request, CancellationToken.None);
        await Assert.That(firstRound.Claims.Count).IsEqualTo(1);
        NotificationFanoutClaim first = firstRound.Claims[0];
        await Assert.That(first.TenantId).IsEqualTo(core.TenantId);
        var cursor = new NotificationFanoutAudienceCursor(Utc(2026, 8, 1, 11), Guid.CreateVersion7());
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(core.TenantId))
        {
            await Assert.That(await new NotificationFanoutRunRepository(runtime).TryCheckpointAsync(
                first, null, cursor, 1, 0, now, CancellationToken.None)).IsTrue();
        }
        await using (var expiration = fixture.CreateDbContext())
        {
            await expiration.NotificationFanoutRuns.Where(run => run.Id == first.RunId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    run => run.ProcessingLeaseExpiresAt, now.AddSeconds(-1)));
        }

        NotificationFanoutClaimRoundResult takeover = await admission.ClaimDueRoundAsync(
            request with { ClaimedAt = DateTime.UtcNow, LeaseOwner = "ordinary-fanout-takeover" },
            CancellationToken.None);
        await Assert.That(takeover.Claims.Count).IsEqualTo(1);
        NotificationFanoutClaim winner = takeover.Claims[0];
        await Assert.That(winner.Cursor).IsEqualTo(cursor);
        await Assert.That(winner.Fence).IsGreaterThan(first.Fence);
        await Assert.That(winner.Generation).IsGreaterThan(first.Generation);
        await Assert.That(winner.LeaseToken).IsNotEqualTo(first.LeaseToken);
        NotificationFanoutProcessorSnapshot deferred = await admission.GetProcessorSnapshotAsync(
            DateTime.UtcNow, CancellationToken.None);
        await Assert.That(deferred.OptionalRemindersDeferred).IsTrue();
        await Assert.That(deferred.ProcessedRecipientCount).IsEqualTo(1);
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(core.TenantId))
        {
            var repository = new NotificationFanoutRunRepository(runtime);
            await Assert.That(await repository.TryCompleteAsync(first, DateTime.UtcNow, CancellationToken.None)).IsFalse();
            await Assert.That(await repository.TryCompleteAsync(winner, DateTime.UtcNow, CancellationToken.None)).IsTrue();
        }

        NotificationFanoutClaimRoundResult released = await admission.ClaimDueRoundAsync(
            request with { ClaimedAt = DateTime.UtcNow }, CancellationToken.None);
        await Assert.That(released.Claims.Count).IsEqualTo(1);
        await Assert.That(released.Claims[0].TenantId).IsEqualTo(reminder.TenantId);
        await Assert.That((await admission.GetProcessorSnapshotAsync(
            DateTime.UtcNow, CancellationToken.None)).OptionalRemindersDeferred).IsFalse();
    }

    private static NotificationFanoutClaimRoundRequest NativeClaimRequest(DateTime now, int maxActiveClaims) =>
        new("ordinary-fanout", now, TimeSpan.FromMinutes(5), maxActiveClaims, maxActiveClaims, 1,
            BacklogHighWatermark, BacklogLowWatermark);

    private sealed class FanoutOrdinaryAuthority(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        private readonly string _runtimeRole = $"fanout_runtime_{Guid.CreateVersion7():N}";
        private readonly string _globalRole = $"fanout_global_{Guid.CreateVersion7():N}";
        private string _schema = null!;
        private string _runs = null!;
        private string _occurrences = null!;
        private string _runtimeConnection = null!;
        private string _globalConnection = null!;
        private bool _capability;
        internal TenantExploreDbContextFactory Runtime { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Global { get; private set; } = null!;
        internal INotificationFanoutAdmissionDbContextFactory AdmissionFactory { get; private set; } = null!;

        internal static async Task<FanoutOrdinaryAuthority> CreateAsync(
            PostgreSqlContainerFixture fixture, bool capability = true)
        {
            var authority = new FanoutOrdinaryAuthority(fixture);
            authority._capability = capability;
            await using var metadata = fixture.CreateDbContext();
            await PostgresNotificationFanoutAdmissionContract.ApplyAsync(metadata);
            var run = metadata.Model.FindEntityType(typeof(NotificationFanoutRun))!;
            var occurrence = metadata.Model.FindEntityType(typeof(NotificationFanoutOccurrence))!;
            authority._schema = run.GetSchema()!;
            authority._runs = $"\"{authority._schema}\".\"{run.GetTableName()}\"";
            authority._occurrences = $"\"{authority._schema}\".\"{occurrence.GetTableName()}\"";
            string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{authority._runtimeRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
                CREATE ROLE "{authority._globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
                GRANT USAGE ON SCHEMA "{authority._schema}" TO "{authority._runtimeRole}", "{authority._globalRole}";
                GRANT SELECT, UPDATE ON {authority._runs} TO "{authority._runtimeRole}";
                GRANT SELECT ON {authority._occurrences} TO "{authority._runtimeRole}";
                """;
            if (capability)
                grant.CommandText += $"\nGRANT {PostgresNotificationFanoutAdmissionContract.GlobalRole} TO \"{authority._globalRole}\" WITH INHERIT TRUE, SET FALSE;";
            await grant.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
            PrimaryDatabaseConnectionOptions Options(string role, string password, PrimaryDatabaseRole purpose) => new()
            {
                Role = purpose,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Schema = authority._schema,
                Username = role,
                Password = password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
            var runtime = Options(authority._runtimeRole, runtimePassword, PrimaryDatabaseRole.Runtime);
            var global = Options(authority._globalRole, globalPassword, PrimaryDatabaseRole.Global);
            authority._runtimeConnection = PrimaryDatabaseConfiguration.BuildConnectionString(runtime).ConnectionString;
            authority._globalConnection = PrimaryDatabaseConfiguration.BuildConnectionString(global).ConnectionString;
            authority.Runtime = new TenantExploreDbContextFactory(runtime, poolSize: 1);
            authority.Global = new GlobalExploreDbContextFactory(global, poolSize: 2);
            authority.AdmissionFactory = new NativeFanoutAdmissionFactory(authority.Global);
            return authority;
        }

        internal async Task AssertOrdinaryAsync(ExploreDbContext context)
        {
            await context.Database.OpenConnectionAsync();
            await using var role = context.Database.GetDbConnection().CreateCommand();
            role.CommandText = $"""
                SELECT rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb,
                    current_setting('app.current_tenant_id', true),
                    pg_has_role(current_user, '{PostgresNotificationFanoutAdmissionContract.OwnerRole}', 'MEMBER')
                        OR pg_has_role(current_user, '{PostgresNotificationFanoutAdmissionContract.MigratorRole}', 'MEMBER')
                FROM pg_catalog.pg_roles WHERE rolname = current_user
                """;
            await using var reader = await role.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetBoolean(0)).IsFalse();
            await Assert.That(reader.GetBoolean(2)).IsFalse();
            string expected = context.TenantContext?.TenantId.ToString() ?? "";
            await Assert.That(reader.IsDBNull(1) ? "" : reader.GetString(1)).IsEqualTo(expected);
        }

        public async ValueTask DisposeAsync()
        {
            using var runtimePool = new NpgsqlConnection(_runtimeConnection);
            using var globalPool = new NpgsqlConnection(_globalConnection);
            NpgsqlConnection.ClearPool(runtimePool);
            NpgsqlConnection.ClearPool(globalPool);
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT, UPDATE ON {_runs} FROM "{_runtimeRole}";
                REVOKE SELECT ON {_occurrences} FROM "{_runtimeRole}";
                REVOKE USAGE ON SCHEMA "{_schema}" FROM "{_runtimeRole}", "{_globalRole}";
                """;
            if (_capability)
                revoke.CommandText += $"\nREVOKE {PostgresNotificationFanoutAdmissionContract.GlobalRole} FROM \"{_globalRole}\";";
            revoke.CommandText += $"\nDROP ROLE \"{_runtimeRole}\";\nDROP ROLE \"{_globalRole}\";";
            await revoke.ExecuteNonQueryAsync();
        }
    }

    private sealed class NativeFanoutAdmissionFactory(GlobalExploreDbContextFactory global)
        : INotificationFanoutAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            global.CreateDbContextAsync(cancellationToken);
    }
}
