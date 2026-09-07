using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Features.EventDays.Requests.Queries;
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

namespace Event.Api.IntegrationTests.Features;

public class EventDayControllerTests
{
    [Test]
    public async Task ManagedReadRoute_UsesAuthenticatedViewManagementContract()
    {
        var action = typeof(EventDayController).GetMethod(nameof(EventDayController.GetManagedByEvent))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var authorization = typeof(GetManagedEventDaysByEventRequest)
            .GetCustomAttribute<AuthorizeResourceAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("management/by-event/{eventId:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetManagedEventDaysByEvent);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Events.ViewManagement);
    }

    [Test]
    public async Task UpdateRoute_UsesPatchRouteIdAuthoritativeContract()
    {
        var action = typeof(EventDayController).GetMethod(nameof(EventDayController.Update))!;
        var route = action.GetCustomAttribute<HttpPatchAttribute>()!;
        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("{id:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.UpdateEventDay);
        await Assert.That(classification.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();

        var commandAuthorization = typeof(UpdateEventDayCommand).GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(commandAuthorization.Resource).IsEqualTo(ResourceKinds.EventDay);
        await Assert.That(commandAuthorization.Action).IsEqualTo(AuthorizationActions.Update);

        var id = Guid.CreateVersion7();
        var secureRequest = (ISecureRequest)new UpdateEventDayCommand
        {
            EventDayId = id,
            ExpectedConcurrencyStamp = Guid.CreateVersion7(),
            EventDayDto = new UpdateEventDayDto
            {
                Label = new UpdateEventDayLabelDto { Value = OptionalUpdate<string?>.Set("Updated day") }
            }
        };
        await Assert.That(secureRequest.ResourceId).IsEqualTo(id.ToString());

        await AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);
        await AssertProducesProblem(action, StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task Delete_WhenPublishedTicketReferencesDay_ReturnsConflict()
    {
        Guid id = Guid.CreateVersion7();
        IMediator mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DeleteEventDayCommand>(), Arg.Any<CancellationToken>()).Returns(
            BaseCommandResponse.Failure<Guid>(
                "event_day_ticket_entitlement_conflict",
                "Event day is referenced by a published ticket catalog.",
                id: id));
        var controller = new EventDayController(
            mediator,
            Substitute.For<ILogger<EventDayController>>(),
            Substitute.For<IResourceAssembler<EventDayDto, EventDayListDto>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        ActionResult result = await controller.Delete(id, CancellationToken.None);

        ObjectResult conflict = (ObjectResult)result;
        await Assert.That(conflict.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(((ProblemDetails)conflict.Value!).Extensions["code"]).IsEqualTo("event_day_ticket_entitlement_conflict");
        await AssertProducesProblem(typeof(EventDayController).GetMethod(nameof(EventDayController.Delete))!, StatusCodes.Status409Conflict);
    }

    private static async Task AssertProducesProblem(MethodInfo action, int statusCode)
    {
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == typeof(ProblemDetails))).IsTrue();
    }
}
