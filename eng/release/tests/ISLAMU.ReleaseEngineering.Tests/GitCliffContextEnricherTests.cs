using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class GitCliffContextEnricherTests
{
    private const string ExtraField = "extra";

    private static readonly ReleasePolicy Policy = ReleasePolicy.LoadFromRepositoryRoot(RepositoryRoot.Find());
    private static readonly string BaseOid = new('a', 40);
    private static readonly string VisibleOid = new('c', 40);
    private static readonly string OmittedOid = new('d', 40);

    private static readonly HashSet<string> AllowedReleaseKeys = new(StringComparer.Ordinal)
    {
        "version",
        "commit_id",
        "commit_range",
        "previous",
        "commits",
        ExtraField,
    };

    private static readonly HashSet<string> AllowedCommitKeys = new(StringComparer.Ordinal)
    {
        "id",
        "group",
        "scope",
        "message",
        "breaking",
        "breaking_description",
        "conventional",
        "merge_commit",
        ExtraField,
    };

    [Test]
    public async Task ReconstructsAllowlistedContextAndDropsEveryPoisonedNativeAndFutureField()
    {
        (ReleaseInputValidationResult input, ReleaseContextValidationResult authority) = CreateValidAuthority(includeFragment: true);

        string poisonedNativeJson = $$"""
            [
              {
                "version": "v1.1.0",
                "message": "POISON_TAG_MESSAGE",
                "commit_id": "{{OmittedOid}}",
                "timestamp": 1795555555,
                "repository": "/home/secret-dev/POISON_REPO_PATH",
                "commit_range": { "from": "{{VisibleOid}}", "to": "{{OmittedOid}}" },
                "previous": {
                  "version": "v1.0.0",
                  "commit_id": "{{BaseOid}}",
                  "repository": "/POISON_PREV_REPO",
                  "timestamp": 1790000000,
                  "github": { "contributors": [{ "username": "POISON_PREV_CONTRIB" }] },
                  "unknown_future_prev": "POISON_FUTURE_PREV"
                },
                "statistics": { "commit_count": 2, "POISON_STAT": 99 },
                "github": { "contributors": [{ "username": "POISON_GH_CONTRIB" }] },
                "gitlab": { "contributors": [{ "username": "POISON_GL_CONTRIB" }] },
                "gitea": { "contributors": [{ "username": "POISON_GT_CONTRIB" }] },
                "bitbucket": { "contributors": [{ "username": "POISON_BB_CONTRIB" }] },
                "azure_devops": { "contributors": [{ "username": "POISON_AZ_CONTRIB" }] },
                "remote": { "owner": "POISON_REMOTE_OWNER" },
                "links": [{ "text": "POISON_REL_LINK", "href": "https://github.com/POISON_ORG/repo" }],
                "unknown_future_release_field": { "nested": "POISON_FUTURE_RELEASE" },
                "commits": [
                  {
                    "id": "{{VisibleOid}}",
                    "group": "Features",
                    "scope": "registration",
                    "message": "let attendees correct registration details",
                    "body": "POISON_COMMIT_BODY with maintainer@example.invalid",
                    "raw_message": "POISON_RAW_MESSAGE",
                    "footers": [{ "token": "Signed-off-by", "value": "POISON_FOOTER_SIGNER <poison@example.invalid>" }],
                    "breaking": false,
                    "breaking_description": null,
                    "conventional": true,
                    "merge_commit": false,
                    "author": { "name": "POISON_AUTHOR_NAME", "email": "poison-author@example.invalid", "timestamp": 1795555555 },
                    "committer": { "name": "POISON_COMMITTER_NAME", "email": "poison-committer@example.invalid", "timestamp": 1795555555 },
                    "links": [{ "text": "POISON_COMMIT_LINK", "href": "https://github.com/POISON/1" }],
                    "remote": { "username": "POISON_COMMIT_REMOTE" },
                    "github": { "username": "POISON_GH_USER", "pr_author": "POISON_PR_AUTHOR", "pr_number": 77 },
                    "gitlab": { "username": "POISON_GL_USER" },
                    "gitea": { "username": "POISON_GT_USER" },
                    "bitbucket": { "username": "POISON_BB_USER" },
                    "azure_devops": { "username": "POISON_AZ_USER" },
                    "statistics": { "files_changed": 3, "POISON_COMMIT_STAT": 42 },
                    "unknown_future_commit_field": "POISON_FUTURE_COMMIT"
                  }
                ]
              }
            ]
            """;

        var request = new GitCliffContextEnrichmentRequest(
            RawNativeContextJson: poisonedNativeJson,
            Input: input,
            AuthorityContext: authority,
            RangeOids: [VisibleOid, OmittedOid],
            OmittedRangeOids: [OmittedOid],
            MaintainerSummary: "Attendees can now correct registration details.",
            ContractDiff: null,
            ImageReferences: null);

        GitCliffContextEnrichmentResult first = GitCliffContextEnricher.Enrich(request);
        GitCliffContextEnrichmentResult second = GitCliffContextEnricher.Enrich(request);

        await Assert.That(first.IsValid).IsTrue().Because(first.Diagnostic ?? "enrich");
        await Assert.That(second.Json).IsEqualTo(first.Json);
        await Assert.That(first.Json!).DoesNotContain("POISON_");
        await Assert.That(first.Json!).DoesNotContain("example.invalid");

        using JsonDocument doc = JsonDocument.Parse(first.Json!);
        await Assert.That(doc.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(1);

        JsonElement release = doc.RootElement[0];
        foreach (string key in release.EnumerateObject().Select(prop => prop.Name))
        {
            await Assert.That(AllowedReleaseKeys.Contains(key)).IsTrue().Because($"Unexpected release key: {key}");
        }

        JsonElement commit = release.GetProperty("commits")[0];
        foreach (string key in commit.EnumerateObject().Select(prop => prop.Name))
        {
            await Assert.That(AllowedCommitKeys.Contains(key)).IsTrue().Because($"Unexpected commit key: {key}");
        }

        // Curated extra is present, and images is absent when no manifest was supplied.
        JsonElement releaseExtra = release.GetProperty(ExtraField);
        await Assert.That(releaseExtra.GetProperty("version").GetString()).IsEqualTo("1.1.0");
        await Assert.That(releaseExtra.GetProperty("maintainerSummary").GetString()).IsEqualTo("Attendees can now correct registration details.");
        await Assert.That(releaseExtra.TryGetProperty("images", out _)).IsFalse();

        JsonElement commitExtra = commit.GetProperty(ExtraField);
        await Assert.That(commitExtra.GetProperty("changeId").GetString()).IsEqualTo("CHG-2026-0001");
        await Assert.That(commitExtra.GetProperty("displayId").GetString()).IsEqualTo(VisibleOid[..12]);
    }

    [Test]
    public async Task RejectsMalformedOrMultiReleaseNativeArraysAndAuthorityDisagreements()
    {
        (ReleaseInputValidationResult input, ReleaseContextValidationResult authority) = CreateValidAuthority(includeFragment: false);

        GitCliffContextEnrichmentResult notArray = GitCliffContextEnricher.Enrich(MakeRequest("{}", input, authority));
        GitCliffContextEnrichmentResult emptyArray = GitCliffContextEnricher.Enrich(MakeRequest("[]", input, authority));
        GitCliffContextEnrichmentResult twoReleases = GitCliffContextEnricher.Enrich(MakeRequest("[{\"version\":\"v1.1.0\",\"commits\":[]},{\"version\":\"v1.0.0\",\"commits\":[]}]", input, authority));
        GitCliffContextEnrichmentResult wrongVersion = GitCliffContextEnricher.Enrich(MakeRequest(
            $$"""[{"version":"v9.9.9","commit_id":"{{VisibleOid}}","commits":[{"id":"{{VisibleOid}}","group":"Features","scope":"registration","message":"let attendees correct registration details","breaking":false,"conventional":true,"merge_commit":false}]}]""",
            input,
            authority));
        GitCliffContextEnrichmentResult extraVisible = GitCliffContextEnricher.Enrich(MakeRequest(
            $$"""[{"version":"v1.1.0","commit_id":"{{OmittedOid}}","commits":[{"id":"{{VisibleOid}}","group":"Features","scope":"registration","message":"let attendees correct registration details","breaking":false,"conventional":true,"merge_commit":false},{"id":"{{OmittedOid}}","group":"Other","scope":"release","message":"internal","breaking":false,"conventional":true,"merge_commit":false}]}]""",
            input,
            authority));
        GitCliffContextEnrichmentResult missingOmitted = GitCliffContextEnricher.Enrich(new GitCliffContextEnrichmentRequest(
            $$"""[{"version":"v1.1.0","commit_id":"{{OmittedOid}}","commits":[{"id":"{{VisibleOid}}","group":"Features","scope":"registration","message":"let attendees correct registration details","breaking":false,"conventional":true,"merge_commit":false}]}]""",
            input,
            authority,
            RangeOids: [VisibleOid, OmittedOid],
            OmittedRangeOids: [],
            MaintainerSummary: "Summary",
            ContractDiff: null,
            ImageReferences: null));
        GitCliffContextEnrichmentResult breakingMismatch = GitCliffContextEnricher.Enrich(MakeRequest(
            $$"""[{"version":"v1.1.0","commit_id":"{{OmittedOid}}","commits":[{"id":"{{VisibleOid}}","group":"Features","scope":"registration","message":"let attendees correct registration details","breaking":true,"conventional":true,"merge_commit":false}]}]""",
            input,
            authority));
        GitCliffContextEnrichmentResult rangeOutside = GitCliffContextEnricher.Enrich(MakeRequest(
            $$"""[{"version":"v1.1.0","commit_id":"{{new string('f', 40)}}","commits":[{"id":"{{VisibleOid}}","group":"Features","scope":"registration","message":"let attendees correct registration details","breaking":false,"conventional":true,"merge_commit":false}]}]""",
            input,
            authority));

        await Assert.That(notArray.Diagnostic).IsEqualTo("enricher_native_context_invalid");
        await Assert.That(emptyArray.Diagnostic).IsEqualTo("enricher_native_context_invalid");
        await Assert.That(twoReleases.Diagnostic).IsEqualTo("enricher_native_context_invalid");
        await Assert.That(wrongVersion.Diagnostic).IsEqualTo("enricher_version_mismatch");
        await Assert.That(extraVisible.Diagnostic).IsEqualTo("enricher_visible_commit_set_mismatch");
        await Assert.That(missingOmitted.Diagnostic).IsEqualTo("enricher_omitted_commit_set_mismatch");
        await Assert.That(breakingMismatch.Diagnostic).IsEqualTo("enricher_breaking_flag_mismatch");
        await Assert.That(rangeOutside.Diagnostic).IsEqualTo("enricher_range_mismatch");
    }

    [Test]
    public async Task RejectsEmbargoedFragmentsAndIncludesVerifiedImagesOnlyWhenManifestSupplied()
    {
        (ReleaseInputValidationResult input, ReleaseContextValidationResult authority) = CreateValidAuthority(includeFragment: true);
        string validNative = $$"""
            [
              {
                "version": "v1.1.0",
                "commit_id": "{{OmittedOid}}",
                "previous": { "version": "v1.0.0", "commit_id": "{{BaseOid}}" },
                "commits": [
                  {
                    "id": "{{VisibleOid}}",
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

        PublicChangeFragment embargoed = input.Fragments[0] with
        {
            Impacts = new Dictionary<string, FragmentImpact>(input.Fragments[0].Impacts, StringComparer.Ordinal)
            {
                ["security"] = new FragmentImpact("docs/internal/SECURITY_OVERVIEW.md", "coordinated", "embargoed", "Private detail"),
            },
        };
        ReleaseInputValidationResult embargoInput = input with { Fragments = [embargoed] };

        GitCliffContextEnrichmentResult embargoRejected = GitCliffContextEnricher.Enrich(new GitCliffContextEnrichmentRequest(
            validNative, embargoInput, authority, [VisibleOid, OmittedOid], [OmittedOid], "Summary", null, null));

        await Assert.That(embargoRejected.IsValid).IsFalse();
        await Assert.That(embargoRejected.Diagnostic).IsEqualTo("enricher_fragment_embargo_or_restricted");

        EnrichedImageReference[] images =
        [
            new("api", "sha256:" + new string('1', 64), ["ghcr.io/islamu-ngo/event-api@sha256:" + new string('1', 64)]),
        ];
        GitCliffContextEnrichmentResult withImages = GitCliffContextEnricher.Enrich(new GitCliffContextEnrichmentRequest(
            validNative, input, authority, [VisibleOid, OmittedOid], [OmittedOid], "Summary", null, images));

        await Assert.That(withImages.IsValid).IsTrue().Because(withImages.Diagnostic ?? "with images");
        await Assert.That(withImages.Json!).Contains("ghcr.io/islamu-ngo/event-api@sha256:" + new string('1', 64));
    }

    private static GitCliffContextEnrichmentRequest MakeRequest(
        string nativeJson,
        ReleaseInputValidationResult input,
        ReleaseContextValidationResult authority) => new(
            nativeJson,
            input,
            authority,
            [VisibleOid, OmittedOid],
            [OmittedOid],
            "Attendees can now correct registration details.",
            ContractDiff: null,
            ImageReferences: null);

    private static (ReleaseInputValidationResult Input, ReleaseContextValidationResult Authority) CreateValidAuthority(bool includeFragment)
    {
        string releaseYaml = $"""
            Version: 1.1.0
            Line: v1.1
            Release-Date: 2026-08-14
            Base-Stable-Tag: v1.0.0
            Previous-Published-Tag: v1.0.0
            Release-Range:
              Base-Ref: v1.0.0
              Base-Oid: {BaseOid}
              Previous-Ref: v1.0.0
              Previous-Oid: {BaseOid}
            Compatibility:
              - v1
            Impact-Dispositions:
              breaking: not-applicable
              security: not-applicable
              migration: not-applicable
              configuration: not-applicable
              openapi: not-applicable
              operator: documented
            """;

        string[] fragments = includeFragment
            ? ["""
                Change-Id: CHG-2026-0001
                Title: Attendee registration edit
                Type: feat
                Scope: registration
                Summary: Attendees can correct registration details.
                Supersedes: []
                Impacts:
                  Breaking:
                    Reference: docs/internal/API_CHANGELOG.md
                    Disposition: not-applicable
                  Security:
                    Reference: docs/internal/SECURITY_OVERVIEW.md
                    Disposition: not-applicable
                  Migration:
                    Reference: docs/internal/RELEASE_RUNBOOK.md
                    Disposition: not-applicable
                  Configuration:
                    Reference: docs/internal/CONFIGURATION.md
                    Disposition: not-applicable
                  OpenAPI:
                    Reference: docs/internal/API_CHANGELOG.md
                    Disposition: not-applicable
                  Operator:
                    Reference: docs/internal/RELEASE_CHECKLIST.md
                    Disposition: documented
                    Detail: Restart registration workers after deployment.
                """]
            : [];

        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(releaseYaml, fragments, []);
        string visibleMsg = includeFragment
            ? "feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001"
            : "feat(registration): let attendees correct registration details";
        ReleaseCommit[] commits =
        [
            new(VisibleOid, visibleMsg),
            new(OmittedOid, "refactor(release): internal cleanup"),
        ];
        ReleaseContextValidationResult authority = ReleaseContextPolicy.Build(input, commits, Policy);
        return (input, authority);
    }
}
