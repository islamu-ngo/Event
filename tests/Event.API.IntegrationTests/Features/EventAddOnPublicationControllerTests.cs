using Explore.API.Controllers;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventAddOnPublicationControllerTests
{
    [Test]
    [Arguments("event_add_on_publish_restricted", StatusCodes.Status403Forbidden)]
    [Arguments("event_add_on_publish_authority_unavailable", StatusCodes.Status503ServiceUnavailable)]
    [Arguments("event_add_on_management_unavailable", StatusCodes.Status404NotFound)]
    public async Task Publication_failures_preserve_denial_unavailability_and_missing_target(
        string failureCode, int expectedStatus)
    {
        var handler = Substitute.For<ICommandHandler<PublishEventAddOnCatalogCommand, EventAddOnPublicationResponse>>();
        var eventId = Guid.CreateVersion7();
        handler.ExecuteAsync(Arg.Any<PublishEventAddOnCatalogCommand>(), Arg.Any<CancellationToken>())
            .Returns(EventAddOnPublicationResponse.Failure(
                BaseCommandResponse.Failure<Guid>(failureCode, "Publication could not proceed.", id: eventId)));

        var response = await Controller(handler).Publish(eventId, CancellationToken.None);
        var result = response.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(result.Value is ProblemDetails).IsTrue();
    }

    [Test]
    public async Task Published_catalog_keeps_the_existing_hal_payload_not_the_internal_command_envelope()
    {
        var eventId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        var catalog = new EventAddOnCatalogDto { Id = catalogId, EventId = eventId, CurrencyCode = "USD", VersionNumber = 2 };
        var handler = Substitute.For<ICommandHandler<PublishEventAddOnCatalogCommand, EventAddOnPublicationResponse>>();
        handler.ExecuteAsync(Arg.Any<PublishEventAddOnCatalogCommand>(), Arg.Any<CancellationToken>())
            .Returns(EventAddOnPublicationResponse.Success(catalog));

        var response = await Controller(handler).Publish(eventId, CancellationToken.None);
        var result = response.Result as OkObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsTypeOf<HalResource<EventAddOnCatalogDto>>();
    }

    private static EventAddOnManagementController Controller(
        ICommandHandler<PublishEventAddOnCatalogCommand, EventAddOnPublicationResponse> publish)
    {
        var assembler = Substitute.For<IResourceAssembler<EventAddOnCatalogDto, EventAddOnCatalogDto>>();
        assembler.ToResource(Arg.Any<EventAddOnCatalogDto>(), Arg.Any<HttpContext>())
            .Returns(call => new HalResource<EventAddOnCatalogDto>((EventAddOnCatalogDto)call[0]!));
        return new EventAddOnManagementController(
            Substitute.For<IQueryHandler<GetEventAddOnCatalogQuery, EventAddOnCatalogDto?>>(),
            Substitute.For<ICommandHandler<CreateEventAddOnCatalogDraftCommand, EventAddOnCatalogDto?>>(),
            Substitute.For<ICommandHandler<AddEventAddOnCatalogItemCommand, EventAddOnCatalogDto?>>(),
            publish,
            Substitute.For<ICommandHandler<RetireEventAddOnCatalogCommand, EventAddOnCatalogDto?>>(),
            assembler, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
