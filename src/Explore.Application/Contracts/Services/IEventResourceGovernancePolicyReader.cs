using Explore.Domain.ValueObjects;

namespace Explore.Application.Contracts.Services;

/// <summary>Reads current instance ceilings and tenant restrictions inside the caller's authority transaction.</summary>
public interface IEventResourceGovernancePolicyReader
{
    Task<EventResourceGovernancePolicy?> ReadAsync(Guid tenantId, CancellationToken cancellationToken);
}
