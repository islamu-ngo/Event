namespace Event.Domain.UnitTests.Storage;

public sealed class LocalStorageQuarantineTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ReclaimRejectsOldCompletionBlockAndRetry()
    {
        var work = Create();
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, first, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(work.TryClaim(first, second, Now.AddSeconds(59), Now.AddMinutes(2))).IsFalse();
        await Assert.That(work.TryClaim(first, second, Now.AddMinutes(1), Now.AddMinutes(2))).IsTrue();
        await Assert.That(work.TryComplete(first, LocalStorageQuarantineOutcome.Moved, Now.AddMinutes(1))).IsFalse();
        await Assert.That(work.TryBlock(first, Now.AddMinutes(1))).IsFalse();
        await Assert.That(work.TryRetry(first, Now.AddMinutes(1), Now.AddMinutes(3))).IsFalse();
        await Assert.That(work.TryComplete(second, LocalStorageQuarantineOutcome.AlreadyQuarantined, Now.AddMinutes(1))).IsTrue();
        await Assert.That(work.State).IsEqualTo(LocalStorageQuarantineState.Completed);
        await Assert.That(work.Outcome).IsEqualTo(LocalStorageQuarantineOutcome.AlreadyQuarantined);
        await Assert.That(work.SettledAtUtc).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task RetryKeepsCapturedEvidenceAndRespectsDueTime()
    {
        var work = Create();
        var identity = (work.Id, work.ProviderBindingId, work.ObjectKey, work.CanonicalPhysicalPath,
            work.PhysicalPathHash, work.ObservedSize, work.ObservedLastModifiedUtc);
        Guid stamp = Guid.CreateVersion7();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, stamp, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(work.TryRetry(stamp, Now, Now.AddMinutes(5))).IsTrue();
        await Assert.That(work.SettledAtUtc).IsNull();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, Guid.CreateVersion7(), Now.AddMinutes(4), Now.AddMinutes(6))).IsFalse();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, Guid.CreateVersion7(), Now.AddMinutes(5), Now.AddMinutes(6))).IsTrue();
        await Assert.That((work.Id, work.ProviderBindingId, work.ObjectKey, work.CanonicalPhysicalPath,
            work.PhysicalPathHash, work.ObservedSize, work.ObservedLastModifiedUtc)).IsEqualTo(identity);
        await Assert.That(work.ToString()).DoesNotContain(work.ObjectKey);
    }

    [Test]
    [Arguments(LocalStorageQuarantineOutcome.Moved)]
    [Arguments(LocalStorageQuarantineOutcome.AlreadyQuarantined)]
    [Arguments(LocalStorageQuarantineOutcome.AlreadyAbsent)]
    public async Task CompletionRetainsIdentityAndCannotReopen(LocalStorageQuarantineOutcome outcome)
    {
        var work = Create();
        Guid stamp = Guid.CreateVersion7();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, stamp, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(work.TryComplete(stamp, outcome, Now.AddMinutes(1))).IsFalse();
        await Assert.That(work.TryComplete(stamp, outcome, Now)).IsTrue();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, Guid.CreateVersion7(), Now.AddMinutes(2), Now.AddMinutes(3))).IsFalse();
        await Assert.That(work.ProviderBindingId == Guid.Empty).IsFalse();
    }

    [Test]
    public async Task BlockedWorkNeverExpiresIntoMoveAuthority()
    {
        var work = Create();
        Guid stamp = Guid.CreateVersion7();
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, stamp, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(work.TryBlock(stamp, Now)).IsTrue();
        await Assert.That(work.State).IsEqualTo(LocalStorageQuarantineState.Blocked);
        await Assert.That(work.Outcome).IsEqualTo(LocalStorageQuarantineOutcome.Blocked);
        await Assert.That(work.SettledAtUtc).IsEqualTo(Now);
        await Assert.That(work.TryClaim(work.ConcurrencyStamp, Guid.CreateVersion7(), Now.AddYears(1), Now.AddYears(1).AddMinutes(1))).IsFalse();
    }

    [Test]
    [Arguments("../owned")]
    [Arguments("/owned")]
    [Arguments("objects/../owned")]
    [Arguments(".quarantine/reconciliation/owned")]
    public async Task AdmissionRejectsNonInventoryKeys(string key)
    {
        await Assert.That(() => LocalStorageQuarantine.Create(
            StorageProviderBinding.Local(Path.GetFullPath("storage")), key, 12, Now, Now, "orphan", Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task EquivalentRootsShareCanonicalPhysicalIdentity()
    {
        string root = Path.GetFullPath("storage");
        var outer = LocalStorageQuarantine.Create(StorageProviderBinding.Local(Path.Combine(root, ".")),
            "nested/file", 12, Now, Now, "orphan", Now);
        var inner = LocalStorageQuarantine.Create(StorageProviderBinding.Local(Path.Combine(root, "nested")),
            "file", 12, Now, Now, "orphan", Now);
        await Assert.That(outer.CanonicalPhysicalPath).IsEqualTo(
            LocalStorageQuarantine.CanonicalizePhysicalPath(Path.Combine(root, "nested", "file")));
        await Assert.That(inner.PhysicalPathHash).IsEqualTo(outer.PhysicalPathHash);
        await Assert.That(outer.Id).IsNotEqualTo(inner.Id);
    }

    [Test]
    public async Task BackslashKeyAliasesHaveOnePhysicalIdentity()
    {
        var binding = StorageProviderBinding.Local(Path.GetFullPath("storage"));
        var slash = LocalStorageQuarantine.Create(binding, "objects/owned", 12, Now, Now, "orphan", Now);
        var backslash = LocalStorageQuarantine.Create(binding, "objects\\owned", 12, Now, Now, "orphan", Now);
        await Assert.That(slash.CanonicalPhysicalPath).IsEqualTo(backslash.CanonicalPhysicalPath);
        await Assert.That(slash.PhysicalPathHash).IsEqualTo(backslash.PhysicalPathHash);
    }

    [Test]
    public async Task HostIdentityUsesConservativeAsciiCaseWithoutClaimingUnicodeAuthority()
    {
        string first = LocalStorageQuarantine.CanonicalizePhysicalPath(Path.GetFullPath("Storage/FILE"));
        string second = LocalStorageQuarantine.CanonicalizePhysicalPath(Path.GetFullPath("storage/file"));
        await Assert.That(first == second).IsEqualTo(!OperatingSystem.IsLinux());
        await Assert.That(LocalStorageQuarantine.CanEstablishCaseIdentity("/storage/\u00e9")).IsEqualTo(OperatingSystem.IsLinux());
    }

    [Test]
    public async Task GraceCutoffMustContainTheExactObservation()
    {
        var binding = StorageProviderBinding.Local(Path.GetFullPath("storage"));
        await Assert.That(() => LocalStorageQuarantine.Create(binding, "owned", 12, Now.AddTicks(1), Now,
            "orphan", Now)).Throws<ArgumentException>();
        await Assert.That(() => LocalStorageQuarantine.Create(binding, "owned", 12, Now, Now.AddTicks(1),
            "orphan", Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task WindowsDriveAndUncRootsUseTheirNativeAbsoluteSyntax()
    {
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(() => StorageProviderBinding.Local(@"C:\Storage"))
                .Throws<ArgumentException>();
            return;
        }
        var drive = LocalStorageQuarantine.Create(StorageProviderBinding.Local(@"C:\Storage\.\Nested"),
            @"Objects\File", 12, Now, Now, "orphan", Now);
        var unc = LocalStorageQuarantine.Create(StorageProviderBinding.Local(@"\\Server\Share\Storage"),
            @"Objects\File", 12, Now, Now, "orphan", Now);
        var extendedDrive = LocalStorageQuarantine.Create(StorageProviderBinding.Local(@"\\?\C:\Storage\Nested"),
            @"Objects\File", 12, Now, Now, "orphan", Now);
        var extendedUnc = LocalStorageQuarantine.Create(StorageProviderBinding.Local(@"\\?\UNC\Server\Share\Storage"),
            @"Objects\File", 12, Now, Now, "orphan", Now);
        await Assert.That(drive.CanonicalPhysicalPath).IsEqualTo("c:/storage/nested/objects/file");
        await Assert.That(unc.CanonicalPhysicalPath).IsEqualTo("//server/share/storage/objects/file");
        await Assert.That(extendedDrive.PhysicalPathHash).IsEqualTo(drive.PhysicalPathHash);
        await Assert.That(extendedUnc.PhysicalPathHash).IsEqualTo(unc.PhysicalPathHash);
        await Assert.That(LocalStorageQuarantine.CanEstablishCaseIdentity("c:/storage/owned.")).IsFalse();
        await Assert.That(LocalStorageQuarantine.CanEstablishCaseIdentity("c:/storage/owned ")).IsFalse();
        await Assert.That(LocalStorageQuarantine.CanEstablishCaseIdentity("c:/storag~1/owned")).IsFalse();
        await Assert.That(LocalStorageQuarantine.CanEstablishCaseIdentity("c:/storage/con.txt")).IsFalse();
        await Assert.That(LocalStorageQuarantine.CanEstablishCaseIdentity("c:/storage/COM1.bin")).IsFalse();
    }

    private static LocalStorageQuarantine Create() =>
        LocalStorageQuarantine.Create(StorageProviderBinding.Local(Path.GetFullPath("storage")), "objects/orphan",
            12, Now.AddDays(-1), Now, "orphan", Now);
}
