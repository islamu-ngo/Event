// ABOUTME: Defines bound anonymous proof issuance and validated internal allocation/recovery authority.
// ABOUTME: Separates original fresh-allocation expiry from exact committed recovery and durable intake quotas.

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Domain.ValueObjects;

// Only the native Infrastructure implementation may construct cryptographically validated authority.
// API consumers cannot derive a forgeable authority or attach arbitrary client-selected order IDs.
[assembly: InternalsVisibleTo("Explore.Infrastructure")]

namespace Explore.Application.Contracts.Services.Registration;

public interface IAnonymousRegistrationChallengeService
{
    AnonymousRegistrationChallengeDto Issue(AnonymousRegistrationChallengeBinding binding, int difficulty = 18);

    /// <summary>
    /// Authenticates binding and proof before returning any capability. Expired results permit only exact
    /// committed recovery. New allocation must recheck IsFresh inside its transaction after all waits.
    /// </summary>
    AnonymousRegistrationChallengeAuthority? Validate(
        AnonymousRegistrationChallengeBinding binding, string? protectedChallenge, string? nonce);
}

public sealed record AnonymousRegistrationChallengeBinding
{
    public AnonymousRegistrationChallengeBinding(Guid tenantId, Guid eventId, string canonicalRequestDigest, string idempotencyKey)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty
            || canonicalRequestDigest is not { Length: 64 }
            || canonicalRequestDigest.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
            || idempotencyKey is not { Length: >= 1 and <= 128 }
            || idempotencyKey.Any(character => character is < '!' or > '~'))
        {
            throw new ArgumentException("The anonymous registration binding is invalid.");
        }

        TenantId = tenantId;
        EventId = eventId;
        CanonicalRequestDigest = canonicalRequestDigest;
        IdempotencyKey = idempotencyKey;
    }

    public Guid TenantId { get; }
    public Guid EventId { get; }
    public string CanonicalRequestDigest { get; }
    public string IdempotencyKey { get; }
    public override string ToString() => "AnonymousRegistrationChallengeBinding { Redacted = true }";
}

/// <summary>
/// Not a client DTO: only a native validated implementation creates this authority, never a body/order ID.
/// JSON deliberately carries no authority or secret. Retain the protected envelope for reconstruction.
/// </summary>
public abstract class AnonymousRegistrationChallengeAuthority
{
    internal AnonymousRegistrationChallengeAuthority(Guid tenantId, Guid eventId, Guid orderId,
        DateTimeOffset expiresAt, string guestCapabilityToken, CapabilityTokenHash guestAccessTokenHash)
    {
        TenantId = tenantId;
        EventId = eventId;
        OrderId = orderId;
        ExpiresAt = expiresAt;
        GuestCapabilityToken = guestCapabilityToken;
        GuestAccessTokenHash = guestAccessTokenHash;
    }

    [JsonIgnore] public Guid TenantId { get; }
    [JsonIgnore] public Guid EventId { get; }
    [JsonIgnore] public Guid OrderId { get; }
    [JsonIgnore] public DateTimeOffset ExpiresAt { get; }
    [JsonIgnore] public DateTimeOffset RecoverUntil => ExpiresAt.AddHours(24);
    [JsonIgnore] public string GuestCapabilityToken { get; }
    [JsonIgnore] public CapabilityTokenHash GuestAccessTokenHash { get; }

    public bool IsFresh(DateTimeOffset now) => now >= ExpiresAt.AddSeconds(-120) && now < ExpiresAt;

    // Cryptographic validation alone cannot bind a mutable internal command. Only the trusted
    // Application consume boundary wraps this authority with a snapshot of the digested request.
    public virtual bool Matches(StartGuestRegistrationOrderCommand request) => false;
    public virtual bool Matches(CreateRegistrationOrderWithHoldCommand request) => false;
    public sealed override string ToString() => "AnonymousRegistrationChallengeAuthority { Redacted = true }";
}

/// <summary>
/// Replica-authoritative tenant/event issuance budget. Never persist raw IP/subnet identifiers here.
/// API perimeter limiters are independent; a process-local implementation cannot satisfy this port.
/// </summary>
public interface IAnonymousRegistrationChallengeQuota
{
    Task<bool> TryAcquireAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
}
