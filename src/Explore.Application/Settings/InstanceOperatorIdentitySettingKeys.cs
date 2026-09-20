using System.Collections.Immutable;

namespace Explore.Application.Settings;

/// <summary>
/// Dedicated setting keys owned by the instance operator identity management API.
/// Generic system and tenant setting mutations against these keys are rejected so the
/// dedicated authority boundary cannot be bypassed.
/// </summary>
public static class InstanceOperatorIdentitySettingKeys
{
    public const string OperatorIdentity = "instance.operator_identity";

    public static ImmutableArray<string> All { get; } = [OperatorIdentity];

    public static bool Contains(string key) =>
        All.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase);

    public static void RejectGenericMutation(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Contains(key))
        {
            throw new InvalidOperationException(
                "The instance operator identity document requires the dedicated operator identity management API.");
        }

        // Database collations may equate accented/full-width characters or ignore padding.
        // Canonical setting identifiers are ASCII; reject aliases before a collation can resolve them.
        if (key.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "Setting mutations require a canonical ASCII setting key.",
                nameof(key));
        }
    }
}
