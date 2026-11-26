using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;

namespace Explore.Infrastructure.Services;

/// <summary>Projects learning access from the retained event creator and assignment sources.</summary>
internal static class HumanLearningEventAuthority
{
    internal static bool CanManage(
        HumanEventAssignmentSourceFacts sources, Guid eventId, DateTimeOffset now)
    {
        var target = sources.Events.SingleOrDefault(row =>
            row.Id == eventId && row.TenantId == sources.TenantId && !row.IsDeleted);
        if (target is null)
            return false;
        var creator = sources.Actors.SingleOrDefault(row =>
            row.Id == target.ActorId && !row.IsDeleted && !row.IsSuspended);
        return creator is not null
            && (creator.UserId == sources.PrincipalId
                || HumanEventAssignmentProjection.Authority(sources, now.UtcDateTime).Events
                    .TryGetValue(eventId, out var authority)
                    && (authority.IsOwner || authority.IsManager
                        || authority.PermissionCodes.Contains(PermissionCodes.EventUpdate)
                        || authority.PermissionCodes.Contains(PermissionCodes.EventRegistrationManage)
                        || authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage)));
    }
}
