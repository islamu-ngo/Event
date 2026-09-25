namespace Explore.Application.Contracts.Services;

/// <summary>Protects a complete external destination for one tenant resource and protection version.</summary>
public interface IEventResourceDestinationProtector
{
    int CurrentVersion { get; }
    string Protect(string destination, Guid tenantId, Guid resourceId, int protectionVersion);
    string Unprotect(string protectedDestination, Guid tenantId, Guid resourceId, int protectionVersion);
}
