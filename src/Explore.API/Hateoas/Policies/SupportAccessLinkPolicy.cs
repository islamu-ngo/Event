using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class SupportAccessSessionDetailLinkPolicy : ILinkPolicy<SupportAccessSessionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(SupportAccessSessionDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.ListSupportAccessSessions,
            new { targetTenantId = dto.TargetTenantId },
            "GET",
            "Support-access sessions")
            .RequirePermission(AuthorizationActions.SupportAccessSessions.List, ResourceDescriptors.SupportAccessSession, dto);

        yield return new LinkDefinition(
            "audit-events",
            RouteNames.GetSupportAccessAuditEvents,
            new { targetTenantId = dto.TargetTenantId, sessionId = dto.Id },
            "GET",
            "Support-access audit events")
            .RequirePermission(AuthorizationActions.SupportAccessSessions.ViewAudit, ResourceDescriptors.SupportAccessSession, dto);

        if (!dto.IsActive)
        {
            yield break;
        }

        if (IsActor(user, dto.ActorUserId))
        {
            yield return new LinkDefinition(
                "stop",
                RouteNames.StopSupportAccessSession,
                new { sessionId = dto.Id },
                "POST",
                "Stop support access",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.SupportAccessSessions.Stop, ResourceDescriptors.SupportAccessSession, dto);
        }

        yield return new LinkDefinition(
            "force-stop",
            RouteNames.ForceStopSupportAccessSession,
            new { sessionId = dto.Id },
            "POST",
            "Force-stop support access",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.SupportAccessSessions.ForceStop, ResourceDescriptors.SupportAccessSession, dto);
    }

    private static bool IsActor(ClaimsPrincipal? user, Guid? actorUserId)
    {
        return actorUserId.HasValue
            && user?.GetPlatformUserId() is { } userId
            && userId == actorUserId;
    }
}

public sealed class SupportAccessSessionCollectionLinkPolicy : ICollectionLinkPolicy<SupportAccessSessionDto>
{
    private readonly SupportAccessSessionDetailLinkPolicy _detailPolicy = new();

    public IEnumerable<LinkDefinition> GetItemLinks(SupportAccessSessionDto dto, ClaimsPrincipal? user) =>
        _detailPolicy.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "start",
            RouteNames.StartSupportAccessSession,
            null,
            "POST",
            "Start support access",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.SupportAccessSessions.Start, ResourceKinds.SupportAccessSession);
    }
}

public sealed class SupportAccessAuditEventDetailLinkPolicy : ILinkPolicy<SupportAccessAuditEventDto>
{
    public IEnumerable<LinkDefinition> GetLinks(SupportAccessAuditEventDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetSupportAccessAuditEvents,
            new { targetTenantId = dto.TargetTenantId, sessionId = dto.SupportAccessSessionId },
            "GET",
            "Support-access audit events")
            .RequirePermission(AuthorizationActions.SupportAccessSessions.ViewAudit, ResourceDescriptors.SupportAccessAuditEvent, dto);
    }
}

public sealed class SupportAccessAuditEventCollectionLinkPolicy : ICollectionLinkPolicy<SupportAccessAuditEventDto>
{
    private readonly SupportAccessAuditEventDetailLinkPolicy _detailPolicy = new();

    public IEnumerable<LinkDefinition> GetItemLinks(SupportAccessAuditEventDto dto, ClaimsPrincipal? user) =>
        _detailPolicy.GetLinks(dto, user);
}
