using Explore.Persistence.Services;
using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel("StorageTargetSqlite")]
public sealed class StorageReconciliationServiceTests
{
    [Test]
    [Arguments("shared", false)]
    [Arguments("held", false)]
    [Arguments("missing_authority", false)]
    [Arguments("invalid", false)]
    [Arguments("eligible", false)]
    [Arguments("unsettled", false)]
    [Arguments("eligible", true)]
    public async Task QuarantineAgeRequiresNativeRetirementAuthority(string scenario, bool dryRun)
    {
        await using var fixture = await NativeFixture.CreateAsync(scenario);
        var result = await fixture.CreateService(dryRun).ReconcileAsync(NativeFixture.Now, default);

        await Assert.That(result.DryRun).IsEqualTo(dryRun);
        await Assert.That(result.DeleteEligibleMetadataCount).IsEqualTo(1);
        await Assert.That(result.OrphanBackingObjectCount).IsEqualTo(0);
        await Assert.That(await File.ReadAllBytesAsync(fixture.TargetPath)).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(await File.ReadAllBytesAsync(fixture.NeighborPath)).IsEquivalentTo(new byte[] { 4, 5, 6 });
        var source = await fixture.Database.Context.StorageObjects.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == fixture.Object.Id);
        var work = await fixture.Database.Context.StorageObjectDeletionTombstones.AsNoTracking().SingleOrDefaultAsync();
        if (!dryRun && scenario is "eligible" or "unsettled")
        {
            await Assert.That(source).IsNull();
            await Assert.That(work).IsNotNull();
            await Assert.That(work!.Id).IsEqualTo(fixture.Object.Id);
            await Assert.That(work.TenantId).IsEqualTo(fixture.Object.TenantId);
            await Assert.That(work.Provider).IsEqualTo(StorageProviders.Local);
            await Assert.That(work.ProviderBindingId).IsEqualTo(fixture.Binding.Id);
            await Assert.That(work.ObjectKey).IsEqualTo("objects/target.png");
            await Assert.That(work.ProviderObjectVersion).IsNull();
            await Assert.That(work.State).IsEqualTo(scenario == "eligible"
                ? StorageObjectDeletionState.Ready : StorageObjectDeletionState.AwaitingProducer);
            await Assert.That(work.NextAttemptAtUtc).IsEqualTo(scenario == "eligible" ? NativeFixture.Now : (DateTime?)null);
            await Assert.That(await fixture.Database.Context.Set<StorageProducerOperation>().AnyAsync()).IsFalse();
            await Assert.That(result.DeletedMetadataCount).IsEqualTo(1);
            await Assert.That(result.FailedCount).IsEqualTo(0);
        }
        else
        {
            await Assert.That(source).IsNotNull();
            await Assert.That(source!.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
            await Assert.That(source.IsDeleted).IsFalse();
            await Assert.That(work).IsNull();
            await Assert.That(result.DeletedMetadataCount).IsEqualTo(0);
            await Assert.That(result.FailedCount).IsEqualTo(scenario == "invalid" ? 1 : 0);
            await Assert.That(result.SkippedCount).IsEqualTo(scenario == "invalid" ? 0 : 1);
            if (dryRun)
                await Assert.That(source.ConcurrencyStamp).IsEqualTo(fixture.Object.ConcurrencyStamp);
            if (scenario == "shared")
                await Assert.That((await fixture.Database.Context.ActorPii.AsNoTracking()
                    .SingleAsync(pii => pii.ProfilePictureStorageObjectId == fixture.Object.Id))
                    .ProfilePictureStorageObjectId).IsEqualTo(fixture.Object.Id);
        }
    }

