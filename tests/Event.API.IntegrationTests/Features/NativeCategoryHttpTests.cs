using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.Categories.Requests.Commands;
using Explore.Application.Features.Categories.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeCategoryHttpTests
{
    [Test]
    public async Task Writes_PreserveTenantAuthorityValidationConcurrencyAndListInvalidation()
    {
        var checks = new ConcurrentQueue<AuthorizationRequest>();
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                CheckPredicate = check =>
                {
                    checks.Enqueue(check);
                    return true;
                }
            }
        };
        using var client = CreateAuthenticatedClient(factory);
        await SeedTenantAsync(factory);
        var baseline = await GetListAsync(client);
        var baselineCount = baseline.GetProperty("totalCount").GetInt32();

        using (var invalid = await client.PostAsJsonAsync("/api/category", new
        {
            masterCode = "INVALID_PARENT",
            fullName = "Invalid parent",
            parentId = Guid.NewGuid()
        }))
        {
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That((await ReadJsonAsync(invalid)).GetProperty("code").GetString())
                .IsEqualTo("validation_failed");
        }

        using var created = await client.PostAsJsonAsync("/api/category", new
        {
            masterCode = "NATIVE_CATEGORY",
            fullName = "000 Native Category"
        });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var createdBody = await ReadJsonAsync(created);
        var id = createdBody.GetProperty("id").GetGuid();
        await Assert.That(createdBody.GetProperty("success").GetBoolean()).IsTrue();
        var location = created.Headers.Location
            ?? throw new InvalidOperationException("Expected the category Location header.");
        await Assert.That(new Uri(new Uri("https://integration.test"), location).AbsolutePath)
            .IsEqualTo($"/api/category/{id}");

        var persisted = await ReadCategoryAsync(factory, id);
        await Assert.That(persisted.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        var createChecks = checks.Where(check => check.ResourceKind == ResourceKinds.Category
            && check.Action == AuthorizationActions.Create).ToArray();
        await Assert.That(createChecks).IsNotEmpty();
        await Assert.That(createChecks.All(check => check.ResourceId == nameof(CreateCategoryCommand)
            && check.Facts is TenantScopedAuthorizationFacts facts
            && facts.TenantId == PlatformDefaults.DefaultTenantId)).IsTrue();

        var afterCreate = await GetListAsync(client);
        await Assert.That(afterCreate.GetProperty("totalCount").GetInt32()).IsEqualTo(baselineCount + 1);
        await Assert.That(Items(afterCreate).Single(item => item.GetProperty("id").GetGuid() == id)
            .GetProperty("fullName").GetString()).IsEqualTo("000 Native Category");

        var update = new { fullName = new { value = "000 Native Updated" } };
        foreach (var ifMatch in new string?[] { null, $"W/\"{persisted.ConcurrencyStamp:D}\"" })
        {
            using var invalidHeader = await PatchAsync(client, id, update, ifMatch);
            await Assert.That(invalidHeader.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var forgedId = await PatchAsync(client, id,
            new { id = Guid.NewGuid(), fullName = new { value = "Forged" } },
            $"\"{persisted.ConcurrencyStamp:D}\""))
        {
            await Assert.That(forgedId.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var emptyUpdate = await PatchAsync(client, id, new { },
            $"\"{persisted.ConcurrencyStamp:D}\""))
        {
            await Assert.That(emptyUpdate.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var stale = await PatchAsync(client, id, update, $"\"{Guid.NewGuid():D}\""))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That((await ReadJsonAsync(stale)).GetProperty("type").GetString())
                .IsEqualTo("/problems/concurrent_update");
        }
        var afterRejectedUpdates = await ReadCategoryAsync(factory, id);
        await Assert.That(afterRejectedUpdates.FullName).IsEqualTo(persisted.FullName);
        await Assert.That(afterRejectedUpdates.ConcurrencyStamp).IsEqualTo(persisted.ConcurrencyStamp);

        using (var updated = await PatchAsync(client, id, update, $"\"{persisted.ConcurrencyStamp:D}\""))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await ReadJsonAsync(updated)).GetProperty("id").GetGuid()).IsEqualTo(id);
        }
        var afterUpdate = await ReadCategoryAsync(factory, id);
        await Assert.That(afterUpdate.FullName).IsEqualTo("000 Native Updated");
        await Assert.That(afterUpdate.ConcurrencyStamp).IsNotEqualTo(persisted.ConcurrencyStamp);
        var updatedList = await GetListAsync(client);
        await Assert.That(Items(updatedList).Single(item => item.GetProperty("id").GetGuid() == id)
            .GetProperty("fullName").GetString()).IsEqualTo("000 Native Updated");
        var updateChecks = checks.Where(check => check.ResourceKind == ResourceKinds.Category
            && check.Action == AuthorizationActions.Update).ToArray();
        await Assert.That(updateChecks).IsNotEmpty();
        await Assert.That(updateChecks.All(check => check.ResourceId == id.ToString())).IsTrue();

        using (var deleted = await client.DeleteAsync($"/api/category/{id}"))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(await deleted.Content.ReadAsStringAsync()).IsEqualTo(string.Empty);
        }
        var afterDelete = await GetListAsync(client);
        await Assert.That(afterDelete.GetProperty("totalCount").GetInt32()).IsEqualTo(baselineCount);
        await Assert.That(Items(afterDelete).Any(item => item.GetProperty("id").GetGuid() == id)).IsFalse();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await db.Categories.IgnoreQueryFilters().AnyAsync(category => category.Id == id)).IsFalse();
        }
        using var missingDelete = await client.DeleteAsync($"/api/category/{id}");
        await Assert.That(missingDelete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var deleteChecks = checks.Where(check => check.ResourceKind == ResourceKinds.Category
            && check.Action == AuthorizationActions.Delete).ToArray();
        await Assert.That(deleteChecks).IsNotEmpty();
        await Assert.That(deleteChecks.All(check => check.ResourceId == id.ToString())).IsTrue();
    }

    [Test]
    public async Task DeniedWrites_DoNotMutatePersistedCategories()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = CreateAuthenticatedClient(factory);
        await SeedTenantAsync(factory);
        var category = await SeedCategoryAsync(factory, "DENIED_CATEGORY");
        var count = await CountCategoriesAsync(factory);

        using var create = await client.PostAsJsonAsync("/api/category",
            new { masterCode = "DENIED_CREATE", fullName = "Denied create" });
        using var update = await PatchAsync(client, category.Id,
            new { fullName = new { value = "Denied update" } }, $"\"{category.ConcurrencyStamp:D}\"");
        using var delete = await client.DeleteAsync($"/api/category/{category.Id}");
        foreach (var response in new[] { create, update, delete })
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        }

        var persisted = await ReadCategoryAsync(factory, category.Id);
        await Assert.That(persisted.FullName).IsEqualTo(category.FullName);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(category.ConcurrencyStamp);
        await Assert.That(await CountCategoriesAsync(factory)).IsEqualTo(count);
    }

    [Test]
    public async Task PublicReads_PreserveHierarchyHalPaginationAndMissingDetail()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using var client = factory.CreateClient();
        await SeedTenantAsync(factory);
        var parent = await SeedCategoryAsync(factory, "000_NATIVE_PARENT");
        var child = await SeedCategoryAsync(factory, "000_NATIVE_CHILD", parent.Id);
        var count = await CountCategoriesAsync(factory);

        var list = await GetListAsync(client, "?pageNumber=1&pageSize=100");
        await Assert.That(list.GetProperty("pageNumber").GetInt32()).IsEqualTo(1);
        await Assert.That(list.GetProperty("pageSize").GetInt32()).IsEqualTo(100);
        var childItem = Items(list).Single(item => item.GetProperty("id").GetGuid() == child.Id);
        await Assert.That(childItem.GetProperty("parentFullName").GetString()).IsEqualTo(parent.FullName);
        await Assert.That(childItem.GetProperty("concurrencyStamp").GetGuid()).IsEqualTo(child.ConcurrencyStamp);
        await Assert.That(childItem.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(list.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(list.GetProperty("_links").TryGetProperty("create", out _)).IsFalse();

        using var detailResponse = await client.GetAsync($"/api/category/{child.Id}");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var detail = await ReadJsonAsync(detailResponse);
        await Assert.That(detail.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[]
            {
                "id", "concurrencyStamp", "masterCode", "fullName",
                "parentId", "parentFullName", "tenantId", "_links"
            });
        await Assert.That(detail.GetProperty("id").GetGuid()).IsEqualTo(child.Id);
        await Assert.That(detail.GetProperty("parentId").GetGuid()).IsEqualTo(parent.Id);
        await Assert.That(detail.GetProperty("parentFullName").GetString()).IsEqualTo(parent.FullName);
        await Assert.That(detail.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(detail.GetProperty("_links").EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { "self", "collection", "parent" });
        var parentHref = detail.GetProperty("_links").GetProperty("parent").GetProperty("href").GetString()
            ?? throw new InvalidOperationException("Expected the parent link.");
        await Assert.That(new Uri(new Uri("https://integration.test"), parentHref).AbsolutePath)
            .IsEqualTo($"/api/category/{parent.Id}");

        // Authenticated requests avoid the existing anonymous output-cache/Prefer variance.
        using var minimalClient = CreateAuthenticatedClient(factory);
        using var minimalResponse = await minimalClient.GetAsync($"/api/category/{child.Id}");
        await Assert.That(minimalResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadJsonAsync(minimalResponse)).TryGetProperty("_links", out _)).IsFalse();

        using var missing = await client.GetAsync($"/api/category/{Guid.NewGuid()}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missing.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        await Assert.That((await ReadJsonAsync(missing)).GetProperty("status").GetInt32()).IsEqualTo(404);
        await Assert.That(await CountCategoriesAsync(factory)).IsEqualTo(count);
        await Assert.That((await ReadCategoryAsync(factory, child.Id)).ConcurrencyStamp)
            .IsEqualTo(child.ConcurrencyStamp);
    }

    [Test]
    public async Task NativePorts_PreserveMissingDetailAndDeleteResults()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var details = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCategoryDetailsRequest, CategoryDto?>>();
        var delete = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteCategoryCommand, bool>>();
        var missingId = Guid.NewGuid();

        await Assert.That(await details.QueryAsync(new GetCategoryDetailsRequest(missingId), CancellationToken.None))
            .IsNull();
        await Assert.That(await delete.ExecuteAsync(new DeleteCategoryCommand { Id = missingId }, CancellationToken.None))
            .IsFalse();

    }

    [Test]
    public async Task HttpMetadata_PreservesRoutesOperationIdsAuthorizationAndResponseContracts()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var paths = (await ReadJsonAsync(response)).GetProperty("paths");
        (string Path, string Verb, string OperationId, string Method, string Classification, string? Cache, int[] Responses)[] contracts =
        [
            ("/api/category", "get", RouteNames.GetCategories, nameof(CategoryController.GetAll),
                "Public", "ListData", [200, 400]),
            ("/api/category/{id}", "get", RouteNames.GetCategoryById, nameof(CategoryController.GetById),
                "Public", "DetailData", [200, 404]),
            ("/api/category", "post", RouteNames.CreateCategory, nameof(CategoryController.Create),
                "Authenticated", null, [201, 400, 401, 403]),
            ("/api/category/{id}", "patch", RouteNames.UpdateCategory, nameof(CategoryController.Update),
                "Authenticated", null, [200, 400, 401, 403, 404, 409]),
            ("/api/category/{id}", "delete", RouteNames.DeleteCategory, nameof(CategoryController.Delete),
                "Authenticated", null, [204, 401, 404])
        ];

        foreach (var contract in contracts)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty(contract.Verb);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
                .IsEquivalentTo(new string?[] { "Category" });
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString())
                .IsEqualTo(contract.Classification);
            var method = typeof(CategoryController).GetMethod(contract.Method)
                ?? throw new InvalidOperationException("Expected the category action.");
            await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(attribute => attribute.StatusCode)).IsEquivalentTo(contract.Responses);
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo(contract.Cache);
            if (contract.Classification == "Public")
            {
                await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
                var content = operation.GetProperty("responses").GetProperty("200").GetProperty("content");
                await Assert.That(content.TryGetProperty(HateoasConstants.JsonMediaType, out _)).IsTrue();
                await Assert.That(content.TryGetProperty(HateoasConstants.HalJsonMediaType, out _)).IsTrue();
            }
            else
            {
                await Assert.That(method.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
            }
            foreach (var status in contract.Responses)
            {
                await Assert.That(operation.GetProperty("responses").TryGetProperty(
                    status.ToString(System.Globalization.CultureInfo.InvariantCulture), out _)).IsTrue();
            }
        }

        var patchParameters = paths.GetProperty("/api/category/{id}").GetProperty("patch")
            .GetProperty("parameters").EnumerateArray().ToArray();
        await Assert.That(patchParameters.Single(parameter => parameter.GetProperty("name").GetString() == "id")
            .GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("uuid");
        await Assert.That(patchParameters.Single(parameter => parameter.GetProperty("name").GetString() == "If-Match")
            .GetProperty("in").GetString()).IsEqualTo("header");
    }

    private static HttpClient CreateAuthenticatedClient(AuthenticatedWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        return client;
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, string? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/category/{id}")
        {
            Content = JsonContent.Create(body)
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> GetListAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync("/api/category" + query);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
    }

    private static JsonElement[] Items(JsonElement collection) =>
        collection.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static async Task SeedTenantAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        db.Tenants.Add(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            FullName = "Native category tenant",
            Slug = "native-category-tenant",
            TenantStatusId = status.Id,
            TenantStatus = status
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Category> SeedCategoryAsync(
        AuthenticatedWebApplicationFactory factory, string name, Guid? parentId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .SingleAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId);
        var category = new Category
        {
            Id = Guid.NewGuid(),
            MasterCode = name,
            FullName = name,
            ParentId = parentId,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static async Task<Category> ReadCategoryAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await db.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(category => category.Id == id);
    }

    private static async Task<int> CountCategoriesAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .Categories.IgnoreQueryFilters().CountAsync();
    }
}
