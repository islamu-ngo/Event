using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventSessionAgendaItemControllerTests
{
    [Test]
    public async Task UpdateRoute_UsesAuthenticatedCanonicalPatchWithoutLegacyPut()
    {
        MethodInfo action = typeof(EventSessionAgendaItemController)
            .GetMethod(nameof(EventSessionAgendaItemController.Update))!;
        var route = action.GetCustomAttribute<HttpPatchAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("{id:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.UpdateEventSessionAgendaItem);
        await Assert.That(action.GetCustomAttribute<HttpPutAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
    }

    [Test]
    public async Task Update_UsesRouteIdAndForwardsAllGroupedProperties()
    {
        var routeId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var locationId = Guid.CreateVersion7();
        var relationship = new UpdateEventSessionAgendaItemRelationshipDto
        {
            EventSessionId = sessionId
        };
        var content = new UpdateEventSessionAgendaItemContentDto
        {
            Title = "Prayer break",
            Description = OptionalUpdate<string?>.Set("Main hall")
        };
        var schedule = new UpdateEventSessionAgendaItemScheduleDto
        {
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var location = new UpdateEventSessionAgendaItemLocationDto
        {
            Value = OptionalUpdate<Guid?>.Set(locationId)
        };
        var dto = new UpdateEventSessionAgendaItemDto
        {
            Relationship = relationship,
            Content = content,
            Schedule = schedule,
            Location = location
        };
        var updateHandler = new UpdateEventSessionAgendaItemCommandHandlerStub();
        var controller = new EventSessionAgendaItemController(
            Substitute.For<IQueryHandler<GetEventSessionAgendaItemListRequest, PaginatedResult<EventSessionAgendaItemListDto>>>(),
            Substitute.For<IQueryHandler<GetEventSessionAgendaItemDetailsRequest, EventSessionAgendaItemDto?>>(),
            Substitute.For<IQueryHandler<GetAgendaItemsBySessionRequest, List<EventSessionAgendaItemListDto>>>(),
            Substitute.For<IQueryHandler<GetManagedAgendaItemsBySessionRequest, List<EventSessionAgendaItemListDto>?>>(),
            Substitute.For<ICommandHandler<CreateEventSessionAgendaItemCommand, BaseCommandResponse<Guid>>>(),
            updateHandler,
            Substitute.For<ICommandHandler<DeleteEventSessionAgendaItemCommand, bool>>(),
            NullLogger<EventSessionAgendaItemController>.Instance);

        await controller.Update(routeId, dto);

        UpdateEventSessionAgendaItemCommand command = updateHandler.LastCommand!;
        await Assert.That(command.EventSessionAgendaItemId).IsEqualTo(routeId);
        await Assert.That(command.AgendaItemDto).IsSameReferenceAs(dto);
        await Assert.That(command.AgendaItemDto.Relationship).IsSameReferenceAs(relationship);
        await Assert.That(command.AgendaItemDto.Content).IsSameReferenceAs(content);
        await Assert.That(command.AgendaItemDto.Schedule).IsSameReferenceAs(schedule);
        await Assert.That(command.AgendaItemDto.Location).IsSameReferenceAs(location);
    }

    private sealed class UpdateEventSessionAgendaItemCommandHandlerStub : ICommandHandler<UpdateEventSessionAgendaItemCommand, BaseCommandResponse<Guid>>
    {
        public UpdateEventSessionAgendaItemCommand? LastCommand { get; private set; }

        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            UpdateEventSessionAgendaItemCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(BaseCommandResponse.Success(
                command.EventSessionAgendaItemId,
                "Agenda item updated."));
        }
    }
}
