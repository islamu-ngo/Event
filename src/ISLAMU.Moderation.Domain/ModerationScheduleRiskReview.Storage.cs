namespace ISLAMU.Moderation.Domain;

public sealed partial class ModerationScheduleRiskReview
{
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public ModerationScopeKind SubjectOriginKind { get; private set; }
    public Guid SubjectOriginTrustDomainId { get; private set; }
    public Guid? SubjectOriginSolutionId { get; private set; }
    public Guid? SubjectOriginInstanceId { get; private set; }
    public Guid? SubjectOriginTenantId { get; private set; }
    public Guid? SubjectOriginCommunityId { get; private set; }
    public Guid? SubjectOriginResourceId { get; private set; }
    public Guid? SubjectOriginEventId { get; private set; }
    public ModerationScopeKind SourceScopeKind { get; private set; }
    public Guid SourceScopeTrustDomainId { get; private set; }
    public Guid? SourceScopeSolutionId { get; private set; }
    public Guid? SourceScopeInstanceId { get; private set; }
    public Guid? SourceScopeTenantId { get; private set; }
    public Guid? SourceScopeCommunityId { get; private set; }
    public Guid? SourceScopeResourceId { get; private set; }
    public Guid? SourceScopeEventId { get; private set; }
    public Guid SourceRuleOwnerId { get; private set; }
    public string SourceRuleCode { get; private set; } = string.Empty;
    public long SourceRuleRevision { get; private set; }
    public ModerationScopeKind EventScopeKind { get; private set; }
    public Guid EventScopeTrustDomainId { get; private set; }
    public Guid? EventScopeSolutionId { get; private set; }
    public Guid? EventScopeInstanceId { get; private set; }
    public Guid? EventScopeTenantId { get; private set; }
    public Guid? EventScopeCommunityId { get; private set; }
    public Guid? EventScopeResourceId { get; private set; }
    public Guid? EventScopeEventId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid OriginalScheduleGeneration { get; private set; }
    public Guid ScheduleGeneration { get; private set; }
    public Guid OriginalParticipantGeneration { get; private set; }
    public Guid ParticipantGeneration { get; private set; }
    public Guid PolicyId { get; private set; }
    public long OriginalPolicyRevision { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid OriginalPublicationId { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid PolicyRuleOwnerId { get; private set; }
    public string PolicyRuleCode { get; private set; } = string.Empty;
    public long OriginalPolicyRuleRevision { get; private set; }
    public long PolicyRuleRevision { get; private set; }
    public ModerationSubjectKind OwnerKind { get; private set; }
    public Guid OwnerId { get; private set; }
    public ModerationScopeKind OwnerOriginKind { get; private set; }
    public Guid OwnerOriginTrustDomainId { get; private set; }
    public Guid? OwnerOriginSolutionId { get; private set; }
    public Guid? OwnerOriginInstanceId { get; private set; }
    public Guid? OwnerOriginTenantId { get; private set; }
    public Guid? OwnerOriginCommunityId { get; private set; }
    public Guid? OwnerOriginResourceId { get; private set; }
    public Guid? OwnerOriginEventId { get; private set; }
    public ModerationSubjectKind EscalationOwnerKind { get; private set; }
    public Guid EscalationOwnerId { get; private set; }
    public ModerationScopeKind EscalationOwnerOriginKind { get; private set; }
    public Guid EscalationOwnerOriginTrustDomainId { get; private set; }
    public Guid? EscalationOwnerOriginSolutionId { get; private set; }
    public Guid? EscalationOwnerOriginInstanceId { get; private set; }
    public Guid? EscalationOwnerOriginTenantId { get; private set; }
    public Guid? EscalationOwnerOriginCommunityId { get; private set; }
    public Guid? EscalationOwnerOriginResourceId { get; private set; }
    public Guid? EscalationOwnerOriginEventId { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }

    public SubjectReference Subject
    {
        get => SubjectReference.Create(SubjectKind, SubjectId, AuthorityScope.FromCoordinates(
            SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
            SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId, SubjectOriginEventId));
        private set
        {
            (SubjectKind, SubjectId) = (value.Kind, value.Id);
            (SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
                SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId, SubjectOriginEventId) = value.Origin.Coordinates;
        }
    }

    public AuthorityScope SourceScope
    {
        get => AuthorityScope.FromCoordinates(SourceScopeKind, SourceScopeTrustDomainId, SourceScopeSolutionId,
            SourceScopeInstanceId, SourceScopeTenantId, SourceScopeCommunityId, SourceScopeResourceId, SourceScopeEventId);
        private set => (SourceScopeKind, SourceScopeTrustDomainId, SourceScopeSolutionId, SourceScopeInstanceId,
            SourceScopeTenantId, SourceScopeCommunityId, SourceScopeResourceId, SourceScopeEventId) = value.Coordinates;
    }

    public ModerationRuleReference SourceRule
    {
        get => ModerationRuleReference.Create(SourceRuleOwnerId, SourceRuleCode, SourceRuleRevision);
        private set => (SourceRuleOwnerId, SourceRuleCode, SourceRuleRevision) = (value.OwnerId, value.Code, value.Revision);
    }

    public AuthorityScope EventScope => AuthorityScope.FromCoordinates(EventScopeKind, EventScopeTrustDomainId,
        EventScopeSolutionId, EventScopeInstanceId, EventScopeTenantId, EventScopeCommunityId, EventScopeResourceId, EventScopeEventId);

    public ModerationScheduleRiskCoordinates OriginalCoordinates
    {
        get => ModerationScheduleRiskCoordinates.Create(EventScope, SessionId, OriginalScheduleGeneration,
            ParticipantId, Subject, OriginalParticipantGeneration, PolicyId, OriginalPolicyRevision, OriginalPublicationId,
            ModerationRuleReference.Create(PolicyRuleOwnerId, PolicyRuleCode, OriginalPolicyRuleRevision));
        private set
        {
            (EventScopeKind, EventScopeTrustDomainId, EventScopeSolutionId, EventScopeInstanceId,
                EventScopeTenantId, EventScopeCommunityId, EventScopeResourceId, EventScopeEventId) = value.EventScope.Coordinates;
            (SessionId, ParticipantId, PolicyId, PolicyRuleOwnerId, PolicyRuleCode) =
                (value.SessionId, value.ParticipantId, value.PolicyId, value.Policy.OwnerId, value.Policy.Code);
            (OriginalScheduleGeneration, OriginalParticipantGeneration, OriginalPolicyRevision, OriginalPublicationId,
                OriginalPolicyRuleRevision) = (value.ScheduleGeneration, value.ParticipantGeneration,
                value.PolicyRevision, value.PublicationId, value.Policy.Revision);
        }
    }

    public ModerationScheduleRiskCoordinates Coordinates
    {
        get => ModerationScheduleRiskCoordinates.Create(EventScope, SessionId, ScheduleGeneration,
            ParticipantId, Subject, ParticipantGeneration, PolicyId, PolicyRevision, PublicationId,
            ModerationRuleReference.Create(PolicyRuleOwnerId, PolicyRuleCode, PolicyRuleRevision));
        private set => (ScheduleGeneration, ParticipantGeneration, PolicyRevision, PublicationId, PolicyRuleRevision) =
            (value.ScheduleGeneration, value.ParticipantGeneration, value.PolicyRevision, value.PublicationId, value.Policy.Revision);
    }

    public SubjectReference Owner
    {
        get => SubjectReference.Create(OwnerKind, OwnerId, AuthorityScope.FromCoordinates(OwnerOriginKind,
            OwnerOriginTrustDomainId, OwnerOriginSolutionId, OwnerOriginInstanceId, OwnerOriginTenantId,
            OwnerOriginCommunityId, OwnerOriginResourceId, OwnerOriginEventId));
        private set
        {
            (OwnerKind, OwnerId) = (value.Kind, value.Id);
            (OwnerOriginKind, OwnerOriginTrustDomainId, OwnerOriginSolutionId, OwnerOriginInstanceId,
                OwnerOriginTenantId, OwnerOriginCommunityId, OwnerOriginResourceId, OwnerOriginEventId) = value.Origin.Coordinates;
        }
    }

    public SubjectReference EscalationOwner
    {
        get => SubjectReference.Create(EscalationOwnerKind, EscalationOwnerId, AuthorityScope.FromCoordinates(
            EscalationOwnerOriginKind, EscalationOwnerOriginTrustDomainId, EscalationOwnerOriginSolutionId,
            EscalationOwnerOriginInstanceId, EscalationOwnerOriginTenantId, EscalationOwnerOriginCommunityId,
            EscalationOwnerOriginResourceId, EscalationOwnerOriginEventId));
        private set
        {
            (EscalationOwnerKind, EscalationOwnerId) = (value.Kind, value.Id);
            (EscalationOwnerOriginKind, EscalationOwnerOriginTrustDomainId, EscalationOwnerOriginSolutionId,
                EscalationOwnerOriginInstanceId, EscalationOwnerOriginTenantId, EscalationOwnerOriginCommunityId,
                EscalationOwnerOriginResourceId, EscalationOwnerOriginEventId) = value.Origin.Coordinates;
        }
    }

    public SubjectReference? WithdrawnBy
    {
        get => WithdrawnByHumanId is { } id ? SubjectReference.Create(ModerationSubjectKind.Human, id, Owner.Origin) : null;
        private set => WithdrawnByHumanId = value?.Id;
    }
}
