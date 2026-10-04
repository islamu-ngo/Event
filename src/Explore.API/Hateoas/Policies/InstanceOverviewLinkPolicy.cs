namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;

public sealed class InstanceOverviewLinkPolicy : ILinkPolicy<InstanceOverviewDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceOverviewDto dto, ClaimsPrincipal? user)
    {
        yield return InstanceSettingLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminOverview,
            "GET",
            "Instance administration overview",
            GetInstanceOverviewQuery.SettingKey);

        yield return InstanceSettingLink(
            "domains",
            RouteNames.GetInstanceAdminDomains,
            "GET",
            "Domain and DNS guidance",
            GetInstanceDomainsQuery.SettingKey);

        yield return InstanceSettingLink(
            "operations",
            RouteNames.GetInstanceAdminOperations,
            "GET",
            "Operations status",
            GetInstanceOperationsQuery.SettingKey);

        yield return InstanceSettingLink(
            "plans",
            RouteNames.GetInstanceAdminTenantPlans,
            "GET",
            "Tenant plan catalog",
            GetInstanceTenantPlanListQuery.SettingKey);

        if (dto.DeploymentMode == nameof(DeploymentMode.MultiTenant))
        {
            yield return InstanceSettingLink(
                "tenants",
                RouteNames.GetInstanceAdminTenants,
                "GET",
                "Tenant lifecycle management",
                GetInstanceTenantListQuery.SettingKey);
        }

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

public sealed class InstanceOverviewCollectionLinkPolicy : ICollectionLinkPolicy<InstanceOverviewDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceOverviewDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
