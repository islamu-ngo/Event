using System.Security.Cryptography;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ProgramTests
{
    [Test]
    public async Task VerifyToolsRejectsMissingLock()
    {
        using var fixture = new ToolFixture();

        (int exitCode, string output) = fixture.Run(lockPath: Path.Combine(fixture.Root, "missing.json"));

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainNotConfigured);
        await Assert.That(output).IsEqualTo("untrusted_tool: toolchain lock is not configured" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsMalformedLock()
    {
        using var fixture = new ToolFixture();
        File.WriteAllText(fixture.LockPath, "{");

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: toolchain lock is malformed" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsUnsupportedSchema()
    {
        using var fixture = new ToolFixture();
        fixture.WriteExecutable("git-cliff 2.13.1");
        fixture.WriteLock(schemaVersion: 2);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: toolchain lock is malformed" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsMissingBundleDirectory()
    {
        using var fixture = new ToolFixture();
        fixture.WriteLock("00");

        (int exitCode, string output) = fixture.Run(bundlePath: Path.Combine(fixture.Root, "missing"));

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: local tool bundle directory is required" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsUnapprovedPlatform()
    {
        using var fixture = new ToolFixture();
        fixture.WriteLock("00");

        (int exitCode, string output) = fixture.Run(platform: "macos-arm64");

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: platform is not approved: macos-arm64" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsMissingExecutable()
    {
        using var fixture = new ToolFixture();
        fixture.WriteLock(new string('0', 64));

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: required executable is missing: git-cliff" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsWrongDigest()
    {
        using var fixture = new ToolFixture();
        fixture.WriteExecutable("git-cliff 2.13.1");
        fixture.WriteLock(new string('0', 64));

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: executable digest mismatch: git-cliff" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsWrongVersion()
    {
        using var fixture = new ToolFixture();
        string digest = fixture.WriteExecutable("git-cliff 2.13.0");
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: version mismatch: expected git-cliff 2.13.1" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsFailedVersionProbe()
    {
        using var fixture = new ToolFixture();
        string digest = fixture.WriteExecutable("probe failed", exitCode: 7);
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: version probe failed" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsHungVersionProbe()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ToolFixture();
        string digest = fixture.WriteExecutable("git-cliff 2.13.1", delaySeconds: 10);
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run(timeout: TimeSpan.FromMilliseconds(100));

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: version probe timed out or exceeded output limit" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsExcessiveVersionOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ToolFixture();
        string digest = fixture.WriteExecutable(new string('x', 5000));
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: version probe timed out or exceeded output limit" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsAcceptsExactLocalExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ToolFixture();
        string digest = fixture.WriteExecutable("git-cliff 2.13.1");
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.Success);
        await Assert.That(output).IsEqualTo("trusted_tool: git-cliff 2.13.1 (linux-x64)" + Environment.NewLine);
    }

    [Test]
    public async Task UnknownCommandReturnsStableBoundedUsageError()
    {
        using var output = new StringWriter();

        int exitCode = Program.Run(["unexpected-command"], output);

        await Assert.That(exitCode).IsEqualTo(Program.UsageError);
        await Assert.That(output.ToString()).IsEqualTo(
            "unknown_command: run without arguments to see supported commands" +
            Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsArgumentsReturnStableUsageError()
    {
        using var output = new StringWriter();

        int exitCode = Program.Run(["verify-tools", "extra"], output);

        await Assert.That(exitCode).IsEqualTo(Program.UsageError);
        await Assert.That(output.ToString()).StartsWith(
            "invalid_arguments: verify-tools accepts no arguments" +
            Environment.NewLine +
            "usage: release-engine allocate-change-id");
        await Assert.That(output.ToString()).Contains("preflight-range");
        await Assert.That(output.ToString()).Contains("rename-change");
    }

    private sealed class ToolFixture : IDisposable
    {
        public ToolFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-release-{Guid.NewGuid():N}");
            BundlePath = Path.Combine(Root, "bundle");
            LockPath = Path.Combine(Root, "toolchain.lock.json");
            Directory.CreateDirectory(BundlePath);
        }

        public string Root { get; }
        public string BundlePath { get; }
        public string LockPath { get; }

        public (int ExitCode, string Output) Run(
            string? lockPath = null,
            string? bundlePath = null,
            string platform = "linux-x64",
            TimeSpan? timeout = null)
        {
            using var output = new StringWriter();
            int exitCode = Program.Run(
                ["verify-tools"],
                output,
                lockPath ?? LockPath,
                bundlePath ?? BundlePath,
                platform,
                timeout ?? TimeSpan.FromSeconds(2));
            return (exitCode, output.ToString());
        }

        public string WriteExecutable(string output, int exitCode = 0, int delaySeconds = 0)
        {
            string path = Path.Combine(BundlePath, "git-cliff");
            string delay = delaySeconds == 0 ? string.Empty : $"sleep {delaySeconds}\n";
            File.WriteAllText(path, $"#!/bin/sh\n{delay}printf '%s\\n' '{output}'\nexit {exitCode}\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }

        public void WriteLock(string executableDigest = "", int schemaVersion = 1)
        {
            File.WriteAllText(
                LockPath,
                $$"""
                {
                  "schemaVersion": {{schemaVersion}},
                  "tool": "git-cliff",
                  "version": "2.13.1",
                  "platforms": [
                    {
                      "platform": "linux-x64",
                      "executable": "git-cliff",
                      "executableSha256": "{{executableDigest}}"
                    }
                  ]
                }
                """);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
