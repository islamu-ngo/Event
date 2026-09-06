// ABOUTME: Binds independent ATProto login flows to one fixed-expiry, origin-protected browser proof cookie.
// ABOUTME: Keeps raw proof out of flow state and reserves the handoff lifetime before state expires.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Blazor.Services.Auth;

public sealed class AtprotoBrowserProof(IDataProtectionProvider protection, TimeProvider clock)
{
    public const string CookieName = "__Host-event-atproto-proof";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HandoffBudget = TimeSpan.FromMinutes(2);

    public BffAtprotoBrowserBinding CreateBinding(HttpContext context)
    {
        var protector = Protector(context.Request);
        var proof = ReadProof(context.Request, protector);
        var now = clock.GetUtcNow();
        if (proof is not null && proof.ExpiresAt <= now)
        {
            CryptographicOperations.ZeroMemory(proof.Secret);
            proof = null;
        }
        bool issueCookie = proof is null;
        proof ??= new(RandomNumberGenerator.GetBytes(32), now, now + Lifetime);
        try
        {
            if (proof.ExpiresAt - now <= HandoffBudget)
                throw new AtprotoProofExpiryException(Math.Clamp((int)Math.Ceiling((proof.ExpiresAt - now).TotalSeconds), 1, 120));
            if (issueCookie)
            {
                string value = WebEncoders.Base64UrlEncode(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(proof)));
                if (value.Length >= 1024) throw InvalidProof();
                context.Response.Cookies.Append(CookieName, value, new CookieOptions
                {
                    Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/",
                    MaxAge = Lifetime, Expires = proof.ExpiresAt, IsEssential = true
                });
            }
            byte[] flowId = RandomNumberGenerator.GetBytes(32);
            return new(WebEncoders.Base64UrlEncode(flowId),
                WebEncoders.Base64UrlEncode(HMACSHA256.HashData(proof.Secret, flowId)), proof.ExpiresAt);
        }
        finally { CryptographicOperations.ZeroMemory(proof.Secret); }
    }

    public bool Validate(HttpRequest request, BffAtprotoBrowserBinding binding)
    {
        if (!IsLive(binding)) return false;
        ProofCookie? proof = null;
        try
        {
            proof = ReadProof(request, Protector(request));
            return proof is not null && proof.ExpiresAt == binding.ProofExpiresAt
                && CryptographicOperations.FixedTimeEquals(Decode32(binding.ProofDigest),
                    HMACSHA256.HashData(proof.Secret, Decode32(binding.FlowId)));
        }
        catch (InvalidOperationException) { return false; }
        finally
        {
            if (proof is not null) CryptographicOperations.ZeroMemory(proof.Secret);
        }
    }

    public DateTimeOffset StateExpiry(BffAtprotoBrowserBinding binding, DateTimeOffset sdkExpiry)
    {
        if (!IsLive(binding)) throw InvalidProof();
        var now = clock.GetUtcNow();
        var expiry = new[] { sdkExpiry, now.AddMinutes(10), binding.ProofExpiresAt - HandoffBudget }.Min();
        return expiry > now ? expiry : throw InvalidProof();
    }

    public DateTimeOffset HandoffExpiry(BffAtprotoBrowserBinding binding)
    {
        if (!IsLive(binding)) throw InvalidProof();
        var expiry = clock.GetUtcNow() + HandoffBudget;
        return expiry < binding.ProofExpiresAt ? expiry : binding.ProofExpiresAt;
    }

    public bool IsLive(BffAtprotoBrowserBinding? binding) => binding is not null
        && binding.ProofExpiresAt > clock.GetUtcNow()
        && binding.ProofExpiresAt <= clock.GetUtcNow() + Lifetime
        && Decode32(binding.FlowId).Length == 32 && Decode32(binding.ProofDigest).Length == 32;

    private ProofCookie? ReadProof(HttpRequest request, IDataProtector protector)
    {
        if (request.Headers.Cookie.SelectMany(header => (header ?? string.Empty).Split(';'))
            .Count(part => part.TrimStart().StartsWith(CookieName + "=", StringComparison.Ordinal)) > 1)
            throw InvalidProof();
        if (!request.Cookies.TryGetValue(CookieName, out string? value)) return null;
        try
        {
            if (value.Length is 0 or >= 1024) throw InvalidProof();
            var proof = JsonSerializer.Deserialize<ProofCookie>(protector.Unprotect(WebEncoders.Base64UrlDecode(value)))
                ?? throw InvalidProof();
            if (proof.Secret is not { Length: 32 } || proof.IssuedAt > clock.GetUtcNow()
                || proof.ExpiresAt - proof.IssuedAt != Lifetime)
            {
                if (proof.Secret is not null) CryptographicOperations.ZeroMemory(proof.Secret);
                throw InvalidProof();
            }
            return proof;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            throw InvalidProof();
        }
    }

    private IDataProtector Protector(HttpRequest request)
    {
        if (!request.IsHttps || !Uri.TryCreate("https://" + request.Host.Value + "/", UriKind.Absolute, out var origin)
            || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/")
            throw InvalidProof();
        return protection.CreateProtector(typeof(AtprotoBrowserProof).FullName!, "v1", origin.AbsoluteUri);
    }

    private static byte[] Decode32(string? value)
    {
        if (value is not { Length: 43 }) return [];
        try
        {
            byte[] bytes = WebEncoders.Base64UrlDecode(value);
            return bytes.Length == 32 && WebEncoders.Base64UrlEncode(bytes) == value ? bytes : [];
        }
        catch (FormatException) { return []; }
    }

    private static InvalidOperationException InvalidProof() => new("ATProto browser proof is invalid.");

    private sealed record ProofCookie(byte[] Secret, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
    {
        public override string ToString() => nameof(ProofCookie);
    }
}

public sealed record BffAtprotoBrowserBinding(string FlowId, string ProofDigest, DateTimeOffset ProofExpiresAt)
{
    public override string ToString() => nameof(BffAtprotoBrowserBinding);
}

public sealed class AtprotoProofExpiryException(int retryAfterSeconds) : InvalidOperationException("The browser proof is near expiry. Retry after it expires.")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
