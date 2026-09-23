using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Authorization;

public sealed class EventResourceAuthorizationFacts
{
    public EventResourcePolicySnapshot? Policy { get; }
    public Guid ResourceId => Policy?.Id ?? Access.Parent.EventId;
    public EventResourceAccessFacts Access { get; }
    public EventResourceManagementFacts Management { get; }
    public Guid ResourceVersion { get; }
    public Guid? StorageObjectId { get; }
    public string AttachmentGeneration { get; }
    public EventResourceParentModerationFacts? ParentModeration { get; }

    public EventResourceAuthorizationFacts(EventResource resource, EventResourceAccessFacts access,
        EventResourceManagementFacts management, string attachmentGeneration,
        EventResourceParentModerationFacts? parentModeration = null)
    {
        Policy = EventResourcePolicySnapshot.Capture(resource);
        Access = access;
        Management = management;
        ResourceVersion = resource.ConcurrencyStamp;
        StorageObjectId = resource.StorageObjectId;
        AttachmentGeneration = attachmentGeneration;
        ParentModeration = parentModeration;
    }

    /// <summary>Parent-event operations resolve the existing Event, never a fabricated persisted resource.</summary>
    public EventResourceAuthorizationFacts(EventResourceParentFacts parent, Guid parentVersion,
        Guid? subjectUserId, bool isMachineCaller, EventResourceManagementFacts management,
        EventResourceGovernancePolicy? governancePolicy)
    {
        Access = new(parent.TenantId, subjectUserId, isMachineCaller, parent, [], false, governancePolicy);
        Management = management;
        ResourceVersion = parentVersion;
        AttachmentGeneration = "parent";
    }

    internal EventResourceEvaluation Evaluate(EventResourceAuthorityRequest request,
        EventResourceProviderSnapshot? route, DateTimeOffset now)
    {
        bool parentTarget = Policy is null;
        bool creating = request.Action == "create";
        if (parentTarget != request.TargetsParentEvent
            || request.IsEventCollection && request.Action is not ("view-management" or "export")
            || request.ExpectedResourceVersion is { } expectedVersion && ResourceVersion != expectedVersion
            || request.ResourceId != ResourceId
            || request.TenantId != (Policy?.TenantId ?? Access.Parent.TenantId)
            || Access.TenantId != request.TenantId || Access.SubjectUserId != request.SubjectUserId
            || Access.IsMachineCaller != request.IsMachineCaller || string.IsNullOrWhiteSpace(AttachmentGeneration))
            return new(false, new(false, false, false), null);
        bool publicOnly = request.IsMachineCaller || request.SubjectUserId is null;
        var access = publicOnly
            ? new EventResourceAccessFacts(Access.TenantId, null, request.IsMachineCaller,
                Access.Parent, [], Access.PayloadSafetySatisfied, Access.GovernancePolicy)
            : Access;
        var decision = Policy is null ? new EventResourceAccessDecision(false, false, false)
            : EventResourceAccessRules.Evaluate(Policy, access, now);
        bool organizer = !publicOnly && Management.OrganizerControl.IsEffectiveAt(now);
        bool update = !publicOnly && Management.Permissions.Any(p =>
            p.Code == PermissionCodes.EventUpdate && p.Authority.IsEffectiveAt(now));
        bool publish = !publicOnly && Management.Permissions.Any(p =>
            p.Code == PermissionCodes.EventPublish && p.Authority.IsEffectiveAt(now));
        bool moderate = !publicOnly && Management.Moderation.IsEffectiveAt(now);
        bool management = Management.ManagementCeiling && Policy?.IsDeleted != true
            && !Access.Parent.EventDeleted && Access.Parent.TenantId == request.TenantId
            && (parentTarget || Access.Parent.EventId == Policy!.EventId);
        bool publication = Policy is not null && management && Management.PublicationCeiling && Access.PayloadSafetySatisfied
            && EventResourceAccessRules.IsGovernanceEligible(Policy, Access.GovernancePolicy)
            && Policy.HasPublishablePayload && EventResourceAccessRules.IsParentEligible(Policy, Access.Parent)
            && Policy.Availability.TryResolve(Access.Parent.Schedule, out _, out _)
            && Policy.PublicationStateId is (int)EventResourcePublicationStateEnum.Draft
                or (int)EventResourcePublicationStateEnum.Withdrawn;
        bool allowed = request.Action switch
        {
            "view" => decision.DiscloseMetadata,
            "access" or "download" => decision.CanAccess,
            "create" or "view-management" or "update" or "unpublish" or "archive" or "delete"
                or "view-audit" or "export" => !publicOnly && management && (organizer || update),
            "publish" => !publicOnly && publication && (organizer || update && publish),
            "moderate" => !publicOnly && management && moderate,
            _ => false
        };
        EventResourceProviderInput? input = publicOnly || route is null ? null : new(route,
            new(request.SubjectUserId!.Value, request.TenantId, Management.TenantMembership.IsEffectiveAt(now),
                organizer, update, publish, moderate),
            new(ResourceId, request.TenantId, Access.Parent.EventId, Policy?.EventSessionId,
                Policy?.PublicationStateId ?? 0, Policy?.DisclosureModeId ?? 0, Policy?.EventResourceKindId ?? 0,
                Policy?.EventResourceDeliveryTypeId ?? 0, decision.DiscloseMetadata, decision.DisclosePrivateMetadata,
                decision.CanAccess, management, publication, creating, allowed), request.Action,
            request.Action == "moderate" && route.ParentEventPolicy is { } parentRoute
                ? ParentModeration?.Evaluate(parentRoute, now) : null);
        return new(allowed, decision, input);
    }
}

public sealed record EventResourceTimedAuthority(
    bool IsCurrent, DateTimeOffset? ValidFromUtc = null, DateTimeOffset? ExpiresAtUtc = null)
{
    public bool IsEffectiveAt(DateTimeOffset instant) => IsCurrent
        && (!ValidFromUtc.HasValue || instant >= ValidFromUtc)
        && (!ExpiresAtUtc.HasValue || instant < ExpiresAtUtc);
}

public sealed record EventResourcePermissionGrant(string Code, EventResourceTimedAuthority Authority);

/// <summary>All grants are already scoped to the exact tenant, event, subject and organizer by the fresh reader.</summary>
public sealed class EventResourceManagementFacts
{
    public EventResourceTimedAuthority OrganizerControl { get; }
    public EventResourceTimedAuthority TenantMembership { get; }
    public EventResourceTimedAuthority Moderation { get; }
    public IReadOnlyList<EventResourcePermissionGrant> Permissions { get; }
    public bool ManagementCeiling { get; }
    public bool PublicationCeiling { get; }

    public EventResourceManagementFacts(EventResourceTimedAuthority organizerControl,
        EventResourceTimedAuthority tenantMembership, EventResourceTimedAuthority moderation,
        IEnumerable<EventResourcePermissionGrant> permissions, bool managementCeiling, bool publicationCeiling)
    {
        OrganizerControl = organizerControl;
        TenantMembership = tenantMembership;
        Moderation = moderation;
        Permissions = Array.AsReadOnly(permissions.ToArray());
        ManagementCeiling = managementCeiling;
        PublicationCeiling = publicationCeiling;
    }
}

internal sealed record EventResourceEvaluation(
    bool Allowed, EventResourceAccessDecision Disclosure, EventResourceProviderInput? ProviderInput);
