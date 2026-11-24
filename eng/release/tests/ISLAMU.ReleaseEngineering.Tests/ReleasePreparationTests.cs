using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("ReleaseToolEnvironment")]
public sealed class ReleasePreparationTests
{
    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    [Test]
    public async Task PrepareWritesThreeNormalizedLayersAndIsByteIdempotent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();

        ReleasePreparationResult first = fixture.Prepare();
        byte[] firstBytes = File.ReadAllBytes(fixture.NotesPath);
        ReleasePreparationResult second = fixture.Prepare();

        await Assert.That(first.IsValid).IsTrue().Because(first.Diagnostic ?? "first prepare");
        await Assert.That(second.IsValid).IsTrue().Because(second.Diagnostic ?? "second prepare");
        await Assert.That(File.ReadAllBytes(fixture.NotesPath)).IsEquivalentTo(firstBytes);
        await Assert.That(Encoding.UTF8.GetString(firstBytes)).IsEqualTo(
            "# Release 1.1.0\n\n## Maintainer Summary\n\nAttendees can now correct registration details.\n\n## Release-Visible Details\n\n- registration: let attendees correct registration details (cccccccccccc)\n\n### Impact Summary\n\n#### Operator\n\n- `CHG-2026-0001` - documented: Restart registration workers after deployment\\. (Evidence: `docs/RELEASE\\_RUNBOOK\\.md`)\n\n## Complete Commit Range\n\n- `cccccccccccc`\n");
        await Assert.That(first.CommitMessage).IsEqualTo(
            "docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
        await Assert.That(Encoding.UTF8.GetString(firstBytes)).DoesNotContain("generated-region");
        await Assert.That(Directory.EnumerateFiles(fixture.ReleaseDirectory, "*.tmp", SearchOption.TopDirectoryOnly)).IsEmpty();
        await Assert.That(File.ReadAllText(fixture.SummaryPath)).IsEqualTo("Attendees can now correct registration details.\n");
        await Assert.That(File.ReadAllText(fixture.ReleasePath)).Contains("Version: 1.1.0");
    }

    [Test]
    public async Task PrepareFailsBeforeWriteForImpactDriftRendererFailureAndUnexpectedOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();
        ReleasePreparationResult drift = fixture.Prepare(
            fixture.Input with
            {
                Descriptor = fixture.Input.Descriptor! with
                {
                    ImpactDispositions = new Dictionary<string, string>(fixture.Input.Descriptor!.ImpactDispositions, StringComparer.Ordinal)
                    {
                        ["operator"] = "accepted",
                    },
                },
            });
        await Assert.That(drift.Diagnostic).IsEqualTo("prepare_impact_not_covered:operator");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();

        fixture.RendererFails = true;
        ReleasePreparationResult renderer = fixture.Prepare();
        await Assert.That(renderer.Diagnostic).IsEqualTo("prepare_renderer_failed");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();

