using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class CommitPolicyTests
{
    private static readonly ReleasePolicy Policy = ReleasePolicy.LoadFromRepositoryRoot(RepositoryRoot.Find());

    [Test]
    public async Task PublicFeatureScopeIsReleaseVisible()
    {
        CommitPolicyResult result = Policy.EvaluateCommit("feat(registration): let attendees correct registration details");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.ScopeKind).IsEqualTo(ScopeKind.Public);
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task EngineeringScopeIsValidButOmittedByDefault()
    {
        CommitPolicyResult result = Policy.EvaluateCommit("ci(release): verify promoted release tooling");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Omitted);
        await Assert.That(result.ScopeKind).IsEqualTo(ScopeKind.Engineering);
    }

    [Test]
    public async Task InternalTypeOnPublicScopeIsOmittedByDefault()
    {
        CommitPolicyResult result = Policy.EvaluateCommit("test(registration): cover attendee correction deadline");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Omitted);
    }

    [Test]
    public async Task BreakingChangeRequiresBangAndFooterTogether()
    {
        CommitPolicyResult valid = Policy.EvaluateCommit(
            "feat(registration)!: simplify attendee check-in credentials\n" +
            "\n" +
            "BREAKING CHANGE: Check-in integrations must send credential after upgrading.");
        CommitPolicyResult missingFooter = Policy.EvaluateCommit("feat(registration)!: simplify attendee check-in credentials");
        CommitPolicyResult missingBang = Policy.EvaluateCommit(
            "feat(registration): simplify attendee check-in credentials\n" +
            "\n" +
            "BREAKING CHANGE: Check-in integrations must send credential after upgrading.");

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(valid.IsBreaking).IsTrue();
        await Assert.That(valid.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(missingFooter.Diagnostics).Contains("breaking_change_requires_bang_and_footer");
        await Assert.That(missingBang.Diagnostics).Contains("breaking_change_requires_bang_and_footer");
    }

    [Test]
    public async Task BreakingFooterCanCoexistWithUnrelatedTerminalTrailer()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration)!: simplify attendee check-in credentials\n" +
            "\n" +
            "BREAKING CHANGE: Check-in integrations must send credential after upgrading.\n" +
            "Refs: #123");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task ChangelogSkipRequiresReasonAndCannotHideBreakingChange()
    {
        CommitPolicyResult missingReason = Policy.EvaluateCommit(
            "chore(release): prepare v1.1.0\n" +
            "\n" +
            "Changelog: skip");
        CommitPolicyResult breakingSkip = Policy.EvaluateCommit(
            "feat(registration)!: simplify attendee check-in credentials\n" +
            "\n" +
            "BREAKING CHANGE: Check-in integrations must send credential after upgrading.\n" +
            "Changelog: skip\n" +
            "Changelog-Reason: release metadata commit");

        await Assert.That(missingReason.Diagnostics).Contains("changelog_skip_requires_reason");
        await Assert.That(breakingSkip.Diagnostics).Contains("breaking_change_cannot_be_skipped");
    }

    [Test]
    public async Task ReleaseMetadataCommitIsValidExplainedSkip()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "chore(release): prepare v1.1.0\n" +
            "\n" +
            "Changelog: skip\n" +
            "Changelog-Reason: release metadata commit");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("release metadata commit");
    }

    [Test]
    public async Task TerminalChangeIdTrailerIsOptionalAndValidated()
    {
        CommitPolicyResult linked = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "Change-Id: CHG-2026-0001");
        CommitPolicyResult malformed = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "Change-Id: 1");
        CommitPolicyResult duplicate = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "Change-Id: CHG-2026-0001\n" +
            "Change-Id: CHG-2026-0002");

        await Assert.That(linked.IsValid).IsTrue();
        await Assert.That(linked.ChangeId).IsEqualTo("CHG-2026-0001");
        await Assert.That(malformed.Diagnostics).Contains("invalid_change_id_trailer");
        await Assert.That(duplicate.Diagnostics).Contains("invalid_change_id_trailer");
    }

    [Test]
    public async Task ChangeIdBeforeBotAttributionStillBindsExactlyOnce()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "Change-Id: CHG-2026-0011\n" +
            "\n" +
            "Generated with the repository automation.\n" +
            "\n" +
            "Co-authored-by: Automation <automation@example.test>");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ChangeId).IsEqualTo("CHG-2026-0011");
    }

    [Test]
    public async Task BodyLookalikeSkipTrailerRemainsInertProse()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "This is body prose, not a terminal trailer block.\n" +
            "Changelog: skip\n" +
            "Changelog-Reason: release metadata commit");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.SkipReason).IsNull();
    }

    [Test]
    public async Task BodyLookalikeBreakingFooterRemainsInertProse()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "The release behavior remains compatible.\n" +
            "BREAKING CHANGE: This is body prose, not a terminal footer block.");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsFalse();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
    }

    [Test]
    public async Task ChangelogSkipMustBeInTerminalTrailerBlock()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "This line makes the changelog lines body text, not a final trailer block.\n" +
            "Changelog: skip\n" +
            "Changelog-Reason: release metadata commit");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.SkipReason).IsNull();
    }

    [Test]
    public async Task UnknownYamlKeysFailClosed()
    {
        string repositoryRoot = RepositoryRoot.Find();
        using var fixture = new PolicyRootFixture(repositoryRoot);

        fixture.AppendPolicyLine("unexpectedPolicyKey: should-fail-closed");
        await Assert.That(() => ReleasePolicy.LoadFromRepositoryRoot(fixture.Root))
            .Throws<InvalidOperationException>();

        using var scopeFixture = new PolicyRootFixture(repositoryRoot);
        scopeFixture.AppendScopeLine("unexpectedScopeKey: should-fail-closed");
        await Assert.That(() => ReleasePolicy.LoadFromRepositoryRoot(scopeFixture.Root))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnknownTypeAndScopeFailDeterministically()
    {
        CommitPolicyResult unknownType = Policy.EvaluateCommit("ship(registration): publish registration policy");
        CommitPolicyResult unknownScope = Policy.EvaluateCommit("feat(api): publish registration policy");

        await Assert.That(unknownType.Diagnostics).IsEquivalentTo(["unknown_type"]);
        await Assert.That(unknownScope.Diagnostics).IsEquivalentTo(["unknown_scope"]);
    }

    [Test]
    public async Task ContradictoryTrailersFailDeterministically()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration): let attendees correct registration details\n" +
            "\n" +
            "Changelog-Reason: release metadata commit");

        await Assert.That(result.Diagnostics).IsEquivalentTo(["changelog_reason_without_skip"]);
    }

    [Test]
    public async Task MalformedAndNonConventionalRevertCasesFailDeterministically()
    {
        CommitPolicyResult malformed = Policy.EvaluateCommit("feat registration: missing scope punctuation");
        CommitPolicyResult nonConventionalRevert = Policy.EvaluateCommit("Revert \"feat(registration): let attendees correct registration details\"");

        await Assert.That(malformed.Diagnostics).IsEquivalentTo(["malformed_header"]);
        await Assert.That(nonConventionalRevert.Diagnostics).IsEquivalentTo(["malformed_header"]);
    }

    [Test]
    public async Task ConventionalRevertIsReleaseVisible()
    {
        CommitPolicyResult result = Policy.EvaluateCommit("revert(registration): restore the original attendee correction flow");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.Type).IsEqualTo("revert");
    }

    [Test]
    public async Task BreakingFooterRemainsEffectiveWithAnAdjacentReferenceTrailer()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(events)!: require organizers to reconfigure check-in\n" +
            "\n" +
            "BREAKING CHANGE: Update check-in configuration before upgrading.\n" +
            "Refs: #42");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
    }

    [Test]
    public async Task ExplainedSkipRemainsEffectiveWithReferenceTrailersInEitherOrder()
    {
        foreach (string trailers in new[]
        {
            "Changelog: skip\nChangelog-Reason: release metadata\nRefs: #42",
            "Refs: #42\nChangelog: skip\nChangelog-Reason: release metadata",
        })
        {
            CommitPolicyResult result = Policy.EvaluateCommit("chore(release): prepare v1.1.0\n\n" + trailers);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Skipped);
            await Assert.That(result.SkipReason).IsEqualTo("release metadata");
        }
    }

    [Test]
    public async Task ExplainedSkipRemainsEffectiveWithCoAuthorTrailer()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "chore(release): prepare v1.1.0\n" +
            "\n" +
            "Changelog: skip\n" +
            "Changelog-Reason: release metadata\n" +
            "Co-authored-by: Release Maintainer <release@example.test>");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("release metadata");
    }

    [Test]
    public async Task ColonBearingBodyProseCannotRequestAChangelogSkip()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(events): retain visible event notes\n" +
            "\n" +
            "This is prose: not a trailer\n" +
            "Changelog: skip");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.SkipReason).IsNull();
    }

    [Test]
    public async Task ColonBearingBodyProseCannotSupplyABreakingFooter()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(events)!: retain visible event notes\n" +
            "\n" +
            "This is prose: not a trailer\n" +
            "BREAKING CHANGE: This is body prose.");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.IsBreaking).IsFalse();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
        await Assert.That(result.Diagnostics).Contains("breaking_change_requires_bang_and_footer");
    }

    [Test]
    public async Task WrappedBreakingFooterRemainsEffective()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(registration)!: simplify attendee check-in credentials\n" +
            "\n" +
            "BREAKING CHANGE: Check-in integrations must send `credential` instead of\n" +
            "`ticketCode` after upgrading.");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
    }

    [Test]
    public async Task ExplainedSkipRetainsMetadataWithFinalLfOrCrlf()
    {
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            CommitPolicyResult result = Policy.EvaluateCommit(
                "chore(release): prepare v1.1.0" + newline +
                newline +
                "Changelog: skip" + newline +
                "Changelog-Reason: release metadata" + newline);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Skipped);
            await Assert.That(result.SkipReason).IsEqualTo("release metadata");
        }
    }

    [Test]
    public async Task ExplainedSkipRetainsMetadataWithMultipleFinalNewlines()
    {
        foreach (string suffix in new[] { "\n\n", "\n\n\n", "\r\n\r\n" })
        {
            CommitPolicyResult result = Policy.EvaluateCommit(
                "chore(release): prepare v1.1.0\n" +
                "\n" +
                "Changelog: skip\n" +
                "Changelog-Reason: release metadata" + suffix);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Skipped);
            await Assert.That(result.SkipReason).IsEqualTo("release metadata");
        }
    }

    [Test]
    public async Task BreakingFooterRetainsMetadataWithFinalLf()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(events)!: require organizers to reconfigure check-in\n" +
            "\n" +
            "BREAKING CHANGE: Update check-in configuration before upgrading.\n");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
    }

    [Test]
    public async Task BreakingFooterRetainsMetadataWithMultipleFinalLfs()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(events)!: require organizers to reconfigure check-in\n" +
            "\n" +
            "BREAKING CHANGE: Update check-in configuration before upgrading.\n" +
            "\n");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
    }

    [Test]
    public async Task OrdinaryUnindentedBreakingContinuationRemainsEffective()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(
            "feat(events)!: require organizers to reconfigure check-in\n" +
            "\n" +
            "BREAKING CHANGE: Update check-in configuration before\n" +
            "upgrading deployed check-in clients.");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.IsBreaking).IsTrue();
        await Assert.That(result.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Visible);
    }

    [Test]
    public async Task OversizedUntrustedInputReturnsBoundedDiagnostic()
    {
        CommitPolicyResult result = Policy.EvaluateCommit(new string('x', 12_000));

        await Assert.That(result.Diagnostics).IsEquivalentTo(["commit_message_too_long"]);
    }

    [Test]
    public async Task OversizedSubjectAndTrailerValuesFailClosed()
    {
        CommitPolicyResult subject = Policy.EvaluateCommit($"feat(events): {new string('x', 260)}");
        CommitPolicyResult trailer = Policy.EvaluateCommit(
            "chore(release): prepare v1.1.0\n" +
            "\n" +
            "Changelog: skip\n" +
            $"Changelog-Reason: {new string('x', 2_100)}");

        await Assert.That(subject.Diagnostics).Contains("commit_subject_too_long");
        await Assert.That(trailer.Diagnostics).Contains("commit_trailer_value_too_long:Changelog-Reason");
    }
}

