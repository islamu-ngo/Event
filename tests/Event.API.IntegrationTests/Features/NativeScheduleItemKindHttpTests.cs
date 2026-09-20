using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeScheduleItemKindHttpTests
{
    [Test]
    public async Task AnonymousCatalogue_PreservesScalarValuesAndNullableDescription()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.ScheduleItemKinds.AddRange(
                new ScheduleItemKind { Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First kind", Description = "Public description" },
                new ScheduleItemKind { Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second kind" });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/scheduleitemkind");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var catalogue = await response.Content.ReadFromJsonAsync<List<ScheduleItemKindListDto>>()
            ?? throw new InvalidOperationException("Expected a schedule-item-kind catalogue.");
        await Assert.That(catalogue.Single(item => item.Id == 8008)).IsEqualTo(new ScheduleItemKindListDto
        {
            Id = 8008,
            MasterCode = "NATIVE_FIRST",
            FullName = "First kind",
            Description = "Public description"
        });
        await Assert.That(catalogue.Single(item => item.Id == 8002)).IsEqualTo(new ScheduleItemKindListDto
        {
            Id = 8002,
            MasterCode = "NATIVE_SECOND",
            FullName = "Second kind"
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
            db.ScheduleItemKinds.RemoveRange(db.ScheduleItemKinds);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/scheduleitemkind");
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
        var operation = paths.GetProperty("/api/scheduleitemkind").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetScheduleItemKinds");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "ScheduleItemKind" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        await Assert.That(paths.TryGetProperty("/api/scheduleitemkind/{id}", out _)).IsFalse();
        var method = typeof(ScheduleItemKindController).GetMethod(nameof(ScheduleItemKindController.GetAll))
            ?? throw new InvalidOperationException("Expected the schedule-item-kind catalogue action.");
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }
}
