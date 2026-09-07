using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class GuestRegistrationOrderCapabilityStore : IGuestRegistrationOrderCapabilityStore
{
    private readonly Dictionary<(Guid EventId, Guid OrderId), GuestRegistrationOrderCapability> _capabilities = [];

    public void Store(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability) =>
        _capabilities[(eventId, orderId)] = capability;

    public bool TryGet(Guid eventId, Guid orderId, out GuestRegistrationOrderCapability? capability) =>
        _capabilities.TryGetValue((eventId, orderId), out capability);

    public void Remove(Guid eventId, Guid orderId) => _capabilities.Remove((eventId, orderId));
}
