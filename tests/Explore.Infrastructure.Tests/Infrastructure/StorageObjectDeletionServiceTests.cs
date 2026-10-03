using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.StorageObjects.Handlers.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel("StorageTargetSqlite")]
public sealed class StorageObjectDeletionServiceTests
{
    [Test]
    public async Task Retirement_WhenProviderDeleteSucceeds_RemovesOnlyCapturedBytesAndWork()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AdmitAsync();
        await Assert.That(File.Exists(fixture.TargetPath)).IsTrue();
        await Assert.That(await fixture.Database.Context.StorageObjects.AnyAsync()).IsFalse();
        var work = await fixture.WorkAsync();
        await Assert.That(work!.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(work.ProviderBindingId).IsEqualTo(fixture.Binding.Id);
        await Assert.That(work.ObjectKey).IsEqualTo(fixture.Object.ObjectKey);

        var result = await fixture.Cleanup.ProcessDueAsync(10, false, default);

        await Assert.That(result.DeletedCount).IsEqualTo(1);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(await fixture.WorkAsync()).IsNull();
        await Assert.That(File.Exists(fixture.TargetPath)).IsFalse();
        await Assert.That(File.Exists(fixture.NeighborPath)).IsTrue();
    }

    [Test]
    public async Task Retirement_WhenProviderThrows_PreservesDurableRetryUntilExactAbsence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AdmitAsync();
        fixture.Provider.FailDelete = true;

        var failed = await fixture.Cleanup.ProcessDueAsync(10, false, default);

        await Assert.That(failed.DeletedCount).IsEqualTo(0);
        await Assert.That(failed.FailedCount).IsEqualTo(1);
        var retry = await fixture.WorkAsync();
        await Assert.That(retry!.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(retry.LeaseExpiresAtUtc).IsNull();
        await Assert.That(retry.NextAttemptAtUtc).IsEqualTo(fixture.Clock.Now.AddMinutes(5).UtcDateTime);
        await Assert.That(retry.ProviderBindingId).IsEqualTo(fixture.Binding.Id);
        await Assert.That(retry.ObjectKey).IsEqualTo(fixture.Object.ObjectKey);
        await Assert.That(File.Exists(fixture.TargetPath)).IsTrue();
        await Assert.That(await fixture.Database.Context.StorageObjects.AnyAsync()).IsFalse();

        fixture.Provider.FailDelete = false;
        var premature = await fixture.Cleanup.ProcessDueAsync(10, false, default);
        await Assert.That(premature.DeletedCount).IsEqualTo(0);
        await Assert.That(File.Exists(fixture.TargetPath)).IsTrue();
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(5);
        var completed = await fixture.Cleanup.ProcessDueAsync(10, false, default);
        await Assert.That(completed.DeletedCount).IsEqualTo(1);
        await Assert.That(await fixture.WorkAsync()).IsNull();
        await Assert.That(File.Exists(fixture.TargetPath)).IsFalse();
        await Assert.That(File.Exists(fixture.NeighborPath)).IsTrue();
    }

