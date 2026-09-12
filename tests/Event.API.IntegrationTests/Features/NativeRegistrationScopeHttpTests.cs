using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeRegistrationScopeHttpTests
{
    [Test]
    public async Task AnonymousCatalogue_PreservesScalarValuesAndNullableDescription()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.RegistrationScopes.AddRange(
                new RegistrationScope { Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First scope", Description = "Public description" },
                new RegistrationScope { Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second scope" });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/registrationscope");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var catalogue = await response.Content.ReadFromJsonAsync<List<RegistrationScopeListDto>>()
            ?? throw new InvalidOperationException("Expected a registration-scope catalogue.");
        await Assert.That(catalogue.Single(item => item.Id == 8008)).IsEqualTo(new RegistrationScopeListDto
        {
            Id = 8008, MasterCode = "NATIVE_FIRST", FullName = "First scope", Description = "Public description"
        });
        await Assert.That(catalogue.Single(item => item.Id == 8002)).IsEqualTo(new RegistrationScopeListDto
        {
            Id = 8002, MasterCode = "NATIVE_SECOND", FullName = "Second scope"
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
            db.RegistrationScopes.RemoveRange(db.RegistrationScopes);
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync("/api/registrationscope");
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
        var operation = paths.GetProperty("/api/registrationscope").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetRegistrationScopes");
        await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
            .IsEquivalentTo(new string?[] { "RegistrationScope" });
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
        await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo("LookupData");
        await Assert.That(paths.TryGetProperty("/api/registrationscope/{id}", out _)).IsFalse();
        var method = typeof(RegistrationScopeController).GetMethod(nameof(RegistrationScopeController.GetAll))
            ?? throw new InvalidOperationException("Expected the registration-scope catalogue action.");
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }
}
