namespace Explore.Application.Authorization;

/// <summary>
/// Generic implementation of <see cref="IAuthorizableResourceDescriptor{TResource}"/>
/// that extracts authorization metadata using delegate functions.
/// <para>
/// Create instances via the <see cref="ResourceDescriptors"/> catalog rather than constructing directly.
/// Each descriptor is immutable and thread-safe.
/// </para>
/// </summary>
/// <typeparam name="TResource">The DTO type from which authorization metadata is extracted.</typeparam>
public sealed class ResourceDescriptor<TResource> : IAuthorizableResourceDescriptor<TResource>
{
    private readonly Func<TResource, string> _getResourceId;
    private readonly Func<TResource, IAuthorizationFacts?> _getFacts;
    private readonly Func<TResource, AuthorizationScope>? _getScope;

    /// <summary>
    /// Creates a new resource descriptor with the given extraction functions.
    /// </summary>
    /// <param name="kind">The resource kind string. Must match a <see cref="ResourceKinds"/> constant.</param>
    /// <param name="getResourceId">Extracts the unique resource ID (typically primary key) from the DTO.</param>
    /// <param name="getFacts">
    /// Extracts the closed typed policy facts for the DTO. Every descriptor must supply facts;
    /// a resource without trusted facts cannot produce an allow decision.
    /// </param>
    /// <param name="getScope">
    /// Extracts the authorization scope for per-tenant/org policy resolution.
    /// When <c>null</c>, returns <see cref="AuthorizationScope.Empty"/>.
    /// </param>
    public ResourceDescriptor(
        string kind,
        Func<TResource, string> getResourceId,
        Func<TResource, IAuthorizationFacts?> getFacts,
        Func<TResource, AuthorizationScope>? getScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(getResourceId);
        ArgumentNullException.ThrowIfNull(getFacts);

        Kind = kind;
        _getResourceId = getResourceId;
        _getFacts = getFacts;
        _getScope = getScope;
    }

    /// <inheritdoc />
    public string Kind { get; }

    /// <inheritdoc />
    public string GetResourceId(TResource resource) => _getResourceId(resource);

    /// <inheritdoc />
    public IAuthorizationFacts? GetFacts(TResource resource) => _getFacts(resource);

    /// <inheritdoc />
    public AuthorizationScope GetScope(TResource resource) =>
        _getScope?.Invoke(resource) ?? AuthorizationScope.Empty;
}
