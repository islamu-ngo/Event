using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using CarpaNet.OAuth.Storage;

namespace Explore.Blazor.Services.Auth;

public sealed class ApiBackedOAuthSessionStore(
    IHttpClientFactory httpClientFactory,
    AtprotoBootstrapAssertionService assertionService,
    AtprotoOAuthFlowContext flowContext,
    TimeProvider timeProvider,
    AtprotoAuthenticationMetrics metrics) : IOAuthSessionStore
{
    public const string HttpClientName = "AtprotoSessionBridge";
    private const int MaximumSessionJsonBytes = 128 * 1024;
    private const int MaximumBridgeResponseBytes = 32 * 1024;
    private const int MaximumStoredSessionResponseBytes = 160 * 1024;
    private const int MaximumPlatformTokenBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StoreAsync(
        string sub,
        OAuthSessionData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var binding = flowContext.Binding
            ?? throw new InvalidOperationException("ATProto OAuth state must be consumed before session storage.");
        ValidateCarpaSession(sub, data, binding);

        var sessionElement = JsonSerializer.SerializeToElement(data, JsonOptions);
        var requestBody = new BffAtprotoSessionBridgeRequest(
            binding.Seed.ExpectedDid,
            binding.Seed.ExpectedPdsUri.AbsoluteUri,
            binding.Seed.OAuthClientKeyId,
            binding.Seed.Classification,
            sessionElement,
            binding.Seed.CanonicalActorId,
            binding.Seed.ExpectedCanonicalActorConcurrencyStamp);
        var body = JsonSerializer.SerializeToUtf8Bytes(requestBody, JsonOptions);
        if (body.Length > MaximumSessionJsonBytes)
        {
            throw new InvalidOperationException("ATProto OAuth session payload is too large.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, AtprotoBootstrapAssertionService.BridgePath)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TryAddWithoutValidation("X-Tenant-Slug", binding.Seed.TenantSlug);
        AtprotoBootstrapRequestOptions.Bind(
            request,
            binding.Seed.TenantId,
            binding.Seed.ExpectedDid,
            binding.Seed.Classification,
            binding.Seed.CanonicalActorId,
            binding.Seed.ExpectedCanonicalActorConcurrencyStamp);
        request.Headers.TryAddWithoutValidation(
            AtprotoBootstrapAssertionService.HeaderName,
            assertionService.Issue(
                binding.Seed.TenantId,
                binding.Seed.ExpectedDid,
                binding.Seed.Classification,
                HttpMethod.Post,
                AtprotoBootstrapAssertionService.BridgePath,
                binding.Seed.CanonicalActorId,
                binding.Seed.ExpectedCanonicalActorConcurrencyStamp));

        var started = Stopwatch.GetTimestamp();
        var outcome = AtprotoAuthenticationOutcome.InternalFailure;
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                outcome = AtprotoAuthenticationOutcome.PdsUnavailable;
                throw new InvalidOperationException("ATProto session verification failed.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var boundedStream = new BoundedReadStream(stream, MaximumBridgeResponseBytes);
            var bridgeResult = await JsonSerializer.DeserializeAsync<BffAtprotoSessionBridgeResponse>(
                boundedStream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("ATProto session bridge returned no result.");
            ValidateBridgeResult(bridgeResult, binding);
            flowContext.CaptureSession(new(
                bridgeResult.UserId,
                bridgeResult.ActorId,
                bridgeResult.ParticipationId,
                bridgeResult.Did,
                bridgeResult.Classification,
                bridgeResult.AccessToken,
                bridgeResult.ExpiresAt,
                bridgeResult.CanonicalActorId,
                bridgeResult.ExpectedCanonicalActorConcurrencyStamp));
            outcome = AtprotoAuthenticationOutcome.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = AtprotoAuthenticationOutcome.Cancelled;
            throw;
        }
        catch
        {
            if (outcome == AtprotoAuthenticationOutcome.InternalFailure)
            {
                outcome = AtprotoAuthenticationOutcome.ValidationFailed;
            }

            throw;
        }
        finally
        {
            metrics.Record(
                AtprotoAuthenticationOperation.BridgeVerification,
                outcome,
                Stopwatch.GetElapsedTime(started));
        }
    }

    public async Task<OAuthSessionData?> GetAsync(
        string sub,
        CancellationToken cancellationToken = default)
    {
        var context = RequireAuthenticatedSession(sub);
        using var request = CreateAuthenticatedSessionRequest(HttpMethod.Get, context);
        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("ATProto OAuth session restore failed.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var boundedStream = new BoundedReadStream(stream, MaximumStoredSessionResponseBytes);
        var bridgeResult = await JsonSerializer.DeserializeAsync<BffAtprotoStoredSessionBridgeResponse>(
            boundedStream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("ATProto OAuth session restore returned no result.");
        if (!string.Equals(bridgeResult.Did, context.Binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || !string.Equals(bridgeResult.OAuthClientKeyId, context.Binding.Seed.OAuthClientKeyId, StringComparison.Ordinal)
            || !Uri.TryCreate(bridgeResult.ExpectedPdsUri, UriKind.Absolute, out var returnedPds)
            || !UrisEqual(returnedPds, context.Binding.Seed.ExpectedPdsUri))
        {
            throw new InvalidOperationException("ATProto OAuth session restore binding is invalid.");
        }

        var session = bridgeResult.OAuthSession.Deserialize<OAuthSessionData>(JsonOptions)
            ?? throw new InvalidOperationException("ATProto OAuth session restore payload is invalid.");
        ValidateCarpaSession(sub, session, context.Binding);
        return session;
    }

    public async Task DeleteAsync(
        string sub,
        CancellationToken cancellationToken = default)
    {
        var context = RequireAuthenticatedSession(sub);
        using var request = CreateAuthenticatedSessionRequest(HttpMethod.Delete, context);
        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("ATProto OAuth session deletion failed.");
        }
    }

    private AuthenticatedSessionContext RequireAuthenticatedSession(string sub)
    {
        var binding = flowContext.Binding
            ?? throw new InvalidOperationException("ATProto OAuth state binding is unavailable.");
        var session = flowContext.SessionResult
            ?? throw new InvalidOperationException("ATProto platform session is unavailable.");
        if (!string.Equals(sub, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || !string.Equals(session.Did, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || session.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(session.AccessToken)
            || session.AccessToken.Length > MaximumPlatformTokenBytes
            || session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            throw new InvalidOperationException("ATProto OAuth session access binding is invalid.");
        }

        return new(binding, session);
    }

    private HttpRequestMessage CreateAuthenticatedSessionRequest(
        HttpMethod method,
        AuthenticatedSessionContext context)
    {
        var request = new HttpRequestMessage(method, AtprotoBootstrapAssertionService.SessionBridgePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.Session.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Tenant-Slug", context.Binding.Seed.TenantSlug);
        request.Headers.TryAddWithoutValidation(
            AtprotoBootstrapAssertionService.SessionBridgeHeaderName,
            assertionService.IssueSessionBridge(
                context.Binding.Seed.TenantId,
                context.Session.UserId,
                context.Session.Did,
                method));
        return request;
    }

    private static void ValidateCarpaSession(
        string sub,
        OAuthSessionData data,
        AtprotoOAuthFlowBinding binding)
    {
        var tokenSet = data.TokenSet;
        if (!string.Equals(sub, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || tokenSet is null
            || !string.Equals(tokenSet.Sub, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || !string.Equals(tokenSet.Issuer, binding.Issuer.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)
            || !Uri.TryCreate(tokenSet.Audience, UriKind.Absolute, out var audience)
            || !UrisEqual(audience, binding.Seed.ExpectedPdsUri)
            || data.DPoPKey is null)
        {
            throw new InvalidOperationException("ATProto OAuth session binding is invalid.");
        }
    }

    private void ValidateBridgeResult(
        BffAtprotoSessionBridgeResponse result,
        AtprotoOAuthFlowBinding binding)
    {
        var now = timeProvider.GetUtcNow();
        if (result.UserId == Guid.Empty
            || result.ActorId == Guid.Empty
            || result.ParticipationId == Guid.Empty
            || !string.Equals(result.Did, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || !string.Equals(result.Classification, binding.Seed.Classification, StringComparison.Ordinal)
            || result.CanonicalActorId != binding.Seed.CanonicalActorId
            || result.ExpectedCanonicalActorConcurrencyStamp != binding.Seed.ExpectedCanonicalActorConcurrencyStamp
            || string.IsNullOrWhiteSpace(result.AccessToken)
            || result.AccessToken.Length > MaximumPlatformTokenBytes
            || result.ExpiresAt <= now
            || result.ExpiresAt > now.AddHours(1))
        {
            throw new InvalidOperationException("ATProto session bridge result is invalid.");
        }
    }

    private static bool UrisEqual(Uri left, Uri right) =>
        string.Equals(left.AbsoluteUri.TrimEnd('/'), right.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal);

    private sealed record BffAtprotoSessionBridgeRequest(
        string ExpectedDid,
        string ExpectedPdsUri,
        string OAuthClientKeyId,
        string Classification,
        JsonElement OAuthSession,
        Guid? CanonicalActorId,
        Guid? ExpectedCanonicalActorConcurrencyStamp);

    private sealed record BffAtprotoSessionBridgeResponse(
        Guid UserId,
        Guid ActorId,
        Guid ParticipationId,
        string Did,
        string Classification,
        string AccessToken,
        DateTimeOffset ExpiresAt,
        Guid? CanonicalActorId,
        Guid? ExpectedCanonicalActorConcurrencyStamp);

    private sealed record BffAtprotoStoredSessionBridgeResponse(
        string Did,
        string ExpectedPdsUri,
        string OAuthClientKeyId,
        JsonElement OAuthSession);

    private sealed record AuthenticatedSessionContext(
        AtprotoOAuthFlowBinding Binding,
        AtprotoBffSessionResult Session);

    private sealed class BoundedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long _read;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReadBoundedAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private async ValueTask<int> ReadBoundedAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return Track(read);
        }

        private int Track(int read)
        {
            _read += read;
            if (_read > maximumBytes)
            {
                throw new InvalidDataException("ATProto session bridge response is too large.");
            }

            return read;
        }
    }
}
