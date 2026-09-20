using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.ActorType;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeActorTypeHttpTests
{
    [Test]
    public async Task AnonymousReads_PreserveValuesAndMissingDetailBehavior()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.ActorTypes.AddRange(
                new ActorType { Id = 8008, MasterCode = "TEST_SERVICE", FullName = "Test Service", Description = "Service actor" },
                new ActorType { Id = 8002, MasterCode = "TEST_BOT", FullName = "Test Bot" });
            await db.SaveChangesAsync();
        }

        using var listResponse = await client.GetAsync("/api/ActorType");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<ActorTypeListDto>>()
            ?? throw new InvalidOperationException("Expected an actor-type array.");
        await Assert.That(list.Single(item => item.Id == 8008)).IsEqualTo(new ActorTypeListDto
        {
            Id = 8008,
            MasterCode = "TEST_SERVICE",
            FullName = "Test Service",
            Description = "Service actor"
        });
        await Assert.That(list.Single(item => item.Id == 8002)).IsEqualTo(new ActorTypeListDto
        {
            Id = 8002,
            MasterCode = "TEST_BOT",
            FullName = "Test Bot"
        });

        using var detailResponse = await client.GetAsync("/api/ActorType/8002");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await detailResponse.Content.ReadFromJsonAsync<ActorTypeDto>()).IsEqualTo(new ActorTypeDto
        {
            Id = 8002,
            MasterCode = "TEST_BOT",
            FullName = "Test Bot"
        });
        using var payload = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "fullName", "id", "masterCode" });

        // Preserve the existing Ok(null) response; its advertised 404 remains a separate transport issue.
        using var missing = await client.GetAsync("/api/ActorType/2147483647");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await missing.Content.ReadAsStringAsync()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task EmptyCatalog_RemainsAnEmptyJsonArray()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.ActorTypes.RemoveRange(db.ActorTypes);
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync("/api/ActorType");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("[]");
    }

    [Test]
    public async Task PublicContract_PreservesOperationIdsTagsAndCachePolicies()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        (string Path, string OperationId, string Method, string Cache)[] expected =
        [
            ("/api/actortype", "GetActorTypes", nameof(ActorTypeController.GetAll), "LookupData"),
            ("/api/actortype/{id}", "GetActorTypeById", nameof(ActorTypeController.GetById), "DetailData")
        ];
        foreach (var contract in expected)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty("get");
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").GetArrayLength()).IsEqualTo(1);
            await Assert.That(operation.GetProperty("tags")[0].GetString()).IsEqualTo("ActorType");
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
            var method = typeof(ActorTypeController).GetMethod(contract.Method)!;
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo(contract.Cache);
        }
        var detailOperation = paths.GetProperty("/api/actortype/{id}").GetProperty("get");
        await Assert.That(detailOperation.GetProperty("responses").TryGetProperty("404", out _)).IsTrue();
        var idParameter = detailOperation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        await Assert.That(idParameter.GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("int32");
    }
}
