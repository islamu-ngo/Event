namespace Explore.Domain.Enums;

public enum EventPublishingPolicyEnum
{
    /// <summary>
    /// Only organizations and groups can publish events.
    /// Users must be members with event:create permission.
    /// </summary>
    OrganizationAndGroupOnly = 1,

    /// <summary>
    /// Organizations and groups publish officially.
    /// Any authenticated user can also report events (marked as user-reported, subject to moderation).
    /// </summary>
    OrganizationGroupAndUserReported = 2
}
