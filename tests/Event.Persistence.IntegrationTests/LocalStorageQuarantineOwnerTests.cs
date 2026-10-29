using Explore.Persistence.Services;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Explore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
public sealed class LocalStorageQuarantineOwnerTests(EventResourceFileUploadTests.Database database)
{
    internal static readonly DateTime Observed = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(7);
    internal static readonly DateTime Cutoff = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ReadOnlyInspectionAndRepeatedAdmissionKeepOnePhysicalCustodyRow()
    {
        var binding = await SeedBindingAsync();
        var alias = StorageProviderBinding.Local(binding.LocalRootPath! + Path.DirectorySeparatorChar + ".");
        await using (var seed = database.CreateContext())
        {
            seed.Add(alias);
            await seed.SaveChangesAsync();
        }
        var owner = Owner();
        await Assert.That(await owner.IsLocalOrphanAsync(binding.Id, "objects/orphan", default)).IsTrue();
        await using (var verify = database.CreateContext())
            await Assert.That(await verify.Set<LocalStorageQuarantine>()
                .AnyAsync(row => row.ProviderBindingId == binding.Id)).IsFalse();
        var first = await AdmitAsync(owner, binding.Id, "objects/orphan");
        var repeated = await AdmitAsync(owner, alias.Id, "objects\\orphan");
        await Assert.That(first).IsNotNull();
        await Assert.That(repeated!.Id).IsEqualTo(first!.Id);
        await Assert.That(await owner.IsLocalOrphanAsync(alias.Id, "objects/orphan", default)).IsFalse();
        await using var inspect = database.CreateContext();
        await Assert.That(await inspect.Set<LocalStorageQuarantine>().CountAsync(row => row.PhysicalPathHash == first.PhysicalPathHash)).IsEqualTo(1);
    }

