using Explore.Persistence.Database.ProviderPrimitives;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class EmailDispatchOutboxTransitionRepositoryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalEmailAuthorityAdmitsAndRecoversWithoutRequestTenant(bool recovery)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        DateTime now = DateTime.UtcNow;
        var firstTenant = await SeedTenantAsync(seed, "email-authority-first");
        var first = await SeedDispatchAsync(seed, firstTenant.Id,
            recovery ? EmailDispatchStatus.Processing : EmailDispatchStatus.Pending);
        EmailDispatchOutbox second;
        if (recovery)
        {
            var graph = await SeedAcceptedSettlementGraphAsync(seed, "email-authority-fenced", now.AddMinutes(-30));
            second = graph.Dispatch;
            await seed.EmailDispatchOutbox.Where(row => row.Id == first.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProcessingStartedAt, now.AddMinutes(-30)));
        }
        else
        {
            var secondTenant = await SeedTenantAsync(seed, "email-authority-second");
            second = await SeedDispatchAsync(seed, secondTenant.Id, EmailDispatchStatus.Pending);
        }

        await using var authority = await CreateEmailAuthorityAsync(seed);
        var admission = authority.Admission;
        if (recovery)
        {
            var result = await admission.RecoverStaleProcessing(
                new EmailDispatchStaleRecoveryRequest(now.AddMinutes(-10), now,
                    "processing_lease_released", "Released before provider handoff.",
                    "processing_lease_expired", "Provider outcome requires reconciliation.", 10),
                CancellationToken.None);
            await Assert.That(result.RetryScheduledCount).IsEqualTo(1);
            await Assert.That(result.UnknownCount).IsEqualTo(1);
            await Assert.That(await seed.EmailDispatchOutbox.AsNoTracking()
                .Where(row => row.Id == first.Id).Select(row => row.Status).SingleAsync())
                .IsEqualTo(EmailDispatchStatus.RetryScheduled);
            await Assert.That(await seed.EmailDispatchOutbox.AsNoTracking()
                .Where(row => row.Id == second.Id).Select(row => row.Status).SingleAsync())
                .IsEqualTo(EmailDispatchStatus.Unknown);
            await Assert.That(await seed.EmailDispatchAttempts.AsNoTracking()
                .Where(row => row.EmailDispatchOutboxId == second.Id).Select(row => row.Outcome).SingleAsync())
                .IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
            await Assert.That(await seed.EmailDispatchReceipts.AsNoTracking()
                .Where(row => row.EmailDispatchOutboxId == second.Id).Select(row => row.Status).SingleAsync())
                .IsEqualTo(EmailDispatchReceiptStatus.Unknown);
            await Assert.That(await seed.NotificationDeliveries.AsNoTracking()
                .Where(row => row.EmailDispatchOutboxId == second.Id).Select(row => row.StatusId).SingleAsync())
                .IsEqualTo((int)NotificationDeliveryStatusEnum.Unknown);
        }
        else
        {
            var claimed = await admission.ClaimPendingBatchAsync(
                new EmailDispatchBatchClaimRequest(Guid.CreateVersion7(), 2, 1, 2, 1, 100, 50, DateTime.UtcNow),
                CancellationToken.None);
            await Assert.That(claimed.Select(row => row.Id)).IsEquivalentTo([first.Id, second.Id])
                .Because("global admission must claim both tenants atomically before exact Runtime execution");
        }
    }

    [Test]
    public async Task GlobalEmailAuthorityDeniesPrivateReadsElevationAndUnboundedAdmission()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(seed, "email-denial");
        var dispatch = await SeedDispatchAsync(seed, tenant.Id, EmailDispatchStatus.Pending);
        await using var authority = await CreateEmailAuthorityAsync(seed);
        await using var global = await authority.GlobalFactory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using (var flags = global.Database.GetDbConnection().CreateCommand())
        {
            flags.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
            await using var reader = await flags.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetBoolean(0)).IsFalse();
            await Assert.That(reader.GetBoolean(1)).IsFalse();
        }
        await Assert.That(global.TenantContext).IsNull();
        foreach (Type type in new[] { typeof(EmailDispatchOutbox), typeof(EmailDispatchAttempt),
                     typeof(EmailDispatchReceipt), typeof(NotificationDelivery), typeof(EmailDispatchTenantControl) })
        {
            var entity = seed.Model.FindEntityType(type)!;
            await RequireEmailDenialAsync(global, $"SELECT * FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
        }
        await RequireEmailDenialAsync(global, $"SET ROLE {PostgresEmailDispatchAdmissionContract.OwnerRole}");
        await RequireEmailDenialAsync(global, $"SET ROLE {PostgresEmailDispatchAdmissionContract.MigratorRole}");
        await using var runtime = await authority.RuntimeFactory.CreateDbContextAsync(tenant.Id);
        await RequireEmailDenialAsync(runtime,
            $"SELECT * FROM {PostgresEmailDispatchAdmissionContract.ClaimFunctionSql(runtime)}" +
            "('00000000-0000-7000-8000-000000000001'::uuid, now(), 1, 1, 1, 1, 100, 50, NULL::uuid, NULL::uuid)");
        string? rejection = null;
        try
        {
            await authority.Admission.ClaimPendingBatchAsync(
                new EmailDispatchBatchClaimRequest(Guid.CreateVersion7(), 1001, 1, 2, 1, 100, 50, DateTime.UtcNow),
                CancellationToken.None);
        }
        catch (PostgresException exception) { rejection = exception.SqlState; }
        await Assert.That(rejection).IsEqualTo("22023");
        await Assert.That(await seed.EmailDispatchOutbox.AsNoTracking()
            .Where(row => row.Id == dispatch.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(EmailDispatchStatus.Pending);
    }

    [Test]
    public async Task GlobalEmailAuthorityPreservesPriorityTenantRoundsAndBothWatermarks()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var a = await SeedTenantAsync(seed, "email-fair-a");
        var b = await SeedTenantAsync(seed, "email-fair-b");
        var first = await SeedDispatchAsync(seed, a.Id, EmailDispatchStatus.Pending);
        var second = await SeedDispatchAsync(seed, a.Id, EmailDispatchStatus.Pending);
        var other = await SeedDispatchAsync(seed, b.Id, EmailDispatchStatus.Pending);
        var required = await SeedDispatchAsync(seed, a.Id, EmailDispatchStatus.Pending);
        var reminder = await SeedDispatchAsync(seed, b.Id, EmailDispatchStatus.Pending);
        DateTime now = DateTime.UtcNow;
        first.CreatedAt = now.AddHours(-5);
        second.CreatedAt = now.AddHours(-4);
        other.CreatedAt = now.AddHours(-1);
        required.Kind = EmailDispatchKind.EventReminder;
        reminder.Kind = EmailDispatchKind.EventReminder;
        reminder.CreatedAt = now.AddHours(-6);
        var delivery = await seed.NotificationDeliveries.SingleAsync(row => row.EmailDispatchOutboxId == required.Id);
        delivery.IsRequired = true;
        delivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired;
        await seed.SaveChangesAsync();
        await using var authority = await CreateEmailAuthorityAsync(seed);
        var request = new EmailDispatchBatchClaimRequest(Guid.CreateVersion7(), 3, 2, 3, 2, 3, 0, now);
        var claimed = await authority.Admission.ClaimPendingBatchAsync(request, CancellationToken.None);
        await Assert.That(claimed.Select(row => row.Id)).IsEquivalentTo([required.Id, first.Id, other.Id]);
        await Assert.That((await seed.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).OptionalRemindersDeferred).IsTrue();
        var replay = await authority.Admission.ClaimPendingBatchAsync(request, CancellationToken.None);
        await Assert.That(replay.Select(row => row.Id)).IsEquivalentTo(claimed.Select(row => row.Id));
        await Assert.That(await authority.Admission.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(b.Id, reminder.PublishEventId, Guid.CreateVersion7(),
                3, 2, 3, 0, now), CancellationToken.None)).IsNull();
        Guid[] admitted = claimed.Select(row => row.Id).ToArray();
        await seed.EmailDispatchOutbox.Where(row => admitted.Contains(row.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, EmailDispatchStatus.Sent)
                .SetProperty(row => row.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(row => row.ProcessingLeaseToken, (Guid?)null));
        var middle = await authority.Admission.ClaimPendingBatchAsync(
            request with { LeaseToken = Guid.CreateVersion7(), BatchSize = 1 }, CancellationToken.None);
        await Assert.That(middle.Single().Id).IsEqualTo(second.Id);
        await Assert.That((await seed.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).OptionalRemindersDeferred).IsTrue();
        var resumed = await authority.Admission.ClaimPendingBatchAsync(
            request with { LeaseToken = Guid.CreateVersion7(), BatchSize = 1 }, CancellationToken.None);
        await Assert.That(resumed.Single().Id).IsEqualTo(reminder.Id);
        await Assert.That((await seed.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).OptionalRemindersDeferred).IsFalse();
    }

    [Test]
    public async Task GlobalEmailAuthorityConcurrentClaimsRespectExistingGlobalAndTenantCaps()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var a = await SeedTenantAsync(seed, "email-cap-a");
        var b = await SeedTenantAsync(seed, "email-cap-b");
        await SeedDispatchAsync(seed, a.Id, EmailDispatchStatus.Processing);
        for (int index = 0; index < 3; index++)
        {
            await SeedDispatchAsync(seed, a.Id, EmailDispatchStatus.Pending);
            await SeedDispatchAsync(seed, b.Id, EmailDispatchStatus.Pending);
        }
        await using var authority = await CreateEmailAuthorityAsync(seed);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IReadOnlyList<EmailDispatchOutbox>> ClaimAsync()
        {
            await start.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return await authority.Admission.ClaimPendingBatchAsync(
                new EmailDispatchBatchClaimRequest(Guid.CreateVersion7(), 3, 3, 3, 2, 100, 50, DateTime.UtcNow),
                CancellationToken.None);
        }
        Task<IReadOnlyList<EmailDispatchOutbox>> first = ClaimAsync();
        Task<IReadOnlyList<EmailDispatchOutbox>> second = ClaimAsync();
        start.SetResult();
        var batches = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        var claimed = batches.SelectMany(batch => batch).ToArray();
        await Assert.That(claimed.Length).IsEqualTo(2);
        await Assert.That(claimed.Select(row => row.Id).Distinct().Count()).IsEqualTo(2);
        await Assert.That(await seed.EmailDispatchOutbox.CountAsync(row => row.Status == EmailDispatchStatus.Processing))
            .IsEqualTo(3);
        await Assert.That(await seed.EmailDispatchOutbox.CountAsync(row => row.Status == EmailDispatchStatus.Processing && row.TenantId == a.Id))
            .IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalEmailAuthorityHydrationCannotExecuteSuccessorOrLoseCancelledClaim(bool cancel)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(seed, "email-hydration");
        var dispatch = await SeedDispatchAsync(seed, tenant.Id, EmailDispatchStatus.Pending);
        await using var authority = await CreateEmailAuthorityAsync(seed);
        using var cancellation = new CancellationTokenSource();
        Guid successor = Guid.CreateVersion7();
        var runtime = new BeforeEmailHydrationFactory(authority.RuntimeFactory, async () =>
        {
            if (cancel)
                cancellation.Cancel();
            else
                await seed.EmailDispatchOutbox.Where(row => row.Id == dispatch.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProcessingLeaseToken, successor));
        });
        var admission = new EmailDispatchAdmission(new EmailGlobalFactory(authority.GlobalFactory), runtime);
        var request = new EmailDispatchBatchClaimRequest(Guid.CreateVersion7(), 1, 1, 1, 1, 100, 50, DateTime.UtcNow);
        if (cancel)
        {
            bool canceled = false;
            try { await admission.ClaimPendingBatchAsync(request, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            await Assert.That(canceled).IsTrue();
            var persisted = await seed.EmailDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Processing);
            await Assert.That(persisted.ProcessingLeaseToken).IsEqualTo(request.LeaseToken);
            var recovered = await authority.Admission.RecoverStaleProcessing(
                new EmailDispatchStaleRecoveryRequest(request.ClaimedAt, DateTime.UtcNow,
                    "cancelled", "Cancelled before hydration.", "in_doubt", "Review provider outcome.", 1),
                CancellationToken.None);
            await Assert.That(recovered.RetryScheduledCount).IsEqualTo(1);
            await Assert.That(recovered.UnknownCount).IsEqualTo(0);
        }
        else
        {
            await Assert.That(await admission.ClaimPendingBatchAsync(request, CancellationToken.None)).IsEmpty();
            await Assert.That(await seed.EmailDispatchOutbox.AsNoTracking().Where(row => row.Id == dispatch.Id)
                .Select(row => row.ProcessingLeaseToken).SingleAsync()).IsEqualTo(successor);
        }
    }

    private static async Task RequireEmailDenialAsync(ExploreDbContext context, string statement)
    {
        string? denial = null;
        try { await context.Database.ExecuteSqlRawAsync(statement); }
        catch (PostgresException exception) { denial = exception.SqlState; }
        await Assert.That(denial).IsEqualTo("42501");
    }

    private sealed class BeforeEmailHydrationFactory(
        ITenantExploreDbContextFactory inner, Func<Task> before) : ITenantExploreDbContextFactory
    {
        public async Task<ExploreDbContext> CreateDbContextAsync(
            Guid tenantId, CancellationToken cancellationToken = default)
        {
            await before();
            return await inner.CreateDbContextAsync(tenantId, cancellationToken);
        }
    }

    private async Task<EmailAuthority> CreateEmailAuthorityAsync(ExploreDbContext seed)
    {
        await PostgresEmailDispatchAdmissionContract.ApplyAsync(seed);
        var entity = seed.Model.FindEntityType(typeof(EmailDispatchOutbox))!;
        string schema = entity.GetSchema()!;
        string table = $"\"{schema}\".\"{entity.GetTableName()}\"";
        string suffix = Guid.CreateVersion7().ToString("N");
        string globalRole = $"email_global_{suffix}";
        string runtimeRole = $"email_runtime_{suffix}";
        string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var grant = administrator.CreateCommand();
        grant.CommandText = $"""
            CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
            CREATE ROLE "{runtimeRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
            GRANT {PostgresEmailDispatchAdmissionContract.GlobalRole} TO "{globalRole}";
            GRANT USAGE ON SCHEMA "{schema}" TO "{runtimeRole}";
            GRANT SELECT ON {table} TO "{runtimeRole}";
            """;
        await grant.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        PrimaryDatabaseConnectionOptions Options(PrimaryDatabaseRole role, string username, string password) => new()
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = schema,
            Username = username,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
        return new EmailAuthority(fixture.ConnectionString, schema, table, globalRole, runtimeRole,
            Options(PrimaryDatabaseRole.Global, globalRole, globalPassword),
            Options(PrimaryDatabaseRole.Runtime, runtimeRole, runtimePassword));
    }

    private sealed class EmailAuthority(
        string administrationConnection, string schema, string outboxTable, string globalRole, string runtimeRole,
        PrimaryDatabaseConnectionOptions globalOptions, PrimaryDatabaseConnectionOptions runtimeOptions) : IAsyncDisposable
    {
        internal GlobalExploreDbContextFactory GlobalFactory { get; } = new(globalOptions, poolSize: 1);
        internal ITenantExploreDbContextFactory RuntimeFactory { get; } = new TenantExploreDbContextFactory(runtimeOptions, poolSize: 1);
        internal IEmailDispatchAdmission Admission => new EmailDispatchAdmission(
            new EmailGlobalFactory(GlobalFactory), RuntimeFactory);

        public async ValueTask DisposeAsync()
        {
            foreach (var options in new[] { globalOptions, runtimeOptions })
            {
                using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
                NpgsqlConnection.ClearPool(pool);
            }
            await using var administrator = new NpgsqlConnection(administrationConnection);
            await administrator.OpenAsync();
            await using var revoke = administrator.CreateCommand();
            revoke.CommandText = $"""
                REVOKE {PostgresEmailDispatchAdmissionContract.GlobalRole} FROM "{globalRole}";
                REVOKE SELECT ON {outboxTable} FROM "{runtimeRole}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{runtimeRole}";
                DROP ROLE "{globalRole}";
                DROP ROLE "{runtimeRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }
    }

    private sealed class EmailGlobalFactory(GlobalExploreDbContextFactory factory) : IEmailDispatchAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }
}
