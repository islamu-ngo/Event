using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Features.Tenants;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

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
        await using ExploreDbContext observer = fixture.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstOwnerEntered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReachedSettingLocks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        await using var secondHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        firstHandler.BeforeLock = async (key, actual, token) =>
        {
            if (key != ConfigurationManifestLockKeys.InstanceManifest) return;
            EventDiscoveryEnrollmentGate.RequireCurrent(actual);
            firstOwnerEntered.TrySetResult(((NpgsqlConnection)actual.Database.GetDbConnection()).ProcessID);
            await releaseFirstOwner.Task.WaitAsync(token);
        };
        secondHandler.BeforeLock = (_, _, _) =>
        {
            secondReachedSettingLocks.TrySetResult();
            return Task.CompletedTask;
        };
        var source = ConfigurationManifestApplicationTestSupport.Source("collision");
        var firstTask = firstHandler.ExecuteAsync(new ApplyConfigurationManifestCommand(source), timeout.Token);
        int firstBackend = await AwaitCheckpointAsync(firstOwnerEntered.Task, firstTask, timeout.Token);
        var secondTask = secondHandler.ExecuteAsync(new ApplyConfigurationManifestCommand(source), timeout.Token);
        bool secondWasPending;
        bool settingLocksWereNotReached;
        try
        {
            await WaitForAdvisoryWaiterAsync(observer, firstBackend, secondTask, timeout.Token);
            secondWasPending = !secondTask.IsCompleted;
            settingLocksWereNotReached = !secondReachedSettingLocks.Task.IsCompleted;
        }
        finally
        {
            releaseFirstOwner.TrySetResult();
        }
        var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(timeout.Token);

        await Assert.That(secondWasPending).IsTrue();
        await Assert.That(settingLocksWereNotReached).IsTrue();
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
        var releaseWriter = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using ExploreDbContext observer = fixture.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool transactionWasActiveAtLaterAttempt = true;
        await using ExploreDbContext writerContext = fixture.CreateDbContext();
        writerContext.ClearTenantFilterBypass();
        writerContext.BindTenantLease();
        var writerLockAcquired = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Guid committedTenantId = Guid.CreateVersion7();
        Task writerTask = writerContext.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                await using var enrollment = new EventDiscoveryEnrollmentGate(writerContext);
                await using var enrollmentLease = await enrollment.AcquireAsync(timeout.Token);
                await using var writerTransaction =
                    await writerContext.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable, timeout.Token);
                await using IAsyncDisposable writerLease =
                    await RelationalNamedLock.AcquireTransactionAsync(
                        writerContext,
                        $"explore:setting-mutation:{slugLockKey}",
                        timeout.Token);
                writerLockAcquired.TrySetResult(((NpgsqlConnection)writerContext.Database.GetDbConnection()).ProcessID);
                await releaseWriter.Task.WaitAsync(timeout.Token);
                writerContext.Tenants.Add(new Tenant
                {
                    Id = committedTenantId,
                    FullName = "Snapshot Fence Tenant",
                    Slug = slug,
                    TenantStatusId = (int)TenantStatusEnum.Provisioning,
                    TenantStatus = null!,
                    CreatedAt = DateTime.UtcNow
                });
                await writerContext.SaveChangesAsync(timeout.Token);
                await writerTransaction.CommitAsync(timeout.Token);
            });
        int writerBackend = await AwaitCheckpointAsync(writerLockAcquired.Task, writerTask, timeout.Token);
        var observed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        handler.BeforeLock = (key, actualOwner, _) =>
        {
            if (key != slugLockKey) return Task.CompletedTask;
            transactionWasActiveAtLaterAttempt = actualOwner.Database.CurrentTransaction is not null;
            laterLockAttempted.TrySetResult();
            return Task.CompletedTask;
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
                timeout.Token);
        try
        {
            await WaitForAdvisoryWaiterAsync(observer, writerBackend, manifestTask, timeout.Token);
            await Assert.That(laterLockAttempted.Task.IsCompleted).IsFalse();
        }
        finally
        {
            releaseWriter.TrySetResult();
        }
        await writerTask.WaitAsync(timeout.Token);
        await laterLockAttempted.Task.WaitAsync(timeout.Token);

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
    public async Task CancelledBootstrap_LaterSettingWaitReleasesEarlierLeasesBeforeAnyTransaction()
    {
        await fixture.ResetAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        const string slug = "cancel-before-begin";
        string laterKey = TenantMutationLockKeys.ForSlug(slug);
        await using var holder = fixture.CreateDbContext();
        await holder.Database.OpenConnectionAsync(timeout.Token);
        await using var heldKey = await RelationalNamedLock.AcquireSessionAsync(
            holder, $"explore:setting-mutation:{laterKey}", timeout.Token);
        int holderBackend = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        await using var observer = fixture.CreateDbContext();
        var data = new ConfigurationManifestBootstrapData(observer);
        var systemsBefore = (await observer.SystemSettings.AsNoTracking().ToArrayAsync(timeout.Token))
            .Select(row => data.Row(row).ToJsonString()).ToArray();
        var policiesBefore = (await observer.PaidEventPolicyVersions.IgnoreQueryFilters().AsNoTracking().ToArrayAsync(timeout.Token))
            .Select(row => data.Row(row).ToJsonString()).ToArray();
        var attempted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var earlierKeys = new List<string>();
        bool transactionWasActive = true;
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        handler.BeforeLock = (key, original, _) =>
        {
            if (key != laterKey)
                earlierKeys.Add(key);
            else
            {
                EventDiscoveryEnrollmentGate.RequireCurrent(original);
                transactionWasActive = original.Database.CurrentTransaction is not null;
                attempted.TrySetResult(((NpgsqlConnection)original.Database.GetDbConnection()).ProcessID);
            }
            return Task.CompletedTask;
        };
        var bootstrap = handler.ExecuteAsync(new ApplyConfigurationManifestCommand(
            ConfigurationManifestApplicationTestSupport.Source(slug)), cancellation.Token);
        Exception? originalFailure = null;
        try
        {
            int bootstrapBackend = await AwaitCheckpointAsync(attempted.Task, bootstrap, timeout.Token);
            await WaitForAdvisoryWaiterAsync(observer, holderBackend, bootstrap, timeout.Token, bootstrapBackend);
            await Assert.That(transactionWasActive).IsFalse();
            await Assert.That(earlierKeys.Contains(ConfigurationManifestLockKeys.InstanceManifest)).IsTrue();

            cancellation.Cancel();
            bool cancelled = false;
            try
            {
                await bootstrap.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !timeout.IsCancellationRequested)
            {
                cancelled = true;
            }
            await Assert.That(cancelled).IsTrue();

            // The independent later-key holder stays active while all bootstrap-owned resources are proved reusable.
            await using (var probe = fixture.CreateDbContext())
            {
                probe.ClearTenantFilterBypass();
                probe.BindTenantLease();
                await using var enrollment = new EventDiscoveryEnrollmentGate(probe);
                await using var gate = await enrollment.AcquireAsync(timeout.Token);
                var leases = new List<IAsyncDisposable>();
                try
                {
                    foreach (string key in earlierKeys)
                        leases.Add(await RelationalNamedLock.AcquireSessionAsync(
                            probe, $"explore:setting-mutation:{key}", timeout.Token));
                    EventDiscoveryEnrollmentGate.RequireCurrent(probe);
                }
                finally
                {
                    for (int index = leases.Count - 1; index >= 0; index--)
                        await leases[index].DisposeAsync();
                }
            }

            await Assert.That((await observer.SystemSettings.AsNoTracking().ToArrayAsync(timeout.Token))
                .Select(row => data.Row(row).ToJsonString())).IsEquivalentTo(systemsBefore);
            await Assert.That((await observer.PaidEventPolicyVersions.IgnoreQueryFilters().AsNoTracking().ToArrayAsync(timeout.Token))
                .Select(row => data.Row(row).ToJsonString())).IsEquivalentTo(policiesBefore);
            await Assert.That(await observer.Tenants.AnyAsync(timeout.Token)).IsFalse();
            await Assert.That(await observer.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AnyAsync(timeout.Token)).IsFalse();
            await Assert.That(await observer.TenantSettingOverrides.IgnoreQueryFilters().AnyAsync(timeout.Token)).IsFalse();
            await Assert.That(await observer.TenantSettingsDocuments.IgnoreQueryFilters().AnyAsync(timeout.Token)).IsFalse();
            await Assert.That(await observer.ConfigurationManifestOperations.AnyAsync(timeout.Token)).IsFalse();
            await Assert.That(await observer.ConfigurationManifestTenantResults.IgnoreQueryFilters().AnyAsync(timeout.Token)).IsFalse();
            await Assert.That(await observer.OutboxMessages.IgnoreQueryFilters().AnyAsync(timeout.Token)).IsFalse();
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                cancellation.Cancel();
            }
            catch when (originalFailure is not null)
            {
                // An assertion failure remains primary even if a cancellation callback also fails.
            }
            finally
            {
                try
                {
                    await bootstrap;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch when (originalFailure is not null)
                {
                    // Preserve the original invariant failure after observing the operation's cleanup failure.
                }
            }
        }
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
    public async Task ConcurrentManifestAndOrdinaryCreateSettleOneCanonicalTenant()
    {
        await fixture.ResetAsync();
        const string slug = "shared";
        var ordinarySave = new OrdinaryCreationSavePause(slug);
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, fixture, ordinarySave);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        await using var observer = OpenMigrator();
        await using var manifestHandler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        var manifestBackend = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        manifestHandler.BeforeLock = (_, actual, _) =>
        {
            manifestBackend.TrySetResult(((NpgsqlConnection)actual.Database.GetDbConnection()).ProcessID);
            return Task.CompletedTask;
        };
        ordinarySave.Armed = true;
        Task<HttpResponseMessage> ordinaryTask = authentication.CreateTenantViaApiAsync(new CreateTenantDto
        {
            FullName = "Shared Community",
            Slug = slug,
            IsActive = false,
            AssignCurrentUserAsTenantAdmin = false
        }, deadline.Token);
        Task<BaseCommandResponse<Guid>>? manifestTask = null;
        bool completed = false;
        try
        {
            int creatorBackend = await AwaitCheckpointAsync(ordinarySave.Persisted.Task, ordinaryTask, deadline.Token);
            manifestTask = manifestHandler.ExecuteAsync(new ApplyConfigurationManifestCommand(
                ConfigurationManifestApplicationTestSupport.Source(slug)), deadline.Token);
            await WaitForAdvisoryWaiterAsync(observer, creatorBackend, manifestTask, deadline.Token);

            // Both creators first own the enrollment gate. Observe that real wait;
            // do not mislabel it as contention on the later normalized-slug lock.
            await using var waiting = observer.Database.GetDbConnection().CreateCommand();
            waiting.CommandText = """
                SELECT DISTINCT locks.pid FROM pg_catalog.pg_locks locks
                JOIN pg_catalog.pg_stat_activity activity ON activity.pid = locks.pid
                WHERE locks.locktype = 'advisory' AND NOT locks.granted
                    AND @holder = ANY (pg_catalog.pg_blocking_pids(locks.pid))
                    AND activity.usename = @login
                """;
            waiting.Parameters.Add(new NpgsqlParameter("holder", creatorBackend));
            waiting.Parameters.Add(new NpgsqlParameter("login", fixture.NativeQueueEnrollment.GlobalLogin));
            var waiters = new List<int>();
            await using (var rows = await waiting.ExecuteReaderAsync(deadline.Token))
                while (await rows.ReadAsync(deadline.Token)) waiters.Add(rows.GetInt32(0));
            await Assert.That(waiters.Count).IsEqualTo(1);
            await Assert.That(manifestTask.IsCompleted).IsFalse();
            await Assert.That(manifestBackend.Task.IsCompleted).IsFalse();
            ordinarySave.Release.TrySetResult();

            using var response = await ordinaryTask.WaitAsync(deadline.Token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(authentication.TenantCreationFailureDescription);
            var ordinaryResult = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>(cancellationToken: deadline.Token);
            await Assert.That(ordinaryResult).IsNotNull();
            await Assert.That(ordinaryResult!.IsSuccess).IsTrue();
            var manifestResult = await manifestTask.WaitAsync(deadline.Token);
            await Assert.That(manifestResult.IsSuccess).IsTrue();
            await Assert.That(await manifestBackend.Task.WaitAsync(deadline.Token)).IsEqualTo(waiters.Single());

            await using var verification = OpenMigrator();
            var tenants = await verification.Tenants.AsNoTracking().Where(tenant => tenant.Slug == slug)
                .ToArrayAsync(deadline.Token);
            await Assert.That(tenants.Length).IsEqualTo(1);
            await Assert.That(tenants[0].Id).IsEqualTo(ordinaryResult.Id);
            var revisions = await verification.Set<EventDiscoveryRevision>()
                .IgnoreTenantFilter("Independent native observer verifies the exact tenant created by the competing original owners.")
                .AsNoTracking().Where(row => row.TenantId == tenants[0].Id).ToArrayAsync(deadline.Token);
            await Assert.That(revisions.Length).IsEqualTo(1);
            await Assert.That(revisions[0].Id.Version).IsEqualTo(7);
            await Assert.That(revisions[0].IdentityEpoch).IsEqualTo(0L);
            await Assert.That(revisions[0].DisclosureEpoch).IsEqualTo(1L);
            var operation = await verification.ConfigurationManifestOperations.AsNoTracking().SingleAsync(deadline.Token);
            await Assert.That(operation.Status).IsEqualTo(ConfigurationManifestOperationStatus.Applied);
            await Assert.That(operation.CreatedTenantCount).IsEqualTo(0);
            await Assert.That(operation.SkippedExistingTenantCount).IsEqualTo(1);
            completed = true;
        }
        finally
        {
            ordinarySave.Release.TrySetResult();
            if (!completed) await deadline.CancelAsync();
            try { (await ordinaryTask.WaitAsync(TimeSpan.FromSeconds(15))).Dispose(); }
            catch when (!completed) { }
            if (manifestTask is not null)
            {
                try { await manifestTask.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch when (!completed) { }
            }
        }

        ExploreDbContext OpenMigrator()
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
            var context = new ExploreDbContext(options.Options);
            context.BindTenantLease();
            return context;
        }
    }

    private sealed class OrdinaryCreationSavePause(string slug) : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource<int> Persisted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context
                || context.ChangeTracker.Entries<Tenant>().SingleOrDefault(entry =>
                    entry.State == EntityState.Unchanged && entry.Entity.Slug == slug) is not { } created)
                return result;
            Armed = false;
            await Assert.That(context.TenantFilterTenantId).IsNull();
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("The original creator transaction must remain active after Save.");
            await Assert.That(transaction.IsolationLevel).IsEqualTo(System.Data.IsolationLevel.Serializable);
            await Assert.That(ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())).IsTrue();
            await Assert.That(context.ChangeTracker.Entries<EventDiscoveryRevision>().Count(entry =>
                entry.Entity.TenantId == created.Entity.Id && entry.State == EntityState.Unchanged)).IsEqualTo(1);
            Persisted.TrySetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
            await Release.Task.WaitAsync(cancellationToken);
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

    private static async Task<T> AwaitCheckpointAsync<T>(Task<T> checkpoint, Task operation, CancellationToken token)
    {
        await Task.WhenAny(checkpoint, operation).WaitAsync(token);
        if (!checkpoint.IsCompleted)
        {
            await operation;
            throw new InvalidOperationException("The operation completed before its required concurrency checkpoint.");
        }
        return await checkpoint.WaitAsync(token);
    }

    private static async Task WaitForAdvisoryWaiterAsync(ExploreDbContext observer, int holderBackend,
        Task operation, CancellationToken token, int? waiterBackend = null)
    {
        await observer.Database.OpenConnectionAsync(token);
        await using var command = observer.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_locks waiting
                WHERE waiting.locktype = 'advisory' AND NOT waiting.granted
                    {(waiterBackend.HasValue ? "AND waiting.pid = @waiter" : "")}
                    AND @holder = ANY (pg_catalog.pg_blocking_pids(waiting.pid)))
            """;
        command.Parameters.Add(new NpgsqlParameter { ParameterName = "holder", Value = holderBackend });
        if (waiterBackend.HasValue)
            command.Parameters.Add(new NpgsqlParameter { ParameterName = "waiter", Value = waiterBackend.Value });
        while (true)
        {
            if (operation.IsCompleted)
            {
                await operation;
                throw new InvalidOperationException("The operation completed before its PostgreSQL advisory wait was observed.");
            }
            if (await command.ExecuteScalarAsync(token) is true)
                return;
            await Task.WhenAny(operation, Task.Delay(TimeSpan.FromMilliseconds(25), token)).WaitAsync(token);
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

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
}
