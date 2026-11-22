using System.Collections.Immutable;
using Explore.Domain.Constants;

namespace Explore.Domain.Settings;

public static class AnnouncementSettingMutationGuard
{
    public const string FailureCode = "announcement_typed_operation_required";

    public static ImmutableArray<string> ProtectedKeys { get; } =
    [
        GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication,
        GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl,
        GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
        GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedRevision
    ];

    public static bool Handles(string? key) => key is not null
        && ProtectedKeys.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsRetired(string? key) => key is not null && Handles(key)
        && !string.Equals(key.Trim(), GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(key.Trim(), GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition, StringComparison.OrdinalIgnoreCase);

    public static void RejectGenericMutation(string key)
    {
        if (Handles(key))
            throw new InvalidOperationException(FailureCode);
    }
}
