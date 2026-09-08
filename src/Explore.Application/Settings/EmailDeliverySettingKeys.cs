
using System.Collections.Immutable;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Settings;

public static class EmailDeliverySettingKeys
{
    public static ImmutableArray<string> All { get; } =
        [.. EmailSettingDefinitions.All.Select(definition => definition.Key), GovernanceSettingKeys.TenantDelegation.LockSmtp];

    public static bool Contains(string key) => All.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase);

    public static void RejectGenericMutation(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Contains(key))
            throw new InvalidOperationException("SMTP policy settings require the dedicated email delivery settings writer.");
        // Database collations may equate accented/full-width characters or ignore padding.
        // Canonical setting identifiers are ASCII; reject aliases before a collation can resolve them.
        if (key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
            throw new ArgumentException("Setting mutations require a canonical ASCII setting key.", nameof(key));
    }
}
