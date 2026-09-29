using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ISLAMU.SetupAssistant.Cli.Tests;

public sealed class SetupCliProgramTests
{
    [Test]
    public async Task EmptyInvocationAndHelpShowAvailableCommands()
    {
        ProcessResult empty = await ExecuteAsync([]);
        ProcessResult help = await ExecuteAsync(["--help"]);

        await Assert.That(empty.ExitCode).IsEqualTo(0);
        await Assert.That(empty.StandardError).IsEmpty();
        string usage = Encoding.UTF8.GetString(empty.StandardOutput);
        foreach (string family in SetupCliContractSpecification.Operations.Keys)
            await Assert.That(usage).Contains(family);
        await Assert.That(usage).Contains("event-setup doctor --machine");
        await Assert.That(help.ExitCode).IsEqualTo(0);
        await Assert.That(help.StandardOutput).IsEquivalentTo(empty.StandardOutput);
    }

    [Test]
    public async Task IncompleteCommandsExplainRequiredOptionsWithoutChangingMachineDiagnostics()
    {
        var cases = new (string[] Arguments, string Code, string Example)[]
        {
            (["catalogue", "list"], "output-required", "event-setup catalogue list --output -"),
            (["catalogue", "show"], "key-required", "event-setup catalogue show --key API_HTTP_PORT --output -"),
            (["manifest", "create"], "output-required", "event-setup manifest create --output instance-manifest.json")
        };

        foreach ((string[] arguments, string code, string example) in cases)
        {
            ProcessResult text = await ExecuteAsync(arguments);
            await Assert.That(text.ExitCode).IsEqualTo(64);
            await Assert.That(text.StandardError).IsEmpty();
            string guidance = Encoding.UTF8.GetString(text.StandardOutput);
            await Assert.That(guidance).Contains(code);
            await Assert.That(guidance).Contains(example);

            ProcessResult machine = await ExecuteAsync([.. arguments, "--machine"]);
            await Assert.That(machine.ExitCode).IsEqualTo(64);
            await Assert.That(machine.StandardError).IsEmpty();
            await Assert.That(SetupCliMachineContractVerifier.Validate(machine.StandardOutput)).IsEmpty();
            using JsonDocument result = JsonDocument.Parse(machine.StandardOutput);
            await Assert.That(result.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString())
                .IsEqualTo(code);
        }

        ProcessResult rejected = await ExecuteAsync(["catalogue", "show", "--key", "person@example.invalid"]);
        await Assert.That(Encoding.UTF8.GetString(rejected.StandardOutput))
            .DoesNotContain("person@example.invalid");
    }

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
