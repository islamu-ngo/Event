using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventSessionSpeakerControllerTests
{
    [Test]
    public async Task ManagementRoutes_UseStableCanonicalRouteNames()
    {
        await AssertRoute(
            nameof(EventSessionSpeakerController.GetBySession),
            typeof(HttpGetAttribute),
            "management/by-session/{eventSessionId:guid}",
            RouteNames.GetEventSessionSpeakersBySession);
        await AssertRoute(
            nameof(EventSessionSpeakerController.Create),
            typeof(HttpPostAttribute),
            "management/by-session/{eventSessionId:guid}",
            RouteNames.CreateEventSessionSpeaker);
        await AssertRoute(
            nameof(EventSessionSpeakerController.Update),
            typeof(HttpPatchAttribute),
            "management/{id:guid}",
            RouteNames.UpdateEventSessionSpeaker);
        await AssertRoute(
            nameof(EventSessionSpeakerController.Delete),
            typeof(HttpDeleteAttribute),
            "management/by-session/{eventSessionId:guid}/{id:guid}",
            RouteNames.DeleteEventSessionSpeaker);
    }

    [Test]
    public async Task Controller_ConsumesOnlyExactSpeakerOperationPortsAndAssembler()
    {
        var parameters = typeof(EventSessionSpeakerController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType);

        await Assert.That(parameters).IsEquivalentTo(new[]
        {
            typeof(IQueryHandler<GetSpeakersBySessionQuery, List<EventSessionSpeakerListDto>>),
            typeof(ICommandHandler<CreateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<DeleteEventSessionSpeakerCommand, bool>),
            typeof(IResourceAssembler<EventSessionSpeakerDto, EventSessionSpeakerListDto>)
        });
    }

    [Test]
    public async Task DetailEditLink_UsesOnlyRelationshipIdForCanonicalPatchRoute()
    {
        var assignmentId = Guid.NewGuid();
        var policy = new EventSessionSpeakerDetailLinkPolicy();
        var edit = policy.GetLinks(new EventSessionSpeakerDto
        {
            Id = assignmentId,
            EventSessionId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ActorId = Guid.NewGuid()
        }, null).Single(link => link.Rel == LinkRelations.Edit);

        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateEventSessionSpeaker);
        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(edit.RouteValues!.GetType().GetProperty("id")?.GetValue(edit.RouteValues))
            .IsEqualTo(assignmentId);
        await Assert.That(edit.RouteValues.GetType().GetProperty("eventSessionId")).IsNull();
    }

    [Test]
    public async Task CollectionEditLink_UsesOnlyRelationshipIdForCanonicalPatchRoute()
    {
        var assignmentId = Guid.NewGuid();
        var edit = new EventSessionSpeakerCollectionLinkPolicy()
            .GetItemLinks(new EventSessionSpeakerListDto
            {
                Id = assignmentId,
                EventSessionId = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid()
            }, null)
            .Single(link => link.Rel == LinkRelations.Edit);

        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(edit.RouteValues!.GetType().GetProperty("id")?.GetValue(edit.RouteValues))
            .IsEqualTo(assignmentId);
        await Assert.That(edit.RouteValues.GetType().GetProperty("eventSessionId")).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not-a-stamp")]
    [Arguments("01900000-0000-7000-8000-000000000099")]
    public async Task Update_WhenIfMatchIsInvalid_ReturnsValidationProblemDetails(string? ifMatch)
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            $"/api/eventsessionspeaker/management/{Guid.NewGuid():D}",
            new UpdateEventSessionSpeakerDto
            {
                Actor = new UpdateEventSessionSpeakerActorDto { ActorId = Guid.NewGuid() }
            });
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        using var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Event session speaker validation failed");
    }

    [Test]
    public async Task Update_InvalidIfMatchRejectsBeforeExactNativePortIngress()
    {
        var controller = new EventSessionSpeakerController(
            null!,
            null!,
            new RejectDispatchUpdatePort(),
            null!,
            null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var action = await controller.Update(
            Guid.CreateVersion7(),
            new UpdateEventSessionSpeakerDto
            {
                Actor = new UpdateEventSessionSpeakerActorDto { ActorId = Guid.CreateVersion7() }
            },
            "01900000-0000-7000-8000-000000000099",
            CancellationToken.None);

        var result = action.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    private static async Task AssertRoute(
        string actionName,
        Type httpMethodAttributeType,
        string template,
        string routeName)
    {
        var action = typeof(EventSessionSpeakerController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} not found.");
        var route = action.GetCustomAttributes()
            .Single(attribute => attribute.GetType() == httpMethodAttributeType) as HttpMethodAttribute;

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
    }

    private static HttpRequestMessage CreateAuthenticatedJsonRequest<TValue>(
        HttpMethod method,
        string url,
        TValue body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private sealed class RejectDispatchUpdatePort
        : ICommandHandler<UpdateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>
    {
        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            UpdateEventSessionSpeakerCommand command,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid If-Match reached the native update operation ingress.");
    }
}
