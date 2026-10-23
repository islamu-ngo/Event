using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Explore.ServiceDefaults.Diagnostics;

/// <summary>
/// Captures declared routing metadata without treating it as effective authorization
/// or as an inventory of middleware-only surfaces.
/// </summary>
public static class EndpointSecurityInventory
{
    public static IReadOnlyList<EndpointSecurityDescription> Capture(IEnumerable<EndpointDataSource> sources)
    {
        return sources.SelectMany(source => source.Endpoints)
            .Distinct<Endpoint>(ReferenceEqualityComparer.Instance)
            .Select(endpoint =>
            {
                ControllerActionDescriptor? action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
                return new EndpointSecurityDescription(
                    endpoint.GetType().FullName ?? endpoint.GetType().Name,
                    endpoint.DisplayName,
                    endpoint is RouteEndpoint route ? route.RoutePattern.RawText : null,
                    endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? [],
                    endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    action?.ControllerTypeInfo.FullName,
                    action?.MethodInfo.Name,
                    endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Select(authorization => new EndpointAuthorizationDescription(
                            authorization.Policy, authorization.Roles, authorization.AuthenticationSchemes))
                        .ToArray(),
                    endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                    endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription == true ||
                    endpoint.Metadata.GetMetadata<IApiDescriptionVisibilityProvider>()?.IgnoreApi == true,
                    endpoint.Metadata.Select(metadata => metadata.GetType().FullName ?? metadata.GetType().Name)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray());
            })
            .OrderBy(endpoint => endpoint.RoutePattern, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.ControllerType, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.ActionName, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.EndpointName, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record EndpointSecurityDescription(
    string EndpointKind,
    string? DisplayName,
    string? RoutePattern,
    string[] HttpMethods,
    string? EndpointName,
    string? ControllerType,
    string? ActionName,
    EndpointAuthorizationDescription[] DeclaredAuthorization,
    bool AllowsAnonymous,
    bool ExcludedFromApiDescription,
    string[] MetadataTypes);

public sealed record EndpointAuthorizationDescription(
    string? Policy,
    string? Roles,
    string? AuthenticationSchemes);
