using System.Collections.Immutable;

namespace Explore.Domain;

/// <summary>
/// An immutable whole-Event proposal, not acceptance. A newer proposal replaces the entire
/// material inventory before acceptance is considered; no reader may reveal an older approval.
/// Ownership, completeness and substantive review qualification must be authenticated natively.
/// </summary>
public sealed class ModerationClassificationMaterialSource
{
    private ModerationClassificationMaterialSource() { }
    private readonly List<ModerationClassificationMaterialEntry> _materials = [];
    private readonly List<ModerationClassificationMaterialComponent> _components = [];
    public IReadOnlyList<ModerationClassificationMaterialEntry> Materials => _materials.AsReadOnly();
    public IReadOnlyList<ModerationClassificationMaterialComponent> Components => _components.AsReadOnly();
    public Guid Id { get; private set; }
    public long Revision { get; private set; }
    public long PreviousGeneration { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventStamp { get; private set; }
    public Guid EventGenerationId { get; private set; }
    public long EventGenerationRevision { get; private set; }
    public long EventWriterGeneration { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public Guid TaxonomyId { get; private set; }
    public Guid TaxonomySourceId { get; private set; }
    public long TaxonomySourceRevision { get; private set; }
    public long TaxonomySourceGeneration { get; private set; }
    public Guid TaxonomyAdoptionId { get; private set; }
    public long TaxonomyAdoptionRevision { get; private set; }
    public long TaxonomyAdoptionGeneration { get; private set; }
    public Guid OwnerHumanId { get; private set; }
    public Guid OwnerControllerHumanId { get; private set; }
    public Guid OwnerAssignmentId { get; private set; }
    public long OwnerAssignmentRevision { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public string OwnershipGrounds { get; private set; } = string.Empty;
    public string ReviewGrounds { get; private set; } = string.Empty;
    public long WriterGeneration { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public ModerationClassificationMaterialMandate Mandate => new(OwnerHumanId, OwnerControllerHumanId,
        OwnerAssignmentId, OwnerAssignmentRevision, ReviewerHumanId, ReviewerControllerHumanId, OwnershipGrounds, ReviewGrounds);

    public static ModerationClassificationMaterialSource Record(
        Guid id, long previousGeneration, ModerationEventGeneration generation,
        ModerationClassificationTaxonomySource taxonomy, ModerationClassificationTaxonomyAdoption taxonomyAdoption,
        ModerationClassificationMaterialMandate mandate, IEnumerable<ModerationClassificationMaterialSelection> materials,
        IEnumerable<ModerationClassificationMaterialComponentVersion> components,
        long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        generation.ValidatePersistedState();
        taxonomy.ValidatePersistedState();
        taxonomyAdoption.ValidatePersistedState();
        mandate.Validate();
        var source = new ModerationClassificationMaterialSource
        {
            Id = id, Revision = 1, PreviousGeneration = previousGeneration,
            HostBindingId = generation.HostBindingId, TrustDomainId = generation.TrustDomainId, TenantId = generation.TenantId,
            EventId = generation.EventId, EventStamp = generation.EventStamp, EventGenerationId = generation.Id,
            EventGenerationRevision = generation.Revision, EventWriterGeneration = generation.WriterGeneration,
            PolicyId = taxonomy.PolicyId, PolicyRevision = taxonomy.PolicyRevision, PublicationId = taxonomy.PublicationId,
            ProfileId = taxonomy.ProfileId, ModeCode = taxonomy.ModeCode, TaxonomyId = taxonomy.TaxonomyId,
            TaxonomySourceId = taxonomy.Id, TaxonomySourceRevision = taxonomy.Revision, TaxonomySourceGeneration = taxonomy.WriterGeneration,
            TaxonomyAdoptionId = taxonomyAdoption.Id, TaxonomyAdoptionRevision = taxonomyAdoption.Revision,
            TaxonomyAdoptionGeneration = taxonomyAdoption.WriterGeneration,
            OwnerHumanId = mandate.OwnerHumanId, OwnerControllerHumanId = mandate.OwnerControllerHumanId,
            OwnerAssignmentId = mandate.OwnerAssignmentId, OwnerAssignmentRevision = mandate.OwnerAssignmentRevision,
            ReviewerHumanId = mandate.ReviewerHumanId, ReviewerControllerHumanId = mandate.ReviewerControllerHumanId,
            OwnershipGrounds = mandate.OwnershipGrounds, ReviewGrounds = mandate.ReviewGrounds,
            WriterGeneration = writerGeneration, RecordedAt = now, ValidUntil = validUntil
        };
        source._materials.AddRange(materials.Take(4097).Select((item, index) => ModerationClassificationMaterialEntry.Capture(id, index, item)));
        source._components.AddRange(components.Take(4097).Select((item, index) => ModerationClassificationMaterialComponent.Capture(id, index, item)));
        source.ValidatePersistedState();
        if (mandate.ReviewerHumanId == generation.SubmittedByHumanId || mandate.ReviewerControllerHumanId == generation.SubmittedByHumanId
            || !source.MatchesCurrent(generation, taxonomy, taxonomyAdoption,
                source.Materials.Select(row => row.Material), source.Components.Select(row => row.Version), now)
            || source.Materials.SelectMany(row => row.Categories).Any(row => !taxonomy.Categories.Any(category => category.Id == row.VersionId)))
            throw new ArgumentException("Material proposal requires exact finite Event, accepted taxonomy and independent publisher review.");
        return source;
    }

    public bool MatchesCurrent(ModerationEventGeneration generation,
        ModerationClassificationTaxonomySource taxonomy, ModerationClassificationTaxonomyAdoption taxonomyAdoption,
        IEnumerable<ModerationClassificationMaterial> materials,
        IEnumerable<ModerationClassificationMaterialComponentVersion> components, DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < RecordedAt || now >= ValidUntil
            || EventGenerationId != generation.Id || EventGenerationRevision != generation.Revision
            || EventWriterGeneration != generation.WriterGeneration || EventId != generation.EventId || EventStamp != generation.EventStamp
            || HostBindingId != generation.HostBindingId || TrustDomainId != generation.TrustDomainId || TenantId != generation.TenantId
            || HostBindingId != taxonomy.HostBindingId || TrustDomainId != taxonomy.TrustDomainId || TenantId != taxonomy.TenantId
            || now < generation.ObservedAt || ValidUntil > generation.ValidUntil
            || TaxonomyId != taxonomy.TaxonomyId || TaxonomySourceId != taxonomy.Id || TaxonomySourceRevision != taxonomy.Revision
            || TaxonomySourceGeneration != taxonomy.WriterGeneration || TaxonomyAdoptionId != taxonomyAdoption.Id
            || TaxonomyAdoptionRevision != taxonomyAdoption.Revision || TaxonomyAdoptionGeneration != taxonomyAdoption.WriterGeneration
            || PolicyId != taxonomy.PolicyId || PolicyRevision != taxonomy.PolicyRevision || PublicationId != taxonomy.PublicationId
            || ProfileId != taxonomy.ProfileId || ModeCode != taxonomy.ModeCode || !taxonomyAdoption.MatchesCurrent(taxonomy, now)
            || RecordedAt < taxonomyAdoption.AdoptedAt || ValidUntil > taxonomyAdoption.ValidUntil)
            return false;
        var currentMaterials = materials.ToArray();
        var currentComponents = components.ToArray();
        return currentMaterials.Length == Materials.Count && currentMaterials.Distinct().Count() == currentMaterials.Length
            && Materials.Select(row => row.Material).ToHashSet().SetEquals(currentMaterials)
            && currentComponents.Length == Components.Count && currentComponents.Distinct().Count() == currentComponents.Length
            && Components.Select(row => row.Version).ToHashSet().SetEquals(currentComponents);
    }

    public ImmutableArray<EventContentClassification> Project(
        ModerationClassificationMaterialReview review, ModerationEventGeneration generation,
        ModerationClassificationTaxonomySource taxonomy, ModerationClassificationTaxonomyAdoption taxonomyAdoption,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption ageAdoption, DateTimeOffset now)
    {
        if (!review.MatchesCurrent(this, now) || !MatchesCurrent(generation, taxonomy, taxonomyAdoption,
            Materials.Select(row => row.Material), Components.Select(row => row.Version), now))
            throw new InvalidOperationException("Only the exact current accepted material snapshot can be projected.");
        var categories = taxonomy.Project(age, ageAdoption).ToDictionary(row => row.Id);
        return Materials.Select(row => EventContentClassification.Review(row.Id, generation, row.Material,
            row.Categories.Select(category => categories[category.VersionId]),
            Materials.Where(child => child.Material.Parent == row.Material).Select(child => child.Material),
            age, ageAdoption, OwnerHumanId, OwnerControllerHumanId, ReviewerHumanId, ReviewerControllerHumanId,
            review.WriterGeneration, review.AcceptedAt, review.ValidUntil)).ToImmutableArray();
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, EventGenerationId, PolicyId, PublicationId,
            ProfileId, TaxonomyId, TaxonomySourceId, TaxonomyAdoptionId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Mandate.Validate();
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (Revision != 1 || PreviousGeneration < 0 || EventId == Guid.Empty || EventStamp == Guid.Empty
            || EventGenerationRevision is < 1 or long.MaxValue || EventWriterGeneration < 1
            || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || TaxonomySourceRevision != 1 || TaxonomyAdoptionRevision != 1 || TaxonomySourceGeneration < 1
            || TaxonomyAdoptionGeneration <= TaxonomySourceGeneration
            || WriterGeneration <= Math.Max(PreviousGeneration, Math.Max(EventWriterGeneration, TaxonomyAdoptionGeneration))
            || WriterGeneration == long.MaxValue || Materials.Count is < 1 or > 4096 || Components.Count is < 1 or > 4096)
            throw new ArgumentException("Material proposal requires bounded immutable native and taxonomy generations.");
        var materials = Materials.Select(row => row.Material).ToHashSet();
        var root = new ModerationClassificationMaterial(EventId, EventStamp, null, null, null, null);
        if (materials.Count != Materials.Count || !materials.Contains(root)
            || Materials.Select(row => row.Id).Distinct().Count() != Materials.Count
            || Materials.Where(row => row.ResourceId is not null).Select(row => row.ResourceId).Distinct().Count()
                != Materials.Count(row => row.ResourceId is not null)
            || Components.Select(row => (row.Kind, row.NativeId)).Distinct().Count() != Components.Count)
            throw new ArgumentException("Material inventory requires unique complete native identities.");
        for (var index = 0; index < Materials.Count; index++)
        {
            var row = Materials[index];
            row.Selection.Validate();
            if (row.SourceId != Id || row.Ordinal != index || row.EventId != EventId || row.EventStamp != EventStamp
                || row.Material.Parent is { } parent && !materials.Contains(parent)
                || row.Categories.Where((category, ordinal) => category.EntryId != row.Id || category.Ordinal != ordinal).Any())
                throw new ArgumentException("Every material must retain its exact immediate native parent and selected categories.");
        }
        for (var index = 0; index < Components.Count; index++)
        {
            var row = Components[index];
            row.Version.Validate();
            if (row.SourceId != Id || row.Ordinal != index)
                throw new ArgumentException("Component does not belong to this immutable inventory.");
        }
        foreach (var material in materials)
        {
            var kind = material.ResourceId is not null ? ModerationClassificationMaterialComponentKind.Resource
                : material.SessionId is not null ? ModerationClassificationMaterialComponentKind.Session
                : ModerationClassificationMaterialComponentKind.Event;
            var id = material.ResourceId ?? material.SessionId ?? material.EventId;
            var stamp = material.ResourceStamp ?? material.SessionStamp ?? material.EventStamp;
            if (!Components.Any(row => row.Kind == kind && row.NativeId == id && row.NativeStamp == stamp))
                throw new ArgumentException("Material GUID ancestry must match its native component stamp.");
        }
    }

    public override string ToString() => nameof(ModerationClassificationMaterialSource);
}

public sealed class ModerationClassificationMaterialEntry
{
    private ModerationClassificationMaterialEntry() { }
    private readonly List<ModerationClassificationMaterialCategory> _categories = [];
    public IReadOnlyList<ModerationClassificationMaterialCategory> Categories => _categories.AsReadOnly();
    public Guid Id { get; private set; }
    public Guid SourceId { get; private set; }
    public int Ordinal { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventStamp { get; private set; }
    public Guid? SessionId { get; private set; }
    public Guid? SessionStamp { get; private set; }
    public Guid? ResourceId { get; private set; }
    public Guid? ResourceStamp { get; private set; }
    public ModerationClassificationMaterial Material => new(EventId, EventStamp, SessionId, SessionStamp, ResourceId, ResourceStamp);
    public ModerationClassificationMaterialSelection Selection => new(Id, Material, Categories.Select(row => row.VersionId).ToImmutableArray());
    internal static ModerationClassificationMaterialEntry Capture(Guid sourceId, int ordinal, ModerationClassificationMaterialSelection selection)
    {
        selection.Validate();
        var material = selection.Material;
        var entry = new ModerationClassificationMaterialEntry { Id = selection.ReviewId, SourceId = sourceId, Ordinal = ordinal,
            EventId = material.EventId, EventStamp = material.EventStamp, SessionId = material.SessionId, SessionStamp = material.SessionStamp,
            ResourceId = material.ResourceId, ResourceStamp = material.ResourceStamp };
        entry._categories.AddRange(selection.CategoryVersionIds.Select((id, index) => ModerationClassificationMaterialCategory.Capture(entry.Id, index, id)));
        return entry;
    }
}

public sealed class ModerationClassificationMaterialCategory
{
    private ModerationClassificationMaterialCategory() { }
    public Guid EntryId { get; private set; }
    public int Ordinal { get; private set; }
    public Guid VersionId { get; private set; }
    internal static ModerationClassificationMaterialCategory Capture(Guid entry, int ordinal, Guid version) =>
        new() { EntryId = entry, Ordinal = ordinal, VersionId = version };
}

public sealed class ModerationClassificationMaterialComponent
{
    private ModerationClassificationMaterialComponent() { }
    public Guid SourceId { get; private set; }
    public int Ordinal { get; private set; }
    public ModerationClassificationMaterialComponentKind Kind { get; private set; }
    public Guid NativeId { get; private set; }
    public Guid? NativeStamp { get; private set; }
    public string Digest { get; private set; } = string.Empty;
    public ModerationClassificationMaterialComponentVersion Version => new(Kind, NativeId, NativeStamp, Digest);
    internal static ModerationClassificationMaterialComponent Capture(Guid sourceId, int ordinal,
        ModerationClassificationMaterialComponentVersion version) =>
        new() { SourceId = sourceId, Ordinal = ordinal, Kind = version.Kind, NativeId = version.NativeId,
            NativeStamp = version.NativeStamp, Digest = version.Digest };
}
