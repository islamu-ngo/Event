using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>
/// Durable digest-only evidence of an API-key issuance operation, independent of key retention.
/// </summary>
public sealed class ExternalApiKeyIssuanceReceipt
{
    /// <summary>Allows persistence materialization while requiring new domain receipts to use the validating factory.</summary>
    private ExternalApiKeyIssuanceReceipt()
    {
    }

    public Guid Id { get; private set; }
    public string OperationFingerprint { get; private set; } = string.Empty;
    public string InputDigest { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public Guid ExternalApiKeyId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Records an acknowledged operation without retaining recoverable credential material.
    /// </summary>
    public static ExternalApiKeyIssuanceReceipt Create(
        Guid id,
        string fingerprint,
        string inputDigest,
        Guid? tenantId,
        Guid keyId,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty || keyId == Guid.Empty || tenantId == Guid.Empty)
            throw new ArgumentException("Receipt, key and tenant identities must be nonempty.");
        if (fingerprint is null || fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)
            || inputDigest is null || inputDigest.Length != 64 || !inputDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("Operation and input digests must be SHA-256 hexadecimal values.");
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Receipt creation time must be UTC.", nameof(createdAtUtc));

        return new ExternalApiKeyIssuanceReceipt
        {
            Id = id,
            OperationFingerprint = fingerprint.ToLowerInvariant(),
            InputDigest = inputDigest.ToLowerInvariant(),
            TenantId = tenantId,
            ExternalApiKeyId = keyId,
            CreatedAtUtc = createdAtUtc
        };
    }

    /// <summary>
    /// Admits exactly the operation-key header's bounded ASCII token grammar.
    /// </summary>
    public static bool IsValidOperationKey(string? operationKey)
    {
        if (operationKey is null || operationKey.Length is < 1 or > 128)
            return false;
        return operationKey.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
    }

    /// <summary>
    /// Separates principal, explicit global or tenant scope, owner and operation without including policy input.
    /// </summary>
    public static string ComputeOperationFingerprint(
        Guid principalId,
        Guid? tenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        string operationKey)
    {
        if (principalId == Guid.Empty || ownerId == Guid.Empty || tenantId == Guid.Empty || !Enum.IsDefined(ownerType))
            throw new ArgumentException("A defined owner type and nonempty identities are required.");
        if (!IsValidOperationKey(operationKey))
            throw new ArgumentException("The operation key must be a bounded ASCII token.", nameof(operationKey));

        string scope = tenantId is Guid tenant ? $"tenant:{tenant:N}" : "global";
        string identity = $"external-api-key-issuance:v1|principal:{principalId:N}|scope:{scope}"
            + $"|owner-type:{((int)ownerType).ToString(CultureInfo.InvariantCulture)}|owner:{ownerId:N}"
            + $"|operation-key:{operationKey.Length.ToString(CultureInfo.InvariantCulture)}:{operationKey}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
