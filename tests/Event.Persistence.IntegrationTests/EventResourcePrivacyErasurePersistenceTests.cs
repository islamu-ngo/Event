using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourceFileUploadTests.Database>]
public sealed class EventResourcePrivacyErasurePersistenceTests(
    EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SubjectErasurePreservesSharedResourceBytesAndOrdinaryFileDeletion()
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        EventResourcePersistenceTests.ResourceScope scope = await seeds.SeedScopeAsync();
        Guid subjectId;
        Guid otherId = Guid.CreateVersion7();
        Guid resourceId = Guid.CreateVersion7();
        Guid legacyResourceId = Guid.CreateVersion7();
        Guid resourceStorageId = Guid.CreateVersion7();
        Guid legacyStorageId = Guid.CreateVersion7();
        Guid ordinaryStorageId = Guid.CreateVersion7();
        Guid resourceSessionId = Guid.CreateVersion7();
        Guid ordinarySessionId = Guid.CreateVersion7();
        Guid sharedStorageStamp = Guid.Empty;
        const string resourceKey = "resources/shared.pdf";
        const string legacyKey = "resources/legacy-shared.pdf";
        const string ordinaryKey = "users/ordinary.pdf";
        const string checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        await using (var seed = database.CreateContext())
        {
            subjectId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            FileType fileType = await seed.FileTypes.SingleAsync(type => type.Id == (int)FileTypeEnum.Document);

            EventResource resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            resourceId = resource.Id;
            var resourceStorage = Storage(resourceStorageId, scope.TenantAId, fileType, resourceKey,
                StorageObjectPurposes.EventResource, StorageOwningResourceKinds.EventResource, resourceId,
                scope.ActorId, subjectId, otherId, checksum);
            resourceStorage.RecordEventResourceInspection(resourceStorageId, checksum);
            resourceStorage.StorageProviderBindingId = binding.Id;
            resource.SetStoredFile(resourceStorageId, resource.ConcurrencyStamp, subjectId, Now);

            EventResource legacyResource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
            legacyResourceId = legacyResource.Id;
            var legacyStorage = Storage(legacyStorageId, scope.TenantAId, fileType, legacyKey,
                StorageObjectPurposes.Attachment, null, null, scope.ActorId, subjectId, otherId, checksum);
            legacyResource.SetStoredFile(legacyStorageId, legacyResource.ConcurrencyStamp, subjectId, Now);

            var ordinaryStorage = Storage(ordinaryStorageId, scope.TenantAId, fileType, ordinaryKey,
                StorageObjectPurposes.Attachment, null, null, scope.ActorId, subjectId, otherId, checksum);

            seed.AddRange(binding, resource, legacyResource, resourceStorage, legacyStorage, ordinaryStorage);
            await seed.SaveChangesAsync();
            sharedStorageStamp = resourceStorage.ConcurrencyStamp;

            var resourceSession = Session(resourceSessionId, scope.TenantAId, subjectId, resourceKey,
                StorageObjectPurposes.EventResource, StorageOwningResourceKinds.EventResource, resourceId);
            resourceSession.BindEventResourceVersion(resource.ConcurrencyStamp);
            resourceSession.StorageProviderBindingId = binding.Id;
            resourceSession.MarkUploading(Now);
            resourceSession.StageEventResourceObject(resourceStorageId);
            resourceSession.RecordProducerSettlement(resourceStorageId, binding.Id, resourceKey, null);
            resourceSession.Finalize(resourceStorageId, resourceKey, checksum, Now);
            resourceSession.RecordFinalizedResourceVersion(resource.ConcurrencyStamp);
            var ordinarySession = Session(ordinarySessionId, scope.TenantAId, subjectId, ordinaryKey,
                StorageObjectPurposes.Attachment, null, null);
            seed.AddRange(resourceSession, ordinarySession);
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var repository = new UserLocationPrivacyErasureRepository(context);
            IReadOnlyList<PrivacyErasureProviderCandidate> candidates =
                await repository.GetProviderCandidatesAsync(subjectId, default);

            await Assert.That(candidates.Any(candidate => candidate.Locator == resourceKey)).IsFalse();
            await Assert.That(candidates.Any(candidate => candidate.Locator == legacyKey)).IsFalse();
            await Assert.That(candidates.Any(candidate => candidate.Locator == ordinaryKey)).IsTrue();

            await repository.EraseProviderBackedLocalUserMetadataAsync(subjectId, default);
            await repository.AnonymizeRetainedAuditEvidenceAsync(subjectId, default);
        }

        await using var verification = database.CreateContext();
        StorageObject shared = await verification.StorageObjects.AsNoTracking()
            .SingleAsync(storage => storage.Id == resourceStorageId);
        StorageObject legacyShared = await verification.StorageObjects.AsNoTracking()
            .SingleAsync(storage => storage.Id == legacyStorageId);
        StorageObject ordinary = await verification.StorageObjects.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(storage => storage.Id == ordinaryStorageId);
        bool resourceUploadExists = await verification.StorageUploadSessions.AsNoTracking()
            .AnyAsync(session => session.Id == resourceSessionId);
        StorageUploadSession ordinaryUpload = await verification.StorageUploadSessions.AsNoTracking()
            .SingleAsync(session => session.Id == ordinarySessionId);

        await Assert.That(shared.ObjectKey).IsEqualTo(resourceKey);
        await Assert.That(shared.Uri).IsEqualTo($"private://{resourceStorageId:N}");
        await Assert.That(shared.Sha256Checksum).IsEqualTo(checksum);
        await Assert.That(shared.InspectedObjectId).IsEqualTo(resourceStorageId);
        await Assert.That(shared.InspectedSha256Checksum).IsEqualTo(checksum);
        await Assert.That(shared.ActorId).IsNull();
        await Assert.That(shared.CreatedBy).IsNull();
        await Assert.That(shared.UpdatedBy).IsEqualTo(otherId);
        await Assert.That(shared.DeletedBy).IsNull();
        await Assert.That(shared.QuarantinedBy).IsNull();
        await Assert.That(shared.ConcurrencyStamp).IsNotEqualTo(sharedStorageStamp);
        await Assert.That(shared.IsDeleted).IsFalse();
        await Assert.That(legacyShared.ObjectKey).IsEqualTo(legacyKey);
        await Assert.That(legacyShared.IsDeleted).IsFalse();
        await Assert.That(resourceUploadExists).IsFalse();

        await Assert.That(ordinary.ObjectKey).IsNull();
        await Assert.That(ordinary.IsDeleted).IsTrue();
        await Assert.That(ordinary.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Deleted);
        await Assert.That(ordinaryUpload.ObjectKey).IsNull();
        await Assert.That(ordinaryUpload.UserId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubjectErasureTransfersUnfinishedProducerAuthorityBeforeRemovingAttribution(bool producerStarted)
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeds.SeedScopeAsync();
        Guid sessionId = Guid.CreateVersion7(), objectId = Guid.CreateVersion7();
        Guid subjectId;
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        string key = $"objects/{objectId:N}";
        await using (var seed = database.CreateContext())
        {
            subjectId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
            seed.AddRange(binding, resource);
            await seed.SaveChangesAsync();
            var session = Session(sessionId, scope.TenantAId, subjectId, key,
                StorageObjectPurposes.EventResource, StorageOwningResourceKinds.EventResource, resource.Id);
            session.ObjectKey = null;
            session.ReservedBytes = 64;
            session.BindEventResourceVersion(resource.ConcurrencyStamp);
            seed.Add(new StorageUsageCounter
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Provider = StorageProviders.Local,
                ReservedBytes = 64
            });
            if (producerStarted)
            {
                var fileType = await seed.FileTypes.SingleAsync(type => type.Id == (int)FileTypeEnum.Document);
                var source = Storage(objectId, scope.TenantAId, fileType, key,
                    StorageObjectPurposes.EventResource, StorageOwningResourceKinds.EventResource, resource.Id,
                    scope.ActorId, subjectId, subjectId, "staged");
                source.LifecycleState = StorageObjectLifecycleStates.DeleteRequested;
                source.StorageProviderBindingId = binding.Id;
                seed.Add(source);
                session.ReserveObjectKey(key);
                session.MarkUploading(Now);
                session.StorageProviderBindingId = binding.Id;
                session.StageEventResourceObject(objectId);
            }
            seed.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var repository = new UserLocationPrivacyErasureRepository(context);
            var candidates = await repository.GetProviderCandidatesAsync(subjectId, default);
            await Assert.That(candidates.Any(candidate => candidate.Locator == key)).IsFalse();
            await repository.EraseProviderBackedLocalUserMetadataAsync(subjectId, default);
            await transaction.CommitAsync();
        }

        await using var verification = database.CreateContext();
        await Assert.That(await verification.StorageUploadSessions.IgnoreQueryFilters()
            .AnyAsync(session => session.UserId == subjectId)).IsFalse();
        var counter = await verification.StorageUsageCounters.SingleAsync(item => item.TenantId == scope.TenantAId);
        await Assert.That(counter.ReservedBytes).IsEqualTo(0);
        var work = await verification.StorageObjectDeletionTombstones.SingleOrDefaultAsync(item => item.Id == objectId);
        await Assert.That(work is not null).IsEqualTo(producerStarted);
        if (producerStarted)
        {
            await Assert.That(work!.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
            await Assert.That(work.ProviderBindingId).IsEqualTo(binding.Id);
            await Assert.That(work.ObjectKey).IsEqualTo(key);
        }
    }

    private static StorageObject Storage(Guid id, Guid tenantId, FileType fileType, string objectKey,
        string purpose, string? ownerKind, Guid? ownerId, Guid actorId, Guid subjectId, Guid otherId,
        string checksum) => new()
    {
        Id = id,
        TenantId = tenantId,
        Tenant = null!,
        FileTypeId = fileType.Id,
        FileType = fileType,
        Uri = $"private://{id:N}",
        ObjectKey = objectKey,
        Provider = StorageProviders.Local,
        FullName = $"{id:N}.pdf",
        SafeDisplayName = "shared.pdf",
        Extension = "pdf",
        ContentType = "application/pdf",
        Sha256Checksum = checksum,
        Size = 64,
        Visibility = StorageObjectVisibilities.PrivateOwner,
        Purpose = purpose,
        LifecycleState = StorageObjectLifecycleStates.Active,
        OwningResourceKind = ownerKind,
        OwningResourceId = ownerId,
        ActorId = actorId,
        CreatedAt = Now,
        CreatedBy = subjectId,
        UpdatedAt = Now,
        UpdatedBy = otherId,
        DeletedBy = subjectId,
        QuarantinedBy = subjectId,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static StorageUploadSession Session(Guid id, Guid tenantId, Guid subjectId, string objectKey,
        string purpose, string? ownerKind, Guid? ownerId) => new()
    {
        Id = id,
        TenantId = tenantId,
        UserId = subjectId,
        Provider = StorageProviders.Local,
        ExpectedSizeBytes = 64,
        ContentType = "application/pdf",
        SafeDisplayName = "upload.pdf",
        Extension = "pdf",
        Purpose = purpose,
        Visibility = StorageObjectVisibilities.PrivateOwner,
        OwningResourceKind = ownerKind,
        OwningResourceId = ownerId,
        Status = StorageUploadSessionStates.Reserved,
        ObjectKey = objectKey,
        ExpiresAt = Now.AddHours(1),
        CreatedAt = Now,
        CreatedBy = subjectId,
        UpdatedBy = subjectId,
        ConcurrencyStamp = Guid.CreateVersion7()
    };
}
