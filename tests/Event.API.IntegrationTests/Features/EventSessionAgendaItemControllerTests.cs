using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

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
        var mediator = new EventSessionAgendaItemMediatorStub();
        var controller = new EventSessionAgendaItemController(
            mediator,
            NullLogger<EventSessionAgendaItemController>.Instance);

        await controller.Update(routeId, dto);

        UpdateEventSessionAgendaItemCommand command = mediator.LastRequest!;
        await Assert.That(command.EventSessionAgendaItemId).IsEqualTo(routeId);
        await Assert.That(command.AgendaItemDto).IsSameReferenceAs(dto);
        await Assert.That(command.AgendaItemDto.Relationship).IsSameReferenceAs(relationship);
        await Assert.That(command.AgendaItemDto.Content).IsSameReferenceAs(content);
        await Assert.That(command.AgendaItemDto.Schedule).IsSameReferenceAs(schedule);
        await Assert.That(command.AgendaItemDto.Location).IsSameReferenceAs(location);
    }

    private sealed class EventSessionAgendaItemMediatorStub : IMediator
    {
        public UpdateEventSessionAgendaItemCommand? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = (UpdateEventSessionAgendaItemCommand)(object)request;
            object response = BaseCommandResponse.Success(
                LastRequest.EventSessionAgendaItemId,
                "Agenda item updated.");
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
