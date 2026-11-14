using Explore.Application.Contracts.Services;

namespace Explore.Application.Exceptions;

/// <summary>Native visitor admission denial containing no protected tenant or resource information.</summary>
public sealed class TenantVisitorReadAccessException(VisitorTenantReadAccessDecision decision)
    : Exception("Tenant read admission was denied.")
{
    public VisitorTenantReadAccessDecision Decision { get; } = decision;
}
