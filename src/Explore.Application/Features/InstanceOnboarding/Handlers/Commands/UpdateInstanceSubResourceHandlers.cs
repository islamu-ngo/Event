using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class UpdateModuleSettingsCommandHandler : IRequestHandler<UpdateModuleSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IDeploymentModeProvider _deploymentModeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateModuleSettingsCommandHandler(
        IAdminContext adminContext,
        IInstanceGovernanceSettingService service,
        IDeploymentModeProvider deploymentModeProvider,
        IUnitOfWork unitOfWork)
    {
        _adminContext = adminContext;
        _service = service;
        _deploymentModeProvider = deploymentModeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateModuleSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one module setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.EnableIslamicModule.HasValue)
            settings.Modules.EnableIslamicModule = request.Patch.EnableIslamicModule.Value;
        if (request.Patch.EnableTechModule.HasValue)
            settings.Modules.EnableTechModule = request.Patch.EnableTechModule.Value;

        Guid? defaultTenantId = await _deploymentModeProvider.IsSingleTenantAsync(cancellationToken)
            ? PlatformDefaults.DefaultTenantId
            : null;
        await _unitOfWork.ExecuteInTransactionAsync(ct =>
            _service.ApplyModuleSettingsPatchAsync(defaultTenantId, request.Patch, settings.Modules, request.UserId, ct), cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Module settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateEventPolicyCommandHandler : IRequestHandler<UpdateEventPolicyCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public UpdateEventPolicyCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork, IMediator mediator)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one event policy setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.AllowUserSubmittedEvents.HasValue)
            settings.EventPolicy.AllowUserSubmittedEvents = request.Patch.AllowUserSubmittedEvents.Value;
        if (request.Patch.AllowOrganizationSubmittedEvents.HasValue)
            settings.EventPolicy.AllowOrganizationSubmittedEvents = request.Patch.AllowOrganizationSubmittedEvents.Value;
        if (request.Patch.AllowGroupSubmittedEvents.HasValue)
            settings.EventPolicy.AllowGroupSubmittedEvents = request.Patch.AllowGroupSubmittedEvents.Value;
        if (request.Patch.EventCardClickOpensDetailPage.HasValue)
            settings.EventPolicy.EventCardClickOpensDetailPage = request.Patch.EventCardClickOpensDetailPage.Value;
        if (request.Patch.LockTenantEventCardClickBehavior.HasValue)
            settings.EventPolicy.LockTenantEventCardClickBehavior = request.Patch.LockTenantEventCardClickBehavior.Value;

        PublicationPolicyMutationResult result = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _service.ApplyEventPolicyPatchAsync(request.Patch, settings.EventPolicy, request.UserId, ct),
            cancellationToken);
        if (!result.Success)
        {
            string failureCode = string.IsNullOrWhiteSpace(result.FailureCode)
                ? "event_reporting_intake_policy_invalid"
                : result.FailureCode;
            string failureMessage = string.IsNullOrWhiteSpace(result.Message)
                ? PublicationPolicyMutationMessages.InvalidPolicy
                : result.Message;
            return BaseCommandResponse.Failure<Guid>(
                failureCode,
                failureMessage,
                [failureCode]);
        }

        foreach (SettingChangedNotification notification in result.DeferredNotifications)
            await _mediator.Publish(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Event policy updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateOrganizationPolicyCommandHandler : IRequestHandler<UpdateOrganizationPolicyCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateOrganizationPolicyCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateOrganizationPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one organization policy setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.RequireOrganizationVerification.HasValue)
            settings.OrganizationPolicy.RequireOrganizationVerification = request.Patch.RequireOrganizationVerification.Value;
        if (request.Patch.AllowTenantToOmitVerification.HasValue)
            settings.OrganizationPolicy.AllowTenantToOmitVerification = request.Patch.AllowTenantToOmitVerification.Value;
        if (request.Patch.AllowOrganizationSelfRegistration.HasValue)
            settings.OrganizationPolicy.AllowOrganizationSelfRegistration = request.Patch.AllowOrganizationSelfRegistration.Value;
        if (request.Patch.AllowGroupSelfRegistration.HasValue)
            settings.OrganizationPolicy.AllowGroupSelfRegistration = request.Patch.AllowGroupSelfRegistration.Value;

        await _unitOfWork.ExecuteInTransactionAsync(ct =>
            _service.ApplyOrganizationPolicyPatchAsync(request.Patch, settings.OrganizationPolicy, request.UserId), cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Organization policy updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateBrandingSettingsCommandHandler : IRequestHandler<UpdateBrandingSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IDeploymentModeProvider _deploymentModeProvider;
    private readonly ITenantBrandingSettingsDocumentProvisioningService _tenantBrandingProvisioningService;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IMediator _mediator;

    public UpdateBrandingSettingsCommandHandler(
        IAdminContext adminContext,
        IInstanceGovernanceSettingService service,
        IDeploymentModeProvider deploymentModeProvider,
        ITenantBrandingSettingsDocumentProvisioningService tenantBrandingProvisioningService,
        ISettingMutationLock mutationLock,
        IMediator mediator)
    {
        _adminContext = adminContext;
        _service = service;
        _deploymentModeProvider = deploymentModeProvider;
        _tenantBrandingProvisioningService = tenantBrandingProvisioningService;
        _mutationLock = mutationLock;
        _mediator = mediator;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateBrandingSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one branding setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.DefaultBrandDisplayName.HasValue)
            settings.Branding.DefaultBrandDisplayName = request.Patch.DefaultBrandDisplayName.Value ?? string.Empty;
        if (request.Patch.DefaultBrandLogoUrl.HasValue)
            settings.Branding.DefaultBrandLogoUrl = request.Patch.DefaultBrandLogoUrl.Value ?? string.Empty;
        if (request.Patch.DefaultBrandFaviconUrl.HasValue)
            settings.Branding.DefaultBrandFaviconUrl = request.Patch.DefaultBrandFaviconUrl.Value ?? string.Empty;
        if (request.Patch.DefaultBrandCustomCssUrl.HasValue)
            settings.Branding.DefaultBrandCustomCssUrl = request.Patch.DefaultBrandCustomCssUrl.Value ?? string.Empty;
        if (request.Patch.LockTenantBrandDisplayName.HasValue)
            settings.Branding.LockTenantBrandDisplayName = request.Patch.LockTenantBrandDisplayName.Value;
        if (request.Patch.LockTenantBrandLogoUrl.HasValue)
            settings.Branding.LockTenantBrandLogoUrl = request.Patch.LockTenantBrandLogoUrl.Value;
        if (request.Patch.LockTenantBrandFaviconUrl.HasValue)
            settings.Branding.LockTenantBrandFaviconUrl = request.Patch.LockTenantBrandFaviconUrl.Value;
        if (request.Patch.LockTenantBrandCustomCssUrl.HasValue)
            settings.Branding.LockTenantBrandCustomCssUrl = request.Patch.LockTenantBrandCustomCssUrl.Value;

        IReadOnlyList<SettingChangedNotification> notifications = [];
        await _mutationLock.ExecuteManyAsync(
            TenantBrandingGovernanceMutationLockKeys.All,
            async ct =>
            {
                notifications = await _service.ApplyBrandingSettingsPatchAsync(
                    request.Patch,
                    settings.Branding,
                    request.UserId,
                    ct);

                if (request.Patch.DefaultBrandDisplayName.HasValue
                    && await _deploymentModeProvider.IsSingleTenantAsync(ct))
                {
                    await _tenantBrandingProvisioningService
                        .EnsureTenantBrandingDocumentAsync(
                            PlatformDefaults.DefaultTenantId,
                            settings.Branding.DefaultBrandDisplayName,
                            ct);
                }

                return true;
            },
            cancellationToken);
        foreach (SettingChangedNotification notification in notifications)
            await _mediator.Publish(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Branding settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateDomainSettingsCommandHandler : IRequestHandler<UpdateDomainSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public UpdateDomainSettingsCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork, IMediator mediator)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateDomainSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one domain setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.InstanceBaseDomain.HasValue)
            settings.Domains.InstanceBaseDomain = request.Patch.InstanceBaseDomain.Value ?? string.Empty;
        if (request.Patch.AdminHost.HasValue)
            settings.Domains.AdminHost = request.Patch.AdminHost.Value ?? string.Empty;
        if (request.Patch.AllowTenantCustomDomains.HasValue)
            settings.Domains.AllowTenantCustomDomains = request.Patch.AllowTenantCustomDomains.Value;
        if (request.Patch.LockTenantSubdomain.HasValue)
            settings.Domains.LockTenantSubdomain = request.Patch.LockTenantSubdomain.Value;
        if (request.Patch.LockTenantCustomDomain.HasValue)
            settings.Domains.LockTenantCustomDomain = request.Patch.LockTenantCustomDomain.Value;

        IReadOnlyList<SettingChangedNotification> notifications = [];
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            notifications = await _service.ApplyDomainSettingsPatchAsync(request.Patch, settings.Domains, request.UserId, ct);
        }, cancellationToken);
        foreach (SettingChangedNotification notification in notifications)
            await _mediator.Publish(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Domain settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateTenantDelegationSettingsCommandHandler : IRequestHandler<UpdateTenantDelegationSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public UpdateTenantDelegationSettingsCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork, IMediator mediator)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateTenantDelegationSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one tenant delegation setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.AllowTenantSelfServiceRegistration.HasValue)
            settings.TenantDelegation.AllowTenantSelfServiceRegistration = request.Patch.AllowTenantSelfServiceRegistration.Value;
        if (request.Patch.AllowTenantWhiteLabeling.HasValue)
            settings.TenantDelegation.AllowTenantWhiteLabeling = request.Patch.AllowTenantWhiteLabeling.Value;
        if (request.Patch.DefaultPublicHomePage.HasValue)
            settings.TenantDelegation.DefaultPublicHomePage = request.Patch.DefaultPublicHomePage.Value ?? string.Empty;
        if (request.Patch.LockTenantHomePagePreference.HasValue)
            settings.TenantDelegation.LockTenantHomePagePreference = request.Patch.LockTenantHomePagePreference.Value;
        if (request.Patch.LockTenantSmtp.HasValue)
            settings.TenantDelegation.LockTenantSmtp = request.Patch.LockTenantSmtp.Value;
        if (request.Patch.LockTenantStorage.HasValue)
            settings.TenantDelegation.LockTenantStorage = request.Patch.LockTenantStorage.Value;
        if (request.Patch.LockTenantAnalytics.HasValue)
            settings.TenantDelegation.LockTenantAnalytics = request.Patch.LockTenantAnalytics.Value;
        if (request.Patch.LockTenantAiAssistant.HasValue)
            settings.TenantDelegation.LockTenantAiAssistant = request.Patch.LockTenantAiAssistant.Value;

        IReadOnlyList<SettingChangedNotification> notifications = [];
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            notifications = await _service.ApplyTenantDelegationSettingsPatchAsync(
                settings.DeploymentMode.Mode == DeploymentMode.MultiTenant,
                request.Patch,
                settings.TenantDelegation,
                request.UserId,
                ct);
        }, cancellationToken);
        foreach (SettingChangedNotification notification in notifications)
            await _mediator.Publish(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Tenant delegation settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateAdminPortalSettingsCommandHandler : IRequestHandler<UpdateAdminPortalSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateAdminPortalSettingsCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateAdminPortalSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one admin portal setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.Enabled.HasValue)
            settings.AdminPortal.Enabled = request.Patch.Enabled.Value;
        if (request.Patch.PublicUrl.HasValue)
            settings.AdminPortal.PublicUrl = request.Patch.PublicUrl.Value ?? string.Empty;
        if (request.Patch.AllowTenantAdminAccess.HasValue)
            settings.AdminPortal.AllowTenantAdminAccess = request.Patch.AllowTenantAdminAccess.Value;

        await _unitOfWork.ExecuteInTransactionAsync(ct =>
            _service.ApplyAdminPortalSettingsPatchAsync(request.Patch, settings.AdminPortal, request.UserId), cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Admin portal settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateMcpGovernanceSettingsCommandHandler : IRequestHandler<UpdateMcpGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public UpdateMcpGovernanceSettingsCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork, IMediator mediator)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateMcpGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one MCP governance setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.Enabled.HasValue)
            settings.Mcp.Enabled = request.Patch.Enabled.Value;
        if (request.Patch.EnableLegacySse.HasValue)
            settings.Mcp.EnableLegacySse = request.Patch.EnableLegacySse.Value;
        if (request.Patch.LockTenantMcp.HasValue)
            settings.Mcp.LockTenantMcp = request.Patch.LockTenantMcp.Value;
        if (request.Patch.LockTenantMcpLegacySse.HasValue)
            settings.Mcp.LockTenantMcpLegacySse = request.Patch.LockTenantMcpLegacySse.Value;

        IReadOnlyList<SettingChangedNotification> notifications = [];
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            notifications = await _service.ApplyMcpGovernanceSettingsPatchAsync(request.Patch, settings.Mcp, request.UserId, ct);
        }, cancellationToken);
        foreach (SettingChangedNotification notification in notifications)
            await _mediator.Publish(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "MCP governance settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateAiAssistantGovernanceSettingsCommandHandler : IRequestHandler<UpdateAiAssistantGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;

    public UpdateAiAssistantGovernanceSettingsCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork, IMediator mediator)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateAiAssistantGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one AI Assistant governance setting must be provided.");
        if (request.Patch.ProviderConfiguration is { HasValue: true, Value: null })
            return Invalid("AI provider configuration must be complete.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.Enabled.HasValue)
            settings.AiAssistant.Enabled = request.Patch.Enabled.Value;
        if (request.Patch.ProviderConfiguration.HasValue)
        {
            var provider = request.Patch.ProviderConfiguration.Value!;
            settings.AiAssistant.Provider = provider.Provider;
            settings.AiAssistant.EndpointUrl = provider.EndpointUrl;
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                settings.AiAssistant.ApiKey = provider.ApiKey;
            settings.AiAssistant.ModelId = provider.ModelId;
            settings.AiAssistant.AllowedModelIds = provider.AllowedModelIds;
        }
        if (request.Patch.AllowAnonymousAccess.HasValue)
            settings.AiAssistant.AllowAnonymousAccess = request.Patch.AllowAnonymousAccess.Value;
        if (request.Patch.ToolProposalsEnabled.HasValue)
            settings.AiAssistant.ToolProposalsEnabled = request.Patch.ToolProposalsEnabled.Value;
        if (request.Patch.LockTenantAiAssistant.HasValue)
            settings.AiAssistant.LockTenantAiAssistant = request.Patch.LockTenantAiAssistant.Value;

        IReadOnlyList<SettingChangedNotification> notifications = [];
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            notifications = await _service.ApplyAiAssistantGovernanceSettingsPatchAsync(request.Patch, settings.AiAssistant, request.UserId, ct);
        }, cancellationToken);
        foreach (SettingChangedNotification notification in notifications)
            await _mediator.Publish(notification, cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "AI Assistant governance settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}

public class UpdateRenderPolicySettingsCommandHandler : IRequestHandler<UpdateRenderPolicySettingsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IInstanceGovernanceSettingService _service;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateRenderPolicySettingsCommandHandler(IAdminContext adminContext, IInstanceGovernanceSettingService service, IUnitOfWork unitOfWork)
    {
        _adminContext = adminContext;
        _service = service;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRenderPolicySettingsCommand request, CancellationToken cancellationToken)
    {
        if (!await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken))
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        if (!request.Patch.HasChanges())
            return Invalid("At least one render policy setting must be provided.");

        var settings = await _service.ReadSettingsAsync();
        if (request.Patch.RenderPolicyPreset.HasValue)
            settings.RenderPolicy.RenderPolicyPreset = request.Patch.RenderPolicyPreset.Value ?? string.Empty;
        if (request.Patch.EnableAdvancedRenderPolicyOverrides.HasValue)
            settings.RenderPolicy.EnableAdvancedRenderPolicyOverrides = request.Patch.EnableAdvancedRenderPolicyOverrides.Value;
        if (request.Patch.GlobalRenderMode.HasValue)
            settings.RenderPolicy.GlobalRenderMode = request.Patch.GlobalRenderMode.Value ?? string.Empty;
        if (request.Patch.GlobalPrerenderEnabled.HasValue)
            settings.RenderPolicy.GlobalPrerenderEnabled = request.Patch.GlobalPrerenderEnabled.Value;
        if (request.Patch.PublicSeoRenderMode.HasValue)
            settings.RenderPolicy.PublicSeoRenderMode = request.Patch.PublicSeoRenderMode.Value ?? string.Empty;
        if (request.Patch.PublicSeoPrerenderEnabled.HasValue)
            settings.RenderPolicy.PublicSeoPrerenderEnabled = request.Patch.PublicSeoPrerenderEnabled.Value;
        if (request.Patch.OperationalRenderMode.HasValue)
            settings.RenderPolicy.OperationalRenderMode = request.Patch.OperationalRenderMode.Value ?? string.Empty;
        if (request.Patch.OperationalPrerenderEnabled.HasValue)
            settings.RenderPolicy.OperationalPrerenderEnabled = request.Patch.OperationalPrerenderEnabled.Value;
        if (request.Patch.AdminRenderMode.HasValue)
            settings.RenderPolicy.AdminRenderMode = request.Patch.AdminRenderMode.Value ?? string.Empty;
        if (request.Patch.AdminPrerenderEnabled.HasValue)
            settings.RenderPolicy.AdminPrerenderEnabled = request.Patch.AdminPrerenderEnabled.Value;
        if (request.Patch.OnboardingRenderMode.HasValue)
            settings.RenderPolicy.OnboardingRenderMode = request.Patch.OnboardingRenderMode.Value ?? string.Empty;
        if (request.Patch.OnboardingPrerenderEnabled.HasValue)
            settings.RenderPolicy.OnboardingPrerenderEnabled = request.Patch.OnboardingPrerenderEnabled.Value;
        if (request.Patch.AllowTenantRenderPolicyOverride.HasValue)
            settings.RenderPolicy.AllowTenantRenderPolicyOverride = request.Patch.AllowTenantRenderPolicyOverride.Value;
        if (request.Patch.LockTenantPublicSeoRenderPolicy.HasValue)
            settings.RenderPolicy.LockTenantPublicSeoRenderPolicy = request.Patch.LockTenantPublicSeoRenderPolicy.Value;
        if (request.Patch.LockTenantOperationalRenderPolicy.HasValue)
            settings.RenderPolicy.LockTenantOperationalRenderPolicy = request.Patch.LockTenantOperationalRenderPolicy.Value;
        if (request.Patch.LockTenantAdminRenderPolicy.HasValue)
            settings.RenderPolicy.LockTenantAdminRenderPolicy = request.Patch.LockTenantAdminRenderPolicy.Value;

        var validator = new RenderPolicySettingsDtoValidator();
        var validation = await validator.ValidateAsync(settings.RenderPolicy, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(e => e.ErrorMessage),
                "Invalid render policy settings.");
        }

        await _unitOfWork.ExecuteInTransactionAsync(ct =>
            _service.ApplyRenderPolicySettingsPatchAsync(request.Patch, settings.RenderPolicy, request.UserId), cancellationToken);
        return BaseCommandResponse.Success(Guid.Empty, "Render policy settings updated successfully.");
    }

    private static BaseCommandResponse<Guid> Invalid(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}
