// ABOUTME: Service implementation for non-secret instance SMTP governance.
// ABOUTME: Credentials never enter SystemSetting records or application write contracts.

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Services;

public class InstanceSmtpSettingService : IInstanceSmtpSettingService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;
    private readonly IPublisher _publisher;

    public InstanceSmtpSettingService(
        ISystemSettingRepository systemSettingRepository,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        IPublisher publisher)
    {
        _systemSettingRepository = systemSettingRepository;
        _emailSettingsWriter = emailSettingsWriter;
        _publisher = publisher;
    }

    public async Task<InstanceSmtpSettingsDto> ReadSettingsAsync()
    {
        var enabled = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.DeliveryEnabled);
        var host = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpHost);
        var port = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpPort);
        var security = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpSecurity);
        var fromAddress = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.FromAddress);
        var fromName = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.FromName);
        var timeout = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpTimeoutSeconds);
        var skipCertValidation = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Email.SmtpSkipCertValidation);

        return new InstanceSmtpSettingsDto
        {
            DeliveryEnabled = DeserializeBoolean(enabled?.Value, false),
            Host = DeserializeString(host?.Value, string.Empty),
            Port = DeserializeInt(port?.Value, 587),
            Security = DeserializeString(security?.Value, "StartTls"),
            FromAddress = DeserializeString(fromAddress?.Value, string.Empty),
            FromName = DeserializeString(fromName?.Value, string.Empty),
            TimeoutSeconds = DeserializeInt(timeout?.Value, 30),
            SkipCertificateValidation = DeserializeBoolean(skipCertValidation?.Value, false)
        };
    }

    public async Task ApplySettingsAsync(
        InstanceSmtpSettingsDto? settings,
        Guid? actorUserId = null,
        bool enableDelivery = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _emailSettingsWriter.ApplyAsync(
            [
                .. (enableDelivery
                    ? new[] { new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true") }
                    : []),
                .. (settings is null ? [] : new[]
                {
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.Host.Trim())),
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpPort,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.Port > 0 ? settings.Port : 587)),
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpSecurity,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.Security.Trim())),
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.FromAddress,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.FromAddress.Trim())),
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.FromName,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.FromName.Trim())),
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpTimeoutSeconds,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30)),
                    new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpSkipCertValidation,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: JsonSerializer.Serialize(settings.SkipCertificateValidation))
                })
            ],
            actorUserId: actorUserId, cancellationToken: cancellationToken);
        result.EnsureAccepted();
        foreach (var notification in result.ToNotifications(actorUserId))
            await _publisher.Publish(notification, CancellationToken.None);
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

}
