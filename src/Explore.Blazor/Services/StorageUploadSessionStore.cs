using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Caching.Distributed;

namespace Explore.Blazor.Services;

public sealed record StorageUploadSession(
    string SessionId,
    string OwnerUserId,
    Guid ApiUploadSessionId,
    string ContentType,
    long ExpectedSizeBytes,
    DateTimeOffset ExpiresAtUtc);

public sealed record StorageUploadSessionIssueResult(
    bool Success,
    string? SessionId,
    string? ObjectKey,
    string? ViewUrl,
    int ExpiresInMinutes,
    string? FailureCode)
{
    public static StorageUploadSessionIssueResult Failed(string failureCode) =>
        new(false, null, null, null, 0, failureCode);

    public static StorageUploadSessionIssueResult Issued(
        StorageUploadSession session,
        int expiresInMinutes) =>
        new(true, session.SessionId, null, null, expiresInMinutes, null);
}

public sealed record StorageUploadSessionResolveResult(
    bool Success,
    StorageUploadSession? Session,
    string? FailureCode)
{
    public static StorageUploadSessionResolveResult Failed(string failureCode) =>
        new(false, null, failureCode);

    public static StorageUploadSessionResolveResult Resolved(StorageUploadSession session) =>
        new(true, session, null);
}

public interface IStorageUploadSessionStore
{
    Task<StorageUploadSessionIssueResult> IssueAsync(
        ClaimsPrincipal user,
        StorageUploadSessionDto response,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<StorageUploadSessionResolveResult> ResolveAsync(
        ClaimsPrincipal user,
        string sessionId,
        string contentType,
        CancellationToken cancellationToken = default);

    Task ConsumeAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed class StorageUploadSessionStore(IDistributedCache cache) : IStorageUploadSessionStore
{
    private const string CacheKeyPrefix = "storage-upload-session:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StorageUploadSessionIssueResult> IssueAsync(
        ClaimsPrincipal user,
        StorageUploadSessionDto response,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = GetRequiredUserId(user);
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return StorageUploadSessionIssueResult.Failed("missing_user");
        }

        if (response.Id is not Guid apiUploadSessionId || apiUploadSessionId == Guid.Empty)
        {
            return StorageUploadSessionIssueResult.Failed("missing_upload_session_id");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return StorageUploadSessionIssueResult.Failed("missing_content_type");
        }

        if (response.ExpectedSizeBytes is not long expectedSizeBytes || expectedSizeBytes <= 0)
        {
            return StorageUploadSessionIssueResult.Failed("missing_expected_size");
        }

        if (response.ExpiresAt is not DateTimeOffset expiresAt)
        {
            return StorageUploadSessionIssueResult.Failed("missing_expiration");
        }

        var expiresAtUtc = expiresAt.ToUniversalTime();
        var nowUtc = DateTimeOffset.UtcNow;
        if (expiresAtUtc <= nowUtc)
        {
            return StorageUploadSessionIssueResult.Failed("upload_session_expired");
        }

        var expiresInMinutes = Math.Min((int)Math.Ceiling((expiresAtUtc - nowUtc).TotalMinutes), 60);
        var session = new StorageUploadSession(
            SessionId: RandomNumberGenerator.GetHexString(32).ToLowerInvariant(),
            OwnerUserId: ownerUserId,
            ApiUploadSessionId: apiUploadSessionId,
            ContentType: contentType.Trim(),
            ExpectedSizeBytes: expectedSizeBytes,
            ExpiresAtUtc: expiresAtUtc);

        var payload = JsonSerializer.Serialize(session, JsonOptions);
        await cache.SetStringAsync(
            BuildCacheKey(session.SessionId),
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.ExpiresAtUtc
            },
            cancellationToken);

        return StorageUploadSessionIssueResult.Issued(session, expiresInMinutes);
    }

    public async Task<StorageUploadSessionResolveResult> ResolveAsync(
        ClaimsPrincipal user,
        string sessionId,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = GetRequiredUserId(user);
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return StorageUploadSessionResolveResult.Failed("missing_user");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return StorageUploadSessionResolveResult.Failed("missing_session");
        }

        var payload = await cache.GetStringAsync(BuildCacheKey(sessionId.Trim()), cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return StorageUploadSessionResolveResult.Failed("session_not_found");
        }

        StorageUploadSession? session;
        try
        {
            session = JsonSerializer.Deserialize<StorageUploadSession>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return StorageUploadSessionResolveResult.Failed("session_corrupt");
        }

        if (session is null)
        {
            return StorageUploadSessionResolveResult.Failed("session_corrupt");
        }

        if (!string.Equals(session.OwnerUserId, ownerUserId, StringComparison.Ordinal))
        {
            return StorageUploadSessionResolveResult.Failed("session_owner_mismatch");
        }

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await ConsumeAsync(session.SessionId, cancellationToken);
            return StorageUploadSessionResolveResult.Failed("session_expired");
        }

        if (!string.Equals(session.ContentType, contentType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return StorageUploadSessionResolveResult.Failed("content_type_mismatch");
        }

        return StorageUploadSessionResolveResult.Resolved(session);
    }

    public Task ConsumeAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.CompletedTask;
        }

        return cache.RemoveAsync(BuildCacheKey(sessionId.Trim()), cancellationToken);
    }

    private static string? GetRequiredUserId(ClaimsPrincipal user) =>
        user.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;

    private static string BuildCacheKey(string sessionId) => CacheKeyPrefix + sessionId;
}
