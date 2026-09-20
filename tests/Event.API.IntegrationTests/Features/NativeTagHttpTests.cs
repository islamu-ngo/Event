using System.Collections.Concurrent;
using System.Globalization;
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
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.Tags.Requests.Commands;
using Explore.Application.Features.Tags.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Explore.Persistence;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeTagHttpTests
{
    [Test]
    public async Task Writes_PreserveValidationRouteAuthorityPartialUpdatesAndDeletion()
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
        await SeedTenantAsync(factory, PlatformDefaults.DefaultTenantId);
        var baselineCount = await CountTagsAsync(factory);

        using (var invalid = await client.PostAsJsonAsync("/api/tag",
            new { masterCode = "", fullName = "Invalid tag" }))
        {
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That((await ReadJsonAsync(invalid)).GetProperty("code").GetString())
                .IsEqualTo("validation_failed");
        }
        using (var forgedTenant = await client.PostAsJsonAsync("/api/tag",
            new { masterCode = "FORGED", fullName = "Forged", tenantId = Guid.NewGuid() }))
        {
            await Assert.That(forgedTenant.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        await Assert.That(await CountTagsAsync(factory)).IsEqualTo(baselineCount);

        using var created = await client.PostAsJsonAsync("/api/tag",
            new { masterCode = " NATIVE_TAG ", fullName = " Native tag " });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var createdBody = await ReadJsonAsync(created);
        var id = createdBody.GetProperty("id").GetGuid();
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await Assert.That(createdBody.GetProperty("success").GetBoolean()).IsTrue();
        var location = created.Headers.Location
            ?? throw new InvalidOperationException("Expected a Location header.");
        await Assert.That(new Uri(new Uri("https://integration.test"), location).AbsolutePath)
            .IsEqualTo($"/api/tag/{id}");
        var persisted = await ReadTagAsync(factory, id);
        await Assert.That(persisted.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(persisted.MasterCode).IsEqualTo(" NATIVE_TAG ");
        await Assert.That(persisted.FullName).IsEqualTo(" Native tag ");

        object[] rejectedUpdates =
        [
            new { },
            new { fullName = new { value = " " } },
            new { id = Guid.NewGuid(), fullName = new { value = "Forged identity" } },
            new { tenantId = Guid.NewGuid(), fullName = new { value = "Forged tenant" } }
        ];
        foreach (var body in rejectedUpdates)
        {
            using var rejected = await client.PatchAsJsonAsync($"/api/tag/{id}", body);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var afterRejection = await ReadTagAsync(factory, id);
        await Assert.That(afterRejection.MasterCode).IsEqualTo(persisted.MasterCode);
        await Assert.That(afterRejection.FullName).IsEqualTo(persisted.FullName);

        // Tags expose grouped PATCH, not Category's If-Match concurrency contract.
        using (var updated = await client.PatchAsJsonAsync($"/api/tag/{id}",
            new { fullName = new { value = " Updated tag " } }))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await ReadJsonAsync(updated);
            await Assert.That(body.GetProperty("id").GetGuid()).IsEqualTo(id);
            await Assert.That(body.GetProperty("success").GetBoolean()).IsTrue();
        }
        var afterNameUpdate = await ReadTagAsync(factory, id);
        await Assert.That(afterNameUpdate.FullName).IsEqualTo("Updated tag");
        await Assert.That(afterNameUpdate.MasterCode).IsEqualTo(persisted.MasterCode);
        using (var updated = await client.PatchAsJsonAsync($"/api/tag/{id}",
            new { masterCode = new { value = " UPDATED_TAG " } }))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var afterCodeUpdate = await ReadTagAsync(factory, id);
        await Assert.That(afterCodeUpdate.MasterCode).IsEqualTo("UPDATED_TAG");
        await Assert.That(afterCodeUpdate.FullName).IsEqualTo("Updated tag");
        using (var detail = await client.GetAsync($"/api/tag/{id}"))
        {
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await ReadJsonAsync(detail)).GetProperty("masterCode").GetString())
                .IsEqualTo("UPDATED_TAG");
        }
        var list = await GetListAsync(client, "?pageNumber=1&pageSize=100");
        await Assert.That(list.GetProperty("totalCount").GetInt32()).IsEqualTo(baselineCount + 1);
        await Assert.That(Items(list).Single(item => item.GetProperty("id").GetGuid() == id)
            .GetProperty("fullName").GetString()).IsEqualTo("Updated tag");

        using (var deleted = await client.DeleteAsync($"/api/tag/{id}"))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(await deleted.Content.ReadAsStringAsync()).IsEqualTo(string.Empty);
        }
        using var repeatedDelete = await client.DeleteAsync($"/api/tag/{id}");
        await Assert.That(repeatedDelete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var missing = await client.GetAsync($"/api/tag/{id}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await ReadJsonAsync(missing)).GetProperty("status").GetInt32()).IsEqualTo(404);
        await Assert.That(await CountTagsAsync(factory)).IsEqualTo(baselineCount);

        var createChecks = checks.Where(check => check.ResourceKind == ResourceKinds.Tag
            && check.Action == AuthorizationActions.Create).ToArray();
        await Assert.That(createChecks).IsNotEmpty();
        await Assert.That(createChecks.All(check => check.ResourceId == nameof(CreateTagCommand)
            && check.Facts is TenantScopedAuthorizationFacts facts
            && facts.TenantId == PlatformDefaults.DefaultTenantId)).IsTrue();
        var updateChecks = checks.Where(check => check.ResourceKind == ResourceKinds.Tag
            && check.Action == AuthorizationActions.Update).ToArray();
        await Assert.That(updateChecks).IsNotEmpty();
        await Assert.That(updateChecks.All(check => check.ResourceId == id.ToString()
            && check.Facts is TenantScopedAuthorizationFacts facts
            && facts.TenantId == PlatformDefaults.DefaultTenantId)).IsTrue();
        var deleteChecks = checks.Where(check => check.ResourceKind == ResourceKinds.Tag
            && check.Action == AuthorizationActions.Delete).ToArray();
        await Assert.That(deleteChecks).IsNotEmpty();
        await Assert.That(deleteChecks.All(check => check.ResourceId == id.ToString())).IsTrue();
    }

    [Test]
    public async Task DeniedAndAnonymousWrites_LeavePersistenceAndHalAuthorityIntact()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = CreateAuthenticatedClient(factory, minimal: false);
        using var anonymous = factory.CreateClient();
        await SeedTenantAsync(factory, PlatformDefaults.DefaultTenantId);
        var tag = await SeedTagAsync(factory, PlatformDefaults.DefaultTenantId, "DENIED");
        var count = await CountTagsAsync(factory);

        foreach (var caller in new[] { client, anonymous })
        {
            var expected = ReferenceEquals(caller, client)
                ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
            using var create = await caller.PostAsJsonAsync("/api/tag",
                new { masterCode = "DENIED_CREATE", fullName = "Denied create" });
            using var update = await caller.PatchAsJsonAsync($"/api/tag/{tag.Id}",
                new { fullName = new { value = "Denied update" } });
            using var delete = await caller.DeleteAsync($"/api/tag/{tag.Id}");
            foreach (var response in new[] { create, update, delete })
            {
                await Assert.That(response.StatusCode).IsEqualTo(expected);
            }
        }

        var persisted = await ReadTagAsync(factory, tag.Id);
        await Assert.That(persisted.MasterCode).IsEqualTo(tag.MasterCode);
        await Assert.That(persisted.FullName).IsEqualTo(tag.FullName);
        await Assert.That(await CountTagsAsync(factory)).IsEqualTo(count);
        using var detail = await client.GetAsync($"/api/tag/{tag.Id}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadJsonAsync(detail)).GetProperty("_links")
            .EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { "self", "collection" });
        var list = await GetListAsync(client);
        await Assert.That(list.GetProperty("_links").TryGetProperty("create", out _)).IsFalse();
    }

    [Test]
    public async Task Reads_PreserveTenantIsolationPaginationHalAndConditionalEtags()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using var anonymous = factory.CreateClient();
        using var authenticated = CreateAuthenticatedClient(factory, minimal: false);
        await SeedTenantAsync(factory, PlatformDefaults.DefaultTenantId);
        var foreignTenantId = Guid.NewGuid();
        await SeedTenantAsync(factory, foreignTenantId);
        var alpha = await SeedTagAsync(factory, PlatformDefaults.DefaultTenantId, "000_ALPHA");
        var zulu = await SeedTagAsync(factory, PlatformDefaults.DefaultTenantId, "ZZZ_ZULU");
        var foreign = await SeedTagAsync(factory, foreignTenantId, "000_FOREIGN");

        var list = await GetListAsync(anonymous, "?pageNumber=1&pageSize=1");
        await Assert.That(list.GetProperty("pageNumber").GetInt32()).IsEqualTo(1);
        await Assert.That(list.GetProperty("pageSize").GetInt32()).IsEqualTo(1);
        await Assert.That(list.GetProperty("totalCount").GetInt32()).IsEqualTo(2);
        var item = Items(list).Single();
        await Assert.That(item.GetProperty("id").GetGuid()).IsEqualTo(alpha.Id);
        await Assert.That(item.GetProperty("masterCode").GetString()).IsEqualTo(alpha.MasterCode);
        await Assert.That(item.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(item.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(list.GetProperty("_links").TryGetProperty("next", out _)).IsTrue();
        await Assert.That(list.GetProperty("_links").TryGetProperty("create", out _)).IsFalse();
        var secondPage = await GetListAsync(anonymous, "?pageNumber=2&pageSize=1");
        await Assert.That(Items(secondPage).Single().GetProperty("id").GetGuid()).IsEqualTo(zulu.Id);

        using (var detailResponse = await anonymous.GetAsync($"/api/tag/{alpha.Id}"))
        {
            await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var detail = await ReadJsonAsync(detailResponse);
            await Assert.That(detail.EnumerateObject().Select(property => property.Name))
                .IsEquivalentTo(new[] { "id", "masterCode", "fullName", "tenantId", "_links" });
            await Assert.That(detail.GetProperty("tenantId").GetGuid())
                .IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(detail.GetProperty("_links").EnumerateObject().Select(property => property.Name))
                .IsEquivalentTo(new[] { "self", "collection" });
        }
        using (var detailResponse = await authenticated.GetAsync($"/api/tag/{alpha.Id}"))
        {
            await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var detail = await ReadJsonAsync(detailResponse);
            await Assert.That(detail.GetProperty("_links").EnumerateObject().Select(property => property.Name))
                .IsEquivalentTo(new[] { "self", "collection", "edit", "delete" });
            var etag = detailResponse.Headers.ETag
                ?? throw new InvalidOperationException("Expected a representation ETag.");
            await Assert.That(etag.IsWeak).IsTrue();
            using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/tag/{alpha.Id}");
            conditional.Headers.IfNoneMatch.Add(etag);
            using var unchanged = await authenticated.SendAsync(conditional);
            await Assert.That(unchanged.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
            await Assert.That(await unchanged.Content.ReadAsStringAsync()).IsEqualTo(string.Empty);
        }
        var authorizedList = await GetListAsync(authenticated);
        await Assert.That(authorizedList.GetProperty("_links").TryGetProperty("create", out _)).IsTrue();

        // Authenticated requests avoid anonymous output-cache/Prefer variance.
        using var minimal = CreateAuthenticatedClient(factory);
        using (var response = await minimal.GetAsync($"/api/tag/{alpha.Id}"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await ReadJsonAsync(response)).TryGetProperty("_links", out _)).IsFalse();
        }
        using (var response = await anonymous.GetAsync($"/api/tag/{foreign.Id}"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That((await ReadJsonAsync(response)).GetProperty("status").GetInt32()).IsEqualTo(404);
        }
        using (var response = await minimal.PatchAsJsonAsync($"/api/tag/{foreign.Id}",
            new { fullName = new { value = "Cross-tenant update" } }))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using (var response = await minimal.DeleteAsync($"/api/tag/{foreign.Id}"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        await Assert.That((await ReadTagAsync(factory, foreign.Id)).FullName).IsEqualTo(foreign.FullName);
        await Assert.That((await ReadTagAsync(factory, alpha.Id)).FullName).IsEqualTo(alpha.FullName);
        await Assert.That(await CountTagsAsync(factory)).IsEqualTo(3);
    }

    [Test]
    public async Task NativePorts_PreserveManualValidationContextAuthorityNullableReadsAndBoolDeletion()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using var client = factory.CreateClient();
        await SeedTenantAsync(factory, PlatformDefaults.DefaultTenantId);
        using var scope = factory.Services.CreateScope();
        var create = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateTagCommand, BaseCommandResponse<Guid>>>();
        var update = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<UpdateTagCommand, BaseCommandResponse<Guid>>>();
        var delete = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteTagCommand, bool>>();
        var details = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTagDetailsRequest, TagDto?>>();
        var list = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetTagListRequest, PaginatedResult<TagListDto>>>();
        var otherTenantId = Guid.NewGuid();
        var missingId = Guid.NewGuid();

        await Assert.That(await details.QueryAsync(new GetTagDetailsRequest(missingId), CancellationToken.None))
            .IsNull();
        await Assert.That(await delete.ExecuteAsync(new DeleteTagCommand { Id = missingId }, CancellationToken.None))
            .IsFalse();
        var invalidCreate = await create.ExecuteAsync(new CreateTagCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            TagDto = new CreateTagDto { MasterCode = "", FullName = "Invalid" }
        }, CancellationToken.None);
        await Assert.That(invalidCreate.IsSuccess).IsFalse();
        await Assert.That(invalidCreate.Errors).IsNotEmpty();
        await Assert.That(await CountTagsAsync(factory)).IsEqualTo(0);

        var created = await create.ExecuteAsync(new CreateTagCommand
        {
            TenantId = otherTenantId,
            TagDto = new CreateTagDto { MasterCode = "NATIVE", FullName = "Native tag" }
        }, CancellationToken.None);
        await Assert.That(created.IsSuccess).IsTrue();
        var snapshot = await details.QueryAsync(new GetTagDetailsRequest(created.Id), CancellationToken.None)
            ?? throw new InvalidOperationException("Expected the created tag.");
        await Assert.That(snapshot.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);

        var invalidUpdate = await update.ExecuteAsync(new UpdateTagCommand
        {
            TagId = created.Id,
            TenantId = otherTenantId,
            Update = new UpdateTagDto()
        }, CancellationToken.None);
        await Assert.That(invalidUpdate.IsSuccess).IsFalse();
        await Assert.That(invalidUpdate.Errors).IsNotEmpty();
        await Assert.That(invalidUpdate.FailureCode).IsNull();
        var wrongTenant = await update.ExecuteAsync(new UpdateTagCommand
        {
            TagId = created.Id,
            TenantId = otherTenantId,
            Update = new UpdateTagDto { FullName = new() { Value = "Wrong tenant" } }
        }, CancellationToken.None);
        await Assert.That(wrongTenant.FailureCode).IsEqualTo(FailureCodes.NotFound);
        await Assert.That((await ReadTagAsync(factory, created.Id)).FullName).IsEqualTo("Native tag");

        var updated = await update.ExecuteAsync(new UpdateTagCommand
        {
            TagId = created.Id,
            TenantId = PlatformDefaults.DefaultTenantId,
            Update = new UpdateTagDto { FullName = new() { Value = " Updated native tag " } }
        }, CancellationToken.None);
        await Assert.That(updated.IsSuccess).IsTrue();
        await Assert.That(snapshot.FullName).IsEqualTo("Native tag");
        var page = await list.QueryAsync(new GetTagListRequest(0, int.MaxValue), CancellationToken.None);
        await Assert.That(page.PageNumber).IsEqualTo(1);
        await Assert.That(page.PageSize).IsEqualTo(100);
        await Assert.That(page.TotalCount).IsEqualTo(1);
        await Assert.That(page.Items.Single().Id).IsEqualTo(created.Id);
        await Assert.That(page.Items.Single().FullName).IsEqualTo("Updated native tag");
        await Assert.That(page.Items.Single().MasterCode).IsEqualTo("NATIVE");
        var smallestPage = await list.QueryAsync(new GetTagListRequest(0, 0), CancellationToken.None);
        await Assert.That(smallestPage.PageSize).IsEqualTo(1);
        await Assert.That(smallestPage.Items.Single().Id).IsEqualTo(created.Id);

        await Assert.That(await delete.ExecuteAsync(new DeleteTagCommand { Id = created.Id }, CancellationToken.None))
            .IsTrue();
        await Assert.That(await delete.ExecuteAsync(new DeleteTagCommand { Id = created.Id }, CancellationToken.None))
            .IsFalse();
        await Assert.That(await details.QueryAsync(new GetTagDetailsRequest(created.Id), CancellationToken.None))
            .IsNull();
        await Assert.That((await list.QueryAsync(new GetTagListRequest(), CancellationToken.None)).TotalCount)
            .IsEqualTo(0);
        await Assert.That(await CountTagsAsync(factory)).IsEqualTo(0);
    }

    [Test]
    public async Task HttpMetadata_PreservesExactTagRoutesAuthorizationAndResponseContracts()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var paths = (await ReadJsonAsync(response)).GetProperty("paths");
        (string Path, string Verb, string OperationId, string Method, string Classification,
            string? Cache, Type SuccessType, int[] Responses)[] contracts =
        [
            ("/api/tag", "get", RouteNames.GetTags, nameof(TagController.GetAll),
                "Public", "ListData", typeof(HalCollectionResource<TagListDto>), [200, 400]),
            ("/api/tag/{id}", "get", RouteNames.GetTagById, nameof(TagController.GetById),
                "Public", "DetailData", typeof(HalResource<TagDto>), [200, 404]),
            ("/api/tag", "post", RouteNames.CreateTag, nameof(TagController.Create),
                "Authenticated", null, typeof(BaseCommandResponse<Guid>), [201, 400, 401, 403]),
            ("/api/tag/{id}", "patch", RouteNames.UpdateTag, nameof(TagController.Update),
                "Authenticated", null, typeof(BaseCommandResponse<Guid>), [200, 400, 401, 404]),
            ("/api/tag/{id}", "delete", RouteNames.DeleteTag, nameof(TagController.Delete),
                "Authenticated", null, typeof(void), [204, 401, 404])
        ];
        foreach (var contract in contracts)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty(contract.Verb);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
                .IsEquivalentTo(new string?[] { "Tag" });
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString())
                .IsEqualTo(contract.Classification);
            var method = typeof(TagController).GetMethod(contract.Method)
                ?? throw new InvalidOperationException("Expected the tag action.");
            var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
            await Assert.That(responses.Select(attribute => attribute.StatusCode))
                .IsEquivalentTo(contract.Responses);
            await Assert.That(responses.Single(attribute => attribute.StatusCode < 300).Type)
                .IsEqualTo(contract.SuccessType);
            foreach (var error in responses.Where(attribute => attribute.StatusCode >= 400))
            {
                await Assert.That(error.Type).IsEqualTo(error.StatusCode == 400
                    ? typeof(ValidationProblemDetails) : typeof(ProblemDetails));
            }
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName)
                .IsEqualTo(contract.Cache);
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
                await Assert.That(operation.GetProperty("responses")
                    .TryGetProperty(status.ToString(CultureInfo.InvariantCulture), out _)).IsTrue();
            }
        }
        var parameters = paths.GetProperty("/api/tag/{id}").GetProperty("patch")
            .GetProperty("parameters").EnumerateArray().ToArray();
        await Assert.That(parameters.Single(parameter => parameter.GetProperty("name").GetString() == "id")
            .GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("uuid");
        await Assert.That(parameters.Any(parameter =>
            parameter.GetProperty("name").GetString() == "If-Match")).IsFalse();
    }

    private static HttpClient CreateAuthenticatedClient(
        AuthenticatedWebApplicationFactory factory, bool minimal = true)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        if (minimal)
        {
            client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        }
        return client;
    }

    private static async Task<JsonElement> GetListAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync("/api/tag" + query);
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

    private static async Task SeedTenantAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        db.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Native tag tenant",
            Slug = $"native-tag-{id:N}",
            TenantStatusId = status.Id,
            TenantStatus = status
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Tag> SeedTagAsync(
        AuthenticatedWebApplicationFactory factory, Guid tenantId, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(tenant => tenant.Id == tenantId);
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            MasterCode = name,
            FullName = name,
            TenantId = tenantId,
            Tenant = tenant
        };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    private static async Task<Tag> ReadTagAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await db.Tags.IgnoreQueryFilters().AsNoTracking().SingleAsync(tag => tag.Id == id);
    }

    private static async Task<int> CountTagsAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .Tags.IgnoreQueryFilters().CountAsync();
    }
}
