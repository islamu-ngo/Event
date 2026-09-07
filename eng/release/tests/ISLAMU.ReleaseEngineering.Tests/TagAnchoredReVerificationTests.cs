// ABOUTME: Proves a governed release re-verifies from its tag alone after its line branch moves or disappears.
// ABOUTME: Pins the durability property while keeping every immutable-object failure closed.

using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// Release identity is the annotated tag object, the preparation commit it points at, the tree at that
/// commit, and ancestry from the base tag. None of those can be moved after the fact. A branch can.
/// These specifications therefore assert that attestation keeps working in exactly the situations a
/// branch-anchored implementation breaks: a later release advancing the branch, the branch being
/// deleted, and a consumer who fetched nothing but the tag.
/// </summary>
[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class TagAnchoredReVerificationTests
{
    [Test]
    public async Task ReleaseReVerifiesAfterTheNextReleaseAdvancesTheLineBranch()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();

        // Construction already published 1.1.1, so the line branch now points at D, not at 1.1.0's B.
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B);
        (int tagCode, string tagOutput) = fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject);

        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
        await Assert.That(candidateOutput).IsEqualTo("release_candidate_verified: docs/internal/releases/1.1.0/release-candidate.v1.json\n");
        await Assert.That(tagOutput).IsEqualTo("release_tag_verified: docs/internal/releases/1.1.0/release-evidence.v1.json\n");
    }

    [Test]
    public async Task ReleaseReVerifiesAfterTheLineBranchIsDeleted()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();
        fixture.DeleteIntegrationBranch();

        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B);
        (int tagCode, string tagOutput) = fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject);

        await Assert.That(fixture.BranchRefs()).IsEqualTo(string.Empty);
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
    }

    [Test]
    public async Task ReleaseReVerifiesInATagOnlyCloneThatNeverHadTheLineBranch()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();
        string clone = fixture.CreateTagOnlyClone("v1.1.0");

        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B, clone);
        (int tagCode, string tagOutput) = fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject, clone);

        await Assert.That(fixture.BranchRefs(clone)).IsEqualTo(string.Empty);
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
    }

    [Test]
    public async Task ReleaseReVerifiesInSha256RepositoriesAfterBranchMovementAndDeletion()
    {
        if (OperatingSystem.IsWindows()) return;

        using GovernedReleaseFixture? fixture = GovernedReleaseFixture.CreateSha256OrNull();
        if (fixture is null) return;

        (int movedCandidateCode, string movedCandidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B);
        (int movedTagCode, string movedTagOutput) = fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject);

        string clone = fixture.CreateTagOnlyClone("v1.1.0");
        fixture.DeleteIntegrationBranch();
        (int deletedCandidateCode, string deletedCandidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B);
        (int cloneCandidateCode, string cloneCandidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B, clone);
        (int cloneTagCode, string cloneTagOutput) = fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject, clone);

        await Assert.That(fixture.B.Length).IsEqualTo(64);
        await Assert.That(movedCandidateCode).IsEqualTo(Program.Success).Because(movedCandidateOutput);
        await Assert.That(movedTagCode).IsEqualTo(Program.Success).Because(movedTagOutput);
        await Assert.That(deletedCandidateCode).IsEqualTo(Program.Success).Because(deletedCandidateOutput);
        await Assert.That(cloneCandidateCode).IsEqualTo(Program.Success).Because(cloneCandidateOutput);
        await Assert.That(cloneTagCode).IsEqualTo(Program.Success).Because(cloneTagOutput);
    }

    [Test]
    public async Task TagIdentityFailuresStayClosedWhenNoLineBranchExists()
    {
        if (OperatingSystem.IsWindows()) return;

        using var wrongTarget = GovernedReleaseFixture.CreateSha1();
        wrongTarget.DeleteIntegrationBranch();
        wrongTarget.VerifyCandidate("1.1.0", wrongTarget.B);
        wrongTarget.DeleteTag("v1.1.0");
        string wrongTargetObject = wrongTarget.CreateSignedTag("v1.1.0", wrongTarget.A, wrongTarget.GenerateTagMessage("1.1.0"));
        (int wrongTargetCode, string wrongTargetOutput) = wrongTarget.VerifyTag("1.1.0", wrongTarget.B, wrongTargetObject);

        using var unsigned = GovernedReleaseFixture.CreateSha1();
        unsigned.DeleteIntegrationBranch();
        unsigned.VerifyCandidate("1.1.0", unsigned.B);
        unsigned.DeleteTag("v1.1.0");
        string unsignedObject = unsigned.CreateUnsignedAnnotatedTag("v1.1.0", unsigned.B, unsigned.GenerateTagMessage("1.1.0"));
        (int unsignedCode, string unsignedOutput) = unsigned.VerifyTag("1.1.0", unsigned.B, unsignedObject);

        using var recreated = GovernedReleaseFixture.CreateSha1();
        recreated.DeleteIntegrationBranch();
        recreated.VerifyCandidate("1.1.0", recreated.B);
        recreated.VerifyTag("1.1.0", recreated.B, recreated.FirstTagObject);
        recreated.DeleteTag("v1.1.0");
        Thread.Sleep(TimeSpan.FromMilliseconds(1100));
        string recreatedObject = recreated.CreateSignedTag("v1.1.0", recreated.B, recreated.GenerateTagMessage("1.1.0"));
        (int recreatedCode, string recreatedOutput) = recreated.VerifyTag("1.1.0", recreated.B, recreatedObject);

        await Assert.That(wrongTargetCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongTargetOutput).IsEqualTo("verify_tag_failed: release_tag_wrong_target\n");
        await Assert.That(unsignedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unsignedOutput).IsEqualTo("verify_tag_failed: release_tag_signature_invalid\n");
        await Assert.That(recreatedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(recreatedOutput).IsEqualTo("verify_tag_failed: release_tag_object_recreated\n");
    }

    [Test]
    public async Task ArtifactAndTerminalCommitFailuresStayClosedWhenNoLineBranchExists()
    {
        if (OperatingSystem.IsWindows()) return;

        using var noteDrift = GovernedReleaseFixture.CreateSha1();
        noteDrift.DeleteIntegrationBranch();
        File.AppendAllText(Path.Combine(noteDrift.FirstReleaseDirectory, "release-notes.md"), "manual drift\n");
        (int noteCandidateCode, string noteCandidateOutput) = noteDrift.VerifyCandidate("1.1.0", noteDrift.B);

        using var tagNoteDrift = GovernedReleaseFixture.CreateSha1();
        tagNoteDrift.DeleteIntegrationBranch();
        tagNoteDrift.VerifyCandidate("1.1.0", tagNoteDrift.B);
        File.AppendAllText(Path.Combine(tagNoteDrift.FirstReleaseDirectory, "release-notes.md"), "manual drift\n");
        (int tagNoteCode, string tagNoteOutput) = tagNoteDrift.VerifyTag("1.1.0", tagNoteDrift.B, tagNoteDrift.FirstTagObject);

        using var contextDrift = GovernedReleaseFixture.CreateSha1();
        contextDrift.DeleteIntegrationBranch();
        File.AppendAllText(Path.Combine(contextDrift.FirstReleaseDirectory, "release-context.v1.json"), "\n");
        (int contextCode, string contextOutput) = contextDrift.VerifyCandidate("1.1.0", contextDrift.B);

        using var terminal = GovernedReleaseFixture.CreateSha1();
        terminal.DeleteIntegrationBranch();
        (int terminalCode, string terminalOutput) = terminal.VerifyCandidate("1.1.0", terminal.C);

        await Assert.That(noteCandidateCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(noteCandidateOutput).IsEqualTo("verify_candidate_failed: candidate_generated_artifacts_dirty\n");
        await Assert.That(tagNoteCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(tagNoteOutput).IsEqualTo("verify_tag_failed: release_notes_hash_mismatch\n");
        await Assert.That(contextCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(contextOutput).IsEqualTo("verify_candidate_failed: candidate_generated_artifacts_dirty\n");
        await Assert.That(terminalCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(terminalOutput).IsEqualTo("verify_candidate_failed: candidate_terminal_commit_not_release_metadata_skip\n");
    }

    [Test]
    public async Task RangeTopologyFailuresStayClosedWhenNoLineBranchExists()
    {
        if (OperatingSystem.IsWindows()) return;

        using var nonLinear = GovernedReleaseFixture.CreateSha1();
        string unrelated = nonLinear.CreateUnrelatedCommit();
        string merge = nonLinear.CreateMergeCommitOnTopOfHead(unrelated);
        nonLinear.DeleteIntegrationBranch();
        GitReleaseValidationResult nonLinearResult = Validate(nonLinear, "1.1.2", "refs/tags/v1.1.0", "refs/tags/v1.1.0", merge);

        using var nonAncestor = GovernedReleaseFixture.CreateSha1();
        string offLine = nonAncestor.CreateUnrelatedCommit();
        nonAncestor.DeleteTag("v1.0.0");
        nonAncestor.CreateUnsignedAnnotatedTag("v1.0.0", offLine, "v1.0.0\n");
        nonAncestor.DeleteIntegrationBranch();
        GitReleaseValidationResult nonAncestorResult = Validate(nonAncestor, "1.1.0", "refs/tags/v1.0.0", "refs/tags/v1.0.0", nonAncestor.B);

        await Assert.That(nonLinearResult.IsValid).IsFalse();
        await Assert.That(nonLinearResult.Diagnostics).Contains("git_non_linear_candidate");
        await Assert.That(nonAncestorResult.IsValid).IsFalse();
        await Assert.That(nonAncestorResult.Diagnostics).Contains("git_base_not_ancestor");
    }

    [Test]
    public async Task VersionTagGlobIsReservedAgainstBranchesAndNeverResolvedAmbiguously()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();

        // A branch that shadows the version-tag namespace is refused by policy, not disambiguated.
        RefNamespaceDecision shadowing = ReleaseRefNamespacePolicy.EvaluateBranchCreation("refs/heads/v1.1");
        RefNamespaceDecision patchShaped = ReleaseRefNamespacePolicy.EvaluateBranchCreation("refs/heads/v1.1.0");
        RefNamespaceDecision maintenance = ReleaseRefNamespacePolicy.EvaluateBranchCreation("refs/heads/release/1.1");

        // Even when such a branch already exists in a hostile clone, attestation never consults it.
        fixture.CreateBranch("v1.1", fixture.A);
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate("1.1.0", fixture.B);
        (int tagCode, string tagOutput) = fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject);

        await Assert.That(shadowing.IsAllowed).IsFalse();
        await Assert.That(shadowing.Diagnostic).IsEqualTo("ref_namespace_version_tag_glob_reserved");
        await Assert.That(patchShaped.IsAllowed).IsFalse();
        await Assert.That(maintenance.IsAllowed).IsTrue();
        await Assert.That(ReleaseRefNamespacePolicy.IsMaintenanceBranchRef("refs/heads/release/1.1")).IsTrue();
        await Assert.That(ReleaseRefNamespacePolicy.MaintenanceBranchRefForLine("v1.1")).IsEqualTo("refs/heads/release/1.1");
        await Assert.That(ReleaseRefNamespacePolicy.ReservedBranchGlob).IsEqualTo("refs/heads/v*");
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
    }

    private static GitReleaseValidationResult Validate(
        GovernedReleaseFixture fixture,
        string selectedVersion,
        string baseStableRef,
        string previousPublishedRef,
        string candidateOid) =>
        GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            fixture.RepositoryPath,
            "v1.1",
            selectedVersion,
            baseStableRef,
            previousPublishedRef,
            candidateOid));
}
