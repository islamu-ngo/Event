using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SyncPublicChangelogTests
{
    private const string SampleVersion = "1.1.0";
    private const string SampleReleaseDate = "2026-08-14";
    private const string SampleSummary = "Attendees can now correct registration details.";
    private const string BaselineTag = "v1.0.0";
    private const string GitUserNameConfig = "user.name=Release Test";
    private const string GitUserEmailConfig = "user.email=release@example.invalid";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    [Test]
    public async Task RebuildsFromSignedAnnotatedReleaseDirectoriesAndCheckModeNeverWrites()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ChangelogFixture();
        fixture.CreateSignedRelease(SampleVersion, SampleReleaseDate, SampleSummary);

        (int staleCode, string staleOutput) = fixture.Run(check: true);
        (int writeCode, string writeOutput) = fixture.Run();
        byte[] firstPage = await File.ReadAllBytesAsync(fixture.PagePath);
        byte[] firstManifest = await File.ReadAllBytesAsync(fixture.ManifestPath);
        (int currentCode, string currentOutput) = fixture.Run(check: true);
        (int repeatedCode, string repeatedOutput) = fixture.Run();

        await Assert.That(staleCode).IsNotEqualTo(Program.Success);
        await Assert.That(staleOutput).Contains("changelog_stale");
        await Assert.That(writeCode).IsEqualTo(Program.Success).Because(writeOutput);
        await Assert.That(Encoding.UTF8.GetString(firstPage)).Contains("## v1.1.0");
        await Assert.That(currentCode).IsEqualTo(Program.Success).Because(currentOutput);
        await Assert.That(repeatedCode).IsEqualTo(Program.Success).Because(repeatedOutput);
        await Assert.That(repeatedOutput).Contains("changelog_unchanged");
        await Assert.That(await File.ReadAllBytesAsync(fixture.PagePath)).IsEquivalentTo(firstPage);
        await Assert.That(await File.ReadAllBytesAsync(fixture.ManifestPath)).IsEquivalentTo(firstManifest);

        fixture.CreateSignedRelease("1.1.1", "2026-08-15", "Registration email validation is stricter.");
        (int newStale, _) = fixture.Run(check: true);
        (int unionCode, string unionOutput) = fixture.Run();
        string union = await File.ReadAllTextAsync(fixture.PagePath);

        await Assert.That(newStale).IsNotEqualTo(Program.Success);
        await Assert.That(unionCode).IsEqualTo(Program.Success).Because(unionOutput);
        await Assert.That(union).Contains("## v1.1.0");
        await Assert.That(union).Contains("## v1.1.1");
        await Assert.That(union.IndexOf("## v1.1.1", StringComparison.Ordinal)).IsLessThan(union.IndexOf("## v1.1.0", StringComparison.Ordinal));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.ManifestPath));
        await Assert.That(manifest.RootElement.GetProperty("releases").GetArrayLength()).IsEqualTo(2);
        await Assert.That(manifest.RootElement.GetProperty("projectionSha256").GetString()).IsEqualTo(Digest(await File.ReadAllBytesAsync(fixture.PagePath)));
    }

    [Test]
    public async Task RejectsUnsignedReleaseTagAndAcceptedPageMutationWithoutOverwriting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ChangelogFixture();
        fixture.CreateSignedRelease(SampleVersion, SampleReleaseDate, SampleSummary, signTag: false);
        (int unsignedCode, string unsignedOutput) = fixture.Run();
        await Assert.That(unsignedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unsignedOutput).Contains("tag_not_signed");

        using var signedFixture = new ChangelogFixture();
        signedFixture.CreateSignedRelease(SampleVersion, SampleReleaseDate, SampleSummary);
        (int generated, string generatedOutput) = signedFixture.Run();
        await Assert.That(generated).IsEqualTo(Program.Success).Because(generatedOutput);

        await File.AppendAllTextAsync(signedFixture.PagePath, "unauthorized mutation\n");
        byte[] mutated = await File.ReadAllBytesAsync(signedFixture.PagePath);
        (int rejected, string diagnostic) = signedFixture.Run();

        await Assert.That(rejected).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(diagnostic).Contains("changelog_accepted_projection_drift");
        await Assert.That(await File.ReadAllBytesAsync(signedFixture.PagePath)).IsEquivalentTo(mutated);
    }

    [Test]
    [Arguments("manifest", false)]
    [Arguments("page", false)]
    [Arguments("manifest", true)]
    [Arguments("page", true)]
    public async Task RecoversExactInterruptedGenerationAndRollsBackIoFailures(string replacement, bool ioFailure)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ChangelogFixture();
        fixture.CreateSignedRelease(SampleVersion, SampleReleaseDate, SampleSummary);
        (int initial, string initialOutput) = fixture.Run();
        await Assert.That(initial).IsEqualTo(Program.Success).Because(initialOutput);
        byte[] oldPage = await File.ReadAllBytesAsync(fixture.PagePath);
        byte[] oldManifest = await File.ReadAllBytesAsync(fixture.ManifestPath);

        fixture.CreateSignedRelease("1.1.1", "2026-08-15", "Registration email validation is stricter.");
        bool injected = false;
        using var failedOutput = new StringWriter();
        int? failedCode = null;
        try
        {
            failedCode = SyncPublicChangelogCommand.Run(
                fixture.Arguments(),
                failedOutput,
                fixture.RepositoryPath,
                member =>
                {
                    if (member != replacement) return;
                    injected = true;
                    if (ioFailure) throw new IOException("injected replacement failure");
                    throw new PublicationInterruptedException();
                });
        }
        catch (PublicationInterruptedException)
        {
        }

        await Assert.That(injected).IsTrue();
        if (ioFailure)
        {
            await Assert.That(failedCode).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(await File.ReadAllBytesAsync(fixture.PagePath)).IsEquivalentTo(oldPage);
            await Assert.That(await File.ReadAllBytesAsync(fixture.ManifestPath)).IsEquivalentTo(oldManifest);
            await Assert.That(File.Exists(fixture.TransactionPath)).IsFalse();
        }
        else
        {
            await Assert.That(File.Exists(fixture.TransactionPath)).IsTrue();
        }

        (int checkCode, string checkOutput) = fixture.Run(check: true);
        await Assert.That(checkCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(checkOutput).Contains(ioFailure ? "changelog_stale" : "changelog_transaction_incomplete");

        (int recovered, string recoveredOutput) = fixture.Run();
        await Assert.That(recovered).IsEqualTo(Program.Success).Because(recoveredOutput);
        await Assert.That(File.Exists(fixture.TransactionPath)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(fixture.PagePath)).Contains("## v1.1.1");
    }

    [Test]
    public async Task RejectsUnknownAndMissingArguments()
    {
        using var output = new StringWriter();
        int unknown = SyncPublicChangelogCommand.Run(["sync-public-changelog", "--unrecognized"], output, ".");
        int missing = SyncPublicChangelogCommand.Run(["sync-public-changelog", "--check"], output, ".");
        await Assert.That(unknown).IsEqualTo(Program.UsageError);
        await Assert.That(missing).IsEqualTo(Program.UsageError);
    }

    [Test]
    public async Task RejectsReleaseNotesContainingLeakedContributorIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ChangelogFixture();
        fixture.CreateSignedRelease(
            SampleVersion,
            SampleReleaseDate,
            "Attendees can edit details.",
            extraNotesContent: "\nMaintainer contact: leak@example.org\n");

        (int code, string output) = fixture.Run();

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).Contains("rendered_notes_identity_or_provider");
    }

    [Test]
    [Arguments("docs/internal/releases/1.1.0/summary.md")]
    [Arguments("docs/internal/releases/1.1.0/release-notes.md")]
    [Arguments("docs/internal/releases/1.1.0/release.yaml")]
    [Arguments("docs/internal/releases/1.1.0/release-context.v1.json")]
    [Arguments("docs/internal/releases/changes/CHG-2026-0001.yaml")]
    public async Task RejectsWorkingTreeEditsAfterSignedTagWithoutPublishingUnsignedText(string tamperedRelativePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ChangelogFixture();
        fixture.CreateSignedRelease(
            SampleVersion,
            SampleReleaseDate,
            SampleSummary,
            linkedChangeId: "CHG-2026-0001");

        (int initialCode, string initialOutput) = fixture.Run();
        await Assert.That(initialCode).IsEqualTo(Program.Success).Because(initialOutput);
        byte[] originalPage = await File.ReadAllBytesAsync(fixture.PagePath);
        byte[] originalManifest = await File.ReadAllBytesAsync(fixture.ManifestPath);

        string tamperedFullPath = Path.Combine(
            fixture.RepositoryPath,
            tamperedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.AppendAllTextAsync(tamperedFullPath, "\n# unsigned post-tag edit\n");

        (int rejectedCode, string rejectedOutput) = fixture.Run();

        await Assert.That(rejectedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(rejectedOutput).Contains($"changelog_signed_artifact_mismatch:{tamperedRelativePath}");
        await Assert.That(await File.ReadAllBytesAsync(fixture.PagePath)).IsEquivalentTo(originalPage);
        await Assert.That(await File.ReadAllBytesAsync(fixture.ManifestPath)).IsEquivalentTo(originalManifest);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class PublicationInterruptedException : Exception;

    private sealed class ChangelogFixture : IDisposable
    {
        private readonly string keyPath;
        private string previousTag;
        private string previousOid;
        private int sequence;

        public ChangelogFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-sync-changelog-{Guid.NewGuid():N}");
            RepositoryPath = Path.Combine(Root, "repo");
            keyPath = Path.Combine(Root, "signer-key");
            AllowedSignersPath = Path.Combine(Root, "allowed-signers");
            Directory.CreateDirectory(RepositoryPath);

            string changelogDir = Path.Combine(RepositoryPath, "docs", "public", "changelog");
            Directory.CreateDirectory(changelogDir);
            PagePath = Path.Combine(changelogDir, "README.md");
            ManifestPath = Path.Combine(changelogDir, "publication-manifest.v1.json");
            TransactionPath = Path.Combine(changelogDir, ".publication-transaction.v1.json");
            File.WriteAllText(PagePath, $"{PublicChangelogPolicy.StartMarker}\n{PublicChangelogPolicy.EndMarker}\n");

            RunExternal(ReleaseToolPaths.SshKeygen, Root, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "changelog-fixture", "-f", keyPath);
            string pub = string.Join(' ', File.ReadAllText(keyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(AllowedSignersPath, $"release-operator namespaces=\"git\" {pub}\n");

            Git("init", "--initial-branch=main");
            string initialOid = Commit("fix(events): establish repository baseline");
            Git("-c", GitUserNameConfig, "-c", GitUserEmailConfig,
                "-c", "gpg.format=ssh", "-c", $"user.signingKey={keyPath}",
                "tag", "-s", BaselineTag, initialOid, "-m", BaselineTag);
            previousTag = BaselineTag;
            previousOid = initialOid;
        }

        public string Root { get; }
        public string RepositoryPath { get; }
        public string AllowedSignersPath { get; }
        public string PagePath { get; }
        public string ManifestPath { get; }
        public string TransactionPath { get; }

        public string[] Arguments(bool check = false)
        {
            string[] args =
            [
                "sync-public-changelog",
                "--publication-base", "https://github.com/islamu-ngo/Event/blob/",
                "--allowed-signers", AllowedSignersPath,
            ];
            return check ? [.. args, "--check"] : args;
        }

        public (int Code, string Output) Run(bool check = false)
        {
            using var output = new StringWriter();
            int code = SyncPublicChangelogCommand.Run(Arguments(check), output, RepositoryPath);
            return (code, output.ToString());
        }

        public void CreateSignedRelease(
            string version,
            string releaseDate,
            string summary,
            bool signTag = true,
            string? extraNotesContent = null,
            string? linkedChangeId = null)
        {
            string line = "v" + string.Join('.', version.Split('.')[0], version.Split('.')[1]);
            string changeCommit = Commit($"feat(registration): deliver {version}");
            string releaseDir = Path.Combine(RepositoryPath, "docs", "internal", "releases", version);
            Directory.CreateDirectory(releaseDir);

            if (linkedChangeId is not null)
            {
                string changesDir = Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes");
                Directory.CreateDirectory(changesDir);
                File.WriteAllText(
                    Path.Combine(changesDir, linkedChangeId + ".yaml"),
                    $"Change-Id: {linkedChangeId}\nTitle: Registration detail correction\nType: feat\nScope: registration\nSummary: {summary}\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: SECURITY-POLICY.md\n    Disposition: not-applicable\n    Public-Disclosure: public\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_CHECKLIST.md\n    Disposition: not-applicable\n");
            }

            File.WriteAllText(
                Path.Combine(releaseDir, "release.yaml"),
                $"Version: {version}\nLine: {line}\nRelease-Date: {releaseDate}\nBase-Stable-Tag: {BaselineTag}\nPrevious-Published-Tag: {previousTag}\nRelease-Range:\n  Base-Ref: {BaselineTag}\n  Base-Oid: {previousOid}\n  Previous-Ref: {previousTag}\n  Previous-Oid: {previousOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: not-applicable\n");
            File.WriteAllText(Path.Combine(releaseDir, "summary.md"), summary + "\n");
            File.WriteAllText(Path.Combine(releaseDir, "release-notes.md"), $"# Release {version}\n\n{summary}\n{extraNotesContent ?? string.Empty}");

            var context = new ReleaseContext(
                1,
                new ReleaseContextRelease(version, line, releaseDate, BaselineTag, previousTag, "minor", "stable", true),
                [new ReleaseContextChange(changeCommit[..12], changeCommit, linkedChangeId, "feat", "registration", $"deliver {version}", summary, false, false, null)],
                new ReleaseContextEvidence(previousOid, previousOid, [
                    new ReleaseContextObject(previousOid[..12], previousOid),
                    new ReleaseContextObject(changeCommit[..12], changeCommit),
                ]));
            File.WriteAllText(Path.Combine(releaseDir, "release-context.v1.json"), JsonSerializer.Serialize(context, JsonOptions) + "\n");

            string prepOid = Commit($"docs(release): prepare {version}");
            if (signTag)
            {
                Git("-c", GitUserNameConfig, "-c", GitUserEmailConfig,
                    "-c", "gpg.format=ssh", "-c", $"user.signingKey={keyPath}",
                    "tag", "-s", $"v{version}", prepOid, "-m", $"Release v{version}");
            }
            else
            {
                Git("-c", GitUserNameConfig, "-c", GitUserEmailConfig,
                    "tag", "-a", $"v{version}", prepOid, "-m", $"Release v{version}");
            }

            previousTag = $"v{version}";
            previousOid = prepOid;
        }

        private string Commit(string message)
        {
            sequence++;
            string file = Path.Combine(RepositoryPath, $"change-{sequence}.txt");
            File.WriteAllText(file, $"value {sequence}\n");
            Git("add", "-A");
            Git("-c", GitUserNameConfig, "-c", GitUserEmailConfig, "commit", "-q", "-m", message);
            return Git("rev-parse", "HEAD^{commit}").Trim();
        }

        private string Git(params string[] arguments) => RunExternal(ReleaseToolPaths.Git, RepositoryPath, arguments);

        private static string RunExternal(string executable, string workingDirectory, params string[] arguments)
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

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
