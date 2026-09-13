using System.Net;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Deployment;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Deployment;
using Explore.Application.Features.Deployment;
using Explore.Infrastructure.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeDeploymentHttpTests
{
    [Test]
    public async Task Controller_ConsumesOnlyTheExactQueryPortAndRetainsAnonymousRoute()
    {
        var controller = typeof(TicketingDeploymentCapabilitiesController);
        await Assert.That(controller.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { typeof(IQueryHandler<GetTicketingDeploymentCapabilitiesQuery, TicketingDeploymentCapabilityMatrixDto>) });
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template)
            .IsEqualTo("api/deployment/ticketing-capabilities");
        var action = controller.GetMethod(nameof(TicketingDeploymentCapabilitiesController.Get))!;
        await Assert.That(action.GetCustomAttribute<HttpGetAttribute>()!.Name)
            .IsEqualTo(RouteNames.GetTicketingDeploymentCapabilities);
        await Assert.That(action.IsDefined(typeof(AllowAnonymousAttribute))).IsTrue();
    }

    [Test]
    public async Task AnonymousGet_ReturnsTheRealCatalogThroughTheNativeHandlerWithPrivateNoStore()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ITicketingDeploymentCapabilityCatalog>();
        await Assert.That(catalog.GetType()).IsEqualTo(typeof(TicketingDeploymentCapabilityCatalog));
        var handler = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetTicketingDeploymentCapabilitiesQuery, TicketingDeploymentCapabilityMatrixDto>>();
        await Assert.That(handler.GetType().Namespace).IsEqualTo("Explore.Application.Operations.Decorators");
        var expected = JsonSerializer.SerializeToElement(catalog.GetSnapshot(), JsonSerializerOptions.Web);

        using var response = await client.GetAsync("/api/deployment/ticketing-capabilities");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value => value.Name == "no-cache")).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        await Assert.That(JsonElement.DeepEquals(body, expected)).IsTrue();
        await Assert.That(body.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
        await Assert.That(body.GetProperty("revision").GetString()).IsEqualTo("event-ticketing-lifecycle-2026-08-29");
        await Assert.That(body.GetProperty("referenceTopology").GetString()).IsEqualTo("split-postgresql-quartz-cluster");
        var capabilities = body.GetProperty("capabilities");
        await Assert.That(capabilities.GetArrayLength()).IsEqualTo(7);
        await Assert.That(capabilities.EnumerateArray().Count(item => item.GetProperty("status").GetString() == "test-only")).IsEqualTo(6);
        await Assert.That(capabilities[6].GetProperty("code").GetString()).IsEqualTo("protected-delayed-payout");
        await Assert.That(capabilities[6].GetProperty("status").GetString()).IsEqualTo("disabled");
        await Assert.That(capabilities[6].GetProperty("reasonCode").GetString()).IsEqualTo("separate_workstream_required");
    }
}