    [Test]
    public async Task FileTimestampRoundTripRetainsEveryTickAndUtcKind()
    {
        var binding = await SeedBindingAsync();
        var work = (await AdmitAsync(Owner(), binding.Id, "objects/precision"))!;
        var claim = (await Owner().ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default)).Single(row => row.Id == work.Id);
        await Assert.That(claim.ObservedLastModifiedUtc.Ticks).IsEqualTo(Observed.Ticks);
        await Assert.That(claim.ObservedLastModifiedUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(claim.ModifiedBeforeUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(claim.AdmittedAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(claim.LeaseExpiresAtUtc!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(await Owner().CompleteLocalQuarantineAsync(claim.Id, claim.ConcurrencyStamp,
            LocalStorageQuarantineOutcome.Moved, default)).IsTrue();
        await using var verify = database.CreateContext();
        var completed = await verify.Set<LocalStorageQuarantine>().SingleAsync(row => row.Id == claim.Id);
        await Assert.That(completed.SettledAtUtc).IsNotNull();
        await Assert.That(completed.SettledAtUtc!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task CompetingIndependentAdmissionsRetainOnePhysicalCustodyIdentity()
    {
        var binding = await SeedBindingAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var contexts = new SimultaneousQuarantineContexts(
            _ => Task.FromResult(database.CreateContext()));
        var owner = new StorageReconciliationOwner(contexts);
        var attempts = await Task.WhenAll(
            owner.AdmitLocalQuarantineAsync(binding.Id, "objects/competing", 19, Observed, Cutoff, "orphan", deadline.Token),
            owner.AdmitLocalQuarantineAsync(binding.Id, "objects\\competing", 19, Observed, Cutoff, "orphan", deadline.Token));
        await Assert.That(attempts[0]).IsNotNull();
        await Assert.That(attempts[1]!.Id).IsEqualTo(attempts[0]!.Id);
        await using var verify = database.CreateContext();
        await Assert.That(await verify.Set<LocalStorageQuarantine>()
            .CountAsync(row => row.PhysicalPathHash == attempts[0]!.PhysicalPathHash)).IsEqualTo(1);
    }

    [Test]
    [Arguments("metadata")]
    [Arguments("deleted")]
    [Arguments("producer")]
    [Arguments("tombstone")]
    [Arguments(StorageUploadSessionStates.Reserved)]
    [Arguments(StorageUploadSessionStates.Uploading)]
    [Arguments(StorageUploadSessionStates.Finalized)]
    [Arguments(StorageUploadSessionStates.Canceled)]
    [Arguments(StorageUploadSessionStates.Failed)]
    [Arguments(StorageUploadSessionStates.Expired)]
    public async Task EveryPhysicalCustodianBlocksAcrossAnOverlappingRoot(string kind)
    {
        var parent = await SeedBindingAsync();
        var nested = StorageProviderBinding.Local(Path.Combine(parent.LocalRootPath!, "nested"));
        var tenant = Tenant();
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(nested, tenant);
            await seed.SaveChangesAsync();
            await SeedCustodyAsync(seed, tenant.Id, nested, "owned", kind);
        }
        await Assert.That(await Owner().IsLocalOrphanAsync(parent.Id, "nested/owned", default)).IsFalse();
        await Assert.That(await AdmitAsync(Owner(), parent.Id, "nested/owned")).IsNull();
    }

    [Test]
    public async Task EnclosingAndEquivalentRootsCannotHideCustodyOrConfuseDistinctKeys()
    {
        var parent = await SeedBindingAsync();
        var nested = StorageProviderBinding.Local(Path.Combine(parent.LocalRootPath!, "nested"));
        var equivalent = StorageProviderBinding.Local(parent.LocalRootPath! + Path.DirectorySeparatorChar + ".");
        var tenant = Tenant();
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(nested, equivalent, tenant);
            await seed.SaveChangesAsync();
            await SeedCustodyAsync(seed, tenant.Id, parent, "nested\\owned", "deleted");
        }
        await Assert.That(await Owner().IsLocalOrphanAsync(nested.Id, "owned", default)).IsFalse();
        await Assert.That(await Owner().IsLocalOrphanAsync(equivalent.Id, "nested/owned", default)).IsFalse();
        await Assert.That(await Owner().IsLocalOrphanAsync(nested.Id, "different", default)).IsTrue();
    }

    [Test]
    public async Task HostCaseSemanticsPreserveLinuxDistinctionAndConservativelyBlockAliasesElsewhere()
    {
        var parent = await SeedBindingAsync();
        var upper = StorageProviderBinding.Local(Path.Combine(parent.LocalRootPath!, "Mixed"));
        var lower = StorageProviderBinding.Local(Path.Combine(parent.LocalRootPath!, "mixed"));
        var tenant = Tenant();
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(upper, lower, tenant);
            await seed.SaveChangesAsync();
            await SeedCustodyAsync(seed, tenant.Id, upper, "FILE", "metadata");
        }
        await Assert.That(await Owner().IsLocalOrphanAsync(lower.Id, "file", default)).IsEqualTo(OperatingSystem.IsLinux());
    }

    [Test]
    public async Task NonAsciiCaseUncertaintyNeverGrantsMoveAuthority()
    {
        var binding = await SeedBindingAsync();
        await Assert.That(await Owner().IsLocalOrphanAsync(binding.Id, "objects/\u00e9", default))
            .IsEqualTo(OperatingSystem.IsLinux());
        var work = await AdmitAsync(Owner(), binding.Id, "objects/\u00e9");
        await Assert.That(work is not null).IsEqualTo(OperatingSystem.IsLinux());
    }

    [Test]
    [Arguments("folder./owned", "folder/owned")]
    [Arguments("folder /owned", "folder/owned")]
    [Arguments("owned.", "owned")]
    [Arguments("owned ", "owned")]
    [Arguments("longna~1", "longname")]
    public async Task UncertainWindowsRetainedKeyAliasesCannotEstablishAbsence(string retained, string candidate)
    {
        var binding = await SeedBindingAsync();
        var tenant = Tenant();
        await using (var seed = database.CreateContext())
        {
            seed.Add(tenant);
            await seed.SaveChangesAsync();
            await SeedCustodyAsync(seed, tenant.Id, binding, retained, "metadata");
        }
        await Assert.That(await Owner().IsLocalOrphanAsync(binding.Id, candidate, default))
            .IsEqualTo(!OperatingSystem.IsWindows());
    }

    [Test]
    public async Task AdmissionIndependentlyRejectsAnObservationNewerThanGrace()
    {
        var binding = await SeedBindingAsync();
        await Assert.ThrowsAsync<ArgumentException>(async () => await Owner().AdmitLocalQuarantineAsync(
            binding.Id, "objects/young", 19, Cutoff.AddTicks(1), Cutoff, "orphan", default));
        await using var verify = database.CreateContext();
        await Assert.That(await verify.Set<LocalStorageQuarantine>().AnyAsync(row => row.ProviderBindingId == binding.Id)).IsFalse();
    }

    [Test]
    public async Task ExpiredReclaimFencesEveryOldSettlementWithoutTimingLuck()
    {
        var binding = await SeedBindingAsync();
        var old = LocalStorageQuarantine.Create(binding, "objects/reclaimed", 19, Observed, Cutoff, "orphan", Cutoff);
        Guid oldStamp = Guid.CreateVersion7();
        old.TryClaim(old.ConcurrencyStamp, oldStamp, Cutoff, Cutoff.AddMinutes(1));
        await using (var seed = database.CreateContext())
        {
            seed.Add(old);
            await seed.SaveChangesAsync();
        }
        var claim = (await Owner().ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default)).Single(row => row.Id == old.Id);
        await Assert.That(claim.ConcurrencyStamp).IsNotEqualTo(oldStamp);
        await Assert.That(await Owner().CompleteLocalQuarantineAsync(old.Id, oldStamp, LocalStorageQuarantineOutcome.Moved, default)).IsFalse();
        await Assert.That(await Owner().BlockLocalQuarantineAsync(old.Id, oldStamp, default)).IsFalse();
        await Assert.That(await Owner().RetryLocalQuarantineAsync(old.Id, oldStamp, DateTime.UtcNow.AddHours(1), default)).IsFalse();
        await Assert.That(await Owner().RetryLocalQuarantineAsync(old.Id, claim.ConcurrencyStamp, DateTime.UtcNow.AddHours(1), default)).IsTrue();
        await Assert.That((await Owner().ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default))
            .Any(row => row.Id == old.Id)).IsFalse();
        await using var verify = database.CreateContext();
        var pending = await verify.Set<LocalStorageQuarantine>().SingleAsync(row => row.Id == old.Id);
        await Assert.That(pending.State).IsEqualTo(LocalStorageQuarantineState.Pending);
        await Assert.That(pending.NextAttemptAtUtc!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(pending.CanonicalPhysicalPath).IsEqualTo(old.CanonicalPhysicalPath);
    }

    [Test]
    public async Task NewlyPresentCustodyBlocksAClaimBeforePhysicalHandoff()
    {
        var binding = await SeedBindingAsync();
        var work = (await AdmitAsync(Owner(), binding.Id, "objects/block"))!;
        var tenant = Tenant();
        await using (var seed = database.CreateContext())
        {
            seed.Add(tenant);
            await seed.SaveChangesAsync();
            await SeedCustodyAsync(seed, tenant.Id, binding, work.ObjectKey, "tombstone");
        }
        await Assert.That((await Owner().ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default))
            .Any(row => row.Id == work.Id)).IsFalse();
        await using var verify = database.CreateContext();
        var blocked = await verify.Set<LocalStorageQuarantine>().SingleAsync(row => row.Id == work.Id);
        await Assert.That(blocked.State).IsEqualTo(LocalStorageQuarantineState.Blocked);
        await Assert.That(blocked.Outcome).IsEqualTo(LocalStorageQuarantineOutcome.Blocked);
        await Assert.That(blocked.SettledAtUtc!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task ExecutionValidationRequiresEveryOriginalClaimFact()
    {
        var binding = await SeedBindingAsync();
        var work = (await AdmitAsync(Owner(), binding.Id, "objects/execution"))!;
        var claim = (await Owner().ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default))
            .Single(row => row.Id == work.Id);
        var input = CaptureClaim(claim);
        await Assert.That(await Owner().IsLocalQuarantineClaimCurrentAsync(input, default)).IsTrue();
        foreach (var forged in new[]
        {
            input with { QuarantineId = Guid.CreateVersion7() },
            input with { ClaimStamp = Guid.CreateVersion7() },
            input with { ProviderBindingId = Guid.CreateVersion7() },
            input with { ObjectKey = "objects/other" },
            input with { CanonicalPhysicalPath = input.CanonicalPhysicalPath + "-other" },
            input with { PhysicalPathHash = new string('0', 64) },
            input with { ExpectedSize = input.ExpectedSize + 1 },
            input with { ExpectedLastModifiedUtc = input.ExpectedLastModifiedUtc.AddTicks(1) },
            input with { LeaseExpiresAtUtc = input.LeaseExpiresAtUtc.AddTicks(1) }
        })
            await Assert.That(await Owner().IsLocalQuarantineClaimCurrentAsync(forged, default)).IsFalse();

        await Assert.That(await Owner().CompleteLocalQuarantineAsync(claim.Id, claim.ConcurrencyStamp,
            LocalStorageQuarantineOutcome.AlreadyAbsent, default)).IsTrue();
        await Assert.That(await Owner().IsLocalQuarantineClaimCurrentAsync(input, default)).IsFalse();
    }

    [Test]
    [Arguments("live")]
    [Arguments("expiry")]
    [Arguments("cancelled")]
    [Arguments("changed-size")]
    [Arguments("changed-time")]
    [Arguments("changed-stamp")]
    [Arguments("new-custody")]
    public async Task FileMoveRevalidatesOriginalCustodyAndSnapshotAfterAwait(string change)
    {
        string root = Directory.CreateTempSubdirectory("quarantine-final-custody-").FullName;
        try
        {
            var binding = StorageProviderBinding.Local(root);
            string source = Path.Combine(root, "orphan.bin");
            byte[] bytes = [7, 8, 9];
            await File.WriteAllBytesAsync(source, bytes);
            File.SetLastWriteTimeUtc(source, Observed);
            var now = DateTime.UtcNow;
            var claim = LocalStorageQuarantine.Create(binding, "orphan.bin", bytes.Length,
                File.GetLastWriteTimeUtc(source), Cutoff, "orphan", now);
            claim.TryClaim(claim.ConcurrencyStamp, Guid.CreateVersion7(), now, now.AddMinutes(5));
            await using (var seed = database.CreateContext())
            {
                seed.AddRange(binding, claim);
                await seed.SaveChangesAsync();
            }
            var input = CaptureClaim(claim);
            var clock = new QuarantineClock(now);
            var contexts = new GatedQuarantineContexts(database);
            var owner = new StorageReconciliationOwner(contexts);
            var provider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = root }),
                NullLogger<LocalFileStorageProvider>.Instance, boundTarget: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var request = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var effect = provider.QuarantineAsync(input, owner, clock, request.Token);
            await contexts.Entered.Task.WaitAsync(deadline.Token);
            try
            {
                if (change == "expiry")
                    clock.UtcNow = input.LeaseExpiresAtUtc;
                else if (change == "cancelled")
                    request.Cancel();
                else if (change == "changed-size")
                    await File.WriteAllBytesAsync(source, [1]);
                else if (change == "changed-time")
                    File.SetLastWriteTimeUtc(source, input.ExpectedLastModifiedUtc.AddDays(1));
                else if (change == "changed-stamp")
                {
                    await using var writer = database.CreateContext();
                    await writer.Set<LocalStorageQuarantine>().Where(row => row.Id == claim.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                }
                else if (change == "new-custody")
                {
                    await using var writer = database.CreateContext();
                    var tenant = Tenant();
                    writer.Add(tenant);
                    await writer.SaveChangesAsync();
                    await SeedCustodyAsync(writer, tenant.Id, binding, claim.ObjectKey, "metadata");
                }
            }
            finally
            {
                contexts.Release.TrySetResult();
            }
            string destination = Path.Combine(root, ".quarantine", "reconciliation", claim.Id.ToString("N"));
            if (change == "cancelled")
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await effect.WaitAsync(deadline.Token));
            else
                await Assert.That((await effect.WaitAsync(deadline.Token)).Outcome).IsEqualTo(
                    change == "live" ? FileStorageQuarantineOutcome.Moved : FileStorageQuarantineOutcome.Blocked);
            await Assert.That(contexts.ObservedToken).IsEqualTo(request.Token);
            await Assert.That(File.Exists(destination)).IsEqualTo(change == "live");
            await Assert.That(File.Exists(source)).IsEqualTo(change != "live");
            await Assert.That(await File.ReadAllBytesAsync(change == "live" ? destination : source))
                .IsEquivalentTo(change == "changed-size" ? new byte[] { 1 } : bytes);
            await using var verify = database.CreateContext();
            var retained = await verify.Set<LocalStorageQuarantine>().SingleAsync(row => row.Id == claim.Id);
            await Assert.That(retained.State).IsEqualTo(LocalStorageQuarantineState.Processing);
            await Assert.That(retained.LeaseExpiresAtUtc).IsEqualTo(input.LeaseExpiresAtUtc);
            if (change != "changed-stamp")
                await Assert.That(retained.ConcurrencyStamp).IsEqualTo(input.ClaimStamp);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static FileStorageQuarantineInput CaptureClaim(LocalStorageQuarantine claim) => new(
        claim.ObjectKey, claim.Id, claim.ObservedSize, claim.ObservedLastModifiedUtc,
        claim.ProviderBindingId, claim.ConcurrencyStamp, claim.LeaseExpiresAtUtc!.Value,
        claim.CanonicalPhysicalPath, claim.PhysicalPathHash);

    internal sealed class QuarantineClock(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class GatedQuarantineContexts(EventResourceFileUploadTests.Database database)
        : IGlobalExploreDbContextFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ObservedToken { get; private set; }

        public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return database.CreateContext();
        }
    }

