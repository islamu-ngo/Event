using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventSessionLanguageControllerTests
{
    [Test]
    public async Task ManagedReadRoute_UsesAuthenticatedViewManagementContract()
    {
        var action = typeof(EventSessionLanguageController).GetMethod(nameof(EventSessionLanguageController.GetManagedBySession))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var authorization = typeof(GetManagedLanguagesBySessionQuery).GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(route.Template).IsEqualTo("management/by-event/{eventId:guid}/by-session/{eventSessionId:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetManagedEventSessionLanguages);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Events.ViewManagement);
    }

    // These adapter-only tests retain the command-result mappings, including the not-found
    // result that normal HTTP authorization rejects before the business handler runs.
    // NativeEventSessionLanguageHttpTests owns real authorization and durable mutation evidence.
    [Test]
    public async Task Update_WhenIfMatchIsMissing_ReturnsValidationProblemDetails()
    {
        var update = new UpdatePort(_ => throw new InvalidOperationException("Dispatch must not run without If-Match."));
        var result = await Controller(update).Update(7, Input(), null);
        var problem = (ValidationProblemDetails)((ObjectResult)result.Result!).Value!;
        await Assert.That(problem.Status).IsEqualTo(400);
        await Assert.That(problem.Title).IsEqualTo("Program validation failed");
        await Assert.That(problem.Extensions["code"]).IsEqualTo("validation_failed");
        await Assert.That(problem.Errors["If-Match"].Length).IsEqualTo(1);
        await Assert.That(update.LastRequest).IsNull();
    }

    [Test]
    public async Task Update_WhenCommandValidationFails_DoesNotProbeBeforeSecuredCommand()
    {
        var update = new UpdatePort(_ => BaseCommandResponse.Validation<int>(["Language not found."], "Event Session Language update failed."));
        var stamp = Guid.CreateVersion7();
        var result = await Controller(update).Update(7, Input(), $"\"{stamp:D}\"");
        var problem = (ValidationProblemDetails)((ObjectResult)result.Result!).Value!;
        await Assert.That(problem.Status).IsEqualTo(400);
        await Assert.That(problem.Title).IsEqualTo("Program validation failed");
        await Assert.That(update.LastRequest!.EventSessionLanguageId).IsEqualTo(7);
        await Assert.That(update.LastRequest.EventSessionId).IsEqualTo(Guid.Empty);
        await Assert.That(update.LastRequest.ExpectedConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task Update_WhenFailureCodeIsNotFound_ReturnsNotFound()
    {
        var update = new UpdatePort(_ => BaseCommandResponse.NotFound<int>("Event session language not found."));
        var result = await Controller(update).Update(7, Input(), $"\"{Guid.CreateVersion7():D}\"");
        await Assert.That(((ObjectResult)result.Result!).StatusCode).IsEqualTo(404);
    }

    private static UpdateEventSessionLanguageDto Input() => new()
    {
        Language = new UpdateEventSessionLanguageLanguageDto { LanguageId = 2 }
    };

    private static EventSessionLanguageController Controller(UpdatePort update) => new(
        Substitute.For<IQueryHandler<GetLanguagesBySessionQuery, List<EventSessionLanguageListDto>>>(),
        Substitute.For<IQueryHandler<GetManagedLanguagesBySessionQuery, List<EventSessionLanguageListDto>>>(),
        Substitute.For<IQueryHandler<GetEventSessionLanguageDetailsQuery, EventSessionLanguageDto?>>(),
        Substitute.For<ICommandHandler<CreateEventSessionLanguageCommand, BaseCommandResponse<int>>>(),
        update,
        Substitute.For<ICommandHandler<DeleteEventSessionLanguageCommand, bool>>(),
        Substitute.For<IResourceAssembler<EventSessionLanguageDto, EventSessionLanguageListDto>>())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private sealed class UpdatePort(Func<UpdateEventSessionLanguageCommand, BaseCommandResponse<int>> respond)
        : ICommandHandler<UpdateEventSessionLanguageCommand, BaseCommandResponse<int>>
    {
        public UpdateEventSessionLanguageCommand? LastRequest { get; private set; }
        public Task<BaseCommandResponse<int>> ExecuteAsync(UpdateEventSessionLanguageCommand command, CancellationToken cancellationToken)
        {
            LastRequest = command;
            return Task.FromResult(respond(command));
        }
    }
}
