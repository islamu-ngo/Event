using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace ISLAMU.SetupAssistant.Cli.Tests;

public sealed class SetupCliProgramTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task FrameworkParseFailureCannotDiscloseInputPaths(bool machine)
    {
        string marker = Guid.CreateVersion7().ToString("N");
        ProcessResult result = await ExecuteAsync(
            ["manifest", "diff", "--input", $"/synthetic/{marker}.json",
             "--baseline", "-x", machine ? "--machine" : "--text"]);

        await Assert.That(Encoding.UTF8.GetString(result.StandardOutput)).DoesNotContain(marker);
        await Assert.That(Encoding.UTF8.GetString(result.StandardError)).DoesNotContain(marker);
        await Assert.That(result.ExitCode).IsEqualTo(64);
        if (machine)
            await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
    }

    [Test]
    public async Task RestrictedEnvironmentCannotReachExecutableStandardOutput()
    {
        ProcessResult result = await ExecuteAsync(["env", "render", "--output", "-"]);

        await Assert.That(Encoding.UTF8.GetString(result.StandardOutput)).DoesNotContain("SETUP_SECRET=");
        await Assert.That(result.ExitCode).IsEqualTo(74);
    }

    [Test]
    public async Task PublicCatalogueStillReachesStandardOutput()
    {
        ProcessResult result = await ExecuteAsync(["catalogue", "show", "--key", "API_HTTP_PORT", "--output", "-"]);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        using JsonDocument catalogue = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(catalogue.RootElement.GetProperty("key").GetString()).IsEqualTo("API_HTTP_PORT");
    }

    [Test]
    public async Task MachineValidationNeverReturnsInputValuesOrPaths()
    {
        string path = Path.Combine(Path.GetTempPath(), "event-setup-input-" + Guid.NewGuid().ToString("N"));
        string value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(path, "SETUP_SECRET=" + value + "\n");
        try
        {
            ProcessResult result = await ExecuteAsync(["env", "validate", "--input", path, "--machine"]);
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardError).IsEmpty();
            await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
            string output = Encoding.UTF8.GetString(result.StandardOutput);
            await Assert.That(output).DoesNotContain(value);
            await Assert.That(output).DoesNotContain(path);
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            await Assert.That(document.RootElement.GetProperty("artifacts")[0].GetProperty("sensitivity").GetString())
                .IsEqualTo("sensitive");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task RestrictedExecutableFileOutputRequiresProvedHostProtection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "event-setup-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "deployment.env");
        try
        {
            ProcessResult result = await ExecuteAsync(["env", "render", "--output", target, "--machine"]);
            await Assert.That(result.StandardError).IsEmpty();
            await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
            await Assert.That(Encoding.UTF8.GetString(result.StandardOutput)).DoesNotContain("SETUP_SECRET=");
            await Assert.That(result.ExitCode).IsEqualTo(OperatingSystem.IsLinux() ? 0 : 74);
            if (OperatingSystem.IsLinux())
            {
                await Assert.That(File.GetUnixFileMode(target)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
                byte[] original = await File.ReadAllBytesAsync(target);
                ProcessResult overwrite = await ExecuteAsync(["env", "render", "--output", target, "--machine"]);
                await Assert.That(overwrite.ExitCode).IsEqualTo(74);
                await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(original);
                await Assert.That(Directory.GetFileSystemEntries(directory)).IsEquivalentTo([target]);
            }
            else
                await Assert.That(Directory.GetFileSystemEntries(directory)).IsEmpty();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

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
        await Assert.That(help.ExitCode).IsEqualTo(0);
        await Assert.That(help.StandardOutput).IsEquivalentTo(empty.StandardOutput);
    }

    [Test]
    [Arguments("catalogue", "")]
    [Arguments("manifest", "validate")]
    [Arguments("env", "render")]
    [Arguments("portability", "import-operator-identity")]
    public async Task HelpDoesNotRequireOperationalInputsOrOutput(string family, string operation)
    {
        string[] arguments = operation.Length == 0
            ? [family, "--help"]
            : [family, operation, "--help"];
        ProcessResult human = await ExecuteAsync(arguments);
        await Assert.That(human.ExitCode).IsEqualTo(0);
        await Assert.That(human.StandardOutput.Length).IsGreaterThan(0);
        await Assert.That(human.StandardError).IsEmpty();

        ProcessResult machine = await ExecuteAsync([.. arguments, "--machine"]);
        await Assert.That(machine.ExitCode).IsEqualTo(0);
        await Assert.That(SetupCliMachineContractVerifier.Validate(machine.StandardOutput)).IsEmpty();
    }

    [Test]
    public async Task IncompleteCommandsRenderHumanDiagnosticsWithoutChangingMachineDiagnostics()
    {
        var cases = new (string[] Arguments, string Code)[]
        {
            (["catalogue", "list"], "output-required"),
            (["catalogue", "show"], "key-required"),
            (["manifest", "create"], "output-required")
        };

        foreach ((string[] arguments, string code) in cases)
        {
            ProcessResult text = await ExecuteAsync(arguments);
            await Assert.That(text.ExitCode).IsEqualTo(64);
            await Assert.That(text.StandardError).IsEmpty();
            string guidance = Encoding.UTF8.GetString(text.StandardOutput);
            await Assert.That(guidance).Contains(code);

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
    public async Task HostileArgumentsAreRejectedBeforeParserOutputCanRevealValues()
    {
        string value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string path = Path.Combine(Path.GetTempPath(), "credential-" + Guid.NewGuid().ToString("N"));
        var vectors = new[]
        {
            new[] { "doctor", "--machine", "--password", value },
            new[] { "manifest", "validate", "--machine", "--input", path },
            new[] { "doctor", "--machine", "\u001b]0;" + value + "\u0007" }
        };

        foreach (string[] arguments in vectors)
        {
            ProcessResult result = await ExecuteAsync(arguments);
            await Assert.That(result.ExitCode).IsEqualTo(64);
            await Assert.That(result.StandardError).IsEmpty();
            await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
            string output = Encoding.UTF8.GetString(result.StandardOutput);
            await Assert.That(output).DoesNotContain(value);
            await Assert.That(output).DoesNotContain(path);
        }
    }

    [Test]
    public async Task ExecutableChecksEnvironmentNamesWithoutReadingTheirValues()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string name = "SERVICE_TOKEN_" + suffix;
        string value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ProcessResult result = await ExecuteAsync(["doctor", "--machine"], new Dictionary<string, string>
        {
            [name] = value
        });

        await Assert.That(result.ExitCode).IsEqualTo(4);
        await Assert.That(result.StandardError).IsEmpty();
        await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
        string output = Encoding.UTF8.GetString(result.StandardOutput);
        await Assert.That(output).DoesNotContain(name);
        await Assert.That(output).DoesNotContain(value);
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

    private static async Task<ProcessResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "Event.SetupAssistant.Cli.exe" : "Event.SetupAssistant.Cli"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        string[] hostVariables = ["PATH", "SystemRoot", "DOTNET_ROOT", "DOTNET_ROOT_X64",
            "DOTNET_ROOT_ARM64", "TEMP", "TMP", "TMPDIR"];
        foreach (string name in info.Environment.Keys.ToArray())
        {
            if (!hostVariables.Contains(name, StringComparer.OrdinalIgnoreCase))
                info.Environment.Remove(name);
        }
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach ((string name, string value) in environment)
                info.Environment[name] = value;
        }
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