    [Test]
    public async Task RetainedBindingCannotBeRemovedWhileQuarantineOwnsItsTarget()
    {
        var binding = await SeedBindingAsync();
        await AdmitAsync(Owner(), binding.Id, "objects/retained");
        await using var removing = database.CreateContext();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
            await removing.Set<StorageProviderBinding>().Where(row => row.Id == binding.Id).ExecuteDeleteAsync());
    }

    [Test]
    public async Task ClaimBoundsRejectUnboundedPagesAndLeases()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await Owner().ClaimLocalQuarantineAsync(
            ILocalStorageQuarantineOwner.MaximumClaims + 1, TimeSpan.FromMinutes(5), default));
        await Assert.ThrowsAsync<ArgumentException>(async () => await Owner().ClaimLocalQuarantineAsync(
            1, TimeSpan.FromHours(2), default));
    }

    [Test]
    public async Task AHashCollisionCannotReturnAnotherPhysicalTargetsCustody()
    {
        var binding = await SeedBindingAsync();
        var collision = LocalStorageQuarantine.Create(binding, "objects/other", 19, Observed, Cutoff, "orphan", Cutoff);
        string target = LocalStorageQuarantine.ResolvePhysicalPath(binding, "objects/requested");
        await using (var seed = database.CreateContext())
        {
            seed.Add(collision);
            seed.Entry(collision).Property(row => row.PhysicalPathHash).CurrentValue = LocalStorageQuarantine.HashPhysicalPath(target);
            await seed.SaveChangesAsync();
        }
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await AdmitAsync(Owner(), binding.Id, "objects/requested"));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Owner().IsLocalOrphanAsync(binding.Id, "objects/requested", default));
            await using var verify = database.CreateContext();
            await Assert.That((await verify.Set<LocalStorageQuarantine>().SingleAsync(row => row.Id == collision.Id)).State)
                .IsEqualTo(LocalStorageQuarantineState.Pending);
        }
        finally
        {
            // Remove only the deliberate corrupt fixture; it must not poison later bounded claim pages.
            await using var cleanup = database.CreateContext();
            await cleanup.Set<LocalStorageQuarantine>().Where(row => row.Id == collision.Id).ExecuteDeleteAsync();
        }
    }

    internal static Task<LocalStorageQuarantine?> AdmitAsync(ILocalStorageQuarantineOwner owner, Guid binding, string key) =>
        owner.AdmitLocalQuarantineAsync(binding, key, 19, Observed, Cutoff, "orphan", default);

    internal static async Task SeedCustodyAsync(ExploreDbContext context, Guid tenant,
        StorageProviderBinding binding, string key, string kind)
    {
        switch (kind)
        {
            case "metadata":
            case "deleted":
                context.Add(new StorageObject
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    FileTypeId = (int)FileTypeEnum.Document,
                    FileType = null!,
                    Provider = StorageProviders.Local,
                    StorageProviderBindingId = binding.Id,
                    ObjectKey = key,
                    FullName = "custody.txt",
                    SafeDisplayName = "custody.txt",
                    Extension = ".txt",
                    Size = 19,
                    Visibility = StorageObjectVisibilities.PrivateOwner,
                    Purpose = StorageObjectPurposes.Document,
                    LifecycleState = StorageObjectLifecycleStates.Active,
                    CreatedAt = Observed,
                    IsDeleted = kind == "deleted",
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                break;
            case "producer":
                context.Add(StorageProducerOperation.Create(Guid.CreateVersion7(), tenant, binding, key, Observed));
                break;
            case "tombstone":
                context.Add(StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), tenant,
                    StorageProviders.Local, binding.Id, key, null, true, Observed));
                break;
            default:
                context.Add(new StorageUploadSession
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Provider = StorageProviders.Local,
                    StorageProviderBindingId = binding.Id,
                    ObjectKey = key,
                    ContentType = "text/plain",
                    SafeDisplayName = "custody.txt",
                    Purpose = StorageObjectPurposes.Document,
                    Visibility = StorageObjectVisibilities.PrivateOwner,
                    Status = kind,
                    CreatedAt = Observed,
                    ExpiresAt = Observed.AddHours(1),
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                break;
        }
        await context.SaveChangesAsync();
    }

    private async Task<StorageProviderBinding> SeedBindingAsync()
    {
        var binding = StorageProviderBinding.Local(Path.GetFullPath($"quarantine-{Guid.CreateVersion7():N}"));
        await using var seed = database.CreateContext();
        seed.Add(binding);
        await seed.SaveChangesAsync();
        return binding;
    }

    private StorageReconciliationOwner Owner() => new(new Contexts(database));
    private static Tenant Tenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Local custody tenant",
        Slug = $"custody-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = Observed
    };

    private sealed class Contexts(EventResourceFileUploadTests.Database database) : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(database.CreateContext());
        }
    }
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresLocalStorageQuarantineOwnerTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task NativeCompetingGlobalLeasesConvergeOnOnePhysicalCustody()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var owner = new StorageReconciliationOwner(
            new SimultaneousQuarantineContexts(rows.Global.CreateDbContextAsync), rows.Enrollment);
        var attempts = await Task.WhenAll(
            owner.AdmitLocalQuarantineAsync(rows.Binding.Id, "objects/competing", 19,
                LocalStorageQuarantineOwnerTests.Observed, LocalStorageQuarantineOwnerTests.Cutoff, "orphan", deadline.Token),
            owner.AdmitLocalQuarantineAsync(rows.Binding.Id, "objects\\competing", 19,
                LocalStorageQuarantineOwnerTests.Observed, LocalStorageQuarantineOwnerTests.Cutoff, "orphan", deadline.Token));
        await Assert.That(attempts[0]).IsNotNull();
        await Assert.That(attempts[1]!.Id).IsEqualTo(attempts[0]!.Id);
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.Set<LocalStorageQuarantine>()
            .CountAsync(row => row.PhysicalPathHash == attempts[0]!.PhysicalPathHash)).IsEqualTo(1);
    }
    [Test]
    public async Task NativeAdmissionDuplicatesAndExactTickRoundTripUseOnlyExecutionAuthority()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var owner = new StorageReconciliationOwner(rows.Global, rows.Enrollment);
        await Assert.That(await owner.IsLocalOrphanAsync(rows.Binding.Id, "objects/orphan", default)).IsTrue();
        var work = (await LocalStorageQuarantineOwnerTests.AdmitAsync(owner, rows.Binding.Id, "objects/orphan"))!;
        var duplicate = (await LocalStorageQuarantineOwnerTests.AdmitAsync(owner, rows.Binding.Id, "objects\\orphan"))!;
        await Assert.That(duplicate.Id).IsEqualTo(work.Id);
        var claim = (await owner.ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default)).Single(row => row.Id == work.Id);
        await Assert.That(claim.ObservedLastModifiedUtc.Ticks).IsEqualTo(LocalStorageQuarantineOwnerTests.Observed.Ticks);
        await Assert.That(claim.ObservedLastModifiedUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(claim.AdmittedAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(claim.LeaseExpiresAtUtc!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        var input = LocalStorageQuarantineOwnerTests.CaptureClaim(claim);
        await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(input, default)).IsTrue();
        foreach (var forged in new[]
        {
            input with { QuarantineId = Guid.CreateVersion7() },
            input with { ClaimStamp = Guid.CreateVersion7() },
            input with { ProviderBindingId = Guid.CreateVersion7() },
            input with { ObjectKey = "objects/other" },
            input with { CanonicalPhysicalPath = input.CanonicalPhysicalPath + "-other" },
            input with { PhysicalPathHash = new string('0', 64) },
            input with { ExpectedSize = input.ExpectedSize + 1 },
            input with { ExpectedLastModifiedUtc = input.ExpectedLastModifiedUtc.AddTicks(1) },
            input with { LeaseExpiresAtUtc = input.LeaseExpiresAtUtc.AddTicks(1) }
        })
            await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(forged, default)).IsFalse();
        await Assert.That(await owner.CompleteLocalQuarantineAsync(claim.Id, claim.ConcurrencyStamp,
            LocalStorageQuarantineOutcome.AlreadyQuarantined, default)).IsTrue();
        await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(input, default)).IsFalse();
        await using var global = await rows.Global.CreateDbContextAsync();
        foreach (string command in new[]
        {
            $"SELECT * FROM {rows.QuarantineTable}", $"DELETE FROM {rows.QuarantineTable}",
            $"SELECT * FROM {rows.StorageTable}", $"UPDATE {rows.StorageTable} SET object_key = 'forged'",
            $"SET ROLE {PostgresLocalStorageQuarantineContract.OwnerRole}",
            $"SET ROLE {PostgresLocalStorageQuarantineContract.MigratorRole}"
        })
            await Assert.ThrowsAsync<PostgresException>(async () => await global.Database.ExecuteSqlRawAsync(command));
    }

    [Test]
    [Arguments("deleted")]
    [Arguments("producer")]
    [Arguments("tombstone")]
    [Arguments(StorageUploadSessionStates.Expired)]
    public async Task NativeForeignTenantAndOverlappingRootCustodyCannotBecomeAnOrphan(string kind)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var nested = StorageProviderBinding.Local(Path.Combine(rows.Binding.LocalRootPath!, "nested"));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(nested);
            await seed.SaveChangesAsync();
            await LocalStorageQuarantineOwnerTests.SeedCustodyAsync(seed, rows.Authentication.B, nested, "owned", kind);
        }
        var owner = new StorageReconciliationOwner(rows.Global, rows.Enrollment);
        await Assert.That(await owner.IsLocalOrphanAsync(rows.Binding.Id, "nested/owned", default)).IsFalse();
        await Assert.That(await LocalStorageQuarantineOwnerTests.AdmitAsync(owner, rows.Binding.Id, "nested/owned")).IsNull();
    }

    [Test]
    [Arguments("deleted")]
    [Arguments("producer")]
    [Arguments("tombstone")]
    [Arguments(StorageUploadSessionStates.Expired)]
    public async Task NativeExecutionRejectsCustodyAddedAfterOriginalClaim(string kind)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var owner = new StorageReconciliationOwner(rows.Global, rows.Enrollment);
        var work = (await LocalStorageQuarantineOwnerTests.AdmitAsync(owner, rows.Binding.Id, "objects/late"))!;
        var claim = (await owner.ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default))
            .Single(row => row.Id == work.Id);
        var input = LocalStorageQuarantineOwnerTests.CaptureClaim(claim);
        await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(input, default)).IsTrue();
        await using (var seed = fixture.CreateDbContext())
            await LocalStorageQuarantineOwnerTests.SeedCustodyAsync(seed, rows.Authentication.B,
                rows.Binding, claim.ObjectKey, kind);
        await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(input, default)).IsFalse();
        await using var verify = fixture.CreateDbContext();
        var retained = await verify.Set<LocalStorageQuarantine>().AsNoTracking().SingleAsync(row => row.Id == claim.Id);
        await Assert.That(retained.State).IsEqualTo(LocalStorageQuarantineState.Processing);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(input.ClaimStamp);
        await Assert.That(retained.LeaseExpiresAtUtc).IsEqualTo(input.LeaseExpiresAtUtc);
    }

    [Test]
    public async Task NativeGraceCutoffAndHashCollisionFailClosed()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var owner = new StorageReconciliationOwner(rows.Global, rows.Enrollment);
        await Assert.ThrowsAsync<PostgresException>(async () => await owner.AdmitLocalQuarantineAsync(
            rows.Binding.Id, "objects/young", 19, LocalStorageQuarantineOwnerTests.Cutoff.AddTicks(1),
            LocalStorageQuarantineOwnerTests.Cutoff, "orphan", default));
        var collision = LocalStorageQuarantine.Create(rows.Binding, "objects/other", 19,
            LocalStorageQuarantineOwnerTests.Observed, LocalStorageQuarantineOwnerTests.Cutoff,
            "orphan", LocalStorageQuarantineOwnerTests.Cutoff);
        string target = LocalStorageQuarantine.ResolvePhysicalPath(rows.Binding, "objects/requested");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(collision);
            seed.Entry(collision).Property(row => row.PhysicalPathHash).CurrentValue = LocalStorageQuarantine.HashPhysicalPath(target);
            await seed.SaveChangesAsync();
        }
        try
        {
            await Assert.ThrowsAsync<PostgresException>(async () => await LocalStorageQuarantineOwnerTests.AdmitAsync(
                owner, rows.Binding.Id, "objects/requested"));
            await Assert.ThrowsAsync<PostgresException>(async () => await owner.IsLocalOrphanAsync(
                rows.Binding.Id, "objects/requested", default));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Set<LocalStorageQuarantine>().Where(row => row.Id == collision.Id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task NativeHostCaseAndUnicodeModesMatchTheApplicationIdentityMode()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var upper = StorageProviderBinding.Local(Path.Combine(rows.Binding.LocalRootPath!, "Mixed"));
        var lower = StorageProviderBinding.Local(Path.Combine(rows.Binding.LocalRootPath!, "mixed"));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(upper, lower);
            await seed.SaveChangesAsync();
            await LocalStorageQuarantineOwnerTests.SeedCustodyAsync(seed, rows.Authentication.B, upper, "FILE", "metadata");
            await LocalStorageQuarantineOwnerTests.SeedCustodyAsync(seed, rows.Authentication.B, rows.Binding,
                "dot/owned.", "metadata");
        }
        var owner = new StorageReconciliationOwner(rows.Global, rows.Enrollment);
        await Assert.That(await owner.IsLocalOrphanAsync(lower.Id, "file", default)).IsEqualTo(OperatingSystem.IsLinux());
        await Assert.That(await owner.IsLocalOrphanAsync(rows.Binding.Id, "objects/\u00e9", default))
            .IsEqualTo(OperatingSystem.IsLinux());
        await Assert.That(await owner.IsLocalOrphanAsync(rows.Binding.Id, "dot/owned", default))
            .IsEqualTo(!OperatingSystem.IsWindows());
    }

    [Test]
    public async Task NativeExpiredReclaimRejectsAnOldWorkersSettlement()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        var old = LocalStorageQuarantine.Create(rows.Binding, "objects/reclaimed", 19,
            LocalStorageQuarantineOwnerTests.Observed, LocalStorageQuarantineOwnerTests.Cutoff,
            "orphan", LocalStorageQuarantineOwnerTests.Cutoff);
        Guid previous = Guid.CreateVersion7();
        old.TryClaim(old.ConcurrencyStamp, previous, LocalStorageQuarantineOwnerTests.Cutoff,
            LocalStorageQuarantineOwnerTests.Cutoff.AddMinutes(1));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(old);
            await seed.SaveChangesAsync();
        }
        var owner = new StorageReconciliationOwner(rows.Global, rows.Enrollment);
        var claimed = (await owner.ClaimLocalQuarantineAsync(500, TimeSpan.FromMinutes(5), default)).Single(row => row.Id == old.Id);
        await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(
            LocalStorageQuarantineOwnerTests.CaptureClaim(old), default)).IsFalse();
        await Assert.That(await owner.IsLocalQuarantineClaimCurrentAsync(
            LocalStorageQuarantineOwnerTests.CaptureClaim(claimed), default)).IsTrue();
        await Assert.That(await owner.CompleteLocalQuarantineAsync(old.Id, previous, LocalStorageQuarantineOutcome.Moved, default)).IsFalse();
        await Assert.That(await owner.BlockLocalQuarantineAsync(old.Id, previous, default)).IsFalse();
        await Assert.That(await owner.RetryLocalQuarantineAsync(old.Id, previous, DateTime.UtcNow.AddHours(1), default)).IsFalse();
        await Assert.That(await owner.CompleteLocalQuarantineAsync(old.Id, claimed.ConcurrencyStamp,
            LocalStorageQuarantineOutcome.AlreadyAbsent, default)).IsTrue();
    }

    [Test]
    public async Task NativeRuntimeAndUnentitledGlobalCannotEnterTheCapability()
    {
        await using var rows = await Rows.CreateAsync(fixture, grantCapability: false);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new StorageReconciliationOwner(rows.Global, rows.Enrollment)
            .IsLocalOrphanAsync(rows.Binding.Id, "objects/orphan", default));
        await using var migration = fixture.CreateDbContext();
        var deniedInstall = await Assert.That(async () =>
            await PostgresLocalStorageQuarantineContract.ApplyAsync(migration, rows.Enrollment)).Throws<PostgresException>();
        await Assert.That(deniedInstall!.MessageText).IsEqualTo("local_storage_quarantine_membership_invalid");
        await using var runtime = rows.Authentication.Context(rows.Authentication.A);
        await Assert.ThrowsAsync<PostgresException>(async () => await runtime.Database.ExecuteSqlRawAsync(
            $"SELECT {PostgresLocalStorageQuarantineContract.FunctionSql(runtime, "inspect")}({{0}},{{1}})",
            rows.Binding.Id, "objects/orphan"));
    }

    [Test]
    [Arguments("function")]
    [Arguments("column")]
    [Arguments("policy")]
    [Arguments("execution_acl")]
    [Arguments("unexpected_policy")]
    [Arguments("tenant_policy")]
    [Arguments("unsafe_role")]
    [Arguments("inherited_authority")]
    public async Task NativeReadinessRejectsAnAlteredFunctionGrantOrPolicy(string change)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var migration = fixture.CreateDbContext();
        string? originalTenantPredicate = null;
        try
        {
            switch (change)
            {
                case "function":
                    await migration.Database.ExecuteSqlRawAsync($"""
                        CREATE OR REPLACE FUNCTION {PostgresLocalStorageQuarantineContract.FunctionSql(migration, "inspect")}
                            (p_binding uuid,p_key text) RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER
                            SET search_path = pg_catalog AS 'BEGIN RETURN true; END;';
                        """);
                    break;
                case "column":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"GRANT UPDATE (object_key) ON {rows.StorageTable} TO {PostgresLocalStorageQuarantineContract.OwnerRole}");
                    break;
                case "policy":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"ALTER POLICY local_storage_quarantine_read ON {rows.StorageTable} USING (false)");
                    break;
                case "execution_acl":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"GRANT EXECUTE ON FUNCTION {PostgresLocalStorageQuarantineContract.FunctionSql(migration, "inspect")}(uuid,text) TO {PostgresDiscoveryDisclosureContract.RuntimeRole}");
                    break;
                case "unexpected_policy":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"CREATE POLICY quarantine_unexpected_owner ON {rows.StorageTable} FOR SELECT TO {PostgresLocalStorageQuarantineContract.OwnerRole} USING (true)");
                    break;
                case "tenant_policy":
                    originalTenantPredicate = await migration.Database.SqlQueryRaw<string>(
                        """SELECT pg_catalog.pg_get_expr(polqual, polrelid) AS "Value" FROM pg_catalog.pg_policy WHERE polrelid = {0}::regclass AND polname = 'tenant_isolation'""",
                        rows.StorageTable).SingleAsync();
                    await migration.Database.ExecuteSqlRawAsync(
                        $"ALTER POLICY tenant_isolation ON {rows.StorageTable} USING (false)");
                    break;
                case "unsafe_role":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"ALTER ROLE {PostgresLocalStorageQuarantineContract.OwnerRole} LOGIN");
                    break;
                case "inherited_authority":
                    await migration.Database.ExecuteSqlRawAsync(
                        $"GRANT pg_read_all_data TO {PostgresLocalStorageQuarantineContract.OwnerRole} WITH INHERIT FALSE, SET TRUE");
                    break;
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await new StorageReconciliationOwner(rows.Global, rows.Enrollment)
                .IsLocalOrphanAsync(rows.Binding.Id, "objects/orphan", default));
            if (change is "unsafe_role" or "inherited_authority")
                await Assert.ThrowsAsync<PostgresException>(
                    () => PostgresLocalStorageQuarantineContract.ApplyAsync(migration, rows.Enrollment));
            if (change == "unsafe_role")
                await Assert.That(await migration.Database.SqlQueryRaw<bool>(
                    $"""SELECT rolcanlogin AS "Value" FROM pg_catalog.pg_roles WHERE rolname = '{PostgresLocalStorageQuarantineContract.OwnerRole}'""")
                    .SingleAsync()).IsTrue();
        }
        finally
        {
            if (change == "execution_acl")
                await migration.Database.ExecuteSqlRawAsync(
                    $"REVOKE EXECUTE ON FUNCTION {PostgresLocalStorageQuarantineContract.FunctionSql(migration, "inspect")}(uuid,text) FROM {PostgresDiscoveryDisclosureContract.RuntimeRole}");
            if (change == "unexpected_policy")
                await migration.Database.ExecuteSqlRawAsync(
                    $"DROP POLICY IF EXISTS quarantine_unexpected_owner ON {rows.StorageTable}");
            if (originalTenantPredicate is not null)
                await migration.Database.ExecuteSqlRawAsync(
                    $"ALTER POLICY tenant_isolation ON {rows.StorageTable} USING ({originalTenantPredicate})");
            if (change == "unsafe_role")
                await migration.Database.ExecuteSqlRawAsync(
                    $"ALTER ROLE {PostgresLocalStorageQuarantineContract.OwnerRole} NOLOGIN");
            if (change == "inherited_authority")
                await migration.Database.ExecuteSqlRawAsync(
                    $"REVOKE pg_read_all_data FROM {PostgresLocalStorageQuarantineContract.OwnerRole}");
            await PostgresLocalStorageQuarantineContract.ApplyAsync(migration, rows.Enrollment);
        }
    }

    private sealed class Rows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal PostgresAuthenticationLookupAuthorityTests.AuthenticationRows Authentication { get; private set; } = null!;
        internal GlobalExploreDbContextFactory Global { get; private set; } = null!;
        internal StorageProviderBinding Binding { get; private set; } = null!;
        internal PostgresNativeQueueEnrollment Enrollment => fixture.NativeQueueEnrollment;
        internal string QuarantineTable { get; private set; } = null!;
        internal string StorageTable { get; private set; } = null!;
        private string? _connection;
        private bool _capabilityRevoked;

        internal static async Task<Rows> CreateAsync(PostgreSqlContainerFixture fixture, bool grantCapability = true)
        {
            var rows = new Rows(fixture);
            try
            {
                rows.Authentication = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
                rows.Binding = StorageProviderBinding.Local(Path.GetFullPath($"native-quarantine-{Guid.CreateVersion7():N}"));
                await using (var seed = fixture.CreateDbContext())
                {
                    seed.Add(rows.Binding);
                    await seed.SaveChangesAsync();
                    string Table<T>() where T : class
                    {
                        var entity = seed.Model.FindEntityType(typeof(T))!;
                        return seed.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                    }
                    rows.QuarantineTable = Table<LocalStorageQuarantine>();
                    rows.StorageTable = Table<StorageObject>();
                    await StorageNativeGlobalProfile.PrepareAsync(seed, rows.Enrollment);
                    await PostgresLocalStorageQuarantineContract.ApplyAsync(seed, rows.Enrollment);
                    if (!grantCapability)
                    {
                        string login = seed.GetService<ISqlGenerationHelper>().DelimitIdentifier(rows.Enrollment.GlobalLogin);
                        rows._capabilityRevoked = true;
                        await using var revoke = seed.Database.GetDbConnection().CreateCommand();
                        revoke.Transaction = seed.Database.CurrentTransaction?.GetDbTransaction();
                        revoke.CommandText = $"REVOKE {PostgresLocalStorageQuarantineContract.GlobalRole} FROM {login}";
                        await revoke.ExecuteNonQueryAsync();
                    }
                }
                var globalOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global);
                rows._connection = PrimaryDatabaseConfiguration.BuildConnectionString(globalOptions).ConnectionString;
                rows.Global = new GlobalExploreDbContextFactory(globalOptions, poolSize: 1);
                return rows;
            }
            catch (Exception initializationFailure)
            {
                try { await rows.DisposeAsync(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Native quarantine initialization and cleanup failed.",
                        initializationFailure, cleanupFailure);
                }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try
            {
                if (_connection is not null)
                {
                    using var pool = new NpgsqlConnection(_connection);
                    NpgsqlConnection.ClearPool(pool);
                }
            }
            catch (Exception failure) { failures.Add(failure); }
            try
            {
                if (_capabilityRevoked)
                {
                    await using var migration = fixture.CreateDbContext();
                    await migration.Database.OpenConnectionAsync();
                    string login = migration.GetService<ISqlGenerationHelper>().DelimitIdentifier(Enrollment.GlobalLogin);
                    await using var restore = migration.Database.GetDbConnection().CreateCommand();
                    restore.Transaction = migration.Database.CurrentTransaction?.GetDbTransaction();
                    restore.CommandText = $"GRANT {PostgresLocalStorageQuarantineContract.GlobalRole} TO {login} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE";
                    await restore.ExecuteNonQueryAsync();
                    _capabilityRevoked = false;
                }
            }
            catch (Exception failure) { failures.Add(failure); }
            try
            {
                if (Authentication is not null) await Authentication.DisposeAsync();
            }
            catch (Exception failure) { failures.Add(failure); }
            if (failures.Count != 0)
                throw new AggregateException("Native quarantine cleanup failed.", failures);
        }
    }
}

