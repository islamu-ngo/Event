using System.Security.Cryptography;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class GitCliffProcessExecutorTests
{
    private const string SampleVersionTag = "v1.1.0";
    private const string UntrustedToolDiagnostic = "git_cliff_tool_untrusted";

    [Test]
    public async Task RejectsDigestMismatchedExecutableWithoutExecutingIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        string markerPath = Path.Combine(fixture.Root, "tampered-executed.marker");
        fixture.WriteExecutable($"touch \"{markerPath}\"\nexit 0", updateLockDigest: false);

        var executor = new GitCliffProcessExecutor();

        GitCliffContextExportResult export = executor.ExportContext(new GitCliffContextExportRequest(
            fixture.ToolConfiguration, fixture.WorkDir, SampleVersionTag, "v1.0.0..HEAD"));
        GitCliffRenderFromContextResult render = executor.RenderFromContext(new GitCliffRenderFromContextRequest(
            fixture.ToolConfiguration, fixture.WorkDir, "[]"));
        GitCliffBumpVersionResult bump = executor.SuggestBumpedVersion(new GitCliffBumpVersionRequest(
            fixture.ToolConfiguration, fixture.WorkDir, "v1.0.0..HEAD"));

        await Assert.That(export.IsValid).IsFalse();
        await Assert.That(export.Diagnostic).IsEqualTo(UntrustedToolDiagnostic);
        await Assert.That(render.IsValid).IsFalse();
        await Assert.That(render.Diagnostic).IsEqualTo(UntrustedToolDiagnostic);
        await Assert.That(bump.IsValid).IsFalse();
        await Assert.That(bump.Diagnostic).IsEqualTo(UntrustedToolDiagnostic);
        await Assert.That(File.Exists(markerPath)).IsFalse();
    }

    [Test]
    public async Task RejectsVersionMismatchedExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        fixture.WriteExecutable("printf 'git-cliff 2.13.1\\n'", updateLockDigest: true);

        GitCliffBumpVersionResult bump = new GitCliffProcessExecutor().SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(fixture.ToolConfiguration, fixture.WorkDir, null));

        await Assert.That(bump.IsValid).IsFalse();
        await Assert.That(bump.Diagnostic).IsEqualTo(UntrustedToolDiagnostic);
    }

    [Test]
    public async Task PassesOfflineNoExecConfigAndArgumentArraysWithoutShellExpansion()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        string argsCapturePath = Path.Combine(fixture.Root, "captured-args.txt");
        string envCapturePath = Path.Combine(fixture.Root, "captured-env.txt");
        string injectionMarker = Path.Combine(fixture.Root, "shell-injection.marker");
        fixture.WriteExecutable(
            $$"""
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            printf '%s\n' "$@" > "{{argsCapturePath}}"
            printf '%s\n' "$GIT_CLIFF_OFFLINE" > "{{envCapturePath}}"
            printf '[{"version":"v1.1.0","commits":[]}]\n'
            """,
            updateLockDigest: true);

        string maliciousRange = $"v1.0.0..HEAD;touch {injectionMarker}";
        GitCliffContextExportResult result = new GitCliffProcessExecutor().ExportContext(
            new GitCliffContextExportRequest(fixture.ToolConfiguration, fixture.WorkDir, SampleVersionTag, maliciousRange));

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "export");
        await Assert.That(File.Exists(injectionMarker)).IsFalse();
        string[] capturedArgs = await File.ReadAllLinesAsync(argsCapturePath);
        await Assert.That(capturedArgs).IsEquivalentTo([
            "--config", fixture.ConfigPath,
            "--offline",
            "--no-exec",
            "--context",
            "--tag", SampleVersionTag,
            "--", maliciousRange,
        ]);
        await Assert.That((await File.ReadAllTextAsync(envCapturePath)).Trim()).IsEqualTo("1");
    }

    [Test]
    public async Task EnforcesTimeoutAndKillsHungProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            sleep 30
            """,
            updateLockDigest: true);

        GitCliffToolConfiguration shortTimeout = fixture.ToolConfiguration with { Timeout = TimeSpan.FromMilliseconds(250) };
        GitCliffBumpVersionResult result = new GitCliffProcessExecutor().SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(shortTimeout, fixture.WorkDir, null));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo("git_cliff_timeout");
    }

    [Test]
    public async Task EnforcesStdoutAndStderrOutputBounds()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            printf '%.0sA' $(seq 1 512)
            """,
            updateLockDigest: true);

        var bounded = new GitCliffProcessExecutor(maximumStdoutBytes: 128, maximumStderrBytes: 128);
        GitCliffContextExportResult stdoutExceeded = bounded.ExportContext(
            new GitCliffContextExportRequest(fixture.ToolConfiguration, fixture.WorkDir, SampleVersionTag, null));

        await Assert.That(stdoutExceeded.IsValid).IsFalse();
        await Assert.That(stdoutExceeded.Diagnostic).IsEqualTo("git_cliff_output_limit_exceeded");

        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            printf '%.0sE' $(seq 1 512) >&2
            printf '[]\n'
            """,
            updateLockDigest: true);

        GitCliffContextExportResult stderrExceeded = bounded.ExportContext(
            new GitCliffContextExportRequest(fixture.ToolConfiguration, fixture.WorkDir, SampleVersionTag, null));

        await Assert.That(stderrExceeded.IsValid).IsFalse();
        await Assert.That(stderrExceeded.Diagnostic).IsEqualTo("git_cliff_output_limit_exceeded");
    }

    [Test]
    public async Task DoesNotLeakRawContextOrSensitiveStderrIntoDiagnostics()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            cat >&2
            exit 9
            """,
            updateLockDigest: true);

        const string poisoned = "[{\"version\":\"v1.1.0\",\"commits\":[],\"extra\":{\"secret\":\"sensitive-author@example.invalid\"}}]";
        GitCliffRenderFromContextResult result = new GitCliffProcessExecutor().RenderFromContext(
            new GitCliffRenderFromContextRequest(fixture.ToolConfiguration, fixture.WorkDir, poisoned));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo("git_cliff_exit_nonzero");
        await Assert.That(result.Diagnostic!).DoesNotContain("sensitive-author@example.invalid");
        await Assert.That(result.Diagnostic!).DoesNotContain("version");
    }

    [Test]
    public async Task SuggestBumpedVersionNormalizesOptionalVPrefixAndRejectsMalformedOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            printf 'v1.2.0\n'
            """,
            updateLockDigest: true);

        GitCliffBumpVersionResult valid = new GitCliffProcessExecutor().SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(fixture.ToolConfiguration, fixture.WorkDir, "v1.1.0..HEAD"));
        await Assert.That(valid.IsValid).IsTrue().Because(valid.Diagnostic ?? "bump");
        await Assert.That(valid.SuggestedVersion).IsEqualTo("1.2.0");
        await Assert.That(valid.RawSuggestedVersion).IsEqualTo("v1.2.0");

        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf 'git-cliff 2.14.2\n'; exit 0; fi
            printf 'not-a-semver-output\n'
            """,
            updateLockDigest: true);

        GitCliffBumpVersionResult invalid = new GitCliffProcessExecutor().SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(fixture.ToolConfiguration, fixture.WorkDir, "v1.1.0..HEAD"));
        await Assert.That(invalid.IsValid).IsFalse();
        await Assert.That(invalid.Diagnostic).IsEqualTo("git_cliff_invalid_bump_version");
    }

    [Test]
    public async Task RejectsSymlinkedConfigOrMissingWorkingDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new FakeGitCliffFixture();
        string linkedConfig = Path.Combine(fixture.Root, "linked-cliff.toml");
        File.CreateSymbolicLink(linkedConfig, fixture.ConfigPath);

        GitCliffContextExportResult badConfig = new GitCliffProcessExecutor().ExportContext(
            new GitCliffContextExportRequest(fixture.ToolConfiguration with { ConfigPath = linkedConfig }, fixture.WorkDir, "v1.0.0", null));
        GitCliffContextExportResult badWorkDir = new GitCliffProcessExecutor().ExportContext(
            new GitCliffContextExportRequest(fixture.ToolConfiguration, Path.Combine(fixture.Root, "missing"), "v1.0.0", null));

        await Assert.That(badConfig.IsValid).IsFalse();
        await Assert.That(badConfig.Diagnostic).IsEqualTo("git_cliff_config_invalid");
        await Assert.That(badWorkDir.IsValid).IsFalse();
        await Assert.That(badWorkDir.Diagnostic).IsEqualTo("git_cliff_working_directory_invalid");
    }

    private sealed class FakeGitCliffFixture : IDisposable
    {
        private readonly string bundleDir;
        private readonly string lockPath;
        private readonly string executablePath;

        public FakeGitCliffFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-gc-exec-{Guid.NewGuid():N}");
            bundleDir = Path.Combine(Root, "bundle");
            WorkDir = Path.Combine(Root, "work");
            Directory.CreateDirectory(bundleDir);
            Directory.CreateDirectory(WorkDir);

            lockPath = Path.Combine(bundleDir, "toolchain.lock.json");
            ConfigPath = Path.Combine(Root, "cliff.toml");
            executablePath = Path.Combine(bundleDir, "git-cliff");
            File.WriteAllText(ConfigPath, "[changelog]\nbody = \"\"\n");

            WriteExecutable("if [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.14.2\\n'; exit 0; fi\nprintf '[]\\n'", updateLockDigest: true);
        }

        public string Root { get; }
        public string WorkDir { get; }
        public string ConfigPath { get; }

        public GitCliffToolConfiguration ToolConfiguration => new(
            lockPath,
            bundleDir,
            ConfigPath,
            "linux-x64",
            TimeSpan.FromSeconds(5));

        public void WriteExecutable(string scriptBody, bool updateLockDigest)
        {
            File.WriteAllText(executablePath, "#!/bin/sh\n" + scriptBody + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (updateLockDigest)
            {
                string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executablePath)));
                File.WriteAllText(lockPath, $$"""
                    {
                      "schemaVersion": 1,
                      "tool": "git-cliff",
                      "version": "2.14.2",
                      "platforms": [
                        {
                          "platform": "linux-x64",
                          "executable": "git-cliff",
                          "executableSha256": "{{digest}}",
                          "archive": "git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz",
                          "archiveSha256": "{{new string('1', 64)}}",
                          "source": "https://github.com/orhun/git-cliff/releases/download/v2.14.2/git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz"
                        }
                      ]
                    }
                    """);
            }
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
