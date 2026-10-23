namespace ISLAMU.Moderation.Domain;

/// <summary>
/// Exact event, participant and adopted policy coordinates for one rescheduled review.
/// ScheduleGeneration and ParticipantGeneration are the native owners' opaque ConcurrencyStamp
/// values, compared only for equality. Policy publication is distinct from rule definition revision.
/// These values identify source state; they prove neither event-relevant risk nor permission.
/// </summary>
public sealed record ModerationScheduleRiskCoordinates
{
    private ModerationScheduleRiskCoordinates(AuthorityScope eventScope, Guid sessionId, Guid scheduleGeneration,
        Guid participantId, SubjectReference participant, Guid participantGeneration,
        Guid policyId, long policyRevision, Guid publicationId, ModerationRuleReference policy)
    {
        EventScope = eventScope;
        SessionId = sessionId;
        ScheduleGeneration = scheduleGeneration;
        ParticipantId = participantId;
        Participant = participant;
        ParticipantGeneration = participantGeneration;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        PublicationId = publicationId;
        Policy = policy;
    }

    public AuthorityScope EventScope { get; }
    public Guid SessionId { get; }
    public Guid ScheduleGeneration { get; }
    public Guid ParticipantId { get; }
    public SubjectReference Participant { get; }
    public Guid ParticipantGeneration { get; }
    public Guid PolicyId { get; }
    public long PolicyRevision { get; }
    public Guid PublicationId { get; }
    public ModerationRuleReference Policy { get; }

    public static ModerationScheduleRiskCoordinates Create(AuthorityScope eventScope, Guid sessionId, Guid scheduleGeneration,
        Guid participantId, SubjectReference participant, Guid participantGeneration,
        Guid policyId, long policyRevision, Guid publicationId, ModerationRuleReference policy)
    {
        ArgumentNullException.ThrowIfNull(eventScope);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(policy);
        if (eventScope.Kind != ModerationScopeKind.Event || sessionId == Guid.Empty || scheduleGeneration == Guid.Empty
            || participantId == Guid.Empty || participantGeneration == Guid.Empty
            || policyId == Guid.Empty || policyRevision < 1 || publicationId == Guid.Empty
            || participant.Kind != ModerationSubjectKind.Human || participant.TrustDomainId != eventScope.TrustDomainId)
            throw new ArgumentException("Review coordinates require an exact event and current canonical participant and policy revisions.");

        return new(eventScope, sessionId, scheduleGeneration, participantId, participant, participantGeneration,
            policyId, policyRevision, publicationId, policy);
    }

    internal bool IsSuccessorOf(ModerationScheduleRiskCoordinates previous) =>
        EventScope == previous.EventScope && SessionId == previous.SessionId && ParticipantId == previous.ParticipantId
        && Participant == previous.Participant && PolicyId == previous.PolicyId && Policy.OwnerId == previous.Policy.OwnerId
        && Policy.Code == previous.Policy.Code
        && PolicyRevision >= previous.PolicyRevision && Policy.Revision >= previous.Policy.Revision
        && this != previous;

    public override string ToString() => nameof(ModerationScheduleRiskCoordinates);
}
