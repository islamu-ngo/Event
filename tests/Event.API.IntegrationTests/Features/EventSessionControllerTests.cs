using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class EventSessionControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/eventsession";

    public EventSessionControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET Endpoints

    [Test]
    public async Task GetAll_ShouldReturnOk_WithPaginatedResult()
    {
        // Act
        var response = await _fixture.Client.GetAsync(BaseUrl);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("items");
    }

    [Test]
    [Arguments(1, 10)]
    [Arguments(1, 20)]
    [Arguments(2, 5)]
    public async Task GetAll_WithPaginationParams_ShouldReturnPaginatedResult(int pageNumber, int pageSize)
    {
        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}?pageNumber={pageNumber}&pageSize={pageSize}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetById_WithRandomId_ShouldReturnNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{id}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_WithInvalidGuidFormat_ShouldReturnNotFound()
    {
        // Act - ASP.NET Core route constraints reject non-GUID strings with 404 (no route match)
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/not-a-guid");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetByEvent_WithRandomEventId_ShouldReturnOk_WithEmptyList()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/by-event/{eventId}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetManagedByEvent_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/management/by-event/{eventId}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetManagedByEventRoute_UsesExplicitAuthenticatedManagementContract()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var action = typeof(EventSessionController).GetMethod(nameof(EventSessionController.GetManagedByEvent))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        // Assert
        await Assert.That(route.Template).IsEqualTo("management/by-event/{eventId:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetManagedEventSessionsByEvent);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();

        var requestAuthorization = typeof(GetManagedSessionsByEventRequest).GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(requestAuthorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(requestAuthorization.Action).IsEqualTo(AuthorizationActions.Events.ViewManagement);

        var secureRequest = (ISecureRequest)new GetManagedSessionsByEventRequest { EventId = eventId };
        await Assert.That(secureRequest.ResourceId).IsEqualTo(eventId.ToString());
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == StatusCodes.Status403Forbidden && attribute.Type == typeof(ProblemDetails))).IsTrue();
    }

    #endregion

    #region POST Endpoints

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var createDto = new CreateEventSessionDto
        {
            EventId = Guid.NewGuid(),
            Title = "Test Session",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(2)
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync(BaseUrl, createDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateDraft_WithoutAuth_ShouldReturnUnauthorized()
    {
        var createDto = new CreateDraftEventSessionRequestDto
        {
            EventId = Guid.NewGuid(),
            Title = "Draft Session"
        };

        var response = await _fixture.Client.PostAsJsonAsync($"{BaseUrl}/drafts", createDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Schedule_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var scheduleDto = new ScheduleEventSessionRequestDto
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1)
        };

        var response = await _fixture.Client.PostAsJsonAsync($"{BaseUrl}/{id}/schedule", scheduleDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Publish_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var publishDto = new PublishEventSessionRequestDto
        {
            ExpectedConcurrencyStamp = Guid.NewGuid()
        };

        var response = await _fixture.Client.PostAsJsonAsync($"{BaseUrl}/{id}/publish", publishDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task LifecycleRoutes_UseExplicitAuthenticatedContracts()
    {
        await AssertLifecyclePostRoute(
            nameof(EventSessionController.CreateDraft),
            "drafts",
            RouteNames.CreateDraftEventSession,
            typeof(CreateDraftEventSessionCommand),
            AuthorizationActions.Create,
            ResourceKinds.EventSession,
            expectsConflict: false);

        await AssertLifecyclePostRoute(
            nameof(EventSessionController.Schedule),
            "{id:guid}/schedule",
            RouteNames.ScheduleEventSession,
            typeof(ScheduleEventSessionCommand),
            AuthorizationActions.Update,
            ResourceKinds.EventSession,
            expectsConflict: true);

        await AssertLifecyclePostRoute(
            nameof(EventSessionController.Publish),
            "{id:guid}/publish",
            RouteNames.PublishEventSession,
            typeof(PublishEventSessionCommand),
            AuthorizationActions.Update,
            ResourceKinds.EventSession,
            expectsConflict: true);
    }

    #endregion

    #region PATCH Endpoints

    [Test]
    public async Task Update_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new UpdateEventSessionDto
        {
            Title = new UpdateEventSessionTitleDto
            {
                Value = OptionalUpdate<string?>.Set("Updated Session")
            }
        };

        // Act
        var response = await _fixture.Client.PatchAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateRoute_UsesPatchRouteIdAuthoritativeContract()
    {
        var action = typeof(EventSessionController).GetMethod(nameof(EventSessionController.Update))!;
        var route = action.GetCustomAttribute<HttpPatchAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("{id:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.UpdateEventSession);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();

        var commandAuthorization = typeof(UpdateEventSessionCommand).GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(commandAuthorization.Resource).IsEqualTo(ResourceKinds.EventSession);
        await Assert.That(commandAuthorization.Action).IsEqualTo(AuthorizationActions.Update);

        var id = Guid.NewGuid();
        var secureRequest = (ISecureRequest)new UpdateEventSessionCommand
        {
            EventSessionId = id,
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            EventSessionDto = new UpdateEventSessionDto
            {
                Title = new UpdateEventSessionTitleDto
                {
                    Value = OptionalUpdate<string?>.Set("Updated Session")
                }
            }
        };
        await Assert.That(secureRequest.ResourceId).IsEqualTo(id.ToString());

        await AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);
        await AssertProducesProblem(action, StatusCodes.Status409Conflict);
    }

    #endregion

    #region DELETE Endpoints

    [Test]
    public async Task Delete_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{id}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Delete_WhenPublishedTicketReferencesSession_ReturnsConflict()
    {
        Guid id = Guid.CreateVersion7();
        IMediator mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DeleteEventSessionCommand>(), Arg.Any<CancellationToken>()).Returns(
            BaseCommandResponse.Failure<Guid>(
                "event_session_ticket_entitlement_conflict",
                "Event session is referenced by a published ticket catalog.",
                id: id));
        var controller = new EventSessionController(
            mediator,
            Substitute.For<ILogger<EventSessionController>>(),
            Substitute.For<Explore.Application.Contracts.Infrastructure.ITenantContext>(),
            Substitute.For<IResourceAssembler<EventSessionDto, EventSessionListDto>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        ActionResult result = await controller.Delete(id, CancellationToken.None);

        ObjectResult conflict = (ObjectResult)result;
        await Assert.That(conflict.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(((ProblemDetails)conflict.Value!).Extensions["code"]).IsEqualTo("event_session_ticket_entitlement_conflict");
        await AssertProducesProblem(typeof(EventSessionController).GetMethod(nameof(EventSessionController.Delete))!, StatusCodes.Status409Conflict);
    }

    #endregion

    private static async Task AssertLifecyclePostRoute(
        string actionName,
        string template,
        string routeName,
        Type commandType,
        string authorizationAction,
        string resourceKind,
        bool expectsConflict)
    {
        var action = typeof(EventSessionController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<HttpPostAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        await Assert.That(route.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();

        var commandAuthorization = commandType.GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(commandAuthorization.Resource).IsEqualTo(resourceKind);
        await Assert.That(commandAuthorization.Action).IsEqualTo(authorizationAction);
        await AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);

        if (expectsConflict)
        {
            await AssertProducesProblem(action, StatusCodes.Status409Conflict);
        }
    }

    private static async Task AssertProducesProblem(MethodInfo action, int statusCode)
    {
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == typeof(ProblemDetails))).IsTrue();
    }
}
