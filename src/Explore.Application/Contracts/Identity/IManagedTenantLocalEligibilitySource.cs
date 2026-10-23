using Explore.Domain;

namespace Explore.Application.Contracts.Identity;

/// <summary>Reads current nonsecret Local eligibility while retaining the identity lifecycle fence.</summary>
public interface IManagedTenantLocalEligibilitySource
{
    Task<IManagedTenantLocalEligibilityLease> AcquireAsync(Guid exactSubject, CancellationToken cancellationToken);
}

/// <summary>Comparison data held through the original worker settlement; it is not mutation authority.</summary>
public interface IManagedTenantLocalEligibilityLease : IAsyncDisposable
{
    ManagedTenantLocalEligibility Eligibility { get; }
    Task RequireCurrentAsync(CancellationToken cancellationToken);
}

public sealed record ManagedTenantLocalEligibility(
    Guid LocalSubjectId,
    Guid CurrentOperationId,
    Guid PersonalActorId,
    Guid ExternalLoginId,
    LocalIdentityEligibilityState State,
    long Generation);
