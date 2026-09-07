using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;

namespace Explore.Blazor.Services;

/// <summary>
/// Represents the result of attempting to resolve a token from the circuit token store.
/// </summary>
public readonly record struct CircuitTokenResolution(
    bool Found,
    string? Token,
    CircuitTokenResolutionSource Source,
    string? FailureCode)
{
    public static CircuitTokenResolution Success(string token, CircuitTokenResolutionSource source) =>
        new(true, token, source, null);

    public static CircuitTokenResolution NotFound(string failureCode) =>
        new(false, null, CircuitTokenResolutionSource.None, failureCode);
}

/// <summary>
/// Identifies where a token was resolved from.
/// </summary>
public enum CircuitTokenResolutionSource
{
    None,
    CircuitLocal,
    SessionStore
}

/// <summary>
/// Abstraction over the bounded circuit token store.
/// </summary>
public interface ICircuitTokenStore
{
    /// <summary>
    /// Stores a token for the specified user and authentication session.
    /// Expired or near-expiry tokens are rejected.
    /// </summary>
    CircuitTokenStoreResult Store(string userId, string? sessionId, string token);

    /// <summary>
    /// Resolves a usable token for the specified user and authentication session.
    /// Returns <see cref="CircuitTokenResolution.NotFound"/> when the token is missing, expired, or belongs to another user.
    /// </summary>
    CircuitTokenResolution Resolve(string userId, string? sessionId);

    /// <summary>
    /// Resolves the most recently stored usable token for a user across all their sessions.
    /// Used as a fallback when session-keyed resolution fails due to session ID mismatches
    /// between the store path (BFF refresh) and the resolve path (AccessTokenForwardingHandler).
    /// </summary>
    CircuitTokenResolution ResolveByUserId(string userId);

    /// <summary>
    /// Clears the token entry for the specified user and authentication session.
    /// </summary>
    void ClearSession(string userId, string? sessionId);

    /// <summary>
    /// Clears all token entries for the specified user across all sessions.
    /// </summary>
    void ClearUser(string userId);

    /// <summary>
    /// Returns the current number of entries in the store (for diagnostics/testing only).
    /// </summary>
    int EntryCount { get; }
}

/// <summary>
/// Store result indicating whether a token was accepted.
/// </summary>
public readonly record struct CircuitTokenStoreResult(bool Accepted, string? RejectionCode);

/// <summary>
/// Bounded, session-scoped circuit token store.
///
/// Design decisions (Phase 1F):
/// - Scope: per-authentication-session (userId + sid). Each entry represents one OIDC session.
/// - Logout: <see cref="ClearSession"/> and <see cref="ClearUser"/> deterministically remove entries.
/// - Refresh: <see cref="Store"/> overwrites the existing entry for the same session.
/// - Expiry: tokens within 30 seconds of expiry are rejected on store and on resolve.
/// - Multiple tabs: multiple circuits sharing the same OIDC session share one token entry.
/// - Cross-user isolation: entries are keyed by userId + sessionId; wrong user = no match.
/// - Capacity: bounded to <see cref="MaxEntries"/>. Excess triggers oldest-entry eviction.
/// - Multi-instance: the store is process-local. Sticky sessions or external session state
///   is required for multi-instance deployments. This is documented, not solved here.
/// - Data Protection: tokens are stored as plaintext in-memory (never persisted to disk);
///   the BFF session cookie is the persistence boundary, not this cache.
/// </summary>
public sealed class CircuitTokenStore : ICircuitTokenStore
{
    /// <summary>
    /// Maximum number of entries before eviction. Sized for a single-instance deployment
    /// with generous headroom (one entry per active OIDC session).
    /// </summary>
    public const int MaxEntries = 2048;

    private static readonly TimeSpan UsabilityBuffer = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, TokenStoreEntry> _entries = new();
    private readonly ILogger<CircuitTokenStore> _logger;

    public CircuitTokenStore(ILogger<CircuitTokenStore> logger)
    {
        _logger = logger;
    }

    public int EntryCount => _entries.Count;

