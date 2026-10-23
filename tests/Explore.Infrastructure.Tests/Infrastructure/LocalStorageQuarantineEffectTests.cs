using Explore.Application.Models.Storage;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class LocalStorageQuarantineEffectTests
{
    [Test]
    [Arguments("move")]
    [Arguments("lost_acknowledgement")]
    [Arguments("absent")]
    [Arguments("changed_size")]
    [Arguments("changed_time")]
    [Arguments("destination_collision")]
    [Arguments("both_present")]
    [Arguments("cancelled")]
    public async Task CapturedTargetConvergesWithoutMovingReplacementBytes(string scenario)
    {
        string root = Directory.CreateTempSubdirectory("local-quarantine-effect-").FullName;
        try
        {
            var provider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = root }),
                NullLogger<LocalFileStorageProvider>.Instance);
            string source = Path.Combine(root, "orphan.bin");
            byte[] bytes = Guid.CreateVersion7().ToByteArray();
            await File.WriteAllBytesAsync(source, bytes);
            await using var custody = await LocalQuarantineClaimFixture.CreateAsync(
                root, "orphan.bin", bytes.Length, File.GetLastWriteTimeUtc(source));
            var input = custody.Input;
            Guid id = input.QuarantineId;
            string destination = Path.Combine(root, ".quarantine", "reconciliation", id.ToString("N"));
            if (scenario is "lost_acknowledgement" or "both_present")
            {
                var first = await provider.QuarantineAsync(input, custody.Owner, TimeProvider.System, default);
                await Assert.That(first.Outcome).IsEqualTo(FileStorageQuarantineOutcome.Moved);
                if (scenario == "both_present")
                    await File.WriteAllBytesAsync(source, bytes);
            }
            else if (scenario == "absent")
                File.Delete(source);
            else if (scenario == "changed_size")
                await File.WriteAllBytesAsync(source, [1]);
            else if (scenario == "changed_time")
                File.SetLastWriteTimeUtc(source, input.ExpectedLastModifiedUtc.AddDays(1));
            else if (scenario == "destination_collision")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, [2]);
            }
            if (scenario == "cancelled")
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => provider.QuarantineAsync(input, custody.Owner, TimeProvider.System, cancellation.Token));
                await Assert.That(await File.ReadAllBytesAsync(source)).IsEquivalentTo(bytes);
                await Assert.That(File.Exists(destination)).IsFalse();
                return;
            }
            var result = await provider.QuarantineAsync(input, custody.Owner, TimeProvider.System, default);
            var expected = scenario switch
            {
                "move" => FileStorageQuarantineOutcome.Moved,
                "lost_acknowledgement" => FileStorageQuarantineOutcome.AlreadyQuarantined,
                "absent" => FileStorageQuarantineOutcome.AlreadyAbsent,
                _ => FileStorageQuarantineOutcome.Blocked
            };
            await Assert.That(result.Outcome).IsEqualTo(expected);
            if (result.Quarantined)
            {
                await Assert.That(File.Exists(source)).IsFalse();
                await Assert.That(await File.ReadAllBytesAsync(destination)).IsEquivalentTo(bytes);
                var replay = await provider.QuarantineAsync(input, custody.Owner, TimeProvider.System, default);
                await Assert.That(replay.Outcome).IsEqualTo(FileStorageQuarantineOutcome.AlreadyQuarantined);
            }
            else if (expected == FileStorageQuarantineOutcome.Blocked)
            {
                await Assert.That(File.Exists(source)).IsTrue();
                if (scenario == "destination_collision")
                    await Assert.That(await File.ReadAllBytesAsync(destination)).IsEquivalentTo(new byte[] { 2 });
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
