namespace Explore.Domain;

/// <summary>
/// Finite review of exact material, category versions and immediate child inventory.
/// Accepted snapshots never change; correction and withdrawal are terminal, including
/// for historical-time queries. A replacement needs a new review and native current-row
/// selection. No transition creates a conduct finding, age assertion or permission.
/// Native writers must authenticate content ownership, reviewer/controller independence,
/// taxonomy and complete material inventory; distinct supplied GUIDs prove none of them.
/// </summary>
public sealed class EventContentClassification
{
    public enum ReviewState { Reviewed = 1, Withdrawn = 2, Corrected = 3 }

    private EventContentClassification(ModerationEventGeneration generation, ModerationClassificationMaterial material)
    {
        EventGeneration = generation;
        Material = material;
    }

    public Guid Id { get; private init; }
    public long Revision { get; private set; }
    public ReviewState State { get; private set; }
    public ModerationEventGeneration EventGeneration { get; }
    public ModerationClassificationMaterial Material { get; }
    public IReadOnlyList<EventContentClassificationVersion> Categories { get; private init; } = [];
    public IReadOnlyList<ModerationClassificationMaterial> RequiredChildren { get; private init; } = [];
    public Guid OwnerHumanId { get; private init; }
    public Guid OwnerControllerHumanId { get; private init; }
    public Guid ReviewerHumanId { get; private init; }
    public Guid ReviewerControllerHumanId { get; private init; }
    public long WriterGeneration { get; private init; }
    public DateTimeOffset ReviewedAt { get; private init; }
    public DateTimeOffset ValidUntil { get; private init; }
    public Guid? EndedByHumanId { get; private set; }
    public long? EndWriterGeneration { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public static EventContentClassification Review(
        Guid id, ModerationEventGeneration generation, ModerationClassificationMaterial material,
        IEnumerable<EventContentClassificationVersion> categories, IEnumerable<ModerationClassificationMaterial> requiredChildren,
        ModerationChildAgePolicySource source, ModerationChildAgePolicyAdoption adoption,
        Guid ownerHumanId, Guid ownerControllerHumanId, Guid reviewerHumanId, Guid reviewerControllerHumanId,
        long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(requiredChildren);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adoption);
        generation.ValidatePersistedState();
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        material.Validate();
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        EventContentClassificationVersion.RequireIndependentReview(ownerHumanId, ownerControllerHumanId,
            reviewerHumanId, reviewerControllerHumanId);
        ModerationQualificationPolicySource.RequireValidity(now, validUntil);
        var categorySnapshot = categories.ToArray();
        var childSnapshot = requiredChildren.ToArray();
        foreach (var child in childSnapshot)
        {
            ArgumentNullException.ThrowIfNull(child);
            child.Validate();
        }
        if (material.EventId != generation.EventId || material.EventStamp != generation.EventStamp
            || generation.HostBindingId != source.HostBindingId || generation.TrustDomainId != source.TrustDomainId
            || generation.TenantId != source.TenantId || reviewerHumanId == generation.SubmittedByHumanId
            || reviewerControllerHumanId == generation.SubmittedByHumanId
            || now < generation.ObservedAt || validUntil > generation.ValidUntil
            || !adoption.MatchesCurrent(source, now) || validUntil > adoption.ValidUntil
            || categorySnapshot.Length == 0 || categorySnapshot.Any(category => category is null
                || !category.MatchesPolicy(source, adoption, now) || validUntil > category.ValidUntil
                || writerGeneration <= category.WriterGeneration)
            || categorySnapshot.Select(category => (category.TaxonomyId, category.CategoryId)).Distinct().Count() != categorySnapshot.Length
            || categorySnapshot.Select(category => category.Id).Distinct().Count() != categorySnapshot.Length
            || childSnapshot.Any(child => child.Parent != material) || childSnapshot.Distinct().Count() != childSnapshot.Length
            || writerGeneration <= generation.WriterGeneration || writerGeneration == long.MaxValue)
            throw new ArgumentException("Material review requires exact Event ancestry, independent review, unique categories and children, and finite current source coverage.");

        return new(generation, material)
        {
            Id = id,
            Revision = 1,
            State = ReviewState.Reviewed,
            Categories = Array.AsReadOnly(categorySnapshot),
            RequiredChildren = Array.AsReadOnly(childSnapshot),
            OwnerHumanId = ownerHumanId,
            OwnerControllerHumanId = ownerControllerHumanId,
            ReviewerHumanId = reviewerHumanId,
            ReviewerControllerHumanId = reviewerControllerHumanId,
            WriterGeneration = writerGeneration,
            ReviewedAt = now,
            ValidUntil = validUntil
        };
    }

