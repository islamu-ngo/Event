namespace Explore.API.Hateoas;

using Explore.Application.Authorization;
using Explore.Application.Hateoas;

/// <summary>
/// Extensions for attaching permission checks to <see cref="LinkDefinition"/> instances.
/// Prefer the descriptor overload; it publishes the closed typed facts the provider evaluates.
/// </summary>
public static class LinkDefinitionPermissionExtensions
{
    /// <summary>
    /// Attaches a permission check using an <see cref="AuthorizationActions"/> string constant.
    /// Uses the specified <paramref name="resourceKind"/> directly.
    /// </summary>
    public static LinkDefinition RequirePermission(
        this LinkDefinition definition,
        string action,
        string resourceKind,
        string? resourceId = null,
        AuthorizationScope? scope = null,
        IAuthorizationFacts? facts = null)
    {
        return definition.WithPermission(resourceKind, action, resourceId, scope, facts);
    }

    /// <summary>
    /// Attaches a permission check using an <see cref="AuthorizationActions"/> string constant.
    /// Resolves the resource kind from the specified <paramref name="resourceType"/> via the registry.
    /// </summary>
    public static LinkDefinition RequirePermission(
        this LinkDefinition definition,
        string action,
        Type resourceType,
        string? resourceId = null,
        AuthorizationScope? scope = null,
        IAuthorizationFacts? facts = null)
    {
        var resourceKind = ResourceDescriptorRegistry.ResolveResourceKind(resourceType);

        return definition.WithPermission(resourceKind, action, resourceId, scope, facts);
    }

    /// <summary>
    /// Attaches a permission check using a resource descriptor to extract authorization metadata.
    /// <para>
    /// Preferred overload for link policies — it replaces manual dictionary construction with
    /// centralized, type-safe fact extraction from the DTO instance.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto)
    /// </code>
    /// </example>
    public static LinkDefinition RequirePermission<TResource>(
        this LinkDefinition definition,
        string action,
        IAuthorizableResourceDescriptor<TResource> descriptor,
        TResource resource)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(resource);

        return definition.WithPermission(
            descriptor.Kind,
            action,
            descriptor.GetResourceId(resource),
            descriptor.GetScope(resource),
            descriptor.GetFacts(resource));
    }
}
