using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarpaNet;
using CarpaNet.OAuth;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoPdsDeliveryGateway(
    IUserAuthenticationTokenRepository tokenRepository,
    AtprotoSessionEnvelopeProtector protector,
    AtprotoCoreClientFactory coreClientFactory,
    IAtprotoSessionRefreshLock refreshLock) : IAtprotoPdsDeliveryGateway
{
    public async Task<AtprotoPdsDeliveryResult> DeliverAsync(
        AtprotoPdsDeliveryRequest command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!AtprotoDid.TryParse(command.Did, out AtprotoDid did))
        {
            return AtprotoPdsDeliveryResult.Failed("session_unavailable", retryable: false);
        }

        var delivery = AtprotoPdsDeliveryCommand.From(command, did);

        try
        {
            await using IAsyncDisposable refreshLease = await refreshLock.AcquireAsync(
                delivery.TenantId,
                delivery.UserId,
                RepositoryBackedAtprotoSession.Provider,
                delivery.Did.Value,
                cancellationToken).ConfigureAwait(false);
            var session = await tokenRepository.GetAtprotoSessionForReadAsync(
                delivery.TenantId,
                delivery.UserId,
                RepositoryBackedAtprotoSession.Provider,
                delivery.Did.Value,
                cancellationToken).ConfigureAwait(false);
            if (session is null
                || string.IsNullOrWhiteSpace(session.PdsHost)
                || string.IsNullOrWhiteSpace(session.OAuthClientKeyId)
                || !Uri.TryCreate(session.PdsHost, UriKind.Absolute, out var storedPds)
                || !SamePds(storedPds, delivery.PdsHost))
            {
                return AtprotoPdsDeliveryResult.Failed("session_unavailable", retryable: false);
            }

            var context = new AtprotoOAuthSessionStoreContext(
                delivery.TenantId,
                delivery.UserId,
                did,
                delivery.PdsHost,
                session.OAuthClientKeyId);
            var store = new RepositoryBackedOAuthSessionStore(tokenRepository, protector, context);
            using var lease = await coreClientFactory.CreateAsync(
                delivery.Did.Value,
                session.OAuthClientKeyId,
                store,
                cancellationToken).ConfigureAwait(false);

            if (!AtprotoDid.TryParse(lease.Client.AuthenticatedDid, out AtprotoDid providerDid)
                || providerDid != delivery.Did
                || !SamePds(lease.Client.BaseUrl, delivery.PdsHost))
            {
                return AtprotoPdsDeliveryResult.Failed("session_binding_mismatch", retryable: false);
            }

            return await AtprotoPdsRepositoryWriter.DeliverAsync(
                lease.Client,
                delivery,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RateLimitException)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_rate_limited", retryable: true);
        }
        catch (TokenRefreshException)
        {
            return AtprotoPdsDeliveryResult.Failed("session_unavailable", retryable: false);
        }
        catch (AuthenticationException)
        {
            return AtprotoPdsDeliveryResult.Failed("reauth_required", retryable: false);
        }
        catch (ValidationException)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_rejected", retryable: false);
        }
        catch (ATProtoException exception) when ((int)exception.StatusCode >= 500)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_unavailable", retryable: true);
        }
        catch (ATProtoException)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_rejected", retryable: false);
        }
        catch (HttpRequestException)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_unavailable", retryable: true);
        }
        catch (OperationCanceledException)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_timeout", retryable: true);
        }
        catch (TimeoutException)
        {
            return AtprotoPdsDeliveryResult.Failed("provider_timeout", retryable: true);
        }
        catch (AtprotoOAuthSessionUnavailableException)
        {
            return AtprotoPdsDeliveryResult.Failed("session_unavailable", retryable: false);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or CryptographicException
            or JsonException
            or NotSupportedException)
        {
            return AtprotoPdsDeliveryResult.Failed("session_unavailable", retryable: false);
        }
    }

    private static bool SamePds(Uri left, Uri right)
    {
        var normalizedLeft = AtprotoOAuthSessionStoreContext.NormalizePdsUri(left.AbsoluteUri);
        var normalizedRight = AtprotoOAuthSessionStoreContext.NormalizePdsUri(right.AbsoluteUri);
        return normalizedLeft is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }
}

