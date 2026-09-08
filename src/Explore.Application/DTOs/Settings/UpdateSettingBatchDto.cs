using System.Collections.ObjectModel;

namespace Explore.Application.DTOs.Settings;

public sealed record UpdateSettingBatchDto
{
    private IReadOnlyDictionary<string, string> _values = null!;

    public required IReadOnlyDictionary<string, string> Values
    {
        get => _values;
        init => _values = value is null
            ? null!
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Optional batch update mode. If not specified, the controller selects an appropriate default:
    /// BestEffort for user scope (skip locked, apply rest) or Strict for tenant scope (reject all if any locked).
    /// </summary>
    public BatchUpdateMode? Mode { get; init; }
}
