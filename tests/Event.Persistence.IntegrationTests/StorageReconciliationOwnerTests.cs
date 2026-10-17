using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Explore.Infrastructure;
using Explore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
public sealed class StorageReconciliationOwnerTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("live")]
    [Arguments("expiry")]
    [Arguments("cancelled")]
    public async Task LocalQuarantineBindingAwaitCannotRestartOriginalClaim(string change)
    {
        await using var isolated = new EventResourceFileUploadTests.Database();
        await isolated.InitializeAsync();
        string root = Directory.CreateTempSubdirectory("quarantine-binding-custody-").FullName;
        try
        {
            var binding = StorageProviderBinding.Local(root);
            string source = Path.Combine(root, "orphan.bin");
            byte[] bytes = [7, 8, 9];
            await File.WriteAllBytesAsync(source, bytes);
            File.SetLastWriteTimeUtc(source, LocalStorageQuarantineOwnerTests.Observed);
            var clock = new LocalStorageQuarantineOwnerTests.QuarantineClock(DateTime.UtcNow);
            var admitted = LocalStorageQuarantine.Create(binding, "orphan.bin", bytes.Length,
                File.GetLastWriteTimeUtc(source), LocalStorageQuarantineOwnerTests.Cutoff, "orphan", clock.UtcNow);
            await using var context = isolated.CreateContext();
            context.AddRange(binding, admitted);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var provider = new ObservedQuarantineProvider(new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = root }),
                NullLogger<LocalFileStorageProvider>.Instance, boundTarget: true));
            var bindings = new GatedQuarantineBindings(binding.Id, provider);
            var lifecycle = new EventResourceStorageLifecycleRepository(context);
            var unit = new EfCoreUnitOfWork(context);
            var cleanup = new EventResourceStorageCleanupService(
                new StorageObjectDeletionTombstoneRepository(context), bindings, clock,
                NullLogger<EventResourceStorageCleanupService>.Instance, lifecycle, unit);
            var owner = new StorageReconciliationOwner(new Contexts(isolated));
            await using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
            var service = new StorageReconciliationService(new StorageObjectRepository(context),
                bindings, new StorageProviderBindingRepository(context), owner, owner,
                Options.Create(new StorageReconciliationSettings
                {
                    DryRun = false,
                    QuarantineOrphanLocalFiles = true,
                    QuarantineMissingObjects = false,
                    DeleteQuarantinedObjects = false
                }),
                new BusinessMetrics(metrics.GetRequiredService<IMeterFactory>()),
                NullLogger<StorageReconciliationService>.Instance, cleanup, lifecycle, unit, clock);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var request = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var run = service.ReconcileAsync(clock.UtcNow, request.Token);
            await bindings.Entered.Task.WaitAsync(deadline.Token);
            FileStorageQuarantineInput original;
            try
            {
                await using var inspect = isolated.CreateContext();
                var claimed = await inspect.Set<LocalStorageQuarantine>().AsNoTracking()
                    .SingleAsync(row => row.Id == admitted.Id);
                original = LocalStorageQuarantineOwnerTests.CaptureClaim(claimed);
                if (change == "expiry")
                    clock.UtcNow = original.LeaseExpiresAtUtc;
                else if (change == "cancelled")
                    request.Cancel();
            }
            finally
            {
                bindings.Release.TrySetResult();
            }
            if (change == "cancelled")
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.WaitAsync(deadline.Token));
            else
                await Assert.That((await run.WaitAsync(deadline.Token)).QuarantinedBackingObjectCount)
                    .IsEqualTo(change == "live" ? 1 : 0);
            await Assert.That(provider.ExecutionCount).IsEqualTo(change == "live" ? 1 : 0);
            await Assert.That(File.Exists(source)).IsEqualTo(change != "live");
            string destination = Path.Combine(root, ".quarantine", "reconciliation", admitted.Id.ToString("N"));
            await Assert.That(File.Exists(destination)).IsEqualTo(change == "live");
            await Assert.That(await File.ReadAllBytesAsync(change == "live" ? destination : source)).IsEquivalentTo(bytes);
            if (change == "live")
            {
                await Assert.That(provider.Input).IsEqualTo(original);
                await Assert.That(provider.Token).IsEqualTo(request.Token);
            }
            else
            {
                await using var verify = isolated.CreateContext();
                var retained = await verify.Set<LocalStorageQuarantine>().SingleAsync(row => row.Id == admitted.Id);
                await Assert.That(retained.State).IsEqualTo(LocalStorageQuarantineState.Processing);
                await Assert.That(retained.ConcurrencyStamp).IsEqualTo(original.ClaimStamp);
                await Assert.That(retained.LeaseExpiresAtUtc).IsEqualTo(original.LeaseExpiresAtUtc);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class GatedQuarantineBindings(Guid bindingId, IFileStorageProvider provider)
        : IStorageProviderBindingService
    {
        private int _resolutions;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<StorageProviderBinding> CaptureAsync(string providerName, Guid tenantId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This retained-target fixture cannot capture new bindings.");

        public async Task<IFileStorageProvider> ResolveAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id != bindingId) throw new InvalidOperationException("Unexpected retained binding.");
            if (++_resolutions == 2)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return provider;
        }
    }

    private sealed class ObservedQuarantineProvider(IFileStorageInventoryProvider provider) : IFileStorageInventoryProvider
    {
        public int ExecutionCount { get; private set; }
        public FileStorageQuarantineInput? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public string Provider => provider.Provider;
        public Task<FileStorageWriteResult> WriteAsync(FileStorageWriteInput input, CancellationToken ct) => provider.WriteAsync(input, ct);
        public Task<bool> ExistsAsync(FileStorageExistsInput input, CancellationToken ct) => provider.ExistsAsync(input, ct);
        public Task<FileStorageReadResult> OpenReadAsync(FileStorageReadInput input, CancellationToken ct) => provider.OpenReadAsync(input, ct);
        public Task<FileStorageDeleteResult> DeleteAsync(FileStorageDeleteInput input, CancellationToken ct) => provider.DeleteAsync(input, ct);
        public Task<FileStorageProviderStatus> TestAsync(CancellationToken ct, bool write = false) => provider.TestAsync(ct, write);
        public IAsyncEnumerable<FileStorageInventoryObject> ListObjectsAsync(int limit, CancellationToken ct) => provider.ListObjectsAsync(limit, ct);
        public Task<FileStorageQuarantineResult> QuarantineAsync(FileStorageQuarantineInput input,
            ILocalStorageQuarantineOwner custody, TimeProvider clock, CancellationToken ct)
        {
            ExecutionCount++;
            Input = input;
            Token = ct;
            return provider.QuarantineAsync(input, custody, clock, ct);
        }
    }

    [Test]
    public async Task UnchangedObservationQuarantinesAndAdvancesOnlyItsPersistedTenant()
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        await using var before = database.CreateContext();
        long epoch = (await before.Set<EventDiscoveryRevision>().AsNoTracking()
            .SingleAsync(row => row.TenantId == source.TenantId)).DisclosureEpoch;

        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsTrue();
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsFalse();

        await using var verify = database.CreateContext();
        var current = await verify.StorageObjects.SingleAsync(row => row.Id == source.Id);
        await Assert.That(current.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
        await Assert.That(current.QuarantineReason).IsEqualTo("backing_object_missing");
        await Assert.That(current.QuarantinedAt).IsEqualTo(Now);
        await Assert.That(current.ConcurrencyStamp).IsNotEqualTo(snapshot.ConcurrencyStamp);
        await Assert.That((await verify.Set<EventDiscoveryRevision>()
            .SingleAsync(row => row.TenantId == source.TenantId)).DisclosureEpoch).IsEqualTo(epoch + 1);
        await Assert.That(await verify.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    [Arguments("stamp")]
    [Arguments("version")]
    [Arguments("tenant")]
    [Arguments("purpose")]
    [Arguments("deleted")]
    [Arguments("created")]
    public async Task ChangedCurrentSourceNeverConsumesEarlierAbsence(string change)
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        await using (var writer = database.CreateContext())
        {
            var query = writer.StorageObjects.Where(row => row.Id == source.Id);
            switch (change)
            {
                case "stamp":
                    await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                    break;
                case "version":
                    await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProviderVersionId, "replacement"));
                    break;
                case "tenant":
                    var tenant = Tenant();
                    writer.Tenants.Add(tenant);
                    await writer.SaveChangesAsync();
                    await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, tenant.Id));
                    break;
                case "purpose":
                    await query.ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.Purpose, StorageObjectPurposes.EventResource)
                        .SetProperty(row => row.OwningResourceKind, StorageOwningResourceKinds.EventResource)
                        .SetProperty(row => row.OwningResourceId, Guid.CreateVersion7()));
                    break;
                case "deleted":
                    await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
                    break;
                case "created":
                    await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, Now.AddDays(1)));
                    break;
            }
        }
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsFalse();
        await using var verify = database.CreateContext();
        var current = await verify.StorageObjects.IgnoreQueryFilters().SingleAsync(row => row.Id == source.Id);
        await Assert.That(current.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That(current.QuarantinedAt).IsNull();
    }

    [Test]
    public async Task ForgedCapturedTargetAndTenantAreNotMutationAuthority()
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        foreach (var mismatch in new[]
        {
            snapshot with { ObjectKey = "another-key" },
            snapshot with { StorageProviderBindingId = Guid.CreateVersion7() },
            snapshot with { Provider = StorageProviders.S3Compatible },
            snapshot with { ProviderVersionId = "another-version" },
            snapshot with { TenantId = Guid.CreateVersion7() },
            snapshot with { OwningResourceId = Guid.CreateVersion7() },
            snapshot with { ActorId = Guid.CreateVersion7() },
            snapshot with { EligibilityCutoffUtc = source.CreatedAt.AddSeconds(-1) }
        })
            await Assert.That(await owner.TryQuarantineMissingBackingAsync(mismatch, Now, default)).IsFalse();
    }

    [Test]
    [Arguments(StorageObjectLifecycleStates.Pending)]
    [Arguments(StorageObjectLifecycleStates.Quarantined)]
    [Arguments(StorageObjectLifecycleStates.DeleteRequested)]
    [Arguments(StorageObjectLifecycleStates.Deleted)]
    public async Task OnlyActiveMetadataCanBecomeAMissingBackingCandidate(string lifecycle)
    {
        var source = await SeedAsync();
        await using (var writer = database.CreateContext())
            await writer.StorageObjects.Where(row => row.Id == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LifecycleState, lifecycle));
        var owner = new StorageReconciliationOwner(new Contexts(database));
        await Assert.That((await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Any(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    public async Task ResourcePurposeAndOwnershipNeverEnterMetadataCustody()
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        await using (var writer = database.CreateContext())
        {
            var query = writer.StorageObjects.Where(row => row.Id == source.Id);
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Purpose, StorageObjectPurposes.EventResource)
                .SetProperty(row => row.OwningResourceKind, StorageOwningResourceKinds.EventResource)
                .SetProperty(row => row.OwningResourceId, Guid.CreateVersion7()));
        }
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsFalse();
        await Assert.That((await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Any(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    [Arguments("session-unsettled")]
    [Arguments("session-mismatch")]
    [Arguments("producer-unsettled")]
    public async Task OldIneligibleCustodyDoesNotStarveTheBoundedCandidatePage(string custody)
    {
        await using var isolated = new EventResourceFileUploadTests.Database();
        await isolated.InitializeAsync();
        var blocked = await SeedAsync(isolated);
        var eligible = await SeedAsync(isolated);
        await using (var writer = isolated.CreateContext())
        {
            await writer.StorageObjects.Where(row => row.Id == blocked.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, Now.AddDays(-3)));
            if (custody == "producer-unsettled")
            {
                var binding = await writer.StorageProviderBindings.SingleAsync(row => row.Id == blocked.StorageProviderBindingId);
                writer.Set<StorageProducerOperation>().Add(StorageProducerOperation.Create(
                    blocked.Id, blocked.TenantId, binding, blocked.ObjectKey!, Now));
            }
            else
            {
                var session = new StorageUploadSession
                {
                    Id = blocked.Id,
                    StorageObjectId = blocked.Id,
                    TenantId = blocked.TenantId,
                    Provider = blocked.Provider,
                    StorageProviderBindingId = blocked.StorageProviderBindingId,
                    ObjectKey = custody == "session-mismatch" ? "other-key" : blocked.ObjectKey,
                    Purpose = blocked.Purpose,
                    Visibility = blocked.Visibility,
                    Status = StorageUploadSessionStates.Uploading,
                    ContentType = "text/plain",
                    SafeDisplayName = "custody.txt",
                    CreatedAt = Now,
                    ExpiresAt = Now.AddHours(1),
                    UploadStartedAt = Now,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                if (custody == "session-mismatch")
                    session.RecordProducerSettlement(blocked.Id, blocked.StorageProviderBindingId!.Value, session.ObjectKey!, null);
                writer.StorageUploadSessions.Add(session);
            }
            await writer.SaveChangesAsync();
        }
        var candidates = await new StorageReconciliationOwner(new Contexts(isolated))
            .ListMissingBackingCandidatesAsync(Now, 1, default);
        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates.Single().Id).IsEqualTo(eligible.Id);
    }

    [Test]
    public async Task RetainedEvidenceAddedAfterObservationPreservesTheActiveSource()
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        await using (var writer = database.CreateContext())
        {
            var document = await writer.StorageObjects.SingleAsync(row => row.Id == source.Id);
            var organization = new Organization
            {
                Id = Guid.CreateVersion7(),
                Pii = new OrganizationPii { FullName = "Custody evidence" },
                CreatedAt = Now
            };
            var participation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = source.TenantId,
                Tenant = null!,
                OrganizationId = organization.Id,
                Organization = organization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!,
                CreatedAt = Now
            };
            writer.OrganizationTenantEvidence.Add(OrganizationTenantEvidence.CreatePending(participation, document));
            await writer.SaveChangesAsync();
        }
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsFalse();
        await Assert.That((await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Any(row => row.Id == source.Id)).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == source.Id)).LifecycleState)
            .IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProducerTransferRequiresOneSettledExactTarget(bool settled, bool mismatch)
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        await using (var writer = database.CreateContext())
        {
            var binding = await writer.StorageProviderBindings.SingleAsync(row => row.Id == source.StorageProviderBindingId);
            var producer = StorageProducerOperation.Create(source.Id, source.TenantId, binding,
                mismatch ? "another-target" : source.ObjectKey!, Now);
            if (settled)
                producer.Settle(binding.Id, binding.Provider, producer.ObjectKey, source.ProviderVersionId);
            writer.Set<StorageProducerOperation>().Add(producer);
            await writer.SaveChangesAsync();
        }
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsEqualTo(settled && !mismatch);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task UnsettledOrMismatchedSessionCannotTransferCustody(bool settled, bool mismatch)
    {
        var source = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default))
            .Single(row => row.Id == source.Id);
        await using (var writer = database.CreateContext())
        {
            var session = new StorageUploadSession
            {
                Id = source.Id,
                StorageObjectId = source.Id,
                TenantId = source.TenantId,
                Provider = source.Provider,
                StorageProviderBindingId = source.StorageProviderBindingId,
                ObjectKey = mismatch ? "different-key" : source.ObjectKey,
                Purpose = source.Purpose,
                Visibility = source.Visibility,
                Status = StorageUploadSessionStates.Uploading,
                ContentType = "text/plain",
                SafeDisplayName = "custody.txt",
                CreatedAt = Now,
                ExpiresAt = Now.AddHours(1),
                UploadStartedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            if (settled)
                session.RecordProducerSettlement(source.Id, source.StorageProviderBindingId!.Value,
                    session.ObjectKey!, source.ProviderVersionId);
            writer.StorageUploadSessions.Add(session);
            await writer.SaveChangesAsync();
        }
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default))
            .IsEqualTo(settled && !mismatch);
    }

    [Test]
    public async Task TombstoneAndProducerCustodyExcludeAnActiveMetadataRow()
    {
        var tombstoned = await SeedAsync();
        var producing = await SeedAsync();
        var owner = new StorageReconciliationOwner(new Contexts(database));
        var snapshots = await owner.ListMissingBackingCandidatesAsync(Now, 500, default);
        await using (var writer = database.CreateContext())
        {
            writer.StorageObjectDeletionTombstones.Add(StorageObjectDeletionTombstone.Create(
                tombstoned.Id, tombstoned.TenantId, tombstoned.Provider,
                tombstoned.StorageProviderBindingId!.Value, tombstoned.ObjectKey!, null, true, Now));
            var binding = await writer.StorageProviderBindings.SingleAsync(row => row.Id == producing.StorageProviderBindingId);
            writer.Set<StorageProducerOperation>().Add(StorageProducerOperation.Create(
                producing.Id, producing.TenantId, binding, producing.ObjectKey!, Now));
            await writer.SaveChangesAsync();
        }
        foreach (var source in new[] { tombstoned, producing })
            await Assert.That(await owner.TryQuarantineMissingBackingAsync(
                snapshots.Single(row => row.Id == source.Id), Now, default)).IsFalse();
        var candidates = await owner.ListMissingBackingCandidatesAsync(Now, 500, default);
        await Assert.That(candidates.Any(row => row.Id == tombstoned.Id || row.Id == producing.Id)).IsFalse();
    }

    private async Task<StorageObject> SeedAsync(EventResourceFileUploadTests.Database? isolated = null)
    {
        await using var context = (isolated ?? database).CreateContext();
        var tenant = Tenant();
        var binding = StorageProviderBinding.Local(Path.GetFullPath("reconciliation-native-storage"));
        context.Tenants.Add(tenant);
        context.StorageProviderBindings.Add(binding);
        await context.SaveChangesAsync();
        var source = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = binding.Id,
            ObjectKey = $"custody/{Guid.CreateVersion7():N}",
            FullName = "custody.txt",
            SafeDisplayName = "custody.txt",
            Extension = ".txt",
            Size = 17,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedAt = Now.AddDays(-1),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.StorageObjects.Add(source);
        await context.SaveChangesAsync();
        return source;
    }

    private static Tenant Tenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Metadata custody",
        Slug = $"custody-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = Now
    };

    private sealed class Contexts(EventResourceFileUploadTests.Database database) : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(database.CreateContext());
    }
}
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed class PostgresStorageReconciliationOwnerTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("column")]
    [Arguments("public")]
    [Arguments("membership")]
    [Arguments("overload")]
    [Arguments("missing-edge")]
    [Arguments("attributes")]
    [Arguments("inherited")]
    [Arguments("grant-option")]
    public async Task ExactPrivilegeDriftRejectsBothEntriesAndReinstallWithoutRepair(string drift)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var migration = fixture.CreateDbContext();
        string list = PostgresStorageReconciliationContract.ListFunctionSql(migration);
        string quarantine = PostgresStorageReconciliationContract.QuarantineFunctionSql(migration);
        string runtime = "\"" + fixture.NativeQueueEnrollment.RuntimeLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string global = "\"" + fixture.NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string inherited = $"storage_drift_{Guid.CreateVersion7():N}";
        (string inject, string restore) = drift switch
        {
            "column" => ($"GRANT UPDATE (object_key) ON {rows.StorageTable} TO {PostgresStorageReconciliationContract.OwnerRole}",
                $"REVOKE UPDATE (object_key) ON {rows.StorageTable} FROM {PostgresStorageReconciliationContract.OwnerRole}"),
            "public" => ($"GRANT SELECT (id) ON {rows.StorageTable} TO PUBLIC",
                $"REVOKE SELECT (id) ON {rows.StorageTable} FROM PUBLIC"),
            "membership" => ($"GRANT {PostgresStorageReconciliationContract.GlobalRole} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {PostgresStorageReconciliationContract.GlobalRole} FROM {runtime}"),
            "overload" => ($"CREATE FUNCTION {list}() RETURNS integer LANGUAGE sql AS 'SELECT 1'", $"DROP FUNCTION {list}()"),
            "missing-edge" => ($"REVOKE {PostgresStorageReconciliationContract.GlobalRole} FROM {global}",
                $"GRANT {PostgresStorageReconciliationContract.GlobalRole} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "attributes" => ($"ALTER ROLE {PostgresStorageReconciliationContract.OwnerRole} INHERIT",
                $"ALTER ROLE {PostgresStorageReconciliationContract.OwnerRole} NOINHERIT"),
            "inherited" => ($"""
                CREATE ROLE "{inherited}" NOLOGIN;
                GRANT SELECT (id) ON {rows.StorageTable} TO "{inherited}";
                GRANT "{inherited}" TO {PostgresStorageReconciliationContract.GlobalRole} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
                """, $"""
                REVOKE "{inherited}" FROM {PostgresStorageReconciliationContract.GlobalRole};
                REVOKE SELECT (id) ON {rows.StorageTable} FROM "{inherited}";
                DROP ROLE "{inherited}";
                """),
            "grant-option" => ($"GRANT SELECT (id) ON {rows.StorageTable} TO {PostgresStorageReconciliationContract.OwnerRole} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR SELECT (id) ON {rows.StorageTable} FROM {PostgresStorageReconciliationContract.OwnerRole}"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(inject);
        try
        {
            await using var caller = await rows.Global.CreateDbContextAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await PostgresStorageReconciliationContract.ValidateReadinessAsync(caller, fixture.NativeQueueEnrollment));
            foreach (string statement in new[]
            {
                $"SELECT * FROM {list}(NULL::timestamp with time zone, 0)",
                $"SELECT {quarantine}('{{\"Id\":\"not-a-uuid\"}}'::jsonb, NULL::timestamp with time zone)"
            })
            {
                PostgresException? entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await caller.Database.ExecuteSqlRawAsync(statement));
                await Assert.That(entry?.SqlState).IsEqualTo(drift == "missing-edge" ? "42501" : "55000");
            }
            PostgresException? reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresStorageReconciliationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(reinstall?.SqlState).IsEqualTo("55000");
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await PostgresStorageReconciliationContract.ValidateReadinessAsync(caller, fixture.NativeQueueEnrollment));
            await using var verify = fixture.CreateDbContext();
            await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == rows.A.Id)).LifecycleState)
                .IsEqualTo(StorageObjectLifecycleStates.Active);
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(restore);
        }
        await PostgresStorageReconciliationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await PostgresStorageReconciliationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task ExecuteOnlyGlobalCustodyQuarantinesExactTenantDespiteOrdinaryImageReference()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var global = await rows.Global.CreateDbContextAsync();
        await Assert.ThrowsAsync<PostgresException>(async () => await global.Database.ExecuteSqlRawAsync(
            $"SELECT * FROM {rows.StorageTable}"));
        await Assert.ThrowsAsync<PostgresException>(async () => await global.Database.ExecuteSqlRawAsync(
            $"UPDATE {rows.StorageTable} SET lifecycle_state = '{StorageObjectLifecycleStates.Quarantined}'"));
        await Assert.ThrowsAsync<PostgresException>(async () => await global.Database.ExecuteSqlRawAsync(
            $"SET ROLE {PostgresStorageReconciliationContract.OwnerRole}"));
        await Assert.ThrowsAsync<PostgresException>(async () => await global.Database.ExecuteSqlRawAsync(
            $"SET ROLE {PostgresStorageReconciliationContract.MigratorRole}"));
        await PostgresStorageReconciliationContract.ValidateReadinessAsync(global, fixture.NativeQueueEnrollment);
        var owner = new StorageReconciliationOwner(rows.Global, fixture.NativeQueueEnrollment);
        var candidates = await owner.ListMissingBackingCandidatesAsync(Now, 500, default);
        await Assert.That(candidates.Select(row => row.Id)).IsEquivalentTo(new[] { rows.A.Id, rows.B.Id });
        var snapshot = candidates.Single(row => row.Id == rows.A.Id);
        await using var before = fixture.CreateDbContext();
        long epochA = (await before.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == rows.A.TenantId)).DisclosureEpoch;
        long epochB = (await before.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == rows.B.TenantId)).DisclosureEpoch;

        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsTrue();
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsFalse();
        await using var verify = fixture.CreateDbContext();
        var source = await verify.StorageObjects.SingleAsync(row => row.Id == rows.A.Id);
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Quarantined);
        await Assert.That(source.QuarantineReason).IsEqualTo("backing_object_missing");
        await Assert.That(source.ConcurrencyStamp).IsNotEqualTo(snapshot.ConcurrencyStamp);
        await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == rows.B.Id)).LifecycleState)
            .IsEqualTo(StorageObjectLifecycleStates.Active);
        await Assert.That((await verify.Set<EventDiscoveryRevision>()
            .SingleAsync(row => row.TenantId == rows.A.TenantId)).DisclosureEpoch).IsEqualTo(epochA + 1);
        await Assert.That((await verify.Set<EventDiscoveryRevision>()
            .SingleAsync(row => row.TenantId == rows.B.TenantId)).DisclosureEpoch).IsEqualTo(epochB);
        await Assert.That(await verify.Events.AnyAsync(row => row.FeaturedImageId == rows.A.Id)).IsTrue();
    }

    [Test]
    public async Task NativeCurrentTargetReplacementAndCutoffMismatchRejectEarlierAbsence()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var owner = new StorageReconciliationOwner(rows.Global, fixture.NativeQueueEnrollment);
        var snapshot = (await owner.ListMissingBackingCandidatesAsync(Now, 500, default)).Single(row => row.Id == rows.A.Id);
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(
            snapshot with { TenantId = rows.B.TenantId }, Now, default)).IsFalse();
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(
            snapshot with { EligibilityCutoffUtc = rows.A.CreatedAt.AddSeconds(-1) }, Now, default)).IsFalse();
        await using (var writer = fixture.CreateDbContext())
            await writer.StorageObjects.Where(row => row.Id == snapshot.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.ProviderVersionId, "replacement")
                    .SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
        await Assert.That(await owner.TryQuarantineMissingBackingAsync(snapshot, Now, default)).IsFalse();
        await using var verify = fixture.CreateDbContext();
        var source = await verify.StorageObjects.SingleAsync(row => row.Id == snapshot.Id);
        await Assert.That(source.ProviderVersionId).IsEqualTo("replacement");
        await Assert.That(source.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Active);
    }

    [Test]
    public async Task RuntimeAndUnentitledGlobalCannotEnterMetadataCapability()
    {
        await using var rows = await Rows.CreateAsync(fixture, grantCapability: false);
        var owner = new StorageReconciliationOwner(rows.Global, fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await owner.ListMissingBackingCandidatesAsync(Now, 500, default));
        await using var runtime = rows.Authentication.Context(rows.A.TenantId);
        await Assert.ThrowsAsync<PostgresException>(async () => await runtime.Database.ExecuteSqlRawAsync(
            $"SELECT * FROM {PostgresStorageReconciliationContract.ListFunctionSql(runtime)}({{0}}, 5)", Now));
        await Assert.ThrowsAsync<PostgresException>(async () => await runtime.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresStorageReconciliationContract.QuarantineFunctionSql(runtime)}(jsonb_build_object(), {{0}})", Now));
    }

    [Test]
    [Arguments("policy")]
    [Arguments("function")]
    [Arguments("column")]
    public async Task AlteredNativeAuthorityFailsClosedWithoutChangingSource(string alteration)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var migration = fixture.CreateDbContext();
        try
        {
            switch (alteration)
            {
                case "policy":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"ALTER POLICY storage_reconciliation_custody ON {rows.StorageTable} USING (false)");
                    break;
                case "function":
                    await migration.Database.ExecuteSqlRawAsync($"""
                        CREATE OR REPLACE FUNCTION {PostgresStorageReconciliationContract.QuarantineFunctionSql(migration)}
                            (p_snapshot jsonb, p_now timestamp with time zone) RETURNS boolean
                            LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS 'SELECT true';
                        """);
                    break;
                case "column":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"GRANT UPDATE (object_key) ON {rows.StorageTable} TO {PostgresStorageReconciliationContract.OwnerRole}");
                    break;
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new StorageReconciliationOwner(rows.Global, fixture.NativeQueueEnrollment)
                    .ListMissingBackingCandidatesAsync(Now, 500, default));
            await using var verify = fixture.CreateDbContext();
            await Assert.That((await verify.StorageObjects.SingleAsync(row => row.Id == rows.A.Id)).LifecycleState)
                .IsEqualTo(StorageObjectLifecycleStates.Active);
        }
        finally
        {
            if (alteration == "column")
                await migration.Database.ExecuteSqlRawAsync(
                    $"REVOKE UPDATE (object_key) ON {rows.StorageTable} FROM {PostgresStorageReconciliationContract.OwnerRole}");
            await PostgresStorageReconciliationContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        }
    }

    private sealed class Rows : IAsyncDisposable
    {
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Authentication { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Global { get; private set; } = null!;
        internal StorageObject A { get; private set; } = null!;
        internal StorageObject B { get; private set; } = null!;
        internal string StorageTable { get; private set; } = null!;
        private string _connection = null!;

        internal static async Task<Rows> CreateAsync(PostgreSqlContainerFixture fixture, bool grantCapability = true)
        {
            var rows = new Rows
            {
                Authentication = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture)
            };
            await using var seed = fixture.CreateDbContext();
            var binding = StorageProviderBinding.Local(Path.GetFullPath("native-reconciliation-storage"));
            seed.StorageProviderBindings.Add(binding);
            await seed.SaveChangesAsync();
            rows.A = Source(rows.Authentication.A, binding.Id);
            rows.B = Source(rows.Authentication.B, binding.Id);
            seed.StorageObjects.AddRange(rows.A, rows.B);
            await seed.SaveChangesAsync();
            var sourceEvent = await seed.Events.SingleAsync(row => row.TenantId == rows.A.TenantId);
            sourceEvent.FeaturedImageId = rows.A.Id;
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(StorageObject))!;
            rows.StorageTable = seed.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            await PostgresStorageReconciliationContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
            var options = fixture.GetNativeQueueOptions(
                grantCapability ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime, rows.Authentication.Schema)
                with
            { Role = PrimaryDatabaseRole.Global };
            rows._connection = PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString;
            rows.Global = new GlobalExploreDbContextFactory(options, poolSize: 1);
            return rows;
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connection);
            NpgsqlConnection.ClearPool(pool);
            await Authentication.DisposeAsync();
        }

        private static StorageObject Source(Guid tenantId, Guid bindingId) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = bindingId,
            ObjectKey = $"native/{Guid.CreateVersion7():N}",
            FullName = "custody.txt",
            SafeDisplayName = "custody.txt",
            Extension = ".txt",
            Size = 19,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedAt = Now.AddDays(-1),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }
}
