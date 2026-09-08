// ABOUTME: Scoped in-memory holder for one guest registration order's opaque capability.
// ABOUTME: Deliberately avoids URLs, request bodies, logs, and browser persistence for bearer capability values.

namespace Explore.Blazor.Client.Contracts.Services;

public interface IGuestRegistrationOrderCapabilityStore
{
    void Store(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability);
    bool TryGet(Guid eventId, Guid orderId, out GuestRegistrationOrderCapability? capability);
    void Remove(Guid eventId, Guid orderId);
    void RestoreBookmark(Guid eventId, Guid orderId, string value);
}
