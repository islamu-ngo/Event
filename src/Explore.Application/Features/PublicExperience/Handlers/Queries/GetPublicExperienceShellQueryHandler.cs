// ABOUTME: Builds the typed public-experience shell from tenant-local settings and referenced content.
// ABOUTME: Keeps OrganizationCentric behavior in Application read models without changing tenant resolution.

using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Features.Tenants.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.PublicExperience.Handlers.Queries;

public class GetPublicExperienceShellQueryHandler(
    IRequestHandler<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto> settingsHandler,
    IRequestHandler<GetTenantNavLinksQuery, List<TenantNavigationLinkDto>> navigationLinksHandler,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver hierarchicalSettingsResolver,
    IOrganizationRepository organizationRepository)
    : IRequestHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto>
{
    public async Task<PublicExperienceShellDto> Handle(GetPublicExperienceShellQuery request, CancellationToken cancellationToken)
    {
        var settings = await settingsHandler.Handle(new GetPublicExperienceSettingsQuery(), cancellationToken);
        if (!settings.IsAvailable)
        {
            return new PublicExperienceShellDto
            {
                IsAvailable = false,
                UnavailableCode = settings.UnavailableCode
            };
        }

        var navigationLinks = await navigationLinksHandler.Handle(new GetTenantNavLinksQuery(), cancellationToken);
        var tenantId = tenantContext.TenantId;
        var settingContext = new SettingContext(TenantId: tenantId);

        var mode = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.PublicExperience.Mode, settingContext, cancellationToken);
        var eventCatalogLabel = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.PublicExperience.EventCatalogLabel, settingContext, cancellationToken);
        var primaryOrganizationId = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.PublicExperience.PrimaryOrganizationId, settingContext, cancellationToken);
        var homeBlocks = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.PublicExperience.HomeBlocks, settingContext, cancellationToken);
        var ctas = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.PublicExperience.Ctas, settingContext, cancellationToken);
        var eventSectionPresets = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.PublicExperience.EventSectionPresets, settingContext, cancellationToken);
        var railPublicVisibility = await hierarchicalSettingsResolver.ResolveAsync<string>(
            GovernanceSettingKeys.UiShell.RailPublicVisibility, settingContext, cancellationToken);
        var resolvedRailPublicVisibility = string.Equals(
            railPublicVisibility?.Trim(),
            "Always",
            StringComparison.OrdinalIgnoreCase)
            ? "Always"
            : "AuthenticatedOnly";

        var parsedMode = ParseMode(mode);
        var primaryOrganization = await ResolvePrimaryOrganizationAsync(primaryOrganizationId, tenantId, cancellationToken);
        var homeBlockDtos = BuildHomeBlocks(homeBlocks);
        var ctaDtos = BuildCtas(ctas);
        var eventSections = BuildEventSections(eventSectionPresets);
        var shellNavigationLinks = BuildNavigationLinks(navigationLinks);
        ApplyOrganizationCentricDefaults(parsedMode, primaryOrganization, eventCatalogLabel, homeBlockDtos, ctaDtos, eventSections);

        return new PublicExperienceShellDto
        {
            SchemaVersion = 1,
            IsAvailable = true,
            VisitorAccess = settings.VisitorAccess,
            DirectoryOperator = settings.DirectoryOperator,
            InstanceOperator = settings.InstanceOperator,
            Revision = BuildRevision(
                settings,
                parsedMode,
                eventCatalogLabel,
                resolvedRailPublicVisibility,
                primaryOrganization,
                homeBlockDtos,
                ctaDtos,
                eventSections,
                shellNavigationLinks),
            Mode = parsedMode,
            RailPublicVisibility = resolvedRailPublicVisibility,
            Home = new PublicExperienceHomeDto
            {
                PreferredHomePage = settings.PreferredHomePage,
                BrandDisplayName = settings.BrandDisplayName,
                BrandLogoUrl = settings.BrandLogoUrl,
                BrandFaviconUrl = settings.BrandFaviconUrl,
                Blocks = homeBlockDtos
            },
            Navigation = new PublicExperienceNavigationDto { Links = shellNavigationLinks },
            EventCatalog = new PublicExperienceEventCatalogDto
            {
                Label = string.IsNullOrWhiteSpace(eventCatalogLabel) ? "Events" : eventCatalogLabel.Trim(),
                Url = "/events"
            },
            PrimaryOrganization = primaryOrganization,
            EventSections = eventSections,
            Ctas = ctaDtos,
            Footer = settings.FooterConfig
        };
    }

    private static PublicExperienceMode ParseMode(string? mode)
    {
        return Enum.TryParse(mode?.Trim().Trim('"'), ignoreCase: true, out PublicExperienceMode parsed)
            ? parsed
            : PublicExperienceMode.DiscoveryCentric;
    }

    private static List<PublicExperienceEventSectionDto> BuildEventSections(string? rawConfig)
    {
        PublicEventSectionPresetsConfig? config = DeserializeConfig<PublicEventSectionPresetsConfig>(rawConfig);
        return config?.Presets?
            .Where(preset => preset.IsEnabled && !string.IsNullOrWhiteSpace(preset.Id) && !string.IsNullOrWhiteSpace(preset.Label))
            .OrderBy(preset => preset.SortOrder)
            .ThenBy(preset => preset.Label, StringComparer.OrdinalIgnoreCase)
            .Select(preset => new PublicExperienceEventSectionDto
            {
                Key = preset.Id.Trim(),
                Label = preset.Label.Trim(),
                Url = BuildEventSectionUrl(preset),
                Icon = preset.Icon?.Trim() ?? string.Empty,
                SortOrder = preset.SortOrder
            })
            .ToList() ?? [];
    }

    private static void ApplyOrganizationCentricDefaults(
        PublicExperienceMode mode,
        PublicExperiencePrimaryOrganizationDto primaryOrganization,
        string? eventCatalogLabel,
        List<PublicExperienceHomeBlockDto> homeBlocks,
        List<PublicExperienceCtaDto> ctas,
        List<PublicExperienceEventSectionDto> eventSections)
    {
        if (mode != PublicExperienceMode.OrganizationCentric ||
            primaryOrganization.State != PublicExperiencePrimaryOrganizationState.Available ||
            primaryOrganization.ActorId is not { } actorId)
        {
            return;
        }

        var catalogLabel = string.IsNullOrWhiteSpace(eventCatalogLabel) ? "Events" : eventCatalogLabel.Trim();
        var actorEventsUrl = BuildQueryString([
            new KeyValuePair<string, string>("ActorId", actorId.ToString("D")),
            new KeyValuePair<string, string>("SortBy", "date"),
            new KeyValuePair<string, string>("SortDescending", "false")]);

        if (homeBlocks.Count == 0)
        {
            homeBlocks.Add(new PublicExperienceHomeBlockDto
            {
                Key = "primary-organization-summary",
                Kind = PublicExperienceHomeBlockKind.OrganizationSummary,
                Title = primaryOrganization.DisplayName,
                Body = "Explore public programs and community updates from this organization.",
                ImageUrl = primaryOrganization.ProfilePictureUri,
                LinkText = $"View {catalogLabel}",
                LinkUrl = $"/events?{actorEventsUrl}",
                SortOrder = 0
            });
        }

        if (eventSections.Count == 0)
        {
            eventSections.Add(new PublicExperienceEventSectionDto
            {
                Key = "primary-organization-events",
                Label = catalogLabel,
                Url = $"/events?{actorEventsUrl}",
                Icon = "calendar",
                SortOrder = 0
            });
        }

        if (ctas.Count == 0)
        {
            ctas.Add(new PublicExperienceCtaDto
            {
                Key = "primary-organization-events",
                Label = $"Browse {catalogLabel}",
                Url = $"/events?{actorEventsUrl}",
                Placement = PublicExperienceCtaPlacement.Hero,
                Style = PublicExperienceCtaStyle.Primary,
                SortOrder = 0
            });
        }
    }

    private static T? DeserializeConfig<T>(string? rawConfig)
    {
        if (string.IsNullOrWhiteSpace(rawConfig))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(rawConfig, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static List<PublicExperienceNavigationLinkDto> BuildNavigationLinks(IReadOnlyList<TenantNavigationLinkDto> navigationLinks)
    {
        return navigationLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.Label) && !string.IsNullOrWhiteSpace(link.Url))
            .OrderBy(link => link.Order)
            .ThenBy(link => link.Label, StringComparer.OrdinalIgnoreCase)
            .Select(link => new PublicExperienceNavigationLinkDto
            {
                Label = link.Label.Trim(),
                Url = link.Url.Trim(),
                SortOrder = link.Order
            })
            .ToList();
    }

    private static List<PublicExperienceHomeBlockDto> BuildHomeBlocks(string? rawConfig)
    {
        PublicExperienceHomeBlocksConfig? config = DeserializeConfig<PublicExperienceHomeBlocksConfig>(rawConfig);
        return config?.Blocks?
            .Where(block => block.IsEnabled && !string.IsNullOrWhiteSpace(block.Id) && !string.IsNullOrWhiteSpace(block.Title))
            .OrderBy(block => block.SortOrder)
            .ThenBy(block => block.Title, StringComparer.OrdinalIgnoreCase)
            .Select(block => new PublicExperienceHomeBlockDto
            {
                Key = block.Id.Trim(),
                Kind = block.Kind,
                Title = block.Title.Trim(),
                Subtitle = block.Subtitle?.Trim() ?? string.Empty,
                Body = block.Body?.Trim() ?? string.Empty,
                ImageUrl = block.ImageUrl?.Trim() ?? string.Empty,
                LinkText = block.LinkText?.Trim() ?? string.Empty,
                LinkUrl = block.LinkUrl?.Trim() ?? string.Empty,
                SortOrder = block.SortOrder
            })
            .ToList() ?? [];
    }

    private static List<PublicExperienceCtaDto> BuildCtas(string? rawConfig)
    {
        PublicExperienceCtasConfig? config = DeserializeConfig<PublicExperienceCtasConfig>(rawConfig);
        return config?.Ctas?
            .Where(cta => cta.IsEnabled && !string.IsNullOrWhiteSpace(cta.Id) && !string.IsNullOrWhiteSpace(cta.Label) && !string.IsNullOrWhiteSpace(cta.Url))
            .OrderBy(cta => cta.SortOrder)
            .ThenBy(cta => cta.Label, StringComparer.OrdinalIgnoreCase)
            .Select(cta => new PublicExperienceCtaDto
            {
                Key = cta.Id.Trim(),
                Label = cta.Label.Trim(),
                Url = cta.Url.Trim(),
                Placement = cta.Placement,
                Style = cta.Style,
                SortOrder = cta.SortOrder
            })
            .ToList() ?? [];
    }

    private static string BuildEventSectionUrl(PublicEventSectionPresetConfig preset)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("SortBy", "date"),
            new("SortDescending", "false")
        };

        AddOwnerParameters(parameters, preset.Owners);
        AddFilterParameters(parameters, preset.Filters);

        if (preset.Limit is > 0)
        {
            parameters.Add(new KeyValuePair<string, string>("PageSize", preset.Limit.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return parameters.Count == 0 ? "/events" : $"/events?{BuildQueryString(parameters)}";
    }

    private static void AddOwnerParameters(List<KeyValuePair<string, string>> parameters, PublicEventSectionOwnerFilter? owners)
    {
        AddGuidValues(parameters, "ActorId", owners?.ActorIds, singleValueOnly: true);
        AddGuidValues(parameters, "OrganizationId", owners?.OrganizationIds, singleValueOnly: true);
        AddGuidValues(parameters, "GroupId", owners?.GroupIds, singleValueOnly: true);
    }

    private static void AddFilterParameters(List<KeyValuePair<string, string>> parameters, PublicEventSectionEventFilter? filters)
    {
        if (filters is null)
        {
            return;
        }

        AddGuidValues(parameters, "IncludedCategoryIds", filters.CategoryIds, singleValueOnly: false);
        AddGuidValues(parameters, "IncludedTagIds", filters.TagIds, singleValueOnly: false);
        AddIntValues(parameters, "AudienceGenderIds", filters.AudienceGenderIds);
        AddIntValues(parameters, "AudienceAgeIds", filters.AudienceAgeIds);
        AddIntValues(parameters, "EventTypeIds", filters.EventTypeIds);
        AddIntValues(parameters, "FormatIds", filters.EventFormatIds);
        AddDateParameters(parameters, filters.Date);
        AddCustomPropertyParameters(parameters, filters.CustomProperties);
    }

    private static void AddGuidValues(
        List<KeyValuePair<string, string>> parameters,
        string key,
        IReadOnlyList<Guid>? values,
        bool singleValueOnly)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        IEnumerable<Guid> effectiveValues = singleValueOnly ? values.Take(1) : values;
        foreach (var value in effectiveValues.Where(value => value != Guid.Empty))
        {
            parameters.Add(new KeyValuePair<string, string>(key, value.ToString("D")));
        }
    }

    private static void AddIntValues(List<KeyValuePair<string, string>> parameters, string key, IReadOnlyList<int>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        foreach (var value in values.Where(value => value > 0))
        {
            parameters.Add(new KeyValuePair<string, string>(key, value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void AddDateParameters(List<KeyValuePair<string, string>> parameters, PublicEventSectionDateFilter? date)
    {
        if (date is null)
        {
            return;
        }

        if (date.Window == PublicEventSectionDateWindow.Custom)
        {
            if (date.StartsOnOrAfter.HasValue)
            {
                parameters.Add(new KeyValuePair<string, string>("DateFrom", date.StartsOnOrAfter.Value.ToString("O", CultureInfo.InvariantCulture)));
            }

            if (date.StartsOnOrBefore.HasValue)
            {
                parameters.Add(new KeyValuePair<string, string>("DateTo", date.StartsOnOrBefore.Value.ToString("O", CultureInfo.InvariantCulture)));
            }
        }
    }

    private static void AddCustomPropertyParameters(
        List<KeyValuePair<string, string>> parameters,
        IReadOnlyList<PublicEventSectionCustomPropertyFilter>? customProperties)
    {
        if (customProperties is null || customProperties.Count == 0)
        {
            return;
        }

        var index = 0;
        foreach (var filter in customProperties.Where(filter => !string.IsNullOrWhiteSpace(filter.Namespace) && !string.IsNullOrWhiteSpace(filter.Key)))
        {
            var prefix = $"CustomPropertyFilters[{index}]";
            parameters.Add(new KeyValuePair<string, string>($"{prefix}.Namespace", filter.Namespace.Trim()));
            parameters.Add(new KeyValuePair<string, string>($"{prefix}.Key", filter.Key.Trim()));
            parameters.Add(new KeyValuePair<string, string>($"{prefix}.Operator", MapCustomPropertyOperator(filter.Operator)));

            if (filter.Values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) is { } firstValue)
            {
                parameters.Add(new KeyValuePair<string, string>($"{prefix}.Value", firstValue.Trim()));
            }

            index++;
        }
    }

    private static string MapCustomPropertyOperator(PublicEventSectionCustomPropertyOperator @operator)
    {
        return @operator switch
        {
            PublicEventSectionCustomPropertyOperator.Contains => "Contains",
            PublicEventSectionCustomPropertyOperator.AnyOf => "Contains",
            _ => "Equals"
        };
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join("&", parameters.Select(parameter =>
            $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
    }

    private async Task<PublicExperiencePrimaryOrganizationDto> ResolvePrimaryOrganizationAsync(
        string? rawOrganizationId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(rawOrganizationId?.Trim().Trim('"'), out var organizationId))
        {
            return new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.NotConfigured
            };
        }

        Organization? organization = await organizationRepository.GetOrganizationWithDetails(organizationId, cancellationToken);
        if (organization is null)
        {
            return new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Missing,
                OrganizationId = organizationId
            };
        }

        OrganizationTenant? participation = organization.TenantParticipations
            .SingleOrDefault(candidate => candidate.TenantId == tenantId);
        if (participation is null)
        {
            return new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.CrossTenantInvalid,
                OrganizationId = organizationId
            };
        }

        if (organization.IsDeleted)
        {
            return new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Deleted,
                OrganizationId = organizationId
            };
        }

        if (participation.ApprovalStatusId != (int)ApprovalStatusEnum.Approved
            || !participation.IsVisible)
        {
            return new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.HiddenOrInactive,
                OrganizationId = organizationId
            };
        }

        if (organization.Actor is null || organization.Actor.IsDeleted)
        {
            return new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.ActorUnavailable,
                OrganizationId = organizationId
            };
        }

        return new PublicExperiencePrimaryOrganizationDto
        {
            State = PublicExperiencePrimaryOrganizationState.Available,
            OrganizationId = organization.Id,
            ActorId = organization.Actor.Id,
            DisplayName = organization.Actor.DisplayName,
            Handle = organization.Actor.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault() ?? string.Empty,
            WebsiteUrl = organization.WebsiteUrl ?? string.Empty,
            ProfilePictureUri = organization.Actor.ProfilePictureUri ?? string.Empty
        };
    }

    private static string BuildRevision(
        PublicExperienceSettingsDto settings,
        PublicExperienceMode mode,
        string? eventCatalogLabel,
        string railPublicVisibility,
        PublicExperiencePrimaryOrganizationDto primaryOrganization,
        IReadOnlyList<PublicExperienceHomeBlockDto> homeBlocks,
        IReadOnlyList<PublicExperienceCtaDto> ctas,
        IReadOnlyList<PublicExperienceEventSectionDto> eventSections,
        IReadOnlyList<PublicExperienceNavigationLinkDto> navigationLinks)
    {
        var primaryOrganizationToken = primaryOrganization.State == PublicExperiencePrimaryOrganizationState.Available
            ? string.Join('=',
                primaryOrganization.OrganizationId?.ToString("N"),
                primaryOrganization.ActorId?.ToString("N"),
                primaryOrganization.DisplayName,
                primaryOrganization.Handle,
                primaryOrganization.WebsiteUrl,
                primaryOrganization.ProfilePictureUri)
            : primaryOrganization.State.ToString();

        return string.Join(':',
            1,
            settings.TenantId.ToString("N"),
            JsonSerializer.Serialize(settings.VisitorAccess),
            mode,
            string.IsNullOrWhiteSpace(eventCatalogLabel) ? "Events" : eventCatalogLabel.Trim(),
            railPublicVisibility,
            primaryOrganizationToken,
            string.Join(',', homeBlocks.Select(block => $"{block.Key}={block.Kind}:{block.Title}")),
            string.Join(',', ctas.Select(cta => $"{cta.Key}={cta.Placement}:{cta.Url}")),
            string.Join(',', eventSections.Select(section => $"{section.Key}={section.Url}")),
            string.Join(',', navigationLinks.Select(link => $"{link.SortOrder}={link.Label}:{link.Url}")),
            settings.FooterConfig.Settings.Template,
            settings.FooterConfig.LinkGroups.Count);
    }
}
