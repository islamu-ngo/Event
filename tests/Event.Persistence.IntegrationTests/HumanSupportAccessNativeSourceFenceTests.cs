using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanSupportAccessNativeSourceFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task OriginalForwardingServiceFeedsSelectedOwnerFactsWithoutRawHeaderConstruction()
    {
        await using var ingress = await EventVisitorCapabilitySqliteFixture.CreatePostgreSqlAsync(
            fixture.ConnectionString, configureDatabase: database =>
            {
                database["Database:Migrator:Username"] = fixture.NativeQueueEnrollment.MigratorLogin;
                foreach (var purpose in new[]
                {
                    PrimaryDatabaseRole.Runtime,
                    PrimaryDatabaseRole.Global,
                    PrimaryDatabaseRole.EmailPolicyInstanceMutation,
                    PrimaryDatabaseRole.EmailPolicyTenantMutation,
                    PrimaryDatabaseRole.InstanceKeyRead,
                    PrimaryDatabaseRole.InstanceKeyWrite,
                    PrimaryDatabaseRole.InstanceKeyIssuance,
                    PrimaryDatabaseRole.PlatformKeyReport
                })
                {
                    var options = fixture.GetNativeQueueOptions(purpose);
                    database[$"Database:{purpose}:Username"] = options.Username;
                    database[$"Database:{purpose}:Password"] = options.Password;
                }
            }, createSeedContext: () => fixture.CreateDbContext());
        var session = await HumanSupportAccessSourceFenceTests.SeedAsync(ingress);
        var (subject, forwarding, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(ingress, session.Id.ToString());
        await using var migration = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresHumanPrincipalSourceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresHumanSupportAccessSourceContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await using var context = Open(session.TargetTenantId);
        var source = new HumanSupportAccessSourceFence(context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant,
            fixture.NativeQueueEnrollment);
        var setSource = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant,
            enrollment: fixture.NativeQueueEnrollment);
        await Assert.That((await source.ObserveAsync()).Sessions.Single().Id).IsEqualTo(session.Id);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var sets = await setSource.AcquireAsync();
        var held = await source.AcquireAsync(sets, await principal.AcquireAsync(sets));
        await Assert.That((await held.ValidateAsync()).Sessions.Single().Id).IsEqualTo(session.Id);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingSessionOrSettingInsertedAfterSnapshotCannotBecomeInvisible(bool governance)
    {
        var session = await SeedAsync();
        await using (var setup = fixture.CreateDbContext())
        {
            if (governance)
                await setup.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled).ExecuteDeleteAsync();
            else
                await setup.SupportAccessSessions.Where(row => row.Id == session.Id).ExecuteDeleteAsync();
        }
        await using var context = Open(session.TargetTenantId);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var observed = JsonDocument.Parse(await ReadNativeAsync(context, session, false));
        if (!governance) await Assert.That(observed.RootElement.GetProperty("q0").GetArrayLength()).IsEqualTo(0);
        await using (var writer = fixture.CreateDbContext())
        {
            if (governance)
                writer.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = GovernanceSettingKeys.SupportAccess.Enabled,
                    Value = "true",
                    ValueType = SettingValueType.Boolean
                });
            else
            {
                writer.Attach(session);
                writer.Entry(session).State = EntityState.Added;
            }
            await writer.SaveChangesAsync();
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => ReadNativeAsync(context, session, true));
        await Assert.That(error!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedSessionOrGovernanceAfterSerializableSnapshotForcesReprepare(bool governance)
    {
        var session = await SeedAsync();
        await using var owner = Open(session.TargetTenantId);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ReadNativeAsync(owner, session, false);
        await using (var writer = fixture.CreateDbContext())
        {
            if (governance)
                await writer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"));
            else
                await writer.SupportAccessSessions.Where(row => row.Id == session.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ModeId, (int)SupportAccessModeEnum.ReadOnly));
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => ReadNativeAsync(owner, session, true));
        await Assert.That(error!.SqlState).IsEqualTo("40001");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task EmptySessionRangeAndActualRowsStayExcludedWhileRuntimeWritesFailClosed()
    {
        var session = await SeedAsync();
        await using var owner = Open(session.TargetTenantId);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var first = JsonDocument.Parse(await ReadNativeAsync(owner, session, true));
        await Assert.That(first.RootElement.GetProperty("q0")[0].GetProperty("ModeId").GetInt32())
            .IsEqualTo((int)SupportAccessModeEnum.Write);
        await using var contender = fixture.CreateDbContext();
        foreach (string sql in new[]
        {
            $"SELECT id FROM {Table(contender, typeof(SupportAccessSession))} WHERE id = '{session.Id}' FOR UPDATE NOWAIT",
            $"SELECT id FROM {Epoch(contender)} WHERE id = 1 FOR UPDATE NOWAIT"
        })
        {
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(sql));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }
        // A read admission retains its locks without granting direct session mutation.
        await transaction.CreateSavepointAsync("runtime_read_only");
        var denied = await Assert.ThrowsAsync<PostgresException>(() => owner.Database.ExecuteSqlRawAsync(
            $"UPDATE {Table(owner, typeof(SupportAccessSession))} SET mode_id = {{0}} WHERE id = {{1}}",
            (int)SupportAccessModeEnum.ReadOnly, session.Id));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await transaction.RollbackToSavepointAsync("runtime_read_only");
        using var final = JsonDocument.Parse(await ReadNativeAsync(owner, session, true));
        await Assert.That(final.RootElement.GetProperty("q0")[0].GetProperty("ModeId").GetInt32())
            .IsEqualTo((int)SupportAccessModeEnum.Write);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EverySessionAndGovernanceStatementParticipatesEvenWithoutMatchingRows(bool governance)
    {
        await SeedAsync();
        await using var writer = fixture.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {Table(writer, governance ? typeof(SystemSetting) : typeof(SupportAccessSession))} WHERE false");
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {Epoch(contender)} WHERE id = 1 FOR UPDATE NOWAIT"));
        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task DisabledWriterFailsExactNativeAdmission()
    {
        var session = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string table = Table(migration, typeof(SupportAccessSession));
        await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DISABLE TRIGGER zz_human_support_access_source_write");
        try
        {
            await using var owner = Open(session.TargetTenantId);
            await Assert.ThrowsAsync<PostgresException>(() => ReadNativeAsync(owner, session, false));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ENABLE ALWAYS TRIGGER zz_human_support_access_source_write");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnexpectedRoutineGrantOrOwnershipFailsLiveAdmission(bool ownership)
    {
        var session = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        await migration.Database.ExecuteSqlRawAsync(
            PostgresHumanSupportAccessSourceContract.AdmissionSql(migration, fixture.NativeQueueEnrollment));
        string schema = migration.Model.FindEntityType(typeof(User))!.GetSchema()!;
        string routine = migration.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier($"unexpected_support_{Guid.CreateVersion7():N}", schema);
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {routine}() RETURNS integer LANGUAGE sql AS 'SELECT 1';
            REVOKE ALL ON FUNCTION {routine}() FROM PUBLIC;
            {(ownership
                ? $"ALTER FUNCTION {routine}() OWNER TO {PostgresHumanSupportAccessSourceContract.OwnerRole};"
                : $"GRANT EXECUTE ON FUNCTION {routine}() TO {PostgresHumanSupportAccessSourceContract.RuntimeRole};")}
            """);
        // The catalog mutation is visible to the same selected physical owner.
        // No cached positive admission may hide an unexpected routine authority.
        var error = await Assert.ThrowsAsync<PostgresException>(() => migration.Database.ExecuteSqlRawAsync(
            PostgresHumanSupportAccessSourceContract.AdmissionSql(migration, fixture.NativeQueueEnrollment)));
        await Assert.That(error!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
        await using var owner = Open(session.TargetTenantId);
        await ReadNativeAsync(owner, session, false);
    }

    private async Task<SupportAccessSession> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanPrincipalSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanSupportAccessSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Support source",
            Slug = $"support-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Support", LastName = "Source" }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var session = SupportAccessSession.Start(user.Id, tenant.Id, SupportAccessModeEnum.Write,
            "source", "Persisted regression", "fixture", start, start.AddHours(1));
        context.SupportAccessSessions.Add(session);
        var enabled = await context.SystemSettings.SingleOrDefaultAsync(row =>
            row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled);
        if (enabled is null)
        {
            enabled = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.SupportAccess.Enabled,
                Value = "true",
                ValueType = SettingValueType.Boolean
            };
            context.SystemSettings.Add(enabled);
        }
        enabled.Value = "true";
        await context.SaveChangesAsync();
        return session;
    }

    private static async Task<string> ReadNativeAsync(ExploreDbContext context, SupportAccessSession session, bool acquire)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {(acquire ? PostgresHumanSupportAccessSourceContract.AcquireFunctionSql(context)
            : PostgresHumanSupportAccessSourceContract.ObserveFunctionSql(context))}(@actor,1,@tenant,@session,@resolved)";
        command.Parameters.Add(new NpgsqlParameter("actor", session.ActorUserId!.Value));
        command.Parameters.Add(new NpgsqlParameter("tenant", session.TargetTenantId));
        command.Parameters.Add(new NpgsqlParameter("session", session.Id));
        command.Parameters.Add(new NpgsqlParameter("resolved", session.TargetTenantId));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing native facts."));
    }

    private ExploreDbContext Open(Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
    private static string Epoch(ExploreDbContext context) => context.GetService<ISqlGenerationHelper>()
        .DelimitIdentifier("human_support_access_source_epoch", context.Model.FindEntityType(typeof(User))!.GetSchema());
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
