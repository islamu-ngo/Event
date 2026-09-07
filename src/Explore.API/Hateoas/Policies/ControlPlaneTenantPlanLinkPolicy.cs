namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

public sealed class ControlPlaneTenantPlanDetailLinkPolicy : ILinkPolicy<ControlPlaneTenantPlanDetailDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ControlPlaneTenantPlanDetailDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return ViewLink(
            LinkRelations.Self,
            RouteNames.GetControlPlaneTenantPlanByKey,
            new { key = dto.Key },
            dto.DisplayName,
            dto.Key);

        yield return ViewLink(
            LinkRelations.Collection,
            RouteNames.GetControlPlaneTenantPlans,
            null,
            "Control-plane tenant plans",
            dto.Key);

        yield return UpdateLink(
            "create-version-draft",
            RouteNames.CreateControlPlaneTenantPlanVersionDraft,
            new { key = dto.Key },
            "Create plan version draft",
            CreateControlPlaneTenantPlanVersionDraftCommand.SettingKey,
            dto.Key);

        yield return ViewLink(
            "validate",
            RouteNames.ValidateControlPlaneTenantPlanDraft,
            null,
            "Validate tenant plan draft",
            dto.Key,
            ValidateControlPlaneTenantPlanDraftQuery.SettingKey,
            method: "POST");

        yield return ViewLink(
            "preview-diff",
            RouteNames.PreviewControlPlaneTenantPlanDiff,
            null,
            "Preview tenant plan diff",
            dto.Key,
            PreviewControlPlaneTenantPlanDiffQuery.SettingKey,
            method: "POST");

    }

    private static LinkDefinition ViewLink(
        string rel,
        string routeName,
        object? routeValues,
        string title,
        string planKey,
        string settingKey = GetControlPlaneTenantPlanListQuery.SettingKey,
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

internal static class ControlPlaneTenantPlanVersionLinks
{
    public static IEnumerable<LinkDefinition> GetLinks(
        string planKey,
        ControlPlaneTenantPlanVersionDto version)
    {
        if (version.StatusId == (int)TenantPlanStatusEnum.Draft)
        {
            yield return UpdateLink(
                "update-version-draft",
                RouteNames.UpdateControlPlaneTenantPlanVersionDraft,
                new { versionId = version.Id },
                "PATCH",
                "Update plan version draft",
                UpdateControlPlaneTenantPlanVersionDraftCommand.SettingKey,
                planKey,
                version.Id,
                sourceVersionId: null);

            yield return UpdateLink(
                LinkRelations.Publish,
                RouteNames.PublishControlPlaneTenantPlanVersion,
                new { versionId = version.Id },
                "POST",
                "Publish plan version",
                PublishControlPlaneTenantPlanVersionCommand.SettingKey,
                planKey,
                version.Id,
                sourceVersionId: null);
        }
        else if (version.StatusId == (int)TenantPlanStatusEnum.Published)
        {
            yield return UpdateLink(
                LinkRelations.Archive,
                RouteNames.ArchiveControlPlaneTenantPlanVersion,
                new { versionId = version.Id },
                "POST",
                "Archive plan version",
                ArchiveControlPlaneTenantPlanVersionCommand.SettingKey,
                planKey,
                version.Id,
                sourceVersionId: null);

            yield return UpdateLink(
                "clone",
                RouteNames.CloneControlPlaneTenantPlan,
                new { sourceVersionId = version.Id },
                "POST",
                "Clone plan version",
                CloneControlPlaneTenantPlanCommand.SettingKey,
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

public sealed class ControlPlaneTenantPlanCollectionLinkPolicy : ICollectionLinkPolicy<ControlPlaneTenantPlanListItemDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ControlPlaneTenantPlanListItemDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetControlPlaneTenantPlanByKey,
            new { key = dto.Key },
            "GET",
            dto.DisplayName,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetControlPlaneTenantPlanListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateControlPlaneTenantPlanDraft,
            null,
            "POST",
            "Create tenant plan draft",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                CreateControlPlaneTenantPlanDraftCommand.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "validate",
            RouteNames.ValidateControlPlaneTenantPlanDraft,
            null,
            "POST",
            "Validate tenant plan draft",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                ValidateControlPlaneTenantPlanDraftQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "preview-diff",
            RouteNames.PreviewControlPlaneTenantPlanDiff,
            null,
            "POST",
            "Preview tenant plan diff",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                PreviewControlPlaneTenantPlanDiffQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }
}
