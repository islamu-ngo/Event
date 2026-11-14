
using Explore.Application.Contracts.Identity;

namespace Explore.Persistence.Identity;

public sealed class LocalIdentityCredentialOperation
{
    private LocalIdentityCredentialOperation()
    {
    }

    public LocalIdentityCredentialOperation(LocalCredentialOperationReceipt receipt, Guid concurrencyStamp)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Kind is not (LocalCredentialOperationKind.Create or LocalCredentialOperationKind.VisitorCreate)
            || receipt.Stage != LocalCredentialOperationStage.ProvisioningPending)
        {
            throw new ArgumentException("A pending creation receipt is required.", nameof(receipt));
        }

        if (concurrencyStamp == Guid.Empty)
            throw new ArgumentException("A concurrency stamp is required.", nameof(concurrencyStamp));

        Id = receipt.OperationId;
        Kind = receipt.Kind;
        Stage = receipt.Stage;
        InitiatingApplicationUserId = receipt.InitiatingApplicationUserId;
        LocalSubjectId = receipt.LocalSubjectId;
        PersonalActorId = receipt.PersonalActorId;
        ExternalLoginId = receipt.ExternalLoginId;
        CreatedAt = receipt.CreatedAt;
        VerifiedByApplicationUserId = receipt.InitiatingApplicationUserId;
        VerifiedAt = receipt.Kind == LocalCredentialOperationKind.Create ? receipt.CreatedAt : null;
        VisitorEnrollmentOperationId = receipt.VisitorEnrollmentOperationId;
        ConcurrencyStamp = concurrencyStamp;
    }

    public LocalIdentityCredentialOperation(
        LocalCredentialResetReceipt receipt,
        Guid concurrencyStamp,
        Guid? verifiedByApplicationUserId,
        DateTime? verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Operation.Stage != LocalCredentialOperationStage.ChangeRequired)
        {
            throw new ArgumentException("A change-required reset receipt is required.", nameof(receipt));
        }
        bool visitorOrigin = receipt.Operation.VisitorEnrollmentOperationId.HasValue;
        if (concurrencyStamp == Guid.Empty || verifiedByApplicationUserId == Guid.Empty
            || (visitorOrigin
                ? verifiedByApplicationUserId.HasValue || verifiedAt.HasValue
                : !verifiedByApplicationUserId.HasValue || !verifiedAt.HasValue))
        {
            throw new ArgumentException("Reset must retain its original administrator or visitor verification provenance.");
        }
        if (verifiedAt.HasValue
            && (verifiedAt.Value.Kind != DateTimeKind.Utc || verifiedAt.Value > receipt.Operation.CreatedAt))
        {
            throw new ArgumentException("Reset must retain prior UTC verification provenance.", nameof(verifiedAt));
        }

        Id = receipt.Operation.OperationId;
        Kind = receipt.Operation.Kind;
        Stage = receipt.Operation.Stage;
        InitiatingApplicationUserId = receipt.Operation.InitiatingApplicationUserId;
        LocalSubjectId = receipt.Operation.LocalSubjectId;
        PersonalActorId = receipt.Operation.PersonalActorId;
        ExternalLoginId = receipt.Operation.ExternalLoginId;
        PreviousOperationId = receipt.PreviousOperationId;
        PreviousOperationConcurrencyStamp = receipt.PreviousOperationConcurrencyStamp;
        ResetReason = receipt.Reason;
        CreatedAt = receipt.Operation.CreatedAt;
        VerifiedByApplicationUserId = verifiedByApplicationUserId;
        VerifiedAt = verifiedAt;
        VisitorEnrollmentOperationId = receipt.Operation.VisitorEnrollmentOperationId;
        ConcurrencyStamp = concurrencyStamp;
    }

    public Guid Id { get; private set; }
    public LocalCredentialOperationKind Kind { get; private set; }
    public LocalCredentialOperationStage Stage { get; private set; }
    public Guid? InitiatingApplicationUserId { get; private set; }
    public Guid LocalSubjectId { get; private set; }
    public Guid ApplicationUserId => LocalSubjectId;
    public Guid PersonalActorId { get; private set; }
    public Guid ExternalLoginId { get; private set; }
    public Guid? PreviousOperationId { get; private set; }
    public Guid? PreviousOperationConcurrencyStamp { get; private set; }
    public string? ResetReason { get; private set; }
    public Guid? VerifiedByApplicationUserId { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public Guid? VisitorEnrollmentOperationId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
}
