// ABOUTME: Proves strict release descriptor and public change-fragment validation.
// ABOUTME: Covers append-only correction, impact references, embargo guards, and stable diagnostics.

using ISLAMU.ReleaseEngineering;
using System.Globalization;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseInputPolicyTests
{
    private static readonly string[] DuplicateFragmentDiagnostics = ["fragment_duplicate_change_id:CHG-2026-0001"];
    private static readonly string[] IncompatibleGroupDiagnostics = ["fragment_incompatible_group:registration-upgrade"];

    [Test]
    public async Task LowImpactDescriptorCanValidateWithoutFragments()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [],
            []);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Descriptor?.Version).IsEqualTo("1.1.0");
        await Assert.That(result.Fragments).IsEmpty();
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task HighImpactFragmentSuppliesRequiredStructuredReferences()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [ValidFragmentYaml()],
            []);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Fragments).Count().IsEqualTo(1);
        await Assert.That(result.Fragments[0].ChangeId).IsEqualTo("CHG-2026-0001");
    }

    [Test]
    public async Task RepositoryChangeFragmentsPassReleaseInputPolicy()
    {
        string changesDirectory = Path.Combine(FindRepositoryRoot(), "docs", "internal", "releases", "changes");
        string[] fragments = Directory.GetFiles(changesDirectory, "CHG-*.yaml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();

        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            fragments,
            []);

        await Assert.That(result.IsValid).IsTrue()
            .Because(string.Join("; ", result.Diagnostics));
        await Assert.That(result.Fragments).IsNotEmpty();
        await Assert.That(result.Fragments.All(fragment => ChangeIdPolicy.IsGenerated(fragment.ChangeId))).IsTrue();
        await Assert.That(result.Fragments.Select(fragment => fragment.ChangeId)).Contains("CHG-01M1VJ17RD9AT6PNN1X0093958");
        await Assert.That(result.Fragments.Select(fragment => fragment.ChangeId)).Contains("CHG-01M1VJ199BWSA2Y38S8W4GNN64");
    }

    [Test]
    public async Task DuplicateFragmentIdsFailDeterministically()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [ValidFragmentYaml(), ValidFragmentYaml()],
            []);

        await Assert.That(result.Diagnostics).IsEquivalentTo(DuplicateFragmentDiagnostics);
    }

    [Test]
    public async Task MissingRequiredImpactReferencesAndDispositionsFail()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml().Replace("migration: planned", "", StringComparison.Ordinal),
            [ValidFragmentYaml().Replace("    Reference: docs/RELEASE_RUNBOOK.md\n", "", StringComparison.Ordinal)],
            []);

        await Assert.That(result.Diagnostics).Contains("release_missing_impact_disposition:migration");
        await Assert.That(result.Diagnostics).Contains("fragment_missing_impact_reference:CHG-2026-0001:migration");
    }

    [Test]
    public async Task IncompatibleMixedGroupsFailClosed()
    {
        string second = ValidFragmentYaml()
            .Replace("Change-Id: CHG-2026-0001", "Change-Id: CHG-2026-0002", StringComparison.Ordinal)
            .Replace("Group: registration-upgrade", "Group: registration-upgrade", StringComparison.Ordinal)
            .Replace("Scope: registration", "Scope: storage", StringComparison.Ordinal);

        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [ValidFragmentYaml(), second],
            []);

        await Assert.That(result.Diagnostics).IsEquivalentTo(IncompatibleGroupDiagnostics);
    }

    [Test]
    public async Task MalformedIdsAndFullObjectIdsFailClosed()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml().Replace("Base-Stable-Tag: v1.0.0", "Base-Stable-Tag: 1.0.0", StringComparison.Ordinal),
            [ValidFragmentYaml().Replace("Change-Id: CHG-2026-0001", "Change-Id: 1", StringComparison.Ordinal)
                .Replace("Backport-Of: 0123456789abcdef0123456789abcdef01234567", "Backport-Of: 0123456789ab", StringComparison.Ordinal)],
            []);

        await Assert.That(result.Diagnostics).Contains("release_malformed_tag:Base-Stable-Tag");
        await Assert.That(result.Diagnostics).Contains("fragment_malformed_change_id:1");
        await Assert.That(result.Diagnostics).Contains("fragment_malformed_full_oid:1:Backport-Of");
    }

    [Test]
    public async Task UnknownYamlKeysFailClosed()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml() + "\nUnexpected: value\n",
            [ValidFragmentYaml() + "\nUnexpected: value\n"],
            []);

        await Assert.That(result.Diagnostics).Contains("release_unknown_key:Unexpected");
        await Assert.That(result.Diagnostics).Contains("fragment_unknown_key:CHG-2026-0001:Unexpected");
    }

    [Test]
    public async Task FragmentMutationAndDeletionAgainstSnapshotFail()
    {
        string prior = ValidFragmentYaml();
        string mutated = prior.Replace("Title: Attendee credential migration", "Title: Mutated public title", StringComparison.Ordinal);

        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [mutated],
            [prior, prior.Replace("CHG-2026-0001", "CHG-2026-0009", StringComparison.Ordinal)]);

        await Assert.That(result.Diagnostics).Contains("fragment_mutated:CHG-2026-0001");
        await Assert.That(result.Diagnostics).Contains("fragment_deleted:CHG-2026-0009");
    }

    [Test]
    public async Task SupersedesAllowsAppendOnlyCorrectionWithoutMutatingPriorFragment()
    {
        string prior = ValidFragmentYaml();
        string correction = ValidFragmentYaml()
            .Replace("Change-Id: CHG-2026-0001", "Change-Id: CHG-2026-0002", StringComparison.Ordinal)
            .Replace("Supersedes: []", "Supersedes:\n  - CHG-2026-0001", StringComparison.Ordinal);

        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [prior, correction],
            [prior]);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task SupersedesRejectsDanglingSelfAndCyclicRelationships()
    {
        string first = ValidFragmentYaml();
        string dangling = Fragment("CHG-2026-0002", "CHG-2026-9999");
        string self = Fragment("CHG-2026-0003", "CHG-2026-0003");
        string cycleA = Fragment("CHG-2026-0004", "CHG-2026-0005");
        string cycleB = Fragment("CHG-2026-0005", "CHG-2026-0004");

        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [first, dangling, self, cycleA, cycleB],
            []);

        await Assert.That(result.Diagnostics).Contains("fragment_dangling_supersedes:CHG-2026-0002:CHG-2026-9999");
        await Assert.That(result.Diagnostics).Contains("fragment_self_supersedes:CHG-2026-0003");
        await Assert.That(result.Diagnostics).Contains("fragment_supersedes_cycle:CHG-2026-0004");
    }

    [Test]
    public async Task SupersedesAcceptsValidMultiHopAppendOnlyCorrection()
    {
        string prior = ValidFragmentYaml();
        string second = Fragment("CHG-2026-0002", "CHG-2026-0001");
        string third = Fragment("CHG-2026-0003", "CHG-2026-0002");

        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [prior, second, third],
            [prior]);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task PublicEmbargoAndRestrictedMarkersFailClosed()
    {
        ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
            ValidReleaseYaml(),
            [ValidFragmentYaml().Replace("Public-Disclosure: coordinated", "Public-Disclosure: embargoed", StringComparison.Ordinal) + "\nRestricted-Details: private reproduction\n"],
            []);

        await Assert.That(result.Diagnostics).Contains("fragment_embargo_not_public:CHG-2026-0001");
        await Assert.That(result.Diagnostics).Contains("fragment_restricted_detail_marker:CHG-2026-0001");
    }

    [Test]
    public async Task PublicMarkersRejectCaseConfusablesAndBidiWithoutRejectingMultilingualText()
    {
        string embargo = ValidFragmentYaml().Replace("Public-Disclosure: coordinated", "Public-Disclosure: Embargoed", StringComparison.Ordinal);
        string confusable = Fragment("CHG-2026-0002").Replace("single credential", "re\u0455tricted credential", StringComparison.Ordinal);
        string bidi = Fragment("CHG-2026-0003").Replace("single credential", "single \u202Ecredential", StringComparison.Ordinal);
        string format = Fragment("CHG-2026-0005").Replace("single credential", "single\u200Dcredential", StringComparison.Ordinal);
        string multilingual = Fragment("CHG-2026-0004").Replace("Attendees use a single credential during check-in.", "يمكن للحاضرين استخدام اعتماد واحد عند تسجيل الوصول.", StringComparison.Ordinal);

        ReleaseInputValidationResult invalid = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [embargo, confusable, bidi, format], []);
        ReleaseInputValidationResult valid = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [multilingual], []);

        await Assert.That(invalid.Diagnostics).Contains("fragment_embargo_not_public:CHG-2026-0001");
        await Assert.That(invalid.Diagnostics).Contains("fragment_restricted_detail_marker:CHG-2026-0002");
        await Assert.That(invalid.Diagnostics).Contains("fragment_ambiguous_unicode:CHG-2026-0003");
        await Assert.That(invalid.Diagnostics).Contains("fragment_ambiguous_unicode:CHG-2026-0005");
        await Assert.That(valid.IsValid).IsTrue();
    }

    [Test]
    public async Task ReleaseRangeIsRequiredStrictAndConsistentWithDescriptorReferences()
    {
        string absent = ValidReleaseYaml().Replace(ValidRangeYaml(), string.Empty, StringComparison.Ordinal);
        string malformed = ValidReleaseYaml().Replace(FullOid('a'), "0123456789ab", StringComparison.Ordinal);
        string mismatch = ValidReleaseYaml().Replace("Base-Ref: v1.0.0", "Base-Ref: v0.9.0", StringComparison.Ordinal);
        string traversal = ValidReleaseYaml().Replace("Previous-Ref: v1.0.0", "Previous-Ref: ../../main", StringComparison.Ordinal);
        string unknown = ValidReleaseYaml().Replace("  Previous-Oid:", "  Unexpected: value\n  Previous-Oid:", StringComparison.Ordinal);

        ReleaseInputValidationResult absentResult = ReleaseInputPolicy.Validate(absent, [], []);
        ReleaseInputValidationResult malformedResult = ReleaseInputPolicy.Validate(malformed, [], []);
        ReleaseInputValidationResult mismatchResult = ReleaseInputPolicy.Validate(mismatch, [], []);
        ReleaseInputValidationResult traversalResult = ReleaseInputPolicy.Validate(traversal, [], []);
        ReleaseInputValidationResult unknownResult = ReleaseInputPolicy.Validate(unknown, [], []);

        await Assert.That(absentResult.Diagnostics).Contains("release_missing_key:Release-Range");
        await Assert.That(malformedResult.Diagnostics).Contains("release_malformed_full_oid:Release-Range:Base-Oid");
        await Assert.That(mismatchResult.Diagnostics).Contains("release_range_base_mismatch");
        await Assert.That(traversalResult.Diagnostics).Contains("release_malformed_range_ref:Previous-Ref");
        await Assert.That(unknownResult.Diagnostics).Contains("release-range_unknown_key:Unexpected");
    }

    [Test]
    public async Task ReleaseRangeAcceptsSha1AndSha256FullObjectIds()
    {
        ReleaseInputValidationResult sha1 = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [], []);
        string sha256Yaml = ValidReleaseYaml()
            .Replace(FullOid('a'), FullOid('c', 64), StringComparison.Ordinal)
            .Replace(FullOid('b'), FullOid('d', 64), StringComparison.Ordinal);
        ReleaseInputValidationResult sha256 = ReleaseInputPolicy.Validate(sha256Yaml, [], []);

        await Assert.That(sha1.IsValid).IsTrue();
        await Assert.That(sha256.IsValid).IsTrue();
        await Assert.That(sha1.Descriptor?.ReleaseRange.BaseOid).IsEqualTo(FullOid('a'));
        await Assert.That(sha256.Descriptor?.ReleaseRange.PreviousOid).IsEqualTo(FullOid('d', 64));
    }

    [Test]
    public async Task FirstReleaseBaselineRangeAcceptsOnlyExactDatedNonSemVerRefAndFullObjectIds()
    {
        string baseline = ValidReleaseYaml()
            .Replace("Base-Stable-Tag: v1.0.0", "Base-Stable-Tag: changelog-baseline-2026-08-15", StringComparison.Ordinal)
            .Replace("Previous-Published-Tag: v1.0.0", "Previous-Published-Tag: changelog-baseline-2026-08-15", StringComparison.Ordinal)
            .Replace("Base-Ref: v1.0.0", "Base-Ref: changelog-baseline-2026-08-15", StringComparison.Ordinal)
            .Replace("Previous-Ref: v1.0.0", "Previous-Ref: changelog-baseline-2026-08-15", StringComparison.Ordinal)
            .Replace(FullOid('b'), FullOid('a'), StringComparison.Ordinal);
        string fakeSemver = baseline.Replace("changelog-baseline-2026-08-15", "v0.0.0", StringComparison.Ordinal);
        string wrongDate = baseline.Replace("changelog-baseline-2026-08-15", "changelog-baseline-2026-8-15", StringComparison.Ordinal);
        string shortOid = baseline.Replace(FullOid('a'), "0123456789ab", StringComparison.Ordinal);
        string mixedLowerBound = baseline.Replace("Previous-Published-Tag: changelog-baseline-2026-08-15", "Previous-Published-Tag: v0.1.0", StringComparison.Ordinal)
            .Replace("Previous-Ref: changelog-baseline-2026-08-15", "Previous-Ref: v0.1.0", StringComparison.Ordinal);
        string splitBaselineOid = baseline.Replace($"Previous-Oid: {FullOid('a')}", $"Previous-Oid: {FullOid('b')}", StringComparison.Ordinal);

        ReleaseInputValidationResult accepted = ReleaseInputPolicy.Validate(baseline, [], []);
        ReleaseInputValidationResult fakeSemverResult = ReleaseInputPolicy.Validate(fakeSemver, [], []);
        ReleaseInputValidationResult wrongDateResult = ReleaseInputPolicy.Validate(wrongDate, [], []);
        ReleaseInputValidationResult shortOidResult = ReleaseInputPolicy.Validate(shortOid, [], []);
        ReleaseInputValidationResult mixedLowerBoundResult = ReleaseInputPolicy.Validate(mixedLowerBound, [], []);
        ReleaseInputValidationResult splitBaselineOidResult = ReleaseInputPolicy.Validate(splitBaselineOid, [], []);

        await Assert.That(accepted.IsValid).IsTrue();
        await Assert.That(accepted.Descriptor?.BaseStableTag).IsEqualTo("changelog-baseline-2026-08-15");
        await Assert.That(fakeSemverResult.Diagnostics).Contains("release_fake_semver_baseline");
        await Assert.That(wrongDateResult.Diagnostics).Contains("release_malformed_baseline_ref:Base-Stable-Tag");
        await Assert.That(shortOidResult.Diagnostics).Contains("release_malformed_full_oid:Release-Range:Base-Oid");
        await Assert.That(mixedLowerBoundResult.Diagnostics).Contains("release_baseline_range_mismatch");
        await Assert.That(splitBaselineOidResult.Diagnostics).Contains("release_baseline_range_mismatch");
    }

    [Test]
    public async Task PublicInputsRejectSecretsAndForgeMetadataButAllowProductNames()
    {
        string privateKey = Fragment("CHG-2026-0002").Replace("single credential", "-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal);
        string bearer = Fragment("CHG-2026-0003").Replace("single credential", "Bearer abcdefghijklmnopqrstuvwxyz012345", StringComparison.Ordinal);
        string credential = Fragment("CHG-2026-0004").Replace("single credential", "client_secret=not-for-release", StringComparison.Ordinal);
        string tokenPrefix = Fragment("CHG-2026-0011").Replace("single credential", "ghp_abcdefghijklmnopqrstuvwxyz123456", StringComparison.Ordinal);
        string password = Fragment("CHG-2026-0012").Replace("single credential", "password=not-for-release", StringComparison.Ordinal);
        string lowercaseBearer = Fragment("CHG-2026-0013").Replace("single credential", "bearer abcdefghijklmnopqrstuvwxyz123456", StringComparison.Ordinal);
        string profile = Fragment("CHG-2026-0005").Replace("single credential", "https://github.com/alice", StringComparison.Ordinal);
        string commit = Fragment("CHG-2026-0006").Replace("single credential", "https://gitlab.com/team/app/-/commit/0123456789abcdef", StringComparison.Ordinal);
        string pullRequest = Fragment("CHG-2026-0007").Replace("single credential", "https://codeberg.org/team/app/pulls/42", StringComparison.Ordinal);
        string handle = Fragment("CHG-2026-0008").Replace("single credential", "approved by @release-admin", StringComparison.Ordinal);
        string runId = Fragment("CHG-2026-0009").Replace("single credential", "workflow run id=123456", StringComparison.Ordinal);
        string productNames = Fragment("CHG-2026-0010").Replace("Attendees use a single credential during check-in.", "GitHub and GitLab users receive clearer release notes.", StringComparison.Ordinal);
        string emailProse = Fragment("CHG-2026-0014").Replace("Attendees use a single credential during check-in.", "Contact support@example.org for migration help.", StringComparison.Ordinal);

        ReleaseInputValidationResult secrets = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [privateKey, bearer, credential, tokenPrefix, password, lowercaseBearer], []);
        ReleaseInputValidationResult providers = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [profile, commit, pullRequest, handle, runId], []);
        ReleaseInputValidationResult prose = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [productNames, emailProse], []);
        ReleaseInputValidationResult descriptorSecret = ReleaseInputPolicy.Validate(ValidReleaseYaml().Replace("  - v1", "  - password=not-for-release", StringComparison.Ordinal), [], []);
        ReleaseInputValidationResult descriptorProvider = ReleaseInputPolicy.Validate(ValidReleaseYaml().Replace("  - v1", "  - https://github.com/release-admin", StringComparison.Ordinal), [], []);

        await Assert.That(secrets.Diagnostics.Count(diagnostic => diagnostic.StartsWith("fragment_secret_material:", StringComparison.Ordinal))).IsEqualTo(6);
        await Assert.That(providers.Diagnostics.Count(diagnostic => diagnostic.StartsWith("fragment_provider_identity:", StringComparison.Ordinal))).IsEqualTo(5);
        await Assert.That(prose.IsValid).IsTrue();
        await Assert.That(descriptorSecret.Diagnostics).Contains("release_secret_material");
        await Assert.That(descriptorProvider.Diagnostics).Contains("release_provider_identity");
    }

    [Test]
    public async Task NullAndOversizedInputsFailBoundedlyWhileEmptyCollectionsRemainValid()
    {
        ReleaseInputValidationResult nullRelease = ReleaseInputPolicy.Validate(null!, [], []);
        ReleaseInputValidationResult nullFragments = ReleaseInputPolicy.Validate(ValidReleaseYaml(), null!, []);
        ReleaseInputValidationResult nullSnapshot = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [], null!);
        ReleaseInputValidationResult oversizedText = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [new string('x', 65_537)], []);
        ReleaseInputValidationResult oversizedCollection = ReleaseInputPolicy.Validate(ValidReleaseYaml(), Enumerable.Repeat(ValidFragmentYaml(), 1_025), []);
        ReleaseInputValidationResult empty = ReleaseInputPolicy.Validate(ValidReleaseYaml(), [], []);

        await Assert.That(nullRelease.Diagnostics).Contains("release_empty_yaml");
        await Assert.That(nullFragments.Diagnostics).Contains("fragment_collection_null");
        await Assert.That(nullSnapshot.Diagnostics).Contains("snapshot_collection_null");
        await Assert.That(oversizedText.Diagnostics).Contains("fragment_yaml_too_large");
        await Assert.That(oversizedCollection.Diagnostics).Contains("fragment_collection_too_large");
        await Assert.That(empty.IsValid).IsTrue();
    }

    [Test]
    [NotInParallel("ReleaseInputCulture")]
    public async Task ValidationIsStableAcrossCrlfCultureAndRepeatedRuns()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            string release = ValidReleaseYaml().Replace("\n", "\r\n", StringComparison.Ordinal);
            string fragment = ValidFragmentYaml().Replace("\n", "\r\n", StringComparison.Ordinal);

            ReleaseInputValidationResult first = ReleaseInputPolicy.Validate(release, [fragment], []);
            ReleaseInputValidationResult second = ReleaseInputPolicy.Validate(release, [fragment], []);

            await Assert.That(first.IsValid).IsTrue();
            await Assert.That(second.Diagnostics).IsEquivalentTo(first.Diagnostics);
            await Assert.That(second.Fragments[0].CanonicalSnapshot).IsEqualTo(first.Fragments[0].CanonicalSnapshot);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task PublicEmbargoCaseVariantsFailClosed()
    {
        foreach (string disclosure in new[] { "Embargoed", "EMBARGOED" })
        {
            ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
                ValidReleaseYaml(),
                [ValidFragmentYaml().Replace("Public-Disclosure: coordinated", $"Public-Disclosure: {disclosure}", StringComparison.Ordinal)],
                []);

            await Assert.That(result.Diagnostics).Contains("fragment_embargo_not_public:CHG-2026-0001");
        }
    }

    [Test]
    public async Task RestrictedDetailMarkerCaseVariantsFailClosed()
    {
        foreach (string marker in new[] { "restricted-details", "RESTRICTED-DETAILS" })
        {
            ReleaseInputValidationResult result = ReleaseInputPolicy.Validate(
                ValidReleaseYaml(),
                [ValidFragmentYaml() + $"\n{marker}: private reproduction\n"],
                []);

            await Assert.That(result.Diagnostics).Contains("fragment_restricted_detail_marker:CHG-2026-0001");
        }
    }

    private static string ValidReleaseYaml() =>
        """
        Version: 1.1.0
        Line: v1.1
        Release-Date: 2026-08-14
        Base-Stable-Tag: v1.0.0
        Previous-Published-Tag: v1.0.0
        Release-Range:
          Base-Ref: v1.0.0
          Base-Oid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
          Previous-Ref: v1.0.0
          Previous-Oid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
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

    private static string ValidRangeYaml() =>
        """
        Release-Range:
          Base-Ref: v1.0.0
          Base-Oid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
          Previous-Ref: v1.0.0
          Previous-Oid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb

        """;

    private static string Fragment(string changeId, string? supersedes = null) =>
        ValidFragmentYaml()
            .Replace("Change-Id: CHG-2026-0001", $"Change-Id: {changeId}", StringComparison.Ordinal)
            .Replace("Supersedes: []", supersedes is null ? "Supersedes: []" : $"Supersedes:\n  - {supersedes}", StringComparison.Ordinal);

    private static string FullOid(char value, int length = 40) => new(value, length);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static string ValidFragmentYaml() =>
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
            Disposition: documented
            Detail: Check-in integrations must send credential after upgrading.
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
