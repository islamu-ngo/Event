using System.Reflection;
using System.Security.Claims;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventLifecycleDeleteControllerTests
{
    [Test]
    public async Task Delete_MapsTypedOutcomesAndReturnsNoContentOnlyForSuccess()
    {
        Guid eventId = Guid.CreateVersion7();
        (BaseCommandResponse<Guid> Response, int Status)[] cases =
        [
            (BaseCommandResponse.NotFound<Guid>("Event not found.", eventId), StatusCodes.Status404NotFound),
            (BaseCommandResponse.Failure<Guid>(DeleteEventFailureCodes.AuthorityDenied,
                "Current event deletion authority is required.", id: eventId), StatusCodes.Status403Forbidden),
            (BaseCommandResponse.Failure<Guid>(DeleteEventFailureCodes.PaidEvidenceConflict,
                "An event with paid evidence cannot be deleted.", id: eventId), StatusCodes.Status409Conflict),
            (BaseCommandResponse.Success(eventId), StatusCodes.Status204NoContent)
        ];

        foreach ((BaseCommandResponse<Guid> response, int expectedStatus) in cases)
        {
            ActionResult result = await Controller(response).Delete(eventId, CancellationToken.None);
            await Assert.That(StatusCode(result)).IsEqualTo(expectedStatus);
        }
    }

    [Test]
    public async Task Delete_PreservesNativeAuthorizationAndDocumentsDistinctFailures()
    {
        MethodInfo action = typeof(EventLifecycleController).GetMethod(nameof(EventLifecycleController.Delete))!;
        AuthorizeResourceAttribute authorization = typeof(DeleteEventCommand).GetCustomAttribute<AuthorizeResourceAttribute>()!;
        int[] documentedStatuses = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Where(attribute => attribute.Type == typeof(ProblemDetails))
            .Select(attribute => attribute.StatusCode)
            .ToArray();

        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Delete);
        await Assert.That(documentedStatuses).Contains(StatusCodes.Status403Forbidden);
        await Assert.That(documentedStatuses).Contains(StatusCodes.Status404NotFound);
        await Assert.That(documentedStatuses).Contains(StatusCodes.Status409Conflict);
    }

    private static EventLifecycleController Controller(BaseCommandResponse<Guid> response)
    {
        var delete = Substitute.For<ICommandHandler<DeleteEventCommand, BaseCommandResponse<Guid>>>();
        delete.ExecuteAsync(Arg.Any<DeleteEventCommand>(), Arg.Any<CancellationToken>()).Returns(response);
        var controller = new EventLifecycleController(
            Substitute.For<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<ImportEventCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<PublishEventCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<ApprovePublishEventCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<UpdateEventCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<ArchiveEventCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<CancelEventCommand, BaseCommandResponse<Guid>>>(),
            delete,
            Substitute.For<ITenantContext>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                    "test"))
            }
        };
        return controller;
    }

    private static int StatusCode(ActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode!.Value,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => throw new InvalidOperationException($"Unexpected action result {result.GetType().Name}.")
    };
}