    [Test]
    public async Task ReconcileAsync_WhenDryRun_DoesNotMutateMissingMetadata()
    {
        await using var fixture = await NativeFixture.CreateAsync("missing");
        File.Delete(fixture.TargetPath);
        var result = await fixture.CreateService(true).ReconcileAsync(NativeFixture.Now, default);

        await Assert.That(result.DryRun).IsTrue();
        await Assert.That(result.MissingBackingObjectCount).IsEqualTo(1);
        await Assert.That(result.QuarantinedMetadataCount).IsEqualTo(0);
        var source = await fixture.Database.Context.StorageObjects.AsNoTracking()
            .SingleAsync(item => item.Id == fixture.Object.Id);
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    public async Task ReconcileAsync_WhenQuarantineEnabled_QuarantinesMissingMetadata()
    {
        await using var fixture = await NativeFixture.CreateAsync("missing");
        File.Delete(fixture.TargetPath);
        var result = await fixture.CreateService(false).ReconcileAsync(NativeFixture.Now, default);

        await Assert.That(result.QuarantinedMetadataCount).IsEqualTo(1);
        var source = await fixture.Database.Context.StorageObjects.AsNoTracking()
            .SingleAsync(item => item.Id == fixture.Object.Id);
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
        await Assert.That(source.QuarantineReason).IsEqualTo("backing_object_missing");
    }

    [Test]
    public async Task ReconcileAsync_WhenDryRun_ReportsLocalOrphanWithoutQuarantine()
    {
        await using var fixture = await NativeFixture.CreateAsync("inventory");
        await File.WriteAllBytesAsync(fixture.OrphanPath, [7, 8, 9]);
        File.SetLastWriteTimeUtc(fixture.OrphanPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var result = await fixture.CreateService(true).ReconcileAsync(DateTime.UtcNow, default);

        await Assert.That(result.OrphanBackingObjectCount).IsEqualTo(1);
        await Assert.That(result.QuarantinedBackingObjectCount).IsEqualTo(0);
        await Assert.That(await File.ReadAllBytesAsync(fixture.OrphanPath)).IsEquivalentTo(new byte[] { 7, 8, 9 });
        await Assert.That(await fixture.Database.Context.Set<LocalStorageQuarantine>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ReconcileAsync_WhenOrphanQuarantineEnabled_QuarantinesLocalOrphan()
    {
        await using var fixture = await NativeFixture.CreateAsync("inventory");
        await File.WriteAllBytesAsync(fixture.OrphanPath, [7, 8, 9]);
        File.SetLastWriteTimeUtc(fixture.OrphanPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var result = await fixture.CreateService(false).ReconcileAsync(DateTime.UtcNow, default);

        await Assert.That(result.OrphanBackingObjectCount).IsEqualTo(1);
        await Assert.That(result.QuarantinedBackingObjectCount).IsEqualTo(1);
        await Assert.That(File.Exists(fixture.OrphanPath)).IsFalse();
        var work = await fixture.Database.Context.Set<LocalStorageQuarantine>().AsNoTracking().SingleAsync();
        await Assert.That(work.Outcome).IsEqualTo(LocalStorageQuarantineOutcome.Moved);
        string destination = Path.Combine(Path.GetDirectoryName(fixture.OrphanPath)!,
            ".quarantine", "reconciliation", work.Id.ToString("N"));
        await Assert.That(await File.ReadAllBytesAsync(destination)).IsEquivalentTo(new byte[] { 7, 8, 9 });
    }

    private sealed class NativeFixture : IAsyncDisposable
    {
        public static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly string _root = Directory.CreateTempSubdirectory("reconciliation-").FullName;
        private readonly ServiceProvider _metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        private readonly IStorageProviderBindingService _resolver = Substitute.For<IStorageProviderBindingService>();
        private bool _quarantineOrphans;

        private NativeFixture(SmtpSettingsDatabase database)
        {
            Database = database;
            database.Context.TenantContext = database;
            Binding = StorageProviderBinding.Local(_root);
            var provider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = _root }),
                NullLogger<LocalFileStorageProvider>.Instance);
            _resolver.ResolveAsync(Binding.Id, Arg.Any<CancellationToken>()).Returns(provider);
            Object = new StorageObject
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Image,
                FileType = null!,
                StorageProviderBindingId = Binding.Id,
                Provider = StorageProviders.Local,
                ObjectKey = "objects/target.png",
                FullName = "target.png",
                SafeDisplayName = "target.png",
                Extension = "png",
                ContentType = "image/png",
                Size = 3,
                Visibility = StorageObjectVisibilities.PublicImage,
                Purpose = StorageObjectPurposes.EventImage,
                LifecycleState = StorageObjectLifecycleStates.Active,
                CreatedAt = Now.AddDays(-40)
            };
        }

        public SmtpSettingsDatabase Database { get; }
        public StorageProviderBinding Binding { get; }
        public StorageObject Object { get; }
        public string TargetPath => Path.Combine(_root, "objects/target.png");
        public string NeighborPath => Path.Combine(_root, "objects/neighbor.png");
        public string OrphanPath => Path.Combine(_root, "orphan.bin");

        public static async Task<NativeFixture> CreateAsync(string scenario)
        {
            var fixture = new NativeFixture(await SmtpSettingsDatabase.CreateAsync());
            fixture._quarantineOrphans = scenario == "inventory";
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.TargetPath)!);
            await File.WriteAllBytesAsync(fixture.TargetPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(fixture.NeighborPath, [4, 5, 6]);
            var context = fixture.Database.Context;
            context.AddRange(fixture.Binding, fixture.Object);
            context.Add(new StorageObject
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.Database.TenantId,
                Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Image,
                FileType = null!,
                StorageProviderBindingId = fixture.Binding.Id,
                Provider = StorageProviders.Local,
                ObjectKey = "objects/neighbor.png",
                FullName = "neighbor.png",
                SafeDisplayName = "neighbor.png",
                Extension = "png",
                ContentType = "image/png",
                Size = 3,
                Visibility = StorageObjectVisibilities.PublicImage,
                Purpose = StorageObjectPurposes.EventImage,
                LifecycleState = StorageObjectLifecycleStates.Active,
                CreatedAt = Now
            });
            await context.SaveChangesAsync();
            if (scenario == "shared")
            {
                var group = new Group { Id = Guid.CreateVersion7(), FullName = "Surviving owner" };
                var actor = new Actor
                {
                    Id = Guid.CreateVersion7(),
                    Group = group,
                    GroupId = group.Id,
                    ActorTypeId = (int)ActorTypeEnum.Group,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Surviving owner" }
                };
                actor.Pii.SetProfilePicture(fixture.Object.Id, null);
                context.Add(actor);
                await context.SaveChangesAsync();
            }
            if (scenario is "held" or "missing_authority")
            {
                fixture.Object.OwningResourceKind = "registration_submission_sink";
                fixture.Object.OwningResourceId = Guid.CreateVersion7();
                fixture.Object.RegistrationContentRetentionUntilUtc = scenario == "held" ? Now.AddDays(1) : Now;
            }
            if (scenario == "eligible")
            {
                var producer = StorageProducerOperation.Create(fixture.Object.Id, fixture.Object.TenantId,
                    fixture.Binding, fixture.Object.ObjectKey!, Now.AddDays(-40));
                producer.Settle(fixture.Binding.Id, fixture.Binding.Provider, fixture.Object.ObjectKey!, null);
                context.Add(producer);
            }
            if (scenario == "invalid")
                context.Add(StorageProducerOperation.Create(fixture.Object.Id, fixture.Object.TenantId,
                    fixture.Binding, "objects/neighbor.png", Now.AddDays(-40)));
            if (scenario is not ("missing" or "inventory"))
                fixture.Object.MarkQuarantined(null, "backing_object_missing", Now.AddDays(-31));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return fixture;
        }

