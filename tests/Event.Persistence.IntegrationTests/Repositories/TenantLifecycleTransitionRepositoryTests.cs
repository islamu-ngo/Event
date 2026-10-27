using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantLifecycleTransitionRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    [Test]
    public async Task LifecycleTransaction_WhenStatusAndAuditSucceed_CommitsExactlyOneAudit()
    {
        await fixture.ResetAsync();
        var tenantId = await SeedActiveTenantAsync();
        var operatorId = Guid.NewGuid();
        var transitionedAt = TruncateToMicroseconds(DateTime.UtcNow);

        await using (var context = await CreateLifecycleContextAsync())
        {
            var tenantRepository = new TenantRepository(context, enrollment: fixture.NativeQueueEnrollment);
            var lifecycleLogRepository = new TenantLifecycleLogRepository(context);
            var unitOfWork = new EfCoreUnitOfWork(context);
            await using var humanGate = new HumanSecuritySourceGate(context);
            await using var heldHuman = await humanGate.AcquireAsync();

            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var transitioned = await tenantRepository.TryTransitionStatusAsync(
                    tenantId,
                    (int)TenantStatusEnum.Active,
                    (int)TenantStatusEnum.Suspended,
                    transitionedAt,
                    operatorId,
                    ct);
                await Assert.That(transitioned).IsTrue();

                await lifecycleLogRepository.CreateAsync(CreateLifecycleLog(
                    tenantId,
                    operatorId,
                    (int)TenantStatusEnum.Suspended,
                    transitionedAt), ct);
            });
        }

        await using var verifyContext = CreateSeedOrObserverContext();
        var savedTenant = await verifyContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId);
        var savedLogs = await verifyContext.TenantLifecycleLogs
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId)
            .ToListAsync();

        await Assert.That(savedTenant.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Suspended);
        await Assert.That(savedLogs).HasSingleItem();
        await Assert.That(savedLogs[0].OldStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(savedLogs[0].NewStatusId).IsEqualTo((int)TenantStatusEnum.Suspended);
        await Assert.That(savedLogs[0].TransitionedByUserId).IsEqualTo(operatorId);
    }

    [Test]
    public async Task LifecycleTransaction_WhenAuditWriteFails_RollsBackStatusAndAudit()
    {
        await fixture.ResetAsync();
        var tenantId = await SeedActiveTenantAsync();
        var operatorId = Guid.NewGuid();
        var transitionedAt = TruncateToMicroseconds(DateTime.UtcNow);

        await using (var context = await CreateLifecycleContextAsync())
        {
            var tenantRepository = new TenantRepository(context, enrollment: fixture.NativeQueueEnrollment);
            var lifecycleLogRepository = new TenantLifecycleLogRepository(context);
            var unitOfWork = new EfCoreUnitOfWork(context);
            await using var humanGate = new HumanSecuritySourceGate(context);
            await using var heldHuman = await humanGate.AcquireAsync();

            await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var transitioned = await tenantRepository.TryTransitionStatusAsync(
                    tenantId,
                    (int)TenantStatusEnum.Active,
                    (int)TenantStatusEnum.Suspended,
                    transitionedAt,
                    operatorId,
                    ct);
                await Assert.That(transitioned).IsTrue();

                await lifecycleLogRepository.CreateAsync(CreateLifecycleLog(
                    tenantId,
                    operatorId,
                    int.MaxValue,
                    transitionedAt), ct);
            }));
        }

        await using var verifyContext = CreateSeedOrObserverContext();
        var savedTenant = await verifyContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId);
        var savedAuditCount = await verifyContext.TenantLifecycleLogs
            .AsNoTracking()
            .CountAsync(log => log.TenantId == tenantId);

        await Assert.That(savedTenant.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(savedAuditCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryTransitionStatusAsync_WhenTwoWritersRace_AllowsExactlyOneWinner()
    {
        await fixture.ResetAsync();
        var tenantId = await SeedActiveTenantAsync();
        var suspendedBy = Guid.NewGuid();
        var archivedBy = Guid.NewGuid();

        await using var suspendedContext = await CreateLifecycleContextAsync();
        await using var archivedContext = await CreateLifecycleContextAsync();
        var suspendedRepository = new TenantRepository(suspendedContext, enrollment: fixture.NativeQueueEnrollment);
        var archivedRepository = new TenantRepository(archivedContext, enrollment: fixture.NativeQueueEnrollment);
        var transitionedAt = TruncateToMicroseconds(DateTime.UtcNow);

        var results = await Task.WhenAll(
            suspendedRepository.TryTransitionStatusAsync(
                tenantId,
                (int)TenantStatusEnum.Active,
                (int)TenantStatusEnum.Suspended,
                transitionedAt,
                suspendedBy,
                CancellationToken.None),
            archivedRepository.TryTransitionStatusAsync(
                tenantId,
                (int)TenantStatusEnum.Active,
                (int)TenantStatusEnum.Archived,
                transitionedAt,
                archivedBy,
                CancellationToken.None));

        await Assert.That(results.Count(result => result)).IsEqualTo(1);

        await using var verifyContext = CreateSeedOrObserverContext();
        var savedTenant = await verifyContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId);
        var expectedStatus = results[0] ? TenantStatusEnum.Suspended : TenantStatusEnum.Archived;
        var expectedOperator = results[0] ? suspendedBy : archivedBy;

        await Assert.That(savedTenant.TenantStatusId).IsEqualTo((int)expectedStatus);
        await Assert.That(savedTenant.UpdatedBy).IsEqualTo(expectedOperator);
        await Assert.That(savedTenant.UpdatedAt).IsEqualTo(transitionedAt);
    }

    [Test]
    public async Task IdentityMutationWinningLease_ActivationRejectsWarmStaleReadiness()
    {
        var seed = await SeedPrivateIdentityAsync();
        await using var writer = CreateIdentityContext(seed.TenantId);
        await using var reader = await CreateLifecycleContextAsync();
        using var writerCache = new MemoryCache(new MemoryCacheOptions());
        using var readerCache = new MemoryCache(new MemoryCacheOptions());
        var readerResolver = new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(reader), readerCache);
        var readiness = new TenantDirectoryOperatorReadinessEvaluator(readerResolver);
        await Assert.That((await readiness.EvaluateAsync(seed.TenantId, TenantDirectoryOperatorIdentityCapability.Activation)).IsReady).IsTrue();
        var winner = new GatedMutationLock(new RelationalSettingMutationLock(writer, new EfCoreUnitOfWork(writer)), hold: true);
        var contender = new GatedMutationLock(new RelationalSettingMutationLock(reader, new EfCoreUnitOfWork(reader)), hold: false);
        var patch = PatchHandler(writer, writerCache, winner, seed.TenantId);
        var activation = ActivationHandler(reader, contender, readiness);
        var mutation = patch.ExecuteAsync(IdentityPatch(seed, null), CancellationToken.None);
        await winner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var transition = activation.ExecuteAsync(new(seed.TenantId, TenantStatusEnum.Active, null), CancellationToken.None);
        await contender.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        winner.Release.TrySetResult();
        await Assert.That((await mutation.WaitAsync(TimeSpan.FromSeconds(15))).IsSuccess).IsTrue();
        await Assert.That((await transition.WaitAsync(TimeSpan.FromSeconds(15))).IsSuccess).IsFalse();
        await VerifyPrivateAsync(seed.TenantId, expectedLogs: 0);
    }

    [Test]
    public async Task ActivationWinningLease_IncompleteIdentityPatchCannotUndoActivatedReadiness()
    {
        var seed = await SeedPrivateIdentityAsync();
        await using var writer = CreateIdentityContext(seed.TenantId);
        await using var reader = await CreateLifecycleContextAsync();
        using var writerCache = new MemoryCache(new MemoryCacheOptions());
        using var readerCache = new MemoryCache(new MemoryCacheOptions());
        var winner = new GatedMutationLock(new RelationalSettingMutationLock(reader, new EfCoreUnitOfWork(reader)), hold: true);
        var contender = new GatedMutationLock(new RelationalSettingMutationLock(writer, new EfCoreUnitOfWork(writer)), hold: false);
        var activation = ActivationHandler(reader, winner, new TenantDirectoryOperatorReadinessEvaluator(
            new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(reader), readerCache)));
        var transition = activation.ExecuteAsync(new(seed.TenantId, TenantStatusEnum.Active, null), CancellationToken.None);
        await winner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var mutation = PatchHandler(writer, writerCache, contender, seed.TenantId).ExecuteAsync(IdentityPatch(seed, null), CancellationToken.None);
        await contender.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        winner.Release.TrySetResult();
        await Assert.That((await transition.WaitAsync(TimeSpan.FromSeconds(15))).IsSuccess).IsTrue();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => mutation.WaitAsync(TimeSpan.FromSeconds(15)));
        await using var verify = CreateSeedOrObserverContext();
        await Assert.That((await verify.Tenants.SingleAsync(t => t.Id == seed.TenantId)).TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That((await verify.TenantSettingsDocuments.SingleAsync(d => d.TenantId == seed.TenantId)).ConcurrencyStamp).IsEqualTo(seed.Revision);
        await Assert.That(await verify.TenantLifecycleLogs.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task IdentityWritersWithSameRevision_SecondLeaseRejectsStalePatchWithoutActivation()
    {
        var seed = await SeedPrivateIdentityAsync();
        await using var first = CreateIdentityContext(seed.TenantId);
        await using var second = CreateIdentityContext(seed.TenantId);
        using var firstCache = new MemoryCache(new MemoryCacheOptions());
        using var secondCache = new MemoryCache(new MemoryCacheOptions());
        var winner = new GatedMutationLock(new RelationalSettingMutationLock(first, new EfCoreUnitOfWork(first)), hold: true);
        var contender = new GatedMutationLock(new RelationalSettingMutationLock(second, new EfCoreUnitOfWork(second)), hold: false);
        var firstTask = PatchHandler(first, firstCache, winner, seed.TenantId).ExecuteAsync(IdentityPatch(seed, "First revision"), CancellationToken.None);
        await winner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var secondTask = PatchHandler(second, secondCache, contender, seed.TenantId).ExecuteAsync(IdentityPatch(seed, "Stale revision"), CancellationToken.None);
        await contender.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        winner.Release.TrySetResult();
        await Assert.That((await firstTask.WaitAsync(TimeSpan.FromSeconds(15))).IsSuccess).IsTrue();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondTask.WaitAsync(TimeSpan.FromSeconds(15)));
        await using var verify = CreateSeedOrObserverContext();
        var saved = await verify.TenantSettingsDocuments.SingleAsync(d => d.TenantId == seed.TenantId);
        await Assert.That(saved.ConcurrencyStamp).IsNotEqualTo(seed.Revision);
        await Assert.That(JsonSerializer.Deserialize<TenantDirectoryOperatorIdentitySettings>(saved.PayloadJson, SerializerOptions)!.LegalName).IsEqualTo("First revision");
        await VerifyPrivateAsync(seed.TenantId, expectedLogs: 0);
    }

    private async Task<(Guid TenantId, Guid Revision)> SeedPrivateIdentityAsync()
    {
        await fixture.ResetAsync();
        await using var db = CreateSeedOrObserverContext();
        await using var humanGate = new HumanSecuritySourceGate(db);
        await using var humanLease = await humanGate.AcquireAsync();
        await using var discoveryGate = new EventDiscoveryEnrollmentGate(db);
        await using var discoveryLease = await discoveryGate.AcquireAsync();
        Guid id = PlatformDefaults.DefaultTenantId;
        db.Tenants.Add(new Tenant { Id = id, FullName = "Private directory", Slug = "private-race", TenantStatusId = (int)TenantStatusEnum.Provisioning, TenantStatus = null! });
        var identity = TenantDirectoryOperatorIdentityDocumentDefaults.Create(id, new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = "Private operator",
            LegalName = "Private operator ASBL",
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            PublicContactEmail = "operator@example.test",
            LegalNoticeUrl = "https://example.test/legal",
            PrivacyUrl = "https://example.test/privacy"
        });
        db.TenantSettingsDocuments.Add(identity);
        await db.SaveChangesAsync();
        return (id, identity.ConcurrencyStamp);
    }

    private async Task VerifyPrivateAsync(Guid tenantId, int expectedLogs)
    {
        await using var verify = CreateSeedOrObserverContext();
        await Assert.That((await verify.Tenants.SingleAsync(t => t.Id == tenantId)).TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
        await Assert.That(await verify.TenantLifecycleLogs.CountAsync()).IsEqualTo(expectedLogs);
    }

    private static PatchTenantDirectoryOperatorIdentityDocumentCommand IdentityPatch((Guid TenantId, Guid Revision) seed, string? legalName) => new()
    {
        TenantId = seed.TenantId,
        Patch = new PatchTenantDirectoryOperatorIdentityDocumentDto
        {
            ExpectedConcurrencyStamp = seed.Revision,
            LegalEntity = new PatchTenantDirectoryOperatorLegalEntityDto { LegalName = OptionalUpdate<string?>.Set(legalName) }
        }
    };

    private static ICurrentUserService CurrentOperator()
    {
        var current = Substitute.For<ICurrentUserService>();
        current.UserId.Returns(Guid.CreateVersion7());
        current.IsAuthenticated.Returns(true);
        return current;
    }

    private static PatchTenantDirectoryOperatorIdentityDocumentCommandHandler PatchHandler(ExploreDbContext db, IMemoryCache cache, ISettingMutationLock mutationLock, Guid tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var documents = new TenantSettingsDocumentRepository(db);
        return new(tenantContext, CurrentOperator(), documents, new TenantRepository(db), mutationLock, new TypedSettingsDocumentResolver(documents, cache));
    }

    private TransitionInstanceTenantLifecycleCommandHandler ActivationHandler(ExploreDbContext db, ISettingMutationLock mutationLock, TenantDirectoryOperatorReadinessEvaluator readiness) => new(
        new TenantRepository(db, enrollment: fixture.NativeQueueEnrollment), new TenantLifecycleLogRepository(db), new EmailDispatchOutboxRepository(db), CurrentOperator(), mutationLock,
        new TenantActivationCapacityPolicy(new InstanceBootstrapStateRepository(db), new TenantRepository(db), new ManagedTenantProvisioningOperationRepository(db), Options.Create(new ManagedControlPlaneOptions())), readiness,
        () => new HumanSecuritySourceGate(db));

    private sealed class GatedMutationLock(ISettingMutationLock inner, bool hold) : ISettingMutationLock
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> ExecuteAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => ExecuteManyAsync([key], operation, cancellationToken);
        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> keys, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var task = inner.ExecuteManyAsync(keys, ct => RunAsync(operation, ct), cancellationToken);
            Started.TrySetResult();
            return task;
        }
        public Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> groups, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var task = inner.ExecuteOrderedGroupsAsync(groups, ct => RunAsync(operation, ct), cancellationToken);
            Started.TrySetResult();
            return task;
        }
        private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
        {
            Entered.TrySetResult();
            if (hold) await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            return await operation(ct);
        }
    }

    private async Task<Guid> SeedActiveTenantAsync()
    {
        await using var context = CreateSeedOrObserverContext();
        await using var humanGate = new HumanSecuritySourceGate(context);
        await using var humanLease = await humanGate.AcquireAsync();
        await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
        await using var discoveryLease = await discoveryGate.AcquireAsync();
        var activeStatus = await context.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            FullName = "Lifecycle CAS Tenant",
            Slug = $"lifecycle-cas-{Guid.NewGuid():N}",
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    // Seed/verification is explicit Migrator work; actual lifecycle and identity
    // mutations below never inherit those credentials or a filter bypass.
    private ExploreDbContext CreateSeedOrObserverContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private Task<ExploreDbContext> CreateLifecycleContextAsync() =>
        new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global))
            .CreateDbContextAsync();

    private ExploreDbContext CreateIdentityContext(Guid tenantId)
    {
        var context = fixture.CreateEnrolledTenantDbContext(new ExactTenant(tenantId));
        context.BindTenantLease();
        return context;
    }

    private sealed record ExactTenant(Guid TenantId) : ITenantContext;

    private static TenantLifecycleLog CreateLifecycleLog(
        Guid tenantId,
        Guid operatorId,
        int newStatusId,
        DateTime transitionedAt) => new()
        {
            TenantId = tenantId,
            Tenant = null!,
            OldStatusId = (int)TenantStatusEnum.Active,
            NewStatusId = newStatusId,
            NewStatus = null!,
            TransitionedByUserId = operatorId,
            Reason = "Lifecycle persistence test",
            TransitionedAt = transitionedAt,
            CreatedAt = transitionedAt,
            CreatedBy = operatorId
        };

    private static DateTime TruncateToMicroseconds(DateTime value) => new(
        value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond,
        DateTimeKind.Utc);
}
