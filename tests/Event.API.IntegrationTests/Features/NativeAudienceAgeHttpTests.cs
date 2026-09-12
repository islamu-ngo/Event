using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.AudienceAge;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeAudienceAgeHttpTests
{
    [Test]
    public async Task AnonymousReads_PreserveLookupValuesAndMissingDetailResponse()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.AudienceAges.AddRange(
                new AudienceAge { Id = 8008, MasterCode = "TEST_ADULT", FullName = "Test Adults", MinAge = 18, Description = "Adult group" },
                new AudienceAge { Id = 8002, MasterCode = "TEST_YOUTH", FullName = "Test Youth", MinAge = 12, MaxAge = 17 });
            await db.SaveChangesAsync();
        }

        using var listResponse = await client.GetAsync("/api/AudienceAge");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<AudienceAgeListDto>>()
            ?? throw new InvalidOperationException("Expected an audience-age array.");
        await Assert.That(list.Single(item => item.Id == 8008)).IsEqualTo(new AudienceAgeListDto
        {
            Id = 8008, MasterCode = "TEST_ADULT", FullName = "Test Adults", MinAge = 18, Description = "Adult group"
        });
        await Assert.That(list.Single(item => item.Id == 8002)).IsEqualTo(new AudienceAgeListDto
        {
            Id = 8002, MasterCode = "TEST_YOUTH", FullName = "Test Youth", MinAge = 12, MaxAge = 17
        });

        using var detailResponse = await client.GetAsync("/api/AudienceAge/8002");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<AudienceAgeDto>();
        await Assert.That(detail).IsEqualTo(new AudienceAgeDto
        {
            Id = 8002, MasterCode = "TEST_YOUTH", FullName = "Test Youth", MinAge = 12, MaxAge = 17
        });
        using var payload = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "fullName", "id", "masterCode", "maxAge", "minAge" });
        await Assert.That(payload.RootElement.TryGetProperty("description", out _)).IsFalse();

        // The existing controller returns Ok(null), which MVC formats as 204, not its advertised 404.
        using var missing = await client.GetAsync("/api/AudienceAge/2147483647");
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
            db.AudienceAges.RemoveRange(db.AudienceAges);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/AudienceAge");
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
            ("/api/audienceage", "GetAudienceAgeOptions", nameof(AudienceAgeController.GetAll), "LookupData"),
            ("/api/audienceage/{id}", "GetAudienceAgeOptionById", nameof(AudienceAgeController.GetById), "DetailData")
        ];
        foreach (var contract in expected)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty("get");
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").GetArrayLength()).IsEqualTo(1);
            await Assert.That(operation.GetProperty("tags")[0].GetString()).IsEqualTo("AudienceAge");
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
            var method = typeof(AudienceAgeController).GetMethod(contract.Method)!;
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo(contract.Cache);
        }
        var detailOperation = paths.GetProperty("/api/audienceage/{id}").GetProperty("get");
        await Assert.That(detailOperation.GetProperty("responses").TryGetProperty("404", out _)).IsTrue();
        var idParameter = detailOperation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        await Assert.That(idParameter.GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("int32");
    }
}
