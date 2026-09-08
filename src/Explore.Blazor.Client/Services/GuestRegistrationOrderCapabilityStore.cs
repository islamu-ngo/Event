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

    public void RestoreBookmark(Guid eventId, Guid orderId, string value)
    {
        Remove(eventId, orderId);
        if (eventId != Guid.Empty && orderId != Guid.Empty && value.Length == 43
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            Store(eventId, orderId, new GuestRegistrationOrderCapability(value));
        }
    }

    public static bool IsStatusPath(string? path) =>
        path?.Contains("/registration/guest/events/", StringComparison.OrdinalIgnoreCase) == true
        && path.TrimEnd('/').EndsWith("/status", StringComparison.OrdinalIgnoreCase);
}
