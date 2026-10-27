using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PolicySourceSetFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LookupSeederCannotRepairEmptyPermissionSetBehindHeldFence()
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        string schema = setup.Model.FindEntityType(typeof(SystemSetting))!.GetSchema()!;
        var original = await setup.RolePermissions.AsNoTracking().ToListAsync();
        await setup.RolePermissions.ExecuteDeleteAsync();
        try
        {
            await using var owner = Open(schema);
            var fence = Fence(owner);
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var lease = await fence.AcquireAsync();
            await using var writer = fixture.CreateDbContext();
            await using (var attempt = await writer.Database.BeginTransactionAsync())
            {
                await BoundLockConflictAsync(writer, () => LookupTableSeeder.SeedAsync(writer));
                await attempt.RollbackAsync();
            }
            await Assert.That(await setup.RolePermissions.AnyAsync()).IsFalse();
            await lease.ValidateAsync();
            await transaction.RollbackAsync();
            writer.ChangeTracker.Clear();
            await LookupTableSeeder.SeedAsync(writer);
            await Assert.That(await setup.RolePermissions.AnyAsync()).IsTrue();
        }
        finally
        {
            await setup.RolePermissions.ExecuteDeleteAsync();
            setup.RolePermissions.AddRange(original);
            await setup.SaveChangesAsync();
        }
    }

    [Test]
    public async Task NativeScalarImportRejectsRouteKeyWithoutBroadeningItsCatalog()
    {
        await using var setup = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(setup, fixture.NativeQueueEnrollment);
        string schema = setup.Model.FindEntityType(typeof(SystemSetting))!.GetSchema()!;
        string global = setup.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        await setup.Database.ExecuteSqlRawAsync(
            $"GRANT {PostgresConfigurationImportScalarSettingContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET FALSE");
        await using var owner = Open(schema);
        var fence = Fence(owner);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await fence.AcquireAsync();
        await using var writer = Open(schema);
        writer.BindConfigurationInstanceImportOwner();
        await using var attempt = await writer.Database.BeginTransactionAsync();
        // A rejected route key cannot acquire an import effect proof. Exercise the actual
        // native adapter's catalog guard directly, rather than satisfying this test with
        // an earlier unauthenticated repository rejection.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PostgresConfigurationImportScalarSettingContract.UpsertAsync(writer, Setting(), CancellationToken.None));
        // Bypass only the C# argument gate, not the real native registry/ACL/selected login.
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await writer.Database.ExecuteSqlRawAsync(
                $$"""
                SELECT "{{schema}}".configuration_import_scalar_upsert
                ({0}::text, NULL::text, NULL::integer, NULL::text, NULL::text, NULL::boolean,
                NULL::jsonb, NULL::integer, NULL::uuid, NULL::uuid, NULL::text, NULL::text)
                """,
                GovernanceSettingKeys.Cerbos.Mode));
        await Assert.That(denied!.SqlState).IsEqualTo("22023");
        await attempt.RollbackAsync();
        await lease.ValidateAsync();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task EmptyPermissionSetExcludesFirstUnrelatedRoleInsertionUntilRollback()
    {
        string schema = await InstallAsync();
        await using var observer = fixture.CreateDbContextForSchema(schema);
        await Assert.That(await observer.RolePermissions.AnyAsync()).IsFalse();
        await using var owner = Open(schema);
        var fence = Fence(owner);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await fence.AcquireAsync();

        await using var writer = fixture.CreateDbContextForSchema(schema);
        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            await BoundLockConflictAsync(writer, () => new RoleRepository(writer)
                .AssignPermissionsAsync((int)RoleEnum.Admin, [1]));
            await attempt.RollbackAsync();
        }
        writer.ChangeTracker.Clear();
        await Assert.That(await observer.RolePermissions.AnyAsync()).IsFalse();
        await lease.ValidateAsync();
        await transaction.RollbackAsync();
        await new RoleRepository(writer).AssignPermissionsAsync((int)RoleEnum.Admin, [1]);
        await Assert.That(await observer.RolePermissions.AnyAsync()).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
    }

    [Test]
    [Arguments("regular")]
    [Arguments("borrowed")]
    [Arguments("lock")]
    [Arguments("tenant")]
    [Arguments("tenant-batch")]
    [Arguments("native-insert")]
    public async Task AbsentRouteInsertionCannotCommitBehindFence(string writerKind)
    {
        string schema = await InstallAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreatePolicyAsync(fixture, schema);
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        Guid assignmentId = Guid.CreateVersion7();
        if (writerKind == "tenant-batch")
        {
            await using var seed = fixture.CreateDbContextForSchema(schema);
            var plan = new TenantPlan { Id = Guid.CreateVersion7(), Key = $"fence-{assignmentId:N}", DisplayName = "Fence plan" };
            var version = new TenantPlanVersion
            {
                Id = Guid.CreateVersion7(),
                TenantPlanId = plan.Id,
                TenantPlan = plan,
                VersionNumber = 1,
                TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
                CurrencyCode = "EUR",
                BillingPeriod = "monthly",
                IsActiveForProvisioning = true
            };
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersionId = version.Id,
                SettingKey = GovernanceSettingKeys.Cerbos.Mode,
                JsonValue = "\"custom_endpoint\"",
                IsLocked = false
            });
            seed.AddRange(plan, version, new TenantPlanAssignment
            {
                Id = assignmentId,
                TenantId = tenantId,
                TenantPlanId = plan.Id,
                TenantPlan = plan,
                TenantPlanVersionId = version.Id,
                TenantPlanVersion = version,
                TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
                AssignedByUserId = authenticated.ActorId,
                AssignedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var owner = Open(schema);
        var fence = Fence(owner);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await fence.AcquireAsync();
        await using var writer = fixture.CreateDbContextForSchema(schema);

        async Task WriteAsync()
        {
            async Task ManagedWriteAsync()
            {
                await authenticated.RunAsync(writerKind is "tenant" or "tenant-batch" ? tenantId : null, async session =>
                {
                    var settings = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
                    var tenants = new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary);
                    if (writerKind == "tenant-batch")
                    {
                        // Paired value/lock writes belong to a real original plan, not a
                        // forged scalar batch. Its native typed writer still changes the
                        // same tenant route source covered by the held source-set fence.
                        var handler = ActivatorUtilities.CreateInstance<ApplyInstanceTenantPlanAssignmentCommandHandler>(
                            session.Services, (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
                        var applied = await handler.ExecuteAsync(new(tenantId, assignmentId, session.ActorId), CancellationToken.None);
                        await Assert.That(applied.IsSuccess).IsTrue();
                    }
                    else
                    {
                        await session.ValueAsync(GovernanceSettingKeys.Cerbos.Mode, "custom_endpoint", "\"custom_endpoint\"",
                            async token =>
                            {
                                if (writerKind == "tenant")
                                    await tenants.SetValueAsync(tenantId, GovernanceSettingKeys.Cerbos.Mode,
                                        "\"custom_endpoint\"", token, session.ActorId);
                                else if (writerKind == "borrowed")
                                    await settings.UpsertInCurrentTransactionAsync(Setting(), token);
                                else
                                    await settings.UpsertAsync(Setting(), token);
                                return true;
                            });
                        if (writerKind == "lock")
                            await session.LockAsync(GovernanceSettingKeys.Cerbos.Mode, false,
                                token => settings.UpsertLockAsync(Setting(), token));
                    }
                    return true;
                }, mixed: writerKind == "tenant-batch", boundNativeLockWait: true);
            }
            switch (writerKind)
            {
                case "regular":
                case "borrowed":
                case "lock":
                case "tenant":
                case "tenant-batch":
                    await ManagedWriteAsync();
                    break;
                case "native-insert":
                    await writer.Database.ExecuteSqlRawAsync(
                        $$"""
                        INSERT INTO {{Table(writer, typeof(SystemSetting))}}
                        (id, setting_key, value, setting_value_type_id, is_locked, display_order, created_at)
                        VALUES ({0}, {1}, {2}, {3}, false, 0, CURRENT_TIMESTAMP)
                        """,
                        Guid.CreateVersion7(), GovernanceSettingKeys.Cerbos.Mode,
                        "\"custom_endpoint\"", (int)SettingValueType.String);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(writerKind));
            }
        }

        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            await BoundLockConflictAsync(writer, WriteAsync);
            await attempt.RollbackAsync();
        }
        writer.ChangeTracker.Clear();
        await using var observer = fixture.CreateDbContextForSchema(schema);
        await Assert.That(await observer.SystemSettings.AnyAsync()).IsFalse();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync()).IsFalse();
        await lease.ValidateAsync();
        await transaction.CommitAsync();

        await using (var released = await writer.Database.BeginTransactionAsync())
        {
            await WriteAsync();
            await released.CommitAsync();
        }
        await Assert.That(await observer.SystemSettings.AnyAsync()
            || await observer.TenantSettingOverrides.AnyAsync()).IsTrue();
    }

    [Test]
    public async Task SnapshotTakenBeforeRouteInsertionMustAbortRatherThanAcquireStaleFence()
    {
        string schema = await InstallAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreatePolicyAsync(fixture, schema);
        await using var owner = Open(schema);
        var fence = Fence(owner);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await owner.Database.ExecuteSqlRawAsync("SELECT count(*) FROM pg_catalog.pg_class");
        await authenticated.RunAsync(null, session =>
            session.ValueAsync(GovernanceSettingKeys.Cerbos.Mode, "custom_endpoint", "\"custom_endpoint\"", token =>
                new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary).UpsertAsync(Setting(), token)));
        var conflict = await Assert.ThrowsAsync<PostgresException>(() => fence.AcquireAsync());
        await Assert.That(conflict!.SqlState).IsEqualTo("40001");
        await transaction.RollbackAsync();
        await using var fresh = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await fence.AcquireAsync();
        await lease.ValidateAsync();
        await fresh.CommitAsync();
    }

    [Test]
    public async Task UnchangedFenceRollsBackStagedEffectsAndCannotMoveToAnotherTransaction()
    {
        string schema = await InstallAsync();
        await using var owner = Open(schema);
        var fence = Fence(owner);
        await using var writer = fixture.CreateDbContextForSchema(schema);
        await new RoleRepository(writer).AssignPermissionsAsync((int)RoleEnum.Admin, [1]);
        string global = writer.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        await writer.Database.ExecuteSqlRawAsync(
            $"""
            CREATE TABLE "{schema}".probe_effects (id integer PRIMARY KEY);
            GRANT INSERT ON "{schema}".probe_effects TO {global}
            """);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await fence.AcquireAsync();
        await lease.ValidateAsync();
        await owner.Database.ExecuteSqlRawAsync($"INSERT INTO \"{schema}\".probe_effects (id) VALUES (1)");
        await lease.ValidateAsync();
        await transaction.RollbackAsync();
        await using var next = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
        await next.RollbackAsync();

        await using var observer = fixture.CreateDbContextForSchema(schema);
        await Assert.That(await observer.Database.SqlQueryRaw<long>(
            $"SELECT count(*) AS \"Value\" FROM \"{schema}\".probe_effects").SingleAsync()).IsEqualTo(0L);
        await Assert.That(await observer.RolePermissions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("disabled-trigger")]
    [Arguments("trigger-body")]
    [Arguments("owner-login")]
    [Arguments("settings-select")]
    [Arguments("public-execute")]
    public async Task SourceDefinedAdmissionRejectsPersistedNativeDrift(string drift)
    {
        string schema = await InstallAsync();
        await using var administrator = fixture.CreateDbContextForSchema(schema);
        await using var tampering = await administrator.Database.BeginTransactionAsync();
        string table = Table(administrator, typeof(SystemSetting));
        string routine = $"\"{schema}\".policy_source_set_acquire(integer,uuid)";
        await administrator.Database.ExecuteSqlRawAsync(drift switch
        {
            "disabled-trigger" => $"ALTER TABLE {table} DISABLE TRIGGER policy_source_set_write",
            "trigger-body" => $"CREATE OR REPLACE FUNCTION \"{schema}\".policy_source_set_setting_write() "
                + "RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog "
                + "AS 'BEGIN RETURN NEW; END'",
            "owner-login" => $"ALTER ROLE {PostgresPolicySourceSetContract.OwnerRole} LOGIN",
            "settings-select" => $"GRANT SELECT ON {table} TO {PostgresPolicySourceSetContract.OwnerRole}",
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        });
        // Publish real drift to the restricted connection. Restore cluster-level changes below;
        // per-test schema objects are owned by the existing fixture's isolated-schema lifecycle.
        await tampering.CommitAsync();
        try
        {
            await using var owner = Open(schema);
            var fence = Fence(owner);
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<PostgresException>(() => fence.AcquireAsync());
            await transaction.RollbackAsync();
        }
        finally
        {
            if (drift == "owner-login")
                await administrator.Database.ExecuteSqlRawAsync(
                    $"ALTER ROLE {PostgresPolicySourceSetContract.OwnerRole} NOLOGIN");
            if (drift == "settings-select")
                await administrator.Database.ExecuteSqlRawAsync(
                    $"REVOKE SELECT ON {table} FROM {PostgresPolicySourceSetContract.OwnerRole}");
        }
    }

    [Test]
    public async Task WrongPurposeAndBroadPrivateProjectionAreDenied()
    {
        string schema = await InstallAsync();
        await using var owner = Open(schema);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await owner.Database.ExecuteSqlRawAsync(
                $"SELECT \"{schema}\".policy_source_set_acquire(1, NULL::uuid)"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
        var projection = await Assert.ThrowsAsync<PostgresException>(async () =>
            await owner.SystemSettings.AsNoTracking().ToListAsync());
        await Assert.That(projection!.SqlState).IsEqualTo("42501");
    }

    private PolicySourceSetFence Fence(ExploreDbContext context) =>
        new(context, PolicySourceSetPurpose.Global, fixture.NativeQueueEnrollment);

    private ExploreDbContext Open(string schema)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global, schema));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options);
    }

    private async Task<string> InstallAsync()
    {
        string schema = await fixture.CreateIsolatedSchemaAsync("policy_sets_");
        await using var original = fixture.CreateDbContext();
        await using var context = fixture.CreateDbContextForSchema(schema);
        // Real persisted source tables with production columns/checks/indexes; no grant mocks.
        foreach (Type type in new[] { typeof(RolePermission), typeof(SystemSetting), typeof(TenantSetting) })
            await context.Database.ExecuteSqlRawAsync(
                $"CREATE TABLE {Table(context, type)} (LIKE {Table(original, type)} INCLUDING ALL)");
        await PostgresPolicySourceSetContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        return schema;
    }

    private static async Task BoundLockConflictAsync(ExploreDbContext writer, Func<Task> write)
    {
        // The server rejects the actual DML lock request. No scheduling delay is evidence.
        await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
        Exception? failure = null;
        try { await write(); }
        catch (Exception error) { failure = error; }
        while (failure is not null and not PostgresException && failure.InnerException is not null)
            failure = failure.InnerException;
        await Assert.That(failure is PostgresException { SqlState: "55P03" }).IsTrue();
    }

    private static SystemSetting Setting() => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = GovernanceSettingKeys.Cerbos.Mode,
        Value = "\"custom_endpoint\"",
        ValueType = SettingValueType.String,
        CreatedAt = DateTime.UtcNow
    };

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
}
