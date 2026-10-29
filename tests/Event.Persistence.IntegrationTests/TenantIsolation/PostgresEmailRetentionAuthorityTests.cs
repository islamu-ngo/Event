using Explore.Persistence.Database.ProviderPrimitives;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed class PostgresEmailRetentionAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = Now.AddDays(-180);

    [Test]
    public async Task UnsupportedEngineAndUnboundRuntimeCannotReportSuccessfulNoWork()
    {
        var unsupported = new UnsupportedFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EmailRetentionTenantDiscoveryRepository(unsupported, null).GetTenantIdsAsync(Cutoff, 1, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EmailRetentionExecutionScopeFactory(unsupported).CreateAsync(Guid.CreateVersion7(), CancellationToken.None));

        await using var unbound = fixture.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EmailDispatchOutboxRepository(unbound).CountRetentionRedactionEligible(
                Guid.CreateVersion7(), Cutoff, 1, CancellationToken.None));
    }

    [Test]
    public async Task NativeTwoTenantDryRunThenSelectedRuntimeRedactionPreservesForeignContent()
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        await GrantRuntimeAsync(setup);
        var first = await SeedAsync(setup, "native-first", EmailDispatchStatus.Sent, Cutoff.AddDays(-2));
        var second = await SeedAsync(setup, "native-second", EmailDispatchStatus.Skipped, Cutoff.AddDays(-1));
        var unresolved = await SeedAsync(setup, "native-unknown", EmailDispatchStatus.Unknown, Cutoff.AddDays(-3));
        var recent = await SeedAsync(setup, "native-recent", EmailDispatchStatus.Sent, Cutoff.AddDays(1));
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var runtime = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), poolSize: 1);
        var discovery = new EmailRetentionTenantDiscoveryRepository(global, fixture.NativeQueueEnrollment);
        var scopes = new EmailRetentionExecutionScopeFactory(runtime);
        var owners = await discovery.GetTenantIdsAsync(Cutoff, 1, CancellationToken.None);
        await Assert.That(owners.Single()).IsEqualTo(first.TenantId);

        var dryRun = await Service(discovery, scopes, dryRun: true).CleanupAsync(Now);
        await Assert.That(dryRun.EligibleCount).IsEqualTo(2);
        await Assert.That(dryRun.RedactedCount).IsEqualTo(0);
        foreach (var graph in new[] { first, second })
        {
            await using var view = await runtime.CreateDbContextAsync(graph.TenantId);
            await AssertGraphAsync(view, graph, redacted: false);
        }
        await using (var globalView = await global.CreateDbContextAsync())
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await globalView.EmailDispatchOutbox.IgnoreQueryFilters().Select(row => row.Subject).ToArrayAsync());
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await globalView.EmailDispatchOutbox.IgnoreQueryFilters().ExecuteDeleteAsync());
            await Assert.That(await globalView.Database.SqlQueryRaw<bool>(
                "SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='email_retention_discovery_owner') AS \"Value\"")
                .SingleAsync()).IsTrue();
        }
        await using (var scope = await scopes.CreateAsync(first.TenantId, CancellationToken.None))
            await Assert.That(await scope.RedactAsync(Cutoff, Now, 1, CancellationToken.None)).IsEqualTo(1);
        await using (var other = await runtime.CreateDbContextAsync(second.TenantId))
        {
            await AssertGraphAsync(other, second, redacted: false);
            await Assert.That(await other.EmailDispatchOutbox.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == first.TenantId)).IsFalse();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new EmailDispatchOutboxRepository(other).CountRetentionRedactionEligible(
                    first.TenantId, Cutoff, 1, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                other.TenantContext = new TenantIdentity(first.TenantId);
                return Task.CompletedTask;
            });
        }
        await using (var scope = await scopes.CreateAsync(second.TenantId, CancellationToken.None))
            await Assert.That(await scope.RedactAsync(Cutoff, Now, 1, CancellationToken.None)).IsEqualTo(1);
        foreach (var graph in new[] { first, second })
        {
            await using var view = await runtime.CreateDbContextAsync(graph.TenantId);
            await AssertGraphAsync(view, graph, redacted: true);
        }
        await Assert.That((await discovery.GetTenantIdsAsync(Cutoff, 2, CancellationToken.None)).Count).IsEqualTo(0);
        foreach (var graph in new[] { unresolved, recent })
        {
            await using var view = await runtime.CreateDbContextAsync(graph.TenantId);
            await AssertGraphAsync(view, graph, redacted: false);
        }
    }

    [Test]
    public async Task NativeLastMutationFailureRollsBackChildrenAndContinuesTheOtherTenant()
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        await GrantRuntimeAsync(setup);
        var first = await SeedAsync(setup, "rollback-first", EmailDispatchStatus.Sent, Cutoff.AddDays(-2));
        var second = await SeedAsync(setup, "rollback-second", EmailDispatchStatus.Sent, Cutoff.AddDays(-1));
        string schema = setup.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            setup.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchema()!);
        string function = schema + ".email_retention_failure";
        string table = Table<EmailDispatchOutbox>(setup);
        await setup.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'retention_test_rollback'; END $body$;
            CREATE TRIGGER email_retention_failure BEFORE UPDATE ON {table}
                FOR EACH ROW WHEN (OLD.id = '{first.OutboxId}'::uuid)
                EXECUTE FUNCTION {function}();
            """);
        try
        {
            var runtime = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), poolSize: 1);
            var result = await Service(new EmailRetentionTenantDiscoveryRepository(new GlobalExploreDbContextFactory(
                    fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1), fixture.NativeQueueEnrollment),
                new EmailRetentionExecutionScopeFactory(runtime), dryRun: false).CleanupAsync(Now);
            await Assert.That(result.FailedTenantCount).IsEqualTo(1);
            await Assert.That(result.SucceededTenantCount).IsEqualTo(1);
            await Assert.That(result.RedactedCount).IsEqualTo(1);
            await using var firstView = await runtime.CreateDbContextAsync(first.TenantId);
            await AssertGraphAsync(firstView, first, redacted: false);
            await using var secondView = await runtime.CreateDbContextAsync(second.TenantId);
            await AssertGraphAsync(secondView, second, redacted: true);
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(
                $"DROP TRIGGER email_retention_failure ON {table}; DROP FUNCTION {function}()");
            await setup.EmailDispatchOutbox.IgnoreQueryFilters().Where(row => row.Id == first.OutboxId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SentAt, Now));
        }
    }

    [Test]
    [Arguments("policy")]
    [Arguments("inherited_content")]
    [Arguments("global_content")]
    [Arguments("global_delegation")]
    [Arguments("rls")]
    public async Task DiscoveryDriftFailsVisiblyAndReinstallDoesNotNormalizeIt(string drift)
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        string table = Table<EmailDispatchOutbox>(setup);
        string global = Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string inherited = Quote("email_retention_content_" + Guid.CreateVersion7().ToString("N"));
        const string policy = "email_retention_metadata_global_read";
        (string Inject, string Restore) fault = drift switch
        {
            "policy" => ($"ALTER POLICY {policy} ON {table} USING (false)", $"ALTER POLICY {policy} ON {table} USING (true)"),
            "inherited_content" => ($"CREATE ROLE {inherited} NOLOGIN; GRANT SELECT (subject) ON {table} TO {inherited}; GRANT {inherited} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {inherited} FROM {global}; REVOKE SELECT (subject) ON {table} FROM {inherited}; DROP ROLE {inherited}"),
            "global_content" => ($"GRANT SELECT (subject) ON {table} TO {global}", $"REVOKE SELECT (subject) ON {table} FROM {global}"),
            "global_delegation" => ($"GRANT {global} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE", $"REVOKE {global} FROM {runtime}"),
            "rls" => ($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY", $"ALTER TABLE {table} FORCE ROW LEVEL SECURITY"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await setup.Database.ExecuteSqlRawAsync(fault.Inject);
        try
        {
            var discovery = new EmailRetentionTenantDiscoveryRepository(new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1), fixture.NativeQueueEnrollment);
            async Task RequireRejectedAsync()
            {
                if (drift is "policy" or "rls")
                {
                    var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        discovery.GetTenantIdsAsync(Cutoff, 2, CancellationToken.None));
                    await Assert.That(denied.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
                }
                else
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        discovery.GetTenantIdsAsync(Cutoff, 2, CancellationToken.None));
            }
            await RequireRejectedAsync();
            var reinstall = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresEmailRetentionReadPermissions.ApplyAsync(setup, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall.Message).IsEqualTo("email_retention_read_permissions_drift");
            await RequireRejectedAsync();
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(fault.Restore);
        }
    }

    [Test]
    public async Task SqliteTwoTenantBoundsDryRunAndRollbackUseRealRelationalState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"email-retention-{Guid.CreateVersion7():N}.db");
        try
        {
            await EmailDispatchSqliteFixture.CreateDatabaseAsync(path);
            await using var seed = EmailDispatchSqliteFixture.CreateContext(path);
            seed.EnableTenantFilterBypass("Email retention test seeding.");
            var first = await SeedAsync(seed, "sqlite-first", EmailDispatchStatus.Sent, Cutoff.AddDays(-2));
            var second = await SeedAsync(seed, "sqlite-second", EmailDispatchStatus.Skipped, Cutoff.AddDays(-1));
            var unresolved = await SeedAsync(seed, "sqlite-unknown", EmailDispatchStatus.Unknown, Cutoff.AddDays(-3));
            var recent = await SeedAsync(seed, "sqlite-recent", EmailDispatchStatus.Sent, Cutoff.AddDays(1));
            var factory = new SqliteFactory(path);
            var discovery = new EmailRetentionTenantDiscoveryRepository(factory, null);
            var scopes = new EmailRetentionExecutionScopeFactory(factory);
            await Assert.That((await discovery.GetTenantIdsAsync(Cutoff, 1, CancellationToken.None)).Single())
                .IsEqualTo(first.TenantId);
            var dryRun = await Service(discovery, scopes, dryRun: true).CleanupAsync(Now);
            await Assert.That(dryRun.EligibleCount).IsEqualTo(2);
            await Assert.That(dryRun.RedactedCount).IsEqualTo(0);
            await using (var view = await factory.CreateDbContextAsync(first.TenantId))
                await AssertGraphAsync(view, first, redacted: false);

            string outboxTable = "\"" + seed.Model.FindEntityType(typeof(EmailDispatchOutbox))!
                .GetTableName()!.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            await seed.Database.ExecuteSqlRawAsync($"""
                CREATE TRIGGER email_retention_failure BEFORE UPDATE ON {outboxTable}
                WHEN lower(OLD.id) = '{first.OutboxId}' BEGIN SELECT RAISE(ABORT, 'retention_test_rollback'); END;
                """);
            await using (var scope = await scopes.CreateAsync(first.TenantId, CancellationToken.None))
                await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
                    await scope.RedactAsync(Cutoff, Now, 1, CancellationToken.None));
            await using (var view = await factory.CreateDbContextAsync(first.TenantId))
                await AssertGraphAsync(view, first, redacted: false);
            await seed.Database.ExecuteSqlRawAsync("DROP TRIGGER email_retention_failure");
            await using (var scope = await scopes.CreateAsync(first.TenantId, CancellationToken.None))
                await Assert.That(await scope.RedactAsync(Cutoff, Now, 1, CancellationToken.None)).IsEqualTo(1);
            await using (var other = await factory.CreateDbContextAsync(second.TenantId))
            {
                await Assert.That((await other.EmailDispatchAttempts.SingleAsync()).SanitizedErrorMessage)
                    .IsEqualTo("retained attempt detail");
                var receipt = await other.EmailDispatchReceipts.SingleAsync();
                await Assert.That(receipt.FailureMessage).IsEqualTo("retained receipt detail");
                await Assert.That(receipt.EmailDispatchOutboxId).IsEqualTo(second.OutboxId);
                await Assert.That((await other.NotificationDeliveries.SingleAsync()).ProviderStatus).IsEqualTo("queued");
                await Assert.That((await other.EmailDispatchOutbox.SingleAsync()).ContentRedactedAt).IsNull();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await new EmailDispatchOutboxRepository(other).RedactRetentionEligible(first.TenantId, Cutoff, Now, 1, CancellationToken.None));
            }
            await using (var scope = await scopes.CreateAsync(second.TenantId, CancellationToken.None))
                await Assert.That(await scope.RedactAsync(Cutoff, Now, 1, CancellationToken.None)).IsEqualTo(1);
            foreach (var graph in new[] { unresolved, recent })
            {
                await using var view = await factory.CreateDbContextAsync(graph.TenantId);
                await AssertGraphAsync(view, graph, redacted: false);
            }
            await Assert.That((await discovery.GetTenantIdsAsync(Cutoff, 2, CancellationToken.None)).Count).IsEqualTo(0);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await Service(discovery, scopes, dryRun: true).CleanupAsync(Now, cancelled.Token));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingDiscoveryGrantCannotReportAnEmptySuccessfulFleet()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(typeof(EmailDispatchOutbox));
        string tenant = names.Column(typeof(EmailDispatchOutbox), nameof(EmailDispatchOutbox.TenantId));
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        await migration.Database.ExecuteSqlRawAsync($"REVOKE SELECT ({tenant}) ON {table} FROM {global}");
        try
        {
            var discovery = new EmailRetentionTenantDiscoveryRepository(new GlobalExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1), fixture.NativeQueueEnrollment);
            var missing = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                discovery.GetTenantIdsAsync(Now, 2, CancellationToken.None));
            await Assert.That(missing.Message).IsEqualTo("email_retention_read_permissions_not_ready");
            var reinstall = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresEmailRetentionReadPermissions.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall.Message).IsEqualTo("email_retention_read_permissions_drift");
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"GRANT SELECT ({tenant}) ON {table} TO {global}");
        }
    }

    [Test]
    public async Task NativeDiscoveryRejectsInvalidBoundsAndTenantSelectedGlobalSessions()
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresEmailRetentionReadPermissions.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global);
        var globalFactory = new GlobalExploreDbContextFactory(options, poolSize: 1);
        var discovery = new EmailRetentionTenantDiscoveryRepository(globalFactory, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => discovery.GetTenantIdsAsync(Cutoff, 0, default));
        foreach (DateTime infinite in new[] { DateTime.MinValue, DateTime.MaxValue })
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => discovery.GetTenantIdsAsync(
                DateTime.SpecifyKind(infinite, DateTimeKind.Utc), 1, default));
        await Assert.ThrowsAsync<ArgumentException>(() => discovery.GetTenantIdsAsync(
            DateTime.SpecifyKind(Cutoff, DateTimeKind.Unspecified), 1, default));
        var selected = new EmailRetentionTenantDiscoveryRepository(
            new TenantSelectedGlobalFactory(globalFactory, Guid.CreateVersion7()), fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selected.GetTenantIdsAsync(Cutoff, 1, default));

        await using var global = await globalFactory.CreateDbContextAsync();
        await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await global.Database.OpenConnectionAsync();
            try
            {
                await using var transaction = await global.Database.BeginTransactionAsync();
                await global.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_catalog.set_config('app.current_tenant_id', {Guid.CreateVersion7().ToString()}, true)");
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresEmailRetentionReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
                await transaction.RollbackAsync();
            }
            finally { await global.Database.CloseConnectionAsync(); }
        });
    }

    private static EmailDispatchRetentionCleanupService Service(
        IEmailRetentionTenantDiscoveryRepository discovery, IEmailRetentionExecutionScopeFactory scopes, bool dryRun) =>
        new(discovery, scopes, Options.Create(new EmailDispatchRetentionSettings
        {
            DryRun = dryRun,
            RetentionDays = 180,
            MaxTenantsPerPass = 2,
            BatchSize = 1
        }), NullLogger<EmailDispatchRetentionCleanupService>.Instance);

    private async Task GrantRuntimeAsync(ExploreDbContext context)
    {
        string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string schema = Quote(context.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchema()!);
        await context.Database.ExecuteSqlRawAsync($"""
            GRANT USAGE ON SCHEMA {schema} TO {runtime};
            GRANT SELECT, UPDATE ON {Table<EmailDispatchOutbox>(context)}, {Table<EmailDispatchAttempt>(context)},
                {Table<EmailDispatchReceipt>(context)}, {Table<NotificationDelivery>(context)} TO {runtime};
            """);
    }

    private static async Task<Graph> SeedAsync(
        ExploreDbContext context, string suffix, EmailDispatchStatus status, DateTime settledAt)
    {
        context.BindTenantLease();
        await using var humanGate = new HumanSecuritySourceGate(context);
        await using var human = await humanGate.AcquireAsync();
        await using var enrollmentGate = new EventDiscoveryEnrollmentGate(context);
        await using var enrollment = await enrollmentGate.AcquireAsync();
        var seeded = await EmailDispatchSqliteFixture.SeedProcessingDispatchAsync(context, suffix);
        var dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == seeded.OutboxId);
        dispatch.Status = status;
        dispatch.CreatedAt = settledAt;
        dispatch.SentAt = status == EmailDispatchStatus.Sent ? settledAt : null;
        dispatch.LastFailureAt = status == EmailDispatchStatus.Skipped ? settledAt : null;
        dispatch.ProcessingStartedAt = null;
        dispatch.ProcessingLeaseToken = null;
        dispatch.UnknownAt = status == EmailDispatchStatus.Unknown ? settledAt : null;
        dispatch.AttemptCount = 1;
        dispatch.ProviderMessageId = "retained provider id";
        context.EmailDispatchAttempts.Add(new EmailDispatchAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = seeded.TenantId,
            EmailDispatchOutboxId = seeded.OutboxId,
            AttemptNumber = 1,
            Outcome = EmailDispatchAttemptOutcome.Succeeded,
            StartedAt = settledAt,
            CompletedAt = settledAt,
            CreatedAt = settledAt,
            SanitizedErrorMessage = "retained attempt detail",
            ProviderMessageId = "retained provider id"
        });
        var receipt = new EmailDispatchReceipt
        {
            Id = Guid.CreateVersion7(),
            TenantId = seeded.TenantId,
            EmailDispatchOutboxId = seeded.OutboxId,
            PublishEventId = dispatch.PublishEventId,
            Status = EmailDispatchReceiptStatus.Completed,
            FirstSeenAt = settledAt,
            CompletedAt = settledAt,
            CreatedAt = settledAt,
            FailureMessage = "retained receipt detail",
            ProviderMessageId = "retained provider id"
        };
        context.EmailDispatchReceipts.Add(receipt);
        await context.SaveChangesAsync();
        return new Graph(seeded.TenantId, seeded.OutboxId, receipt.Id, dispatch.PublishEventId);
    }

    private static async Task AssertGraphAsync(ExploreDbContext context, Graph graph, bool redacted)
    {
        var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == graph.OutboxId);
        var attempt = await context.EmailDispatchAttempts.AsNoTracking().SingleAsync(row => row.EmailDispatchOutboxId == graph.OutboxId);
        var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync(row => row.Id == graph.ReceiptId);
        var delivery = await context.NotificationDeliveries.AsNoTracking().SingleAsync(row => row.EmailDispatchOutboxId == graph.OutboxId);
        await Assert.That(dispatch.ContentRedactedAt).IsEqualTo(redacted ? Now : (DateTime?)null);
        await Assert.That(dispatch.Subject).IsEqualTo(redacted ? string.Empty : "Registration confirmation");
        await Assert.That(dispatch.PlainTextBody).IsEqualTo(redacted ? null : "Registration confirmed.");
        await Assert.That(attempt.SanitizedErrorMessage).IsEqualTo(redacted ? null : "retained attempt detail");
        await Assert.That(receipt.FailureMessage).IsEqualTo(redacted ? null : "retained receipt detail");
        await Assert.That(receipt.EmailDispatchOutboxId).IsEqualTo(graph.OutboxId);
        await Assert.That(receipt.PublishEventId).IsEqualTo(graph.PublishEventId);
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);
        await Assert.That(delivery.ProviderStatus).IsEqualTo(redacted ? null : "queued");
    }

    private static string Table<T>(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(T))!.GetTableName()!, context.Model.FindEntityType(typeof(T))!.GetSchema());
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private sealed record Graph(Guid TenantId, Guid OutboxId, Guid ReceiptId, Guid PublishEventId);
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed class TenantSelectedGlobalFactory(GlobalExploreDbContextFactory factory, Guid tenantId)
        : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ((IMixedSettingsDbContextFactory)factory).CreateMixedSettingsAsync(tenantId, cancellationToken);
    }

    private sealed class UnsupportedFactory : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
                .UseInMemoryDatabase($"email-retention-unsupported-{Guid.CreateVersion7():N}").Options));

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            CreateDbContextAsync(cancellationToken);
    }

    private sealed class SqliteFactory(string path) : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = EmailDispatchSqliteFixture.CreateContext(path);
            context.BindTenantLease();
            return Task.FromResult(context);
        }

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = EmailDispatchSqliteFixture.CreateContext(path);
            context.TenantContext = new TenantIdentity(tenantId);
            context.BindTenantLease();
            return Task.FromResult(context);
        }
    }
}
