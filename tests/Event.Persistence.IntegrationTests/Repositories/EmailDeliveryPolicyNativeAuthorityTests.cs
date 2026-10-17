using System.Data;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EmailDeliveryPolicyNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TenantMutationSnapshotDoesNotRequireCrossTenantPolicyExecution()
    {
        await fixture.ResetAsync();
        Guid selected = Guid.CreateVersion7();
        Guid foreign = Guid.CreateVersion7();
        await using var seed = fixture.CreateDbContext();
        foreach (Guid id in new[] { selected, foreign })
            seed.Tenants.Add(new Tenant
            {
                Id = id,
                FullName = "Policy snapshot",
                Slug = $"snapshot-{id:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = ControlCutoff
            });
        seed.TenantSettingOverrides.AddRange(
            Override(selected, GovernanceSettingKeys.Email.FromName, "\"Selected sender\""),
            Override(foreign, GovernanceSettingKeys.Email.FromName, "\"Foreign sender\""));
        await seed.SaveChangesAsync();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        var factories = new EmailPolicyMutationDbContextFactories(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyTenantMutation));
        await using var context = await factories.CreateAsync(
            new Explore.Application.Contracts.Operations.PolicyMutationScope(selected)
            {
                Purpose = Explore.Application.Contracts.Operations.PolicyMutationPurpose.EmailDelivery
            }, CancellationToken.None);
        var snapshot = await EmailDeliveryPolicyReader.ReadTenantSettingsAsync(
            context, context.BindTenantLease(), CancellationToken.None);
        await Assert.That(snapshot.Keys).IsEquivalentTo([selected]);
        await Assert.That(snapshot[selected][GovernanceSettingKeys.Email.FromName].Value).IsEqualTo("\"Selected sender\"");
        await RequireDenialAsync(context,
            $"SELECT * FROM {PostgresEmailDeliveryPolicyContract.FunctionSql(context)}()");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProcessorMutationCannotCreateAnotherIdentityOrPausedOperatorState(bool paused)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        var factories = new EmailPolicyMutationDbContextFactories(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation),
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyTenantMutation));
        await using var context = await factories.CreateAsync(
            Explore.Application.Contracts.Operations.PolicyMutationScope.EmailInstance, CancellationToken.None);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({RelationalSettingMutationLock.ComputeStableLockKey(GovernanceSettingKeys.Email.DeliveryEnabled)})");
            var setting = await context.SystemSettings.SingleOrDefaultAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            if (setting is null)
                context.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    CreatedAt = DateTime.UtcNow
                });
            else
                setting.Value = setting.Value == "true" ? "false" : "true";
            await context.SaveChangesAsync();
            await PostgresEmailDeliveryPolicyContract.RequireMutationAsync(context, null, CancellationToken.None);
            context.EmailDispatchProcessorStates.Add(new EmailDispatchProcessorState
            {
                Id = Guid.CreateVersion7(),
                ProcessorCode = paused ? EmailDispatchOutboxRepository.SmtpProcessorCode : "foreign",
                IsPaused = paused,
                DeliveryPolicyRevision = 1,
                UpdatedAt = DateTime.UtcNow
            });
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
            await Assert.That((rejected!.InnerException as PostgresException)?.SqlState)
                .IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
        });
        await Assert.That(await seed.EmailDispatchProcessorStates.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("function")]
    [Arguments("membership")]
    [Arguments("column")]
    [Arguments("schema")]
    public async Task NativeReadAndReinstallRejectUnexpectedAuthorityWithoutHidingIt(string drift)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var enrolled = await CreateAuthorityAsync(seed, global: true);
        await using var outsider = await CreateAuthorityAsync(seed, global: false, capability: false);
        await using var reader = await new GlobalExploreDbContextFactory(enrolled.Options, poolSize: 1).CreateDbContextAsync();
        string function = PostgresEmailDeliveryPolicyContract.FunctionSql(seed) + "()";
        var sql = seed.GetService<ISqlGenerationHelper>();
        string owner = sql.DelimitIdentifier(PostgresEmailDeliveryPolicyContract.OwnerRole);
        string schema = sql.DelimitIdentifier(enrolled.Options.Schema);
        string outsiderRole = sql.DelimitIdentifier(outsider.Options.Username!);
        var tenant = seed.Model.FindEntityType(typeof(Tenant))!;
        string fullName = sql.DelimitIdentifier(tenant.FindProperty(nameof(Tenant.FullName))!.GetColumnName(
            Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tenant.GetTableName()!, tenant.GetSchema()))!);
        (string Inject, string Restore) statements = drift switch
        {
            "function" => ($"GRANT EXECUTE ON FUNCTION {function} TO {outsiderRole}",
                $"REVOKE EXECUTE ON FUNCTION {function} FROM {outsiderRole}"),
            "membership" => ($"GRANT {PostgresEmailDeliveryPolicyContract.GlobalRole} TO {outsiderRole} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {PostgresEmailDeliveryPolicyContract.GlobalRole} FROM {outsiderRole}"),
            "column" => ($"GRANT SELECT ({fullName}) ON {Table<Tenant>(seed)} TO {owner}",
                $"REVOKE SELECT ({fullName}) ON {Table<Tenant>(seed)} FROM {owner}"),
            "schema" => ($"GRANT CREATE ON SCHEMA {schema} TO {owner}",
                $"REVOKE CREATE ON SCHEMA {schema} FROM {owner}"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await seed.Database.ExecuteSqlRawAsync(statements.Inject);
        try
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
                await reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {function}"));
            await Assert.That(rejected!.SqlState).IsEqualTo("55000");
            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
            var stillRejected = await Assert.ThrowsAsync<PostgresException>(async () =>
                await reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {function}"));
            await Assert.That(stillRejected!.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await seed.Database.ExecuteSqlRawAsync(statements.Restore);
        }
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {function}");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ReadEntitlementCannotReadOrAuthorAnotherTenantsControls(bool global, bool read)
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        DateTime cutoff = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);
        await using var seed = fixture.CreateDbContext();
        foreach (Guid tenant in new[] { tenantA, tenantB })
            seed.Tenants.Add(new Tenant
            {
                Id = tenant,
                FullName = "Control custody",
                Slug = $"custody-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = cutoff
            });
        seed.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantB,
            Tenant = null!,
            DeliveryPolicyRevision = 5,
            OptionalSuppressedThroughRevision = 3,
            OptionalSuppressedThroughUtc = cutoff,
            CreatedAt = cutoff,
            UpdatedAt = cutoff
        });
        await seed.SaveChangesAsync();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global);
        await using var context = global
            ? await new GlobalExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync()
            : await new TenantExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync(tenantA);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                if (read)
                    await PostgresEmailDeliveryPolicyContract.ReadControlsAsync(context, [tenantB], CancellationToken.None);
                else
                    await PostgresEmailDeliveryPolicyContract.ApplyControlsAsync(context,
                        [new(tenantB, 5, 5, 6, null, null)], cutoff.AddDays(1), null, CancellationToken.None);
            });
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        });
        var retained = await seed.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().SingleAsync(row => row.TenantId == tenantB);
        await Assert.That(retained.DeliveryPolicyRevision).IsEqualTo(5L);
        await Assert.That(retained.OptionalSuppressedThroughRevision).IsEqualTo((long?)3L);
        await Assert.That(retained.OptionalSuppressedThroughUtc).IsEqualTo((DateTime?)cutoff);
    }

    [Test]
    public async Task NativeTenantDeletionCascadeDoesNotRequireEmailMutationPurpose()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        await using var seed = fixture.CreateDbContext();
        seed.Tenants.Add(new Tenant
        {
            Id = tenantId,
            FullName = "Cascade custody",
            Slug = $"cascade-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = ControlCutoff
        });
        seed.TenantSettingOverrides.Add(Override(tenantId, GovernanceSettingKeys.Email.FromName, "\"Sender\""));
        await seed.SaveChangesAsync();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global: false, tenantDeletion: true);
        await using var context = await new TenantExploreDbContextFactory(authority.Options, poolSize: 1)
            .CreateDbContextAsync(tenantId);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await context.Tenants.Where(row => row.Id == tenantId).ExecuteDeleteAsync()).IsEqualTo(1);
            await Assert.That(await context.TenantSettingOverrides
                .Where(row => row.TenantId == tenantId).CountAsync()).IsEqualTo(0);
            await transaction.RollbackAsync();
        });
        await Assert.That(await seed.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().Where(row => row.TenantId == tenantId).CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantMutationRetainsHistoryAndOnlyChangesItsOwnControl(bool rollback)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var (tenantA, tenantB) = await SeedControlCustodyAsync(seed);
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global: false,
            mutationPurpose: PostgresEmailDeliveryPolicyContract.TenantMutationRole);
        await using var context = await new TenantExploreDbContextFactory(authority.Options, poolSize: 1)
            .CreateDbContextAsync(tenantA);
        await using var policyLock = await RelationalNamedLock.AcquireSessionAsync(context,
            $"explore:setting-mutation:{GovernanceSettingKeys.Email.DeliveryEnabled}", CancellationToken.None);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var before = await EmailDeliveryPolicyReader.ReadAsync(context, tenantA, CancellationToken.None);
            await Assert.That(before.State).IsEqualTo(EmailDeliveryState.Available);
            await context.TenantSettingOverrides
                .Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.FromName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"Changed sender\""));
            await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantA, before, null, CancellationToken.None);
            await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantA, before, null, CancellationToken.None);
            var controls = await PostgresEmailDeliveryPolicyContract.ReadControlsAsync(context, [tenantA], CancellationToken.None);
            await Assert.That(controls[tenantA].DeliveryPolicyRevision).IsEqualTo(6L);
            await Assert.That(controls[tenantA].OptionalSuppressedThroughRevision).IsEqualTo((long?)3);
            await Assert.That(controls[tenantA].OptionalSuppressedThroughUtc).IsEqualTo((DateTime?)ControlCutoff);
            if (rollback)
                await transaction.RollbackAsync();
            else
                await transaction.CommitAsync();
        });
        var retained = await seed.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().Where(row => row.TenantId == tenantA || row.TenantId == tenantB)
            .ToDictionaryAsync(row => row.TenantId);
        await Assert.That(retained[tenantA].DeliveryPolicyRevision).IsEqualTo(rollback ? 5L : 6L);
        await Assert.That(retained[tenantB].DeliveryPolicyRevision).IsEqualTo(5L);
        await Assert.That(retained.Values.All(row => row.OptionalSuppressedThroughRevision == 3
            && row.OptionalSuppressedThroughUtc == ControlCutoff && row.IsPaused && row.SmtpAvailableTokens == 7)).IsTrue();

        // A committed or rolled-back receipt never authorizes the next transaction.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresEmailDeliveryPolicyContract.RequireMutationAsync(context, tenantA, CancellationToken.None));
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    [Arguments("NoSource", PostgresErrorCodes.InsufficientPrivilege)]
    [Arguments("Tenant", PostgresErrorCodes.InsufficientPrivilege)]
    [Arguments("Baseline", PostgresErrorCodes.InsufficientPrivilege)]
    [Arguments("Revision", PostgresErrorCodes.InvalidParameterValue)]
    [Arguments("Expected", PostgresErrorCodes.SerializationFailure)]
    [Arguments("Suppression", PostgresErrorCodes.InsufficientPrivilege)]
    [Arguments("ReplaySuppression", PostgresErrorCodes.InsufficientPrivilege)]
    [Arguments("ReadTenant", PostgresErrorCodes.InsufficientPrivilege)]
    [Arguments("Instance", PostgresErrorCodes.InsufficientPrivilege)]
    public async Task MutationPurposeCannotForgeSourceBaselineRevisionOrSuppression(string forgery, string sqlState)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var (tenantA, tenantB) = await SeedControlCustodyAsync(seed);
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global: false,
            mutationPurpose: PostgresEmailDeliveryPolicyContract.TenantMutationRole);
        await using var context = await new TenantExploreDbContextFactory(authority.Options, poolSize: 1)
            .CreateDbContextAsync(tenantA);
        await using var policyLock = await RelationalNamedLock.AcquireSessionAsync(context,
            $"explore:setting-mutation:{GovernanceSettingKeys.Email.DeliveryEnabled}", CancellationToken.None);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            if (forgery == "ReplaySuppression")
            {
                var before = await EmailDeliveryPolicyReader.ReadAsync(context, tenantA, CancellationToken.None);
                await context.TenantSettingOverrides
                    .Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "false"));
                await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantA, before, null, CancellationToken.None);
                // A no-op DML statement cannot authorize undoing this transition.
                await context.TenantSettingOverrides
                    .Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "false"));
            }
            else if (forgery != "NoSource")
                await context.TenantSettingOverrides
                    .Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.FromName)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"Changed sender\""));
            PostgresEmailDeliveryPolicyContract.ControlChange proposed = forgery switch
            {
                "Tenant" => new(tenantB, 5, 5, 6, 3, ControlCutoff),
                "Baseline" => new(tenantA, 4, 5, 5, 3, ControlCutoff),
                "Revision" => new(tenantA, 5, 5, 7, 3, ControlCutoff),
                "Expected" or "ReplaySuppression" => new(tenantA, 5, 6, 6, 3, ControlCutoff),
                "Suppression" => new(tenantA, 5, 5, 6, null, null),
                _ => new(tenantA, 5, 5, 6, 3, ControlCutoff)
            };
            // The expected database exception is caught inside the strategy,
            // so its serialization failure is evidence, not a retry signal.
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                if (forgery == "Instance")
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.FromName)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"Forged instance\""));
                else if (forgery == "ReadTenant")
                    await PostgresEmailDeliveryPolicyContract.ReadControlsAsync(context, [tenantB], CancellationToken.None);
                else
                    await PostgresEmailDeliveryPolicyContract.ApplyControlsAsync(
                        context, [proposed], DateTime.UtcNow, null, CancellationToken.None);
            });
            await Assert.That(denied!.SqlState).IsEqualTo(sqlState);
            await transaction.RollbackAsync();
        });
        var controls = await seed.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().Where(row => row.TenantId == tenantA || row.TenantId == tenantB).ToArrayAsync();
        await Assert.That(controls.Length).IsEqualTo(2);
        await Assert.That(controls.All(row => row.DeliveryPolicyRevision == 5
            && row.OptionalSuppressedThroughRevision == 3 && row.OptionalSuppressedThroughUtc == ControlCutoff)).IsTrue();
        await Assert.That(await seed.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.FromName)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"Original sender\"");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedNativeSettingCanRestoreOnlyItsTransactionsOriginalHistory(bool rollback)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var (tenantA, tenantB) = await SeedControlCustodyAsync(seed);
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global: false,
            mutationPurpose: PostgresEmailDeliveryPolicyContract.TenantMutationRole);
        await using var context = await new TenantExploreDbContextFactory(authority.Options, poolSize: 1)
            .CreateDbContextAsync(tenantA);
        await using var policyLock = await RelationalNamedLock.AcquireSessionAsync(context,
            $"explore:setting-mutation:{GovernanceSettingKeys.Email.DeliveryEnabled}", CancellationToken.None);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var before = await EmailDeliveryPolicyReader.ReadAsync(context, tenantA, CancellationToken.None);
            await context.TenantSettingOverrides
                .Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "false"));
            await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantA, before, null, CancellationToken.None);
            var disabled = await PostgresEmailDeliveryPolicyContract.ReadControlsAsync(context, [tenantA], CancellationToken.None);
            await Assert.That(disabled[tenantA].OptionalSuppressedThroughRevision).IsEqualTo((long?)6);
            await context.TenantSettingOverrides
                .Where(row => row.TenantId == tenantA && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "true"));
            await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantA, before, null, CancellationToken.None);
            var restored = await PostgresEmailDeliveryPolicyContract.ReadControlsAsync(context, [tenantA], CancellationToken.None);
            await Assert.That(restored[tenantA].DeliveryPolicyRevision).IsEqualTo(6L);
            await Assert.That(restored[tenantA].OptionalSuppressedThroughRevision).IsEqualTo((long?)3);
            await Assert.That(restored[tenantA].OptionalSuppressedThroughUtc).IsEqualTo((DateTime?)ControlCutoff);
            if (rollback)
                await transaction.RollbackAsync();
            else
                await transaction.CommitAsync();
        });
        var controls = await seed.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().Where(row => row.TenantId == tenantA || row.TenantId == tenantB).ToArrayAsync();
        await Assert.That(controls.Length).IsEqualTo(2);
        await Assert.That(controls.Single(row => row.TenantId == tenantA).DeliveryPolicyRevision).IsEqualTo(rollback ? 5L : 6L);
        await Assert.That(controls.Single(row => row.TenantId == tenantB).DeliveryPolicyRevision).IsEqualTo(5L);
        await Assert.That(controls.All(row => row.OptionalSuppressedThroughRevision == 3
            && row.OptionalSuppressedThroughUtc == ControlCutoff)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwningTransactionSeesForeignOverridesLocksRevisionAndRollback(bool global)
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        await using var seed = fixture.CreateDbContext();
        foreach (Guid id in new[] { tenantA, tenantB })
        {
            seed.Tenants.Add(new Tenant
            {
                Id = id,
                FullName = "Policy authority",
                Slug = $"policy-{id:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            });
            seed.TenantSettingOverrides.AddRange(
                Override(id, GovernanceSettingKeys.Email.DeliveryEnabled, "true"),
                Override(id, GovernanceSettingKeys.Email.SmtpHost, "\"smtp.tenant.test\""),
                Override(id, GovernanceSettingKeys.Email.FromAddress, "\"events@tenant.test\""));
        }
        foreach (string key in EmailDeliverySettingKeys.All)
        {
            SettingDefinition definition = SettingRegistry.Get(key)!;
            string value = key switch
            {
                GovernanceSettingKeys.Email.DeliveryEnabled => "true",
                GovernanceSettingKeys.Email.SmtpHost => "\"smtp.instance.test\"",
                GovernanceSettingKeys.Email.FromAddress => "\"events@instance.test\"",
                GovernanceSettingKeys.TenantDelegation.LockSmtp => "false",
                _ => definition.DefaultValue
            };
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
            {
                setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    ValueType = definition.ValueType,
                    Value = value,
                    CreatedAt = DateTime.UtcNow
                };
                seed.SystemSettings.Add(setting);
            }
            setting.Value = value;
            setting.IsLocked = false;
        }
        seed.EmailDispatchProcessorStates.Add(new EmailDispatchProcessorState
        {
            Id = Guid.CreateVersion7(),
            ProcessorCode = EmailDispatchOutboxRepository.SmtpProcessorCode,
            DeliveryPolicyRevision = 11,
            UpdatedAt = DateTime.UtcNow
        });
        DateTime originalCutoff = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);
        seed.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantA,
            Tenant = null!,
            DeliveryPolicyRevision = 5,
            OptionalSuppressedThroughRevision = 3,
            OptionalSuppressedThroughUtc = originalCutoff,
            IsPaused = true,
            PauseReason = "Operator pause",
            PausedAt = originalCutoff,
            SmtpAvailableTokens = 7,
            SmtpRefillAt = originalCutoff,
            CreatedAt = originalCutoff,
            UpdatedAt = originalCutoff
        });
        await seed.SaveChangesAsync();
        seed.ChangeTracker.Clear();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);

        await using var authority = await CreateAuthorityAsync(seed, global,
            mutationPurpose: PostgresEmailDeliveryPolicyContract.InstanceMutationRole);
        await using var context = global
            ? await new GlobalExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync()
            : await new TenantExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync(tenantA);
        await context.Database.OpenConnectionAsync();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                    AND NOT rolcreaterole AND NOT rolreplication
                FROM pg_catalog.pg_roles WHERE rolname = current_user
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
            command.CommandText = $"""
                SELECT relrowsecurity AND relforcerowsecurity
                FROM pg_catalog.pg_class WHERE oid = '{Table<TenantSetting>(seed)}'::regclass
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        }
        // Positive ordinary SELECT control: EF bypass cannot expose the foreign tenant.
        await Assert.That(await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .CountAsync()).IsEqualTo(global ? 0 : 3);
        await Assert.That(await context.Tenants.CountAsync()).IsEqualTo(2);

        await using var policyLock = await RelationalNamedLock.AcquireSessionAsync(context,
            $"explore:setting-mutation:{GovernanceSettingKeys.Email.DeliveryEnabled}", CancellationToken.None);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var before = await EmailDeliveryPolicyReader.ReadAllAsync(context, CancellationToken.None);
            await Assert.That(before.Tenants.Count).IsEqualTo(2);
            // Intended Red: the foreign override is hidden by RLS, not missing from seed.
            await Assert.That(before.Tenants[tenantB].TransportTenantId).IsEqualTo((Guid?)tenantB);
            await Assert.That(before.Tenants[tenantB].Enabled).IsTrue();

            await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.FromName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"Uncommitted sender\""));
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(context, before, null, CancellationToken.None);
            await Assert.That(await RevisionAsync(context)).IsEqualTo(12L);
            await Assert.That(await RevisionAsync(seed)).IsEqualTo(11L);

            await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Value, "false").SetProperty(row => row.IsLocked, true));
            var locked = await EmailDeliveryPolicyReader.ReadAllAsync(context, CancellationToken.None);
            await Assert.That(locked.Instance.Enabled).IsFalse();
            await Assert.That(locked.Tenants[tenantB].Enabled).IsFalse();
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(context, before, null, CancellationToken.None);
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(context, before, null, CancellationToken.None);
            await Assert.That(await RevisionAsync(context)).IsEqualTo(12L);
            await using (var controls = context.Database.GetDbConnection().CreateCommand())
            {
                string schema = seed.Model.FindEntityType(typeof(EmailDispatchTenantControl))!.GetSchema()!;
                string function = seed.GetService<ISqlGenerationHelper>().DelimitIdentifier(
                    "email_delivery_policy_controls", schema);
                controls.Transaction = transaction.GetDbTransaction();
                controls.CommandText = $"SELECT * FROM {function}(@tenants)";
                controls.Parameters.Add(new NpgsqlParameter("tenants", new[] { tenantA, tenantB }));
                await using var reader = await controls.ExecuteReaderAsync();
                var observed = new Dictionary<Guid, (long Revision, long? Suppressed, DateTime? Cutoff)>();
                while (await reader.ReadAsync())
                    observed.Add(reader.GetGuid(0), (reader.GetInt64(1),
                        reader.IsDBNull(2) ? null : reader.GetInt64(2),
                        reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
                await Assert.That(observed.Count).IsEqualTo(2);
                await Assert.That(observed[tenantA].Revision).IsEqualTo(6L);
                await Assert.That(observed[tenantA].Suppressed).IsEqualTo((long?)6L);
                await Assert.That(observed[tenantB].Revision).IsEqualTo(1L);
                await Assert.That(observed[tenantB].Suppressed).IsEqualTo((long?)1L);
                await Assert.That(observed.Values.All(row => row.Cutoff > originalCutoff)).IsTrue();
            }
            // A separate pool cannot substitute for reads of this uncommitted lock.
            await Assert.That(await seed.SystemSettings.AsNoTracking()
                .Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                .Select(row => row.Value).SingleAsync()).IsEqualTo("true");
            await transaction.RollbackAsync();
        });
        await Assert.That(await RevisionAsync(seed)).IsEqualTo(11L);
        var restoredControls = await seed.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .AsNoTracking().Where(row => row.TenantId == tenantA || row.TenantId == tenantB).ToArrayAsync();
        await Assert.That(restoredControls.Length).IsEqualTo(1);
        var restoredControl = restoredControls.Single();
        await Assert.That(restoredControl.TenantId).IsEqualTo(tenantA);
        await Assert.That(restoredControl.DeliveryPolicyRevision).IsEqualTo(5L);
        await Assert.That(restoredControl.OptionalSuppressedThroughRevision).IsEqualTo((long?)3L);
        await Assert.That(restoredControl.OptionalSuppressedThroughUtc).IsEqualTo((DateTime?)originalCutoff);
        await Assert.That(restoredControl.IsPaused).IsTrue();
        await Assert.That(restoredControl.PauseReason).IsEqualTo("Operator pause");
        await Assert.That(restoredControl.SmtpAvailableTokens).IsEqualTo((int?)7);
        await Assert.That(restoredControl.SmtpRefillAt).IsEqualTo((DateTime?)originalCutoff);
        var enabled = await seed.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
        await Assert.That(enabled.Value).IsEqualTo("true");
        await Assert.That(enabled.IsLocked).IsFalse();
        await Assert.That(await seed.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.Email.FromName)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(SettingRegistry.Get(GovernanceSettingKeys.Email.FromName)!.DefaultValue);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(global ? (Guid?)null : tenantA);
        await Assert.That(await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .CountAsync()).IsEqualTo(global ? 0 : 3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FixedKeysPreserveEntityMetadataAndEmptyTenantMembershipWithoutTableAuthority(bool global)
    {
        await fixture.ResetAsync();
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid emptyTenant = Guid.CreateVersion7();
        DateTime created = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);
        await using var seed = fixture.CreateDbContext();
        foreach (Guid id in new[] { tenantA, tenantB, emptyTenant })
            seed.Tenants.Add(new Tenant
            {
                Id = id,
                FullName = "Fixed-key policy",
                Slug = $"policy-bounds-{id:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = created
            });
        var expected = EmailDeliverySettingKeys.All.Select(key => Override(tenantB, key, "false")).ToArray();
        foreach (var row in expected)
        {
            row.IsLocked = true;
            row.CreatedAt = created;
            row.UpdatedAt = created.AddDays(1);
        }
        seed.TenantSettingOverrides.AddRange(expected);
        seed.TenantSettingOverrides.Add(Override(tenantB, "events.max_sessions", "71"));
        await seed.SaveChangesAsync();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global);
        await using var context = global
            ? await new GlobalExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync()
            : await new TenantExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync(tenantA);
        await context.Database.OpenConnectionAsync();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"""
                SELECT pg_has_role(current_user, '{PostgresEmailDeliveryPolicyContract.OwnerRole}', 'MEMBER')
                    OR pg_has_role(current_user, '{PostgresEmailDeliveryPolicyContract.MigratorRole}', 'MEMBER')
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
            command.CommandText = $"""
                SELECT has_table_privilege('{PostgresEmailDeliveryPolicyContract.OwnerRole}', '{Table<TenantSetting>(seed)}', 'SELECT')
                    OR has_any_column_privilege('{PostgresEmailDeliveryPolicyContract.OwnerRole}', '{Table<TenantSetting>(seed)}', 'UPDATE')
                    OR has_table_privilege('{PostgresEmailDeliveryPolicyContract.OwnerRole}', '{Table<TenantSetting>(seed)}', 'INSERT,DELETE')
                    OR has_any_column_privilege('{(global ? PostgresEmailDeliveryPolicyContract.GlobalRole : PostgresEmailDeliveryPolicyContract.RuntimeRole)}', '{Table<TenantSetting>(seed)}', 'SELECT')
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
            command.CommandText = $"""
                SELECT pronargs = 0 AND prosecdef AND proconfig = ARRAY['search_path=pg_catalog']
                    AND pg_catalog.pg_get_userbyid(proowner) = '{PostgresEmailDeliveryPolicyContract.OwnerRole}'
                FROM pg_catalog.pg_proc WHERE oid = '{PostgresEmailDeliveryPolicyContract.FunctionSql(context)}()'::regprocedure
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
        }
        await RequireDenialAsync(context, $"SET ROLE {PostgresEmailDeliveryPolicyContract.OwnerRole}");
        await RequireDenialAsync(context, $"SET ROLE {PostgresEmailDeliveryPolicyContract.MigratorRole}");
        await Assert.That(async () => await PostgresEmailDeliveryPolicyContract.ReadAsync(context, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var settings = await EmailDeliveryPolicyReader.ReadTenantSettingsAsync(context, null, CancellationToken.None);
            await Assert.That(settings.Keys).IsEquivalentTo(new[] { tenantA, tenantB, emptyTenant });
            await Assert.That(settings[tenantA].Count).IsEqualTo(0);
            await Assert.That(settings[emptyTenant].Count).IsEqualTo(0);
            await Assert.That(settings[tenantB].Keys).IsEquivalentTo(EmailDeliverySettingKeys.All);
            foreach (var row in expected)
            {
                var actual = settings[tenantB][row.SettingKey];
                await Assert.That(actual.Id).IsEqualTo(row.Id);
                await Assert.That(actual.Value).IsEqualTo(row.Value);
                await Assert.That(actual.IsLocked).IsTrue();
                await Assert.That(actual.CreatedAt).IsEqualTo(created);
                await Assert.That(actual.UpdatedAt).IsEqualTo(created.AddDays(1));
            }
            await Assert.That(context.ChangeTracker.Entries<TenantSetting>().Count()).IsEqualTo(0);
            await transaction.RollbackAsync();
        });
        await Assert.That(await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnentitledLoginCannotReadReconciliationOverrides(bool global)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresEmailDeliveryPolicyContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        await using var authority = await CreateAuthorityAsync(seed, global, capability: false);
        await using var context = global
            ? await new GlobalExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync()
            : await new TenantExploreDbContextFactory(authority.Options, poolSize: 1).CreateDbContextAsync(Guid.CreateVersion7());
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await EmailDeliveryPolicyReader.ReadAllAsync(context, CancellationToken.None));
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        });
    }

    private static readonly DateTime ControlCutoff = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<(Guid TenantA, Guid TenantB)> SeedControlCustodyAsync(ExploreDbContext context)
    {
        Guid tenantA = Guid.CreateVersion7(), tenantB = Guid.CreateVersion7();
        foreach (Guid id in new[] { tenantA, tenantB })
        {
            context.Tenants.Add(new Tenant
            {
                Id = id,
                FullName = "Control custody",
                Slug = $"control-{id:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = ControlCutoff
            });
            context.TenantSettingOverrides.Add(Override(id, GovernanceSettingKeys.Email.FromName, "\"Original sender\""));
            context.TenantSettingOverrides.Add(Override(id, GovernanceSettingKeys.Email.DeliveryEnabled, "true"));
            context.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
            {
                Id = Guid.CreateVersion7(),
                TenantId = id,
                Tenant = null!,
                DeliveryPolicyRevision = 5,
                OptionalSuppressedThroughRevision = 3,
                OptionalSuppressedThroughUtc = ControlCutoff,
                CreatedAt = ControlCutoff,
                UpdatedAt = ControlCutoff,
                IsPaused = true,
                PauseReason = "Operator pause",
                SmtpAvailableTokens = 7,
                SmtpRefillAt = ControlCutoff
            });
        }
        foreach (string key in EmailDeliverySettingKeys.All)
        {
            var definition = SettingRegistry.Get(key)!;
            var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
            {
                setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    ValueType = definition.ValueType,
                    Value = definition.DefaultValue,
                    CreatedAt = ControlCutoff
                };
                context.SystemSettings.Add(setting);
            }
            setting.IsLocked = false;
            setting.Value = key switch
            {
                GovernanceSettingKeys.Email.DeliveryEnabled => "true",
                GovernanceSettingKeys.Email.SmtpHost => "\"smtp.instance.test\"",
                GovernanceSettingKeys.Email.FromAddress => "\"events@instance.test\"",
                GovernanceSettingKeys.TenantDelegation.LockSmtp => "false",
                _ => definition.DefaultValue
            };
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (tenantA, tenantB);
    }

    private static async Task RequireDenialAsync(ExploreDbContext context, string statement)
    {
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(statement));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static Task<long> RevisionAsync(ExploreDbContext context) =>
        context.EmailDispatchProcessorStates.AsNoTracking()
            .Where(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
            .Select(row => row.DeliveryPolicyRevision).SingleAsync();

    private static TenantSetting Override(Guid tenantId, string key, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        SettingKey = key,
        Value = value,
        CreatedAt = DateTime.UtcNow
    };

    private async Task<Authority> CreateAuthorityAsync(
        ExploreDbContext model, bool global, bool capability = true, string? mutationPurpose = null,
        bool tenantDeletion = false)
    {
        string schema = model.Model.FindEntityType(typeof(TenantSetting))!.GetSchema()!;
        PrimaryDatabaseRole purpose = mutationPurpose switch
        {
            PostgresEmailDeliveryPolicyContract.InstanceMutationRole => PrimaryDatabaseRole.EmailPolicyInstanceMutation,
            PostgresEmailDeliveryPolicyContract.TenantMutationRole => PrimaryDatabaseRole.EmailPolicyTenantMutation,
            null => global ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime,
            _ => throw new ArgumentOutOfRangeException(nameof(mutationPurpose))
        };
        bool ownsLogin = !capability && mutationPurpose is null;
        var declaredOptions = fixture.GetNativeQueueOptions(purpose, schema);
        string role = ownsLogin ? $"policy_unentitled_{Guid.CreateVersion7():N}" : declaredOptions.Username!;
        string password = ownsLogin ? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) : declaredOptions.Password!;
        string[] tables = [Table<Tenant>(model), Table<TenantSetting>(model), Table<SystemSetting>(model),
            Table<EmailDispatchProcessorState>(model), Table<EmailDispatchTenantControl>(model)];
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        if (ownsLogin)
        {
            command.CommandText = $"CREATE ROLE \"{role}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{password}'";
            await command.ExecuteNonQueryAsync();
        }
        command.CommandText = $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT SELECT ON {string.Join(", ", tables)} TO "{role}";
            GRANT UPDATE ON {Table<SystemSetting>(model)}, {Table<EmailDispatchProcessorState>(model)} TO "{role}";
            """;
        await command.ExecuteNonQueryAsync();
        if (mutationPurpose is not null)
        {
            command.CommandText = $"GRANT UPDATE ON {Table<TenantSetting>(model)} TO \"{role}\"";
            await command.ExecuteNonQueryAsync();
        }
        if (tenantDeletion)
        {
            command.CommandText = $"GRANT DELETE ON {Table<Tenant>(model)} TO \"{role}\"";
            await command.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        return new(fixture.ConnectionString, schema, role, tables, ownsLogin, new PrimaryDatabaseConnectionOptions
        {
            Role = global ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = schema,
            Username = role,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        });
    }

    private static string Table<T>(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private sealed class Authority(
        string administratorConnection, string schema, string role, string[] tables, bool ownsLogin,
        PrimaryDatabaseConnectionOptions options) : IAsyncDisposable
    {
        internal PrimaryDatabaseConnectionOptions Options { get; } = options;

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(Options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administrator = new NpgsqlConnection(administratorConnection);
            await administrator.OpenAsync();
            await using var command = administrator.CreateCommand();
            command.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", tables)} FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                """;
            await command.ExecuteNonQueryAsync();
            if (ownsLogin)
            {
                command.CommandText = $"DROP ROLE \"{role}\"";
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
