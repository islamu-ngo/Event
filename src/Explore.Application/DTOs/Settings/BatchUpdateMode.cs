namespace Explore.Application.DTOs.Settings;

/// <summary>
/// Controls how batch setting updates handle locked or invalid keys.
/// </summary>
public enum BatchUpdateMode
{
    /// <summary>
    /// Skip locked/invalid keys and apply the rest. Used for drawer autosave.
    /// </summary>
    BestEffort = 0,

    /// <summary>
    /// Reject the entire batch if any key is locked or invalid. Used for admin operations.
    /// </summary>
    Strict = 1
}
