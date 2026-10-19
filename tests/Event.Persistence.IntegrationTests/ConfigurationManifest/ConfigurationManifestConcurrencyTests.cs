using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.Management;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Features.Tenants;
using Explore.Application.Features.Tenants.Handlers.Commands.CreateTenant;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Settings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestConcurrencyTests(
    PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ConcurrentBootstrap_SameSlugCreatesOnceAndAuditsSecondAsSkipped()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        var initialGate = new InitialPreflightGate(participantCount: 2);
        await using var firstHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        await using var secondHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        firstHandler.BeforeLock = secondHandler.BeforeLock = (key, _, token) =>
            key == ConfigurationManifestLockKeys.InstanceManifest ? initialGate.SignalAndWaitAsync(token) : Task.CompletedTask;
        var source = ConfigurationManifestApplicationTestSupport.Source("collision");

        var results = await Task.WhenAll(
            firstHandler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(source),
                CancellationToken.None),
            secondHandler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(source),
                CancellationToken.None));

        await Assert.That(results.All(result => result.IsSuccess)).IsTrue();
        await using ExploreDbContext verification = fixture.CreateDbContext();
        ConfigurationManifestOperation[] operations =
            await verification.ConfigurationManifestOperations
                .AsNoTracking()
                .ToArrayAsync();
        ConfigurationManifestTenantResult[] tenantResults =
            await verification.ConfigurationManifestTenantResults
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToArrayAsync();

        await Assert.That(await verification.Tenants.CountAsync(tenant =>
            tenant.Slug == "collision")).IsEqualTo(1);
        await Assert.That(operations.Length).IsEqualTo(2);
        await Assert.That(operations.All(operation => operation.Status == ConfigurationManifestOperationStatus.Applied)).IsTrue();
        await Assert.That(operations.Count(operation =>
            operation.CreatedTenantCount == 1)).IsEqualTo(1);
        await Assert.That(operations.Count(operation =>
            operation.SkippedExistingTenantCount == 1)).IsEqualTo(1);
        var effects = await verification.OutboxMessages.AsNoTracking().ToArrayAsync();
        await Assert.That(effects.Length).IsEqualTo(1);
        var creator = operations.Single(operation => operation.CreatedTenantCount == 1);
        var skipped = operations.Single(operation => operation.SkippedExistingTenantCount == 1);
        await Assert.That(skipped.EffectOutboxId).IsNull();
        await Assert.That(effects[0].Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(effects[0].AggregateType).IsEqualTo(ConfigurationManifestEffectOutbox.AggregateType);
        await Assert.That(effects[0].EventType).IsEqualTo(ConfigurationManifestEffectOutbox.EventType);
        var instructions = ConfigurationManifestEffectPayload.Read(effects[0].Payload, effects[0].CreatedAt);
        await Assert.That(instructions.Version).IsEqualTo(ConfigurationManifestEffectPayload.CurrentVersion);
        await Assert.That(instructions.Tenants.Length).IsEqualTo(1);
        await Assert.That(effects[0].AggregateId).IsEqualTo(creator.Id);
        await Assert.That(effects[0].Id).IsEqualTo(creator.EffectOutboxId);
        await Assert.That(tenantResults.Select(result => result.Status)).IsEquivalentTo(
        [
            ConfigurationManifestTenantResultStatus.Created,
            ConfigurationManifestTenantResultStatus.SkippedExisting
        ]);
    }

    [Test]
    public async Task FreshPreflight_StartsAfterLaterAuthorityWaitAndSeesCommittedTenant()
    {
        await fixture.ResetAsync();
        const string slug = "snapshot-fence";
        string slugLockKey = TenantMutationLockKeys.ForSlug(slug);
        var laterLockAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLaterLockAttempt = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using ExploreDbContext manifestContext = fixture.CreateDbContext();
        bool transactionWasActiveAtLaterAttempt = true;
        await using ExploreDbContext writerContext = fixture.CreateDbContext();
        var writerLockAcquired = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Guid committedTenantId = Guid.CreateVersion7();
        Task writerTask = writerContext.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                await using var writerTransaction =
                    await writerContext.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable);
                await using IAsyncDisposable writerLease =
                    await RelationalNamedLock.AcquireTransactionAsync(
                        writerContext,
                        $"explore:setting-mutation:{slugLockKey}",
                        CancellationToken.None);
                writerLockAcquired.TrySetResult();
                await laterLockAttempted.Task.WaitAsync(TimeSpan.FromSeconds(30));
                writerContext.Tenants.Add(new Tenant
                {
                    Id = committedTenantId,
                    FullName = "Snapshot Fence Tenant",
                    Slug = slug,
                    TenantStatusId = (int)TenantStatusEnum.Provisioning,
                    TenantStatus = null!,
                    CreatedAt = DateTime.UtcNow
                });
                await writerContext.SaveChangesAsync();
                await writerTransaction.CommitAsync();
            });
        await writerLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var observed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        handler.BeforeLock = async (key, actualOwner, cancellationToken) =>
        {
            if (key != slugLockKey) return;
            transactionWasActiveAtLaterAttempt = actualOwner.Database.CurrentTransaction is not null;
            laterLockAttempted.TrySetResult();
            await releaseLaterLockAttempt.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        };
        handler.AfterObservation = async (actualOwner, _, token) =>
        {
            await Assert.That(actualOwner.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(System.Data.IsolationLevel.Serializable);
            await using var independent = fixture.CreateDbContext();
            observed.TrySetResult(await independent.Tenants.Where(tenant => tenant.Slug == slug)
                .Select(tenant => tenant.Id).SingleAsync(token));
        };

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> manifestTask =
            handler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.Source(slug)),
                CancellationToken.None);
        await laterLockAttempted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        try
        {
            await writerTask.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            releaseLaterLockAttempt.TrySetResult();
        }

        Guid observedTenant = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Explore.Application.Responses.BaseCommandResponse<Guid> result =
            await manifestTask.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(transactionWasActiveAtLaterAttempt).IsFalse();
        await Assert.That(observedTenant).IsEqualTo(committedTenantId);
        await Assert.That(result.IsSuccess).IsTrue();
        await using ExploreDbContext verification = fixture.CreateDbContext();
        ConfigurationManifestOperation operation = await verification
            .ConfigurationManifestOperations
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(operation.CreatedTenantCount).IsEqualTo(0);
        await Assert.That(operation.SkippedExistingTenantCount).IsEqualTo(1);
        await Assert.That((await verification.ConfigurationManifestTenantResults.IgnoreQueryFilters().SingleAsync()).Status)
            .IsEqualTo(ConfigurationManifestTenantResultStatus.SkippedExisting);
    }

    [Test]
    public async Task PaidPolicyRevisionDrift_RollsBackTenantSettingsDocumentAndPolicy()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            await new PaidEventPolicyRepository(seed).AddAsync(
                PaidEventPolicyVersion.CreateDefaultInstance(),
                CancellationToken.None);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext apply = fixture.CreateDbContext();
        var sql = apply.GetService<ISqlGenerationHelper>();
        var tenantType = apply.Model.FindEntityType(typeof(Tenant))!;
        var policyType = apply.Model.FindEntityType(typeof(PaidEventPolicyVersion))!;
        var tenantStore = StoreObjectIdentifier.Table(tenantType.GetTableName()!, tenantType.GetSchema());
        var policyStore = StoreObjectIdentifier.Table(policyType.GetTableName()!, policyType.GetSchema());
        string tenants = sql.DelimitIdentifier(tenantStore.Name, tenantStore.Schema ?? "public");
        string policies = sql.DelimitIdentifier(policyStore.Name, policyStore.Schema ?? "public");
        string slugColumn = sql.DelimitIdentifier(tenantType.FindProperty(nameof(Tenant.Slug))!.GetColumnName(tenantStore)!);
        string versionColumn = sql.DelimitIdentifier(policyType.FindProperty(nameof(PaidEventPolicyVersion.VersionNumber))!.GetColumnName(policyStore)!);
        string activeColumn = sql.DelimitIdentifier(policyType.FindProperty(nameof(PaidEventPolicyVersion.IsActive))!.GetColumnName(policyStore)!);
        string tenantColumn = sql.DelimitIdentifier(policyType.FindProperty(nameof(PaidEventPolicyVersion.TenantId))!.GetColumnName(policyStore)!);
        string function = sql.DelimitIdentifier("test_manifest_policy_drift", tenantStore.Schema ?? "public");
        await apply.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
            BEGIN
                IF NEW.{slugColumn} = 'paid-drift' THEN
                    UPDATE {policies} SET {versionColumn} = {versionColumn} + 1
                    WHERE {tenantColumn} IS NULL AND {activeColumn};
                END IF;
                RETURN NEW;
            END;
            $body$;
            CREATE TRIGGER test_manifest_policy_drift AFTER INSERT ON {tenants}
            FOR EACH ROW EXECUTE FUNCTION {function}();
            """);
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        Explore.Application.Responses.BaseCommandResponse<Guid> result;
        try
        {
            result = await handler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.PaidPolicySource(
                        new string('d', ConfigurationManifestOperation.DigestLength),
                        "paid-drift")),
                CancellationToken.None);
        }
        finally
        {
            await apply.Database.ExecuteSqlRawAsync(
                $"DROP TRIGGER test_manifest_policy_drift ON {tenants}; DROP FUNCTION {function}();");
        }

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ConfigurationManifestApplicationFailureCodes.WriteConflict);
        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That((await verification.ConfigurationManifestOperations.SingleAsync()).Status)
            .IsEqualTo(ConfigurationManifestOperationStatus.Failed);
        await Assert.That(await verification.Tenants
            .IgnoreQueryFilters()
            .CountAsync(tenant => tenant.Slug == "paid-drift")).IsEqualTo(0);
        await Assert.That(await verification.TenantSettingOverrides
            .IgnoreQueryFilters()
            .CountAsync()).IsEqualTo(0);
        await Assert.That(await verification.TenantSettingsDocuments
            .IgnoreQueryFilters()
            .CountAsync()).IsEqualTo(0);
        await Assert.That(await verification.PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .CountAsync(policy => policy.TenantId != null)).IsEqualTo(0);
        await Assert.That(await verification.PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .CountAsync(policy =>
                policy.TenantId == null
                && policy.IsActive
                && policy.VersionNumber == 1)).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentManifestAndInstancePolicyRevision_LinearizeWithoutPartialState()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            await new PaidEventPolicyRepository(seed).AddAsync(
                PaidEventPolicyVersion.CreateDefaultInstance(),
                CancellationToken.None);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext manifestContext = fixture.CreateDbContext();
        await using ExploreDbContext policyContext = fixture.CreateDbContext();
        var initialGate = new InitialPreflightGate(participantCount: 2);
        await using var manifestHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        manifestHandler.BeforeLock = (key, _, token) =>
            key == ConfigurationManifestLockKeys.InstanceManifest ? initialGate.SignalAndWaitAsync(token) : Task.CompletedTask;
        var policyUnitOfWork = new EfCoreUnitOfWork(policyContext);
        var policyBoundary = new PaidEventPolicyMutationBoundary(
            new PaidEventPolicyRepository(policyContext),
            policyUnitOfWork,
            new RelationalSettingMutationLock(policyContext, policyUnitOfWork));

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> manifestTask =
            manifestHandler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.PaidPolicySource(
                        new string(
                            'e',
                            ConfigurationManifestOperation.DigestLength),
                        "paid-race")),
                CancellationToken.None);
        Task<PaidEventPolicyMutationResult> policyTask = ReviseInstanceAfterGateAsync();
        await Task.WhenAll(manifestTask, policyTask);

        PaidEventPolicyMutationResult policyResult = await policyTask;
        Explore.Application.Responses.BaseCommandResponse<Guid> manifestResult =
            await manifestTask;
        await Assert.That(policyResult.Success).IsTrue();
        await using ExploreDbContext verification = fixture.CreateDbContext();
        PaidEventPolicyVersion activeInstance = await verification
            .PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(policy => policy.TenantId == null && policy.IsActive);
        PaidEventPolicyVersion? tenantPolicy = await verification
            .PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(policy =>
                policy.TenantId != null
                && policy.IsActive);
        int tenantCount = await verification.Tenants
            .IgnoreQueryFilters()
            .CountAsync(tenant => tenant.Slug == "paid-race");

        await Assert.That(activeInstance.VersionNumber).IsEqualTo(2);
        if (manifestResult.IsSuccess)
        {
            await Assert.That(tenantCount).IsEqualTo(1);
            await Assert.That(tenantPolicy).IsNotNull();
            PaidEventPolicyRules.ValidateTenantPolicy(activeInstance, tenantPolicy!);
        }
        else
        {
            await Assert.That(manifestResult.FailureCode)
                .IsEqualTo(
                    ConfigurationManifestApplicationFailureCodes.WriteConflict);
            await Assert.That(tenantCount).IsEqualTo(0);
            await Assert.That(tenantPolicy).IsNull();
        }

        async Task<PaidEventPolicyMutationResult> ReviseInstanceAfterGateAsync()
        {
            await initialGate.SignalAndWaitAsync(CancellationToken.None);
            return await policyBoundary.ReviseInstanceAsync(
                InstanceRevision(),
                CancellationToken.None);
        }
    }

    [Test]
    public async Task ManifestSerializesWithOrdinarySettingAndBrandingLockSets()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext manifestContext = fixture.CreateDbContext();
        await using ExploreDbContext settingContext = fixture.CreateDbContext();
        await using ExploreDbContext brandingContext = fixture.CreateDbContext();
        var tenantCreationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTenantCreation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        handler.AfterObservation = async (_, _, token) =>
        {
            tenantCreationEntered.TrySetResult();
            await releaseTenantCreation.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        };

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> manifestTask =
            handler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.Source(
                        "authority-lock-race")),
                CancellationToken.None);
        await tenantCreationEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await using (ExploreDbContext competitor = fixture.CreateDbContext())
        {
            await Assert.That(await ConfigurationManifestCompetingWriterRedTests.TryAcquireCompetingLockAsync(
                competitor, PublicExperienceSettingDefinitions.EventCatalogLabel.Key)).IsFalse();
            foreach (string key in TenantBrandingGovernanceMutationLockKeys.All)
                await Assert.That(await ConfigurationManifestCompetingWriterRedTests.TryAcquireCompetingLockAsync(competitor, key)).IsFalse();
        }
        var settingAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var settingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var brandingAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var brandingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> settingTask = ObserveTenantAfterLockAsync(
            new RelationalSettingMutationLock(
                settingContext,
                new EfCoreUnitOfWork(settingContext)),
            [
                PublicExperienceSettingDefinitions.EventCatalogLabel.Key
            ],
            settingContext,
            "authority-lock-race",
            settingAttempted,
            settingEntered);
        Task<bool> brandingTask = ObserveTenantAfterLockAsync(
            new RelationalSettingMutationLock(
                brandingContext,
                new EfCoreUnitOfWork(brandingContext)),
            TenantBrandingGovernanceMutationLockKeys.All,
            brandingContext,
            "authority-lock-race",
            brandingAttempted,
            brandingEntered);
        await Task.WhenAll(
            settingAttempted.Task.WaitAsync(TimeSpan.FromSeconds(30)),
            brandingAttempted.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        await Assert.That(settingEntered.Task.IsCompleted).IsFalse();
        await Assert.That(brandingEntered.Task.IsCompleted).IsFalse();

        releaseTenantCreation.TrySetResult();
        await Task.WhenAll(manifestTask, settingTask, brandingTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That((await manifestTask).IsSuccess).IsTrue();
        await Assert.That(await settingTask).IsTrue();
        await Assert.That(await brandingTask).IsTrue();
        await using var receipt = fixture.CreateDbContext();
        await Assert.That((await receipt.ConfigurationManifestOperations.SingleAsync()).Status)
            .IsEqualTo(ConfigurationManifestOperationStatus.Applied);
    }

    [Test]
    public async Task ConcurrentManifestAndOrdinaryCreateShareNormalizedSlugLock()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext manifestContext = fixture.CreateDbContext();
        var initialGate = new InitialPreflightGate(participantCount: 2);
        await using ExploreDbContext ordinaryContext = fixture.CreateDbContext(new OrdinarySlugObservation(initialGate));
        await using var manifestHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        manifestHandler.BeforeLock = (key, _, token) =>
            key == ConfigurationManifestLockKeys.InstanceManifest ? initialGate.SignalAndWaitAsync(token) : Task.CompletedTask;
        var ordinaryRepository = new TenantRepository(ordinaryContext);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var ordinaryHandler = new CreateTenantCommandHandler(
            ordinaryRepository,
            new TenantUserRoleGrantRepository(ordinaryContext),
            new TenantUserRepository(ordinaryContext),
            new RoleRepository(ordinaryContext),
            new TenantCreationService(
                ordinaryRepository,
                new TenantSettingsDocumentRepository(ordinaryContext)),
            new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(ordinaryContext), cache),
            NullLogger<CreateTenantCommandHandler>.Instance,
            new RelationalSettingMutationLock(
                ordinaryContext,
                new EfCoreUnitOfWork(ordinaryContext)),
            new TenantActivationCapacityPolicy(
                new InstanceBootstrapStateRepository(ordinaryContext),
                ordinaryRepository,
                new ManagedTenantProvisioningOperationRepository(ordinaryContext),
                Options.Create(new ManagedControlPlaneOptions())));
        var ordinaryCommand = new CreateTenantCommand
        {
            TenantDto = new CreateTenantDto
            {
                FullName = "Shared Community",
                Slug = "shared",
                IsActive = false,
                AssignCurrentUserAsTenantAdmin = false
            }
        };

        var results = await Task.WhenAll(
            manifestHandler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.Source("shared")),
                CancellationToken.None),
            ordinaryHandler.ExecuteAsync(ordinaryCommand, CancellationToken.None));

        await Assert.That(results[0].IsSuccess).IsTrue();
        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That(await verification.Tenants.CountAsync(tenant =>
            tenant.Slug == "shared")).IsEqualTo(1);
        ConfigurationManifestOperation operation =
            await verification.ConfigurationManifestOperations
                .AsNoTracking()
                .SingleAsync();
        await Assert.That(operation.CreatedTenantCount + operation.SkippedExistingTenantCount)
            .IsEqualTo(1);
    }

    private sealed class OrdinarySlugObservation(InitialPreflightGate gate) : DbCommandInterceptor
    {
        private int observed;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is "shared")
                && Interlocked.Exchange(ref observed, 1) == 0)
                await gate.SignalAndWaitAsync(cancellationToken);
            return result;
        }
    }

    private static RevisePaidEventPolicyDto InstanceRevision() => new()
    {
        IsPaymentsEnabled = false,
        AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization],
        RequiresLocalVerification = false,
        AllowedCurrencyCodes = ["USD"],
        DefaultCurrencyCode = "USD",
        RefundProtectionIds = Enum.GetValues<PaidEventRefundProtection>()
            .Select(protection => (int)protection)
            .ToArray(),
        CurrencyRiskLimits = [],
        RequiresFirstPaidEventReview = false,
        FarFutureReviewThresholdDays = null
    };

    private static async Task<bool> ObserveTenantAfterLockAsync(
        ISettingMutationLock mutationLock,
        IEnumerable<string> lockKeys,
        ExploreDbContext context,
        string tenantSlug,
        TaskCompletionSource attempted,
        TaskCompletionSource entered)
    {
        attempted.TrySetResult();
        return await mutationLock.ExecuteManyAsync(
            lockKeys,
            async cancellationToken =>
            {
                entered.TrySetResult();
                return await context.Tenants
                    .AsNoTracking()
                    .AnyAsync(
                        tenant => tenant.Slug == tenantSlug,
                        cancellationToken);
            },
            CancellationToken.None);
    }

    private sealed class InitialPreflightGate(int participantCount)
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == participantCount)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
}
