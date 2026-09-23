using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services;

/// <summary>Opaque paging position, bound to the current observer and query, never authority.</summary>
public sealed class EventResourceCursorProtector(IDataProtectionProvider provider, TimeProvider clock)
    : IEventResourceCursorProtector
{
    private const int QueryVersion = 1;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly IDataProtector _protector = provider.CreateProtector("EventResource.AudienceCursor.v1");

    public string Protect(EventResourceCursorScope scope, EventResourceCursorPosition position) =>
        _protector.Protect(JsonSerializer.Serialize(new Payload(QueryVersion, scope, position,
            clock.GetUtcNow().Add(Lifetime))));

    public bool TryUnprotect(string cursor, EventResourceCursorScope scope, out EventResourceCursorPosition? position)
    {
        position = null;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 2048) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(cursor));
            var now = clock.GetUtcNow();
            if (payload is null || payload.Version != QueryVersion || payload.Scope != scope
                || payload.Position is not { ResourceId: var id } || id == Guid.Empty
                || payload.ExpiresAt <= now || payload.ExpiresAt > now.Add(Lifetime)) return false;
            position = payload.Position;
            return true;
        }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
    }

    private sealed record Payload(int Version, EventResourceCursorScope Scope,
        EventResourceCursorPosition Position, DateTimeOffset ExpiresAt);
}
