// ABOUTME: Proves governed release-context policy for versions, prereleases, backports, and display IDs.
// ABOUTME: Compares deterministic release-context.v1.json against checked-in golden fixtures.

using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseContextPolicyTests
{
    private static readonly ReleasePolicy Policy = ReleasePolicy.LoadFromRepositoryRoot(RepositoryRoot.Find());

    [Test]
    public async Task StableContextMatchesGoldenAndSerializesRepeatably()
    {
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(ReleaseYaml("1.1.0", "v1.1", "v1.0.0", "v1.0.0", FullOid('a'), FullOid('b')), [], []);
        ReleaseCommit[] commits = [new(FullOid('c'), "feat(registration): let attendees correct registration details")];

        ReleaseContextValidationResult first = ReleaseContextPolicy.Build(input, commits, Policy);
        ReleaseContextValidationResult second = ReleaseContextPolicy.Build(input, commits, Policy);

        await Assert.That(first.IsValid).IsTrue();
        await Assert.That(first.Json).IsEqualTo(Golden("stable-release-context.v1.json"));
        await Assert.That(second.Json).IsEqualTo(first.Json);
    }

    [Test]
    public async Task FirstGovernedReleaseCanUseVerifiedBaselineLowerBoundWithoutSemVerHistory()
    {
        const string baselineRef = "changelog-baseline-2026-08-15";
        ReleaseInputValidationResult preOne = ReleaseInputPolicy.Validate(ReleaseYaml("0.1.0", "v0.1", baselineRef, baselineRef, FullOid('a'), FullOid('a')), [], []);
        ReleaseInputValidationResult laterSemVer = ReleaseInputPolicy.Validate(ReleaseYaml("2.0.0", "v2.0", baselineRef, baselineRef, FullOid('a'), FullOid('a')), [], []);
        ReleaseCommit[] commits = [new(FullOid('c'), "feat(events): publish first governed release notes")];

        ReleaseContextValidationResult preOneResult = ReleaseContextPolicy.Build(preOne, commits, Policy, verifiedBaselineRef: baselineRef, verifiedBaselineOid: FullOid('a'));
        ReleaseContextValidationResult laterResult = ReleaseContextPolicy.Build(laterSemVer, commits, Policy, verifiedBaselineRef: baselineRef, verifiedBaselineOid: FullOid('a'));

        await Assert.That(preOneResult.IsValid).IsTrue();
        await Assert.That(preOneResult.Context?.Release.Version).IsEqualTo("0.1.0");
        await Assert.That(preOneResult.Context?.Release.BaseStableTag).IsEqualTo(baselineRef);
        await Assert.That(preOneResult.Context?.Release.PreviousPublishedTag).IsEqualTo(baselineRef);
        await Assert.That(preOneResult.Context?.Evidence.BaseStableOid).IsEqualTo(FullOid('a'));
        await Assert.That(preOneResult.Context?.Evidence.PreviousPublishedOid).IsEqualTo(FullOid('a'));
        await Assert.That(laterResult.IsValid).IsTrue();
        await Assert.That(laterResult.Context?.Release.Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task BaselineLowerBoundRequiresExplicitVerifiedEvidence()
    {
        const string baselineRef = "changelog-baseline-2026-08-15";
        ReleaseInputValidationResult firstInput = ReleaseInputPolicy.Validate(ReleaseYaml("0.1.0", "v0.1", baselineRef, baselineRef, FullOid('a'), FullOid('a')), [], []);

        ReleaseContextValidationResult missingEvidence = ReleaseContextPolicy.Build(firstInput, [], Policy);
        ReleaseContextValidationResult wrongEvidence = ReleaseContextPolicy.Build(firstInput, [], Policy, verifiedBaselineRef: baselineRef, verifiedBaselineOid: FullOid('b'));

        await Assert.That(missingEvidence.Diagnostics).Contains("context_baseline_evidence_required");
        await Assert.That(wrongEvidence.Diagnostics).Contains("context_baseline_evidence_mismatch");
    }

    [Test]
    public async Task PrereleaseContextIsCumulativeFromStableAndDoesNotAdvanceMain()
    {
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(ReleaseYaml("1.2.0-beta.2", "v1.2", "v1.1.0", "v1.2.0-beta.1", FullOid('d'), FullOid('e')), [], []);
        ReleaseCommit[] commits = [new(FullOid('f'), "feat(discovery): show richer event filters")];

        ReleaseContextValidationResult result = ReleaseContextPolicy.Build(input, commits, Policy);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Context?.Release.AdvancesMain).IsFalse();
        await Assert.That(result.Json).IsEqualTo(Golden("prerelease-release-context.v1.json"));
    }

    [Test]
    public async Task FragmentLinkedVisibleCommitYieldsOneEnrichedBackportChange()
    {
        string currentOid = FullOid('3');
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(
            ReleaseYaml("1.1.1", "v1.1", "v1.1.0", "v1.1.0", FullOid('1'), FullOid('2')),
            [FragmentYaml()],
            []);

        ReleaseContextValidationResult result = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(currentOid, "fix(registration): backport attendee credential migration\n\nChange-Id: CHG-2026-0001")],
            Policy);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Context?.Release.MinimumBump).IsEqualTo("patch");
        await Assert.That(result.Context?.Changes).Count().IsEqualTo(1);
        await Assert.That(result.Context?.Changes.Single().Oid).IsEqualTo(currentOid);
        await Assert.That(result.Context?.Changes.Single().ChangeId).IsEqualTo("CHG-2026-0001");
        await Assert.That(result.Context?.Changes.Single().BackportOf).IsEqualTo("0123456789abcdef0123456789abcdef01234567");
        await Assert.That(result.Context?.Changes.Single().Title).StartsWith("Backport: ");
        await Assert.That(result.Context?.Evidence.Objects.Select(item => item.Oid)).Contains(currentOid);
        await Assert.That(result.Context?.Evidence.Objects.Select(item => item.Oid)).Contains("0123456789abcdef0123456789abcdef01234567");
        await Assert.That(result.Context?.Evidence.Objects.Select(item => item.Oid)).DoesNotContain("863a3bec89cb5741068dd942803c3989078910701dcba1902bf24ba11b35567d");
        await Assert.That(result.Json).IsEqualTo(Golden("backport-release-context.v1.json"));
    }

    [Test]
    public async Task FragmentChangeIdLinksAreRequiredAndUnique()
    {
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(
            ReleaseYaml("1.1.1", "v1.1", "v1.1.0", "v1.1.0", FullOid('1'), FullOid('2')),
            [FragmentYaml()],
            []);
        ReleaseContextValidationResult missing = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(FullOid('3'), "fix(registration): backport attendee credential migration")],
            Policy);
        ReleaseContextValidationResult duplicate = ReleaseContextPolicy.Build(
            input,
            [
                new ReleaseCommit(FullOid('3'), "fix(registration): backport attendee credential migration\n\nChange-Id: CHG-2026-0001"),
                new ReleaseCommit(FullOid('4'), "fix(registration): adjust attendee credential migration\n\nChange-Id: CHG-2026-0001"),
            ],
            Policy);

        await Assert.That(missing.Diagnostics).Contains("context_fragment_missing_commit_link:CHG-2026-0001");
        await Assert.That(duplicate.Diagnostics).Contains("context_fragment_duplicate_commit_link:CHG-2026-0001");
    }

    [Test]
    public async Task PreOneAndPostOneBreakingBumpRulesAreExplicit()
    {
        string breaking = "feat(registration)!: replace attendee credentials\n\nBREAKING CHANGE: Regenerate check-in clients.";
        ReleaseContextValidationResult preOne = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml("0.3.0", "v0.3", "v0.2.0", "v0.2.0", FullOid('a'), FullOid('b')), [], []),
            [new ReleaseCommit(FullOid('c'), breaking)],
            Policy);
        ReleaseContextValidationResult postOneTooLow = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml("1.3.0", "v1.3", "v1.2.0", "v1.2.0", FullOid('d'), FullOid('e')), [], []),
            [new ReleaseCommit(FullOid('f'), breaking)],
            Policy);

        await Assert.That(preOne.IsValid).IsTrue();
        await Assert.That(preOne.Context?.Release.MinimumBump).IsEqualTo("minor");
        await Assert.That(postOneTooLow.Diagnostics).Contains("context_selected_version_below_minimum_bump");
    }

    [Test]
    public async Task MalformedVersionLinePrereleaseAndDisplayCollisionsFailClosed()
    {
        ReleaseContextValidationResult buildMetadata = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml("1.1.0+build.7", "v1.1", "v1.0.0", "v1.0.0", FullOid('a'), FullOid('b')), [], []),
            [],
            Policy);
        ReleaseContextValidationResult skippedCounter = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml("1.2.0-rc.3", "v1.2", "v1.1.0", "v1.2.0-rc.1", FullOid('c'), FullOid('d')), [], []),
            [new ReleaseCommit(FullOid('e'), "feat(discovery): show richer event filters")],
            Policy);
        ReleaseContextValidationResult wrongLine = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml("1.1.0", "v1.0", "v1.0.0", "v1.0.0", FullOid('f'), FullOid('a')), [], []),
            [],
            Policy);
        ReleaseContextValidationResult collision = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml("1.1.0", "v1.1", "v1.0.0", "v1.0.0", FullOid('0'), FullOid('1')), [], []),
            [new ReleaseCommit(FullOid('0') + new string('c', 24), "fix(events): restore published event notes")],
            Policy);

        await Assert.That(buildMetadata.Diagnostics).Contains("release_malformed_version");
        await Assert.That(skippedCounter.Diagnostics).Contains("context_prerelease_counter_not_contiguous");
        await Assert.That(wrongLine.Diagnostics).Contains("release_line_version_mismatch");
        await Assert.That(collision.Diagnostics).Contains($"context_display_id_collision:{FullOid('0')}");
    }

    [Test]
    public async Task MixedObjectFormatsExtendDisplayIdsUntilTheyAreUnique()
    {
        const string sharedPrefix = "123456789abc";
        string sha1 = sharedPrefix + "0" + new string('a', 27);
        string sha256 = sharedPrefix + "1" + new string('b', 51);
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(
            ReleaseYaml("1.1.0", "v1.1", "v1.0.0", "v1.0.0", FullOid('a'), FullOid('b')),
            [],
            []);

        ReleaseContextValidationResult result = ReleaseContextPolicy.Build(
            input,
            [
                new ReleaseCommit(sha1, "fix(events): restore published event notes"),
                new ReleaseCommit(sha256, "fix(events): preserve event note ordering"),
            ],
            Policy);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Context?.Changes.Select(change => change.DisplayId)).IsEquivalentTo([sharedPrefix + "0", sharedPrefix + "1"]);
        await Assert.That(result.Context?.Evidence.Objects.Select(item => item.Oid)).Contains(sha1);
        await Assert.That(result.Context?.Evidence.Objects.Select(item => item.Oid)).Contains(sha256);
    }

    [Test]
    public async Task CanonicalContextRejectsIdentityBearingSubjectsAndOmitsRawBodies()
    {
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(ReleaseYaml("1.0.1", "v1.0", "v1.0.0", "v1.0.0", FullOid('a'), FullOid('b')), [], []);
        ReleaseContextValidationResult emailSubject = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(FullOid('c'), "fix(events): contact maintainer@example.org")],
            Policy);
        ReleaseContextValidationResult providerSubject = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(FullOid('d'), "fix(events): preserve https://github.com/example/repository links")],
            Policy);
        ReleaseContextValidationResult rawBody = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(FullOid('e'), "fix(events): restore published event notes\n\nAuthor: maintainer@example.org\nSource: https://github.com/example/repository")],
            Policy);

        await Assert.That(emailSubject.Diagnostics).Contains($"context_identity_or_provider_data:{FullOid('c')}");
        await Assert.That(providerSubject.Diagnostics).Contains($"context_identity_or_provider_data:{FullOid('d')}");
        await Assert.That(rawBody.IsValid).IsTrue();
        await Assert.That(rawBody.Json).DoesNotContain("maintainer@example.org");
        await Assert.That(rawBody.Json).DoesNotContain("github.com");
    }

    [Test]
    public async Task GitCliffBumpDisagreementIsReviewFailureEvidenceOnly()
    {
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(ReleaseYaml("1.1.0", "v1.1", "v1.0.0", "v1.0.0", FullOid('a'), FullOid('b')), [], []);

        ReleaseContextValidationResult result = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(FullOid('c'), "feat(registration): let attendees correct registration details")],
            Policy,
            gitCliffSuggestedVersion: "1.0.1");

        await Assert.That(result.Diagnostics).Contains("context_git_cliff_bump_disagreement");
    }

    [Test]
    public async Task OversizedSemanticVersionComponentsFailClosed()
    {
        string oversized = new('9', 100);
        ReleaseContextValidationResult result = ReleaseContextPolicy.Build(
            ReleaseInputPolicy.Validate(ReleaseYaml($"{oversized}.1.0", "v1.0", "v1.0.0", "v1.0.0", FullOid('a'), FullOid('b')), [], []),
            [],
            Policy);

        await Assert.That(result.Diagnostics).Contains("context_malformed_version");
    }

    private static string Golden(string fileName) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "eng", "release", "tests", "ISLAMU.ReleaseEngineering.Tests", "Fixtures", fileName));

    private static string FullOid(char value) => new(value, 40);

    private static string ReleaseYaml(string version, string line, string baseTag, string previousTag, string baseOid, string previousOid) =>
        $$"""
        Version: {{version}}
        Line: {{line}}
        Release-Date: 2026-08-14
        Base-Stable-Tag: {{baseTag}}
        Previous-Published-Tag: {{previousTag}}
        Release-Range:
          Base-Ref: {{baseTag}}
          Base-Oid: {{baseOid}}
          Previous-Ref: {{previousTag}}
          Previous-Oid: {{previousOid}}
        Compatibility:
          - v1
        Impact-Dispositions:
          breaking: not-applicable
          security: coordinated
          migration: planned
          configuration: not-applicable
          openapi: documented
          operator: documented
        """;

    private static string FragmentYaml() =>
        """
        Change-Id: CHG-2026-0001
        Title: Attendee credential migration
        Type: feat
        Scope: registration
        Summary: Attendees use a single credential during check-in.
        Group: registration-upgrade
        Backport-Of: 0123456789abcdef0123456789abcdef01234567
        Supersedes: []
        Impacts:
          Breaking:
            Reference: docs/internal/releases/README.md
            Disposition: not-applicable
          Security:
            Reference: SECURITY-POLICY.md
            Disposition: coordinated
            Public-Disclosure: coordinated
          Migration:
            Reference: docs/RELEASE_RUNBOOK.md
            Disposition: planned
          Configuration:
            Reference: docs/CONFIGURATION.md
            Disposition: not-applicable
          OpenAPI:
            Reference: docs/API_CHANGELOG.md
            Disposition: documented
          Operator:
            Reference: docs/RELEASE_CHECKLIST.md
            Disposition: documented
        """;
}
