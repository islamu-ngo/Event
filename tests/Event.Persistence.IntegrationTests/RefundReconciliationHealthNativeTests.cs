using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.HealthChecks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class RefundReconciliationHealthNativeTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime ObservedAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("private-column")]
    [Arguments("public-column")]
    [Arguments("global-row-read")]
    [Arguments("executor")]
    [Arguments("missing-executor")]
    [Arguments("overload")]
    [Arguments("owner-login")]
    [Arguments("unsafe-global")]
    [Arguments("membership-set")]
    [Arguments("membership-admin")]
    [Arguments("membership-inherit")]
    [Arguments("policy-hidden")]
    [Arguments("restrictive-policy")]
    [Arguments("force-disabled")]
    public async Task AuthorityDriftFailsEntryAndReadinessBeforeArgumentsAndIsNotNormalized(string drift)
    {
        await using var migration = fixture.CreateDbContext();
        string function = PostgresRefundReconciliationHealthContract.FunctionSql(migration);
        string signature = function + "(timestamp with time zone)";
        string table = Table<RefundAttempt>(migration);
        string global = Identifier(fixture.NativeQueueEnrollment.GlobalLogin);
        string runtime = Identifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        const string owner = PostgresRefundReconciliationHealthContract.OwnerRole;
        const string executor = PostgresRefundReconciliationHealthContract.ExecutorRole;
        const string migrator = PostgresRefundReconciliationHealthContract.MigratorRole;
        const string policy = PostgresRefundReconciliationHealthContract.PolicyName;
        (string inject, string restore, string stillDrifted) = drift switch
        {
            "private-column" => ($"GRANT SELECT (provider_payment_id) ON {table} TO {owner}",
                $"REVOKE SELECT (provider_payment_id) ON {table} FROM {owner}",
                $"SELECT has_column_privilege('{owner}', {Literal(table)}, 'provider_payment_id', 'SELECT') AS \"Value\""),
            "public-column" => ($"GRANT SELECT (id) ON {table} TO PUBLIC",
                $"REVOKE SELECT (id) ON {table} FROM PUBLIC",
                $"SELECT has_column_privilege('{executor}', {Literal(table)}, 'id', 'SELECT') AS \"Value\""),
            "global-row-read" => ($"GRANT SELECT ON {table} TO {global}",
                $"REVOKE SELECT ON {table} FROM {global}",
                $"SELECT has_table_privilege({Literal(fixture.NativeQueueEnrollment.GlobalLogin)}, {Literal(table)}, 'SELECT') AS \"Value\""),
            "executor" => ($"GRANT EXECUTE ON FUNCTION {signature} TO {runtime}",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM {runtime}",
                $"SELECT has_function_privilege({Literal(fixture.NativeQueueEnrollment.RuntimeLogin)}, {Literal(signature)}, 'EXECUTE') AS \"Value\""),
            "missing-executor" => ($"REVOKE EXECUTE ON FUNCTION {signature} FROM {executor}",
                $"GRANT EXECUTE ON FUNCTION {signature} TO {executor}",
                $"SELECT NOT has_function_privilege('{executor}', {Literal(signature)}, 'EXECUTE') AS \"Value\""),
            "overload" => ($"CREATE FUNCTION {function}() RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {function}()",
                $"SELECT to_regprocedure({Literal(function + "()")}) IS NOT NULL AS \"Value\""),
            "owner-login" => ($"ALTER ROLE {owner} LOGIN",
                $"ALTER ROLE {owner} NOLOGIN",
                $"SELECT rolcanlogin AS \"Value\" FROM pg_roles WHERE rolname = '{owner}'"),
            "unsafe-global" => ($"ALTER ROLE {global} BYPASSRLS",
                $"ALTER ROLE {global} NOBYPASSRLS",
                $"SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}"),
            "membership-set" => ($"GRANT {executor} TO {global} WITH SET TRUE",
                $"GRANT {executor} TO {global} WITH SET FALSE",
                $"SELECT set_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{executor}'::regrole AND member = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}::regrole"),
            "membership-admin" => ($"GRANT {executor} TO {global} WITH ADMIN TRUE",
                $"GRANT {executor} TO {global} WITH ADMIN FALSE",
                $"SELECT admin_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{executor}'::regrole AND member = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}::regrole"),
            "membership-inherit" => ($"GRANT {owner} TO {migrator} WITH INHERIT TRUE",
                $"GRANT {owner} TO {migrator} WITH INHERIT FALSE",
                $"SELECT inherit_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{owner}'::regrole AND member = '{migrator}'::regrole"),
            "policy-hidden" => ($"ALTER POLICY {policy} ON {table} USING (false)",
                $"ALTER POLICY {policy} ON {table} USING (true)",
                $"SELECT pg_get_expr(polqual, polrelid) = 'false' AS \"Value\" FROM pg_policy WHERE polrelid = {Literal(table)}::regclass AND polname = '{policy}'"),
            "restrictive-policy" => ($"CREATE POLICY refund_health_deny ON {table} AS RESTRICTIVE FOR SELECT TO {owner} USING (false)",
                $"DROP POLICY refund_health_deny ON {table}",
                $"SELECT EXISTS (SELECT 1 FROM pg_policy WHERE polrelid = {Literal(table)}::regclass AND polname = 'refund_health_deny') AS \"Value\""),
            "force-disabled" => ($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY",
                $"SELECT NOT relforcerowsecurity AS \"Value\" FROM pg_class WHERE oid = {Literal(table)}::regclass"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        long oid = await migration.Database.SqlQuery<long>(
            $"SELECT to_regprocedure({signature})::oid::bigint AS \"Value\"").SingleAsync();
        // Each DDL statement commits before observation through another login.
        await migration.Database.ExecuteSqlRawAsync(inject);
        try
        {
            // Raw enrolled connection makes the definer, not generic connection
            // admission, prove that drift is checked before NULL arguments.
            await using var connection = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {function}(NULL::timestamp with time zone)";
            var entry = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
            await Assert.That(entry?.SqlState).IsEqualTo(
                drift == "missing-executor" ? PostgresErrorCodes.InsufficientPrivilege : "55000");

            var factory = new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
            await using var services = CreateHealthServices(factory);
            HealthReport report = await services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Name == "refund-reconciliation");
            await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(report.Entries["refund-reconciliation"].Data.Count).IsEqualTo(0);

            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresRefundReconciliationHealthContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall?.SqlState).IsEqualTo("55000");
            await Assert.That(await migration.Database.SqlQueryRaw<bool>(stillDrifted).SingleAsync()).IsTrue();
            await Assert.That(await migration.Database.SqlQuery<long>(
                $"SELECT to_regprocedure({signature})::oid::bigint AS \"Value\"").SingleAsync()).IsEqualTo(oid);
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(restore);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PartialRoleGraphAndOrphanArtifactsAreNotAdopted(bool allRolesAbsent)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string[] roles = allRolesAbsent
            ? [PostgresRefundReconciliationHealthContract.OwnerRole,
                PostgresRefundReconciliationHealthContract.MigratorRole,
                PostgresRefundReconciliationHealthContract.ExecutorRole]
            : [PostgresRefundReconciliationHealthContract.OwnerRole];
        foreach (string role in roles)
            await migration.Database.ExecuteSqlRawAsync($"ALTER ROLE {role} RENAME TO \"refund_health_drift_{Guid.CreateVersion7():N}\"");
        await transaction.CreateSavepointAsync("drift");
        try
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
                await migration.Database.ExecuteSqlRawAsync(
                    PostgresRefundReconciliationHealthContract.BuildSql(migration, fixture.NativeQueueEnrollment)));
            await Assert.That(rejected?.SqlState).IsEqualTo("55000");
            await transaction.RollbackToSavepointAsync("drift");
            foreach (string role in roles)
                await Assert.That(await migration.Database.SqlQuery<bool>(
                    $"SELECT to_regrole({role}) IS NULL AS \"Value\"").SingleAsync()).IsTrue();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        });
    }

    [Test]
    public async Task ReinstallPreservesExecuteOnlyObservationAndInvalidClockNeverBecomesEmptyHealth()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresRefundReconciliationHealthContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresRefundReconciliationHealthContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using var global = await factory.CreateDbContextAsync();
        string function = PostgresRefundReconciliationHealthContract.FunctionSql(global);
        foreach (string value in new[] { "NULL", "'infinity'", "'-infinity'" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {function}({value}::timestamp with time zone)"));
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        }
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new RefundReconciliationHealthObserver(factory).GetReconciliationHealthAsync(default, CancellationToken.None));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RegisteredReadinessObservesForeignUnknownWorkWithoutGlobalBusinessReads(bool boundRequest)
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        Guid tenantA = Guid.CreateVersion7(), tenantB = Guid.CreateVersion7();
        await SeedAsync(setup, tenantA, tenantB);
        var globalFactory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using var request = CreateRequestContext(boundRequest ? tenantA : null);
        string runtime = Identifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        string schema = Identifier(setup.Model.FindEntityType(typeof(RefundAttempt))!.GetSchema()!);
        await setup.Database.ExecuteSqlRawAsync(
            $"GRANT USAGE ON SCHEMA {schema} TO {runtime}; GRANT SELECT ON {Table<RefundAttempt>(setup)} TO {runtime}");
        try
        {
            // This is the original false-Healthy seam: neither an A-bound nor
            // unbound ordinary RLS connection may see B's Unknown attempt.
            await Assert.That(await request.RefundAttempts.IgnoreQueryFilters()
                .CountAsync(row => row.TenantId == tenantB)).IsEqualTo(0);

            await using var services = CreateHealthServices(globalFactory, request);
            HealthReport report = await services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Tags.Contains("ready"));
            HealthReportEntry entry = report.Entries["refund-reconciliation"];
            await Assert.That(entry.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(entry.Data["pending"]).IsEqualTo(3);
            await Assert.That(entry.Data["unknown"]).IsEqualTo(1);
            await Assert.That(entry.Data["oldestNonTerminalAtUtc"])
                .IsEqualTo(ObservedAt.AddMinutes(-30).ToString("O"));
            await Assert.That(entry.Data.Keys).IsEquivalentTo([
                "pending", "unknown", "requiresAction", "failed", "campaignsRequiringOperator",
                "disputesDueSoon", "disputesDueWithin72Hours", "disputesOverdue", "oldestNonTerminalAtUtc"]);

            var health = await services.GetRequiredService<IRefundReconciliationHealthObserver>()
                .GetReconciliationHealthAsync(ObservedAt, CancellationToken.None);
            await Assert.That(health).IsEqualTo(new RefundReconciliationHealth(
                3, 1, 1, 2, 1, 2, 1, 1, ObservedAt.AddMinutes(-30)));

            await using var global = await globalFactory.CreateDbContextAsync();
            foreach (string table in new[] { Table<RefundAttempt>(setup), Table<RefundCampaign>(setup), Table<PaymentDispute>(setup) })
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {table}"));
                await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            await global.Database.OpenConnectionAsync();
            await using var command = global.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT * FROM {PostgresRefundReconciliationHealthContract.FunctionSql(global)}(@observed)";
            command.Parameters.Add(new NpgsqlParameter("observed", ObservedAt));
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.FieldCount).IsEqualTo(9);
            for (int i = 0; i < 8; i++)
                await Assert.That(reader.GetFieldType(i)).IsEqualTo(typeof(int));
            await Assert.That(reader.GetFieldType(8)).IsEqualTo(typeof(DateTime));
            await Assert.That(await reader.ReadAsync()).IsFalse();
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT ON {Table<RefundAttempt>(setup)} FROM {runtime}");
        }
    }

    [Test]
    public async Task EmptyFleetIsHealthyButRuntimeAndMigratorCannotExecuteObservation()
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await new RefundReconciliationHealthObserver(factory)
            .GetReconciliationHealthAsync(ObservedAt, CancellationToken.None);
        await using var services = CreateHealthServices(factory);
        HealthReport report = await services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "refund-reconciliation");
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Healthy);
        await using var runtime = CreateRequestContext(null);
        foreach (var caller in new[] { runtime, setup })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await caller.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM {PostgresRefundReconciliationHealthContract.FunctionSql(setup)}({{0}})", [ObservedAt]));
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private ServiceProvider CreateHealthServices(IGlobalExploreDbContextFactory factory, ExploreDbContext? request = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddRefundReconciliationHealthPersistence();
        if (request is not null)
            services.AddScoped<IRefundAttemptRepository>(_ => new RefundAttemptRepository(request));
        services.AddHealthChecks().AddCheck<RefundReconciliationHealthCheck>(
            "refund-reconciliation", HealthStatus.Unhealthy,
            ["ready", "payments", "refunds", "reconciliation", "infrastructure"]);
        return services.BuildServiceProvider();
    }

    private ExploreDbContext CreateRequestContext(Guid? tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(builder.Options) { TenantContext = tenant is { } id ? new TenantIdentity(id) : null };
    }

    private static async Task SeedAsync(ExploreDbContext context, Guid tenantA, Guid tenantB)
    {
        // Only setup disables parent FKs, as in the native refund reservation
        // suite. Observation uses real, forced-RLS tables and ordinary logins.
        await context.Database.OpenConnectionAsync();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
        SeedRows(context, tenantA, tenantB);
        await context.SaveChangesAsync();
        // Commit is the observation barrier; no observer starts before rows
        // become visible and no timing delay stands in for that event.
        await transaction.CommitAsync();
        });
    }

    internal static void SeedRows(ExploreDbContext context, Guid tenantA, Guid tenantB)
    {
        foreach (Guid tenant in new[] { tenantA, tenantB })
        {
            context.Tenants.Add(new Tenant
            {
                Id = tenant,
                FullName = "Refund observation",
                Slug = $"refund-health-{tenant:N}",
                TenantStatusId = 1,
                TenantStatus = null!,
                CreatedAt = ObservedAt
            });
            var refund = CreateRefund(tenant, tenant == tenantB ? ObservedAt.AddMinutes(-30) : ObservedAt);
            if (tenant == tenantB)
                refund.MarkUnknown(ObservedAt.AddMinutes(-30), null);
            context.RefundAttempts.Add(refund);
        }
        var dispatch = CreateRefund(tenantB, ObservedAt);
        dispatch.MarkDispatchPending(ObservedAt, null);
        var pending = CreateRefund(tenantB, ObservedAt);
        pending.MarkPending($"re_{pending.Id:N}", ObservedAt, null);
        var action = CreateRefund(tenantB, ObservedAt);
        action.MarkRequiresAction($"re_{action.Id:N}", ObservedAt, null);
        var cutoffFailure = CreateRefund(tenantB, ObservedAt.AddHours(-25));
        cutoffFailure.MarkFailed($"re_{cutoffFailure.Id:N}", ObservedAt.AddHours(-24), null);
        cutoffFailure.UpdatedAt = null;
        var expiredFailure = CreateRefund(tenantB, ObservedAt.AddHours(-25));
        expiredFailure.MarkFailed($"re_{expiredFailure.Id:N}", ObservedAt.AddHours(-24).AddTicks(-10), null);
        var updatedFailure = CreateRefund(tenantB, ObservedAt.AddHours(-25));
        updatedFailure.MarkFailed($"re_{updatedFailure.Id:N}", ObservedAt.AddHours(-25), null);
        updatedFailure.UpdatedAt = ObservedAt.AddHours(-24);
        var succeeded = CreateRefund(tenantB, ObservedAt.AddDays(-2));
        succeeded.MarkSucceeded($"re_{succeeded.Id:N}", ObservedAt.AddDays(-2), null);
        var cancelled = CreateRefund(tenantB, ObservedAt.AddDays(-2));
        cancelled.MarkCancelled($"re_{cancelled.Id:N}", ObservedAt.AddDays(-2), null);
        context.RefundAttempts.AddRange(dispatch, pending, action, cutoffFailure, expiredFailure, updatedFailure, succeeded, cancelled);
        var campaign = RefundCampaign.CreateCancellation(
            Guid.CreateVersion7(), tenantB, Guid.CreateVersion7(), Guid.CreateVersion7(), "Health fixture", ObservedAt);
        var claim = campaign.Claim(Guid.CreateVersion7(), ObservedAt, TimeSpan.FromMinutes(1));
        campaign.CompleteBatch(claim, null, new RefundCampaignBatchOutcome(1, 0, 1), false, ObservedAt);
        context.RefundCampaigns.Add(campaign);
        foreach (DateTime due in new[]
        {
            ObservedAt.AddTicks(-10), ObservedAt, ObservedAt.AddHours(24),
            ObservedAt.AddHours(72), ObservedAt.AddHours(72).AddTicks(10)
        })
            context.PaymentDisputes.Add(PaymentDispute.Observe(
                Guid.CreateVersion7(), tenantB, Guid.CreateVersion7(), $"dp_{Guid.CreateVersion7():N}",
                (PaymentDisputeStage)1, PaymentDisputeStatus.Open, 100, "EUR", ObservedAt, due));
        context.PaymentDisputes.Add(PaymentDispute.Observe(
            Guid.CreateVersion7(), tenantB, Guid.CreateVersion7(), $"dp_{Guid.CreateVersion7():N}",
            (PaymentDisputeStage)1, PaymentDisputeStatus.Open, 100, "EUR", ObservedAt));
    }

    private static RefundAttempt CreateRefund(Guid tenant, DateTime observed)
    {
        Guid payment = Guid.CreateVersion7(), actor = Guid.CreateVersion7();
        var acceptance = PaidOrderAcceptanceSnapshot.Create(
            payment, tenant, tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), "health-fixture", "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText, actor, "Example Organizer",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(), "Example Operator", false, "https://events.example.test", "BE",
                "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
                "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety", "Payments Operations",
                "Dispute Operations", "Payment Reconciliation", "approved"),
            PaidOrderDeliverySnapshot.Create(
                new DateTimeOffset(ObservedAt.AddDays(10)), new DateTimeOffset(ObservedAt.AddDays(10).AddHours(2)), "Europe/Brussels"),
            "EUR", 1_000, 75, 0, 1_000, Guid.CreateVersion7(), 7, "Accepted refund policy.", "en-GB", "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            [PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Admission", 1, 1_000, 0, 1_000)], observed,
            organizerPaymentProviderConnectionId: Guid.CreateVersion7(), connectPlatformId: "platform-test",
            externalAccountId: "acct_health_private", merchantCountryCode: "BE");
        return RefundAttempt.Create(Guid.CreateVersion7(), tenant, payment, acceptance,
            "acct_health_private", $"pi_{payment:N}", $"refund:{payment:N}", 100, observed);
    }

    private static string Table<T>(ExploreDbContext context) where T : class
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ObservedAt);
    }
}
