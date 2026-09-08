namespace Explore.Application.Features.Settings.Requests.Commands;

using Explore.Application.DTOs.Settings;
using Explore.Domain.Settings;
using MediatR;

/// <summary>
/// Batch-updates multiple setting values within a category. In BestEffort mode (default, for autosave),
/// locked keys are skipped and the rest applied. In Strict mode (admin), the entire batch is rejected
/// if any key is locked.
/// </summary>
public sealed record UpdateSettingBatchCommand : IRequest<BatchUpdateResponseDto>
{
    /// <summary>
    /// Setting category to scope the update (e.g., "EventList"). All keys must belong to this category.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Key-value pairs to update. Keys are fully qualified (e.g., "event_list.page_size").
    /// Values are plain strings — handler validates and serializes per ValueType.
    /// </summary>
    private IReadOnlyDictionary<string, string> _values =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public required IReadOnlyDictionary<string, string> Values
    {
        get => _values;
        init => _values = value is null
            ? null!
            : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(value, StringComparer.Ordinal));
    }

    /// <summary>
    /// The scope at which to write the overrides.
    /// </summary>
    public required SettingScope Scope { get; init; }

    /// <summary>
    /// How to handle locked or invalid keys within the batch.
    /// </summary>
    public BatchUpdateMode Mode { get; init; } = BatchUpdateMode.BestEffort;
}
