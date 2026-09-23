using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources.Handlers.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.StorageObjects.Handlers.Commands;
using Explore.Application.Telemetry;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
public sealed class EventResourceFileUploadTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Pdf = "%PDF-1.7\nresource test\n%%EOF"u8.ToArray();

    [Test]
    public async Task NativeReservationBindsVersionSubjectAndRequestIdentity()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var handler = new CreateEventResourceUploadSessionCommandHandler(Workflow(context, seed));
        var dto = Intent(seed.Version);
        var first = await handler.ExecuteAsync(new(seed.ResourceId, dto), default);
        var replay = await handler.ExecuteAsync(new(seed.ResourceId, dto), default);
        var mismatch = await handler.ExecuteAsync(new(seed.ResourceId, dto with { ExpectedSizeBytes = Pdf.Length + 1 }), default);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.Id!.Id).IsEqualTo(first.Id!.Id);
        await Assert.That(mismatch.IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        var saved = await verify.StorageUploadSessions.SingleAsync(value => value.Id == first.Id.Id);
        await Assert.That(saved.ExpectedResourceVersion).IsEqualTo(seed.Version);
        await Assert.That(saved.UserId).IsEqualTo(seed.UserId);
        await Assert.That(saved.OwningResourceId).IsEqualTo(seed.ResourceId);
        await Assert.That(saved.Visibility).IsEqualTo(StorageObjectVisibilities.PrivateOwner);
        var quota = await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId);
        await Assert.That(quota.ReservedBytes).IsEqualTo(Pdf.Length);
    }

    [Test]
    public async Task GenericFinalizeAndCancelDispatchResourceSessionsWithoutGenericCollectionAuthority()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed);
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(seed.TenantId);
        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(seed.UserId);
        user.IsAuthenticated.Returns(true);
        var sessions = new StorageUploadSessionRepository(context);
        var resolver = new AuthorizationResourceContextResolver(tenantContext: tenant, storageUploadSessionRepository: sessions);
        using var content = new MemoryStream(Pdf);
        var command = new FinalizeStorageUploadSessionCommand { UploadSessionId = session.Id, Content = content };
        var bound = await resolver.ResolveAsync(command, ResourceKinds.StorageObject, AuthorizationActions.Create,
            session.Id.ToString("D"), null, default);
        await Assert.That(bound.Facts is EventResourceUploadAuthorizationFacts { ResourceId: var target } && target == seed.ResourceId).IsTrue();
        using var meters = new TestMeterFactory();
        using var metrics = new BusinessMetrics(meters);
        var policy = Substitute.For<IStoragePolicyResolver>();
        var handler = new FinalizeStorageUploadSessionCommandHandler(Substitute.For<IFileStorageProviderResolver>(), policy,
            sessions, new StorageUsageCounterRepository(context), new StorageObjectRepository(context),
            new PrivacyErasureStateRepository(context), tenant, user, new EfCoreUnitOfWork(context), metrics, workflow);
        var result = await handler.ExecuteAsync(command, default);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var read = database.CreateContext();
        var resource = await read.EventResources.SingleAsync(value => value.Id == seed.ResourceId);
        await Assert.That(resource.StorageObjectId).IsEqualTo(result.Id!.StorageObjectId);
        var pending = (await workflow.ReserveAsync(seed.ResourceId, Intent(resource.ConcurrencyStamp), default)).Id!;
        var cancelCommand = new CancelStorageUploadSessionCommand { UploadSessionId = pending.Id, TenantId = seed.TenantId };
        bound = await resolver.ResolveAsync(cancelCommand, ResourceKinds.StorageObject, AuthorizationActions.Delete,
            pending.Id.ToString("D"), new StorageObjectCollectionAuthorizationFacts(seed.TenantId), default);
        await Assert.That(bound.Facts is EventResourceUploadAuthorizationFacts).IsTrue();
        var cancel = new CancelStorageUploadSessionCommandHandler(policy, sessions, new StorageUsageCounterRepository(context),
            tenant, user, new EfCoreUnitOfWork(context), metrics, workflow);
        await Assert.That((await cancel.ExecuteAsync(cancelCommand, default)).Id!.Status).IsEqualTo(StorageUploadSessionStates.Canceled);
    }

    [Test]
    public async Task FinalizationAndReplayProduceOneAttachmentAuditAndQuotaCharge()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed);
        var reservation = await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
        var result = await FinalizeAsync(workflow, reservation.Id!.Id);
        var replay = await FinalizeAsync(workflow, reservation.Id.Id);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id!.StorageObjectId).IsEqualTo(result.Id!.StorageObjectId);
        await using var verify = database.CreateContext();
        var resource = await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId);
        var stored = await verify.StorageObjects.SingleAsync(value => value.Id == resource.StorageObjectId);
        await Assert.That(stored.HasBoundDocumentInspection).IsTrue();
        await Assert.That(stored.DocumentSafetyState).IsEqualTo(StorageDocumentSafetyStates.Unscanned);
        await Assert.That(stored.OwningResourceId).IsEqualTo(seed.ResourceId);
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == seed.ResourceId
            && value.Action == EventResourceAuditAction.ConfigureDelivery)).IsEqualTo(1);
        var quota = await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId);
        await Assert.That(quota.ReservedBytes).IsEqualTo(0);
        await Assert.That(quota.UsedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.ObjectCount).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulReplacementRetiresOldObjectAndOldReplayConflicts()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed);
        var first = await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
        var attached = await FinalizeAsync(workflow, first.Id!.Id);
        await Assert.That(attached.IsSuccess).IsTrue();
        Guid version;
        await using (var read = database.CreateContext())
            version = (await read.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).ConcurrencyStamp;
        var second = await workflow.ReserveAsync(seed.ResourceId, Intent(version), default);
        var replacement = await FinalizeAsync(workflow, second.Id!.Id);
        await Assert.That(replacement.IsSuccess).IsTrue();
        await Assert.That((await FinalizeAsync(workflow, first.Id.Id)).IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        var old = await verify.StorageObjects.SingleAsync(value => value.Id == attached.Id!.StorageObjectId);
        await Assert.That(old.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId)
            .IsEqualTo(replacement.Id!.StorageObjectId);
    }

    [Test]
    public async Task InvalidReplacementBytesPreserveExistingAttachment()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed);
        var first = await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
        var attached = await FinalizeAsync(workflow, first.Id!.Id);
        Guid version;
        await using (var read = database.CreateContext())
            version = (await read.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).ConcurrencyStamp;
        var second = await workflow.ReserveAsync(seed.ResourceId, Intent(version), default);
        using var content = new MemoryStream(new byte[Pdf.Length]);
        var rejected = await workflow.FinalizeAsync(new() { UploadSessionId = second.Id!.Id, Content = content }, default);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(content.CanRead).IsTrue();
        await using var verify = database.CreateContext();
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId)
            .IsEqualTo(attached.Id!.StorageObjectId);
        await Assert.That((await verify.StorageObjects.SingleAsync(value => value.Id == attached.Id.StorageObjectId)).LifecycleState)
            .IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    public async Task CompetingFinalizationsHaveExactlyOneWinner()
    {
        var seed = await SeedAsync();
        Guid firstId, secondId;
        await using (var reserve = database.CreateContext())
        {
            var workflow = Workflow(reserve, seed);
            firstId = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!.Id;
            secondId = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!.Id;
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstContext = database.CreateContext();
        var firstWorkflow = Workflow(firstContext, seed, async () =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
        });
        var first = FinalizeAsync(firstWorkflow, firstId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var secondContext = database.CreateContext();
        var second = await FinalizeAsync(Workflow(secondContext, seed), secondId);
        release.SetResult();
        var loser = await first.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(loser.IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == seed.ResourceId)).IsEqualTo(1);
        var quota = await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId);
        await Assert.That(quota.UsedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.ReservedBytes).IsEqualTo(0);
        await Assert.That(await verify.StorageObjects.CountAsync(value => value.OwningResourceId == seed.ResourceId
            && value.LifecycleState == StorageObjectLifecycleStates.DeleteRequested)).IsEqualTo(1);
    }

    [Test]
    public async Task RevocationDuringProviderWritePreventsAttachmentAndReplay()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed, async () =>
        {
            await using var revoke = database.CreateContext();
            (await revoke.Events.SingleAsync(value => value.Id == seed.EventId)).OrganizerActorId = null;
            await revoke.SaveChangesAsync();
        });
        var reservation = await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
        await Assert.That((await FinalizeAsync(workflow, reservation.Id!.Id)).IsSuccess).IsFalse();
        await Assert.That((await FinalizeAsync(workflow, reservation.Id.Id)).IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId).IsNull();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == seed.ResourceId)).IsFalse();
    }

    [Test]
    public async Task FinalizedReplayRechecksRevokedAuthority()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed);
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await Assert.That((await FinalizeAsync(workflow, session.Id)).IsSuccess).IsTrue();
        await using (var revoke = database.CreateContext())
        {
            (await revoke.Events.SingleAsync(value => value.Id == seed.EventId)).OrganizerActorId = null;
            await revoke.SaveChangesAsync();
        }
        await Assert.That((await FinalizeAsync(workflow, session.Id)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task CancellationDuringProviderWriteReleasesReservationOnceAndNeverAttaches()
    {
        var seed = await SeedAsync();
        Guid id = Guid.Empty;
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed, async () =>
        {
            await using var cancel = database.CreateContext();
            var cancellation = Workflow(cancel, seed);
            await Assert.That((await cancellation.CancelAsync(id, default)).IsSuccess).IsTrue();
            await Assert.That((await cancellation.CancelAsync(id, default)).IsSuccess).IsTrue();
        });
        id = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!.Id;
        await Assert.That((await FinalizeAsync(workflow, id)).IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        var quota = await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId);
        await Assert.That(quota.ReservedBytes).IsEqualTo(0);
        await Assert.That(quota.UsedBytes).IsEqualTo(0);
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId).IsNull();
    }

    [Test]
    public async Task ProviderSuccessAuditFailureRollsBackAttachmentAndLeavesReconciliationIdentity()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext(new AuditFailure());
        var workflow = Workflow(context, seed);
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await Assert.That(async () => await FinalizeAsync(workflow, session.Id)).Throws<DbUpdateException>();
        await using var verify = database.CreateContext();
        var saved = await verify.StorageUploadSessions.SingleAsync(value => value.Id == session.Id);
        await Assert.That(saved.Status).IsEqualTo(StorageUploadSessionStates.Uploading);
        await Assert.That(saved.ObjectKey).IsNotNull();
        await Assert.That(saved.StorageObjectId).IsNotNull();
        var candidates = await new StorageObjectRepository(verify).ListDeleteEligibleForReconciliationAsync(Now.AddYears(1), 500, default);
        await Assert.That(candidates.Any(value => value.Id == saved.StorageObjectId && value.ObjectKey == saved.ObjectKey)).IsTrue();
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId).IsNull();
        var quota = await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId);
        await Assert.That(quota.ReservedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.UsedBytes).IsEqualTo(0);
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == seed.ResourceId)).IsFalse();
    }

    [Test]
    public async Task PersistedResourceShapeAndCrossTenantSessionObjectAreRejected()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var session = (await Workflow(context, seed).ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        var saved = await context.StorageUploadSessions.SingleAsync(value => value.Id == session.Id);
        saved.Visibility = StorageObjectVisibilities.PublicImage;
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
        context.ChangeTracker.Clear();
        saved = await context.StorageUploadSessions.SingleAsync(value => value.Id == session.Id);
        saved.StorageObjectId = seed.ForeignObjectId;
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task PolicyTighteningDuringWriteDeniesTheAttachment()
    {
        var seed = await SeedAsync();
        var policy = EventResourceGovernancePolicy.Default(long.MaxValue);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed, () =>
        {
            policy = EventResourceGovernancePolicy.Default(1);
            return Task.CompletedTask;
        }, governancePolicy: () => policy);
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await Assert.That((await FinalizeAsync(workflow, session.Id)).IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId).IsNull();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == seed.ResourceId)).IsFalse();
    }

    [Test]
    public async Task ProviderChecksumMustMatchTheInspectedBytes()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed, corruptChecksum: true);
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await Assert.That((await FinalizeAsync(workflow, session.Id)).FailureCode).IsEqualTo(FailureCodes.StorageUploadWriteFailed);
        await using var verify = database.CreateContext();
        var staged = await verify.StorageObjects.SingleAsync(value => value.OwningResourceId == seed.ResourceId);
        await Assert.That(staged.DocumentSafetyState).IsEqualTo(StorageDocumentSafetyStates.Unavailable);
        await Assert.That(staged.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That((await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId)).ReservedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SessionOwnerAndCurrentTenantAreRequiredEvenForManagementAuthority()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var session = (await Workflow(context, seed).ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await using var foreign = database.CreateContext();
        var differentSubject = Workflow(foreign, seed with { UserId = Guid.CreateVersion7() });
        await Assert.That((await FinalizeAsync(differentSubject, session.Id)).IsSuccess).IsFalse();
        await Assert.That((await differentSubject.CancelAsync(session.Id, default)).IsSuccess).IsFalse();
        var differentTenant = Workflow(foreign, seed with { TenantId = Guid.CreateVersion7() });
        await Assert.That((await FinalizeAsync(differentTenant, session.Id)).IsSuccess).IsFalse();
        await Assert.That((await differentTenant.CancelAsync(session.Id, default)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task PrivacyFenceCommittedDuringWriteBlocksAttachmentAndFurtherReservation()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed, async () =>
        {
            await using var fence = database.CreateContext();
            var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User, seed.UserId,
                Enum.GetValues<PrivacyErasureReasonCode>()[0], 1, Now, Now);
            fence.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(intent, 1, RandomNumberGenerator.GetBytes(32), Now.AddHours(1), Now));
            await fence.SaveChangesAsync();
        });
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await Assert.That((await FinalizeAsync(workflow, session.Id)).FailureCode).IsEqualTo("privacy_erasure_fenced");
        await Assert.That((await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).FailureCode).IsEqualTo("privacy_erasure_fenced");
        await using var verify = database.CreateContext();
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId).IsNull();
    }

    [Test]
    public async Task ResourceDeletedDuringWriteCannotBeResurrected()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, seed, async () =>
        {
            await using var deleting = database.CreateContext();
            var resource = await deleting.EventResources.SingleAsync(value => value.Id == seed.ResourceId);
            resource.Delete(resource.ConcurrencyStamp, seed.UserId, Now);
            await deleting.SaveChangesAsync();
        });
        var session = (await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default)).Id!;
        await Assert.That((await FinalizeAsync(workflow, session.Id)).IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.AnyAsync(value => value.Id == seed.ResourceId)).IsFalse();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == seed.ResourceId)).IsFalse();
    }

    [Test]
    public async Task QuotaDenialUsesExistingStructuredContractWithoutReservingBytes()
    {
        var seed = await SeedAsync();
        await using var context = database.CreateContext();
        var result = await Workflow(context, seed, quotaBytes: Pdf.Length - 1)
            .ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded!.Attempted).IsEqualTo(Pdf.Length);
        await using var verify = database.CreateContext();
        await Assert.That(await verify.StorageUploadSessions.AnyAsync(value => value.OwningResourceId == seed.ResourceId)).IsFalse();
        await Assert.That((await verify.StorageUsageCounters.SingleAsync(value => value.TenantId == seed.TenantId)).ReservedBytes).IsEqualTo(0);
    }

    private async Task<Seed> SeedAsync()
    {
        await using var seeder = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeder.SeedScopeAsync();
        await using var context = database.CreateContext();
        var actor = (await context.Actors.SingleAsync(value => value.Id == scope.ActorId)).UserId!.Value;
        (await context.Events.SingleAsync(value => value.Id == scope.EventAId)).OrganizerActorId = scope.ActorId;
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, UserId = actor, User = null!,
            ActorId = scope.ActorId, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
        });
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        context.EventResources.Add(resource);
        await context.SaveChangesAsync();
        return new(scope.TenantAId, scope.EventAId, actor, resource.Id, resource.ConcurrencyStamp, scope.StorageBId);
    }

    private EventResourceFileUploadWorkflow Workflow(ExploreDbContext context, Seed seed, Func<Task>? beforeWrite = null,
        bool corruptChecksum = false, Func<EventResourceGovernancePolicy>? governancePolicy = null, long quotaBytes = 10_000_000)
    {
        var repository = new EventResourceRepository(context);
        var unit = new EfCoreUnitOfWork(context);
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(seed.TenantId, Arg.Any<CancellationToken>()).Returns(_ => governancePolicy?.Invoke() ?? EventResourceGovernancePolicy.Default(long.MaxValue));
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(seed.TenantId, Arg.Any<CancellationToken>()).Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        var authorityProvider = Substitute.For<IEventResourceAuthorizationProvider>();
        authorityProvider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>()).Returns(EventResourceProviderDecision.Allow);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(seed.TenantId);
        var user = Substitute.For<ICurrentUserService>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(seed.UserId);
        var policy = Substitute.For<IStoragePolicyResolver>();
        policy.ResolveAsync(seed.TenantId, Arg.Any<StoragePolicyIntent>(), Arg.Any<CancellationToken>()).Returns(
            new ResolvedStoragePolicy(seed.TenantId, StorageProviders.Local, 1_000_000, quotaBytes, 1_000_000,
                false, true, SettingSource.SystemDefault, SettingSource.SystemDefault, SettingSource.SystemDefault));
        var provider = Substitute.For<IFileStorageProvider>();
        provider.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            var input = call.Arg<FileStorageWriteInput>() ?? throw new InvalidOperationException("A provider write requires content.");
            await using (var verifyStage = database.CreateContext())
                await Assert.That(await verifyStage.StorageObjects.AnyAsync(value => value.ObjectKey == input.ObjectKey
                    && value.LifecycleState == StorageObjectLifecycleStates.DeleteRequested)).IsTrue();
            if (beforeWrite is not null) await beforeWrite();
            using var buffer = new MemoryStream();
            await input.Content.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey!, bytes.Length, input.ContentType,
                corruptChecksum ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(bytes)));
        });
        var providers = Substitute.For<IFileStorageProviderResolver>();
        providers.GetRequired(StorageProviders.Local).Returns(provider);
        var authority = new EventResourceAuthorityOrchestrator(unit,
            new EventResourceAuthoritySnapshotReader(repository, new EventAuthoritySnapshotService(context), governance),
            routes, authorityProvider, new Clock());
        return new(repository, new StorageUploadSessionRepository(context), new StorageObjectRepository(context),
            new StorageUsageCounterRepository(context), new PrivacyErasureStateRepository(context), policy, providers, unit,
            authority, tenant, user, Substitute.For<IMachinePrincipalAccessor>(), new Clock());
    }

    private static CreateEventResourceUploadSessionDto Intent(Guid version) => new()
    {
        ExpectedVersion = version, ExpectedSizeBytes = Pdf.Length, ContentType = "application/pdf",
        SafeDisplayName = "resource.pdf", Extension = "pdf", IdempotencyKey = Guid.CreateVersion7().ToString("N")
    };

    private static async Task<BaseCommandResponse<Explore.Application.DTOs.StorageObject.StorageUploadSessionDto>> FinalizeAsync(
        EventResourceFileUploadWorkflow workflow, Guid id)
    {
        using var content = new MemoryStream(Pdf);
        return await workflow.FinalizeAsync(new FinalizeStorageUploadSessionCommand
        { UploadSessionId = id, Content = content, ContentLength = Pdf.Length, ContentType = "application/pdf" }, default);
    }

    private sealed record Seed(Guid TenantId, Guid EventId, Guid UserId, Guid ResourceId, Guid Version, Guid ForeignObjectId);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class AuditFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"ie_event_resource_audit_entries\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected audit storage failure.");
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Current-model SQLite schema until the parent generates forward migrations; existing seeds and context infrastructure.</summary>
    public sealed class Database : IAsyncInitializer, IAsyncDisposable
    {
        private SqliteConnection _connection = null!;
        private DbContextOptions<ExploreDbContext> _options = null!;
        public async Task InitializeAsync()
        {
            _connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            _options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(_connection.ConnectionString)
                .UseSnakeCaseNamingConvention().Options;
            await using var schema = new ExploreDbContext(_options);
            await schema.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(schema);
            _options = TestDbContextOptions.Create(_options).UseModel(schema.Model).Options;
        }
        public ExploreDbContext CreateContext(params IInterceptor[] interceptors)
        {
            var options = TestDbContextOptions.Create(_options).AddInterceptors(interceptors).Options;
            var context = new ExploreDbContext(options);
            context.EnableTenantFilterBypass("Resource upload invariant test.");
            return context;
        }
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
