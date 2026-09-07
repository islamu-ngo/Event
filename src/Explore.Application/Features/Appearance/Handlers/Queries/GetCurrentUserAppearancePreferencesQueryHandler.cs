namespace Explore.Application.Features.Appearance.Handlers.Queries;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using MediatR;

public class GetCurrentUserAppearancePreferencesQueryHandler : IRequestHandler<GetCurrentUserAppearancePreferencesQuery, UserAppearancePreferencesDto>
{
    private readonly IHierarchicalSettingsResolver _hierarchicalSettingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public GetCurrentUserAppearancePreferencesQueryHandler(
        IHierarchicalSettingsResolver hierarchicalSettingsResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _hierarchicalSettingsResolver = hierarchicalSettingsResolver;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    public async Task<UserAppearancePreferencesDto> Handle(GetCurrentUserAppearancePreferencesQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
        {
            return new UserAppearancePreferencesDto();
        }

        var context = new SettingContext(
            TenantId: _tenantContext.TenantId,
            UserId: userId.Value);

        var appearance = await _hierarchicalSettingsResolver.ResolveGroupAsync<AppearanceSettingGroup>(context, cancellationToken);

        return new UserAppearancePreferencesDto
        {
            ThemeMode = appearance.ThemeMode,
            Direction = appearance.Direction,
            Language = appearance.Language,
            DefaultThemeId = appearance.ActiveProfileId
        };
    }
}
