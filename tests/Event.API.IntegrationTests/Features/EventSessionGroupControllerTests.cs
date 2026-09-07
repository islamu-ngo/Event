using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class EventSessionGroupControllerTests
{
    private const string BaseUrl = "/api/eventsessiongroup";

    private readonly ApiTestFixture _fixture;

    public EventSessionGroupControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task ProgramUpdateRoutes_AreCanonicalPatchContracts()
    {
        var agendaRoute = typeof(EventSessionAgendaItemController)
            .GetMethod(nameof(EventSessionAgendaItemController.Update))!
            .GetCustomAttribute<HttpPatchAttribute>()!;
        var groupRoute = typeof(EventSessionGroupController)
            .GetMethod(nameof(EventSessionGroupController.Update))!
            .GetCustomAttribute<HttpPatchAttribute>()!;

        await Assert.That(agendaRoute.Template).IsEqualTo("{id:guid}");
        await Assert.That(agendaRoute.Name).IsEqualTo(RouteNames.UpdateEventSessionAgendaItem);
        await Assert.That(groupRoute.Template).IsEqualTo("{id:guid}");
        await Assert.That(groupRoute.Name).IsEqualTo(RouteNames.UpdateEventSessionGroup);
    }

    [Test]
    public async Task CollectionEditLink_UsesPatchAndRouteOwnedId()
    {
        var id = Guid.NewGuid();
        var edit = new EventSessionGroupCollectionLinkPolicy()
            .GetItemLinks(new EventSessionGroupListDto
            {
                Id = id,
                EventId = Guid.NewGuid(),
                Name = "Main stage"
            }, null)
            .Single(link => link.Rel == LinkRelations.Edit);

        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(edit.RouteValues!.GetType().GetProperty("id")?.GetValue(edit.RouteValues))
            .IsEqualTo(id);
    }

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized()
    {
        var createDto = new CreateEventSessionGroupRequestDto
        {
            EventId = Guid.NewGuid(),
            Name = "Main stage",
            Slug = "main-stage",
            SortOrder = 10,
            IsPublished = true
        };

        var response = await _fixture.Client.PostAsJsonAsync(BaseUrl, createDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Update_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var updateDto = new UpdateEventSessionGroupRequestDto
        {
            Metadata = new UpdateEventSessionGroupMetadataDto { Name = "Updated main stage" }
        };

        var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/{id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.NewGuid()}\"");
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Delete_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{id}?eventId={eventId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AssignSession_WithoutAuth_ShouldReturnUnauthorized()
    {
        var groupId = Guid.NewGuid();
        var assignment = new AssignSessionToGroupRequestDto
        {
            EventId = Guid.NewGuid(),
            EventSessionGroupId = groupId,
            EventSessionId = Guid.NewGuid(),
            IsPrimary = true,
            SortOrder = 0
        };

        var response = await _fixture.Client.PostAsJsonAsync($"{BaseUrl}/{groupId}/sessions", assignment);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UnassignSession_WithoutAuth_ShouldReturnUnauthorized()
    {
        var groupId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{groupId}/sessions/{sessionId}?eventId={eventId}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
