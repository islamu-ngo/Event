using ISLAMU.Moderation.Domain;

namespace ISLAMU.Moderation.Application;

public enum ModerationOperationKind
{
    Evaluate,
    RecordResponse,
    AcceptFinding,
    Approve,
    Activate,
    Reject,
    Withdraw,
    Revoke,
    Propose,
    Reconcile,
    Supersede
}

public enum ModerationOperationOutcome
{
    Unavailable,
    Known,
    Denied,
    Conflict,
    NotFound
}

/// <summary>Immutable intent, not authority. The host allocates mutation identities before any retry delegate.</summary>
public sealed record ModerationOperation
{
    private ModerationOperation(
        ModerationOperationKind kind, SubjectReference subject, AuthorityScope target,
        ModerationCapability? capability, Guid operationId, Guid effectId, Guid decisionId,
        long expectedRevision, long expectedPolicyRevision, Guid referenceId)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);
        if (subject.TrustDomainId != target.TrustDomainId)
        {
            throw new ArgumentException("The requested subject and target must be qualified together.", nameof(target));
        }

        Kind = kind;
        Subject = subject;
        Target = target;
        Capability = capability;
        OperationId = operationId;
        EffectId = effectId;
        DecisionId = decisionId;
        ExpectedRevision = expectedRevision;
        ExpectedPolicyRevision = expectedPolicyRevision;
        ReferenceId = referenceId;
    }

    public ModerationOperationKind Kind { get; }
    public SubjectReference Subject { get; }
    public AuthorityScope Target { get; }
    public ModerationCapability? Capability { get; }
    public Guid OperationId { get; }
    public Guid EffectId { get; }
    public Guid DecisionId { get; }
    public long ExpectedRevision { get; }
    public long ExpectedPolicyRevision { get; }
    /// <summary>A finding identity, new contribution identity or existing contribution identity, as required by Kind.</summary>
    public Guid ReferenceId { get; }
    public ModerationProposal? Proposal { get; private init; }
    /// <summary>The existing contribution revision for reconciliation or supersession.</summary>
    public long ExpectedSanctionRevision { get; private init; }
    public Guid SupersededSanctionId { get; private init; }
    public AuthorityScope? SupersededScope { get; private init; }

    public static ModerationOperation Evaluate(
        SubjectReference subject, AuthorityScope target, ModerationCapability capability)
    {
        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentException("A supported capability is required.", nameof(capability));
        }

        return new ModerationOperation(ModerationOperationKind.Evaluate, subject, target,
            capability, Guid.Empty, Guid.Empty, Guid.Empty, 0, 0, Guid.Empty);
    }

    public static ModerationOperation Transition(
        ModerationOperationKind kind, SubjectReference subject, AuthorityScope target,
        Guid operationId, Guid effectId, Guid decisionId, long expectedRevision,
        long expectedPolicyRevision, Guid referenceId)
    {
        if (kind is not (ModerationOperationKind.RecordResponse or ModerationOperationKind.AcceptFinding
            or ModerationOperationKind.Approve or ModerationOperationKind.Activate
            or ModerationOperationKind.Reject or ModerationOperationKind.Withdraw or ModerationOperationKind.Revoke)
            || operationId == Guid.Empty || effectId == Guid.Empty || decisionId == Guid.Empty
            || expectedRevision < 1 || expectedPolicyRevision < 1)
        {
            throw new ArgumentException("A mutation requires stable identities and positive expected revisions.", nameof(operationId));
        }

        var requiresReference = kind is ModerationOperationKind.AcceptFinding
            or ModerationOperationKind.Activate or ModerationOperationKind.Revoke;
        if (requiresReference == (referenceId == Guid.Empty))
        {
            throw new ArgumentException("The reference identity must match the requested transition.", nameof(referenceId));
        }

        return new ModerationOperation(kind, subject, target, null, operationId, effectId,
            decisionId, expectedRevision, expectedPolicyRevision, referenceId);
    }

    public override string ToString() => Kind.ToString();

    public static ModerationOperation Propose(
        SubjectReference subject, AuthorityScope target, Guid operationId, Guid effectId,
        Guid decisionId, long expectedCaseRevision, long expectedPolicyRevision, ModerationProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(target);
        RequireMutation(operationId, effectId, decisionId, expectedCaseRevision, expectedPolicyRevision);
        if (target.Kind is ModerationScopeKind.TrustDomain or ModerationScopeKind.Solution && proposal.RequiredApprovals != 2)
        {
            throw new ArgumentException("A broad-scope proposal requires two independent approvals.", nameof(proposal));
        }
        return new ModerationOperation(ModerationOperationKind.Propose, subject, target, null,
            operationId, effectId, decisionId, expectedCaseRevision, expectedPolicyRevision, Guid.Empty)
        {
            Proposal = proposal
        };
    }

    public static ModerationOperation Reconcile(
        SubjectReference subject, AuthorityScope target, Guid operationId, Guid effectId,
        Guid decisionId, long expectedDecisionRevision, long expectedPolicyRevision,
        Guid sanctionId, long expectedSanctionRevision)
    {
        RequireMutation(operationId, effectId, decisionId, expectedDecisionRevision, expectedPolicyRevision);
        if (sanctionId == Guid.Empty || expectedSanctionRevision < 1)
        {
            throw new ArgumentException("Reconciliation requires an exact existing contribution and revision.", nameof(sanctionId));
        }

        return new ModerationOperation(ModerationOperationKind.Reconcile, subject, target, null,
            operationId, effectId, decisionId, expectedDecisionRevision, expectedPolicyRevision, sanctionId)
        {
            ExpectedSanctionRevision = expectedSanctionRevision
        };
    }

    public static ModerationOperation Supersede(
        SubjectReference subject, AuthorityScope target, Guid operationId, Guid effectId,
        Guid replacementDecisionId, long expectedDecisionRevision, long expectedPolicyRevision,
        Guid newSanctionId, Guid supersededSanctionId, AuthorityScope supersededScope, long expectedSanctionRevision)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(supersededScope);
        RequireMutation(operationId, effectId, replacementDecisionId, expectedDecisionRevision, expectedPolicyRevision);
        if (newSanctionId == Guid.Empty || supersededSanctionId == Guid.Empty || newSanctionId == supersededSanctionId
            || expectedSanctionRevision < 1 || supersededScope.TrustDomainId != subject.TrustDomainId)
        {
            throw new ArgumentException("Supersession requires distinct stable contributions and an exact old revision.", nameof(newSanctionId));
        }

        return new ModerationOperation(ModerationOperationKind.Supersede, subject, target, null,
            operationId, effectId, replacementDecisionId, expectedDecisionRevision, expectedPolicyRevision, newSanctionId)
        {
            SupersededSanctionId = supersededSanctionId,
            SupersededScope = supersededScope,
            ExpectedSanctionRevision = expectedSanctionRevision
        };
    }

    private static void RequireMutation(Guid operationId, Guid effectId, Guid decisionId, long revision, long policyRevision)
    {
        if (operationId == Guid.Empty || effectId == Guid.Empty || decisionId == Guid.Empty || revision < 1 || policyRevision < 1)
        {
            throw new ArgumentException("A mutation requires stable identities and positive expected revisions.", nameof(operationId));
        }
    }
}

