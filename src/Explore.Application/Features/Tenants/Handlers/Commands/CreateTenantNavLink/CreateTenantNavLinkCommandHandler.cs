using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Features.Tenants.Requests.Commands.CreateTenantNavLink;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Commands.CreateTenantNavLink;

/// <summary>
/// Handler for CreateTenantNavLinkCommand.
/// Creates a new navigation link for the current tenant.
/// Automatically assigns the next order value.
/// </summary>
public class CreateTenantNavLinkCommandHandler : IRequestHandler<CreateTenantNavLinkCommand, BaseCommandResponse<Guid>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly IMapper _mapper;

    public CreateTenantNavLinkCommandHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext,
        IHierarchicalSettingsResolver settingsResolver,
        IMapper mapper)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
        _settingsResolver = settingsResolver;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateTenantNavLinkCommand request, CancellationToken cancellationToken)
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

        // Map DTO to entity
        var navigationLink = _mapper.Map<TenantNavigationLink>(request.NavigationLinkDto);

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
