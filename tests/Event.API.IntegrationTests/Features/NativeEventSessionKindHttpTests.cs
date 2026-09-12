using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventSessionKindHttpTests
{
    [Test]
    public async Task AnonymousCatalogue_PreservesScalarValuesAndNullableDescription()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventSessionKinds.AddRange(
                new EventSessionKind { Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First kind", Description = "Public description" },
                new EventSessionKind { Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second kind" });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/eventsessionkind");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var catalogue = await response.Content.ReadFromJsonAsync<List<EventSessionKindListDto>>()
            ?? throw new InvalidOperationException("Expected an event-session-kind catalogue.");
        await Assert.That(catalogue.Single(item => item.Id == 8008)).IsEqualTo(new EventSessionKindListDto
        {
            Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First kind", Description = "Public description"
        });
        await Assert.That(catalogue.Single(item => item.Id == 8002)).IsEqualTo(new EventSessionKindListDto
        {
            Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second kind"
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
            db.EventSessionKinds.RemoveRange(db.EventSessionKinds);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/eventsessionkind");
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
        var operation = paths.GetProperty("/api/eventsessionkind").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetEventSessionKinds");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "EventSessionKind" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        await Assert.That(paths.TryGetProperty("/api/eventsessionkind/{id}", out _)).IsFalse();
        var method = typeof(EventSessionKindController).GetMethod(nameof(EventSessionKindController.GetAll))
            ?? throw new InvalidOperationException("Expected the event-session-kind catalogue action.");
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }
}
