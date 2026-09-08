// ABOUTME: Thin orchestrator for instance-level governance settings using typed setting groups.
// ABOUTME: Reads resolved settings and routes SMTP governance through the atomic email settings writer.

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public class InstanceGovernanceSettingService : IInstanceGovernanceSettingService
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly SettingUpsertService _upsertService;
    private readonly IModuleCapabilityService _moduleCapabilityService;
    private readonly ILogger<InstanceGovernanceSettingService> _logger;
    private readonly IEmailDeliverySettingsWriter _emailDeliverySettingsWriter;

    public InstanceGovernanceSettingService(
        IHierarchicalSettingsResolver resolver,
        SettingUpsertService upsertService,
        IModuleCapabilityService moduleCapabilityService,
        ILogger<InstanceGovernanceSettingService> logger,
        IEmailDeliverySettingsWriter emailDeliverySettingsWriter)
    {
        _resolver = resolver;
        _upsertService = upsertService;
        _moduleCapabilityService = moduleCapabilityService;
        _logger = logger;
        _emailDeliverySettingsWriter = emailDeliverySettingsWriter;
    }

    public async Task<InstanceGovernanceSettings> ReadSettingsAsync()
    {
        var context = new SettingContext();
        var resolved = await ResolveBatchForAllCategories(context);

        _logger.LogDebug("Resolved {Count} governance settings at instance scope", resolved.Count);

        var deployment = PopulateGroup<DeploymentSettingGroup>(resolved);
        var modules = PopulateGroup<ModuleSettingGroup>(resolved);
        var events = PopulateGroup<EventSettingGroup>(resolved);
        var orgs = PopulateGroup<OrganizationSettingGroup>(resolved);
        var groups = PopulateGroup<GroupSettingGroup>(resolved);
        var branding = PopulateGroup<BrandingSettingGroup>(resolved);
        var domains = PopulateGroup<DomainSettingGroup>(resolved);
        var delegation = PopulateGroup<TenantDelegationSettingGroup>(resolved);
        var adminPortal = PopulateGroup<AdminPortalSettingGroup>(resolved);
        var aiAssistant = PopulateGroup<AiAssistantSettingGroup>(resolved);
        var mcp = PopulateGroup<McpSettingGroup>(resolved);
        var renderPolicy = PopulateGroup<RenderPolicySettingGroup>(resolved);
        var routing = PopulateGroup<RoutingSettingGroup>(resolved);

        var isMultiTenant = deployment.Mode.Equals("MultiTenant", StringComparison.OrdinalIgnoreCase);

        var rpDto = MapRenderPolicyDto(renderPolicy, resolved);
        NormalizeRenderPolicySettings(rpDto);

        return new InstanceGovernanceSettings
        {
            DeploymentMode = new DeploymentModeDto
            {
                Mode = Enum.TryParse<DeploymentMode>(deployment.Mode, ignoreCase: true, out var dm)
                    ? dm : DeploymentMode.SingleTenant
            },
            Modules = new ModuleSettingsDto
            {
                EnableIslamicModule = modules.IslamicEnabled,
                EnableTechModule = modules.TechEnabled
            },
            EventPolicy = new EventPolicyDto
            {
                AllowUserSubmittedEvents = events.UserSubmissionEnabled,
                AllowOrganizationSubmittedEvents = events.OrganizationSubmissionEnabled,
                AllowGroupSubmittedEvents = events.GroupSubmissionEnabled,
                EventCardClickOpensDetailPage = events.CardClickOpensDetailPage,
                LockTenantEventCardClickBehavior = IsLocked(resolved, GovernanceSettingKeys.Events.CardClickOpensDetailPage)
            },
            OrganizationPolicy = new OrganizationPolicyDto
            {
                RequireOrganizationVerification = orgs.VerificationRequired,
                AllowTenantToOmitVerification = orgs.TenantCanOmitVerification,
                AllowOrganizationSelfRegistration = orgs.SelfRegistrationEnabled,
                AllowGroupSelfRegistration = groups.SelfRegistrationEnabled
            },
            Branding = new BrandingSettingsDto
            {
                DefaultBrandDisplayName = branding.DisplayName,
                SupportEmail = branding.SupportEmail,
                DefaultBrandLogoUrl = branding.LogoUrl ?? string.Empty,
                DefaultBrandFaviconUrl = branding.FaviconUrl ?? string.Empty,
                DefaultBrandCustomCssUrl = branding.CustomCssUrl ?? string.Empty,
                LockTenantBrandDisplayName = IsLocked(resolved, GovernanceSettingKeys.Branding.DisplayName),
                LockTenantBrandLogoUrl = IsLocked(resolved, GovernanceSettingKeys.Branding.LogoUrl),
                LockTenantBrandFaviconUrl = IsLocked(resolved, GovernanceSettingKeys.Branding.FaviconUrl),
                LockTenantBrandCustomCssUrl = IsLocked(resolved, GovernanceSettingKeys.Branding.CustomCssUrl)
            },
            Domains = new DomainSettingsDto
            {
                InstanceBaseDomain = domains.InstanceBaseDomain,
                AdminHost = domains.AdminHost,
                AllowTenantCustomDomains = domains.AllowTenantCustomDomain,
                LockTenantSubdomain = IsLocked(resolved, GovernanceSettingKeys.Domains.TenantSubdomain),
                LockTenantCustomDomain = IsLocked(resolved, GovernanceSettingKeys.Domains.TenantCustomDomain)
            },
            TenantDelegation = MapTenantDelegationDto(delegation, routing, resolved, isMultiTenant),
            AdminPortal = MapAdminPortalDto(adminPortal),
            AiAssistant = MapAiAssistantDto(aiAssistant, delegation),
            Mcp = MapMcpDto(mcp, delegation),
            RenderPolicy = rpDto,
            LocationPrivacy = MapLocationPrivacyDto(resolved)
        };
    }

    public async Task<InstanceGovernanceSettings> ReadEffectiveSettingsForTenantAsync(Guid tenantId)
    {
        var settings = await ReadSettingsAsync();

        if (!settings.RenderPolicy.AllowTenantRenderPolicyOverride)
            return settings;

        _logger.LogDebug("Resolving tenant render policy overrides");

        var tenantContext = new SettingContext(TenantId: tenantId);
        var tenantResolved = await _resolver.ResolveBatchAsync(RenderPolicySettingGroup.SettingKeys, tenantContext);
        var tenantLookup = tenantResolved.ToDictionary(r => r.Key, r => r);
        var tenantRp = PopulateGroup<RenderPolicySettingGroup>(tenantLookup);

        var rp = settings.RenderPolicy;

        rp.RenderPolicyPreset = NormalizeRenderPolicyPreset(tenantRp.Preset);
        rp.EnableAdvancedRenderPolicyOverrides = tenantRp.AdvancedEnabled;
        rp.GlobalRenderMode = NormalizeRenderMode(tenantRp.FallbackRenderMode);
        rp.GlobalPrerenderEnabled = tenantRp.FallbackPrerenderEnabled;

        NormalizeRenderPolicySettings(rp);

        if (!rp.LockTenantPublicSeoRenderPolicy)
        {
            rp.PublicSeoRenderMode = NormalizeRenderMode(tenantRp.PublicSeoRenderMode);
            rp.PublicSeoPrerenderEnabled = tenantRp.PublicSeoPrerenderEnabled;
        }

        if (!rp.LockTenantOperationalRenderPolicy)
        {
            rp.OperationalRenderMode = NormalizeRenderMode(tenantRp.OperationalRenderMode);
            rp.OperationalPrerenderEnabled = tenantRp.OperationalPrerenderEnabled;
        }

        if (!rp.LockTenantAdminRenderPolicy)
        {
            rp.AdminRenderMode = NormalizeRenderMode(tenantRp.AdminRenderMode);
            rp.AdminPrerenderEnabled = tenantRp.AdminPrerenderEnabled;
        }

        return settings;
    }

    public async Task<InstanceGovernanceSettingApplyResult> ApplySettingsAsync(
        Guid? defaultTenantId,
        InstanceGovernanceSettings settings,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var locationPrivacyMutations = new List<LocationPrivacyGovernanceMutationResult>();
        var deferredNotifications = new List<SettingChangedNotification>();
        var isMultiTenant = settings.DeploymentMode.Mode == DeploymentMode.MultiTenant;

        deferredNotifications.AddRange(await ApplySmtpDelegationAsync(
            settings.TenantDelegation.LockTenantSmtp, actorUserId, cancellationToken));

        await _upsertService.UpsertValueAsync(GovernanceSettingKeys.Deployment.Mode, SettingValueSerializer.Serialize(settings.DeploymentMode.Mode.ToString()), isLocked: true, actorUserId, cancellationToken: cancellationToken);

        settings.TenantDelegation.LockTenantAiAssistant = settings.AiAssistant.LockTenantAiAssistant;

        await ApplyTenantDelegationSettingsInternalAsync(settings.TenantDelegation, isMultiTenant, actorUserId, cancellationToken);
        await ApplyAdminPortalSettingsAsync(settings.AdminPortal, actorUserId);
        await ApplyAiAssistantGovernanceSettingsAsync(settings.AiAssistant, actorUserId);
        await ApplyMcpGovernanceSettingsAsync(settings.Mcp, actorUserId);
        await ApplyRenderPolicySettingsInternalAsync(settings.RenderPolicy, actorUserId);
        await ApplyModuleSettingsAsync(defaultTenantId, settings.Modules, actorUserId, cancellationToken);
        PublicationPolicyMutationResult eventPolicyResult = await ApplyEventPolicyAsync(
            settings.EventPolicy,
            actorUserId,
            cancellationToken);
        if (!eventPolicyResult.Success)
        {
            string failureCode = string.IsNullOrWhiteSpace(eventPolicyResult.FailureCode)
                ? "event_reporting_intake_policy_invalid"
                : eventPolicyResult.FailureCode;
            throw new FluentValidation.ValidationException([
                new FluentValidation.Results.ValidationFailure(nameof(InstanceGovernanceSettings.EventPolicy), string.Empty)
                {
                    ErrorCode = failureCode
                }
            ]);
        }

        deferredNotifications.AddRange(eventPolicyResult.DeferredNotifications);
        await ApplyOrganizationPolicyAsync(settings.OrganizationPolicy, actorUserId);
        await ApplyBrandingSettingsAsync(settings.Branding, actorUserId);
        await ApplyDomainSettingsAsync(settings.Domains, actorUserId);
        if (settings.LocationPrivacy is not null)
        {
            await ApplyLocationPrivacySettingsAsync(
                settings.LocationPrivacy,
                actorUserId,
                locationPrivacyMutations,
                deferredNotifications);
        }

        return new(locationPrivacyMutations, deferredNotifications);
    }

    public async Task ApplyModuleSettingsAsync(
        Guid? defaultTenantId,
        ModuleSettingsDto modules,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Modules.IslamicEnabled,
            SettingValueSerializer.Serialize(modules.EnableIslamicModule),
            actorUserId,
            cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Modules.TechEnabled,
            SettingValueSerializer.Serialize(modules.EnableTechModule),
            actorUserId,
            cancellationToken);

        if (defaultTenantId.HasValue)
            await _moduleCapabilityService.SyncTenantModuleCapabilitiesAsync(
                defaultTenantId.Value,
                modules.EnableIslamicModule,
                modules.EnableTechModule,
                actorUserId,
                cancellationToken);
    }

    public async Task ApplyModuleSettingsPatchAsync(
        Guid? defaultTenantId,
        PatchModuleSettingsDto patch,
        ModuleSettingsDto modules,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.EnableIslamicModule.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Modules.IslamicEnabled,
                SettingValueSerializer.Serialize(modules.EnableIslamicModule),
                actorUserId,
                cancellationToken);
        }

        if (patch.EnableTechModule.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Modules.TechEnabled,
                SettingValueSerializer.Serialize(modules.EnableTechModule),
                actorUserId,
                cancellationToken);
        }

        if (defaultTenantId.HasValue && patch.HasChanges())
        {
            await _moduleCapabilityService.SyncTenantModuleCapabilityPatchAsync(
                defaultTenantId.Value,
                patch.EnableIslamicModule.HasValue ? modules.EnableIslamicModule : null,
                patch.EnableTechModule.HasValue ? modules.EnableTechModule : null,
                actorUserId,
                cancellationToken);
        }
    }

    public async Task<PublicationPolicyMutationResult> ApplyEventPolicyAsync(
        EventPolicyDto ep,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        DateTime occurredAtUtc = DateTime.UtcNow;
        PublicationPolicyMutationResult boundaryResult = await _upsertService.ApplyInstancePublicationPolicyAsync(
            new PublicationPolicyInstanceMutationRequest(
                actorUserId ?? Guid.Empty,
                occurredAtUtc,
                CreateSubmissionMutations(
                    ep.AllowUserSubmittedEvents,
                    ep.AllowOrganizationSubmittedEvents,
                    ep.AllowGroupSubmittedEvents)),
            cancellationToken);
        if (!boundaryResult.Success)
            return boundaryResult;

        DeferredSettingUpsertResult cardClickResult = await _upsertService.UpsertValueWithDeferredInvalidationAsync(
            GovernanceSettingKeys.Events.CardClickOpensDetailPage,
            SettingValueSerializer.Serialize(ep.EventCardClickOpensDetailPage),
            actorUserId,
            isLocked: ep.LockTenantEventCardClickBehavior,
            cancellationToken: cancellationToken);
        return SucceededEventPolicyResult(boundaryResult, [cardClickResult.Notification]);
    }

    public async Task<PublicationPolicyMutationResult> ApplyEventPolicyPatchAsync(
        PatchEventPolicyDto patch,
        EventPolicyDto eventPolicy,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var valuesByKey = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (patch.AllowUserSubmittedEvents.HasValue)
            valuesByKey[GovernanceSettingKeys.Events.UserSubmissionEnabled] = eventPolicy.AllowUserSubmittedEvents;
        if (patch.AllowOrganizationSubmittedEvents.HasValue)
            valuesByKey[GovernanceSettingKeys.Events.OrganizationSubmissionEnabled] = eventPolicy.AllowOrganizationSubmittedEvents;
        if (patch.AllowGroupSubmittedEvents.HasValue)
            valuesByKey[GovernanceSettingKeys.Events.GroupSubmissionEnabled] = eventPolicy.AllowGroupSubmittedEvents;

        PublicationPolicyMutationResult boundaryResult = new(true, null, string.Empty, []);
        if (valuesByKey.Count > 0)
        {
            DateTime occurredAtUtc = DateTime.UtcNow;
            boundaryResult = await _upsertService.ApplyInstancePublicationPolicyAsync(
                new PublicationPolicyInstanceMutationRequest(
                    actorUserId ?? Guid.Empty,
                    occurredAtUtc,
                    [.. PublicationPolicySettingKeys.All
                        .Where(valuesByKey.ContainsKey)
                        .Select(key => new PublicationPolicySettingMutation(
                            key,
                            PublicationPolicyMutationKind.Set,
                            SettingValueSerializer.Serialize(valuesByKey[key]),
                            TenantId: null,
                            IsLocked: false))]),
                cancellationToken);
            if (!boundaryResult.Success)
                return boundaryResult;
        }

        var unguardedNotifications = new List<SettingChangedNotification>();
        if (patch.EventCardClickOpensDetailPage.HasValue)
        {
            unguardedNotifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Events.CardClickOpensDetailPage,
                SettingValueSerializer.Serialize(eventPolicy.EventCardClickOpensDetailPage),
                actorUserId,
                isLocked: eventPolicy.LockTenantEventCardClickBehavior,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantEventCardClickBehavior.HasValue)
        {
            unguardedNotifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Events.CardClickOpensDetailPage,
                SettingValueSerializer.Serialize(eventPolicy.EventCardClickOpensDetailPage),
                eventPolicy.LockTenantEventCardClickBehavior,
                actorUserId,
                cancellationToken));
        }

        return SucceededEventPolicyResult(boundaryResult, unguardedNotifications);
    }

    private static ImmutableArray<PublicationPolicySettingMutation> CreateSubmissionMutations(
        bool allowUserSubmittedEvents,
        bool allowOrganizationSubmittedEvents,
        bool allowGroupSubmittedEvents)
    {
        var valuesByKey = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [GovernanceSettingKeys.Events.UserSubmissionEnabled] = allowUserSubmittedEvents,
            [GovernanceSettingKeys.Events.OrganizationSubmissionEnabled] = allowOrganizationSubmittedEvents,
            [GovernanceSettingKeys.Events.GroupSubmissionEnabled] = allowGroupSubmittedEvents
        };

        return [.. PublicationPolicySettingKeys.All
            .Where(valuesByKey.ContainsKey)
            .Select(key => new PublicationPolicySettingMutation(
                key,
                PublicationPolicyMutationKind.Set,
                SettingValueSerializer.Serialize(valuesByKey[key]),
                TenantId: null,
                IsLocked: false))];
    }

    private static PublicationPolicyMutationResult SucceededEventPolicyResult(
        PublicationPolicyMutationResult boundaryResult,
        IReadOnlyList<SettingChangedNotification> unguardedNotifications) =>
        new(true, null, boundaryResult.Message, [
            .. boundaryResult.DeferredNotifications,
            .. unguardedNotifications
        ]);

    public async Task ApplyOrganizationPolicyAsync(OrganizationPolicyDto op, Guid? actorUserId)
    {
        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Organizations.VerificationRequired,
            SettingValueSerializer.Serialize(op.RequireOrganizationVerification), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
            SettingValueSerializer.Serialize(op.AllowTenantToOmitVerification), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Organizations.SelfRegistrationEnabled,
            SettingValueSerializer.Serialize(op.AllowOrganizationSelfRegistration), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Groups.SelfRegistrationEnabled,
            SettingValueSerializer.Serialize(op.AllowGroupSelfRegistration), actorUserId);
    }

    public async Task ApplyOrganizationPolicyPatchAsync(
        PatchOrganizationPolicyDto patch,
        OrganizationPolicyDto organizationPolicy,
        Guid? actorUserId)
    {
        if (patch.RequireOrganizationVerification.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Organizations.VerificationRequired,
                SettingValueSerializer.Serialize(organizationPolicy.RequireOrganizationVerification),
                actorUserId);
        }

        if (patch.AllowTenantToOmitVerification.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
                SettingValueSerializer.Serialize(organizationPolicy.AllowTenantToOmitVerification),
                actorUserId);
        }

        if (patch.AllowOrganizationSelfRegistration.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Organizations.SelfRegistrationEnabled,
                SettingValueSerializer.Serialize(organizationPolicy.AllowOrganizationSelfRegistration),
                actorUserId);
        }

        if (patch.AllowGroupSelfRegistration.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Groups.SelfRegistrationEnabled,
                SettingValueSerializer.Serialize(organizationPolicy.AllowGroupSelfRegistration),
                actorUserId);
        }
    }

    public async Task ApplyBrandingSettingsAsync(BrandingSettingsDto b, Guid? actorUserId)
    {
        b.DefaultBrandDisplayName = NormalizeRequiredDisplayName(b.DefaultBrandDisplayName);
        b.DefaultBrandLogoUrl = NormalizeOptionalUrl(b.DefaultBrandLogoUrl);
        b.DefaultBrandFaviconUrl = NormalizeOptionalUrl(b.DefaultBrandFaviconUrl);
        b.DefaultBrandCustomCssUrl = NormalizeOptionalUrl(b.DefaultBrandCustomCssUrl);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Branding.DisplayName,
            SettingValueSerializer.Serialize(b.DefaultBrandDisplayName),
            isLocked: b.LockTenantBrandDisplayName, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Branding.LogoUrl,
            SettingValueSerializer.Serialize(b.DefaultBrandLogoUrl),
            isLocked: b.LockTenantBrandLogoUrl, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Branding.FaviconUrl,
            SettingValueSerializer.Serialize(b.DefaultBrandFaviconUrl),
            isLocked: b.LockTenantBrandFaviconUrl, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Branding.CustomCssUrl,
            SettingValueSerializer.Serialize(b.DefaultBrandCustomCssUrl),
            isLocked: b.LockTenantBrandCustomCssUrl, actorUserId);
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyBrandingSettingsPatchAsync(
        PatchBrandingSettingsDto patch,
        BrandingSettingsDto branding,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = new List<SettingChangedNotification>();
        branding.DefaultBrandDisplayName = NormalizeRequiredDisplayName(branding.DefaultBrandDisplayName);
        branding.DefaultBrandLogoUrl = NormalizeOptionalUrl(branding.DefaultBrandLogoUrl);
        branding.DefaultBrandFaviconUrl = NormalizeOptionalUrl(branding.DefaultBrandFaviconUrl);
        branding.DefaultBrandCustomCssUrl = NormalizeOptionalUrl(branding.DefaultBrandCustomCssUrl);

        if (patch.DefaultBrandDisplayName.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Branding.DisplayName,
                SettingValueSerializer.Serialize(branding.DefaultBrandDisplayName),
                actorUserId,
                isLocked: branding.LockTenantBrandDisplayName,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantBrandDisplayName.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Branding.DisplayName,
                SettingValueSerializer.Serialize(branding.DefaultBrandDisplayName),
                branding.LockTenantBrandDisplayName,
                actorUserId,
                cancellationToken));
        }

        if (patch.DefaultBrandLogoUrl.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Branding.LogoUrl,
                SettingValueSerializer.Serialize(branding.DefaultBrandLogoUrl),
                actorUserId,
                isLocked: branding.LockTenantBrandLogoUrl,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantBrandLogoUrl.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Branding.LogoUrl,
                SettingValueSerializer.Serialize(branding.DefaultBrandLogoUrl),
                branding.LockTenantBrandLogoUrl,
                actorUserId,
                cancellationToken));
        }

        if (patch.DefaultBrandFaviconUrl.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Branding.FaviconUrl,
                SettingValueSerializer.Serialize(branding.DefaultBrandFaviconUrl),
                actorUserId,
                isLocked: branding.LockTenantBrandFaviconUrl,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantBrandFaviconUrl.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Branding.FaviconUrl,
                SettingValueSerializer.Serialize(branding.DefaultBrandFaviconUrl),
                branding.LockTenantBrandFaviconUrl,
                actorUserId,
                cancellationToken));
        }

        if (patch.DefaultBrandCustomCssUrl.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Branding.CustomCssUrl,
                SettingValueSerializer.Serialize(branding.DefaultBrandCustomCssUrl),
                actorUserId,
                isLocked: branding.LockTenantBrandCustomCssUrl,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantBrandCustomCssUrl.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Branding.CustomCssUrl,
                SettingValueSerializer.Serialize(branding.DefaultBrandCustomCssUrl),
                branding.LockTenantBrandCustomCssUrl,
                actorUserId,
                cancellationToken));
        }

        return notifications;
    }

    public async Task ApplyDomainSettingsAsync(DomainSettingsDto d, Guid? actorUserId)
    {
        d.InstanceBaseDomain = NormalizeOptionalHost(d.InstanceBaseDomain);
        d.AdminHost = NormalizeOptionalHost(d.AdminHost);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Domains.InstanceBaseDomain,
            SettingValueSerializer.Serialize(d.InstanceBaseDomain), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Domains.AdminHost,
            SettingValueSerializer.Serialize(d.AdminHost), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
            SettingValueSerializer.Serialize(d.AllowTenantCustomDomains), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Domains.TenantSubdomain,
            SettingValueSerializer.Serialize(string.Empty),
            isLocked: d.LockTenantSubdomain, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Domains.TenantCustomDomain,
            SettingValueSerializer.Serialize(string.Empty),
            isLocked: d.LockTenantCustomDomain, actorUserId);
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyDomainSettingsPatchAsync(
        PatchDomainSettingsDto patch,
        DomainSettingsDto domains,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = new List<SettingChangedNotification>();
        domains.InstanceBaseDomain = NormalizeOptionalHost(domains.InstanceBaseDomain);
        domains.AdminHost = NormalizeOptionalHost(domains.AdminHost);

        if (patch.InstanceBaseDomain.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Domains.InstanceBaseDomain,
                SettingValueSerializer.Serialize(domains.InstanceBaseDomain),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.AdminHost.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Domains.AdminHost,
                SettingValueSerializer.Serialize(domains.AdminHost),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.AllowTenantCustomDomains.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
                SettingValueSerializer.Serialize(domains.AllowTenantCustomDomains),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.LockTenantSubdomain.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Domains.TenantSubdomain,
                SettingValueSerializer.Serialize(string.Empty),
                domains.LockTenantSubdomain,
                actorUserId,
                cancellationToken));
        }

        if (patch.LockTenantCustomDomain.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Domains.TenantCustomDomain,
                SettingValueSerializer.Serialize(string.Empty),
                domains.LockTenantCustomDomain,
                actorUserId,
                cancellationToken));
        }

        return notifications;
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyTenantDelegationSettingsAsync(
        TenantDelegationSettingsDto delegation,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SettingChangedNotification> notifications = await ApplySmtpDelegationAsync(
            delegation.LockTenantSmtp, actorUserId, cancellationToken);
        await ApplyTenantDelegationSettingsInternalAsync(delegation, false, actorUserId, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyTenantDelegationSettingsPatchAsync(
        bool isMultiTenant,
        PatchTenantDelegationSettingsDto patch,
        TenantDelegationSettingsDto delegation,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = new List<SettingChangedNotification>();
        if (patch.LockTenantSmtp.HasValue)
        {
            notifications.AddRange(await ApplySmtpDelegationAsync(
                delegation.LockTenantSmtp, actorUserId, cancellationToken));
        }

        delegation.DefaultPublicHomePage = NormalizeHomePage(delegation.DefaultPublicHomePage);

        if (patch.AllowTenantSelfServiceRegistration.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Tenants.SelfServiceRegistration,
                SettingValueSerializer.Serialize(isMultiTenant && delegation.AllowTenantSelfServiceRegistration),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.AllowTenantWhiteLabeling.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
                SettingValueSerializer.Serialize(isMultiTenant && delegation.AllowTenantWhiteLabeling),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.DefaultPublicHomePage.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Routing.DefaultPublicHomePage,
                SettingValueSerializer.Serialize(delegation.DefaultPublicHomePage),
                actorUserId,
                isLocked: delegation.LockTenantHomePagePreference,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantHomePagePreference.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Routing.DefaultPublicHomePage,
                SettingValueSerializer.Serialize(delegation.DefaultPublicHomePage),
                delegation.LockTenantHomePagePreference,
                actorUserId,
                cancellationToken));
        }

        if (patch.LockTenantStorage.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.TenantDelegation.LockStorage,
                SettingValueSerializer.Serialize(delegation.LockTenantStorage),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.LockTenantAnalytics.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.TenantDelegation.LockAnalytics,
                SettingValueSerializer.Serialize(delegation.LockTenantAnalytics),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.LockTenantAiAssistant.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
                SettingValueSerializer.Serialize(delegation.LockTenantAiAssistant),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        return notifications;
    }

    public async Task ApplyAdminPortalSettingsAsync(AdminPortalSettingsDto adminPortal, Guid? actorUserId)
    {
        adminPortal.PublicUrl = NormalizeOptionalUrl(adminPortal.PublicUrl);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AdminPortal.Enabled,
            SettingValueSerializer.Serialize(adminPortal.Enabled), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AdminPortal.PublicUrl,
            SettingValueSerializer.Serialize(adminPortal.PublicUrl), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess,
            SettingValueSerializer.Serialize(adminPortal.AllowTenantAdminAccess), actorUserId);
    }

    public async Task ApplyAdminPortalSettingsPatchAsync(
        PatchAdminPortalSettingsDto patch,
        AdminPortalSettingsDto adminPortal,
        Guid? actorUserId)
    {
        adminPortal.PublicUrl = NormalizeOptionalUrl(adminPortal.PublicUrl);

        if (patch.Enabled.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.AdminPortal.Enabled,
                SettingValueSerializer.Serialize(adminPortal.Enabled),
                actorUserId);
        }

        if (patch.PublicUrl.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.AdminPortal.PublicUrl,
                SettingValueSerializer.Serialize(adminPortal.PublicUrl),
                actorUserId);
        }

        if (patch.AllowTenantAdminAccess.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess,
                SettingValueSerializer.Serialize(adminPortal.AllowTenantAdminAccess),
                actorUserId);
        }
    }

    public async Task ApplyAiAssistantGovernanceSettingsAsync(AiAssistantGovernanceSettingsDto aiAssistant, Guid? actorUserId)
    {
        var provider = NormalizeAiAssistantProvider(aiAssistant.Provider, aiAssistant.Enabled);
        var usesOfficialProvider = provider == AiProviderDefaults.ProviderOpenAi
            || provider == AiProviderDefaults.ProviderAnthropic;
        var usesCompatibleProvider = provider == AiProviderDefaults.ProviderOpenAiCompatible
            || provider == AiProviderDefaults.ProviderAnthropicCompatible;
        var usesExternalProvider = usesOfficialProvider || usesCompatibleProvider;
        var endpointUrl = usesCompatibleProvider
            ? NormalizeOptionalUrl(aiAssistant.EndpointUrl)
            : string.Empty;
        var apiKey = usesExternalProvider ? aiAssistant.ApiKey?.Trim() ?? string.Empty : string.Empty;
        var modelId = usesExternalProvider ? aiAssistant.ModelId?.Trim() ?? string.Empty : string.Empty;
        var allowedModelIds = usesExternalProvider
            ? NormalizeAiModelIds([modelId], aiAssistant.AllowedModelIds)
            : [];

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.Enabled,
            SettingValueSerializer.Serialize(aiAssistant.Enabled),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.Provider,
            SettingValueSerializer.Serialize(provider),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.EndpointUrl,
            SettingValueSerializer.Serialize(endpointUrl),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.ApiKey,
            SettingValueSerializer.Serialize(apiKey),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.ModelId,
            SettingValueSerializer.Serialize(modelId),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.AllowedModelIds,
            SettingValueSerializer.Serialize(allowedModelIds),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
            SettingValueSerializer.Serialize(aiAssistant.AllowAnonymousAccess),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled,
            SettingValueSerializer.Serialize(aiAssistant.ToolProposalsEnabled),
            isLocked: aiAssistant.LockTenantAiAssistant, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
            SettingValueSerializer.Serialize(aiAssistant.LockTenantAiAssistant), actorUserId);
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyAiAssistantGovernanceSettingsPatchAsync(
        PatchAiAssistantGovernanceSettingsDto patch,
        AiAssistantGovernanceSettingsDto aiAssistant,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = new List<SettingChangedNotification>();
        var provider = NormalizeAiAssistantProvider(aiAssistant.Provider, aiAssistant.Enabled);
        var usesOfficialProvider = provider == AiProviderDefaults.ProviderOpenAi
            || provider == AiProviderDefaults.ProviderAnthropic;
        var usesCompatibleProvider = provider == AiProviderDefaults.ProviderOpenAiCompatible
            || provider == AiProviderDefaults.ProviderAnthropicCompatible;
        var usesExternalProvider = usesOfficialProvider || usesCompatibleProvider;
        var endpointUrl = usesCompatibleProvider
            ? NormalizeOptionalUrl(aiAssistant.EndpointUrl)
            : string.Empty;
        var apiKey = usesExternalProvider ? aiAssistant.ApiKey?.Trim() ?? string.Empty : string.Empty;
        var modelId = usesExternalProvider ? aiAssistant.ModelId?.Trim() ?? string.Empty : string.Empty;
        var allowedModelIds = usesExternalProvider
            ? NormalizeAiModelIds([modelId], aiAssistant.AllowedModelIds)
            : [];

        if (patch.Enabled.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.Enabled,
                SettingValueSerializer.Serialize(aiAssistant.Enabled),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantAiAssistant.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.Enabled,
                SettingValueSerializer.Serialize(aiAssistant.Enabled),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
        }

        if (patch.ProviderConfiguration.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.Provider,
                SettingValueSerializer.Serialize(provider),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.EndpointUrl,
                SettingValueSerializer.Serialize(endpointUrl),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.ApiKey,
                SettingValueSerializer.Serialize(apiKey),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.ModelId,
                SettingValueSerializer.Serialize(modelId),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.AllowedModelIds,
                SettingValueSerializer.Serialize(allowedModelIds),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantAiAssistant.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.Provider,
                SettingValueSerializer.Serialize(provider),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.EndpointUrl,
                SettingValueSerializer.Serialize(endpointUrl),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.ApiKey,
                SettingValueSerializer.Serialize(apiKey),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.ModelId,
                SettingValueSerializer.Serialize(modelId),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.AllowedModelIds,
                SettingValueSerializer.Serialize(allowedModelIds),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
        }

        if (patch.AllowAnonymousAccess.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
                SettingValueSerializer.Serialize(aiAssistant.AllowAnonymousAccess),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantAiAssistant.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
                SettingValueSerializer.Serialize(aiAssistant.AllowAnonymousAccess),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
        }

        if (patch.ToolProposalsEnabled.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled,
                SettingValueSerializer.Serialize(aiAssistant.ToolProposalsEnabled),
                actorUserId,
                isLocked: aiAssistant.LockTenantAiAssistant,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantAiAssistant.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled,
                SettingValueSerializer.Serialize(aiAssistant.ToolProposalsEnabled),
                aiAssistant.LockTenantAiAssistant,
                actorUserId,
                cancellationToken));
        }

        if (patch.LockTenantAiAssistant.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
                SettingValueSerializer.Serialize(aiAssistant.LockTenantAiAssistant),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        return notifications;
    }

    public async Task ApplyMcpGovernanceSettingsAsync(McpGovernanceSettingsDto mcp, Guid? actorUserId)
    {
        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Mcp.Enabled,
            SettingValueSerializer.Serialize(mcp.Enabled),
            isLocked: mcp.LockTenantMcp, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Mcp.EnableLegacySse,
            SettingValueSerializer.Serialize(mcp.EnableLegacySse),
            isLocked: mcp.LockTenantMcpLegacySse, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.TenantDelegation.LockMcp,
            SettingValueSerializer.Serialize(mcp.LockTenantMcp), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse,
            SettingValueSerializer.Serialize(mcp.LockTenantMcpLegacySse), actorUserId);
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyMcpGovernanceSettingsPatchAsync(
        PatchMcpGovernanceSettingsDto patch,
        McpGovernanceSettingsDto mcp,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = new List<SettingChangedNotification>();
        if (patch.Enabled.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Mcp.Enabled,
                SettingValueSerializer.Serialize(mcp.Enabled),
                actorUserId,
                isLocked: mcp.LockTenantMcp,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantMcp.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Mcp.Enabled,
                SettingValueSerializer.Serialize(mcp.Enabled),
                mcp.LockTenantMcp,
                actorUserId,
                cancellationToken));
        }

        if (patch.EnableLegacySse.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.Mcp.EnableLegacySse,
                SettingValueSerializer.Serialize(mcp.EnableLegacySse),
                actorUserId,
                isLocked: mcp.LockTenantMcpLegacySse,
                cancellationToken: cancellationToken)).Notification);
        }
        else if (patch.LockTenantMcpLegacySse.HasValue)
        {
            notifications.Add(await _upsertService.UpsertLockAsync(
                GovernanceSettingKeys.Mcp.EnableLegacySse,
                SettingValueSerializer.Serialize(mcp.EnableLegacySse),
                mcp.LockTenantMcpLegacySse,
                actorUserId,
                cancellationToken));
        }

        if (patch.LockTenantMcp.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.TenantDelegation.LockMcp,
                SettingValueSerializer.Serialize(mcp.LockTenantMcp),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        if (patch.LockTenantMcpLegacySse.HasValue)
        {
            notifications.Add((await _upsertService.UpsertValueWithDeferredInvalidationAsync(
                GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse,
                SettingValueSerializer.Serialize(mcp.LockTenantMcpLegacySse),
                actorUserId,
                cancellationToken: cancellationToken)).Notification);
        }

        return notifications;
    }

    public async Task ApplyRenderPolicySettingsAsync(RenderPolicySettingsDto renderPolicy, Guid? actorUserId)
        => await ApplyRenderPolicySettingsInternalAsync(renderPolicy, actorUserId);

    public async Task ApplyRenderPolicySettingsPatchAsync(
        PatchRenderPolicySettingsDto patch,
        RenderPolicySettingsDto renderPolicy,
        Guid? actorUserId)
    {
        NormalizeRenderPolicySettings(renderPolicy);

        if (patch.RenderPolicyPreset.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Preset,
                SettingValueSerializer.Serialize(renderPolicy.RenderPolicyPreset),
                actorUserId);
        }

        if (patch.EnableAdvancedRenderPolicyOverrides.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled,
                SettingValueSerializer.Serialize(renderPolicy.EnableAdvancedRenderPolicyOverrides),
                actorUserId);
        }

        if (patch.GlobalRenderMode.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode,
                SettingValueSerializer.Serialize(renderPolicy.GlobalRenderMode),
                actorUserId);
        }

        if (patch.GlobalPrerenderEnabled.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled,
                SettingValueSerializer.Serialize(renderPolicy.GlobalPrerenderEnabled),
                actorUserId);
        }

        if (patch.PublicSeoRenderMode.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode,
                SettingValueSerializer.Serialize(renderPolicy.PublicSeoRenderMode),
                actorUserId);
        }

        if (patch.PublicSeoPrerenderEnabled.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled,
                SettingValueSerializer.Serialize(renderPolicy.PublicSeoPrerenderEnabled),
                actorUserId);
        }

        if (patch.OperationalRenderMode.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode,
                SettingValueSerializer.Serialize(renderPolicy.OperationalRenderMode),
                actorUserId);
        }

        if (patch.OperationalPrerenderEnabled.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled,
                SettingValueSerializer.Serialize(renderPolicy.OperationalPrerenderEnabled),
                actorUserId);
        }

        if (patch.AdminRenderMode.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode,
                SettingValueSerializer.Serialize(renderPolicy.AdminRenderMode),
                actorUserId);
        }

        if (patch.AdminPrerenderEnabled.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled,
                SettingValueSerializer.Serialize(renderPolicy.AdminPrerenderEnabled),
                actorUserId);
        }

        if (patch.OnboardingRenderMode.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.RenderMode,
                SettingValueSerializer.Serialize(renderPolicy.OnboardingRenderMode),
                actorUserId);
        }

        if (patch.OnboardingPrerenderEnabled.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.PrerenderEnabled,
                SettingValueSerializer.Serialize(renderPolicy.OnboardingPrerenderEnabled),
                actorUserId);
        }

        if (patch.AllowTenantRenderPolicyOverride.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride,
                SettingValueSerializer.Serialize(renderPolicy.AllowTenantRenderPolicyOverride),
                actorUserId);
        }

        if (patch.LockTenantPublicSeoRenderPolicy.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo,
                SettingValueSerializer.Serialize(renderPolicy.LockTenantPublicSeoRenderPolicy),
                actorUserId);
        }

        if (patch.LockTenantOperationalRenderPolicy.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational,
                SettingValueSerializer.Serialize(renderPolicy.LockTenantOperationalRenderPolicy),
                actorUserId);
        }

        if (patch.LockTenantAdminRenderPolicy.HasValue)
        {
            await _upsertService.UpsertValueAsync(
                GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin,
                SettingValueSerializer.Serialize(renderPolicy.LockTenantAdminRenderPolicy),
                actorUserId);
        }
    }

    // ── Batch resolution ──────────────────────────────────────

    private async Task<Dictionary<string, ResolvedSetting>> ResolveBatchForAllCategories(SettingContext context)
    {
        var allKeys = CollectAllKeys();
        var resolved = await _resolver.ResolveBatchAsync(allKeys, context);
        return resolved.ToDictionary(r => r.Key, r => r);
    }

    private static IEnumerable<string> CollectAllKeys()
    {
        return DeploymentSettingGroup.SettingKeys
            .Concat(ModuleSettingGroup.SettingKeys)
            .Concat(EventSettingGroup.SettingKeys)
            .Concat(OrganizationSettingGroup.SettingKeys)
            .Concat(GroupSettingGroup.SettingKeys)
            .Concat(BrandingSettingGroup.SettingKeys)
            .Concat(DomainSettingGroup.SettingKeys)
            .Concat(TenantDelegationSettingGroup.SettingKeys)
            .Concat(AdminPortalSettingGroup.SettingKeys)
            .Concat(AiAssistantSettingGroup.SettingKeys)
            .Concat(McpSettingGroup.SettingKeys)
            .Concat(RenderPolicySettingGroup.SettingKeys)
            .Concat(RoutingSettingGroup.SettingKeys)
            .Concat(LocationPrivacySettingDefinitions.All.Select(definition => definition.Key))
            .Concat(
            [
                GovernanceSettingKeys.Tenants.SelfServiceRegistration,
                GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
                GovernanceSettingKeys.Security.AuthorizationProvider
            ])
            .Distinct();
    }

    // ── Group population ──────────────────────────────────────

    private static TGroup PopulateGroup<TGroup>(IReadOnlyDictionary<string, ResolvedSetting> settings)
        where TGroup : ISettingGroup, new()
    {
        var group = new TGroup();
        group.Populate(settings);
        return group;
    }

    private static bool IsLocked(IReadOnlyDictionary<string, ResolvedSetting> settings, string key)
        => settings.TryGetValue(key, out var s) && s.IsLocked;

    // ── DTO mapping ──────────────────────────────────────

    private static TenantDelegationSettingsDto MapTenantDelegationDto(
        TenantDelegationSettingGroup delegation,
        RoutingSettingGroup routing,
        IReadOnlyDictionary<string, ResolvedSetting> resolved,
        bool isMultiTenant)
    {
        var selfService = resolved.TryGetValue(GovernanceSettingKeys.Tenants.SelfServiceRegistration, out var ss)
            ? SettingValueSerializer.Deserialize(ss.Value, false) : false;
        var whiteLabeling = resolved.TryGetValue(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, out var wl)
            ? SettingValueSerializer.Deserialize(wl.Value, false) : false;
        var authProvider = resolved.TryGetValue(GovernanceSettingKeys.Security.AuthorizationProvider, out var ap)
            ? NormalizeAuthorizationProvider(SettingValueSerializer.DeserializeString(ap.Value, "local")) : "local";

        return new TenantDelegationSettingsDto
        {
            AllowTenantSelfServiceRegistration = isMultiTenant && selfService,
            AllowTenantWhiteLabeling = isMultiTenant && whiteLabeling,
            DefaultPublicHomePage = NormalizeHomePage(routing.DefaultPublicHomePage),
            LockTenantHomePagePreference = IsLocked(resolved, GovernanceSettingKeys.Routing.DefaultPublicHomePage),
            LockTenantSmtp = delegation.LockSmtp,
            LockTenantStorage = delegation.LockStorage,
            LockTenantAnalytics = delegation.LockAnalytics,
            LockTenantAiAssistant = delegation.LockAiAssistant,
            AuthorizationProvider = authProvider
        };
    }

    private static AdminPortalSettingsDto MapAdminPortalDto(AdminPortalSettingGroup adminPortal)
    {
        return new AdminPortalSettingsDto
        {
            Enabled = adminPortal.Enabled,
            PublicUrl = adminPortal.PublicUrl ?? string.Empty,
            AllowTenantAdminAccess = adminPortal.AllowTenantAdminAccess
        };
    }

    private static McpGovernanceSettingsDto MapMcpDto(
        McpSettingGroup mcp,
        TenantDelegationSettingGroup delegation)
    {
        return new McpGovernanceSettingsDto
        {
            Enabled = mcp.Enabled,
            EnableLegacySse = mcp.EnableLegacySse,
            LockTenantMcp = delegation.LockMcp,
            LockTenantMcpLegacySse = delegation.LockMcpLegacySse
        };
    }

    private static AiAssistantGovernanceSettingsDto MapAiAssistantDto(
        AiAssistantSettingGroup aiAssistant,
        TenantDelegationSettingGroup delegation)
    {
        return new AiAssistantGovernanceSettingsDto
        {
            Enabled = aiAssistant.Enabled,
            Provider = NormalizeAiAssistantProvider(aiAssistant.Provider, aiAssistant.Enabled),
            EndpointUrl = aiAssistant.EndpointUrl ?? string.Empty,
            ApiKey = aiAssistant.ApiKey ?? string.Empty,
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(aiAssistant.ApiKey),
            ModelId = aiAssistant.ModelId ?? string.Empty,
            AllowedModelIds = NormalizeAiModelIds([aiAssistant.ModelId], aiAssistant.AllowedModelIds),
            AllowAnonymousAccess = aiAssistant.AllowAnonymousAccess,
            ToolProposalsEnabled = aiAssistant.ToolProposalsEnabled,
            LockTenantAiAssistant = delegation.LockAiAssistant
        };
    }

    private static RenderPolicySettingsDto MapRenderPolicyDto(
        RenderPolicySettingGroup rp,
        IReadOnlyDictionary<string, ResolvedSetting> resolved)
    {
        return new RenderPolicySettingsDto
        {
            RenderPolicyVersion = Math.Max(int.TryParse(rp.Version, out var v) ? v : 1, 1),
            RenderPolicyPreset = NormalizeRenderPolicyPreset(rp.Preset),
            EnableAdvancedRenderPolicyOverrides = rp.AdvancedEnabled,
            GlobalRenderMode = NormalizeRenderMode(rp.FallbackRenderMode),
            GlobalPrerenderEnabled = rp.FallbackPrerenderEnabled,
            PublicSeoRenderMode = NormalizeRenderMode(rp.PublicSeoRenderMode),
            PublicSeoPrerenderEnabled = rp.PublicSeoPrerenderEnabled,
            OperationalRenderMode = NormalizeRenderMode(rp.OperationalRenderMode),
            OperationalPrerenderEnabled = rp.OperationalPrerenderEnabled,
            AdminRenderMode = NormalizeRenderMode(rp.AdminRenderMode),
            AdminPrerenderEnabled = rp.AdminPrerenderEnabled,
            OnboardingRenderMode = NormalizeRenderMode(rp.OnboardingRenderMode),
            OnboardingPrerenderEnabled = rp.OnboardingPrerenderEnabled,
            DisallowInteractiveServerOnOnboarding = rp.DisallowInteractiveServerOnOnboarding,
            AllowTenantRenderPolicyOverride = rp.AllowTenantOverride,
            LockTenantPublicSeoRenderPolicy = rp.LockTenantPublicSeo,
            LockTenantOperationalRenderPolicy = rp.LockTenantOperational,
            LockTenantAdminRenderPolicy = rp.LockTenantAdmin
        };
    }

    private static LocationPrivacyGovernanceSettingsDto MapLocationPrivacyDto(
        IReadOnlyDictionary<string, ResolvedSetting> resolved)
    {
        LocationPrivacyGovernanceSettingValue allowHomes = ResolveLocationPrivacyValue(
            resolved,
            GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations);
        LocationPrivacyGovernanceSettingValue allowAddress = ResolveLocationPrivacyValue(
            resolved,
            GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress);
        LocationPrivacyGovernanceSettingValue allowCoordinates = ResolveLocationPrivacyValue(
            resolved,
            GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates);
        LocationPrivacyGovernanceSettingValue audience = ResolveLocationPrivacyValue(
            resolved,
            GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience);
        LocationPrivacyGovernanceSettingValue revealOffset = ResolveLocationPrivacyValue(
            resolved,
            GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset);

        return new()
        {
            AllowHomeLocations = allowHomes.Boolean == true,
            AllowPublicExactAddress = allowAddress.Boolean == true,
            AllowPublicCoordinates = allowCoordinates.Boolean == true,
            MinimumHomeAudience = audience.Audience switch
            {
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant => "ANY_CURRENT_REGISTRANT",
                LocationDisclosureAudienceEnum.ConfirmedParticipant => "CONFIRMED_PARTICIPANT",
                _ => "NEVER"
            },
            DefaultRevealOffset = System.Xml.XmlConvert.ToString(
                revealOffset.Duration ?? TimeSpan.FromDays(30))
        };
    }

    private static LocationPrivacyGovernanceSettingValue ResolveLocationPrivacyValue(
        IReadOnlyDictionary<string, ResolvedSetting> resolved,
        string key)
    {
        string storedValue = resolved.TryGetValue(key, out ResolvedSetting? setting)
            ? setting.Value
            : LocationPrivacyGovernancePolicy.DefaultStoredValue(key);
        if (LocationPrivacyGovernancePolicy.TryParse(key, storedValue, out var value, out _))
        {
            return value;
        }

        LocationPrivacyGovernancePolicy.TryParse(
            key,
            LocationPrivacyGovernancePolicy.DefaultStoredValue(key),
            out value,
            out _);
        return value;
    }

    // ── Internal write methods ──────────────────────────────────────

    private async Task<IReadOnlyList<SettingChangedNotification>> ApplySmtpDelegationAsync(
        bool lockTenantSmtp,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        EmailDeliverySettingsWriteResult result = await _emailDeliverySettingsWriter.ApplyAsync(
            mutations: [new EmailDeliverySettingMutation(
                TenantId: null,
                Key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                Kind: EmailDeliverySettingMutationKind.SetValue,
                Value: SettingValueSerializer.Serialize(lockTenantSmtp),
                IsLocked: false)],
            actorUserId: actorUserId,
            cancellationToken: cancellationToken);
        result.EnsureAccepted();
        return result.ToNotifications(actorUserId);
    }

    private async Task ApplyTenantDelegationSettingsInternalAsync(
        TenantDelegationSettingsDto d,
        bool isMultiTenant,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        d.DefaultPublicHomePage = NormalizeHomePage(d.DefaultPublicHomePage);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Tenants.SelfServiceRegistration,
            SettingValueSerializer.Serialize(isMultiTenant && d.AllowTenantSelfServiceRegistration), actorUserId, cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
            SettingValueSerializer.Serialize(isMultiTenant && d.AllowTenantWhiteLabeling), actorUserId, cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.DefaultPublicHomePage,
            SettingValueSerializer.Serialize(d.DefaultPublicHomePage),
            isLocked: d.LockTenantHomePagePreference, actorUserId, cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Security.AuthorizationProvider,
            SettingValueSerializer.Serialize(NormalizeAuthorizationProvider(d.AuthorizationProvider)),
            isLocked: true, actorUserId, cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.TenantDelegation.LockStorage,
            SettingValueSerializer.Serialize(d.LockTenantStorage), actorUserId, cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.TenantDelegation.LockAnalytics,
            SettingValueSerializer.Serialize(d.LockTenantAnalytics), actorUserId, cancellationToken);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
            SettingValueSerializer.Serialize(d.LockTenantAiAssistant), actorUserId, cancellationToken);
    }

    private async Task ApplyLocationPrivacySettingsAsync(
        LocationPrivacyGovernanceSettingsDto settings,
        Guid? actorUserId,
        ICollection<LocationPrivacyGovernanceMutationResult> mutations,
        ICollection<SettingChangedNotification> notifications)
    {
        await AddMutationAsync(
            GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations,
            SettingValueSerializer.Serialize(settings.AllowHomeLocations),
            actorUserId);
        await AddMutationAsync(
            GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress,
            SettingValueSerializer.Serialize(settings.AllowPublicExactAddress),
            actorUserId);
        await AddMutationAsync(
            GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates,
            SettingValueSerializer.Serialize(settings.AllowPublicCoordinates),
            actorUserId);
        await AddMutationAsync(
            GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience,
            SettingValueSerializer.Serialize(settings.MinimumHomeAudience),
            actorUserId);
        await AddMutationAsync(
            GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset,
            SettingValueSerializer.Serialize(settings.DefaultRevealOffset),
            actorUserId);

        async Task AddMutationAsync(string key, string value, Guid? actorId)
        {
            DeferredSettingUpsertResult result =
                await _upsertService.UpsertValueWithDeferredInvalidationAsync(key, value, actorId);
            notifications.Add(result.Notification);
            if (result.Mutation is not null)
            {
                mutations.Add(result.Mutation);
            }
        }
    }

    private async Task ApplyRenderPolicySettingsInternalAsync(RenderPolicySettingsDto rp, Guid? actorUserId)
    {
        NormalizeRenderPolicySettings(rp);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Version,
            SettingValueSerializer.Serialize(Math.Max(rp.RenderPolicyVersion, 1)),
            isLocked: true, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Preset,
            SettingValueSerializer.Serialize(rp.RenderPolicyPreset), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled,
            SettingValueSerializer.Serialize(rp.EnableAdvancedRenderPolicyOverrides), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode,
            SettingValueSerializer.Serialize(rp.GlobalRenderMode), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled,
            SettingValueSerializer.Serialize(rp.GlobalPrerenderEnabled), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode,
            SettingValueSerializer.Serialize(rp.PublicSeoRenderMode), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled,
            SettingValueSerializer.Serialize(rp.PublicSeoPrerenderEnabled), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode,
            SettingValueSerializer.Serialize(rp.OperationalRenderMode), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled,
            SettingValueSerializer.Serialize(rp.OperationalPrerenderEnabled), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode,
            SettingValueSerializer.Serialize(rp.AdminRenderMode), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled,
            SettingValueSerializer.Serialize(rp.AdminPrerenderEnabled), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.RenderMode,
            SettingValueSerializer.Serialize(rp.OnboardingRenderMode),
            isLocked: true, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.PrerenderEnabled,
            SettingValueSerializer.Serialize(rp.OnboardingPrerenderEnabled), actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.DisallowInteractiveServerOnOnboarding,
            SettingValueSerializer.Serialize(rp.DisallowInteractiveServerOnOnboarding),
            isLocked: true, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride,
            SettingValueSerializer.Serialize(rp.AllowTenantRenderPolicyOverride),
            isLocked: true, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo,
            SettingValueSerializer.Serialize(rp.LockTenantPublicSeoRenderPolicy),
            isLocked: true, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational,
            SettingValueSerializer.Serialize(rp.LockTenantOperationalRenderPolicy),
            isLocked: true, actorUserId);

        await _upsertService.UpsertValueAsync(
            GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin,
            SettingValueSerializer.Serialize(rp.LockTenantAdminRenderPolicy),
            isLocked: true, actorUserId);
    }

    // ── Normalization helpers (business logic) ──────────────────────────────────────

    private static string NormalizeRequiredDisplayName(string? value)
        => value?.Trim() ?? string.Empty;

    private static string NormalizeOptionalUrl(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeOptionalHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = value.Trim().ToLowerInvariant();
        sanitized = sanitized.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        return sanitized.Trim().Trim('/');
    }

    private static string NormalizeHomePage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "EventList";
        return raw.Equals("LandingPage", StringComparison.OrdinalIgnoreCase) ? "LandingPage" : "EventList";
    }

    private static void NormalizeRenderPolicySettings(RenderPolicySettingsDto rp)
    {
        rp.RenderPolicyVersion = Math.Max(rp.RenderPolicyVersion, 1);
        rp.RenderPolicyPreset = NormalizeRenderPolicyPreset(rp.RenderPolicyPreset);
        rp.GlobalRenderMode = NormalizeRenderMode(rp.GlobalRenderMode);

        ApplyPresetDefaults(rp);

        rp.PublicSeoRenderMode = NormalizeRenderMode(rp.PublicSeoRenderMode);
        rp.OperationalRenderMode = NormalizeRenderMode(rp.OperationalRenderMode);
        rp.AdminRenderMode = NormalizeRenderMode(rp.AdminRenderMode);
        rp.OnboardingRenderMode = NormalizeRenderMode(rp.OnboardingRenderMode);

        if (!rp.EnableAdvancedRenderPolicyOverrides)
        {
            rp.PublicSeoRenderMode = rp.GlobalRenderMode;
            rp.PublicSeoPrerenderEnabled = rp.GlobalPrerenderEnabled;
            rp.OperationalRenderMode = rp.GlobalRenderMode;
            rp.OperationalPrerenderEnabled = rp.GlobalPrerenderEnabled;
            rp.AdminRenderMode = rp.GlobalRenderMode;
            rp.AdminPrerenderEnabled = rp.GlobalPrerenderEnabled;
            rp.OnboardingRenderMode = rp.GlobalRenderMode;
            rp.OnboardingPrerenderEnabled = rp.GlobalPrerenderEnabled;

            if (rp.RenderPolicyPreset.Equals(RenderPolicyPresetEnum.SeoBalanced.ToString(), StringComparison.OrdinalIgnoreCase))
                rp.PublicSeoPrerenderEnabled = true;
        }

        if (IsInteractiveServerRenderMode(rp.OnboardingRenderMode))
            rp.OnboardingRenderMode = RenderModeOptionEnum.InteractiveAuto.ToString();

        rp.DisallowInteractiveServerOnOnboarding = true;
    }

    private static string NormalizeRenderPolicyPreset(string? raw)
        => Enum.TryParse(raw, ignoreCase: true, out RenderPolicyPresetEnum preset)
            ? preset.ToString() : RenderPolicyPresetEnum.AllInteractiveServer.ToString();

    private static string NormalizeRenderMode(string? raw)
        => Enum.TryParse(raw, ignoreCase: true, out RenderModeOptionEnum mode)
            ? mode.ToString() : RenderModeOptionEnum.InteractiveServer.ToString();

    private static bool IsInteractiveServerRenderMode(string? renderMode)
        => Enum.TryParse(renderMode, ignoreCase: true, out RenderModeOptionEnum mode)
            && mode == RenderModeOptionEnum.InteractiveServer;

    private static void ApplyPresetDefaults(RenderPolicySettingsDto rp)
    {
        if (!Enum.TryParse(rp.RenderPolicyPreset, ignoreCase: true, out RenderPolicyPresetEnum preset))
            preset = RenderPolicyPresetEnum.SeoBalanced;

        switch (preset)
        {
            case RenderPolicyPresetEnum.AllPrerendered:
                rp.EnableAdvancedRenderPolicyOverrides = false;
                rp.GlobalPrerenderEnabled = true;
                break;
            case RenderPolicyPresetEnum.AllInteractiveAutoNoPrerender:
                rp.EnableAdvancedRenderPolicyOverrides = false;
                rp.GlobalRenderMode = RenderModeOptionEnum.InteractiveAuto.ToString();
                rp.GlobalPrerenderEnabled = false;
                break;
            case RenderPolicyPresetEnum.AllInteractiveServer:
                rp.EnableAdvancedRenderPolicyOverrides = false;
                rp.GlobalRenderMode = RenderModeOptionEnum.InteractiveServer.ToString();
                rp.GlobalPrerenderEnabled = false;
                break;
            case RenderPolicyPresetEnum.SeoBalanced:
                rp.EnableAdvancedRenderPolicyOverrides = false;
                rp.GlobalRenderMode = RenderModeOptionEnum.InteractiveAuto.ToString();
                rp.GlobalPrerenderEnabled = false;
                rp.PublicSeoPrerenderEnabled = true;
                break;
            case RenderPolicyPresetEnum.CustomAdvanced:
                rp.EnableAdvancedRenderPolicyOverrides = true;
                break;
        }
    }

    private static string NormalizeAuthorizationProvider(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "local";
        return raw.Trim().ToLowerInvariant() switch { "cerbos" => "cerbos", _ => "local" };
    }

    private static string NormalizeAiAssistantProvider(string? provider, bool enabled)
    {
        if (!enabled)
            return AiProviderDefaults.ProviderNone;

        var normalized = provider?.Trim().ToLowerInvariant();
        return normalized is AiProviderDefaults.ProviderFake
            or AiProviderDefaults.ProviderOpenAi
            or AiProviderDefaults.ProviderOpenAiCompatible
            or AiProviderDefaults.ProviderAnthropic
            or AiProviderDefaults.ProviderAnthropicCompatible
            ? normalized
            : AiProviderDefaults.ProviderOpenAi;
    }

    private static IReadOnlyList<string> NormalizeAiModelIds(params IEnumerable<string?>[] modelIdGroups)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();

        foreach (var group in modelIdGroups)
        {
            foreach (var modelId in group)
            {
                if (string.IsNullOrWhiteSpace(modelId))
                    continue;

                var normalized = modelId.Trim();
                if (seen.Add(normalized))
                    values.Add(normalized);
            }
        }

        return values;
    }
}
