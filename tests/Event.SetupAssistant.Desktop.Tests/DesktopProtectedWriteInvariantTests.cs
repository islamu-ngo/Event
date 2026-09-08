namespace Event.SetupAssistant.Desktop.Tests;

using System.Reflection;
using System.Security.Cryptography;

public sealed class DesktopProtectedWriteInvariantTests
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Test]
    public async Task UnixCreateNewWritesExactBytesWithOwnerOnlyMode()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;

        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "protected.env");
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        var contract = new DesktopProtectedWriteContract();
        object writer = contract.CreateUnixWriter();
        object request = contract.CreateRequest(target, bytes, allowOverwrite: false);

        using ProtectedWritePreparationHandle preparation =
            await contract.PrepareAsync(writer, request);
        object result = await preparation.CommitAsync();

        await Assert.That(DesktopProtectedWriteContract.PropertyName(
            result, "Disposition")).IsEqualTo("Written");
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(bytes);
        await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(OwnerOnly);
        await AssertNoSidecars(directory.Path, target);
    }

    [Test]
    public async Task ExistingTargetWithoutApprovalRemainsUnchanged()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;
        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "existing.env");
        byte[] original = RandomNumberGenerator.GetBytes(32);
        await File.WriteAllBytesAsync(target, original);
        File.SetUnixFileMode(target, OwnerOnly);
        var contract = new DesktopProtectedWriteContract();

        using ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
            contract.CreateUnixWriter(),
            contract.CreateRequest(target, RandomNumberGenerator.GetBytes(48), false));
        object result = await preparation.CommitAsync();

        await Assert.That(DesktopProtectedWriteContract.PropertyName(
            result, "FailureCode")).IsEqualTo("TargetExists");
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(original);
        await AssertNoSidecars(directory.Path, target);
    }

    [Test]
    public async Task ApprovedOverwriteAtomicallyReplacesWithoutBackup()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;
        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "replace.env");
        byte[] replacement = RandomNumberGenerator.GetBytes(48);
        await File.WriteAllBytesAsync(target, RandomNumberGenerator.GetBytes(24));
        File.SetUnixFileMode(target, OwnerOnly);
        var contract = new DesktopProtectedWriteContract();

        using ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
            contract.CreateUnixWriter(),
            contract.CreateRequest(target, replacement, true));
        object result = await preparation.CommitAsync();

        await Assert.That(DesktopProtectedWriteContract.PropertyName(
            result, "Disposition")).IsEqualTo("Written");
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(replacement);
        await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(OwnerOnly);
        await AssertNoSidecars(directory.Path, target);
    }

    [Test]
    public async Task DirectorySymlinkAndSpecialTargetsFailClosed()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;
        using var directory = TemporaryDirectory.Create();
        string real = Path.Combine(directory.Path, "real.env");
        string link = Path.Combine(directory.Path, "link.env");
        string child = Path.Combine(directory.Path, "child");
        await File.WriteAllBytesAsync(real, RandomNumberGenerator.GetBytes(16));
        Directory.CreateDirectory(child);
        File.CreateSymbolicLink(link, real);
        var contract = new DesktopProtectedWriteContract();
        object writer = contract.CreateUnixWriter();

        foreach (string target in new[] { child, link, "/dev/null" })
        {
            using ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
                writer,
                contract.CreateRequest(target, RandomNumberGenerator.GetBytes(16), true));
            object result = await preparation.CommitAsync();
            await Assert.That(DesktopProtectedWriteContract.PropertyName(
                result, "Disposition")).IsEqualTo("Rejected");
        }

        await AssertNoSidecars(directory.Path, real, link, child);
    }

    [Test]
    public async Task TargetCreatedAfterPrepareIsRejectedAndTemporaryFileIsRemoved()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;
        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "swap.env");
        byte[] attacker = RandomNumberGenerator.GetBytes(17);
        var contract = new DesktopProtectedWriteContract();

        using ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
            contract.CreateUnixWriter(),
            contract.CreateRequest(target, RandomNumberGenerator.GetBytes(64), false));
        await File.WriteAllBytesAsync(target, attacker);
        object result = await preparation.CommitAsync();

        await Assert.That(DesktopProtectedWriteContract.PropertyName(
            result, "FailureCode")).IsEqualTo("TargetChanged");
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(attacker);
        await AssertNoSidecars(directory.Path, target);
    }

    [Test]
    public async Task ExistingTargetChangedAfterPrepareIsRejected()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;
        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "swap-existing.env");
        await File.WriteAllBytesAsync(target, RandomNumberGenerator.GetBytes(19));
        File.SetUnixFileMode(target, OwnerOnly);
        var contract = new DesktopProtectedWriteContract();

        using ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
            contract.CreateUnixWriter(),
            contract.CreateRequest(target, RandomNumberGenerator.GetBytes(64), true));
        byte[] changed = RandomNumberGenerator.GetBytes(37);
        await File.WriteAllBytesAsync(target, changed);
        object result = await preparation.CommitAsync();

        await Assert.That(DesktopProtectedWriteContract.PropertyName(
            result, "FailureCode")).IsEqualTo("TargetChanged");
        await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(changed);
        await AssertNoSidecars(directory.Path, target);
    }

    [Test]
    public async Task DisposingUncommittedPreparationRemovesTemporaryFile()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
            return;
        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "cancelled.env");
        var contract = new DesktopProtectedWriteContract();

        ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
            contract.CreateUnixWriter(),
            contract.CreateRequest(target, RandomNumberGenerator.GetBytes(64), false));
        preparation.Dispose();

        await Assert.That(File.Exists(target)).IsFalse();
        await AssertNoSidecars(directory.Path, target);
    }

    [Test]
    public async Task WindowsWriterFailsClosedWhenProtectionIsUnavailable()
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = TemporaryDirectory.Create();
        string target = Path.Combine(directory.Path, "windows.env");
        var contract = new DesktopProtectedWriteContract();
        object writer = contract.CreateWindowsWriter();

        await Assert.That(DesktopProtectedWriteContract.IsAvailable(writer)).IsFalse();
        using ProtectedWritePreparationHandle preparation = await contract.PrepareAsync(
            writer,
            contract.CreateRequest(target, RandomNumberGenerator.GetBytes(32), false));
        object result = await preparation.CommitAsync();
        await Assert.That(DesktopProtectedWriteContract.PropertyName(
            result, "Disposition")).IsEqualTo("Unsupported");
        await Assert.That(File.Exists(target)).IsFalse();
    }

    [Test]
    public async Task ResultContractIsClosedAndValueFree()
    {
        var contract = new DesktopProtectedWriteContract();
        Type type = contract.ResultType;
        string[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(type.IsSealed).IsTrue();
        await Assert.That(properties).IsEquivalentTo(["Disposition", "FailureCode"]);
        await Assert.That(type.GetFields(BindingFlags.Public | BindingFlags.Instance)).IsEmpty();
        await Assert.That(properties.Any(name =>
            name.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Content", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Value", StringComparison.OrdinalIgnoreCase)
            || name.Contains("User", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private static async Task AssertNoSidecars(
        string directory,
        params string[] permitted)
    {
        HashSet<string> allowed = permitted.Select(Path.GetFullPath)
            .ToHashSet(StringComparer.Ordinal);
        string[] unexpected = Directory.GetFileSystemEntries(directory)
            .Where(path => !allowed.Contains(Path.GetFullPath(path)))
            .ToArray();
        await Assert.That(unexpected).IsEmpty();
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path) => Path = path;

    internal string Path { get; }

    internal static TemporaryDirectory Create()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"event-desktop-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
