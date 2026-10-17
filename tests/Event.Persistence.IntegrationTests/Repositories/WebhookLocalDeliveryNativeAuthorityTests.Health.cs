using Explore.Persistence.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class WebhookLocalDeliveryNativeAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalHealthObservesBothTenantsWithoutReadingOrChangingPrivateGraphs(bool instanceEndpoint)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        // All work is safely in the past for real readiness; the explicit observation
        // still distinguishes future work without sleeps or a timing-sensitive lease.
        var observedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var rows = await SeedAsync(seed, observedAt, instanceFirst: instanceEndpoint);
        rows.Second.Target.ClaimForDelivery("health-control", Guid.CreateVersion7(),
            observedAt.AddSeconds(-1), observedAt.AddMinutes(-1));
        await seed.SaveChangesAsync();
        await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var before = await HealthTargetStatesAsync(seed);

        var health = await new WebhookLocalDeliveryHealthObserver(factory)
            .ObserveAsync(observedAt, CancellationToken.None);

        await Assert.That(health).IsEqualTo(new WebhookLocalDeliveryHealth(1, 1));
        await Assert.That(await HealthTargetStatesAsync(seed)).IsEquivalentTo(before);
        await using var global = await factory.CreateDbContextAsync();
        foreach (string table in new[] { Table<WebhookLocalTargetSnapshot>(seed),
                     Table<WebhookEndpoint>(seed), Table<WebhookMessage>(seed) })
            await RequireStatementDenialAsync(global, $"SELECT * FROM {table}");
        await RequireStatementDenialAsync(global,
            $"SELECT {Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.DestinationUrl))} " +
            $"FROM {Table<WebhookLocalTargetSnapshot>(seed)}");
        await RequireStatementDenialAsync(global,
            $"SELECT {Column<WebhookEndpoint>(seed, nameof(WebhookEndpoint.SecretRef))} FROM {Table<WebhookEndpoint>(seed)}");

        // Readiness resolves the actual observer from a requestless ordinary scope.
        await using var services = HealthServices(factory);
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Degraded);
        var data = report.Entries["local-webhook-health"].Data;
        await Assert.That(data["dueLocalTargets"]).IsEqualTo(2);
        await Assert.That(data["staleDeliveringTargets"]).IsEqualTo(1);
        await Assert.That(data.Keys).DoesNotContain("tenantId");
        await Assert.That(data.Keys).DoesNotContain("endpointUrl");
        await Assert.That(data.Keys).DoesNotContain("payloadJson");
        await Assert.That(data.Keys).DoesNotContain("secretRef");
        await Assert.That(await HealthTargetStatesAsync(seed)).IsEquivalentTo(before);
    }

    [Test]
    [Arguments("private-column")]
    [Arguments("public-column")]
    [Arguments("global-row-read")]
    [Arguments("executor")]
    [Arguments("missing-executor")]
    [Arguments("overload")]
    [Arguments("owner-login")]
    [Arguments("unsafe-global")]
    [Arguments("schema-create")]
    [Arguments("membership-set")]
    [Arguments("membership-admin")]
    [Arguments("membership-inherit")]
    [Arguments("policy-hidden")]
    [Arguments("restrictive-policy")]
    [Arguments("force-disabled")]
    public async Task LocalHealthAuthorityDriftFailsReadinessWithoutCountsAndCannotBeNormalized(string drift)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await SeedAsync(seed);
        await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string function = PostgresWebhookLocalDeliveryHealthContract.FunctionSql(seed);
        string signature = function + "(timestamp with time zone)";
        string table = Table<WebhookLocalTargetSnapshot>(seed);
        string column = Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.DestinationUrl));
        string global = $"\"{fixture.NativeQueueEnrollment.GlobalLogin}\"";
        string runtime = $"\"{fixture.NativeQueueEnrollment.RuntimeLogin}\"";
        const string owner = PostgresWebhookLocalDeliveryHealthContract.OwnerRole;
        const string executor = PostgresWebhookLocalDeliveryHealthContract.ExecutorRole;
        const string migrator = PostgresWebhookLocalDeliveryHealthContract.MigratorRole;
        const string policy = PostgresWebhookLocalDeliveryHealthContract.PolicyName;
        string schema = $"\"{seed.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetSchema()}\"";
        (string inject, string restore) = drift switch
        {
            "private-column" => ($"GRANT SELECT ({column}) ON {table} TO {owner}",
                $"REVOKE SELECT ({column}) ON {table} FROM {owner}"),
            "public-column" => ($"GRANT SELECT ({column}) ON {table} TO PUBLIC",
                $"REVOKE SELECT ({column}) ON {table} FROM PUBLIC"),
            "global-row-read" => ($"GRANT SELECT ON {table} TO {global}",
                $"REVOKE SELECT ON {table} FROM {global}"),
            "executor" => ($"GRANT EXECUTE ON FUNCTION {signature} TO {runtime}",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM {runtime}"),
            "missing-executor" => ($"REVOKE EXECUTE ON FUNCTION {signature} FROM {executor}",
                $"GRANT EXECUTE ON FUNCTION {signature} TO {executor}"),
            "overload" => ($"CREATE FUNCTION {function}() RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {function}()"),
            "owner-login" => ($"ALTER ROLE {owner} LOGIN", $"ALTER ROLE {owner} NOLOGIN"),
            "unsafe-global" => ($"ALTER ROLE {global} BYPASSRLS", $"ALTER ROLE {global} NOBYPASSRLS"),
            "schema-create" => ($"GRANT CREATE ON SCHEMA {schema} TO {executor}",
                $"REVOKE CREATE ON SCHEMA {schema} FROM {executor}"),
            "membership-set" => ($"GRANT {executor} TO {global} WITH SET TRUE",
                $"GRANT {executor} TO {global} WITH SET FALSE"),
            "membership-admin" => ($"GRANT {executor} TO {global} WITH ADMIN TRUE",
                $"GRANT {executor} TO {global} WITH ADMIN FALSE"),
            "membership-inherit" => ($"GRANT {owner} TO {migrator} WITH INHERIT TRUE",
                $"GRANT {owner} TO {migrator} WITH INHERIT FALSE"),
            "policy-hidden" => ($"ALTER POLICY {policy} ON {table} USING (false)",
                $"ALTER POLICY {policy} ON {table} USING (true)"),
            "restrictive-policy" => ($"CREATE POLICY local_health_deny ON {table} AS RESTRICTIVE FOR SELECT TO {owner} USING (false)",
                $"DROP POLICY local_health_deny ON {table}"),
            "force-disabled" => ($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var observer = new WebhookLocalDeliveryHealthObserver(factory);
        await Assert.That(await observer.ObserveAsync(Now, CancellationToken.None))
            .IsEqualTo(new WebhookLocalDeliveryHealth(2, 0));
        var before = await HealthTargetStatesAsync(seed);
        long oid = await seed.Database.SqlQuery<long>(
            $"SELECT to_regprocedure({signature})::oid::bigint AS \"Value\"").SingleAsync();
        await seed.Database.ExecuteSqlRawAsync(inject);
        try
        {
            // Raw ordinary login proves native entry guard runs before NULL bounds.
            await using var connection = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {function}(NULL::timestamp with time zone)";
            var rejected = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(rejected?.SqlState).IsEqualTo(drift == "missing-executor" ? "42501" : "55000");
            await using var services = HealthServices(factory);
            var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
            var entry = report.Entries["local-webhook-health"];
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.Data.Keys).DoesNotContain("dueLocalTargets");
            await Assert.That(entry.Data.Keys).DoesNotContain("staleDeliveringTargets");
            await Assert.That(entry.Data.Keys).DoesNotContain("observedAtUtc");
            await Assert.That(async () => await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(
                seed, fixture.NativeQueueEnrollment)).Throws<PostgresException>()
                .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
            await Assert.That(await seed.Database.SqlQuery<long>(
                $"SELECT to_regprocedure({signature})::oid::bigint AS \"Value\"").SingleAsync()).IsEqualTo(oid);
            await Assert.That(await HealthTargetStatesAsync(seed)).IsEquivalentTo(before);
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync(restore);
        }
        await Assert.That(await observer.ObserveAsync(Now, CancellationToken.None))
            .IsEqualTo(new WebhookLocalDeliveryHealth(2, 0));
    }

    [Test]
    public async Task LocalHealthValidEmptyFleetRemainsHealthyAfterIdempotentInstallation()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await Assert.That(await new WebhookLocalDeliveryHealthObserver(factory).ObserveAsync(Now, CancellationToken.None))
            .IsEqualTo(new WebhookLocalDeliveryHealth(0, 0));
        await using var services = HealthServices(factory);
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(report.Entries["local-webhook-health"].Data["dueLocalTargets"]).IsEqualTo(0);
        await Assert.That(report.Entries["local-webhook-health"].Data["staleDeliveringTargets"]).IsEqualTo(0);
    }

    [Test]
    public async Task LocalHealthRefusesRuntimeEvenWithInheritedGlobalLoginAndSetRole()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await SeedAsync(seed);
        await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string global = $"\"{fixture.NativeQueueEnrollment.GlobalLogin}\"";
        string runtime = $"\"{fixture.NativeQueueEnrollment.RuntimeLogin}\"";
        string function = PostgresWebhookLocalDeliveryHealthContract.FunctionSql(seed);
        await seed.Database.ExecuteSqlRawAsync(
            $"GRANT {global} TO {runtime} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
        try
        {
            await using var connection = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)).ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {function}(NULL::timestamp with time zone)";
            var inherited = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(inherited?.SqlState).IsEqualTo("42501");
            command.CommandText = $"SET ROLE {global}";
            await command.ExecuteNonQueryAsync();
            command.CommandText = $"SELECT * FROM {function}(NULL::timestamp with time zone)";
            var impersonated = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(impersonated?.SqlState).IsEqualTo("42501");
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync($"REVOKE {global} FROM {runtime}");
        }
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await Assert.That(await new WebhookLocalDeliveryHealthObserver(factory).ObserveAsync(Now, CancellationToken.None))
            .IsEqualTo(new WebhookLocalDeliveryHealth(2, 0));
    }

    [Test]
    public async Task LocalHealthRefusesRuntimeLoginAndSelectedTenantGlobalSessions()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string function = PostgresWebhookLocalDeliveryHealthContract.FunctionSql(seed);
        var runtimeOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        await using (var runtime = new NpgsqlConnection(
            PrimaryDatabaseConfiguration.BuildConnectionString(runtimeOptions).ConnectionString))
        {
            await runtime.OpenAsync();
            await using var command = runtime.CreateCommand();
            command.CommandText = $"SELECT * FROM {function}(NULL::timestamp with time zone)";
            var rejected = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(rejected?.SqlState).IsEqualTo("42501");
        }
        var wrongFactory = new GlobalExploreDbContextFactory(runtimeOptions with { Role = PrimaryDatabaseRole.Global }, poolSize: 1);
        await using (var services = HealthServices(wrongFactory))
        {
            var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(report.Entries["local-webhook-health"].Data.Keys).DoesNotContain("dueLocalTargets");
        }
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await global.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {rows.First.TenantId.ToString()}, false)");
        await RequireStatementDenialAsync(global, $"SELECT * FROM {function}(NULL::timestamp with time zone)");
        await global.Database.ExecuteSqlRawAsync("SELECT set_config('app.current_tenant_id', '', false)");
        await Assert.That(await new WebhookLocalDeliveryHealthObserver(factory).ObserveAsync(Now, CancellationToken.None))
            .IsEqualTo(new WebhookLocalDeliveryHealth(2, 0));
    }

    private static ServiceProvider HealthServices(IGlobalExploreDbContextFactory factory)
    {
        var meters = Substitute.For<System.Diagnostics.Metrics.IMeterFactory>();
        meters.Create(Arg.Any<System.Diagnostics.Metrics.MeterOptions>())
            .Returns(new System.Diagnostics.Metrics.Meter(BusinessMetrics.MeterName));
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        services.AddScoped<IWebhookLocalDeliveryHealthObserver, WebhookLocalDeliveryHealthObserver>();
        services.AddSingleton<IOptions<WebhookDeliveryProcessorSettings>>(Options.Create(new WebhookDeliveryProcessorSettings()));
        services.AddSingleton<IOptionsMonitor<WebhookOptions>>(new StaticOptionsMonitor<WebhookOptions>(
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal }));
        services.AddSingleton(new BusinessMetrics(meters));
        services.AddHealthChecks().AddCheck<LocalWebhookDeliveryHealthCheck>("local-webhook-health");
        return services.BuildServiceProvider();
    }

    private static async Task<HealthTargetState[]> HealthTargetStatesAsync(ExploreDbContext context) =>
        await context.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Select(target => new HealthTargetState(target.Id, target.TenantId, target.DeliveryStatusId,
                target.NextActionAtUtc, target.ProcessingLeaseToken, target.ProcessingLeaseExpiresAtUtc,
                target.DeliveryFence, target.ConcurrencyVersion, target.DestinationUrl, target.CredentialReference,
                target.WebhookEndpoint.StatusId, target.WebhookEndpoint.ConfigurationVersion,
                target.WebhookEndpoint.DeliveryStateVersion, target.WebhookEndpoint.ConsecutiveFailureCount,
                target.WebhookEndpoint.LastSuccessAt, target.WebhookEndpoint.LastFailureAt,
                target.WebhookMessage.PayloadHash, target.WebhookMessage.PayloadByteLength,
                target.WebhookMessage.PayloadClearedAt))
            .ToArrayAsync();

    private sealed record HealthTargetState(Guid Id, Guid TenantId, int Status, DateTimeOffset NextAction,
        Guid? Token, DateTimeOffset? ExpiresAt, long Fence, long Version, string Destination,
        string Credential, int EndpointStatus, int EndpointVersion, long EndpointDeliveryVersion,
        int EndpointFailures, DateTime? LastSuccessAt, DateTime? LastFailureAt,
        string PayloadHash, long PayloadByteLength, DateTime? PayloadClearedAt);
}
