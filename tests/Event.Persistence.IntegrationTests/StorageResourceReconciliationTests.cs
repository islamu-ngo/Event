using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class StorageResourceReconciliationTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(StorageObjectLifecycleStates.Active)]
    [Arguments(StorageObjectLifecycleStates.DeleteRequested)]
    public async Task ResourceOwnedObjectsNeverEnterUnfencedProviderLoops(string state)
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeds.SeedScopeAsync();
        Guid resourceId = Guid.CreateVersion7(), ownedId = Guid.CreateVersion7(), ordinaryId = Guid.CreateVersion7();
        await using var context = database.CreateContext();
        context.AddRange(Object(ownedId, true), Object(ordinaryId, false));
        await context.SaveChangesAsync();
        var repository = new StorageObjectRepository(context);
        var candidates = state == StorageObjectLifecycleStates.Active
            ? await repository.ListActiveForReconciliationAsync(Now.AddDays(1), 1000, default)
            : await repository.ListDeleteEligibleForReconciliationAsync(Now.AddDays(1), 1000, default);
        await Assert.That(candidates.Any(item => item.Id == ownedId)).IsFalse();
        await Assert.That(candidates.Any(item => item.Id == ordinaryId)).IsTrue();
        var direct = await repository.ListDeleteRequestedForResourceAsync(scope.TenantAId,
            StorageOwningResourceKinds.EventResource, resourceId, 100, default);
        await Assert.That(direct.Any(item => item.Id == ownedId)).IsFalse();

        StorageObject Object(Guid id, bool resource) => new()
        {
            Id = id, TenantId = scope.TenantAId, Tenant = null!, FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
            Provider = StorageProviders.Local, ObjectKey = $"objects/{id:N}", Uri = "/private",
            FullName = "file.pdf", SafeDisplayName = "file.pdf", Extension = "pdf", ContentType = "application/pdf",
            Size = 10, Purpose = resource ? StorageObjectPurposes.EventResource : StorageObjectPurposes.Attachment,
            OwningResourceKind = resource ? StorageOwningResourceKinds.EventResource : null,
            OwningResourceId = resource ? resourceId : null, Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = state, CreatedAt = Now.AddDays(-2)
        };
    }

    [Test]
    public async Task PendingDeletionKeyRemainsKnownAfterSourceMetadataDisappears()
    {
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", null, false, Now);
        await using var context = database.CreateContext();
        context.AddRange(binding, work);
        await context.SaveChangesAsync();
        var repository = new StorageObjectRepository(context);
        var known = await repository.ListKnownObjectKeysAsync(StorageProviders.Local,
            [work.ObjectKey, $"unknown/{Guid.CreateVersion7():N}"], default);
        await Assert.That(known).Contains(work.ObjectKey);
        await Assert.That(known.Count).IsEqualTo(1);
    }
}
