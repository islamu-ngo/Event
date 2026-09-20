using System.Collections.Immutable;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Failure codes reported by the instance operator identity readiness evaluator.
/// </summary>
public static class InstanceOperatorIdentityFailureCodes
{
    public const string Missing = "instance_operator_identity_missing";

    public const string IntegrityError = "instance_operator_identity_integrity_error";
}

/// <summary>
/// Per-operation readiness assessment of the persisted instance operator identity.
/// </summary>
/// <param name="IsReady">True when the persisted identity satisfies the full readiness contract.</param>
/// <param name="FailureCode">
/// One of <see cref="InstanceOperatorIdentityFailureCodes.Missing"/>,
/// <see cref="InstanceOperatorIdentityFailureCodes.IntegrityError"/>, or the domain
/// incomplete failure code; null when ready.
/// </param>
/// <param name="ReasonCodes">Bounded reason codes when the stored document is incomplete.</param>
/// <param name="Identity">The validated operator identity when ready; null otherwise.</param>
/// <param name="DocumentRevision">The persisted document revision when a document exists.</param>
public sealed record InstanceOperatorIdentityReadinessAssessment(
    bool IsReady,
    string? FailureCode,
    ImmutableArray<string> ReasonCodes,
    InstanceOperatorIdentity? Identity,
    Guid? DocumentRevision);

/// <summary>
/// Scoped, uncached readiness evaluator over the persisted
/// <c>instance.operator_identity</c> system setting. Consumers must fail closed when the
/// assessment is not ready.
/// </summary>
public interface IInstanceOperatorIdentityReadinessEvaluator
{
    Task<InstanceOperatorIdentityReadinessAssessment> EvaluateAsync(
        CancellationToken cancellationToken = default);
}
