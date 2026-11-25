using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("ReleaseToolEnvironment")]
public sealed class ReleasePreparationTests
{
    private const string SampleSummaryLine = "Attendees can now correct registration details.\n";
    private const string SampleReleaseRelativePath = "docs/internal/releases/1.1.0";
    private const string ReleaseFolderSegment = "release";

    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    [Test]
    public async Task PrepareWritesAllowlistedRenderingContextAndNormalizedNotesByteIdempotently()
    {
        using var fixture = new PreparationFixture();

        ReleasePreparationResult first = fixture.Prepare();
        byte[] firstNotes = await File.ReadAllBytesAsync(fixture.NotesPath);
        byte[] firstRenderContext = await File.ReadAllBytesAsync(fixture.RenderingContextPath);
        ReleasePreparationResult second = fixture.Prepare();

        await Assert.That(first.IsValid).IsTrue().Because(first.Diagnostic ?? "first prepare");
        await Assert.That(second.IsValid).IsTrue().Because(second.Diagnostic ?? "second prepare");
        await Assert.That(await File.ReadAllBytesAsync(fixture.NotesPath)).IsEquivalentTo(firstNotes);
        await Assert.That(await File.ReadAllBytesAsync(fixture.RenderingContextPath)).IsEquivalentTo(firstRenderContext);
        await Assert.That(Encoding.UTF8.GetString(firstNotes)).Contains("# Release 1.1.0");
        await Assert.That(Encoding.UTF8.GetString(firstNotes)).Contains("## Community Admin");
        await Assert.That(Encoding.UTF8.GetString(firstNotes)).Contains("## Self-Hoster / Operator");
        await Assert.That(Encoding.UTF8.GetString(firstNotes)).Contains("## Developer / API Integrator");
        await Assert.That(first.CommitMessage).IsEqualTo(
            "docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
        await Assert.That(Directory.EnumerateFiles(fixture.ReleaseDirectory, "*.tmp", SearchOption.TopDirectoryOnly)).IsEmpty();
    }

    [Test]
    public async Task PrepareFailsBeforeWriteForImpactDriftRendererFailurePostRenderLeakAndUnexpectedOutput()
    {
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
        await Assert.That(File.Exists(fixture.RenderingContextPath)).IsFalse();

        fixture.RendererFails = false;
        fixture.RenderedMarkdownOverride = "# Release 1.1.0\n\nLeaked contact: leak@example.org\n";
        ReleasePreparationResult leaked = fixture.Prepare();
        await Assert.That(leaked.IsValid).IsFalse();
        await Assert.That(leaked.Diagnostic).IsEqualTo("rendered_notes_identity_or_provider");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
        await Assert.That(File.Exists(fixture.RenderingContextPath)).IsFalse();

        fixture.RenderedMarkdownOverride = null;
        await File.WriteAllTextAsync(fixture.NotesPath, "unexpected\n");
        ReleasePreparationResult collision = fixture.Prepare();
        await Assert.That(collision.Diagnostic).IsEqualTo("prepare_generated_file_unexpected");
        await Assert.That(await File.ReadAllTextAsync(fixture.NotesPath)).IsEqualTo("unexpected\n");
    }

    [Test]
    public async Task PrepareRejectsMissingBlankOrRestrictedApplicableImpactDetailBeforeRendering()
    {
        using var fixture = new PreparationFixture { RendererFails = true };
        PublicChangeFragment fragment = fixture.Input.Fragments.Single();
        FragmentImpact impact = fragment.Impacts["operator"];

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
        ReleaseContextValidationResult context = PreparationFixture.AsValidationResult(ordered);

        ReleasePreparationResult result = fixture.Prepare(context: context, rangeOids: [new string('c', 40), secondOid]);

        await Assert.That(result.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
    }

    [Test]
    public async Task PrepareRejectsVersionRangeFragmentSummaryAndPathDriftBeforeRendering()
    {
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
    public async Task PrepareCommandReadsReleaseInputsRunsRealGitCliffTwiceIdempotentlyAndOmitsImageClaimsWithoutManifest()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();

        (int firstCode, string firstOutput, byte[] firstNotes) = fixture.RunCommand();
        byte[] firstContext = await File.ReadAllBytesAsync(fixture.CommandContextPath);
        byte[] firstRenderContext = await File.ReadAllBytesAsync(fixture.CommandRenderingContextPath);
        byte[] firstContractDiff = await File.ReadAllBytesAsync(fixture.CommandContractDiffPath);
        (int secondCode, string secondOutput, byte[] secondNotes) = fixture.RunCommand();

        await Assert.That(firstCode).IsEqualTo(Program.Success).Because(firstOutput);
        await Assert.That(secondCode).IsEqualTo(Program.Success).Because(secondOutput);
        await Assert.That(secondOutput).IsEqualTo(firstOutput);
        await Assert.That(secondNotes).IsEquivalentTo(firstNotes);
        await Assert.That(await File.ReadAllBytesAsync(fixture.CommandContextPath)).IsEquivalentTo(firstContext);
        await Assert.That(await File.ReadAllBytesAsync(fixture.CommandRenderingContextPath)).IsEquivalentTo(firstRenderContext);
        await Assert.That(await File.ReadAllBytesAsync(fixture.CommandContractDiffPath)).IsEquivalentTo(firstContractDiff);
        string notesText = Encoding.UTF8.GetString(firstNotes);
        await Assert.That(notesText).Contains("# Release 1.1.0");
        await Assert.That(notesText).Contains("## Community Admin");
        await Assert.That(notesText).Contains("## Self-Hoster / Operator");
        await Assert.That(notesText).Contains("## Developer / API Integrator");
        await Assert.That(notesText).DoesNotContain("Verified Container Images");
        await Assert.That(notesText).DoesNotContain("@sha256:");
        await Assert.That(firstOutput).IsEqualTo(
            "docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
    }

    [Test]
    public async Task PrepareCommandRejectsMalformedImageManifestAndBadSummaryBeforeWriting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();
        (int badSummaryCode, string badSummaryOutput) = fixture.RunBadCommand();
        await Assert.That(badSummaryCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(badSummaryOutput).IsEqualTo("prepare_failed: prepare_summary_restricted\n");
        await Assert.That(File.Exists(fixture.BadNotesPath)).IsFalse();

        using var manifestFixture = new PreparationFixture();
        string badManifest = Path.Combine(manifestFixture.Root, "bad-images.json");
        await File.WriteAllTextAsync(badManifest, "{ \"schemaVersion\": 1, \"version\": \"9.9.9\", \"sourceCommitOid\": \"" + new string('a', 40) + "\", \"images\": [] }");
        (int manifestCode, string manifestOutput, byte[] manifestNotes) = manifestFixture.RunCommand(imageManifestPath: badManifest);

        await Assert.That(manifestCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(manifestOutput).Contains("image_manifest_version_mismatch");
        await Assert.That(manifestNotes).IsEmpty();
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
        await Assert.That(File.Exists(accepted.CommandRenderingContextPath)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(accepted.CommandContextPath)).Contains("\"version\": \"0.1.0\"");

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
            "invalid_arguments: prepare requires <release-directory> [--image-manifest <path>]" + Environment.NewLine);
    }

    private sealed class PreparationFixture : IDisposable
    {
        private readonly string candidateRoot;
        private readonly string signerKeyPath;
        private readonly string allowedSignersPath;
        private int commitSequence;

        public PreparationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-prepare-{Guid.NewGuid():N}");
            ReleaseDirectory = Path.Combine(Root, "docs", "internal", "releases", "1.1.0");
            candidateRoot = Path.Combine(Root, "candidate");
            signerKeyPath = Path.Combine(Root, "release-signer-key");
            allowedSignersPath = Path.Combine(Root, "allowed-signers");

            Directory.CreateDirectory(ReleaseDirectory);
            Directory.CreateDirectory(candidateRoot);

            ReleasePath = Path.Combine(ReleaseDirectory, "release.yaml");
            SummaryPath = Path.Combine(ReleaseDirectory, "summary.md");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            RenderingContextPath = Path.Combine(ReleaseDirectory, "release-rendering-context.v1.json");
            File.WriteAllText(ReleasePath, "Version: 1.1.0\n");
            File.WriteAllText(SummaryPath, SampleSummaryLine);

            Input = ValidInput();
            Context = ValidContext();
            CreateSigningKey();
        }

        public string Root { get; }
        public string ReleaseDirectory { get; }
        public string ReleasePath { get; }
        public string SummaryPath { get; }
        public string NotesPath { get; }
        public string RenderingContextPath { get; }
        public string CommandContextPath { get; private set; } = string.Empty;
        public string CommandRenderingContextPath { get; private set; } = string.Empty;
        public string CommandContractDiffPath { get; private set; } = string.Empty;
        public string BadNotesPath { get; private set; } = string.Empty;
        public ReleaseInputValidationResult Input { get; }
        public ReleaseContextValidationResult Context { get; }
        public bool RendererFails { get; set; }
        public string? RenderedMarkdownOverride { get; set; }

        public ReleasePreparationResult Prepare(
            ReleaseInputValidationResult? input = null,
            ReleaseContextValidationResult? context = null,
            byte[]? summary = null,
            IReadOnlyList<string>? rangeOids = null,
            string? releaseDirectory = null)
        {
            ReleaseContextValidationResult effectiveContext = context ?? Context;
            IReadOnlyList<string> effectiveRange = rangeOids ?? [new string('c', 40)];
            string version = (input ?? Input).Descriptor?.Version ?? "1.1.0";
            string nativeJson = $$"""
                [
                  {
                    "version": "v{{version}}",
                    "commit_id": "{{(effectiveRange.Count > 0 ? effectiveRange[^1] : new string('c', 40))}}",
                    "commits": [
                      {
                        "id": "{{new string('c', 40)}}",
                        "group": "Features",
                        "scope": "registration",
                        "message": "let attendees correct registration details",
                        "breaking": false,
                        "conventional": true,
                        "merge_commit": false
                      }
                    ]
                  }
                ]
                """;
            var executor = new InMemoryGitCliffExecutor
            {
                OnExportContext = _ => RendererFails
                    ? new GitCliffContextExportResult(false, null, "prepare_renderer_failed")
                    : new GitCliffContextExportResult(true, nativeJson, null),
                OnRenderFromContext = _ => RendererFails
                    ? new GitCliffRenderFromContextResult(false, null, "prepare_renderer_failed")
                    : new GitCliffRenderFromContextResult(
                        true,
                        RenderedMarkdownOverride ?? $"# Release {version}\n\n## Maintainer Summary\n\nAttendees can now correct registration details.\n\n## Community Admin\n\n- **registration**: Registration worker restart (`cccccccccccc`)\n\n## Self-Hoster / Operator\n\n- Restart registration workers.\n\n## Developer / API Integrator\n\nNo API changes.\n\n## Complete Commit Range\n\n- `cccccccccccc`\n",
                        null),
            };
            var tool = new GitCliffToolConfiguration("lock.json", Root, "cliff.toml", "linux-x64", TimeSpan.FromSeconds(2));

            return ReleasePreparation.Prepare(new ReleasePreparationRequest(
                releaseDirectory ?? ReleaseDirectory,
                Root,
                input ?? Input,
                effectiveContext,
                summary ?? File.ReadAllBytes(SummaryPath),
                effectiveRange,
                [],
                tool,
                executor,
                "v1.0.0..HEAD"));
        }

        public (int ExitCode, string Output, byte[] Notes) RunCommandWithExecutor(IGitCliffExecutor executor)
        {
            string cliReleaseDirectory = EnsureCommandRepository(SampleSummaryLine);
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", SampleReleaseRelativePath],
                    output,
                    candidateRoot,
                    GitCliffToolchainStore.GetCurrentPlatform(),
                    TimeSpan.FromSeconds(10),
                    executor);
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            });
        }

