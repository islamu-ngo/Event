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
    Revoke
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
        if (!Enum.IsDefined(kind) || kind == ModerationOperationKind.Evaluate
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
    ModerationOperationKind Kind)
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
