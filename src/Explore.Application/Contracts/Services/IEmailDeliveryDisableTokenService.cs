
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Services;

public interface IEmailDeliveryDisableTokenService
{
    EmailDeliveryDisableToken Issue(Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot);

    bool Matches(string? token, Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot);
}

public sealed record EmailDeliveryDisableToken(string Token, DateTimeOffset ExpiresAtUtc);
