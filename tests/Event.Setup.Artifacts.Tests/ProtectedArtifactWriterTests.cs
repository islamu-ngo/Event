using System.Security.Cryptography;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace ISLAMU.Setup.Artifacts.Tests;

public sealed class ProtectedArtifactWriterTests
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Test]
    public async Task RestrictedAndUnknownBytesNeverReachPublicStreams()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        foreach (SetupArtifactKind kind in new[]
        {
            SetupArtifactKind.Environment, SetupArtifactKind.Configuration,
            SetupArtifactKind.OperatorIdentity, SetupArtifactKind.Unknown, (SetupArtifactKind)999
        })
        {
            using var output = new MemoryStream();
            await Assert.That(() => ProtectedArtifactWriter.WritePublic(output, kind, bytes)).Throws<IOException>();
            await Assert.That(output.Length).IsEqualTo(0);
        }
        using var publicOutput = new MemoryStream();
        ProtectedArtifactWriter.WritePublic(publicOutput, SetupArtifactKind.PublicCatalogue, bytes);
        await Assert.That(publicOutput.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task HostAvailabilityDoesNotInferWindowsOrMacProtection()
    {
        var writer = new ProtectedArtifactWriter();
        await Assert.That(writer.IsAvailable).IsEqualTo(OperatingSystem.IsLinux());
        if (writer.IsAvailable) return;
        using var directory = new TestDirectory();
        using var preparation = await writer.PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "output"), RandomNumberGenerator.GetBytes(64));
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.UnsupportedPlatform);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEmpty();
    }

    [Test]
    public async Task NewFileHasExactBytesOwnerModeAndNoSidecars()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Path, "output");
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(SetupArtifactKind.Environment, target, bytes);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.Written);
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(bytes);
        await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(OwnerOnly);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEquivalentTo([target]);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.InvalidRequest);
    }

    [Test]
    public async Task ExistingTargetIsNeverChanged()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Path, "output");
        byte[] original = RandomNumberGenerator.GetBytes(64);
        await File.WriteAllBytesAsync(target, original);
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, target, RandomNumberGenerator.GetBytes(64));
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.TargetExists);
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(original);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEquivalentTo([target]);
    }

    [Test]
    public async Task RacingPreparationsHaveExactlyOneWinner()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Path, "output");
        byte[] first = RandomNumberGenerator.GetBytes(64);
        byte[] second = RandomNumberGenerator.GetBytes(64);
        var writer = new ProtectedArtifactWriter();
        using var one = await writer.PrepareAsync(SetupArtifactKind.Environment, target, first);
        using var two = await writer.PrepareAsync(SetupArtifactKind.Environment, target, second);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ProtectedArtifactStatus> Commit(ProtectedArtifactPreparation prepared) => Task.Run(async () =>
        {
            await start.Task;
            return await prepared.CommitAsync();
        });
        Task<ProtectedArtifactStatus> firstCommit = Commit(one);
        Task<ProtectedArtifactStatus> secondCommit = Commit(two);
        start.SetResult();
        ProtectedArtifactStatus[] statuses = await Task.WhenAll(firstCommit, secondCommit).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(statuses.Count(status => status == ProtectedArtifactStatus.Written)).IsEqualTo(1);
        byte[] actual = await File.ReadAllBytesAsync(target);
        await Assert.That(actual).IsEquivalentTo(statuses[0] == ProtectedArtifactStatus.Written ? first : second);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEquivalentTo([target]);
    }

    [Test]
    public async Task TargetAppearingAfterPrepareSurvivesCommit()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Path, "output");
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, target, RandomNumberGenerator.GetBytes(64));
        byte[] competing = RandomNumberGenerator.GetBytes(64);
        await File.WriteAllBytesAsync(target, competing);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.TargetChanged);
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(competing);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEquivalentTo([target]);
    }

    [Test]
    public async Task CancellationAndDisposalLeaveNoStagedBytes()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        var writer = new ProtectedArtifactWriter();
        using var cancellation = new CancellationTokenSource();
        using var preparation = await writer.PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "cancelled"), RandomNumberGenerator.GetBytes(64));
        cancellation.Cancel();
        await Assert.That(async () => await preparation.CommitAsync(cancellation.Token)).Throws<OperationCanceledException>();
        using (await writer.PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "disposed"), RandomNumberGenerator.GetBytes(64))) { }
        await Assert.That(async () => await writer.PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "pre-cancelled"),
            RandomNumberGenerator.GetBytes(64), cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEmpty();
    }

    [Test]
    public async Task LinksDirectoriesAndPublicDestinationAreRefused()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string link = Path.Combine(directory.Path, "dangling");
        File.CreateSymbolicLink(link, Path.Combine(directory.Path, "absent"));
        string child = Path.Combine(directory.Path, "child");
        Directory.CreateDirectory(child);
        string parentLink = Path.Combine(directory.Path, "parent-link");
        Directory.CreateSymbolicLink(parentLink, child);
        foreach (string target in new[] { link, child, Path.Combine(parentLink, "output"), "-", "/dev/null" })
        {
            using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
                SetupArtifactKind.Environment, target, RandomNumberGenerator.GetBytes(64));
            await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.UnsafeTarget);
        }
        await Assert.That(Directory.GetFileSystemEntries(child)).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(directory.Path, "absent"))).IsFalse();
    }

    [Test]
    public async Task WritableParentCannotStagePrivateBytes()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        File.SetUnixFileMode(directory.Path, OwnerOnly | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "output"), RandomNumberGenerator.GetBytes(64));
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.UnsafeTarget);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEmpty();
    }

    [Test]
    public async Task PermissionChangeBetweenPrepareAndCommitFailsClosed()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "output"), RandomNumberGenerator.GetBytes(64));
        File.SetUnixFileMode(directory.Path, OwnerOnly | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.UnsafeTarget);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEmpty();
    }

    [Test]
    public async Task ParentReplacementCannotRedirectPreparedOutput()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string parent = Path.Combine(directory.Path, "parent");
        string moved = Path.Combine(directory.Path, "moved");
        Directory.CreateDirectory(parent);
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(parent, "output"), RandomNumberGenerator.GetBytes(64));
        Directory.Move(parent, moved);
        Directory.CreateDirectory(parent);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.TargetChanged);
        await Assert.That(Directory.GetFileSystemEntries(parent)).IsEmpty();
        await Assert.That(Directory.GetFileSystemEntries(moved)).IsEmpty();
    }

    [Test]
    public async Task PrivateStagingHasNoReplaceableDirectoryEntry()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, Path.Combine(directory.Path, "output"), RandomNumberGenerator.GetBytes(64));
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEmpty();
    }

    [Test]
    public async Task BoundsRejectBeforeStagingAndUnknownKindStillUsesPrivateFile()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        var writer = new ProtectedArtifactWriter();
        string target = Path.Combine(directory.Path, "output");
        foreach (byte[] bytes in new[] { Array.Empty<byte>(), RandomNumberGenerator.GetBytes(ProtectedArtifactWriter.MaximumBytes + 1) })
        {
            using var rejected = await writer.PrepareAsync(SetupArtifactKind.Environment, target, bytes);
            await Assert.That(await rejected.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.InvalidRequest);
            await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEmpty();
        }
        byte[] unknown = RandomNumberGenerator.GetBytes(32);
        using var preparation = await writer.PrepareAsync((SetupArtifactKind)999, target, unknown);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.Written);
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(unknown);
        await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(OwnerOnly);
    }

    [Test]
    public async Task SymlinkInstalledAfterPrepareCannotReceiveBytes()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Path, "output");
        string original = Path.Combine(directory.Path, "original");
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        await File.WriteAllBytesAsync(original, bytes);
        using var preparation = await new ProtectedArtifactWriter().PrepareAsync(
            SetupArtifactKind.Environment, target, RandomNumberGenerator.GetBytes(64));
        File.CreateSymbolicLink(target, original);
        await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.TargetChanged);
        await Assert.That(await File.ReadAllBytesAsync(original)).IsEquivalentTo(bytes);
        await Assert.That(new FileInfo(target).LinkTarget).IsEqualTo(original);
        await Assert.That(Directory.GetFileSystemEntries(directory.Path)).IsEquivalentTo([target, original]);
    }
}

internal sealed class TestDirectory : IDisposable
{
    internal TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "event-artifacts-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(Path);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
