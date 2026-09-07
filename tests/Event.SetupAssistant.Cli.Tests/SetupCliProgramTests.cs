using System.Diagnostics;

namespace ISLAMU.SetupAssistant.Cli.Tests;

public sealed class SetupCliProgramTests
{
    [Test]
    public async Task OversizedMachineArgumentProducesOneUsageObjectWithoutStderr()
    {
        ProcessResult result = await ExecuteAsync(["doctor", "--machine", new string('x', 5_000)]);

        await Assert.That(result.ExitCode).IsEqualTo(64);
        await Assert.That(result.StandardError).IsEmpty();
        await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
    }

    [Test]
    public async Task OversizedMachineArtifactProducesOneIoObjectWithoutStderr()
    {
        string path = Path.Combine(Path.GetTempPath(), "event-setup-bound-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(path, new byte[(4 * 1024 * 1024) + 1]);
        try
        {
            ProcessResult result = await ExecuteAsync(["manifest", "validate", "--input", path, "--machine"]);
            await Assert.That(result.ExitCode).IsEqualTo(74);
            await Assert.That(result.StandardError).IsEmpty();
            await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
        }
        finally { File.Delete(path); }
    }

    private static async Task<ProcessResult> ExecuteAsync(IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Event.SetupAssistant.Cli"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        if (!process.Start()) throw new InvalidOperationException("process-start-failed");
        Task<byte[]> output = ReadAllAsync(process.StandardOutput.BaseStream);
        Task<byte[]> error = ReadAllAsync(process.StandardError.BaseStream);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await exited.Task.WaitAsync(timeout.Token);
        return new ProcessResult(process.ExitCode, await output.WaitAsync(timeout.Token), await error.WaitAsync(timeout.Token));
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private sealed record ProcessResult(int ExitCode, byte[] StandardOutput, byte[] StandardError);
}
