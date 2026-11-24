using System.Diagnostics;
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
        fixture.WriteExecutable("git-cliff 2.14.2");
        fixture.WriteLock(schemaVersion: 2);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: toolchain lock is malformed" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsMissingBundleDirectory()
    {
        using var fixture = new ToolFixture();
        fixture.WriteLock(new string('0', 64));

        (int exitCode, string output) = fixture.Run(bundlePath: Path.Combine(fixture.Root, "missing"));

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: local tool bundle directory is required" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsUnapprovedPlatform()
    {
        using var fixture = new ToolFixture();
        fixture.WriteLock(new string('0', 64));

        (int exitCode, string output) = fixture.Run(platform: "freebsd-x64");

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: platform is not approved: freebsd-x64" + Environment.NewLine);
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
        fixture.WriteExecutable("git-cliff 2.14.2");
        fixture.WriteLock(new string('0', 64));

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: executable digest mismatch: git-cliff" + Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsRejectsWrongVersion()
    {
        using var fixture = new ToolFixture();
        string digest = fixture.WriteExecutable("git-cliff 2.13.1");
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("untrusted_tool: version mismatch: expected git-cliff 2.14.2" + Environment.NewLine);
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
        string digest = fixture.WriteExecutable("git-cliff 2.14.2", delaySeconds: 10);
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
        string digest = fixture.WriteExecutable("git-cliff 2.14.2");
        fixture.WriteLock(digest);

        (int exitCode, string output) = fixture.Run();

        await Assert.That(exitCode).IsEqualTo(Program.Success);
        await Assert.That(output).IsEqualTo("trusted_tool: git-cliff 2.14.2 (linux-x64)" + Environment.NewLine);
    }

    [Test]
    [Arguments("unexpected-command")]
    [Arguments("run-promoted")]
    [Arguments("verify-candidate")]
    [Arguments("verify-tag")]
    [Arguments("tag-message")]
    [Arguments("verify-main")]
    [Arguments("verify-baseline")]
    [Arguments("open-maintenance-line")]
    [Arguments("activate-trust")]
    [Arguments("prepare-publication-inventory")]
    [Arguments("execute-security-execution")]
    [Arguments("execute-api-benchmarks")]
    [Arguments("produce-security-execution")]
    [Arguments("rename-change")]
    public async Task RetiredAndUnknownCommandsReturnUsageErrorWithoutCompatibilityAliases(string command)
    {
        using var output = new StringWriter();

        int exitCode = Program.Run([command], output);

        await Assert.That(exitCode).IsEqualTo(Program.UsageError);
        await Assert.That(output.ToString()).IsEqualTo(
            "unknown_command: run without arguments to see supported commands" +
            Environment.NewLine);
    }

    [Test]
    public async Task VerifyToolsArgumentsReturnStreamlinedUsageError()
    {
        using var output = new StringWriter();

        int exitCode = Program.Run(["verify-tools", "extra"], output);

        await Assert.That(exitCode).IsEqualTo(Program.UsageError);
        string text = output.ToString();
        await Assert.That(text).StartsWith(
            "invalid_arguments: verify-tools accepts no arguments" +
            Environment.NewLine +
            "usage: release-engine ");
        await Assert.That(text).Contains("ensure-git-cliff");
        await Assert.That(text).Contains("verify-release-tag");
        await Assert.That(text).Contains("preflight-range");
        await Assert.That(text).DoesNotContain("run-promoted");
        await Assert.That(text).DoesNotContain("verify-candidate");
        await Assert.That(text).DoesNotContain("activate-trust");
        await Assert.That(text).DoesNotContain("execute-security-execution");
        await Assert.That(text).DoesNotContain("rename-change");
    }

    [Test]
    public async Task VerifyReleaseTagCliAcceptsValidSignedTagAndRejectsTamperedTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"islamu-cli-tag-{Guid.NewGuid():N}");
        string repoRoot = Path.Combine(tempRoot, "repo");
        string keyPath = Path.Combine(tempRoot, "signer");
        string allowedSigners = Path.Combine(tempRoot, "allowed-signers");
        Directory.CreateDirectory(repoRoot);
        try
        {
            RunProcess(ReleaseToolPaths.SshKeygen, tempRoot, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "cli-test", "-f", keyPath);
            string pub = string.Join(' ', File.ReadAllText(keyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedSigners, $"release-operator namespaces=\"git\" {pub}\n");
            RunProcess(ReleaseToolPaths.Git, repoRoot, "init", "--initial-branch=main");
            File.WriteAllText(Path.Combine(repoRoot, "README.md"), "hello\n");
            RunProcess(ReleaseToolPaths.Git, repoRoot, "add", "README.md");
            RunProcess(ReleaseToolPaths.Git, repoRoot, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-q", "-m", "feat(events): init");
            string commit = RunProcess(ReleaseToolPaths.Git, repoRoot, "rev-parse", "HEAD^{commit}").Trim();
            RunProcess(ReleaseToolPaths.Git, repoRoot, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "gpg.format=ssh", "-c", $"user.signingKey={keyPath}", "tag", "-s", "v0.1.0", commit, "-m", "Release v0.1.0");

            using var okOutput = new StringWriter();
            int okCode = Program.Run(
                ["verify-release-tag", "--repository", repoRoot, "--tag", "v0.1.0", "--target", commit, "--event-sha", commit, "--allowed-signers", allowedSigners],
                okOutput);
            await Assert.That(okCode).IsEqualTo(Program.Success).Because(okOutput.ToString());
            await Assert.That(okOutput.ToString()).Contains("verified_release_tag: v0.1.0");
            await Assert.That(okOutput.ToString()).Contains("signer=release-operator");

            using var badOutput = new StringWriter();
            int badCode = Program.Run(
                ["verify-release-tag", "--repository", repoRoot, "--tag", "v0.1.0", "--event-sha", new string('a', 40), "--allowed-signers", allowedSigners],
                badOutput);
            await Assert.That(badCode).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(badOutput.ToString()).Contains("tag_event_sha_mismatch");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string RunProcess(string executable, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"{executable} failed: {stderr}");
        }

        return stdout;
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
                  "version": "2.14.2",
                  "platforms": [
                    {
                      "platform": "linux-x64",
                      "executable": "git-cliff",
                      "executableSha256": "{{executableDigest}}",
                      "archive": "git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz",
                      "archiveSha256": "{{new string('1', 64)}}",
                      "source": "https://github.com/orhun/git-cliff/releases/download/v2.14.2/git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz"
                    }
                  ]
                }
                """);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
