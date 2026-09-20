using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.CategoryType;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeCategoryTypeHttpTests
{
    [Test]
    public async Task AnonymousReads_PreserveValuesAndMissingDetailBehavior()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.CategoryTypes.AddRange(
                new CategoryType { Id = 8008, MasterCode = "TEST_FIRST", FullName = "Test First", Description = "First lookup item" },
                new CategoryType { Id = 8002, MasterCode = "TEST_SECOND", FullName = "Test Second" });
            await db.SaveChangesAsync();
        }

        using var listResponse = await client.GetAsync("/api/CategoryType");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<CategoryTypeListDto>>()
            ?? throw new InvalidOperationException("Expected a category-type array.");
        await Assert.That(list.Single(item => item.Id == 8008)).IsEqualTo(new CategoryTypeListDto
        {
            Id = 8008,
            MasterCode = "TEST_FIRST",
            FullName = "Test First"
        });
        await Assert.That(list.Single(item => item.Id == 8002)).IsEqualTo(new CategoryTypeListDto
        {
            Id = 8002,
            MasterCode = "TEST_SECOND",
            FullName = "Test Second"
        });

        using var detailResponse = await client.GetAsync("/api/CategoryType/8002");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await detailResponse.Content.ReadFromJsonAsync<CategoryTypeDto>()).IsEqualTo(new CategoryTypeDto
        {
            Id = 8002,
            MasterCode = "TEST_SECOND",
            FullName = "Test Second"
        });
        using var payload = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "fullName", "id", "masterCode" });

        // Preserve the existing Ok(null) response; no new not-found status is introduced by the dispatch migration.
        using var missing = await client.GetAsync("/api/CategoryType/2147483647");
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
            db.CategoryTypes.RemoveRange(db.CategoryTypes);
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync("/api/CategoryType");
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
            ("/api/categorytype", "GetCategoryTypeOptions", nameof(CategoryTypeController.GetAll), "LookupData"),
            ("/api/categorytype/{id}", "GetCategoryTypeOptionById", nameof(CategoryTypeController.GetById), "DetailData"),
            ("/api/categorytype/with-categories", "GetCategoryTypeOptionsWithCategories", nameof(CategoryTypeController.GetWithCategories), "LookupData")
        ];
        foreach (var contract in expected)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty("get");
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").GetArrayLength()).IsEqualTo(1);
            await Assert.That(operation.GetProperty("tags")[0].GetString()).IsEqualTo("CategoryType");
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
            var method = typeof(CategoryTypeController).GetMethod(contract.Method)!;
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo(contract.Cache);
        }
        var detailOperation = paths.GetProperty("/api/categorytype/{id}").GetProperty("get");
        await Assert.That(detailOperation.GetProperty("responses").TryGetProperty("404", out _)).IsFalse();
        var idParameter = detailOperation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        await Assert.That(idParameter.GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("int32");
    }
}
