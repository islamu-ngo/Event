using System.Security.Cryptography;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Repositories;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class EventResourceCleanupTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task HeavyParentRedactionDetachesPrivateFilesAndClearsProtectedDestinations()
    {
        await using var seed = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seed.SeedScopeAsync();
        Guid fileId = Guid.CreateVersion7(), linkId = Guid.CreateVersion7(), objectId = Guid.CreateVersion7();
        await using var context = database.CreateContext();
        Guid managerId = (await context.Actors.Where(actor => actor.Id == scope.ActorId)
            .Select(actor => actor.UserId).SingleAsync())!.Value;
        var parent = await context.Events.SingleAsync(row => row.Id == scope.EventAId);
        if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
        var file = Draft(fileId, EventResourceDeliveryTypeEnum.StoredFile);
        var link = Draft(linkId, EventResourceDeliveryTypeEnum.ExternalLink);
        string checksum = Convert.ToHexString(SHA256.HashData("%PDF-1.7\ncleanup\n%%EOF"u8));
        var storage = new StorageObject
        {
            Id = objectId, TenantId = scope.TenantAId, Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
            Provider = StorageProviders.Local, ObjectKey = $"tenants/{scope.TenantAId:N}/{objectId:N}.pdf",
            Uri = $"/api/eventresource/{fileId}/content", FullName = "handout.pdf",
            SafeDisplayName = "handout.pdf", Extension = "pdf", ContentType = "application/pdf",
            Size = 21, Sha256Checksum = checksum, Purpose = StorageObjectPurposes.EventResource,
            Visibility = StorageObjectVisibilities.PrivateOwner, OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = fileId, LifecycleState = StorageObjectLifecycleStates.Active
        };
        storage.RecordEventResourceInspection(objectId, checksum);
        file.SetStoredFile(objectId, file.ConcurrencyStamp, managerId, Now);
        link.SetExternalDestination("protected-test-envelope", 1, "https://materials.example.test",
            link.ConcurrencyStamp, managerId, Now);
        var facts = new EventResourceParentFacts(scope.TenantAId, scope.EventAId, null,
            EventStatusEnum.Published, false, true, null, false, new(null, null, null, null));
        file.Publish(facts, true, file.ConcurrencyStamp, managerId, Now);
        link.Publish(facts, true, link.ConcurrencyStamp, managerId, Now);
        context.AddRange(storage, file, link);
        await context.SaveChangesAsync();
        var repository = new EventHeavyRedactionRepository(context);
        var graph = await repository.GetForUpdateAsync(scope.EventAId, default);
        EventHeavyRedactionApplicator.Apply(graph!, managerId, new DateTimeOffset(Now));
        await repository.SaveChangesAsync(default);

        await using var verify = database.CreateContext();
        var saved = await verify.EventResources.IgnoreQueryFilters()
            .Where(row => row.TenantId == scope.TenantAId && (row.Id == fileId || row.Id == linkId))
            .ToArrayAsync();
        await Assert.That(saved.Length).IsEqualTo(2);
        await Assert.That(saved.All(row => row.IsDeleted)).IsTrue();
        await Assert.That(saved.All(row => row.StorageObjectId is null && row.ExternalDestinationCiphertext is null
            && row.ExternalDestinationProtectionVersion is null && row.ExternalDestinationSafeOrigin is null)).IsTrue();
        var retired = await verify.StorageObjects.SingleAsync(row => row.Id == objectId);
        await Assert.That(retired.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That(retired.OwningResourceKind).IsEqualTo(StorageOwningResourceKinds.EventResource);
        await Assert.That(retired.OwningResourceId).IsEqualTo(fileId);

        EventResource Draft(Guid id, EventResourceDeliveryTypeEnum delivery) =>
            EventResource.CreateDraft(id, scope.TenantAId, scope.EventAId, null,
                new EventResourceMetadata
                {
                    Title = "Private material", Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, delivery, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, id,
                    EventResourceAudienceKindEnum.AuthenticatedTenantMember)], managerId, Now);
    }

    [Test]
    public async Task AuditExpiryAndPhysicalResourceRemovalCannotDiscardFailedByteCleanup()
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeds.SeedScopeAsync();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        Guid objectId = Guid.CreateVersion7(), managerId = Guid.CreateVersion7();
        string key = $"objects/{objectId:N}";
        var clock = new CleanupClock(new DateTimeOffset(Now));
        await using var context = database.CreateContext();
        context.AddRange(resource, binding, new StorageObject
        {
            Id = objectId, TenantId = scope.TenantAId, Tenant = null!, FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!, Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
            ObjectKey = key, Uri = "/private", FullName = "private.pdf", SafeDisplayName = "private.pdf",
            Extension = "pdf", ContentType = "application/pdf", Size = 64,
            Purpose = StorageObjectPurposes.EventResource, OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = resource.Id, Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = StorageObjectLifecycleStates.Active, CreatedAt = Now,
            CreatedBy = managerId
        }, new StorageUsageCounter
        {
            Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Provider = StorageProviders.Local,
            UsedBytes = 64, ObjectCount = 1
        });
        await context.SaveChangesAsync();
        resource.SetStoredFile(objectId, resource.ConcurrencyStamp, managerId, Now);
        context.Add(EventResourceAuditEntry.Create(scope.TenantAId, resource.Id, managerId,
            EventResourceAuditAction.ConfigureDelivery, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, Now.AddDays(-31)));
        await context.SaveChangesAsync();
        var unit = new EfCoreUnitOfWork(context);
        var lifecycle = new EventResourceStorageLifecycleRepository(context);
        await unit.ExecuteSerializableAsync(async ct =>
        {
            resource.Delete(resource.ConcurrencyStamp, managerId, Now);
            await context.SaveChangesAsync(ct);
            await lifecycle.RetireAsync(scope.TenantAId, [resource.Id], [], Now, ct);
            return true;
        });
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        bool unavailable = true, exists = true;
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (unavailable) throw new IOException("Provider unavailable.");
            exists = false;
            return new FileStorageDeleteResult(StorageProviders.Local, key, true);
        });
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(_ => exists);
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(binding.Id, Arg.Any<CancellationToken>()).Returns(provider);
        var tombstones = new StorageObjectDeletionTombstoneRepository(context);
        var worker = new EventResourceStorageCleanupService(tombstones, bindings, clock,
            NullLogger<EventResourceStorageCleanupService>.Instance, lifecycle, unit);
        await worker.ProcessDueAsync(100, false, default);
        await Assert.That(exists).IsTrue();
        await Assert.That(await tombstones.GetByIdAsync(objectId, default)).IsNotNull();
        await Assert.That(await context.StorageObjects.IgnoreQueryFilters().AnyAsync(item => item.Id == objectId)).IsFalse();

        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceGovernancePolicy.Default(long.MaxValue));
        var retention = new EventResourceAuditRetentionService(new EventResourceAuditRetentionRepository(context),
            governance, unit, clock);
        await retention.CleanupAsync(default);
        await Assert.That(await context.EventResourceAuditEntries.AnyAsync(item => item.EventResourceId == resource.Id)).IsFalse();
        await context.EventResources.IgnoreQueryFilters().Where(item => item.Id == resource.Id).ExecuteDeleteAsync();
        var retained = await tombstones.GetByIdAsync(objectId, default);
        await Assert.That(retained).IsNotNull();
        clock.UtcNow = new DateTimeOffset(DateTime.SpecifyKind(retained!.NextAttemptAtUtc!.Value, DateTimeKind.Utc));
        unavailable = false;
        await worker.ProcessDueAsync(100, false, default);
        await Assert.That(exists).IsFalse();
        await Assert.That(await tombstones.GetByIdAsync(objectId, default)).IsNull();
    }

    private sealed class CleanupClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
