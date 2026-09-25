using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourceFileUploadTests.Database>]
[NotInParallel]
public sealed class EventResourceStorageCleanupTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task FailedDeletionRetainsAuthorityUntilRetryConfirmsAbsence()
    {
        var work = await SeedAsync(producerSettled: true);
        await using var context = database.CreateContext();
        var repository = new StorageObjectDeletionTombstoneRepository(context);
        var clock = new Clock(new DateTimeOffset(Now));
        bool exists = true, fail = true;
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (fail) throw new IOException("Provider failure without a durable acknowledgement.");
                exists = false;
                return new FileStorageDeleteResult(StorageProviders.Local, work.ObjectKey, true);
            });
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(_ => exists);
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(work.ProviderBindingId, Arg.Any<CancellationToken>()).Returns(provider);
        var service = new EventResourceStorageCleanupService(repository, bindings, clock,
            NullLogger<EventResourceStorageCleanupService>.Instance,
            new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context));
        var first = await service.ProcessDueAsync(100, dryRun: false, default);
        await Assert.That(first.FailedCount).IsEqualTo(1);
        var retained = await repository.GetByIdAsync(work.Id, default);
        await Assert.That(retained!.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(exists).IsTrue();
        clock.UtcNow = new DateTimeOffset(DateTime.SpecifyKind(retained.NextAttemptAtUtc!.Value, DateTimeKind.Utc));
        fail = false;
        var retry = await service.ProcessDueAsync(100, dryRun: false, default);
        await Assert.That(retry.DeletedCount).IsEqualTo(1);
        await Assert.That(exists).IsFalse();
        await Assert.That(await repository.GetByIdAsync(work.Id, default)).IsNull();
    }

    [Test]
    public async Task DryRunAndDeleteAcknowledgementWithoutAbsenceCannotPurgeAuthority()
    {
        var work = await SeedAsync(producerSettled: true);
        await using var context = database.CreateContext();
        var repository = new StorageObjectDeletionTombstoneRepository(context);
        bool attempted = false;
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            attempted = true;
            return new FileStorageDeleteResult(StorageProviders.Local, work.ObjectKey, true);
        });
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(true);
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(work.ProviderBindingId, Arg.Any<CancellationToken>()).Returns(provider);
        var service = new EventResourceStorageCleanupService(repository, bindings, new Clock(new DateTimeOffset(Now)),
            NullLogger<EventResourceStorageCleanupService>.Instance,
            new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context));
        await service.ProcessDueAsync(100, dryRun: true, default);
        await Assert.That(attempted).IsFalse();
        await Assert.That((await repository.GetByIdAsync(work.Id, default))!.ConcurrencyStamp).IsEqualTo(work.ConcurrencyStamp);
        await service.ProcessDueAsync(100, dryRun: false, default);
        await Assert.That(attempted).IsTrue();
        await Assert.That(await repository.GetByIdAsync(work.Id, default)).IsNotNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExistingReconcilerProcessesCommittedDeletionIndependentlyOfQuarantinePolicy(bool dryRun)
    {
        var work = await SeedAsync(producerSettled: true);
        await using var context = database.CreateContext();
        var tombstones = new StorageObjectDeletionTombstoneRepository(context);
        bool exists = true;
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            exists = false;
            return new FileStorageDeleteResult(StorageProviders.Local, work.ObjectKey, true);
        });
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(_ => exists);
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(work.ProviderBindingId, Arg.Any<CancellationToken>()).Returns(provider);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var service = new StorageReconciliationService(
            new StorageObjectRepository(context), Substitute.For<IFileStorageProviderResolver>(), [],
            Options.Create(new StorageReconciliationSettings
            {
                DryRun = dryRun, DeleteQuarantinedObjects = false, QuarantineMissingObjects = false
            }),
            new BusinessMetrics(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<StorageReconciliationService>.Instance,
            new EventResourceStorageCleanupService(tombstones, bindings, new Clock(new DateTimeOffset(Now)),
                NullLogger<EventResourceStorageCleanupService>.Instance,
                new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context)));
        await service.ReconcileAsync(Now, default);
        await Assert.That(await tombstones.GetByIdAsync(work.Id, default) is not null).IsEqualTo(dryRun);
        await Assert.That(exists).IsEqualTo(dryRun);
    }

    private async Task<StorageObjectDeletionTombstone> SeedAsync(bool producerSettled)
    {
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", null, producerSettled, Now);
        await using var context = database.CreateContext();
        context.AddRange(binding, work);
        await context.SaveChangesAsync();
        return work;
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SourceTransferRemovesAttributionWithoutGuessingProducerSettlement(bool settled, bool dryRun)
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeds.SeedScopeAsync();
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), scope.TenantAId,
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", null, settled, Now);
        await using var context = database.CreateContext();
        context.AddRange(binding, work, new StorageObject
        {
            Id = work.Id, TenantId = scope.TenantAId, Tenant = null!, FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!, Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
            ObjectKey = work.ObjectKey, Uri = "/private", FullName = "private.pdf", SafeDisplayName = "private.pdf",
            Extension = "pdf", ContentType = "application/pdf", Size = 64,
            Purpose = StorageObjectPurposes.EventResource, OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = Guid.CreateVersion7(), Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = StorageObjectLifecycleStates.DeleteRequested, CreatedAt = Now
        });
        await context.SaveChangesAsync();
        var repository = new StorageObjectDeletionTombstoneRepository(context);
        if (settled)
        {
            var claim = await repository.TryClaimAsync(work.Id, work.ConcurrencyStamp, Now, Now.AddMinutes(1), default);
            await Assert.That(claim).IsNull();
        }
        bool exists = true;
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            exists = false;
            return new FileStorageDeleteResult(StorageProviders.Local, work.ObjectKey, true);
        });
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(_ => exists);
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(binding.Id, Arg.Any<CancellationToken>()).Returns(provider);
        var worker = new EventResourceStorageCleanupService(repository, bindings, new Clock(new DateTimeOffset(Now)),
            NullLogger<EventResourceStorageCleanupService>.Instance,
            new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context));
        await worker.ProcessDueAsync(100, dryRun, default);
        await Assert.That(await context.StorageObjects.IgnoreQueryFilters().AnyAsync(item => item.Id == work.Id)).IsEqualTo(dryRun);
        await Assert.That(exists).IsEqualTo(dryRun || !settled);
        await Assert.That(await repository.GetByIdAsync(work.Id, default) is not null).IsEqualTo(dryRun || !settled);
        if (!settled)
            await Assert.That((await repository.GetByIdAsync(work.Id, default))!.State)
                .IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExpiryReleasesReservationWithoutTreatingAnUnknownWriteAsSettled(bool started, bool dryRun)
    {
        await using var seeds = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        var scope = await seeds.SeedScopeAsync();
        await using var context = database.CreateContext();
        Guid subject = (await context.Actors.Where(actor => actor.Id == scope.ActorId)
            .Select(actor => actor.UserId).SingleAsync())!.Value;
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        context.Add(resource);
        await context.SaveChangesAsync();
        var session = new StorageUploadSession
        {
            Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, UserId = subject,
            Provider = StorageProviders.Local, RouteKey = "documents", PolicyVersion = "test",
            PolicyMaxUploadBytes = 1024, ExpectedSizeBytes = 64, ReservedBytes = 64,
            Status = StorageUploadSessionStates.Reserved,
            ContentType = "application/pdf", SafeDisplayName = "file.pdf", Extension = "pdf",
            Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
            OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resource.Id,
            ExpiresAt = Now.AddMinutes(-1), CreatedAt = Now.AddMinutes(-3)
        };
        session.BindEventResourceVersion(resource.ConcurrencyStamp);
        Guid objectId = Guid.CreateVersion7();
        if (started)
        {
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            string key = $"objects/{objectId:N}";
            context.AddRange(binding, new StorageObject
            {
                Id = objectId, TenantId = scope.TenantAId, Tenant = null!, FileTypeId = (int)FileTypeEnum.Document,
                FileType = null!, Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
                ObjectKey = key, Uri = "/private", FullName = "file.pdf", SafeDisplayName = "file.pdf",
                Extension = "pdf", ContentType = "application/pdf", Size = 64,
                Purpose = StorageObjectPurposes.EventResource, OwningResourceKind = StorageOwningResourceKinds.EventResource,
                OwningResourceId = resource.Id, Visibility = StorageObjectVisibilities.PrivateOwner,
                LifecycleState = StorageObjectLifecycleStates.DeleteRequested, CreatedAt = Now.AddMinutes(-2)
            });
            session.ReserveObjectKey(key);
            session.MarkUploading(Now.AddMinutes(-2));
            session.StorageProviderBindingId = binding.Id;
            session.StageEventResourceObject(objectId);
        }
        context.AddRange(session, new StorageUsageCounter
        {
            Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Provider = StorageProviders.Local, ReservedBytes = 64
        });
        await context.SaveChangesAsync();
        bool providerAttempted = false;
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            providerAttempted = true;
            return Substitute.For<IFileStorageProvider>();
        });
        var tombstones = new StorageObjectDeletionTombstoneRepository(context);
        var worker = new EventResourceStorageCleanupService(tombstones, bindings, new Clock(new DateTimeOffset(Now)),
            NullLogger<EventResourceStorageCleanupService>.Instance,
            new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context));
        await worker.ProcessDueAsync(100, dryRun, default);
        context.ChangeTracker.Clear();
        await Assert.That((await context.StorageUsageCounters.SingleAsync(item => item.TenantId == scope.TenantAId))
            .ReservedBytes).IsEqualTo(dryRun ? 64 : 0);
        await Assert.That(await context.StorageUploadSessions.AnyAsync(item => item.Id == session.Id)).IsEqualTo(dryRun);
        await Assert.That(providerAttempted).IsFalse();
        var work = await tombstones.GetByIdAsync(objectId, default);
        await Assert.That(work is not null).IsEqualTo(started && !dryRun);
        if (work is not null) await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
    }
}
