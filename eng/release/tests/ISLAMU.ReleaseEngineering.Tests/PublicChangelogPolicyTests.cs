using System.Text;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class PublicChangelogPolicyTests
{
    [Test]
    public async Task OrdersByDateThenSemVerAndPreservesOlderMaintenanceAndPrereleases()
    {
        PublicChangelogRelease[] releases =
        [
            Release("1.10.0", "2026-08-01"),
            Release("1.9.0", "2026-08-01"),
            Release("2.0.0-rc.1", "2026-07-15"),
            Release("1.9.1", "2026-09-01"),
            Release("2.0.0", "2026-08-02"),
        ];

        PublicChangelogResult result = PublicChangelogPolicy.Generate(releases, new Uri("https://example.org/releases/"));
        string markdown = Encoding.UTF8.GetString(result.Page!);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid releases");
        await Assert.That(result.HighestStableVersion).IsEqualTo("2.0.0");
        await Assert.That(markdown.IndexOf("## v1.9.1", StringComparison.Ordinal)).IsLessThan(markdown.IndexOf("## v2.0.0", StringComparison.Ordinal));
        await Assert.That(markdown.IndexOf("## v1.10.0", StringComparison.Ordinal)).IsLessThan(markdown.IndexOf("## v1.9.0", StringComparison.Ordinal));
        await Assert.That(markdown).Contains("Pre-release");
        await Assert.That(markdown).Contains("## v2.0.0-rc.1");
    }

    [Test]
    public async Task ComposesEscapedChangesAndUpgradeGuidanceBeforeOrdinaryCategories()
    {
        PublicChangelogRelease release = Release("1.2.0", "2026-09-01") with
        {
            Context = Context("1.2.0", "2026-09-01",
            [
                Change("CHG-BREAK", "fix", "api", "retire *old* endpoint", breaking: true),
                Change("CHG-FEAT", "feat", "events", "display [schedule](bad)"),
            ]),
            Fragments =
            [
                Fragment("CHG-BREAK", "fix", "api", "retire *old* endpoint",
                    new Dictionary<string, FragmentImpact> { ["openapi"] = new("docs/api-upgrade.md#governance-report", "documented", null, "Update callers before deployment") }),
                Fragment("CHG-FEAT", "feat", "events", "display [schedule](bad)", new Dictionary<string, FragmentImpact>()),
            ],
        };

        PublicChangelogResult result = PublicChangelogPolicy.Generate([release], new Uri("https://example.org/releases/"));
        string markdown = Encoding.UTF8.GetString(result.Page!);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid impacts");
        await Assert.That(markdown).Contains("retire \\*old\\* endpoint");
        await Assert.That(markdown).Contains("display \\[schedule\\]\\(bad\\)");
        await Assert.That(markdown.IndexOf("### Breaking Changes", StringComparison.Ordinal)).IsLessThan(markdown.IndexOf("### Features", StringComparison.Ordinal));
        await Assert.That(markdown).Contains("Update callers before deployment");
        await Assert.That(markdown).Contains($"https://example.org/releases/{new string('e', 40)}/docs/api-upgrade.md#governance-report");
        await Assert.That(markdown).Contains("### Verify this release");
        await Assert.That(markdown).Contains(new string('a', 64));
        await Assert.That(markdown).DoesNotContain("### Performance");
    }

    [Test]
    public async Task RejectsMissingUpgradeEvidenceAndUntrustedHtml()
    {
        PublicChangelogRelease breaking = Release("1.2.0", "2026-09-01") with
        {
            Context = Context("1.2.0", "2026-09-01", [Change("CHG-BREAK", "fix", "api", "remove endpoint", breaking: true)]),
            Fragments = [Fragment("CHG-BREAK", "fix", "api", "remove endpoint", new Dictionary<string, FragmentImpact>())],
        };
        PublicChangelogRelease html = Release("1.2.0", "2026-09-01") with { Summary = "<script>unsafe</script>" };

        await Assert.That(PublicChangelogPolicy.Generate([breaking], new Uri("https://example.org/releases/")).IsValid).IsFalse();
        await Assert.That(PublicChangelogPolicy.Generate([html], new Uri("https://example.org/releases/")).IsValid).IsFalse();
    }

    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("../private/evidence.md")]
    [Arguments("/internal/evidence.md")]
    [Arguments("http://example.org/evidence")]
    public async Task RejectsUnsafeEvidenceReferences(string reference)
    {
        PublicChangelogRelease release = Release("1.2.0", "2026-09-01") with
        {
            Fragments =
            [
                Fragment("CHG-FEAT", "feat", "events", "publish schedule",
                    new Dictionary<string, FragmentImpact>
                    {
                        ["operator"] = new(reference, "documented", null, "Read operator guidance"),
                    }),
            ],
        };

        await Assert.That(PublicChangelogPolicy.Generate([release], new Uri("https://example.org/releases/")).IsValid).IsFalse();
    }

    [Test]
    public async Task EnforcesWholePageByteBudgetWithoutTruncatingHistory()
    {
        PublicChangelogRelease release = Release("1.2.0", "2026-09-01");
        PublicChangelogResult tooLarge = PublicChangelogPolicy.Generate(
            Enumerable.Repeat(release, 400).Select((item, index) => item with
            {
                Descriptor = item.Descriptor with { Version = $"1.2.{index}" },
                Context = item.Context with { Release = item.Context.Release with { Version = $"1.2.{index}" } },
                Summary = new string('x', 4000),
            }).ToArray(),
            new Uri("https://example.org/releases/"));

        await Assert.That(tooLarge.IsValid).IsFalse();
        await Assert.That(tooLarge.Page).IsNull();
    }

    [Test]
    public async Task WarnsBeforeReachingTheUtf8PageCeiling()
    {
        PublicChangelogRelease release = Release("1.2.0", "2026-09-01");
        PublicChangelogRelease[] nearLimit = Enumerable.Range(0, 260).Select(index => release with
        {
            Descriptor = release.Descriptor with { Version = $"1.2.{index}" },
            Context = release.Context with { Release = release.Context.Release with { Version = $"1.2.{index}" } },
            Summary = new string('x', 3450),
        }).ToArray();

        PublicChangelogResult result = PublicChangelogPolicy.Generate(nearLimit, new Uri("https://example.org/releases/"));

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "near-limit page");
        await Assert.That(result.Warning).IsEqualTo("changelog_page_near_limit");
        await Assert.That(result.Page!.Length).IsLessThanOrEqualTo(PublicChangelogPolicy.MaximumPageBytes);
    }

    [Test]
    public async Task SentinelReplacementPreservesMetadataAndRejectsCorruption()
    {
        byte[] generated = PublicChangelogPolicy.Generate([Release("1.0.0", "2026-09-01")], new Uri("https://example.org/releases/")).Page!;
        byte[] accepted = Encoding.UTF8.GetBytes("---\nlayout: changelog\n---\n" + Encoding.UTF8.GetString(generated));
        PublicChangelogResult replaced = PublicChangelogPolicy.ReplaceRegion(accepted, generated, bootstrap: false);
        PublicChangelogResult missing = PublicChangelogPolicy.ReplaceRegion("no markers"u8.ToArray(), generated, bootstrap: false);
        PublicChangelogResult reversed = PublicChangelogPolicy.ReplaceRegion(
            Encoding.UTF8.GetBytes($"{PublicChangelogPolicy.EndMarker}\n{PublicChangelogPolicy.StartMarker}\n"), generated, bootstrap: false);
        PublicChangelogResult invalid = PublicChangelogPolicy.ReplaceRegion([0xff], generated, bootstrap: false);

        await Assert.That(replaced.IsValid).IsTrue();
        await Assert.That(replaced.Page).IsEquivalentTo(accepted);
        await Assert.That(missing.IsValid).IsFalse();
        await Assert.That(reversed.IsValid).IsFalse();
        await Assert.That(invalid.IsValid).IsFalse();
        await Assert.That(PublicChangelogPolicy.ReplaceRegion([], generated, bootstrap: true).IsValid).IsTrue();
        await Assert.That(PublicChangelogPolicy.ReplaceRegion([], generated, bootstrap: false).IsValid).IsFalse();
    }

    private static PublicChangelogRelease Release(string version, string date) => new(
        new ReleaseDescriptor(version, "v1.0", DateOnly.Parse(date), "v0.9.0", "v0.9.0",
            new ReleaseRangeReference("refs/tags/v0.9.0", new string('c', 40), "refs/tags/v0.9.0", new string('c', 40)), [], new Dictionary<string, string>()),
        Context(version, date, [Change("CHG-FEAT", "feat", "events", "publish schedule")]),
        [Fragment("CHG-FEAT", "feat", "events", "publish schedule", new Dictionary<string, FragmentImpact>())],
        "A curated summary of this release.",
        new string('a', 64),
        new string('b', 40),
        new string('e', 40));

    private static ReleaseContext Context(string version, string date, IReadOnlyList<ReleaseContextChange> changes) =>
        new(1, new ReleaseContextRelease(version, "v1.0", date, "v0.9.0", "v0.9.0", "minor", "stable", false),
            changes, new ReleaseContextEvidence(new string('c', 40), new string('c', 40), []));

    private static ReleaseContextChange Change(string id, string type, string scope, string title, bool breaking = false) =>
        new("dddddddddddd", new string('d', 40), id, type, scope, title, "Curated change summary.", breaking, false, null);

    private static PublicChangeFragment Fragment(string id, string type, string scope, string title, IReadOnlyDictionary<string, FragmentImpact> impacts) =>
        new(id, title, type, scope, "Curated change summary.", null, null, [], impacts, "verified snapshot");
}
