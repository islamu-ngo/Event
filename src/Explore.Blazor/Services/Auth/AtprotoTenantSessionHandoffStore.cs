// ABOUTME: Protects first-party ATProto sessions behind tenant- and browser-bound one-time relational handoffs.
// ABOUTME: Validates decrypted metadata and proof before requesting candidate-bound consumption from the private API.

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Explore.Blazor.Services.Auth;

public sealed record AtprotoTenantHandoff(AtprotoOAuthFlowSeed Seed, AtprotoBffSessionResult Session, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(AtprotoTenantHandoff);
}

public sealed class AtprotoTenantSessionHandoffStore(ApiBackedAtprotoTransientStore transport,
    IDataProtectionProvider protection, AtprotoTenantOriginResolver resolver, AtprotoBrowserProof proof, TimeProvider clock)
{
    private const string Purpose = "tenant_handoff";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector(typeof(AtprotoTenantSessionHandoffStore).FullName!, "tenant-handoff-v2");

    public async Task<string> CreateAsync(AtprotoOAuthFlowSeed seed, AtprotoBffSessionResult session, CancellationToken cancellationToken)
    {
        AtprotoOAuthFlowValidation.Validate(seed, resolver, proof);
        if (!ValidSession(seed, session)) throw new InvalidOperationException("ATProto handoff session binding is invalid.");
        var expiry = DateTimeOffset.FromUnixTimeMilliseconds(proof.HandoffExpiry(seed.BrowserBinding).ToUnixTimeMilliseconds());
        var handoff = new AtprotoTenantHandoff(seed, session, expiry);
        var payload = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(handoff, JsonOptions));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            if (await transport.CreateAsync(Purpose, code, seed.TenantId, payload, expiry, cancellationToken).ConfigureAwait(false))
                return code;
        }
        throw new InvalidOperationException("ATProto tenant handoff could not be created.");
    }

    public async Task<AtprotoTenantHandoff?> ConsumeAsync(string code, HttpRequest request, CancellationToken cancellationToken)
    {
        AtprotoTenantOriginBinding current;
        try
        {
            if (!request.IsHttps) return null;
            current = resolver.Resolve(request);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return null; }

        var candidate = await transport.ReadAsync(Purpose, code, current.TenantId, cancellationToken).ConfigureAwait(false);
        if (candidate is null) return null;
        AtprotoTenantHandoff? handoff;
        try
        {
            handoff = JsonSerializer.Deserialize<AtprotoTenantHandoff>(protector.Unprotect(Convert.FromBase64String(candidate.ProtectedPayload)), JsonOptions);
            if (handoff is null || handoff.Seed is null || handoff.Seed.Origin is null || handoff.ExpiresAt <= clock.GetUtcNow()
                || handoff.ExpiresAt.ToUnixTimeMilliseconds() != candidate.ExpiresAtUnixMilliseconds
                || handoff.Seed.TenantId != candidate.TenantId || handoff.Seed.TenantId != current.TenantId
                || handoff.Seed.TenantSlug != current.TenantSlug
                || !AtprotoTenantOriginResolver.OriginsEqual(handoff.Seed.Origin, current.Origin)
                || !proof.Validate(request, handoff.Seed.BrowserBinding)
                || handoff.ExpiresAt > handoff.Seed.BrowserBinding.ProofExpiresAt
                || !ValidSession(handoff.Seed, handoff.Session)) return null;
            AtprotoOAuthFlowValidation.Validate(handoff.Seed, resolver, proof);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return null;
        }
        return await transport.ConsumeAsync(candidate, cancellationToken).ConfigureAwait(false) ? handoff : null;
    }

    private bool ValidSession(AtprotoOAuthFlowSeed seed, AtprotoBffSessionResult? session) => session is not null
        && session.UserId != Guid.Empty && session.ActorId != Guid.Empty && session.ParticipationId != Guid.Empty
        && session.Did == seed.ExpectedDid && session.Classification == seed.Classification
        && session.CanonicalActorId == seed.CanonicalActorId
        && session.ExpectedCanonicalActorConcurrencyStamp == seed.ExpectedCanonicalActorConcurrencyStamp
        && !string.IsNullOrWhiteSpace(session.AccessToken) && session.AccessToken.Length <= 16 * 1024
        && session.ExpiresAt > clock.GetUtcNow();
}
