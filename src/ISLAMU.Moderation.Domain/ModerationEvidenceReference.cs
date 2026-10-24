namespace ISLAMU.Moderation.Domain;

/// <summary>
/// Payload-free provenance for one material fact about an allegation occurrence.
/// This is neither permission nor verified actor attribution. Native storage owns occurrence
/// and material-fact allocation, source authentication, confidentiality and case binding.
/// A copied report, provider opinion or possession of these coordinates grants no authority.
/// </summary>
public sealed record ModerationEvidenceReference
{
    private ModerationEvidenceReference(
        ModerationCaseTarget target, AuthorityScope sourceOrigin, Guid occurrenceId,
        Guid sourceId, Guid sourceVersion, Guid materialFactId)
    {
        Target = target;
        SourceOrigin = sourceOrigin;
        OccurrenceId = occurrenceId;
        SourceId = sourceId;
        SourceVersion = sourceVersion;
        MaterialFactId = materialFactId;
    }

    public ModerationCaseTarget Target { get; }
    public AuthorityScope SourceOrigin { get; }
    public Guid OccurrenceId { get; }
    public Guid SourceId { get; }
    public Guid SourceVersion { get; }
    public Guid MaterialFactId { get; }

    /// <summary>
    /// Exact value equality preserves source and target generations and distinct material facts.
    /// It is not a duplicate-submission policy: contact details and alleged actor identity are
    /// deliberately absent, and a native owner must qualify all references before deduplicating.
    /// </summary>
    public static ModerationEvidenceReference Create(
        ModerationCaseTarget target, AuthorityScope sourceOrigin, Guid occurrenceId,
        Guid sourceId, Guid sourceVersion, Guid materialFactId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceOrigin);
        if (occurrenceId == Guid.Empty || sourceId == Guid.Empty
            || sourceVersion == Guid.Empty || materialFactId == Guid.Empty)
            throw new ArgumentException("Complete occurrence and material provenance are required.", nameof(occurrenceId));
        if (sourceOrigin.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || !sourceOrigin.Contains(target.Origin) && !target.Origin.Contains(sourceOrigin))
            throw new ArgumentException("Evidence provenance must retain the exact native ancestry.", nameof(sourceOrigin));

        return new(target, sourceOrigin, occurrenceId, sourceId, sourceVersion, materialFactId);
    }

    public override string ToString() => nameof(ModerationEvidenceReference);
}
