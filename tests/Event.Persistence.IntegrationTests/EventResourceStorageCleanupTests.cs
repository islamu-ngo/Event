using Explore.Persistence.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
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
    [Arguments("claim", "expired")]
    [Arguments("claim", "replaced")]
    [Arguments("claim", "live")]
    [Arguments("binding", "expired")]
    [Arguments("binding", "replaced")]
    [Arguments("binding", "live")]
    [Arguments("custody", "expired")]
    [Arguments("custody", "live")]
    [Arguments("deletion", "expired")]
    [Arguments("deletion", "replaced")]
    [Arguments("deletion", "live")]
    public async Task ProviderEntryRequiresOriginalPersistedCustodyAfterHandoff(string handoff, string outcome)
    {
        var work = await SeedAsync(producerSettled: true, providerVersion: "original-version");
        await using var context = database.CreateContext();
        var persisted = new StorageObjectDeletionTombstoneRepository(context);
        var repository = new HandoffRepository(persisted, handoff, work.Id);
        var clock = new Clock(new DateTimeOffset(Now));
        bool exists = true;
        FileStorageDeleteInput? deletedInput = null;
        FileStorageExistsInput? checkedInput = null;
        bool providerEnteredWithTransaction = false;
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.DeleteAsync(Arg.Any<FileStorageDeleteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            call.Arg<CancellationToken>().ThrowIfCancellationRequested();
            providerEnteredWithTransaction = context.Database.CurrentTransaction is not null;
            deletedInput = call.Arg<FileStorageDeleteInput>();
            exists = false;
            await repository.PauseAsync("deletion");
            return new FileStorageDeleteResult(work.Provider, work.ObjectKey, true, work.ProviderObjectVersion);
        });
        provider.ExistsAsync(Arg.Any<FileStorageExistsInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            checkedInput = call.Arg<FileStorageExistsInput>();
            return exists;
        });
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(work.ProviderBindingId, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await repository.PauseAsync("binding");
            return provider;
        });
        var service = new EventResourceStorageCleanupService(repository, bindings, clock,
            NullLogger<EventResourceStorageCleanupService>.Instance,
            new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context));

        var processing = service.ProcessDueAsync(100, dryRun: false, default);
        try
        {
            await repository.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var competingContext = database.CreateContext();
            var competing = new StorageObjectDeletionTombstoneRepository(competingContext);
            var original = (await competing.GetByIdAsync(work.Id, default))!;
            await Assert.That(original.State).IsEqualTo(StorageObjectDeletionState.Deleting);
            await Assert.That(original.LeaseExpiresAtUtc).IsEqualTo(Now.AddMinutes(5));
            var resumedAt = outcome == "expired" ? Now.AddMinutes(5) : Now.AddMinutes(1);
            Guid expectedStamp = original.ConcurrencyStamp;
            if (outcome == "replaced")
            {
                await Assert.That(await competing.TryScheduleRetryAsync(work.Id, original.ConcurrencyStamp,
                    Now.AddSeconds(1), Now.AddSeconds(2), default)).IsTrue();
                var ready = (await competing.GetByIdAsync(work.Id, default))!;
                var replacement = await competing.TryClaimAsync(work.Id, ready.ConcurrencyStamp,
                    Now.AddSeconds(2), Now.AddMinutes(7), default);
                await Assert.That(replacement).IsNotNull();
                expectedStamp = replacement!.ConcurrencyStamp;
            }
            clock.UtcNow = new DateTimeOffset(resumedAt);
            repository.Resume.TrySetResult(true);
            var result = await processing.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(result.DeletedCount).IsEqualTo(outcome == "live" ? 1 : 0);
            await Assert.That(exists).IsEqualTo(outcome != "live" && handoff != "deletion");
            await Assert.That(providerEnteredWithTransaction).IsFalse();
            if (outcome == "live")
            {
                await Assert.That(deletedInput).IsEqualTo(new FileStorageDeleteInput(work.ObjectKey, work.ProviderObjectVersion));
                await Assert.That(checkedInput).IsEqualTo(new FileStorageExistsInput(work.ObjectKey, work.ProviderObjectVersion));
                await Assert.That(await competing.GetByIdAsync(work.Id, default)).IsNull();
            }
            else
            {
                if (handoff == "deletion")
                    await Assert.That(deletedInput).IsEqualTo(new FileStorageDeleteInput(work.ObjectKey, work.ProviderObjectVersion));
                else
                    await Assert.That(deletedInput).IsNull();
                await Assert.That(checkedInput).IsNull();
                var retained = (await competing.GetByIdAsync(work.Id, default))!;
                await Assert.That(retained.ConcurrencyStamp).IsEqualTo(expectedStamp);
                await Assert.That(retained.LeaseExpiresAtUtc)
                    .IsEqualTo(outcome == "replaced" ? Now.AddMinutes(7) : original.LeaseExpiresAtUtc);
                await Assert.That(retained.State).IsEqualTo(StorageObjectDeletionState.Deleting);
            }
        }
        finally
        {
            repository.Resume.TrySetResult(true);
            await processing.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class HandoffRepository(
        IStorageObjectDeletionTombstoneRepository repository, string handoff, Guid objectId)
        : IStorageObjectDeletionTombstoneRepository
    {
        public TaskCompletionSource<bool> Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PauseAsync(string stage)
        {
            if (stage != handoff) return;
            Paused.TrySetResult(true);
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task<StorageObjectDeletionTombstone?> GetByIdAsync(Guid id, CancellationToken ct) =>
            repository.GetByIdAsync(id, ct);
        public Task AddAsync(StorageObjectDeletionTombstone work, CancellationToken ct) => repository.AddAsync(work, ct);
        public async Task<IReadOnlyList<StorageObjectDeletionTombstone>> ListWithSourcesAsync(int limit, CancellationToken ct) =>
            (await repository.ListWithSourcesAsync(limit, ct)).Where(work => work.Id == objectId).ToArray();
        public async Task<IReadOnlyList<StorageObjectDeletionTombstone>> ListDueAsync(DateTime now, int limit, CancellationToken ct) =>
            (await repository.ListDueAsync(now, limit, ct)).Where(work => work.Id == objectId).ToArray();
        public async Task<StorageObjectDeletionTombstone?> TryClaimAsync(
            Guid id, Guid stamp, DateTime now, DateTime expiry, CancellationToken ct)
        {
            var work = await repository.TryClaimAsync(id, stamp, now, expiry, ct);
            await PauseAsync("claim");
            return work;
        }
        public async Task<bool> HasLiveClaimAsync(StorageObjectDeletionTombstone claim, DateTime now, CancellationToken ct)
        {
            bool live = await repository.HasLiveClaimAsync(claim, now, ct);
            await PauseAsync("custody");
            return live;
        }
        public Task<bool> TrySettleProducerAsync(Guid id, Guid binding, string key, string? version, DateTime now, CancellationToken ct) =>
            repository.TrySettleProducerAsync(id, binding, key, version, now, ct);
        public Task<bool> TryRecordAbsenceAsync(Guid id, Guid stamp, DateTime now, CancellationToken ct) =>
            repository.TryRecordAbsenceAsync(id, stamp, now, ct);
        public Task<bool> TryScheduleRetryAsync(Guid id, Guid stamp, DateTime now, DateTime next, CancellationToken ct) =>
            repository.TryScheduleRetryAsync(id, stamp, now, next, ct);
    }

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
        var inventoryBindings = Substitute.For<IStorageProviderBindingRepository>();
        inventoryBindings.ListLocalAsync(Arg.Any<CancellationToken>()).Returns([]);
        var reconciliation = new StorageReconciliationOwner(new ReconciliationFactory(database));
        var service = new StorageReconciliationService(
            new StorageObjectRepository(context), bindings, inventoryBindings, reconciliation, reconciliation,
            Options.Create(new StorageReconciliationSettings
            {
                DryRun = dryRun,
                DeleteQuarantinedObjects = false,
                QuarantineMissingObjects = false
            }),
            new BusinessMetrics(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<StorageReconciliationService>.Instance,
            new EventResourceStorageCleanupService(tombstones, bindings, new Clock(new DateTimeOffset(Now)),
                NullLogger<EventResourceStorageCleanupService>.Instance,
                new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context)),
            new EventResourceStorageLifecycleRepository(context), new EfCoreUnitOfWork(context),
            new Clock(new DateTimeOffset(Now)));
        await service.ReconcileAsync(Now, default);
        await Assert.That(await tombstones.GetByIdAsync(work.Id, default) is not null).IsEqualTo(dryRun);
        await Assert.That(exists).IsEqualTo(dryRun);
    }

    private sealed class ReconciliationFactory(EventResourceFileUploadTests.Database database)
        : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(database.CreateContext());
        }
    }

    private async Task<StorageObjectDeletionTombstone> SeedAsync(bool producerSettled, string? providerVersion = null)
    {
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var work = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.Local, binding.Id, $"objects/{Guid.CreateVersion7():N}", providerVersion, producerSettled, Now);
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
            Id = work.Id,
            TenantId = scope.TenantAId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = binding.Id,
            ObjectKey = work.ObjectKey,
            FullName = "private.pdf",
            SafeDisplayName = "private.pdf",
            Extension = "pdf",
            ContentType = "application/pdf",
            Size = 64,
            Purpose = StorageObjectPurposes.EventResource,
            OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = Guid.CreateVersion7(),
            Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = StorageObjectLifecycleStates.DeleteRequested,
            CreatedAt = Now
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
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantAId,
            UserId = subject,
            Provider = StorageProviders.Local,
            RouteKey = "documents",
            PolicyVersion = "test",
            PolicyMaxUploadBytes = 1024,
            ExpectedSizeBytes = 64,
            ReservedBytes = 64,
            Status = StorageUploadSessionStates.Reserved,
            ContentType = "application/pdf",
            SafeDisplayName = "file.pdf",
            Extension = "pdf",
            Purpose = StorageObjectPurposes.EventResource,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = resource.Id,
            ExpiresAt = Now.AddMinutes(-1),
            CreatedAt = Now.AddMinutes(-3)
        };
        session.BindEventResourceVersion(resource.ConcurrencyStamp);
        Guid objectId = Guid.CreateVersion7();
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        context.Add(binding);
        session.StorageProviderBindingId = binding.Id;
        if (started)
        {
            session.ReserveObjectKey($"objects/{objectId:N}");
            context.Add(new StorageObject
            {
                Id = objectId,
                TenantId = scope.TenantAId,
                Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Document,
                FileType = null!,
                Provider = StorageProviders.Local,
                StorageProviderBindingId = binding.Id,
                ObjectKey = session.ObjectKey,
                FullName = "file.pdf",
                SafeDisplayName = "file.pdf",
                Extension = "pdf",
                ContentType = "application/pdf",
                Size = 64,
                Purpose = StorageObjectPurposes.EventResource,
                OwningResourceKind = StorageOwningResourceKinds.EventResource,
                OwningResourceId = resource.Id,
                Visibility = StorageObjectVisibilities.PrivateOwner,
                LifecycleState = StorageObjectLifecycleStates.DeleteRequested,
                CreatedAt = Now.AddMinutes(-2)
            });
            session.MarkUploading(Now.AddMinutes(-2));
            session.StageEventResourceObject(objectId);
        }
        context.AddRange(session, new StorageUsageCounter
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantAId,
            Provider = StorageProviders.Local,
            ReservedBytes = 64
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
