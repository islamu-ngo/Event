namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Generates absolute, canonical public URLs for externally shared resources.
/// Handles reverse proxy awareness, tenant domain resolution, and scheme detection.
/// All returned URLs are absolute and safe for use in OG tags, share links, and calendar entries.
/// </summary>
public interface IPublicUrlBuilder
{
    /// <summary>
    /// Gets the absolute canonical URL for a specific event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <returns>Absolute URL, e.g. "https://events.example.org/events/550e8400-...".</returns>
    string GetEventUrl(Guid eventId);

    /// <summary>
    /// Gets the absolute canonical URL for an actor's public profile page.
    /// Routes to the appropriate profile page based on actor type (organization, user, group).
    /// </summary>
    /// <param name="actorId">The actor identifier.</param>
    /// <returns>Absolute URL for the actor's public profile.</returns>
    string GetActorUrl(Guid actorId);

    /// <summary>
    /// Gets the absolute canonical URL for an organization's public profile page.
    /// </summary>
    /// <param name="organizationId">The organization identifier.</param>
    /// <returns>Absolute URL for the organization's public profile.</returns>
    string GetOrganizationUrl(Guid organizationId);

    /// <summary>
    /// Gets the absolute canonical URL for a group's public profile page.
    /// </summary>
    /// <param name="groupId">The group identifier.</param>
    /// <returns>Absolute URL for the group's public profile.</returns>
    string GetGroupUrl(Guid groupId);

    /// <summary>
    /// Gets the absolute canonical URL for a user's public profile page.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>Absolute URL for the user's public profile.</returns>
    string GetUserProfileUrl(Guid userId);

    /// <summary>
    /// Gets the absolute base URL for the current tenant/deployment.
    /// Includes scheme, host, and path base. No trailing slash.
    /// </summary>
    /// <returns>Absolute base URL, e.g. "https://events.example.org".</returns>
    string GetBaseUrl();

    /// <summary>
    /// Gets the absolute public URL for a storage object image via the public proxy.
    /// Returns a stable, non-expiring URL suitable for OG image tags.
    /// </summary>
    /// <param name="storageObjectId">The storage object identifier.</param>
    /// <returns>Absolute URL to the public image proxy endpoint.</returns>
    string GetPublicImageUrl(Guid storageObjectId);
}
