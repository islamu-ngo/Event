namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class ControlPlaneOverviewLinkPolicy : ILinkPolicy<ControlPlaneOverviewDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ControlPlaneOverviewDto dto, ClaimsPrincipal? user)
    {
        _ = dto;
        _ = user;

        yield return InstanceSettingLink(
            LinkRelations.Self,
            RouteNames.GetControlPlaneOverview,
            "GET",
            "Control-plane overview",
            GetControlPlaneOverviewQuery.SettingKey);

        yield return InstanceSettingLink(
            "domains",
            RouteNames.GetControlPlaneDomains,
            "GET",
            "Domain and DNS guidance",
            GetControlPlaneDomainsQuery.SettingKey);

        yield return InstanceSettingLink(
            "operations",
            RouteNames.GetControlPlaneOperations,
            "GET",
            "Operations status",
            GetControlPlaneOperationsQuery.SettingKey);

        yield return InstanceSettingLink(
            "plans",
            RouteNames.GetControlPlaneTenantPlans,
            "GET",
            "Tenant plan catalog",
            GetControlPlaneTenantPlanListQuery.SettingKey);

        yield return InstanceSettingLink(
            "storage",
            RouteNames.GetInstanceStorageSettings,
            "GET",
            "Storage settings",
            "storage");

        yield return InstanceSettingLink(
            "authentication",
            RouteNames.GetInstanceAuthProviderConfigurationStatus,
            "GET",
            "Authentication provider status",
            "auth-provider");

        yield return InstanceSettingLink(
            "authorization",
            RouteNames.GetInstanceAuthorizationProviderConfigurationStatus,
            "GET",
            "Authorization provider status",
            "authorization-provider");

        yield return InstanceSettingLink(
            LinkRelations.LocalIdentities,
            RouteNames.ListLocalIdentities,
            HttpMethods.Get,
            "Local identities",
            ListLocalIdentitiesQuery.ResourceKey);

        yield return ConfigurationManifestExportLink(
            LinkRelations.ExportConfigurationOverrides,
            ConfigurationManifestExportView.Overrides);

        yield return ConfigurationManifestExportLink(
            LinkRelations.ExportConfigurationPortable,
            ConfigurationManifestExportView.Portable);

        yield return new LinkDefinition(
                LinkRelations.CreateConfigurationImportSession,
                RouteNames.CreateInstanceConfigurationImportSession,
                null,
                HttpMethods.Post,
                "Import configuration manifest",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                CreateInstanceConfigurationImportSessionCommand.ResourceKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
                LinkRelations.ConfigurationImportHistory,
                RouteNames.ListInstanceConfigurationImportHistory,
                null,
                HttpMethods.Get,
                "Configuration import history",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                CreateInstanceConfigurationImportSessionCommand.ResourceKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
                LinkRelations.CreateConfigurationDirectTransfer,
                RouteNames.CreateInstanceConfigurationTransfer,
                null,
                HttpMethods.Post,
                "Create direct configuration transfer",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                CreateInstanceConfigurationImportSessionCommand.ResourceKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }

    private static LinkDefinition ConfigurationManifestExportLink(
        string relation,
        ConfigurationManifestExportView view) =>
        new LinkDefinition(
                relation,
                RouteNames.ExportConfigurationManifest,
                new { view = view.ToString() },
                HttpMethods.Get,
                view == ConfigurationManifestExportView.Portable
                    ? "Export portable configuration manifest"
                    : "Export configuration manifest overrides",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                ExportConfigurationManifestQuery.ResourceKey,
                facts: new ConfigurationManifestExportAuthorizationFacts());

    private static LinkDefinition InstanceSettingLink(
        string rel,
        string routeName,
        string method,
        string title,
        string settingKey) =>
        new LinkDefinition(rel, routeName, null, method, title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class ControlPlaneOverviewCollectionLinkPolicy : ICollectionLinkPolicy<ControlPlaneOverviewDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ControlPlaneOverviewDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
