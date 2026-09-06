// ABOUTME: Signs purpose-separated transient requests with the existing active OAuth-client key authority.
// ABOUTME: Binds exact serialized bytes and canonical operation paths without borrowing a user's or pinned flow's identity.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Blazor.Services.Auth;

public sealed class AtprotoTransientAssertionService(AtprotoClientKeyProvider keys, TimeProvider clock)
{
    public const string HeaderName = "X-Atproto-Transient-Assertion";
    public const string Prefix = "/api/auth/atproto/transient/";

    public string Issue(string operation, string purpose, ReadOnlySpan<byte> body)
    {
        if (operation is not ("create" or "read" or "consume" or "probe")
            || (operation == "probe" ? purpose != "health_probe" : purpose is not ("oauth_state" or "tenant_handoff"))
            || body.Length is 0 or > 80 * 1024)
            throw new ArgumentException("Invalid ATProto transient assertion target.");

        if (!keys.IsReady || keys.ActiveKeyId is not { } keyId)
            throw new InvalidOperationException("ATProto transient signing is unavailable.");

        using var ecdsa = keys.CreateActiveSigningKey();
        var signingKey = new ECDsaSecurityKey(ecdsa)
        {
            KeyId = keyId,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        long issuedAt = clock.GetUtcNow().ToUnixTimeSeconds();
        var payload = new JwtPayload
        {
            ["iss"] = "event-atproto-transient-bff",
            ["aud"] = "event-atproto-transient-api",
            ["sub"] = "event-blazor-bff",
            ["use"] = "atproto-transient",
            ["jti"] = Guid.CreateVersion7().ToString("D"),
            ["iat"] = issuedAt,
            ["exp"] = issuedAt + 30,
            ["method"] = "POST",
            ["path"] = Prefix + operation,
            ["operation"] = operation,
            ["purpose"] = purpose,
            ["body_sha256"] = Convert.ToHexStringLower(SHA256.HashData(body))
        };
        var header = new JwtHeader(new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}
