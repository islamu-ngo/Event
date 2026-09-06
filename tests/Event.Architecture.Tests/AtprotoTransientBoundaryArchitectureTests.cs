// ABOUTME: Inspects actual MVC route metadata for the private transient machine capability.
// ABOUTME: Guards the closed route set, dedicated authorization and generic replay suppression without reading source text.

using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

public sealed class AtprotoTransientBoundaryArchitectureTests
{
    [Test]
    public async Task RuntimeEndpoints_AreClosedMachineOnlyAndExcludedFromPublicDiscovery()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(AtprotoTransientStoreController).Assembly);
        builder.Services.AddApiVersioning().AddMvc();
        await using var app = builder.Build();
        app.MapControllers();
        Endpoint[] endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo
                == typeof(AtprotoTransientStoreController)).ToArray();
        await Assert.That(endpoints.Select(endpoint => ((RouteEndpoint)endpoint).RoutePattern.RawText ?? string.Empty))
            .IsEquivalentTo(new[] { "api/auth/atproto/transient/create", "api/auth/atproto/transient/read", "api/auth/atproto/transient/consume", "api/auth/atproto/transient/probe" });
        foreach (var endpoint in endpoints)
        {
            await Assert.That(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods).IsEquivalentTo(new[] { "POST" });
            await Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>()).IsNull();
            await Assert.That(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(auth => auth.AuthenticationSchemes ?? string.Empty))
                .IsEquivalentTo(new[] { AtprotoTransientAuthenticationDefaults.Scheme });
            await Assert.That(endpoint.Metadata.GetMetadata<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
            await Assert.That(endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName)
                .IsEqualTo(AtprotoTransientAuthenticationDefaults.RatePolicy);
        }
        var descriptions = app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(group => group.Items);
        await Assert.That(descriptions.Any(description => description.RelativePath?.StartsWith(
            "api/auth/atproto/transient/", StringComparison.Ordinal) == true)).IsFalse();
    }
}
