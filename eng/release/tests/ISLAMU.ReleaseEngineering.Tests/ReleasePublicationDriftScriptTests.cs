// ABOUTME: Proves publication drift is reported against canonical release identity and never repaired.
// ABOUTME: Exercises the repository-owned file-based drift reporter with synthetic projection documents.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// A forge release page is mutable, unsigned, and editable by any maintainer or by the forge
/// itself, so it can never be release truth. These specifications pin the only guarantees that are
/// actually enforceable: each page must carry the canonical notes hash and its tag reference,
/// divergence is reported rather than repaired, and a provider without a release API degrades to a
/// recorded operator no-op instead of a failed release.
/// </summary>
[NotInParallel]
public sealed class ReleasePublicationDriftScriptTests
{
    [Test]
    public async Task DriftReporterAcceptsAProjectionCarryingTheCanonicalHashAndTagReference()
    {
        using var fixture = DriftFixture.Create();
        fixture.WriteProjections(fixture.InSyncProjection("github"));

        ScriptResult result = fixture.Run();
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(fixture.ReportPath));
        JsonElement root = report.RootElement;

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output);
        await Assert.That(result.Output).Contains("github: in-sync");
        await Assert.That(result.Output).Contains("publication_drift_none");
        await Assert.That(root.GetProperty("schemaVersion").GetString()).IsEqualTo("publication-drift-report.v1");
        await Assert.That(root.GetProperty("autoRepair").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("releaseInvalidated").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("canonical").GetProperty("tagRef").GetString()).IsEqualTo("refs/tags/v1.1.0");
        await Assert.That(root.GetProperty("canonical").GetProperty("releaseNotesSha256").GetString()).IsEqualTo(fixture.NotesSha256);
    }

    [Test]
    public async Task DriftReporterReportsAnEditedPageWithoutRepairingItOrFailingTheRelease()
    {
        using var edited = DriftFixture.Create();
        edited.WriteProjections(edited.ProjectionWithBody("github", "Someone edited this release page and removed the provenance header.\n"));
        byte[] notesBefore = File.ReadAllBytes(edited.NotesPath);

        ScriptResult result = edited.Run();
        byte[] notesAfter = File.ReadAllBytes(edited.NotesPath);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(edited.ReportPath));
        string findings = report.RootElement.GetProperty("projections")[0].GetProperty("findings").ToString();

        // Exit 0: drift is a report. The release is closed by its signed tag and stays valid.
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output);
        await Assert.That(result.Output).Contains("github: drift");
        await Assert.That(result.Output).Contains("publication_drift_reported: release remains valid");
        await Assert.That(findings).Contains("published_body_missing_canonical_notes_sha256");
        await Assert.That(findings).Contains("published_body_missing_tag_reference");
        await Assert.That(notesAfter).IsEquivalentTo(notesBefore);
        await Assert.That(report.RootElement.GetProperty("releaseInvalidated").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task DriftReporterFlagsWrongCanonicalHashWrongTagReferenceAndMissingAssets()
    {
        using var wrongHash = DriftFixture.Create();
        wrongHash.WriteProjections(wrongHash.InSyncProjection("github").Replace(wrongHash.NotesSha256, new string('0', 64), StringComparison.Ordinal));

        using var wrongTag = DriftFixture.Create();
        wrongTag.WriteProjections(wrongTag.InSyncProjection("github").Replace("refs/tags/v1.1.0", "refs/tags/v9.9.9", StringComparison.Ordinal));

        using var missingAsset = DriftFixture.Create();
        missingAsset.WriteProjections(missingAsset.InSyncProjection("github").Replace("\"sbom.spdx.json\"", "\"unrelated.txt\"", StringComparison.Ordinal));

        ScriptResult hashResult = wrongHash.Run();
        ScriptResult tagResult = wrongTag.Run();
        ScriptResult assetResult = missingAsset.Run();

        await Assert.That(hashResult.Output).Contains("github: drift");
        await Assert.That(File.ReadAllText(wrongHash.ReportPath)).Contains("declared_canonical_notes_sha256_mismatch");
        await Assert.That(tagResult.Output).Contains("github: drift");
        await Assert.That(File.ReadAllText(wrongTag.ReportPath)).Contains("declared_tag_reference_mismatch");
        await Assert.That(assetResult.Output).Contains("github: drift");
        await Assert.That(File.ReadAllText(missingAsset.ReportPath)).Contains("required_asset_missing:sbom.spdx.json");
    }

    [Test]
    public async Task ProviderWithoutAReleaseApiDegradesToARecordedNoOpBackedByOperatorEvidence()
    {
        using var evidenced = DriftFixture.Create();
        evidenced.WriteProjections(evidenced.NoOpProjection("tangled", "unsupported", "docs/internal/releases/evidence/tangled-2026-08-23.json"));

        using var unevidenced = DriftFixture.Create();
        unevidenced.WriteProjections(unevidenced.NoOpProjection("forgejo-codeberg", "unavailable", operatorEvidence: null));

        ScriptResult evidencedResult = evidenced.Run();
        ScriptResult unevidencedResult = unevidenced.Run();

        await Assert.That(evidencedResult.ExitCode).IsEqualTo(0).Because(evidencedResult.Output);
        await Assert.That(evidencedResult.Output).Contains("tangled: recorded-no-op");
        await Assert.That(evidencedResult.Output).Contains("publication_drift_none");
        await Assert.That(unevidencedResult.Output).Contains("forgejo-codeberg: drift");
        await Assert.That(File.ReadAllText(unevidenced.ReportPath)).Contains("recorded_no_op_missing_operator_evidence");
    }

    [Test]
    public async Task DriftReporterFailsClosedOnMalformedInputAndOnLocalCanonicalTampering()
    {
        using var malformed = DriftFixture.Create();
        File.WriteAllText(malformed.ProjectionsPath, "{\"schemaVersion\":\"something-else\"}\n");

        using var tampered = DriftFixture.Create();
        tampered.WriteProjections(tampered.InSyncProjection("github"));
        File.AppendAllText(tampered.NotesPath, "locally tampered\n");

        using var duplicate = DriftFixture.Create();
        duplicate.WriteProjections(duplicate.InSyncProjection("github"), duplicate.InSyncProjection("github"));

        ScriptResult malformedResult = malformed.Run();
        ScriptResult tamperedResult = tampered.Run();
        ScriptResult duplicateResult = duplicate.Run();

        await Assert.That(malformedResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(malformedResult.Output).Contains("drift_projection_schema_invalid");
        await Assert.That(tamperedResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(tamperedResult.Output).Contains("drift_canonical_notes_mismatch");
        await Assert.That(duplicateResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(duplicateResult.Output).Contains("drift_projection_provider_invalid");
    }

    [Test]
    public async Task OperatorsMayOptIntoAFailingGateWithoutChangingTheReport()
    {
        using var fixture = DriftFixture.Create();
        fixture.WriteProjections(fixture.ProjectionWithBody("github", "edited\n"));

        ScriptResult advisory = fixture.Run();
        ScriptResult gated = fixture.Run("--fail-on-drift");

        await Assert.That(advisory.ExitCode).IsEqualTo(0);
        await Assert.That(gated.ExitCode).IsEqualTo(3);
        await Assert.That(gated.Output).Contains("publication_drift_reported: release remains valid");
    }

    private sealed class DriftFixture : IDisposable
    {
        private const string TagName = "v1.1.0";

        private DriftFixture(string root)
        {
            Root = root;
            ReleaseDirectory = Path.Combine(root, "docs", "internal", "releases", "1.1.0");
            OutputDirectory = Path.Combine(root, "out");
            ProjectionsPath = Path.Combine(root, "publication-projection.v1.json");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            Directory.CreateDirectory(ReleaseDirectory);

            File.WriteAllText(NotesPath, "# Release 1.1.0\n\n## Maintainer Summary\n\nAttendees can now correct registration details.\n", new UTF8Encoding(false));
            NotesSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(NotesPath)));
            TagObjectId = new string('a', 40);
            File.WriteAllText(
                Path.Combine(ReleaseDirectory, "release-evidence.v1.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "release-evidence.v1",
                    version = "1.1.0",
                    tagName = TagName,
                    tagObjectId = TagObjectId,
                    releaseNotesSha256 = NotesSha256,
                }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }

        public string Root { get; }
        public string ReleaseDirectory { get; }
        public string OutputDirectory { get; }
        public string ProjectionsPath { get; }
        public string NotesPath { get; }
        public string NotesSha256 { get; }
        public string TagObjectId { get; }
        public string ReportPath => Path.Combine(OutputDirectory, "publication-drift-report.v1.json");

        public static DriftFixture Create() => new(Path.Combine(Path.GetTempPath(), $"islamu-drift-{Guid.NewGuid():N}"));

        public string InSyncProjection(string providerId) => ProjectionWithBody(
            providerId,
            $"Canonical notes SHA-256: {NotesSha256}\nTag: refs/tags/{TagName}\n\nSee the repository for the signed release.\n");

        public string ProjectionWithBody(string providerId, string body) => $$"""
            {
              "providerId": "{{providerId}}",
              "state": "published",
              "declaredCanonicalNotesSha256": "{{NotesSha256}}",
              "declaredTagRef": "refs/tags/{{TagName}}",
              "publishedBody": {{JsonSerializer.Serialize(body)}},
              "assets": ["release-evidence.v1.json", "artifacts.sha256", "container-image-digests.json", "sbom.spdx.json"]
            }
            """;

        public string NoOpProjection(string providerId, string state, string? operatorEvidence)
        {
            string evidence = operatorEvidence is null ? string.Empty : $",\n  \"operatorEvidenceReference\": \"{operatorEvidence}\"";
            return $$"""
                {
                  "providerId": "{{providerId}}",
                  "state": "{{state}}"{{evidence}}
                }
                """;
        }

        public void WriteProjections(params string[] projections) => File.WriteAllText(
            ProjectionsPath,
            $"{{\n  \"schemaVersion\": \"release-publication-projection.v1\",\n  \"projections\": [{string.Join(",", projections)}]\n}}\n",
            new UTF8Encoding(false));

        public ScriptResult Run(params string[] extraArguments)
        {
            string repositoryRoot = FindRepositoryRoot();
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--file");
            startInfo.ArgumentList.Add(".ci/scripts/report-publication-drift.cs");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--release-directory");
            startInfo.ArgumentList.Add(ReleaseDirectory);
            startInfo.ArgumentList.Add("--projections");
            startInfo.ArgumentList.Add(ProjectionsPath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(OutputDirectory);
            foreach (string argument in extraArguments) startInfo.ArgumentList.Add(argument);

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet failed to start");
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(output);
            }

            return new ScriptResult(process.ExitCode, output);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static string FindRepositoryRoot() => RepositoryRoot.Find();
    }

    private sealed record ScriptResult(int ExitCode, string Output);
}
