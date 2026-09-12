using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.StatusType;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeApprovalStatusHttpTests
{
    [Test]
    public async Task AnonymousCatalogue_PreservesScalarValuesAndNullableDescription()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.ApprovalStatuses.AddRange(
                new ApprovalStatus { Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First status", Description = "Public description" },
                new ApprovalStatus { Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second status" });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/approvalstatus");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var catalogue = await response.Content.ReadFromJsonAsync<List<StatusTypeListDto>>()
            ?? throw new InvalidOperationException("Expected an approval-status catalogue.");
        await Assert.That(catalogue.Single(item => item.Id == 8008)).IsEqualTo(new StatusTypeListDto
        {
            Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First status", Description = "Public description"
        });
        await Assert.That(catalogue.Single(item => item.Id == 8002)).IsEqualTo(new StatusTypeListDto
        {
            Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second status"
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
            db.ApprovalStatuses.RemoveRange(db.ApprovalStatuses);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/approvalstatus");
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
        var operation = paths.GetProperty("/api/approvalstatus").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetApprovalStatusOptions");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "ApprovalStatus" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        await Assert.That(paths.TryGetProperty("/api/approvalstatus/{id}", out _)).IsFalse();
        var method = typeof(ApprovalStatusController).GetMethod(nameof(ApprovalStatusController.GetAll))
            ?? throw new InvalidOperationException("Expected the approval-status catalogue action.");
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }
}
