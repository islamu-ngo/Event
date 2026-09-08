namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class OrganizationSettingGroup : ISettingGroup
{
    public bool VerificationRequired { get; private set; } = true;
    public bool TenantCanOmitVerification { get; private set; }
    public bool SelfRegistrationEnabled { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Organizations.VerificationRequired,
        GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
        GovernanceSettingKeys.Organizations.SelfRegistrationEnabled
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Organizations.VerificationRequired, out var ver))
            VerificationRequired = SettingValueSerializer.Deserialize(ver.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Organizations.TenantCanOmitVerification, out var omit))
            TenantCanOmitVerification = SettingValueSerializer.Deserialize(omit.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Organizations.SelfRegistrationEnabled, out var selfReg))
            SelfRegistrationEnabled = SettingValueSerializer.Deserialize(selfReg.Value, true);
    }
}
