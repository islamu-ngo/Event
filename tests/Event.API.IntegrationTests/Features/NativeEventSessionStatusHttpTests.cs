using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventSessionStatusHttpTests
{
    [Test]
    public async Task AnonymousSeededCatalogue_PreservesGlobalLifecycleIdentities()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/eventsessionstatus");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<EventSessionStatusListDto>>()
            ?? throw new InvalidOperationException("Expected a session-status catalogue.");
        await Assert.That(list.Select(item => (item.Id, item.MasterCode))).IsEquivalentTo(new[]
        {
            (1, "DRAFT"), (2, "SUBMITTED"), (3, "UNDER_REVIEW"), (4, "APPROVED"), (5, "PUBLISHED"),
            (6, "REJECTED"), (7, "CANCELLED"), (8, "ARCHIVED"), (9, "COMPLETED"), (10, "MODERATED")
        });
        foreach (var status in list)
        {
            using var detail = await client.GetAsync($"/api/eventsessionstatus/{status.Id}");
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await detail.Content.ReadFromJsonAsync<EventSessionStatusDto>()).IsEqualTo(new EventSessionStatusDto
            {
                Id = status.Id, MasterCode = status.MasterCode, FullName = status.FullName, Description = status.Description
            });
        }
    }

    [Test]
    public async Task AnonymousReads_PreserveMappedValuesNullableDescriptionAndMissingDetail()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventSessionStatuses.AddRange(
                new EventSessionStatus { Id = 8008, MasterCode = "TEST_FIRST", FullName = "Test First", Description = "First lookup item" },
                new EventSessionStatus { Id = 8002, MasterCode = "TEST_SECOND", FullName = "Test Second" });
            await db.SaveChangesAsync();
        }

        using var listResponse = await client.GetAsync("/api/eventsessionstatus");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<EventSessionStatusListDto>>()
            ?? throw new InvalidOperationException("Expected a session-status array.");
        await Assert.That(list.Single(item => item.Id == 8008)).IsEqualTo(new EventSessionStatusListDto
        {
            Id = 8008, MasterCode = "TEST_FIRST", FullName = "Test First", Description = "First lookup item"
        });
        await Assert.That(list.Single(item => item.Id == 8002)).IsEqualTo(new EventSessionStatusListDto
        {
            Id = 8002, MasterCode = "TEST_SECOND", FullName = "Test Second"
        });

        using var detailResponse = await client.GetAsync("/api/eventsessionstatus/8002");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await detailResponse.Content.ReadFromJsonAsync<EventSessionStatusDto>()).IsEqualTo(new EventSessionStatusDto
        {
            Id = 8002, MasterCode = "TEST_SECOND", FullName = "Test Second"
        });
        using var payload = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { "fullName", "id", "masterCode" });

        // Existing Ok(null) produces 204 despite advertised 404; dispatch migration does not repair that mismatch.
        using var missing = await client.GetAsync("/api/eventsessionstatus/2147483647");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await missing.Content.ReadAsStringAsync()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ActualHost_ResolvesClosedDecoratedQueriesInIndependentScopes()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var list = first.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionStatusListQuery, List<EventSessionStatusListDto>>>();
        var detail = first.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionStatusDetailsQuery, EventSessionStatusDto>>();
        var otherList = second.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionStatusListQuery, List<EventSessionStatusListDto>>>();
        var otherDetail = second.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionStatusDetailsQuery, EventSessionStatusDto>>();
        await Assert.That(list).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSessionStatusListQuery, List<EventSessionStatusListDto>>>();
        await Assert.That(detail).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSessionStatusDetailsQuery, EventSessionStatusDto>>();
        await Assert.That(ReferenceEquals(list, first.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionStatusListQuery, List<EventSessionStatusListDto>>>())).IsTrue();
        await Assert.That(ReferenceEquals(detail, first.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionStatusDetailsQuery, EventSessionStatusDto>>())).IsTrue();
        await Assert.That(ReferenceEquals(list, otherList)).IsFalse();
        await Assert.That(ReferenceEquals(detail, otherDetail)).IsFalse();
        await Assert.That((await list.QueryAsync(new GetEventSessionStatusListQuery(), CancellationToken.None)).Count).IsEqualTo(10);
        await Assert.That((await otherDetail.QueryAsync(new GetEventSessionStatusDetailsQuery { Id = 5 }, CancellationToken.None)).MasterCode)
            .IsEqualTo("PUBLISHED");
        await Assert.That(await detail.QueryAsync(new GetEventSessionStatusDetailsQuery { Id = int.MaxValue }, CancellationToken.None)).IsNull();
        await Assert.That(typeof(EventSessionStatusController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType))
            .IsEquivalentTo(new[]
            {
                typeof(IQueryHandler<GetEventSessionStatusListQuery, List<EventSessionStatusListDto>>),
                typeof(IQueryHandler<GetEventSessionStatusDetailsQuery, EventSessionStatusDto>)
            });
        await Assert.That(ActivatorUtilities.CreateInstance<EventSessionStatusController>(first.ServiceProvider)).IsNotNull();
    }

    [Test]
    public async Task PublicContract_PreservesRouteMetadataAndCachePolicies()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Explore.slnx")))
            root = root.Parent;
        await using var schemaStream = File.OpenRead(Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("Could not locate the repository root."),
            "schemas", "openapi_islamu-event.json"));
        using var generated = await JsonDocument.ParseAsync(schemaStream);
        (string Path, string OperationId, string Method, string Cache)[] expected =
        [
            ("/api/eventsessionstatus", "GetEventSessionStatuses", nameof(EventSessionStatusController.GetAll), "LookupData"),
            ("/api/eventsessionstatus/{id}", "GetEventSessionStatusById", nameof(EventSessionStatusController.GetById), "DetailData")
        ];
        foreach (var contract in expected)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty("get");
            await Assert.That(JsonElement.DeepEquals(paths.GetProperty(contract.Path),
                generated.RootElement.GetProperty("paths").GetProperty(contract.Path))).IsTrue();
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.OperationId);
            await Assert.That(operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()))
                .IsEquivalentTo(new string?[] { "EventSessionStatus" });
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
            await Assert.That(operation.GetProperty("x-output-cache-policy").GetString()).IsEqualTo(contract.Cache);
            var method = typeof(EventSessionStatusController).GetMethod(contract.Method)!;
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<OutputCacheAttribute>()!.PolicyName).IsEqualTo(contract.Cache);
        }
        var detailOperation = paths.GetProperty("/api/eventsessionstatus/{id}").GetProperty("get");
        await Assert.That(detailOperation.GetProperty("responses").TryGetProperty("404", out _)).IsTrue();
        var idParameter = detailOperation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        await Assert.That(idParameter.GetProperty("schema").GetProperty("format").GetString()).IsEqualTo("int32");
        foreach (var name in new[] { "EventSessionStatusDto", "EventSessionStatusListDto" })
        {
            await Assert.That(JsonElement.DeepEquals(
                document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name),
                generated.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name))).IsTrue();
        }
    }
}
