using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeDidCustodyTypeHttpTests
{
    [Test]
    public async Task AnonymousReads_PreserveValuesAndMissingDetailBehavior()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.DidCustodyTypes.AddRange(
                new DidCustodyType { Id = 8008, MasterCode = "TEST_SELF", FullName = "Test Self Custody", Description = "Self-custodied identity" },
                new DidCustodyType { Id = 8002, MasterCode = "TEST_MANAGED", FullName = "Test Managed Custody" });
            await db.SaveChangesAsync();
        }

        using var listResponse = await client.GetAsync("/api/DidCustodyType");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<DidCustodyTypeListDto>>()
            ?? throw new InvalidOperationException("Expected an custody-type array.");
        await Assert.That(list.Single(item => item.Id == 8008)).IsEqualTo(new DidCustodyTypeListDto
        {
            Id = 8008, MasterCode = "TEST_SELF", FullName = "Test Self Custody", Description = "Self-custodied identity"
        });
        await Assert.That(list.Single(item => item.Id == 8002)).IsEqualTo(new DidCustodyTypeListDto
        {
            Id = 8002, MasterCode = "TEST_MANAGED", FullName = "Test Managed Custody"
        });

        using var detailResponse = await client.GetAsync("/api/DidCustodyType/8002");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await detailResponse.Content.ReadFromJsonAsync<DidCustodyTypeDto>()).IsEqualTo(new DidCustodyTypeDto
        {
            Id = 8002, MasterCode = "TEST_MANAGED", FullName = "Test Managed Custody"
        });
        using var payload = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "fullName", "id", "masterCode" });

        // Preserve the existing Ok(null) response; its advertised 404 remains a separate transport issue.
        using var missing = await client.GetAsync("/api/DidCustodyType/2147483647");
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
            db.DidCustodyTypes.RemoveRange(db.DidCustodyTypes);
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync("/api/DidCustodyType");
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
            ("/api/didcustodytype", "GetDidCustodyTypeOptions", nameof(DidCustodyTypeController.GetAll), "LookupData"),
            ("/api/didcustodytype/{id}", "GetDidCustodyTypeOptionById", nameof(DidCustodyTypeController.GetById), "DetailData")
        ];
        foreach (var contract in expected)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty("get");
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").GetArrayLength()).IsEqualTo(1);
            await Assert.That(operation.GetProperty("tags")[0].GetString()).IsEqualTo("DidCustodyType");
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
            var method = typeof(DidCustodyTypeController).GetMethod(contract.Method)!;
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo(contract.Cache);
        }
        var detailOperation = paths.GetProperty("/api/didcustodytype/{id}").GetProperty("get");
        await Assert.That(detailOperation.GetProperty("responses").TryGetProperty("404", out _)).IsTrue();
        var idParameter = detailOperation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        await Assert.That(idParameter.GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("int32");
    }
}
