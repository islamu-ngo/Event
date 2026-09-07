using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class RegistrationProviderHealthLinkPolicy : ILinkPolicy<RegistrationProviderBindingHealthDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationProviderBindingHealthDto dto, ClaimsPrincipal? user)
    {
        yield return RegistrationProviderManagementLinks.EventPermissionLink(
            LinkRelations.Self,
            RouteNames.GetRegistrationProviderHealth,
            new { eventId = dto.EventId, tenantId = dto.TenantId },
            HttpMethods.Get,
            "Registration provider health",
            AuthorizationActions.Events.ViewRegistrationProviderHealth,
            dto.TenantId,
            dto.EventId);

        yield return RegistrationProviderManagementLinks.EventPermissionLink(
            LinkRelations.Poll,
            RouteNames.PollRegistrationProviderReconciliation,
            new { eventId = dto.EventId, tenantId = dto.TenantId, bindingId = dto.BindingId },
            HttpMethods.Post,
            "Poll registration provider reconciliation",
            AuthorizationActions.Events.ManageRegistrationChannels,
            dto.TenantId,
            dto.EventId);
    }
}

public sealed class RegistrationProviderHealthCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationProviderBindingHealthDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationProviderBindingHealthDto dto, ClaimsPrincipal? user) => new RegistrationProviderHealthLinkPolicy().GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class RegistrationProviderQueueLinkPolicy : ILinkPolicy<RegistrationProviderParkedQueueItemDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationProviderParkedQueueItemDto dto, ClaimsPrincipal? user) => [];
}

public sealed class RegistrationProviderQueueCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationProviderParkedQueueItemDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationProviderParkedQueueItemDto dto, ClaimsPrincipal? user)
    {
        if (dto.EffectOutboxId.HasValue)
        {
            yield return RegistrationProviderManagementLinks.EventPermissionLink(
                LinkRelations.Retry,
                RouteNames.RetryRegistrationProviderParkedItem,
                new { eventId = dto.EventId, tenantId = dto.TenantId },
                HttpMethods.Post,
                "Retry parked registration provider item",
                AuthorizationActions.Events.ManageRegistrationChannels,
                dto.TenantId,
                dto.EventId);
        }

        if (dto.SubmissionId.HasValue || dto.EffectOutboxId.HasValue)
        {
            yield return RegistrationProviderManagementLinks.EventPermissionLink(
                LinkRelations.Resolve,
                RouteNames.ResolveRegistrationProviderQueueItem,
                new { eventId = dto.EventId, tenantId = dto.TenantId },
                HttpMethods.Post,
                "Resolve registration provider item",
                AuthorizationActions.Events.ManageRegistrationChannels,
                dto.TenantId,
                dto.EventId);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user, ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not RegistrationProviderEventCollectionContext context)
        {
            yield break;
        }

        yield return RegistrationProviderManagementLinks.EventPermissionLink(
            LinkRelations.ManualImport,
            RouteNames.QueueManualRegistrationProviderImport,
            new { context.TenantId, context.EventId },
            HttpMethods.Post,
            "Queue manual registration provider import",
            AuthorizationActions.Events.ManageRegistrationChannels,
            context.TenantId,
            context.EventId);
    }
}

public sealed class RegistrationProviderConnectionLinkPolicy : ILinkPolicy<RegistrationProviderConnectionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationProviderConnectionDto dto, ClaimsPrincipal? user)
    {
        yield return RegistrationProviderManagementLinks.TenantPermissionLink(LinkRelations.Self, RouteNames.GetRegistrationProviderConnection,
            new { tenantId = dto.TenantId, eventId = dto.EventId, connectionId = dto.Id }, HttpMethods.Get, "Registration provider connection", AuthorizationActions.Tenants.Update, dto.TenantId);
        yield return RegistrationProviderManagementLinks.TenantPermissionLink(LinkRelations.Edit, RouteNames.UpdateRegistrationProviderConnection,
            new { tenantId = dto.TenantId, eventId = dto.EventId, connectionId = dto.Id }, HttpMethods.Put, "Update registration provider connection", AuthorizationActions.Tenants.Update, dto.TenantId);
        yield return RegistrationProviderManagementLinks.TenantPermissionLink(LinkRelations.Delete, RouteNames.DeleteRegistrationProviderConnection,
            new { tenantId = dto.TenantId, eventId = dto.EventId, connectionId = dto.Id }, HttpMethods.Delete, "Delete registration provider connection", AuthorizationActions.Tenants.Update, dto.TenantId);
        yield return RegistrationProviderManagementLinks.TenantPermissionLink(LinkRelations.Origins, RouteNames.ReplaceRegistrationProviderApprovedOrigins,
            new { tenantId = dto.TenantId, eventId = dto.EventId, connectionId = dto.Id }, HttpMethods.Put, "Replace approved origins", AuthorizationActions.Tenants.Update, dto.TenantId);
    }
}

public sealed class RegistrationProviderConnectionCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationProviderConnectionDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationProviderConnectionDto dto, ClaimsPrincipal? user) => new RegistrationProviderConnectionLinkPolicy().GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user, ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not RegistrationProviderEventCollectionContext context)
        {
            yield break;
        }

        yield return RegistrationProviderManagementLinks.TenantPermissionLink(
            LinkRelations.ProviderCreate,
            RouteNames.CreateRegistrationProviderConnection,
            new { context.TenantId, context.EventId },
            HttpMethods.Post,
            "Create registration provider connection",
            AuthorizationActions.Tenants.Update,
            context.TenantId);
    }
}

public sealed class RegistrationProviderBindingLinkPolicy : ILinkPolicy<RegistrationProviderBindingDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationProviderBindingDto dto, ClaimsPrincipal? user)
    {
        yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Self, RouteNames.GetRegistrationProviderBinding,
            new { tenantId = dto.TenantId, eventId = dto.EventId, bindingId = dto.Id }, HttpMethods.Get, "Registration provider binding", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
        if (dto.StateId == (int)RegistrationProviderBindingStateEnum.Draft)
        {
            yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Edit, RouteNames.UpdateRegistrationProviderBinding,
                new { tenantId = dto.TenantId, eventId = dto.EventId, bindingId = dto.Id }, HttpMethods.Put, "Update registration provider binding", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
            yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Delete, RouteNames.DeleteRegistrationProviderBinding,
                new { tenantId = dto.TenantId, eventId = dto.EventId, bindingId = dto.Id }, HttpMethods.Delete, "Delete registration provider binding", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
            yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Mappings, RouteNames.ReplaceRegistrationProviderMappings,
                new { tenantId = dto.TenantId, eventId = dto.EventId, bindingId = dto.Id }, HttpMethods.Put, "Replace registration provider mappings", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
            if (dto.DriftClassId < (int)RegistrationProviderDriftClassEnum.MappingRequired)
            {
                yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Publish, RouteNames.PublishRegistrationProviderBinding,
                    new { tenantId = dto.TenantId, eventId = dto.EventId, bindingId = dto.Id }, HttpMethods.Post, "Publish registration provider binding", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
            }
        }
    }
}

public sealed class RegistrationProviderBindingCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationProviderBindingDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationProviderBindingDto dto, ClaimsPrincipal? user) => new RegistrationProviderBindingLinkPolicy().GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user, ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not RegistrationProviderEventCollectionContext context)
        {
            yield break;
        }

        yield return RegistrationProviderManagementLinks.EventPermissionLink(
            LinkRelations.ProviderCreate,
            RouteNames.CreateRegistrationProviderBinding,
            new { context.TenantId, context.EventId },
            HttpMethods.Post,
            "Create registration provider binding",
            AuthorizationActions.Events.ManageRegistrationChannels,
            context.TenantId,
            context.EventId);
        yield return RegistrationProviderManagementLinks.EventPermissionLink(
            LinkRelations.ManualImport,
            RouteNames.QueueManualRegistrationProviderImport,
            new { context.TenantId, context.EventId },
            HttpMethods.Post,
            "Queue manual registration provider import",
            AuthorizationActions.Events.ManageRegistrationChannels,
            context.TenantId,
            context.EventId);
    }
}

public sealed class RegistrationChannelLinkPolicy : ILinkPolicy<RegistrationChannelDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationChannelDto dto, ClaimsPrincipal? user)
    {
        yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Self, RouteNames.GetRegistrationChannels,
            new { tenantId = dto.TenantId, eventId = dto.EventId, workflowId = dto.RegistrationWorkflowId, requirementId = dto.RegistrationRequirementId }, HttpMethods.Get, "Registration channels", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
        yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Edit, RouteNames.UpdateRegistrationChannel,
            new { tenantId = dto.TenantId, eventId = dto.EventId, workflowId = dto.RegistrationWorkflowId, requirementId = dto.RegistrationRequirementId, channelId = dto.Id }, HttpMethods.Put, "Update registration channel", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
        yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Delete, RouteNames.DeleteRegistrationChannel,
            new { tenantId = dto.TenantId, eventId = dto.EventId, workflowId = dto.RegistrationWorkflowId, requirementId = dto.RegistrationRequirementId, channelId = dto.Id }, HttpMethods.Delete, "Delete registration channel", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
        if (!dto.IsNative && dto.RegistrationProviderBindingId is { } bindingId)
        {
            yield return RegistrationProviderManagementLinks.EventPermissionLink("launch-descriptor", RouteNames.GetRegistrationProviderLaunchDescriptor,
                new { tenantId = dto.TenantId, eventId = dto.EventId, workflowId = dto.RegistrationWorkflowId, requirementId = dto.RegistrationRequirementId, channelId = dto.Id, bindingId }, HttpMethods.Get, "Registration provider launch descriptor", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
        }
    }
}

