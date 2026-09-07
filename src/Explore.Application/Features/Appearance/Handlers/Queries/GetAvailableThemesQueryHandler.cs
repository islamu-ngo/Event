namespace Explore.Application.Features.Appearance.Handlers.Queries;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Common;
using Explore.Application.Features.Appearance.Requests.Queries;
using MediatR;

public class GetAvailableThemesQueryHandler : IRequestHandler<GetAvailableThemesQuery, IReadOnlyList<AvailableThemeDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IUiThemeRepository _uiThemeRepository;

    public GetAvailableThemesQueryHandler(
        ITenantContext tenantContext,
        IUiThemeRepository uiThemeRepository)
    {
        _tenantContext = tenantContext;
        _uiThemeRepository = uiThemeRepository;
    }

    public async Task<IReadOnlyList<AvailableThemeDto>> Handle(GetAvailableThemesQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var themes = await _uiThemeRepository.GetAvailableThemesForTenantAsync(tenantId, activeOnly: true);

        return themes
            .Select(theme => new AvailableThemeDto
            {
                Id = theme.Id,
                ThemeKey = theme.ThemeKey,
                DisplayName = theme.DisplayName,
                Description = theme.Description,
                IsDefault = theme.IsDefault,
                IsPlatformTheme = !theme.TenantId.HasValue,
                SortOrder = theme.SortOrder,
                LightPalette = UiThemeMapper.ToPaletteDto(theme.LightPalette),
                DarkPalette = UiThemeMapper.ToPaletteDto(theme.DarkPalette)
            })
            .ToList();
    }
}
