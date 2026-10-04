using Explore.Application.Authentication;
using Explore.Domain;
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
    IdentityCorrelationDecision Decision, User? User, bool CanClaimVerifiedEmail);
