using System.Text;
using Explore.Domain.ValueObjects;

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
    public AtprotoAuthenticationFlowKind? FlowKind { get; private set; }
    public string ProtectedPayload { get; private set; } = string.Empty;
    public long ExpiresAtUnixMilliseconds { get; private set; }

    public static AtprotoTransientRecord Create(AtprotoTransientPurpose purpose, string tokenDigest,
        AtprotoAuthenticationFlowTarget target, string protectedPayload, long expiresAtUnixMilliseconds)
    {
        if (purpose is not (AtprotoTransientPurpose.OAuthState or AtprotoTransientPurpose.TenantHandoff)) throw new ArgumentOutOfRangeException(nameof(purpose));
        ArgumentNullException.ThrowIfNull(target);
        if (purpose == AtprotoTransientPurpose.TenantHandoff && target.Kind != AtprotoAuthenticationFlowKind.Targeted)
            throw new ArgumentException("A tenant handoff requires an exact tenant target.", nameof(target));
        return CreateCore(purpose, tokenDigest, target.TenantId, target.Kind, protectedPayload, expiresAtUnixMilliseconds);
    }

    public static AtprotoTransientRecord CreateHealthProbe(string tokenDigest, string protectedPayload, long expiresAtUnixMilliseconds) =>
        CreateCore(AtprotoTransientPurpose.HealthProbe, tokenDigest, null, null, protectedPayload, expiresAtUnixMilliseconds);

    private static AtprotoTransientRecord CreateCore(AtprotoTransientPurpose purpose, string tokenDigest, Guid? tenantId,
        AtprotoAuthenticationFlowKind? flowKind, string protectedPayload, long expiresAtUnixMilliseconds)
    {
        ValidateDigest(tokenDigest);
        ArgumentNullException.ThrowIfNull(protectedPayload);
        if (Encoding.UTF8.GetByteCount(protectedPayload) > MaximumProtectedPayloadBytes) throw new ArgumentException("The protected payload exceeds 64 KiB.", nameof(protectedPayload));
        if (expiresAtUnixMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(expiresAtUnixMilliseconds));
        return new() { Id = Guid.CreateVersion7(), Purpose = purpose, TokenDigest = tokenDigest, TenantId = tenantId, FlowKind = flowKind, ProtectedPayload = protectedPayload, ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds };
    }

    internal static void ValidateDigest(string digest)
    {
        if (digest is null || digest.Length != Sha256DigestLength || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase hexadecimal SHA-256 digest is required.", nameof(digest));
    }
}
