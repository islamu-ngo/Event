using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetInstanceSmtpSettingsQueryHandler : IRequestHandler<GetInstanceSmtpSettingsQuery, InstanceSmtpSettingsDto>
{
    private readonly IInstanceSmtpSettingService _smtpSettingService;

    public GetInstanceSmtpSettingsQueryHandler(IInstanceSmtpSettingService smtpSettingService)
    {
        _smtpSettingService = smtpSettingService;
    }

    public async Task<InstanceSmtpSettingsDto> Handle(GetInstanceSmtpSettingsQuery request, CancellationToken cancellationToken)
    {
        return await _smtpSettingService.ReadSettingsAsync();
    }
}