    /// <summary>
    /// Compares supplied current coordinates with the retained review. Callers must acquire
    /// both through native owners; this method cannot establish completeness or authenticity.
    /// </summary>
    public bool MatchesCurrent(
        ModerationEventGeneration generation, ModerationClassificationMaterial material,
        IEnumerable<EventContentClassificationVersion> categories, IEnumerable<ModerationClassificationMaterial> requiredChildren,
        ModerationChildAgePolicySource source, ModerationChildAgePolicyAdoption adoption, DateTimeOffset now)
    {
        if (State != ReviewState.Reviewed || Revision != 1 || now.Offset != TimeSpan.Zero
            || now < ReviewedAt || now >= ValidUntil || Material != material
            || EventGeneration.Id != generation.Id || EventGeneration.Revision != generation.Revision
            || EventGeneration.WriterGeneration != generation.WriterGeneration
            || EventGeneration.ObservedAt != generation.ObservedAt || EventGeneration.ValidUntil != generation.ValidUntil
            || EventGeneration.ObservedByHumanId != generation.ObservedByHumanId
            || !EventGeneration.HasSameNativeVersion(generation)
            || now < generation.ObservedAt || now >= generation.ValidUntil
            || Categories.Any(category => !category.MatchesPolicy(source, adoption, now)))
            return false;
        var currentCategories = categories.ToArray();
        var currentChildren = requiredChildren.ToArray();
        return currentCategories.Length == Categories.Count && currentCategories.Distinct().Count() == currentCategories.Length
            && Categories.ToHashSet().SetEquals(currentCategories)
            && currentChildren.Length == RequiredChildren.Count && currentChildren.Distinct().Count() == currentChildren.Length
            && RequiredChildren.ToHashSet().SetEquals(currentChildren);
    }

    public void Withdraw(long expectedRevision, Guid humanId, long writerGeneration, DateTimeOffset now) =>
        End(ReviewState.Withdrawn, expectedRevision, humanId, writerGeneration, now);

    public void Correct(long expectedRevision, Guid humanId, long writerGeneration, DateTimeOffset now) =>
        End(ReviewState.Corrected, expectedRevision, humanId, writerGeneration, now);

    private void End(ReviewState state, long expectedRevision, Guid humanId, long writerGeneration, DateTimeOffset now)
    {
        if (Revision != expectedRevision || State != ReviewState.Reviewed)
            throw new InvalidOperationException("Classification correction or withdrawal requires the exact current unended review.");
        if (humanId == Guid.Empty || writerGeneration <= WriterGeneration || writerGeneration == long.MaxValue
            || now.Offset != TimeSpan.Zero || now < ReviewedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Ending a review requires accountable authority, a newer generation and non-regressing UTC time.");
        Revision = checked(Revision + 1);
        State = state;
        EndedByHumanId = humanId;
        EndWriterGeneration = writerGeneration;
        EndedAt = now;
    }

    public override string ToString() => nameof(EventContentClassification);
}

/// <summary>
/// Payload-free exact material ancestry. Null child pairs identify the Event itself;
/// a session pair identifies a session; a resource pair identifies a resource under
/// that session or directly under the Event. Native GUID stamps are never counters.
/// </summary>
public sealed record ModerationClassificationMaterial(
    Guid EventId, Guid EventStamp, Guid? SessionId, Guid? SessionStamp, Guid? ResourceId, Guid? ResourceStamp)
{
    public ModerationClassificationMaterial? Parent => ResourceId is not null
        ? this with { ResourceId = null, ResourceStamp = null }
        : SessionId is not null ? this with { SessionId = null, SessionStamp = null } : null;

    public void Validate()
    {
        if (EventId == Guid.Empty || EventStamp == Guid.Empty || SessionId == Guid.Empty || SessionStamp == Guid.Empty
            || ResourceId == Guid.Empty || ResourceStamp == Guid.Empty || SessionId.HasValue != SessionStamp.HasValue
            || ResourceId.HasValue != ResourceStamp.HasValue)
            throw new ArgumentException("Material ancestry requires the exact Event and paired native session/resource identities and GUID stamps.");
    }

    public override string ToString() => nameof(ModerationClassificationMaterial);
}