internal sealed record AtprotoPdsDeliveryCommand(
    Guid TenantId,
    Guid UserId,
    AtprotoDid Did,
    Uri PdsHost,
    string Collection,
    string RecordKey,
    PdsSyncOperation Operation,
    string? Payload,
    string? ExpectedCid,
    IReadOnlyList<string>? CompensationBasePayloads,
    IReadOnlyList<string>? CompensationBaseCids,
    bool CompensationEvidenceComplete)
{
    internal static AtprotoPdsDeliveryCommand From(
        AtprotoPdsDeliveryRequest request,
        AtprotoDid did) => new(
            request.TenantId,
            request.UserId,
            did,
            request.PdsHost,
            request.Collection,
            request.RecordKey,
            request.Operation,
            request.Payload,
            request.ExpectedCid,
            request.CompensationBasePayloads,
            request.CompensationBaseCids,
            request.CompensationEvidenceComplete);
}

internal static class AtprotoPdsRepositoryWriter
{
    internal const string GetRecordNsid = "com.atproto.repo.getRecord";
    internal const string PutRecordNsid = "com.atproto.repo.putRecord";
    internal const string DeleteRecordNsid = "com.atproto.repo.deleteRecord";

    internal static async Task<AtprotoPdsDeliveryResult> DeliverAsync(
        IATProtoClient client,
        AtprotoPdsDeliveryCommand delivery,
        CancellationToken cancellationToken)
    {
        bool isCidlessCompensation = delivery.ExpectedCid is null
            && delivery.Operation is PdsSyncOperation.Update or PdsSyncOperation.Delete;
        if (isCidlessCompensation
            && (!delivery.CompensationEvidenceComplete
                || delivery.CompensationBasePayloads is not { Count: > 0 }
                    && delivery.CompensationBaseCids is not { Count: > 0 }))
        {
            return AtprotoPdsDeliveryResult.Failed("record_conflict", retryable: false);
        }

        var existing = await GetExistingAsync(client, delivery, cancellationToken).ConfigureAwait(false);
        if (delivery.Operation == PdsSyncOperation.Delete)
        {
            return await DeleteAsync(client, delivery, existing, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(delivery.Payload))
        {
            return AtprotoPdsDeliveryResult.Failed("payload_invalid", retryable: false);
        }

        JsonElement desired;
        try
        {
            desired = JsonSerializer.Deserialize<JsonElement>(delivery.Payload);
        }
        catch (JsonException)
        {
            return AtprotoPdsDeliveryResult.Failed("payload_invalid", retryable: false);
        }

        if (existing is not null && JsonElement.DeepEquals(existing.Value, desired))
        {
            return ValidResponse(existing.Uri, existing.Cid, existing.Cid);
        }

        if (delivery.Operation == PdsSyncOperation.Create && existing is not null)
        {
            return AtprotoPdsDeliveryResult.Failed("record_conflict", retryable: false);
        }

        if (delivery.Operation == PdsSyncOperation.Update
            && delivery.ExpectedCid is not null
            && existing is not null
            && !string.Equals(existing.Cid, delivery.ExpectedCid, StringComparison.Ordinal))
        {
            return AtprotoPdsDeliveryResult.Failed("record_conflict", retryable: false);
        }

        if (delivery.Operation == PdsSyncOperation.Update
            && existing is null
            && delivery.ExpectedCid is not null)
        {
            return AtprotoPdsDeliveryResult.Failed("remote_record_missing", retryable: false);
        }

        if (delivery.Operation == PdsSyncOperation.Update
            && delivery.ExpectedCid is null
            && existing is not null
            && !MatchesCompensationBase(existing, delivery))
        {
            return AtprotoPdsDeliveryResult.Failed("record_conflict", retryable: false);
        }

        var response = await client.PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            PutRecordNsid,
            new AtprotoPutRecordInput
            {
                Repo = delivery.Did.Value,
                Collection = delivery.Collection,
                RecordKey = delivery.RecordKey,
                Validate = true,
                Record = desired,
                SwapRecord = delivery.Operation == PdsSyncOperation.Update
                    ? delivery.ExpectedCid ?? existing?.Cid
                    : null
            },
            cancellationToken).ConfigureAwait(false);
        return ValidResponse(response.Uri, response.Cid, existing?.Cid);
    }

