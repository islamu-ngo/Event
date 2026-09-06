// ABOUTME: Owns the server-private BFF transient transport without exposing backend implementation types.
// ABOUTME: Binds protected records to purpose, digest, tenant and candidate identity for single-use consumption.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.Blazor.Services.Auth;

public sealed class ApiBackedAtprotoTransientStore(
    IHttpClientFactory clients, AtprotoTransientAssertionService assertions, TimeProvider clock)
{
    public const string HttpClientName = "AtprotoTransientBridge";
    private const int MaximumWireBytes = 80 * 1024;
    private const int MaximumCiphertextBytes = 64 * 1024;
    private static readonly byte[] ProbeBody = "{\"purpose\":\"health_probe\"}"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public async Task<bool> CreateAsync(string purpose, string token, Guid tenantId, byte[] payload, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        string digest = Digest(purpose, token);
        ArgumentNullException.ThrowIfNull(payload);
        if (tenantId == Guid.Empty || payload.Length is 0 or > MaximumCiphertextBytes * 3 / 4
            || !IsLive(purpose, expiresAt.ToUnixTimeMilliseconds()))
            throw new ArgumentException("Invalid ATProto transient creation.");
        string protectedPayload = Convert.ToBase64String(payload);
        long expiry = expiresAt.ToUnixTimeMilliseconds();
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            purpose, tokenDigest = digest, tenantId, protectedPayload, expiresAtUnixMilliseconds = expiry
        }, JsonOptions);
        var result = await SendAsync("create", purpose, body, HttpStatusCode.Conflict, cancellationToken).ConfigureAwait(false);
        if (result is null) return false;
        ValidateCandidate(result, purpose, digest, tenantId);
        if (result.ProtectedPayload != protectedPayload || result.ExpiresAtUnixMilliseconds != expiry)
            throw InvalidResponse();
        return true;
    }

    public async Task<BffAtprotoTransientCandidate?> ReadAsync(string purpose, string token, Guid? expectedTenantId = null,
        CancellationToken cancellationToken = default)
    {
        string digest = Digest(purpose, token);
        if (expectedTenantId == Guid.Empty || (purpose == "tenant_handoff" && expectedTenantId is null))
            throw new ArgumentException("ATProto handoff lookup requires its expected tenant.");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { purpose, tokenDigest = digest, expectedTenantId }, JsonOptions);
        var result = await SendAsync("read", purpose, body, HttpStatusCode.NotFound, cancellationToken).ConfigureAwait(false);
        if (result is not null) ValidateCandidate(result, purpose, digest, expectedTenantId);
        return result;
    }

    public async Task<bool> ConsumeAsync(BffAtprotoTransientCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateCandidate(candidate, candidate.Purpose, candidate.TokenDigest, candidate.TenantId);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            candidateId = candidate.Id, candidate.Purpose, candidate.TokenDigest, expectedTenantId = candidate.TenantId
        }, JsonOptions);
        var result = await SendAsync("consume", candidate.Purpose, body, HttpStatusCode.NotFound, cancellationToken).ConfigureAwait(false);
        if (result is null) return false;
        // A successful delete is usable only for the exact previously validated immutable candidate.
        if (result != candidate) throw InvalidResponse();
        return IsLive(candidate.Purpose, candidate.ExpiresAtUnixMilliseconds);
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2), clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, AtprotoTransientAssertionService.Prefix + "probe")
        {
            Content = new ByteArrayContent(ProbeBody)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add(AtprotoTransientAssertionService.HeaderName, assertions.Issue("probe", "health_probe", ProbeBody));
        using var client = clients.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NoContent;
    }

    private async Task<BffAtprotoTransientCandidate?> SendAsync(string operation, string purpose, byte[] body,
        HttpStatusCode absent, CancellationToken cancellationToken)
    {
        if (body.Length > MaximumWireBytes) throw new ArgumentException("ATProto transient request is too large.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Post, AtprotoTransientAssertionService.Prefix + operation)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add(AtprotoTransientAssertionService.HeaderName, assertions.Issue(operation, purpose, body));
        using var client = clients.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == absent) return null;
        if (response.StatusCode != HttpStatusCode.OK
            || response.Content.Headers.ContentType?.MediaType != "application/json")
            throw InvalidResponse();
        // Native bounded buffering covers chunked responses as well as declared Content-Length.
        await response.Content.LoadIntoBufferAsync(MaximumWireBytes, deadline.Token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<BffAtprotoTransientCandidate>(
                await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false), JsonOptions)
                ?? throw InvalidResponse();
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private void ValidateCandidate(BffAtprotoTransientCandidate candidate, string purpose, string digest, Guid? tenant)
    {
        if (purpose is not ("oauth_state" or "tenant_handoff")
            || candidate.Id == Guid.Empty || candidate.Purpose != purpose || candidate.TokenDigest != digest
            || digest is not { Length: 64 } || digest.Any(c => !char.IsAsciiHexDigitLower(c))
            || candidate.TenantId == Guid.Empty || (tenant.HasValue && candidate.TenantId != tenant.Value)
            || string.IsNullOrEmpty(candidate.ProtectedPayload)
            || Encoding.UTF8.GetByteCount(candidate.ProtectedPayload) > MaximumCiphertextBytes
            || !IsLive(purpose, candidate.ExpiresAtUnixMilliseconds))
            throw InvalidResponse();
    }

    private bool IsLive(string purpose, long expiresAt)
    {
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        return expiresAt > now && expiresAt <= now + (purpose == "oauth_state" ? 600_000 : 120_000);
    }

    private static string Digest(string purpose, string token)
    {
        if (purpose is not ("oauth_state" or "tenant_handoff")
            || string.IsNullOrWhiteSpace(token) || token.Length is < 16 or > 512
            || token.Any(char.IsControl))
            throw new ArgumentException("Invalid ATProto transient locator.");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static InvalidOperationException InvalidResponse() => new("ATProto transient operation failed.");
}

public sealed record BffAtprotoTransientCandidate(Guid Id, string Purpose, string TokenDigest, Guid TenantId,
    string ProtectedPayload, long ExpiresAtUnixMilliseconds)
{
    public override string ToString() => nameof(BffAtprotoTransientCandidate);
}