    [Test]
    public async Task Retirement_WhenObjectKeyBlank_RejectsAdmissionWithoutLosingMetadataOrBytes()
    {
        await using var fixture = await Fixture.CreateAsync(missingKey: true);
        var response = await fixture.Delete.ExecuteAsync(new DeleteStorageObjectCommand { Id = fixture.Object.Id }, default);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.StorageObjectInvalidTarget);
        var retained = await fixture.Database.Context.StorageObjects.AsNoTracking().SingleAsync();
        await Assert.That(retained.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(retained.IsDeleted).IsFalse();
        await Assert.That(await fixture.WorkAsync()).IsNull();
        var result = await fixture.Cleanup.ProcessDueAsync(10, false, default);
        await Assert.That(result.DeletedCount).IsEqualTo(0);
        await Assert.That(File.Exists(fixture.TargetPath)).IsTrue();
        await Assert.That(File.Exists(fixture.NeighborPath)).IsTrue();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("retirement-").FullName;
        private Fixture(SmtpSettingsDatabase database, bool missingKey)
        {
            Database = database;
            database.Context.TenantContext = database;
            Binding = StorageProviderBinding.Local(_root);
            Provider = new FailureProvider(new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = _root }),
                NullLogger<LocalFileStorageProvider>.Instance));
            var bindings = Substitute.For<IStorageProviderBindingService>();
            bindings.ResolveAsync(Binding.Id, Arg.Any<CancellationToken>()).Returns(Provider);
            var lifecycle = new EventResourceStorageLifecycleRepository(database.Context);
            var unit = new EfCoreUnitOfWork(database.Context);
            Delete = new DeleteStorageObjectCommandHandler(new StorageObjectRepository(database.Context), lifecycle, unit, Clock);
            Cleanup = new EventResourceStorageCleanupService(new StorageObjectDeletionTombstoneRepository(database.Context),
                bindings, Clock, NullLogger<EventResourceStorageCleanupService>.Instance, lifecycle, unit);
            Object = new StorageObject
            {
                Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Image, FileType = null!,
                Provider = Binding.Provider, StorageProviderBindingId = Binding.Id,
                ObjectKey = missingKey ? " " : "objects/target.png", FullName = "target.png", SafeDisplayName = "target.png",
                Extension = "png", Size = 1, Purpose = StorageObjectPurposes.EventImage,
                Visibility = StorageObjectVisibilities.PublicImage, LifecycleState = StorageObjectLifecycleStates.Active,
                CreatedAt = Clock.Now.UtcDateTime
            };
        }

        public SmtpSettingsDatabase Database { get; }
        public StorageProviderBinding Binding { get; }
        public StorageObject Object { get; }
        public FailureProvider Provider { get; }
        public Clock Clock { get; } = new();
        public DeleteStorageObjectCommandHandler Delete { get; }
        public EventResourceStorageCleanupService Cleanup { get; }
        public string TargetPath => Path.Combine(_root, "objects/target.png");
        public string NeighborPath => Path.Combine(_root, "objects/neighbor.png");

        public static async Task<Fixture> CreateAsync(bool missingKey = false)
        {
            var fixture = new Fixture(await SmtpSettingsDatabase.CreateAsync(), missingKey);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.TargetPath)!);
            await File.WriteAllBytesAsync(fixture.TargetPath, [1]);
            await File.WriteAllBytesAsync(fixture.NeighborPath, [2]);
            fixture.Database.Context.AddRange(fixture.Binding, fixture.Object,
                new FileType { Id = (int)FileTypeEnum.Image, MasterCode = "Image", FullName = "Image" });
            await fixture.Database.Context.SaveChangesAsync();
            fixture.Database.Context.ChangeTracker.Clear();
            return fixture;
        }

        public async Task AdmitAsync()
        {
            var response = await Delete.ExecuteAsync(new DeleteStorageObjectCommand { Id = Object.Id }, default);
            await Assert.That(response.IsSuccess).IsTrue();
        }

        public Task<StorageObjectDeletionTombstone?> WorkAsync() =>
            Database.Context.StorageObjectDeletionTombstones.AsNoTracking().SingleOrDefaultAsync();

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            Directory.Delete(_root, true);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FailureProvider(IFileStorageProvider inner) : IFileStorageProvider
    {
        public bool FailDelete { get; set; }
        public string Provider => inner.Provider;
        public Task<FileStorageDeleteResult> DeleteAsync(FileStorageDeleteInput input, CancellationToken cancellationToken) =>
            FailDelete ? throw new IOException("Provider unavailable.") : inner.DeleteAsync(input, cancellationToken);
        public Task<bool> ExistsAsync(FileStorageExistsInput input, CancellationToken cancellationToken) =>
            inner.ExistsAsync(input, cancellationToken);
        public Task<FileStorageWriteResult> WriteAsync(FileStorageWriteInput input, CancellationToken cancellationToken) =>
            inner.WriteAsync(input, cancellationToken);
        public Task<FileStorageReadResult> OpenReadAsync(FileStorageReadInput input, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(input, cancellationToken);
        public Task<FileStorageProviderStatus> TestAsync(CancellationToken cancellationToken, bool testWritePermissions = false) =>
            inner.TestAsync(cancellationToken, testWritePermissions);
    }
}
