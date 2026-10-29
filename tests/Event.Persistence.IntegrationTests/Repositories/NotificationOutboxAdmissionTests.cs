using Explore.Persistence.Database.ProviderPrimitives;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class NotificationOutboxAdmissionTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime ClaimedAt = new(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task EnrolledRuntimeLoadsAndSettlesOnlyItsOriginalNotificationClaim()
    {
        await fixture.ResetAsync();
        var (tenant, seeded) = await SeedAsync("publication");
        var admission = new NotificationOutboxAdmission(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(Explore.Persistence.Database.PrimaryDatabaseRole.Global)),
            TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, DateTime.UtcNow, default)).Single();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            repository.GetClaimedNotificationAsync(seeded.Id, claim.ProcessingLeaseExpiresAt));
        await Assert.ThrowsAsync<PostgresException>(() => runtime.OutboxMessages.AsNoTracking().ToListAsync());
        var authority = new NotificationOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, default);
        var loaded = await repository.GetClaimedNotificationAsync(seeded.Id, claim.ProcessingLeaseExpiresAt);
        await Assert.That(loaded).IsNotNull();
        await Assert.That(loaded!.Payload).IsEqualTo(seeded.Payload);
        await authority.RequireAsync(loaded, default);
        var source = await new EventRepository(runtime, nativeEnrollment: fixture.NativeQueueEnrollment)
            .GetAuthorizationTargetByIdAsync(claim.AggregateId, default);
        await Assert.That(source).IsNotNull();
        await Assert.That(source!.Id).IsEqualTo(claim.AggregateId);
        await Assert.That(source.TenantId).IsEqualTo(tenant);
        await using (var expectedSource = fixture.CreateDbContext())
            await Assert.That(source.Title).IsEqualTo(await expectedSource.Events.AsNoTracking()
                .Where(row => row.Id == source.Id).Select(row => row.Title).SingleAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            repository.MarkAsCompleted(Guid.CreateVersion7(), claim.ProcessingLeaseExpiresAt));
        await Assert.That(await repository.MarkAsCompleted(seeded.Id, claim.ProcessingLeaseExpiresAt)).IsTrue();
        await Assert.That(await repository.MarkAsCompleted(seeded.Id, claim.ProcessingLeaseExpiresAt)).IsFalse();
        await using var observation = fixture.CreateDbContext();
        await Assert.That((await observation.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == seeded.Id)).Status)
            .IsEqualTo(OutboxMessageStatus.Completed);
    }

    [Test]
    [Arguments("public-execute")]
    [Arguments("invoker")]
    [Arguments("body")]
    public async Task ExecutionDriftFailsClosedAndCannotBeHiddenByReinstallation(string drift)
    {
        await fixture.ResetAsync();
        var (tenant, seeded) = await SeedAsync("publication");
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, DateTime.UtcNow, default)).Single();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        await new NotificationOutboxExecutionAuthority(runtime, admission).BindAsync(claim, default);
        await using var setup = fixture.CreateDbContext();
        string function = new HumanPrincipalSourceSql(setup).Name("outbox_execution");
        string signature = function + "(jsonb,boolean,integer,integer,text,boolean,integer,timestamp with time zone,text,text)";
        await setup.Database.OpenConnectionAsync();
        await using var metadata = setup.Database.GetDbConnection().CreateCommand();
        metadata.CommandText = "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure(@signature))";
        metadata.Parameters.Add(new NpgsqlParameter("signature", signature));
        string original = (string)(await metadata.ExecuteScalarAsync())!;
        try
        {
            string change = drift switch
            {
                "public-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
                "invoker" => $"ALTER FUNCTION {signature} SECURITY INVOKER",
                _ => $"""
                    CREATE OR REPLACE FUNCTION {function}(
                        p_claim jsonb,p_notification boolean,p_operation integer,p_status integer,
                        p_error text,p_retryable boolean,p_delay integer,p_failed timestamp with time zone,
                        p_expected text,p_replacement text)
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS 'BEGIN RETURN NULL; END;'
                    """
            };
            await setup.Database.ExecuteSqlRawAsync(change);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                repository.GetClaimedNotificationAsync(seeded.Id, claim.ProcessingLeaseExpiresAt));
            await Assert.That(denied!.SqlState).IsEqualTo("55000");
            var reinstall = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresOutboxExecutionContract.ApplyAsync(setup, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(original);
            await setup.Database.ExecuteSqlRawAsync($"REVOKE ALL ON FUNCTION {signature} FROM PUBLIC");
        }
        var restored = await repository.GetClaimedNotificationAsync(seeded.Id, claim.ProcessingLeaseExpiresAt);
        await Assert.That(restored!.Payload).IsEqualTo(seeded.Payload);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalJsonBytesAndDuplicateMemberDenialSurvivePostgresStorage(bool duplicateTenant)
    {
        await fixture.ResetAsync();
        var (tenantId, seeded) = await SeedAsync("publication");
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(seeded.Payload!)!;
        string members = string.Join(",\n", fields.Reverse().Select(pair =>
            JsonSerializer.Serialize(pair.Key) + " : " + pair.Value.GetRawText()));
        string original = "{\n" + members + ",\n\"ByteSentinel\" : \"\\u0645\""
            + (duplicateTenant ? ",\n\"TenantId\" : \"" + tenantId.ToString("D") + "\"" : string.Empty)
            + "\n}\n";
        await using (var writer = fixture.CreateDbContext())
        {
            await writer.OutboxMessages.Where(row => row.Id == seeded.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Payload, original));
        }
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claims = await admission.ClaimDueAsync(10, ClaimedAt, default);
        if (duplicateTenant)
        {
            await Assert.That(claims).IsEmpty();
            return;
        }
        var claim = claims.Single();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        var authority = new NotificationOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, default);
        OutboxMessage claimed = (await repository.GetClaimedNotificationAsync(
            seeded.Id, claim.ProcessingLeaseExpiresAt))!;
        await Assert.That(claimed.Payload).IsEqualTo(original);
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(original)));
        await Assert.That(string.Equals(claim.PayloadDigest, digest, StringComparison.OrdinalIgnoreCase)).IsTrue();
        await authority.RequireAsync(claimed, default);
        await Assert.That(await repository.TryReplaceProcessingPayloadAsync(
            seeded.Id, claim.ProcessingLeaseExpiresAt, original + " ", original + "\n")).IsFalse();
        await Assert.That(await repository.TryReplaceProcessingPayloadAsync(
            seeded.Id, claim.ProcessingLeaseExpiresAt, original, original + "\n")).IsTrue();
        var changed = await repository.GetClaimedNotificationAsync(seeded.Id, claim.ProcessingLeaseExpiresAt);
        await Assert.That(changed).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RequireAsync(claimed, default));
    }

    [Test]
    public async Task NotificationAdmissionAndGenericDrain_DoNotBorrowEachOthersRoutes()
    {
        await fixture.ResetAsync();
        var (_, notification) = await SeedAsync("publication");
        await using var context = fixture.CreateDbContext();
        var privacy = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        var repository = new OutboxRepository(context);
        await repository.Create(privacy);
        var generic = await repository.GetPendingBatch(10);
        await Assert.That(generic.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { privacy.Id });
        var admitted = await new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System)
            .ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None);
        await Assert.That(admitted.Select(row => row.MessageId).ToArray()).IsEquivalentTo(new[] { notification.Id });
        context.ChangeTracker.Clear();
        await Assert.That((await context.OutboxMessages.SingleAsync(row => row.Id == privacy.Id)).Status)
            .IsEqualTo(OutboxMessageStatus.Pending);
        await context.OutboxMessages.Where(row => row.Id == notification.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(row => row.NextRetryAt, DateTime.UtcNow.AddMinutes(-1)));
        var afterDeadLetter = await repository.GetPendingBatch(10);
        await Assert.That(afterDeadLetter.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { privacy.Id });
        await Assert.That((await new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System)
            .ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task OrdinaryGlobalCapability_AdmitsIdentityButCannotReadPrivateSourcesOrPayload()
    {
        await fixture.ResetAsync();
        var (tenantId, message) = await SeedAsync("heavy");
        string role = "notification_admission_test_" + Guid.CreateVersion7().ToString("N");
        await using var metadata = fixture.CreateDbContext();
        var sql = metadata.GetService<ISqlGenerationHelper>();
        string schema = metadata.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()!.TargetSchema;
        string Table<T>()
        {
            var entity = metadata.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.GetTableName()!, schema);
        }
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var setup = connection.CreateCommand();
        setup.CommandText = $"""
            CREATE ROLE "{role}" NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
            GRANT {PostgresNotificationOutboxAdmissionContract.GlobalRole} TO "{role}" WITH INHERIT TRUE, SET FALSE;
            """;
        await setup.ExecuteNonQueryAsync();
        try
        {
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"SET LOCAL ROLE \"{role}\"";
                await command.ExecuteNonQueryAsync();
                command.CommandText = $"""
                    SELECT rolsuper OR rolbypassrls
                        OR pg_has_role(current_user, '{PostgresNotificationOutboxAdmissionContract.OwnerRole}', 'MEMBER')
                        OR pg_has_role(current_user, '{PostgresNotificationOutboxAdmissionContract.MigratorRole}', 'MEMBER')
                    FROM pg_catalog.pg_roles WHERE rolname = current_user
                    """;
                await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
                command.CommandText = $"SELECT * FROM {PostgresNotificationOutboxAdmissionContract.ClaimFunctionSql(metadata)}(10, statement_timestamp())";
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    await Assert.That(await reader.ReadAsync()).IsTrue();
                    await Assert.That(reader.GetGuid(reader.GetOrdinal("MessageId"))).IsEqualTo(message.Id);
                    await Assert.That(reader.GetGuid(reader.GetOrdinal("TenantId"))).IsEqualTo(tenantId);
                    await Assert.That(reader.FieldCount).IsEqualTo(8);
                    await Assert.That(await reader.ReadAsync()).IsFalse();
                }
                await transaction.CommitAsync();
            }
            foreach (string query in new[]
            {
                $"SELECT title FROM {Table<Explore.Domain.Event>()}",
                $"SELECT reason_code FROM {Table<EventModerationRecord>()}",
                $"SELECT safe_after_snapshot_json FROM {Table<NotificationFanoutOccurrence>()}",
                $"SELECT payload FROM {Table<OutboxMessage>()}"
            })
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"SET LOCAL ROLE \"{role}\"";
                await command.ExecuteNonQueryAsync();
                command.CommandText = query;
                var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await command.ExecuteScalarAsync());
                await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await transaction.RollbackAsync();
            }
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"DROP ROLE \"{role}\"";
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task ChangedPersistedPayload_UnderOriginalLease_CannotReplaceBoundAdmission()
    {
        await fixture.ResetAsync();
        var (tenantId, _) = await SeedAsync("publication");
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, ClaimedAt, CancellationToken.None)).Single();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        var authority = new NotificationOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, CancellationToken.None);
        var message = await repository.GetClaimedNotificationAsync(claim.MessageId, claim.ProcessingLeaseExpiresAt);
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message!.Payload!)!;
        fields["EventTitle"] = JsonSerializer.SerializeToElement("replacement under same claim");
        await Assert.That(await repository.TryReplaceProcessingPayloadAsync(
            message.Id, claim.ProcessingLeaseExpiresAt, message.Payload!, JsonSerializer.Serialize(fields))).IsTrue();
        var changed = await repository.GetClaimedNotificationAsync(message.Id, claim.ProcessingLeaseExpiresAt);
        await Assert.That(changed).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireAsync(message, CancellationToken.None));
        await using var observation = fixture.CreateDbContext();
        await Assert.That(await observation.NotificationFanoutRuns.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("publication")]
    [Arguments("light")]
    [Arguments("heavy")]
    [Arguments("occurrence")]
    public async Task PersistedSource_AdmitsOnlyOriginalClaimAndExactPayload(string route)
    {
        await fixture.ResetAsync();
        var (tenantId, message) = await SeedAsync(route);
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claims = await admission.ClaimDueAsync(10, ClaimedAt, CancellationToken.None);
        var claim = claims.Single(row => row.MessageId == message.Id);
        await Assert.That(claim.TenantId).IsEqualTo(tenantId);
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        var authority = new NotificationOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, CancellationToken.None);
        var claimed = await repository.GetClaimedNotificationAsync(
            claim.MessageId, claim.ProcessingLeaseExpiresAt, CancellationToken.None);
        await Assert.That(claimed).IsNotNull();
        await authority.RequireAsync(claimed!, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.BindAsync(claim, CancellationToken.None));

        string original = claimed!.Payload!;
        claimed.Payload = original + " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireAsync(claimed, CancellationToken.None));
        claimed.Payload = original;
        var replacement = claim.ProcessingLeaseExpiresAt.AddMinutes(1);
        await using (var competingOwner = fixture.CreateDbContext())
            await competingOwner.OutboxMessages.Where(row => row.Id == message.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.NextRetryAt, replacement));
        await Assert.That(replacement).IsNotEqualTo(claim.ProcessingLeaseExpiresAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireAsync(claimed, CancellationToken.None));
        await Assert.That(await repository.MarkAsCompleted(
            message.Id, claim.ProcessingLeaseExpiresAt)).IsFalse();
        await Assert.That(await repository.TryReplaceProcessingPayloadAsync(
            message.Id, claim.ProcessingLeaseExpiresAt, original, "{}")).IsFalse();
    }

    [Test]
    public async Task SyntheticAndUnclaimedRows_CannotEstablishExecutionAuthority()
    {
        await fixture.ResetAsync();
        var (tenantId, message) = await SeedAsync("publication");
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        var authority = new NotificationOutboxExecutionAuthority(runtime, admission);
        message.Status = OutboxMessageStatus.Processing;
        message.NextRetryAt = ClaimedAt.AddMinutes(5);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireAsync(message, CancellationToken.None));
        message.Id = Guid.CreateVersion7();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireAsync(message, CancellationToken.None));
        await using var observation = fixture.CreateDbContext();
        await Assert.That(await observation.NotificationFanoutRuns.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForeignOrUnboundRuntime_CannotBorrowAdmittedTenant(bool unbound)
    {
        await fixture.ResetAsync();
        var (_, message) = await SeedAsync("heavy");
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, ClaimedAt, CancellationToken.None)).Single();
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(Guid.CreateVersion7()));
        if (unbound) runtime.TenantContext = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new OutboxRepository(runtime, fixture.NativeQueueEnrollment).GetClaimedNotificationAsync(
                message.Id, claim.ProcessingLeaseExpiresAt, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NotificationOutboxExecutionAuthority(runtime, admission).BindAsync(claim, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NotificationOutboxExecutionAuthority(runtime, admission)
                .RequireAsync(message, CancellationToken.None));
        await using var observation = fixture.CreateDbContext();
        await Assert.That(await observation.NotificationFanoutOccurrences.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("source")]
    [Arguments("envelope")]
    public async Task PersistedWrongCustody_IsNotExportedAsAnAdmittedClaim(string mismatch)
    {
        await fixture.ResetAsync();
        var (_, message) = await SeedAsync("heavy");
        await using var context = fixture.CreateDbContext();
        var row = await context.OutboxMessages.SingleAsync(item => item.Id == message.Id);
        using var json = JsonDocument.Parse(row.Payload!);
        var tenant = json.RootElement.GetProperty("TenantId").GetGuid();
        var record = json.RootElement.GetProperty("ModerationRecordId").GetGuid();
        row.Payload = JsonSerializer.Serialize(new
        {
            TenantId = mismatch == "tenant" ? Guid.CreateVersion7() : tenant,
            ModerationRecordId = mismatch == "source" ? Guid.CreateVersion7() : record,
            Version = 1
        });
        if (mismatch == "envelope")
            row.AggregateId = Guid.CreateVersion7();
        await context.SaveChangesAsync();
        var claims = await new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System)
            .ClaimDueAsync(10, ClaimedAt, CancellationToken.None);
        await Assert.That(claims.Count).IsEqualTo(0);
        context.ChangeTracker.Clear();
        var rejected = await context.OutboxMessages.SingleAsync(item => item.Id == message.Id);
        await Assert.That(rejected.Status).IsEqualTo(OutboxMessageStatus.Failed);
        await Assert.That(await context.NotificationFanoutOccurrences.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("none")]
    [Arguments("missing")]
    [Arguments("tenant")]
    [Arguments("time")]
    [Arguments("body")]
    [Arguments("action")]
    [Arguments("entity")]
    [Arguments("ambiguous")]
    public async Task DiscoveryCorrectionAdmissionRequiresOneExactPairedAuditReceipt(string changed)
    {
        await fixture.ResetAsync();
        var (tenantId, message) = await SeedAsync("publication");
        var request = new EventDiscoveryIdentityCorrectionRequested(
            tenantId, message.AggregateId, Guid.CreateVersion7(), 1, "confirmed_alias", "same_offering");
        string payload = JsonSerializer.Serialize(request);
        var receipt = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EntityType = "EventDiscoveryIdentity",
            EntityId = message.AggregateId.ToString("D"),
            Action = "EventDiscoveryAliasConfirmed",
            NewValues = payload,
            Timestamp = message.CreatedAt
        };
        await using (var seed = fixture.CreateDbContext())
        {
            var row = await seed.OutboxMessages.SingleAsync(value => value.Id == message.Id);
            row.AggregateType = "EventDiscoveryIdentity";
            row.EventType = EventDiscoveryIdentityCorrectionRequested.EventType;
            row.Payload = payload;
            if (changed == "tenant")
            {
                var other = new Tenant
                {
                    Id = Guid.CreateVersion7(),
                    FullName = "Receipt source tenant",
                    Slug = "receipt-" + Guid.CreateVersion7().ToString("N"),
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                };
                seed.Tenants.Add(other);
                receipt.TenantId = other.Id;
            }
            else if (changed == "time")
                receipt.Timestamp = receipt.Timestamp.AddTicks(10);
            else if (changed == "body")
                receipt.NewValues = JsonSerializer.Serialize(request with { Revision = 2 });
            else if (changed == "action")
                receipt.Action = "UnrelatedAuditAction";
            else if (changed == "entity")
                receipt.EntityType = "OtherEntity";
            if (changed != "missing")
                seed.Set<AuditLog>().Add(receipt);
            if (changed == "ambiguous")
                seed.Set<AuditLog>().Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = receipt.TenantId,
                    Tenant = null!,
                    EntityType = receipt.EntityType,
                    EntityId = receipt.EntityId,
                    Action = receipt.Action,
                    NewValues = receipt.NewValues,
                    Timestamp = receipt.Timestamp
                });
            await seed.SaveChangesAsync();
        }
        var admission = new NotificationOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claims = await admission.ClaimDueAsync(10, ClaimedAt, CancellationToken.None);
        if (changed != "none")
        {
            await Assert.That(claims).IsEmpty();
            await using var verify = fixture.CreateDbContext();
            var row = await verify.OutboxMessages.AsNoTracking().SingleAsync(value => value.Id == message.Id);
            await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Failed);
            await Assert.That(row.LastError).IsEqualTo("notification_source_not_admitted");
            return;
        }
        var claim = claims.Single();
        await Assert.That(claim.SourceId).IsEqualTo(receipt.Id);
        await Assert.That(claim.TenantId).IsEqualTo(tenantId);
        await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        var authority = new NotificationOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, CancellationToken.None);
        var admitted = await new OutboxRepository(runtime, fixture.NativeQueueEnrollment).GetClaimedNotificationAsync(
            message.Id, claim.ProcessingLeaseExpiresAt, CancellationToken.None);
        await authority.RequireAsync(admitted!, CancellationToken.None);
    }

    private async Task<(Guid TenantId, OutboxMessage Message)> SeedAsync(string route)
    {
        await using var context = fixture.CreateDbContext();
        await PostgresNotificationOutboxAdmissionContract.ApplyAsync(context);
        var tenant = new Tenant
        {
            FullName = "Notification admission tenant",
            Slug = "outbox-" + Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Pii = new UserPii { Email = $"outbox-{Guid.CreateVersion7():N}@example.test", FirstName = "Admission", LastName = "Source" } };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Notification source" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var source = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Persisted publication",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!
        };
        context.Events.Add(source);
        await context.SaveChangesAsync();
        OutboxMessage message;
        if (route == "publication")
            message = EventPublishedOutboxMessageFactory.CreateNotificationFanoutOutboxMessage(
                Guid.CreateVersion7(), source, new DateTimeOffset(ClaimedAt.AddMinutes(-1)));
        else if (route == "occurrence")
        {
            var occurrence = NotificationFanoutOccurrence.Create(
                Guid.CreateVersion7(), tenant.Id, source.Id, null, ClaimedAt, ClaimedAt,
                Guid.CreateVersion7(), "[]", "{}", "{}", "event_changed", 1,
                (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired, 1, 100, ClaimedAt,
                "event", source.Id, "outbox-admission", null, 0);
            context.NotificationFanoutOccurrences.Add(occurrence);
            message = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
        }
        else
        {
            var record = route == "heavy"
                ? EventModerationRecord.CreateHeavyRedaction(Guid.CreateVersion7(), tenant.Id, source.Id,
                    user.Id, "policy", (int)EventStatusEnum.Published, null, new DateTimeOffset(ClaimedAt))
                : EventModerationRecord.CreateLightModeration(Guid.CreateVersion7(), tenant.Id, source.Id,
                    user.Id, "policy", (int)EventStatusEnum.Published, null, new DateTimeOffset(ClaimedAt));
            context.EventModerationRecords.Add(record);
            message = route == "heavy"
                ? EventModerationOutboxMessageFactory.CreateHeavyRedactionNotificationFanoutMessage(source, record)
                : EventModerationOutboxMessageFactory.CreateLightModerationNotificationFanoutMessage(
                    Guid.CreateVersion7(), source, record);
        }
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        return (tenant.Id, message);
    }

    private sealed class AdmissionFactory(PostgreSqlContainerFixture fixture)
        : INotificationOutboxAdmissionDbContextFactory
    {
        private readonly GlobalExploreDbContextFactory _global = new(
            fixture.GetNativeQueueOptions(Explore.Persistence.Database.PrimaryDatabaseRole.Global));

        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            _global.CreateDbContextAsync(cancellationToken);
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
