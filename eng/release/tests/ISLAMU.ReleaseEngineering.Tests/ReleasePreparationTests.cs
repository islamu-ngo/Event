// ABOUTME: Proves prepare composes canonical three-layer notes and preserves human-owned release inputs.
// ABOUTME: Exercises idempotency, impact coverage, renderer failure, and generated-file collision behavior.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleasePreparationTests
{
    private static readonly System.Text.Json.JsonSerializerOptions ContextJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    [Test]
    public async Task PrepareWritesThreeCanonicalLayersAndIsByteIdempotent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();

        ReleasePreparationResult first = fixture.Prepare();
        byte[] firstBytes = File.ReadAllBytes(fixture.NotesPath);
        ReleasePreparationResult second = fixture.Prepare();

        await Assert.That(first.IsValid).IsTrue();
        await Assert.That(second.IsValid).IsTrue();
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
    public async Task PrepareRejectsReorderedOtherwiseExactRangeBeforeRendering()
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
                Objects = current.Evidence.Objects.Append(new ReleaseContextObject(secondOid[..12], secondOid)).ToArray(),
            },
        };
        ReleaseContextValidationResult context = fixture.AsValidationResult(ordered);

        ReleasePreparationResult result = fixture.Prepare(context: context, rangeOids: [secondOid, new string('c', 40)]);

        await Assert.That(result.Diagnostic).IsEqualTo("prepare_range_context_mismatch");
        await Assert.That(File.Exists(fixture.NotesPath)).IsFalse();
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
    public async Task PrepareCommandReadsReleaseInputsBuildsContextAndRunsTwiceWithoutMutationCommands()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new PreparationFixture();

        (int firstCode, string firstOutput, byte[] firstNotes) = fixture.RunCommand();
        (int secondCode, string secondOutput, byte[] secondNotes) = fixture.RunCommand();

        await Assert.That(firstCode).IsEqualTo(Program.Success);
        await Assert.That(secondCode).IsEqualTo(Program.Success);
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
    public async Task PrepareCommandRejectsBaselineDescriptorWhenStableSemVerTagIsReachable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var accepted = new PreparationFixture();
        using var rejected = new PreparationFixture();

        (int acceptedCode, string acceptedOutput, byte[] acceptedNotes) = accepted.RunBaselineCommand("0.1.0", includeReachableStableTag: false);
        (int rejectedCode, string rejectedOutput, byte[] rejectedNotes) = rejected.RunBaselineCommand("0.2.0", includeReachableStableTag: true);

        await Assert.That(acceptedCode).IsEqualTo(Program.Success);
        await Assert.That(acceptedOutput).IsEqualTo("docs(release): prepare 0.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n");
        await Assert.That(acceptedNotes).IsNotEmpty();
        await Assert.That(File.Exists(accepted.CommandContextPath)).IsTrue();
        await Assert.That(File.ReadAllText(accepted.CommandContextPath)).Contains("\"version\": \"0.1.0\"");
        await Assert.That(rejectedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(rejectedOutput).IsEqualTo("prepare_failed: git_baseline_stable_tag_exists:v0.1.0\n");
        await Assert.That(rejectedNotes).IsEmpty();
        await Assert.That(File.Exists(rejected.CommandContextPath)).IsFalse();
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
    public async Task PrepareExecutableRunsSignedBundlePositiveAndNegativeTwice()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using (var positive = new PreparationFixture())
        {
            (int firstCode, string firstOutput, byte[] firstNotes) = positive.RunSpawnedCommand("Attendees can now correct registration details.\n");
            (int secondCode, string secondOutput, byte[] secondNotes) = positive.RunSpawnedCommand("Attendees can now correct registration details.\n");
            await Assert.That(firstCode).IsEqualTo(Program.Success);
            await Assert.That(secondCode).IsEqualTo(Program.Success);
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
        private readonly string authorityRoot;
        private readonly string privateKeyPath;
        private readonly string receiptPath;
        private readonly string signaturePath;
        private readonly string allowedSignersPath;
        public PreparationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-prepare-{Guid.NewGuid():N}");
            ReleaseDirectory = Path.Combine(Root, "docs", "internal", "releases", "1.1.0");
            bundleRoot = Path.Combine(Root, "bundle");
            isolationRoot = Path.Combine(Root, "isolation");
            lockPath = Path.Combine(bundleRoot, "toolchain.lock.json");
            configPath = Path.Combine(bundleRoot, "config", "cliff.toml");
            executablePath = Path.Combine(bundleRoot, "git-cliff");
            candidateRoot = Path.Combine(Root, "candidate");
            authorityRoot = Path.Combine(Root, "authority");
            privateKeyPath = Path.Combine(authorityRoot, "promotion-key");
            receiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
            signaturePath = receiptPath + ".sig";
            allowedSignersPath = Path.Combine(authorityRoot, "allowed-promoters");
            Directory.CreateDirectory(ReleaseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(isolationRoot);
            Directory.CreateDirectory(candidateRoot);
            Directory.CreateDirectory(authorityRoot);
            ReleasePath = Path.Combine(ReleaseDirectory, "release.yaml");
            SummaryPath = Path.Combine(ReleaseDirectory, "summary.md");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            File.WriteAllText(ReleasePath, "Version: 1.1.0\n");
            File.WriteAllText(SummaryPath, "Attendees can now correct registration details.\n");
            File.WriteAllText(configPath,
                "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n");
            Input = ValidInput();
            Context = ValidContext();
            CreatePromotionAuthority();
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
        public string CommandRangePath { get; private set; } = string.Empty;
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
                    VerifiedBundle(),
                    Encoding.UTF8.GetBytes(effectiveContext.Json!),
                    "linux-x64",
                    isolationRoot,
                TimeSpan.FromSeconds(2))));
        }

        public (int ExitCode, string Output, byte[] Notes) RunCommand()
        {
            string cliReleaseDirectory = EnsureCommandRepository("Attendees can now correct registration details.\n");
            RewriteManifestAndReceipt();

            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot,
                ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath,
                ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath,
                ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
                ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")),
                ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
                ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
                ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
                ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
                ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
            };
            Dictionary<string, string?> originalVariables = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
            try
            {
                foreach ((string name, string value) in variables) Environment.SetEnvironmentVariable(name, value);
                using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", "docs/internal/releases/1.1.0"],
                    output,
                    candidateRoot,
                    "linux-x64",
                    TimeSpan.FromSeconds(2));
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            }
            finally
            {
                foreach ((string name, string? value) in originalVariables) Environment.SetEnvironmentVariable(name, value);
            }
        }

        public (int ExitCode, string Output, byte[] Notes) RunBaselineCommand(string version, bool includeReachableStableTag)
        {
            string cliReleaseDirectory = EnsureBaselineCommandRepository(version, includeReachableStableTag);
            WriteRenderer(version);
            RewriteManifestAndReceipt();

            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot,
                ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath,
                ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath,
                ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
                ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")),
                ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
                ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
                ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
                ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
                ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
            };
            Dictionary<string, string?> originalVariables = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
            try
            {
                foreach ((string name, string value) in variables) Environment.SetEnvironmentVariable(name, value);
                using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(
                    ["prepare", $"docs/internal/releases/{version}"],
                    output,
                    candidateRoot,
                    "linux-x64",
                    TimeSpan.FromSeconds(2));
                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (exitCode, output.ToString(), File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            }
            finally
            {
                foreach ((string name, string? value) in originalVariables) Environment.SetEnvironmentVariable(name, value);
            }
        }

        public (int ExitCode, string Output) RunBadCommand()
        {
            string cliReleaseDirectory = EnsureCommandRepository("# generated-region\n");
            BadNotesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
            BadContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            RewriteManifestAndReceipt();
            return RunPrepareOnly();
        }

        public (int ExitCode, string Output, byte[] Notes) RunSpawnedCommand(string summary)
        {
            string cliReleaseDirectory = EnsureCommandRepository(summary);
            RewriteManifestAndReceipt();
            string assemblyPath = typeof(Program).Assembly.Location;
            using (RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath))
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = candidateRoot,
                };
                foreach (string argument in new[] { assemblyPath, "prepare", "docs/internal/releases/1.1.0" })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                startInfo.Environment["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot;
                startInfo.Environment["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath;
                startInfo.Environment["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath;
                startInfo.Environment["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter";
                startInfo.Environment["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json"));
                startInfo.Environment["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering";
                startInfo.Environment["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0";
                startInfo.Environment["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1";
                startInfo.Environment["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1";
                startInfo.Environment["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1";
                using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("spawned_prepare_timeout");
                }

                if (error.Length != 0)
                {
                    throw new InvalidOperationException("spawned_prepare_stderr");
                }

                string notesPath = Path.Combine(cliReleaseDirectory, "release-notes.md");
                return (process.ExitCode, output, File.Exists(notesPath) ? File.ReadAllBytes(notesPath) : []);
            }
        }

        private (int ExitCode, string Output) RunPrepareOnly()
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot,
                ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath,
                ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath,
                ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
                ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")),
                ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
                ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
                ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
                ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
                ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
            };
            Dictionary<string, string?> originalVariables = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
            try
            {
                foreach ((string name, string value) in variables) Environment.SetEnvironmentVariable(name, value);
                using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(["prepare", "docs/internal/releases/1.1.0"], output, candidateRoot, "linux-x64", TimeSpan.FromSeconds(2));
                return (exitCode, output.ToString());
            }
            finally
            {
                foreach ((string name, string? value) in originalVariables) Environment.SetEnvironmentVariable(name, value);
            }
        }

        private void WriteRenderer(string version = "1.1.0")
        {
            string body = RendererFails
                ? "exit 7"
                : $"if [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.13.1\\n'; exit 0; fi\nprintf '# Release {version}\\n\\n- registration: let attendees correct registration details (cccccccccccc)\\n'";
            File.WriteAllText(executablePath, "#!/bin/sh\n" + body + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            File.WriteAllText(lockPath, $$"""
                {
                  "schemaVersion": 1,
                  "tool": "git-cliff",
                  "version": "2.13.1",
                  "platforms": [{ "platform": "linux-x64", "executable": "git-cliff", "executableSha256": "{{Digest(executablePath)}}" }]
                }
                """);
            RewriteManifestAndReceipt();
        }

        private string EnsureCommandRepository(string summary)
        {
            if (!Directory.Exists(Path.Combine(candidateRoot, ".git")))
            {
                Directory.CreateDirectory(Path.Combine(candidateRoot, "eng", "release", "policy"));
                File.WriteAllText(Path.Combine(candidateRoot, "eng", "release", "policy", "release-policy.yaml"),
                    "schemaVersion: 1\nmaximumCommitMessageBytes: 8192\nreleaseVisibleTypes:\n  - feat\n  - fix\n  - perf\n  - revert\n  - docs\ninternalTypes:\n  - test\n  - refactor\n  - style\n  - build\n  - ci\n  - chore\nrequiredBreakingSignals:\n  bang: true\n  footer: BREAKING CHANGE\nskipTrailer: Changelog\nskipValue: skip\nskipReasonTrailer: Changelog-Reason\n");
                File.WriteAllText(Path.Combine(candidateRoot, "eng", "release", "policy", "scope-registry.yaml"),
                    "schemaVersion: 1\npublicScopes:\n  - events\n  - registration\nengineeringScopes:\n  - release\n");
                Git("init", "--initial-branch=main");
                string previous = Commit("fix(events): preserve published event notes");
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v1.0.0", previous, "-m", "v1.0.0");
                string feature = Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
            }

            string cliReleaseDirectory = Path.Combine(candidateRoot, "docs", "internal", "releases", "1.1.0");
            Directory.CreateDirectory(cliReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes"));
            string previousOid = Git("rev-list", "--max-parents=0", "HEAD").Trim();
            CommandReleasePath = Path.Combine(cliReleaseDirectory, "release.yaml");
            CommandSummaryPath = Path.Combine(cliReleaseDirectory, "summary.md");
            string featureOid = Git("rev-parse", "HEAD^{commit}").Trim();
            CommandContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            CommandRangePath = Path.Combine(cliReleaseDirectory, "range.txt");
            File.WriteAllText(CommandReleasePath,
                $"Version: 1.1.0\nLine: v1.1\nRelease-Date: 2026-08-14\nBase-Stable-Tag: v1.0.0\nPrevious-Published-Tag: v1.0.0\nRelease-Range:\n  Base-Ref: v1.0.0\n  Base-Oid: {previousOid}\n  Previous-Ref: v1.0.0\n  Previous-Oid: {previousOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(CommandSummaryPath, summary);
            File.WriteAllText(Path.Combine(candidateRoot, "context.json"), "caller supplied context must be ignored\n");
            File.WriteAllText(Path.Combine(candidateRoot, "range.txt"), new string('f', 40) + "\n");
            File.WriteAllText(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"),
                "Change-Id: CHG-2026-0001\nTitle: Registration worker restart\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n");
            return cliReleaseDirectory;
        }

        private string EnsureBaselineCommandRepository(string version, bool includeReachableStableTag)
        {
            string baselineRef = "changelog-baseline-2026-08-15";
            string line = "v" + string.Join('.', version.Split('.')[0], version.Split('.')[1]);
            Directory.CreateDirectory(Path.Combine(candidateRoot, "eng", "release", "policy"));
            File.WriteAllText(Path.Combine(candidateRoot, "eng", "release", "policy", "release-policy.yaml"),
                "schemaVersion: 1\nmaximumCommitMessageBytes: 8192\nreleaseVisibleTypes:\n  - feat\n  - fix\n  - perf\n  - revert\n  - docs\ninternalTypes:\n  - test\n  - refactor\n  - style\n  - build\n  - ci\n  - chore\nrequiredBreakingSignals:\n  bang: true\n  footer: BREAKING CHANGE\nskipTrailer: Changelog\nskipValue: skip\nskipReasonTrailer: Changelog-Reason\n");
            File.WriteAllText(Path.Combine(candidateRoot, "eng", "release", "policy", "scope-registry.yaml"),
                "schemaVersion: 1\npublicScopes:\n  - events\n  - registration\nengineeringScopes:\n  - release\n");
            Git("init", "--initial-branch=main");
            string baselineOid = Commit("baseline lower bound");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", baselineRef, baselineOid, "-m", baselineRef);
            string baselineTagObjectId = Git("rev-parse", $"refs/tags/{baselineRef}^{{object}}").Trim();
            Directory.CreateDirectory(Path.Combine(candidateRoot, "docs", "internal", "releases", "baselines"));
            File.WriteAllBytes(Path.Combine(candidateRoot, "docs", "internal", "releases", "baselines", baselineRef + ".v1.json"), CanonicalArtifactPolicy.CanonicalizeJson(JsonSerializer.Serialize(new
            {
                schemaVersion = "release-baseline.v1",
                baselineRef,
                targetOid = baselineOid,
                tagObjectId = baselineTagObjectId,
            })).Bytes!);

            if (includeReachableStableTag)
            {
                string stable = Commit("existing governed stable");
                Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v0.1.0", stable, "-m", "v0.1.0");
            }

            string feature = Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
            Git("branch", "-f", line, feature);

            string cliReleaseDirectory = Path.Combine(candidateRoot, "docs", "internal", "releases", version);
            Directory.CreateDirectory(cliReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes"));
            CommandReleasePath = Path.Combine(cliReleaseDirectory, "release.yaml");
            CommandSummaryPath = Path.Combine(cliReleaseDirectory, "summary.md");
            CommandContextPath = Path.Combine(cliReleaseDirectory, "release-context.v1.json");
            File.WriteAllText(CommandReleasePath,
                $"Version: {version}\nLine: {line}\nRelease-Date: 2026-08-14\nBase-Stable-Tag: {baselineRef}\nPrevious-Published-Tag: {baselineRef}\nRelease-Range:\n  Base-Ref: {baselineRef}\n  Base-Oid: {baselineOid}\n  Previous-Ref: {baselineRef}\n  Previous-Oid: {baselineOid}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(CommandSummaryPath, "Attendees can now correct registration details.\n");
            File.WriteAllText(Path.Combine(candidateRoot, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"),
                "Change-Id: CHG-2026-0001\nTitle: Registration worker restart\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n");
            return cliReleaseDirectory;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

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
                [new ReleaseContextChange("cccccccccccc", new string('c', 40), "CHG-2026-0001", "feat", "registration", "Registration worker restart", "Attendees can now correct registration details.", false, false, null)],
                new ReleaseContextEvidence(new string('a', 40), new string('b', 40),
                    [new ReleaseContextObject("aaaaaaaaaaaa", new string('a', 40)), new ReleaseContextObject("bbbbbbbbbbbb", new string('b', 40)), new ReleaseContextObject("cccccccccccc", new string('c', 40))]));
            string json = System.Text.Json.JsonSerializer.Serialize(context, ContextJsonOptions) + "\n";
            return new ReleaseContextValidationResult(true, context, json, []);
        }

        public ReleaseContextValidationResult WithEvidenceObject(string oid)
        {
            ReleaseContext current = Context.Context!;
            var objects = current.Evidence.Objects.Append(new ReleaseContextObject(oid[..12], oid)).ToArray();
            ReleaseContext changed = current with { Evidence = current.Evidence with { Objects = objects } };
            string json = JsonSerializer.Serialize(changed, ContextJsonOptions) + "\n";
            return new ReleaseContextValidationResult(true, changed, json, []);
        }

        public ReleaseContextValidationResult AsValidationResult(ReleaseContext context)
        {
            string json = JsonSerializer.Serialize(context, ContextJsonOptions) + "\n";
            return new ReleaseContextValidationResult(true, context, json, []);
        }

        private VerifiedTrustedBundle VerifiedBundle()
        {
            RewriteManifestAndReceipt();
            using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
            var request = new TrustedBundleVerificationRequest(
                bundleRoot,
                candidateRoot,
                new PromotionAuthorityInput(receiptPath, signaturePath, "fixture-tooling-promoter"),
                "islamu-release-engineering",
                "1.0.0",
                "policy-v1",
                "config-v1",
                "trust-v1")
            { ExpectedManifestDigest = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")) };
            TrustedBundleResult result = TrustedBundlePolicy.Verify(request);
            if (!result.IsValid || result.Bundle is null) throw new InvalidOperationException(result.Diagnostic);
            return result.Bundle;
        }

        private void RewriteManifestAndReceipt()
        {
            EnsureFile("bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
            EnsureFile("policy/context-version.txt", "context-v1\n");
            EnsureFile("policy/schema-version.txt", "schema-v1\n");
            EnsureFile("trust/allowed-signers", "# production signers absent\n");
            string manifestJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle.v1",
                bundleId = "islamu-release-engineering",
                bundleVersion = "1.0.0",
                policyVersion = "policy-v1",
                configVersion = "config-v1",
                trustVersion = "trust-v1",
                policyDigest = Digest(EnsureFile("policy/release-policy.yaml", "release-policy")),
                configDigest = Digest(configPath),
                trustDigest = Digest(EnsureFile("trust/release-signing-policy.yaml", "status: inactive-fixture-only\n")),
                files = Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path) != "trusted-bundle.manifest.json")
                    .Select(path => new { path = Path.GetRelativePath(bundleRoot, path).Replace(Path.DirectorySeparatorChar, '/'), sha256 = Digest(path) })
                    .OrderBy(item => item.path, StringComparer.Ordinal)
                    .ToArray(),
            });
            File.WriteAllBytes(Path.Combine(bundleRoot, "trusted-bundle.manifest.json"), CanonicalArtifactPolicy.CanonicalizeJson(manifestJson).Bytes!);
            ResignReceipt();
        }

        private string EnsureFile(string path, string content)
        {
            string fullPath = Path.Combine(bundleRoot, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (!File.Exists(fullPath)) File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private void CreatePromotionAuthority()
        {
            Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion-fixture", "-f", privateKeyPath);
            string publicKey = string.Join(' ', File.ReadAllText(privateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedSignersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey}\n");
        }

        private void ResignReceipt()
        {
            string manifestPath = Path.Combine(bundleRoot, "trusted-bundle.manifest.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            JsonElement root = document.RootElement;
            string receiptJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle-promotion.v1",
                receiptId = "promotion-fixture-0001",
                bundleManifestSha256 = Digest(manifestPath),
                bundleId = root.GetProperty("bundleId").GetString(),
                bundleVersion = root.GetProperty("bundleVersion").GetString(),
                policyVersion = root.GetProperty("policyVersion").GetString(),
                configVersion = root.GetProperty("configVersion").GetString(),
                trustVersion = root.GetProperty("trustVersion").GetString(),
                policyDigest = root.GetProperty("policyDigest").GetString(),
                configDigest = root.GetProperty("configDigest").GetString(),
                trustDigest = root.GetProperty("trustDigest").GetString(),
                promotionPrincipal = "fixture-tooling-promoter",
            });
            File.WriteAllBytes(receiptPath, CanonicalArtifactPolicy.CanonicalizeJson(receiptJson).Bytes!);
            if (File.Exists(signaturePath)) File.Delete(signaturePath);
            Run("/usr/bin/ssh-keygen", "-Y", "sign", "-f", privateKeyPath, "-n", "islamu-release-promotion", receiptPath);
        }

        private static void Run(string executable, params string[] arguments)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("synthetic_fixture_timeout");
            }

            if (process.ExitCode != 0) throw new InvalidOperationException("synthetic_fixture_failed");
        }

        private string Commit(string message)
        {
            File.AppendAllText(Path.Combine(candidateRoot, "file.txt"), message + Environment.NewLine);
            Git("add", "file.txt");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        private string Git(params string[] arguments)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = candidateRoot };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("synthetic_git_failed");
            return output;
        }

        private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
