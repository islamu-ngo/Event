namespace Explore.Application.Features.Appearance.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Appearance.Requests.Commands;
using MediatR;

public class DeleteUiThemeCommandHandler : IRequestHandler<DeleteUiThemeCommand, bool>
{
    private readonly IUiThemeRepository _uiThemeRepository;
    private readonly IAdminContext _adminContext;

    public DeleteUiThemeCommandHandler(
        IUiThemeRepository uiThemeRepository,
        IAdminContext adminContext)
    {
        _uiThemeRepository = uiThemeRepository;
        _adminContext = adminContext;
    }

    public async Task<bool> Handle(DeleteUiThemeCommand request, CancellationToken cancellationToken)
    {
        var theme = await _uiThemeRepository.GetById(request.Id);
        if (theme is null)
        {
            return false;
        }

        if (!await IsAuthorizedAsync(theme, cancellationToken))
        {
            throw new AuthorizationException(theme.TenantId.HasValue
                ? "Only tenant administrators or instance administrators can manage this tenant theme."
                : "Only instance administrators can manage platform themes.");
        }

        if (theme.IsDefault)
        {
            throw new BadRequestException(
                "A default theme cannot be deleted. Promote another theme to default before deleting this one.");
        }

        await _uiThemeRepository.Delete(theme);
        return true;
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
