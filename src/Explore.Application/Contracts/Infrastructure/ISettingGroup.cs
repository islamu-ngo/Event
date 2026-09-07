namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// A strongly-typed group of related settings.
/// Each group declares its required keys and populates itself from resolved values.
/// </summary>
public interface ISettingGroup
{
    /// <summary>
    /// The setting keys this group requires.
    /// Used by the resolver to batch-load only the needed settings.
    /// </summary>
    static abstract IEnumerable<string> SettingKeys { get; }

    /// <summary>
    /// Populates this group's properties from the resolved settings dictionary.
    /// Called by the resolver after batch-loading all required keys.
    /// </summary>
    void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings);
}