        public (int ExitCode, string Output, byte[] Notes) RunCommand(string? imageManifestPath = null)
        {
            string cliReleaseDirectory = EnsureCommandRepository(SampleSummaryLine);
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                string[] args = imageManifestPath is null
                    ? ["prepare", SampleReleaseRelativePath]
                    : ["prepare", SampleReleaseRelativePath, "--image-manifest", imageManifestPath];
                int exitCode = PrepareCommand.Run(
                    args,
                    output,
                    candidateRoot,
                    GitCliffToolchainStore.GetCurrentPlatform(),
                    TimeSpan.FromSeconds(10));
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            });
        }

        public (int ExitCode, string Output, byte[] Notes) RunBaselineCommand(string version, bool signBaseline, bool includeReachableStableTag)
        {
            string cliReleaseDirectory = EnsureBaselineCommandRepository(version, signBaseline, includeReachableStableTag);
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", $"docs/internal/releases/{version}"],
                    output,
                    candidateRoot,
                    GitCliffToolchainStore.GetCurrentPlatform(),
                    TimeSpan.FromSeconds(10));
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            });
        }

        public (int ExitCode, string Output) RunBadCommand()
        {
            string cliReleaseDirectory = EnsureCommandRepository("# generated-region\n");
            BadNotesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
            SyncRepoToolFiles();

            return WithToolEnvironment(() =>
            {
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", SampleReleaseRelativePath],
                    output,
                    candidateRoot,
                    GitCliffToolchainStore.GetCurrentPlatform(),
                    TimeSpan.FromSeconds(10));
                return (exitCode, output.ToString());
            });
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

        public static ReleaseContextValidationResult AsValidationResult(ReleaseContext context) =>
            new(true, context, JsonSerializer.Serialize(context, ContextJsonOptions) + "\n", []);

        private T WithToolEnvironment<T>(Func<T> action)
        {
            string? oldSigners = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_ALLOWED_SIGNERS");
            try
            {
                Environment.SetEnvironmentVariable("ISLAMU_RELEASE_ALLOWED_SIGNERS", allowedSignersPath);
                return action();
            }
            finally
            {
                Environment.SetEnvironmentVariable("ISLAMU_RELEASE_ALLOWED_SIGNERS", oldSigners);
            }
        }

        private void SyncRepoToolFiles()
        {
            string sourceRoot = RepositoryRoot.Find();
            string engRelease = Path.Combine(candidateRoot, "eng", ReleaseFolderSegment);
            Directory.CreateDirectory(engRelease);
            File.Copy(Path.Combine(sourceRoot, "eng", ReleaseFolderSegment, "toolchain.lock.json"), Path.Combine(engRelease, "toolchain.lock.json"), overwrite: true);
            File.Copy(Path.Combine(sourceRoot, "eng", ReleaseFolderSegment, "cliff.toml"), Path.Combine(engRelease, "cliff.toml"), overwrite: true);
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
            CommandContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            CommandRenderingContextPath = Path.Combine(cliReleaseDirectory, "release-rendering-context.v1.json");
            CommandContractDiffPath = Path.Combine(cliReleaseDirectory, "contract-diff.v1.json");
            File.WriteAllText(Path.Combine(cliReleaseDirectory, "release.yaml"),
                $"Version: 1.1.0\nLine: v1.1\nRelease-Date: 2026-08-14\nBase-Stable-Tag: v1.0.0\nPrevious-Published-Tag: v1.0.0\nRelease-Range:\n  Base-Ref: v1.0.0\n  Base-Oid: {previousOid}\n  Previous-Ref: v1.0.0\n  Previous-Oid: {previousOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(Path.Combine(cliReleaseDirectory, "summary.md"), summary);
            File.WriteAllText(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"), SampleFragmentYaml);
            return cliReleaseDirectory;
        }

        private string EnsureBaselineCommandRepository(string version, bool signBaseline, bool includeReachableStableTag)
        {
            string baselineRef = "changelog-baseline-2026-08-15";
            string line = "v" + string.Join('.', version.Split('.')[0], version.Split('.')[1]);
            WritePolicyFiles();
            Git("init", "--initial-branch=main");
            string baselineOid = Commit("chore(release): establish initial baseline");
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
                string stable = Commit("feat(events): existing governed stable");
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v0.1.0", stable, "-m", "v0.1.0");
            }

            Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");

            string cliReleaseDirectory = Path.Combine(candidateRoot, "docs", "internal", "releases", version);
            Directory.CreateDirectory(cliReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes"));
            CommandContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            CommandRenderingContextPath = Path.Combine(cliReleaseDirectory, "release-rendering-context.v1.json");
            CommandContractDiffPath = Path.Combine(cliReleaseDirectory, "contract-diff.v1.json");
            File.WriteAllText(Path.Combine(cliReleaseDirectory, "release.yaml"),
                $"Version: {version}\nLine: {line}\nRelease-Date: 2026-08-14\nBase-Stable-Tag: {baselineRef}\nPrevious-Published-Tag: {baselineRef}\nRelease-Range:\n  Base-Ref: {baselineRef}\n  Base-Oid: {baselineOid}\n  Previous-Ref: {baselineRef}\n  Previous-Oid: {baselineOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(Path.Combine(cliReleaseDirectory, "summary.md"), SampleSummaryLine);
            File.WriteAllText(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"), SampleFragmentYaml);
            return cliReleaseDirectory;
        }

        private void WritePolicyFiles()
        {
            string sourceRoot = RepositoryRoot.Find();
            Directory.CreateDirectory(Path.Combine(candidateRoot, "eng", ReleaseFolderSegment, "policy"));
            File.Copy(
                Path.Combine(sourceRoot, "eng", ReleaseFolderSegment, "policy", "release-policy.yaml"),
                Path.Combine(candidateRoot, "eng", ReleaseFolderSegment, "policy", "release-policy.yaml"),
                overwrite: true);
            File.Copy(
                Path.Combine(sourceRoot, "eng", ReleaseFolderSegment, "policy", "scope-registry.yaml"),
                Path.Combine(candidateRoot, "eng", ReleaseFolderSegment, "policy", "scope-registry.yaml"),
                overwrite: true);
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

        private const string SampleFragmentYaml =
            "Change-Id: CHG-2026-0001\nTitle: Registration worker restart\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/API_CHANGELOG.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/internal/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/internal/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/internal/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/internal/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/internal/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n";

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
                ["breaking"] = new("docs/internal/API_CHANGELOG.md", "not-applicable", null, null),
                ["security"] = new("docs/internal/SECURITY_OVERVIEW.md", "not-applicable", null, null),
                ["migration"] = new("docs/internal/RELEASE_RUNBOOK.md", "not-applicable", null, null),
                ["configuration"] = new("docs/internal/CONFIGURATION.md", "not-applicable", null, null),
                ["openapi"] = new("docs/internal/API_CHANGELOG.md", "not-applicable", null, null),
                ["operator"] = new("docs/internal/RELEASE_RUNBOOK.md", "documented", null, "Restart registration workers after deployment."),
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
