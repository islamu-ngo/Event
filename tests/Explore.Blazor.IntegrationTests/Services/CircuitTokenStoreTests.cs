using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Explore.Blazor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Services;

public class CircuitTokenStoreTests
{
    private readonly CircuitTokenStore _store = new(NullLogger<CircuitTokenStore>.Instance);

    // ──────────────────────────────────────────────
    // Store / Resolve basics
    // ──────────────────────────────────────────────

    [Test]
    public async Task Store_WithValidToken_AcceptsAndResolvesSuccessfully()
    {
        var userId = Guid.NewGuid().ToString();
        var token = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30));

        var storeResult = _store.Store(userId, null, token);
        var resolution = _store.Resolve(userId, null);

        await Assert.That(storeResult.Accepted).IsTrue();
        await Assert.That(resolution.Found).IsTrue();
        await Assert.That(resolution.Token).IsEqualTo(token);
        await Assert.That(resolution.Source).IsEqualTo(CircuitTokenResolutionSource.SessionStore);
    }

    [Test]
    public async Task Store_WithSessionId_ScopesToUserAndSession()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();
        var token = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionId);

        _store.Store(userId, sessionId, token);

        var withSession = _store.Resolve(userId, sessionId);
        var withoutSession = _store.Resolve(userId, null);

        await Assert.That(withSession.Found).IsTrue();
        await Assert.That(withSession.Token).IsEqualTo(token);
        await Assert.That(withoutSession.Found).IsFalse();
    }

    [Test]
    public async Task Store_WithEmptyUserId_RejectsToken()
    {
        var result = _store.Store("", null, "some-token");
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.RejectionCode).IsEqualTo("missing_user_id");
    }

    [Test]
    public async Task Store_WithEmptyToken_RejectsToken()
    {
        var result = _store.Store("user-1", null, "");
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.RejectionCode).IsEqualTo("empty_token");
    }

    // ──────────────────────────────────────────────
    // Expiry
    // ──────────────────────────────────────────────

    [Test]
    public async Task Store_WithExpiredToken_RejectsToken()
    {
        var userId = Guid.NewGuid().ToString();
        var expiredToken = CreateJwt(userId, DateTime.UtcNow.AddMinutes(-5));

        var result = _store.Store(userId, null, expiredToken);
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.RejectionCode).IsEqualTo("token_expired_or_near_expiry");
    }

    [Test]
    public async Task Store_WithNearExpiryToken_RejectsToken()
    {
        var userId = Guid.NewGuid().ToString();
        var nearExpiryToken = CreateJwt(userId, DateTime.UtcNow.AddSeconds(15));

        var result = _store.Store(userId, null, nearExpiryToken);
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.RejectionCode).IsEqualTo("token_expired_or_near_expiry");
    }

    [Test]
    public async Task Resolve_WithExpiredStoredToken_EvictsAndReturnsNotFound()
    {
        var userId = Guid.NewGuid().ToString();
        // Store a token that expires in 31 seconds (barely above the buffer)
        var shortLivedToken = CreateJwt(userId, DateTime.UtcNow.AddSeconds(31));
        _store.Store(userId, null, shortLivedToken);

        // Resolve immediately — should still be valid
        var resolution = _store.Resolve(userId, null);
        await Assert.That(resolution.Found).IsTrue();
    }

    // ──────────────────────────────────────────────
    // Cross-user isolation
    // ──────────────────────────────────────────────

    [Test]
    public async Task Resolve_ForDifferentUser_ReturnsNotFound()
    {
        var userA = Guid.NewGuid().ToString();
        var userB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userA, DateTime.UtcNow.AddMinutes(30));

        _store.Store(userA, null, tokenA);
        var resolution = _store.Resolve(userB, null);

        await Assert.That(resolution.Found).IsFalse();
        await Assert.That(resolution.FailureCode).IsEqualTo("no_entry");
    }

    [Test]
    public async Task MultipleUsers_EachResolveOwnToken()
    {
        var userA = Guid.NewGuid().ToString();
        var userB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userA, DateTime.UtcNow.AddMinutes(30));
        var tokenB = CreateJwt(userB, DateTime.UtcNow.AddMinutes(30));

        _store.Store(userA, null, tokenA);
        _store.Store(userB, null, tokenB);

        var resolvedA = _store.Resolve(userA, null);
        var resolvedB = _store.Resolve(userB, null);

        await Assert.That(resolvedA.Token).IsEqualTo(tokenA);
        await Assert.That(resolvedB.Token).IsEqualTo(tokenB);
    }

    // ──────────────────────────────────────────────
    // Session isolation
    // ──────────────────────────────────────────────

    [Test]
    public async Task DifferentSessions_SameUser_AreScopedIndependently()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionA);
        var tokenB = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionB);

        _store.Store(userId, sessionA, tokenA);
        _store.Store(userId, sessionB, tokenB);

        var resolvedA = _store.Resolve(userId, sessionA);
        var resolvedB = _store.Resolve(userId, sessionB);

        await Assert.That(resolvedA.Token).IsEqualTo(tokenA);
        await Assert.That(resolvedB.Token).IsEqualTo(tokenB);
    }

    // ──────────────────────────────────────────────
    // Deterministic cleanup
    // ──────────────────────────────────────────────

    [Test]
    public async Task ClearSession_RemovesOnlyTargetedSession()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionA);
        var tokenB = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionB);

        _store.Store(userId, sessionA, tokenA);
        _store.Store(userId, sessionB, tokenB);

        _store.ClearSession(userId, sessionA);

        var resolvedA = _store.Resolve(userId, sessionA);
        var resolvedB = _store.Resolve(userId, sessionB);

        await Assert.That(resolvedA.Found).IsFalse();
        await Assert.That(resolvedB.Found).IsTrue();
        await Assert.That(resolvedB.Token).IsEqualTo(tokenB);
    }

    [Test]
    public async Task ClearUser_RemovesAllSessionsForUser()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionA);
        var tokenB = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30), sessionB);

        _store.Store(userId, sessionA, tokenA);
        _store.Store(userId, sessionB, tokenB);

        _store.ClearUser(userId);

        var resolvedA = _store.Resolve(userId, sessionA);
        var resolvedB = _store.Resolve(userId, sessionB);

        await Assert.That(resolvedA.Found).IsFalse();
        await Assert.That(resolvedB.Found).IsFalse();
    }

    [Test]
    public async Task ClearUser_DoesNotAffectOtherUsers()
    {
        var userA = Guid.NewGuid().ToString();
        var userB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userA, DateTime.UtcNow.AddMinutes(30));
        var tokenB = CreateJwt(userB, DateTime.UtcNow.AddMinutes(30));

        _store.Store(userA, null, tokenA);
        _store.Store(userB, null, tokenB);

        _store.ClearUser(userA);

        var resolvedA = _store.Resolve(userA, null);
        var resolvedB = _store.Resolve(userB, null);

        await Assert.That(resolvedA.Found).IsFalse();
        await Assert.That(resolvedB.Found).IsTrue();
    }

    // ──────────────────────────────────────────────
    // Token refresh (overwrite)
    // ──────────────────────────────────────────────

    [Test]
    public async Task Store_OverwritesExistingTokenForSameSession()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();
        var oldToken = CreateJwt(userId, DateTime.UtcNow.AddMinutes(10), sessionId);
        var newToken = CreateJwt(userId, DateTime.UtcNow.AddMinutes(40), sessionId);

        _store.Store(userId, sessionId, oldToken);
        _store.Store(userId, sessionId, newToken);

        var resolution = _store.Resolve(userId, sessionId);
        await Assert.That(resolution.Token).IsEqualTo(newToken);
    }

    // ──────────────────────────────────────────────
    // Capacity management
    // ──────────────────────────────────────────────

    [Test]
    public async Task Store_BeyondCapacity_EvictsOldestEntries()
    {
        // Store MaxEntries + 10 entries to trigger eviction
        for (int i = 0; i < CircuitTokenStore.MaxEntries + 10; i++)
        {
            var userId = $"user-{i}";
            var token = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30));
            _store.Store(userId, null, token);
        }

        // Entry count should be at or below MaxEntries after eviction
        await Assert.That(_store.EntryCount).IsLessThanOrEqualTo(CircuitTokenStore.MaxEntries);
    }

    // ──────────────────────────────────────────────
    // IsTokenUsable static helper
    // ──────────────────────────────────────────────

    [Test]
    public async Task IsTokenUsable_WithValidJwt_ReturnsTrue()
    {
        var token = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(30));
        await Assert.That(CircuitTokenStore.IsTokenUsable(token)).IsTrue();
    }

    [Test]
    public async Task IsTokenUsable_WithExpiredJwt_ReturnsFalse()
    {
        var token = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(-5));
        await Assert.That(CircuitTokenStore.IsTokenUsable(token)).IsFalse();
    }

    [Test]
    public async Task IsTokenUsable_WithNull_ReturnsFalse()
    {
        await Assert.That(CircuitTokenStore.IsTokenUsable(null)).IsFalse();
    }

    [Test]
    public async Task IsTokenUsable_WithOpaqueToken_ReturnsTrue()
    {
        // Opaque tokens (non-JWT) should be accepted
        await Assert.That(CircuitTokenStore.IsTokenUsable("opaque-token-value")).IsTrue();
    }

    [Test]
    public async Task IsTokenForwardable_WithNearExpiryJwt_ReturnsTrue()
    {
        var token = CreateJwt("user-1", DateTime.UtcNow.AddSeconds(10));
        await Assert.That(CircuitTokenStore.IsTokenForwardable(token)).IsTrue();
    }

    [Test]
    public async Task IsTokenForwardable_WithExpiredJwt_ReturnsFalse()
    {
        var token = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(-5));
        await Assert.That(CircuitTokenStore.IsTokenForwardable(token)).IsFalse();
    }

    // ──────────────────────────────────────────────
    // Edge cases
    // ──────────────────────────────────────────────

    [Test]
    public async Task ClearSession_WithNullUserId_DoesNotThrow()
    {
        _store.ClearSession(null!, null);
        await Assert.That(true).IsTrue(); // No exception = pass
    }

    [Test]
    public async Task ClearUser_WithNullUserId_DoesNotThrow()
    {
        _store.ClearUser(null!);
        await Assert.That(true).IsTrue(); // No exception = pass
    }

    [Test]
    public async Task Resolve_WithNullUserId_ReturnsNotFound()
    {
        var resolution = _store.Resolve(null!, null);
        await Assert.That(resolution.Found).IsFalse();
        await Assert.That(resolution.FailureCode).IsEqualTo("missing_user_id");
    }

    [Test]
    public async Task Store_WithOpaqueToken_AcceptsToken()
    {
        // Opaque tokens have no expiry to parse — should be accepted
        var userId = Guid.NewGuid().ToString();
        var result = _store.Store(userId, null, "opaque-access-token");

        await Assert.That(result.Accepted).IsTrue();
        var resolution = _store.Resolve(userId, null);
        await Assert.That(resolution.Token).IsEqualTo("opaque-access-token");
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static string CreateJwt(string sub, DateTime? expires = null, string? sessionId = null)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            claims.Add(new Claim("sid", sessionId));
        }

        var jwt = new JwtSecurityToken(claims: claims, expires: expires ?? DateTime.UtcNow.AddMinutes(30));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
