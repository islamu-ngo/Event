using Explore.Persistence.Database.ProviderPrimitives;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PaymentReconciliationNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("inherit")]
    [Arguments("set")]
    [Arguments("admin")]
    [Arguments("executor")]
    [Arguments("overload")]
    [Arguments("public-column")]
    public async Task NativePrivilegeDriftRejectsEntryAndReinstallWithoutRepair(string drift)
    {
        await using var migration = fixture.CreateDbContext();
        string globalLogin = "\"" + fixture.NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string runtimeLogin = "\"" + fixture.NativeQueueEnrollment.RuntimeLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string function = PostgresPaymentReconciliationContract.SnapshotFunctionSql(migration);
        string signature = function + "(timestamp with time zone)";
        string effects = Table<PaymentReconciliationEffect>(migration);
        (string inject, string restore, string observed) = drift switch
        {
            "inherit" => ($"GRANT {PostgresPaymentReconciliationContract.OwnerRole} TO {PostgresPaymentReconciliationContract.MigratorRole} WITH INHERIT TRUE",
                $"GRANT {PostgresPaymentReconciliationContract.OwnerRole} TO {PostgresPaymentReconciliationContract.MigratorRole} WITH INHERIT FALSE",
                $"SELECT inherit_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{PostgresPaymentReconciliationContract.OwnerRole}'::regrole AND member = '{PostgresPaymentReconciliationContract.MigratorRole}'::regrole"),
            "set" => ($"GRANT {PostgresPaymentReconciliationContract.GlobalRole} TO {globalLogin} WITH SET TRUE",
                $"GRANT {PostgresPaymentReconciliationContract.GlobalRole} TO {globalLogin} WITH SET FALSE",
                $"SELECT set_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{PostgresPaymentReconciliationContract.GlobalRole}'::regrole AND member = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}::regrole"),
            "admin" => ($"GRANT {PostgresPaymentReconciliationContract.GlobalRole} TO {globalLogin} WITH ADMIN TRUE",
                $"GRANT {PostgresPaymentReconciliationContract.GlobalRole} TO {globalLogin} WITH ADMIN FALSE",
                $"SELECT admin_option AS \"Value\" FROM pg_auth_members WHERE roleid = '{PostgresPaymentReconciliationContract.GlobalRole}'::regrole AND member = {Literal(fixture.NativeQueueEnrollment.GlobalLogin)}::regrole"),
            "executor" => ($"GRANT EXECUTE ON FUNCTION {signature} TO {runtimeLogin}",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM {runtimeLogin}",
                $"SELECT has_function_privilege({Literal(fixture.NativeQueueEnrollment.RuntimeLogin)}, {Literal(signature)}, 'EXECUTE') AS \"Value\""),
            "overload" => ($"CREATE FUNCTION {function}() RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {function}()",
                $"SELECT to_regprocedure({Literal(function + "()")}) IS NOT NULL AS \"Value\""),
            "public-column" => ($"GRANT SELECT (id) ON {effects} TO PUBLIC",
                $"REVOKE SELECT (id) ON {effects} FROM PUBLIC",
                $"SELECT has_column_privilege('{PostgresPaymentReconciliationContract.GlobalRole}', {Literal(effects)}, 'id', 'SELECT') AS \"Value\""),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(inject);
        try
        {
            var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
            await using var global = await factory.CreateDbContextAsync();
            foreach (string statement in new[]
            {
                $"SELECT * FROM {function}(NULL::timestamp with time zone)",
                $"SELECT * FROM {PostgresPaymentReconciliationContract.ClaimFunctionSql(migration)}(NULL::uuid, NULL::text, 0, NULL::timestamp with time zone, NULL::timestamp with time zone, NULL::timestamp with time zone, true)"
            })
            {
                PostgresException? entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await global.Database.ExecuteSqlRawAsync(statement));
                await Assert.That(entry?.SqlState).IsEqualTo("55000");
            }
            PostgresException? reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresPaymentReconciliationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall?.SqlState).IsEqualTo("55000");
            await Assert.That(await migration.Database.SqlQueryRaw<bool>(observed).SingleAsync()).IsTrue();
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(restore);
        }
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    [Test]
    public async Task MigratorCannotUsePaymentDefinersEvenWithImplicitSuperuserExecution()
    {
        await using var migration = fixture.CreateDbContext();
        PostgresException? rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
            await migration.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresPaymentReconciliationContract.SnapshotFunctionSql(migration)}(NULL::timestamp with time zone)"));
        await Assert.That(rejected?.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PartialRoleSetOrRemainingArtifactsCannotBecomeFreshBootstrap(bool renameAll)
    {
        await using var migration = fixture.CreateDbContext();
        await migration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migration.Database.BeginTransactionAsync();
            string[] roles = renameAll
                ? [PostgresPaymentReconciliationContract.OwnerRole, PostgresPaymentReconciliationContract.MigratorRole,
                    PostgresPaymentReconciliationContract.GlobalRole]
                : [PostgresPaymentReconciliationContract.OwnerRole];
            foreach (string role in roles)
                await migration.Database.ExecuteSqlRawAsync(
                    $"ALTER ROLE {role} RENAME TO \"payment_drift_{Guid.CreateVersion7():N}\"");
            await transaction.CreateSavepointAsync("drift");
            try
            {
                PostgresException? rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await migration.Database.ExecuteSqlRawAsync(
                        PostgresPaymentReconciliationContract.BuildSql(migration, fixture.NativeQueueEnrollment)));
                await Assert.That(rejected?.SqlState).IsEqualTo("55000");
                await transaction.RollbackToSavepointAsync("drift");
                foreach (string role in roles)
                    await Assert.That(await migration.Database.SqlQuery<bool>(
                        $"SELECT pg_catalog.to_regrole({role}) IS NULL AS \"Value\"").SingleAsync()).IsTrue();
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdministratorProvisioningPreservesOrdinaryMigratorRoleBoundaries(bool provisionAsAdministrator)
    {
        await using var administration = fixture.CreateDbContext();
        await administration.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await administration.Database.BeginTransactionAsync();
            string suffix = Guid.CreateVersion7().ToString("N");
            string schema = "payment_bootstrap_" + suffix;
            string migrator = "payment_creator_" + suffix;
            var enrollment = fixture.NativeQueueEnrollment with { MigratorLogin = migrator };
            await using var model = fixture.CreateDbContextForSchema(schema);
            try
            {
                await administration.Database.ExecuteSqlRawAsync($"""
                    CREATE ROLE "{migrator}" LOGIN NOSUPERUSER NOCREATEDB CREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
                    CREATE SCHEMA "{schema}" AUTHORIZATION "{migrator}";
                    """);
                foreach (Type type in new[] { typeof(PaymentReconciliationEffect), typeof(CheckoutDispatchEffect), typeof(PaymentSucceededObservation) })
                {
                    var entity = administration.Model.FindEntityType(type)!;
                    var sql = administration.GetService<ISqlGenerationHelper>();
                    string original = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                    string isolated = sql.DelimitIdentifier(entity.GetTableName()!, schema);
                    await administration.Database.ExecuteSqlRawAsync($"""
                        CREATE TABLE {isolated} (LIKE {original} INCLUDING ALL);
                        ALTER TABLE {isolated} OWNER TO "{migrator}";
                        """);
                }
                foreach (string role in new[]
                {
                    PostgresPaymentReconciliationContract.OwnerRole, PostgresPaymentReconciliationContract.MigratorRole,
                    PostgresPaymentReconciliationContract.GlobalRole
                })
                    await administration.Database.ExecuteSqlRawAsync(
                        $"ALTER ROLE {role} RENAME TO \"payment_previous_{Guid.CreateVersion7():N}\"");

                if (provisionAsAdministrator)
                    await administration.Database.ExecuteSqlRawAsync(PostgresPaymentReconciliationContract.BuildSql(model, enrollment));
                await administration.Database.ExecuteSqlRawAsync($"SET SESSION AUTHORIZATION \"{migrator}\"");
                await Assert.That(await administration.Database.SqlQueryRaw<bool>("""
                    SELECT session_user = current_user AND NOT rolsuper AND rolcreaterole
                        AND NOT rolbypassrls AS "Value"
                    FROM pg_catalog.pg_roles WHERE rolname = session_user
                    """).SingleAsync()).IsTrue();
                if (!provisionAsAdministrator)
                {
                    await transaction.CreateSavepointAsync("ordinary_bootstrap");
                    PostgresException? rejection = null;
                    try
                    {
                        await administration.Database.ExecuteSqlRawAsync(PostgresPaymentReconciliationContract.BuildSql(model, enrollment));
                    }
                    catch (PostgresException exception)
                    {
                        rejection = exception;
                    }
                    await transaction.RollbackToSavepointAsync("ordinary_bootstrap");
                    await Assert.That(rejection).IsNotNull();
                    await Assert.That(rejection!.SqlState).IsEqualTo("55000");
                    await Assert.That(rejection.MessageText)
                        .IsEqualTo("payment_reconciliation_administrator_bootstrap_required");
                    await Assert.That(await administration.Database.SqlQueryRaw<int>($"""
                        SELECT count(*)::integer AS "Value" FROM pg_catalog.pg_roles
                        WHERE rolname IN ('{PostgresPaymentReconciliationContract.OwnerRole}',
                            '{PostgresPaymentReconciliationContract.MigratorRole}',
                            '{PostgresPaymentReconciliationContract.GlobalRole}')
                        """).SingleAsync()).IsEqualTo(0);
                    return;
                }
                await administration.Database.ExecuteSqlRawAsync(PostgresPaymentReconciliationContract.BuildSql(model, enrollment));
                await administration.Database.ExecuteSqlRawAsync(PostgresPaymentReconciliationContract.BuildSql(model, enrollment));
                await Assert.That(await administration.Database.SqlQueryRaw<bool>($"""
                    SELECT NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_auth_members edge
                        JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                        JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                        WHERE member.rolname = {Literal(migrator)}
                            AND parent.rolname IN ('{PostgresPaymentReconciliationContract.OwnerRole}',
                                '{PostgresPaymentReconciliationContract.GlobalRole}'))
                        AND EXISTS (
                            SELECT 1 FROM pg_catalog.pg_auth_members edge
                            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                            WHERE parent.rolname = '{PostgresPaymentReconciliationContract.MigratorRole}'
                                AND member.rolname = {Literal(migrator)}
                                AND NOT edge.inherit_option AND edge.set_option AND NOT edge.admin_option) AS "Value"
                    """).SingleAsync()).IsTrue();
            }
            finally
            {
                await transaction.RollbackAsync();
                await administration.Database.ExecuteSqlRawAsync("RESET SESSION AUTHORIZATION");
            }
        });
    }

    [Test]
    public async Task NativeReinstallRetainsExecuteOnlyAttestationAndNoServerTokenOverload()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresPaymentReconciliationContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await PostgresPaymentReconciliationContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string function = PostgresPaymentReconciliationContract.ClaimFunctionSql(seed);
        string signature = $"{function}(uuid,text,integer,timestamp with time zone,timestamp with time zone,timestamp with time zone,boolean)";
        string obsoleteSignature = $"{function}(text,integer,timestamp with time zone,interval)";
        string effects = Table<PaymentReconciliationEffect>(seed);
        bool obsoleteRemoved = await seed.Database.SqlQuery<bool>(
            $"SELECT pg_catalog.to_regprocedure({obsoleteSignature}) IS NULL AS \"Value\"").SingleAsync();
        bool attested = await seed.Database.SqlQuery<bool>($"""
            SELECT function.prosecdef
                AND function.provolatile = 'v'
                AND pg_catalog.pg_get_userbyid(function.proowner) = {PostgresPaymentReconciliationContract.OwnerRole}
                AND function.proconfig @> ARRAY['search_path=pg_catalog']::text[]
                AND pg_catalog.has_function_privilege(
                    {PostgresPaymentReconciliationContract.GlobalRole}, function.oid, 'EXECUTE')
                AND NOT EXISTS (
                    SELECT 1 FROM pg_catalog.aclexplode(function.proacl) AS permission
                    WHERE permission.grantee = 0 AND permission.privilege_type = 'EXECUTE')
                AND NOT pg_catalog.has_table_privilege(
                    {PostgresPaymentReconciliationContract.GlobalRole}, {effects}, 'SELECT')
                AND NOT pg_catalog.has_table_privilege(
                    {PostgresPaymentReconciliationContract.GlobalRole}, {effects}, 'UPDATE')
                AND NOT pg_catalog.has_schema_privilege(
                    {PostgresPaymentReconciliationContract.OwnerRole}, function.pronamespace, 'CREATE') AS "Value"
            FROM pg_catalog.pg_proc AS function
            WHERE function.oid = pg_catalog.to_regprocedure({signature})
            """).SingleAsync();

        await Assert.That(obsoleteRemoved).IsTrue();
        await Assert.That(attested).IsTrue();
    }

    [Test]
    public async Task NativeRetryNeverAdmitsFreshWorkAfterIndependentRecoverySettlesOriginalAcquisition()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed);
        PaymentSeed second = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var payments = new[] { first, second }.ToDictionary(value => value.Attempt.Id);
        var recoveryAdmission = new PaymentReconciliationAdmission(new PaymentGlobalFactory(authority.Global));
        PaymentReconciliationAcquisition acquisition = PaymentReconciliationAcquisition.Create(
            "native-retry-settled", 1, Now.AddSeconds(3), TimeSpan.FromMinutes(2));
        async Task SettleCommittedAcquisitionAsync()
        {
            PaymentReconciliationClaim recovered = (await recoveryAdmission.RecoverReconciliationAcquisitionAsync(
                acquisition, Now.AddSeconds(4), CancellationToken.None)).Single();
            await using var scope = await new PaymentReconciliationExecutionScopeFactory(authority.Runtime)
                .CreateAsync(recovered.TenantId, CancellationToken.None);
            await Assert.That(await scope.SettleAsync(
                recovered, Success(payments[recovered.PaymentAttemptId]), CancellationToken.None)).IsTrue();
        }
        var lostAck = new LostAcquisitionAcknowledgement(acquisition.LeaseToken, null, SettleCommittedAcquisitionAsync);
        var admission = new PaymentReconciliationAdmission(
            new InterceptedGlobalFactory(authority.Global, lostAck, retryLostAcknowledgement: true));

        IReadOnlyList<PaymentReconciliationClaim> replay = await admission.ClaimDueReconciliationsAsync(
            acquisition, acquisition.ClaimedAt, CancellationToken.None);

        await Assert.That(lostAck.CommittedEffectIds.Count).IsEqualTo(1);
        await Assert.That(replay).IsEmpty();
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentReconciliationEffects.CountAsync(
            value => value.Status == OutboxMessageStatus.Pending)).IsEqualTo(1);
        await Assert.That(await seed.PaymentReconciliationEffects.CountAsync(
            value => value.Status == OutboxMessageStatus.Completed && value.AttemptCount == 1 &&
                value.ProcessingFence == 1)).IsEqualTo(1);
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(1);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeGlobalRecoversCommittedAcquisitionAfterLostAcknowledgementWithoutFreshClaims(bool cancelAfterCommit)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        _ = await SeedPaymentAsync(seed);
        _ = await SeedPaymentAsync(seed);
        _ = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        PaymentReconciliationAcquisition acquisition = PaymentReconciliationAcquisition.Create(
            "native-lost-acquisition", 2, Now.AddSeconds(3), TimeSpan.FromMinutes(2));
        using var cancellation = new CancellationTokenSource();
        var lostAck = new LostAcquisitionAcknowledgement(acquisition.LeaseToken, cancelAfterCommit ? cancellation : null);
        var admission = new PaymentReconciliationAdmission(new InterceptedGlobalFactory(authority.Global, lostAck));

        IReadOnlyList<PaymentReconciliationClaim> recovered = await admission.ClaimDueReconciliationsAsync(
            acquisition, acquisition.ClaimedAt, cancellation.Token);
        IReadOnlyList<PaymentReconciliationClaim> replay = await admission.ClaimDueReconciliationsAsync(
            acquisition, Now.AddSeconds(4), CancellationToken.None);

        await Assert.That(lostAck.CommittedEffectIds.Count).IsEqualTo(2);
        await Assert.That(recovered.Select(value => value.EffectId).Order()
            .SequenceEqual(lostAck.CommittedEffectIds.Order())).IsTrue();
        await Assert.That(replay.SequenceEqual(recovered)).IsTrue();
        await Assert.That(cancellation.IsCancellationRequested).IsEqualTo(cancelAfterCommit);
        await Assert.That(await admission.ClaimDueReconciliationsAsync(
            acquisition with { LeaseOwner = "other-owner" }, Now.AddSeconds(4), CancellationToken.None)).IsEmpty();
        await Assert.That(await admission.RecoverReconciliationAcquisitionAsync(
            acquisition with { LeaseToken = Guid.CreateVersion7() }, Now.AddSeconds(4), CancellationToken.None)).IsEmpty();
        seed.ChangeTracker.Clear();
        PaymentReconciliationEffect[] effects = await seed.PaymentReconciliationEffects.AsNoTracking().ToArrayAsync();
        await Assert.That(effects.Count(value => value.Status == OutboxMessageStatus.Pending)).IsEqualTo(1);
        foreach (PaymentReconciliationEffect effect in effects.Where(value => value.Status == OutboxMessageStatus.Processing))
        {
            await Assert.That(effect.ProcessingLeaseToken).IsEqualTo(acquisition.LeaseToken);
            await Assert.That(effect.UpdatedAt).IsEqualTo(acquisition.ClaimedAt);
            await Assert.That(effect.ProcessingLeaseExpiresAt).IsEqualTo(acquisition.LeaseExpiresAt);
            await Assert.That(effect.ProcessingFence).IsEqualTo(1);
            await Assert.That(effect.AttemptCount).IsEqualTo(1);
        }
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireDeniedAsync(global, $"SELECT id FROM {Table<PaymentReconciliationEffect>(seed)}");
        await RequireDeniedAsync(global, $"SELECT provider_idempotency_key FROM {authority.PaymentTable}");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeServiceRecoversLostAcquisitionAndHonorsCancellationBeforeTenantExecution(bool cancelAfterCommit)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        _ = await SeedPaymentAsync(seed);
        _ = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        using var cancellation = new CancellationTokenSource();
        var lostAck = new LostAcquisitionAcknowledgement(null, cancelAfterCommit ? cancellation : null);
        var admission = new PaymentReconciliationAdmission(new InterceptedGlobalFactory(authority.Global, lostAck));
        var checkout = Substitute.For<IHostedCheckoutSessionRetriever>();
        checkout.RetrieveAsync(Arg.Any<HostedCheckoutRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => cancelAfterCommit
                ? throw new InvalidOperationException("Cancelled acquisition must not reach the provider.")
                : HostedCheckoutRetrieveResult.Succeeded(
                    Session(call.Arg<HostedCheckoutRetrieveRequest>()!.ProviderCheckoutSessionId), "checkout-read"));
        var payment = Substitute.For<IPaymentIntentRetriever>();
        payment.RetrievePaymentIntentAsync(Arg.Any<PaymentIntentRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => PaymentIntentRetrieveResult.Succeeded(
                new PaymentIntentObservation(call.Arg<PaymentIntentRetrieveRequest>()!.PaymentIntentId,
                    1_000, "EUR", 75, PaymentIntentStatus.Succeeded), "payment-read"));
        var service = new RegistrationPaymentReconciliationService(
            admission, new PaymentReconciliationExecutionScopeFactory(authority.Runtime),
            checkout, payment, new FixedTimeProvider(Now.AddSeconds(3)));

        if (cancelAfterCommit)
        {
            await Assert.That(async () => await service.ReconcileDueAsync(
                new("native-service-lost-ack", 1), cancellation.Token)).Throws<OperationCanceledException>();
        }
        else
        {
            RegistrationPaymentReconciliationResult result = await service.ReconcileDueAsync(
                new("native-service-lost-ack", 1), cancellation.Token);
            await Assert.That(result.Claimed).IsEqualTo(1);
            await Assert.That(result.Succeeded).IsEqualTo(1);
            await Assert.That(result.Stale).IsEqualTo(0);
        }
        await Assert.That(lostAck.CommittedEffectIds.Count).IsEqualTo(1);
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentReconciliationEffects.CountAsync(
            value => value.Status == OutboxMessageStatus.Pending)).IsEqualTo(1);
        PaymentReconciliationEffect owned = await seed.PaymentReconciliationEffects.AsNoTracking()
            .SingleAsync(value => value.Id == lostAck.CommittedEffectIds.Single());
        await Assert.That(owned.AttemptCount).IsEqualTo(1);
        await Assert.That(owned.ProcessingFence).IsEqualTo(1);
        await Assert.That(owned.Status).IsEqualTo(
            cancelAfterCommit ? OutboxMessageStatus.Processing : OutboxMessageStatus.Completed);
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(cancelAfterCommit ? 0 : 1);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(cancelAfterCommit ? 0 : 1);
    }

    [Test]
    public async Task NativeAcquisitionRecoveryRejectsOwnerEpochExpiryAndReclaimedTenantClaims()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed payment = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var admission = new PaymentReconciliationAdmission(new PaymentGlobalFactory(authority.Global));
        PaymentReconciliationAcquisition acquisition = PaymentReconciliationAcquisition.Create(
            "native-acquisition-owner", 1, Now.AddSeconds(3), TimeSpan.FromMinutes(1));
        PaymentReconciliationClaim original = (await admission.ClaimDueReconciliationsAsync(
            acquisition, acquisition.ClaimedAt, CancellationToken.None)).Single();
        foreach (PaymentReconciliationAcquisition changed in new[]
        {
            acquisition with { LeaseOwner = "other-owner" },
            acquisition with { LeaseToken = Guid.CreateVersion7() },
            acquisition with { ClaimedAt = Now.AddSeconds(4) },
            acquisition with { LeaseExpiresAt = Now.AddMinutes(3) }
        })
        {
            await Assert.That(await admission.RecoverReconciliationAcquisitionAsync(
                changed, Now.AddSeconds(5), CancellationToken.None)).IsEmpty();
        }
        await Assert.That(await admission.RecoverReconciliationAcquisitionAsync(
            acquisition, acquisition.LeaseExpiresAt, CancellationToken.None)).IsEmpty();
        PaymentReconciliationAcquisition successor = PaymentReconciliationAcquisition.Create(
            "native-successor-owner", 1, acquisition.LeaseExpiresAt, TimeSpan.FromMinutes(1));
        PaymentReconciliationClaim reclaimed = (await admission.ClaimDueReconciliationsAsync(
            successor, successor.ClaimedAt, CancellationToken.None)).Single();
        await Assert.That(reclaimed.EffectId).IsEqualTo(original.EffectId);
        await Assert.That(reclaimed.ProcessingFence).IsEqualTo(2);
        await Assert.That(reclaimed.AttemptCount).IsEqualTo(2);
        await Assert.That(await admission.RecoverReconciliationAcquisitionAsync(
            acquisition, Now.AddSeconds(5), CancellationToken.None)).IsEmpty();
        await using var runtime = await authority.Runtime.CreateDbContextAsync(payment.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(runtime);
        await Assert.That(await repository.GetReconciliationAttemptAsync(
            original, successor.ClaimedAt, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetReconciliationAttemptAsync(
            reclaimed with { TenantId = Guid.CreateVersion7() }, successor.ClaimedAt, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetReconciliationAttemptAsync(
            reclaimed, successor.ClaimedAt, CancellationToken.None)).IsNotNull();
    }

    [Test]
    public async Task NativeGlobalAcquisitionReconcilesBothTenantsWithoutPrivateTablePrivileges()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed);
        PaymentSeed second = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId))
        {
            await Assert.That(await runtime.PaymentAttempts.CountAsync()).IsEqualTo(1);
            await Assert.That(await runtime.PaymentAttempts.AnyAsync(row => row.Id == second.Attempt.Id)).IsFalse();
        }
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireDeniedAsync(global, $"SELECT id FROM {authority.PaymentTable}");
        await RequireDeniedAsync(global, $"SET ROLE {PostgresPaymentReconciliationContract.OwnerRole}");
        var checkout = Substitute.For<IHostedCheckoutSessionRetriever>();
        checkout.RetrieveAsync(Arg.Any<HostedCheckoutRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                HostedCheckoutRetrieveRequest? request = call.Arg<HostedCheckoutRetrieveRequest>();
                ArgumentNullException.ThrowIfNull(request);
                return HostedCheckoutRetrieveResult.Succeeded(Session(request.ProviderCheckoutSessionId), "checkout-read");
            });
        var payment = Substitute.For<IPaymentIntentRetriever>();
        payment.RetrievePaymentIntentAsync(Arg.Any<PaymentIntentRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                PaymentIntentRetrieveRequest? request = call.Arg<PaymentIntentRetrieveRequest>();
                ArgumentNullException.ThrowIfNull(request);
                return PaymentIntentRetrieveResult.Succeeded(
                    new PaymentIntentObservation(request.PaymentIntentId, 1_000, "EUR", 75, PaymentIntentStatus.Succeeded),
                    "payment-read");
            });
        var service = authority.CreateService(checkout, payment);

        RegistrationPaymentReconciliationResult result = await service.ReconcileDueAsync(
            new("native-global-payment", 2, TimeSpan.FromMinutes(2)), CancellationToken.None);

        await Assert.That(result.Claimed).IsEqualTo(2);
        await Assert.That(result.Succeeded).IsEqualTo(2);
        await Assert.That(result.Stale).IsEqualTo(0);
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(2);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(2);
        await Assert.That(await seed.PaymentAttempts.AllAsync(row =>
            row.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded)).IsTrue();
        foreach (PaymentSeed value in new[] { first, second })
            await Assert.That(await seed.PaymentAttempts.Where(row => row.Id == value.Attempt.Id)
                .Select(row => row.ProviderIdempotencyKey).SingleAsync()).IsEqualTo(value.Attempt.ProviderIdempotencyKey);
        await RequireDeniedAsync(global, $"UPDATE {authority.PaymentTable} SET total_minor = 0");
    }

    [Test]
    public async Task NativeOrdinarySettlementPreservesIdempotencyAndExcludesForeignTenant()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true);
        PaymentSeed foreign = await SeedPaymentAsync(seed, claimed: true);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(runtime);
        PaymentAttempt? acquired = await repository.GetReconciliationAttemptAsync(first.Claim!, Now.AddSeconds(3), CancellationToken.None);
        await Assert.That(acquired!.ProviderIdempotencyKey).IsEqualTo(first.Attempt.ProviderIdempotencyKey);
        await Assert.That(await repository.GetReconciliationAttemptAsync(foreign.Claim!, Now.AddSeconds(3), CancellationToken.None)).IsNull();
        await Assert.That(await repository.SettleReconciliationAsync(foreign.Claim!, Success(foreign), CancellationToken.None)).IsFalse();
        await Assert.That(await repository.SettleReconciliationAsync(
            first.Claim! with { ProcessingFence = first.Claim.ProcessingFence + 1 },
            Success(first), CancellationToken.None)).IsFalse();
        await Assert.That(await repository.SettleReconciliationAsync(
            first.Claim! with { LeaseToken = Guid.CreateVersion7() }, Success(first), CancellationToken.None)).IsFalse();
        await Assert.That(await repository.SettleReconciliationAsync(first.Claim!, Success(first), CancellationToken.None)).IsTrue();
        await Assert.That(await repository.SettleReconciliationAsync(first.Claim!, Success(first), CancellationToken.None)).IsFalse();
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(1);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
        await Assert.That(await seed.PaymentAttempts.Where(row => row.Id == first.Attempt.Id)
            .Select(row => row.ProviderIdempotencyKey).SingleAsync()).IsEqualTo(first.Attempt.ProviderIdempotencyKey);
        await Assert.That(await seed.PaymentAttempts.Where(row => row.Id == foreign.Attempt.Id)
            .Select(row => row.PaymentAttemptStatusId).SingleAsync()).IsEqualTo((int)PaymentAttemptStatusEnum.RequiresAction);
        await RequireDeniedAsync(runtime, $"ALTER TABLE {authority.PaymentTable} DISABLE ROW LEVEL SECURITY");
    }

    [Test]
    public async Task SettledPaymentOwnerCannotAuthorizeANewObservationOnTheSameContext()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(runtime);
        await Assert.That(await repository.SettleReconciliationAsync(
            first.Claim!, Success(first), CancellationToken.None)).IsTrue();

        PaymentAttempt attempt = await runtime.PaymentAttempts.SingleAsync();
        runtime.Add(PaymentSucceededObservation.Create(
            attempt, null, attempt.ProviderCheckoutSessionId!, attempt.ProviderPaymentId!, null, Now.AddSeconds(4)));
        string? state = null;
        try
        {
            await runtime.SaveChangesAsync();
        }
        catch (PostgresException exception)
        {
            state = exception.SqlState;
        }
        await Assert.That(state).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(1);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task NativeProviderUncertaintyRetainsCapturedIdempotencyAndSchedulesUnknownRetry()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        await Assert.That(await runtime.PaymentAttempts.Select(row => row.PaymentAttemptStatusId).SingleAsync())
            .IsEqualTo((int)PaymentAttemptStatusEnum.RequiresAction);
        var checkout = Substitute.For<IHostedCheckoutSessionRetriever>();
        checkout.RetrieveAsync(Arg.Any<HostedCheckoutRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutRetrieveResult.Unknown(
                new HostedCheckoutFailure("payment_provider_unavailable", HostedCheckoutFailureKind.Network)));
        var service = authority.CreateService(checkout, Substitute.For<IPaymentIntentRetriever>());

        RegistrationPaymentReconciliationResult result = await service.ReconcileDueAsync(
            new("native-unknown-payment", 1), CancellationToken.None);

        await Assert.That(result.Claimed).IsEqualTo(1);
        await Assert.That(result.Unknown).IsEqualTo(1);
        await Assert.That(result.Succeeded).IsEqualTo(0);
        await Assert.That(result.Stale).IsEqualTo(0);
        seed.ChangeTracker.Clear();
        PaymentAttempt persisted = await seed.PaymentAttempts.AsNoTracking().SingleAsync();
        await Assert.That(persisted.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Unknown);
        await Assert.That(persisted.ProviderIdempotencyKey).IsEqualTo(first.Attempt.ProviderIdempotencyKey);
        await Assert.That(persisted.ProviderCheckoutSessionId).IsEqualTo(first.Attempt.ProviderCheckoutSessionId);
        PaymentReconciliationEffect effect = await seed.PaymentReconciliationEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.Failed);
        await Assert.That(effect.UnknownAt).IsEqualTo(Now.AddSeconds(3));
        await Assert.That(effect.NextAttemptAt).IsEqualTo(Now.AddMinutes(2).AddSeconds(3));
        await Assert.That(effect.ProcessingLeaseToken).IsNull();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(0);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task NativeSettlementRejectsAClaimForAnotherPaymentAttemptWithoutEconomicEffect()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true);
        PaymentSeed other = await SeedPaymentAsync(seed, tenantId: first.TenantId);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(runtime);
        PaymentReconciliationClaim forged = first.Claim! with { PaymentAttemptId = other.Attempt.Id };

        bool settled = await repository.SettleReconciliationAsync(forged, Success(other), CancellationToken.None);

        await Assert.That(settled).IsFalse();
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(0);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(0);
        await Assert.That(await seed.PaymentAttempts.AllAsync(row =>
            row.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.RequiresAction)).IsTrue();
        await Assert.That(await seed.PaymentReconciliationEffects.Where(row => row.Id == first.Effect.Id)
            .Select(row => row.ProcessingLeaseToken).SingleAsync()).IsEqualTo(first.Claim.LeaseToken);
    }

    [Test]
    public async Task NativeFinalizationInsertFailureRollsBackPaymentObservationAndLiveClaim()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true);
        await using var authority = await CreateAuthorityAsync(seed);
        string table = Table<RegistrationFinalizationEffect>(seed);
        string constraint = $"payment_rollback_{Guid.CreateVersion7():N}";
        await seed.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {table} ADD CONSTRAINT \"{constraint}\" CHECK (registration_order_id <> '{first.Attempt.RegistrationOrderId:D}'::uuid)");
        try
        {
            await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
            var repository = new RegistrationPaymentAttemptRepository(runtime);
            string? violation = null;
            try
            {
                _ = await repository.SettleReconciliationAsync(first.Claim!, Success(first), CancellationToken.None);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
            {
                violation = postgres.ConstraintName;
            }
            await Assert.That(violation).IsEqualTo(constraint);
            seed.ChangeTracker.Clear();
            await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(0);
            await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(0);
            await Assert.That(await seed.PaymentAttempts.Select(row => row.PaymentAttemptStatusId).SingleAsync())
                .IsEqualTo((int)PaymentAttemptStatusEnum.RequiresAction);
            PaymentReconciliationEffect persisted = await seed.PaymentReconciliationEffects.AsNoTracking().SingleAsync();
            await Assert.That(persisted.Status).IsEqualTo(OutboxMessageStatus.Processing);
            await Assert.That(persisted.ProcessingLeaseToken).IsEqualTo(first.Claim!.LeaseToken);
            await Assert.That(persisted.ProcessingFence).IsEqualTo(first.Claim.ProcessingFence);
            await Assert.That(persisted.AttemptCount).IsEqualTo(first.Claim.AttemptCount);
            await Assert.That(await seed.PaymentAttempts.Select(row => row.ProviderIdempotencyKey).SingleAsync())
                .IsEqualTo(first.Attempt.ProviderIdempotencyKey);
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DROP CONSTRAINT \"{constraint}\"");
        }
    }

    [Test]
    [Arguments("attempt-count")]
    [Arguments("dispatch-id")]
    [Arguments("dispatch-observed")]
    [Arguments("dispatch-fence")]
    [Arguments("dispatch-attempts")]
    public async Task NativeLiveClaimRejectsChangedCapturedIdentityBeforeHydrationOrSettlement(string field)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(runtime);
        PaymentReconciliationClaim claim = first.Claim!;
        PaymentReconciliationClaim forged = field switch
        {
            "attempt-count" => claim with { AttemptCount = claim.AttemptCount + 1 },
            "dispatch-id" => claim with { CheckoutDispatchEffectId = Guid.CreateVersion7() },
            "dispatch-observed" => claim with { CheckoutDispatchUnknownAt = Now },
            "dispatch-fence" => claim with { CheckoutDispatchProcessingFence = 1 },
            "dispatch-attempts" => claim with { CheckoutDispatchAttemptCount = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        await Assert.That(await repository.GetReconciliationAttemptAsync(forged, Now.AddSeconds(3), CancellationToken.None)).IsNull();
        await Assert.That(await repository.SettleReconciliationAsync(forged, Success(first), CancellationToken.None)).IsFalse();

        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(0);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(0);
        await Assert.That(await seed.PaymentReconciliationEffects.Select(row => row.ProcessingLeaseToken).SingleAsync())
            .IsEqualTo(claim.LeaseToken);
        await Assert.That(await seed.PaymentAttempts.Select(row => row.PaymentAttemptStatusId).SingleAsync())
            .IsEqualTo((int)PaymentAttemptStatusEnum.RequiresAction);
    }

    [Test]
    public async Task NativeGlobalReclaimFencesExpiredOwnerAndSuccessRemainsExactlyOnce()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true);
        await using var authority = await CreateAuthorityAsync(seed);
        var admission = new PaymentReconciliationAdmission(new PaymentGlobalFactory(authority.Global));
        PaymentReconciliationClaim successor = (await admission.ClaimDueReconciliationsAsync(
            PaymentReconciliationAcquisition.Create("native-successor", 1, Now.AddMinutes(2), TimeSpan.FromMinutes(2)),
            Now.AddMinutes(2), CancellationToken.None)).Single();
        await Assert.That(successor.PaymentAttemptId).IsEqualTo(first.Attempt.Id);
        await Assert.That(successor.ProcessingFence).IsEqualTo(first.Claim!.ProcessingFence + 1);
        await Assert.That(successor.AttemptCount).IsEqualTo(first.Claim.AttemptCount + 1);
        await Assert.That(successor.LeaseToken).IsNotEqualTo(first.Claim.LeaseToken);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(runtime);
        await Assert.That(await repository.GetReconciliationAttemptAsync(first.Claim, Now.AddSeconds(3), CancellationToken.None)).IsNull();
        await Assert.That(await repository.SettleReconciliationAsync(first.Claim, Success(first), CancellationToken.None)).IsFalse();
        var decision = Success(first) with { ObservedAt = Now.AddMinutes(2).AddSeconds(1) };
        await Assert.That(await repository.SettleReconciliationAsync(successor, decision, CancellationToken.None)).IsTrue();
        await Assert.That(await repository.SettleReconciliationAsync(successor, decision, CancellationToken.None)).IsFalse();
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(1);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
        await Assert.That(await seed.PaymentAttempts.Select(row => row.ProviderIdempotencyKey).SingleAsync())
            .IsEqualTo(first.Attempt.ProviderIdempotencyKey);
    }

    [Test]
    public async Task NativeConcurrentGlobalClaimsHaveOneOwnerPerEffectAndOneEconomicObservation()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed);
        PaymentSeed second = await SeedPaymentAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        var admission = new PaymentReconciliationAdmission(new PaymentGlobalFactory(authority.Global));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimAfterReleaseAsync(string owner)
        {
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return await admission.ClaimDueReconciliationsAsync(
                PaymentReconciliationAcquisition.Create(owner, 1, Now.AddSeconds(3), TimeSpan.FromMinutes(2)),
                Now.AddSeconds(3), CancellationToken.None);
        }
        Task<IReadOnlyList<PaymentReconciliationClaim>> firstWorker = ClaimAfterReleaseAsync("native-payment-one");
        Task<IReadOnlyList<PaymentReconciliationClaim>> secondWorker = ClaimAfterReleaseAsync("native-payment-two");
        release.SetResult();
        IReadOnlyList<PaymentReconciliationClaim>[] rounds =
            await Task.WhenAll(firstWorker, secondWorker).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(rounds[0].Count).IsEqualTo(1);
        await Assert.That(rounds[1].Count).IsEqualTo(1);
        PaymentReconciliationClaim[] claims = rounds.SelectMany(round => round).ToArray();
        await Assert.That(claims.Select(claim => claim.EffectId).Distinct().Count()).IsEqualTo(2);
        var payments = new[] { first, second }.ToDictionary(value => value.Attempt.Id);
        foreach (PaymentReconciliationClaim claim in claims)
        {
            await Assert.That(claim.ProcessingFence).IsEqualTo(1);
            await Assert.That(claim.AttemptCount).IsEqualTo(1);
            await using var runtime = await authority.Runtime.CreateDbContextAsync(claim.TenantId);
            var repository = new RegistrationPaymentAttemptRepository(runtime);
            await Assert.That(await repository.SettleReconciliationAsync(claim, Success(payments[claim.PaymentAttemptId]), CancellationToken.None)).IsTrue();
            await Assert.That(await repository.SettleReconciliationAsync(claim, Success(payments[claim.PaymentAttemptId]), CancellationToken.None)).IsFalse();
        }
        seed.ChangeTracker.Clear();
        await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(2);
        await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCapturedUnknownDispatchRecoveryCommitsOrRollsBackWithItsExactLiveClaim(bool rollback)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        PaymentSeed first = await SeedPaymentAsync(seed, claimed: true, unknownDispatch: true);
        PaymentSeed other = await SeedPaymentAsync(seed, tenantId: first.TenantId, unknownDispatch: true);
        await using var authority = await CreateAuthorityAsync(seed);
        string table = Table<PaymentReconciliationEffect>(seed);
        string constraint = $"payment_unknown_rollback_{Guid.CreateVersion7():N}";
        if (rollback)
            await seed.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {table} ADD CONSTRAINT \"{constraint}\" CHECK (id <> '{first.Effect.Id:D}'::uuid OR status <> {(int)OutboxMessageStatus.Failed})");
        try
        {
            await using var scope = await new PaymentReconciliationExecutionScopeFactory(authority.Runtime)
                .CreateAsync(first.TenantId, CancellationToken.None);
            var decision = new PaymentReconciliationDecision(
                PaymentReconciliationDisposition.Retry, PaymentAttemptStatusEnum.Unknown, null, null,
                "native-dispatch-recovery", "payment_reconciliation_awaiting_idempotent_dispatch",
                Now.AddSeconds(4), Now.AddMinutes(2).AddSeconds(4));
            string? violation = null;
            PaymentReconciliationRecoveryResult? result = null;
            try
            {
                result = await scope.RecoverUnknownDispatchAsync(first.Claim!, decision, CancellationToken.None);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
            {
                violation = postgres.ConstraintName;
            }
            if (rollback)
            {
                await Assert.That(violation).IsEqualTo(constraint);
                await Assert.That(result).IsNull();
            }
            else
            {
                await Assert.That(violation).IsNull();
                await Assert.That(result!.Settled).IsTrue();
                await Assert.That(result.RequeuedDispatch).IsTrue();
                await Assert.That((await scope.RecoverUnknownDispatchAsync(
                    first.Claim!, decision, CancellationToken.None)).Settled).IsFalse();
            }
            seed.ChangeTracker.Clear();
            CheckoutDispatchEffect dispatch = await seed.CheckoutDispatchEffects.AsNoTracking()
                .SingleAsync(row => row.Id == first.Dispatch!.Id);
            await Assert.That(dispatch.Status).IsEqualTo(rollback ? OutboxMessageStatus.Unknown : OutboxMessageStatus.Failed);
            await Assert.That(dispatch.ProcessingFence).IsEqualTo(first.Claim!.CheckoutDispatchProcessingFence);
            await Assert.That(dispatch.AttemptCount).IsEqualTo(first.Claim.CheckoutDispatchAttemptCount);
            await Assert.That(dispatch.UnknownAt).IsEqualTo(first.Claim.CheckoutDispatchUnknownAt);
            await Assert.That(await seed.CheckoutDispatchEffects.Where(row => row.Id == other.Dispatch!.Id)
                .Select(row => row.Status).SingleAsync()).IsEqualTo(OutboxMessageStatus.Unknown);
            PaymentReconciliationEffect effect = await seed.PaymentReconciliationEffects.AsNoTracking()
                .SingleAsync(row => row.Id == first.Effect.Id);
            await Assert.That(effect.Status).IsEqualTo(rollback ? OutboxMessageStatus.Processing : OutboxMessageStatus.Failed);
            await Assert.That(effect.ProcessingLeaseToken).IsEqualTo(rollback ? first.Claim.LeaseToken : (Guid?)null);
            await Assert.That(await seed.PaymentAttempts.Where(row => row.Id == first.Attempt.Id)
                .Select(row => row.ProviderIdempotencyKey).SingleAsync()).IsEqualTo(first.Attempt.ProviderIdempotencyKey);
            await Assert.That(await seed.PaymentSucceededObservations.CountAsync()).IsEqualTo(0);
            await Assert.That(await seed.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(0);
        }
        finally
        {
            if (rollback)
                await seed.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DROP CONSTRAINT \"{constraint}\"");
        }
    }

    [Test]
    public async Task NativeGlobalHealthReportsOnlyCrossTenantAggregates()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        _ = await SeedPaymentAsync(seed);
        PaymentSeed unknown = await SeedPaymentAsync(seed, claimed: true);
        unknown.Effect.Retry("checkout_provider_unavailable", Now.AddSeconds(2), Now.AddSeconds(2), unknown: true);
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);
        var admission = new PaymentReconciliationAdmission(new PaymentGlobalFactory(authority.Global));

        PaymentReconciliationHealth health = await admission.GetReconciliationHealthAsync(Now.AddSeconds(3), CancellationToken.None);

        await Assert.That(health.Due).IsEqualTo(2);
        await Assert.That(health.Unknown).IsEqualTo(1);
        await Assert.That(health.Parked).IsEqualTo(0);
        await Assert.That(health.OldestDueAt).IsEqualTo(Now.AddSeconds(2));
        await Assert.That(health.ConfigurationBlocked).IsEqualTo(1);
        await Assert.That(health.DuplicateSucceededOrders).IsEqualTo(0);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireDeniedAsync(global, $"SELECT id FROM {Table<PaymentReconciliationEffect>(seed)}");
        await RequireDeniedAsync(global, $"SELECT registration_order_id FROM {Table<PaymentSucceededObservation>(seed)}");
    }

    private static HostedCheckoutSession Session(string sessionId) =>
        new(sessionId, null, HostedCheckoutSessionStatus.Complete, HostedCheckoutPaymentStatus.Paid,
            $"pi_{sessionId}", Now.AddMinutes(30), 1_000, "EUR");

    private static PaymentReconciliationDecision Success(PaymentSeed value) =>
        new(PaymentReconciliationDisposition.Complete, PaymentAttemptStatusEnum.Succeeded,
            value.Attempt.ProviderCheckoutSessionId, $"pi_{value.Attempt.ProviderCheckoutSessionId}",
            "native-payment-read", string.Empty, Now.AddSeconds(4));

    private static async Task<PaymentSeed> SeedPaymentAsync(
        ExploreDbContext context, bool claimed = false, Guid? tenantId = null, bool unknownDispatch = false)
    {
        var tenant = tenantId.HasValue
            ? await context.Tenants.SingleAsync(row => row.Id == tenantId.Value)
            : new Tenant { FullName = "Payment reconciliation", Slug = $"payment-{Guid.CreateVersion7():N}", TenantStatusId = 2, TenantStatus = null! };
        if (!tenantId.HasValue)
            context.Tenants.Add(tenant);
        var user = new User { Pii = new UserPii { Email = $"payment-{Guid.CreateVersion7():N}@example.test", FirstName = "Payment", LastName = "Reconciliation" } };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Payment merchant" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var target = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Payment reconciliation",
            Subtitle = "",
            Description = "",
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            EventFormatId = 1,
            EventFormat = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var catalog = EventTicketCatalogVersion.Create(tenant.Id, target.Id, "EUR", 1);
        var ticket = EventTicketType.Create(
            Guid.CreateVersion7(), tenant.Id, catalog.Id, "Admission", "EUR", TicketPricingModeEnum.Fixed,
            Money.Create(1_000, "EUR"), null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant.Id, target.Id, 1));
        catalog.UpdateCommercialDisclosures("Payment merchant", "Refund policy", "support@example.test");
        catalog.Publish();
        RegistrationOrder order = RegistrationOrder.Create(
            tenant.Id, target.Id, user.Id, actor.Id, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null, null, "EUR", Now, Now.AddMinutes(30));
        order.AddLine(RegistrationOrderLine.Create(catalog, ticket, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 1_000, 75, 925, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, Now);
        context.AddRange(target, catalog, order);
        await context.SaveChangesAsync();
        var recipient = OrganizerPaymentRecipientSnapshot.Create(
            tenant.Id, actor.Id, Guid.CreateVersion7(), "stripe", "payment-platform", $"acct_{actor.Id:N}",
            "BE", "EUR", Guid.CreateVersion7(), null, Now);
        string composition = $"native-{Guid.CreateVersion7():N}";
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(), tenant.Id, order.Id, recipient, "OrganizerDirect", "2026-08-20.acacia",
            composition, Money.Create(1_000, "EUR"), Money.Create(75, "EUR"), Money.Create(0, "EUR"),
            $"checkout:{tenant.Id:N}:{order.Id:N}", Now, Now.AddMinutes(30));
        attempt.AssignCampaignCursor(1);
        attempt.AttachAcceptance(PaidAcceptanceTestFacts.Create(tenant.Id, order.Id, target.Id, composition,
            recipient.InstancePolicyVersionId, 1_000, 75, 0, Now, recipient: recipient));
        CheckoutDispatchEffect? dispatch = null;
        if (unknownDispatch)
        {
            attempt.MarkUnknown(Now.AddSeconds(1), "native-dispatch");
            dispatch = CheckoutDispatchEffect.Create(attempt, Now);
            dispatch.Claim("native-checkout-worker", Guid.CreateVersion7(), Now.AddMinutes(1), Now.AddSeconds(1));
            context.AddRange(attempt, dispatch);
            await context.SaveChangesAsync();
            var dispatchClaim = new CheckoutDispatchClaim(dispatch.Id, tenant.Id, order.Id, attempt.Id,
                dispatch.ProcessingLeaseToken!.Value, dispatch.ProcessingFence,
                dispatch.ProcessingLeaseExpiresAt!.Value, AttemptCount: dispatch.AttemptCount);
            await Assert.That(await new RegistrationPaymentAttemptRepository(context).MarkDispatchUnknownAsync(
                dispatchClaim, Now.AddSeconds(1), CancellationToken.None)).IsTrue();
        }
        else
        {
            attempt.MarkRequiresAction($"cs_{attempt.Id:N}", Now.AddSeconds(1), "native-checkout");
            context.Add(attempt);
        }
        PaymentReconciliationEffect effect = PaymentReconciliationEffect.Create(attempt, Now.AddSeconds(2),
            checkoutDispatchEffectId: dispatch?.Id,
            checkoutDispatchUnknownAt: dispatch is null ? null : Now.AddSeconds(1),
            checkoutDispatchProcessingFence: dispatch?.ProcessingFence,
            checkoutDispatchAttemptCount: dispatch?.AttemptCount);
        PaymentReconciliationClaim? claim = null;
        if (claimed)
        {
            Guid leaseToken = Guid.CreateVersion7();
            effect.Claim("native-payment-worker", leaseToken, Now.AddMinutes(2), Now.AddSeconds(2));
            claim = new(tenant.Id, effect.Id, attempt.Id, leaseToken, effect.ProcessingFence, effect.AttemptCount,
                effect.CheckoutDispatchEffectId, effect.CheckoutDispatchUnknownAt,
                effect.CheckoutDispatchProcessingFence, effect.CheckoutDispatchAttemptCount);
        }
        context.Add(effect);
        await context.SaveChangesAsync();
        return new(tenant.Id, attempt, effect, claim, dispatch);
    }

    private async Task<PaymentAuthority> CreateAuthorityAsync(ExploreDbContext seed)
    {
        await PostgresPaymentReconciliationContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        string schema = seed.Model.FindEntityType(typeof(PaymentAttempt))!.GetSchema()!;
        string payment = Table<PaymentAttempt>(seed);
        string reconciliation = Table<PaymentReconciliationEffect>(seed);
        string observation = Table<PaymentSucceededObservation>(seed);
        string finalization = Table<RegistrationFinalizationEffect>(seed);
        string order = Table<RegistrationOrder>(seed);
        string material = Table<RegistrationMaterialChangeChoice>(seed);
        string dispatch = Table<CheckoutDispatchEffect>(seed);
        string globalRole = fixture.NativeQueueEnrollment.GlobalLogin;
        string runtimeRole = fixture.NativeQueueEnrollment.RuntimeLogin;
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var grant = administrator.CreateCommand();
        grant.CommandText = $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{runtimeRole}";
            GRANT SELECT, UPDATE ON {payment}, {reconciliation}, {finalization}, {material}, {dispatch} TO "{runtimeRole}";
            GRANT SELECT, INSERT ON {observation} TO "{runtimeRole}";
            GRANT INSERT ON {finalization} TO "{runtimeRole}";
            GRANT SELECT ON {order} TO "{runtimeRole}";
            """;
        await grant.ExecuteNonQueryAsync();
        await using var flags = administrator.CreateCommand();
        flags.CommandText = """
            SELECT count(*) FROM pg_catalog.pg_roles
            WHERE rolname IN (@global, @runtime) AND NOT rolsuper AND NOT rolbypassrls
                AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolreplication
            """;
        flags.Parameters.AddWithValue("global", globalRole);
        flags.Parameters.AddWithValue("runtime", runtimeRole);
        await Assert.That(Convert.ToInt32(await flags.ExecuteScalarAsync())).IsEqualTo(2);
        return new(fixture.ConnectionString, schema, payment,
            [payment, reconciliation, observation, finalization, order, material, dispatch],
            runtimeRole, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global, schema),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime, schema));
    }

    private static string Table<TEntity>(ExploreDbContext context) where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    private static async Task RequireDeniedAsync(ExploreDbContext context, string command)
    {
        string? sqlState = null;
        try { await context.Database.ExecuteSqlRawAsync(command); }
        catch (PostgresException exception) { sqlState = exception.SqlState; }
        await Assert.That(sqlState).IsEqualTo("42501");
    }

    private sealed record PaymentSeed(
        Guid TenantId, PaymentAttempt Attempt, PaymentReconciliationEffect Effect, PaymentReconciliationClaim? Claim,
        CheckoutDispatchEffect? Dispatch = null);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PaymentAuthority(
        string administrationConnection, string schema, string paymentTable, string[] tables,
        string runtimeRole, PrimaryDatabaseConnectionOptions globalOptions,
        PrimaryDatabaseConnectionOptions runtimeOptions) : IAsyncDisposable
    {
        internal GlobalExploreDbContextFactory Global { get; } = new(globalOptions, poolSize: 1);
        internal ITenantExploreDbContextFactory Runtime { get; } = new TenantExploreDbContextFactory(runtimeOptions, poolSize: 1);
        internal string PaymentTable => paymentTable;

        internal RegistrationPaymentReconciliationService CreateService(
            IHostedCheckoutSessionRetriever checkout, IPaymentIntentRetriever payment) =>
            new(new PaymentReconciliationAdmission(new PaymentGlobalFactory(Global)),
                new PaymentReconciliationExecutionScopeFactory(Runtime),
                checkout, payment, new FixedTimeProvider(Now.AddSeconds(3)));

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
                REVOKE ALL PRIVILEGES ON {string.Join(", ", tables)} FROM "{runtimeRole}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{runtimeRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }
    }

    private sealed class PaymentGlobalFactory(GlobalExploreDbContextFactory factory)
        : IPaymentReconciliationAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }

    private sealed class InterceptedGlobalFactory(
        GlobalExploreDbContextFactory factory, IInterceptor interceptor, bool retryLostAcknowledgement = false)
        : IPaymentReconciliationAdmissionDbContextFactory
    {
        public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            await using var template = await factory.CreateDbContextAsync(cancellationToken);
            var builder = new DbContextOptionsBuilder<ExploreDbContext>(
                (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>())
                .AddInterceptors(interceptor);
            if (retryLostAcknowledgement)
                builder.ReplaceService<IExecutionStrategyFactory, AcquisitionRetryStrategyFactory>();
            return new ExploreDbContext(builder.Options);
        }
    }

    private sealed class LostAcquisitionAcknowledgement(
        Guid? leaseToken, CancellationTokenSource? cancellation, Func<Task>? afterCommit = null)
        : DbCommandInterceptor
    {
        internal List<Guid> CommittedEffectIds { get; } = [];
        private bool interrupted;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!interrupted && command.Parameters.Count == 7 &&
                command.Parameters[0].Value is Guid token && (!leaseToken.HasValue || token == leaseToken.Value))
            {
                interrupted = true;
                // Drain and dispose the real server result so the autocommit is
                // complete before losing the acknowledgement delivered to EF.
                while (await result.ReadAsync(cancellationToken))
                    CommittedEffectIds.Add(result.GetGuid(1));
                while (await result.NextResultAsync(cancellationToken)) { }
                await result.DisposeAsync();
                if (afterCommit is not null)
                    await afterCommit();
                cancellation?.Cancel();
                if (cancellation is not null)
                    throw new OperationCanceledException(cancellation.Token);
                throw new IOException("Native reconciliation acquisition committed without acknowledgement.");
            }
            return result;
        }
    }

    private sealed class AcquisitionRetryStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new AcquisitionRetryStrategy(dependencies);
    }

    private sealed class AcquisitionRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is IOException;
    }
}
