using System.Data;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class VisitorAccessSettingsWriterNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string ModeKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode;
    private const string Conflict = "visitor_access_account_required_conflict";

    [Test]
    public async Task PreparedVisitorCannotBorrowCallerTransactionOrWrite()
    {
        await PrepareColocatedAuthorityAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture);
        await authenticated.RunAsync(null, async session =>
        {
            var preparation = session.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var prepared = await preparation.PrepareVisitorAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AnonymousOnly\"")],
                SettingsMutationOperation.InstanceSettings, session.ActorId);
            bool entered = false;
            var context = session.Services.GetRequiredService<ExploreDbContext>();
            try
            {
                return await session.Services.GetRequiredService<ISettingMutationLock>()
                    .ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys], async token =>
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        preparation.ExecutePreparedAsync(prepared, (_, _) =>
                        {
                            entered = true;
                            return Task.FromResult(true);
                        }, token));
                    await Assert.That(entered).IsFalse();
                    await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
                    await transaction.RollbackAsync(token);
                    return true;
                });
            }
            finally { await preparation.CloseAsync(prepared, null); }
        });
    }

    [Test]
    public async Task PreparedVisitorRejectsSubstitutedEffectsWithoutWriting()
    {
        await PrepareColocatedAuthorityAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture);
        await authenticated.RunAsync(PlatformDefaults.DefaultTenantId, async session =>
        {
            var preparation = session.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var mutations = ImmutableArray.Create(new VisitorAccessSettingMutation(
                PlatformDefaults.DefaultTenantId, ModeKey, VisitorAccessSettingMutationKind.SetValue,
                "\"FullRegistrationAndAuth\""));
            var prepared = await preparation.PrepareVisitorAsync(mutations,
                SettingsMutationOperation.TenantSettings, session.ActorId);
            try
            {
                return await session.Services.GetRequiredService<ISettingMutationLock>()
                    .ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys], lockToken =>
                    preparation.ExecutePreparedAsync(prepared, async (_, token) =>
                {
                    var visitor = (IHumanVisitorSettingsPreparation)preparation;
                    var original = visitor.CurrentVisitorOriginal!;
                    var store = visitor.CurrentVisitorStore!;
                    var substituted = new VisitorSettingsOriginal(original.Command, original.Effects,
                        original.Subject, original.TenantId, original.AuditActor, original.OccurredAt);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        store.ReadSnapshotAsync(substituted, token));
                    var anotherTenant = new VisitorSettingsOriginal(original.Command, original.Effects,
                        original.Subject, Guid.CreateVersion7(), original.AuditActor, original.OccurredAt);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        store.ReadSnapshotAsync(anotherTenant, token));
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                            [mutations[0] with { Value = "\"AnonymousOnly\"" }],
                            session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                            SettingsMutationOperation.TenantSettings, session.ActorId, token));
                    var accepted = await session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                        mutations, session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                        SettingsMutationOperation.TenantSettings, session.ActorId, token);
                    await Assert.That(accepted.Success).IsTrue();
                    return true;
                }, lockToken));
            }
            finally { await preparation.CloseAsync(prepared, null); }
        });
        var inspectOptions = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(inspectOptions,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var inspect = new ExploreDbContext(inspectOptions.Options)
        {
            TenantContext = new NativeTenant(PlatformDefaults.DefaultTenantId)
        };
        inspect.BindTenantLease();
        await Assert.That((await inspect.TenantSettingOverrides.SingleAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == ModeKey)).Value)
            .IsEqualTo("\"FullRegistrationAndAuth\"");
    }

    [Test]
    public async Task NativeVisitorLockPreservesProvisioningSourceForTheNextOrdinaryWrite()
    {
        await PrepareColocatedAuthorityAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture);
        var initialized = await authenticated.RunAsync(null, session =>
            session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"FullRegistrationAndAuth\"")],
                session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                SettingsMutationOperation.InstanceSettings, session.ActorId));
        await Assert.That(initialized.Success).IsTrue();
        var locked = await authenticated.RunAsync(null, session =>
            session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetLock, null, true)],
                session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                SettingsMutationOperation.InstanceSettings, session.ActorId));
        await Assert.That(locked.Success).IsTrue();

        await using var ordinary = OpenInstanceMigrator();
        var setting = await ordinary.SystemSettings.SingleAsync(row => row.SettingKey == ModeKey);
        await Assert.That(setting.IsLocked).IsTrue();
        Guid id = setting.Id;
        setting.IsLocked = false;
        await ordinary.SaveChangesAsync();
        var persisted = await ordinary.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == ModeKey);
        await Assert.That(persisted.Id).IsEqualTo(id);
        await Assert.That(persisted.IsLocked).IsFalse();
        await Assert.That(persisted.Value).IsEqualTo("\"FullRegistrationAndAuth\"");
    }

    [Test]
    public async Task PreparedVisitorUpdatesExistingJsonValueWithoutReplacingItsRow()
    {
        await PrepareColocatedAuthorityAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture);
        var first = await authenticated.RunAsync(null, session =>
            session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AuthenticatedOnly\"")],
                session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                SettingsMutationOperation.InstanceSettings, session.ActorId));
        await Assert.That(first.Success).IsTrue();
        await using var inspect = OpenInstanceMigrator();
        var original = await inspect.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == ModeKey);
        await Assert.That(original.Value).IsEqualTo("\"AuthenticatedOnly\"");

        var second = await authenticated.RunAsync(null, session =>
            session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"FullRegistrationAndAuth\"")],
                session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                SettingsMutationOperation.InstanceSettings, session.ActorId));
        await Assert.That(second.Success).IsTrue();
        await Assert.That(second.FailureCode).IsNull();
        var updated = await inspect.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == ModeKey);
        await Assert.That(updated.Id).IsEqualTo(original.Id);
        await Assert.That(updated.Value).IsEqualTo("\"FullRegistrationAndAuth\"");
    }

    [Test]
    public async Task PreparedVisitorCannotReplayAfterItsPhysicalAttempt()
    {
        await PrepareColocatedAuthorityAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture);
        await authenticated.RunAsync(null, async session =>
        {
            var preparation = session.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var mutations = ImmutableArray.Create(new VisitorAccessSettingMutation(
                null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"FullRegistrationAndAuth\""));
            var prepared = await preparation.PrepareVisitorAsync(mutations,
                SettingsMutationOperation.InstanceSettings, session.ActorId);
            VisitorSettingsOriginal? retainedOriginal = null;
            IVisitorSettingsOriginalStore? retainedStore = null;
            try
            {
                await session.Services.GetRequiredService<ISettingMutationLock>()
                    .ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys], lockToken =>
                    preparation.ExecutePreparedAsync(prepared, async (_, token) =>
                {
                    var visitor = (IHumanVisitorSettingsPreparation)preparation;
                    retainedOriginal = visitor.CurrentVisitorOriginal!;
                    retainedStore = visitor.CurrentVisitorStore!;
                    var result = await session.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                        mutations, session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                        SettingsMutationOperation.InstanceSettings, session.ActorId, token);
                    await Assert.That(result.Success).IsTrue();
                    return true;
                }, lockToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    preparation.ExecutePreparedAsync(prepared, (_, _) => Task.FromResult(true)));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    retainedStore!.ReadSnapshotAsync(retainedOriginal!, CancellationToken.None));
                return true;
            }
            finally { await preparation.CloseAsync(prepared, null); }
        });
    }

    [Test]
    [Arguments("FullRegistrationAndAuth", false, true)]
    [Arguments("AnonymousOnly", false, false)]
    [Arguments("FullRegistrationAndAuth", true, false)]
    public async Task GlobalMutationValidatesForeignOverrideAndInstanceLock(
        string foreignMode, bool instanceLocked, bool accepted)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, foreignMode);
        await rows.AssertRuntimeIsolationAsync();
        await using var context = await rows.Factory.CreateDbContextAsync();
        await rows.AssertGlobalAuthorityAsync(context);
        var writer = CreateWriter(context);

        var result = await writer.ApplyInternalAsync(
            [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AnonymousOnly\"", instanceLocked)],
            rows.Authentication.UserId);

        await Assert.That(result.Success).IsEqualTo(accepted);
        await Assert.That(result.FailureCode).IsEqualTo(accepted ? null : Conflict);
        await Assert.That(result.DeferredNotifications.Length).IsEqualTo(accepted ? 1 : 0);
        await using var inspect = fixture.CreateDbContext();
        await Assert.That((await inspect.SystemSettings.SingleAsync(row => row.SettingKey == ModeKey)).Value)
            .IsEqualTo(accepted ? "\"AnonymousOnly\"" : "\"FullRegistrationAndAuth\"");
        await Assert.That((await inspect.SystemSettings.SingleAsync(row => row.SettingKey == ModeKey)).IsLocked)
            .IsEqualTo(accepted && instanceLocked);
        await Assert.That((await inspect.TenantSettingOverrides.SingleAsync(row =>
            row.TenantId == rows.Authentication.A && row.SettingKey == ModeKey)).Value)
            .IsEqualTo($"\"{foreignMode}\"");
    }

    [Test]
    public async Task CurrentTransactionReadsUncommittedInstanceLockAndDoesNotCommitRejectedMutation()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, "FullRegistrationAndAuth");
        await rows.AssertRuntimeIsolationAsync();
        await using var context = await rows.Factory.CreateDbContextAsync();
        await rows.AssertGlobalAuthorityAsync(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var writer = new VisitorAccessSettingsWriter(context, mutationLock, unitOfWork,
            new EventParticipationConfigurationRepository(context), new ConfigurationBuilder().Build());
        var mode = context.Model.FindEntityType(typeof(SystemSetting))!;
        string table = $"\"{mode.GetSchema()}\".\"{mode.GetTableName()}\"";

        await mutationLock.ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys], async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"UPDATE {table} SET value = @value, is_locked = TRUE WHERE setting_key = @key";
            command.Parameters.Add(new NpgsqlParameter("value", "\"AnonymousOnly\""));
            command.Parameters.Add(new NpgsqlParameter("key", ModeKey));
            await command.ExecuteNonQueryAsync(token);

            var result = await writer.ApplyInternalAsync(
                [new(null, GovernanceSettingKeys.Authentication.GooglePublicSignupUrl,
                    VisitorAccessSettingMutationKind.SetValue, "\"https://accounts.example.test/changed\"")],
                rows.Authentication.UserId, token);
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(Conflict);
            await Assert.That(result.DeferredNotifications).IsEmpty();
            await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
            await transaction.RollbackAsync(token);
            return true;
        });

        await using var inspect = fixture.CreateDbContext();
        await Assert.That((await inspect.SystemSettings.SingleAsync(row => row.SettingKey == ModeKey)).IsLocked).IsFalse();
        await Assert.That((await inspect.SystemSettings.SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Authentication.GooglePublicSignupUrl)).Value)
            .IsEqualTo("\"https://accounts.example.test/signup\"");
    }

    [Test]
    public async Task MissingVisitorAuthorityFailsClosedWithoutMutations()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, "FullRegistrationAndAuth", grantVisitorAuthority: false);
        await rows.AssertRuntimeIsolationAsync();
        await using var context = await rows.Factory.CreateDbContextAsync();
        await rows.AssertGlobalAuthorityAsync(context);
        var writer = CreateWriter(context);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => writer.ApplyInternalAsync(
            [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AnonymousOnly\"")],
            rows.Authentication.UserId));
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
        await using var inspect = fixture.CreateDbContext();
        await Assert.That((await inspect.SystemSettings.SingleAsync(row => row.SettingKey == ModeKey)).Value)
            .IsEqualTo("\"FullRegistrationAndAuth\"");
        await Assert.That(inspect.ChangeTracker.Entries<TenantSetting>()).IsEmpty();
    }

    [Test]
    public async Task CurrentTransactionRetainsVisitorAuthoritySnapshotWithoutSwitchingLeases()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, "FullRegistrationAndAuth");
        await using var context = await rows.Factory.CreateDbContextAsync();
        await rows.AssertGlobalAuthorityAsync(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var writer = new VisitorAccessSettingsWriter(context, mutationLock, unitOfWork,
            new EventParticipationConfigurationRepository(context), new ConfigurationBuilder().Build());

        await mutationLock.ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys], async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var snapshot = await context.TenantSettingOverrides.FromSqlRaw(
                    $"SELECT * FROM {PostgresVisitorPolicyAuthorityContract.OverridesFunctionSql(context)}()")
                .IgnoreQueryFilters().AsNoTracking().SingleAsync(token);
            await Assert.That(snapshot.Value).IsEqualTo("\"FullRegistrationAndAuth\"");

            // A fixture-owned write establishes a newer committed state without changing this lease.
            // A reader that opens another context would validate that state instead of this snapshot.
            await using (var seed = fixture.CreateDbContext())
            {
                var foreignOverride = await seed.TenantSettingOverrides.SingleAsync(row =>
                    row.TenantId == rows.Authentication.A && row.SettingKey == ModeKey, token);
                foreignOverride.Value = "\"AnonymousOnly\"";
                await seed.SaveChangesAsync(token);
            }

            var result = await writer.ApplyInternalAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AnonymousOnly\"")],
                rows.Authentication.UserId, token);
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.DeferredNotifications.Length).IsEqualTo(1);
            await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
            await Assert.That(context.TenantFilterTenantId).IsNull();
            await transaction.RollbackAsync(token);
            return true;
        });

        await using var inspect = fixture.CreateDbContext();
        await Assert.That((await inspect.SystemSettings.SingleAsync(row => row.SettingKey == ModeKey)).Value)
            .IsEqualTo("\"FullRegistrationAndAuth\"");
        await Assert.That((await inspect.TenantSettingOverrides.SingleAsync(row =>
            row.TenantId == rows.Authentication.A && row.SettingKey == ModeKey)).Value).IsEqualTo("\"AnonymousOnly\"");
    }

    [Test]
    [Arguments("overrides")]
    [Arguments("participation")]
    public async Task RuntimeCannotExecuteGlobalVisitorAuthority(string purpose)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, "FullRegistrationAndAuth");
        await rows.AssertRuntimeIsolationAsync();
        await using var request = rows.Authentication.Context(rows.Authentication.B);
        await rows.Authentication.AssertOrdinaryRoleAsync(request);
        await using var command = request.Database.GetDbConnection().CreateCommand();
        string function = purpose == "overrides"
            ? PostgresVisitorPolicyAuthorityContract.OverridesFunctionSql(request)
            : PostgresVisitorPolicyAuthorityContract.ParticipationFunctionSql(request);
        command.CommandText = $"SELECT * FROM {function}()";
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteScalarAsync(); });
        await Assert.That(denied?.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task NativeVisitorReadsExposeOnlyCompiledAuthorityAndLiveAccountRequiredParticipation()
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, "FullRegistrationAndAuth");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.Authentication.A,
                Tenant = null!,
                SettingKey = GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
                Value = "\"Unrelated catalog\"",
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var context = await rows.Factory.CreateDbContextAsync();
        await rows.AssertGlobalAuthorityAsync(context);
        var overrides = await context.TenantSettingOverrides.FromSqlRaw(
                $"SELECT * FROM {PostgresVisitorPolicyAuthorityContract.OverridesFunctionSql(context)}()")
            .IgnoreQueryFilters().AsNoTracking().ToListAsync();
        await Assert.That(overrides.Select(row => row.SettingKey)).IsEquivalentTo([ModeKey]);
        await Assert.That(overrides.Select(row => row.TenantId)).IsEquivalentTo([rows.Authentication.A]);
        var participation = await context.Database.SqlQueryRaw<Guid>(
                $"SELECT * FROM {PostgresVisitorPolicyAuthorityContract.ParticipationFunctionSql(context)}()")
            .ToListAsync();
        await Assert.That(participation).IsEquivalentTo([rows.Authentication.A]);
        await using (var shape = context.Database.GetDbConnection().CreateCommand())
        {
            shape.CommandText = $"SELECT * FROM {PostgresVisitorPolicyAuthorityContract.ParticipationFunctionSql(context)}()";
            await using var reader = await shape.ExecuteReaderAsync();
            await Assert.That(reader.FieldCount).IsEqualTo(1);
            await Assert.That(reader.GetName(0)).IsEqualTo("Value");
        }
        await using var qualification = fixture.CreateDbContext();
        await Assert.That(await qualification.EventParticipationConfigurations
            .AnyAsync(row => row.TenantId == participation.Single()
                && row.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired && !row.IsDeleted)).IsTrue();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            SELECT rolcanlogin OR rolsuper OR rolbypassrls
            FROM pg_catalog.pg_roles WHERE rolname = '{PostgresVisitorPolicyAuthorityContract.OwnerRole}'
            """;
        await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
        command.CommandText = $"""
            SELECT count(*) FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_roles r ON r.oid = p.proowner
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE r.rolname = '{PostgresVisitorPolicyAuthorityContract.OwnerRole}'
              AND n.nspname = @schema AND p.pronargs = 0
              AND p.prosecdef AND 'search_path=pg_catalog' = ANY(p.proconfig)
            """;
        command.Parameters.Add(new NpgsqlParameter("schema", rows.Authentication.Schema));
        await Assert.That((long)(await command.ExecuteScalarAsync())!).IsEqualTo(2);
    }

    [Test]
    [Arguments("event")]
    [Arguments("participation")]
    public async Task DeletedParticipationDoesNotBlockInstanceVisitorMutation(string deleted)
    {
        await using var rows = await AuthorityRows.CreateAsync(fixture, "FullRegistrationAndAuth");
        await using (var seed = fixture.CreateDbContext())
        {
            if (deleted == "event")
                (await seed.Events.SingleAsync(row => row.TenantId == rows.Authentication.A)).IsDeleted = true;
            else
                (await seed.EventParticipationConfigurations.SingleAsync(row =>
                    row.TenantId == rows.Authentication.A)).IsDeleted = true;
            await seed.SaveChangesAsync();
        }
        await using var context = await rows.Factory.CreateDbContextAsync();
        await rows.AssertGlobalAuthorityAsync(context);
        var result = await CreateWriter(context).ApplyInternalAsync(
            [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AnonymousOnly\"", true)],
            rows.Authentication.UserId);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications.Length).IsEqualTo(1);
    }

    [Test]
    public async Task LocalLoginKeepsPrivateBirthDataUnreadable()
    {
        await PrepareColocatedAuthorityAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture);
        await using var inspect = OpenInstanceMigrator();
        string login = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter).Username!;
        var pii = inspect.Model.FindEntityType(typeof(UserPii))!;
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
            pii.GetTableName()!, pii.GetSchema());
        using var identifiers = new NpgsqlCommandBuilder();
        string relation = identifiers.QuoteIdentifier(pii.GetSchema()!) + "."
            + identifiers.QuoteIdentifier(pii.GetTableName()!);
        foreach (string property in new[] { "_dateOfBirth", "_birthDateCollectionReceiptId" })
        {
            bool readable = await inspect.Database.SqlQueryRaw<bool>(
                """SELECT pg_catalog.has_column_privilege({0}, {1}, {2}, 'SELECT') AS "Value" """,
                login, relation, pii.FindProperty(property)!.GetColumnName(table)!).SingleAsync();
            await Assert.That(readable).IsFalse();
        }
        bool revisionReadable = await inspect.Database.SqlQueryRaw<bool>(
            """SELECT pg_catalog.has_column_privilege({0}, {1}, {2}, 'SELECT') AS "Value" """,
            login, relation, pii.FindProperty("_birthDateRevision")!.GetColumnName(table)!).SingleAsync();
        await Assert.That(revisionReadable).IsTrue();
        foreach (Type type in new[] { typeof(ModerationRecognitionPending), typeof(ModerationRecognitionCaptureItem) })
        {
            var entity = inspect.Model.FindEntityType(type)!;
            string metadata = identifiers.QuoteIdentifier(entity.GetSchema()!) + "."
                + identifiers.QuoteIdentifier(entity.GetTableName()!);
            bool writable = await inspect.Database.SqlQueryRaw<bool>(
                """
                SELECT (pg_catalog.has_any_column_privilege({0}, {1}, 'INSERT,UPDATE')
                    OR pg_catalog.has_table_privilege({0}, {1}, 'DELETE')) AS "Value"
                """, login, metadata).SingleAsync();
            await Assert.That(writable).IsFalse();
        }
    }

    private sealed record NativeTenant(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;

    private async Task PrepareColocatedAuthorityAsync()
    {
        var options = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(options.Options);
        await using var seed = OpenInstanceMigrator();
        var first = authority.Model.GetEntityTypes().First();
        bool exists = await seed.Database.SqlQueryRaw<int>(
            """SELECT COUNT(*)::integer AS "Value" FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}""",
            first.GetSchema()!, first.GetTableName()!).SingleAsync() != 0;
        if (!exists)
            await seed.Database.ExecuteSqlRawAsync(authority.Database.GenerateCreateScript());

        // This topology executes the authority's C# repository through the declared Runtime
        // connection. Provision its fixed model rights before creating a real Local account.
        using var identifiers = new NpgsqlCommandBuilder();
        string runtime = identifiers.QuoteIdentifier(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username!);
        await seed.Database.OpenConnectionAsync();
        foreach (var table in authority.Model.GetRelationalModel().Tables)
        {
            string schema = identifiers.QuoteIdentifier(table.Schema!);
            string relation = schema + "." + identifiers.QuoteIdentifier(table.Name);
            await using var grant = seed.Database.GetDbConnection().CreateCommand();
            grant.CommandText = $"GRANT USAGE ON SCHEMA {schema} TO {runtime}; "
                + $"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE {relation} TO {runtime};";
            await grant.ExecuteNonQueryAsync();
        }
    }

    private ExploreDbContext OpenInstanceMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static VisitorAccessSettingsWriter CreateWriter(ExploreDbContext context)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        return new(context, new RelationalSettingMutationLock(context, unitOfWork), unitOfWork,
            new EventParticipationConfigurationRepository(context), new ConfigurationBuilder().Build());
    }

    private sealed class AuthorityRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Authentication { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Factory { get; private set; } = null!;
        private readonly string _role = $"visitor_global_{Guid.CreateVersion7():N}";
        private string _connectionString = null!;
        private readonly List<string> _tables = [];

        internal static async Task<AuthorityRows> CreateAsync(
            PostgreSqlContainerFixture fixture, string foreignMode, bool grantVisitorAuthority = true)
        {
            var rows = new AuthorityRows(fixture);
            rows.Authentication = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
            await using var seed = fixture.CreateDbContext();
            var source = await seed.Events.SingleAsync(row => row.TenantId == rows.Authentication.A);
            seed.EventParticipationConfigurations.Add(EventParticipationConfiguration.Create(
                source.Id, rows.Authentication.A, (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.AccountRequired,
                null, DateTime.UtcNow));
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.Authentication.A,
                Tenant = null!,
                SettingKey = ModeKey,
                Value = $"\"{foreignMode}\"",
                CreatedAt = DateTime.UtcNow
            });
            foreach (var (key, value) in new (string, string)[]
            {
                (ModeKey, "\"FullRegistrationAndAuth\""),
                (GovernanceSettingKeys.Authentication.GoogleSsoEnabled, "true"),
                (GovernanceSettingKeys.Authentication.GoogleClientId, "\"public-client\""),
                (GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, "\"Allowed\""),
                (GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, "\"https://accounts.example.test/signup\"")
            })
            {
                var definition = SettingRegistry.Get(key)!;
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null)
                {
                    setting = new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = key,
                        Value = value,
                        ValueType = definition.ValueType,
                        Category = definition.Category,
                        Description = definition.Description,
                        CreatedAt = DateTime.UtcNow
                    };
                    seed.SystemSettings.Add(setting);
                }
                setting.Value = value;
                setting.IsLocked = false;
            }
            await seed.SaveChangesAsync();
            foreach (Type type in new[] { typeof(SystemSetting), typeof(TenantSetting),
                typeof(EventParticipationConfiguration), typeof(Explore.Domain.Event) })
            {
                var entity = seed.Model.FindEntityType(type)!;
                rows._tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }

            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grants = administration.CreateCommand();
            string visitorGrant = grantVisitorAuthority
                ? $"GRANT {PostgresVisitorPolicyAuthorityContract.GlobalRole} TO \"{rows._role}\";"
                : string.Empty;
            grants.CommandText = $"""
                CREATE ROLE "{rows._role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT USAGE ON SCHEMA "{rows.Authentication.Schema}" TO "{rows._role}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON {rows._tables[0]} TO "{rows._role}";
                GRANT SELECT ON {string.Join(", ", rows._tables.Skip(1))} TO "{rows.Authentication.RuntimeOptions.Username}";
                {visitorGrant}
                """;
            await grants.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = rows._role,
                Password = password
            };
            rows._connectionString = connection.ConnectionString;
            rows.Factory = new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = rows._role,
                Password = password,
                Schema = rows.Authentication.Schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            }, poolSize: 1);
            return rows;
        }

        internal async Task AssertRuntimeIsolationAsync()
        {
            await using var request = Authentication.Context(Authentication.B);
            await Authentication.AssertOrdinaryRoleAsync(request);
            await Assert.That(await request.TenantSettingOverrides.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == Authentication.A)).IsFalse();
            await Assert.That(await request.EventParticipationConfigurations.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == Authentication.A)).IsFalse();
            await Assert.That(request.TenantFilterTenantId).IsEqualTo(Authentication.B);
        }

        internal async Task AssertGlobalAuthorityAsync(ExploreDbContext context)
        {
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_user, rolsuper, rolbypassrls FROM pg_catalog.pg_roles WHERE rolname = current_user";
            await using (var reader = await command.ExecuteReaderAsync())
            {
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetString(0)).IsEqualTo(_role);
                await Assert.That(reader.GetBoolean(1)).IsFalse();
                await Assert.That(reader.GetBoolean(2)).IsFalse();
            }
            foreach (string table in _tables.Skip(1))
            {
                command.CommandText = $"SELECT 1 FROM {table} LIMIT 1";
                var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteScalarAsync(); });
                await Assert.That(denied?.SqlState).IsEqualTo("42501");
            }
            await Assert.That(context.TenantFilterTenantId).IsNull();
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _tables)} FROM "{_role}";
                REVOKE USAGE ON SCHEMA "{Authentication.Schema}" FROM "{_role}";
                REVOKE SELECT ON {string.Join(", ", _tables.Skip(1))} FROM "{Authentication.RuntimeOptions.Username}";
                REVOKE {PostgresVisitorPolicyAuthorityContract.GlobalRole} FROM "{_role}";
                DROP ROLE "{_role}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await Authentication.DisposeAsync();
        }
    }
}
