using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Local transactional custody of deployment approval and value-free evidence.
/// Application constructs the intent and time. The coordinator independently resolves the
/// authenticated operator, fences current persisted administrator/bootstrap authority,
/// and compares the independently selected deployment profile before committing evidence.
/// </summary>
public interface IAuthenticationCredentialCutoverCoordinator
{
    Task<AuthenticationCredentialCleanupInventory> InspectCleanupAsync(
        Guid instanceId, Guid operationId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<AuthenticationCredentialCutover> CleanupAsync(
        Guid instanceId, Guid operationId, Guid deploymentRevision,
        Guid expectedHeadStamp, Guid expectedOperationStamp,
        IReadOnlyCollection<Guid> approvedSettingIds, IReadOnlyCollection<Guid> approvedBindingIds,
        DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<AuthenticationCredentialCutover> PrepareAsync(
        AuthenticationCredentialCutover operation,
        Guid? expectedCurrentOperationId,
        Guid? expectedHeadStamp,
        Guid? expectedPredecessorStamp,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<AuthenticationCredentialCutover> ReportActivationAsync(
        AuthenticationCredentialCutover.ActivationReport report,
        Guid expectedHeadStamp,
        Guid expectedOperationStamp,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<AuthenticationCredentialCutover> AttestFenceAsync(
        AuthenticationCredentialCutover.FenceAttestation attestation,
        Guid expectedHeadStamp,
        Guid expectedOperationStamp,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
