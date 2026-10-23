using System.Net;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class WebhookLocalDeliveryNativeAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalClaimSettlesTenantOrRetainedInstanceWithoutHydratingCurrentEndpoint(bool instance)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow, instanceFirst: instance);
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireGlobalDenialAsync(global, seed, authority.GlobalRole);
        await RequireStatementDenialAsync(global,
            $"SELECT * FROM {PostgresWebhookClaimEndpointStateContract.FunctionSql(global)}(NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)");

        await using var services = NativeServices(authority);
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var result = await CreateDrain(services, authority, rows, handler).ProcessBatchAsync(CancellationToken.None);
        await Assert.That(result.SucceededCount).IsEqualTo(2);
        await Assert.That(handler.MessageIds).Contains(rows.First.Message.Id);
        await using var verify = fixture.CreateDbContext();
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.Id == rows.First.Endpoint.Id);
        var target = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == rows.First.Target.Id);
        await Assert.That(endpoint.LastSuccessAt).IsNotNull();
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(1);
        await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Succeeded);
        await Assert.That(target.ProcessingLeaseToken).IsNull();
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(a => a.MessageId == rows.First.Message.Id)).IsEqualTo(1);

        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        if (instance)
        {
            await Assert.That(await runtime.WebhookEndpoints.IgnoreQueryFilters()
                .Where(e => e.Id == rows.First.Endpoint.Id).Select(e => e.SecretRef).ToListAsync()).IsEmpty();
            await Assert.That(await runtime.WebhookEndpoints.IgnoreQueryFilters()
                .Where(e => e.Id == rows.First.Endpoint.Id).Select(e => e.Url).ToListAsync()).IsEmpty();
        }
    }

    [Test]
    [Arguments("token")]
    [Arguments("fence")]
    [Arguments("expired")]
    [Arguments("replaced")]
    [Arguments("foreign-endpoint")]
    [Arguments("foreign-target-endpoint")]
    [Arguments("wrong-plan")]
    [Arguments("clock-expired")]
    [Arguments("paused")]
    public async Task RetainedInstanceHealthRejectsAnythingExceptLiveOriginalCustody(string denial)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed,
            denial == "clock-expired" ? DateTimeOffset.UtcNow.AddMinutes(-4) : null, instanceFirst: true);
        Guid original = Guid.CreateVersion7();
        var claimedAt = denial == "clock-expired" ? DateTimeOffset.UtcNow.AddMinutes(-3) : Now;
        rows.First.Target.ClaimForDelivery("endpoint-state", original, claimedAt.AddMinutes(2), claimedAt);
        if (denial == "replaced")
        {
            rows.First.Target.RecoverExpiredClaim(Now.AddMinutes(2));
            rows.First.Target.ClaimForDelivery("replacement", Guid.CreateVersion7(), Now.AddMinutes(4), Now.AddMinutes(2));
        }
        if (denial == "paused")
            rows.First.Endpoint.Status = WebhookEndpointStatus.Disabled;
        await seed.SaveChangesAsync();
        if (denial == "foreign-target-endpoint")
            await seed.Database.ExecuteSqlRawAsync(
                $"UPDATE {Table<WebhookLocalTargetSnapshot>(seed)} SET {Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.WebhookEndpointId))} = {{0}} WHERE {Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.Id))} = {{1}}",
                rows.Second.Endpoint.Id, rows.First.Target.Id);
        if (denial == "wrong-plan")
            await seed.Database.ExecuteSqlRawAsync(
                $"UPDATE {Table<WebhookLocalTargetSnapshot>(seed)} SET {Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.DeliveryPlanSnapshotId))} = {{0}} WHERE {Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.Id))} = {{1}}",
                rows.Future.Target.DeliveryPlanSnapshotId, rows.First.Target.Id);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var endpoints = new WebhookEndpointRepository(runtime);
        var observed = denial == "expired" ? Now.AddMinutes(2) : claimedAt;
        var endpointId = denial is "foreign-endpoint" or "foreign-target-endpoint" ? rows.Second.Endpoint.Id : rows.First.Endpoint.Id;
        var lease = denial == "token" ? Guid.CreateVersion7() : original;
        long fence = denial == "fence" ? 2 : 1;
        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            await Assert.That(await endpoints.MarkSuccessAsync(rows.First.TenantId, endpointId,
                rows.First.Target.Id, lease, fence, observed.UtcDateTime, token)).IsFalse();
            await Assert.That(await endpoints.RecordFailureAsync(rows.First.TenantId, endpointId,
                rows.First.Target.Id, lease, fence, observed.UtcDateTime, "network_error", 1, token)).IsNull();
        });
        await using var verify = fixture.CreateDbContext();
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.Id == rows.First.Endpoint.Id);
        await Assert.That(endpoint.LastSuccessAt).IsNull();
        await Assert.That(endpoint.LastFailureAt).IsNull();
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(0);
        await Assert.That(endpoint.Status).IsEqualTo(denial == "paused"
            ? WebhookEndpointStatus.Disabled : WebhookEndpointStatus.Active);
    }

    [Test]
    public async Task RetainedInstanceReaderReturnsFrozenTargetWithoutCurrentEndpointNavigation()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("endpoint-state", lease, Now.AddMinutes(2), Now);
        rows.First.Endpoint.Url = "https://203.0.113.9/new-configuration";
        rows.First.Endpoint.SecretRef = $"rotated/{Guid.CreateVersion7():N}";
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var target = await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
            rows.First.TenantId, rows.First.Target.Id, lease, 1, Now, CancellationToken.None);
        await Assert.That(target).IsNotNull();
        await Assert.That(target!.DestinationUrl).IsEqualTo(rows.First.Target.DestinationUrl);
        await Assert.That(target.CredentialReference).IsEqualTo(rows.First.Target.CredentialReference);
        await Assert.That(target.WebhookEndpoint).IsNull();
        await Assert.That(runtime.ChangeTracker.Entries<WebhookEndpoint>()).IsEmpty();
        await Assert.That(await runtime.WebhookEndpoints.IgnoreQueryFilters()
            .Where(e => e.Id == rows.First.Endpoint.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.LastSuccessAt, Now.UtcDateTime))).IsEqualTo(0);
        await using var foreign = await authority.Runtime.CreateDbContextAsync(rows.Second.TenantId);
        await Assert.That(await new WebhookLocalTargetRepository(foreign).GetActiveClaimAsync(
            rows.Second.TenantId, rows.First.Target.Id, lease, 1, Now, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task HealthCannotUseFlushedTerminalTargetAsAnOriginalClaimReceipt()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        Guid lease = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("endpoint-state", lease, Now.AddMinutes(2), Now);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        var endpoints = new WebhookEndpointRepository(runtime);
        await Assert.That(async () => await endpoints.MarkSuccessAsync(rows.First.TenantId,
            rows.First.Endpoint.Id, rows.First.Target.Id, lease, 1, Now.UtcDateTime, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
        {
            var target = await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
                rows.First.TenantId, rows.First.Target.Id, lease, 1, Now, token);
            await Assert.That(target).IsNotNull();
            await Assert.That(await endpoints.MarkSuccessAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                target!.Id, lease, 1, Now.UtcDateTime, token)).IsTrue();
            target.MarkSucceeded(lease, 1, Now);
            await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(new WebhookDeliveryAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.First.TenantId,
                MessageId = rows.First.Message.Id,
                EndpointId = rows.First.Endpoint.Id,
                AttemptNumber = 1,
                ProcessingFence = 1,
                Outcome = WebhookDeliveryAttemptOutcome.Succeeded,
                ScheduledAt = Now.UtcDateTime,
                SentAt = Now.UtcDateTime,
                CompletedAt = Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime
            }, token);
            await Assert.That(await endpoints.MarkSuccessAsync(rows.First.TenantId, rows.First.Endpoint.Id,
                target.Id, lease, 1, Now.UtcDateTime, token)).IsFalse();
        });
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.WebhookEndpoints.IgnoreQueryFilters()
            .Where(e => e.Id == rows.First.Endpoint.Id).Select(e => e.DeliveryStateVersion).SingleAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task GlobalSessionCannotBorrowRuntimeMembershipToUseRetainedEndpointCapability()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        await using var authority = await CreateAuthorityAsync(seed);
        await seed.Database.ExecuteSqlRawAsync(
            $"GRANT \"{fixture.NativeQueueEnrollment.RuntimeLogin}\" TO \"{fixture.NativeQueueEnrollment.GlobalLogin}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
        try
        {
            await using var global = await authority.Global.CreateDbContextAsync();
            await global.Database.OpenConnectionAsync();
            await global.Database.ExecuteSqlRawAsync($"SET ROLE \"{fixture.NativeQueueEnrollment.RuntimeLogin}\"");
            await global.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {rows.First.TenantId.ToString()}, false)");
            await RequireStatementDenialAsync(global,
                $"SELECT * FROM {PostgresWebhookClaimEndpointStateContract.FunctionSql(global)}(NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)");
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync(
                $"REVOKE \"{fixture.NativeQueueEnrollment.RuntimeLogin}\" FROM \"{fixture.NativeQueueEnrollment.GlobalLogin}\"");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentOriginalClaimSettlementsCommitEndpointHealthOnlyOnce(bool instance)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: instance);
        Guid lease = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("endpoint-state", lease, Now.AddMinutes(2), Now);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<bool> SettleAsync()
        {
            await start.Task.WaitAsync(deadline.Token);
            await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId, deadline.Token);
            return await new EfCoreUnitOfWork(runtime).ExecuteInTransactionAsync(async token =>
            {
                var target = await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
                    rows.First.TenantId, rows.First.Target.Id, lease, 1, Now, token);
                if (target is null) return false;
                if (!await new WebhookEndpointRepository(runtime).MarkSuccessAsync(
                    rows.First.TenantId, rows.First.Endpoint.Id, target.Id, lease, 1, Now.UtcDateTime, token))
                    return false;
                target.MarkSucceeded(lease, 1, Now);
                await new WebhookDeliveryAttemptRepository(runtime).CreateAsync(new WebhookDeliveryAttempt
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = rows.First.TenantId,
                    MessageId = rows.First.Message.Id,
                    EndpointId = rows.First.Endpoint.Id,
                    AttemptNumber = 1,
                    ProcessingFence = 1,
                    Outcome = WebhookDeliveryAttemptOutcome.Succeeded,
                    ScheduledAt = Now.UtcDateTime,
                    SentAt = Now.UtcDateTime,
                    CompletedAt = Now.UtcDateTime,
                    CreatedAt = Now.UtcDateTime
                }, token);
                return true;
            }, deadline.Token);
        }
        var first = SettleAsync();
        var second = SettleAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        await Assert.That(results.Count(accepted => accepted)).IsEqualTo(1);
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.WebhookEndpoints.IgnoreQueryFilters()
            .Where(e => e.Id == rows.First.Endpoint.Id).Select(e => e.DeliveryStateVersion).SingleAsync()).IsEqualTo(1);
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(a => a.MessageId == rows.First.Message.Id)).IsEqualTo(1);
    }

    [Test]
    [Arguments("target")]
    [Arguments("attempt")]
    [Arguments("audit")]
    public async Task FailedSettlementDmlRollsBackInstanceHealthTargetAttemptAndAudit(string stage)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow, instanceFirst: true);
        rows.First.Endpoint.ConsecutiveFailureCount = 4;
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        string schema = seed.Model.FindEntityType(typeof(WebhookEndpoint))!.GetSchema()!;
        string name = $"endpoint_settlement_failure_{Guid.CreateVersion7():N}";
        (string table, string column, Guid id, string action) = stage switch
        {
            "target" => (Table<WebhookLocalTargetSnapshot>(seed),
                Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.Id)), rows.First.Target.Id, "UPDATE"),
            "attempt" => (Table<WebhookDeliveryAttempt>(seed),
                Column<WebhookDeliveryAttempt>(seed, nameof(WebhookDeliveryAttempt.MessageId)), rows.First.Message.Id, "INSERT"),
            "audit" => (Table<WebhookAuditEvent>(seed),
                Column<WebhookAuditEvent>(seed, nameof(WebhookAuditEvent.TargetId)), rows.First.Endpoint.Id, "INSERT"),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        // Target admission must succeed. Reject only the later lease-clearing terminal/retry write.
        string settlement = stage == "target"
            ? $"AND NEW.{Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.ProcessingLeaseToken))} IS NULL"
            : string.Empty;
        command.CommandText = $"""
            CREATE FUNCTION "{schema}"."{name}"() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.{column} = '{id}'::uuid {settlement} THEN
                    RAISE EXCEPTION 'injected_endpoint_settlement_failure' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END $body$;
            CREATE TRIGGER "{name}" BEFORE {action} ON {table}
                FOR EACH ROW EXECUTE FUNCTION "{schema}"."{name}"();
            """;
        await command.ExecuteNonQueryAsync();
        try
        {
            await using var services = NativeServices(authority);
            var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(
                stage == "audit" ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent)));
            await Assert.That(async () => await CreateDrain(services, authority, rows, handler)
                .ProcessBatchAsync(CancellationToken.None)).Throws<DbUpdateException>();
            await using var verify = fixture.CreateDbContext();
            var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(e => e.Id == rows.First.Endpoint.Id);
            var target = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(t => t.Id == rows.First.Target.Id);
            await Assert.That(endpoint.LastSuccessAt).IsNull();
            await Assert.That(endpoint.LastFailureAt).IsNull();
            await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(4);
            await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(0);
            await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Active);
            await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Delivering);
            await Assert.That(target.ProcessingLeaseToken).IsNotNull();
            await Assert.That(target.DeliveryFence).IsEqualTo(1);
            await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
                .CountAsync(a => a.MessageId == rows.First.Message.Id)).IsEqualTo(0);
            await Assert.That(await verify.WebhookAuditEvents.IgnoreQueryFilters()
                .CountAsync(a => a.TargetId == rows.First.Endpoint.Id)).IsEqualTo(0);
        }
        finally
        {
            command.CommandText = $"""
                DROP TRIGGER "{name}" ON {table};
                DROP FUNCTION "{schema}"."{name}"();
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task RetainedInstanceFailureCommitsCircuitAndTenantDeliveryAuditTogether()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow, instanceFirst: true);
        rows.First.Endpoint.ConsecutiveFailureCount = 4;
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await using var services = NativeServices(authority);
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var result = await CreateDrain(services, authority, rows, handler).ProcessBatchAsync(CancellationToken.None);
        await Assert.That(result.RetryScheduledCount).IsEqualTo(2);
        await using var verify = fixture.CreateDbContext();
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.Id == rows.First.Endpoint.Id);
        var target = await verify.WebhookLocalTargetSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == rows.First.Target.Id);
        var audit = await verify.WebhookAuditEvents.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.TargetId == rows.First.Endpoint.Id);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.AutoPaused);
        await Assert.That(endpoint.ConsecutiveFailureCount).IsEqualTo(5);
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(1);
        await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.RetryDue);
        await Assert.That(target.ProcessingLeaseToken).IsNull();
        await Assert.That(audit.TenantId).IsEqualTo(rows.First.TenantId);
        await Assert.That(audit.Action).IsEqualTo(WebhookAuditAction.EndpointAutoPaused);
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(a => a.MessageId == rows.First.Message.Id)).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task InstanceEndpointPausedDuringTransportCannotBeReopenedByLateCompletion(bool succeeded)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow, instanceFirst: true);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var services = NativeServices(authority);
        var handler = new RecordingHandler(async (request, token) =>
        {
            if (Guid.Parse(request.Headers.GetValues("svix-id").Single()) == rows.First.Message.Id)
            {
                await using var pause = fixture.CreateDbContext();
                await pause.WebhookEndpoints.IgnoreQueryFilters().Where(e => e.Id == rows.First.Endpoint.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.StatusId, (int)WebhookEndpointStatus.Disabled)
                        .SetProperty(e => e.DeliveryStateVersion, e => e.DeliveryStateVersion + 1), token);
            }
            return new HttpResponseMessage(succeeded ? HttpStatusCode.NoContent : HttpStatusCode.BadGateway);
        });
        var result = await CreateDrain(services, authority, rows, handler).ProcessBatchAsync(CancellationToken.None);
        await Assert.That(result.AlreadyClaimedCount).IsEqualTo(1);
        await using var verify = fixture.CreateDbContext();
        var endpoint = await verify.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.Id == rows.First.Endpoint.Id);
        await Assert.That(endpoint.Status).IsEqualTo(WebhookEndpointStatus.Disabled);
        await Assert.That(endpoint.LastSuccessAt).IsNull();
        await Assert.That(endpoint.LastFailureAt).IsNull();
        await Assert.That(endpoint.DeliveryStateVersion).IsEqualTo(1);
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters()
            .CountAsync(a => a.MessageId == rows.First.Message.Id)).IsEqualTo(0);
    }

    [Test]
    [Arguments("configuration-column")]
    [Arguments("execute-grantee")]
    [Arguments("overload")]
    [Arguments("membership")]
    public async Task EndpointCapabilityRejectsPrivilegeDriftBeforeReadingCustody(string drift)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, instanceFirst: true);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        string function = PostgresWebhookClaimEndpointStateContract.FunctionSql(seed);
        string signature = $"{function}(uuid, uuid, uuid, bigint, uuid, timestamp with time zone, integer, text, integer)";
        string owner = PostgresWebhookClaimEndpointStateContract.OwnerRole;
        string executor = PostgresWebhookClaimEndpointStateContract.RuntimeRole;
        (string corrupt, string restore) = drift switch
        {
            "configuration-column" => (
                $"GRANT SELECT ({Column<WebhookEndpoint>(seed, nameof(WebhookEndpoint.SecretRef))}) ON {Table<WebhookEndpoint>(seed)} TO {owner}",
                $"REVOKE SELECT ({Column<WebhookEndpoint>(seed, nameof(WebhookEndpoint.SecretRef))}) ON {Table<WebhookEndpoint>(seed)} FROM {owner}"),
            "execute-grantee" => (
                $"GRANT EXECUTE ON FUNCTION {signature} TO \"{fixture.NativeQueueEnrollment.GlobalLogin}\"",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{fixture.NativeQueueEnrollment.GlobalLogin}\""),
            "overload" => ($"CREATE FUNCTION {function}(integer) RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {function}(integer)"),
            "membership" => (
                $"GRANT {executor} TO \"{fixture.NativeQueueEnrollment.RuntimeLogin}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
                $"GRANT {executor} TO \"{fixture.NativeQueueEnrollment.RuntimeLogin}\" WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await seed.Database.ExecuteSqlRawAsync(corrupt);
        try
        {
            await Assert.That(async () => await runtime.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {function}(NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL)"))
                .Throws<PostgresException>().And.Satisfies(e => e != null && e.SqlState == "55000");
            await Assert.That(async () => await PostgresWebhookClaimEndpointStateContract.ApplyAsync(
                seed, fixture.NativeQueueEnrollment))
                .Throws<PostgresException>().And.Satisfies(e => e != null && e.SqlState == "55000");
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync(restore);
        }
    }
}
