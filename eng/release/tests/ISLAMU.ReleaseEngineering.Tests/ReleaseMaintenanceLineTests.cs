using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// Decision 11: nothing is provisioned at release time, and a maintenance line is opened only when a
/// real backport needs somewhere to accumulate commits. These specifications pin the properties that
/// make that safe — the source is always the verified release tag, re-running never force-updates,
/// the branch can never land in the reserved version-tag namespace, and deleting it afterwards
/// leaves every release on the line fully verifiable.
/// </summary>
[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleaseMaintenanceLineTests
{
    [Test]
    public async Task OpeningALineFromAVerifiedStableTagPlansTheExactOperatorCommandWithoutMutating()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();
        fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);
        fixture.DeleteIntegrationBranch();
        string refsBefore = fixture.AllRefs();

        (int exitCode, string output) = fixture.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, fixture.FirstTagObject);

        await Assert.That(exitCode).IsEqualTo(Program.Success).Because(output);
        await Assert.That(output).IsEqualTo(
            $"maintenance_line_verified: action=create-maintenance-line branch=refs/heads/release/1.1 source-tag=v1.1.0 expected-old=none expected-new={fixture.B} instruction=git switch -c release/1.1 v1.1.0\n");
        await Assert.That(fixture.AllRefs()).IsEqualTo(refsBefore);
    }

    [Test]
    public async Task ReRunningAgainstAnExistingLineIsANoOpAndNeverForceUpdates()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();
        fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);

        // The operator opens the line, then adds a backport commit on top of it.
        fixture.CreateBranch("release/1.1", fixture.B);
        string refsBefore = fixture.AllRefs();

        (int firstCode, string firstOutput) = fixture.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, fixture.FirstTagObject);
        (int secondCode, string secondOutput) = fixture.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, fixture.FirstTagObject);

        await Assert.That(firstCode).IsEqualTo(Program.Success).Because(firstOutput);
        await Assert.That(secondCode).IsEqualTo(Program.Success).Because(secondOutput);
        await Assert.That(firstOutput).Contains("action=already-open");
        await Assert.That(firstOutput).Contains("instruction=no-op-maintenance-line-already-open");
        await Assert.That(firstOutput).Contains($"expected-old={fixture.B} expected-new={fixture.B}");
        await Assert.That(secondOutput).IsEqualTo(firstOutput);
        await Assert.That(fixture.AllRefs()).IsEqualTo(refsBefore);
    }

    [Test]
    public async Task ALineThatDoesNotContainTheReleasedCommitIsRejected()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();

        // A branch cut from unrelated integration work rather than from the release tag: it carries
        // commits that were never in v1.1.0, so a patch built on it would ship unreviewed work.
        string unrelated = fixture.CreateUnrelatedCommit();
        fixture.CreateBranch("release/1.1", unrelated);
        fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);

        (int exitCode, string output) = fixture.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, fixture.FirstTagObject);

        await Assert.That(exitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("open_maintenance_line_failed: maintenance_line_source_not_release_tag\n");
    }

    [Test]
    public async Task UnverifiedTagsMismatchedTagObjectsAndPrereleasesFailClosed()
    {
        if (OperatingSystem.IsWindows()) return;

        using var mismatched = GovernedReleaseFixture.CreateSha1();
        mismatched.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, mismatched.B);
        mismatched.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, mismatched.B, mismatched.FirstTagObject);
        (int mismatchCode, string mismatchOutput) = mismatched.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, mismatched.SecondTagObject);

        using var unverified = GovernedReleaseFixture.CreateSha1();
        unverified.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, unverified.B);
        unverified.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, unverified.B, unverified.FirstTagObject);
        File.AppendAllText(Path.Combine(unverified.FirstReleaseDirectory, "release-notes.md"), "drift\n");
        (int unverifiedCode, string unverifiedOutput) = unverified.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, unverified.FirstTagObject);

        using var missing = GovernedReleaseFixture.CreateSha1();
        (int missingCode, string missingOutput) = missing.OpenMaintenanceLine(GovernedReleaseFixture.FirstReleaseVersion, missing.FirstTagObject);

        await Assert.That(mismatchCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(mismatchOutput).IsEqualTo("open_maintenance_line_failed: maintenance_line_tag_object_mismatch\n");
        await Assert.That(unverifiedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unverifiedOutput).IsEqualTo("open_maintenance_line_failed: maintenance_line_tag_unverified\n");
        await Assert.That(missingCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingOutput).IsEqualTo("open_maintenance_line_failed: maintenance_line_evidence_invalid\n");
    }

    [Test]
    public async Task MaintenanceBranchNamesCanNeverLandInTheReservedVersionTagNamespace()
    {
        // The grammar itself is the guarantee: every line label maps to release/<major>.<minor>,
        // which can never match refs/heads/v*.
        foreach (string line in new[] { "v0.1", "v1.1", "v10.20", "v0.0" })
        {
            string branchRef = ReleaseRefNamespacePolicy.MaintenanceBranchRefForLine(line);
            await Assert.That(ReleaseRefNamespacePolicy.IsReservedBranchRef(branchRef)).IsFalse();
            await Assert.That(ReleaseRefNamespacePolicy.IsMaintenanceBranchRef(branchRef)).IsTrue();
            await Assert.That(ReleaseRefNamespacePolicy.EvaluateBranchCreation(branchRef).IsAllowed).IsTrue();
        }

        await Assert.That(ReleaseRefNamespacePolicy.MaintenanceBranchRefForLine("v1.1")).IsEqualTo("refs/heads/release/1.1");
        await Assert.That(Assert.Throws<ArgumentException>(() => ReleaseRefNamespacePolicy.MaintenanceBranchRefForLine("release/1.1"))).IsNotNull();
    }

    [Test]
    public async Task DeletingAMaintenanceLineLeavesEveryReleaseOnItFullyVerifiable()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = GovernedReleaseFixture.CreateSha1();
        fixture.CreateBranch("release/1.1", fixture.B);
        fixture.DeleteIntegrationBranch();
        fixture.DeleteBranch("release/1.1");

        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(GovernedReleaseFixture.FirstReleaseVersion, fixture.B);
        (int tagCode, string tagOutput) = fixture.VerifyTag(GovernedReleaseFixture.FirstReleaseVersion, fixture.B, fixture.FirstTagObject);

        await Assert.That(fixture.BranchRefs()).IsEqualTo(string.Empty);
        await Assert.That(candidateCode).IsEqualTo(Program.Success).Because(candidateOutput);
        await Assert.That(tagCode).IsEqualTo(Program.Success).Because(tagOutput);
    }
}
