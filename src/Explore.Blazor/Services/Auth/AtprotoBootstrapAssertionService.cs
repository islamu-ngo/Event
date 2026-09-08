using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Blazor.Services.Auth;

public sealed class AtprotoBootstrapAssertionService(
    AtprotoClientKeyProvider keyProvider,
    TimeProvider timeProvider)
{
    public const string HeaderName = "X-Atproto-Bootstrap-Assertion";
    public const string Issuer = "islamu-event-blazor:atproto-bootstrap";
    public const string Audience = "islamu-event-api:atproto-bootstrap";
    public const string BridgePath = "/api/auth/atproto/session";
    public const string TenantClaim = "tenant_id";
    public const string ClassificationClaim = "subject_classification";
    public const string CanonicalActorIdClaim = "canonical_actor_id";
    public const string ExpectedCanonicalActorConcurrencyStampClaim = "expected_actor_concurrency_stamp";
    public const string MethodClaim = "http_method";
    public const string PathClaim = "http_path";
    public const string SessionBridgeHeaderName = "X-Atproto-Session-Bridge-Assertion";
    public const string SessionBridgeIssuer = "islamu-event-blazor:atproto-session-bridge";
    public const string SessionBridgeAudience = "islamu-event-api:atproto-session-bridge";
    public const string SessionBridgePath = "/api/auth/atproto/session/current";
    public const string UserIdClaim = "user_id";
    public const string DidClaim = "atproto_did";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    public string Issue(
        Guid tenantId,
        string did,
        string rawClassification,
        HttpMethod method,
        string path,
        Guid? canonicalActorId = null,
        Guid? expectedCanonicalActorConcurrencyStamp = null)
    {
        if (tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(did)
            || did.Length is < 5 or > 2048
            || !did.StartsWith("did:", StringComparison.Ordinal)
            || did.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || method != HttpMethod.Post
            || !string.Equals(path, BridgePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The ATProto bootstrap assertion target is invalid.");
        }

        if (canonicalActorId.HasValue != expectedCanonicalActorConcurrencyStamp.HasValue
            || canonicalActorId == Guid.Empty
            || expectedCanonicalActorConcurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("The ATProto canonical Actor target is invalid.");
        }

        var classification = AtprotoSubjectClassifications.Normalize(rawClassification);

        if (!keyProvider.IsReady || keyProvider.ActiveKeyId is not { } keyId)
        {
            throw new InvalidOperationException("ATProto bootstrap signing is unavailable.");
        }

        var now = timeProvider.GetUtcNow();
        using var ecdsa = keyProvider.CreateActiveSigningKey();
        var key = CreateSigningKey(ecdsa, keyId);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(Lifetime).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString("D"),
                [JwtRegisteredClaimNames.Sub] = "event-blazor-bff",
                [TenantClaim] = tenantId.ToString("D"),
                [DidClaim] = did,
                [ClassificationClaim] = classification,
                [MethodClaim] = HttpMethods.Post,
                [PathClaim] = BridgePath
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
            TokenType = "JWT"
        };

        if (canonicalActorId is { } actorId)
        {
            descriptor.Claims[CanonicalActorIdClaim] = actorId.ToString("D");
            descriptor.Claims[ExpectedCanonicalActorConcurrencyStampClaim] = expectedCanonicalActorConcurrencyStamp!.Value.ToString("D");
        }

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    public string IssueSessionBridge(
        Guid tenantId,
        Guid userId,
        string did,
        HttpMethod method)
    {
        if (tenantId == Guid.Empty
            || userId == Guid.Empty
            || string.IsNullOrWhiteSpace(did)
            || did.Length is < 5 or > 2048
            || !did.StartsWith("did:", StringComparison.Ordinal)
            || did.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || (method != HttpMethod.Get && method != HttpMethod.Post && method != HttpMethod.Delete))
        {
            throw new ArgumentException("The ATProto session bridge assertion target is invalid.");
        }

        if (!keyProvider.IsReady || keyProvider.ActiveKeyId is not { } keyId)
        {
            throw new InvalidOperationException("ATProto session bridge signing is unavailable.");
        }

        var now = timeProvider.GetUtcNow();
        using var ecdsa = keyProvider.CreateActiveSigningKey();
        var key = CreateSigningKey(ecdsa, keyId);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = SessionBridgeIssuer,
            Audience = SessionBridgeAudience,
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(Lifetime).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString("D"),
                [JwtRegisteredClaimNames.Sub] = "event-blazor-bff",
                [TenantClaim] = tenantId.ToString("D"),
                [UserIdClaim] = userId.ToString("D"),
                [DidClaim] = did,
                [MethodClaim] = method.Method,
                [PathClaim] = SessionBridgePath
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
            TokenType = "JWT"
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private static ECDsaSecurityKey CreateSigningKey(System.Security.Cryptography.ECDsa ecdsa, string keyId) =>
        new(ecdsa)
        {
            KeyId = keyId,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
}
