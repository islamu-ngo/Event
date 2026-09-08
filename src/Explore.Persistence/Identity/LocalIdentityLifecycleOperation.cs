// ABOUTME: Stores one-use Local lifecycle authority separately from credential/session admission.
// ABOUTME: Retains fixed deadlines and stamp-bound mirror receipts, never transport or password material.

using Explore.Application.Contracts.Identity;

namespace Explore.Persistence.Identity;

public sealed class LocalIdentityLifecycleOperation
{
    private LocalIdentityLifecycleOperation() { }
    internal LocalIdentityLifecycleOperation(LocalIdentityLifecyclePointer pointer, string pendingAddress,
        string securityStamp, Guid credentialOperationId, DateTime createdAt)
    {
        Id = pointer.OperationId; LocalSubjectId = pointer.LocalSubjectId; PersonalActorId = pointer.PersonalActorId;
        ExternalLoginId = pointer.ExternalLoginId; Purpose = pointer.Purpose; Generation = pointer.Generation;
        PendingAddress = pendingAddress; SecurityStamp = securityStamp; CredentialOperationId = credentialOperationId;
        CreatedAt = createdAt;
        ExpiresAt = createdAt.AddMinutes(Purpose == LocalIdentityLifecyclePurpose.PasswordRecovery ? 15 : 30);
    }
    public Guid Id { get; private set; }
    public Guid LocalSubjectId { get; private set; }
    public Guid PersonalActorId { get; private set; }
    public Guid ExternalLoginId { get; private set; }
    public LocalIdentityLifecyclePurpose Purpose { get; private set; }
    public Guid Generation { get; private set; }
    public Guid CredentialOperationId { get; private set; }
    public string PendingAddress { get; private set; } = null!;
    public string SecurityStamp { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    public string? ResultSecurityStamp { get; private set; }
    public DateTime? SynchronizedAt { get; private set; }
    public LocalIdentityLifecycleDeliveryState DeliveryState { get; private set; } = LocalIdentityLifecycleDeliveryState.Pending;
    public Guid? DeliveryAttemptId { get; private set; }
    public int DeliveryAttemptCount { get; private set; }
    public DateTime? DeliveryAdmittedAt { get; private set; }
    public DateTime? DeliveryCompletedAt { get; private set; }
    internal LocalIdentityLifecyclePointer Pointer() => new(Id, LocalSubjectId, PersonalActorId, ExternalLoginId, Purpose, Generation);
}
