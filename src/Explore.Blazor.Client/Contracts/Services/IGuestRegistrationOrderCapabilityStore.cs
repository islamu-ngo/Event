namespace Explore.Blazor.Client.Contracts.Services;

public interface IGuestRegistrationOrderCapabilityStore
{
    void Store(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability);
    bool TryGet(Guid eventId, Guid orderId, out GuestRegistrationOrderCapability? capability);
    void Remove(Guid eventId, Guid orderId);
}
