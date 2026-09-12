using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeGroupMemberHttpTests
{
    [Test]
    public async Task AnonymousReads_PreserveEmptyHalAndMissingDetailWhileWritesRequireIdentity()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var missingId = Guid.CreateVersion7();
        using var list = await client.GetAsync($"/api/groupmember/{missingId}");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var collection = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        await Assert.That(collection.RootElement.GetProperty("_embedded")
            .GetProperty("items").GetArrayLength()).IsEqualTo(0);
        await Assert.That(collection.RootElement.GetProperty("_links")
            .TryGetProperty("self", out _)).IsTrue();
        using var detail = await client.GetAsync($"/api/groupmember/member/{missingId}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var problem = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(404);

        using var create = await client.PostAsJsonAsync("/api/groupmember",
            new { groupId = missingId, email = "member@example.test" });
        using var update = await client.PutAsJsonAsync("/api/groupmember/role",
            new { id = missingId, role = RoleEnum.GroupMember });
        using var delete = await client.DeleteAsync($"/api/groupmember/{missingId}");
        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AuthenticatedCommands_ReachNativeHandlersAndPreserveValidationProblems()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        var missingId = Guid.CreateVersion7();
        using var create = await client.PostAsJsonAsync("/api/groupmember",
            new { groupId = missingId, email = "member@example.test" });
        using var update = await client.PutAsJsonAsync("/api/groupmember/role",
            new { id = missingId, role = RoleEnum.GroupMember });
        using var delete = await client.DeleteAsync($"/api/groupmember/{missingId}");
        foreach (var response in new[] { create, update, delete })
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            await Assert.That(problem.RootElement.GetProperty("code").GetString())
                .IsEqualTo("validation_failed");
            await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(400);
        }
    }

    [Test]
    public async Task OpenApi_PreservesMembershipRoutesClassificationAndCachePolicies()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        (string Path, string Verb, string Id, string Classification, string? Cache)[] routes =
        [
            ("/api/groupmember/{groupId}", "get", RouteNames.GetGroupMembers, "Public", "ListData"),
            ("/api/groupmember/member/{id}", "get", RouteNames.GetGroupMemberById, "Public", "DetailData"),
            ("/api/groupmember", "post", RouteNames.CreateGroupMember, "Authenticated", null),
            ("/api/groupmember/role", "put", RouteNames.UpdateGroupMember, "Authenticated", null),
            ("/api/groupmember/{id}", "delete", RouteNames.DeleteGroupMember, "Authenticated", null)
        ];
        foreach (var route in routes)
        {
            var operation = paths.GetProperty(route.Path).GetProperty(route.Verb);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(route.Id);
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString())
                .IsEqualTo(route.Classification);
            if (route.Cache is not null)
            {
                await Assert.That(operation.GetProperty("x-output-cache-policy").GetString())
                    .IsEqualTo(route.Cache);
            }
            await Assert.That(operation.GetProperty("responses").TryGetProperty("200", out _)).IsTrue();
        }
    }
}
