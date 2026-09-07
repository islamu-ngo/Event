// ABOUTME: Advisory end-to-end dry run of the governed release flow against a disposable repository.
// ABOUTME: Walks prepare, exact-B candidate, signed tag, final evidence, and main verification without mutating refs.

using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// Task 8.1's advisory activation dry run, expressed as an executable specification rather than a
/// document that can drift. It exercises the complete governed flow end to end — preparation,
/// exact-<c>B</c> candidate attestation, canonical tag message, SSH-signed annotated tag, final
/// evidence, and the stable-main proposal — and then re-verifies an already-closed release after
/// the branch that carried its commits has advanced and after it has been deleted outright.
/// Nothing here creates, moves, or pushes a ref in a real repository.
/// </summary>
[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleaseActivationDryRunTests
{
    [Test]
    public async Task GovernedFlowClosesTwoReleasesAndProposesAStableMainFastForward()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();

        // Re-close the newest release so its final evidence exists for the main proposal.
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(GovernedReleaseFixture.SecondReleaseVersion, fixture.D);
        (int tagCode, string tagOutput) = fixture.VerifyTag(GovernedReleaseFixture.SecondReleaseVersion, fixture.D, fixture.SecondTagObject);

        // `main` is a derived pointer: it currently sits at the previous release's commit B.
        fixture.SetObservedOriginMain(fixture.B);
        (int mainCode, string mainOutput) = fixture.VerifyMain(GovernedReleaseFixture.SecondReleaseVersion, fixture.B, fixture.SecondTagObject);

        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
        await Assert.That(mainCode).IsEqualTo(Program.Success).Because(mainOutput);
        await Assert.That(mainOutput).IsEqualTo(
            $"release_main_verified: action=move-main old={fixture.B} new={fixture.D} tag=v{GovernedReleaseFixture.SecondReleaseVersion} instruction=update-main-fast-forward\n");

        // The tool proposes; it never mutates. origin/main must be exactly where it was.
        await Assert.That(fixture.ResolveRef("refs/remotes/origin/main")).IsEqualTo(fixture.B);
    }

    [Test]
    public async Task DryRunReVerifiesTheEarlierReleaseAfterTheBranchMovesAndAfterItIsDeleted()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();

        // State one: release 1.1.1 has already advanced the branch past 1.1.0's commit B.
        (int movedCandidate, string movedCandidateOutput) = fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        (int movedTag, string movedTagOutput) = fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);
        byte[] evidenceAfterMove = File.ReadAllBytes(Path.Combine(fixture.FirstReleaseDirectory, "release-evidence.v1.json"));

        // State two: the branch is gone entirely. The same bytes must still be reproducible.
        fixture.DeleteGeneratedManifests(GovernedReleaseFixture.FirstReleaseVersion);
        fixture.DeleteIntegrationBranch();
        (int deletedCandidate, string deletedCandidateOutput) = fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        (int deletedTag, string deletedTagOutput) = fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);
        byte[] evidenceAfterDelete = File.ReadAllBytes(Path.Combine(fixture.FirstReleaseDirectory, "release-evidence.v1.json"));

        await Assert.That(movedCandidate).IsEqualTo(Program.Success).Because(movedCandidateOutput);
        await Assert.That(movedTag).IsEqualTo(Program.Success).Because(movedTagOutput);
        await Assert.That(deletedCandidate).IsEqualTo(Program.Success).Because(deletedCandidateOutput);
        await Assert.That(deletedTag).IsEqualTo(Program.Success).Because(deletedTagOutput);
        await Assert.That(fixture.BranchRefs()).IsEqualTo(string.Empty);
        await Assert.That(evidenceAfterDelete).IsEquivalentTo(evidenceAfterMove);
    }

    [Test]
    public async Task CanonicalEvidenceCarriesNoBranchIdentityIdentityOrProviderMetadata()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();
        fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);

        string candidate = File.ReadAllText(Path.Combine(fixture.FirstReleaseDirectory, "release-candidate.v1.json"));
        string evidence = File.ReadAllText(Path.Combine(fixture.FirstReleaseDirectory, "release-evidence.v1.json"));
        string notes = File.ReadAllText(Path.Combine(fixture.FirstReleaseDirectory, "release-notes.md"));
        using JsonDocument candidateDocument = JsonDocument.Parse(candidate);
        using JsonDocument evidenceDocument = JsonDocument.Parse(evidence);

        foreach (string forbidden in new[] { "refs/heads", "releaseBranchRef", "releaseLineHeadOid", "@example", "github", "provider" })
        {
            await Assert.That(candidate).DoesNotContain(forbidden);
            await Assert.That(evidence).DoesNotContain(forbidden);
        }

        await Assert.That(candidateDocument.RootElement.TryGetProperty("tagObjectId", out _)).IsFalse();
        await Assert.That(evidenceDocument.RootElement.GetProperty("tagName").GetString()).IsEqualTo($"v{GovernedReleaseFixture.FirstReleaseVersion}");
        await Assert.That(notes).StartsWith($"# Release {GovernedReleaseFixture.FirstReleaseVersion}\n");
        await Assert.That(notes).Contains("## Maintainer Summary");
        await Assert.That(notes).Contains("## Complete Commit Range");
    }

    [Test]
    public async Task OrdinaryDevelopmentCommitsProduceNoGeneratedChangelogOutsideAReleaseDirectory()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();

        // Every generated artifact must live under docs/internal/releases/<version>/. Nothing in the
        // repository root, docs/, or CHANGELOG.md may be written by the release flow, so an
        // ordinary push to a development branch cannot produce changelog churn.
        string[] generated = Directory
            .EnumerateFiles(fixture.RepositoryPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(fixture.RepositoryPath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path.EndsWith("release-notes.md", StringComparison.Ordinal) ||
                path.EndsWith("release-context.v1.json", StringComparison.Ordinal) ||
                path.EndsWith("release-candidate.v1.json", StringComparison.Ordinal) ||
                path.EndsWith("release-evidence.v1.json", StringComparison.Ordinal) ||
                path.Equals("CHANGELOG.md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(generated).IsEquivalentTo(new[]
        {
            $"docs/internal/releases/{GovernedReleaseFixture.FirstReleaseVersion}/release-context.v1.json",
            $"docs/internal/releases/{GovernedReleaseFixture.FirstReleaseVersion}/release-notes.md",
            $"docs/internal/releases/{GovernedReleaseFixture.SecondReleaseVersion}/release-context.v1.json",
            $"docs/internal/releases/{GovernedReleaseFixture.SecondReleaseVersion}/release-notes.md",
        });
    }
}
