using Explore.API.Filters;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Middleware;

public class BlockInSingleTenantAttributeTests
{
    [Test]
    public async Task OnAuthorizationAsync_HiddenSingleTenantEndpoint_Returns404ProblemDetails()
    {
        var context = CreateAuthorizationContext(
            new DeploymentSettings { HidePlatformAdminInSingleTenant = true, Mode = DeploymentMode.SingleTenant },
            isSingleTenant: true);

        var attribute = new BlockInSingleTenantAttribute();

        await attribute.OnAuthorizationAsync(context);

        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(result.Value).IsTypeOf<ProblemDetails>();

        var details = (ProblemDetails)result.Value!;
        await Assert.That(details.Title).IsEqualTo("Endpoint unavailable in single-tenant mode");
        await Assert.That(details.Status).IsEqualTo(StatusCodes.Status404NotFound);
    }

    [Test]
    public async Task OnAuthorizationAsync_MultiTenantRequirement_Returns403ProblemDetails()
    {
        var context = CreateAuthorizationContext(
            new DeploymentSettings { Mode = DeploymentMode.SingleTenant },
            isSingleTenant: true);

        var attribute = new RequireMultiTenantAttribute();

        await attribute.OnAuthorizationAsync(context);

        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(result.Value).IsTypeOf<ProblemDetails>();

        var details = (ProblemDetails)result.Value!;
        await Assert.That(details.Title).IsEqualTo("Multi-tenant required");
        await Assert.That(details.Status).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task OnAuthorizationAsync_MultiTenantRuntime_AllowsRequest()
    {
        var context = CreateAuthorizationContext(
            new DeploymentSettings { HidePlatformAdminInSingleTenant = true, Mode = DeploymentMode.MultiTenant },
            isSingleTenant: false);

        var attribute = new BlockInSingleTenantAttribute();

        await attribute.OnAuthorizationAsync(context);

        await Assert.That(context.Result).IsNull();
    }

    private static AuthorizationFilterContext CreateAuthorizationContext(DeploymentSettings settings, bool isSingleTenant)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<DeploymentSettings>>(Options.Create(settings));

        var provider = Substitute.For<IDeploymentModeProvider>();
        provider.IsSingleTenantAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(isSingleTenant));
        provider.GetCurrentModeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(isSingleTenant ? DeploymentMode.SingleTenant : DeploymentMode.MultiTenant));
        services.AddSingleton(provider);

        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }
}
