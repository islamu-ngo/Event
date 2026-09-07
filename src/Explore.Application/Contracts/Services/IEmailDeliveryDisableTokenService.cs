// ABOUTME: Defines opaque, short-lived confirmation tokens bound to email-disable preview impacts.
// ABOUTME: Keeps protection details in Infrastructure and current administrator checks in Application handlers.

using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Services;

public interface IEmailDeliveryDisableTokenService
{
    EmailDeliveryDisableToken Issue(Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot);

    bool Matches(string? token, Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot);
}

public sealed record EmailDeliveryDisableToken(string Token, DateTimeOffset ExpiresAtUtc);
