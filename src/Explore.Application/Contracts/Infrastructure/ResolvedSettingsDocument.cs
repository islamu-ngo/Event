namespace Explore.Application.Contracts.Infrastructure;

public sealed record ResolvedSettingsDocument<TPayload>
    where TPayload : notnull
{
    public Guid? DocumentId { get; init; }

    public required string DocumentKey { get; init; }

    public required int SchemaVersion { get; init; }

    public required string DefaultsVersion { get; init; }

    public required TPayload Payload { get; init; }

    public required SettingsDocumentSource Source { get; init; }

    public required Guid SourceScopeId { get; init; }

    public required Guid ConcurrencyStamp { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public Guid? UpdatedBy { get; init; }
}

public enum SettingsDocumentSource
{
    Tenant = 0
}
