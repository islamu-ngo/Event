using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore : ILocalVisitorEnrollmentStore
{
    Guid ILocalVisitorEnrollmentStore.TenantId => RequireVisitorPreparationOwner();

    Task<VisitorEnrollmentOperation> ILocalVisitorEnrollmentStore.ReserveAsync(
        Guid operationId, string proofDigest, Func<CancellationToken, Task> authorizeNewEnrollment,
        CancellationToken cancellationToken) =>
        ReserveVisitorAsync(operationId, proofDigest, authorizeNewEnrollment, cancellationToken);

    async Task<VisitorEnrollmentOperation?> ILocalVisitorEnrollmentStore.ReadAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        Guid tenant = RequireVisitorPreparationOwner();
        var receipt = await ReadVisitorPrimaryReceiptAsync(operationId, tenant, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        return receipt;
    }

    Task<LocalCredentialProvisioningSnapshot?> ILocalVisitorEnrollmentStore.PrepareAsync(
        Guid operationId, string username, string password, string? email, CancellationToken cancellationToken) =>
        PrepareVisitorAsync(operationId, username, password, email, cancellationToken);

    Task<VisitorEnrollmentOperation?> ILocalVisitorEnrollmentStore.RecordPreparedAsync(
        Guid operationId, CancellationToken cancellationToken) =>
        RecordVisitorPreparedAsync(operationId, cancellationToken);

    ITenantEnrollmentAdmissionWriter ILocalVisitorEnrollmentStore.CreateAdmissionWriter(Guid operationId) =>
        CreateVisitorAdmissionWriter(operationId);

    async Task<LocalCredentialActivationOutcome> ILocalVisitorEnrollmentStore.ActivateAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        Guid tenant = RequireVisitorPreparationOwner();
        var receipt = await ReadVisitorPrimaryReceiptAsync(operationId, tenant, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        if (receipt is null) return LocalCredentialActivationOutcome.NotFound;
        if (receipt.Stage is not (VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active)
            || receipt.CredentialGeneration is not > 0)
            return LocalCredentialActivationOutcome.Conflict;
        var evidence = await ReadVisitorNativeEvidenceAsync(receipt, true, cancellationToken).ConfigureAwait(false);
        RequireVisitorPreparationOwner(tenant);
        return await ActivateVisitorAsync(operationId, evidence.Operation.ConcurrencyStamp,
            receipt.CredentialGeneration.Value, cancellationToken).ConfigureAwait(false);
    }

    Task<VisitorEnrollmentOperation?> ILocalVisitorEnrollmentStore.AcknowledgeAsync(
        Guid operationId, CancellationToken cancellationToken) =>
        AcknowledgeVisitorActivationAsync(operationId, cancellationToken);

    Task<VisitorEnrollmentOperation?> ILocalVisitorEnrollmentStore.CancelAsync(
        Guid operationId, bool requireExpired, CancellationToken cancellationToken) =>
        CancelVisitorAsync(operationId, requireExpired, cancellationToken);

    Task<bool> ILocalVisitorEnrollmentStore.DeleteCanceledAsync(
        Guid operationId, CancellationToken cancellationToken) =>
        DeleteCanceledVisitorAsync(operationId, cancellationToken);

    Task<bool> ILocalVisitorEnrollmentStore.RecoverAsync(
        VisitorEnrollmentRecoveryCandidate candidate, CancellationToken cancellationToken) =>
        RecoverVisitorAsync(candidate, cancellationToken);
}
