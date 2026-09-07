using System.Diagnostics.Metrics;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StorageObjectDeletionServiceTests
{
    [Test]
    public async Task DeleteRequestedForResourceAsync_WhenProviderDeleteSucceeds_MarksMetadataDeleted()
    {
        var tenantId = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        var deletedBy = Guid.CreateVersion7();
        var storageObject = CreateStorageObject(tenantId, resourceId);
        var repository = CreateRepository(tenantId, resourceId, [storageObject]);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>())
            .Returns(new FileStorageDeleteResult(StorageProviders.Local, storageObject.ObjectKey!, Deleted: true));
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        var service = CreateService(repository, resolver);

        var result = await service.DeleteRequestedForResourceAsync(
            tenantId,
            ResourceKinds.Event,
            resourceId,
            deletedBy,
            limit: 10,
            CancellationToken.None);

        await Assert.That(result.ScannedCount).IsEqualTo(1);
        await Assert.That(result.DeletedCount).IsEqualTo(1);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(storageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Deleted);
        await Assert.That(storageObject.IsDeleted).IsTrue();
        await Assert.That(storageObject.DeletedBy).IsEqualTo(deletedBy);
        await provider.Received(1).DeleteAsync(
            Arg.Is<FileStorageDeleteInput>(input => input.ObjectKey == storageObject.ObjectKey),
            Arg.Any<CancellationToken>());
        await repository.Received(1).Update(storageObject);
    }

    [Test]
    public async Task DeleteRequestedForResourceAsync_WhenProviderFails_LeavesMetadataRetryable()
    {
        var tenantId = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        var storageObject = CreateStorageObject(tenantId, resourceId);
        var repository = CreateRepository(tenantId, resourceId, [storageObject]);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<FileStorageDeleteResult>>(_ => throw new IOException("provider unavailable"));
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        var service = CreateService(repository, resolver);

        var result = await service.DeleteRequestedForResourceAsync(
            tenantId,
            ResourceKinds.Event,
            resourceId,
            deletedBy: null,
            limit: 10,
            CancellationToken.None);

        await Assert.That(result.ScannedCount).IsEqualTo(1);
        await Assert.That(result.DeletedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(storageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That(storageObject.IsDeleted).IsFalse();
        await repository.DidNotReceive().Update(storageObject);
    }

    [Test]
    public async Task DeleteRequestedForResourceAsync_WhenObjectKeyMissing_MarksMetadataDeletedWithoutProviderCall()
    {
        var tenantId = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        var storageObject = CreateStorageObject(tenantId, resourceId);
        storageObject.ObjectKey = null;
        var repository = CreateRepository(tenantId, resourceId, [storageObject]);
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        var service = CreateService(repository, resolver);

        var result = await service.DeleteRequestedForResourceAsync(
            tenantId,
            ResourceKinds.Event,
            resourceId,
            deletedBy: null,
            limit: 10,
            CancellationToken.None);

        await Assert.That(result.ScannedCount).IsEqualTo(1);
        await Assert.That(result.MissingKeyDeletedCount).IsEqualTo(1);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(storageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Deleted);
        resolver.DidNotReceiveWithAnyArgs().GetRequired(default!);
        await repository.Received(1).Update(storageObject);
    }

    private static StorageObjectDeletionService CreateService(
        IStorageObjectRepository repository,
        IFileStorageProviderResolver resolver)
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new StorageObjectDeletionService(
            repository,
            resolver,
            new BusinessMetrics(meterFactory),
            NullLogger<StorageObjectDeletionService>.Instance);
    }

    private static IStorageObjectRepository CreateRepository(
        Guid tenantId,
        Guid resourceId,
        IReadOnlyList<StorageObject> storageObjects)
    {
        var repository = Substitute.For<IStorageObjectRepository>();
        repository
            .ListDeleteRequestedForResourceAsync(
                tenantId,
                ResourceKinds.Event,
                resourceId,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(storageObjects);
        repository.Update(Arg.Any<StorageObject>()).Returns(Task.CompletedTask);
        return repository;
    }

    private static StorageObject CreateStorageObject(Guid tenantId, Guid resourceId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        Provider = StorageProviders.Local,
        ObjectKey = $"tenants/{tenantId:N}/illegal.png",
        Uri = "/images/illegal.png",
        FullName = "illegal.png",
        SafeDisplayName = "illegal.png",
        Extension = ".png",
        Size = 100,
        Visibility = StorageObjectVisibilities.PublicImage,
        Purpose = StorageObjectPurposes.EventImage,
        LifecycleState = StorageObjectLifecycleStates.DeleteRequested,
        OwningResourceKind = ResourceKinds.Event,
        OwningResourceId = resourceId,
        CreatedAt = new DateTime(2026, 6, 23, 9, 0, 0, DateTimeKind.Utc)
    };
}
