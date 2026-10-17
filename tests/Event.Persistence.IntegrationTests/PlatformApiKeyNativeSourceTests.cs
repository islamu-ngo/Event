using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

/// <summary>Real SQL facts/privileges, not fabricated HTTP authentication or managed action receipts.</summary>
[NotInParallel("PersistenceDb")]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PlatformApiKeyNativeSourceTests(PostgreSqlContainerFixture fixture)
{
    internal static PlatformApiKeySourceEnrollment Enrollment(PostgreSqlContainerFixture fixture) =>
        new(fixture.NativeQueueEnrollment,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.InstanceKeyRead).Username!,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.InstanceKeyWrite).Username!,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.PlatformKeyReport).Username!);

    [Test]
    [Arguments(PrimaryDatabaseRole.InstanceKeyRead, PlatformApiKeySourcePurpose.Read)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyWrite, PlatformApiKeySourcePurpose.Write)]
    [Arguments(PrimaryDatabaseRole.PlatformKeyReport, PlatformApiKeySourcePurpose.Report)]
    public async Task DedicatedLoginHoldsAllOriginalSourceAnchorsWithoutRawSourceAccess(
        PrimaryDatabaseRole role, PlatformApiKeySourcePurpose purpose)
    {
        Guid actor = await SeedAsync();
        await using var context = Open(role);
        using var observed = JsonDocument.Parse(await ReadAsync(context, actor, purpose, false));
        await Assert.That(observed.RootElement.GetProperty("Principal").GetProperty("q0")[0].GetProperty("Id").GetGuid())
            .IsEqualTo(actor);
        // No Local account was provisioned by this SQL-only control. Facts cannot invent readiness.
        await Assert.That(observed.RootElement.GetProperty("Principal").GetProperty("l0").GetArrayLength()).IsEqualTo(0);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var held = JsonDocument.Parse(await ReadAsync(context, actor, purpose, true));
        await Assert.That(held.RootElement.GetProperty("Route").GetProperty("System").GetArrayLength()).IsGreaterThan(0);
        await using var contender = fixture.CreateDbContext();
        foreach (string anchor in new[] { "policy_source_set", "human_principal_source_epoch", "human_support_access_source_epoch" })
        {
            await using var transaction = await contender.Database.BeginTransactionAsync();
            var denied = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {new HumanPrincipalSourceSql(contender).Name(anchor)} WHERE id = 1 FOR UPDATE NOWAIT"));
            await Assert.That(denied!.SqlState).IsEqualTo("55P03");
            await transaction.RollbackAsync();
        }
        await owner.RollbackAsync();
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            $"SELECT revision FROM {new HumanPrincipalSourceSql(context).Name("human_principal_source_epoch")}"));
        await Assert.ThrowsAsync<PostgresException>(() => context.LocalIdentityUsers.CountAsync());
    }

    [Test]
    public async Task AStaleSnapshotCannotHideANewNegativeSetContributor()
    {
        Guid actor = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.InstanceKeyRead);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadAsync(context, actor, PlatformApiKeySourcePurpose.Read, false);
        await using (var writer = fixture.CreateDbContext())
        {
            writer.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = actor,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            await writer.SaveChangesAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            ReadAsync(context, actor, PlatformApiKeySourcePurpose.Read, true));
        await Assert.That(denied!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyWrite)]
    [Arguments(PrimaryDatabaseRole.PlatformKeyReport)]
    public async Task AnotherActualLoginCannotClaimTheReadSourcePurpose(PrimaryDatabaseRole role)
    {
        Guid actor = await SeedAsync();
        await using var context = Open(role);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, actor, PlatformApiKeySourcePurpose.Read, false));
    }

    [Test]
    public async Task AcquiringFactsRequiresTheActualSerializableOwner()
    {
        Guid actor = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.InstanceKeyRead);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            ReadAsync(context, actor, PlatformApiKeySourcePurpose.Read, true));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments("extra-select")]
    [Arguments("public-execute")]
    [Arguments("disabled-writer")]
    [Arguments("executor-set")]
    public async Task NativeSourceOrPrivilegeDriftCannotPassAdmission(string drift)
    {
        Guid actor = await SeedAsync();
        await using var administration = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(administration);
        string signature = PostgresPlatformApiKeySourceContract.FunctionSql(administration, false)
            + "(uuid,integer,text,integer,text,uuid,uuid)";
        var originalSelectColumns = await administration.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.quote_ident(a.attname)::text AS "Value"
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid = pg_catalog.to_regclass({0}) AND a.attnum > 0 AND NOT a.attisdropped
                AND pg_catalog.has_column_privilege({1}, a.attrelid, a.attnum, 'SELECT')
            ORDER BY a.attnum
            """, sql.Table(typeof(SystemSetting)), PostgresPlatformApiKeySourceContract.OwnerRole).ToListAsync();
        string change = drift switch
        {
            "extra-select" => $"GRANT SELECT ON {sql.Table(typeof(SystemSetting))} TO {PostgresPlatformApiKeySourceContract.OwnerRole}",
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            "disabled-writer" => $"ALTER TABLE {sql.Table(typeof(User))} DISABLE TRIGGER human_principal_source_write",
            "executor-set" => $"GRANT {PostgresInstanceApiKeyManagementContract.ReadExecutor} TO \"{Enrollment(fixture).ReadLogin}\" WITH SET TRUE",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string restore = drift switch
        {
            "extra-select" => $"REVOKE SELECT ON {sql.Table(typeof(SystemSetting))} FROM {PostgresPlatformApiKeySourceContract.OwnerRole}; GRANT SELECT ({string.Join(",", originalSelectColumns)}) ON {sql.Table(typeof(SystemSetting))} TO {PostgresPlatformApiKeySourceContract.OwnerRole}",
            "public-execute" => $"REVOKE EXECUTE ON FUNCTION {signature} FROM PUBLIC",
            "disabled-writer" => $"ALTER TABLE {sql.Table(typeof(User))} ENABLE ALWAYS TRIGGER human_principal_source_write",
            "executor-set" => $"GRANT {PostgresInstanceApiKeyManagementContract.ReadExecutor} TO \"{Enrollment(fixture).ReadLogin}\" WITH SET FALSE",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await administration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var context = Open(PrimaryDatabaseRole.InstanceKeyRead);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                ReadAsync(context, actor, PlatformApiKeySourcePurpose.Read, false));
            await Assert.That(denied!.SqlState).IsEqualTo("55000");
        }
        finally { await administration.Database.ExecuteSqlRawAsync(restore); }
    }

    private async Task<Guid> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPlatformApiKeySourceContract.ApplyAsync(context, Enrollment(fixture));
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = string.Empty, FirstName = "Platform", LastName = "Source" }
        };
        context.Users.Add(user);
        var route = await context.SystemSettings.SingleOrDefaultAsync(setting =>
            setting.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
        if (route is null)
            context.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"",
                ValueType = SettingValueType.String
            });
        else
            route.Value = "\"local\"";
        await context.SaveChangesAsync();
        return user.Id;
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private async Task<string> ReadAsync(ExploreDbContext context, Guid actor,
        PlatformApiKeySourcePurpose purpose, bool acquire)
    {
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = PostgresPlatformApiKeySourceContract.AdmissionSql(context, Enrollment(fixture));
        await command.ExecuteNonQueryAsync();
        command.CommandText = $"SELECT {PostgresPlatformApiKeySourceContract.FunctionSql(context, acquire)}"
            + "(@actor,@provider,@account,@purpose,@stamp,@session,@resolved)";
        command.Parameters.Add(new NpgsqlParameter("actor", actor));
        command.Parameters.Add(new NpgsqlParameter("provider", (int)AuthenticationProviderKind.Local));
        command.Parameters.Add(new NpgsqlParameter("account", actor.ToString("D")));
        command.Parameters.Add(new NpgsqlParameter("purpose", (int)purpose));
        command.Parameters.Add(new NpgsqlParameter("stamp", Guid.CreateVersion7().ToString("D")));
        command.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Uuid) { Value = DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("resolved", NpgsqlDbType.Uuid) { Value = DBNull.Value });
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("No native facts."));
    }
}
