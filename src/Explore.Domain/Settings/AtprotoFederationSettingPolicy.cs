namespace Explore.Domain.Settings;

/// <summary>
/// Defines admission and reset boundaries for the three dedicated federation settings.
/// </summary>
public static class AtprotoFederationSettingPolicy
{
    public static bool IsDedicatedKey(string? key) => key is
        Constants.GovernanceSettingKeys.Federation.AtprotoEventsEnabled or
        Constants.GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled or
        Constants.GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode;

    /// <summary>
    /// Accepts only the command serializer's finite scalar vocabulary without rewriting admitted effects.
    /// </summary>
    public static void Validate(string key, string value, SettingScope scope)
    {
        if (!IsDedicatedKey(key))
            throw new ArgumentException("A dedicated federation setting key is required.", nameof(key));
        if (scope is not (SettingScope.Instance or SettingScope.Tenant))
            throw new ArgumentException("Federation policy belongs to instance or tenant scope.", nameof(scope));

        var valid = key == Constants.GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode
            ? IsBackfillMode(value)
            : value is "true" or "false";
        if (!valid)
            throw new ArgumentException("The serialized value is outside the dedicated federation vocabulary.", nameof(value));
    }

    /// <summary>Only deleting a tenant override can restore an inherited federation value.</summary>
    public static bool CanReset(SettingScope scope) => scope == SettingScope.Tenant;

    internal static void ValidateAuditTime(DateTime timestamp)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Federation setting audit time must be UTC.", nameof(timestamp));
    }

    private static bool IsBackfillMode(string? value)
    {
        if (value is null || value.Any(character => character > 127))
            return false;

        return string.Equals(value, "\"downtime_only\"", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "\"full\"", StringComparison.OrdinalIgnoreCase);
    }
}
