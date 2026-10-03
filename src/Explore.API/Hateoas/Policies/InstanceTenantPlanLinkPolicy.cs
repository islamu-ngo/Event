namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

public sealed class InstanceTenantPlanDetailLinkPolicy : ILinkPolicy<InstanceTenantPlanDetailDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceTenantPlanDetailDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return ViewLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminTenantPlanByKey,
            new { key = dto.Key },
            dto.DisplayName,
            dto.Key);

        yield return ViewLink(
            LinkRelations.Collection,
            RouteNames.GetInstanceAdminTenantPlans,
            null,
            "Instance administration tenant plans",
            dto.Key);

        yield return UpdateLink(
            "create-version-draft",
            RouteNames.CreateInstanceAdminTenantPlanVersionDraft,
            new { key = dto.Key },
            "Create plan version draft",
            CreateInstanceTenantPlanVersionDraftCommand.SettingKey,
            dto.Key);

        yield return ViewLink(
            "validate",
            RouteNames.ValidateInstanceAdminTenantPlanDraft,
            null,
            "Validate tenant plan draft",
            dto.Key,
            ValidateInstanceTenantPlanDraftQuery.SettingKey,
            method: "POST");

        yield return ViewLink(
            "preview-diff",
            RouteNames.PreviewInstanceAdminTenantPlanDiff,
            null,
            "Preview tenant plan diff",
            dto.Key,
            PreviewInstanceTenantPlanDiffQuery.SettingKey,
            method: "POST");

    }

    private static LinkDefinition ViewLink(
        string rel,
        string routeName,
        object? routeValues,
        string title,
        string planKey,
        string settingKey = GetInstanceTenantPlanListQuery.SettingKey,
        string method = "GET") =>
        new LinkDefinition(rel, routeName, routeValues, method, title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

    private static LinkDefinition UpdateLink(
        string rel,
        string routeName,
        object? routeValues,
        string title,
        string settingKey,
        string planKey,
        Guid? versionId = null,
        Guid? sourceVersionId = null) =>
        new LinkDefinition(rel, routeName, routeValues, "POST", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

internal static class InstanceTenantPlanVersionLinks
{
    public static IEnumerable<LinkDefinition> GetLinks(
        string planKey,
        InstanceTenantPlanVersionDto version)
    {
        if (version.StatusId == (int)TenantPlanStatusEnum.Draft)
        {
            yield return UpdateLink(
                "update-version-draft",
                RouteNames.UpdateInstanceAdminTenantPlanVersionDraft,
                new { versionId = version.Id },
                "PATCH",
                "Update plan version draft",
                UpdateInstanceTenantPlanVersionDraftCommand.SettingKey,
                planKey,
                version.Id,
                sourceVersionId: null);

            yield return UpdateLink(
                LinkRelations.Publish,
                RouteNames.PublishInstanceAdminTenantPlanVersion,
                new { versionId = version.Id },
                "POST",
                "Publish plan version",
                PublishInstanceTenantPlanVersionCommand.SettingKey,
                planKey,
                version.Id,
                sourceVersionId: null);
        }
        else if (version.StatusId == (int)TenantPlanStatusEnum.Published)
        {
            yield return UpdateLink(
                LinkRelations.Archive,
                RouteNames.ArchiveInstanceAdminTenantPlanVersion,
                new { versionId = version.Id },
                "POST",
                "Archive plan version",
                ArchiveInstanceTenantPlanVersionCommand.SettingKey,
                planKey,
                version.Id,
                sourceVersionId: null);

            yield return UpdateLink(
                "clone",
                RouteNames.CloneInstanceAdminTenantPlan,
                new { sourceVersionId = version.Id },
                "POST",
                "Clone plan version",
                CloneInstanceTenantPlanCommand.SettingKey,
                planKey,
                versionId: null,
                sourceVersionId: version.Id);
        }
    }

    private static LinkDefinition UpdateLink(
        string relation,
        string routeName,
        object routeValues,
        string method,
        string title,
        string settingKey,
        string planKey,
        Guid? versionId,
        Guid? sourceVersionId) =>
        new LinkDefinition(relation, routeName, routeValues, method, title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class InstanceTenantPlanCollectionLinkPolicy : ICollectionLinkPolicy<InstanceTenantPlanListItemDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceTenantPlanListItemDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminTenantPlanByKey,
            new { key = dto.Key },
            "GET",
            dto.DisplayName,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstanceTenantPlanListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateInstanceAdminTenantPlanDraft,
            null,
            "POST",
            "Create tenant plan draft",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                CreateInstanceTenantPlanDraftCommand.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "validate",
            RouteNames.ValidateInstanceAdminTenantPlanDraft,
            null,
            "POST",
            "Validate tenant plan draft",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                ValidateInstanceTenantPlanDraftQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "preview-diff",
            RouteNames.PreviewInstanceAdminTenantPlanDiff,
            null,
            "POST",
            "Preview tenant plan diff",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                PreviewInstanceTenantPlanDiffQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }
}
