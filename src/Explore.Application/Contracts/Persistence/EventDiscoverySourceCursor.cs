namespace Explore.Application.Contracts.Persistence;

/// <summary>Ephemeral, complete source seek position inside one caller-owned consistent capture.</summary>
public sealed record EventDiscoverySourceCursor(
    Guid Id, string Title, int Views, DateTime CreatedAtUtc, DateTimeOffset? StartsAt);
