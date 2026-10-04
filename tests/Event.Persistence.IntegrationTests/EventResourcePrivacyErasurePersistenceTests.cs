using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourceFileUploadTests.Database>]
public sealed class EventResourcePrivacyErasurePersistenceTests(
    EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("settled")]
    [Arguments("picture-only")]
    [Arguments("unsettled")]
    [Arguments("source-less")]
    [Arguments("source-less-retained")]
    [Arguments("missing-custody")]
    [Arguments("producer-operation")]
    [Arguments("report-evidence")]
    [Arguments("held")]
    [Arguments("other-owner")]
    [Arguments("audit-only")]
    [Arguments("missing-binding")]
    [Arguments("version-conflict")]
    [Arguments("rollback")]
    public async Task OrdinaryPrivacyErasureTransfersOnlyExactUnreferencedCustody(string scenario)
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeds.SeedScopeAsync();
        Guid objectId = Guid.CreateVersion7();
        Guid subjectId;
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        string key = $"privacy/{objectId:N}.pdf";
        bool sourceLess = scenario is "source-less" or "source-less-retained";
        await using (var seed = database.CreateContext())
        {
            subjectId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            var fileType = await seed.FileTypes.SingleAsync(type => type.Id == (int)FileTypeEnum.Document);
            var source = Storage(objectId, scope.TenantAId, fileType, key,
                StorageObjectPurposes.Attachment,
                scenario == "held" ? "registration_submission_sink" : null, null,
                scope.ActorId, subjectId, subjectId, "checksum");
            source.StorageProviderBindingId = scenario == "missing-binding" ? null : binding.Id;
            source.ProviderVersionId = "original-version";
            if (scenario == "audit-only")
            {
                source.ActorId = null;
                source.SourceUri = "https://media.example.invalid/retained.pdf";
            }
            if (scenario == "picture-only")
            {
                source.ActorId = null;
                source.CreatedBy = null;
                source.UpdatedBy = null;
            }
            if (scenario == "missing-custody")
                source.LifecycleState = StorageObjectLifecycleStates.DeleteRequested;
            if (scenario == "missing-binding")
                await seed.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
            seed.Add(binding);
            if (!sourceLess)
                seed.Add(source);
            await seed.SaveChangesAsync();
            if (scenario == "picture-only")
            {
                var picture = await seed.Set<ActorPii>().SingleAsync(item => item.ActorId == scope.ActorId);
                picture.SetProfilePicture(objectId, null);
            }
            if (scenario == "missing-binding")
                await seed.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
            if (scenario is "other-owner" or "audit-only")
            {
                var parent = await seed.Events.SingleAsync(item => item.Id == scope.EventAId);
                parent.FeaturedImageId = objectId;
                parent.IsDeleted = true;
            }
            if (scenario is "unsettled" or "version-conflict" || sourceLess)
            {
                var session = Session(sourceLess ? objectId : Guid.CreateVersion7(),
                    scope.TenantAId, subjectId, key,
                    StorageObjectPurposes.Attachment, null, null);
                session.StorageProviderBindingId = binding.Id;
                session.StorageObjectId = sourceLess ? null : objectId;
                session.MarkUploading(Now);
                session.ProviderVersionId = scenario == "version-conflict" ? "different-version" : null;
                seed.Add(session);
                if (scenario == "source-less-retained")
                    seed.Add(StorageObjectDeletionTombstone.Create(objectId, scope.TenantAId,
                        StorageProviders.Local, binding.Id, key, null, false, Now));
            }
            if (scenario == "producer-operation")
                seed.Add(StorageProducerOperation.Create(objectId, scope.TenantAId, binding, key, Now));
            if (scenario == "report-evidence")
            {
                var report = EventReport.Create(scope.TenantAId, scope.EventAId, subjectId, scope.ActorId,
                    EventReporterKind.AuthenticatedUser, EventReportSourceKind.UserReport, "privacy", null,
                    EventReportPriority.Normal, null, false, false, null, null, null, Now);
                var evidence = EventReportEvidence.CreateReporterText(scope.TenantAId, report.Id,
                    "protected-evidence", EventReportEvidenceClassification.Sensitive, null, subjectId, Now);
                seed.AddRange(report, evidence);
                seed.Entry(evidence).Property(item => item.StorageObjectId).CurrentValue = objectId;
            }
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var repository = new UserLocationPrivacyErasureRepository(context);
            var candidates = await repository.GetProviderCandidatesAsync(subjectId, default);
            await Assert.That(candidates.Any(candidate => candidate.ProviderKind == PrivacyErasureProviderKind.ObjectStorage))
                .IsFalse();
            if (scenario is "missing-binding" or "version-conflict")
            {
                await Assert.That(async () => await repository.EraseProviderBackedLocalUserMetadataAsync(subjectId, default))
                    .Throws<InvalidOperationException>();
                await transaction.RollbackAsync();
            }
            else
            {
                await repository.EraseProviderBackedLocalUserMetadataAsync(subjectId, default);
                if (scenario == "report-evidence")
                    await repository.AnonymizeRetainedAuditEvidenceAsync(subjectId, default);
                if (scenario == "rollback") await transaction.RollbackAsync();
                else await transaction.CommitAsync();
            }
        }

        await using var verification = database.CreateContext();
        var retained = await verification.StorageObjects.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == objectId);
        var work = await verification.StorageObjectDeletionTombstones.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == objectId);
        if (scenario is "settled" or "picture-only" or "unsettled" or "missing-custody" or "producer-operation" or "report-evidence" || sourceLess)
        {
            await Assert.That(retained).IsNull();
            await Assert.That(work).IsNotNull();
            await Assert.That(work!.ProviderBindingId).IsEqualTo(binding.Id);
            await Assert.That(work.ObjectKey).IsEqualTo(key);
            await Assert.That(work.ProviderObjectVersion).IsEqualTo(sourceLess ? null : "original-version");
            await Assert.That(work.State).IsEqualTo(scenario is "settled" or "picture-only" or "report-evidence"
                ? StorageObjectDeletionState.Ready : StorageObjectDeletionState.AwaitingProducer);
            await Assert.That(await verification.StorageUploadSessions.AnyAsync(item => item.StorageObjectId == objectId)).IsFalse();
            await Assert.That(await verification.StorageUploadSessions.AnyAsync(item => item.Id == objectId)).IsFalse();
            await Assert.That(await verification.Set<StorageProducerOperation>().AnyAsync(item => item.Id == objectId)).IsFalse();
        }
        else
        {
            await Assert.That(work).IsNull();
            await Assert.That(retained).IsNotNull();
            await Assert.That(retained!.ObjectKey).IsEqualTo(key);
            await Assert.That(retained.ProviderVersionId).IsEqualTo("original-version");
            await Assert.That(retained.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
            await Assert.That(retained.ActorId).IsEqualTo(scenario is "held" or "other-owner" or "audit-only" ? null : scope.ActorId);
            if (scenario == "audit-only")
                await Assert.That(retained.SourceUri).IsEqualTo("https://media.example.invalid/retained.pdf");
        }
        if (scenario == "settled")
        {
            var provider = Substitute.For<IFileStorageProvider>();
            provider.Provider.Returns(StorageProviders.Local);
            provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var input = call.Arg<FileStorageDeleteInput>()
                    ?? throw new InvalidOperationException("Cleanup requires its deletion target.");
                await Assert.That(input.ObjectKey).IsEqualTo(key);
                await Assert.That(input.ProviderVersionId).IsEqualTo("original-version");
                await using var committed = database.CreateContext();
                await Assert.That(await committed.StorageObjectDeletionTombstones.AnyAsync(item => item.Id == objectId)).IsTrue();
                await Assert.That(await committed.StorageObjects.IgnoreQueryFilters().AnyAsync(item => item.Id == objectId)).IsFalse();
                return new FileStorageDeleteResult(StorageProviders.Local, key, true, "original-version");
            });
            provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var input = call.Arg<FileStorageExistsInput>()
                    ?? throw new InvalidOperationException("Cleanup requires its absence target.");
                if (input.ObjectKey != key || input.ProviderVersionId != "original-version")
                    throw new InvalidOperationException("Cleanup must verify the retired version.");
                return false;
            });
            var bindings = Substitute.For<IStorageProviderBindingService>();
            bindings.ResolveAsync(binding.Id, Arg.Any<CancellationToken>()).Returns(provider);
            var cleanup = new EventResourceStorageCleanupService(
                new StorageObjectDeletionTombstoneRepository(verification), bindings, new CleanupClock(),
                NullLogger<EventResourceStorageCleanupService>.Instance,
                new EventResourceStorageLifecycleRepository(verification), new EfCoreUnitOfWork(verification));
            var outcome = await cleanup.ProcessDueAsync(100, false, default);
            await Assert.That(outcome.DeletedCount).IsEqualTo(1);
            await Assert.That(await verification.StorageObjectDeletionTombstones.AsNoTracking()
                .AnyAsync(item => item.Id == objectId)).IsFalse();
        }
    }

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
            legacyStorage.StorageProviderBindingId = binding.Id;
            legacyResource.SetStoredFile(legacyStorageId, legacyResource.ConcurrencyStamp, subjectId, Now);

            var ordinaryStorage = Storage(ordinaryStorageId, scope.TenantAId, fileType, ordinaryKey,
                StorageObjectPurposes.Attachment, null, null, scope.ActorId, subjectId, otherId, checksum);
            ordinaryStorage.StorageProviderBindingId = binding.Id;

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
            ordinarySessionId = ordinaryStorageId;
            var ordinarySession = Session(ordinarySessionId, scope.TenantAId, subjectId, ordinaryKey,
                StorageObjectPurposes.Attachment, null, null);
            ordinarySession.StorageProviderBindingId = binding.Id;
            ordinarySession.MarkUploading(Now);
            ordinarySession.RecordProducerSettlement(ordinaryStorageId, binding.Id, ordinaryKey, null);
            ordinarySession.Finalize(ordinaryStorageId, ordinaryKey, checksum, Now);
            seed.AddRange(resourceSession, ordinarySession);
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var repository = new UserLocationPrivacyErasureRepository(context);
            IReadOnlyList<PrivacyErasureProviderCandidate> candidates =
                await repository.GetProviderCandidatesAsync(subjectId, default);

            await Assert.That(candidates.Any(candidate => candidate.Locator == resourceKey)).IsFalse();
            await Assert.That(candidates.Any(candidate => candidate.Locator == legacyKey)).IsFalse();
            await Assert.That(candidates.Any(candidate => candidate.Locator == ordinaryKey)).IsFalse();

            await repository.EraseProviderBackedLocalUserMetadataAsync(subjectId, default);
            await repository.AnonymizeRetainedAuditEvidenceAsync(subjectId, default);
            await transaction.CommitAsync();
        }

        await using var verification = database.CreateContext();
        StorageObject shared = await verification.StorageObjects.AsNoTracking()
            .SingleAsync(storage => storage.Id == resourceStorageId);
        StorageObject legacyShared = await verification.StorageObjects.AsNoTracking()
            .SingleAsync(storage => storage.Id == legacyStorageId);
        StorageObject? ordinary = await verification.StorageObjects.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(storage => storage.Id == ordinaryStorageId);
        bool resourceUploadExists = await verification.StorageUploadSessions.AsNoTracking()
            .AnyAsync(session => session.Id == resourceSessionId);
        var ordinaryWork = await verification.StorageObjectDeletionTombstones.AsNoTracking()
            .SingleAsync(item => item.Id == ordinaryStorageId);

        await Assert.That(shared.ObjectKey).IsEqualTo(resourceKey);
        await Assert.That(shared.SourceUri).IsNull();
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

        await Assert.That(ordinary).IsNull();
        await Assert.That(ordinaryWork.ObjectKey).IsEqualTo(ordinaryKey);
        await Assert.That(ordinaryWork.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(await verification.StorageUploadSessions.AnyAsync(item => item.Id == ordinarySessionId)).IsFalse();
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
            var session = Session(sessionId, scope.TenantAId, subjectId, null,
                StorageObjectPurposes.EventResource, StorageOwningResourceKinds.EventResource, resource.Id);
            session.StorageProviderBindingId = binding.Id;
            session.ReservedBytes = 64;
            session.BindEventResourceVersion(resource.ConcurrencyStamp);
            seed.Add(new StorageUsageCounter
            {
                Id = Guid.CreateVersion7(),
                TenantId = scope.TenantAId,
                Provider = StorageProviders.Local,
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

    private static StorageUploadSession Session(Guid id, Guid tenantId, Guid subjectId, string? objectKey,
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

    private sealed class CleanupClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
