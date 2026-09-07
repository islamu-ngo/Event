using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Features.Tenants.Requests.Commands.UpdateTenantNavLink;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Commands.UpdateTenantNavLink;

/// <summary>
/// Handler for UpdateTenantNavLinkCommand.
/// Updates an existing navigation link for the current tenant.
/// Verifies the link belongs to the tenant before updating.
/// </summary>
public class UpdateTenantNavLinkCommandHandler : IRequestHandler<UpdateTenantNavLinkCommand, BaseCommandResponse<bool>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IHierarchicalSettingsResolver _settingsResolver;

    public UpdateTenantNavLinkCommandHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext,
        IHierarchicalSettingsResolver settingsResolver)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
        _settingsResolver = settingsResolver;
    }

    public async Task<BaseCommandResponse<bool>> Handle(UpdateTenantNavLinkCommand request, CancellationToken cancellationToken)
    {
        // Validate the DTO
        bool requireHttps = await _settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Security.RequireHttpsExternalUrls,
            new SettingContext(),
            cancellationToken);
        var validator = new UpdateTenantNavigationLinkDtoValidator(requireHttps);
        var validationResult = await validator.ValidateAsync(request.Update, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<bool>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Validation failed.");
        }

        if (request.TenantId != _tenantContext.TenantId)
        {
            return BaseCommandResponse.NotFound<bool>(
                "Navigation link not found or does not belong to your tenant.");
        }

        var existingLink = await _navigationLinkRepository.GetByIdAndTenantAsync(
            request.NavigationLinkId,
            _tenantContext.TenantId,
            cancellationToken);

        if (existingLink == null)
        {
            return BaseCommandResponse.NotFound<bool>(
                "Navigation link not found or does not belong to your tenant.");
        }

        if (request.Update.Label is not null)
            existingLink.Label = request.Update.Label.Value.Trim();

        if (request.Update.Url is not null)
            existingLink.Url = request.Update.Url.Value.Trim();

        if (request.Update.Icon is { Value.HasValue: true })
            existingLink.Icon = string.IsNullOrWhiteSpace(request.Update.Icon.Value.Value)
                ? null
                : request.Update.Icon.Value.Value.Trim();

        if (request.Update.OpenInNewTab?.Value is bool openInNewTab)
            existingLink.OpenInNewTab = openInNewTab;

        // Update the entity
        await _navigationLinkRepository.Update(existingLink);

        return BaseCommandResponse.Success(false, "Navigation link updated successfully.");
    }
}
