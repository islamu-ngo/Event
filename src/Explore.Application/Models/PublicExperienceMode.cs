namespace Explore.Application.Models;

/// <summary>
/// Describes how the anonymous public site should present tenant-local event content.
/// </summary>
/// <remarks>
/// This is an Application/read-model vocabulary, not a Domain scope. Organization-centric mode
/// emphasizes one configured in-tenant organization/publisher while Tenant remains the isolation
/// and governance boundary. Audience and section segmentation should continue to be modeled with
/// existing event filters, categories, tags, groups, actors, and custom-property projections rather
/// than a workspace or organization-scope hierarchy.
/// </remarks>
public enum PublicExperienceMode
{
    /// <summary>
    /// Marketplace/directory posture where the public event catalog is the primary experience.
    /// </summary>
    DiscoveryCentric = 0,

    /// <summary>
    /// Public-site posture emphasizing a configured tenant-local primary organization/publisher.
    /// </summary>
    OrganizationCentric = 1
}
