// ABOUTME: Service implementation for non-secret instance SMTP governance.
// ABOUTME: Credentials never enter SystemSetting records or application write contracts.

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public class InstanceSmtpSettingService : IInstanceSmtpSettingService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly ISettingMutationLock _mutationLock;

    public InstanceSmtpSettingService(ISystemSettingRepository systemSettingRepository, ISettingMutationLock mutationLock)
    {
        _systemSettingRepository = systemSettingRepository;
        _mutationLock = mutationLock;
    }

    public async Task<InstanceSmtpSettingsDto> ReadSettingsAsync()
    {
        var host = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpHost);
        var port = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpPort);
        var security = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpSecurity);
        var fromAddress = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.FromAddress);
        var fromName = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.FromName);
        var timeout = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpTimeoutSeconds);
        var skipCertValidation = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpSkipCertValidation);

        return new InstanceSmtpSettingsDto
        {
            Host = DeserializeString(host?.Value, string.Empty),
            Port = DeserializeInt(port?.Value, 587),
            Security = DeserializeString(security?.Value, "StartTls"),
            FromAddress = DeserializeString(fromAddress?.Value, string.Empty),
            FromName = DeserializeString(fromName?.Value, string.Empty),
            TimeoutSeconds = DeserializeInt(timeout?.Value, 30),
            SkipCertificateValidation = DeserializeBoolean(skipCertValidation?.Value, false)
        };
    }

    public Task ApplySettingsAsync(InstanceSmtpSettingsDto settings) =>
        _mutationLock.ExecuteManyAsync(EmailSettingGroup.SettingKeys, async _ =>
        {
            await ApplySettingsCoreAsync(settings);
            return true;
        });

    private async Task ApplySettingsCoreAsync(InstanceSmtpSettingsDto settings)
    {
        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.SmtpHost,
            JsonSerializer.Serialize(settings.Host.Trim()),
            SettingValueType.String,
            "Email",
            1,
            "SMTP host server name");

        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.SmtpPort,
            JsonSerializer.Serialize(settings.Port > 0 ? settings.Port : 587),
            SettingValueType.Integer,
            "Email",
            2,
            "SMTP server port");

        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.SmtpSecurity,
            JsonSerializer.Serialize(settings.Security.Trim()),
            SettingValueType.String,
            "Email",
            5,
            "SMTP security mode: None, StartTls, SslOnConnect, or Auto");

        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.FromAddress,
            JsonSerializer.Serialize(settings.FromAddress.Trim()),
            SettingValueType.String,
            "Email",
            6,
            "Default sender email address");

        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.FromName,
            JsonSerializer.Serialize(settings.FromName.Trim()),
            SettingValueType.String,
            "Email",
            7,
            "Default sender display name");

        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.SmtpTimeoutSeconds,
            JsonSerializer.Serialize(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30),
            SettingValueType.Integer,
            "Email",
            8,
            "SMTP connection timeout in seconds");

        await UpsertSystemSettingAsync(
            GovernanceSettingKeys.Email.SmtpSkipCertValidation,
            JsonSerializer.Serialize(settings.SkipCertificateValidation),
            SettingValueType.Boolean,
            "Email",
            9,
            "Skip TLS certificate validation (for development only)");
    }

    private static int DeserializeInt(string? rawValue, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<int>(rawValue);
        }
        catch
        {
            return int.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static bool DeserializeBoolean(string? rawValue, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch
        {
            return bool.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static string DeserializeString(string? rawValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }

    private async Task UpsertSystemSettingAsync(
        string settingKey,
        string value,
        SettingValueType valueType,
        string category,
        int displayOrder,
        string description)
    {
        var existing = await _systemSettingRepository.GetByKey(settingKey);
        await _systemSettingRepository.UpsertInCurrentTransactionAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = value,
            ValueType = valueType,
            IsLocked = existing?.IsLocked ?? false,
            Description = description,
            Category = category,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }
}
