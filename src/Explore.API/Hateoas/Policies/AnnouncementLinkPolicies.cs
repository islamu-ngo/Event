using System.Security.Claims;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Hateoas;
using Explore.Domain.Constants;
using ISLAMU.Wire.Contracts.Announcements;

namespace Explore.API.Hateoas.Policies;

public sealed class AnnouncementSourceLinkPolicy(ITenantContext tenantContext)
    : ILinkPolicy<AnnouncementSourceResource>
{
    public IEnumerable<LinkDefinition> GetLinks(AnnouncementSourceResource dto, ClaimsPrincipal? user)
    {
        const string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        string read, write, kind, resourceId;
        object? routeValues = null;
        AuthorizationScope? scope = null;
        IAuthorizationFacts facts;
        string viewAction, updateAction;
        switch (dto.Target)
        {
            case AnnouncementSourceTarget.Instance when dto.Source.Owner.TenantId is null:
                read = RouteNames.GetInstanceAnnouncementSource;
                write = RouteNames.UpdateInstanceAnnouncement;
                kind = ResourceKinds.InstanceSetting;
                resourceId = key;
                facts = InstanceScopedAuthorizationFacts.Instance;
                viewAction = AuthorizationActions.InstanceSettings.View;
                updateAction = AuthorizationActions.InstanceSettings.Update;
                break;
            case AnnouncementSourceTarget.Tenant when dto.Source.Owner.TenantId is { } tenant
                && tenant != Guid.Empty && tenant == tenantContext.TenantId:
                read = RouteNames.GetTenantAnnouncementSource;
                write = RouteNames.UpdateTenantAnnouncement;
                kind = ResourceKinds.TenantSetting;
                resourceId = key;
                scope = new AuthorizationScope(TenantId: tenant.ToString("D"));
                facts = new TenantSettingAuthorizationFacts(tenant, key);
                viewAction = AuthorizationActions.TenantSettings.View;
                updateAction = AuthorizationActions.TenantSettings.Update;
                break;
            case AnnouncementSourceTarget.ManagedTenant when dto.Source.Owner.TenantId is { } managedTenant
                && managedTenant != Guid.Empty:
                read = RouteNames.GetManagedTenantAnnouncementSource;
                write = RouteNames.UpdateManagedTenantAnnouncement;
                routeValues = new { tenantId = managedTenant };
                kind = ResourceKinds.InstanceSetting;
                resourceId = "control-plane.tenant-effective-configuration";
                facts = InstanceScopedAuthorizationFacts.Instance;
                viewAction = AuthorizationActions.InstanceSettings.View;
                updateAction = AuthorizationActions.InstanceSettings.Update;
                break;
            default:
                yield break;
        }

        yield return new LinkDefinition(LinkRelations.Self, read, routeValues, "GET", RequiresAuth: true)
            .RequirePermission(viewAction, kind, resourceId, scope, facts);
        if (dto.Target != AnnouncementSourceTarget.Tenant || !dto.Source.IsLocked)
            yield return new LinkDefinition("publish", write, routeValues, "PUT", RequiresAuth: true)
                .RequirePermission(updateAction, kind, resourceId, scope, facts);
    }
}

public sealed class AnnouncementSourceCollectionLinkPolicy : ICollectionLinkPolicy<AnnouncementSourceResource>
{
    public IEnumerable<LinkDefinition> GetItemLinks(AnnouncementSourceResource dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class AnnouncementSelfLinkPolicy : ILinkPolicy<AnnouncementSelfStateDto>
{
    public IEnumerable<LinkDefinition> GetLinks(AnnouncementSelfStateDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Self, RouteNames.GetMyAnnouncementDismissal, null, "GET", RequiresAuth: true);
        var observed = dto.CurrentObservation;
        if (observed is null || observed.TenantId != dto.TenantId)
            yield break;
        var dismissed = dto.Marker?.Observation;
        if (dismissed is not null && dismissed.TenantId == observed.TenantId
            && dismissed.Owner == observed.Owner && dismissed.Edition == observed.Edition)
            yield break;
        yield return new("dismiss", RouteNames.DismissAnnouncement, null, "POST", RequiresAuth: true);
    }
}

public sealed class AnnouncementSelfCollectionLinkPolicy : ICollectionLinkPolicy<AnnouncementSelfStateDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(AnnouncementSelfStateDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

/// <summary>A publication receipt settles an attempt; only a fresh source read supplies another action.</summary>
public sealed class AnnouncementPublicationReceiptLinkPolicy : ILinkPolicy<AnnouncementPublicationReceiptDto>
{
    public IEnumerable<LinkDefinition> GetLinks(AnnouncementPublicationReceiptDto dto, ClaimsPrincipal? user) => [];
}

public sealed class AnnouncementPublicationReceiptCollectionLinkPolicy : ICollectionLinkPolicy<AnnouncementPublicationReceiptDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(AnnouncementPublicationReceiptDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class AnnouncementDismissalReceiptLinkPolicy : ILinkPolicy<AnnouncementDismissalReceiptDto>
{
    public IEnumerable<LinkDefinition> GetLinks(AnnouncementDismissalReceiptDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Self, RouteNames.GetMyAnnouncementDismissal, null, "GET", RequiresAuth: true);
    }
}

public sealed class AnnouncementDismissalReceiptCollectionLinkPolicy : ICollectionLinkPolicy<AnnouncementDismissalReceiptDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(AnnouncementDismissalReceiptDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
