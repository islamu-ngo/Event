namespace Explore.Domain.Settings.Documents;

/// <summary>
/// Versioned typed settings document value used by application code before persistence serialization.
/// </summary>
/// <typeparam name="TPayload">Strongly typed non-secret settings payload.</typeparam>
public sealed record SettingsDocument<TPayload>
    where TPayload : notnull
{
    public required string DocumentKey { get; init; }

    public required int SchemaVersion { get; init; }

    public required TPayload Payload { get; init; }

    public required string DefaultsVersion { get; init; }

    public Guid? UpdatedBy { get; init; }

    public DateTime? UpdatedAt { get; init; }
}
