namespace Event.Architecture.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// Locks the Phase 3 cache-governance boundary: classify key families and prevent high-cardinality telemetry.
/// </summary>
public sealed partial class CacheGovernanceTests
{
    private static readonly string SourceRoot = LocateSourceRoot();

    private static readonly Dictionary<string, CacheFamilyClassification> ApprovedHybridCacheFamilies =
        new Dictionary<string, CacheFamilyClassification>(StringComparer.Ordinal)
        {
            ["actor:detail"] = CacheFamilyClassification.EntityScoped("actor.detail"),
            ["categories:detail"] = CacheFamilyClassification.BoundedTag("categories.detail"),
            ["categories:list"] = CacheFamilyClassification.PublicList("categories.list"),
            ["custom-property-definitions:detail"] = CacheFamilyClassification.EntityScoped("custom-property-definitions.detail"),
            ["custom-property-definitions:list"] = CacheFamilyClassification.BoundedList("custom-property-definitions.list"),
            ["event-aggregate:detail"] = CacheFamilyClassification.EntityScoped("event-aggregate.detail"),
            ["event-aggregate:list"] = CacheFamilyClassification.FilteredList("event-aggregate.list"),
            ["event-custom-properties:detail"] = CacheFamilyClassification.EntityScoped("event-custom-properties.detail"),
            ["event-custom-properties:list"] = CacheFamilyClassification.EntityScopedList("event-custom-properties.list"),
            ["event-locations:event"] = CacheFamilyClassification.EntityScopedList("event-locations.event"),
            ["event-locations:tenant"] = CacheFamilyClassification.TenantScopedList("event-locations.tenant"),
            ["event-reporting:my-report"] = CacheFamilyClassification.UserScoped("event-reporting.my-report"),
            ["event-reporting:reason-options"] = CacheFamilyClassification.BoundedList("event-reporting.reason-options"),
            ["event-templates:detail"] = CacheFamilyClassification.EntityScoped("event-templates.detail"),
            ["event-templates:list"] = CacheFamilyClassification.TenantScopedList("event-templates.list"),
            ["event:detail"] = CacheFamilyClassification.EntityScoped("event.detail"),
            ["event:public-detail"] = CacheFamilyClassification.EntityScoped("event.public-detail"),
            ["events:detail"] = CacheFamilyClassification.BoundedTag("events.detail"),
            ["events:list"] = CacheFamilyClassification.BoundedTag("events.list"),
            ["events:list:tenant"] = CacheFamilyClassification.TenantScopedList("events.list.tenant"),
            ["group:detail"] = CacheFamilyClassification.EntityScoped("group.detail"),
            ["groups:detail"] = CacheFamilyClassification.BoundedTag("groups.detail"),
            ["groups:list"] = CacheFamilyClassification.BoundedTag("groups.list"),
            ["organization:detail"] = CacheFamilyClassification.EntityScoped("organization.detail"),
            ["organizations:detail"] = CacheFamilyClassification.BoundedTag("organizations.detail"),
            ["organizations:list"] = CacheFamilyClassification.BoundedTag("organizations.list"),
            ["session-custom-properties:detail"] = CacheFamilyClassification.EntityScoped("session-custom-properties.detail"),
            ["session-custom-properties:list"] = CacheFamilyClassification.EntityScopedList("session-custom-properties.list"),
            ["session-templates:detail"] = CacheFamilyClassification.EntityScoped("session-templates.detail"),
            ["session-templates:list"] = CacheFamilyClassification.EntityScopedList("session-templates.list"),
            ["user:detail"] = CacheFamilyClassification.UserScoped("user.detail"),
        };

    [Test]
    public async Task HybridCache_TagMetrics_ShouldRemainDisabledUntilTelemetryAdrApprovesSafeLabels()
    {
        var cachingExtensions = File.ReadAllText(Path.Combine(SourceRoot, "src", "Explore.API", "Extensions", "CachingExtensions.cs"));

        await Assert.That(cachingExtensions).DoesNotContain("ReportTagMetrics = true");
        await Assert.That(cachingExtensions).DoesNotContain("ReportTagMetrics=true");
    }

