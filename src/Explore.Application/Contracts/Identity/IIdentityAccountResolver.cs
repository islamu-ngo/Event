using Explore.Application.Authentication;
using Explore.Domain.Services.Identity;

namespace Explore.Application.Contracts.Identity;

public interface IIdentityAccountResolver
{
    Task<IdentityAccountResolution> ResolveAsync(
        ProviderAccountKey accountKey,
        IdentityAuthorityEvidence? evidence,
        CancellationToken cancellationToken);
}

public sealed record IdentityAccountResolution(
    IdentityCorrelationDecision Decision, Guid? UserId, bool CanClaimVerifiedEmail);