        fixture.RendererFails = false;
        File.WriteAllText(fixture.NotesPath, "unexpected\n");
        ReleasePreparationResult collision = fixture.Prepare();
        await Assert.That(collision.Diagnostic).IsEqualTo("prepare_generated_file_unexpected");
        await Assert.That(File.ReadAllText(fixture.NotesPath)).IsEqualTo("unexpected\n");
        await Assert.That(File.ReadAllText(fixture.SummaryPath)).IsEqualTo("Attendees can now correct registration details.\n");
    }

    [Test]
    public async Task PrepareRejectsMissingBlankOrRestrictedApplicableImpactDetailBeforeRendering()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();
        PublicChangeFragment fragment = fixture.Input.Fragments.Single();
        FragmentImpact impact = fragment.Impacts["operator"];
        fixture.RendererFails = true;

        foreach (string? detail in new[] { null, "   ", "Contact maintainer@example.org after deployment." })
        {
            var impacts = new Dictionary<string, FragmentImpact>(fragment.Impacts, StringComparer.Ordinal)
            {
                ["operator"] = impact with { Detail = detail },
            };
            ReleasePreparationResult result = fixture.Prepare(fixture.Input with
            {
                Fragments = [fragment with { Impacts = impacts }],
            });

            await Assert.That(result.Diagnostic).IsEqualTo("prepare_impact_detail_invalid:operator:CHG-2026-0001");
            await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
        }
    }

    [Test]
    public async Task PrepareRequiresExactRangeAndContextEvidenceAccountingBeforeRendering()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture { RendererFails = true };
        ReleasePreparationResult extra = fixture.Prepare(rangeOids: [new string('c', 40), new string('d', 40)]);
        ReleasePreparationResult missing = fixture.Prepare(rangeOids: []);
        ReleasePreparationResult orphanEvidence = fixture.Prepare(context: fixture.WithEvidenceObject(new string('d', 40)));

        await Assert.That(extra.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(missing.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(orphanEvidence.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
    }

    [Test]
    public async Task PrepareRejectsUnnormalizedEvidenceOrderingBeforeRendering()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture { RendererFails = true };
        string secondOid = new('d', 40);
        ReleaseContext current = fixture.Context.Context!;
        ReleaseContext ordered = current with
        {
            Changes = current.Changes.Append(new ReleaseContextChange(
                secondOid[..12], secondOid, null, "fix", "registration", "Second change", "Second change", false, false, null)).ToArray(),
            Evidence = current.Evidence with
            {
                Objects = current.Evidence.Objects
                    .Where(value => value.Oid != new string('c', 40))
                    .Append(new ReleaseContextObject(secondOid[..12], secondOid))
                    .Append(new ReleaseContextObject(new string('c', 12), new string('c', 40))).ToArray(),
            },
        };
        ReleaseContextValidationResult context = fixture.AsValidationResult(ordered);

        ReleasePreparationResult result = fixture.Prepare(context: context, rangeOids: [new string('c', 40), secondOid]);

        await Assert.That(result.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
    }

    [Test]
    public async Task PreparePreservesChronologicalRangeIndependentOfNormalizedEvidenceOrder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();
        string secondOid = new('d', 40);
        ReleaseContext current = fixture.Context.Context!;
        ReleaseContext normalized = current with
        {
            Changes = current.Changes.Append(new ReleaseContextChange(
                secondOid[..12], secondOid, null, "fix", "registration", "Second change", "Second change", false, false, null)).ToArray(),
            Evidence = current.Evidence with
            {
                Objects = current.Evidence.Objects.Append(new ReleaseContextObject(secondOid[..12], secondOid)).ToArray(),
            },
        };
        ReleaseContextValidationResult context = fixture.AsValidationResult(normalized);

        ReleasePreparationResult result = fixture.Prepare(context: context, rangeOids: [secondOid, new string('c', 40)]);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "chronological range");
        await Assert.That(await File.ReadAllTextAsync(fixture.NotesPath)).Contains(
            "## Complete Commit Range\n\n- `dddddddddddd`\n- `cccccccccccc`\n");
        await Assert.That(fixture.AsValidationResult(context.Context!).Json).IsEqualTo(context.Json);
    }

    [Test]
    public async Task PrepareAcceptsOrderedRangeWithBackportOriginalEvidenceWithoutDuplicateNoteEntry()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();
        string currentOid = new('c', 40);
        string originalOid = new('e', 40);
        ReleaseContext current = fixture.Context.Context!;
        ReleaseContext backport = current with
        {
            Changes = [current.Changes.Single() with { Backport = true, BackportOf = originalOid }],
            Evidence = current.Evidence with
            {
                Objects = current.Evidence.Objects.Append(new ReleaseContextObject(originalOid[..12], originalOid)).ToArray(),
            },
        };

        ReleasePreparationResult result = fixture.Prepare(
            context: fixture.AsValidationResult(backport),
            rangeOids: [currentOid]);

        string notes = Encoding.UTF8.GetString(result.Notes!);
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(notes).Contains("- `cccccccccccc`");
        await Assert.That(notes).DoesNotContain("eeeeeeeeeeee");
        await Assert.That(notes.Split("- `cccccccccccc`", StringSplitOptions.None).Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task PrepareRejectsVersionRangeFragmentSummaryAndPathDriftBeforeRendering()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();
        ReleasePreparationResult version = fixture.Prepare(context: fixture.Context with
        {
            Context = fixture.Context.Context! with
            {
                Release = fixture.Context.Context!.Release with { Version = "1.1.1" },
            },
        });
        ReleasePreparationResult range = fixture.Prepare(rangeOids: [new string('d', 40)]);
        ReleasePreparationResult fragment = fixture.Prepare(fixture.Input with
        {
            Fragments = [new PublicChangeFragment(
                "CHG-2026-0001", "Change", "feat", "registration", "Different summary", null, null, [],
                new Dictionary<string, FragmentImpact>(StringComparer.Ordinal), string.Empty)],
        });
        ReleasePreparationResult summary = fixture.Prepare(summary: Encoding.UTF8.GetBytes("maintainer@example.org\n"));

        string linked = Path.Combine(fixture.Root, "linked-release");
        Directory.CreateSymbolicLink(linked, fixture.ReleaseDirectory);
        ReleasePreparationResult path = fixture.Prepare(releaseDirectory: linked);

        await Assert.That(version.Diagnostic).IsEqualTo("prepare_context_release_mismatch");
        await Assert.That(range.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(fragment.Diagnostic).IsEqualTo("prepare_fragment_context_mismatch:CHG-2026-0001");
        await Assert.That(summary.Diagnostic).IsEqualTo("prepare_summary_restricted");
        await Assert.That(path.Diagnostic).IsEqualTo("prepare_release_path_invalid");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
    }

    [Test]
    public async Task PrepareCommandReadsReleaseInputsBuildsContextAndRunsTwiceIdempotently()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();

        (int firstCode, string firstOutput, byte[] firstNotes) = fixture.RunCommand();
        (int secondCode, string secondOutput, byte[] secondNotes) = fixture.RunCommand();

        await Assert.That(firstCode).IsEqualTo(Program.Success).Because(firstOutput);
        await Assert.That(secondCode).IsEqualTo(Program.Success).Because(secondOutput);
        await Assert.That(secondOutput).IsEqualTo(firstOutput);
        await Assert.That(secondNotes).IsEquivalentTo(firstNotes);
        await Assert.That(File.Exists(fixture.CommandContextPath)).IsTrue();
        await Assert.That(File.ReadAllText(fixture.CommandReleasePath)).Contains("Version: 1.1.0");
        await Assert.That(File.ReadAllText(fixture.CommandSummaryPath)).IsEqualTo("Attendees can now correct registration details.\n");
        await Assert.That(File.ReadAllText(fixture.CommandContextPath)).Contains("\"schemaVersion\": 1");
        await Assert.That(File.ReadAllText(fixture.CommandContextPath)).Contains("Attendees can now correct registration details.");
        await Assert.That(firstOutput).IsEqualTo(
            "docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
    }

    [Test]
    public async Task PrepareCommandRejectsBadReleaseInputBeforeWritingGeneratedArtifacts()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();

        (int exitCode, string output) = fixture.RunBadCommand();

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("prepare_failed: prepare_summary_restricted\n");
        await Assert.That(File.Exists(fixture.BadNotesPath)).IsFalse();
    }

    [Test]
    public async Task PrepareCommandVerifiesSignedAnnotatedBaselineTagDirectlyAndRejectsUnsignedOrStableReachable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var accepted = new PreparationFixture();
        using var unsigned = new PreparationFixture();
        using var rejected = new PreparationFixture();

        (int acceptedCode, string acceptedOutput, byte[] acceptedNotes) = accepted.RunBaselineCommand("0.1.0", signBaseline: true, includeReachableStableTag: false);
        (int unsignedCode, string unsignedOutput, byte[] unsignedNotes) = unsigned.RunBaselineCommand("0.1.0", signBaseline: false, includeReachableStableTag: false);
        (int rejectedCode, string rejectedOutput, byte[] rejectedNotes) = rejected.RunBaselineCommand("0.2.0", signBaseline: true, includeReachableStableTag: true);

        await Assert.That(acceptedCode).IsEqualTo(Program.Success).Because(acceptedOutput);
        await Assert.That(acceptedOutput).IsEqualTo("docs(release): prepare 0.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
        await Assert.That(acceptedNotes).IsNotEmpty();
        await Assert.That(File.Exists(accepted.CommandContextPath)).IsTrue();
        await Assert.That(File.ReadAllText(accepted.CommandContextPath)).Contains("\"version\": \"0.1.0\"");

        await Assert.That(unsignedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unsignedOutput).Contains("tag_not_signed");
        await Assert.That(unsignedNotes).IsEmpty();

        await Assert.That(rejectedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(rejectedOutput).IsEqualTo("prepare_failed: git_baseline_stable_tag_exists:v0.1.0\n");
        await Assert.That(rejectedNotes).IsEmpty();
        await Assert.That(File.Exists(rejected.CommandContextPath)).IsFalse();
    }

    [Test]
    public async Task PrepareCommandRejectsGitCliffBumpDisagreementAndExecutorFailureWhileKeepingCSharpAuthority()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var disagreementFixture = new PreparationFixture();
        var disagreeingExecutor = new InMemoryGitCliffExecutor
        {
            OnSuggestBumpedVersion = _ => new GitCliffBumpVersionResult(true, "1.0.1", "v1.0.1", null),
        };
        (int disagreeCode, string disagreeOutput, byte[] disagreeNotes) = disagreementFixture.RunCommandWithExecutor(disagreeingExecutor);

        await Assert.That(disagreeCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(disagreeOutput).IsEqualTo("prepare_failed: context_git_cliff_bump_disagreement\n");
        await Assert.That(disagreeNotes).IsEmpty();
        await Assert.That(disagreeingExecutor.BumpRequests).Count().IsEqualTo(1);
        await Assert.That(disagreeingExecutor.BumpRequests[0].CommitRange).IsEqualTo("v1.0.0..HEAD");
        await Assert.That(File.Exists(disagreementFixture.CommandContextPath)).IsFalse();

        using var failureFixture = new PreparationFixture();
        var failingExecutor = new InMemoryGitCliffExecutor
        {
            OnSuggestBumpedVersion = _ => new GitCliffBumpVersionResult(false, null, null, "git_cliff_tool_untrusted"),
        };
        (int failCode, string failOutput, byte[] failNotes) = failureFixture.RunCommandWithExecutor(failingExecutor);

        await Assert.That(failCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(failOutput).IsEqualTo("prepare_failed: git_cliff_tool_untrusted\n");
        await Assert.That(failNotes).IsEmpty();
        await Assert.That(File.Exists(failureFixture.CommandContextPath)).IsFalse();
    }

    [Test]
    public async Task PrepareCommandRejectsMissingReleaseDirectoryOperand()
    {
        using var output = new StringWriter();

        int exitCode = PrepareCommand.Run(["prepare"], output, Path.GetTempPath(), "linux-x64", TimeSpan.FromSeconds(2));

        await Assert.That(exitCode).IsEqualTo(Program.UsageError);
        await Assert.That(output.ToString()).IsEqualTo(
            "invalid_arguments: prepare requires release directory" + Environment.NewLine);
    }

    [Test]
    public async Task PrepareExecutableRunsPositiveAndNegativeTwiceWithoutPromotionCeremony()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using (var positive = new PreparationFixture())
        {
            (int firstCode, string firstOutput, byte[] firstNotes) = positive.RunSpawnedCommand("Attendees can now correct registration details.\n");
            (int secondCode, string secondOutput, byte[] secondNotes) = positive.RunSpawnedCommand("Attendees can now correct registration details.\n");
            await Assert.That(firstCode).IsEqualTo(Program.Success).Because(firstOutput);
            await Assert.That(secondCode).IsEqualTo(Program.Success).Because(secondOutput);
            await Assert.That(secondOutput).IsEqualTo(firstOutput);
            await Assert.That(secondNotes).IsEquivalentTo(firstNotes);
            await Assert.That(firstOutput).IsEqualTo(
                "docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
            await Assert.That(File.Exists(positive.CommandContextPath)).IsTrue();
            await Assert.That(File.ReadAllText(positive.CommandContextPath)).Contains("\"schemaVersion\": 1");
        }

        using (var negative = new PreparationFixture())
        {
            (int firstCode, string firstOutput, byte[] firstNotes) = negative.RunSpawnedCommand("# generated-region\n");
            (int secondCode, string secondOutput, byte[] secondNotes) = negative.RunSpawnedCommand("# generated-region\n");
            await Assert.That(firstCode).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(secondCode).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(firstOutput).IsEqualTo("prepare_failed: prepare_summary_restricted\n");
            await Assert.That(secondOutput).IsEqualTo(firstOutput);
            await Assert.That(firstNotes).IsEmpty();
            await Assert.That(secondNotes).IsEmpty();
            await Assert.That(File.Exists(negative.BadContextPath)).IsFalse();
        }
    }

    private sealed class PreparationFixture : IDisposable
    {
        private readonly string bundleRoot;
        private readonly string isolationRoot;
        private readonly string lockPath;
        private readonly string configPath;
        private readonly string executablePath;
        private readonly string candidateRoot;
        private readonly string signerKeyPath;
        private readonly string allowedSignersPath;
        private int commitSequence;

        public PreparationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-prepare-{Guid.NewGuid():N}");
            ReleaseDirectory = Path.Combine(Root, "docs", "internal", "releases", "1.1.0");
            bundleRoot = Path.Combine(Root, "bundle");
            isolationRoot = Path.Combine(Root, "isolation");
            lockPath = Path.Combine(bundleRoot, "toolchain.lock.json");
            configPath = Path.Combine(bundleRoot, "cliff.toml");
            executablePath = Path.Combine(bundleRoot, "git-cliff");
            candidateRoot = Path.Combine(Root, "candidate");
            signerKeyPath = Path.Combine(Root, "release-signer-key");
            allowedSignersPath = Path.Combine(Root, "allowed-signers");

            Directory.CreateDirectory(ReleaseDirectory);
            Directory.CreateDirectory(bundleRoot);
            Directory.CreateDirectory(isolationRoot);
            Directory.CreateDirectory(candidateRoot);

            ReleasePath = Path.Combine(ReleaseDirectory, "release.yaml");
            SummaryPath = Path.Combine(ReleaseDirectory, "summary.md");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            File.WriteAllText(ReleasePath, "Version: 1.1.0\n");
            File.WriteAllText(SummaryPath, "Attendees can now correct registration details.\n");
            File.WriteAllText(configPath,
                "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n");
            Input = ValidInput();
            Context = ValidContext();
            CreateSigningKey();
            WriteRenderer();
        }

        public string Root { get; }
        public string ReleaseDirectory { get; }
        public string ReleasePath { get; }
        public string SummaryPath { get; }
        public string NotesPath { get; }
        public string CommandReleasePath { get; private set; } = string.Empty;
        public string CommandSummaryPath { get; private set; } = string.Empty;
        public string CommandContextPath { get; private set; } = string.Empty;
        public string BadNotesPath { get; private set; } = string.Empty;
        public string BadContextPath { get; private set; } = string.Empty;
        public ReleaseInputValidationResult Input { get; }
        public ReleaseContextValidationResult Context { get; }
        public bool RendererFails { get; set; }

        public ReleasePreparationResult Prepare(
            ReleaseInputValidationResult? input = null,
            ReleaseContextValidationResult? context = null,
            byte[]? summary = null,
            IReadOnlyList<string>? rangeOids = null,
            string? releaseDirectory = null)
        {
            WriteRenderer();
            ReleaseContextValidationResult effectiveContext = context ?? Context;
            return ReleasePreparation.Prepare(new ReleasePreparationRequest(
                releaseDirectory ?? ReleaseDirectory,
                input ?? Input,
                effectiveContext,
                summary ?? File.ReadAllBytes(SummaryPath),
                rangeOids ?? [new string('c', 40)],
                new GitCliffRenderRequest(
                    lockPath,
                    bundleRoot,
                    configPath,
                    Encoding.UTF8.GetBytes(effectiveContext.Json!),
                    "linux-x64",
                    isolationRoot,
                    TimeSpan.FromSeconds(2))));
        }

        public (int ExitCode, string Output, byte[] Notes) RunCommandWithExecutor(IGitCliffExecutor executor)
        {
            string cliReleaseDirectory = EnsureCommandRepository("Attendees can now correct registration details.\n");
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", "docs/internal/releases/1.1.0"],
                    output,
                    candidateRoot,
                    "linux-x64",
                    TimeSpan.FromSeconds(2),
                    executor);
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            });
        }

        public (int ExitCode, string Output, byte[] Notes) RunCommand()
        {
            string cliReleaseDirectory = EnsureCommandRepository("Attendees can now correct registration details.\n");
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", "docs/internal/releases/1.1.0"],
                    output,
                    candidateRoot,
                    "linux-x64",
                    TimeSpan.FromSeconds(2));
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            });
        }

        public (int ExitCode, string Output, byte[] Notes) RunBaselineCommand(string version, bool signBaseline, bool includeReachableStableTag)
        {
            string cliReleaseDirectory = EnsureBaselineCommandRepository(version, signBaseline, includeReachableStableTag);
            WriteRenderer(version);
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", $"docs/internal/releases/{version}"],
                    output,
                    candidateRoot,
                    "linux-x64",
                    TimeSpan.FromSeconds(2));
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            });
        }

        public (int ExitCode, string Output) RunBadCommand()
        {
            string cliReleaseDirectory = EnsureCommandRepository("# generated-region\n");
            BadNotesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
            BadContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(["prepare", "docs/internal/releases/1.1.0"], output, candidateRoot, "linux-x64", TimeSpan.FromSeconds(2));
                return (exitCode, output.ToString());
            });
        }

        public (int ExitCode, string Output, byte[] Notes) RunSpawnedCommand(string summary)
        {
            string cliReleaseDirectory = EnsureCommandRepository(summary);
            BadNotesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
            BadContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            SyncRepoToolFiles();

            string assemblyPath = typeof(Program).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = candidateRoot,
            };
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("prepare");
            startInfo.ArgumentList.Add("docs/internal/releases/1.1.0");
            startInfo.Environment["ISLAMU_RELEASE_TOOL_BUNDLE"] = bundleRoot;
            startInfo.Environment["ISLAMU_RELEASE_ALLOWED_SIGNERS"] = allowedSignersPath;

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("spawned_prepare_timeout");
            }

            if (error.Length != 0)
            {
                throw new InvalidOperationException($"spawned_prepare_stderr: {error}");
            }

            string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
            return (process.ExitCode, output, File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
        }

        public ReleaseContextValidationResult WithEvidenceObject(string oid)
        {
            ReleaseContext current = Context.Context!;
            ReleaseContext updated = current with
            {
                Evidence = current.Evidence with
                {
                    Objects = current.Evidence.Objects.Append(new ReleaseContextObject(oid[..12], oid)).ToArray(),
                },
            };
            return AsValidationResult(updated);
        }

        public ReleaseContextValidationResult AsValidationResult(ReleaseContext context) =>
            new(true, context, JsonSerializer.Serialize(context, ContextJsonOptions) + "\n", []);

        private T WithToolEnvironment<T>(Func<T> action)
        {
            string? oldBundle = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_TOOL_BUNDLE");
            string? oldSigners = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_ALLOWED_SIGNERS");
            try
            {
                Environment.SetEnvironmentVariable("ISLAMU_RELEASE_TOOL_BUNDLE", bundleRoot);
                Environment.SetEnvironmentVariable("ISLAMU_RELEASE_ALLOWED_SIGNERS", allowedSignersPath);
                return action();
            }
            finally
            {
                Environment.SetEnvironmentVariable("ISLAMU_RELEASE_TOOL_BUNDLE", oldBundle);
                Environment.SetEnvironmentVariable("ISLAMU_RELEASE_ALLOWED_SIGNERS", oldSigners);
            }
        }

        private void SyncRepoToolFiles()
        {
            string engRelease = Path.Combine(candidateRoot, "eng", "release");
            Directory.CreateDirectory(engRelease);
            File.Copy(lockPath, Path.Combine(engRelease, "toolchain.lock.json"), overwrite: true);
            File.Copy(configPath, Path.Combine(engRelease, "cliff.toml"), overwrite: true);
        }

        private void WriteRenderer(string version = "1.1.0")
        {
            string body = RendererFails
                ? "exit 7"
                : $"if [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.14.2\\n'; exit 0; fi\nfor arg in \"$@\"; do if [ \"$arg\" = \"--bumped-version\" ]; then printf 'v{version}\\n'; exit 0; fi; done\nprintf '# Release {version}\\n\\n- registration: let attendees correct registration details (cccccccccccc)\\n'";
            File.WriteAllText(executablePath, "#!/bin/sh\n" + body + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            File.WriteAllText(lockPath, $$"""
                {
                  "schemaVersion": 1,
                  "tool": "git-cliff",
                  "version": "2.14.2",
                  "platforms": [
                    {
                      "platform": "linux-x64",
                      "executable": "git-cliff",
                      "executableSha256": "{{Digest(executablePath)}}",
                      "archive": "git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz",
                      "archiveSha256": "{{new string('1', 64)}}",
                      "source": "https://github.com/orhun/git-cliff/releases/download/v2.14.2/git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz"
                    }
                  ]
                }
                """);
        }

        private void CreateSigningKey()
        {
            RunExternal(ReleaseToolPaths.SshKeygen, Root, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "prepare-fixture", "-f", signerKeyPath);
            string pub = string.Join(' ', File.ReadAllText(signerKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedSignersPath, $"release-operator namespaces=\"git\" {pub}\n");
        }

        private string EnsureCommandRepository(string summary)
        {
            if (!Directory.Exists(Path.Combine(candidateRoot, ".git")))
            {
                WritePolicyFiles();
                Git("init", "--initial-branch=main");
                string previous = Commit("fix(events): preserve published event notes");
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v1.0.0", previous, "-m", "v1.0.0");
                Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
            }

            string cliReleaseDirectory = Path.Combine(candidateRoot, "docs", "internal", "releases", "1.1.0");
            Directory.CreateDirectory(cliReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes"));
            string previousOid = Git("rev-list", "--max-parents=0", "HEAD").Trim();
            CommandReleasePath = Path.Combine(cliReleaseDirectory, "release.yaml");
            CommandSummaryPath = Path.Combine(cliReleaseDirectory, "summary.md");
            CommandContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            File.WriteAllText(CommandReleasePath,
                $"Version: 1.1.0\nLine: v1.1\nRelease-Date: 2026-08-14\nBase-Stable-Tag: v1.0.0\nPrevious-Published-Tag: v1.0.0\nRelease-Range:\n  Base-Ref: v1.0.0\n  Base-Oid: {previousOid}\n  Previous-Ref: v1.0.0\n  Previous-Oid: {previousOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(CommandSummaryPath, summary);
            File.WriteAllText(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"), SampleFragmentYaml);
            return cliReleaseDirectory;
        }

        private string EnsureBaselineCommandRepository(string version, bool signBaseline, bool includeReachableStableTag)
        {
            string baselineRef = "changelog-baseline-2026-08-15";
            string line = "v" + string.Join('.', version.Split('.')[0], version.Split('.')[1]);
            WritePolicyFiles();
            Git("init", "--initial-branch=main");
            string baselineOid = Commit("baseline lower bound");
            if (signBaseline)
            {
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid",
                    "-c", "gpg.format=ssh", "-c", $"user.signingKey={signerKeyPath}",
                    "tag", "-s", baselineRef, baselineOid, "-m", baselineRef);
            }
            else
            {
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid",
                    "tag", "-a", baselineRef, baselineOid, "-m", baselineRef);
            }

            if (includeReachableStableTag)
            {
                string stable = Commit("existing governed stable");
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v0.1.0", stable, "-m", "v0.1.0");
            }

            Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");

            string cliReleaseDirectory = Path.Combine(candidateRoot, "docs", "internal", "releases", version);
            Directory.CreateDirectory(cliReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes"));
            CommandReleasePath = Path.Combine(cliReleaseDirectory, "release.yaml");
            CommandSummaryPath = Path.Combine(cliReleaseDirectory, "summary.md");
            CommandContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            File.WriteAllText(CommandReleasePath,
                $"Version: {version}\nLine: {line}\nRelease-Date: 2026-08-14\nBase-Stable-Tag: {baselineRef}\nPrevious-Published-Tag: {baselineRef}\nRelease-Range:\n  Base-Ref: {baselineRef}\n  Base-Oid: {baselineOid}\n  Previous-Ref: {baselineRef}\n  Previous-Oid: {baselineOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(CommandSummaryPath, "Attendees can now correct registration details.\n");
            File.WriteAllText(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"), SampleFragmentYaml);
            return cliReleaseDirectory;
        }

        private void WritePolicyFiles()
        {
            Directory.CreateDirectory(Path.Combine(candidateRoot, "eng", "release", "policy"));
            File.WriteAllText(Path.Combine(candidateRoot, "eng", "release", "policy", "release-policy.yaml"),
                "schemaVersion: 1\nmaximumCommitMessageBytes: 8192\nreleaseVisibleTypes:\n  - feat\n  - fix\n  - perf\n  - revert\n  - docs\ninternalTypes:\n  - test\n  - refactor\n  - style\n  - build\n  - ci\n  - chore\nrequiredBreakingSignals:\n  bang: true\n  footer: BREAKING CHANGE\nskipTrailer: Changelog\nskipValue: skip\nskipReasonTrailer: Changelog-Reason\n");
            File.WriteAllText(Path.Combine(candidateRoot, "eng", "release", "policy", "scope-registry.yaml"),
                "schemaVersion: 1\npublicScopes:\n  - events\n  - registration\nengineeringScopes:\n  - release\n");
        }

        private string Commit(string message)
        {
            commitSequence++;
            string file = Path.Combine(candidateRoot, $"file-{commitSequence}.txt");
            File.WriteAllText(file, $"content {commitSequence}\n");
            Git("add", file);
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-q", "-m", message);
            return Git("rev-parse", "HEAD^{commit}").Trim();
        }

        private string Git(params string[] arguments) => RunExternal(ReleaseToolPaths.Git, candidateRoot, arguments);

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

        private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        private const string SampleFragmentYaml =
            "Change-Id: CHG-2026-0001\nTitle: Registration worker restart\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n";

        private static ReleaseInputValidationResult ValidInput()
        {
            var impacts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["breaking"] = "not-applicable",
                ["security"] = "not-applicable",
                ["migration"] = "not-applicable",
                ["configuration"] = "not-applicable",
                ["openapi"] = "not-applicable",
                ["operator"] = "documented",
            };
            var descriptor = new ReleaseDescriptor(
                "1.1.0", "v1.1", new DateOnly(2026, 8, 14), "v1.0.0", "v1.0.0",
                new ReleaseRangeReference("refs/tags/v1.0.0", new string('a', 40), "refs/tags/v1.0.0", new string('b', 40)),
                ["v1"], impacts);
            var fragmentImpacts = new Dictionary<string, FragmentImpact>(StringComparer.Ordinal)
            {
                ["breaking"] = new("docs/internal/releases/README.md", "not-applicable", null, null),
                ["security"] = new("docs/SECURITY_OVERVIEW.md", "not-applicable", null, null),
                ["migration"] = new("docs/RELEASE_RUNBOOK.md", "not-applicable", null, null),
                ["configuration"] = new("docs/CONFIGURATION.md", "not-applicable", null, null),
                ["openapi"] = new("docs/API_CHANGELOG.md", "not-applicable", null, null),
                ["operator"] = new("docs/RELEASE_RUNBOOK.md", "documented", null, "Restart registration workers after deployment."),
            };
            var fragment = new PublicChangeFragment(
                "CHG-2026-0001", "Registration worker restart", "feat", "registration",
                "Attendees can now correct registration details.", null, null, [], fragmentImpacts, string.Empty);
            return new ReleaseInputValidationResult(true, descriptor, [fragment], []);
        }

        private static ReleaseContextValidationResult ValidContext()
        {
            var context = new ReleaseContext(
                1,
                new ReleaseContextRelease("1.1.0", "v1.1", "2026-08-14", "v1.0.0", "v1.0.0", "minor", "stable", true),
                [
                    new ReleaseContextChange(
                        new string('c', 12), new string('c', 40), "CHG-2026-0001", "feat", "registration",
                        "Registration worker restart", "Attendees can now correct registration details.", false, false, null),
                ],
                new ReleaseContextEvidence(
                    new string('a', 40),
                    new string('b', 40),
                    [
                        new ReleaseContextObject(new string('a', 12), new string('a', 40)),
                        new ReleaseContextObject(new string('b', 12), new string('b', 40)),
                        new ReleaseContextObject(new string('c', 12), new string('c', 40)),
                    ]));
            return new ReleaseContextValidationResult(true, context, JsonSerializer.Serialize(context, ContextJsonOptions) + "\n", []);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
