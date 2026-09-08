// ABOUTME: Stores one opaque versioned AES-256-GCM ciphertext for a sensitive registration answer.
// ABOUTME: Deliberately exposes no plaintext property or encryption implementation in the Domain layer.

using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationSensitiveAnswerValue : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    private RegistrationSensitiveAnswerValue()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public string Ciphertext { get; private set; } = string.Empty;
    public int KeyVersion { get; private set; }
    public DateTime? RetentionUntil { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationSensitiveAnswerValue Create(
        Guid tenantId,
        string ciphertext,
        int keyVersion,
        DateTime createdAt) => Create(
            tenantId, ciphertext, keyVersion, (int)Enums.RegistrationRetentionPolicyEnum.SensitiveShort, createdAt);

    public static RegistrationSensitiveAnswerValue Create(
        Guid tenantId,
        string ciphertext,
        int keyVersion,
        int retentionPolicyId,
        DateTime createdAt,
        DateTime? anonymousUpperBoundUtc = null)
    {
        if (tenantId == Guid.Empty || keyVersion <= 0 || createdAt == default || createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Sensitive answer identity, key version, and UTC creation time are required.");
        }

        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            throw new ArgumentException("Ciphertext is required.", nameof(ciphertext));
        }

        try
        {
            byte[] envelope = Convert.FromBase64String(ciphertext);
            if (envelope.Length < 29 || !string.Equals(Convert.ToBase64String(envelope), ciphertext, StringComparison.Ordinal))
            {
                throw new ArgumentException("Ciphertext must be a canonical AES-GCM envelope.", nameof(ciphertext));
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Ciphertext must be a canonical AES-GCM envelope.", nameof(ciphertext), exception);
        }

        return new RegistrationSensitiveAnswerValue
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Ciphertext = ciphertext,
            KeyVersion = keyVersion,
            RetentionUntil = RegistrationRetentionDeadline.Resolve(retentionPolicyId, createdAt, anonymousUpperBoundUtc),
            CreatedAt = createdAt
        };
    }
}