internal static class RepositoryRoot
{
    public static string Find()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "eng", "release", "toolchain.lock.json")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

internal sealed class PolicyRootFixture : IDisposable
{
    public PolicyRootFixture(string repositoryRoot)
    {
        Root = Path.Combine(Path.GetTempPath(), $"islamu-release-policy-{Guid.NewGuid():N}");
        string sourcePolicyDirectory = Path.Combine(repositoryRoot, "eng", "release", "policy");
        string targetPolicyDirectory = Path.Combine(Root, "eng", "release", "policy");
        Directory.CreateDirectory(targetPolicyDirectory);
        File.Copy(Path.Combine(sourcePolicyDirectory, "release-policy.yaml"), PolicyPath);
        File.Copy(Path.Combine(sourcePolicyDirectory, "scope-registry.yaml"), ScopePath);
    }

    public string Root { get; }
    private string PolicyPath => Path.Combine(Root, "eng", "release", "policy", "release-policy.yaml");
    private string ScopePath => Path.Combine(Root, "eng", "release", "policy", "scope-registry.yaml");

    public void AppendPolicyLine(string line) => File.AppendAllText(PolicyPath, Environment.NewLine + line + Environment.NewLine);
    public void AppendScopeLine(string line) => File.AppendAllText(ScopePath, Environment.NewLine + line + Environment.NewLine);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