/// <summary>Requested proposal terms, never proof of case ownership or adopted rule/profile authority.</summary>
public sealed record ModerationProposal
{
    public ModerationProposal(
        Guid caseId, ModerationRuleReference rule, IEnumerable<ModerationCapability> capabilities,
        DateTimeOffset startsAt, DateTimeOffset? endsAt, DateTimeOffset? reviewAt, int requiredApprovals)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(capabilities);
        var snapshot = capabilities.Distinct().Order().ToArray();
        if (caseId == Guid.Empty || snapshot.Length == 0
            || snapshot.Any(capability => !Enum.IsDefined(capability) || EffectiveRestrictions.IsProtectedRemedy(capability))
            || requiredApprovals is < 1 or > 2 || (endsAt is not null && endsAt <= startsAt)
            || (endsAt is null && (reviewAt is null || requiredApprovals != 2))
            || (reviewAt is not null && (reviewAt <= startsAt || (endsAt is not null && reviewAt > endsAt))))
        {
            throw new ArgumentException("Proposal terms require a case, restrictive capabilities, quorum and valid interval.", nameof(caseId));
        }

        CaseId = caseId;
        Rule = rule;
        Capabilities = Array.AsReadOnly(snapshot);
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt?.ToUniversalTime();
        ReviewAt = reviewAt?.ToUniversalTime();
        RequiredApprovals = requiredApprovals;
    }

    public Guid CaseId { get; }
    public ModerationRuleReference Rule { get; }
    public IReadOnlyList<ModerationCapability> Capabilities { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset? EndsAt { get; }
    public DateTimeOffset? ReviewAt { get; }
    public int RequiredApprovals { get; }
    public override string ToString() => "ModerationProposal";
}

