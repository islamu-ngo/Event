namespace Explore.Application.Features.Appearance.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Appearance.Validators;
using Explore.Application.Features.Appearance.Common;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

public class CreateUiThemeCommandHandler : IRequestHandler<CreateUiThemeCommand, BaseCommandResponse<Guid>>
{
    private readonly IUiThemeRepository _uiThemeRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public CreateUiThemeCommandHandler(
        IUiThemeRepository uiThemeRepository,
        ITenantContext tenantContext,
        IAdminContext adminContext,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        _uiThemeRepository = uiThemeRepository;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateUiThemeCommand request, CancellationToken cancellationToken)
    {
        Guid? ownerTenantId = request.UiThemeDto.IsPlatformTheme ? null : _tenantContext.TenantId;

        if (!await IsAuthorizedAsync(request.UiThemeDto.IsPlatformTheme, ownerTenantId, cancellationToken))
        {
            string message = request.UiThemeDto.IsPlatformTheme
                ? "Only instance administrators can manage platform themes."
                : "Only tenant administrators or instance administrators can manage tenant themes.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var validator = new CreateUiThemeDtoValidator(_uiThemeRepository, ownerTenantId);
        var validationResult = await validator.ValidateAsync(request.UiThemeDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "UI theme creation failed.");
        }

        var theme = UiThemeMapper.CreateEntity(request.UiThemeDto, ownerTenantId);

        theme = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (theme.IsDefault)
            {
                await _uiThemeRepository.ClearDefaultAsync(ownerTenantId, null);
            }

            return await _uiThemeRepository.Create(theme);
        }, cancellationToken);

        return BaseCommandResponse.Success(theme.Id, "UI theme created successfully.");
    }

    private async Task<bool> IsAuthorizedAsync(bool isPlatformTheme, Guid? ownerTenantId, CancellationToken cancellationToken)
    {
        if (isPlatformTheme)
        {
            return await _adminContext.IsInstanceAdminAsync(cancellationToken);
        }

        return await _adminContext.IsTenantAdminAsync(ownerTenantId!.Value, cancellationToken)
            || await _adminContext.IsInstanceAdminAsync(cancellationToken);
    }
}
