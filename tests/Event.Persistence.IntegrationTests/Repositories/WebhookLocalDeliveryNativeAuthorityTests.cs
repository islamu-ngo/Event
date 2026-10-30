using Explore.Persistence.Database.ProviderPrimitives;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed partial class WebhookLocalDeliveryNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("enrolled-grantee")]
    [Arguments("overload")]
    [Arguments("inherited-column")]
    [Arguments("public-column")]
    [Arguments("schema-create")]
    [Arguments("membership-options")]
    public async Task LocalWebhookDriftFailsEveryEntrypointBeforeBoundsOrGovernanceReads(string drift)
    {
        await fixture.ResetAsync();
        await using var administrator = fixture.CreateDbContext();
        await using var authority = await CreateAuthorityAsync(administrator);
        await using var global = await authority.Global.CreateDbContextAsync();
        string schema = administrator.Model.FindEntityType(typeof(WebhookMessage))!.GetSchema()!;
        string table = Table<WebhookMessage>(administrator);
        string column = Column<WebhookMessage>(administrator, nameof(WebhookMessage.Id));
        string owner = PostgresWebhookLocalDeliveryAdmissionContract.OwnerRole;
        string executor = PostgresWebhookLocalDeliveryAdmissionContract.GlobalRole;
        string ancestor = $"local_column_ancestor_{Guid.CreateVersion7():N}";
        string governance = PostgresWebhookLocalDeliveryAdmissionContract.SystemGovernanceFunctionSql(global);
        (string corrupt, string restore) = drift switch
        {
            "enrolled-grantee" => ($"GRANT EXECUTE ON FUNCTION {governance}() TO \"{fixture.NativeQueueEnrollment.EmailInstanceMutationLogin}\"",
                $"REVOKE EXECUTE ON FUNCTION {governance}() FROM \"{fixture.NativeQueueEnrollment.EmailInstanceMutationLogin}\""),
            "overload" => ($"CREATE FUNCTION {governance}(integer) RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {governance}(integer)"),
            "inherited-column" => ($"""
                CREATE ROLE "{ancestor}" NOLOGIN NOINHERIT;
                GRANT SELECT ({column}) ON {table} TO "{ancestor}";
                GRANT "{ancestor}" TO {owner} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
                """, $"""
                REVOKE "{ancestor}" FROM {owner};
                REVOKE SELECT ({column}) ON {table} FROM "{ancestor}";
                DROP ROLE "{ancestor}";
                """),
            "public-column" => ($"GRANT SELECT ({column}) ON {table} TO PUBLIC",
                $"REVOKE SELECT ({column}) ON {table} FROM PUBLIC"),
            "schema-create" => ($"GRANT CREATE ON SCHEMA \"{schema}\" TO {executor}",
                $"REVOKE CREATE ON SCHEMA \"{schema}\" FROM {executor}"),
            "membership-options" => ($"GRANT {executor} TO \"{fixture.NativeQueueEnrollment.GlobalLogin}\" WITH INHERIT TRUE, SET FALSE, ADMIN TRUE",
                $"GRANT {executor} TO \"{fixture.NativeQueueEnrollment.GlobalLogin}\" WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await Assert.That(await authority.Admission.CountDueAsync(Now, CancellationToken.None)).IsEqualTo(0);
        await administrator.Database.ExecuteSqlRawAsync(corrupt);
        try
        {
            foreach (string invocation in new[]
            {
                $"{PostgresWebhookLocalDeliveryAdmissionContract.DiscoveryFunctionSql(global)}(NULL, NULL)",
                $"{PostgresWebhookLocalDeliveryAdmissionContract.CountFunctionSql(global)}(NULL, NULL)",
                $"{PostgresWebhookLocalDeliveryAdmissionContract.ClaimFunctionSql(global)}(NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL)",
                $"{PostgresWebhookLocalDeliveryAdmissionContract.RecoveryFunctionSql(global)}(NULL, NULL, NULL)",
                $"{governance}()",
                $"{PostgresWebhookLocalDeliveryAdmissionContract.TenantGovernanceFunctionSql(global)}(NULL)"
            })
                await Assert.That(async () => await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {invocation}"))
                    .Throws<PostgresException>()
                    .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
            await Assert.That(async () => await PostgresWebhookLocalDeliveryAdmissionContract.ApplyAsync(
                administrator, fixture.NativeQueueEnrollment))
                .Throws<PostgresException>()
                .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(restore);
        }
        await Assert.That(await authority.Admission.CountDueAsync(Now, CancellationToken.None)).IsEqualTo(0);
    }

    [Test]
    public async Task LocalWebhookEnrolledRuntimeCannotImpersonateGlobalThroughInheritedMembership()
    {
        await fixture.ResetAsync();
        await using var administrator = fixture.CreateDbContext();
        await using var authority = await CreateAuthorityAsync(administrator);
        var outsiderOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime) with { Role = PrimaryDatabaseRole.Global };
        var outsider = new GlobalExploreDbContextFactory(outsiderOptions, poolSize: 1);
        await administrator.Database.ExecuteSqlRawAsync(
            $"GRANT \"{fixture.NativeQueueEnrollment.GlobalLogin}\" TO \"{fixture.NativeQueueEnrollment.RuntimeLogin}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
        try
        {
            await Assert.That(await authority.Admission.CountDueAsync(Now, CancellationToken.None)).IsEqualTo(0);
            await using var connection = await outsider.CreateDbContextAsync();
            await connection.Database.OpenConnectionAsync();
            string invocation = $"SELECT * FROM {PostgresWebhookLocalDeliveryAdmissionContract.SystemGovernanceFunctionSql(connection)}()";
            await RequireStatementDenialAsync(connection, invocation);
            await connection.Database.ExecuteSqlRawAsync($"SET ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\"");
            await RequireStatementDenialAsync(connection, invocation);
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(
                $"REVOKE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" FROM \"{fixture.NativeQueueEnrollment.RuntimeLogin}\"");
        }
    }

    [Test]
    public async Task GlobalDiscoveryFindsBothDueTenantsWithoutPrivateTablePrivileges()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireGlobalDenialAsync(global, seed, authority.GlobalRole);

        var repository = authority.Admission;
        await Assert.That(await repository.GetDueTenantIdsAsync(10, Now, CancellationToken.None))
            .IsEquivalentTo([rows.First.TenantId, rows.Second.TenantId]);
        await Assert.That(await repository.CountDueAsync(Now, CancellationToken.None)).IsEqualTo(2);
        await Assert.That(await repository.CountStaleDeliveringAsync(Now, CancellationToken.None)).IsEqualTo(0);
    }

    [Test]
    public async Task GlobalClaimHonorsFairTenantEndpointAndGlobalCapacityWithoutPayloadAuthority()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireGlobalDenialAsync(global, seed, authority.GlobalRole);
        var repository = authority.Admission;
        var request = new WebhookLocalTargetClaimRequest(
            2, 10, 2, [rows.First.TenantId, rows.Second.TenantId], Now, TimeSpan.FromMinutes(5));
        var limits = new Dictionary<Guid, WebhookDeliveryClaimLimits>
        {
            [rows.First.TenantId] = new(1, 1, 1),
            [rows.Second.TenantId] = new(1, 1, 1)
        };

        var claims = await repository.ClaimDueAsync(request, limits, CancellationToken.None);

        await Assert.That(claims.Select(claim => claim.TargetId))
            .IsEquivalentTo([rows.First.Target.Id, rows.Second.Target.Id]);
        await Assert.That(claims.All(claim => claim.LeaseToken != Guid.Empty && claim.DeliveryFence == 1)).IsTrue();
        await Assert.That(await repository.ClaimDueAsync(request, limits, CancellationToken.None)).IsEmpty();
        await using var verify = fixture.CreateDbContext();
        var targets = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .ToDictionaryAsync(row => row.Id);
        foreach (var claim in claims)
        {
            await Assert.That(targets[claim.TargetId].ProcessingLeaseToken).IsEqualTo(claim.LeaseToken);
            await Assert.That(targets[claim.TargetId].ProcessingLeaseExpiresAtUtc).IsEqualTo(Now.AddMinutes(5));
        }
        await Assert.That(targets[rows.Future.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
        await Assert.That(targets[rows.Inactive.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
    }

    [Test]
    public async Task GlobalRecoveryRecordsExpiredAttemptsAndRejectsOldRuntimeFences()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        Guid expiredToken = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("native-recovery", expiredToken, Now.AddSeconds(-30), Now.AddMinutes(-1));
        rows.Second.Target.ClaimForDelivery("native-recovery", expiredToken, Now.AddSeconds(-30), Now.AddMinutes(-1));
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireGlobalDenialAsync(global, seed, authority.GlobalRole);
        var repository = authority.Admission;

        await Assert.That(await repository.CountStaleDeliveringAsync(Now, CancellationToken.None)).IsEqualTo(2);
        await Assert.That(await repository.RecoverExpiredClaimsAsync(
            Now, "processing_lease_expired", 2, CancellationToken.None)).IsEqualTo(2);

        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await Assert.That(await new WebhookLocalTargetRepository(runtime).GetActiveClaimAsync(
            rows.First.TenantId, rows.First.Target.Id, expiredToken, 1, Now, CancellationToken.None)).IsNull();
        await using var verify = fixture.CreateDbContext();
        var attempts = await verify.WebhookDeliveryAttempts.AsNoTracking().IgnoreQueryFilters().ToArrayAsync();
        await Assert.That(attempts.Length).IsEqualTo(2);
        await Assert.That(attempts.Select(row => row.TenantId))
            .IsEquivalentTo([rows.First.TenantId, rows.Second.TenantId]);
        await Assert.That(attempts.All(row => row.ProcessingFence == 1 &&
            row.AttemptNumber == 1 && row.Outcome == WebhookDeliveryAttemptOutcome.Failed &&
            row.FailureCategory == "processing_lease_expired")).IsTrue();
        var targets = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .Where(row => row.Id == rows.First.Target.Id || row.Id == rows.Second.Target.Id).ToArrayAsync();
        await Assert.That(targets.All(row => row.DeliveryStatus == WebhookLocalDeliveryStatus.RetryDue &&
            row.ProcessingLeaseToken == null && row.DeliveryFence == 1 &&
            row.NextActionAtUtc == Now)).IsTrue();
    }

    [Test]
    public async Task FreshRuntimeLeasesSealTenantAndSettlementRequiresExactTokenFenceAndExpiry()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        Guid token = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("native-runtime", token, Now.AddMinutes(5), Now);
        rows.Second.Target.ClaimForDelivery("native-runtime", token, Now.AddMinutes(5), Now);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId))
        {
            var repository = new WebhookLocalTargetRepository(runtime);
            await Assert.That(async () => await repository.GetActiveClaimAsync(rows.Second.TenantId, rows.Second.Target.Id,
                token, 1, Now, CancellationToken.None)).Throws<InvalidOperationException>();
            await Assert.That(await repository.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
                Guid.CreateVersion7(), 1, Now, CancellationToken.None)).IsNull();
            await Assert.That(await repository.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
                token, 2, Now, CancellationToken.None)).IsNull();
            await Assert.That(await repository.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
                token, 1, Now.AddMinutes(5), CancellationToken.None)).IsNull();
            var owned = await repository.GetActiveClaimAsync(rows.First.TenantId, rows.First.Target.Id,
                token, 1, Now, CancellationToken.None);
            await Assert.That(owned).IsNotNull();
            owned!.MarkSucceeded(token, 1, Now.AddSeconds(1));
            await repository.SaveChangesAsync(CancellationToken.None);
        }
        await using var next = await authority.Runtime.CreateDbContextAsync(rows.Second.TenantId);
        await Assert.That(next.TenantFilterTenantId).IsEqualTo(rows.Second.TenantId);
        await Assert.That(await next.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .Where(row => row.Id == rows.First.Target.Id).AnyAsync()).IsFalse();
        await Assert.That(await next.WebhookLocalTargetSnapshots.AsNoTracking()
            .Where(row => row.Id == rows.Second.Target.Id).Select(row => row.DeliveryStatusId).SingleAsync())
            .IsEqualTo((int)WebhookLocalDeliveryStatus.Delivering);
    }

    [Test]
    public async Task GlobalAndRuntimeLoginsCannotReadForeignPayloadsOrElevateAuthority()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireGlobalDenialAsync(global, seed, authority.GlobalRole);
        await RequireStatementDenialAsync(global, "SET ROLE postgres");
        await RequireStatementDenialAsync(global, $"SET ROLE {PostgresWebhookLocalDeliveryAdmissionContract.OwnerRole}");
        await RequireStatementDenialAsync(global, $"SET ROLE {PostgresWebhookLocalDeliveryAdmissionContract.MigratorRole}");
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await Assert.That(await runtime.WebhookMessages.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == rows.Second.Message.Id)).IsFalse();
        await RequireStatementDenialAsync(runtime, $"SET ROLE \"{authority.GlobalRole}\"");
        await RequireStatementDenialAsync(runtime,
            $"SELECT * FROM {PostgresWebhookLocalDeliveryAdmissionContract.DiscoveryFunctionSql(runtime)}(now(), 1)");
        await RequireStatementDenialAsync(runtime,
            $"SELECT * FROM {PostgresWebhookLocalDeliveryAdmissionContract.SystemGovernanceFunctionSql(runtime)}()");
    }

    [Test]
    public async Task PurposeBoundGovernancePreservesInstanceLocksAndExactTenantOverrides()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        string concurrencyKey = GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries;
        string timeoutKey = GovernanceSettingKeys.WebhookDelivery.EndpointTimeoutSeconds;
        seed.SystemSettings.AddRange(
            new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = concurrencyKey,
                Value = "16",
                IsLocked = true,
                ValueType = SettingValueType.Integer
            },
            new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = timeoutKey,
                Value = "30",
                ValueType = SettingValueType.Integer
            },
            new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = $"native.unrelated.{Guid.CreateVersion7():N}",
                Value = "not_webhook_governance",
                ValueType = SettingValueType.String
            });
        seed.TenantSettingOverrides.AddRange(
            new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.First.TenantId,
                Tenant = null!,
                SettingKey = concurrencyKey,
                Value = "1"
            },
            new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.First.TenantId,
                Tenant = null!,
                SettingKey = timeoutKey,
                Value = "9"
            },
            new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.Second.TenantId,
                Tenant = null!,
                SettingKey = timeoutKey,
                Value = "45"
            });
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        var first = await authority.Admission.GetGovernanceSettingsAsync(rows.First.TenantId, CancellationToken.None);
        var second = await authority.Admission.GetGovernanceSettingsAsync(rows.Second.TenantId, CancellationToken.None);
        var instance = await authority.Admission.GetGovernanceSettingsAsync(null, CancellationToken.None);
        await Assert.That(first.MaxConcurrentDeliveries).IsEqualTo(16);
        await Assert.That(first.EndpointTimeoutSeconds).IsEqualTo(9);
        await Assert.That(second.EndpointTimeoutSeconds).IsEqualTo(45);
        await Assert.That(instance.EndpointTimeoutSeconds).IsEqualTo(30);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireStatementDenialAsync(global, $"SELECT * FROM {Table<SystemSetting>(seed)}");
        await RequireStatementDenialAsync(global, $"SELECT * FROM {Table<TenantSetting>(seed)}");
        var allowed = await global.SystemSettings.FromSqlRaw(
            $"SELECT * FROM {PostgresWebhookLocalDeliveryAdmissionContract.SystemGovernanceFunctionSql(global)}()")
            .AsNoTracking().ToArrayAsync();
        await Assert.That(allowed.Select(row => row.SettingKey)).IsEquivalentTo([concurrencyKey, timeoutKey]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task NativeDrainUsesFreshRuntimeLeasesAndPersistsProviderOutcome(bool succeeded)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow);
        await using var authority = await CreateAuthorityAsync(seed);
        await RequirePositiveControlsAsync(authority, rows);
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(
            succeeded ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable)));
        await using var services = NativeServices(authority);
        var drain = CreateDrain(services, authority, rows, handler);

        var result = await drain.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.PendingCount).IsEqualTo(2);
        await Assert.That(result.ProcessedCount).IsEqualTo(2);
        await Assert.That(result.SucceededCount).IsEqualTo(succeeded ? 2 : 0);
        await Assert.That(result.RetryScheduledCount).IsEqualTo(succeeded ? 0 : 2);
        await Assert.That(handler.MessageIds).IsEquivalentTo([rows.First.Message.Id, rows.Second.Message.Id]);
        await using var verify = fixture.CreateDbContext();
        var attempts = await verify.WebhookDeliveryAttempts.AsNoTracking().IgnoreQueryFilters().ToArrayAsync();
        await Assert.That(attempts.Length).IsEqualTo(2);
        await Assert.That(attempts.Select(row => row.TenantId))
            .IsEquivalentTo([rows.First.TenantId, rows.Second.TenantId]);
        await Assert.That(attempts.All(row => row.AttemptNumber == 1 && row.ProcessingFence == 1 &&
            row.Outcome == (succeeded ? WebhookDeliveryAttemptOutcome.Succeeded : WebhookDeliveryAttemptOutcome.Failed)))
            .IsTrue();
        var targets = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .Where(row => row.Id == rows.First.Target.Id || row.Id == rows.Second.Target.Id).ToArrayAsync();
        await Assert.That(targets.All(row => row.ProcessingLeaseToken == null &&
            row.DeliveryStatus == (succeeded ? WebhookLocalDeliveryStatus.Succeeded : WebhookLocalDeliveryStatus.RetryDue)))
            .IsTrue();
        var endpoints = await verify.WebhookEndpoints.AsNoTracking().IgnoreQueryFilters()
            .Where(row => row.Id == rows.First.Endpoint.Id || row.Id == rows.Second.Endpoint.Id).ToArrayAsync();
        await Assert.That(endpoints.All(row => row.Status == WebhookEndpointStatus.Active &&
            row.ConsecutiveFailureCount == (succeeded ? 0 : 1))).IsTrue();
    }

    [Test]
    public async Task NativeLateProviderSuccessCannotSettleSuccessorClaim()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow);
        rows.Second.Endpoint.Status = WebhookEndpointStatus.Archived;
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        WebhookLocalDeliveryClaim? successor = null;
        var handler = new RecordingHandler(async (_, token) =>
        {
            DateTimeOffset afterExpiry = DateTimeOffset.UtcNow.AddHours(1);
            await Assert.That(await authority.Admission.RecoverExpiredClaimsAsync(
                afterExpiry, "processing_lease_expired", 10, token)).IsEqualTo(1);
            successor = (await authority.Admission.ClaimDueAsync(
                new WebhookLocalTargetClaimRequest(1, 10, 2, [rows.First.TenantId],
                    afterExpiry, TimeSpan.FromMinutes(5), rows.First.Target.Id),
                new Dictionary<Guid, WebhookDeliveryClaimLimits> { [rows.First.TenantId] = new(2, 1, 1) }, token)).Single();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using var services = NativeServices(authority);
        var drain = CreateDrain(services, authority, rows, handler);

        var result = await drain.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.SucceededCount).IsEqualTo(0);
        await Assert.That(result.AlreadyClaimedCount).IsEqualTo(1);
        await Assert.That(successor).IsNotNull();
        await using var verify = fixture.CreateDbContext();
        var persisted = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(row => row.Id == rows.First.Target.Id);
        await Assert.That(persisted.DeliveryFence).IsEqualTo(2);
        await Assert.That(persisted.ProcessingLeaseToken).IsEqualTo(successor!.LeaseToken);
        await Assert.That(persisted.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Delivering);
        var evidence = await verify.WebhookDeliveryAttempts.AsNoTracking().IgnoreQueryFilters()
            .Where(row => row.MessageId == rows.First.Message.Id).ToArrayAsync();
        await Assert.That(evidence.Length).IsEqualTo(1);
        await Assert.That(evidence[0].Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Failed);
        await Assert.That(evidence[0].FailureCategory).IsEqualTo("processing_lease_expired");
        await Assert.That(evidence[0].ProcessingFence).IsEqualTo(1);
    }

    [Test]
    public async Task RecoveryRollsBackAllAttemptEvidenceAndTargetChangesOnFailure()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed);
        Guid token = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("native-rollback", token, Now.AddMinutes(-1), Now.AddMinutes(-2));
        rows.Second.Target.ClaimForDelivery("native-rollback", token, Now.AddSeconds(-30), Now.AddMinutes(-1));
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        string schema = seed.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetSchema()!;
        string name = $"local_recovery_failure_{Guid.CreateVersion7():N}";
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText = $"""
            CREATE FUNCTION "{schema}"."{name}"() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.{Column<WebhookLocalTargetSnapshot>(seed, nameof(WebhookLocalTargetSnapshot.Id))} = '{rows.Second.Target.Id}'::uuid THEN
                    RAISE EXCEPTION 'injected_local_recovery_failure' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END $body$;
            CREATE TRIGGER "{name}" BEFORE UPDATE ON {Table<WebhookLocalTargetSnapshot>(seed)}
                FOR EACH ROW EXECUTE FUNCTION "{schema}"."{name}"();
            """;
        await command.ExecuteNonQueryAsync();
        try
        {
            string? state = null;
            try { await authority.Admission.RecoverExpiredClaimsAsync(Now, "processing_lease_expired", 2, CancellationToken.None); }
            catch (PostgresException exception) { state = exception.SqlState; }
            await Assert.That(state).IsEqualTo("23514");
            await using var verify = fixture.CreateDbContext();
            await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
            var targets = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
                .Where(row => row.Id == rows.First.Target.Id || row.Id == rows.Second.Target.Id).ToArrayAsync();
            await Assert.That(targets.All(row => row.DeliveryStatus == WebhookLocalDeliveryStatus.Delivering &&
                row.ProcessingLeaseToken == token && row.DeliveryFence == 1 && row.ConcurrencyVersion == 2)).IsTrue();
        }
        finally
        {
            command.CommandText = $"""
                DROP TRIGGER "{name}" ON {Table<WebhookLocalTargetSnapshot>(seed)};
                DROP FUNCTION "{schema}"."{name}"();
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task RecoveryDeadLettersExhaustedBudgetWithoutReleasingLiveLease()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, firstMaxAttempts: 1);
        Guid expiredToken = Guid.CreateVersion7();
        Guid liveToken = Guid.CreateVersion7();
        rows.First.Target.ClaimForDelivery("native-expired", expiredToken, Now.AddSeconds(-30), Now.AddMinutes(-1));
        rows.Second.Target.ClaimForDelivery("native-live", liveToken, Now.AddMinutes(5), Now);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);

        await Assert.That(await authority.Admission.RecoverExpiredClaimsAsync(
            Now, "processing_lease_expired", 10, CancellationToken.None)).IsEqualTo(1);

        await using var verify = fixture.CreateDbContext();
        var targets = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .ToDictionaryAsync(row => row.Id);
        await Assert.That(targets[rows.First.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.DeadLettered);
        await Assert.That(targets[rows.First.Target.Id].DeliveryFence).IsEqualTo(1);
        await Assert.That(targets[rows.First.Target.Id].ProcessingLeaseToken).IsNull();
        await Assert.That(targets[rows.Second.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Delivering);
        await Assert.That(targets[rows.Second.Target.Id].ProcessingLeaseToken).IsEqualTo(liveToken);
        await Assert.That(targets[rows.Future.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
        await Assert.That(targets[rows.Inactive.Target.Id].DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.Pending);
        var evidence = await verify.WebhookDeliveryAttempts.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        await Assert.That(evidence.TenantId).IsEqualTo(rows.First.TenantId);
        await Assert.That(evidence.Outcome).IsEqualTo(WebhookDeliveryAttemptOutcome.Failed);
        await Assert.That(evidence.AttemptNumber).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationAfterAdmissionLeavesFencedRecoverableClaimsWithoutSending()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var rows = await SeedAsync(seed, DateTimeOffset.UtcNow);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var services = NativeServices(authority);
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var cancellation = new CancellationTokenSource();
        var execution = new WebhookLocalDeliveryExecutionScopeFactory(
            authority.Runtime, services.GetRequiredService<IServiceScopeFactory>());
        var drain = CreateDrain(services, authority, rows, handler,
            new CancelBeforeExecutionFactory(execution, cancellation));

        await Assert.That(async () => { await drain.ProcessBatchAsync(cancellation.Token); })
            .Throws<OperationCanceledException>();

        await Assert.That(handler.MessageIds.Count).IsEqualTo(0);
        await using var verify = fixture.CreateDbContext();
        var claims = await verify.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .Where(row => row.Id == rows.First.Target.Id || row.Id == rows.Second.Target.Id).ToArrayAsync();
        await Assert.That(claims.All(row => row.DeliveryStatus == WebhookLocalDeliveryStatus.Delivering &&
            row.DeliveryFence == 1 && row.ProcessingLeaseToken != null)).IsTrue();
        await Assert.That(await verify.WebhookDeliveryAttempts.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await authority.Admission.RecoverExpiredClaimsAsync(
            claims.Max(row => row.ProcessingLeaseExpiresAtUtc!.Value).AddSeconds(1),
            "processing_lease_expired", 10, CancellationToken.None)).IsEqualTo(2);
    }

    private static async Task RequirePositiveControlsAsync(Authority authority, SeedRows rows)
    {
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows.First.TenantId);
        await Assert.That(runtime.TenantFilterTenantId).IsEqualTo(rows.First.TenantId);
        await Assert.That(await runtime.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == rows.First.Target.Id)).IsTrue();
        await Assert.That(await runtime.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == rows.Second.Target.Id)).IsFalse();
        await Assert.That(await runtime.WebhookMessages.AsNoTracking()
            .Where(row => row.Id == rows.First.Message.Id).Select(row => row.PayloadHash).SingleAsync())
            .IsEqualTo(rows.First.Message.PayloadHash);
        await runtime.Database.OpenConnectionAsync();
        await using var identity = runtime.Database.GetDbConnection().CreateCommand();
        identity.CommandText = """
            SELECT current_user, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole, rolreplication
            FROM pg_roles WHERE rolname = current_user
            """;
        await using var reader = await identity.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo(authority.RuntimeRole);
        for (int index = 1; index < 6; index++)
            await Assert.That(reader.GetBoolean(index)).IsFalse();
    }

    private static async Task RequireGlobalDenialAsync(ExploreDbContext global, ExploreDbContext seed, string expectedRole)
    {
        await global.Database.OpenConnectionAsync();
        await using (var identity = global.Database.GetDbConnection().CreateCommand())
        {
            identity.CommandText = """
                SELECT current_user, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole, rolreplication
                FROM pg_roles WHERE rolname = current_user
                """;
            await using var reader = await identity.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetString(0)).IsEqualTo(expectedRole);
            for (int index = 1; index < 6; index++)
                await Assert.That(reader.GetBoolean(index)).IsFalse();
        }
        await Assert.That(global.TenantContext).IsNull();
        foreach (string table in new[] { Table<WebhookLocalTargetSnapshot>(seed), Table<WebhookMessage>(seed),
                     Table<WebhookEndpoint>(seed), Table<WebhookDeliveryAttempt>(seed), Table<UserPii>(seed) })
            await RequireStatementDenialAsync(global, $"SELECT * FROM {table}");
    }

    private static async Task RequireStatementDenialAsync(ExploreDbContext context, string statement)
    {
        string? state = null;
        try { await context.Database.ExecuteSqlRawAsync(statement); }
        catch (PostgresException exception) { state = exception.SqlState; }
        await Assert.That(state).IsEqualTo("42501");
    }

    private async Task<Authority> CreateAuthorityAsync(ExploreDbContext seed)
    {
        await PostgresWebhookLocalDeliveryAdmissionContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await PostgresWebhookClaimEndpointStateContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string schema = seed.Model.FindEntityType(typeof(WebhookLocalTargetSnapshot))!.GetSchema()!;
        var globalOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global);
        var runtimeOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        string runtimeRole = fixture.NativeQueueEnrollment.RuntimeLogin;
        string target = Table<WebhookLocalTargetSnapshot>(seed);
        string attempts = Table<WebhookDeliveryAttempt>(seed);
        string reads = $"{Table<WebhookMessage>(seed)}, {Table<WebhookEndpoint>(seed)}, {Table<WebhookConsumer>(seed)}, {Table<WebhookDeliveryPlanSnapshot>(seed)}";
        string endpointWrites = string.Join(", ", new[]
        {
            nameof(WebhookEndpoint.LastSuccessAt), nameof(WebhookEndpoint.LastFailureAt),
            nameof(WebhookEndpoint.ConsecutiveFailureCount), nameof(WebhookEndpoint.DeliveryStateVersion),
            nameof(WebhookEndpoint.StatusId), nameof(WebhookEndpoint.CircuitOpenedAt), nameof(WebhookEndpoint.AutoPausedAt),
            nameof(WebhookEndpoint.AutoPauseReason), nameof(WebhookEndpoint.UpdatedAt)
        }.Select(name => Column<WebhookEndpoint>(seed, name)));
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText = $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{runtimeRole}";
            GRANT SELECT, UPDATE ON {target} TO "{runtimeRole}";
            GRANT SELECT, INSERT ON {attempts} TO "{runtimeRole}";
            GRANT SELECT ON {reads} TO "{runtimeRole}";
            GRANT UPDATE ({endpointWrites}) ON {Table<WebhookEndpoint>(seed)} TO "{runtimeRole}";
            GRANT INSERT ON {Table<WebhookAuditEvent>(seed)} TO "{runtimeRole}";
            """;
        await command.ExecuteNonQueryAsync();
        return new Authority(fixture.ConnectionString, target, attempts, reads,
            Table<WebhookEndpoint>(seed), endpointWrites, Table<WebhookAuditEvent>(seed),
            globalOptions, runtimeOptions);
    }

    private sealed class Authority(
        string administratorConnection, string target, string attempts, string reads,
        string endpointTable, string endpointWrites, string auditTable,
        PrimaryDatabaseConnectionOptions globalOptions, PrimaryDatabaseConnectionOptions runtimeOptions) : IAsyncDisposable
    {
        internal string GlobalRole => globalOptions.Username
            ?? throw new InvalidOperationException("Native authority requires a Global login.");
        internal string RuntimeRole => runtimeOptions.Username
            ?? throw new InvalidOperationException("Native authority requires a Runtime login.");
        internal GlobalExploreDbContextFactory Global { get; } = new(globalOptions, poolSize: 1);
        internal ITenantExploreDbContextFactory Runtime { get; } = new TenantExploreDbContextFactory(runtimeOptions, poolSize: 1);
        internal IWebhookLocalDeliveryAdmission Admission => new WebhookLocalDeliveryAdmission(
            new ProductionGlobalFactory(Global));

        public async ValueTask DisposeAsync()
        {
            foreach (var options in new[] { globalOptions, runtimeOptions })
            {
                using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
                NpgsqlConnection.ClearPool(pool);
            }
            await using var administrator = new NpgsqlConnection(administratorConnection);
            await administrator.OpenAsync();
            await using var command = administrator.CreateCommand();
            string runtimeRole = RuntimeRole;
            command.CommandText = $"""
                REVOKE SELECT, UPDATE ON {target} FROM "{runtimeRole}";
                REVOKE SELECT, INSERT ON {attempts} FROM "{runtimeRole}";
                REVOKE SELECT ON {reads} FROM "{runtimeRole}";
                REVOKE UPDATE ({endpointWrites}) ON {endpointTable} FROM "{runtimeRole}";
                REVOKE INSERT ON {auditTable} FROM "{runtimeRole}";
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class ProductionGlobalFactory(GlobalExploreDbContextFactory factory) : IWebhookLocalDeliveryAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }

    private static ServiceProvider NativeServices(Authority authority)
    {
        var processor = Options.Create(new WebhookDeliveryProcessorSettings());
        var options = new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions());
        return new ServiceCollection()
            .AddSingleton(Substitute.For<ICurrentUserService>())
            .AddSingleton(Substitute.For<IMachinePrincipalAccessor>())
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .AddSingleton<IWebhookRetentionPolicyResolver>(new WebhookRetentionPolicyResolver(
                new StaticOptionsMonitor<WebhookRetentionSettings>(new WebhookRetentionSettings())))
            .AddSingleton<IWebhookDeliveryGovernanceResolver>(new WebhookDeliveryGovernanceResolver(
                authority.Admission, processor, options)).BuildServiceProvider();
    }

    private static WebhookDeliveryDrainService CreateDrain(
        ServiceProvider services, Authority authority, SeedRows rows, HttpMessageHandler handler,
        IWebhookLocalDeliveryExecutionScopeFactory? execution = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new[] { rows.First, rows.Second, rows.Future, rows.Inactive }.ToDictionary(
                row => $"{WebhookOptions.SectionName}:EndpointSecrets:{row.Endpoint.SecretRef}",
                _ => (string?)("whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))))).Build();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new WebhookDeliveryDrainService(services.GetRequiredService<IServiceScopeFactory>(), authority.Admission,
            execution ?? new WebhookLocalDeliveryExecutionScopeFactory(authority.Runtime, services.GetRequiredService<IServiceScopeFactory>()),
            new StaticHttpClientFactory(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }),
            new WebhookSignatureService(), new WebhookEndpointSafetyPolicy(new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions())),
            new WebhookRetryScheduler(), new WebhookEndpointSecretResolver(configuration),
            Options.Create(new WebhookDeliveryProcessorSettings()), new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions()),
            new BusinessMetrics(meterFactory), NullLogger<WebhookDeliveryDrainService>.Instance);
    }

    private sealed class StaticOptionsMonitor<T>(T current) : IOptionsMonitor<T>
    {
        public T CurrentValue => current;
        public T Get(string? name) => current;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CancelBeforeExecutionFactory(
        IWebhookLocalDeliveryExecutionScopeFactory inner, CancellationTokenSource cancellation)
        : IWebhookLocalDeliveryExecutionScopeFactory
    {
        public Task<IWebhookLocalDeliveryExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return inner.CreateAsync(tenantId, cancellationToken);
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        internal ConcurrentBag<Guid> MessageIds { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            MessageIds.Add(Guid.Parse(request.Headers.GetValues("svix-id").Single()));
            return await send(request, cancellationToken);
        }
    }

    private static string Column<T>(ExploreDbContext context, string name)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return $"\"{entity.FindProperty(name)!.GetColumnName(mapping)}\"";
    }

    private static string Table<T>(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    private static async Task<SeedRows> SeedAsync(
        ExploreDbContext context, DateTimeOffset? dueAt = null, int firstMaxAttempts = 8,
        bool instanceFirst = false)
    {
        Guid firstTenant = Guid.CreateVersion7();
        Guid secondTenant = Guid.CreateVersion7();
        context.Tenants.AddRange(new[] { firstTenant, secondTenant }.Select(id => new Tenant
        {
            Id = id,
            FullName = "Native local delivery",
            Slug = $"local-delivery-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        }));
        DateTimeOffset observedAt = dueAt ?? Now;
        Guid? instanceId = null;
        if (instanceFirst)
        {
            var instance = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.MultiTenant, observedAt.UtcDateTime);
            context.InstanceBootstrapStates.Add(instance);
            instanceId = instance.Id;
        }
        var first = AddGraph(context, firstTenant, observedAt.AddMinutes(-2), firstMaxAttempts, instanceId);
        var second = AddGraph(context, secondTenant, observedAt.AddMinutes(-1));
        var future = AddGraph(context, firstTenant, observedAt.AddMinutes(1));
        var inactive = AddGraph(context, secondTenant, observedAt.AddMinutes(-3));
        inactive.Endpoint.Status = WebhookEndpointStatus.Archived;
        await context.SaveChangesAsync();
        await Assert.That(await context.WebhookLocalTargetSnapshots.AsNoTracking().IgnoreQueryFilters().CountAsync())
            .IsEqualTo(4);
        await Assert.That(await context.WebhookMessages.AsNoTracking().IgnoreQueryFilters().CountAsync()).IsEqualTo(4);
        return new(first, second, future, inactive);
    }

    private static TargetGraph AddGraph(
        ExploreDbContext context, Guid tenantId, DateTimeOffset capturedAt, int maxAttempts = 8,
        Guid? instanceId = null)
    {
        var consumer = new WebhookConsumer
        {
            Id = Guid.CreateVersion7(),
            TenantId = instanceId.HasValue ? null : tenantId,
            InstanceId = instanceId,
            ConsumerKind = instanceId.HasValue ? WebhookConsumerKind.Instance : WebhookConsumerKind.Tenant,
            Name = $"Native delivery {Guid.CreateVersion7():N}",
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Local,
            ConfigurationVersion = 1,
            CreatedAt = capturedAt.UtcDateTime
        };
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = instanceId.HasValue ? null : tenantId,
            InstanceId = instanceId,
            ConsumerId = consumer.Id,
            Url = $"https://203.0.113.7/{consumer.Id:N}",
            Status = WebhookEndpointStatus.Active,
            SecretRef = $"webhooks/{consumer.Id:N}/signing",
            SecretVersion = 1,
            SecretActivatedAt = capturedAt.AddDays(-1).UtcDateTime,
            ConfigurationVersion = 1,
            MaxAttempts = maxAttempts,
            TimeoutSeconds = 15,
            CreatedAt = capturedAt.UtcDateTime
        };
        var message = WebhookMessage.Create(tenantId, "event.published", Guid.CreateVersion7().ToString("N"),
            "event", Guid.CreateVersion7(), consumer.Id, Encoding.UTF8.GetBytes("{\"native\":\"private-payload\"}"),
            "application/json", "utf-8", capturedAt.UtcDateTime, Now.AddDays(14).UtcDateTime, capturedAt.UtcDateTime);
        var plan = WebhookDeliveryPlanSnapshot.Create(tenantId, message.Id, consumer.Id, WebhookProviderMode.Local,
            "consumer-v1", "contract-v1", "standard", "retention-v1", Now.AddDays(14), Now.AddDays(30),
            Now.AddDays(90), Now.AddDays(90), Now.AddDays(30), capturedAt);
        var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, capturedAt.AddDays(-1), null, capturedAt);
        context.AddRange(consumer, endpoint, message, plan, target);
        return new(tenantId, message, endpoint, target);
    }

    private sealed record TargetGraph(
        Guid TenantId, WebhookMessage Message, WebhookEndpoint Endpoint, WebhookLocalTargetSnapshot Target);

    private sealed record SeedRows(TargetGraph First, TargetGraph Second, TargetGraph Future, TargetGraph Inactive);
}
