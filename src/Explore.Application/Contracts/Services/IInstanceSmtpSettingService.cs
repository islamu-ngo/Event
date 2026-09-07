// ABOUTME: Service contract for managing instance SMTP configuration.
// ABOUTME: Handles SMTP settings persistence in SystemSetting records for admin UI usage.

using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IInstanceSmtpSettingService
{
    Task<InstanceSmtpSettingsDto> ReadSettingsAsync();

    Task ApplySettingsAsync(
        InstanceSmtpSettingsDto? settings,
        Guid? actorUserId = null,
        bool enableDelivery = false,
        CancellationToken cancellationToken = default);
}