public sealed class RegistrationChannelCollectionLinkPolicy : ICollectionLinkPolicy<RegistrationChannelDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationChannelDto dto, ClaimsPrincipal? user) => new RegistrationChannelLinkPolicy().GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user, ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not RegistrationProviderChannelCollectionContext context)
        {
            yield break;
        }

        yield return RegistrationProviderManagementLinks.EventPermissionLink(
            LinkRelations.ProviderCreate,
            RouteNames.CreateRegistrationChannel,
            new { context.TenantId, context.EventId, context.WorkflowId, context.RequirementId },
            HttpMethods.Post,
            "Create registration channel",
            AuthorizationActions.Events.ManageRegistrationChannels,
            context.TenantId,
            context.EventId);
    }
}

public sealed class RegistrationProviderLaunchDescriptorLinkPolicy : ILinkPolicy<RegistrationProviderLaunchDescriptorDto>, ICollectionLinkPolicy<RegistrationProviderLaunchDescriptorDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RegistrationProviderLaunchDescriptorDto dto, ClaimsPrincipal? user)
    {
        yield return RegistrationProviderManagementLinks.EventPermissionLink(LinkRelations.Self, RouteNames.GetRegistrationProviderLaunchDescriptor,
            new { tenantId = dto.TenantId, eventId = dto.EventId, workflowId = dto.WorkflowId, requirementId = dto.RequirementId, channelId = dto.ChannelId, bindingId = dto.BindingId },
            HttpMethods.Get, "Registration provider launch descriptor", AuthorizationActions.Events.ManageRegistrationChannels, dto.TenantId, dto.EventId);
    }

    public IEnumerable<LinkDefinition> GetItemLinks(RegistrationProviderLaunchDescriptorDto dto, ClaimsPrincipal? user) => GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

internal static class RegistrationProviderManagementLinks
{
    public static LinkDefinition EventPermissionLink(string relation, string routeName, object? routeValues, string method, string title, string action, Guid tenantId, Guid eventId) =>
        new LinkDefinition(relation, routeName, routeValues, method, title, RequiresAuth: true)
            .RequirePermission(action, ResourceKinds.Event, eventId.ToString("D"), new AuthorizationScope(TenantId: tenantId.ToString("D")), RegistrationProviderAuthorization.EventFacts(tenantId, eventId));

    public static LinkDefinition TenantPermissionLink(string relation, string routeName, object? routeValues, string method, string title, string action, Guid tenantId) =>
        new LinkDefinition(relation, routeName, routeValues, method, title, RequiresAuth: true)
            .RequirePermission(action, ResourceKinds.Tenant, tenantId.ToString("D"), new AuthorizationScope(TenantId: tenantId.ToString("D")), RegistrationProviderAuthorization.TenantFacts(tenantId));
}

public sealed class RegistrationProviderEventCollectionContext(Guid tenantId, Guid eventId) : ICollectionAuthorizationContext
{
    public Guid TenantId { get; } = tenantId;
    public Guid EventId { get; } = eventId;

    string ICollectionAuthorizationContext.AuthorizationResourceId => EventId.ToString("D");

    IAuthorizationFacts? ICollectionAuthorizationContext.AuthorizationFacts =>
        RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

// Workflow and requirement identifiers scope which channels are listed. Authorization stays on the
// parent event, so they are route context rather than policy facts.
public sealed class RegistrationProviderChannelCollectionContext(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId) : ICollectionAuthorizationContext
{
    public Guid TenantId { get; } = tenantId;
    public Guid EventId { get; } = eventId;
    public Guid WorkflowId { get; } = workflowId;
    public Guid RequirementId { get; } = requirementId;

    string ICollectionAuthorizationContext.AuthorizationResourceId => EventId.ToString("D");

    IAuthorizationFacts? ICollectionAuthorizationContext.AuthorizationFacts =>
        RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}