    [Test]
    public async Task HybridCache_KeyFamilies_ShouldBeExplicitlyClassified()
    {
        var observedFamilies = EnumerateApplicationSourceFiles()
            .Where(path => File.ReadAllText(path).Contains("HybridCache", StringComparison.Ordinal))
            .SelectMany(ExtractCacheKeyFamilies)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var missing = observedFamilies
            .Where(family => !ApprovedHybridCacheFamilies.ContainsKey(family))
            .ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"Every HybridCache key family must be classified before observability can safely bucket it. Observed families: {string.Join(", ", observedFamilies)}");
    }

    [Test]
    public async Task MetricBuckets_ShouldBeLowCardinalityAndNeverExposeRawIdentifiers()
    {
        var violations = ApprovedHybridCacheFamilies
            .Where(pair => !SafeMetricBucketPattern().IsMatch(pair.Value.MetricBucket))
            .Select(pair => $"{pair.Key} -> {pair.Value.MetricBucket}")
            .ToList();

        violations.AddRange(ApprovedHybridCacheFamilies
            .Where(pair => pair.Value.SafeAsRawMetricLabel)
            .Select(pair => $"{pair.Key} is marked safe as a raw metric label; use bucket {pair.Value.MetricBucket} instead."));

        await Assert.That(violations)
            .IsEmpty()
            .Because("Cache telemetry may use only bounded family buckets, never raw cache keys, tenant IDs, user IDs, request IDs, or payload-derived values.");
    }

    [Test]
    public async Task TenantScopedEventLists_ShouldUseTenantNamespaceAndTenantInvalidationTag()
    {
        var handler = File.ReadAllText(Path.Combine(
            SourceRoot,
            "src",
            "Explore.Application",
            "Features",
            "Events",
            "Handlers",
            "Queries",
            "GetEventListRequestHandler.cs"));

        await Assert.That(handler).Contains("events:list:tenant:{tenantCacheKey}");
        await Assert.That(handler).Contains("CacheTags.EventListByTenant(_tenantContext.TenantId)");

        var cacheTags = File.ReadAllText(Path.Combine(SourceRoot, "src", "Explore.Application", "Caching", "CacheTags.cs"));
        await Assert.That(cacheTags).Contains("events:list:tenant:{tenantId:N}");
    }

    [Test]
    public async Task FilterDerivedEventAggregateKeys_ShouldRemainDocumentedAsUnsafeForRawTelemetryLabels()
    {
        var classification = ApprovedHybridCacheFamilies["event-aggregate:list"];

        await Assert.That(classification.Scope).IsEqualTo(CacheKeyScope.FilteredList);
        await Assert.That(classification.SafeAsRawMetricLabel).IsFalse();
        await Assert.That(classification.MetricBucket).IsEqualTo("event-aggregate.list");
    }

    private static IEnumerable<string> ExtractCacheKeyFamilies(string filePath)
    {
        var source = File.ReadAllText(filePath);
        foreach (Match match in CacheStringPattern().Matches(source))
        {
            var contextStart = Math.Max(0, match.Index - 256);
            if (!source.AsSpan(contextStart, match.Index - contextStart)
                    .Contains("cache", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = match.Groups["value"].Value;
            var family = NormalizeCacheFamily(value);
            if (family is not null)
            {
                yield return family;
            }
        }
    }

    private static string? NormalizeCacheFamily(string value)
    {
        var staticParts = value
            .Split(':')
            .TakeWhile(part => !part.Contains('{', StringComparison.Ordinal))
            .ToList();

        if (staticParts.Count < 2)
        {
            return null;
        }

        if (staticParts.Count >= 3 && staticParts[2] is "tenant")
        {
            return string.Join(':', staticParts.Take(3));
        }

        return string.Join(':', staticParts.Take(2));
    }

    private static IEnumerable<string> EnumerateApplicationSourceFiles()
    {
        var applicationRoot = Path.Combine(SourceRoot, "src", "Explore.Application");
        return Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string LocateSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx")) &&
                (Directory.Exists(Path.Combine(current.FullName, "Explore.Application")) ||
                 Directory.Exists(Path.Combine(current.FullName, "src", "Explore.Application"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Explore.slnx and Explore.Application.");
    }

    [GeneratedRegex("\\$?\"(?<value>[a-z][a-z0-9-]*(?::[a-zA-Z0-9_{}().?<>-]+)+)\"", RegexOptions.Compiled)]
    private static partial Regex CacheStringPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*(?:\\.[a-z][a-z0-9-]*)+$", RegexOptions.Compiled)]
    private static partial Regex SafeMetricBucketPattern();

    private sealed record CacheFamilyClassification(
        CacheKeyScope Scope,
        string MetricBucket,
        bool SafeAsRawMetricLabel)
    {
        public static CacheFamilyClassification PublicList(string metricBucket) => new(CacheKeyScope.PublicList, metricBucket, false);

        public static CacheFamilyClassification BoundedList(string metricBucket) => new(CacheKeyScope.BoundedList, metricBucket, false);

        public static CacheFamilyClassification BoundedTag(string metricBucket) => new(CacheKeyScope.BoundedTag, metricBucket, false);

        public static CacheFamilyClassification TenantScopedList(string metricBucket) => new(CacheKeyScope.TenantScopedList, metricBucket, false);

        public static CacheFamilyClassification EntityScopedList(string metricBucket) => new(CacheKeyScope.EntityScopedList, metricBucket, false);

        public static CacheFamilyClassification FilteredList(string metricBucket) => new(CacheKeyScope.FilteredList, metricBucket, false);

        public static CacheFamilyClassification EntityScoped(string metricBucket) => new(CacheKeyScope.EntityScoped, metricBucket, false);

        public static CacheFamilyClassification UserScoped(string metricBucket) => new(CacheKeyScope.UserScoped, metricBucket, false);
    }

    private enum CacheKeyScope
    {
        PublicList,
        BoundedList,
        BoundedTag,
        TenantScopedList,
        EntityScopedList,
        FilteredList,
        EntityScoped,
        UserScoped
    }
}
