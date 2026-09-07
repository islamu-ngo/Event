// ABOUTME: Query handler returning the current instance SMTP configuration.
// ABOUTME: Reads SMTP settings from the infrastructure config store.
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetInstanceSmtpSettingsQueryHandler : IRequestHandler<GetInstanceSmtpSettingsQuery, InstanceSmtpSettingsDto>
{
    private readonly IInstanceSmtpSettingService _smtpSettingService;
    private readonly IAdminContext _adminContext;
    private readonly IPlatformUserRoleRepository _platformRoles;

    public GetInstanceSmtpSettingsQueryHandler(IInstanceSmtpSettingService smtpSettingService,
        IAdminContext adminContext, IPlatformUserRoleRepository platformRoles)
    {
        _smtpSettingService = smtpSettingService;
        _adminContext = adminContext;
        _platformRoles = platformRoles;
    }

    public async Task<InstanceSmtpSettingsDto> Handle(GetInstanceSmtpSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _smtpSettingService.ReadSettingsAsync();
        var actorId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        return settings with
        {
            CanManageDelivery = actorId is { } actor && actor != Guid.Empty
                && await _platformRoles.IsUserPlatformAdmin(actor)
        };
    }
}
