namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class EventSettingGroup : ISettingGroup
{
    public int MaxSessionsPerEvent { get; private set; } = 100;
    public bool UserSubmissionEnabled { get; private set; } = true;
    public bool OrganizationSubmissionEnabled { get; private set; } = true;
    public bool GroupSubmissionEnabled { get; private set; } = true;
    public bool RequireApproval { get; private set; }
    public bool CardClickOpensDetailPage { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Events.UserSubmissionEnabled,
        GovernanceSettingKeys.Events.OrganizationSubmissionEnabled,
        GovernanceSettingKeys.Events.GroupSubmissionEnabled,
        GovernanceSettingKeys.Events.RequireApproval,
        GovernanceSettingKeys.Events.CardClickOpensDetailPage
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Events.UserSubmissionEnabled, out var userSub))
            UserSubmissionEnabled = SettingValueSerializer.Deserialize(userSub.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, out var orgSub))
            OrganizationSubmissionEnabled = SettingValueSerializer.Deserialize(orgSub.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Events.GroupSubmissionEnabled, out var grpSub))
            GroupSubmissionEnabled = SettingValueSerializer.Deserialize(grpSub.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Events.RequireApproval, out var approval))
            RequireApproval = SettingValueSerializer.Deserialize(approval.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Events.CardClickOpensDetailPage, out var cardClick))
            CardClickOpensDetailPage = SettingValueSerializer.Deserialize(cardClick.Value, true);
    }
}
