namespace Explore.Application.Features.Appearance.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Appearance.Validators;
using Explore.Application.Features.Appearance.Common;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

public class UpdateUiThemeCommandHandler : IRequestHandler<UpdateUiThemeCommand, BaseCommandResponse<Guid>>
{
    private readonly IUiThemeRepository _uiThemeRepository;
    private readonly IAdminContext _adminContext;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateUiThemeCommandHandler(
        IUiThemeRepository uiThemeRepository,
        IAdminContext adminContext,
        IUnitOfWork unitOfWork)
    {
        _uiThemeRepository = uiThemeRepository;
        _adminContext = adminContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateUiThemeCommand request, CancellationToken cancellationToken)
    {
        var theme = await _uiThemeRepository.GetById(request.Id);

        if (theme is null)
        {
            return BaseCommandResponse.NotFound<Guid>("UI theme not found.");
        }

        if (!await IsAuthorizedAsync(theme, cancellationToken))
        {
            return BaseCommandResponse.Authorization<Guid>(
                theme.TenantId.HasValue
                    ? "Only tenant administrators or instance administrators can manage this tenant theme."
                    : "Only instance administrators can manage platform themes.");
        }

        var validator = new UpdateUiThemeDtoValidator(_uiThemeRepository, theme);
        var validationResult = await validator.ValidateAsync(request.UiThemeDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "UI theme update failed.");
        }

        if (request.UiThemeDto.RowVersion != theme.RowVersion)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["The theme has changed since it was loaded. Refresh and try again."],
                "UI theme update failed because the theme was modified by another administrator.");
        }

        var willBeDefault = request.UiThemeDto.State?.IsDefault ?? theme.IsDefault;
        if (theme.IsDefault && !willBeDefault)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Select another default theme before unsetting the current default."],
                "A default theme cannot be unset directly.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (request.UiThemeDto.State?.IsDefault == true)
            {
                await _uiThemeRepository.ClearDefaultAsync(theme.TenantId, theme.Id);
            }

            UiThemeMapper.Apply(request.UiThemeDto, theme);
            await _uiThemeRepository.Update(theme);
        }, cancellationToken);

        return BaseCommandResponse.Success(theme.Id, "UI theme updated successfully.");
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
