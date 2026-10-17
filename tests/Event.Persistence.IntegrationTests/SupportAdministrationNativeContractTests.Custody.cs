using System.Data;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Event.Persistence.IntegrationTests;

public sealed partial class SupportAdministrationNativeContractTests
{
    [Test]
    public async Task AdditionalPrivateAttemptConstraintFailsBeforeDataObservation()
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await setup.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment);
        await using (var transaction = await setup.Database.BeginTransactionAsync())
        {
            await setup.Database.ExecuteSqlRawAsync($"""
                ALTER TABLE {new HumanPrincipalSourceSql(setup).Name("support_administration_attempt")}
                    ADD CONSTRAINT support_attempt_unexpected CHECK (attempt > 0)
                """);
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
            await Assert.That(error!.MessageText).IsEqualTo("support_administration_private_seal_drift");
            await transaction.RollbackAsync();
        }
        await Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments(SupportAccessSessionStatusEnum.PendingApproval)]
    [Arguments(SupportAccessSessionStatusEnum.Stopped)]
    public async Task WrongSessionStatusUniquePredicateFailsBeforeDataObservation(SupportAccessSessionStatusEnum status)
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await setup.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment);
        var sql = new HumanPrincipalSourceSql(setup);
        string table = Table(setup, "Sessions");
        string index = await setup.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.format('%I.%I', n.nspname, c.relname) AS "Value"
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE i.indrelid = pg_catalog.to_regclass({0})
                AND i.indisunique AND i.indpred IS NOT NULL
            """, table).SingleAsync();
        await using (var transaction = await setup.Database.BeginTransactionAsync())
        {
            await setup.Database.ExecuteSqlRawAsync($"DROP INDEX {index}");
            await setup.Database.ExecuteSqlRawAsync($"""
                CREATE UNIQUE INDEX support_session_wrong_status ON {table}
                    ({sql.Quote(sql.ColumnName(typeof(SupportAccessSession), nameof(SupportAccessSession.ActorUserId)))})
                    WHERE {sql.Quote(sql.ColumnName(typeof(SupportAccessSession), nameof(SupportAccessSession.StatusId)))}
                        = {(int)status}
                    AND {sql.Quote(sql.ColumnName(typeof(SupportAccessSession), nameof(SupportAccessSession.EndedAtUtc)))} IS NULL
                """);
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
            await Assert.That(error!.MessageText).IsEqualTo("support_administration_source_key_drift");
            await transaction.RollbackAsync();
        }
        await Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments("Sessions")]
    [Arguments("Audits")]
    [Arguments("Targets")]
    [Arguments("Members")]
    public async Task EveryZeroRowWriterParticipatesInTheCompleteDataPredicateEpoch(string target)
    {
        await SeedAsync();
        await using var writer = fixture.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync($"DELETE FROM {Table(writer, target)} WHERE false");
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {new HumanPrincipalSourceSql(contender).Name(Native.SourceTable)} WHERE id = 1 FOR UPDATE NOWAIT"));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await attempt.RollbackAsync();
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("Sessions")]
    [Arguments("Audits")]
    [Arguments("Targets")]
    [Arguments("Members")]
    public async Task CommittedWriterAfterSerializableSnapshotCannotHideBehindEmptyOrLimitedReads(string target)
    {
        var source = await SeedAsync();
        var request = new Native.Request("List", source.Actor, source.Tenant, TargetTenantId: source.Tenant, Limit: 1);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await using (var writer = fixture.CreateDbContext())
            await writer.Database.ExecuteSqlRawAsync($"DELETE FROM {Table(writer, target)} WHERE false");
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
        await owner.RollbackAsync();
    }

    [Test]
    public async Task AcquiredEmptyListRetainsWriterExclusionThroughPrivateCompletion()
    {
        var source = await SeedAsync();
        var request = new Native.Request("List", source.Actor, source.Tenant, TargetTenantId: source.Tenant);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, new(), default);
        string applied = await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, applied, default);
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {new HumanPrincipalSourceSql(contender).Name(Native.SourceTable)} WHERE id = 1 FOR UPDATE NOWAIT"));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await attempt.RollbackAsync();
        await owner.CommitAsync();
    }

    [Test]
    [Arguments("Sessions")]
    [Arguments("Audits")]
    [Arguments("Targets")]
    [Arguments("Members")]
    public async Task RuntimeHasExecuteOnlyDataAuthorityWithoutScalarReadsOrWrites(string target)
    {
        var source = await SeedAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        foreach (string statement in new[]
        {
            $"SELECT id FROM {Table(caller, target)} LIMIT 1",
            $"DELETE FROM {Table(caller, target)} WHERE false",
            $"UPDATE {Table(caller, target)} SET id = id WHERE false"
        })
        {
            await using var attempt = await caller.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(statement));
            await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await attempt.RollbackAsync();
        }
    }

    [Test]
    [Arguments("support_administration_source")]
    [Arguments("support_administration_attempt")]
    public async Task RuntimeCannotForgeTheEpochExpectedImagesOrCompletionSeal(string relation)
    {
        var source = await SeedAsync();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await using var attempt = await caller.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => caller.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {new HumanPrincipalSourceSql(caller).Name(relation)} WHERE false"));
        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await attempt.RollbackAsync();
    }

    [Test]
    [Arguments("Sessions")]
    [Arguments("Audits")]
    [Arguments("Targets")]
    [Arguments("Members")]
    public async Task DisabledWriterIsRejectedBeforeDataObservation(string target)
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await using var transaction = await setup.Database.BeginTransactionAsync();
        await setup.Database.ExecuteSqlRawAsync($"ALTER TABLE {Table(setup, target)} DISABLE TRIGGER support_administration_changed");
        // Inspect within the same deployment transaction, so no other session waits
        // on its DDL lock. Readiness checks do not grant the setup login DATA execution.
        await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("read")]
    [Arguments("compile")]
    [Arguments("apply")]
    [Arguments("complete")]
    public async Task BodyAndMatchingSelfHashReplacementStillFailsExactManagedAttestation(string function)
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await using var transaction = await setup.Database.BeginTransactionAsync();
        string signature = function is "compile" or "complete" ? "jsonb,jsonb" : "jsonb";
        string arguments = function switch
        {
            "compile" => "p_request jsonb,p_accepted jsonb",
            "complete" => "p_request jsonb,p_disclosed jsonb",
            _ => "p_request jsonb"
        };
        const string body = "BEGIN RETURN '{}'::jsonb; END;";
        await using var bodyCommand = setup.Database.GetDbConnection().CreateCommand();
        bodyCommand.Transaction = transaction.GetDbTransaction();
        bodyCommand.CommandText = $"""
            CREATE OR REPLACE FUNCTION {Native.FunctionSql(setup, function)}({arguments}) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
            """;
        await bodyCommand.ExecuteNonQueryAsync();
        bodyCommand.CommandText = $"""
            DO $comment$ BEGIN
                EXECUTE pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                    '{Native.FunctionSql(setup, function)}({signature})',
                    'support-administration:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to($value${body}$value$,'UTF8')),'hex'));
            END $comment$;
            """;
        await bodyCommand.ExecuteNonQueryAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
        await Assert.That(error!.MessageText).IsEqualTo("support_administration_exact_body_drift");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WidenedRuntimeOrOwnerPrivilegesFailBeforeObservation(bool owner)
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await using var transaction = await setup.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(setup);
        string role = owner ? Native.OwnerRole : fixture.NativeQueueEnrollment.RuntimeLogin;
        await setup.Database.ExecuteSqlRawAsync($"GRANT DELETE ON {Table(setup, "Sessions")} TO {sql.Quote(role)}");
        await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task MissingDeferredPhysicalSealIsRejectedBeforeAcquisition()
    {
        await SeedAsync();
        await using var setup = fixture.CreateDbContext();
        await using var transaction = await setup.Database.BeginTransactionAsync();
        await setup.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {new HumanPrincipalSourceSql(setup).Name("support_administration_attempt")} DISABLE TRIGGER support_administration_settlement");
        await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CompletionErasesPrivateReasonAndAuditImagesButRetainsPhysicalTombstone()
    {
        var source = await SeedAsync();
        var request = Start(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var transactionCommand = caller.Database.GetDbConnection().CreateCommand();
        transactionCommand.Transaction = owner.GetDbTransaction();
        transactionCommand.CommandText = "SELECT pg_catalog.txid_current()";
        long transactionId = (long)(await transactionCommand.ExecuteScalarAsync())!;
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, default);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, Mutation(), default);
        string applied = await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, default);
        await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, applied, default);
        await owner.CommitAsync();
        await using var observer = fixture.CreateDbContext();
        string row = await observer.Database.SqlQueryRaw<string>(
            $"SELECT pg_catalog.row_to_json(a)::text AS \"Value\" FROM {new HumanPrincipalSourceSql(observer).Name("support_administration_attempt")} a WHERE attempt = {{0}}",
            transactionId).SingleAsync();
        var tombstone = JsonNode.Parse(row)!;
        await Assert.That(tombstone["completed"]!.GetValue<bool>()).IsTrue();
        await Assert.That(tombstone["applied"]!.GetValue<bool>()).IsTrue();
        foreach (string field in new[] { "request", "preimage", "postimage" })
            await Assert.That(tombstone[field]!.AsObject().Count).IsEqualTo(0);
        await Assert.That(tombstone["plan"]!.AsArray().Count).IsEqualTo(0);
        await Assert.That(tombstone["result"]).IsNull();
    }

    private static string Table(ExploreDbContext context, string target) =>
        new HumanPrincipalSourceSql(context).Table(target switch
        {
            "Sessions" => typeof(SupportAccessSession),
            "Audits" => typeof(SupportAccessAuditEvent),
            "Targets" => typeof(Tenant),
            "Members" => typeof(TenantUser),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        });
}
