using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.EventType;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventTypeHttpTests
{
    [Test]
    public async Task AnonymousCatalogue_ReturnsGlobalAndCurrentTenantTypesWithoutForeignRows()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory);

        using var response = await client.GetAsync("/api/eventtype");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var catalogue = await response.Content.ReadFromJsonAsync<List<EventTypeListDto>>()
            ?? throw new InvalidOperationException("Expected an event-type catalogue.");
        await Assert.That(catalogue.Single(item => item.Id == 8008)).IsEqualTo(new EventTypeListDto
        {
            Id = 8008,
            MasterCode = "NATIVE_GLOBAL",
            FullName = "Global event type",
            Description = "Shared type"
        });
        await Assert.That(catalogue.Single(item => item.Id == 8002)).IsEqualTo(new EventTypeListDto
        {
            Id = 8002,
            MasterCode = "NATIVE_LOCAL",
            FullName = "Current tenant type"
        });
        await Assert.That(catalogue.Any(item => item.Id == 8010)).IsFalse();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in payload.RootElement.EnumerateArray())
        {
            await Assert.That(item.TryGetProperty("tenantId", out _)).IsFalse();
            await Assert.That(item.TryGetProperty("tenant", out _)).IsFalse();
        }
        var local = payload.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == 8002);
        await Assert.That(local.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { "id", "masterCode", "fullName" });
    }

    [Test]
    public async Task CatalogueWithOnlyForeignTypes_ReturnsEmptyJsonArray()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var foreignTenantId = await SeedAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventTypes.RemoveRange(await db.EventTypes.ToListAsync());
            await db.SaveChangesAsync();
            await Assert.That(await db.EventTypes.IgnoreQueryFilters()
                .AnyAsync(item => item.TenantId == foreignTenantId)).IsTrue();
        }

        using var response = await client.GetAsync("/api/eventtype");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("[]");
    }

    [Test]
    public async Task PublicContract_PreservesTheSingleCatalogueRouteAndCachePolicy()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        var operation = paths.GetProperty("/api/eventtype").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetEventTypes");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "EventType" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        await Assert.That(paths.TryGetProperty("/api/eventtype/{id}", out _)).IsFalse();
        var method = typeof(EventTypeController).GetMethod(nameof(EventTypeController.GetAll))
            ?? throw new InvalidOperationException("Expected the event-type catalogue action.");
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }

    private static async Task<Guid> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var currentTenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            FullName = "Event-type tenant",
            Slug = "event-type-tenant",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var otherTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Other event-type tenant",
            Slug = "other-event-type-tenant",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        db.Tenants.AddRange(currentTenant, otherTenant);
        db.EventTypes.AddRange(
            new EventType
            {
                Id = 8008,
                MasterCode = "NATIVE_GLOBAL",
                FullName = "Global event type",
                Description = "Shared type"
            },
            new EventType
            {
                Id = 8002,
                MasterCode = "NATIVE_LOCAL",
                FullName = "Current tenant type",
                TenantId = currentTenant.Id,
                Tenant = currentTenant
            },
            new EventType
            {
                Id = 8010,
                MasterCode = "NATIVE_FOREIGN",
                FullName = "Foreign tenant type",
                TenantId = otherTenant.Id,
                Tenant = otherTenant
            });
        await db.SaveChangesAsync();
        return otherTenant.Id;
    }
}
