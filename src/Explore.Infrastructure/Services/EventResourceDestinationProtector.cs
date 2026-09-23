using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services;

/// <summary>Uses the host's configured Data Protection keyring; never stores or logs destinations.</summary>
public sealed class EventResourceDestinationProtector(IDataProtectionProvider provider)
    : IEventResourceDestinationProtector
{
    public int CurrentVersion => 1;

    private IDataProtector For(Guid tenantId, Guid resourceId, int protectionVersion)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (resourceId == Guid.Empty) throw new ArgumentException("A resource is required.", nameof(resourceId));
        if (protectionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(protectionVersion));

        return provider.CreateProtector("EventResource", "ExternalDestination", $"v{protectionVersion}",
            tenantId.ToString("D"), resourceId.ToString("D"));
    }

    public string Protect(string destination, Guid tenantId, Guid resourceId, int protectionVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return For(tenantId, resourceId, protectionVersion).Protect(destination);
    }

    public string Unprotect(string protectedDestination, Guid tenantId, Guid resourceId, int protectionVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedDestination);
        return For(tenantId, resourceId, protectionVersion).Unprotect(protectedDestination);
    }
}
