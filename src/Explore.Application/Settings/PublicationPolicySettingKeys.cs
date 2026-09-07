namespace Explore.Application.Settings;

using System.Collections.Immutable;
using Explore.Domain.Settings.Definitions;

public static class PublicationPolicySettingKeys
{
    private static readonly ImmutableArray<string> GuardedKeys =
    [
        EventReportingIntakeSettingDefinitions.IntakeEnabled.Key,
        EventSettingDefinitions.RequireApproval.Key,
        EventSettingDefinitions.UserSubmissionEnabled.Key,
        EventSettingDefinitions.OrganizationSubmissionEnabled.Key,
        EventSettingDefinitions.GroupSubmissionEnabled.Key
    ];

    public static IReadOnlyList<string> All => GuardedKeys;
}
