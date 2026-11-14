using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Primary-store admission evidence. Its native credential operation remains a separate identity-store record.
/// Owning workflows must verify fresh admission and identity authority before advancing this receipt.
/// </summary>
public sealed class VisitorEnrollmentOperation : ITenantEntity
{
    private Guid _tenantId;

    private VisitorEnrollmentOperation() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(VisitorEnrollmentOperation));
    }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid LocalSubjectId { get; private set; }
    public Guid PersonalActorId { get; private set; }
    public Guid ExternalLoginId { get; private set; }
    public Guid CredentialOperationId { get; private set; }
    public long? CredentialGeneration { get; private set; }
    public Guid? TenantUserId { get; private set; }
    public string IntentDigest { get; private set; } = "";
    public VisitorEnrollmentStage Stage { get; private set; }
    public long Generation { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? CleanupCompletedAt { get; private set; }

    public static VisitorEnrollmentOperation Reserve(Guid operationId, Guid tenantId, Guid localSubjectId,
        Guid personalActorId, Guid externalLoginId, Guid credentialOperationId, string intentDigest, DateTime occurredAt)
    {
        RequireId(operationId, nameof(operationId));
        RequireId(tenantId, nameof(tenantId));
        RequireId(localSubjectId, nameof(localSubjectId));
        RequireId(personalActorId, nameof(personalActorId));
        RequireId(externalLoginId, nameof(externalLoginId));
        RequireId(credentialOperationId, nameof(credentialOperationId));
        RequireUtc(occurredAt);
        if (intentDigest is null || intentDigest.Length != 64 || intentDigest.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 intent digest is required.", nameof(intentDigest));
        return new VisitorEnrollmentOperation
        {
            Id = operationId,
            TenantId = tenantId,
            LocalSubjectId = localSubjectId,
            PersonalActorId = personalActorId,
            ExternalLoginId = externalLoginId,
            CredentialOperationId = credentialOperationId,
            IntentDigest = intentDigest.ToLowerInvariant(),
            Stage = VisitorEnrollmentStage.Reserved,
            Generation = 1,
            CreatedAt = occurredAt,
            ExpiresAt = occurredAt.AddMinutes(15)
        };
    }

    public void RecordIdentityPrepared(long expectedGeneration, Guid credentialOperationId,
        long credentialGeneration, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, credentialOperationId, occurredAt);
        if (credentialGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(credentialGeneration));
        if (Stage != VisitorEnrollmentStage.Reserved || occurredAt >= ExpiresAt)
            throw new InvalidOperationException("Only an unexpired reservation can prepare its identity.");
        CredentialGeneration = credentialGeneration;
        Advance(VisitorEnrollmentStage.IdentityPrepared, occurredAt);
    }

    public void RecordPrimaryAdmission(long expectedGeneration, Guid credentialOperationId,
        Guid tenantUserId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, credentialOperationId, occurredAt);
        RequireId(tenantUserId, nameof(tenantUserId));
        if (Stage != VisitorEnrollmentStage.IdentityPrepared || occurredAt >= ExpiresAt)
            throw new InvalidOperationException("Only an unexpired prepared identity can commit primary admission.");
        TenantUserId = tenantUserId;
        Advance(VisitorEnrollmentStage.PrimaryAdmitted, occurredAt);
    }

    public void Activate(long expectedGeneration, Guid credentialOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, credentialOperationId, occurredAt);
        if (Stage != VisitorEnrollmentStage.PrimaryAdmitted)
            throw new InvalidOperationException("Only committed primary admission can activate its identity.");
        Advance(VisitorEnrollmentStage.Active, occurredAt);
    }

    public void Cancel(long expectedGeneration, Guid credentialOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, credentialOperationId, occurredAt);
        if (Stage is not (VisitorEnrollmentStage.Reserved or VisitorEnrollmentStage.IdentityPrepared))
            throw new InvalidOperationException("Committed or terminal enrollment cannot be canceled.");
        Advance(VisitorEnrollmentStage.Canceled, occurredAt);
    }

    public void AcknowledgeCancellationCleanup(
        long expectedGeneration, Guid credentialOperationId, DateTime occurredAt)
    {
        RequireMutation(expectedGeneration, credentialOperationId, occurredAt);
        if (Stage != VisitorEnrollmentStage.Canceled)
            throw new InvalidOperationException("Only canceled enrollment can acknowledge native cleanup.");
        if (CleanupCompletedAt.HasValue) return;
        CleanupCompletedAt = occurredAt;
        Advance(VisitorEnrollmentStage.Canceled, occurredAt);
    }

    private void RequireMutation(long expectedGeneration, Guid credentialOperationId, DateTime occurredAt)
    {
        RequireUtc(occurredAt);
        if (expectedGeneration != Generation || credentialOperationId != CredentialOperationId
            || occurredAt < (UpdatedAt ?? CreatedAt))
            throw new InvalidOperationException("The original enrollment generation, credential and UTC timeline are required.");
    }

    private void Advance(VisitorEnrollmentStage stage, DateTime occurredAt)
    {
        Generation = checked(Generation + 1);
        Stage = stage;
        UpdatedAt = occurredAt;
    }

    private static void RequireId(Guid value, string parameter)
    {
        if (value == Guid.Empty) throw new ArgumentException("A bound identifier is required.", parameter);
    }

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("A UTC timestamp is required.", nameof(value));
    }
}

public enum VisitorEnrollmentStage
{
    Reserved = 1,
    IdentityPrepared = 2,
    PrimaryAdmitted = 3,
    Active = 4,
    Canceled = 5
}
