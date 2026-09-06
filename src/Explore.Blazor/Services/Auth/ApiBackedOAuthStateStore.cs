// ABOUTME: Adapts protected CarpaNet OAuth state to the instance-private relational transient transport.
// ABOUTME: Validates issuer, tenant, origin and browser binding before candidate-bound consumption.

using System.Text.Json;
using System.Security.Cryptography;
using CarpaNet.OAuth.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Blazor.Services.Auth;

public sealed class ApiBackedOAuthStateStore(
    ApiBackedAtprotoTransientStore transport, IDataProtectionProvider protection,
    AtprotoOAuthFlowContext flow, IHttpContextAccessor accessor, AtprotoTenantOriginResolver resolver,
    AtprotoBrowserProof proof, TimeProvider clock) : IOAuthStateStore
{
    private const string Purpose = "oauth_state";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector(typeof(ApiBackedOAuthStateStore).FullName!, "oauth-state-v2");

    public static string EncodeAppState(AtprotoOAuthFlowSeed seed) => JsonSerializer.Serialize(seed, JsonOptions);

    public async Task StoreAsync(string state, OAuthStateData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var seed = ParseSeed(data);
        var expiry = DateTimeOffset.FromUnixTimeMilliseconds(proof.StateExpiry(seed.BrowserBinding, data.ExpiresAt).ToUnixTimeMilliseconds());
        // Snapshot all SDK-owned PKCE/DPoP fields unchanged, without mutating the caller's SDK object.
        var document = JsonSerializer.SerializeToNode(data, JsonOptions)!.AsObject();
        document["expiresAt"] = expiry;
        var payload = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
        if (!await transport.CreateAsync(Purpose, state, seed.TenantId, payload, expiry, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("ATProto OAuth state collision.");
    }

    public async Task<string?> GetPinnedKeyIdAsync(string state, CancellationToken cancellationToken = default) =>
        (await ReadValidatedAsync(state, cancellationToken).ConfigureAwait(false))?.Seed.OAuthClientKeyId;

    public async Task<OAuthStateData?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        if (flow.Binding is not null) return null;
        var recovered = await ReadValidatedAsync(state, cancellationToken).ConfigureAwait(false);
        if (recovered is null) return null;
        var context = accessor.HttpContext ?? throw new InvalidOperationException("ATProto callback HTTP context is unavailable.");
        var issuerValues = context.Request.Query["iss"];
        if (!context.Request.IsHttps || issuerValues.Count != 1
            || !Uri.TryCreate(issuerValues[0], UriKind.Absolute, out var issuer)
            || !AtprotoOAuthFlowValidation.IsHttpsOrigin(issuer)
            || issuer != new Uri(recovered.Data.Issuer)
            || !Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host.Value}/", UriKind.Absolute, out var callbackOrigin)
            || !AtprotoTenantOriginResolver.OriginsEqual(callbackOrigin, resolver.ParseCanonicalOrigin()))
            return null;
        if (AtprotoTenantOriginResolver.OriginsEqual(callbackOrigin, recovered.Seed.Origin)
            && !proof.Validate(context.Request, recovered.Seed.BrowserBinding))
            return null;
        if (!await transport.ConsumeAsync(recovered.Candidate, cancellationToken).ConfigureAwait(false)) return null;
        flow.BindConsumedState(new(recovered.Seed, issuer));
        return recovered.Data;
    }

    private async Task<RecoveredState?> ReadValidatedAsync(string state, CancellationToken cancellationToken)
    {
        var candidate = await transport.ReadAsync(Purpose, state, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (candidate is null) return null;
        try
        {
            var data = JsonSerializer.Deserialize<OAuthStateData>(protector.Unprotect(Convert.FromBase64String(candidate.ProtectedPayload)), JsonOptions);
            if (data is null || data.ExpiresAt <= clock.GetUtcNow()
                || data.ExpiresAt.ToUnixTimeMilliseconds() != candidate.ExpiresAtUnixMilliseconds) return null;
            var seed = ParseSeed(data);
            if (candidate.TenantId != seed.TenantId
                || data.ExpiresAt > seed.BrowserBinding.ProofExpiresAt.AddMinutes(-2)) return null;
            return new(candidate, data, seed);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private AtprotoOAuthFlowSeed ParseSeed(OAuthStateData data)
    {
        if (string.IsNullOrWhiteSpace(data.AppState) || data.AppState.Length > 4096)
            throw new InvalidOperationException("ATProto application state is invalid.");
        var seed = JsonSerializer.Deserialize<AtprotoOAuthFlowSeed>(data.AppState, JsonOptions)
            ?? throw new InvalidOperationException("ATProto application state is invalid.");
        AtprotoOAuthFlowValidation.Validate(seed, resolver, proof);
        if (!Uri.TryCreate(data.Issuer, UriKind.Absolute, out var issuer) || !AtprotoOAuthFlowValidation.IsHttpsOrigin(issuer)
            || !Uri.TryCreate(data.PdsUrl, UriKind.Absolute, out var pds) || pds != seed.ExpectedPdsUri)
            throw new InvalidOperationException("ATProto OAuth issuer binding is invalid.");
        return seed;
    }

    private sealed record RecoveredState(BffAtprotoTransientCandidate Candidate, OAuthStateData Data, AtprotoOAuthFlowSeed Seed)
    {
        public override string ToString() => nameof(RecoveredState);
    }
}
