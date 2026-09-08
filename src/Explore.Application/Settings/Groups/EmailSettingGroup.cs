namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

/// <summary>
/// Strongly-typed group for all email/SMTP settings.
/// Resolved in a single batch call instead of 9 individual GetSettingAsync calls.
/// </summary>
public class EmailSettingGroup : ISettingGroup
{
    public bool DeliveryEnabled { get; private set; }
    public string? SmtpHost { get; private set; }
    public int SmtpPort { get; private set; } = 587;
    public string SmtpSecurity { get; private set; } = "StartTls";
    public string? FromAddress { get; private set; }
    public string FromName { get; private set; } = "Explore";
    public int SmtpTimeoutSeconds { get; private set; } = 30;
    public bool SmtpSkipCertValidation { get; private set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Email.DeliveryEnabled,
        GovernanceSettingKeys.Email.SmtpHost,
        GovernanceSettingKeys.Email.SmtpPort,
        GovernanceSettingKeys.Email.SmtpSecurity,
        GovernanceSettingKeys.Email.FromAddress,
        GovernanceSettingKeys.Email.FromName,
        GovernanceSettingKeys.Email.SmtpTimeoutSeconds,
        GovernanceSettingKeys.Email.SmtpSkipCertValidation
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Email.DeliveryEnabled, out var enabled))
            DeliveryEnabled = SettingValueSerializer.Deserialize(enabled.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Email.SmtpHost, out var host))
            SmtpHost = SettingValueSerializer.DeserializeString(host.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Email.SmtpPort, out var port))
            SmtpPort = SettingValueSerializer.Deserialize(port.Value, 587);
        if (settings.TryGetValue(GovernanceSettingKeys.Email.SmtpSecurity, out var sec))
            SmtpSecurity = SettingValueSerializer.Deserialize(sec.Value, "StartTls");
        if (settings.TryGetValue(GovernanceSettingKeys.Email.FromAddress, out var from))
            FromAddress = SettingValueSerializer.DeserializeString(from.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Email.FromName, out var name))
            FromName = SettingValueSerializer.Deserialize(name.Value, "Explore");
        if (settings.TryGetValue(GovernanceSettingKeys.Email.SmtpTimeoutSeconds, out var timeout))
            SmtpTimeoutSeconds = SettingValueSerializer.Deserialize(timeout.Value, 30);
        if (settings.TryGetValue(GovernanceSettingKeys.Email.SmtpSkipCertValidation, out var skipCert))
            SmtpSkipCertValidation = SettingValueSerializer.Deserialize(skipCert.Value, false);
    }
}
