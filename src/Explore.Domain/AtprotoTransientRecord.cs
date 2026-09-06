// ABOUTME: Models an immutable encrypted ATProto authentication value with tenant and expiry binding.
// ABOUTME: Keeps locator digests and health probes closed while accepting only bounded opaque payloads.

using System.Text;

namespace Explore.Domain;

public enum AtprotoTransientPurpose
{
    OAuthState = 1,
    TenantHandoff = 2,
    HealthProbe = 3,
}

public sealed class AtprotoTransientRecord
{
    public const int Sha256DigestLength = 64;
    public const int MaximumProtectedPayloadBytes = 64 * 1024;

    private AtprotoTransientRecord() { }

    public Guid Id { get; private set; }
    public AtprotoTransientPurpose Purpose { get; private set; }
    public string TokenDigest { get; private set; } = string.Empty;
    public Guid? TenantId { get; private set; }
    public string ProtectedPayload { get; private set; } = string.Empty;
    public long ExpiresAtUnixMilliseconds { get; private set; }

    public static AtprotoTransientRecord Create(AtprotoTransientPurpose purpose, string tokenDigest, Guid tenantId, string protectedPayload, long expiresAtUnixMilliseconds)
    {
        if (purpose is not (AtprotoTransientPurpose.OAuthState or AtprotoTransientPurpose.TenantHandoff)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (tenantId == Guid.Empty) throw new ArgumentException("Authentication transient records require a tenant.", nameof(tenantId));
        return CreateCore(purpose, tokenDigest, tenantId, protectedPayload, expiresAtUnixMilliseconds);
    }

    public static AtprotoTransientRecord CreateHealthProbe(string tokenDigest, string protectedPayload, long expiresAtUnixMilliseconds) =>
        CreateCore(AtprotoTransientPurpose.HealthProbe, tokenDigest, null, protectedPayload, expiresAtUnixMilliseconds);

    private static AtprotoTransientRecord CreateCore(AtprotoTransientPurpose purpose, string tokenDigest, Guid? tenantId, string protectedPayload, long expiresAtUnixMilliseconds)
    {
        ValidateDigest(tokenDigest);
        ArgumentNullException.ThrowIfNull(protectedPayload);
        if (Encoding.UTF8.GetByteCount(protectedPayload) > MaximumProtectedPayloadBytes) throw new ArgumentException("The protected payload exceeds 64 KiB.", nameof(protectedPayload));
        if (expiresAtUnixMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(expiresAtUnixMilliseconds));
        return new() { Id = Guid.CreateVersion7(), Purpose = purpose, TokenDigest = tokenDigest, TenantId = tenantId, ProtectedPayload = protectedPayload, ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds };
    }

    internal static void ValidateDigest(string digest)
    {
        if (digest is null || digest.Length != Sha256DigestLength || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase hexadecimal SHA-256 digest is required.", nameof(digest));
    }
}
