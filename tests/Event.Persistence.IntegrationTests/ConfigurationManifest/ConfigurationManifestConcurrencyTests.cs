using System.Collections.Immutable;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.Management;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Compilation;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

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
        var failureRecorder =
            Substitute.For<IConfigurationManifestFailureRecorder>();
        var firstHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            firstContext,
            new GatedExistencePreflight(
                new TenantRepository(firstContext),
                initialGate),
            new ConfigurationManifestOperationRepository(firstContext),
            failureRecorder);
        var secondHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            secondContext,
            new GatedExistencePreflight(
                new TenantRepository(secondContext),
                initialGate),
            new ConfigurationManifestOperationRepository(secondContext),
            failureRecorder);
        var source = ConfigurationManifestApplicationTestSupport.Source("collision");

        var results = await Task.WhenAll(
            firstHandler.Handle(
                new ApplyConfigurationManifestCommand(source),
                CancellationToken.None),
            secondHandler.Handle(
                new ApplyConfigurationManifestCommand(source),
                CancellationToken.None));

        await Assert.That(results.All(result => result.IsSuccess)).IsTrue();
        await failureRecorder.DidNotReceiveWithAnyArgs()
            .RecordAsync(default!, default);
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
        await Assert.That(operations.Count(operation =>
            operation.CreatedTenantCount == 1)).IsEqualTo(1);
        await Assert.That(operations.Count(operation =>
            operation.SkippedExistingTenantCount == 1)).IsEqualTo(1);
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
        var manifestUnitOfWork = new EfCoreUnitOfWork(manifestContext);
        var manifestMutationLock = new RelationalSettingMutationLock(
            manifestContext,
            manifestUnitOfWork,
            async (canonicalKey, cancellationToken) =>
            {
                if (!string.Equals(
                        canonicalKey,
                        slugLockKey,
                        StringComparison.Ordinal))
                {
                    return;
                }

                transactionWasActiveAtLaterAttempt =
                    manifestContext.Database.CurrentTransaction is not null;
                laterLockAttempted.TrySetResult();
                await releaseLaterLockAttempt.Task.WaitAsync(cancellationToken);
            });
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
                await laterLockAttempted.Task;
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
        var preflight = new CapturingFreshPreflight(
            new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                new TenantRepository(manifestContext)));
        var failureRecorder =
            Substitute.For<IConfigurationManifestFailureRecorder>();
        var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            manifestContext,
            preflight,
            new ConfigurationManifestOperationRepository(manifestContext),
            failureRecorder,
            mutationLock: manifestMutationLock);

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> manifestTask =
            handler.Handle(
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

        ConfigurationManifestPreflightResult fresh =
            await preflight.FreshEvaluation.Task.WaitAsync(
                TimeSpan.FromSeconds(30));
        Explore.Application.Responses.BaseCommandResponse<Guid> result =
            await manifestTask.WaitAsync(TimeSpan.FromSeconds(30));

        ConfigurationManifestPreflightTenant tenant = fresh.Tenants.Single();
        await Assert.That(transactionWasActiveAtLaterAttempt).IsFalse();
        await Assert.That(tenant.Disposition)
            .IsEqualTo(ConfigurationManifestTenantDisposition.SkippedExisting);
        await Assert.That(tenant.TenantId).IsEqualTo(committedTenantId);
        await Assert.That(result.IsSuccess).IsTrue();
        await failureRecorder.DidNotReceiveWithAnyArgs()
            .RecordAsync(default!, default);
        await using ExploreDbContext verification = fixture.CreateDbContext();
        ConfigurationManifestOperation operation = await verification
            .ConfigurationManifestOperations
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(operation.CreatedTenantCount).IsEqualTo(0);
        await Assert.That(operation.SkippedExistingTenantCount).IsEqualTo(1);
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
        var failureRecorder =
            Substitute.For<IConfigurationManifestFailureRecorder>();
        var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            apply,
            new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                new TenantRepository(apply),
                new PaidEventPolicyRepository(apply),
                forcedExpectedPaidPolicyVersion: 2),
            new ConfigurationManifestOperationRepository(apply),
            failureRecorder);

        var result = await handler.Handle(
            new ApplyConfigurationManifestCommand(
                ConfigurationManifestApplicationTestSupport.PaidPolicySource(
                    new string('d', ConfigurationManifestOperation.DigestLength),
                    "paid-drift")),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ConfigurationManifestApplicationFailureCodes.WriteConflict);
        await failureRecorder.Received(1).RecordAsync(
            Arg.Is<ConfigurationManifestOperation>(operation =>
                operation.Status == ConfigurationManifestOperationStatus.Failed),
            Arg.Any<CancellationToken>());
        await using ExploreDbContext verification = fixture.CreateDbContext();
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
        var failureRecorder =
            Substitute.For<IConfigurationManifestFailureRecorder>();
        var manifestHandler =
            ConfigurationManifestApplicationTestSupport.CreateHandler(
                manifestContext,
                new GatedExistencePreflight(
                    new TenantRepository(manifestContext),
                    initialGate,
                    new PaidEventPolicyRepository(manifestContext)),
                new ConfigurationManifestOperationRepository(manifestContext),
                failureRecorder);
        var policyUnitOfWork = new EfCoreUnitOfWork(policyContext);
        var policyBoundary = new PaidEventPolicyMutationBoundary(
            new PaidEventPolicyRepository(policyContext),
            policyUnitOfWork,
            new RelationalSettingMutationLock(policyContext, policyUnitOfWork));

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> manifestTask =
            manifestHandler.Handle(
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
        var pausingTenantCreation = new PausingTenantCreationService(
            new TenantCreationService(
                new TenantRepository(manifestContext),
                new TenantSettingsDocumentRepository(manifestContext)),
            tenantCreationEntered,
            releaseTenantCreation);
        var failureRecorder =
            Substitute.For<IConfigurationManifestFailureRecorder>();
        var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            manifestContext,
            new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                new TenantRepository(manifestContext)),
            new ConfigurationManifestOperationRepository(manifestContext),
            failureRecorder,
            tenantCreationService: pausingTenantCreation);

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> manifestTask =
            handler.Handle(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.Source(
                        "authority-lock-race")),
                CancellationToken.None);
        await tenantCreationEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));

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
        await failureRecorder.DidNotReceiveWithAnyArgs()
            .RecordAsync(default!, default);
    }

    [Test]
    public async Task ConcurrentManifestAndOrdinaryCreateShareCanonicalSlugLock()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext manifestContext = fixture.CreateDbContext();
        await using ExploreDbContext ordinaryContext = fixture.CreateDbContext();
        var initialGate = new InitialPreflightGate(participantCount: 2);
        var manifestHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            manifestContext,
            new GatedExistencePreflight(
                new TenantRepository(manifestContext),
                initialGate),
            new ConfigurationManifestOperationRepository(manifestContext),
            Substitute.For<IConfigurationManifestFailureRecorder>());
        var realOrdinaryRepository = new TenantRepository(ordinaryContext);
        var ordinaryRepository = Substitute.For<ITenantRepository>();
        int slugLookupCount = 0;
        ordinaryRepository.GetTenantBySlug(Arg.Any<string>())
            .Returns(async call =>
            {
                Tenant? tenant = await realOrdinaryRepository.GetTenantBySlug(call.Arg<string>());
                if (Interlocked.Increment(ref slugLookupCount) == 1)
                {
                    await initialGate.SignalAndWaitAsync(CancellationToken.None);
                }

                return tenant;
            });
        ordinaryRepository.Create(Arg.Any<Tenant>())
            .Returns(call => realOrdinaryRepository.Create(call.Arg<Tenant>()));
        var ordinaryHandler = new CreateTenantCommandHandler(
            ordinaryRepository,
            Substitute.For<ITenantUserRoleGrantRepository>(),
            Substitute.For<ITenantUserRepository>(),
            Substitute.For<IRoleRepository>(),
            new TenantCreationService(
                ordinaryRepository,
                new TenantSettingsDocumentRepository(ordinaryContext)),
            Substitute.For<ITypedSettingsDocumentResolver>(),
            NullLogger<CreateTenantCommandHandler>.Instance,
            new RelationalSettingMutationLock(
                ordinaryContext,
                new EfCoreUnitOfWork(ordinaryContext)),
            new TenantActivationCapacityPolicy(
                Substitute.For<IInstanceBootstrapStateRepository>(),
                ordinaryRepository,
                Substitute.For<IManagedTenantProvisioningOperationRepository>(),
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
            manifestHandler.Handle(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.Source("shared")),
                CancellationToken.None),
            ordinaryHandler.Handle(ordinaryCommand, CancellationToken.None));

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

    private sealed class CapturingFreshPreflight(
        IConfigurationManifestPreflight inner) : IConfigurationManifestPreflight
    {
        private int _evaluationCount;

        public TaskCompletionSource<ConfigurationManifestPreflightResult>
            FreshEvaluation
        { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ConfigurationManifestPreflightResult> EvaluateAsync(
            ConfigurationManifestApplyPlan plan,
            CancellationToken cancellationToken)
        {
            ConfigurationManifestPreflightResult result =
                await inner.EvaluateAsync(plan, cancellationToken);
            if (Interlocked.Increment(ref _evaluationCount) == 2)
            {
                FreshEvaluation.TrySetResult(result);
            }

            return result;
        }
    }

    private sealed class GatedExistencePreflight(
        ITenantRepository tenantRepository,
        InitialPreflightGate initialGate,
        IPaidEventPolicyRepository? paidEventPolicies = null)
        : IConfigurationManifestPreflight
    {
        private int _evaluationCount;

        public async Task<ConfigurationManifestPreflightResult> EvaluateAsync(
            ConfigurationManifestApplyPlan plan,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _evaluationCount) == 1)
            {
                await initialGate.SignalAndWaitAsync(cancellationToken);
            }

            return await new ConfigurationManifestApplicationTestSupport
                .ExistencePreflight(tenantRepository, paidEventPolicies)
                .EvaluateAsync(plan, cancellationToken);
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

    private sealed class PausingTenantCreationService(
        ITenantCreationService inner,
        TaskCompletionSource entered,
        TaskCompletionSource release) : ITenantCreationService
    {
        public async Task<TenantCreationOutcome> CreateInCurrentTransactionAsync(
            TenantCreationRequest request,
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.CreateInCurrentTransactionAsync(
                request,
                cancellationToken);
        }
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

            await _allArrived.Task.WaitAsync(cancellationToken);
        }
    }
}
