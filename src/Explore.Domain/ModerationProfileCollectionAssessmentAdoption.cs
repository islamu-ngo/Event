using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Private finite independent acceptance of one exact individual assessment proposal, not
/// a legal determination, prerequisite, consent or collection receipt. Native owners prove
/// reviewer/controller identity, mandates, applicability and latest policy/slot generations.
/// The original binding, complete required set and accepted window cannot be renewed or
/// rebound. Later native PII integration must hard-delete this row, not retain recognition history.
/// </summary>
public sealed class ModerationProfileCollectionAssessmentAdoption
{
    private ModerationProfileCollectionAssessmentAdoption() { }

    public Guid Id { get; private set; }
    public ModerationProfileCollectionAssessmentSource Source { get; private set; } = null!;
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceGeneration { get; private set; }
    public Guid AdoptedByHumanId { get; private set; }
    public long Generation { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ModerationProfileCollectionAssessmentAdoption Adopt(Guid id,
        ModerationProfileCollectionAssessmentSource source,
        ModerationProfileCollectionProcedureSource procedureSource,
        ModerationProfileCollectionProcedureAdoption procedureAdoption,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption parentAdoption,
        ModerationIndividualBinding binding, Guid humanId, long generation,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.ValidatePersistedState();
        if (!source.MatchesCurrentMetadata(procedureSource, procedureAdoption, age, parentAdoption, binding, now))
            throw new ArgumentException("Assessment acceptance requires exact live source, binding, procedure and parents.");
        var adoption = new ModerationProfileCollectionAssessmentAdoption
        {
            Id = id,
            Source = source,
            SourceId = source.Id,
            SourceRevision = source.Revision,
            SourceGeneration = source.Generation,
            AdoptedByHumanId = humanId,
            Generation = generation,
            Revision = 1,
            AdoptedAt = now,
            ValidUntil = validUntil
        };
        adoption.ValidatePersistedState();
        return adoption;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (Source is null)
            throw new ArgumentException("Assessment adoption requires its original source.");
        Source.ValidatePersistedState();
        var procedure = Source.ProcedureSource;
        var subjectId = Source.Binding.Target.HumanSubject!.Id;
        if (Id == Source.Id || Source.ConflictsWithIdentity(Id)
            || SourceId != Source.Id || SourceRevision != 1 || SourceRevision != Source.Revision
            || SourceGeneration != Source.Generation
            || Generation <= SourceGeneration || Generation == long.MaxValue
            || AdoptedByHumanId != procedure.ReviewerHumanId
            || AdoptedByHumanId == Source.RecordedByHumanId
            || AdoptedByHumanId == procedure.OwnerHumanId || AdoptedByHumanId == procedure.OwnerControllerHumanId
            || procedure.ReviewerControllerHumanId == procedure.OwnerControllerHumanId
            || subjectId == AdoptedByHumanId || subjectId == procedure.ReviewerControllerHumanId
            || !ModerationProfileCollectionAssessmentSource.IsInstant(AdoptedAt)
            || !ModerationProfileCollectionAssessmentSource.IsInstant(ValidUntil)
            || AdoptedAt < Source.RecordedAt || ValidUntil > Source.ValidUntil
            || Revision is not (1 or 2)
            || Revision == 1 && (WithdrawnAt is not null || WithdrawnByHumanId is not null)
            || Revision == 2 && (WithdrawnAt is not { } withdrawn || WithdrawnByHumanId is not { } human
                || !IsWithdrawalOwner(human) || !ModerationProfileCollectionAssessmentSource.IsInstant(withdrawn)
                || withdrawn < AdoptedAt))
            throw new ArgumentException("Assessment adoption requires original source and newer same-slot generation, nominated independent non-subject review and terminal withdrawal.");
    }

    /// <summary>
    /// Structural equality with exact originals only. Native owners resolve current inputs
    /// and fence retirement/supersession; this cannot discover latest native generations.
    /// </summary>
    public bool MatchesCurrentMetadata(ModerationProfileCollectionAssessmentSource source,
        ModerationProfileCollectionProcedureSource procedureSource,
        ModerationProfileCollectionProcedureAdoption procedureAdoption,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption parentAdoption,
        ModerationIndividualBinding binding, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidatePersistedState();
        source.ValidatePersistedState();
        return Revision == 1 && WithdrawnAt is null
            && ModerationProfileCollectionAssessmentSource.IsInstant(now) && now >= AdoptedAt && now < ValidUntil
            && SourceId == source.Id && SourceRevision == source.Revision && SourceGeneration == source.Generation
            && Source.Matches(source)
            && source.MatchesCurrentMetadata(procedureSource, procedureAdoption, age, parentAdoption, binding, now);
    }

    /// <summary>
    /// Explicit accepted metadata conversion only. The native reference uses this adoption's
    /// exact ID/revision/assessment-slot generation and ORIGINAL accepted time/window, with
    /// the entire original required set. No prerequisite or collection receipt is supplied.
    /// </summary>
    public ModerationCollectionPrerequisiteAssessment ToAssessment(ModerationProfileCollectionAssessmentSource source,
        ModerationProfileCollectionProcedureSource procedureSource,
        ModerationProfileCollectionProcedureAdoption procedureAdoption,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption parentAdoption,
        ModerationIndividualBinding binding, DateTimeOffset now)
    {
        if (!MatchesCurrentMetadata(source, procedureSource, procedureAdoption, age, parentAdoption, binding, now))
            throw new InvalidOperationException("Only live independently accepted exact original assessment can supply metadata.");
        return new(Source.Binding, new(Id, Revision, Generation, AdoptedAt, ValidUntil), Source.Required);
    }

    /// <summary>
    /// Terminal exact-original-revision withdrawal by nominated reviewer/controller or
    /// collection owner/controller, authenticated by native owners. Expired or withdrawn
    /// parents and current prerequisites are deliberately unnecessary for this transition.
    /// </summary>
    public void Withdraw(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || Revision != 1 || WithdrawnAt is not null)
            throw new InvalidOperationException("Assessment withdrawal requires the original unwithdrawn adoption revision.");
        if (!IsWithdrawalOwner(humanId) || !ModerationProfileCollectionAssessmentSource.IsInstant(now) || now < AdoptedAt)
            throw new ArgumentException("Assessment withdrawal requires nominated accountable identity and nonregressing finite UTC time.");
        Revision = 2;
        WithdrawnByHumanId = humanId;
        WithdrawnAt = now;
    }

    private bool IsWithdrawalOwner(Guid humanId) =>
        humanId != Guid.Empty && (humanId == Source.ProcedureSource.ReviewerHumanId
            || humanId == Source.ProcedureSource.ReviewerControllerHumanId
            || humanId == Source.ProcedureSource.OwnerHumanId || humanId == Source.ProcedureSource.OwnerControllerHumanId);

    public override string ToString() => nameof(ModerationProfileCollectionAssessmentAdoption);
}