    public CircuitTokenStoreResult Store(string userId, string? sessionId, string token)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new CircuitTokenStoreResult(false, "missing_user_id");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new CircuitTokenStoreResult(false, "empty_token");
        }

        var expiry = GetTokenExpiryUtc(token);
        if (expiry.HasValue && expiry.Value <= DateTime.UtcNow.Add(UsabilityBuffer))
        {
            _logger.LogDebug(
                "[CircuitTokenStore] Store completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SessionPresent={SessionPresent}",
                "rejected", "token_expired_or_near_expiry", "circuit", !string.IsNullOrWhiteSpace(sessionId));
            return new CircuitTokenStoreResult(false, "token_expired_or_near_expiry");
        }

        var key = BuildKey(userId, sessionId);
        var entry = new TokenStoreEntry(userId, sessionId, token, DateTime.UtcNow, expiry);
        _entries[key] = entry;

        _logger.LogDebug(
            "[CircuitTokenStore] Store completed | Outcome={Outcome} Purpose={Purpose} SessionPresent={SessionPresent} EntryCount={EntryCount}",
            "accepted", "circuit", !string.IsNullOrWhiteSpace(sessionId), _entries.Count);

        EvictIfOverCapacity();
        EvictExpired();

        return new CircuitTokenStoreResult(true, null);
    }

    public CircuitTokenResolution Resolve(string userId, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return CircuitTokenResolution.NotFound("missing_user_id");
        }

        var key = BuildKey(userId, sessionId);
        if (!_entries.TryGetValue(key, out var entry))
        {
            _logger.LogDebug(
                "[CircuitTokenStore] Resolve completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SessionPresent={SessionPresent}",
                "not_found", "no_entry", "circuit", !string.IsNullOrWhiteSpace(sessionId));
            return CircuitTokenResolution.NotFound("no_entry");
        }

        // Cross-user isolation check (defense-in-depth — key construction should prevent this)
        if (!string.Equals(entry.UserId, userId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "[CircuitTokenStore] Resolve completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                "rejected", "cross_user_mismatch", "circuit");
            return CircuitTokenResolution.NotFound("cross_user_mismatch");
        }

        if (!IsUsableToken(entry.Token, entry.ExpiresAtUtc))
        {
            _entries.TryRemove(key, out _);
            _logger.LogDebug(
                "[CircuitTokenStore] Resolve completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SessionPresent={SessionPresent}",
                "rejected", "token_expired", "circuit", !string.IsNullOrWhiteSpace(sessionId));
            return CircuitTokenResolution.NotFound("token_expired");
        }

        return CircuitTokenResolution.Success(entry.Token, CircuitTokenResolutionSource.SessionStore);
    }

    public CircuitTokenResolution ResolveByUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return CircuitTokenResolution.NotFound("missing_user_id");
        }

        var candidates = _entries
            .Where(kvp => string.Equals(kvp.Value.UserId, userId, StringComparison.Ordinal))
            .OrderByDescending(kvp => kvp.Value.StoredAtUtc)
            .ToList();

        foreach (var kvp in candidates)
        {
            if (IsUsableToken(kvp.Value.Token, kvp.Value.ExpiresAtUtc))
            {
                return CircuitTokenResolution.Success(kvp.Value.Token, CircuitTokenResolutionSource.SessionStore);
            }

            _entries.TryRemove(kvp.Key, out _);
            _logger.LogDebug(
                "[CircuitTokenStore] User resolve evicted an entry | Outcome={Outcome} Reason={Reason} Purpose={Purpose} SessionPresent={SessionPresent}",
                "rejected", "token_expired", "circuit", !string.IsNullOrWhiteSpace(kvp.Value.SessionId));
        }

        _logger.LogDebug(
            "[CircuitTokenStore] User resolve completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} CandidateCount={CandidateCount}",
            "not_found", "no_usable_token_for_subject", "circuit", candidates.Count);

        return CircuitTokenResolution.NotFound("no_usable_token_for_user");
    }

    public void ClearSession(string userId, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var key = BuildKey(userId, sessionId);
        if (_entries.TryRemove(key, out _))
        {
            _logger.LogDebug(
                "[CircuitTokenStore] Cleanup completed | Outcome={Outcome} Purpose={Purpose} SessionPresent={SessionPresent}",
                "cleared", "circuit", !string.IsNullOrWhiteSpace(sessionId));
        }
    }

    public void ClearUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var keysToRemove = _entries
            .Where(kvp => string.Equals(kvp.Value.UserId, userId, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _entries.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug(
                "[CircuitTokenStore] Subject cleanup completed | Outcome={Outcome} Purpose={Purpose} EntryCount={EntryCount}",
                "cleared", "circuit", keysToRemove.Count);
        }
    }

    private void EvictIfOverCapacity()
    {
        if (_entries.Count <= MaxEntries)
        {
            return;
        }

        // Evict oldest entries first
        var overCount = _entries.Count - MaxEntries;
        var candidates = _entries
            .OrderBy(kvp => kvp.Value.StoredAtUtc)
            .Take(overCount + 64) // evict a small batch to avoid repeated evictions
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in candidates)
        {
            _entries.TryRemove(key, out _);
        }

        _logger.LogInformation(
            "[CircuitTokenStore] Evicted {Count} entries due to capacity limit ({Max})",
            candidates.Count, MaxEntries);
    }

    private void EvictExpired()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _entries
            .Where(kvp => !IsUsableToken(kvp.Value.Token, kvp.Value.ExpiresAtUtc))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private static bool IsUsableToken(string? token, DateTime? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (expiresAtUtc.HasValue)
        {
            return expiresAtUtc.Value > DateTime.UtcNow.Add(UsabilityBuffer);
        }

        // No expiry information — conservatively treat as usable
        return true;
    }

    private static string BuildKey(string userId, string? sessionId) =>
        string.Concat(userId, "\u001f", sessionId ?? string.Empty);

    private static DateTime? GetTokenExpiryUtc(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                return null;
            }

            var jwt = handler.ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue ? null : jwt.ValidTo;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsTokenUsable(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                return true; // Opaque tokens are accepted as-is
            }

            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo == DateTime.MinValue)
            {
                return true;
            }

            return jwt.ValidTo > DateTime.UtcNow.Add(UsabilityBuffer);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsTokenForwardable(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                return true; // Opaque tokens are accepted as-is
            }

            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo == DateTime.MinValue)
            {
                return true;
            }

            return jwt.ValidTo > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    private sealed record TokenStoreEntry(
        string UserId,
        string? SessionId,
        string Token,
        DateTime StoredAtUtc,
        DateTime? ExpiresAtUtc);
}