/// <summary>Canonical Global profile confined to each native storage class's dedicated container.</summary>
internal static class StorageNativeGlobalProfile
{
    internal static async Task PrepareAsync(ExploreDbContext migrator, PostgresNativeQueueEnrollment enrollment)
    {
        migrator.ClearTenantFilterBypass();
        migrator.BindTenantLease();
        await migrator.Database.OpenConnectionAsync();
        var model = migrator.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))!;
        string table = entity.GetTableName()!;
        string schema = entity.GetSchema()!;
        string qualified = migrator.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema);
        bool exists = await migrator.Database.SqlQueryRaw<bool>(
            """SELECT pg_catalog.to_regclass({0}) IS NOT NULL AS "Value" """, qualified).SingleAsync();
        if (!exists)
        {
            MigrationOperation[] operations = migrator.GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model.GetRelationalModel())
                .Where(operation => operation is EnsureSchemaOperation ensure && ensure.Name == schema
                    || operation is CreateTableOperation create && create.Name == table && create.Schema == schema
                    || operation is CreateIndexOperation index && index.Table == table && index.Schema == schema).ToArray();
            if (operations.OfType<CreateTableOperation>().Count() != 1)
                throw new InvalidOperationException("The native storage policy projection must have one mapped table.");
            foreach (var command in migrator.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
                await command.ExecuteNonQueryAsync(migrator.GetService<IRelationalConnection>());
        }
        await PostgresTenantCreationPermissions.ApplyAsync(migrator, enrollment, IdentityDatabaseTopology.Colocated);
        await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, enrollment);
        await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(migrator, enrollment, IdentityDatabaseTopology.Colocated);
    }
}

/// <summary>Both real independent leases must enter before either database admission begins.</summary>
internal sealed class SimultaneousQuarantineContexts(
    Func<CancellationToken, Task<ExploreDbContext>> create) : IGlobalExploreDbContextFactory
{
    private readonly TaskCompletionSource _bothAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _acquired;

    public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var context = await create(cancellationToken);
        try
        {
            int position = Interlocked.Increment(ref _acquired);
            if (position == 2) _bothAcquired.TrySetResult();
            if (position <= 2) await _bothAcquired.Task.WaitAsync(cancellationToken);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}