    private static async Task<AtprotoGetRecordResponse?> GetExistingAsync(
        IATProtoClient client,
        AtprotoPdsDeliveryCommand delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAsync<AtprotoGetRecordResponse>(
                GetRecordNsid,
                [
                    new("repo", delivery.Did.Value),
                    new("collection", delivery.Collection),
                    new("rkey", delivery.RecordKey)
                ],
                cancellationToken).ConfigureAwait(false);
        }
        catch (ATProtoException exception) when (
            exception.StatusCode == HttpStatusCode.NotFound
            || string.Equals(exception.ErrorCode, "RecordNotFound", StringComparison.Ordinal))
        {
            return null;
        }
    }

    private static async Task<AtprotoPdsDeliveryResult> DeleteAsync(
        IATProtoClient client,
        AtprotoPdsDeliveryCommand delivery,
        AtprotoGetRecordResponse? existing,
        CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            return delivery.ExpectedCid is { Length: > 0 } expectedCid
                ? AtprotoPdsDeliveryResult.Success(BuildAtUri(delivery), expectedCid)
                : AtprotoPdsDeliveryResult.SuccessAbsent(BuildAtUri(delivery));
        }

        if (delivery.ExpectedCid is not null
            && !string.Equals(existing.Cid, delivery.ExpectedCid, StringComparison.Ordinal))
        {
            return AtprotoPdsDeliveryResult.Failed("record_conflict", retryable: false);
        }

        if (delivery.ExpectedCid is null
            && !MatchesCompensationBase(existing, delivery))
        {
            return AtprotoPdsDeliveryResult.Failed("record_conflict", retryable: false);
        }

        var swapRecord = delivery.ExpectedCid ?? existing.Cid;
        await client.PostAsync<AtprotoDeleteRecordInput, AtprotoDeleteRecordResponse>(
            DeleteRecordNsid,
            new AtprotoDeleteRecordInput
            {
                Repo = delivery.Did.Value,
                Collection = delivery.Collection,
                RecordKey = delivery.RecordKey,
                SwapRecord = swapRecord
            },
            cancellationToken).ConfigureAwait(false);
        return ValidResponse(existing.Uri, existing.Cid, existing.Cid);
    }

    private static AtprotoPdsDeliveryResult ValidResponse(
        string? uri,
        string? cid,
        string? observedBaseCid = null) =>
        string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(cid)
            ? AtprotoPdsDeliveryResult.Failed("provider_response_invalid", retryable: true)
            : AtprotoPdsDeliveryResult.Success(uri, cid, observedBaseCid);

    private static string BuildAtUri(AtprotoPdsDeliveryCommand delivery) =>
        $"at://{delivery.Did.Value}/{delivery.Collection}/{delivery.RecordKey}";

    private static bool MatchesCompensationBase(
        AtprotoGetRecordResponse existing,
        AtprotoPdsDeliveryCommand delivery)
    {
        if (delivery.CompensationBaseCids?.Contains(existing.Cid, StringComparer.Ordinal) == true)
        {
            return true;
        }

        if (delivery.CompensationBasePayloads is null)
        {
            return false;
        }

        foreach (string payload in delivery.CompensationBasePayloads)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                if (JsonElement.DeepEquals(existing.Value, document.RootElement))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // Stored invalid evidence is ignored so compensation fails closed.
            }
        }

        return false;
    }

}

internal sealed record AtprotoGetRecordResponse(
    string Uri,
    string Cid,
    JsonElement Value);

internal sealed class AtprotoPutRecordInput
{
    public required string Repo { get; init; }
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public required string RecordKey { get; init; }

    public bool Validate { get; init; }
    public required JsonElement Record { get; init; }
    public string? SwapRecord { get; init; }
}

internal sealed record AtprotoPutRecordResponse(string Uri, string Cid);

internal sealed class AtprotoDeleteRecordInput
{
    public required string Repo { get; init; }
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public required string RecordKey { get; init; }

    public string? SwapRecord { get; init; }
}

internal sealed class AtprotoDeleteRecordResponse;
