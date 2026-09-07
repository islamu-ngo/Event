using System.Collections.Immutable;

namespace Explore.Domain.Settings;

/// <summary>
/// Defines the metadata for a single setting in the hierarchical settings engine.
/// Definitions are registered in <see cref="SettingRegistry"/> at startup and are immutable.
/// </summary>
/// <param name="Key">Unique dot-notation key (e.g., "email.smtp_host").</param>
/// <param name="ValueType">Data type of the value for serialization and validation.</param>
/// <param name="DefaultValue">JSON-serialized default value used when no scope override exists.</param>
/// <param name="Category">Grouping category for admin UI display (e.g., "Email", "Branding").</param>
/// <param name="Description">Human-readable description of the setting.</param>
/// <param name="MinScope">Broadest scope at which this setting can be set. Defaults to Instance.</param>
/// <param name="MaxScope">Narrowest scope at which this setting can be overridden. Defaults to Tenant.</param>
/// <param name="IsLockable">Whether a parent scope can lock this setting to prevent child overrides.</param>
/// <param name="IsSensitive">Whether this setting contains credentials or secrets requiring masked display.</param>
/// <param name="AllowedValues">Optional constrained set of allowed values (JSON-serialized strings).</param>
public sealed record SettingDefinition
{
    public SettingDefinition(
        string Key,
        SettingValueType ValueType,
        string DefaultValue,
        string Category,
        string Description,
        SettingScope MinScope = SettingScope.Instance,
        SettingScope MaxScope = SettingScope.Tenant,
        bool IsLockable = true,
        bool IsSensitive = false,
        IEnumerable<string>? AllowedValues = null)
    {
        this.Key = Key;
        this.ValueType = ValueType;
        this.DefaultValue = DefaultValue;
        this.Category = Category;
        this.Description = Description;
        this.MinScope = MinScope;
        this.MaxScope = MaxScope;
        this.IsLockable = IsLockable;
        this.IsSensitive = IsSensitive;
        this.AllowedValues = AllowedValues?.ToImmutableList();
    }

    public string Key { get; }
    public SettingValueType ValueType { get; }
    public string DefaultValue { get; }
    public string Category { get; }
    public string Description { get; }
    public SettingScope MinScope { get; }
    public SettingScope MaxScope { get; }
    public bool IsLockable { get; }
    public bool IsSensitive { get; }
    public ImmutableList<string>? AllowedValues { get; }

    /// <summary>Whether this setting must participate in coordinated policy mutation.</summary>
    public bool RequiresCoordinatedMutation { get; init; } = false;
}
