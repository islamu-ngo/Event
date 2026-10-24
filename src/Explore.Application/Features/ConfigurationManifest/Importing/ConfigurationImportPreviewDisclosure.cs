using System.Collections.Immutable;

namespace Explore.Application.Features.ConfigurationManifest.Importing;

public sealed record ConfigurationImportModerationChange(
    string Section, string? ReferenceCode, long? ReferenceRevision, string Kind, bool Conflict)
{
    public override string ToString() => nameof(ConfigurationImportModerationChange);
}

public sealed record ConfigurationImportModerationDiagnostic(
    string Kind, string Severity, string? RuleCode, long? RuleRevision, string? ConsumerId, string? Mode)
{
    public override string ToString() => nameof(ConfigurationImportModerationDiagnostic);
}

public sealed record ConfigurationImportModerationTargetPreview(
    string SectionKey, string SourceName, string TargetName, long CurrentPolicyRevision,
    bool HasBaseline, bool CanApply, ImmutableArray<ConfigurationImportModerationChange> Changes,
    ImmutableArray<ConfigurationImportModerationDiagnostic> Diagnostics)
{
    public override string ToString() => nameof(ConfigurationImportModerationTargetPreview);
}

/// <summary>Owns one private preparation and delivery under the original session and native fences.</summary>
public sealed class ConfigurationImportPreviewDisclosure : IAsyncDisposable
{
    internal delegate Task DisclosureOperation(
        Func<ConfigurationImportPreviewResult, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> deliver,
        CancellationToken cancellationToken);

    private DisclosureOperation? operation;
    private readonly CancellationToken origin;
    private readonly DateTimeOffset expiresAt;
    private readonly TimeProvider clock;

    internal ConfigurationImportPreviewDisclosure(DisclosureOperation operation,
        DateTimeOffset expiresAt, TimeProvider clock, CancellationToken origin)
    {
        this.operation = operation;
        this.origin = origin;
        this.expiresAt = expiresAt;
        this.clock = clock;
    }

    public async Task DiscloseAsync(
        Func<ConfigurationImportPreviewResult, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> deliver,
        Func<CancellationToken, Task>? complete = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var execute = Interlocked.Exchange(ref operation, null)
            ?? throw new InvalidOperationException("Configuration preview disclosure is no longer available.");
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) throw new OperationCanceledException("Configuration preview expired.");
        using var deadline = new CancellationTokenSource(remaining, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(origin, cancellationToken, deadline.Token);
        linked.Token.ThrowIfCancellationRequested();
        await execute(prepare, deliver, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (clock.GetUtcNow() >= expiresAt) throw new OperationCanceledException("Configuration preview expired.");
        if (complete is not null) await complete(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (clock.GetUtcNow() >= expiresAt) throw new OperationCanceledException("Configuration preview expired.");
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref operation, null);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(ConfigurationImportPreviewDisclosure);
}
