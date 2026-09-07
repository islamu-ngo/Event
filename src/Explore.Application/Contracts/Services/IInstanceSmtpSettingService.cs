using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IInstanceSmtpSettingService
{
    Task<InstanceSmtpSettingsDto> ReadSettingsAsync();

    Task ApplySettingsAsync(InstanceSmtpSettingsDto settings);
}
