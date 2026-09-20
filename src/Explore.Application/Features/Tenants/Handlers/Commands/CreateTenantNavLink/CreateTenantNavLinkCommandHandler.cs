using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Features.Tenants.Requests.Commands.CreateTenantNavLink;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Features.Tenants.Handlers.Commands.CreateTenantNavLink;

/// <summary>
/// Handler for CreateTenantNavLinkCommand.
/// Creates a new navigation link for the current tenant.
/// Automatically assigns the next order value.
/// </summary>
public class CreateTenantNavLinkCommandHandler : ICommandHandler<CreateTenantNavLinkCommand, BaseCommandResponse<Guid>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IHierarchicalSettingsResolver _settingsResolver;

    public CreateTenantNavLinkCommandHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext,
        IHierarchicalSettingsResolver settingsResolver)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
        _settingsResolver = settingsResolver;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateTenantNavLinkCommand request, CancellationToken cancellationToken = default)
    {
        // Validate the DTO
        bool requireHttps = await _settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Security.RequireHttpsExternalUrls,
            new SettingContext(),
            cancellationToken);
        var validator = new CreateTenantNavigationLinkDtoValidator(requireHttps);
        var validationResult = await validator.ValidateAsync(request.NavigationLinkDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Validation failed.");
        }

        var navigationLink = new TenantNavigationLink
        {
            Label = request.NavigationLinkDto.Label,
            Url = request.NavigationLinkDto.Url,
            Icon = request.NavigationLinkDto.Icon,
            OpenInNewTab = request.NavigationLinkDto.OpenInNewTab
        };

        // Set tenant ID from context
        navigationLink.TenantId = _tenantContext.TenantId;

        // Normalize: trim values, blank icon → null
        navigationLink.Label = navigationLink.Label.Trim();
        navigationLink.Url = navigationLink.Url.Trim();
        navigationLink.Icon = string.IsNullOrWhiteSpace(navigationLink.Icon) ? null : navigationLink.Icon.Trim();

        // Get the next order value
        var maxOrder = await _navigationLinkRepository.GetMaxOrderByTenantIdAsync(
            _tenantContext.TenantId,
            cancellationToken);
        navigationLink.Order = maxOrder + 1;

        // Set default active state
        navigationLink.IsActive = true;

        // Create the navigation link
        navigationLink = await _navigationLinkRepository.Create(navigationLink);

        return BaseCommandResponse.Success(navigationLink.Id, "Navigation link created successfully.");
    }
}
