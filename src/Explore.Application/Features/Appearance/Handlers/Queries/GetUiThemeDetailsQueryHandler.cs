namespace Explore.Application.Features.Appearance.Handlers.Queries;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Common;
using Explore.Application.Features.Appearance.Requests.Queries;
using MediatR;

public class GetUiThemeDetailsQueryHandler : IRequestHandler<GetUiThemeDetailsQuery, UiThemeDetailsDto?>
{
    private readonly IUiThemeRepository _uiThemeRepository;
    private readonly IAdminContext _adminContext;

    public GetUiThemeDetailsQueryHandler(
        IUiThemeRepository uiThemeRepository,
        IAdminContext adminContext)
    {
        _uiThemeRepository = uiThemeRepository;
        _adminContext = adminContext;
    }

    public async Task<UiThemeDetailsDto?> Handle(GetUiThemeDetailsQuery request, CancellationToken cancellationToken)
    {
        var theme = await _uiThemeRepository.GetById(request.Id);
        if (theme is null)
        {
            return null;
        }

        if (!await IsAuthorizedAsync(theme, cancellationToken))
        {
            return null;
        }

        return UiThemeMapper.ToDetails(theme);
    }

    private async Task<bool> IsAuthorizedAsync(Explore.Domain.UiTheme theme, CancellationToken cancellationToken)
    {
        if (!theme.TenantId.HasValue)
        {
            return await _adminContext.IsInstanceAdminAsync(cancellationToken);
        }

        return await _adminContext.IsTenantAdminAsync(theme.TenantId.Value, cancellationToken)
            || await _adminContext.IsInstanceAdminAsync(cancellationToken);
    }
}
