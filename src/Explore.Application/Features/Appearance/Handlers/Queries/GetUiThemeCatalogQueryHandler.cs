namespace Explore.Application.Features.Appearance.Handlers.Queries;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Common;
using Explore.Application.Features.Appearance.Requests.Queries;
using MediatR;

public class GetUiThemeCatalogQueryHandler : IRequestHandler<GetUiThemeCatalogQuery, IReadOnlyList<UiThemeListItemDto>>
{
    private readonly IUiThemeRepository _uiThemeRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;

    public GetUiThemeCatalogQueryHandler(
        IUiThemeRepository uiThemeRepository,
        ITenantContext tenantContext,
        IAdminContext adminContext)
    {
        _uiThemeRepository = uiThemeRepository;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
    }

    public async Task<IReadOnlyList<UiThemeListItemDto>> Handle(GetUiThemeCatalogQuery request, CancellationToken cancellationToken)
    {
        Guid? ownerTenantId = request.IsPlatformCatalog ? null : _tenantContext.TenantId;

        if (!await IsAuthorizedAsync(request.IsPlatformCatalog, ownerTenantId, cancellationToken))
        {
            return [];
        }

        var themes = await _uiThemeRepository.GetOwnedThemesAsync(ownerTenantId, request.ActiveOnly);
        return themes.Select(UiThemeMapper.ToListItem).ToList();
    }

    private async Task<bool> IsAuthorizedAsync(bool isPlatformCatalog, Guid? ownerTenantId, CancellationToken cancellationToken)
    {
        if (isPlatformCatalog)
        {
            return await _adminContext.IsInstanceAdminAsync(cancellationToken);
        }

        return await _adminContext.IsTenantAdminAsync(ownerTenantId!.Value, cancellationToken)
            || await _adminContext.IsInstanceAdminAsync(cancellationToken);
    }
}
