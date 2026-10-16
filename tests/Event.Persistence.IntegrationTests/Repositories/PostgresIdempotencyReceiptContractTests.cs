using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

/// <summary>Native receipt custody under genuine enrolled logins, without repository substitutes.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresIdempotencyReceiptContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeClaimPreservesOriginalIdentityAndDeniesRawReceiptAndOwnershipAccess()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var caller = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        await AssertRuntimeAsync(caller);
        var proposal = Proposal(tenant);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        var claim = await ClaimAsync(caller, proposal, nonce);
        await Assert.That(claim.IsOwner).IsTrue();
        await Assert.That(claim.Lookup.Record!.Id).IsEqualTo(proposal.Id);
        await Assert.That(claim.Lookup.Record.CreatedAt).IsEqualTo(proposal.CreatedAt);
        var sql = new IdempotencyReceiptSql(caller);
        foreach (string query in new[]
        {
            $"SELECT * FROM {sql.Table(typeof(IdempotencyRecord))}",
            $"UPDATE {sql.Table(typeof(IdempotencyRecord))} SET status_code = -1 WHERE false",
            $"DELETE FROM {sql.Table(typeof(IdempotencyRecord))} WHERE false",
            $"SELECT * FROM {sql.Name(PostgresIdempotencyReceiptContract.OwnershipTable)}"
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await caller.Database.ExecuteSqlRawAsync(query));
            await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    public async Task ReusingProposedIdentifierDoesNotManufactureOwnership()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var first = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        await using var second = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        byte[] ownerNonce = RandomNumberGenerator.GetBytes(32), forgedNonce = RandomNumberGenerator.GetBytes(32);
        await Assert.That((await ClaimAsync(first, proposal, ownerNonce)).IsOwner).IsTrue();
        var repeated = await ClaimAsync(second, proposal, forgedNonce);
        await Assert.That(repeated.IsOwner).IsFalse();
        await Assert.That(repeated.Lookup.Record!.Id).IsEqualTo(proposal.Id);
        await Assert.That(await CompleteAsync(second, proposal, forgedNonce, 201, "forged")).IsFalse();
        await Assert.That(await CompleteAsync(second, proposal, ownerNonce, 201, "transplanted")).IsFalse();
        await Assert.That(await CompleteAsync(first, proposal, ownerNonce, 201, "original")).IsTrue();
        await Assert.That(await CompleteAsync(first, proposal, ownerNonce, 202, "replacement")).IsFalse();
    }

    [Test]
    [Arguments("UserId")]
    [Arguments("RequestMethod")]
    [Arguments("RequestTarget")]
    [Arguments("RequestContentType")]
    [Arguments("RequestBodyHash")]
    [Arguments("PrincipalFingerprint")]
    public async Task IdentityMismatchDisclosesNoPriorRecordOrPayload(string field)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var caller = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        await ClaimAsync(caller, proposal, nonce);
        await Assert.That(await CompleteAsync(caller, proposal, nonce, 201, "private payload")).IsTrue();
        var original = IdempotencyRequestDescriptor.FromRecord(proposal);
        var changed = field switch
        {
            "UserId" => original with { UserId = "different-user" },
            "RequestMethod" => original with { RequestMethod = "PATCH" },
            "RequestTarget" => original with { RequestTarget = "/different" },
            "RequestContentType" => original with { RequestContentType = "application/cbor" },
            "RequestBodyHash" => original with { RequestBodyHash = new string('b', 64) },
            "PrincipalFingerprint" => original with { PrincipalFingerprint = new string('c', 64) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var result = await PostgresIdempotencyReceiptContract.ReadAsync(
            caller, fixture.NativeQueueEnrollment, changed, DateTime.UtcNow, CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(IdempotencyLookupOutcome.RequestMismatch);
        await Assert.That(result.Record).IsNull();
        var contender = Proposal(tenant, proposal.Key);
        contender.UserId = changed.UserId;
        contender.RequestMethod = changed.RequestMethod;
        contender.RequestTarget = changed.RequestTarget;
        contender.RequestContentType = changed.RequestContentType;
        contender.RequestBodyHash = changed.RequestBodyHash;
        contender.PrincipalFingerprint = changed.PrincipalFingerprint;
        var denied = await ClaimAsync(caller, contender, RandomNumberGenerator.GetBytes(32));
        await Assert.That(denied.IsOwner).IsFalse();
        await Assert.That(denied.Lookup.Record).IsNull();
        var replay = await PostgresIdempotencyReceiptContract.ReadAsync(
            caller, fixture.NativeQueueEnrollment, original, DateTime.UtcNow, CancellationToken.None);
        await Assert.That(replay.Record!.ResponseBody).IsEqualTo("private payload");
    }

    [Test]
    public async Task ContendedClaimsHaveExactlyOneOwnerAcrossIndependentBackends()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var first = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        await using var second = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        var contender = Proposal(tenant, proposal.Key);
        var firstNonce = RandomNumberGenerator.GetBytes(32);
        var secondNonce = RandomNumberGenerator.GetBytes(32);
        await using var transaction = await first.Database.BeginTransactionAsync();
        var owner = await ClaimAsync(first, proposal, firstNonce, true);
        await Assert.That(owner.IsOwner).IsTrue();
        var connection = (NpgsqlConnection)second.Database.GetDbConnection();
        var dispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNotice(object sender, NpgsqlNoticeEventArgs args)
        {
            if (args.Notice.MessageText == "native_receipt_contender_dispatched") dispatch.TrySetResult();
        }
        connection.Notice += OnNotice;
        try
        {
            await using var command = connection.CreateCommand();
            // The server notice is subscribed before dispatch, while the first native
            // insert is uncommitted. No timing delay or test-only capability is used.
            command.CommandText = $"""
                DO $dispatch$ BEGIN RAISE NOTICE 'native_receipt_contender_dispatched'; END $dispatch$;
                SELECT {PostgresIdempotencyReceiptContract.FunctionSql(second, "claim")}(@tenant,@proposal,@nonce,false)::text
                """;
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("proposal", NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new
            {
                contender.Id,
                Request = IdempotencyRequestDescriptor.FromRecord(contender),
                CreatedAt = contender.CreatedAt.ToString("O"),
                ExpiresAt = contender.ExpiresAt.ToString("O")
            }));
            command.Parameters.AddWithValue("nonce", secondNonce);
            Task<object?> pending = command.ExecuteScalarAsync();
            await dispatch.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(await CompleteAsync(first, proposal, firstNonce, 201, "winner")).IsTrue();
            await transaction.CommitAsync();
            using var result = JsonDocument.Parse((string)(await pending.WaitAsync(TimeSpan.FromSeconds(15)))!);
            await Assert.That(result.RootElement.GetProperty("IsOwner").GetBoolean()).IsFalse();
            await Assert.That(result.RootElement.GetProperty("Record").GetProperty("Id").GetGuid()).IsEqualTo(proposal.Id);
            await Assert.That(result.RootElement.GetProperty("Record").GetProperty("ResponseBody").GetString()).IsEqualTo("winner");
        }
        finally { connection.Notice -= OnNotice; }
    }

    [Test]
    public async Task SameTransactionRollbackRemovesClaimButDifferentTransactionCannotCompleteIt()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var caller = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        await using (var transaction = await caller.Database.BeginTransactionAsync())
        {
            await Assert.That((await ClaimAsync(caller, proposal, nonce, true)).IsOwner).IsTrue();
            await Assert.That(await CompleteAsync(caller, proposal, nonce, 201, "rolled back")).IsTrue();
            await transaction.RollbackAsync();
        }
        await using (var transaction = await caller.Database.BeginTransactionAsync())
        {
            await Assert.That((await ClaimAsync(caller, proposal, nonce, true)).IsOwner).IsTrue();
            await transaction.CommitAsync();
        }
        await using (var different = await caller.Database.BeginTransactionAsync())
        {
            await Assert.That(await CompleteAsync(caller, proposal, nonce, 201, "foreign transaction")).IsFalse();
            await different.RollbackAsync();
        }
        var retained = await PostgresIdempotencyReceiptContract.ReadAsync(caller, fixture.NativeQueueEnrollment,
            IdempotencyRequestDescriptor.FromRecord(proposal), DateTime.UtcNow, CancellationToken.None);
        await Assert.That(retained.Record!.StatusCode).IsEqualTo(IdempotencyRecord.InProgressStatusCode);
    }

    [Test]
    public async Task UnrelatedRollbackDoesNotReopenDurableClaimAndUnavailableCannotBeResurrected()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var caller = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        await ClaimAsync(caller, proposal, nonce);
        await using (var unrelated = await caller.Database.BeginTransactionAsync())
            await unrelated.RollbackAsync();
        await Assert.That((await ClaimAsync(caller, proposal, RandomNumberGenerator.GetBytes(32))).IsOwner).IsFalse();
        await Assert.That(await CompleteAsync(caller, proposal, nonce, -1, null)).IsTrue();
        await Assert.That(await CompleteAsync(caller, proposal, nonce, 201, "resurrected")).IsFalse();
        var retained = await PostgresIdempotencyReceiptContract.ReadAsync(caller, fixture.NativeQueueEnrollment,
            IdempotencyRequestDescriptor.FromRecord(proposal), DateTime.UtcNow, CancellationToken.None);
        await Assert.That(retained.Record!.StatusCode).IsEqualTo(-1);
        await Assert.That(retained.Record.ResponseBody).IsNull();
        await Assert.That(retained.Record.Id).IsEqualTo(proposal.Id);
        await Assert.That(retained.Record.ExpiresAt).IsEqualTo(proposal.ExpiresAt);
    }

    [Test]
    public async Task CleanupIsBoundedAndRetainsExpiredAndOrphanedTenantKeyFences()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), orphan = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        await using var other = await OpenAsync(PrimaryDatabaseRole.Runtime, orphan);
        await using var global = await OpenAsync(PrimaryDatabaseRole.Global);
        var first = Proposal(tenant);
        var second = Proposal(tenant);
        var third = Proposal(orphan);
        DateTime cutoff = new(first.ExpiresAt.Ticks - first.ExpiresAt.Ticks % 10, DateTimeKind.Utc);
        first.ExpiresAt = cutoff;
        second.ExpiresAt = cutoff;
        third.ExpiresAt = cutoff;
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        await ClaimAsync(runtime, first, nonce);
        await ClaimAsync(runtime, second, RandomNumberGenerator.GetBytes(32));
        await ClaimAsync(other, third, RandomNumberGenerator.GetBytes(32));
        var expired = await PostgresIdempotencyReceiptContract.ReadAsync(runtime, fixture.NativeQueueEnrollment,
            IdempotencyRequestDescriptor.FromRecord(first), cutoff, CancellationToken.None);
        await Assert.That(expired.Record!.StatusCode).IsEqualTo(-1);
        await Assert.That(expired.Record.ResponseBody).IsNull();
        var tenants = await PostgresIdempotencyReceiptContract.GetExpiredTenantIdsAsync(
            global, fixture.NativeQueueEnrollment, cutoff, 1000, CancellationToken.None);
        await Assert.That(tenants.Contains(orphan)).IsTrue();
        await Assert.That(await PostgresIdempotencyReceiptContract.CountExpiredAsync(
            runtime, fixture.NativeQueueEnrollment, tenant, cutoff, 1, CancellationToken.None)).IsEqualTo(1);
        await Assert.That(await PostgresIdempotencyReceiptContract.ScrubExpiredAsync(
            runtime, fixture.NativeQueueEnrollment, tenant, cutoff, 1, CancellationToken.None)).IsEqualTo(1);
        await Assert.That(await PostgresIdempotencyReceiptContract.ScrubExpiredAsync(
            runtime, fixture.NativeQueueEnrollment, tenant, cutoff, 1000, CancellationToken.None)).IsEqualTo(1);
        await Assert.That(await PostgresIdempotencyReceiptContract.CountExpiredAsync(
            runtime, fixture.NativeQueueEnrollment, tenant, cutoff, 1000, CancellationToken.None)).IsEqualTo(0);
        await Assert.That(await CompleteAsync(runtime, first, nonce, 201, "late")).IsFalse();
        await Assert.That((await ClaimAsync(runtime, first, nonce)).IsOwner).IsFalse();
        var retained = await PostgresIdempotencyReceiptContract.ReadAsync(runtime, fixture.NativeQueueEnrollment,
            IdempotencyRequestDescriptor.FromRecord(first), DateTime.UtcNow, CancellationToken.None);
        await Assert.That(retained.Record!.Id).IsEqualTo(first.Id);
        await Assert.That(retained.Record.StatusCode).IsEqualTo(-1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PostgresIdempotencyReceiptContract.GetExpiredTenantIdsAsync(
                global, fixture.NativeQueueEnrollment, cutoff, 1001, CancellationToken.None));
    }

    [Test]
    public async Task LockedReceiptDoesNotBlockBoundedScrubbingOfAnotherReceipt()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var first = Proposal(tenant);
        await ClaimAsync(runtime, first, RandomNumberGenerator.GetBytes(32));
        await ClaimAsync(runtime, Proposal(tenant), RandomNumberGenerator.GetBytes(32));
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await using var held = await migration.Database.BeginTransactionAsync();
        var sql = new IdempotencyReceiptSql(migration);
        await migration.Database.ExecuteSqlRawAsync(
            $"SELECT 1 FROM {sql.Table(typeof(IdempotencyRecord))} WHERE tenant_id = {{0}} AND id = {{1}} FOR UPDATE",
            tenant, first.Id);
        await Assert.That(await PostgresIdempotencyReceiptContract.ScrubExpiredAsync(runtime, fixture.NativeQueueEnrollment,
            tenant, DateTime.UtcNow.AddHours(2), 2, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo(1);
        await held.RollbackAsync();
    }

    [Test]
    public async Task BootstrapConsumptionIsOneTimeWithoutReplayOrCompletionAuthority()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        string jti = Guid.CreateVersion7().ToString("N");
        DateTime now = DateTime.UtcNow;
        await Assert.That(await PostgresIdempotencyReceiptContract.ConsumeAsync(runtime, fixture.NativeQueueEnrollment,
            tenant, jti, now, now.AddHours(1), CancellationToken.None)).IsTrue();
        await Assert.That(await PostgresIdempotencyReceiptContract.ConsumeAsync(runtime, fixture.NativeQueueEnrollment,
            tenant, jti, now, now.AddHours(1), CancellationToken.None)).IsFalse();
        var attemptedReplay = Proposal(tenant, "atproto-bootstrap:" + jti);
        var result = await ClaimAsync(runtime, attemptedReplay, RandomNumberGenerator.GetBytes(32));
        await Assert.That(result.IsOwner).IsFalse();
        await Assert.That(result.Lookup.Outcome).IsEqualTo(IdempotencyLookupOutcome.RequestMismatch);
        await Assert.That(result.Lookup.Record).IsNull();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherPurposesCannotClaimOrReadTenantReceipt(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = await OpenAsync(role);
        var proposal = Proposal(Guid.CreateVersion7());
        await Assert.ThrowsAsync<PostgresException>(() => ClaimAsync(caller, proposal, RandomNumberGenerator.GetBytes(32)));
        await Assert.ThrowsAsync<PostgresException>(() => PostgresIdempotencyReceiptContract.ReadAsync(
            caller, fixture.NativeQueueEnrollment, IdempotencyRequestDescriptor.FromRecord(proposal),
            DateTime.UtcNow, CancellationToken.None));
    }

    [Test]
    public async Task RuntimeTenantGucCannotDisagreeAndGlobalCannotReadOrScrub()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7(), foreign = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        await using var global = await OpenAsync(PrimaryDatabaseRole.Global);
        var proposal = Proposal(foreign);
        await Assert.ThrowsAsync<PostgresException>(() => ClaimAsync(runtime, proposal, RandomNumberGenerator.GetBytes(32)));
        await Assert.ThrowsAsync<PostgresException>(() => PostgresIdempotencyReceiptContract.GetExpiredTenantIdsAsync(
            runtime, fixture.NativeQueueEnrollment, DateTime.UtcNow, 1, CancellationToken.None));
        await Assert.ThrowsAsync<PostgresException>(() => PostgresIdempotencyReceiptContract.ScrubExpiredAsync(
            global, fixture.NativeQueueEnrollment, tenant, DateTime.UtcNow, 1, CancellationToken.None));
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {new IdempotencyReceiptSql(global).Table(typeof(IdempotencyRecord))}"));
    }

    [Test]
    public async Task ProtectedPayloadExpansionBeyondCaptureLimitIsNotTruncated()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        await ClaimAsync(runtime, proposal, nonce);
        string protectedEnvelope = new('a', 2 * 1024 * 1024);
        await Assert.That(await CompleteAsync(runtime, proposal, nonce, 201, protectedEnvelope)).IsTrue();
        var replay = await PostgresIdempotencyReceiptContract.ReadAsync(runtime, fixture.NativeQueueEnrollment,
            IdempotencyRequestDescriptor.FromRecord(proposal), DateTime.UtcNow, CancellationToken.None);
        await Assert.That(replay.Record!.ResponseBody).IsEqualTo(protectedEnvelope);
    }

    [Test]
    public async Task ExpiredStoredWinnerCannotDisclosePayloadOrBeReclaimed()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        var expired = Proposal(tenant);
        expired.CreatedAt = expired.CreatedAt.AddHours(-2);
        expired.ExpiresAt = expired.ExpiresAt.AddHours(-2);
        expired.StatusCode = 201;
        expired.ResponseBody = "expired-private-payload";
        expired.ContentType = "application/json";
        await using (var migration = fixture.CreateDbContext())
        {
            migration.Set<IdempotencyRecord>().Add(expired);
            await migration.SaveChangesAsync();
        }
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var lookup = await PostgresIdempotencyReceiptContract.ReadAsync(runtime, fixture.NativeQueueEnrollment,
            IdempotencyRequestDescriptor.FromRecord(expired), DateTime.UtcNow, CancellationToken.None);
        await Assert.That(lookup.Record!.StatusCode).IsEqualTo(-1);
        await Assert.That(lookup.Record.ResponseBody).IsNull();
        await Assert.That(lookup.Record.ContentType).IsNull();
        var contender = Proposal(tenant, expired.Key);
        var claim = await ClaimAsync(runtime, contender, RandomNumberGenerator.GetBytes(32));
        await Assert.That(claim.IsOwner).IsFalse();
        await Assert.That(claim.Lookup.Record!.Id).IsEqualTo(expired.Id);
        await Assert.That(claim.Lookup.Record.CreatedAt).IsEqualTo(expired.CreatedAt);
        await Assert.That(claim.Lookup.Record.ExpiresAt).IsEqualTo(expired.ExpiresAt);
        await Assert.That(claim.Lookup.Record.ResponseBody).IsNull();
    }

    [Test]
    public async Task ClosedClaimShapeCannotInsertAnArbitraryCompletedReceipt()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var proposal = Proposal(tenant);
        await using var command = runtime.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {PostgresIdempotencyReceiptContract.FunctionSql(runtime, "claim")}(@tenant,@proposal,@nonce,false)";
        command.Parameters.Add(new NpgsqlParameter("tenant", tenant));
        command.Parameters.Add(new NpgsqlParameter("proposal", NpgsqlTypes.NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new
            {
                proposal.Id,
                Request = IdempotencyRequestDescriptor.FromRecord(proposal),
                CreatedAt = proposal.CreatedAt.ToString("O"),
                ExpiresAt = proposal.ExpiresAt.ToString("O"),
                StatusCode = 201,
                ResponseBody = "injected-completed-payload"
            })
        });
        command.Parameters.Add(new NpgsqlParameter("nonce", RandomNumberGenerator.GetBytes(32)));
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        await Assert.That((await ClaimAsync(runtime, proposal, RandomNumberGenerator.GetBytes(32))).IsOwner).IsTrue();
    }

    [Test]
    public async Task AdapterNormalizesMicrosecondsAndRetainsTheOriginalOpenTransaction()
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var runtime = await OpenAsync(PrimaryDatabaseRole.Runtime, tenant);
        var connection = runtime.Database.GetDbConnection();
        await using var transaction = await runtime.Database.BeginTransactionAsync();
        var proposal = Proposal(tenant);
        DateTime created = proposal.CreatedAt, expires = proposal.ExpiresAt;
        proposal.CreatedAt = created.AddTicks(1);
        proposal.ExpiresAt = expires.AddTicks(9);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        var claim = await ClaimAsync(runtime, proposal, nonce, true);
        await Assert.That(claim.IsOwner).IsTrue();
        await Assert.That(claim.Lookup.Record!.CreatedAt).IsEqualTo(created);
        await Assert.That(claim.Lookup.Record.ExpiresAt).IsEqualTo(expires);
        await Assert.That(await CompleteAsync(runtime, proposal, nonce, 201, "same transaction")).IsTrue();
        await Assert.That(ReferenceEquals(runtime.Database.GetDbConnection(), connection)).IsTrue();
        await Assert.That(ReferenceEquals(runtime.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(connection.State).IsEqualTo(System.Data.ConnectionState.Open);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task MissingNamespaceCannotFallBackToAnInstalledReceiptContract()
    {
        await InstallAsync();
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global, "missing_receipt_namespace"));
        await using var caller = new ExploreDbContext(builder.Options);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresIdempotencyReceiptContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
    }

    [Test]
    [Arguments("public-execute")]
    [Arguments("owner-login")]
    [Arguments("executor-set")]
    [Arguments("source-type")]
    [Arguments("source-rls")]
    [Arguments("private-column")]
    [Arguments("native-index-predicate")]
    [Arguments("native-index-order")]
    [Arguments("body-forged-stamp")]
    [Arguments("routine-overload")]
    [Arguments("missing-routine")]
    [Arguments("standing-create")]
    public async Task ReadinessAndReinstallationRejectDriftWithoutRepair(string drift)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var sql = new IdempotencyReceiptSql(migration);
        string routine = PostgresIdempotencyReceiptContract.FunctionSql(migration, "read");
        string signature = routine + "(uuid,jsonb,timestamp with time zone)";
        string table = sql.Table(typeof(IdempotencyRecord));
        const string forgedBody = "BEGIN RETURN NULL::jsonb; END;";
        string change = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "owner-login" => $"ALTER ROLE {PostgresIdempotencyReceiptContract.OwnerRole} LOGIN",
            "executor-set" => $"GRANT {PostgresIdempotencyReceiptContract.RuntimeRole} TO {sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "source-type" => $"ALTER TABLE {table} ALTER COLUMN request_target TYPE text",
            "source-rls" => $"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY",
            "private-column" => $"ALTER TABLE {sql.Name(PostgresIdempotencyReceiptContract.OwnershipTable)} ADD COLUMN copied_nonce bytea",
            "native-index-predicate" => $"DROP INDEX {sql.Name("idempotency_receipt_expiry")}; CREATE INDEX idempotency_receipt_expiry ON {table} (expires_at,id) INCLUDE (tenant_id) WHERE status_code <> 0",
            "native-index-order" => $"DROP INDEX {sql.Name("idempotency_receipt_expiry")}; CREATE INDEX idempotency_receipt_expiry ON {table} (expires_at DESC,id) INCLUDE (tenant_id) WHERE status_code <> -1",
            "body-forged-stamp" => $"""
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {PostgresIdempotencyReceiptContract.OwnerRole};
                SET LOCAL ROLE {PostgresIdempotencyReceiptContract.OwnerRole};
                CREATE OR REPLACE FUNCTION {routine}(p_tenant uuid,p_request jsonb,p_now timestamp with time zone)
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${forgedBody}$body$;
                COMMENT ON FUNCTION {signature} IS 'idempotency-receipt:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(forgedBody)))}';
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {PostgresIdempotencyReceiptContract.OwnerRole};
                """,
            "routine-overload" => $"""
                CREATE FUNCTION {routine}(p_tenant uuid) RETURNS jsonb LANGUAGE plpgsql AS $body$BEGIN RETURN NULL::jsonb; END;$body$;
                """,
            "missing-routine" => $"DROP FUNCTION {signature}",
            "standing-create" => $"GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {PostgresIdempotencyReceiptContract.OwnerRole}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await using var transaction = await migration.Database.BeginTransactionAsync();
        await migration.Database.ExecuteSqlRawAsync(change);
        await transaction.CreateSavepointAsync("drift");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresIdempotencyReceiptContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await transaction.RollbackToSavepointAsync("drift");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresIdempotencyReceiptContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
        await PostgresIdempotencyReceiptContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await PostgresIdempotencyReceiptContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private async Task<ExploreDbContext> OpenAsync(PrimaryDatabaseRole role, Guid? tenant = null)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime) builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(builder.Options)
        {
            TenantContext = tenant.HasValue ? new SelectedTenant(tenant.Value) : null
        };
        try { await context.Database.OpenConnectionAsync(); return context; }
        catch { await context.DisposeAsync(); throw; }
    }

    private async Task AssertRuntimeAsync(ExploreDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT session_user::text,rolsuper,rolbypassrls,rolcreatedb,rolcreaterole,rolreplication
            FROM pg_catalog.pg_roles WHERE rolname = session_user
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo(fixture.NativeQueueEnrollment.RuntimeLogin);
        for (int i = 1; i <= 5; i++) await Assert.That(reader.GetBoolean(i)).IsFalse();
    }

    private static IdempotencyRecord Proposal(Guid tenant, string? key = null)
    {
        DateTime now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Key = key ?? $"native-receipt-{Guid.CreateVersion7():N}",
            UserId = "original-user",
            RequestMethod = "POST",
            RequestTarget = "/api/native-receipt",
            RequestContentType = "application/json",
            RequestBodyHash = new string('a', 64),
            PrincipalFingerprint = new string('d', 64),
            CreatedAt = now,
            ExpiresAt = now.AddHours(1)
        };
    }

    private Task<PostgresIdempotencyReceiptContract.NativeClaim> ClaimAsync(
        ExploreDbContext context, IdempotencyRecord proposal, byte[] nonce, bool transactionBound = false) =>
        PostgresIdempotencyReceiptContract.ClaimAsync(context, fixture.NativeQueueEnrollment,
            proposal, nonce, transactionBound, CancellationToken.None);

    private Task<bool> CompleteAsync(
        ExploreDbContext context, IdempotencyRecord record, byte[] nonce, int status, string? body) =>
        PostgresIdempotencyReceiptContract.CompleteAsync(context, fixture.NativeQueueEnrollment,
            record.TenantId, record.Id, nonce, status, body, body is null ? null : "application/json",
            DateTime.UtcNow, CancellationToken.None);

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
