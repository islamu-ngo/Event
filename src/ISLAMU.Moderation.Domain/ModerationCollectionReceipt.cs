namespace ISLAMU.Moderation.Domain;

public enum ModerationCollectionPrerequisiteKind { AccountEligibility = 1, SubjectConsent = 2, GuardianConsent = 3 }

/// <summary>
/// A native applicability determination for this individual and exact purpose/policy observation.
/// The native owner derives the required set, including a positively determined empty set.
/// This structure checks completeness, not whether the determination is lawful or authentic.
/// </summary>
public sealed class ModerationCollectionPrerequisiteAssessment
{
    public ModerationCollectionPrerequisiteAssessment(ModerationIndividualBinding binding,
        ModerationIndividualNativeReference reference, IEnumerable<ModerationCollectionPrerequisiteKind> required)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(required);
        var kinds = required.Order().ToArray();
        if (kinds.Any(kind => !Enum.IsDefined(kind)) || kinds.Distinct().Count() != kinds.Length)
            throw new ArgumentException("A native assessment requires distinct known prerequisite kinds.", nameof(required));
        Binding = binding;
        Reference = reference;
        Required = Array.AsReadOnly(kinds);
    }

    public ModerationIndividualBinding Binding { get; }
    public ModerationIndividualNativeReference Reference { get; }
    public IReadOnlyList<ModerationCollectionPrerequisiteKind> Required { get; }

    internal bool Matches(ModerationCollectionPrerequisiteAssessment other) =>
        Binding == other.Binding && Reference == other.Reference && Required.SequenceEqual(other.Required);

    public override string ToString() => nameof(ModerationCollectionPrerequisiteAssessment);
}

/// <summary>
/// Native-bound prerequisite coordinates, never a consent flag, guardian identity assertion
/// or purchaser age. Native consent/guardian owners authenticate applicability, actual subject,
/// purpose, accepted policy, relationship, current generation and withdrawal before use.
/// </summary>
public sealed record ModerationCollectionPrerequisite
{
    public ModerationCollectionPrerequisite(ModerationIndividualBinding binding,
        ModerationCollectionPrerequisiteKind kind, ModerationIndividualNativeReference reference)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(reference);
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("A collection prerequisite requires a known native kind.", nameof(kind));
        Binding = binding;
        Kind = kind;
        Reference = reference;
    }

    public ModerationIndividualBinding Binding { get; }
    public ModerationCollectionPrerequisiteKind Kind { get; }
    public ModerationIndividualNativeReference Reference { get; }
    public override string ToString() => nameof(ModerationCollectionPrerequisite);
}

/// <summary>
/// Finite individual collection metadata, not a signed permit, activation, measurement receipt
/// or sensitive-body acceptance endpoint. Native owners must authenticate source/adoption,
/// applicability and each prerequisite, then enforce the gate before requesting or accepting PII.
/// Currentness can only narrow the original window; retirement cannot be reversed or renewed.
/// </summary>
public sealed class ModerationCollectionReceipt
{
    private ModerationCollectionReceipt(Guid id, ModerationIndividualBinding binding,
        ModerationCollectionPrerequisiteAssessment assessment, ModerationCollectionPrerequisite[] prerequisites,
        DateTimeOffset recordedAt, DateTimeOffset validUntil)
    {
        Id = id;
        Binding = binding;
        Assessment = assessment;
        Prerequisites = Array.AsReadOnly(prerequisites);
        RecordedAt = recordedAt;
        UpdatedAt = recordedAt;
        ValidUntil = validUntil;
        State = ModerationIndividualRecordState.Current;
        Revision = 1;
    }

    public Guid Id { get; }
    public ModerationIndividualBinding? Binding { get; private set; }
    public ModerationCollectionPrerequisiteAssessment? Assessment { get; private set; }
    public IReadOnlyList<ModerationCollectionPrerequisite> Prerequisites { get; private set; }
    public DateTimeOffset RecordedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationIndividualRecordState State { get; private set; }
    public long Revision { get; private set; }

    public static ModerationCollectionReceipt Record(
        Guid id, ModerationIndividualBinding binding, ModerationCollectionPrerequisiteAssessment assessment,
        IEnumerable<ModerationCollectionPrerequisite> prerequisites, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ModerationPolicy.RequireVersion7(id, nameof(id));
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(prerequisites);
        ModerationIndividualValidity.RequireWindow(now, validUntil);
        var supplied = prerequisites.ToArray();
        if (binding.Purpose is not (ModerationIndividualPurpose.IdentityEnrollment
                or ModerationIndividualPurpose.ProfileCompletion or ModerationIndividualPurpose.ParticipantCollection)
            || assessment.Binding != binding || now < binding.ObservedAt || validUntil > binding.ValidUntil
            || !assessment.Reference.Covers(now, validUntil)
            || supplied.Any(item => item is null || item.Binding != binding || !item.Reference.Covers(now, validUntil)))
            throw new ArgumentException("Collection requires exact native bindings and validity within every prerequisite.");
        var ordered = supplied.OrderBy(item => item.Kind).ToArray();
        if (!assessment.Required.SequenceEqual(ordered.Select(item => item.Kind))
            || ordered.Select(item => item.Reference.Id).Distinct().Count() != ordered.Length
            || ordered.Any(item => item.Reference.Id == assessment.Reference.Id))
            throw new ArgumentException("Collection requires each assessed prerequisite exactly once with distinct native records.");
        return new(id, binding, assessment, ordered, now, validUntil);
    }

    /// <summary>
    /// Structural currentness only. Inputs must come from current native owners, not be echoed
    /// from this receipt. Replaced, missing, withdrawn or changed-purpose prerequisites cannot
    /// reuse this metadata; account eligibility never substitutes for consent.
    /// </summary>
    public bool MatchesCurrentMetadata(
        ModerationIndividualBinding currentBinding, ModerationCollectionPrerequisiteAssessment currentAssessment,
        IEnumerable<ModerationCollectionPrerequisite> currentPrerequisites, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(currentBinding);
        ArgumentNullException.ThrowIfNull(currentAssessment);
        ArgumentNullException.ThrowIfNull(currentPrerequisites);
        if (State != ModerationIndividualRecordState.Current || Binding != currentBinding
            || Assessment is null || !Assessment.Matches(currentAssessment)
            || !ModerationIndividualValidity.Contains(RecordedAt, ValidUntil, now))
            return false;
        var supplied = currentPrerequisites.ToArray();
        return supplied.All(item => item is not null)
            && Prerequisites.SequenceEqual(supplied.OrderBy(item => item.Kind));
    }

    /// <summary>Retires metadata without acquiring DOB or replaying the original consent proof.</summary>
    public void Retire(long expectedRevision, ModerationIndividualRecordState state, DateTimeOffset now)
    {
        ModerationIndividualValidity.RequireRetirement(State, state, Revision, expectedRevision, UpdatedAt, ValidUntil, now);
        Binding = null;
        Assessment = null;
        Prerequisites = Array.Empty<ModerationCollectionPrerequisite>();
        State = state;
        UpdatedAt = now;
        Revision = checked(Revision + 1);
    }

    public override string ToString() => nameof(ModerationCollectionReceipt);
}