        public StorageReconciliationService CreateService(bool dryRun)
        {
            var repository = new StorageObjectRepository(Database.Context);
            var lifecycle = new EventResourceStorageLifecycleRepository(Database.Context);
            var unit = new EfCoreUnitOfWork(Database.Context);
            var cleanup = new Explore.Application.Services.EventResourceStorageCleanupService(
                new StorageObjectDeletionTombstoneRepository(Database.Context), _resolver,
                new Clock(), NullLogger<Explore.Application.Services.EventResourceStorageCleanupService>.Instance,
                lifecycle, unit);
            var reconciliation = new StorageReconciliationOwner(new GlobalExploreDbContextFactory(
                new PrimaryDatabaseConnectionOptions
                {
                    Role = PrimaryDatabaseRole.Global,
                    Provider = PrimaryDatabaseProvider.Sqlite,
                    Database = Database.Context.Database.GetDbConnection().DataSource
                }));
            return new StorageReconciliationService(repository, _resolver,
                new StorageProviderBindingRepository(Database.Context), reconciliation, reconciliation,
                Options.Create(new StorageReconciliationSettings
                {
                    DryRun = dryRun,
                    QuarantineMissingObjects = true,
                    DeleteQuarantinedObjects = true,
                    QuarantineOrphanLocalFiles = _quarantineOrphans
                }),
                new BusinessMetrics(_metrics.GetRequiredService<IMeterFactory>()),
                NullLogger<StorageReconciliationService>.Instance, cleanup, lifecycle, unit, TimeProvider.System);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await _metrics.DisposeAsync();
            Directory.Delete(_root, true);
        }

        private sealed class Clock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(Now);
        }
    }

}
