using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

namespace Explore.Application.Settings.Groups;

public sealed class SupportAccessSettingGroup : ISettingGroup
{
    public const int DefaultMaxReadOnlyMinutes = 30;
    public const int DefaultMaxWriteMinutes = 10;
    public const int AbsoluteMaxReadOnlyMinutes = 240;
    public const int AbsoluteMaxWriteMinutes = 60;

    public bool Enabled { get; private set; }
    public int MaxReadOnlyMinutes { get; private set; } = DefaultMaxReadOnlyMinutes;
    public int MaxWriteMinutes { get; private set; } = DefaultMaxWriteMinutes;
    public bool AllowWriteMode { get; private set; }
    public bool RequireTicketReference { get; private set; } = true;
    public bool OneActiveSessionPerActor { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.SupportAccess.Enabled,
        GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes,
        GovernanceSettingKeys.SupportAccess.MaxWriteMinutes,
        GovernanceSettingKeys.SupportAccess.AllowWriteMode,
        GovernanceSettingKeys.SupportAccess.RequireTicketReference,
        GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.SupportAccess.Enabled, out var enabled))
            Enabled = SettingValueSerializer.Deserialize(enabled.Value, false);

        if (settings.TryGetValue(GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes, out var readOnlyMinutes))
            MaxReadOnlyMinutes = ClampMinutes(
                SettingValueSerializer.DeserializeInt(readOnlyMinutes.Value, DefaultMaxReadOnlyMinutes),
                DefaultMaxReadOnlyMinutes,
                AbsoluteMaxReadOnlyMinutes);

        if (settings.TryGetValue(GovernanceSettingKeys.SupportAccess.MaxWriteMinutes, out var writeMinutes))
            MaxWriteMinutes = ClampMinutes(
                SettingValueSerializer.DeserializeInt(writeMinutes.Value, DefaultMaxWriteMinutes),
                DefaultMaxWriteMinutes,
                AbsoluteMaxWriteMinutes);

        if (settings.TryGetValue(GovernanceSettingKeys.SupportAccess.AllowWriteMode, out var allowWriteMode))
            AllowWriteMode = SettingValueSerializer.Deserialize(allowWriteMode.Value, false);

        if (settings.TryGetValue(GovernanceSettingKeys.SupportAccess.RequireTicketReference, out var requireTicket))
            RequireTicketReference = SettingValueSerializer.Deserialize(requireTicket.Value, true);

        if (settings.TryGetValue(GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor, out var oneActive))
            OneActiveSessionPerActor = SettingValueSerializer.Deserialize(oneActive.Value, true);
    }

    public int GetMaxDurationMinutes(bool writeMode) =>
        writeMode ? MaxWriteMinutes : MaxReadOnlyMinutes;

    private static int ClampMinutes(int value, int fallback, int maximum)
    {
        if (value <= 0)
            return fallback;

        return Math.Min(value, maximum);
    }
}
