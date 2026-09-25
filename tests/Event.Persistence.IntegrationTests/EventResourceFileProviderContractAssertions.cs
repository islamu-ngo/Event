using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

internal static class EventResourceFileProviderContractAssertions
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Pdf = "%PDF-1.7\nprovider contract\n%%EOF"u8.ToArray();
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task AssertRevocationPrecedesFinalSnapshotAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await AssertRevocationPrecedesFinalSnapshotAsync(() => fixture.CreateSystemContext());
    }

    public static async Task AssertFinalReadDoesNotReuseTrackedAuthorityAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        PublishedFile seed = await SeedPublishedFileAsync(
            () => fixture.CreateSystemContext(), EventResourceAudienceKindEnum.Public);
        await SetUnscannedPolicyAsync(() => fixture.CreateSystemContext(), allowed: true);

        await using var read = fixture.CreateSystemContext();
        await using var writer = fixture.CreateSystemContext();
        EventResource trackedResource = await read.EventResources.SingleAsync(row => row.Id == seed.ResourceId);
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var opened = new TrackingStream(Pdf);
        EventResourceContentService service = ContentService(read, seed, async cancellationToken =>
        {
            providerEntered.TrySetResult();
            await releaseProvider.Task.WaitAsync(cancellationToken);
            return new FileStorageReadResult(opened, "application/pdf", Pdf.Length, null);
        });
        using var deadline = new CancellationTokenSource(Timeout);
        Task<EventResourceAuthorityResult> pending = service.PrepareAsync(
            seed.ResourceId, new DateTimeOffset(Now.AddMinutes(1)), deadline.Token);
        try
        {
            await providerEntered.Task.WaitAsync(Timeout, deadline.Token);
            await Assert.That(read.Database.CurrentTransaction).IsNull();
            EventResource current = await writer.EventResources.SingleAsync(
                row => row.Id == seed.ResourceId, deadline.Token);
            current.Withdraw(current.ConcurrencyStamp, seed.SubjectUserId, Now.AddSeconds(30));
            await writer.SaveChangesAsync(deadline.Token);
        }
        finally
        {
            releaseProvider.TrySetResult();
        }

        await using EventResourceAuthorityResult result = await pending.WaitAsync(Timeout, deadline.Token);
        await Assert.That(trackedResource.PublicationStateId)
            .IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(result.Lease).IsNull();
        await Assert.That(opened.WasDisposed).IsTrue();
    }

    public static async Task AssertAttachmentHasOneOwnerAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await AssertAttachmentHasOneOwnerAsync(() => fixture.CreateSystemContext());
    }

    public static async Task AssertPolicyTighteningDeniesNewAccessAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await AssertPolicyTighteningDeniesNewAccessAsync(() => fixture.CreateSystemContext());
    }

    internal static async Task AssertRevocationPrecedesFinalSnapshotAsync(
        Func<ExploreDbContext> contextFactory)
    {
        PublishedFile seed = await SeedPublishedFileAsync(contextFactory,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        await SetUnscannedPolicyAsync(contextFactory, allowed: true);

        await using var read = contextFactory();
        await using var writer = contextFactory();
        TenantUser trackedMembership = await read.TenantUsers.SingleAsync(row =>
            row.TenantId == seed.TenantId && row.UserId == seed.SubjectUserId);
        bool independentConnections = !ReferenceEquals(
            read.Database.GetDbConnection(), writer.Database.GetDbConnection());
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var opened = new TrackingStream(Pdf);
        EventResourceContentService service = ContentService(read, seed, async cancellationToken =>
        {
            providerEntered.TrySetResult();
            await releaseProvider.Task.WaitAsync(cancellationToken);
            return new FileStorageReadResult(opened, "application/pdf", Pdf.Length, null);
        });
        using var deadline = new CancellationTokenSource(Timeout);
        Task<EventResourceAuthorityResult> pending = service.PrepareAsync(
            seed.ResourceId, new DateTimeOffset(Now.AddMinutes(1)), deadline.Token);
        try
        {
            await providerEntered.Task.WaitAsync(Timeout, deadline.Token);
            await Assert.That(read.Database.CurrentTransaction).IsNull();
            TenantUser membership = await writer.TenantUsers.SingleAsync(row =>
                row.TenantId == seed.TenantId && row.UserId == seed.SubjectUserId, deadline.Token);
            membership.StatusId = (int)TenantUserStatusEnum.Suspended;
            await writer.SaveChangesAsync(deadline.Token);
        }
        finally
        {
            releaseProvider.TrySetResult();
        }

        await using EventResourceAuthorityResult result = await pending.WaitAsync(Timeout, deadline.Token);
        await Assert.That(independentConnections).IsTrue();
        await Assert.That(trackedMembership.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(result.Lease).IsNull();
        await Assert.That(opened.WasDisposed).IsTrue();
        await SetUnscannedPolicyAsync(contextFactory, allowed: false);
    }

    internal static async Task AssertAttachmentHasOneOwnerAsync(
        Func<ExploreDbContext> contextFactory)
    {
        UploadSeed seed = await SeedUploadAsync(contextFactory);
        Guid firstSessionId;
        Guid secondSessionId;
        await using (ExploreDbContext reserve = contextFactory())
        {
            EventResourceFileUploadWorkflow workflow = UploadWorkflow(reserve, contextFactory, seed);
            var firstReservation = await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
            if (firstReservation.Id is null)
                throw new InvalidOperationException($"First upload reservation failed: {firstReservation.FailureCode}.");
            firstSessionId = firstReservation.Id.Id;
            var secondReservation = await workflow.ReserveAsync(seed.ResourceId, Intent(seed.Version), default);
            if (secondReservation.Id is null)
                throw new InvalidOperationException($"Second upload reservation failed: {secondReservation.FailureCode}.");
            secondSessionId = secondReservation.Id.Id;
        }

        var firstProviderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(Timeout);
        await using var firstContext = contextFactory();
        EventResourceFileUploadWorkflow firstWorkflow = UploadWorkflow(firstContext, contextFactory, seed,
            async cancellationToken =>
            {
                firstProviderEntered.TrySetResult();
                await releaseFirstProvider.Task.WaitAsync(cancellationToken);
            });
        Task<Explore.Application.Responses.BaseCommandResponse<Explore.Application.DTOs.StorageObject.StorageUploadSessionDto>> first =
            FinalizeAsync(firstWorkflow, firstSessionId, deadline.Token);
        await firstProviderEntered.Task.WaitAsync(Timeout, deadline.Token);

        Explore.Application.Responses.BaseCommandResponse<Explore.Application.DTOs.StorageObject.StorageUploadSessionDto> winner;
        await using (ExploreDbContext secondContext = contextFactory())
            winner = await FinalizeAsync(UploadWorkflow(secondContext, contextFactory, seed), secondSessionId, deadline.Token);
        releaseFirstProvider.TrySetResult();
        var loser = await first.WaitAsync(Timeout, deadline.Token);

        await Assert.That(winner.IsSuccess).IsTrue();
        await Assert.That(loser.IsSuccess).IsFalse();
        await Assert.That((await FinalizeAsync(firstWorkflow, firstSessionId, deadline.Token)).IsSuccess).IsFalse();
        if (winner.Id?.StorageObjectId is not { } winnerObjectId)
            throw new InvalidOperationException(
                $"Second finalize did not return a storage object (success={winner.IsSuccess}, code={winner.FailureCode}).");

        await using (ExploreDbContext conflictingOwner = contextFactory())
        {
            EventResource secondResource = await conflictingOwner.EventResources.SingleAsync(row => row.Id == seed.SecondResourceId,
                deadline.Token);
            secondResource.SetStoredFile(winnerObjectId, secondResource.ConcurrencyStamp, seed.UserId, Now);
            await Assert.That(() => conflictingOwner.SaveChangesAsync(deadline.Token)).Throws<DbUpdateException>();
        }

        await using ExploreDbContext verify = contextFactory();
        EventResource attached = await verify.EventResources.AsNoTracking()
            .SingleAsync(row => row.Id == seed.ResourceId, deadline.Token);
        await Assert.That(attached.StorageObjectId).IsEqualTo(winner.Id!.StorageObjectId);
        await Assert.That(await verify.EventResources.AsNoTracking()
            .CountAsync(row => row.StorageObjectId == winner.Id.StorageObjectId, deadline.Token)).IsEqualTo(1);
        await Assert.That(await verify.EventResourceAuditEntries.AsNoTracking().CountAsync(row =>
            row.EventResourceId == seed.ResourceId && row.Action == EventResourceAuditAction.ConfigureDelivery,
            deadline.Token)).IsEqualTo(1);
        StorageUsageCounter quota = await verify.StorageUsageCounters.AsNoTracking()
            .SingleAsync(row => row.TenantId == seed.TenantId, deadline.Token);
        await Assert.That(quota.ReservedBytes).IsEqualTo(0);
        await Assert.That(quota.UsedBytes).IsEqualTo(Pdf.Length);
        await Assert.That(quota.ObjectCount).IsEqualTo(1);
        await Assert.That(await verify.StorageObjects.AsNoTracking().CountAsync(row =>
            row.OwningResourceId == seed.ResourceId && row.LifecycleState == StorageObjectLifecycleStates.Active,
            deadline.Token)).IsEqualTo(1);
        await Assert.That(await verify.StorageObjects.AsNoTracking().CountAsync(row =>
            row.OwningResourceId == seed.ResourceId && row.LifecycleState == StorageObjectLifecycleStates.DeleteRequested,
            deadline.Token)).IsEqualTo(1);
    }

    internal static async Task AssertPolicyTighteningDeniesNewAccessAsync(
        Func<ExploreDbContext> contextFactory)
    {
        PublishedFile seed = await SeedPublishedFileAsync(contextFactory, EventResourceAudienceKindEnum.Public);
        await SetUnscannedPolicyAsync(contextFactory, allowed: true);
        int opens = 0;

        await using (ExploreDbContext acceptedContext = contextFactory())
        {
            EventResourceContentService acceptedService = ContentService(acceptedContext, seed, _ =>
            {
                Interlocked.Increment(ref opens);
                return Task.FromResult(new FileStorageReadResult(
                    new MemoryStream(Pdf), "application/pdf", Pdf.Length, null));
            });
            await using EventResourceAuthorityResult accepted = await acceptedService.PrepareAsync(
                seed.ResourceId, new DateTimeOffset(Now.AddMinutes(1)), default);
            await Assert.That(accepted.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
            EventResourceHeaderResult header = await Authority(acceptedContext).CompleteHeadersAsync(accepted.Lease!, default);
            await Assert.That(header.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
            await using var prepared = (EventResourcePreparedContent)header.Preparation!;
            StorageObjectContentResult content = prepared.TakeContent();
            await using (content.Content)
                await Assert.That(content.Length).IsEqualTo(Pdf.Length);
        }
        await Assert.That(opens).IsEqualTo(1);

        await SetUnscannedPolicyAsync(contextFactory, allowed: false);
        await using ExploreDbContext deniedContext = contextFactory();
        EventResourceContentService deniedService = ContentService(deniedContext, seed, _ =>
        {
            Interlocked.Increment(ref opens);
            return Task.FromResult(new FileStorageReadResult(
                new MemoryStream(Pdf), "application/pdf", Pdf.Length, null));
        });
        await using EventResourceAuthorityResult denied = await deniedService.PrepareAsync(
            seed.ResourceId, new DateTimeOffset(Now.AddMinutes(1)), default);
        await Assert.That(denied.Outcome).IsNotEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(denied.Lease).IsNull();
        await Assert.That(opens).IsEqualTo(1);
    }

    internal static async Task AssertExactBoundVersionAsync(Func<ExploreDbContext> contextFactory, bool mutateDuringOpen,
        bool mutateBinding = false)
    {
        var seed = await SeedPublishedFileAsync(contextFactory, EventResourceAudienceKindEnum.Public);
        await SetUnscannedPolicyAsync(contextFactory, allowed: true);
        Guid objectId;
        await using (var setup = contextFactory())
        {
            objectId = (await setup.EventResources.SingleAsync(value => value.Id == seed.ResourceId)).StorageObjectId!.Value;
            await setup.StorageObjects.Where(value => value.Id == objectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ProviderVersionId, "exact-version"));
        }
        using var opened = new TrackingStream(Pdf);
        await using var context = contextFactory();
        var service = ContentService(context, seed, _ => throw new InvalidOperationException("Version-aware open is required."),
            async (input, ct) =>
            {
                // Behavior depends on selecting the bound version, not on invocation counts.
                if (input.ProviderVersionId != "exact-version")
                    return new FileStorageReadResult(new MemoryStream([]), "application/pdf", 0, null, "current-version");
                if (mutateDuringOpen)
                {
                    await using var mutation = contextFactory();
                    if (mutateBinding)
                    {
                        var otherBinding = StorageProviderBinding.Local(Path.GetFullPath("other-storage-target"));
                        mutation.Set<StorageProviderBinding>().Add(otherBinding);
                        await mutation.SaveChangesAsync(ct);
                        await mutation.StorageObjects.Where(value => value.Id == objectId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.StorageProviderBindingId, otherBinding.Id), ct);
                    }
                    else
                        await mutation.StorageObjects.Where(value => value.Id == objectId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ProviderVersionId, "new-version"), ct);
                }
                return new FileStorageReadResult(opened, "application/pdf", Pdf.Length, null, "exact-version");
            });
        await using var result = await service.PrepareAsync(seed.ResourceId, new DateTimeOffset(Now.AddMinutes(1)), default);
        if (mutateDuringOpen)
        {
            await Assert.That(result.Lease).IsNull();
            await Assert.That(opened.WasDisposed).IsTrue();
        }
        else
        {
            await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
            var header = await Authority(context).CompleteHeadersAsync(result.Lease!, default);
            await Assert.That(header.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
            await using var preparation = (EventResourcePreparedContent)header.Preparation!;
            var content = preparation.TakeContent();
            await using (content.Content)
            {
                using var buffer = new MemoryStream();
                await content.Content.CopyToAsync(buffer);
                await Assert.That(buffer.ToArray().SequenceEqual(Pdf)).IsTrue();
            }
        }
        await SetUnscannedPolicyAsync(contextFactory, allowed: false);
    }

    private static async Task<PublishedFile> SeedPublishedFileAsync(
        Func<ExploreDbContext> contextFactory, EventResourceAudienceKindEnum audience)
    {
        await using var database = EventResourcePersistenceTests.TestDatabase.CreateProvider(contextFactory);
        var scope = await database.SeedScopeAsync();
        Guid subjectId = Guid.CreateVersion7();
        Guid resourceId = Guid.CreateVersion7();
        Guid storageId = Guid.CreateVersion7();
        string checksum = Convert.ToHexStringLower(SHA256.HashData(Pdf));
        await using ExploreDbContext context = contextFactory();
        Guid publisherId = (await context.Actors.Where(row => row.Id == scope.ActorId)
            .Select(row => row.UserId).SingleAsync())!.Value;
        var subject = new User
        {
            Id = subjectId,
            Pii = new UserPii
            {
                Email = $"provider-contract-{subjectId:N}@example.test", FirstName = "Resource", LastName = "Reader"
            }
        };
        context.Users.Add(subject);
        context.TenantUsers.AddRange(
            new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, UserId = publisherId,
                User = null!, ActorId = scope.ActorId, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            },
            new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, UserId = subjectId,
                User = subject, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            });
        var binding = StorageProviderBinding.Local(Path.GetFullPath("provider-contract-storage"));
        context.Set<StorageProviderBinding>().Add(binding);
        var storage = new StorageObject
        {
            StorageProviderBindingId = binding.Id,
            Id = storageId, TenantId = scope.TenantAId, Tenant = null!, FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!, Uri = $"/api/storageobject/{storageId}/content", Provider = StorageProviders.Local,
            ObjectKey = $"provider-contract/{storageId:N}.pdf", FullName = "provider-contract.pdf",
            SafeDisplayName = "provider-contract.pdf", Extension = "pdf", ContentType = "application/pdf",
            Size = Pdf.Length, Sha256Checksum = checksum, Purpose = StorageObjectPurposes.EventResource,
            Visibility = StorageObjectVisibilities.PrivateOwner, OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = resourceId, LifecycleState = StorageObjectLifecycleStates.Active
        };
        storage.RecordEventResourceInspection(storageId, checksum);
        var parent = await context.Events.SingleAsync(row => row.Id == scope.EventAId);
        if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
        parent.VisibilityTypeId = (int)VisibilityTypeEnum.Public;
        EventResource resource = EventResource.CreateDraft(resourceId, scope.TenantAId, scope.EventAId, null,
            new EventResourceMetadata
            {
                Title = "Provider contract material", Kind = (EventResourceKindEnum)1,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId, audience)], publisherId, Now);
        resource.SetStoredFile(storageId, resource.ConcurrencyStamp, publisherId, Now);
        resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published,
            false, true, null, false, new(new(Now), new(Now.AddHours(1)), null, null)),
            true, resource.ConcurrencyStamp, publisherId, Now);
        context.AddRange(storage, resource);
        await context.SaveChangesAsync();
        return new(scope.TenantAId, resourceId, subjectId);
    }

    private static async Task<UploadSeed> SeedUploadAsync(Func<ExploreDbContext> contextFactory)
    {
        await using var database = EventResourcePersistenceTests.TestDatabase.CreateProvider(contextFactory);
        var scope = await database.SeedScopeAsync();
        await using ExploreDbContext context = contextFactory();
        Guid userId = (await context.Actors.Where(row => row.Id == scope.ActorId)
            .Select(row => row.UserId).SingleAsync())!.Value;
        (await context.Events.SingleAsync(row => row.Id == scope.EventAId)).OrganizerActorId = scope.ActorId;
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, UserId = userId,
            User = null!, ActorId = scope.ActorId, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
        });
        EventResource resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        EventResource second = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        context.EventResources.AddRange(resource, second);
        await context.SaveChangesAsync();
        return new(scope.TenantAId, userId, resource.Id, second.Id, resource.ConcurrencyStamp);
    }

    private static EventResourceFileUploadWorkflow UploadWorkflow(ExploreDbContext context,
        Func<ExploreDbContext> contextFactory, UploadSeed seed, Func<CancellationToken, Task>? beforeWrite = null)
    {
        var repository = new EventResourceRepository(context);
        var unit = new EfCoreUnitOfWork(context);
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(seed.TenantId, Arg.Any<CancellationToken>())
            .Returns(EventResourceGovernancePolicy.Default(long.MaxValue));
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(seed.TenantId, Arg.Any<CancellationToken>())
            .Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        var authorization = Substitute.For<IEventResourceAuthorizationProvider>();
        authorization.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceProviderDecision.Allow);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(seed.TenantId);
        var user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(seed.UserId);
        user.IsAuthenticated.Returns(true);
        var storagePolicy = Substitute.For<IStoragePolicyResolver>();
        storagePolicy.ResolveAsync(seed.TenantId, Arg.Any<StoragePolicyIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvedStoragePolicy(seed.TenantId, StorageProviders.Local, 1_000_000, 10_000_000,
                1_000_000, false, true, SettingSource.SystemDefault, SettingSource.SystemDefault,
                SettingSource.SystemDefault));
        var provider = Substitute.For<IFileStorageProvider>();
        provider.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (beforeWrite is not null) await beforeWrite(call.ArgAt<CancellationToken>(1));
            FileStorageWriteInput input = call.ArgAt<FileStorageWriteInput>(0);
            using var buffer = new MemoryStream();
            await input.Content.CopyToAsync(buffer, call.ArgAt<CancellationToken>(1));
            byte[] bytes = buffer.ToArray();
            return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey!, bytes.Length, input.ContentType,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        });
        var providers = Substitute.For<IStorageProviderBindingService>();
        providers.CaptureAsync(StorageProviders.Local, seed.TenantId, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var binding = StorageProviderBinding.Local(Path.GetFullPath("provider-contract-storage"));
            await context.Set<StorageProviderBinding>().AddAsync(binding);
            return binding;
        });
        providers.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(provider);
        var authority = new EventResourceAuthorityOrchestrator(unit,
            new EventResourceAuthoritySnapshotReader(repository, new EventAuthoritySnapshotService(context), governance),
            routes, authorization, new ContractClock());
        return new(repository, new StorageUploadSessionRepository(context), new StorageObjectRepository(context),
            new StorageUsageCounterRepository(context), new PrivacyErasureStateRepository(context), storagePolicy,
            providers, unit, new EventResourceStorageLifecycleService(new EventResourceStorageLifecycleRepository(context),
                unit, new ContractClock()), authority, tenant, user, Substitute.For<IMachinePrincipalAccessor>(), new ContractClock());
    }

    private static EventResourceContentService ContentService(ExploreDbContext context, PublishedFile seed,
        Func<CancellationToken, Task<FileStorageReadResult>> open,
        Func<FileStorageReadInput, CancellationToken, Task<FileStorageReadResult>>? versionedOpen = null)
    {
        var provider = Substitute.For<IFileStorageProvider>();
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(call => versionedOpen is null ? open(call.ArgAt<CancellationToken>(1))
                : versionedOpen(call.ArgAt<FileStorageReadInput>(0), call.ArgAt<CancellationToken>(1)));
        var providers = Substitute.For<IStorageProviderBindingService>();
        providers.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(provider);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(seed.TenantId);
        var user = Substitute.For<ICurrentUserService>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(seed.SubjectUserId);
        return new(Authority(context), new EventResourceRepository(context), providers, tenant, user,
            Substitute.For<IMachinePrincipalAccessor>());
    }

    private static EventResourceAuthorityOrchestrator Authority(ExploreDbContext context)
    {
        var unit = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unit);
        var governance = new EventResourceGovernancePolicyReader(
            new SystemSettingRepository(context, mutationLock), new TenantSettingRepository(context, mutationLock));
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceProviderDecision.Allow);
        var repository = new EventResourceRepository(context);
        return new(unit, new EventResourceAuthoritySnapshotReader(repository,
            new EventAuthoritySnapshotService(context), governance), routes, provider, new ContractClock());
    }

    private static async Task SetUnscannedPolicyAsync(Func<ExploreDbContext> contextFactory, bool allowed)
    {
        await using ExploreDbContext context = contextFactory();
        var unit = new EfCoreUnitOfWork(context);
        var writer = new EventResourceSettingsWriter(context, new RelationalSettingMutationLock(context, unit), unit);
        EventResourceSettingsWriteResult result = await writer.ApplyAsync(
            [new(null, GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                EventResourceSettingMutationKind.SetValue, allowed ? "true" : "false")], null);
        await Assert.That(result.Success).IsTrue().Because(result.FailureCode ?? "accepted");
    }

    private static CreateEventResourceUploadSessionDto Intent(Guid version) => new()
    {
        ExpectedVersion = version, ExpectedSizeBytes = Pdf.Length, ContentType = "application/pdf",
        SafeDisplayName = "provider-contract.pdf", Extension = "pdf", IdempotencyKey = Guid.CreateVersion7().ToString("N")
    };

    private static async Task<Explore.Application.Responses.BaseCommandResponse<Explore.Application.DTOs.StorageObject.StorageUploadSessionDto>>
        FinalizeAsync(EventResourceFileUploadWorkflow workflow, Guid id, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(Pdf);
        return await workflow.FinalizeAsync(new FinalizeStorageUploadSessionCommand
        {
            UploadSessionId = id, Content = content, ContentLength = Pdf.Length, ContentType = "application/pdf"
        }, cancellationToken);
    }

    private sealed record PublishedFile(Guid TenantId, Guid ResourceId, Guid SubjectUserId);
    private sealed record UploadSeed(Guid TenantId, Guid UserId, Guid ResourceId, Guid SecondResourceId, Guid Version);
    private sealed class ContractClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}

