// ABOUTME: Validates bounded ES256 machine assertions against the existing instance OAuth signing authority.
// ABOUTME: Rejects ambiguous JSON, key-source injection and byte/path/purpose confusion before durable replay admission.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Federation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

namespace Explore.API.Authentication;

public sealed class AtprotoTransientAssertionValidator(ISecretResolver secrets, TimeProvider clock)
{
    public async Task<AtprotoTransientAssertion?> ValidateAsync(string token, HttpRequest request,
        byte[] body, CancellationToken cancellationToken)
    {
        if (!AtprotoJwtService.IsBoundedCompactJwt(token, AtprotoTransientAuthenticationDefaults.MaximumAssertionBytes)
            || AtprotoTransientAuthenticationDefaults.Operation(request) is not { } operation
            || request.QueryString.HasValue || request.PathBase.HasValue
            || (request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget is { Length: > 0 } rawTarget
                && !string.Equals(rawTarget, request.Path.Value, StringComparison.Ordinal))) return null;

        string[] parts = token.Split('.');
        JsonDocument? header = null;
        JsonDocument? claims = null;
        byte[] signature;
        try
        {
            header = JsonDocument.Parse(Decode(parts[0]), new JsonDocumentOptions { MaxDepth = 4 });
            claims = JsonDocument.Parse(Decode(parts[1]), new JsonDocumentOptions { MaxDepth = 4 });
            signature = Decode(parts[2]);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            header?.Dispose();
            claims?.Dispose();
            return null;
        }
        using (header)
        using (claims)
        {
            JsonElement h = header.RootElement;
            JsonElement c = claims.RootElement;
            if (!HasUniqueProperties(h) || !HasUniqueProperties(c)
                || h.EnumerateObject().Any(p => p.Name is not ("alg" or "kid" or "typ"))
                || String(h, "alg") != "ES256" || String(h, "typ") != "JWT"
                || String(h, "kid") is not { Length: > 0 and <= 128 } kid
                || signature.Length != 64
                || c.EnumerateObject().Any(p => p.Name is not ("iss" or "aud" or "sub" or "use" or "jti"
                    or "iat" or "exp" or "method" or "path" or "operation" or "purpose" or "body_sha256"))
                || String(c, "iss") != AtprotoTransientAuthenticationDefaults.Issuer
                || String(c, "aud") != AtprotoTransientAuthenticationDefaults.Audience
                || String(c, "sub") != AtprotoTransientAuthenticationDefaults.Subject
                || String(c, "use") != AtprotoTransientAuthenticationDefaults.Use
                || String(c, "method") != "POST" || String(c, "path") != request.Path.Value
                || String(c, "operation") != operation
                || (operation == "probe" ? String(c, "purpose") != "health_probe"
                    : String(c, "purpose") is not ("oauth_state" or "tenant_handoff"))
                || !Guid.TryParseExact(String(c, "jti"), "D", out var jti) || jti == Guid.Empty
                || !Integer(c, "iat", out long issuedAt) || !Integer(c, "exp", out long expiresAt)
                || !Fresh(issuedAt, expiresAt)
                || String(c, "body_sha256") != Convert.ToHexStringLower(SHA256.HashData(body))) return null;
            try
            {
                using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
                if (!HasUniqueProperties(document.RootElement)
                    || String(document.RootElement, "purpose") != String(c, "purpose")
                    || (operation == "probe" && document.RootElement.EnumerateObject().Any(property => property.Name != "purpose"))) return null;
            }
            catch (JsonException) { return null; }

            var resolved = await secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
                tenantId: null, cancellationToken);
            var ring = InfrastructureAtprotoKeyRing.Parse(resolved.Value);
            if (!ring.IsReady) throw new InvalidOperationException("Transient signing authority unavailable.");
            if (!ring.HasKey(kid)) return null;
            using var key = ring.CreateEcdsaKey(kid, includePrivateKey: false);
            if (!key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return null;
            return new(jti.ToString("D"), checked((expiresAt + AtprotoTransientAuthenticationDefaults.SkewSeconds) * 1000), String(c, "purpose")!);
        }
    }

    private bool Fresh(long issuedAt, long expiresAt)
    {
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        return issuedAt >= now - 35 && issuedAt <= now + 5
            && expiresAt > issuedAt && expiresAt <= issuedAt + 30 && expiresAt > now - 5;
    }

    private static bool Integer(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number
            && field.TryGetInt64(out value);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static bool HasUniqueProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return false;
            if (property.Value.ValueKind == JsonValueKind.Object && !HasUniqueProperties(property.Value)) return false;
            // The private wire contract has no arrays, including array-valued security claims.
            if (property.Value.ValueKind == JsonValueKind.Array) return false;
        }
        return true;
    }

    private static byte[] Decode(string part)
    {
        if (part.Length == 0 || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new FormatException();
        byte[] decoded = Base64UrlEncoder.DecodeBytes(part);
        if (Base64UrlEncoder.Encode(decoded) != part) throw new FormatException();
        return decoded;
    }
}

public sealed record AtprotoTransientAssertion(string Jti, long AcceptanceExpiresAtUnixMilliseconds, string Purpose)
{
    public override string ToString() => nameof(AtprotoTransientAssertion);
}
