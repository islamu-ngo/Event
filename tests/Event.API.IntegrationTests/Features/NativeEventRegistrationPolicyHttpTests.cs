using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventRegistrationPolicyHttpTests
{
    [Test]
    public async Task AnonymousCatalogue_PreservesScalarValuesAndNullableDescription()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventRegistrationPolicies.AddRange(
                new EventRegistrationPolicy { Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First policy", Description = "Public description" },
                new EventRegistrationPolicy { Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second policy" });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/eventregistrationpolicy");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var catalogue = await response.Content.ReadFromJsonAsync<List<EventRegistrationPolicyListDto>>()
            ?? throw new InvalidOperationException("Expected an event registration-policy catalogue.");
        await Assert.That(catalogue.Single(item => item.Id == 8008)).IsEqualTo(new EventRegistrationPolicyListDto
        {
            Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First policy", Description = "Public description"
        });
        await Assert.That(catalogue.Single(item => item.Id == 8002)).IsEqualTo(new EventRegistrationPolicyListDto
        {
            Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second policy"
        });
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var withoutDescription = payload.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == 8002);
        await Assert.That(withoutDescription.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { "id", "masterCode", "fullName" });
    }

    [Test]
    public async Task EmptyCatalogue_RemainsAnEmptyJsonArray()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventRegistrationPolicies.RemoveRange(db.EventRegistrationPolicies);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/eventregistrationpolicy");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("[]");
    }

    [Test]
    public async Task PublicContract_PreservesTheCatalogueRouteAndCachePolicy()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths.GetProperty("/api/eventregistrationpolicy").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetEventRegistrationPolicies");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "EventRegistrationPolicy" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        await Assert.That(paths.TryGetProperty("/api/eventregistrationpolicy/{id}", out _)).IsFalse();
        var method = typeof(EventRegistrationPolicyController).GetMethod(nameof(EventRegistrationPolicyController.GetAll))
            ?? throw new InvalidOperationException("Expected the event registration-policy catalogue action.");
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }
}