[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
public sealed class CanonicalSqliteEventResourceFileProviderContractTests(
    EventResourceFileUploadTests.Database database)
{
    [Test]
    public Task EventResourceRevocationPrecedesFinalSnapshot() =>
        EventResourceFileProviderContractAssertions.AssertRevocationPrecedesFinalSnapshotAsync(
            () => database.CreateContext());

    [Test]
    public Task EventResourceAttachmentHasOneOwner() =>
        EventResourceFileProviderContractAssertions.AssertAttachmentHasOneOwnerAsync(
            () => database.CreateContext());

    [Test]
    public Task EventResourcePolicyTighteningDeniesNewAccess() =>
        EventResourceFileProviderContractAssertions.AssertPolicyTighteningDeniesNewAccessAsync(
            () => database.CreateContext());

    [Test]
    public Task ExactBoundVersionIsOpened() => EventResourceFileProviderContractAssertions.AssertExactBoundVersionAsync(
        () => database.CreateContext(), mutateDuringOpen: false);

    [Test]
    public Task ChangedVersionBeforeFinalAuthorityGateDisposesPreparedContent() =>
        EventResourceFileProviderContractAssertions.AssertExactBoundVersionAsync(
            () => database.CreateContext(), mutateDuringOpen: true);

    [Test]
    public Task ChangedBindingBeforeFinalAuthorityGateDisposesPreparedContent() =>
        EventResourceFileProviderContractAssertions.AssertExactBoundVersionAsync(
            () => database.CreateContext(), mutateDuringOpen: true, mutateBinding: true);
}
