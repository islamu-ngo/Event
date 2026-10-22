using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Uses actual Local authentication, human preparation and original transaction settlement.</summary>
[NotInParallel]
[ClassDataSource<TenantPreferenceEfTests.PreferenceFixture>(Shared = SharedType.PerAssembly)]
public sealed class TenantPreferenceEfTests(TenantPreferenceEfTests.PreferenceFixture fixture)
{
    [Test]
    [Arguments("active")]
    [Arguments("suspended-tenant")]
    [Arguments("revoked-membership")]
    [Arguments("foreign-membership")]
    public async Task OriginalHumanPreferenceSettlesWithoutDatabaseBusinessRoutines(string scenario)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        var observed = new Observation();
        await using var authentication = await AuthenticateAsync(observed);
        Guid target = await SeedTargetAsync(authentication.ActorId, scenario, token);
        await RemoveRetiredRoutinesAsync(token);
        var before = await ReadUserAsync(authentication.ActorId, token);

        bool result = await authentication.RunAsync(null, session =>
            session.Services.GetRequiredService<IHumanTenantPreferencePreparation>()
                .ExecuteAsync(new UpdateUserLastActiveTenantCommand(session.ActorId, target), token),
            cancellationToken: token);

        bool admitted = scenario is "active" or "suspended-tenant";
        await Assert.That(result).IsEqualTo(admitted);
        var after = await ReadUserAsync(authentication.ActorId, token);
        await Assert.That(after.Preference).IsEqualTo(admitted ? target : before.Preference);
        await Assert.That(after.UpdatedBy).IsEqualTo(admitted ? authentication.ActorId : before.UpdatedBy);
        await Assert.That(after.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
        if (admitted)
        {
            await Assert.That(observed.Written).IsTrue();
            await Assert.That(observed.OriginalOwnerRetained).IsTrue();
            await Assert.That(observed.Isolation).IsEqualTo(IsolationLevel.Serializable);
            await Assert.That(observed.Login).IsEqualTo(fixture.NativeQueueEnrollment.GlobalLogin);
            await Assert.That(after.UpdatedAt).IsNotNull();
        }
        else
        {
            await Assert.That(after).IsEqualTo(before);
        }
    }

