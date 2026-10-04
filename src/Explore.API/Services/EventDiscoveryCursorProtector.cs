using System.Text.Json;
using Explore.Application.Features.Events.Discovery;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.API.Services;

public sealed class EventDiscoveryCursorProtector(IDataProtectionProvider provider)
    : IEventDiscoveryCursorProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector(
        "Explore.EventDiscoveryContinuation", "v1");

    public string Protect(EventDiscoveryContinuation continuation) =>
        _protector.Protect(JsonSerializer.Serialize(continuation));

    public EventDiscoveryContinuation Unprotect(string cursor) =>
        JsonSerializer.Deserialize<EventDiscoveryContinuation>(_protector.Unprotect(cursor))
        ?? throw new JsonException("Discovery continuation is empty.");
}
