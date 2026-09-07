using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StorageReconciliationServiceTests
{
    [Test]
    public async Task ReconcileAsync_WhenDryRun_DoesNotMutateMissingMetadata()
    {
        var utcNow = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var storageObject = CreateStorageObject();
        var repository = CreateRepository(activeObjects: [storageObject]);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(false);
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        var service = CreateService(repository, resolver, [], new StorageReconciliationSettings
        {
            DryRun = true,
            QuarantineMissingObjects = true
        });

        var result = await service.ReconcileAsync(utcNow, CancellationToken.None);

        await Assert.That(result.DryRun).IsTrue();
        await Assert.That(result.MissingBackingObjectCount).IsEqualTo(1);
        await Assert.That(result.QuarantinedMetadataCount).IsEqualTo(0);
        await Assert.That(storageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await repository.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Test]
    public async Task ReconcileAsync_WhenQuarantineEnabled_QuarantinesMissingMetadata()
    {
        var utcNow = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var storageObject = CreateStorageObject();
        var repository = CreateRepository(activeObjects: [storageObject]);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(false);
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        var service = CreateService(repository, resolver, [], new StorageReconciliationSettings
        {
            DryRun = false,
            QuarantineMissingObjects = true
        });

        var result = await service.ReconcileAsync(utcNow, CancellationToken.None);

        await Assert.That(result.QuarantinedMetadataCount).IsEqualTo(1);
        await Assert.That(storageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
        await Assert.That(storageObject.QuarantineReason).IsEqualTo("backing_object_missing");
        await repository.Received(1).Update(storageObject);
    }

    [Test]
    public async Task ReconcileAsync_WhenDeleteEnabled_DeletesQuarantinedMetadataIdempotently()
    {
        var utcNow = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var storageObject = CreateStorageObject();
        storageObject.MarkQuarantined(null, "backing_object_missing", utcNow.AddDays(-31));
        var repository = CreateRepository(deleteEligibleObjects: [storageObject]);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>())
            .Returns(new FileStorageDeleteResult(StorageProviders.Local, storageObject.ObjectKey!, Deleted: false));
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        var service = CreateService(repository, resolver, [], new StorageReconciliationSettings
        {
            DryRun = false,
            DeleteQuarantinedObjects = true
        });

        var result = await service.ReconcileAsync(utcNow, CancellationToken.None);

        await Assert.That(result.DeletedMetadataCount).IsEqualTo(1);
        await Assert.That(storageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Deleted);
        await Assert.That(storageObject.IsDeleted).IsTrue();
        await provider.Received(1).DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>());
        await repository.Received(1).Update(storageObject);
    }

    [Test]
    public async Task ReconcileAsync_WhenDryRun_ReportsLocalOrphanWithoutQuarantine()
    {
        var utcNow = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var repository = CreateRepository(knownObjectKeys: []);
        var inventoryProvider = new FakeInventoryProvider(
            [new FileStorageInventoryObject(StorageProviders.Local, "tenants/a/orphan.txt", 10, utcNow.AddDays(-2))]);
        var service = CreateService(repository, Substitute.For<IFileStorageProviderResolver>(), [inventoryProvider], new StorageReconciliationSettings
        {
            DryRun = true,
            QuarantineOrphanLocalFiles = true
        });

        var result = await service.ReconcileAsync(utcNow, CancellationToken.None);

        await Assert.That(result.OrphanBackingObjectCount).IsEqualTo(1);
        await Assert.That(result.QuarantinedBackingObjectCount).IsEqualTo(0);
        await Assert.That(inventoryProvider.QuarantineCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ReconcileAsync_WhenOrphanQuarantineEnabled_QuarantinesLocalOrphan()
    {
        var utcNow = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var repository = CreateRepository(knownObjectKeys: []);
        var inventoryProvider = new FakeInventoryProvider(
            [new FileStorageInventoryObject(StorageProviders.Local, "tenants/a/orphan.txt", 10, utcNow.AddDays(-2))]);
        var service = CreateService(repository, Substitute.For<IFileStorageProviderResolver>(), [inventoryProvider], new StorageReconciliationSettings
        {
            DryRun = false,
            QuarantineOrphanLocalFiles = true
        });

        var result = await service.ReconcileAsync(utcNow, CancellationToken.None);

        await Assert.That(result.OrphanBackingObjectCount).IsEqualTo(1);
        await Assert.That(result.QuarantinedBackingObjectCount).IsEqualTo(1);
        await Assert.That(inventoryProvider.QuarantineCalls).IsEqualTo(1);
    }

    private static StorageReconciliationService CreateService(
        IStorageObjectRepository repository,
        IFileStorageProviderResolver resolver,
        IReadOnlyList<IFileStorageProvider> providers,
        StorageReconciliationSettings settings)
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new StorageReconciliationService(
            repository,
            resolver,
            providers,
            Options.Create(settings),
            new BusinessMetrics(meterFactory),
            NullLogger<StorageReconciliationService>.Instance);
    }

    private static IStorageObjectRepository CreateRepository(
        IReadOnlyList<StorageObject>? activeObjects = null,
        IReadOnlyList<StorageObject>? deleteEligibleObjects = null,
        IReadOnlyList<string>? knownObjectKeys = null)
    {
        var repository = Substitute.For<IStorageObjectRepository>();
        repository.ListActiveForReconciliationAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(activeObjects ?? []);
        repository.ListDeleteEligibleForReconciliationAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(deleteEligibleObjects ?? []);
        repository.ListKnownObjectKeysAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(knownObjectKeys ?? []);
        repository.Update(Arg.Any<StorageObject>()).Returns(Task.CompletedTask);
        return repository;
    }

    private static StorageObject CreateStorageObject()
        => new()
        {
            Id = Guid.CreateVersion7(),
            Uri = "/api/storageobject/test/content",
            ObjectKey = "tenants/a/2026/06/02/file.txt",
            Provider = StorageProviders.Local,
            FullName = "file.txt",
            SafeDisplayName = "file.txt",
            Extension = ".txt",
            ContentType = "text/plain",
            Size = 10,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Attachment,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            FileTypeId = 1,
            FileType = null!,
            CreatedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)
        };

    private sealed class FakeInventoryProvider(IReadOnlyList<FileStorageInventoryObject> inventory) : IFileStorageInventoryProvider
    {
        public string Provider => StorageProviders.Local;
        public int QuarantineCalls { get; private set; }

        public Task<FileStorageWriteResult> WriteAsync(FileStorageWriteInput input, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(FileStorageExistsInput input, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<FileStorageReadResult> OpenReadAsync(FileStorageReadInput input, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<FileStorageDeleteResult> DeleteAsync(FileStorageDeleteInput input, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<FileStorageProviderStatus> TestAsync(
            CancellationToken cancellationToken,
            bool testWritePermissions = false)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<FileStorageInventoryObject> ListObjectsAsync(
            int limit,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in inventory.Take(limit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public Task<FileStorageQuarantineResult> QuarantineAsync(
            FileStorageQuarantineInput input,
            CancellationToken cancellationToken)
        {
            QuarantineCalls++;
            return Task.FromResult(new FileStorageQuarantineResult(Provider, input.ObjectKey, Quarantined: true));
        }
    }
}