    [Test]
    public async Task ConfiguredAccountsHaveOnlyTheFixedPreferenceColumnsAndNoPrivateUserAuthority()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var global = await new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)).CreateDbContextAsync(token);
        await global.Database.OpenConnectionAsync(token);
        var entity = global.Model.FindEntityType(typeof(User))!;
        var names = global.GetService<ISqlGenerationHelper>();
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string table = names.DelimitIdentifier(store.Name, store.Schema);
        string Column(string property) => entity.FindProperty(property)!.GetColumnName(store)!;
        string[] read = [.. new[] { nameof(User.Id), nameof(User.IsDeleted), nameof(User.LastActiveTenantId),
            nameof(User.UpdatedAt), nameof(User.UpdatedBy) }.Select(Column)];
        string[] update = [.. new[] { nameof(User.LastActiveTenantId), nameof(User.UpdatedAt), nameof(User.UpdatedBy) }.Select(Column)];
        await using (var command = global.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT session_user=@login AND current_user=session_user
                    AND NOT pg_catalog.has_table_privilege(current_user,@table,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    AND NOT EXISTS (SELECT 1 FROM unnest(@read::text[]) expected(name)
                        WHERE NOT pg_catalog.has_column_privilege(current_user,@table,expected.name,'SELECT'))
                    AND NOT EXISTS (SELECT 1 FROM unnest(@update::text[]) expected(name)
                        WHERE NOT pg_catalog.has_column_privilege(current_user,@table,expected.name,'UPDATE'))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                OR (pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,permission.privilege)
                                    AND NOT (a.attnum>0 AND
                                        (permission.privilege='SELECT' AND a.attname=ANY(@read::text[])
                                            OR permission.privilege='UPDATE' AND a.attname=ANY(@update::text[]))))))
                """;
            command.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
            command.Parameters.Add(new NpgsqlParameter("table", table));
            command.Parameters.Add(new NpgsqlParameter("read", read));
            command.Parameters.Add(new NpgsqlParameter("update", update));
            await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(true);
        }
        foreach (string sql in new[]
        {
            $"SELECT * FROM {table} WHERE false",
            $"SELECT {names.DelimitIdentifier(Column(nameof(User.CreatedAt)))} FROM {table} WHERE false",
            $"UPDATE {table} SET {names.DelimitIdentifier(Column(nameof(User.IsDeleted)))}=true WHERE false"
        })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => global.Database.ExecuteSqlRawAsync(sql, token));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        var privateRead = await Assert.ThrowsAsync<PostgresException>(() =>
            global.Users.IgnoreAutoIncludes().Where(user => user.Id == Guid.Empty)
                .Select(user => user.Pii.Email).ToArrayAsync(token));
        await Assert.That(privateRead!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        runtime.BindTenantLease();
        var runtimeWrite = await Assert.ThrowsAsync<PostgresException>(() =>
            runtime.Users.IgnoreAutoIncludes().Where(user => user.Id == Guid.Empty)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastActiveTenantId, (Guid?)null), token));
        await Assert.That(runtimeWrite!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task ChangedCompletionImageRollsBackTheOriginalHumanPreferenceEffect()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        var observed = new Observation();
        await using var authentication = await AuthenticateAsync(observed, tamper: true);
        Guid target = await SeedTargetAsync(authentication.ActorId, "active", token);
        var before = await ReadUserAsync(authentication.ActorId, token);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authentication.RunAsync(null, session =>
                session.Services.GetRequiredService<IHumanTenantPreferencePreparation>()
                    .ExecuteAsync(new UpdateUserLastActiveTenantCommand(session.ActorId, target), token),
                cancellationToken: token));

        await Assert.That(observed.Written).IsTrue();
        await Assert.That(observed.Tampered).IsTrue();
        await Assert.That(observed.OriginalOwnerRetained).IsTrue();
        await Assert.That(await ReadUserAsync(authentication.ActorId, token)).IsEqualTo(before);
    }

    private Task<AuthenticatedSettingsFixture> AuthenticateAsync(Observation observed, bool tamper = false) =>
        AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture, configureServices: services =>
        {
            services.RemoveAll<IHumanTenantPreferenceStore>();
            services.AddScoped<HumanTenantPreferenceStore>();
            services.AddScoped<IHumanTenantPreferenceStore>(provider => new ObservedStore(
                provider.GetRequiredService<HumanTenantPreferenceStore>(),
                provider.GetRequiredService<ExploreDbContext>(), observed, tamper));
        });

    private ExploreDbContext OpenInstanceMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private async Task<Guid> SeedTargetAsync(Guid subject, string scenario, CancellationToken token)
    {
        Guid target = Guid.CreateVersion7();
        await using var seed = OpenMigrator();
        await using var human = new HumanSecuritySourceGate(seed);
        await using var humanLease = await human.AcquireAsync(token);
        await using var discovery = new EventDiscoveryEnrollmentGate(seed);
        await using var discoveryLease = await discovery.AcquireAsync(token);
        seed.Tenants.Add(new Tenant
        {
            Id = target,
            FullName = "Preference EF target",
            Slug = $"preference-ef-{target:N}",
            TenantStatusId = (int)(scenario == "suspended-tenant" ? TenantStatusEnum.Suspended : TenantStatusEnum.Active),
            TenantStatus = null!
        });
        Guid member = subject;
        if (scenario == "foreign-membership")
        {
            member = Guid.CreateVersion7();
            seed.Users.Add(new User { Id = member, Pii = null! });
        }
        seed.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = target,
            Tenant = null!,
            UserId = member,
            User = null!,
            StatusId = (int)(scenario == "revoked-membership" ? TenantUserStatusEnum.Suspended : TenantUserStatusEnum.Active)
        });
        await seed.SaveChangesAsync(token);
        return target;
    }

    private async Task RemoveRetiredRoutinesAsync(CancellationToken token)
    {
        await using var context = OpenMigrator();
        string schema = context.Model.FindEntityType(typeof(User))!.GetSchema()!;
        var names = context.GetService<ISqlGenerationHelper>();
        string[] signatures = [.. new[] { "human_tenant_preference_write", "human_tenant_preference_image" }
            .Select(name => names.DelimitIdentifier(name, schema) + "(uuid,uuid,timestamp with time zone)")];
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            foreach (string signature in signatures)
                await context.Database.ExecuteSqlRawAsync($"DROP FUNCTION IF EXISTS {signature}", token);
            await using var absent = context.Database.GetDbConnection().CreateCommand();
            absent.Transaction = transaction.GetDbTransaction();
            absent.CommandText = "SELECT bool_and(pg_catalog.to_regprocedure(signature) IS NULL) FROM unnest(@signatures::text[]) signature";
            absent.Parameters.Add(new NpgsqlParameter("signatures", signatures));
            await Assert.That(await absent.ExecuteScalarAsync(token)).IsEqualTo(true);
            await transaction.CommitAsync(token);
        });
    }

    private async Task<UserImage> ReadUserAsync(Guid subject, CancellationToken token)
    {
        await using var observer = OpenMigrator();
        return await observer.Users.IgnoreAutoIncludes().AsNoTracking().Where(user => user.Id == subject)
            .Select(user => new UserImage(user.LastActiveTenantId, user.UpdatedAt, user.UpdatedBy, user.ConcurrencyStamp))
            .SingleAsync(token);
    }

    private ExploreDbContext OpenMigrator()
    {
        var context = fixture.CreateDbContext();
        context.BindTenantLease();
        return context;
    }

    private sealed record UserImage(Guid? Preference, DateTime? UpdatedAt, Guid? UpdatedBy, Guid ConcurrencyStamp);

    private sealed class Observation
    {
        internal bool Written { get; set; }
        internal bool Tampered { get; set; }
        internal bool OriginalOwnerRetained { get; set; }
        internal IsolationLevel Isolation { get; set; }
        internal string? Login { get; set; }
    }

    private sealed class ObservedStore(
        IHumanTenantPreferenceStore inner, ExploreDbContext context, Observation observed, bool tamper)
        : IHumanTenantPreferenceStore
    {
        public async Task<bool> WriteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
        {
            var transaction = context.Database.CurrentTransaction!;
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var contextId = context.ContextId;
            int backend = connection.ProcessID;
            await using (var identity = connection.CreateCommand())
            {
                identity.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                identity.CommandText = "SELECT session_user::text";
                observed.Login = (string)(await identity.ExecuteScalarAsync(token))!;
            }
            observed.Isolation = transaction.GetDbTransaction().IsolationLevel;
            bool written = await inner.WriteAsync(mutation, token);
            observed.Written = written;
            observed.OriginalOwnerRetained = ReferenceEquals(transaction, context.Database.CurrentTransaction)
                && ReferenceEquals(connection, context.Database.GetDbConnection()) && backend == connection.ProcessID
                && contextId == context.ContextId && context.TenantFilterTenantId is null
                && !context.ChangeTracker.HasChanges();
            if (written && tamper)
                observed.Tampered = await context.Users.IgnoreAutoIncludes().Where(user => user.Id == mutation.SubjectId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastActiveTenantId, (Guid?)null), token) == 1;
            return written;
        }

        public Task RequireCompleteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token) =>
            inner.RequireCompleteAsync(mutation, token);
    }

    /// <summary>Routine retirement is confined to this class's disposable database.</summary>
    public sealed class PreferenceFixture : PostgreSqlContainerFixture;
}
