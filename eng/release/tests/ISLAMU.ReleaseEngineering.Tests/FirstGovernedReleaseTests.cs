using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// Task 8.2 in rehearsal. Eleven months of pre-automation development have no release tags, so the
/// first governed release is lower-bounded by one signed <c>changelog-baseline-YYYY-MM-DD</c> tag
/// rather than by re-parsing messy historical commits. Everything below runs for real against a
/// disposable repository: the baseline is verified and recorded, <c>0.1.0</c> is prepared, attested
/// at exact <c>B</c> with no branch input, signed, closed by final evidence, and re-verified offline
/// in a clone that fetched only tags — with no <c>release/0.1</c> branch created anywhere.
///
/// What these tests deliberately cannot supply is the part that is not engineering: a
/// steward-approved version, a merged activation commit in the real repository, and a real release
/// signer's key. Those remain an operator action, and fabricating them here would manufacture a
/// first governed release that nobody actually authorised.
/// </summary>
[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class FirstGovernedReleaseTests
{
    [Test]
    public async Task BaselineTagIsVerifiedAndRecordedInBaselineEvidence()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateFirstGovernedRelease();
        string evidencePath = Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", "baselines", $"{GovernedReleaseFixture.BaselineRef}.v1.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(evidencePath));
        JsonElement root = document.RootElement;
        bool readBack = BaselineEvidencePolicy.TryRead(fixture.RepositoryPath, GovernedReleaseFixture.BaselineRef, out VerifiedBaseline baseline);

        await Assert.That(root.GetProperty("schemaVersion").GetString()).IsEqualTo("release-baseline.v1");
        await Assert.That(root.GetProperty("baselineRef").GetString()).IsEqualTo(GovernedReleaseFixture.BaselineRef);
        await Assert.That(root.GetProperty("targetOid").GetString()).IsEqualTo(fixture.BaselineTargetOid);
        await Assert.That(root.GetProperty("tagObjectId").GetString()).IsEqualTo(fixture.BaselineTagObject);
        await Assert.That(readBack).IsTrue();
        await Assert.That(baseline.TargetOid).IsEqualTo(fixture.BaselineTargetOid);
    }

    [Test]
    public async Task FirstGovernedReleaseProducesDeterministicThreeLayerNotes()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateFirstGovernedRelease();
        string version = GovernedReleaseFixture.FirstGovernedReleaseVersion;
        string notes = File.ReadAllText(Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", version, "release-notes.md"));

        await Assert.That(notes).StartsWith($"# Release {version}\n");
        await Assert.That(notes).Contains("## Maintainer Summary");
        await Assert.That(notes).Contains("## Release-Visible Details");
        await Assert.That(notes).Contains("## Complete Commit Range");
        await Assert.That(notes).DoesNotContain("@example");
        await Assert.That(notes).DoesNotContain("Release Test");
    }

    [Test]
    [Explicit]
    [Category("Runtime")]
    public async Task CategorizedReleaseVerifiesAfterBranchDeletion()
    {
        using var fixture = GovernedReleaseFixture.CreateCategorizedFirstGovernedRelease();
        string version = GovernedReleaseFixture.FirstGovernedReleaseVersion;
        string directory = Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", version);
        byte[] notesBytes = File.ReadAllBytes(Path.Combine(directory, "release-notes.md"));
        string notes = System.Text.Encoding.UTF8.GetString(notesBytes);
        using JsonDocument context = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "release-context.v1.json")));
        JsonElement[] changes = context.RootElement.GetProperty("changes").EnumerateArray().ToArray();

        await Assert.That(changes.Length).IsEqualTo(5);
        await Assert.That(changes.Single(change => change.GetProperty("breaking").GetBoolean()).GetProperty("type").GetString()).IsEqualTo("fix");
        await Assert.That(context.RootElement.GetProperty("release").GetProperty("minimumBump").GetString()).IsEqualTo("minor");

        string primary = notes.Split("## Release-Visible Details\n", StringSplitOptions.None)[1]
            .Split("### Impact Summary\n", StringSplitOptions.None)[0];
        // This assertion is the real-renderer Red gate: preparation and signed closure already succeeded.
        await Assert.That(primary).Contains("### ⚠️ Breaking Changes");
        (string Heading, string Type, bool Breaking)[] categories =
        [
            ("### ⚠️ Breaking Changes", "fix", true),
            ("### 🚀 Features", "feat", false),
            ("### 🐛 Bug Fixes", "fix", false),
            ("### ⚡ Performance", "perf", false),
            ("### 🔧 Other Improvements", "docs", false),
        ];
        string[] expectedPrimary = categories.SelectMany(category =>
        {
            JsonElement change = changes.Single(value =>
                value.GetProperty("type").GetString() == category.Type &&
                value.GetProperty("breaking").GetBoolean() == category.Breaking);
            return new[] { category.Heading, $"- {change.GetProperty("scope").GetString()}: {change.GetProperty("title").GetString()} ({change.GetProperty("displayId").GetString()})" };
        }).ToArray();
        await Assert.That(string.Join('\n', primary.Split('\n', StringSplitOptions.RemoveEmptyEntries)))
            .IsEqualTo(string.Join('\n', expectedPrimary));

        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(
            File.ReadAllText(Path.Combine(directory, "release.yaml")),
            Directory.EnumerateFiles(Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", "changes"), "*.yaml")
                .Order(StringComparer.Ordinal).Select(File.ReadAllText).ToArray(),
            []);
        await Assert.That(input.IsValid).IsTrue();
        PublicChangeFragment upgrade = input.Fragments.Single(fragment => fragment.ChangeId == "CHG-2026-0002");
        foreach ((string impact, string heading) in new[]
        {
            ("breaking", "Breaking"), ("security", "Security"), ("migration", "Migration"),
            ("configuration", "Configuration"), ("operator", "Operator"),
        })
        {
            FragmentImpact evidence = upgrade.Impacts[impact];
            await Assert.That(input.Descriptor!.ImpactDispositions[impact]).IsEqualTo("documented");
            await Assert.That(evidence.Disposition).IsEqualTo("documented");
            await Assert.That(string.IsNullOrWhiteSpace(evidence.Detail)).IsFalse();
            string section = notes.Split($"#### {heading}\n", StringSplitOptions.None)[1].Split("\n#", StringSplitOptions.None)[0];
            await Assert.That(section).Contains($"- `{upgrade.ChangeId}` - documented: {CanonicalArtifactPolicy.EscapeUntrustedMarkdown(evidence.Detail!).Text} (Evidence: `{CanonicalArtifactPolicy.EscapeUntrustedMarkdown(evidence.Reference).Text}`)");
        }
        string range = notes.Split("## Complete Commit Range\n", StringSplitOptions.None)[1].Trim();
        await Assert.That(range).IsEqualTo(string.Join('\n',
            fixture.RangeOids(GovernedReleaseFixture.BaselineRef, fixture.A).Select(oid =>
                $"- `{changes.Single(change => change.GetProperty("oid").GetString() == oid).GetProperty("displayId").GetString()}`")));
        await Assert.That(notes).DoesNotContain("release@example.invalid");
        await Assert.That(notes).DoesNotContain("Release Test");

        fixture.DeleteIntegrationBranch();
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, fixture.B);
        (int tagCode, string tagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
        byte[] candidateBytes = File.ReadAllBytes(Path.Combine(directory, "release-candidate.v1.json"));
        byte[] evidenceBytes = File.ReadAllBytes(Path.Combine(directory, "release-evidence.v1.json"));
        using JsonDocument candidate = JsonDocument.Parse(candidateBytes);
        using JsonDocument finalEvidence = JsonDocument.Parse(evidenceBytes);
        await Assert.That(fixture.BranchRefs()).IsEqualTo(string.Empty);
        await Assert.That(candidate.RootElement.GetProperty("candidateOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(candidate.RootElement.GetProperty("candidateParentOid").GetString()).IsEqualTo(fixture.A);
        await Assert.That(candidate.RootElement.GetProperty("releaseNotesSha256").GetString())
            .IsEqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(notesBytes)));
        await Assert.That(finalEvidence.RootElement.GetProperty("targetOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(finalEvidence.RootElement.GetProperty("tagObjectId").GetString()).IsEqualTo(fixture.FirstTagObject);
        await Assert.That(finalEvidence.RootElement.GetProperty("tagName").GetString()).IsEqualTo($"v{version}");
        await Assert.That(finalEvidence.RootElement.GetProperty("baseStableTag").GetString()).IsEqualTo(GovernedReleaseFixture.BaselineRef);

        (int repeatedCandidateCode, string repeatedCandidateOutput) = fixture.VerifyCandidate(version, fixture.B);
        (int repeatedTagCode, string repeatedTagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);
        await Assert.That(repeatedCandidateCode).IsEqualTo(Program.Success).Because(repeatedCandidateOutput);
        await Assert.That(repeatedTagCode).IsEqualTo(Program.Success).Because(repeatedTagOutput);
        await Assert.That(File.ReadAllBytes(Path.Combine(directory, "release-candidate.v1.json"))).IsEquivalentTo(candidateBytes);
        await Assert.That(File.ReadAllBytes(Path.Combine(directory, "release-evidence.v1.json"))).IsEquivalentTo(evidenceBytes);

        string clone = fixture.CreateTagOnlyClone($"v{version}");
        (int cloneCandidateCode, string cloneCandidateOutput) = fixture.VerifyCandidate(version, fixture.B, clone);
        (int cloneTagCode, string cloneTagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject, clone);
        await Assert.That(cloneCandidateCode).IsEqualTo(Program.Success).Because(cloneCandidateOutput);
        await Assert.That(cloneTagCode).IsEqualTo(Program.Success).Because(cloneTagOutput);
        await Assert.That(fixture.BranchRefs(clone)).IsEqualTo(string.Empty);
        string cloneDirectory = Path.Combine(clone, "docs", "internal", "releases", version);
        await Assert.That(File.ReadAllBytes(Path.Combine(cloneDirectory, "release-notes.md"))).IsEquivalentTo(notesBytes);
        await Assert.That(File.ReadAllBytes(Path.Combine(cloneDirectory, "release-candidate.v1.json"))).IsEquivalentTo(candidateBytes);
        await Assert.That(File.ReadAllBytes(Path.Combine(cloneDirectory, "release-evidence.v1.json"))).IsEquivalentTo(evidenceBytes);
    }

    [Test]
    public async Task CandidateBPassesFullAttestationWithNoBranchInputAndTheTagClosesTheRelease()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateFirstGovernedRelease();
        string version = GovernedReleaseFixture.FirstGovernedReleaseVersion;

        // Remove every branch first: attestation must not need one.
        fixture.DeleteIntegrationBranch();
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, fixture.B);
        (int tagCode, string tagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);

        string candidate = File.ReadAllText(Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", version, "release-candidate.v1.json"));
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", version, "release-evidence.v1.json")));

        await Assert.That(fixture.BranchRefs()).IsEqualTo(string.Empty);
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
        await Assert.That(candidate).DoesNotContain("refs/heads");
        await Assert.That(evidence.RootElement.GetProperty("tagName").GetString()).IsEqualTo($"v{version}");
        await Assert.That(evidence.RootElement.GetProperty("line").GetString()).IsEqualTo("v0.1");
        await Assert.That(evidence.RootElement.GetProperty("targetOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(evidence.RootElement.GetProperty("baseStableTag").GetString()).IsEqualTo(GovernedReleaseFixture.BaselineRef);
    }

    [Test]
    public async Task TheTagReVerifiesInAFreshTagOnlyCloneWithNoForgeApiAndNoBranch()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateFirstGovernedRelease();
        string version = GovernedReleaseFixture.FirstGovernedReleaseVersion;
        fixture.VerifyCandidate(version, fixture.B);
        fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);
        byte[] originalEvidence = File.ReadAllBytes(Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", version, "release-evidence.v1.json"));

        string clone = fixture.CreateTagOnlyClone($"v{version}");
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, fixture.B, clone);
        (int tagCode, string tagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject, clone);
        byte[] cloneEvidence = File.ReadAllBytes(Path.Combine(clone, "docs", "internal", "releases", version, "release-evidence.v1.json"));

        await Assert.That(fixture.BranchRefs(clone)).IsEqualTo(string.Empty);
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
        await Assert.That(cloneEvidence).IsEquivalentTo(originalEvidence);
    }

    [Test]
    public async Task NoMaintenanceBranchIsCreatedAndTheStableMainMoveIsMerelyProposed()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateFirstGovernedRelease();
        string version = GovernedReleaseFixture.FirstGovernedReleaseVersion;
        fixture.VerifyCandidate(version, fixture.B);
        fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);

        fixture.SetObservedOriginMain(fixture.BaselineTargetOid);
        string refsBefore = fixture.AllRefs();
        (int mainCode, string mainOutput) = fixture.VerifyMain(version, fixture.BaselineTargetOid, fixture.FirstTagObject);

        await Assert.That(mainCode).IsEqualTo(Program.Success).Because(mainOutput);
        await Assert.That(mainOutput).IsEqualTo(
            $"release_main_verified: action=move-main old={fixture.BaselineTargetOid} new={fixture.B} tag=v{version} instruction=update-main-fast-forward\n");

        // Lazy maintenance lines are genuinely optional: nothing named release/0.1 exists.
        await Assert.That(fixture.BranchRefs()).DoesNotContain("release/0.1");
        await Assert.That(fixture.AllRefs()).IsEqualTo(refsBefore);
    }

    [Test]
    public async Task ASecondBaselineIsRefusedOnceAGovernedStableTagIsReachable()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateFirstGovernedRelease();

        // The baseline is a one-time lower bound. Once v0.1.0 is reachable, later releases must use
        // stable SemVer tags, so the baseline can never be reused to re-anchor history.
        GitReleaseValidationResult reuse = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            fixture.RepositoryPath,
            "v0.2",
            "0.2.0",
            $"refs/tags/{GovernedReleaseFixture.BaselineRef}",
            $"refs/tags/{GovernedReleaseFixture.BaselineRef}",
            fixture.B));

        await Assert.That(reuse.IsValid).IsFalse();
        await Assert.That(reuse.Diagnostics).Contains($"git_baseline_stable_tag_exists:v{GovernedReleaseFixture.FirstGovernedReleaseVersion}");
    }

    [Test]
    public async Task Sha256RepositoriesCompleteTheSameFirstGovernedReleaseFlow()
    {
        if (OperatingSystem.IsWindows()) return;

        GovernedReleaseFixture? fixture;
        try
        {
            fixture = GovernedReleaseFixture.CreateFirstGovernedRelease("sha256");
        }
        catch (InvalidOperationException)
        {
            return;
        }

        using (fixture)
        {
            string version = GovernedReleaseFixture.FirstGovernedReleaseVersion;
            fixture.DeleteIntegrationBranch();
            (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, fixture.B);
            (int tagCode, string tagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);

            await Assert.That(fixture.B.Length).IsEqualTo(64);
            await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
            await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
        }
    }
}
