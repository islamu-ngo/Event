namespace Event.SetupAssistant.Desktop.Tests;

using System.Security.Cryptography;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;

public sealed class DesktopProtectedWriteInvariantTests
{
    [Test]
    public async Task DesktopDependencyUsesSharedCreateOnlyWriter()
    {
        if (!OperatingSystem.IsLinux()) return;
        string directory = Path.Combine(Path.GetTempPath(), "event-desktop-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "output");
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var writer = new ProtectedArtifactWriter();
            using var preparation = await writer.PrepareAsync(SetupArtifactKind.OperatorIdentity, target, bytes);
            await Assert.That(await preparation.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.Written);
            using var overwrite = await writer.PrepareAsync(
                SetupArtifactKind.OperatorIdentity, target, RandomNumberGenerator.GetBytes(64));
            await Assert.That(await overwrite.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.TargetExists);
            await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(bytes);
            await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await Assert.That(Directory.GetFileSystemEntries(directory)).IsEquivalentTo([target]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task UnprovedHostsCannotEnableProtectedSave()
    {
        await Assert.That(new ProtectedArtifactWriter().IsAvailable).IsEqualTo(OperatingSystem.IsLinux());
    }
}
