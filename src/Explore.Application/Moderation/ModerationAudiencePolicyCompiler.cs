using Explore.Domain;

namespace Explore.Application.Moderation;

/// <summary>
/// Pure whole-event compilation of supplied current metadata, never an access decision.
/// Native readers must authenticate taxonomy, owners/reviewers/controllers, source/adoption,
/// current review selection and the complete material inventory on the original transaction.
/// Event observations and their target adoptions do not approve material or classification.
/// CurrentMetadata is not a qualification receipt, assurance, consent, activation or permission.
/// Every session/resource contributes to the whole-event maximum. This compiler has no
/// segregation bypass: previews, entitlements, doors and safe parent summaries need the
/// separately approved complete conformance contract, not a flag on this metadata.
/// </summary>
public static class ModerationAudiencePolicyCompiler
{
    public enum CompilationStatus { ReviewRequired = 1, CurrentMetadata = 2 }

    /// <summary>
    /// No partial floor is exposed on uncertainty: even the known maximum could understate
    /// an unknown child. ValidUntil bounds metadata freshness only, not an authorization lease.
    /// </summary>
    public sealed record Compilation
    {
        internal Compilation(CompilationStatus status, int? wholeEventMinimumAge, DateTimeOffset? validUntil)
        {
            Status = status;
            WholeEventMinimumAge = wholeEventMinimumAge;
            ValidUntil = validUntil;
        }

        public CompilationStatus Status { get; }
        public int? WholeEventMinimumAge { get; }
        public DateTimeOffset? ValidUntil { get; }
    }

    /// <summary>
    /// Each material entry supplies its currently selected category versions; an empty
    /// selection is unknown, not General. Reviews additionally bind exact immediate children,
    /// so additions, removals, reparenting and stamp changes invalidate retained review.
    /// The current taxonomy excludes withdrawn/superseded versions. Malformed coordinates
    /// are rejected; missing, stale or inconsistent supplied metadata requires review.
    /// </summary>
    public static Compilation Compile(
        ModerationEventGeneration generation,
        IReadOnlyDictionary<ModerationClassificationMaterial, IReadOnlyCollection<EventContentClassificationVersion>> currentMaterials,
        IEnumerable<EventContentClassificationVersion> currentTaxonomy,
        IEnumerable<EventContentClassification> currentReviews,
        ModerationChildAgePolicySource? source, ModerationChildAgePolicyAdoption? adoption, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(currentMaterials);
        ArgumentNullException.ThrowIfNull(currentTaxonomy);
        ArgumentNullException.ThrowIfNull(currentReviews);
        generation.ValidatePersistedState();
        if (source is null || adoption is null || now.Offset != TimeSpan.Zero
            || now < generation.ObservedAt || now >= generation.ValidUntil)
            return ReviewRequired();
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(source, now) || source.HostBindingId != generation.HostBindingId
            || source.TrustDomainId != generation.TrustDomainId || source.TenantId != generation.TenantId
            || currentMaterials.Any(pair => pair.Key is null || pair.Value is null))
            return ReviewRequired();

        var materials = currentMaterials.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var taxonomy = currentTaxonomy.ToArray();
        var reviews = currentReviews.ToArray();
        var root = new ModerationClassificationMaterial(generation.EventId, generation.EventStamp, null, null, null, null);
        if (!materials.ContainsKey(root) || taxonomy.Any(category => category is null)
            || reviews.Any(review => review is null) || reviews.Length != materials.Count
            || reviews.Select(review => review.Id).Distinct().Count() != reviews.Length
            || reviews.Select(review => review.Material).Distinct().Count() != reviews.Length
            || taxonomy.Select(category => (category.TaxonomyId, category.CategoryId)).Distinct().Count() != taxonomy.Length
            || taxonomy.Select(category => (category.TaxonomyId, category.Code)).Distinct().Count() != taxonomy.Length
            || taxonomy.Select(category => category.Id).Distinct().Count() != taxonomy.Length)
            return ReviewRequired();

        foreach (var material in materials.Keys)
        {
            material.Validate();
            if (material.EventId != root.EventId || material.EventStamp != root.EventStamp
                || material.Parent is { } parent && !materials.ContainsKey(parent))
                return ReviewRequired();
        }
        var sessions = materials.Keys.Where(material => material.SessionId is not null && material.ResourceId is null).ToArray();
        var resources = materials.Keys.Where(material => material.ResourceId is not null).ToArray();
        if (sessions.Select(material => material.SessionId).Distinct().Count() != sessions.Length
            || resources.Select(material => material.ResourceId).Distinct().Count() != resources.Length)
            return ReviewRequired();

        var byMaterial = reviews.ToDictionary(review => review.Material);
        var children = materials.Keys.ToLookup(material => material.Parent);
        var taxonomyVersions = taxonomy.ToHashSet();
        var minimumAge = 0;
        var validUntil = generation.ValidUntil < adoption.ValidUntil ? generation.ValidUntil : adoption.ValidUntil;
        foreach (var (material, categories) in materials)
        {
            if (categories.Length == 0 || categories.Any(category => category is null || !taxonomyVersions.Contains(category)
                    || !category.MatchesPolicy(source, adoption, now))
                || !byMaterial.TryGetValue(material, out var review)
                || !review.MatchesCurrent(generation, material, categories, children[material], source, adoption, now))
                return ReviewRequired();

            foreach (var category in categories)
            {
                minimumAge = Math.Max(minimumAge, category.MinimumAge);
                if (category.Kind == EventContentClassificationVersion.CategoryKind.Debate)
                    minimumAge = Math.Max(minimumAge, source.DebateMinimumAge);
            }
            if (review.ValidUntil < validUntil) validUntil = review.ValidUntil;
        }
        return new(CompilationStatus.CurrentMetadata, minimumAge, validUntil);
    }

    private static Compilation ReviewRequired() => new(CompilationStatus.ReviewRequired, null, null);
}
