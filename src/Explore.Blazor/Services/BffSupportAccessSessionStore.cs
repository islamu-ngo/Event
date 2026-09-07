using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Caching.Distributed;

namespace Explore.Blazor.Services;

public sealed record BffSupportAccessSession(
    Guid SessionId,
    string OwnerUserId,
    string? OwnerSessionId,
    Guid TargetTenantId,
    int ModeId,
    bool AllowsWrites,
    DateTimeOffset ExpiresAtUtc);

public sealed record BffSupportAccessStoreResult(
    bool Success,
    BffSupportAccessSession? Session,
    string? FailureCode)
{
    public static BffSupportAccessStoreResult Failed(string failureCode) =>
        new(false, null, failureCode);

    public static BffSupportAccessStoreResult Stored(BffSupportAccessSession session) =>
        new(true, session, null);
}

public interface IBffSupportAccessSessionStore
{
    Task<BffSupportAccessStoreResult> StoreAsync(
        ClaimsPrincipal user,
        SupportAccessSessionDto session,
        CancellationToken cancellationToken = default);

    Task<BffSupportAccessStoreResult> ResolveCurrentAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task ClearCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed class BffSupportAccessSessionStore(
    IDistributedCache cache,
    IHttpContextAccessor httpContextAccessor,
    ICircuitUserContext circuitUserContext) : IBffSupportAccessSessionStore
{
    private const string CacheKeyPrefix = "bff-support-access:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BffSupportAccessStoreResult> StoreAsync(
        ClaimsPrincipal user,
        SupportAccessSessionDto session,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(user);
        if (owner is null)
        {
            return BffSupportAccessStoreResult.Failed("missing_owner");
        }

        var validationFailure = ValidateSession(session);
        if (validationFailure is not null)
        {
            return BffSupportAccessStoreResult.Failed(validationFailure);
        }

        var trustedSession = new BffSupportAccessSession(
            SessionId: session.Id!.Value,
            OwnerUserId: owner.UserId,
            OwnerSessionId: owner.SessionId,
            TargetTenantId: session.TargetTenantId!.Value,
            ModeId: session.ModeId ?? 0,
            AllowsWrites: session.AllowsWrites == true,
            ExpiresAtUtc: session.ExpiresAtUtc!.Value);

        await cache.SetStringAsync(
            BuildCacheKey(owner),
            JsonSerializer.Serialize(trustedSession, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = trustedSession.ExpiresAtUtc
            },
            cancellationToken);

        return BffSupportAccessStoreResult.Stored(trustedSession);
    }

    public Task<BffSupportAccessStoreResult> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(null);
        return owner is null
            ? Task.FromResult(BffSupportAccessStoreResult.Failed("missing_owner"))
            : ResolveAsync(owner, cancellationToken);
    }

    public Task ClearAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(user);
        return owner is null
            ? Task.CompletedTask
            : cache.RemoveAsync(BuildCacheKey(owner), cancellationToken);
    }

    public Task ClearCurrentAsync(CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(null);
        return owner is null
            ? Task.CompletedTask
            : cache.RemoveAsync(BuildCacheKey(owner), cancellationToken);
    }

    private async Task<BffSupportAccessStoreResult> ResolveAsync(
        SupportAccessOwner owner,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(owner);
        var payload = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return BffSupportAccessStoreResult.Failed("session_not_found");
        }

        BffSupportAccessSession? session;
        try
        {
            session = JsonSerializer.Deserialize<BffSupportAccessSession>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            await cache.RemoveAsync(cacheKey, cancellationToken);
            return BffSupportAccessStoreResult.Failed("session_corrupt");
        }

        if (session is null ||
            session.SessionId == Guid.Empty ||
            session.TargetTenantId == Guid.Empty)
        {
            await cache.RemoveAsync(cacheKey, cancellationToken);
            return BffSupportAccessStoreResult.Failed("session_corrupt");
        }

        if (!string.Equals(session.OwnerUserId, owner.UserId, StringComparison.Ordinal) ||
            !string.Equals(session.OwnerSessionId, owner.SessionId, StringComparison.Ordinal))
        {
            await cache.RemoveAsync(cacheKey, cancellationToken);
            return BffSupportAccessStoreResult.Failed("owner_mismatch");
        }

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await cache.RemoveAsync(cacheKey, cancellationToken);
            return BffSupportAccessStoreResult.Failed("session_expired");
        }

        return BffSupportAccessStoreResult.Stored(session);
    }

    private SupportAccessOwner? ResolveOwner(ClaimsPrincipal? user)
    {
        var principal = user ?? httpContextAccessor.HttpContext?.User;
        var userId = ResolveUserId(principal);
        var sessionId = ResolveSessionId(principal);

        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = circuitUserContext.UserId;
            sessionId = circuitUserContext.SessionId;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return new SupportAccessOwner(
            userId.Trim(),
            string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim());
    }

    private static string? ValidateSession(SupportAccessSessionDto session)
    {
        if (session.Id is not Guid sessionId || sessionId == Guid.Empty)
        {
            return "missing_session_id";
        }

        if (session.TargetTenantId is not Guid targetTenantId || targetTenantId == Guid.Empty)
        {
            return "missing_target_tenant";
        }

        if (session.IsActive != true)
        {
            return "session_not_active";
        }

        return session.ExpiresAtUtc is not DateTimeOffset expiresAtUtc || expiresAtUtc <= DateTimeOffset.UtcNow
            ? "session_expired"
            : null;
    }

    private static string? ResolveUserId(ClaimsPrincipal? user)
    {
        return user.TryGetCircuitSubject(out var subject) ? subject.PartitionKey : null;
    }

    private static string? ResolveSessionId(ClaimsPrincipal? user) =>
        user.TryGetSessionId(out var sessionId) ? sessionId.PartitionKey : null;

    private static string BuildCacheKey(SupportAccessOwner owner)
    {
        var material = $"{owner.UserId}\n{owner.SessionId ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return CacheKeyPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record SupportAccessOwner(string UserId, string? SessionId);
}
