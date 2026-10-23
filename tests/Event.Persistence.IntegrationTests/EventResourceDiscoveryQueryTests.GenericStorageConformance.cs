using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    [Arguments("resource", false)]
    [Arguments("resource", true)]
    [Arguments("quarantined", false)]
    [Arguments("quarantined", true)]
    [Arguments("retained-deleted-quarantine", false)]
    [Arguments("retained-deleted-quarantine", true)]
    [Arguments("reverse-reference", false)]
    [Arguments("reverse-reference", true)]
    [Arguments("tombstone", false)]
    [Arguments("tombstone", true)]
    public async Task GenericStorageDeniesResourceOwnershipWhileRealGenericBytesRemainReadable(
        string exclusion, bool asManager)
    {
        var (scope, manager) = await SeedAsync();
        var uploader = await MemberAsync(scope.TenantAId);
        await Assert.That(uploader == manager).IsFalse();
        var resource = await ActionResourceAsync(scope, uploader, "download");
        var nativeStorageId = resource.StorageObjectId!.Value;
        var subject = asManager ? manager : uploader;
        var rootPath = Path.Combine(Path.GetTempPath(), $"generic-storage-{Guid.CreateVersion7():N}");
        string? nativePath = null;
        Directory.CreateDirectory(rootPath);
        try
        {
            await using var context = database.CreateMeasurementContext();
            await context.Database.OpenConnectionAsync();
            var native = await context.StorageObjects.SingleAsync(row => row.Id == nativeStorageId);
            var nativeBinding = await context.Set<StorageProviderBinding>()
                .AsNoTracking().SingleAsync(row => row.Id == native.StorageProviderBindingId);
            var genericBinding = StorageProviderBinding.Local(rootPath);
            var genericProvider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = rootPath }),
                NullLogger<LocalFileStorageProvider>.Instance, boundTarget: true);
            var nativeProvider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = nativeBinding.LocalRootPath! }),
                NullLogger<LocalFileStorageProvider>.Instance, boundTarget: true);

            // Populate the helper's exact inspected content identity at its captured target.
            // The fixture's object key is unique; only this file is removed in finally.
            byte[] nativeBytes = "audience action fixture"u8.ToArray();
            nativePath = Path.Combine(nativeBinding.LocalRootPath!, native.ObjectKey!);
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            await File.WriteAllBytesAsync(nativePath, nativeBytes);
            native.Size = nativeBytes.Length;
            native.CreatedBy = uploader;

            byte[] genericBytes = RandomNumberGenerator.GetBytes(1024);
            var control = await WriteGenericControlAsync(genericProvider, genericBinding, native,
                uploader, genericBytes);
            control.Visibility = asManager
                ? StorageObjectVisibilities.AuthenticatedTenant
                : StorageObjectVisibilities.PrivateOwner;
            context.AddRange(genericBinding, control);
            await context.SaveChangesAsync();

            var target = native;
            Guid? causeId = null;
            Guid? effectId = null;
            Guid? quarantinedMaterial = null;
            if (exclusion is "quarantined" or "retained-deleted-quarantine")
            {
                var current = await context.EventResources.Include(row => row.ModerationQuarantines)
                    .SingleAsync(row => row.Id == resource.Id);
                causeId = Guid.CreateVersion7();
                effectId = Guid.CreateVersion7();
                quarantinedMaterial = current.MaterialVersionId!.Value;
                current.ApplyModerationQuarantine(causeId.Value, effectId.Value, 0,
                    quarantinedMaterial.Value, current.ConcurrencyStamp, new DateTimeOffset(Now));
                if (exclusion == "retained-deleted-quarantine")
                {
                    // Native deletion detaches the FK, but neither retires the cause nor
                    // converts the retained storage object's ownership into generic access.
                    await Assert.That(current.Delete(current.ConcurrencyStamp, uploader, Now))
                        .IsEqualTo(nativeStorageId);
                }
                await context.SaveChangesAsync();
            }
            else if (exclusion is "reverse-reference" or "tombstone")
            {
                target = await WriteGenericControlAsync(genericProvider, genericBinding, native,
                    uploader, genericBytes);
                target.Visibility = control.Visibility;
                context.StorageObjects.Add(target);
                if (exclusion == "reverse-reference")
                {
                    var current = await context.EventResources.SingleAsync(row => row.Id == resource.Id);
                    current.SetStoredFile(target.Id, current.ConcurrencyStamp, uploader, Now);
                }
                else
                {
                    context.StorageObjectDeletionTombstones.Add(StorageObjectDeletionTombstone.Create(
                        target.Id, target.TenantId, target.Provider, genericBinding.Id,
                        target.ObjectKey!, target.ProviderVersionId, true, Now));
                }
                await context.SaveChangesAsync();
            }

            var repository = new StorageObjectRepository(context);
            var bindings = new GenericStorageTargets(nativeBinding.Id, nativeProvider,
                genericBinding.Id, genericProvider);
            using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            using var metrics = new BusinessMetrics(services.GetRequiredService<IMeterFactory>());
            var reader = new StorageObjectContentReader(repository, bindings,
                new GenericStorageSubject(subject), NullLogger<StorageObjectContentReader>.Instance,
                metrics, new Clock());

            // Prove the negative target has readable bytes at a working, exact bound provider.
            var targetProvider = await bindings.ResolveTargetAsync(
                target.StorageProviderBindingId, target.Provider, default);
            var raw = await targetProvider.OpenReadAsync(
                new FileStorageReadInput(target.ObjectKey!, target.ContentType, target.ProviderVersionId), default);
            await using (raw.Content)
            {
                using var rawBytes = new MemoryStream();
                await raw.Content.CopyToAsync(rawBytes);
                await Assert.That(rawBytes.ToArray().SequenceEqual(
                    target.Id == nativeStorageId ? nativeBytes : genericBytes)).IsTrue();
            }
            await Assert.That(target.TenantId).IsEqualTo(scope.TenantAId);
            await Assert.That(target.CreatedBy).IsEqualTo(uploader);
            await Assert.That(target.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
            await Assert.That(target.IsDeleted).IsFalse();
            await Assert.That(await repository.GetForGenericAccessAsync(target.Id, default)).IsNull();
            await Assert.That(await repository.GetFileWithDetails(target.Id)).IsNull();
            await Assert.That((await repository.GetFilesWithDetails()).Any(row => row.Id == target.Id)).IsFalse();
            await Assert.That(await reader.OpenAsync(target.Id, false, default)).IsNull();
            await Assert.That(await reader.OpenAsync(target.Id, true, default)).IsNull();

            if (target.Id == nativeStorageId)
            {
                await Assert.That(native.HasBoundDocumentInspection).IsTrue();
                await Assert.That(StoragePresentationUrlResolver.DeliveryUri(native)).IsNull();
                // These detached metadata views isolate each URL guard. The relational
                // owner constraint correctly forbids persisting either partial owner shape.
                var purposeOnly = await repository.GetForAuthorizationAsync(native.Id, native.TenantId, default);
                purposeOnly!.OwningResourceKind = null;
                purposeOnly.OwningResourceId = null;
                await Assert.That(StoragePresentationUrlResolver.DeliveryUri(purposeOnly)).IsNull();
                var kindOnly = await repository.GetForAuthorizationAsync(native.Id, native.TenantId, default);
                kindOnly!.Purpose = StorageObjectPurposes.Document;
                await Assert.That(StoragePresentationUrlResolver.DeliveryUri(kindOnly)).IsNull();
            }
            else
            {
                // A pure metadata resolver cannot see a reverse FK or deletion tombstone.
                await Assert.That(StoragePresentationUrlResolver.DeliveryUri(target))
                    .IsEqualTo($"/api/storageobject/{target.Id}/content");
            }

            await Assert.That(await repository.GetForGenericAccessAsync(control.Id, default)).IsNotNull();
            await Assert.That(await repository.GetFileWithDetails(control.Id)).IsNotNull();
            await Assert.That(StoragePresentationUrlResolver.DeliveryUri(control))
                .IsEqualTo($"/api/storageobject/{control.Id}/content");
            var allowed = await reader.OpenAsync(control.Id, false, default);
            await Assert.That(allowed).IsNotNull();
            await using (allowed!.Content)
            {
                using var received = new MemoryStream();
                await allowed.Content.CopyToAsync(received);
                await Assert.That(received.ToArray().SequenceEqual(genericBytes)).IsTrue();
                await Assert.That(allowed.Length).IsEqualTo(1024L);
                await Assert.That(allowed.Sha256Checksum).IsEqualTo(control.Sha256Checksum);
            }
            await Assert.That(await reader.OpenAsync(control.Id, true, default)).IsNull();

            if (causeId is not null)
            {
                var retained = await context.EventResources.AsNoTracking()
                    .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .Include(row => row.ModerationQuarantines).SingleAsync(row => row.Id == resource.Id);
                await Assert.That(retained.HasModerationQuarantine).IsTrue();
                await Assert.That(retained.ModerationQuarantineCount).IsEqualTo(1);
                await Assert.That(retained.MaterialVersionId).IsEqualTo(quarantinedMaterial);
                await Assert.That(retained.IsDeleted).IsEqualTo(exclusion == "retained-deleted-quarantine");
                await Assert.That(retained.StorageObjectId)
                    .IsEqualTo(exclusion == "retained-deleted-quarantine" ? null : (Guid?)nativeStorageId);
                var cause = retained.ModerationQuarantines.Single();
                cause.ValidatePersistedState();
                await Assert.That(cause.Id).IsEqualTo(causeId.Value);
                await Assert.That(cause.EffectId).IsEqualTo(effectId!.Value);
                await Assert.That(cause.MaterialVersionId).IsEqualTo(quarantinedMaterial!.Value);
                await Assert.That(cause.ReleaseId).IsNull();
            }
            await RecordAudienceDatabaseObservationAsync(context);
        }
        finally
        {
            if (nativePath is not null) File.Delete(nativePath);
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static async Task<StorageObject> WriteGenericControlAsync(
        LocalFileStorageProvider provider, StorageProviderBinding binding, StorageObject native,
        Guid uploader, byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        var written = await provider.WriteAsync(new FileStorageWriteInput(native.TenantId, input,
            "application/octet-stream", "generic.bin", ".bin", bytes.LongLength, bytes.LongLength), default);
        return new StorageObject
        {
            Id = Guid.CreateVersion7(), TenantId = native.TenantId, Tenant = null!,
            FileTypeId = native.FileTypeId, FileType = null!, Provider = written.Provider,
            StorageProviderBindingId = binding.Id, ObjectKey = written.ObjectKey,
            FullName = "generic.bin", SafeDisplayName = "generic.bin", Extension = ".bin",
            ContentType = written.ContentType, Sha256Checksum = written.Sha256Checksum,
            Size = written.SizeBytes, Purpose = StorageObjectPurposes.Document,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedAt = Now, CreatedBy = uploader, ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private sealed record GenericStorageSubject(Guid Subject) : ICurrentUserService
    {
        public Guid? UserId => Subject;
        public bool IsAuthenticated => true;
    }

    private sealed class GenericStorageTargets(
        Guid nativeBindingId, IFileStorageProvider nativeProvider,
        Guid genericBindingId, IFileStorageProvider genericProvider) : IStorageProviderBindingService
    {
        public Task<StorageProviderBinding> CaptureAsync(
            string provider, Guid tenantId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Read conformance uses only captured storage targets.");

        public Task<IFileStorageProvider> ResolveAsync(Guid bindingId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(bindingId == nativeBindingId ? nativeProvider
                : bindingId == genericBindingId ? genericProvider
                : throw new InvalidOperationException("Storage target is outside this controlled fixture."));
        }
    }
}
