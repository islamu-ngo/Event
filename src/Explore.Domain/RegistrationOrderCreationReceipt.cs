using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Non-reopening creation evidence retained independently of account, order and HTTP response retention.
/// The fingerprints contain no recoverable request, account identifier or credential.
/// </summary>
public sealed class RegistrationOrderCreationReceipt : ITenantEntity
{
    private Guid _tenantId;

    private RegistrationOrderCreationReceipt() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(RegistrationOrderCreationReceipt));
    }
    public string OperationFingerprint { get; private set; } = string.Empty;
    public string InputDigest { get; private set; } = string.Empty;
    public Guid RegistrationOrderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static RegistrationOrderCreationReceipt Create(
        Guid id, Guid tenantId, string fingerprint, string inputDigest, Guid orderId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || orderId == Guid.Empty)
            throw new ArgumentException("Creation receipt identities must be nonempty.");
        if (!IsDigest(fingerprint) || !IsDigest(inputDigest))
            throw new ArgumentException("Creation fingerprints must be SHA-256 hexadecimal digests.");
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Creation receipt time must be UTC.", nameof(createdAtUtc));
        return new RegistrationOrderCreationReceipt
        {
            Id = id,
            TenantId = tenantId,
            OperationFingerprint = fingerprint.ToLowerInvariant(),
            InputDigest = inputDigest.ToLowerInvariant(),
            RegistrationOrderId = orderId,
            CreatedAtUtc = createdAtUtc
        };
    }

    public static bool IsValidOperationKey(string? operationKey) =>
        operationKey is { Length: >= 1 and <= 128 } &&
        operationKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    public static string ComputeOperationFingerprint(
        Guid tenantId, Guid accountUserId, string principalScopeFingerprint, string operationKey)
    {
        if (tenantId == Guid.Empty || accountUserId == Guid.Empty || !IsDigest(principalScopeFingerprint)
            || !IsValidOperationKey(operationKey))
            throw new ArgumentException("An exact tenant, account, trusted principal scope and operation key are required.");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Family = "authenticated-registration-creation:v1",
            TenantId = tenantId,
            AccountUserId = accountUserId,
            PrincipalScope = principalScopeFingerprint.ToLowerInvariant(),
            OperationKey = operationKey
        }))));
    }

    private static bool IsDigest(string? digest) =>
        digest is { Length: 64 } && digest.All(Uri.IsHexDigit);
}