/// <summary>Server-qualified current permission, obtained under the store's authority fence, never from request fields.</summary>
public sealed record ModerationAuthorityGrant(
    SubjectReference Principal,
    SubjectReference Subject,
    AuthorityScope Scope,
    ModerationOperationKind Purpose,
    long PolicyRevision,
    long QualificationRevision,
    ModerationAuthorityState RestrictionAuthority)
{
    public override string ToString() => "ModerationAuthorityGrant";
}

public sealed record ModerationAuthorityResolution
{
    private ModerationAuthorityResolution(ModerationOperationOutcome outcome, ModerationAuthorityGrant? grant)
    {
        Outcome = outcome;
        Grant = grant;
    }

    public ModerationOperationOutcome Outcome { get; }
    public ModerationAuthorityGrant? Grant { get; }

    public static ModerationAuthorityResolution Known(ModerationAuthorityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(grant.Principal);
        ArgumentNullException.ThrowIfNull(grant.Subject);
        ArgumentNullException.ThrowIfNull(grant.Scope);
        if (grant.PolicyRevision < 1 || grant.QualificationRevision < 1
            || !Enum.IsDefined(grant.Purpose) || !Enum.IsDefined(grant.RestrictionAuthority)
            || grant.Principal.TrustDomainId != grant.Scope.TrustDomainId
            || grant.Subject.TrustDomainId != grant.Scope.TrustDomainId)
        {
            throw new ArgumentException("A grant requires positively qualified current authority.", nameof(grant));
        }

        return new ModerationAuthorityResolution(ModerationOperationOutcome.Known, grant);
    }

    public static ModerationAuthorityResolution Denied() => new(ModerationOperationOutcome.Denied, null);
    public static ModerationAuthorityResolution Unavailable() => new(ModerationOperationOutcome.Unavailable, null);
    public override string ToString() => Outcome.ToString();
}

/// <summary>A minimized durable state-change pointer; custody evidence and external delivery belong to their native owners.</summary>
public sealed record ModerationPlannedEffect(
    Guid Id,
    Guid OperationId,
    Guid DecisionId,
    long DecisionRevision,
    Guid? SanctionId,
    long? SanctionRevision,
    SubjectReference Subject,
    AuthorityScope Scope,
    ModerationOperationKind Kind,
    Guid? SupersededSanctionId,
    long? SupersededSanctionRevision)
{
    public override string ToString() => "ModerationPlannedEffect";
}

public sealed record ModerationOperationResult
{
    private ModerationOperationResult(
        ModerationOperationOutcome outcome, ModerationRestrictionEvaluation? evaluation,
        Guid? decisionId, Guid? sanctionId, long? revision)
    {
        Outcome = outcome;
        Evaluation = evaluation;
        DecisionId = decisionId;
        SanctionId = sanctionId;
        Revision = revision;
    }

    public ModerationOperationOutcome Outcome { get; }
    public ModerationRestrictionEvaluation? Evaluation { get; }
    public Guid? DecisionId { get; }
    public Guid? SanctionId { get; }
    public long? Revision { get; }

    public static ModerationOperationResult Evaluated(ModerationRestrictionEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var outcome = evaluation.Outcome switch
        {
            ModerationRestrictionOutcome.Unrestricted => ModerationOperationOutcome.Known,
            ModerationRestrictionOutcome.Denied => ModerationOperationOutcome.Denied,
            _ => ModerationOperationOutcome.Unavailable
        };
        return new ModerationOperationResult(outcome, evaluation, null, null, null);
    }

    public static ModerationOperationResult Applied(Guid decisionId, Guid? sanctionId, long revision)
    {
        if (decisionId == Guid.Empty || sanctionId == Guid.Empty || revision < 1)
        {
            throw new ArgumentException("An applied result requires the exact accepted aggregate identity and revision.", nameof(decisionId));
        }

        return new ModerationOperationResult(ModerationOperationOutcome.Known, null, decisionId, sanctionId, revision);
    }

    public static ModerationOperationResult Denied() => new(ModerationOperationOutcome.Denied, null, null, null, null);
    public static ModerationOperationResult Unavailable() => new(ModerationOperationOutcome.Unavailable, null, null, null, null);
    public static ModerationOperationResult Conflict() => new(ModerationOperationOutcome.Conflict, null, null, null, null);
    public static ModerationOperationResult NotFound() => new(ModerationOperationOutcome.NotFound, null, null, null, null);
    public override string ToString() => Outcome.ToString();
}
