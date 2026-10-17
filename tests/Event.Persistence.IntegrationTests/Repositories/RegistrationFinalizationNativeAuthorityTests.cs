using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

/// <summary>
/// Actual migrated finalization rows, independently selected Global and Runtime
/// credentials, and production pooled acquisition. Administrator connections
/// only seed rows, install fault controls, and inspect committed outcomes.
/// </summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationFinalizationNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false, "enrolled-grantee")]
    [Arguments(true, "enrolled-grantee")]
    [Arguments(false, "overload")]
    [Arguments(true, "overload")]
    [Arguments(false, "public-column")]
    [Arguments(true, "public-column")]
    [Arguments(false, "schema-create")]
    [Arguments(true, "schema-create")]
    [Arguments(false, "membership-options")]
    [Arguments(true, "membership-options")]
    [Arguments(false, "owner-inherit")]
    [Arguments(true, "owner-inherit")]
    [Arguments(false, "global-superuser")]
    [Arguments(true, "global-superuser")]
    [Arguments(false, "global-bypassrls")]
    [Arguments(true, "global-bypassrls")]
    [Arguments(false, "global-createrole")]
    [Arguments(true, "global-createrole")]
    [Arguments(false, "partial-roles")]
    [Arguments(true, "partial-roles")]
    [Arguments(false, "artifacts-without-roles")]
    [Arguments(true, "artifacts-without-roles")]
    public async Task RegistrationPrivilegeDriftRejectsClaimBeforeBoundsAndInstallerBeforeRepair(bool providerWrite, string drift)
    {
        await fixture.ResetAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Global.CreateDbContextAsync();
        await using var administrator = fixture.CreateDbContext();
        string function = providerWrite
            ? PostgresRegistrationProviderWriteQueueContract.FunctionSql(global)
            : PostgresRegistrationFinalizationQueueContract.FunctionSql(global);
        string owner = providerWrite ? PostgresRegistrationProviderWriteQueueContract.OwnerRole
            : PostgresRegistrationFinalizationQueueContract.OwnerRole;
        string executor = providerWrite ? PostgresRegistrationProviderWriteQueueContract.GlobalRole
            : PostgresRegistrationFinalizationQueueContract.GlobalRole;
        string migrator = providerWrite ? PostgresRegistrationProviderWriteQueueContract.MigratorRole
            : PostgresRegistrationFinalizationQueueContract.MigratorRole;
        string signature = $"{function}(text, integer, timestamp with time zone, interval)";
        string schema = administrator.Model.FindEntityType(typeof(UserPii))!.GetSchema()!;
        var pii = administrator.Model.FindEntityType(typeof(UserPii))!;
        string column = pii.FindProperty(nameof(UserPii.Email))!.GetColumnName(
            Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(pii.GetTableName()!, pii.GetSchema()))!;
        string renamed = $"queue_displaced_{Guid.CreateVersion7():N}";
        string renamedExecutor = $"queue_executor_{Guid.CreateVersion7():N}";
        string renamedMigrator = $"queue_migrator_{Guid.CreateVersion7():N}";
        (string corrupt, string restore) = drift switch
        {
            "enrolled-grantee" => ($"GRANT EXECUTE ON FUNCTION {signature} TO \"{fixture.NativeQueueEnrollment.RuntimeLogin}\"",
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{fixture.NativeQueueEnrollment.RuntimeLogin}\""),
            "overload" => ($"CREATE FUNCTION {function}(integer) RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                $"DROP FUNCTION {function}(integer)"),
            "public-column" => ($"GRANT SELECT (\"{column}\") ON {Table<UserPii>(administrator)} TO PUBLIC",
                $"REVOKE SELECT (\"{column}\") ON {Table<UserPii>(administrator)} FROM PUBLIC"),
            "schema-create" => ($"GRANT CREATE ON SCHEMA \"{schema}\" TO {owner}",
                $"REVOKE CREATE ON SCHEMA \"{schema}\" FROM {owner}"),
            "membership-options" => ($"GRANT {executor} TO \"{fixture.NativeQueueEnrollment.GlobalLogin}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
                $"GRANT {executor} TO \"{fixture.NativeQueueEnrollment.GlobalLogin}\" WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "owner-inherit" => ($"ALTER ROLE {owner} INHERIT", $"ALTER ROLE {owner} NOINHERIT"),
            "global-superuser" => ($"ALTER ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" SUPERUSER",
                $"ALTER ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" NOSUPERUSER"),
            "global-bypassrls" => ($"ALTER ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" BYPASSRLS",
                $"ALTER ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" NOBYPASSRLS"),
            "global-createrole" => ($"ALTER ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" CREATEROLE",
                $"ALTER ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" NOCREATEROLE"),
            "partial-roles" => ($"ALTER ROLE {owner} RENAME TO \"{renamed}\"", $"ALTER ROLE \"{renamed}\" RENAME TO {owner}"),
            "artifacts-without-roles" => ($"""
                ALTER ROLE {owner} RENAME TO "{renamed}";
                ALTER ROLE {executor} RENAME TO "{renamedExecutor}";
                ALTER ROLE {migrator} RENAME TO "{renamedMigrator}";
                """, $"""
                ALTER ROLE "{renamed}" RENAME TO {owner};
                ALTER ROLE "{renamedExecutor}" RENAME TO {executor};
                ALTER ROLE "{renamedMigrator}" RENAME TO {migrator};
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string valid = $"SELECT * FROM {function}('native-drift', 1, now(), interval '1 minute')";
        await global.Database.ExecuteSqlRawAsync(valid);
        await administrator.Database.ExecuteSqlRawAsync(corrupt);
        try
        {
            await Assert.That(async () => await global.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {function}(NULL, NULL, NULL, NULL)"))
                .Throws<PostgresException>()
                .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
            await Assert.That(async () =>
            {
                if (providerWrite)
                    await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(administrator, fixture.NativeQueueEnrollment);
                else
                    await PostgresRegistrationFinalizationQueueContract.ApplyAsync(administrator, fixture.NativeQueueEnrollment);
            }).Throws<PostgresException>()
                .And.Satisfies(exception => exception != null && exception.SqlState == "55000");
            if (drift == "owner-inherit")
                await Assert.That(await administrator.Database.SqlQueryRaw<bool>(
                    "SELECT rolinherit AS \"Value\" FROM pg_catalog.pg_roles WHERE rolname = {0}", owner).SingleAsync()).IsTrue();
            if (drift is "partial-roles" or "artifacts-without-roles")
                await Assert.That(await administrator.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::integer AS \"Value\" FROM pg_catalog.pg_roles WHERE rolname = {0}", owner).SingleAsync()).IsEqualTo(0);
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(restore);
        }
        await global.Database.ExecuteSqlRawAsync(valid);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RegistrationRejectsInheritedGlobalSessionAndFalseMigratorDeclaration(bool providerWrite)
    {
        await fixture.ResetAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var outsider = await CreateAuthorityAsync(entitled: false);
        await using var administrator = fixture.CreateDbContext();
        await using var global = await authority.Global.CreateDbContextAsync();
        string function = providerWrite
            ? PostgresRegistrationProviderWriteQueueContract.FunctionSql(global)
            : PostgresRegistrationFinalizationQueueContract.FunctionSql(global);
        string claim = $"SELECT * FROM {function}('native-enrollment', 1, now(), interval '1 minute')";
        await administrator.Database.ExecuteSqlRawAsync(
            $"GRANT \"{fixture.NativeQueueEnrollment.GlobalLogin}\" TO \"{outsider.GlobalLogin}\" WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
        try
        {
            await global.Database.ExecuteSqlRawAsync(claim);
            await using var unauthorized = await outsider.Global.CreateDbContextAsync();
            await unauthorized.Database.OpenConnectionAsync();
            await unauthorized.Database.ExecuteSqlRawAsync($"SET ROLE \"{fixture.NativeQueueEnrollment.GlobalLogin}\"");
            await Assert.That(async () => await unauthorized.Database.ExecuteSqlRawAsync(claim))
                .Throws<PostgresException>()
                .And.Satisfies(exception => exception != null && exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            await administrator.Database.ExecuteSqlRawAsync(
                $"REVOKE \"{fixture.NativeQueueEnrollment.GlobalLogin}\" FROM \"{outsider.GlobalLogin}\"");
        }
        var falseEnrollment = fixture.NativeQueueEnrollment with
        {
            MigratorLogin = fixture.NativeQueueEnrollment.RuntimeLogin,
            RuntimeLogin = fixture.NativeQueueEnrollment.MigratorLogin
        };
        await Assert.That(async () =>
        {
            if (providerWrite)
                await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(administrator, falseEnrollment);
            else
                await PostgresRegistrationFinalizationQueueContract.ApplyAsync(administrator, falseEnrollment);
        }).Throws<PostgresException>()
            .And.Satisfies(exception => exception != null && exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        await global.Database.ExecuteSqlRawAsync(claim);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RegistrationEnrollmentTreatsSqlDelimitersAsLoginData(bool providerWrite)
    {
        await fixture.ResetAsync();
        await using var administrator = fixture.CreateDbContext();
        var enrollment = fixture.NativeQueueEnrollment with
        {
            GlobalLogin = "untrusted'\"\\$ddl$$function$$enrollment$"
        };
        await Assert.That(async () =>
        {
            if (providerWrite)
                await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(administrator, enrollment);
            else
                await PostgresRegistrationFinalizationQueueContract.ApplyAsync(administrator, enrollment);
        }).Throws<PostgresException>()
            .And.Satisfies(exception => exception != null && exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Global.CreateDbContextAsync();
        string function = providerWrite ? PostgresRegistrationProviderWriteQueueContract.FunctionSql(global)
            : PostgresRegistrationFinalizationQueueContract.FunctionSql(global);
        await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {function}('enrollment-control', 1, now(), interval '1 minute')");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NonSuperuserFreshRegistrationBootstrapCannotCommitCreatorShortcuts(bool providerWrite)
    {
        await fixture.ResetAsync();
        await using var administrator = fixture.CreateDbContext();
        string owner = providerWrite ? PostgresRegistrationProviderWriteQueueContract.OwnerRole
            : PostgresRegistrationFinalizationQueueContract.OwnerRole;
        string migrator = providerWrite ? PostgresRegistrationProviderWriteQueueContract.MigratorRole
            : PostgresRegistrationFinalizationQueueContract.MigratorRole;
        string executor = providerWrite ? PostgresRegistrationProviderWriteQueueContract.GlobalRole
            : PostgresRegistrationFinalizationQueueContract.GlobalRole;
        string suffix = Guid.CreateVersion7().ToString("N");
        string schema = $"queue_bootstrap_{suffix}";
        string login = $"queue_creator_{suffix}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string displacedOwner = $"original_owner_{suffix}";
        string displacedMigrator = $"original_migrator_{suffix}";
        string displacedExecutor = $"original_executor_{suffix}";
        Type[] relations = providerWrite
            ? [typeof(RegistrationProviderSubmissionWriteEffect), typeof(StorageObject)]
            : [typeof(RegistrationFinalizationEffect)];
        string clone = string.Join(Environment.NewLine, relations.Select(type =>
        {
            var entity = administrator.Model.FindEntityType(type)!;
            string table = $"\"{schema}\".\"{entity.GetTableName()}\"";
            return $"""
                CREATE TABLE {table} (LIKE {Table(administrator, type)} INCLUDING ALL);
                ALTER TABLE {table} OWNER TO "{login}";
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                """;
        }));
        await administrator.Database.ExecuteSqlRawAsync($"""
            CREATE ROLE "{login}" LOGIN NOSUPERUSER NOCREATEDB CREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
            CREATE SCHEMA "{schema}" AUTHORIZATION "{login}";
            {clone}
            ALTER ROLE {owner} RENAME TO "{displacedOwner}";
            ALTER ROLE {migrator} RENAME TO "{displacedMigrator}";
            ALTER ROLE {executor} RENAME TO "{displacedExecutor}";
            """);
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator, schema) with
        {
            Username = login,
            Password = password
        };
        bool installed = false;
        try
        {
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            await using var context = new ExploreDbContext(builder.Options);
            var enrollment = fixture.NativeQueueEnrollment with { MigratorLogin = login };
            PostgresException? failure = null;
            try
            {
                if (providerWrite)
                    await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(context, enrollment);
                else
                    await PostgresRegistrationFinalizationQueueContract.ApplyAsync(context, enrollment);
                installed = true;
            }
            catch (PostgresException exception)
            {
                failure = exception;
            }
            if (failure is not null)
            {
                // A deployment needing a privileged bootstrap must reject atomically,
                // never leave its creator's implicit ADMIN memberships behind.
                await Assert.That(failure.SqlState == PostgresErrorCodes.InsufficientPrivilege ||
                    (failure.SqlState == "55000" && failure.MessageText == "native_privilege_guard_not_ready")).IsTrue();
                await Assert.That(await administrator.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::integer AS \"Value\" FROM pg_catalog.pg_roles WHERE rolname IN ({0}, {1}, {2})",
                    owner, migrator, executor).SingleAsync()).IsEqualTo(0);
                await Assert.That(await administrator.Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::integer AS "Value" FROM pg_catalog.pg_proc routine
                    JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                    WHERE namespace.nspname = {0}
                    """, schema).SingleAsync()).IsEqualTo(0);
            }
            else
            {
                await using var command = administrator.Database.GetDbConnection().CreateCommand();
                await administrator.Database.OpenConnectionAsync();
                command.CommandText = """
                    SELECT parent.rolname, member.rolname, edge.inherit_option, edge.set_option, edge.admin_option
                    FROM pg_catalog.pg_auth_members edge
                    JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                    JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                    WHERE parent.rolname = ANY(@roles) OR member.rolname = ANY(@roles)
                    """;
                command.Parameters.Add(new NpgsqlParameter("roles", new[] { owner, migrator, executor }));
                List<(string Parent, string Member, bool Inherit, bool Set, bool Admin)> edges = [];
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    edges.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                        reader.GetBoolean(3), reader.GetBoolean(4)));
                await Assert.That(edges).IsEquivalentTo(new[]
                {
                    (owner, migrator, false, true, false),
                    (migrator, login, false, true, false),
                    (executor, fixture.NativeQueueEnrollment.GlobalLogin, true, false, false)
                });
            }
        }
        finally
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await administrator.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{schema}\" CASCADE");
            if (installed)
                await administrator.Database.ExecuteSqlRawAsync($"DROP ROLE {owner}, {migrator}, {executor}");
            await administrator.Database.ExecuteSqlRawAsync($"""
                ALTER ROLE "{displacedOwner}" RENAME TO {owner};
                ALTER ROLE "{displacedMigrator}" RENAME TO {migrator};
                ALTER ROLE "{displacedExecutor}" RENAME TO {executor};
                DROP ROLE "{login}";
                """);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderWriteQueueExportsOriginalClaimMetadataWithoutPrivateReadAuthority(bool recover)
    {
        var rows = await SeedAsync();
        DateTime observed = DateTime.UtcNow;
        var effects = rows.Select(row =>
        {
            var revision = RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var attempt = RegistrationAttempt.Create(row.TenantId, row.Order.EventId, row.Order.Id,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(),
                CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                Guid.CreateVersion7(), revision, observed.AddMinutes(-5), observed.AddDays(1));
            var submission = attempt.SubmitHeadlessProvider(revision, observed.AddMinutes(-4), null);
            return RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, observed.AddMinutes(-4));
        }).ToArray();
        if (recover)
            foreach (var effect in effects)
                effect.Claim("expired-worker", Guid.CreateVersion7(), observed.AddSeconds(-1), observed.AddMinutes(-1));
        await using (var writer = fixture.CreateDbContext())
        {
            writer.RegistrationProviderSubmissionWriteEffects.AddRange(effects);
            await writer.SaveChangesAsync();
        }
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireOrdinaryAsync(global);
        await RequirePrivateDenialsAsync(global);
        IRegistrationProviderSubmissionWriteQueueRepository repository =
            new RegistrationProviderSubmissionWriteQueueRepository(authority.Global);
        await Assert.That(async () => await global.Database.ExecuteSqlRawAsync(
            $"SET ROLE {PostgresRegistrationProviderWriteQueueContract.OwnerRole}"))
            .Throws<PostgresException>()
            .And.Satisfies(exception => exception is not null && exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        var duration = TimeSpan.FromMinutes(5);
        var claims = await repository.ClaimDueAsync("native-worker", 20,
            observed.AddDays(1), duration, default);
        await Assert.That(claims.Select(claim => claim.EffectId)).IsEquivalentTo(effects.Select(effect => effect.Id));
        DateTime serverNow = await global.Database.SqlQueryRaw<DateTime>(
            "SELECT statement_timestamp() AS \"Value\"").SingleAsync();
        foreach (var claim in claims)
        {
            var source = effects.Single(effect => effect.Id == claim.EffectId);
            await Assert.That(claim.TenantId).IsEqualTo(source.TenantId);
            await Assert.That(claim.RegistrationSubmissionId).IsEqualTo(source.RegistrationSubmissionId);
            await Assert.That(claim.RegistrationAttemptId).IsEqualTo(source.RegistrationAttemptId);
            await Assert.That(claim.RegistrationProviderBindingId).IsEqualTo(source.RegistrationProviderBindingId);
            await Assert.That(claim.ProcessingFence).IsEqualTo(recover ? 2L : 1L);
            await Assert.That(claim.AttemptCount).IsEqualTo(recover ? 2 : 1);
            await Assert.That(claim.LeaseToken.Version).IsEqualTo(7);
            await Assert.That(claim.LeaseExpiresAt > serverNow && claim.LeaseExpiresAt <= serverNow.Add(duration)).IsTrue();
        }
        await Assert.That(await repository.ClaimDueAsync("native-worker", 20,
            observed.AddDays(1), duration, default)).IsEmpty();
    }

    [Test]
    public async Task ProviderWriteQueueRequiresItsOwnExecuteEntitlement()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync(entitled: false);
        await using var global = await authority.Global.CreateDbContextAsync();
        await Assert.That(async () => await new RegistrationProviderSubmissionWriteEffectRepository(global)
            .ClaimDueAsync("unentitled", 10, DateTime.UtcNow, TimeSpan.FromMinutes(1), default))
            .Throws<PostgresException>()
            .And.Satisfies(exception => exception is not null && exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows[0].TenantId);
        await Assert.That(async () => await new RegistrationProviderSubmissionWriteEffectRepository(runtime)
            .ClaimDueAsync("runtime", 10, DateTime.UtcNow, TimeSpan.FromMinutes(1), default))
            .Throws<PostgresException>()
            .And.Satisfies(exception => exception is not null && exception.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task ProviderWriteNativeClaimSharesRetentionCleanupAdvisoryLock()
    {
        await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var blocker = fixture.CreateDbContext();
        await using var holding = await blocker.Database.BeginTransactionAsync();
        await using var ownedLock = await RelationalNamedLock.AcquireTransactionAsync(
            blocker, "registration-provider-submission-write-claim", CancellationToken.None);
        await using var global = await authority.Global.CreateDbContextAsync();
        await using var blockedTransaction = await global.Database.BeginTransactionAsync();
        await global.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1s'");
        await Assert.That(async () => await new RegistrationProviderSubmissionWriteEffectRepository(global)
            .ClaimDueAsync("blocked-worker", 10, DateTime.UtcNow, TimeSpan.FromMinutes(1), default))
            .Throws<PostgresException>()
            .And.Satisfies(exception => exception is not null && exception.SqlState == PostgresErrorCodes.LockNotAvailable);
    }

    [Test]
    public async Task RestrictedGlobalClaimsBothTenantsWithoutPrivateTableAuthority()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireOrdinaryAsync(global);
        await RequirePrivateDenialsAsync(global);
        await Assert.That(global.TenantFilterTenantId).IsNull();

        // The same committed rows are genuinely claimable before testing the
        // restricted production acquisition. Recovery must keep their identity.
        var original = await ClaimControlAsync(Now);
        await Assert.That(original.Select(claim => claim.TenantId)).IsEquivalentTo(rows.Select(row => row.TenantId));
        await Assert.That(original.All(claim => claim.ProcessingFence == 1)).IsTrue();
        var recovered = await ClaimGlobalAsync(authority, Now.AddSeconds(30));
        await Assert.That(recovered.Select(claim => claim.EffectId)).IsEquivalentTo(original.Select(claim => claim.EffectId));
        await Assert.That(recovered.Select(claim => claim.RegistrationOrderId)).IsEquivalentTo(rows.Select(row => row.Order.Id));
        await Assert.That(recovered.All(claim => claim.ProcessingFence == 2)).IsTrue();
        await Assert.That((await ClaimGlobalAsync(authority, Now.AddSeconds(31))).Count).IsEqualTo(0);
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task GlobalClaimEntitlementCannotBecomeOwnerAndRuntimeCannotClaimGlobally()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireOrdinaryAsync(global);
        foreach (string role in new[]
        {
            PostgresRegistrationFinalizationQueueContract.OwnerRole,
            PostgresRegistrationFinalizationQueueContract.MigratorRole
        })
        {
            await Assert.That(await global.Database.SqlQueryRaw<bool>(
                "SELECT pg_has_role(current_user, {0}, 'MEMBER') AS \"Value\"", role).SingleAsync()).IsFalse();
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"SET ROLE {role}"));
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        await RequirePrivateDenialsAsync(global);
        var invalid = await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresRegistrationFinalizationQueueContract.FunctionSql(global)}({{0}}, {{1}}, {{2}}, {{3}})",
                "native-finalization", 0, Now, TimeSpan.FromSeconds(30)));
        await Assert.That(invalid!.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(rows[0].TenantId);
        var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresRegistrationFinalizationQueueContract.FunctionSql(runtime)}({{0}}, {{1}}, {{2}}, {{3}})",
                "native-finalization", 2, Now, TimeSpan.FromSeconds(30)));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var competing = await Task.WhenAll(ClaimGlobalAsync(authority, Now), ClaimGlobalAsync(authority, Now));
        var claims = competing.SelectMany(value => value).ToArray();
        await Assert.That(claims.Select(claim => claim.EffectId)).IsEquivalentTo(rows.Select(row => row.EffectId));
        await Assert.That(claims.Length).IsEqualTo(2);
        await Assert.That(claims.All(claim => claim.ProcessingFence == 1)).IsTrue();
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task IndependentlySelectedButUnentitledGlobalLoginCannotClaimEffects()
    {
        var rows = await SeedAsync();
        await using var unentitled = await CreateAuthorityAsync(entitled: false);
        await using var global = await unentitled.Global.CreateDbContextAsync();
        await RequireOrdinaryAsync(global);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => await ClaimGlobalAsync(unentitled, Now));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var entitled = await CreateAuthorityAsync();
        var claims = await ClaimGlobalAsync(entitled, Now);
        await Assert.That(claims.Select(claim => claim.EffectId)).IsEquivalentTo(rows.Select(row => row.EffectId));
        await Assert.That(claims.All(claim => claim.ProcessingFence == 1)).IsTrue();
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task OrdinarySettlementRejectsForeignForgedExpiredAndPredecessorClaims()
    {
        var rows = await SeedAsync();
        var original = await ClaimControlAsync(Now);
        await using var authority = await CreateAuthorityAsync();
        var own = original.Single(claim => claim.TenantId == rows[0].TenantId);
        var foreign = original.Single(claim => claim.TenantId == rows[1].TenantId);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(own.TenantId);
        await RequireOrdinaryAsync(runtime);
        var repository = new RegistrationFinalizationRepository(runtime);
        await Assert.That((await repository.GetSucceededPaymentAsync(
            own.TenantId, own.RegistrationOrderId, CancellationToken.None)).Status).IsEqualTo(SucceededPaymentLookupStatus.Found);
        await Assert.That((await repository.GetSucceededPaymentAsync(
            foreign.TenantId, foreign.RegistrationOrderId, CancellationToken.None)).Status).IsEqualTo(SucceededPaymentLookupStatus.Missing);
        await Assert.That(await repository.AreMandatoryRequirementsFulfilledAsync(
            own.TenantId, own.RegistrationOrderId, CancellationToken.None)).IsTrue();
        foreach (var invalid in new[]
        {
            foreign,
            own with { TenantId = foreign.TenantId },
            own with { EffectId = foreign.EffectId },
            own with { RegistrationOrderId = foreign.RegistrationOrderId },
            own with { LeaseToken = Guid.CreateVersion7() },
            own with { ProcessingFence = own.ProcessingFence + 1 }
        })
        {
            await Assert.That(await repository.CompleteAsync(invalid, Now.AddSeconds(1), CancellationToken.None)).IsFalse();
            await Assert.That(await repository.RetryAsync(invalid, Now.AddMinutes(1), Now.AddSeconds(1), CancellationToken.None)).IsFalse();
        }
        await Assert.That(await repository.CompleteAsync(own, Now.AddSeconds(30), CancellationToken.None)).IsFalse();
        await Assert.That(await repository.RetryAsync(own, Now.AddMinutes(1), Now.AddSeconds(30), CancellationToken.None)).IsFalse();
        var successor = (await ClaimControlAsync(Now.AddSeconds(30))).Single(claim => claim.EffectId == own.EffectId);
        await Assert.That(successor.ProcessingFence).IsEqualTo(2);
        await Assert.That(successor.LeaseToken).IsNotEqualTo(own.LeaseToken);
        await Assert.That(await repository.CompleteAsync(own, Now.AddSeconds(31), CancellationToken.None)).IsFalse();
        await Assert.That(await repository.RetryAsync(own, Now.AddMinutes(1), Now.AddSeconds(31), CancellationToken.None)).IsFalse();
        await Assert.That(await repository.CompleteAsync(successor, Now.AddSeconds(31), CancellationToken.None)).IsTrue();
        await Assert.That(await repository.CompleteAsync(successor, Now.AddSeconds(32), CancellationToken.None)).IsFalse();
        await using var verification = fixture.CreateDbContext();
        var effects = await verification.RegistrationFinalizationEffects.AsNoTracking().ToListAsync();
        await Assert.That(effects.Single(effect => effect.Id == own.EffectId).Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(effects.Single(effect => effect.Id == foreign.EffectId).Status).IsEqualTo(OutboxMessageStatus.Processing);
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task RetryAndRequestReplayPreserveCommittedEffectAndSucceededPaymentIdentity()
    {
        var rows = await SeedAsync();
        var claim = (await ClaimControlAsync(Now)).Single(value => value.TenantId == rows[0].TenantId);
        await using var authority = await CreateAuthorityAsync();
        await using var runtime = await authority.Runtime.CreateDbContextAsync(claim.TenantId);
        var repository = new RegistrationFinalizationRepository(runtime);
        await Assert.That(await repository.RetryAsync(claim, Now.AddMinutes(1), Now.AddSeconds(1), CancellationToken.None)).IsTrue();
        await Assert.That(await repository.RetryAsync(claim, Now.AddMinutes(1), Now.AddSeconds(2), CancellationToken.None)).IsFalse();
        await repository.RequestAsync(rows[0].Order, Now.AddSeconds(3), CancellationToken.None);
        await repository.RequestAsync(rows[0].Order, Now.AddSeconds(4), CancellationToken.None);
        await using var verification = fixture.CreateDbContext();
        var effect = await verification.RegistrationFinalizationEffects.AsNoTracking()
            .SingleAsync(value => value.RegistrationOrderId == claim.RegistrationOrderId);
        await Assert.That(effect.Id).IsEqualTo(claim.EffectId);
        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(effect.ProcessingFence).IsEqualTo(1);
        await Assert.That(effect.AttemptCount).IsEqualTo(1);
        await Assert.That(effect.ProcessingLeaseToken).IsNull();
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task OrdinaryLastSeatReservationRollbackAndConsumptionRemainExact()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        var row = rows[0];
        await using (var rollback = await authority.Runtime.CreateDbContextAsync(row.TenantId))
        {
            await rollback.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await rollback.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var reservation = await ReserveAsync(rollback, row, row.Order.Id);
                await Assert.That(reservation.Reserved).IsTrue();
                await transaction.RollbackAsync();
            });
        }
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(row.TenantId))
        {
            await RequireOrdinaryAsync(runtime);
            var inventory = new RegistrationInventoryRepository(runtime);
            await Assert.That(await inventory.GetAllocatedQuantityAsync(row.PoolId, row.TenantId, CancellationToken.None)).IsEqualTo(0);
            var reserved = await new EfCoreUnitOfWork(runtime).ExecuteSerializableAsync(token =>
                ReserveAsync(runtime, row, row.Order.Id, token));
            await Assert.That(reserved.Reserved).IsTrue();
            var full = await new EfCoreUnitOfWork(runtime).ExecuteSerializableAsync(token =>
                ReserveAsync(runtime, row, row.SecondOrderId, token));
            await Assert.That(full.Reserved).IsFalse();
            await Assert.That(await inventory.GetAllocatedQuantityAsync(row.PoolId, row.TenantId, CancellationToken.None)).IsEqualTo(1);
            await Assert.That(await inventory.TryConsumeActiveHoldsForOrderAsync(row.Order.Id, row.TenantId, Now, CancellationToken.None)).IsEqualTo(1);
            await Assert.That(await inventory.TryConsumeActiveHoldsForOrderAsync(row.Order.Id, row.TenantId, Now, CancellationToken.None)).IsEqualTo(0);
            await Assert.That(await inventory.TryConsumeActiveHoldsForOrderAsync(
                rows[1].Order.Id, rows[1].TenantId, Now, CancellationToken.None)).IsEqualTo(0);
        }
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.RegistrationInventoryHolds.CountAsync(
            hold => hold.CapacityPoolId == row.PoolId &&
                hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Consumed)).IsEqualTo(1);
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task CancelledProductionAcquisitionDoesNotClaimOrSettleAnyEffect()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await ClaimGlobalAsync(authority, Now, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(async () => await authority.Runtime.CreateDbContextAsync(rows[0].TenantId, cancellation.Token))
            .Throws<OperationCanceledException>();
        await using var verification = fixture.CreateDbContext();
        var effects = await verification.RegistrationFinalizationEffects.AsNoTracking().ToListAsync();
        await Assert.That(effects.Count).IsEqualTo(2);
        await Assert.That(effects.All(effect => effect.Status == OutboxMessageStatus.Pending &&
            effect.ProcessingFence == 0 && effect.AttemptCount == 0)).IsTrue();
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task GlobalClaimStatementFailureRollsBackAllClaimIdentities()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using var control = fixture.CreateDbContext();
        string table = Table<RegistrationFinalizationEffect>(control);
        var effectType = control.Model.FindEntityType(typeof(RegistrationFinalizationEffect))!;
        string schema = effectType.GetSchema()!;
        string idColumn = effectType.FindProperty(nameof(RegistrationFinalizationEffect.Id))!.GetColumnName(
            Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(effectType.GetTableName()!, schema))!;
        Guid lastEffectId = await control.RegistrationFinalizationEffects.AsNoTracking()
            .OrderBy(effect => effect.NextAttemptAt ?? effect.CreatedAt).ThenBy(effect => effect.Id)
            .Select(effect => effect.Id).LastAsync();
        string function = $"\"{schema}\".finalization_native_claim_fault";
        await control.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'finalization_native_fault' USING ERRCODE = '23514'; END $fault$;
            CREATE TRIGGER finalization_native_claim_fault BEFORE UPDATE ON {table}
                FOR EACH ROW WHEN (NEW."{idColumn}" = '{lastEffectId:D}'::uuid)
                EXECUTE FUNCTION {function}();
            """);
        try
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(async () => await ClaimGlobalAsync(authority, Now));
            await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
            var effects = await control.RegistrationFinalizationEffects.AsNoTracking().ToListAsync();
            await Assert.That(effects.All(effect => effect.Status == OutboxMessageStatus.Pending &&
                effect.ProcessingFence == 0 && effect.AttemptCount == 0)).IsTrue();
        }
        finally
        {
            await control.Database.ExecuteSqlRawAsync($"""
                DROP TRIGGER finalization_native_claim_fault ON {table};
                DROP FUNCTION {function}();
                """);
        }
        await Assert.That((await ClaimGlobalAsync(authority, Now)).Count).IsEqualTo(2);
        await RequireFinancialIdentityAsync(rows);
    }

    [Test]
    public async Task OrdinaryLeasesRejectTenantSwitchAndForeignEffectCreation()
    {
        var rows = await SeedAsync();
        await using var authority = await CreateAuthorityAsync();
        await using (var own = await authority.Runtime.CreateDbContextAsync(rows[0].TenantId))
        {
            await RequireOrdinaryAsync(own);
            await Assert.That(() => own.TenantContext = new TenantContext(rows[1].TenantId))
                .Throws<InvalidOperationException>();
            await Assert.That(await own.RegistrationOrders.IgnoreQueryFilters()
                .CountAsync(order => order.TenantId == rows[1].TenantId)).IsEqualTo(0);
        }
        await using (var own = await authority.Runtime.CreateDbContextAsync(rows[0].TenantId))
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(async () =>
                await new RegistrationFinalizationRepository(own).RequestAsync(rows[1].Order, Now, CancellationToken.None));
            await Assert.That((failure!.InnerException as PostgresException)?.SqlState)
                .IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        await using var other = await authority.Runtime.CreateDbContextAsync(rows[1].TenantId);
        await Assert.That(other.TenantFilterTenantId).IsEqualTo(rows[1].TenantId);
        await Assert.That(await other.RegistrationOrders.IgnoreQueryFilters()
            .CountAsync(order => order.TenantId == rows[0].TenantId)).IsEqualTo(0);
        await RequireFinancialIdentityAsync(rows);
    }

    private static Task<RegistrationInventoryReservationResult> ReserveAsync(
        ExploreDbContext context, Seeded row, Guid orderId, CancellationToken token = default) =>
        new RegistrationInventoryRepository(context).ReserveNonTimedHoldsAsync(
            row.Order.EventId, row.TenantId,
            [new RegistrationInventoryReservation(Guid.CreateVersion7(), orderId, row.PoolId, row.TicketTypeId, 1)],
            approvalGranted: false, Now, token);

    private async Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimControlAsync(DateTime time)
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        return await new RegistrationFinalizationQueueRepository(factory).ClaimDueAsync(
            "native-finalization", 2, time, TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static async Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimGlobalAsync(
        Authority authority, DateTime time, CancellationToken token = default)
    {
        return await new RegistrationFinalizationQueueRepository(authority.Global).ClaimDueAsync(
            "native-finalization", 2, time, TimeSpan.FromSeconds(30), token);
    }

    private async Task<Seeded[]> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var user = new User { Pii = new UserPii { Email = "finalization@example.test", FirstName = "Finalization", LastName = "Authority" } };
        context.Add(user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Finalization" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Add(actor);
        await context.SaveChangesAsync();
        var result = new List<Seeded>();
        for (int index = 0; index < 2; index++)
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Finalization authority",
                Slug = $"finalization-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var target = new DomainEvent(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                Title = "Finalization authority",
                Subtitle = string.Empty,
                Description = string.Empty,
                FirstSessionDate = DateOnly.FromDateTime(Now.AddDays(1)),
                LastSessionDate = DateOnly.FromDateTime(Now.AddDays(1)),
                EventTypeId = 1,
                AudienceGenderId = 1,
                AudienceAgeId = 1,
                ActorId = actor.Id,
                Actor = null!,
                OrganizerActorId = actor.Id,
                VisibilityTypeId = 1,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = 1,
                EventFormat = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated
            };
            var catalog = EventTicketCatalogVersion.Create(tenant.Id, target.Id, "USD", 1);
            var pool = EventCapacityPool.Create(tenant.Id, target.Id, "Last seat", 1, 900,
                CapacityHoldPolicyEnum.NoHoldUntilReady, CapacityOversellPolicyEnum.Disallow, true);
            var ticket = EventTicketType.Create(Guid.CreateVersion7(), tenant.Id, catalog.Id, "General", "USD",
                TicketPricingModeEnum.Fixed, Money.Create(100, "USD"), null, null,
                ParticipantDataCollectionModeEnum.None, pool.Id, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, pool);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant.Id, target.Id, 1));
            catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
            catalog.Publish();
            var first = Order(catalog, ticket);
            var second = Order(catalog, ticket);
            string suffix = first.Id.ToString("N");
            var recipient = OrganizerPaymentRecipientSnapshot.Create(tenant.Id, actor.Id, Guid.CreateVersion7(),
                "stripe", "platform-eu", $"acct_{suffix}", "BE", "USD", Guid.CreateVersion7(), Guid.CreateVersion7(), Now.AddMinutes(-5));
            var payment = PaymentAttempt.Create(Guid.CreateVersion7(), tenant.Id, first.Id, recipient, "OrganizerDirect",
                "2026-08-20.acacia", $"composition-{suffix}", Money.Create(100, "USD"), Money.Create(0, "USD"),
                Money.Create(0, "USD"), $"checkout:{suffix}", Now.AddMinutes(-2), Now.AddMinutes(30));
            payment.MarkSucceededFromCheckout($"cs_{suffix}", $"pi_{suffix}", Now, null);
            var observation = PaymentSucceededObservation.Create(payment, null, $"cs_{suffix}", $"pi_{suffix}", null, Now);
            var effect = RegistrationFinalizationEffect.Create(first, Now);
            context.AddRange(tenant, target, catalog, pool, first, second, payment, observation, effect);
            result.Add(new Seeded(tenant.Id, first, second.Id, pool.Id, ticket.Id, effect.Id, payment.Id, observation.Id));
        }
        await context.SaveChangesAsync();
        return result.ToArray();
    }

    private static RegistrationOrder Order(EventTicketCatalogVersion catalog, EventTicketType ticket)
    {
        var order = RegistrationOrder.Create(catalog.TenantId, catalog.EventId, null, null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null, CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            catalog.CurrencyCode, Now.AddMinutes(-5), Now.AddMinutes(15));
        order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, ticket, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 100, 0, 100, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, Now.AddMinutes(-4));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now.AddMinutes(-4));
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now.AddMinutes(-3));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, Now.AddMinutes(-2));
        return order;
    }

    private async Task RequireFinancialIdentityAsync(Seeded[] rows)
    {
        await using var verification = fixture.CreateDbContext();
        foreach (var row in rows)
        {
            var repository = new RegistrationFinalizationRepository(verification);
            var paid = await repository.GetSucceededPaymentAsync(row.TenantId, row.Order.Id, CancellationToken.None);
            await Assert.That(paid.Status).IsEqualTo(SucceededPaymentLookupStatus.Found);
            await Assert.That(paid.Attempt!.Id).IsEqualTo(row.PaymentId);
            await Assert.That(paid.Attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
            await Assert.That(paid.Attempt.CurrencyCode).IsEqualTo("USD");
            await Assert.That(paid.Attempt.OrganizerAmountMinor).IsEqualTo(100L);
            await Assert.That(paid.Attempt.PlatformFeeMinor).IsEqualTo(0L);
            await Assert.That(paid.Attempt.PlatformContributionMinor).IsEqualTo(0L);
            await Assert.That(paid.Attempt.TotalMinor).IsEqualTo(100L);
            await Assert.That(paid.Attempt.ProviderIdempotencyKey).IsEqualTo($"checkout:{row.Order.Id:N}");
            await Assert.That(paid.Attempt.ProfileCode).IsEqualTo("OrganizerDirect");
            await Assert.That(paid.Observation!.Id).IsEqualTo(row.ObservationId);
            var order = await new RegistrationInventoryRepository(verification).GetOrderByIdAsync(
                row.Order.Id, row.TenantId, CancellationToken.None);
            await Assert.That(order!.TotalDueMinorSnapshot).IsEqualTo(100L);
            await Assert.That(order.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.AwaitingPayment);
            await Assert.That(await verification.RegistrationFinalizationEffects
                .Where(effect => effect.RegistrationOrderId == row.Order.Id).Select(effect => effect.Id).SingleAsync()).IsEqualTo(row.EffectId);
        }
        await Assert.That(await verification.EventRegistrations.CountAsync()).IsEqualTo(0);
        await Assert.That(await verification.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    private async Task<Authority> CreateAuthorityAsync(bool entitled = true)
    {
        await using var model = fixture.CreateDbContext();
        await PostgresRegistrationFinalizationQueueContract.ApplyAsync(model, fixture.NativeQueueEnrollment);
        await PostgresRegistrationFinalizationQueueContract.ApplyAsync(model, fixture.NativeQueueEnrollment);
        await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(model, fixture.NativeQueueEnrollment);
        string schema = model.Model.FindEntityType(typeof(RegistrationFinalizationEffect))!.GetSchema()!;
        var globalOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global);
        var runtimeOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        if (!entitled)
            globalOptions = globalOptions with
            {
                Username = $"finalization_outsider_{Guid.CreateVersion7():N}",
                Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
            };
        Type[] runtimeTypes =
        [
            typeof(RegistrationFinalizationEffect), typeof(RegistrationOrder), typeof(RegistrationOrderLine),
            typeof(PaymentAttempt), typeof(PaymentSucceededObservation), typeof(RegistrationInventoryHold),
            typeof(EventCapacityPool), typeof(EventTicketCatalogVersion), typeof(EventTicketType),
            typeof(RegistrationRequirement), typeof(RegistrationRequirementFulfillment)
        ];
        string tables = string.Join(", ", runtimeTypes.Select(type => Table(model, type)));
        string capacityPolicies = Table<CapacityHoldPolicy>(model);
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        if (!entitled)
        {
            command.CommandText = $"""
                CREATE ROLE "{globalOptions.Username}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalOptions.Password}';
                GRANT USAGE ON SCHEMA "{schema}" TO "{globalOptions.Username}";
                """;
            await command.ExecuteNonQueryAsync();
        }
        command.CommandText = $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{runtimeOptions.Username}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON {tables} TO "{runtimeOptions.Username}";
            GRANT SELECT ON {capacityPolicies} TO "{runtimeOptions.Username}";
            """;
        await command.ExecuteNonQueryAsync();
        return new Authority(fixture.ConnectionString, schema, tables, capacityPolicies,
            globalOptions, runtimeOptions, entitled);
    }

    private static async Task RequireOrdinaryAsync(ExploreDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication
            FROM pg_catalog.pg_roles WHERE rolname = current_user
            """;
        await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        command.CommandText = """
            SELECT count(*)::integer FROM pg_catalog.pg_class
            WHERE relowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user)
                AND relnamespace = (SELECT oid FROM pg_catalog.pg_namespace WHERE nspname = @schema)
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "schema";
        parameter.Value = context.Model.FindEntityType(typeof(RegistrationFinalizationEffect))!.GetSchema()!;
        command.Parameters.Add(parameter);
        await Assert.That((int)(await command.ExecuteScalarAsync())!).IsEqualTo(0);
    }

    private static async Task RequirePrivateDenialsAsync(ExploreDbContext global)
    {
        foreach (Type type in new[] { typeof(RegistrationFinalizationEffect), typeof(RegistrationOrder),
            typeof(PaymentAttempt), typeof(PaymentSucceededObservation), typeof(UserPii) })
        {
            var denial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {Table(global, type)}"));
            await Assert.That(denial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            denial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"DELETE FROM {Table(global, type)} WHERE false"));
            await Assert.That(denial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private static string Table<T>(ExploreDbContext context) => Table(context, typeof(T));
    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    private sealed class Authority(string administratorConnection, string schema, string tables, string capacityPolicies,
        PrimaryDatabaseConnectionOptions globalOptions, PrimaryDatabaseConnectionOptions runtimeOptions,
        bool fixtureOwned) : IAsyncDisposable
    {
        public string GlobalLogin => globalOptions.Username
            ?? throw new InvalidOperationException("Native authority requires a Global login.");
        public GlobalExploreDbContextFactory Global { get; } = new(globalOptions, poolSize: 1);
        public TenantExploreDbContextFactory Runtime { get; } = new(runtimeOptions, poolSize: 1);

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
            command.CommandText = $"""
                REVOKE SELECT, INSERT, UPDATE, DELETE ON {tables} FROM "{runtimeOptions.Username}";
                REVOKE SELECT ON {capacityPolicies} FROM "{runtimeOptions.Username}";
                """;
            await command.ExecuteNonQueryAsync();
            if (!fixtureOwned)
            {
                command.CommandText = $"""
                    REVOKE USAGE ON SCHEMA "{schema}" FROM "{globalOptions.Username}";
                    DROP ROLE "{globalOptions.Username}";
                    """;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed record Seeded(Guid TenantId, RegistrationOrder Order, Guid SecondOrderId,
        Guid PoolId, Guid TicketTypeId, Guid EffectId, Guid PaymentId, Guid ObservationId);
    private sealed record TenantContext(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
}
